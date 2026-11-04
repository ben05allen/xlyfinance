namespace XlyFinance.Core;

/// <summary>
/// Source of daily FX closes. Implementations are responsible for talking to the outside world;
/// everything above this interface is pure and unit testable.
/// </summary>
public interface IFxHistoryProvider
{
    /// <summary>
    /// Loads daily closes for <paramref name="symbol"/> between two dates, both inclusive.
    /// </summary>
    /// <param name="symbol">A Yahoo style ticker, for example <c>EURUSD=X</c>.</param>
    /// <param name="start">First date to include.</param>
    /// <param name="end">Last date to include.</param>
    /// <param name="cancellationToken">Cancels the request, for example when Excel abandons the calculation.</param>
    /// <returns>The observations the provider found. May be empty; may be unordered and may contain duplicates.</returns>
    /// <exception cref="FxDataException">The provider could not retrieve data.</exception>
    Task<IReadOnlyList<FxRate>> GetDailyRatesAsync(
        string symbol,
        DateOnly start,
        DateOnly end,
        CancellationToken cancellationToken = default);
}
