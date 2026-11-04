using System.Globalization;

namespace XlyFinance.Core;

/// <summary>
/// Application service behind the spreadsheet functions: asks a provider for data, then cleans it up
/// (range filter, date order, duplicates, unusable rows) and turns failures into <see cref="FxError"/>s.
/// </summary>
public sealed class FxHistoryService
{
    private readonly IFxHistoryProvider _provider;

    public FxHistoryService(IFxHistoryProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Loads daily closes for <paramref name="query"/>, oldest first.
    /// </summary>
    public async Task<FxFetchResult> GetDailyRatesAsync(FxQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            var raw = await _provider
                .GetDailyRatesAsync(query.Symbol, query.Start, query.End, cancellationToken)
                .ConfigureAwait(false);

            var rates = Normalise(raw, query.Start, query.End);

            if (rates.Count == 0)
            {
                return FxFetchResult.Fail(new FxError(
                    FxErrorKind.NoData,
                    $"No daily rates came back for {query.DisplayName} between {Format(query.Start)} and {Format(query.End)}. " +
                    "Check the pair, and remember that weekends and market holidays have no fixing."));
            }

            return FxFetchResult.Ok(rates);
        }
        catch (OperationCanceledException)
        {
            // Excel abandoned the calculation; let the caller decide what to do.
            throw;
        }
        catch (FxDataException ex)
        {
            return FxFetchResult.Fail(new FxError(FxErrorKind.ProviderUnavailable, ex.Message, ex));
        }
        catch (Exception ex)
        {
            return FxFetchResult.Fail(new FxError(
                FxErrorKind.Unexpected,
                $"Unexpected error while loading {query.DisplayName}: {ex.Message}",
                ex));
        }
    }

    /// <summary>
    /// Loads the closing rate for each of <paramref name="dates"/>, in the order given.
    /// </summary>
    /// <remarks>
    /// One provider request covers the whole span from the earliest to the latest requested date, so a
    /// column of dates costs the same as a single date. Dates with no fixing come back as <c>null</c>.
    /// </remarks>
    public async Task<FxLookupResult> GetClosesAsync(
        string? pair,
        IReadOnlyList<DateOnly> dates,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dates);

        if (dates.Count == 0)
            return FxLookupResult.Fail(new FxError(FxErrorKind.InvalidRequest, "At least one date is required."));

        if (!FxQuery.TryCreate(pair, dates.Min(), dates.Max(), out var query, out var error))
            return FxLookupResult.Fail(new FxError(FxErrorKind.InvalidRequest, error!));

        var series = await GetDailyRatesAsync(query, cancellationToken).ConfigureAwait(false);

        // "No data in the span" is not a lookup failure: every requested date is simply missing, which
        // keeps the result aligned with the input range instead of collapsing it to one #N/A cell.
        if (!series.Success && series.Error!.Kind != FxErrorKind.NoData)
            return FxLookupResult.Fail(series.Error!);

        var closesByDate = new Dictionary<DateOnly, decimal>(series.Rates.Count);
        foreach (var rate in series.Rates)
            closesByDate[rate.Date] = rate.Close;

        var closes = new decimal?[dates.Count];
        for (var i = 0; i < dates.Count; i++)
            closes[i] = closesByDate.TryGetValue(dates[i], out var close) ? close : null;

        return FxLookupResult.Ok(closes);
    }

    private static List<FxRate> Normalise(IReadOnlyList<FxRate>? raw, DateOnly start, DateOnly end)
    {
        // SortedDictionary keeps the dates ordered and collapses duplicates (the latest value wins).
        var byDate = new SortedDictionary<DateOnly, FxRate>();

        if (raw is null)
            return [];

        foreach (var rate in raw)
        {
            if (rate.Date < start || rate.Date > end)
                continue;

            // Yahoo returns placeholder rows (for example around holidays) where the close is zero.
            if (rate.Close <= 0m)
                continue;

            byDate[rate.Date] = rate;
        }

        return [.. byDate.Values];
    }

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
