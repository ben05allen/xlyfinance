using System.Globalization;
using XlyFinance.Core.Excel;

namespace XlyFinance.Core.Dates;

/// <summary>
/// Converts the loosely typed values Excel hands to a UDF into <see cref="DateOnly"/>.
/// Deliberately lives in Core so the parsing rules can be tested without Excel.
/// </summary>
public static class DateValueParser
{
    private static readonly string[] IsoFormats =
    [
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyyMMdd",
        "yyyy-MM-dd HH:mm:ss",
    ];

    private static readonly string[] TextualMonthFormats =
    [
        "d MMM yyyy",
        "dd MMM yyyy",
        "dd-MMM-yyyy",
        "MMM d yyyy",
    ];

    /// <summary>
    /// Tries to read a date from anything Excel might pass: a <see cref="DateTime"/>, an Excel serial number,
    /// an ISO string, or a free form date string.
    /// </summary>
    /// <returns><c>false</c> for blanks, omitted arguments, errors and unparseable text.</returns>
    public static bool TryParse(object? value, out DateOnly date)
    {
        date = default;

        switch (value)
        {
            case null:
                return false;

            case DateOnly dateOnly:
                date = dateOnly;
                return true;

            case DateTime dateTime:
                date = DateOnly.FromDateTime(dateTime);
                return true;

            case DateTimeOffset dateTimeOffset:
                date = DateOnly.FromDateTime(dateTimeOffset.DateTime);
                return true;

            case double serial:
                return ExcelSerialDate.TryToDateOnly(serial, out date);

            case int integer:
                return ExcelSerialDate.TryToDateOnly(integer, out date);

            case decimal value1:
                return ExcelSerialDate.TryToDateOnly((double)value1, out date);

            case string text:
                return TryParseText(text, out date);

            default:
                return false;
        }
    }

    private static bool TryParseText(string text, out DateOnly date)
    {
        date = default;

        var trimmed = text.Trim();
        if (trimmed.Length == 0)
            return false;

        if (DateOnly.TryParseExact(trimmed, IsoFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        if (DateOnly.TryParseExact(trimmed, TextualMonthFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        // Culture-invariant fallback, which also picks up textual dates such as "Tuesday, 2 January 2024".
        if (DateOnly.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return true;

        // A date typed as text but stored as a serial number, for example "45293".
        if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return ExcelSerialDate.TryToDateOnly(serial, out date);

        return false;
    }
}
