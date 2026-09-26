namespace SolidGround.Core.Sources;

/// <summary>
/// Resolves a parcel's boundary and identifying attributes from a point or an address substring. Sibling to
/// <see cref="IElevationSource"/> and <see cref="IAddressGeocoder"/>: mechanism (this interface) is kept
/// separate from policy (which implementation is selected). See <see cref="Sources.CountyParcels.CountyParcelRegistrySource"/>
/// and <see cref="Sources.LocalParcelFile.LocalParcelFileSource"/> for the two shipped implementations, and
/// docs/architecture/parcel-boundary-sources.md for the full design record.
/// </summary>
public interface IParcelBoundarySource
{
    /// <exception cref="ParcelBoundarySourceException">
    /// The lookup could not be completed: a configuration problem (e.g. an unregistered GEOID), the source
    /// rejected the request, a transport-level failure occurred, or the response could not be interpreted. A
    /// *zero-match* result (the point is outside every known parcel, or no address matches) is not an
    /// exception -- see <see cref="ParcelBoundaryAcquisition"/>.
    /// </exception>
    ValueTask<ParcelBoundaryAcquisition> FindAsync(
        ParcelBoundaryQuery query,
        CancellationToken cancellationToken = default);
}
