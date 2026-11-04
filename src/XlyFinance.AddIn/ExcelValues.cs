using System.Globalization;
using ExcelDna.Integration;
using XlyFinance.Core;
using XlyFinance.Core.Excel;

namespace XlyFinance.AddIn;

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
