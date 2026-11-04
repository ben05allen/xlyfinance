namespace XlyFinance.Core.Excel;

/// <summary>
/// Conversions between <see cref="DateOnly"/> and Excel's serial date numbers.
/// </summary>
/// <remarks>
/// Assumes the 1900 date system, which is Excel's default (and the only one on Windows).
/// Both directions round trip through <see cref="DateTime.ToOADate"/> so the 1900 leap-year
/// quirk is handled by the framework rather than here.
/// </remarks>
public static class ExcelSerialDate
{
    /// <summary>Serial number of 1900-01-01, the smallest value Excel considers a date.</summary>
    public const double MinSerial = 1d;

    /// <summary>Serial number of 9999-12-31, the largest value Excel considers a date.</summary>
    public const double MaxSerial = 2958465d;

    /// <summary>
    /// Converts a serial number (any value with a time fraction) to the day it falls on.
    /// </summary>
    public static bool TryToDateOnly(double serial, out DateOnly date)
    {
        date = default;

        if (double.IsNaN(serial) || double.IsInfinity(serial))
            return false;

        if (serial < MinSerial || serial >= MaxSerial + 1d)
            return false;

        try
        {
            date = DateOnly.FromDateTime(DateTime.FromOADate(Math.Floor(serial)));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Converts a date to the serial number Excel stores for it (midnight).</summary>
    public static double ToSerial(DateOnly date) => date.ToDateTime(TimeOnly.MinValue).ToOADate();
}
