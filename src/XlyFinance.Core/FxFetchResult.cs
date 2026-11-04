namespace XlyFinance.Core;

/// <summary>
/// Outcome of a FX history request: either the ordered observations or a described failure.
/// </summary>
public sealed class FxFetchResult
{
    private FxFetchResult(IReadOnlyList<FxRate> rates, FxError? error)
    {
        Rates = rates;
        Error = error;
    }

    /// <summary>The observations, oldest first. Empty when <see cref="Success"/> is <c>false</c>.</summary>
    public IReadOnlyList<FxRate> Rates { get; }

    /// <summary>Details of the failure, or <c>null</c> on success.</summary>
    public FxError? Error { get; }

    /// <summary><c>true</c> when <see cref="Rates"/> holds at least one observation.</summary>
    public bool Success => Error is null;

    public static FxFetchResult Ok(IReadOnlyList<FxRate> rates) =>
        new(rates ?? throw new ArgumentNullException(nameof(rates)), null);

    public static FxFetchResult Fail(FxError error) =>
        new([], error ?? throw new ArgumentNullException(nameof(error)));
}
