using XlyFinance.Core;
using XlyFinance.Core.Providers.Yahoo;
using XlyFinance.Tests.Fakes;

namespace XlyFinance.Tests;

public class YahooFxHistoryProviderTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly End = new(2024, 1, 5);

    [Fact]
    public async Task Maps_bars_to_rates()
    {
        var client = FakeYahooHistoryClient.Returning(
            new YahooDailyBar(new DateOnly(2024, 1, 2), 1.0954m),
            new YahooDailyBar(new DateOnly(2024, 1, 3), 1.0961m));

        var rates = await CreateProvider(client).GetDailyRatesAsync("EURUSD=X", Start, End);

        Assert.Equal(
            new[] { new FxRate(new DateOnly(2024, 1, 2), 1.0954m), new FxRate(new DateOnly(2024, 1, 3), 1.0961m) },
            rates.ToArray());
    }

    [Fact]
    public async Task Retries_transient_failures_and_then_succeeds()
    {
        var attempts = 0;
        var client = new FakeYahooHistoryClient((_, _, _, _) =>
        {
            attempts++;
            if (attempts < 3)
                throw new InvalidOperationException("502 Bad Gateway");

            return Task.FromResult<IReadOnlyList<YahooDailyBar>>([new YahooDailyBar(new DateOnly(2024, 1, 2), 1.0954m)]);
        });

        var rates = await CreateProvider(client).GetDailyRatesAsync("EURUSD=X", Start, End);

        Assert.Equal(3, attempts);
        var single = Assert.Single(rates);
        Assert.Equal(1.0954m, single.Close);
    }

    [Fact]
    public async Task Does_not_retry_a_permanent_failure()
    {
        var client = new FakeYahooHistoryClient((symbol, _, _, _) =>
            throw new FxDataException($"Yahoo Finance has no data for '{symbol}'."));

        var exception = await Assert.ThrowsAsync<FxDataException>(
            async () => await CreateProvider(client).GetDailyRatesAsync("EURUSD=X", Start, End));

        Assert.Equal(1, client.CallCount);
        Assert.Contains("no data", exception.Message);
    }

    [Fact]
    public async Task Gives_up_after_the_configured_number_of_attempts()
    {
        var client = new FakeYahooHistoryClient((_, _, _, _) => throw new TimeoutException("took too long"));

        var exception = await Assert.ThrowsAsync<FxDataException>(
            async () => await CreateProvider(client, maxAttempts: 4).GetDailyRatesAsync("EURUSD=X", Start, End));

        Assert.Equal(4, client.CallCount);
        Assert.Contains("took too long", exception.Message);
    }

    [Fact]
    public async Task Retries_a_timeout_and_reports_it_as_a_provider_failure()
    {
        // HttpClient surfaces its own timeout as a cancellation with the caller's token untouched.
        var client = new FakeYahooHistoryClient((_, _, _, _) =>
            throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing."));

        var exception = await Assert.ThrowsAsync<FxDataException>(
            async () => await CreateProvider(client, maxAttempts: 3).GetDailyRatesAsync("EURUSD=X", Start, End));

        Assert.Equal(3, client.CallCount);
        Assert.Contains("timed out", exception.Message);
    }

    [Fact]
    public async Task Lets_caller_cancellation_through_without_retrying_or_wrapping_it()
    {
        var client = new FakeYahooHistoryClient((_, _, _, _) => throw new OperationCanceledException());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await CreateProvider(client).GetDailyRatesAsync("EURUSD=X", Start, End, cancellation.Token));

        Assert.Equal(1, client.CallCount);
    }

    private static YahooFxHistoryProvider CreateProvider(IYahooHistoryClient client, int maxAttempts = 3) =>
        new(client, maxAttempts, TimeSpan.Zero);
}
