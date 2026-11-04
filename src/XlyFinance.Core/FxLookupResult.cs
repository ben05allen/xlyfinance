namespace XlyFinance.Core;

/// <summary>
/// Outcome of asking for the close on specific dates: one entry per requested date, in request order,
/// or a described failure.
/// </summary>
public sealed class FxLookupResult
{
    private FxLookupResult(IReadOnlyList<decimal?> closes, FxError? error)
    {
        Closes = closes;
        Error = error;
    }

    /// <summary>
    /// One entry per requested date. <c>null</c> means that date has no fixing (weekend, holiday, no
    /// history). Empty when <see cref="Success"/> is <c>false</c>.
    /// </summary>
    public IReadOnlyList<decimal?> Closes { get; }

    /// <summary>Details of the failure, or <c>null</c> on success.</summary>
    public FxError? Error { get; }

    /// <summary><c>true</c> when the lookup ran; individual dates may still be <c>null</c>.</summary>
    public bool Success => Error is null;

    public static FxLookupResult Ok(IReadOnlyList<decimal?> closes) =>
        new(closes ?? throw new ArgumentNullException(nameof(closes)), null);

    public static FxLookupResult Fail(FxError error) =>
        new([], error ?? throw new ArgumentNullException(nameof(error)));
}
