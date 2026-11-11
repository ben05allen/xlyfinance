using ExcelDna.Integration;

namespace XlyFinance.AddIn;

/// <summary>Retains the latest diagnostic and pushes changes to subscribed Excel cells.</summary>
internal sealed class LastErrorObservable : IExcelObservable
{
    private readonly object _gate = new();
    private readonly List<IExcelObserver> _observers = [];
    private string _message = "No FX failure has been recorded since Excel started.";

    public void Publish(string message)
    {
        // Serialize the initial value, updates and disposal so a subscriber cannot receive an older
        // value after a newer one, or a notification after its subscription has been disposed.
        lock (_gate)
        {
            _message = message;
            foreach (var observer in _observers.ToArray())
                observer.OnNext(message);
        }
    }

    public IDisposable Subscribe(IExcelObserver observer)
    {
        lock (_gate)
        {
            _observers.Add(observer);
            observer.OnNext(_message);
        }

        return new Subscription(this, observer);
    }

    private sealed class Subscription(LastErrorObservable owner, IExcelObserver observer) : IDisposable
    {
        public void Dispose()
        {
            lock (owner._gate)
                owner._observers.Remove(observer);
        }
    }
}
