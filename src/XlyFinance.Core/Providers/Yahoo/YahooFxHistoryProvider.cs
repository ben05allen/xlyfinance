namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>
/// Default <see cref="IFxHistoryProvider"/>: fetches from Yahoo Finance through an
/// <see cref="IYahooHistoryClient"/>, retries transient failures and reports failures as
/// <see cref="FxDataException"/>.
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
                // Excel abandoned the calculation.
                throw;
            }
            catch (FxDataException)
            {
                // The client classified this as permanent (unknown ticker, no data); retrying will not help.
                throw;
            }
            catch (Exception) when (attempt < _maxAttempts)
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

    private static string Describe(Exception exception) =>
        exception.InnerException is null
            ? exception.Message
            : $"{exception.Message} ({exception.InnerException.Message})";
}
