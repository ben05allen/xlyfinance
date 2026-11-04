namespace XlyFinance.Core;

/// <summary>
/// A single daily observation for a currency pair.
/// </summary>
/// <param name="Date">
/// The trading date. Yahoo Finance reports daily FX bars against the New York session, so weekends and
/// market holidays simply do not appear in the data set.
/// </param>
/// <param name="Close">The daily closing rate, quoted as units of the quote currency per unit of the base currency.</param>
public readonly record struct FxRate(DateOnly Date, decimal Close);
