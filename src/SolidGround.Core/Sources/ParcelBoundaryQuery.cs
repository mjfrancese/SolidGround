using SolidGround.Core.Aois;

namespace SolidGround.Core.Sources;

/// <summary>
/// One input shape an <see cref="IParcelBoundarySource"/> can be asked to resolve. Mirrors
/// <c>AreaOfInterest</c>'s own pattern -- one operation, several input shapes selected by the caller's own
/// concrete type, not a "kind" flag -- rather than two separate interface methods.
/// </summary>
public abstract record ParcelBoundaryQuery;

/// <summary>A WGS 84 point to test for parcel containment/intersection.</summary>
public sealed record ParcelPointQuery : ParcelBoundaryQuery
{
    public ParcelPointQuery(double latitude, double longitude)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }
}

/// <summary>A case-insensitive address substring to search for.</summary>
public sealed record ParcelAddressQuery : ParcelBoundaryQuery
{
    public ParcelAddressQuery(string searchText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchText);
        SearchText = searchText;
    }

    public string SearchText { get; }
}

/// <summary>
/// A WGS 84 point plus a search radius: the nearby-parcel fallback tier's own query shape, used only when a
/// prior <see cref="ParcelPointQuery"/> for the identical point returned zero candidates (a geocoded point
/// commonly lands a few meters outside its true parcel -- see <see cref="NearbyParcelBoundaryFinder"/> and
/// docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier"). A source that supports this
/// query type answers it with every candidate whose boundary intersects the envelope
/// <see cref="ParcelBoundaryProximity.ComputeEnvelope"/> derives from <see cref="Latitude"/>/<see cref="Longitude"/>/
/// <see cref="RadiusMeters"/> -- an unranked, unfiltered-by-true-distance superset (a square envelope
/// circumscribes the requested circle); <see cref="NearbyParcelBoundaryFinder"/> is what ranks by true
/// distance and drops anything actually farther than <see cref="RadiusMeters"/>.
/// </summary>
public sealed record ParcelNearbyQuery : ParcelBoundaryQuery
{
    public ParcelNearbyQuery(double latitude, double longitude, double radiusMeters)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "Search radius must be finite and positive.");
        }

        Latitude = latitude;
        Longitude = longitude;
        RadiusMeters = radiusMeters;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public double RadiusMeters { get; }
}
