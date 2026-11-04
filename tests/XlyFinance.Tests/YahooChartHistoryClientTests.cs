using System.Net;
using XlyFinance.Core;
using XlyFinance.Core.Providers.Yahoo;
using XlyFinance.Tests.Fakes;

namespace XlyFinance.Tests;

public class YahooChartHistoryClientTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly End = new(2024, 1, 5);

    // Trimmed copy of a real v8/finance/chart response for EURUSD=X, including the shape Yahoo uses:
    // parallel timestamp/close arrays with nulls where a session had no quote.
    private const string EurUsdPayload = """
        {"chart":{"result":[{"meta":{"currency":"USD","symbol":"EURUSD=X","exchangeName":"CCY",
        "gmtoffset":0,"exchangeTimezoneName":"Europe/London"},
        "timestamp":[1704067200,1704153600,1704240000,1704326400,1704412800,1704499200],
        "indicators":{"quote":[{"close":[1.1055831909179688,1.1038745641708374,null,1.0941756963729858,1.0927767753601174,0.0]}],
        "adjclose":[{"adjclose":[1.1055831909179688,1.1038745641708374,null,1.0941756963729858,1.0927767753601174,0.0]}]}}],
        "error":null}}
        """;

    [Fact]
    public async Task Reads_closes_and_skips_null_or_zero_quotes()
    {
        var handler = StubHttpMessageHandler.Json(EurUsdPayload);
        var client = new YahooChartHistoryClient(new HttpClient(handler));

        var bars = await client.GetDailyBarsAsync("EURUSD=X", Start, End);

        // The payload has six sessions: one null close (2024-01-03), one zero close (2024-01-06,
        // also outside the range) and four usable closes inside the range.
        Assert.Equal(4, bars.Count);
        Assert.Equal(new DateOnly(2024, 1, 1), bars[0].Date);
        Assert.Equal(1.1055831909179688m, bars[0].Close);
        Assert.Equal(new DateOnly(2024, 1, 2), bars[1].Date);
        Assert.Equal(new DateOnly(2024, 1, 4), bars[2].Date);
        Assert.Equal(new DateOnly(2024, 1, 5), bars[3].Date);
    }

    [Fact]
    public async Task Asks_for_a_window_widened_by_a_day_each_side()
    {
        var handler = StubHttpMessageHandler.Json(EurUsdPayload);
        var client = new YahooChartHistoryClient(new HttpClient(handler));

        await client.GetDailyBarsAsync("EURUSD=X", Start, End);

        var uri = Assert.Single(handler.RequestUris).ToString();
        Assert.Contains("interval=1d", uri);
        Assert.Contains("EURUSD%3DX", uri);
        // 2023-12-31 and 2024-01-06, 00:00 UTC.
        Assert.Contains("period1=1703980800", uri);
        Assert.Contains("period2=1704499200", uri);
    }

    [Fact]
    public async Task Shifts_timestamps_by_the_reported_exchange_offset()
    {
        // gmtoffset +3600 with a 23:00 UTC timestamp means the bar belongs to the next day locally.
        const string payload = """
            {"chart":{"result":[{"meta":{"gmtoffset":3600},
            "timestamp":[1704322800],
            "indicators":{"quote":[{"close":[1.1]}]}}],"error":null}}
            """;

        var client = new YahooChartHistoryClient(new HttpClient(StubHttpMessageHandler.Json(payload)));
        var bars = await client.GetDailyBarsAsync("EURUSD=X", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 10));

        var bar = Assert.Single(bars);
        Assert.Equal(new DateOnly(2024, 1, 4), bar.Date);
    }

    [Fact]
    public async Task Reports_unknown_tickers_as_permanent_failures()
    {
        const string payload = """
            {"chart":{"result":null,"error":{"code":"Not Found","description":"No data found, symbol may be delisted"}}}
            """;

        var client = new YahooChartHistoryClient(
            new HttpClient(StubHttpMessageHandler.Json(payload, HttpStatusCode.NotFound)));

        var exception = await Assert.ThrowsAsync<FxDataException>(
            async () => await client.GetDailyBarsAsync("NOSUCHPAIR=X", Start, End));

        Assert.Contains("No data found", exception.Message);
    }

    [Fact]
    public async Task Treats_server_errors_as_retryable()
    {
        var client = new YahooChartHistoryClient(
            new HttpClient(StubHttpMessageHandler.NotJson("<html>upstream unavailable</html>", HttpStatusCode.BadGateway)));

        await Assert.ThrowsAsync<HttpRequestException>(
            async () => await client.GetDailyBarsAsync("EURUSD=X", Start, End));
    }

    [Fact]
    public async Task Rejects_empty_responses()
    {
        var client = new YahooChartHistoryClient(
            new HttpClient(StubHttpMessageHandler.Empty(HttpStatusCode.Unauthorized)));

        await Assert.ThrowsAsync<HttpRequestException>(
            async () => await client.GetDailyBarsAsync("EURUSD=X", Start, End));
    }

    [Fact]
    public async Task Returns_no_bars_when_the_range_holds_no_sessions()
    {
        const string payload = """
            {"chart":{"result":[{"meta":{"gmtoffset":0},"timestamp":[1704412800],
            "indicators":{"quote":[{"close":[1.09]}]}}],"error":null}}
            """;

        var client = new YahooChartHistoryClient(new HttpClient(StubHttpMessageHandler.Json(payload)));

        var bars = await client.GetDailyBarsAsync("EURUSD=X", Start, Start);

        Assert.Empty(bars);
    }
}
