using XlyFinance.Core;
using XlyFinance.Tests.Fakes;

namespace XlyFinance.Tests;

public class FxHistoryServiceTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly End = new(2024, 1, 5);

    [Fact]
    public async Task Passes_the_normalised_symbol_and_range_to_the_provider()
    {
        var provider = new StubFxHistoryProvider([Rate(2024, 1, 2, 1.0954m)]);
        var service = new FxHistoryService(provider);

        await service.GetDailyRatesAsync(Query());

        Assert.Equal("EURUSD=X", provider.RequestedSymbol);
        Assert.Equal(Start, provider.RequestedStart!.Value);
        Assert.Equal(End, provider.RequestedEnd!.Value);
    }

    [Fact]
    public async Task Orders_by_date_and_drops_rows_outside_the_range()
    {
        var provider = new StubFxHistoryProvider(
        [
            Rate(2024, 1, 3, 1.10m),
            Rate(2024, 1, 1, 1.09m),
            Rate(2023, 12, 29, 1.08m), // before the range
            Rate(2024, 1, 8, 1.11m),   // after the range
            Rate(2024, 1, 2, 1.095m),
            Rate(2024, 1, 4, 1.10m),
        ]);

        var result = await new FxHistoryService(provider).GetDailyRatesAsync(Query());

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(
            new[] { new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3), new DateOnly(2024, 1, 4) },
            result.Rates.Select(rate => rate.Date).ToArray());
    }

    [Fact]
    public async Task Keeps_the_last_value_for_duplicate_dates()
    {
        var provider = new StubFxHistoryProvider(
        [
            Rate(2024, 1, 2, 1.0950m),
            Rate(2024, 1, 2, 1.0955m),
        ]);

        var result = await new FxHistoryService(provider).GetDailyRatesAsync(Query());

        var single = Assert.Single(result.Rates);
        Assert.Equal(1.0955m, single.Close);
    }

    [Fact]
    public async Task Drops_bars_without_a_usable_close()
    {
        var provider = new StubFxHistoryProvider(
        [
            Rate(2024, 1, 1, 0m),
            Rate(2024, 1, 2, 1.0954m),
            Rate(2024, 1, 3, -1m),
        ]);

        var result = await new FxHistoryService(provider).GetDailyRatesAsync(Query());

        var single = Assert.Single(result.Rates);
        Assert.Equal(new DateOnly(2024, 1, 2), single.Date);
    }

    [Fact]
    public async Task Reports_no_data_when_the_provider_returns_nothing()
    {
        var provider = new StubFxHistoryProvider(Array.Empty<FxRate>());

        var result = await new FxHistoryService(provider).GetDailyRatesAsync(Query());

        Assert.False(result.Success);
        Assert.Empty(result.Rates);
        Assert.Equal(FxErrorKind.NoData, result.Error!.Kind);
        Assert.Contains("EUR/USD", result.Error.Message);
    }

    [Fact]
    public async Task Reports_provider_failures_with_the_original_exception()
    {
        var cause = new FxDataException("Yahoo said no");
        var provider = new StubFxHistoryProvider((_, _, _, _) => throw cause);

        var result = await new FxHistoryService(provider).GetDailyRatesAsync(Query());

        Assert.False(result.Success);
        Assert.Equal(FxErrorKind.ProviderUnavailable, result.Error!.Kind);
        Assert.Equal("Yahoo said no", result.Error.Message);
        Assert.Same(cause, result.Error.Exception);
    }

    [Fact]
    public async Task Reports_unexpected_failures_separately()
    {
        var provider = new StubFxHistoryProvider((_, _, _, _) => throw new InvalidOperationException("boom"));

        var result = await new FxHistoryService(provider).GetDailyRatesAsync(Query());

        Assert.False(result.Success);
        Assert.Equal(FxErrorKind.Unexpected, result.Error!.Kind);
        Assert.Contains("boom", result.Error.Message);
    }

    [Fact]
    public async Task Lets_cancellation_through()
    {
        var provider = new StubFxHistoryProvider((_, _, _, _) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FxHistoryService(provider).GetDailyRatesAsync(Query()));
    }

    private static FxQuery Query() => Create("EURUSD", Start, End);

    private static FxQuery Create(string pair, DateOnly start, DateOnly end)
    {
        Assert.True(FxQuery.TryCreate(pair, start, end, out var query, out _));
        return query!;
    }

    private static FxRate Rate(int year, int month, int day, decimal close) =>
        new(new DateOnly(year, month, day), close);
}
