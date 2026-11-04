using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using ExcelDna.Integration;
using XlyFinance.Core;
using XlyFinance.Core.Dates;
using XlyFinance.Core.Excel;

namespace XlyFinance.AddIn;

/// <summary>
/// A date range as Excel handed it over: the flattened dates plus the shape they came in, so the result
/// can be laid out the same way.
/// </summary>
/// <param name="Dates">Dates in row-major order.</param>
/// <param name="Rows">Height of the input range.</param>
/// <param name="Columns">Width of the input range.</param>
/// <param name="IsScalar"><c>true</c> when the argument was a single value, not a range.</param>
internal sealed record DateGridInput(DateOnly[] Dates, int Rows, int Columns, bool IsScalar);

/// <summary>
/// Small helpers for the values Excel actually hands to a UDF. Keeping them here means the functions
/// themselves stay readable.
/// </summary>
internal static class ExcelValues
{
    /// <summary>
    /// A UDF argument as text. Excel can pass numbers, booleans and cell errors, not just strings.
    /// </summary>
    public static string? AsText(object? value) => value switch
    {
        null => null,
        string text => text,
        ExcelEmpty => null,
        ExcelMissing => null,
        ExcelError => null,
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// A user facing description of an argument, used in error messages. Turns Excel's opaque
    /// placeholder objects into something someone can act on.
    /// </summary>
    public static string Describe(object? value) => value switch
    {
        null => "blank",
        ExcelEmpty => "blank cell",
        ExcelMissing => "omitted argument",
        ExcelError error => error.ToString(),
        string text => $"\"{text}\"",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? value.GetType().Name,
    };

    /// <summary>
    /// Reads the <c>dates</c> argument of a UDF: either one date or a rectangular range of dates.
    /// </summary>
    /// <remarks>
    /// Excel hands a multi-cell reference to an <c>object</c> parameter as a 2D array, with blanks as
    /// <see cref="ExcelEmpty"/> and cell errors as <see cref="ExcelError"/>. Anything that is not a date
    /// fails the whole call, which keeps typos and stray header rows visible.
    /// </remarks>
    public static bool TryReadDateGrid(
        object? value,
        [NotNullWhen(true)] out DateGridInput? input,
        out string? error)
    {
        input = null;
        error = null;

        if (value is not object[,] cells)
        {
            // A single cell or a literal: nothing to spill, so answer with a scalar.
            if (!DateValueParser.TryParse(value, out var single))
            {
                error = $"dates is not a date (got {Describe(value)}).";
                return false;
            }

            input = new DateGridInput([single], 1, 1, IsScalar: true);
            return true;
        }

        if (!DateGridReader.TryRead(cells, out var dates, out var bad))
        {
            error = bad is { } position
                ? $"dates contains a value that is not a date (row {position.Row + 1}, column {position.Column + 1} of the range, got {Describe(position.Value)})."
                : "dates contains a value that is not a date.";

            return false;
        }

        input = new DateGridInput(dates, cells.GetLength(0), cells.GetLength(1), IsScalar: false);
        return true;
    }

    /// <summary>
    /// Lays the looked-up closes back over the shape of the input range, with <c>#N/A</c> where a date had
    /// no fixing. A single-cell argument gives a single value rather than a 1x1 array.
    /// </summary>
    public static object ToLookupGrid(IReadOnlyList<decimal?> closes, DateGridInput input)
    {
        ArgumentNullException.ThrowIfNull(closes);
        ArgumentNullException.ThrowIfNull(input);

        if (input.IsScalar)
            return CloseOrNa(closes, 0);

        var values = new object[input.Rows, input.Columns];
        var index = 0;

        for (var row = 0; row < input.Rows; row++)
        {
            for (var column = 0; column < input.Columns; column++)
            {
                values[row, column] = CloseOrNa(closes, index);
                index++;
            }
        }

        return values;
    }

    private static object CloseOrNa(IReadOnlyList<decimal?> closes, int index) =>
        index < closes.Count && closes[index] is { } close ? (double)close : ExcelError.ExcelErrorNA;

    /// <summary>
    /// Converts observations into the 2 column variant array Excel spills: date serial and close.
    /// </summary>
    public static object[,] ToRateArray(IReadOnlyList<FxRate> rates)
    {
        ArgumentNullException.ThrowIfNull(rates);

        var values = new object[rates.Count, 2];

        for (var row = 0; row < rates.Count; row++)
        {
            values[row, 0] = ExcelSerialDate.ToSerial(rates[row].Date);
            values[row, 1] = (double)rates[row].Close;
        }

        return values;
    }
}
