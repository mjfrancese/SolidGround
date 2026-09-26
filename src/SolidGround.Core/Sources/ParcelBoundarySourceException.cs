namespace SolidGround.Core.Sources;

/// <summary>
/// Base type for every exception raised while resolving a parcel boundary, across both shipped sources
/// (the county REST registry and the local parcel file). Mirrors <see cref="AddressGeocoderException"/>'s
/// exact shape and rationale: <see cref="IParcelBoundarySource"/> is a second interface with multiple
/// interchangeable implementations, so a caller holding an <see cref="IParcelBoundarySource"/> reference needs
/// one catchable "parcel lookup failed" type without knowing which concrete source is active.
/// </summary>
public abstract class ParcelBoundarySourceException : Exception
{
    private protected ParcelBoundarySourceException(string sourceName, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        SourceName = sourceName;
    }

    /// <summary>"CountyParcelRegistry" or "LocalParcelFile" -- lets a caller that catches this base type generically still report which source failed.</summary>
    public string SourceName { get; }
}
