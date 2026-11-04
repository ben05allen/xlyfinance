namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>One daily bar as reported by Yahoo Finance.</summary>
/// <param name="Date">The trading date the bar belongs to.</param>
/// <param name="Close">The closing rate.</param>
public readonly record struct YahooDailyBar(DateOnly Date, decimal Close);

/// <summary>
/// Thin seam over the <c>YahooFinanceApi</c> package, so the mapping, retry and validation logic can be
/// tested without touching the network.
/// </summary>
public interface IYahooHistoryClient
{
    /// <summary>
    /// Requests daily bars for an inclusive date range.
    /// </summary>
    /// <exception cref="Exception">Whatever the Yahoo client throws. The provider decides how to react.</exception>
    Task<IReadOnlyList<YahooDailyBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default);
}
