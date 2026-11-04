using System.Globalization;
using System.Text.Json;

namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>
/// <see cref="IYahooHistoryClient"/> over Yahoo's public chart endpoint
/// (<c>v8/finance/chart/{symbol}?interval=1d</c>) — the same endpoint the Python <c>yfinance</c> library
/// uses for daily history.
/// </summary>
/// <remarks>
/// This deliberately does not use the <c>YahooFinanceApi</c> package: its historical method calls
/// <c>v7/finance/download</c>, which requires a session cookie and crumb and now answers HTTP 401 for every
/// symbol. The chart endpoint needs no session, only a browser-style user agent.
/// </remarks>
public sealed class YahooChartHistoryClient : IYahooHistoryClient
{
    private const string BaseUrl = "https://query1.finance.yahoo.com/v8/finance/chart/";

    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

    private static readonly HttpClient SharedHttpClient = CreateHttpClient();

    private readonly HttpClient _httpClient;

    /// <summary>Creates a client that shares the add-in's single <see cref="HttpClient"/>.</summary>
    public YahooChartHistoryClient()
        : this(SharedHttpClient)
    {
    }

    /// <summary>Creates a client over a supplied <see cref="HttpClient"/> (used by tests).</summary>
    public YahooChartHistoryClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

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

        var requestUri = BuildRequestUri(symbol, startInclusive, endInclusive);

        using var response = await _httpClient
            .GetAsync(requestUri, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);

        var payload = await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        if (payload.Length == 0)
            throw new HttpRequestException($"Yahoo Finance returned an empty response (HTTP {(int)response.StatusCode}).");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException ex)
        {
            // An HTML block page or proxy error: worth another attempt.
            throw new HttpRequestException(
                $"Yahoo Finance returned an unreadable response (HTTP {(int)response.StatusCode}).", ex);
        }

        using (document)
        {
            var chart = document.RootElement.GetProperty("chart");

            // Yahoo reports unknown tickers as an inline error object, often with a 404 status.
            if (chart.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                var description = error.TryGetProperty("description", out var descriptionElement)
                    ? descriptionElement.GetString()
                    : null;

                throw new FxDataException(
                    $"Yahoo Finance has no data for '{symbol}': {description ?? "unknown error"}");
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Yahoo Finance returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

            return ReadBars(chart, startInclusive, endInclusive);
        }
    }

    private static List<YahooDailyBar> ReadBars(JsonElement chart, DateOnly startInclusive, DateOnly endInclusive)
    {
        var bars = new List<YahooDailyBar>();

        if (!chart.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array
            || result.GetArrayLength() == 0)
        {
            return bars;
        }

        var series = result[0];

        if (!series.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
            return bars;

        if (!series.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quotes)
            || quotes.ValueKind != JsonValueKind.Array
            || quotes.GetArrayLength() == 0
            || !quotes[0].TryGetProperty("close", out var closes)
            || closes.ValueKind != JsonValueKind.Array)
        {
            return bars;
        }

        // Daily FX bars are stamped at midnight in the exchange's timezone (GMT for FX); shifting by the
        // reported offset gives the calendar date Yahoo means, whatever the caller's timezone is.
        var offsetSeconds = 0;
        if (series.TryGetProperty("meta", out var meta)
            && meta.TryGetProperty("gmtoffset", out var offsetElement)
            && offsetElement.TryGetInt32(out var offset))
        {
            offsetSeconds = offset;
        }

        var count = Math.Min(timestamps.GetArrayLength(), closes.GetArrayLength());

        for (var i = 0; i < count; i++)
        {
            var timestamp = timestamps[i];
            var close = closes[i];

            // Holidays and halted sessions come back as nulls in the close array.
            if (timestamp.ValueKind != JsonValueKind.Number || close.ValueKind != JsonValueKind.Number)
                continue;

            var date = ToTradingDate(timestamp.GetInt64(), offsetSeconds);

            if (date < startInclusive || date > endInclusive)
                continue;

            // Parse the JSON number straight to decimal: no double round trip, no lost digits.
            if (!close.TryGetDecimal(out var value) || value <= 0m)
                continue;

            bars.Add(new YahooDailyBar(date, value));
        }

        return bars;
    }

    private static DateOnly ToTradingDate(long unixSeconds, int offsetSeconds) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime.AddSeconds(offsetSeconds));

    private static Uri BuildRequestUri(string symbol, DateOnly startInclusive, DateOnly endInclusive)
    {
        // Widen the window by a day on each side: Yahoo resolves bounds against the exchange timezone and
        // rounds to whole sessions, and the range filter downstream keeps the requested dates exact.
        var period1 = ToUnixSeconds(startInclusive.AddDays(-1));
        var period2 = ToUnixSeconds(endInclusive.AddDays(1));

        var url = string.Create(
            CultureInfo.InvariantCulture,
            $"{BaseUrl}{Uri.EscapeDataString(symbol)}?interval=1d&events=history&period1={period1}&period2={period2}");

        return new Uri(url, UriKind.Absolute);
    }

    private static long ToUnixSeconds(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");

        return client;
    }
}
