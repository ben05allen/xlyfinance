using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Threading;
using ExcelDna.Integration;
using XlyFinance.Core;
using XlyFinance.Core.Dates;
using XlyFinance.Core.Providers.Yahoo;

namespace XlyFinance.AddIn;

/// <summary>
/// The user defined functions exposed to Excel.
/// </summary>
/// <remarks>
/// This layer only deals with Excel concerns: coercing loosely typed arguments, wiring up the async
/// machinery and mapping failures onto Excel errors. All fetching and validation lives in XlyFinance.Core.
/// </remarks>
public static class FxFunctions
{
    private const string Category = "Xly Finance";

    private static readonly Lazy<FxHistoryService> Service =
        new(CreateService, LazyThreadSafetyMode.ExecutionAndPublication);

    private static string? _lastError;

    /// <summary>
    /// Loads daily closes for a currency pair and spills them into two columns: date and close.
    /// </summary>
    [ExcelFunction(
        Name = "FXRATES",
        Category = Category,
        Description = "Loads daily FX closing rates from Yahoo Finance. Returns a two column array: date (serial) and close.",
        IsThreadSafe = true)]
    public static object FxRates(
        [ExcelArgument(Name = "pair", Description = "Currency pair, for example \"EURUSD\" or \"EUR/USD\".")] object pair,
        [ExcelArgument(Name = "start_date", Description = "First date to include, for example \"2024-01-01\".")] object startDate,
        [ExcelArgument(Name = "end_date", Description = "Last date to include, inclusive, for example \"2024-12-31\".")] object endDate)
    {
        if (!TryBuildQuery(pair, startDate, endDate, out var query, out var errorResult))
            return errorResult;

        // The argument key doubles as the async (RTD) topic: identical arguments reuse the value that is
        // already in memory instead of hitting Yahoo Finance on every recalculation.
        var key = new object[]
        {
            query.Symbol,
            query.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            query.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        };

        return ExcelAsyncUtil.RunTaskWithCancellation<object>(
            "FXRATES",
            key,
            cancellationToken => FetchAsync(query, cancellationToken));
    }

    /// <summary>
    /// Normalises a pair to the ticker Yahoo Finance uses. Handy for sanity checking a sheet.
    /// </summary>
    [ExcelFunction(
        Name = "FXSYMBOL",
        Category = Category,
        Description = "Normalises a currency pair to the Yahoo Finance ticker, for example \"eur/usd\" becomes \"EURUSD=X\".",
        IsThreadSafe = true)]
    public static object FxSymbol(
        [ExcelArgument(Name = "pair", Description = "Currency pair, for example \"EURUSD\".")] object pair)
    {
        if (!FxPair.TryNormalize(ExcelValues.AsText(pair), out var symbol, out var error))
            return Fail(error, ExcelError.ExcelErrorValue);

        return symbol;
    }

    /// <summary>
    /// Explains the most recent failure. Excel only shows <c>#VALUE!</c> or <c>#N/A</c> from
    /// <c>FXRATES</c>, which is not much to go on.
    /// </summary>
    [ExcelFunction(
        Name = "FXLASTERROR",
        Category = Category,
        Description = "Returns the message of the most recent FXRATES failure.",
        IsThreadSafe = true)]
    public static object FxLastError() =>
        Volatile.Read(ref _lastError) ?? "No FX failure has been recorded since Excel started.";

    private static FxHistoryService CreateService()
    {
        var provider = new YahooFxHistoryProvider(new YahooChartHistoryClient());
        return new FxHistoryService(provider);
    }

    private static async Task<object> FetchAsync(FxQuery query, CancellationToken cancellationToken)
    {
        var result = await Service.Value
            .GetDailyRatesAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            var error = result.Error!;
            return Fail(error.Message, ToExcelError(error.Kind), error.Exception);
        }

        return ExcelValues.ToRateArray(result.Rates);
    }

    private static bool TryBuildQuery(
        object pair,
        object startDate,
        object endDate,
        [NotNullWhen(true)] out FxQuery? query,
        out object errorResult)
    {
        query = null;

        if (!DateValueParser.TryParse(startDate, out var start))
        {
            errorResult = Fail(
                $"start_date is not a valid date (got {ExcelValues.Describe(startDate)}).",
                ExcelError.ExcelErrorValue);
            return false;
        }

        if (!DateValueParser.TryParse(endDate, out var end))
        {
            errorResult = Fail(
                $"end_date is not a valid date (got {ExcelValues.Describe(endDate)}).",
                ExcelError.ExcelErrorValue);
            return false;
        }

        if (!FxQuery.TryCreate(ExcelValues.AsText(pair), start, end, out query, out var error))
        {
            errorResult = Fail(error, ExcelError.ExcelErrorValue);
            return false;
        }

        errorResult = ExcelError.ExcelErrorValue;
        return true;
    }

    private static ExcelError ToExcelError(FxErrorKind kind) => kind switch
    {
        FxErrorKind.NoData => ExcelError.ExcelErrorNA,
        _ => ExcelError.ExcelErrorValue,
    };

    private static object Fail(string? message, ExcelError excelError, Exception? exception = null)
    {
        var text = string.IsNullOrWhiteSpace(message) ? "Unknown FX failure." : message!;

        Volatile.Write(ref _lastError, text);
        SafeLog(text);

        if (exception is not null)
            SafeLog(exception.ToString());

        return excelError;
    }

    private static void SafeLog(string message)
    {
        // Excel-DNA hooks a trace listener into its diagnostics log, and this also keeps the detail
        // visible to a debugger attached to Excel. Never let logging break a function call.
        try
        {
            Trace.WriteLine($"XlyFinance: {message}");
            Trace.Flush();
        }
        catch
        {
            // ignored on purpose
        }
    }
}
