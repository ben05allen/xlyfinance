using XlyFinance.Core;

namespace XlyFinance.Tests;

public class FxQueryTests
{
    [Fact]
    public void Creates_query_with_normalised_symbol_and_inclusive_range()
    {
        Assert.True(FxQuery.TryCreate(
            "eur/usd",
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 31),
            out var query,
            out var error));

        Assert.Null(error);
        Assert.NotNull(query);
        Assert.Equal("EURUSD=X", query!.Symbol);
        Assert.Equal("EUR/USD", query.DisplayName);
        Assert.Equal(new DateOnly(2024, 1, 1), query.Start);
        Assert.Equal(new DateOnly(2024, 1, 31), query.End);
        Assert.Equal(31, query.DayCount);
    }

    [Fact]
    public void Single_day_range_counts_as_one_day()
    {
        var day = new DateOnly(2024, 3, 15);

        Assert.True(FxQuery.TryCreate("GBPJPY", day, day, out var query, out _));
        Assert.Equal(1, query!.DayCount);
    }

    [Fact]
    public void Rejects_reversed_range()
    {
        Assert.False(FxQuery.TryCreate(
            "EURUSD",
            new DateOnly(2024, 2, 1),
            new DateOnly(2024, 1, 1),
            out var query,
            out var error));

        Assert.Null(query);
        Assert.NotNull(error);
        Assert.Contains("must be on or before", error);
    }

    [Fact]
    public void Rejects_invalid_pair()
    {
        Assert.False(FxQuery.TryCreate("NOPE", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2), out var query, out var error));

        Assert.Null(query);
        Assert.NotNull(error);
    }
    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(1969, 12, 31)]
    public void Rejects_dates_before_1970(int year, int month, int day)
    {
        Assert.False(FxQuery.TryCreate("EURUSD", new DateOnly(year, month, day),
            new DateOnly(2024, 1, 1), out var query, out var error));

        Assert.Null(query);
        Assert.Contains("1970-01-01", error);
    }

    [Fact]
    public void Accepts_the_supported_boundaries()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.True(FxQuery.TryCreate("EURUSD", new DateOnly(1970, 1, 1), today, out _, out _));
    }

    [Fact]
    public void Rejects_future_dates()
    {
        var tomorrow = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        Assert.False(FxQuery.TryCreate("EURUSD", new DateOnly(2024, 1, 1), tomorrow,
            out var query, out var error));

        Assert.Null(query);
        Assert.Contains("today", error);
    }
}
