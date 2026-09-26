namespace SolidGround.Core.Sources;

/// <summary>
/// Base type for every exception raised while geocoding an address, across all three shipped providers
/// (Census, Geocodio, Esri). Unlike <c>OpenTopographyException</c> -- the only implementation of
/// <see cref="Sources.IElevationSource"/> so far, so no caller has ever needed to catch "an elevation-source
/// failure" without knowing which concrete source it was -- <see cref="IAddressGeocoder"/> is SolidGround's
/// first interface with several interchangeable implementations selected at runtime by a settings value
/// (<see cref="AddressGeocoderFactory"/>). A caller holding an <see cref="IAddressGeocoder"/>-typed
/// reference will not statically know which provider is active; this shared base lets it catch one type that
/// means "geocoding failed, show the message" while still being able to pattern-match a concrete provider
/// subtype when it cares. Each provider still gets its own fully independent set of <c>sealed</c> leaf types
/// and its own failure-reason enum where warranted; nothing about this shared base forces a
/// lowest-common-denominator shape.
/// </summary>
public abstract class AddressGeocoderException : Exception
{
    private protected AddressGeocoderException(string providerName, string message, string redactedRequestUri, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(redactedRequestUri);
        ProviderName = providerName;
        RedactedRequestUri = redactedRequestUri;
    }

    /// <summary>"Census", "Geocodio", or "Esri" -- lets a caller that catches this base type generically still report which provider failed.</summary>
    public string ProviderName { get; }

    /// <summary>The request URI with every sensitive query parameter redacted via <c>SolidGround.Core.Http.SensitiveQueryRedactor</c>. Safe to log or display. Never contains an API key.</summary>
    public string RedactedRequestUri { get; }
}
