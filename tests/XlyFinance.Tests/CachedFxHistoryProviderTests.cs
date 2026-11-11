using XlyFinance.Core;

namespace XlyFinance.Tests;

public class CachedFxHistoryProviderTests
{
    private static readonly DateOnly Start = new(2024, 1, 1);
    private static readonly DateOnly End = new(2024, 1, 5);
    private const string Symbol = "EURUSD=X";

    [Fact]
    public async Task Reuses_a_covering_window_across_history_and_lookup_calls()
    {
        var inner = new RecordingProvider([new(Start, 1.1m), new(End, 1.2m)]);
        var service = new FxHistoryService(new CachedFxHistoryProvider(inner));
        Assert.True(FxQuery.TryCreate("eur/usd", Start, End, out var query, out _));

        var history = await service.GetDailyRatesAsync(query!);
        var lookup = await service.GetClosesAsync("EURUSD", [End, Start.AddDays(1), Start]);

        Assert.True(history.Success);
        Assert.True(lookup.Success);
        Assert.Equal(new decimal?[] { 1.2m, null, 1.1m }, lookup.Closes);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Returns_only_requested_dates_from_a_cached_window()
    {
        var inner = new RecordingProvider([new(Start, 1.1m), new(End, 1.2m)]);
        var cache = new CachedFxHistoryProvider(inner);
        await cache.GetDailyRatesAsync(Symbol, Start, End);

        var subset = await cache.GetDailyRatesAsync(Symbol, End, End);

        Assert.Equal(new FxRate(End, 1.2m), Assert.Single(subset));
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Caches_empty_coverage_so_missing_days_do_not_trigger_more_requests()
    {
        var inner = new RecordingProvider([]);
        var cache = new CachedFxHistoryProvider(inner);
        await cache.GetDailyRatesAsync(Symbol, Start, End);

        Assert.Empty(await cache.GetDailyRatesAsync(Symbol, Start.AddDays(1), End));
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Fetches_partial_overlaps_and_different_pairs_separately()
    {
        var inner = new RecordingProvider([]);
        var cache = new CachedFxHistoryProvider(inner);
        await cache.GetDailyRatesAsync(Symbol, Start, End);
        await cache.GetDailyRatesAsync(Symbol, Start, End.AddDays(1));
        await cache.GetDailyRatesAsync("GBPUSD=X", Start, End);

        Assert.Equal(3, inner.Requests.Count);
    }

    [Theory]
    [InlineData(false, 1440)]
    [InlineData(true, 5)]
    public async Task Expires_history_after_24_hours_and_current_windows_after_five_minutes(bool current, int minutes)
    {
        var clock = new ManualClock(current ? End : End.AddDays(1));
        var inner = new RecordingProvider([]);
        var cache = new CachedFxHistoryProvider(inner, clock);
        await cache.GetDailyRatesAsync(Symbol, Start, End);

        clock.Advance(TimeSpan.FromMinutes(minutes) - TimeSpan.FromSeconds(1));
        await cache.GetDailyRatesAsync(Symbol, Start, End);
        Assert.Single(inner.Requests);

        clock.Advance(TimeSpan.FromSeconds(1));
        await cache.GetDailyRatesAsync(Symbol, Start, End);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Evicts_least_recently_used_windows_when_entry_limit_is_reached()
    {
        var inner = new RecordingProvider([]);
        var cache = new CachedFxHistoryProvider(inner, maxEntries: 2);
        await cache.GetDailyRatesAsync("EURUSD=X", Start, End);
        await cache.GetDailyRatesAsync("GBPUSD=X", Start, End);
        await cache.GetDailyRatesAsync("EURUSD=X", Start, End); // Keep EUR warm.
        await cache.GetDailyRatesAsync("USDJPY=X", Start, End);
        await cache.GetDailyRatesAsync("EURUSD=X", Start, End);
        Assert.Equal(3, inner.Requests.Count);

        await cache.GetDailyRatesAsync("GBPUSD=X", Start, End);
        Assert.Equal(4, inner.Requests.Count);
    }

    [Fact]
    public async Task Also_bounds_the_total_number_of_observations()
    {
        var inner = new RecordingProvider([new(Start, 1.1m), new(End, 1.2m)]);
        var cache = new CachedFxHistoryProvider(inner, maxRates: 3);
        await cache.GetDailyRatesAsync(Symbol, Start, End);
        await cache.GetDailyRatesAsync("GBPUSD=X", Start, End);
        await cache.GetDailyRatesAsync(Symbol, Start, End);

        Assert.Equal(3, inner.Requests.Count);
    }

    [Fact]
    public async Task Returns_oversized_responses_without_caching_them()
    {
        var inner = new RecordingProvider([new(Start, 1.1m), new(End, 1.2m)]);
        var cache = new CachedFxHistoryProvider(inner, maxRates: 1);
        Assert.Equal(2, (await cache.GetDailyRatesAsync(Symbol, Start, End)).Count);
        await cache.GetDailyRatesAsync(Symbol, Start, End);

        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Does_not_cache_failures()
    {
        var inner = new RecordingProvider([])
        {
            Handler = _ => throw new FxDataException("Unavailable"),
        };
        var cache = new CachedFxHistoryProvider(inner);
        await Assert.ThrowsAsync<FxDataException>(async () => await cache.GetDailyRatesAsync(Symbol, Start, End));
        inner.Handler = _ => Task.FromResult<IReadOnlyList<FxRate>>([]);

        Assert.Empty(await cache.GetDailyRatesAsync(Symbol, Start, End));
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Shares_pending_windows_and_isolates_each_callers_cancellation()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<FxRate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new RecordingProvider([]) { Handler = _ => pending.Task };
        var cache = new CachedFxHistoryProvider(inner);
        using var cancellation = new CancellationTokenSource();
        var first = cache.GetDailyRatesAsync(Symbol, Start, End, cancellation.Token);
        var second = cache.GetDailyRatesAsync(Symbol, End, End);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await first);
        Assert.False(Assert.Single(inner.Requests).Token.CanBeCanceled);
        pending.SetResult([new(Start, 1.1m), new(End, 1.2m)]);

        Assert.Equal(new FxRate(End, 1.2m), Assert.Single(await second));
        await cache.GetDailyRatesAsync(Symbol, Start, End);
        Assert.Single(inner.Requests);
    }

    [Fact]
    public async Task Already_canceled_calls_do_not_start_a_fetch()
    {
        var inner = new RecordingProvider([]);
        var cache = new CachedFxHistoryProvider(inner);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await cache.GetDailyRatesAsync(Symbol, Start, End, new CancellationToken(true)));

        Assert.Empty(inner.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Clear_detaches_old_fetches_and_prevents_them_overwriting_new_history(bool oldFinishesFirst)
    {
        var oldFetch = new TaskCompletionSource<IReadOnlyList<FxRate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newFetch = new TaskCompletionSource<IReadOnlyList<FxRate>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var inner = new RecordingProvider([]) { Handler = index => index == 1 ? oldFetch.Task : newFetch.Task };
        var cache = new CachedFxHistoryProvider(inner);
        var oldRequest = cache.GetDailyRatesAsync(Symbol, Start, End);
        var generation = cache.Generation;

        cache.Clear();
        Assert.NotEqual(generation, cache.Generation);
        var newRequest = cache.GetDailyRatesAsync(Symbol, Start, End);
        if (oldFinishesFirst)
        {
            oldFetch.SetResult([new(Start, 1.1m)]);
            await oldRequest;
        }

        newFetch.SetResult([new(Start, 1.2m)]);
        await newRequest;
        if (!oldFinishesFirst)
        {
            oldFetch.SetResult([new(Start, 1.1m)]);
            await oldRequest;
        }

        var cached = await cache.GetDailyRatesAsync(Symbol, Start, End);
        Assert.Equal(1.2m, Assert.Single(cached).Close);
        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Clear_removes_completed_history()
    {
        var inner = new RecordingProvider([]);
        var cache = new CachedFxHistoryProvider(inner);
        await cache.GetDailyRatesAsync(Symbol, Start, End);
        cache.Clear();
        await cache.GetDailyRatesAsync(Symbol, Start, End);

        Assert.Equal(2, inner.Requests.Count);
    }

    [Fact]
    public async Task Provider_and_caller_cannot_mutate_cached_observations()
    {
        var raw = new List<FxRate> { new(Start, 1.1m) };
        var inner = new RecordingProvider(raw);
        var cache = new CachedFxHistoryProvider(inner);
        var first = await cache.GetDailyRatesAsync(Symbol, Start, End);
        raw[0] = new(Start, 9m);

        Assert.Throws<NotSupportedException>(() => ((IList<FxRate>)first)[0] = new(Start, 8m));
        Assert.Equal(1.1m, Assert.Single(await cache.GetDailyRatesAsync(Symbol, Start, End)).Close);
    }

    private sealed class RecordingProvider(IReadOnlyList<FxRate> rates) : IFxHistoryProvider
    {
        public List<(string Symbol, DateOnly Start, DateOnly End, CancellationToken Token)> Requests { get; } = [];
        public Func<int, Task<IReadOnlyList<FxRate>>>? Handler { get; set; }

        public Task<IReadOnlyList<FxRate>> GetDailyRatesAsync(
            string symbol, DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
        {
            Requests.Add((symbol, start, end, cancellationToken));
            return Handler?.Invoke(Requests.Count) ?? Task.FromResult(rates);
        }
    }

    private sealed class ManualClock(DateOnly today) : TimeProvider
    {
        private DateTimeOffset _now = new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan elapsed) => _now += elapsed;
    }
}
