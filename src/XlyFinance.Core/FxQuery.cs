using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace XlyFinance.Core;

/// <summary>
/// A validated request for daily FX closes over an inclusive date range.
/// </summary>
public sealed record FxQuery
{
    private FxQuery(string symbol, DateOnly start, DateOnly end)
    {
        Symbol = symbol;
        Start = start;
        End = end;
    }

    /// <summary>The Yahoo Finance ticker, for example <c>EURUSD=X</c>.</summary>
    public string Symbol { get; }

    /// <summary>The pair as a human would write it, for example <c>EUR/USD</c>.</summary>
    public string DisplayName => FxPair.ToDisplayName(Symbol);

    /// <summary>First date of the range (inclusive).</summary>
    public DateOnly Start { get; }

    /// <summary>Last date of the range (inclusive).</summary>
    public DateOnly End { get; }

    /// <summary>Number of calendar days covered by the request (inclusive of both ends).</summary>
    public int DayCount => End.DayNumber - Start.DayNumber + 1;

    /// <summary>
    /// Validates a raw pair and date range coming from the spreadsheet.
    /// </summary>
    public static bool TryCreate(
        string? pair,
        DateOnly start,
        DateOnly end,
        [NotNullWhen(true)] out FxQuery? query,
        [NotNullWhen(false)] out string? error)
    {
        query = null;
        error = null;

        if (!FxPair.TryNormalize(pair, out var symbol, out error))
            return false;

        if (start > end)
        {
            error = $"start_date ({Format(start)}) must be on or before end_date ({Format(end)}).";
            return false;
        }

        query = new FxQuery(symbol, start, end);
        return true;
    }

    /// <inheritdoc />
    public override string ToString() => $"{DisplayName} {Format(Start)}..{Format(End)}";

    private static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
