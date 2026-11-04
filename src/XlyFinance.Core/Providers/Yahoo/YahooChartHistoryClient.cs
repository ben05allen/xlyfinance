using System.Globalization;
using System.Net;
using System.Text.Json;

namespace XlyFinance.Core.Providers.Yahoo;

/// <summary>
/// <see cref="IYahooHistoryClient"/> over Yahoo's public chart endpoint
/// (<c>v8/finance/chart/{symbol}?interval=1d</c>) — the same endpoint the Python <c>yfinance</c> library
/// uses for daily history.
/// </summary>
/// <remarks>
/// <para>
/// This deliberately does not use the <c>YahooFinanceApi</c> package: its historical method calls
/// <c>v7/finance/download</c>, which requires a session cookie and crumb and now answers HTTP 401 for every
/// symbol. The chart endpoint needs no session, only a browser-style user agent.
/// </para>
/// <para>
/// Failures are classified for the caller: <see cref="FxDataException"/> means the request cannot succeed as
/// stated (unknown symbol, malformed request), while everything else (timeouts, throttling, 5xx, unexpected
/// payloads) is thrown as-is so <see cref="YahooFxHistoryProvider"/> can retry it.
/// </para>
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

        var parsed = TryParseJson(payload, out var document);

        using (document)
        {
            var yahooError = parsed && TryGetChartError(document!.RootElement, out var description)
                ? description
                : null;

            if (!response.IsSuccessStatusCode)
            {
                var detail = yahooError is null
                    ? $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})"
                    : $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase}): {yahooError}";

                // Status, not the body, decides whether another attempt is worth making: Yahoo wraps
                // temporary outages in the same error object it uses for unknown symbols.
                if (IsPermanentFailure(response.StatusCode))
                    throw new FxDataException($"Yahoo Finance has no data for '{symbol}' ({detail}).");

                throw new HttpRequestException($"Yahoo Finance returned {detail}.");
            }

            if (!parsed)
                throw UnexpectedResponse("the body is not valid JSON");

            if (!document!.RootElement.TryGetProperty("chart", out var chart) || chart.ValueKind != JsonValueKind.Object)
                throw UnexpectedResponse("the body has no 'chart' object");

            if (yahooError is not null)
                throw new FxDataException($"Yahoo Finance rejected the request for '{symbol}': {yahooError}");

            return ReadBars(chart, startInclusive, endInclusive);
        }
    }

    private static List<YahooDailyBar> ReadBars(JsonElement chart, DateOnly startInclusive, DateOnly endInclusive)
    {
        var bars = new List<YahooDailyBar>();

        if (!chart.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array)
            throw UnexpectedResponse("the body has no 'result' array");

        if (result.GetArrayLength() == 0)
            return bars;

        var series = result[0];

        if (!series.TryGetProperty("timestamp", out var timestamps) || timestamps.ValueKind != JsonValueKind.Array)
            throw UnexpectedResponse("the series has no 'timestamp' array");

        // An empty timestamp array is a legitimate answer: no session in that window.
        var count = timestamps.GetArrayLength();
        if (count == 0)
            return bars;

        if (!series.TryGetProperty("indicators", out var indicators)
            || !indicators.TryGetProperty("quote", out var quotes)
            || quotes.ValueKind != JsonValueKind.Array
            || quotes.GetArrayLength() == 0
            || !quotes[0].TryGetProperty("close", out var closes)
            || closes.ValueKind != JsonValueKind.Array)
        {
            throw UnexpectedResponse("the series has timestamps but no 'close' array");
        }

        // Yahoo always returns these in lockstep. A mismatch means we are not reading what we think we are,
        // which must not be reported as "that date had no fixing".
        if (closes.GetArrayLength() != count)
            throw UnexpectedResponse($"the series has {count} timestamps but {closes.GetArrayLength()} closes");

        var exchangeTimeZone = ResolveExchangeTimeZone(series, out var offsetSeconds);

        for (var i = 0; i < count; i++)
        {
            var timestamp = timestamps[i];
            var close = closes[i];

            // Holidays and halted sessions come back as nulls in the close array.
            if (timestamp.ValueKind != JsonValueKind.Number || close.ValueKind != JsonValueKind.Number)
                continue;

            var date = ToTradingDate(timestamp.GetInt64(), exchangeTimeZone, offsetSeconds);

            if (date < startInclusive || date > endInclusive)
                continue;

            // Parse the JSON number straight to decimal: no double round trip, no lost digits.
            if (!close.TryGetDecimal(out var value) || value <= 0m)
                continue;

            bars.Add(new YahooDailyBar(date, value));
        }

        return bars;
    }

    /// <summary>
    /// Finds the exchange's timezone so each bar can be dated with the rules in force <em>on that date</em>.
    /// </summary>
    /// <remarks>
    /// <c>meta.gmtoffset</c> is the exchange's <em>current</em> offset, so using it for history shifts every
    /// summer bar by an hour and, for a midnight-local stamp, onto the previous calendar day.
    /// </remarks>
    /// <param name="series">The chart result element, which carries <c>meta</c>.</param>
    /// <param name="offsetSeconds">The reported current offset, used only if the timezone cannot be resolved.</param>
    private static TimeZoneInfo? ResolveExchangeTimeZone(JsonElement series, out int offsetSeconds)
    {
        offsetSeconds = 0;

        if (!series.TryGetProperty("meta", out var meta) || meta.ValueKind != JsonValueKind.Object)
            return null;

        if (meta.TryGetProperty("gmtoffset", out var offsetElement) && offsetElement.TryGetInt32(out var offset))
            offsetSeconds = offset;

        if (!meta.TryGetProperty("exchangeTimezoneName", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            return null;

        var name = nameElement.GetString();
        if (string.IsNullOrWhiteSpace(name))
            return null;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(name);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
        catch (InvalidTimeZoneException)
        {
            return null;
        }
    }

    private static DateOnly ToTradingDate(long unixSeconds, TimeZoneInfo? exchangeTimeZone, int offsetSeconds)
    {
        var utc = DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;

        return exchangeTimeZone is null
            ? DateOnly.FromDateTime(utc.AddSeconds(offsetSeconds))
            : DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, exchangeTimeZone));
    }

    /// <summary>
    /// Reads the description out of a Yahoo <c>chart.error</c> object, if there is one.
    /// </summary>
    private static bool TryGetChartError(JsonElement root, out string? description)
    {
        description = null;

        if (!root.TryGetProperty("chart", out var chart) || chart.ValueKind != JsonValueKind.Object)
            return false;

        if (!chart.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            return false;

        description =
            (error.TryGetProperty("description", out var descriptionElement) && descriptionElement.ValueKind == JsonValueKind.String
                ? descriptionElement.GetString()
                : null)
            ?? (error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                ? codeElement.GetString()
                : null)
            ?? "unknown error";

        return true;
    }

    private static bool TryParseJson(byte[] payload, out JsonDocument? document)
    {
        document = null;

        if (payload.Length == 0)
            return false;

        try
        {
            document = JsonDocument.Parse(payload);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Unknown symbols and malformed requests will not improve on a retry; everything else might.</summary>
    private static bool IsPermanentFailure(HttpStatusCode status) =>
        status is HttpStatusCode.BadRequest or HttpStatusCode.NotFound or HttpStatusCode.Gone;

    private static HttpRequestException UnexpectedResponse(string detail) =>
        new($"Yahoo Finance returned an unexpected response ({detail}).");

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
