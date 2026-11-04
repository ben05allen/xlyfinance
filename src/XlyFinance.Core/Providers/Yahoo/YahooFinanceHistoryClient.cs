using YahooApi = global::YahooFinanceApi.Yahoo;

namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>
/// <see cref="IYahooHistoryClient"/> backed by the <c>YahooFinanceApi</c> NuGet package.
/// </summary>
/// <remarks>
/// This is the only type in the solution that knows about that package: if it is ever replaced
/// (or Yahoo changes its download endpoint) nothing else has to move.
/// </remarks>
public sealed class YahooFinanceHistoryClient : IYahooHistoryClient
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<YahooDailyBar>> GetDailyBarsAsync(
        string symbol,
        DateOnly startInclusive,
        DateOnly endInclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        if (endInclusive < startInclusive)
            throw new ArgumentException("The end date must not precede the start date.", nameof(endInclusive));

        // Yahoo resolves the period bounds in New York time and treats the upper bound as exclusive,
        // so ask for one extra day and trim locally. That keeps both ends of the user's range inclusive
        // regardless of how the endpoint rounds the boundary.
        var periodStart = startInclusive.ToDateTime(TimeOnly.MinValue);
        var periodEnd = endInclusive.ToDateTime(TimeOnly.MinValue).AddDays(1);

        var candles = await YahooApi
            .GetHistoricalAsync(symbol, periodStart, periodEnd, global::YahooFinanceApi.Period.Daily, cancellationToken)
            .ConfigureAwait(false);

        var bars = new List<YahooDailyBar>(candles.Count);

        foreach (var candle in candles)
        {
            var date = DateOnly.FromDateTime(candle.DateTime);

            if (date < startInclusive || date > endInclusive)
                continue;

            // Placeholder rows around holidays come back with a zero close; they are not real quotes.
            if (candle.Close <= 0m)
                continue;

            bars.Add(new YahooDailyBar(date, candle.Close));
        }

        return bars;
    }
}
