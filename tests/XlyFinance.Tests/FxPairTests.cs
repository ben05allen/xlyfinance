using XlyFinance.Core;

namespace XlyFinance.Tests;

public class FxPairTests
{
    [Theory]
    [InlineData("EURUSD", "EURUSD=X")]
    [InlineData("eurusd", "EURUSD=X")]
    [InlineData("EUR/USD", "EURUSD=X")]
    [InlineData(" eur / usd ", "EURUSD=X")]
    [InlineData("EUR_USD", "EURUSD=X")]
    [InlineData("EUR-USD", "EURUSD=X")]
    [InlineData("EURUSD=X", "EURUSD=X")]
    [InlineData("eurusd=x", "EURUSD=X")]
    [InlineData("EUR/USD=X", "EURUSD=X")]
    public void Normalises_input_to_yahoo_ticker(string input, string expected)
    {
        Assert.True(FxPair.TryNormalize(input, out var symbol, out var error));
        Assert.Null(error);
        Assert.Equal(expected, symbol);
    }

    [Theory]
    [InlineData((string?)null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("EUR")]
    [InlineData("EURUSDX")]
    [InlineData("EUREUR")]
    [InlineData("eur/eur")]
    [InlineData("EUR1SD")]
    [InlineData("=X")]
    public void Rejects_input_that_is_not_a_pair(string? input)
    {
        Assert.False(FxPair.TryNormalize(input, out var symbol, out var error));
        Assert.Null(symbol);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Theory]
    [InlineData("EURUSD=X", "EUR/USD")]
    [InlineData("GBPJPY=X", "GBP/JPY")]
    [InlineData("EURUSD", "EUR/USD")]
    [InlineData("", "(unknown pair)")]
    public void Formats_display_name(string symbol, string expected) =>
        Assert.Equal(expected, FxPair.ToDisplayName(symbol));
}
