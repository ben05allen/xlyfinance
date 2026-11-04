namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>
/// Default <see cref="IFxHistoryProvider"/>: fetches from Yahoo Finance, retries transient failures and
/// reports everything else as an <see cref="FxDataException"/>.
/// </summary>
public sealed class YahooFxHistoryProvider : IFxHistoryProvider
{
    private readonly IYahooHistoryClient _client;
    private readonly int _maxAttempts;
    private readonly TimeSpan _retryDelay;

    /// <param name="client">Transport used to reach Yahoo Finance.</param>
    /// <param name="maxAttempts">Total attempts, including the first one. Must be at least 1.</param>
    /// <param name="retryDelay">Delay before the first retry; it doubles on every further attempt.</param>
    public YahooFxHistoryProvider(IYahooHistoryClient client, int maxAttempts = 3, TimeSpan? retryDelay = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));

        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "At least one attempt is required.");

        _maxAttempts = maxAttempts;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(300);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FxRate>> GetDailyRatesAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);

        var delay = _retryDelay;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var bars = await _client
                    .GetDailyBarsAsync(symbol, start, end, cancellationToken)
                    .ConfigureAwait(false);

                var rates = new List<FxRate>(bars.Count);
                foreach (var bar in bars)
                    rates.Add(new FxRate(bar.Date, bar.Close));

                return rates;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _maxAttempts && ShouldRetry(ex))
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                delay += delay;
            }
            catch (Exception ex)
            {
                throw new FxDataException(
                    $"Could not load {FxPair.ToDisplayName(symbol)} history from Yahoo Finance: {Describe(ex)}",
                    ex);
            }
        }
    }

    /// <summary>
    /// Yahoo reports unknown tickers with a plain <see cref="Exception"/> rather than a typed HTTP error,
    /// so the only cheap signal is the message. Retrying those would just waste the user's patience;
    /// everything else (timeouts, throttling, 5xx) is worth another try.
    /// </summary>
    private static bool ShouldRetry(Exception exception) =>
        !exception.Message.Contains("Invalid ticker", StringComparison.OrdinalIgnoreCase);

    private static string Describe(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} ({exception.InnerException.Message})";
}
