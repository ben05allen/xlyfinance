using XlyFinance.Core;

namespace XlyFinance.Tests.Fakes;

/// <summary>
/// Stand-in for <see cref="IFxHistoryProvider"/> used to test <see cref="FxHistoryService"/>.
/// </summary>
internal sealed class StubFxHistoryProvider : IFxHistoryProvider
{
    private readonly Func<string, DateOnly, DateOnly, CancellationToken, Task<IReadOnlyList<FxRate>>> _handler;

    public StubFxHistoryProvider(Func<string, DateOnly, DateOnly, CancellationToken, Task<IReadOnlyList<FxRate>>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public StubFxHistoryProvider(IReadOnlyList<FxRate> rates)
        : this((_, _, _, _) => Task.FromResult(rates))
    {
    }

    public string? RequestedSymbol { get; private set; }

    public DateOnly? RequestedStart { get; private set; }

    public DateOnly? RequestedEnd { get; private set; }

    public Task<IReadOnlyList<FxRate>> GetDailyRatesAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default)
    {
        RequestedSymbol = symbol;
        RequestedStart = start;
        RequestedEnd = end;
        return _handler(symbol, start, end, cancellationToken);
    }
}
