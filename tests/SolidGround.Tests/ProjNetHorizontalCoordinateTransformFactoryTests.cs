using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ProjNetHorizontalCoordinateTransformFactoryTests
{
    [Fact]
    public void Wgs84WellKnownTextParsesToTheCanonicalEpsg4326GeographicReference()
    {
        HorizontalReference reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;

        Assert.Equal("EPSG:4326", reference.CoordinateReferenceSystem);
        Assert.Equal(HorizontalReferenceKind.Geographic, reference.Kind);
        Assert.Equal(HorizontalAxisOrder.LongitudeLatitude, reference.AxisOrder);
    }

    [Fact]
    public void Wgs84WellKnownTextHasNoCarriageReturnAndIsMultiLine()
    {
        // Regression guard for the source-checkout-dependent line-ending bug: this value must never bake in
        // '\r' regardless of whether the source file itself was checked out with CRLF or LF line endings, and
        // it must remain multi-line by design (it is a formatted, indented WKT1 block).
        Assert.DoesNotContain('\r', ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText);
        Assert.Contains('\n', ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText);
    }

    [Fact]
    public void EngineVersionIsThePinnedPackageVersionNotTheReflectedAssemblyVersion()
    {
        Assert.Equal("2.1.0", ProjNetHorizontalCoordinateTransformFactory.EngineVersion);

        // Regression guard against ever "simplifying" the constant into a reflection call: the ProjNET 2.1.0
        // assembly itself reflects as version "2.0.0.0" (verified empirically), which would misleadingly
        // disagree with the pinned NuGet package version if EngineVersion were ever derived from it.
        Assert.NotEqual(
            ProjNetHorizontalCoordinateTransformFactory.EngineVersion,
            typeof(ProjNet.CoordinateSystems.CoordinateSystemFactory).Assembly.GetName().Version!.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsNullOrBlankSourceWellKnownText(string? sourceWellKnownText)
    {
        // Null specifically throws ArgumentNullException, a subtype of ArgumentException; ThrowsAny accepts
        // either so this verifies the documented contract without over-specifying which exact subtype backs
        // the null case (mirrors WellKnownTextReferenceParserTests.RejectsNullOrBlankInput).
        Assert.ThrowsAny<ArgumentException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create(sourceWellKnownText!, ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateRejectsNullOrBlankTargetWellKnownText(string? targetWellKnownText)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, targetWellKnownText!));
    }

    [Fact]
    public void CreateWrapsStructurallyInvalidWktSolidGroundItselfCannotParse()
    {
        HorizontalCoordinateTransformException error = Assert.Throws<HorizontalCoordinateTransformException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create("""FOO["bar"]""", ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText));

        Assert.IsType<FormatException>(error.InnerException);
        Assert.Contains("source", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateWrapsAWkt2DefinitionThatSolidGroundAcceptsButProjNetRejects()
    {
        const string wkt2Geographic = """
            GEOGCRS["WGS 84",
                DATUM["World Geodetic System 1984",
                    ELLIPSOID["WGS 84",6378137,298.257223563]],
                CS[ellipsoidal,2],
                    AXIS["geodetic latitude (Lat)",north],
                    AXIS["geodetic longitude (Lon)",east],
                    ANGLEUNIT["degree",0.0174532925199433],
                ID["EPSG",4326]]
            """;

        // SolidGround's own generic parser accepts this WKT2 text; ProjNET 2.1.0 is WKT1-only and rejects it
        // structurally with an ArgumentException that Create must never let escape unwrapped (it would be
        // indistinguishable from Create's own null/blank argument validation, which also throws ArgumentException).
        HorizontalReference parsed = WellKnownTextReferenceParser.Parse(wkt2Geographic).Horizontal;
        Assert.Equal("EPSG:4326", parsed.CoordinateReferenceSystem);
        Assert.Equal("World Geodetic System 1984", parsed.Datum);

        HorizontalCoordinateTransformException error = Assert.Throws<HorizontalCoordinateTransformException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create(wkt2Geographic, ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText));

        Assert.Contains("EPSG:4326", error.Message, StringComparison.Ordinal);
        Assert.IsType<ArgumentException>(error.InnerException);
        Assert.Contains("not recognized", error.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateWrapsAnUnsupportedProjectionMethod()
    {
        const string badProjectionWkt = """
            PROJCS["Bad Projection Test",
                GEOGCS["Test Geographic",
                    DATUM["Test Datum",
                        SPHEROID["Test Spheroid",6378137,298.257223563]],
                    PRIMEM["Greenwich",0],
                    UNIT["degree",0.0174532925199433]],
                PROJECTION["Not_A_Real_Projection"],
                PARAMETER["False_Easting",500000.0],
                PARAMETER["False_Northing",0.0],
                PARAMETER["Central_Meridian",-93.0],
                PARAMETER["Scale_Factor",0.9996],
                PARAMETER["Latitude_Of_Origin",0.0],
                UNIT["Meter",1.0]]
            """;

        HorizontalReference parsed = WellKnownTextReferenceParser.Parse(badProjectionWkt).Horizontal;
        Assert.Equal("Bad Projection Test", parsed.CoordinateReferenceSystem);
        Assert.Equal("Test Datum", parsed.Datum);

        HorizontalCoordinateTransformException error = Assert.Throws<HorizontalCoordinateTransformException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, badProjectionWkt));

        Assert.Contains("Bad Projection Test", error.Message, StringComparison.Ordinal);
        Assert.IsType<NotSupportedException>(error.InnerException);
        Assert.Contains("Not_A_Real_Projection", error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateRejectsAGeographicToGeographicPairExplicitly()
    {
        // An independent spike against the pinned ProjNET 2.1.0 package (2026-09-19) found that
        // CreateFromCoordinateSystems does NOT itself throw for a geographic-to-geographic pair: it silently
        // builds a GeographicTransform with no true datum shift, which would be a misleading "transform" under
        // SolidGround's own Forward=lon/lat->easting/northing convention. SolidGround rejects this pairing
        // itself, with an actionable message, rather than depending on that ProjNET-internal behavior.
        HorizontalCoordinateTransformException error = Assert.Throws<HorizontalCoordinateTransformException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create(
                ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText,
                ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText));

        Assert.Contains("geographic", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("projected", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void CreateRejectsAProjectedSourceCoordinateReferenceSystem()
    {
        string fixtureWkt = ReadFixture("example-site-synthetic.prj");

        HorizontalCoordinateTransformException error = Assert.Throws<HorizontalCoordinateTransformException>(
            () => ProjNetHorizontalCoordinateTransformFactory.Create(fixtureWkt, ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText));

        Assert.Contains("EPSG:26915", error.Message, StringComparison.Ordinal);
        Assert.Contains("geographic", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void CreateBuildsATransformWithTheCorrectDefinitionForWgs84ToTheExampleSiteFixture()
    {
        string fixtureWkt = ReadFixture("example-site-synthetic.prj");

        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, fixtureWkt);

        Assert.Equal("EPSG:4326", transform.Definition.SourceReference.CoordinateReferenceSystem);
        Assert.Equal("EPSG:26915", transform.Definition.TargetReference.CoordinateReferenceSystem);
        Assert.Equal(ProjNetHorizontalCoordinateTransformFactory.EngineName, transform.Definition.EngineName);
        Assert.Equal(ProjNetHorizontalCoordinateTransformFactory.EngineVersion, transform.Definition.EngineVersion);
        Assert.Equal("WKT1", transform.Definition.ForwardOperation.Format);
        Assert.Equal(fixtureWkt, transform.Definition.ForwardOperation.Definition);
        Assert.Equal(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, transform.Definition.InverseOperation.Definition);
    }

    [Fact]
    public void ForwardTransformsTheExampleSiteCentroidToItsMeasuredUtmCoordinate()
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();

        Coordinate2D result = transform.Forward(new Coordinate2D(-93.603806, 41.591194));

        // ProjNET's own golden value, measured against the pinned 2.1.0 package and the committed fixture
        // (2026-09-25, when the fixture was relocated to the example-site scenario); independently reproduced
        // three times to within a few mm. See ForwardAgreesWithAnIndependentProjEngineReferenceValue below for
        // a cross-check against an independent implementation.
        Assert.Equal(449675.327630d, result.X, 6);
        Assert.Equal(4604564.615466d, result.Y, 6);
    }

    [Fact]
    public void ForwardAgreesWithAnIndependentProjEngineReferenceValue()
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();

        Coordinate2D result = transform.Forward(new Coordinate2D(-93.603806, 41.591194));

        // Independent-engine agreement check, not a second ProjNET golden value: reference computed on
        // 2026-09-25 with a from-scratch, hand-coded implementation of the classic Snyder/Krueger forward
        // transverse-Mercator series (GRS80 ellipsoid, UTM zone 15N central meridian -93 degrees; written from
        // the published formula, not derived from or copied out of ProjNET's own source) for this same
        // lon/lat. Sanity-checked against this repository's own already-independently-verified previous
        // fixture point, where it agreed with that point's PROJ/pyproj reference value to within ~0.12 mm.
        // Because this is a totally independent implementation from ProjNET, this catches an axis swap or a
        // gross coordinate-system parameter misread that a same-engine golden value could never catch on its
        // own.
        const double kruegerEasting = 449675.3275973967d;
        const double kruegerNorthing = 4604564.6109847911d;
        double dx = result.X - kruegerEasting;
        double dy = result.Y - kruegerNorthing;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));

        Assert.True(distance <= 0.01d, $"Distance from the independent Krueger-series reference value was {distance} m, exceeding 0.01 m.");
    }

    [Fact]
    public void InverseRecoversTheGeographicCoordinateOfAUtmPoint()
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();

        Coordinate2D result = transform.Inverse(new Coordinate2D(449655.327630d, 4604544.615466d));

        Assert.Equal(-93.604044272d, result.X, 6);
        Assert.Equal(41.591012685d, result.Y, 6);
    }

    [Theory]
    [InlineData(449655.327630d, 4604544.615466d)]
    [InlineData(449695.327630d, 4604544.615466d)]
    [InlineData(449695.327630d, 4604584.615466d)]
    [InlineData(449655.327630d, 4604584.615466d)]
    [InlineData(449674d, 4604563d)]
    [InlineData(449677d, 4604566d)]
    public void InverseThenForwardRoundTripsWithinTheDocumentedProjectedTolerance(double x, double y)
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();
        Coordinate2D original = new(x, y);

        Coordinate2D roundTripped = transform.Forward(transform.Inverse(original));

        double dx = roundTripped.X - original.X;
        double dy = roundTripped.Y - original.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));

        // Measured worst case among these points: ~8.633e-3 m; see
        // docs/architecture/coordinate-transformation-and-units.md for the full measurement table.
        double toleranceMeters = ProjNetHorizontalCoordinateTransformFactory.ProjectedRoundTripTolerance.ToMeters();
        Assert.True(
            distance <= toleranceMeters,
            $"Round trip distance {distance} m for ({x}, {y}) exceeded the documented tolerance of {toleranceMeters} m.");
    }

    [Theory]
    [InlineData(-93.603806d, 41.591194d)]
    [InlineData(-93.604044272d, 41.591012685d)]
    [InlineData(-93.603567727d, 41.591375478d)]
    public void ForwardThenInverseRoundTripsWithinTheDocumentedGeographicTolerance(double longitude, double latitude)
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();
        Coordinate2D original = new(longitude, latitude);

        Coordinate2D roundTripped = transform.Inverse(transform.Forward(original));

        double dLon = roundTripped.X - original.X;
        double dLat = roundTripped.Y - original.Y;
        double distance = Math.Sqrt((dLon * dLon) + (dLat * dLat));

        // Measured worst case among these points: ~7.776e-8 deg; see
        // docs/architecture/coordinate-transformation-and-units.md for the full measurement table.
        Assert.True(
            distance <= ProjNetHorizontalCoordinateTransformFactory.GeographicRoundTripToleranceDegrees,
            $"Round trip distance {distance} deg for ({longitude}, {latitude}) exceeded the documented tolerance.");
    }

    [Fact]
    public void ForwardIsBitExactAcrossRepeatedCalls()
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();
        Coordinate2D input = new(-93.603806, 41.591194);

        Coordinate2D first = transform.Forward(input);
        Coordinate2D second = transform.Forward(input);

        Assert.Equal(first.X, second.X);
        Assert.Equal(first.Y, second.Y);
    }

    [Fact]
    public void CompleteChainRoundTripsFromUtmThroughALocalFrameAndTransformAndBackToUtmWithinTolerance()
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();
        LocalCoordinateFrame frame = new(
            new Coordinate3D(449674d, 4604563d, 183d),
            transform.Definition.TargetReference,
            new VerticalReference("NAVD88", LengthUnit.Meter),
            LengthUnit.UsSurveyFoot);
        Coordinate3D utm = new(449655.327630d, 4604544.615466d, 190d);

        LocalCoordinate local = frame.ToLocal(utm);
        Coordinate3D backToSource = frame.ToSource(local);
        Coordinate2D geographic = transform.Inverse(new Coordinate2D(backToSource.X, backToSource.Y));
        Coordinate2D roundTrippedUtm = transform.Forward(geographic);

        double dx = roundTrippedUtm.X - utm.X;
        double dy = roundTrippedUtm.Y - utm.Y;
        double distance = Math.Sqrt((dx * dx) + (dy * dy));
        double toleranceMeters = ProjNetHorizontalCoordinateTransformFactory.ProjectedRoundTripTolerance.ToMeters();

        Assert.True(
            distance <= toleranceMeters,
            $"Composed round trip distance {distance} m exceeded the documented tolerance of {toleranceMeters} m.");
    }

    [Fact]
    public void CreatesATransformAndLocalFrameThatTogetherSatisfyTerrainProvenancesInvariants()
    {
        IHorizontalCoordinateTransform transform = CreateFixtureTransform();
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        LocalCoordinateFrame frame = new(new Coordinate3D(449674d, 4604563d, 183d), transform.Definition.TargetReference, vertical, LengthUnit.UsSurveyFoot);

        TerrainProvenance provenance = new(
            1,
            new ElevationSourceMetadata("OpenTopography", "USGS1m"),
            transform.Definition,
            vertical,
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            frame,
            new SimplificationRequest(),
            0,
            0,
            null);

        Assert.Equal("EPSG:4326", provenance.SourceHorizontalReference.CoordinateReferenceSystem);
    }

    private static IHorizontalCoordinateTransform CreateFixtureTransform() =>
        ProjNetHorizontalCoordinateTransformFactory.Create(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, ReadFixture("example-site-synthetic.prj"));

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));
}
