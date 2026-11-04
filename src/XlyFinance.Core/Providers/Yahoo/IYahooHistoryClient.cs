namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>One daily bar as reported by Yahoo Finance.</summary>
/// <param name="Date">The trading date the bar belongs to.</param>
/// <param name="Close">The closing rate.</param>
public readonly record struct YahooDailyBar(DateOnly Date, decimal Close);

/// <summary>
/// Thin seam over the Yahoo Finance HTTP API, so mapping, retry and validation can be tested without
/// touching the network.
/// </summary>
public interface IYahooHistoryClient
{
    /// <summary>
    /// Requests daily bars for an inclusive date range.
    /// </summary>
    /// <exception cref="FxDataException">
    /// The request cannot succeed as stated (unknown ticker, no data). Implementations should use this for
    /// permanent failures; anything else (timeouts, throttling, 5xx) is thrown as-is and retried upstream.
    /// </exception>
    Task<IReadOnlyList<YahooDailyBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default);
}
