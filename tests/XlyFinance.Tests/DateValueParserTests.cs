using XlyFinance.Core.Dates;

namespace XlyFinance.Tests;

public class DateValueParserTests
{
    private static readonly DateOnly Expected = new(2024, 1, 2);

    [Fact]
    public void Reads_DateTime_values() =>
        Assert.True(DateValueParser.TryParse(new DateTime(2024, 1, 2, 14, 30, 0), out var date) && date == Expected);

    [Fact]
    public void Reads_DateTimeOffset_values() =>
        Assert.True(DateValueParser.TryParse(new DateTimeOffset(2024, 1, 2, 14, 30, 0, TimeSpan.FromHours(2)), out var date) && date == Expected);

    [Fact]
    public void Reads_date_only_values() =>
        Assert.True(DateValueParser.TryParse(Expected, out var date) && date == Expected);

    [Theory]
    [InlineData(45293d)]
    [InlineData(45293)]
    public void Reads_excel_serials_from_numbers(object value) =>
        Assert.True(DateValueParser.TryParse(value, out var date) && date == Expected);

    [Fact]
    public void Reads_decimal_serials() =>
        Assert.True(DateValueParser.TryParse(45293m, out var date) && date == Expected);

    [Theory]
    [InlineData("2024-01-02")]
    [InlineData("2024/01/02")]
    [InlineData("20240102")]
    [InlineData("2024-01-02 09:30:00")]
    [InlineData("2 Jan 2024")]
    [InlineData("02 Jan 2024")]
    [InlineData("02-Jan-2024")]
    [InlineData("Jan 2 2024")]
    [InlineData("45293")]
    [InlineData("  2024-01-02  ")]
    public void Reads_text_dates(string text) =>
        Assert.True(DateValueParser.TryParse(text, out var date) && date == Expected);

    [Theory]
    [InlineData((object?)null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a date")]
    [InlineData("2024-13-40")]
    [InlineData(0d)]
    [InlineData(-3d)]
    public void Rejects_values_that_are_not_dates(object? value) =>
        Assert.False(DateValueParser.TryParse(value, out _));
}
