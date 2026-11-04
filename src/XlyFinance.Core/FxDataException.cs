namespace XlyFinance.Core;

/// <summary>
/// Raised by a <see cref="IFxHistoryProvider"/> when historic rates cannot be retrieved.
/// Anything else thrown by a provider is treated as an unexpected bug.
/// </summary>
public sealed class FxDataException : Exception
{
    public FxDataException(string message)
        : base(message)
    {
    }

    public FxDataException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
