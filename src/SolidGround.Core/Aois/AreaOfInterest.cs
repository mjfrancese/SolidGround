using SolidGround.Core.Metadata;

namespace SolidGround.Core.Aois;

/// <summary>
/// Identifies a geographic area requested from an elevation source.
/// </summary>
public abstract record AreaOfInterest;

/// <summary>
/// A WGS 84 geographic bounding box that does not cross the antimeridian.
/// </summary>
public sealed record Wgs84BoundingBoxAoi : AreaOfInterest
{
    public Wgs84BoundingBoxAoi(double westLongitude, double southLatitude, double eastLongitude, double northLatitude)
    {
        ValidateLongitude(westLongitude, nameof(westLongitude));
        ValidateLongitude(eastLongitude, nameof(eastLongitude));
        ValidateLatitude(southLatitude, nameof(southLatitude));
        ValidateLatitude(northLatitude, nameof(northLatitude));
        if (westLongitude >= eastLongitude)
        {
            throw new ArgumentException("West longitude must be less than east longitude.", nameof(westLongitude));
        }

        if (southLatitude >= northLatitude)
        {
            throw new ArgumentException("South latitude must be less than north latitude.", nameof(southLatitude));
        }

        WestLongitude = westLongitude;
        SouthLatitude = southLatitude;
        EastLongitude = eastLongitude;
        NorthLatitude = northLatitude;
    }

    public double WestLongitude { get; }
    public double SouthLatitude { get; }
    public double EastLongitude { get; }
    public double NorthLatitude { get; }

    internal static void ValidateLatitude(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < -90d or > 90d)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Latitude must be finite and between -90 and 90 degrees.");
        }
    }

    internal static void ValidateLongitude(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value is < -180d or > 180d)
        {
            throw new ArgumentOutOfRangeException(parameterName, value, "Longitude must be finite and between -180 and 180 degrees.");
        }
    }
}

/// <summary>A WGS 84 location and a positive radial distance in meters.</summary>
public sealed record Wgs84RadiusAoi : AreaOfInterest
{
    public Wgs84RadiusAoi(double latitude, double longitude, double radiusMeters)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "Radius must be finite and positive.");
        }

        Latitude = latitude;
        Longitude = longitude;
        RadiusMeters = radiusMeters;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public double RadiusMeters { get; }
}

/// <summary>The serialization format used for a parcel boundary.</summary>
public enum ParcelGeometryFormat { Wkt, GeoJson }

/// <summary>A parcel boundary supplied as WKT or GeoJSON. BufferMeters is applied after projection into a suitable metric CRS.</summary>
public sealed record ParcelGeometryAoi : AreaOfInterest
{
    public ParcelGeometryAoi(ParcelGeometryFormat format, string geometry, HorizontalReference horizontalReference, double bufferMeters = 0d)
    {
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported parcel geometry format.");
        }

        if (string.IsNullOrWhiteSpace(geometry))
        {
            throw new ArgumentException("Parcel geometry is required.", nameof(geometry));
        }

        ArgumentNullException.ThrowIfNull(horizontalReference);

        if (!double.IsFinite(bufferMeters) || bufferMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferMeters), bufferMeters, "Buffer must be finite and non-negative.");
        }

        Format = format;
        Geometry = geometry;
        HorizontalReference = horizontalReference;
        BufferMeters = bufferMeters;
    }

    public ParcelGeometryFormat Format { get; }
    public string Geometry { get; }
    public HorizontalReference HorizontalReference { get; }
    public double BufferMeters { get; }
}
