namespace SolidGround.Core.Sources;

/// <summary>
/// Resolves a parcel's boundary and identifying attributes from one of three query shapes: a WGS 84 point
/// (<see cref="ParcelPointQuery"/>), an address substring (<see cref="ParcelAddressQuery"/>), or a point plus a
/// search radius (<see cref="ParcelNearbyQuery"/>). Sibling to <see cref="IElevationSource"/> and
/// <see cref="IAddressGeocoder"/>: mechanism (this interface) is kept separate from policy (which
/// implementation is selected). See <see cref="Sources.CountyParcels.CountyParcelRegistrySource"/> and
/// <see cref="Sources.LocalParcelFile.LocalParcelFileSource"/> for the two shipped implementations, and
/// docs/architecture/parcel-boundary-sources.md for the full design record.
/// </summary>
/// <remarks>
/// <see cref="NearbyParcelBoundaryFinder"/> calls <see cref="FindAsync"/> a second time -- with a
/// <see cref="ParcelNearbyQuery"/>, unconditionally, with no capability check or opt-out -- against whichever
/// <see cref="IParcelBoundarySource"/> it was given, whenever a first <see cref="ParcelPointQuery"/> call for
/// the identical point returns zero candidates (docs/architecture/parcel-boundary-sources.md's "Nearby-parcel
/// fallback tier"). Every implementation of this interface must therefore handle <see cref="ParcelNearbyQuery"/>
/// too, not only the original two shapes, for that fallback tier to work against it.
/// </remarks>
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
