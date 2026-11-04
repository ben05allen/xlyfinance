using System.Diagnostics.CodeAnalysis;

namespace XlyFinance.Core;

/// <summary>
/// Turns the pair a user types in a spreadsheet (<c>"EURUSD"</c>, <c>"eur/usd"</c>) into the ticker
/// Yahoo Finance expects (<c>"EURUSD=X"</c>).
/// </summary>
public static class FxPair
{
    /// <summary>The suffix Yahoo Finance uses for currency pairs.</summary>
    public const string YahooFxSuffix = "=X";

    /// <summary>
    /// Normalises <paramref name="pair"/> to a Yahoo Finance FX ticker.
    /// </summary>
    /// <param name="pair">A 6 letter pair such as <c>EURUSD</c>, optionally with separators or the Yahoo suffix.</param>
    /// <param name="symbol">The normalised ticker, for example <c>EURUSD=X</c>.</param>
    /// <param name="error">A user facing message when the input is not a usable pair.</param>
    /// <returns><c>true</c> when a ticker was produced.</returns>
    public static bool TryNormalize(
        string? pair,
        [NotNullWhen(true)] out string? symbol,
        [NotNullWhen(false)] out string? error)
    {
        symbol = null;
        error = null;

        if (string.IsNullOrWhiteSpace(pair))
        {
            error = "A currency pair is required, for example \"EURUSD\".";
            return false;
        }

        var cleaned = pair
            .Replace("/", string.Empty, StringComparison.Ordinal)
            .Replace("\\", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();

        var code = cleaned.EndsWith(YahooFxSuffix, StringComparison.Ordinal)
            ? cleaned[..^YahooFxSuffix.Length]
            : cleaned;

        if (!IsCurrencyCodePair(code, out error))
            return false;

        if (string.Equals(code[..3], code[3..], StringComparison.Ordinal))
        {
            error = $"\"{code}\" is not a valid pair: the base and quote currencies are the same.";
            return false;
        }

        symbol = code + YahooFxSuffix;
        return true;
    }

    /// <summary>
    /// Formats a normalised ticker for messages, for example <c>EURUSD=X</c> becomes <c>EUR/USD</c>.
    /// </summary>
    public static string ToDisplayName(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            return "(unknown pair)";

        var code = symbol.EndsWith(YahooFxSuffix, StringComparison.Ordinal)
            ? symbol[..^YahooFxSuffix.Length]
            : symbol;

        return code.Length == 6 ? $"{code[..3]}/{code[3..]}" : symbol;
    }

    private static bool IsCurrencyCodePair(string code, [NotNullWhen(false)] out string? error)
    {
        error = null;

        if (code.Length != 6 || !IsUpperAsciiLetters(code))
        {
            error = $"\"{code}\" is not a currency pair. Use a 6 letter ISO pair such as \"EURUSD\".";
            return false;
        }

        return true;
    }

    private static bool IsUpperAsciiLetters(string value)
    {
        foreach (var c in value)
        {
            if (c is < 'A' or > 'Z')
                return false;
        }

        return true;
    }
}
