using XlyFinance.Core;
using XlyFinance.Tests.Fakes;

namespace XlyFinance.Tests;

/// <summary>Behaviour of <see cref="FxHistoryService.GetClosesAsync"/> (the FXRANGE function).</summary>
public class FxClosesLookupTests
{
    [Fact]
    public async Task Returns_closes_in_the_order_requested_with_nulls_for_gaps()
    {
        var provider = new StubFxHistoryProvider(
        [
            Rate(2024, 1, 1, 1.10m),
            Rate(2024, 1, 2, 1.20m),
            Rate(2024, 1, 3, 1.30m),
        ]);

        var dates = new[]
        {
            new DateOnly(2024, 1, 3),
            new DateOnly(2024, 1, 1),
            new DateOnly(2024, 1, 6), // Saturday
            new DateOnly(2024, 1, 3), // repeated on purpose
        };

        var result = await new FxHistoryService(provider).GetClosesAsync("eurusd", dates);

        Assert.True(result.Success);
        Assert.Null(result.Error);
        Assert.Equal(new decimal?[] { 1.30m, 1.10m, null, 1.30m }, result.Closes.ToArray());
    }

    [Fact]
    public async Task Makes_one_request_covering_the_earliest_and_latest_date()
    {
        var provider = new StubFxHistoryProvider(
        [
            Rate(2024, 1, 1, 1.10m),
            Rate(2024, 3, 1, 1.20m),
        ]);

        await new FxHistoryService(provider).GetClosesAsync(
            "EURUSD",
            [new DateOnly(2024, 3, 1), new DateOnly(2024, 2, 1), new DateOnly(2024, 1, 1)]);

        Assert.Equal("EURUSD=X", provider.RequestedSymbol);
        Assert.Equal(new DateOnly(2024, 1, 1), provider.RequestedStart);
        Assert.Equal(new DateOnly(2024, 3, 1), provider.RequestedEnd);
    }

    [Fact]
    public async Task Marks_every_requested_date_missing_when_the_provider_has_none_of_them()
    {
        var provider = new StubFxHistoryProvider(
        [
            Rate(2024, 1, 5, 1.10m), // not requested
        ]);

        var result = await new FxHistoryService(provider).GetClosesAsync(
            "EURUSD",
            [new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 2)]);

        Assert.True(result.Success);
        Assert.Equal(new decimal?[] { null, null }, result.Closes.ToArray());
    }

    [Fact]
    public async Task Rejects_an_empty_date_list()
    {
        var provider = new StubFxHistoryProvider(Array.Empty<FxRate>());

        var result = await new FxHistoryService(provider).GetClosesAsync("EURUSD", []);

        Assert.False(result.Success);
        Assert.Equal(FxErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Contains("At least one date", result.Error.Message);
        Assert.Null(provider.RequestedSymbol);
    }

    [Fact]
    public async Task Rejects_an_invalid_pair_without_calling_the_provider()
    {
        var provider = new StubFxHistoryProvider(Array.Empty<FxRate>());

        var result = await new FxHistoryService(provider).GetClosesAsync("NOPE", [new DateOnly(2024, 1, 1)]);

        Assert.False(result.Success);
        Assert.Equal(FxErrorKind.InvalidRequest, result.Error!.Kind);
        Assert.Null(provider.RequestedSymbol);
    }

    [Fact]
    public async Task Returns_all_nulls_when_the_span_holds_no_sessions()
    {
        var provider = new StubFxHistoryProvider(Array.Empty<FxRate>());

        var result = await new FxHistoryService(provider).GetClosesAsync(
            "EURUSD",
            [new DateOnly(2024, 1, 6), new DateOnly(2024, 1, 7)]);

        // The whole span being empty must still come back shaped as the input, not as a single #N/A.
        Assert.True(result.Success);
        Assert.Equal(new decimal?[] { null, null }, result.Closes.ToArray());
    }

    [Fact]
    public async Task Passes_provider_failures_through()
    {
        var provider = new StubFxHistoryProvider((_, _, _, _) => throw new FxDataException("Yahoo said no"));

        var result = await new FxHistoryService(provider).GetClosesAsync("EURUSD", [new DateOnly(2024, 1, 1)]);

        Assert.False(result.Success);
        Assert.Equal(FxErrorKind.ProviderUnavailable, result.Error!.Kind);
        Assert.Equal("Yahoo said no", result.Error.Message);
    }

    [Fact]
    public async Task Lets_cancellation_through()
    {
        var provider = new StubFxHistoryProvider((_, _, _, _) => throw new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await new FxHistoryService(provider).GetClosesAsync("EURUSD", [new DateOnly(2024, 1, 1)]));
    }

    private static FxRate Rate(int year, int month, int day, decimal close) =>
        new(new DateOnly(year, month, day), close);
}
