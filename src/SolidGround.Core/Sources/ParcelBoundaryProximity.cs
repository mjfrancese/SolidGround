using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Distance;
using SolidGround.Core.Aois;

namespace SolidGround.Core.Sources;

/// <summary>
/// Shared distance/envelope math for the nearby-parcel fallback tier (see
/// <see cref="NearbyParcelBoundaryFinder"/>), reused by both shipped <see cref="IParcelBoundarySource"/>
/// implementations so the two never disagree about what "nearby" means. Public, not internal (mirrors
/// <see cref="ParcelBoundaryWgs84"/>'s own reasoning): <c>SolidGround.Core</c> grants no
/// <c>InternalsVisibleTo</c> to <c>SolidGround.Tests</c>, and this codebase's own tests call this type's
/// members directly rather than re-deriving the same formulas independently. Neither member exposes a
/// NetTopologySuite type in its own public signature
/// (<c>ArchitectureTests.NetTopologySuiteTypesNeverAppearInAnyNewPublicCoreSignature</c> covers this
/// namespace). See docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier".
/// </summary>
public static class ParcelBoundaryProximity
{
    /// <summary>
    /// <paramref name="boundary"/>'s distance from the WGS 84 point (<paramref name="latitude"/>,
    /// <paramref name="longitude"/>), in meters -- 0 when the point is inside or touching it (boundary-
    /// inclusive <c>Intersects</c>, not <c>Contains</c>, matching the exact point-in-parcel query's own
    /// convention so the two never disagree about a point exactly on a parcel line), otherwise the distance
    /// to the nearest point on its boundary.
    /// </summary>
    /// <remarks>
    /// Method (documented per AGENTS.md's numeric-contract rule): the nearest point on <paramref name="boundary"/>
    /// to the query point is found in raw WGS 84 degree space (<see cref="DistanceOp.NearestPoints"/>) -- a
    /// *search*, not a measurement, so the meters-per-degree anisotropy between latitude and longitude does
    /// not affect *which* point is nearest at this short range (tens of meters; a parcel boundary is
    /// effectively locally flat at that scale). Only that one already-tiny (lat, lon) offset is then converted
    /// to meters, using the same per-degree ellipsoid scale factors <see cref="ParcelBoundaryWgs84.ComputeAreaSquareMeters"/>
    /// already relies on (<see cref="Wgs84Ellipsoid.MetersPerDegreeLatitude"/>/<see cref="Wgs84Ellipsoid.MetersPerDegreeLongitude"/>),
    /// evaluated at the query point's own latitude, followed by a plain planar (Pythagorean) distance -- a
    /// local, equirectangular-style approximation centered on the query point, not a new geodesic/haversine
    /// formula. This introduces no transcendental math beyond what this codebase's own ellipsoid factors
    /// already contribute elsewhere.
    /// </remarks>
    public static double DistanceMeters(PolygonalRegion boundary, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));

        GeometryFactory factory = GeometryInterop.Services.CreateGeometryFactory();
        Point queryPoint = factory.CreatePoint(new Coordinate(longitude, latitude));

        if (boundary.Geometry.Intersects(queryPoint))
        {
            return 0d;
        }

        Coordinate[] nearestPoints = DistanceOp.NearestPoints(boundary.Geometry, queryPoint);
        Coordinate nearestOnBoundary = nearestPoints[0];

        double metersPerDegreeLongitude = Wgs84Ellipsoid.MetersPerDegreeLongitude(latitude);
        double metersPerDegreeLatitude = Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude);
        double dx = (nearestOnBoundary.X - longitude) * metersPerDegreeLongitude;
        double dy = (nearestOnBoundary.Y - latitude) * metersPerDegreeLatitude;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// The axis-aligned WGS 84 envelope covering a circle of <paramref name="radiusMeters"/> centered on
    /// (<paramref name="latitude"/>, <paramref name="longitude"/>) -- a square that circumscribes the
    /// requested circle (an intersects-against-this-envelope test can therefore return a candidate genuinely
    /// farther away than <paramref name="radiusMeters"/>, at a corner; <see cref="NearbyParcelBoundaryFinder"/>
    /// is what drops those, by true distance, not this envelope alone), converted from meters to degrees with
    /// the same per-degree ellipsoid factors <see cref="DistanceMeters"/> uses, evaluated at
    /// <paramref name="latitude"/>. Clamped to the valid geographic range ([-90, 90] latitude, [-180, 180]
    /// longitude) rather than ever producing an out-of-range <see cref="PlanarEnvelope"/>.
    /// </summary>
    public static PlanarEnvelope ComputeEnvelope(double latitude, double longitude, double radiusMeters)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitude, nameof(latitude));
        Wgs84BoundingBoxAoi.ValidateLongitude(longitude, nameof(longitude));
        if (!double.IsFinite(radiusMeters) || radiusMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(radiusMeters), radiusMeters, "Search radius must be finite and positive.");
        }

        double degreesLatitude = radiusMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude);
        double degreesLongitude = radiusMeters / Wgs84Ellipsoid.MetersPerDegreeLongitude(latitude);

        double minY = Math.Max(latitude - degreesLatitude, -90d);
        double maxY = Math.Min(latitude + degreesLatitude, 90d);
        double minX = Math.Max(longitude - degreesLongitude, -180d);
        double maxX = Math.Min(longitude + degreesLongitude, 180d);
        return new PlanarEnvelope(minX, minY, maxX, maxY);
    }
}
