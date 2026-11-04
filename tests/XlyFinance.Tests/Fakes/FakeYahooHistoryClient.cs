using XlyFinance.Core.Providers.Yahoo;

namespace XlyFinance.Tests.Fakes;

/// <summary>
/// Configurable stand-in for <see cref="IYahooHistoryClient"/>, so provider behaviour (retries, mapping,
/// error wrapping) can be exercised without the network.
/// </summary>
internal sealed class FakeYahooHistoryClient : IYahooHistoryClient
{
    private readonly Func<string, DateOnly, DateOnly, CancellationToken, Task<IReadOnlyList<YahooDailyBar>>> _handler;

    public FakeYahooHistoryClient(Func<string, DateOnly, DateOnly, CancellationToken, Task<IReadOnlyList<YahooDailyBar>>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public int CallCount { get; private set; }

    public static FakeYahooHistoryClient Returning(params YahooDailyBar[] bars) =>
        new((_, _, _, _) => Task.FromResult<IReadOnlyList<YahooDailyBar>>(bars));

    public Task<IReadOnlyList<YahooDailyBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default)
    {
        CallCount++;
        return _handler(symbol, startInclusive, endInclusive, cancellationToken);
    }
}
