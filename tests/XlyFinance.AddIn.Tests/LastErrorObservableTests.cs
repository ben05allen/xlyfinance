using ExcelDna.Integration;
using Xunit;

namespace XlyFinance.AddIn.Tests;

public class LastErrorObservableTests
{
    [Fact]
    public async Task Existing_subscriber_receives_background_failure_without_resubscribing()
    {
        var source = new LastErrorObservable();
        var observer = new RecordingObserver();
        using var subscription = source.Subscribe(observer);

        await Task.Run(() => source.Publish("Yahoo request timed out"));

        Assert.Equal(new[]
        {
            "No FX failure has been recorded since Excel started.",
            "Yahoo request timed out",
        }, observer.Values);
    }

    [Fact]
    public void New_subscriber_receives_latest_failure_and_disposal_stops_updates()
    {
        var source = new LastErrorObservable();
        source.Publish("First failure");
        var first = new RecordingObserver();
        var subscription = source.Subscribe(first);
        var second = new RecordingObserver();
        using var secondSubscription = source.Subscribe(second);

        subscription.Dispose();
        subscription.Dispose();
        source.Publish("Second failure");

        Assert.Equal(new[] { "First failure" }, first.Values);
        Assert.Equal(new[] { "First failure", "Second failure" }, second.Values);
    }

    private sealed class RecordingObserver : IExcelObserver
    {
        public List<string> Values { get; } = [];
        public void OnNext(object value) => Values.Add(Assert.IsType<string>(value));
        public void OnError(Exception exception) => throw exception;
        public void OnCompleted() => throw new InvalidOperationException("Diagnostics must stay subscribed.");
    }
}
