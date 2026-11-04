namespace XlyFinance.Core;

/// <summary>Classifies why a FX request could not be served.</summary>
public enum FxErrorKind
{
    /// <summary>The provider answered successfully, but no usable observation fell inside the range.</summary>
    NoData,

    /// <summary>Yahoo Finance could not be reached, or rejected the request.</summary>
    ProviderUnavailable,

    /// <summary>Anything else. Always worth reporting with the exception detail.</summary>
    Unexpected,
}

/// <summary>A failure the caller can translate into an Excel error and a diagnostic message.</summary>
/// <param name="Kind">The broad category of the failure.</param>
/// <param name="Message">A message suitable for showing to a user.</param>
/// <param name="Exception">The underlying exception, when there is one.</param>
public sealed record FxError(FxErrorKind Kind, string Message, Exception? Exception = null);
