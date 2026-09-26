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
