namespace XlyFinance.Core;

/// <summary>
/// Bounded, process-local history cache shared by all callers. Expiry controls reuse on the next request;
/// this class does not schedule refreshes. Successful empty windows are cached as well as populated ones.
/// </summary>
public sealed class CachedFxHistoryProvider : IFxHistoryProvider
{
    private static readonly TimeSpan HistoricalLifetime = TimeSpan.FromHours(24);
    private static readonly TimeSpan CurrentLifetime = TimeSpan.FromMinutes(5);
    private readonly IFxHistoryProvider _inner;
    private readonly TimeProvider _clock;
    private readonly int _maxEntries;
    private readonly int _maxRates;
    private readonly object _gate = new();
    private readonly Dictionary<Window, Entry> _entries = [];
    private readonly Dictionary<Window, TaskCompletionSource<IReadOnlyList<FxRate>>> _pending = [];
    private int _rateCount;
    private long _access;
    private long _generation;

    public CachedFxHistoryProvider(
        IFxHistoryProvider inner,
        TimeProvider? clock = null,
        int maxEntries = 64,
        int maxRates = 100_000)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRates);
        _inner = inner;
        _clock = clock ?? TimeProvider.System;
        _maxEntries = maxEntries;
        _maxRates = maxRates;
    }

    /// <summary>Included in Excel async topics so a manual refresh replaces previous results and errors.</summary>
    public long Generation
    {
        get { lock (_gate) return _generation; }
    }

    /// <summary>
    /// Invalidates stored history and detaches outstanding fetches. Those fetches can finish for existing
    /// callers, but cannot repopulate this cache or be reused by requests made after the clear.
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            _pending.Clear();
            _rateCount = 0;
            _generation++;
        }
    }

    public async Task<IReadOnlyList<FxRate>> GetDailyRatesAsync(
        string symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        if (!FxQuery.TryValidateDates(start, end, out var error))
            throw new FxDataException(error);

        var window = new Window(symbol, start, end);
        TaskCompletionSource<IReadOnlyList<FxRate>> completion;
        var startFetch = false;
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            foreach (var expired in _entries.Where(item => item.Value.Expires <= now).Select(item => item.Key).ToArray())
                Remove(expired);

            foreach (var (cachedWindow, entry) in _entries)
            {
                if (!cachedWindow.Covers(window))
                    continue;

                entry.LastAccess = ++_access;
                return Slice(entry.Rates, start, end);
            }

            // A wider in-flight window can serve a subset too. Partial overlaps are deliberately not
            // stitched together: a single fetched window records coverage, including days with no bars.
            var pending = _pending.FirstOrDefault(item => item.Key.Covers(window)).Value;
            if (pending is not null)
            {
                completion = pending;
            }
            else
            {
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending.Add(window, completion);
                startFetch = true;
            }
        }

        if (startFetch)
            _ = FetchAsync(window, completion);

        // Each caller can stop waiting without aborting a shared fetch needed by other formulas.
        // The underlying provider retains its normal per-attempt HTTP timeout and retry limits.
        var rates = await completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return Slice(rates, start, end);
    }

    private async Task FetchAsync(Window window, TaskCompletionSource<IReadOnlyList<FxRate>> completion)
    {
        try
        {
            var raw = await _inner.GetDailyRatesAsync(window.Symbol, window.Start, window.End, CancellationToken.None)
                .ConfigureAwait(false);
            // Own a snapshot so provider or caller mutations cannot corrupt a later cache hit.
            var rates = Slice(raw, window.Start, window.End);
            lock (_gate)
            {
                if (IsCurrent(window, completion))
                {
                    _pending.Remove(window);
                    if (rates.Count <= _maxRates)
                    {
                        while (_entries.Count >= _maxEntries || _rateCount + rates.Count > _maxRates)
                            Remove(_entries.MinBy(item => item.Value.LastAccess).Key);

                        var now = _clock.GetUtcNow();
                        var lifetime = window.End >= DateOnly.FromDateTime(now.UtcDateTime)
                            ? CurrentLifetime : HistoricalLifetime;
                        _entries.Add(window, new Entry(rates, now + lifetime, ++_access));
                        _rateCount += rates.Count;
                    }
                }
                completion.TrySetResult(rates);
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (IsCurrent(window, completion))
                    _pending.Remove(window);
                completion.TrySetException(ex);
                // All waiters may have canceled. Observe the failure even if nobody remains to await it.
                _ = completion.Task.Exception;
            }
        }
    }

    private bool IsCurrent(Window window, TaskCompletionSource<IReadOnlyList<FxRate>> completion) =>
        _pending.TryGetValue(window, out var current) && ReferenceEquals(current, completion);

    private void Remove(Window window)
    {
        _rateCount -= _entries[window].Rates.Count;
        _entries.Remove(window);
    }

    private static IReadOnlyList<FxRate> Slice(IReadOnlyList<FxRate> rates, DateOnly start, DateOnly end) =>
        Array.AsReadOnly(rates.Where(rate => rate.Date >= start && rate.Date <= end).ToArray());

    private readonly record struct Window(string Symbol, DateOnly Start, DateOnly End)
    {
        public bool Covers(Window other) =>
            Symbol == other.Symbol && Start <= other.Start && End >= other.End;
    }

    private sealed class Entry(IReadOnlyList<FxRate> rates, DateTimeOffset expires, long lastAccess)
    {
        public IReadOnlyList<FxRate> Rates { get; } = rates;
        public DateTimeOffset Expires { get; } = expires;
        public long LastAccess { get; set; } = lastAccess;
    }
}
