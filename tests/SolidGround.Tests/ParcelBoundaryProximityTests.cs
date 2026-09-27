using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;

namespace SolidGround.Tests;

/// <summary>
/// Proves <see cref="ParcelBoundaryProximity"/>'s two math primitives -- <see cref="ParcelBoundaryProximity.DistanceMeters"/>
/// and <see cref="ParcelBoundaryProximity.ComputeEnvelope"/> -- independently of <see cref="NearbyParcelBoundaryFinder"/>'s
/// own orchestration (see <see cref="NearbyParcelBoundaryFinderTests"/>). Uses small, inline WKT square
/// literals (the same "inline small ring arrays" style <see cref="ParcelBoundaryAreaTests"/> already uses)
/// rather than a committed fixture, centered near the public example site coordinate so every literal here
/// stays within <c>PersonalInformationGuardTests</c>' 0.01-degree tolerance of it.
/// </summary>
public sealed class ParcelBoundaryProximityTests
{
    // A small square: longitude -93.6045 to -93.6035 (width 0.001 deg), latitude 41.5905 to 41.5915 (height
    // 0.001 deg) -- both edges running exactly north-south/east-west (no tilt), so a point due east/north of
    // an edge has an exact, hand-derivable nearest point with no shape-induced approximation error.
    private const string SquareWkt = "POLYGON((-93.6045 41.5905, -93.6035 41.5905, -93.6035 41.5915, -93.6045 41.5915, -93.6045 41.5905))";

    [Fact]
    public void DistanceMetersIsZeroWhenThePointIsInsideTheBoundary()
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, SquareWkt, GeographicReference());

        double actual = ParcelBoundaryProximity.DistanceMeters(boundary, 41.5910d, -93.6040d);

        Assert.Equal(0d, actual);
    }

    [Fact]
    public void DistanceMetersIsZeroWhenThePointIsExactlyOnTheBoundary()
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, SquareWkt, GeographicReference());

        // On the square's south edge, boundary-inclusive -- matching the point-in-parcel query's own
        // Intersects (not Contains) convention, so the two never disagree about a point on a parcel line.
        double actual = ParcelBoundaryProximity.DistanceMeters(boundary, 41.5905d, -93.6040d);

        Assert.Equal(0d, actual);
    }

    [Fact]
    public void DistanceMetersComputesTheLocalMetricDistanceForAPointDueEastOfAVerticalEdge()
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, SquareWkt, GeographicReference());
        const double queryLatitude = 41.5910d; // within the square's own latitude range: the nearest boundary
                                                // point sits directly on the vertical east edge, dy = 0 exactly.
        const double offsetDegrees = 0.0001d;
        double queryLongitude = -93.6035d + offsetDegrees;

        double actual = ParcelBoundaryProximity.DistanceMeters(boundary, queryLatitude, queryLongitude);

        // Independently derived from the same per-degree ellipsoid factor this method itself uses (not a
        // second, hand-typed constant): dy is exactly 0 here because the edge is exactly vertical and the
        // query point's own latitude already sits on it, so this is not a tautological re-check of the
        // method's own internal nearest-point search, only of the degrees-to-meters conversion.
        double expected = offsetDegrees * Wgs84Ellipsoid.MetersPerDegreeLongitude(queryLatitude);
        double relativeDifference = Math.Abs(actual - expected) / expected;
        Assert.True(relativeDifference < 1e-6, $"Expected approximately {expected} m, computed {actual} m.");
    }

    [Fact]
    public void DistanceMetersRejectsANullBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => ParcelBoundaryProximity.DistanceMeters(null!, 41.591194d, -93.603806d));
    }

    [Theory]
    [InlineData(double.NaN, -93.603806d)]
    [InlineData(90.0001d, -93.603806d)]
    [InlineData(-90.0001d, -93.603806d)]
    public void DistanceMetersRejectsAnOutOfRangeOrNonFiniteLatitude(double latitude, double longitude)
    {
        PolygonalRegion boundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, SquareWkt, GeographicReference());

        Assert.Throws<ArgumentOutOfRangeException>(() => ParcelBoundaryProximity.DistanceMeters(boundary, latitude, longitude));
    }

    [Fact]
    public void ComputeEnvelopeProducesBoundsConsistentWithTheEllipsoidScaleFactorsAtTheGivenLatitude()
    {
        const double latitude = 41.591194d;
        const double longitude = -93.603806d;
        const double radiusMeters = 30d;

        PlanarEnvelope envelope = ParcelBoundaryProximity.ComputeEnvelope(latitude, longitude, radiusMeters);

        double expectedDegreesLongitude = radiusMeters / Wgs84Ellipsoid.MetersPerDegreeLongitude(latitude);
        double expectedDegreesLatitude = radiusMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude);

        AssertClose(longitude - expectedDegreesLongitude, envelope.MinX);
        AssertClose(longitude + expectedDegreesLongitude, envelope.MaxX);
        AssertClose(latitude - expectedDegreesLatitude, envelope.MinY);
        AssertClose(latitude + expectedDegreesLatitude, envelope.MaxY);
    }

    [Fact]
    public void ComputeEnvelopeClampsToTheGeographicRangeRatherThanThrowingForAnExtremeRadius()
    {
        // Not a realistic radius (the default is 30 m), but proves the defensive clamp keeps MinY/MaxY within
        // [-90, 90] and MinX/MaxX within [-180, 180] instead of ever producing an invalid PlanarEnvelope.
        PlanarEnvelope envelope = ParcelBoundaryProximity.ComputeEnvelope(89.9d, 179.9d, 5_000_000d);

        Assert.True(envelope.MinY >= -90d);
        Assert.True(envelope.MaxY <= 90d);
        Assert.True(envelope.MinX >= -180d);
        Assert.True(envelope.MaxX <= 180d);
    }

    [Theory]
    [InlineData(double.NaN, -93.603806d)]
    [InlineData(90.0001d, -93.603806d)]
    [InlineData(41.591194d, 180.0001d)]
    public void ComputeEnvelopeRejectsAnOutOfRangeOrNonFiniteCoordinate(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ParcelBoundaryProximity.ComputeEnvelope(latitude, longitude, 30d));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ComputeEnvelopeRejectsANonFiniteOrNonPositiveRadius(double radiusMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ParcelBoundaryProximity.ComputeEnvelope(41.591194d, -93.603806d, radiusMeters));
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.True(Math.Abs(expected - actual) < 1e-9, $"Expected {expected}, got {actual}.");
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
