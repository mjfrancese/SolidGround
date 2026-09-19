using System.Globalization;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class AoiNormalizerTests
{
    private const double ExampleSiteLatitude = [withheld]d;
    private const double ExampleSiteLongitude = [withheld]d;

    [Fact]
    public void NormalizeRejectsANullAreaOfInterest()
    {
        Assert.Throws<ArgumentNullException>(() => AoiNormalizer.Normalize(null!));
    }

    [Fact]
    public void BoundingBoxWithZeroMarginProducesAValueEqualFetchEnvelope()
    {
        Wgs84BoundingBoxAoi bbox = new([withheld]d, [withheld]d, [withheld]d, [withheld]d);

        NormalizedAoi result = AoiNormalizer.Normalize(bbox);

        Assert.Equal(bbox, result.FetchEnvelope);
        Assert.Same(bbox, result.Source);
        Assert.Null(result.Parcel);
        Assert.Equal(LinearDistance.Zero, result.Buffer);
        Assert.Equal(FetchEnvelopeBasis.BoundingBox, result.Basis);
        Assert.Equal(LinearDistance.Zero, result.EnvelopeMargin);
    }

    [Fact]
    public void BoundingBoxMarginPadsConservativelyUsingTheExtremeLongitudeAndAlwaysTheEquatorForLatitude()
    {
        // South = 10 (|10|), north = 50 (|50|): the longitude factor must use the latitude with the larger
        // absolute value (50, where a degree of longitude is narrowest). The latitude factor always uses the
        // equator (0) — the global minimum of MetersPerDegreeLatitude over the entire domain, not just this
        // envelope's own latitudes — regardless of where the envelope sits, so the pad over-covers
        // unconditionally rather than only for a small pad; see docs/architecture/aoi-normalization-and-clipping.md.
        Wgs84BoundingBoxAoi bbox = new(-10d, 10d, -9d, 50d);
        LinearDistance margin = LinearDistance.Meters(1000d);
        AoiNormalizationOptions options = new() { EnvelopeMargin = margin };

        NormalizedAoi result = AoiNormalizer.Normalize(bbox, options);

        double expectedDLon = 1000d / Wgs84Ellipsoid.MetersPerDegreeLongitude(50d);
        double expectedDLat = 1000d / Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);
        Assert.Equal(-10d - expectedDLon, result.FetchEnvelope.WestLongitude, 9);
        Assert.Equal(-9d + expectedDLon, result.FetchEnvelope.EastLongitude, 9);
        Assert.Equal(10d - expectedDLat, result.FetchEnvelope.SouthLatitude, 9);
        Assert.Equal(50d + expectedDLat, result.FetchEnvelope.NorthLatitude, 9);
        Assert.Equal(FetchEnvelopeBasis.BoundingBox, result.Basis);
        Assert.Equal(margin, result.EnvelopeMargin);
    }

    [Fact]
    public void PadEnvelopeNeverUnderCoversTheTrueGeodesicDistanceForALargeNonStraddlingPad()
    {
        // A large (50 km) margin on a bounding box entirely north of the equator is exactly the regime where
        // evaluating the latitude factor at the envelope's own latitude (rather than unconditionally at the
        // equator) would under-cover by a few meters (confirmed independently while diagnosing this test: about
        // 1.9 m short at this latitude and pad size). This test checks the padded envelope against ground
        // truth — a true meridian arc length computed by numerically integrating
        // Wgs84Ellipsoid.MetersPerDegreeLatitude, entirely independently of AoiNormalizer's own PadEnvelope
        // formula — rather than re-deriving the expected value from that same formula, so it would have caught
        // the under-coverage an earlier version of PadEnvelope had.
        Wgs84BoundingBoxAoi bbox = new([withheld]d, ExampleSiteLatitude, [withheld]d, ExampleSiteLatitude + 0.02d);
        LinearDistance margin = LinearDistance.Meters(50000d);
        AoiNormalizationOptions options = new() { EnvelopeMargin = margin };

        NormalizedAoi result = AoiNormalizer.Normalize(bbox, options);

        double trueSouthPadDistance = TrueMeridianDistanceMeters(result.FetchEnvelope.SouthLatitude, ExampleSiteLatitude);
        double trueNorthPadDistance = TrueMeridianDistanceMeters(ExampleSiteLatitude + 0.02d, result.FetchEnvelope.NorthLatitude);
        Assert.True(
            trueSouthPadDistance >= 50000d,
            $"South pad covered only {trueSouthPadDistance.ToString(CultureInfo.InvariantCulture)} m of true geodesic distance, less than the requested 50000 m.");
        Assert.True(
            trueNorthPadDistance >= 50000d,
            $"North pad covered only {trueNorthPadDistance.ToString(CultureInfo.InvariantCulture)} m of true geodesic distance, less than the requested 50000 m.");
    }

    [Fact]
    public void BoundingBoxMarginStraddlingTheEquatorPadsUsingTheEquatorNotTheNearerEndpoint()
    {
        // South = -10, north = 89: the envelope's smallest-absolute-value latitude is the interior point 0,
        // not either endpoint (|south| = 10 is the smaller endpoint, but 0 is smaller still and lies inside
        // [-10, 89]). Padding with the south endpoint's factor instead of latitude 0's would under-cover the
        // documented "always over-covers" guarantee.
        Wgs84BoundingBoxAoi bbox = new(-1d, -10d, 1d, 89d);
        LinearDistance margin = LinearDistance.Meters(100000d);
        AoiNormalizationOptions options = new() { EnvelopeMargin = margin };

        NormalizedAoi result = AoiNormalizer.Normalize(bbox, options);

        double expectedDLat = 100000d / Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);
        Assert.Equal(-10d - expectedDLat, result.FetchEnvelope.SouthLatitude, 9);
        Assert.Equal(89d + expectedDLat, result.FetchEnvelope.NorthLatitude, 9);
    }

    [Fact]
    public void RadiusAoiPadsSymmetricallyAroundItsCenterUsingTheCenterLatitudeForBothFactors()
    {
        Wgs84RadiusAoi radiusAoi = new(ExampleSiteLatitude, ExampleSiteLongitude, LinearDistance.Meters(100d));

        NormalizedAoi result = AoiNormalizer.Normalize(radiusAoi);

        double expectedDLon = 100d / Wgs84Ellipsoid.MetersPerDegreeLongitude(ExampleSiteLatitude);
        double expectedDLat = 100d / Wgs84Ellipsoid.MetersPerDegreeLatitude(ExampleSiteLatitude);
        AssertRelativelyClose(ExampleSiteLongitude - expectedDLon, result.FetchEnvelope.WestLongitude);
        AssertRelativelyClose(ExampleSiteLongitude + expectedDLon, result.FetchEnvelope.EastLongitude);
        AssertRelativelyClose(ExampleSiteLatitude - expectedDLat, result.FetchEnvelope.SouthLatitude);
        AssertRelativelyClose(ExampleSiteLatitude + expectedDLat, result.FetchEnvelope.NorthLatitude);
        Assert.Equal(FetchEnvelopeBasis.RadiusOnWgs84Ellipsoid, result.Basis);
        Assert.Equal(LinearDistance.Zero, result.Buffer);
        Assert.Null(result.Parcel);
        Assert.Same(radiusAoi, result.Source);
    }

    [Fact]
    public void RadiusNearTheAntimeridianIsRejected()
    {
        Wgs84RadiusAoi radiusAoi = new(ExampleSiteLatitude, 179.9999d, LinearDistance.Meters(1000d));

        AoiNormalizationException exception = Assert.Throws<AoiNormalizationException>(() => AoiNormalizer.Normalize(radiusAoi));
        Assert.Contains("antimeridian", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RadiusNearThePoleIsRejected()
    {
        Wgs84RadiusAoi radiusAoi = new(89.9995d, ExampleSiteLongitude, LinearDistance.Meters(1000d));

        AoiNormalizationException exception = Assert.Throws<AoiNormalizationException>(() => AoiNormalizer.Normalize(radiusAoi));
        Assert.Contains("pole", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeographicParcelFetchEnvelopeIncludesBufferPlusMargin()
    {
        ParcelGeometryAoi parcelAoi = new(
            ParcelGeometryFormat.Wkt,
            "POLYGON (([withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld]))",
            GeographicReference(),
            LinearDistance.Meters(5d));
        AoiNormalizationOptions options = new() { EnvelopeMargin = LinearDistance.Meters(3d) };

        NormalizedAoi result = AoiNormalizer.Normalize(parcelAoi, options);

        const double padMeters = 8d; // buffer 5 + margin 3
        double expectedDLon = padMeters / Wgs84Ellipsoid.MetersPerDegreeLongitude([withheld]d); // larger |lat|
        double expectedDLat = padMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(0d); // equator: global minimum, always used
        Assert.Equal([withheld]d - expectedDLon, result.FetchEnvelope.WestLongitude, 9);
        Assert.Equal([withheld]d + expectedDLon, result.FetchEnvelope.EastLongitude, 9);
        Assert.Equal([withheld]d - expectedDLat, result.FetchEnvelope.SouthLatitude, 9);
        Assert.Equal([withheld]d + expectedDLat, result.FetchEnvelope.NorthLatitude, 9);
        Assert.Equal(FetchEnvelopeBasis.GeographicParcelEnvelope, result.Basis);
        Assert.Equal(LinearDistance.Meters(5d), result.Buffer);
        Assert.Equal(LinearDistance.Meters(3d), result.EnvelopeMargin);
        Assert.NotNull(result.Parcel);
        Assert.Equal(HorizontalReferenceKind.Geographic, result.Parcel!.HorizontalReference.Kind);
    }

    [Fact]
    public void ProjectedParcelWithoutATransformThrowsNamingIssueSix()
    {
        ParcelGeometryAoi parcelAoi = new(
            ParcelGeometryFormat.Wkt,
            "POLYGON (([withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld]))",
            ProjectedReference());

        AoiNormalizationException exception = Assert.Throws<AoiNormalizationException>(() => AoiNormalizer.Normalize(parcelAoi));
        Assert.Contains("Issue #6", exception.Message);
    }

    [Fact]
    public void ProjectedParcelWithATransformRecordsEveryShellAndHoleVertexAndBuildsTheEnvelopeFromTransformedVertices()
    {
        ParcelGeometryAoi parcelAoi = new(
            ParcelGeometryFormat.Wkt,
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 3, 3 3, 3 2, 2 2))",
            ProjectedReference());
        RecordingHorizontalTransform transform = new(
            source => new Coordinate2D((source.X / 100000d) - 90d, (source.Y / 100000d) + 38d),
            TestTransformationDefinition());

        NormalizedAoi result = AoiNormalizer.Normalize(parcelAoi, parcelToWgs84: transform);

        // 5 shell vertices (closed ring) + 5 hole vertices (closed ring) = 10 Forward calls.
        Assert.Equal(10, transform.ForwardCalls.Count);
        Assert.Contains(new Coordinate2D(0d, 0d), transform.ForwardCalls);
        Assert.Contains(new Coordinate2D(10d, 10d), transform.ForwardCalls);
        Assert.Contains(new Coordinate2D(2d, 2d), transform.ForwardCalls);
        Assert.Contains(new Coordinate2D(3d, 3d), transform.ForwardCalls);

        Assert.Equal(FetchEnvelopeBasis.TransformedParcelEnvelope, result.Basis);
        double expectedWest = (0d / 100000d) - 90d;
        double expectedEast = (10d / 100000d) - 90d;
        double expectedSouth = (0d / 100000d) + 38d;
        double expectedNorth = (10d / 100000d) + 38d;
        Assert.Equal(expectedWest, result.FetchEnvelope.WestLongitude, 9);
        Assert.Equal(expectedEast, result.FetchEnvelope.EastLongitude, 9);
        Assert.Equal(expectedSouth, result.FetchEnvelope.SouthLatitude, 9);
        Assert.Equal(expectedNorth, result.FetchEnvelope.NorthLatitude, 9);

        Assert.Same(parcelAoi, result.Source);
        Assert.NotNull(result.Parcel);
        Assert.Equal(HorizontalReferenceKind.Projected, result.Parcel!.HorizontalReference.Kind);
    }

    [Fact]
    public void NormalizedAoiRequiresAParcelExactlyWhenItsBasisIsAParcelBasis()
    {
        Wgs84BoundingBoxAoi bbox = new([withheld]d, [withheld]d, [withheld]d, [withheld]d);
        PolygonalRegion parcel = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 0))"), ProjectedReference());

        Assert.Throws<ArgumentException>(() => new NormalizedAoi(bbox, bbox, parcel, LinearDistance.Zero, FetchEnvelopeBasis.BoundingBox, LinearDistance.Zero));
        Assert.Throws<ArgumentException>(() => new NormalizedAoi(bbox, bbox, null, LinearDistance.Zero, FetchEnvelopeBasis.GeographicParcelEnvelope, LinearDistance.Zero));
    }

    private static Geometry ReadWkt(string wkt)
    {
        NtsGeometryServices services = new(new PrecisionModel(PrecisionModels.Floating), 0);
        WKTReader reader = new(services);
        return reader.Read(wkt);
    }

    private static void AssertRelativelyClose(double expected, double actual)
    {
        double relativeDifference = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(relativeDifference <= 1e-9, $"Expected {actual} to be within 1e-9 relative of {expected} (difference {relativeDifference}).");
    }

    /// <summary>
    /// Independently reimplements the true meridian arc length between <paramref name="aDegrees"/> and
    /// <paramref name="bDegrees"/> (<paramref name="aDegrees"/> &lt;= <paramref name="bDegrees"/>) with
    /// Simpson's rule over <see cref="Wgs84Ellipsoid.MetersPerDegreeLatitude"/>, which is exactly
    /// d(distance)/d(latitude-degrees), so it integrates to the true distance in meters. This exists so
    /// <see cref="PadEnvelopeNeverUnderCoversTheTrueGeodesicDistanceForALargeNonStraddlingPad"/> can check
    /// <c>AoiNormalizer</c>'s padding against ground truth rather than against <c>PadEnvelope</c>'s own
    /// formula. 20,000 subdivisions converges to well under a micrometer for the pad sizes this test uses.
    /// </summary>
    private static double TrueMeridianDistanceMeters(double aDegrees, double bDegrees)
    {
        const int Subdivisions = 20000;
        double h = (bDegrees - aDegrees) / Subdivisions;
        double sum = Wgs84Ellipsoid.MetersPerDegreeLatitude(aDegrees) + Wgs84Ellipsoid.MetersPerDegreeLatitude(bDegrees);
        for (int i = 1; i < Subdivisions; i++)
        {
            double latitude = aDegrees + (i * h);
            double coefficient = i % 2 == 0 ? 2d : 4d;
            sum += coefficient * Wgs84Ellipsoid.MetersPerDegreeLatitude(latitude);
        }

        return sum * h / 3d;
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static HorizontalTransformationDefinition TestTransformationDefinition() => new(
        ProjectedReference(),
        GeographicReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "test-double-engine",
        "1.0");

    /// <summary>A test double that records every coordinate passed to <see cref="Forward"/> and applies a fixed, invertible mapping.</summary>
    private sealed class RecordingHorizontalTransform : IHorizontalCoordinateTransform
    {
        private readonly Func<Coordinate2D, Coordinate2D> forward;
        private readonly List<Coordinate2D> forwardCalls = [];

        public RecordingHorizontalTransform(Func<Coordinate2D, Coordinate2D> forward, HorizontalTransformationDefinition definition)
        {
            this.forward = forward;
            Definition = definition;
        }

        public HorizontalTransformationDefinition Definition { get; }
        public List<Coordinate2D> ForwardCalls => forwardCalls;

        public Coordinate2D Forward(Coordinate2D source)
        {
            forwardCalls.Add(source);
            return forward(source);
        }

        public Coordinate2D Inverse(Coordinate2D target) => throw new NotSupportedException("This test double is forward-only.");
    }
}
