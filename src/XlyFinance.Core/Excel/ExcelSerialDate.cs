namespace XlyFinance.Core.Excel;

/// <summary>
/// Conversions between <see cref="DateOnly"/> and Excel's serial date numbers.
/// </summary>
/// <remarks>
/// <para>
/// Assumes the 1900 date system, which is Excel's default (and the only one on Windows).
/// </para>
/// <para>
/// Excel counts 1900-01-01 as serial 1 and includes a fictitious 1900-02-29 (serial 60, a bug kept for
/// Lotus 1-2-3 compatibility). .NET's <see cref="DateTime.ToOADate"/> has no such leap day, so serials
/// before 1900-03-01 are off by one relative to Excel and are corrected here.
/// </para>
/// </remarks>
public static class ExcelSerialDate
{
    /// <summary>Serial number of 1900-01-01, the smallest value Excel considers a date.</summary>
    public const double MinSerial = 1d;

    /// <summary>Serial number of 9999-12-31, the largest value Excel considers a date.</summary>
    public const double MaxSerial = 2958465d;

    /// <summary>Excel's fictitious 1900-02-29.</summary>
    private const double PhantomLeapDaySerial = 60d;

    /// <summary>First serial that lines up with .NET's OLE automation dates.</summary>
    private const double FirstAlignedSerial = 61d;

    /// <summary>
    /// Converts a serial number (any value with a time fraction) to the day it falls on.
    /// </summary>
    /// <returns><c>false</c> for anything outside Excel's date range, including the phantom 1900-02-29.</returns>
    public static bool TryToDateOnly(double serial, out DateOnly date)
    {
        date = default;

        if (double.IsNaN(serial) || double.IsInfinity(serial))
            return false;

        if (serial < MinSerial || serial >= MaxSerial + 1d)
            return false;

        var day = Math.Floor(serial);

        // 1900-02-29 has no real world equivalent, so refuse it rather than invent a date.
        if (day == PhantomLeapDaySerial)
            return false;

        try
        {
            var oaDate = day < FirstAlignedSerial ? day + 1d : day;
            date = DateOnly.FromDateTime(DateTime.FromOADate(oaDate));
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Converts a date to the serial number Excel stores for it (midnight).</summary>
    public static double ToSerial(DateOnly date)
    {
        var oaDate = date.ToDateTime(TimeOnly.MinValue).ToOADate();
        return oaDate < 61d ? oaDate - 1d : oaDate;
    }
}
