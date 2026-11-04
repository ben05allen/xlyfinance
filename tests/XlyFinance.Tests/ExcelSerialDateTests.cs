using XlyFinance.Core.Excel;

namespace XlyFinance.Tests;

public class ExcelSerialDateTests
{
    [Theory]
    [InlineData(1, 1900, 1, 1)]
    [InlineData(59, 1900, 2, 28)]
    [InlineData(61, 1900, 3, 1)]
    [InlineData(45292, 2024, 1, 1)]
    [InlineData(45293, 2024, 1, 2)]
    [InlineData(45657, 2024, 12, 31)]
    [InlineData(2958465, 9999, 12, 31)]
    public void Converts_serial_to_date(double serial, int year, int month, int day)
    {
        Assert.True(ExcelSerialDate.TryToDateOnly(serial, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(60)] // Excel's phantom 1900-02-29
    [InlineData(2958466)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Rejects_values_outside_the_excel_date_range(double serial)
    {
        Assert.False(ExcelSerialDate.TryToDateOnly(serial, out _));
    }

    [Fact]
    public void Ignores_the_time_fraction()
    {
        Assert.True(ExcelSerialDate.TryToDateOnly(45292.75, out var date));
        Assert.Equal(new DateOnly(2024, 1, 1), date);
    }

    [Theory]
    [InlineData(1900, 1, 1, 1d)]
    [InlineData(1900, 2, 28, 59d)]
    [InlineData(1900, 3, 1, 61d)]
    [InlineData(2024, 1, 1, 45292d)]
    [InlineData(2024, 1, 2, 45293d)]
    [InlineData(1970, 1, 1, 25569d)]
    [InlineData(9999, 12, 31, 2958465d)]
    public void Converts_date_to_serial(int year, int month, int day, double expected)
    {
        Assert.Equal(expected, ExcelSerialDate.ToSerial(new DateOnly(year, month, day)));
    }

    [Theory]
    [InlineData(1900, 1, 1)]
    [InlineData(1900, 2, 28)]
    [InlineData(1900, 3, 1)]
    [InlineData(2024, 6, 17)]
    [InlineData(9999, 12, 31)]
    public void Round_trips_dates(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);

        Assert.True(ExcelSerialDate.TryToDateOnly(ExcelSerialDate.ToSerial(date), out var roundTripped));
        Assert.Equal(date, roundTripped);
    }
}
