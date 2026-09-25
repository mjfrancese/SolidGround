using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class PolygonalRegionReprojectionTests
{
    [Fact]
    public void ReprojectRejectsANullRegionOrTransform()
    {
        PolygonalRegion region = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 0))"), SourceReference());
        RecordingBidirectionalTransform transform = CreateTranslationTransform();

        Assert.Throws<ArgumentNullException>(() => PolygonalRegionReprojection.Reproject(null!, transform, HorizontalTransformDirection.Forward));
        Assert.Throws<ArgumentNullException>(() => PolygonalRegionReprojection.Reproject(region, null!, HorizontalTransformDirection.Forward));
    }

    [Fact]
    public void ReprojectRejectsAnUndefinedDirection()
    {
        PolygonalRegion region = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 0))"), SourceReference());
        RecordingBidirectionalTransform transform = CreateTranslationTransform();

        Assert.Throws<ArgumentOutOfRangeException>(() => PolygonalRegionReprojection.Reproject(region, transform, (HorizontalTransformDirection)99));
    }

    [Fact]
    public void ReprojectForwardAppliesTheTransformToEveryShellAndHoleVertexAndAdoptsTheTargetReference()
    {
        Geometry squareWithHole = ReadWkt("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 3, 3 3, 3 2, 2 2))");
        PolygonalRegion region = PolygonalRegion.FromGeometry(squareWithHole, SourceReference());
        RecordingBidirectionalTransform transform = CreateTranslationTransform();

        PolygonalRegion result = PolygonalRegionReprojection.Reproject(region, transform, HorizontalTransformDirection.Forward);

        Assert.Equal(transform.Definition.TargetReference, result.HorizontalReference);
        PolygonRings originalRings = region.Polygons.Single();
        PolygonRings resultRings = result.Polygons.Single();
        Assert.Equal(originalRings.Shell.Count, resultRings.Shell.Count);
        Assert.Equal(originalRings.Holes.Single().Count, resultRings.Holes.Single().Count);
        foreach (Coordinate2D input in originalRings.Shell)
        {
            Assert.Contains(new Coordinate2D(input.X + 10000d, input.Y + 20000d), resultRings.Shell);
        }

        foreach (Coordinate2D input in originalRings.Holes.Single())
        {
            Assert.Contains(new Coordinate2D(input.X + 10000d, input.Y + 20000d), resultRings.Holes.Single());
        }

        // 5 shell vertices (closed ring) + 5 hole vertices (closed ring) = 10 Forward calls, no Inverse calls.
        Assert.Equal(10, transform.ForwardCalls.Count);
        Assert.Empty(transform.InverseCalls);
    }

    [Fact]
    public void ReprojectInverseAppliesTheTransformsInverseMethodAndAdoptsTheSourceReference()
    {
        Geometry square = ReadWkt("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
        PolygonalRegion region = PolygonalRegion.FromGeometry(square, TargetReference());
        RecordingBidirectionalTransform transform = CreateTranslationTransform();

        PolygonalRegion result = PolygonalRegionReprojection.Reproject(region, transform, HorizontalTransformDirection.Inverse);

        Assert.Equal(transform.Definition.SourceReference, result.HorizontalReference);
        PolygonRings resultRings = result.Polygons.Single();
        foreach (Coordinate2D input in region.Polygons.Single().Shell)
        {
            Assert.Contains(new Coordinate2D(input.X - 10000d, input.Y - 20000d), resultRings.Shell);
        }

        Assert.Empty(transform.ForwardCalls);
        Assert.Equal(5, transform.InverseCalls.Count);
    }

    [Fact]
    public void ReprojectRejectsATransformWhoseExpectedFromReferenceDoesNotMatchTheRegion()
    {
        HorizontalReference mismatched = MismatchedReference();
        PolygonalRegion region = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 0))"), mismatched);
        RecordingBidirectionalTransform transform = CreateTranslationTransform();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => PolygonalRegionReprojection.Reproject(region, transform, HorizontalTransformDirection.Forward));

        Assert.Contains(mismatched.CoordinateReferenceSystem, error.Message, StringComparison.Ordinal);
        Assert.Contains(transform.Definition.SourceReference.CoordinateReferenceSystem, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReprojectDoesNotMutateTheOriginalRegionsGeometryOrEnvelope()
    {
        Geometry square = ReadWkt("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0))");
        PolygonalRegion region = PolygonalRegion.FromGeometry(square, SourceReference());
        RecordingBidirectionalTransform transform = CreateTranslationTransform();

        // Snapshots of ordinate VALUES, not the live Coordinate object references (NTS's Coordinate is a
        // mutable reference type, exactly what ICoordinateFilter.Filter mutates in place) -- an array-of-
        // references snapshot would still show a later in-place mutation, silently defeating this aliasing
        // regression guard.
        (double X, double Y)[] before = [.. region.Geometry.Coordinates.Select(c => (c.X, c.Y))];
        PlanarEnvelope originalEnvelope = region.Envelope;

        PolygonalRegionReprojection.Reproject(region, transform, HorizontalTransformDirection.Forward);

        (double X, double Y)[] after = [.. region.Geometry.Coordinates.Select(c => (c.X, c.Y))];
        Assert.Equal(before, after);
        Assert.Equal(originalEnvelope, region.Envelope);
    }

    [Fact]
    public void ReprojectsTheRealExampleSiteGeojsonParcelForwardThenInverseRoundTripsToItsOriginalWgs84Vertices()
    {
        string geoJsonText = ReadFixture("example-site-synthetic-parcel.geojson");
        HorizontalReference wgs84 = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
        ParcelGeometryAoi aoi = new(ParcelGeometryFormat.GeoJson, geoJsonText, wgs84);
        PolygonalRegion parcel = ParcelGeometryParser.Parse(aoi);
        string fixtureWkt = ReadFixture("example-site-synthetic.prj");
        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, fixtureWkt);

        PolygonalRegion projected = PolygonalRegionReprojection.Reproject(parcel, transform, HorizontalTransformDirection.Forward);
        PolygonalRegion roundTripped = PolygonalRegionReprojection.Reproject(projected, transform, HorizontalTransformDirection.Inverse);

        Assert.Equal(HorizontalReferenceKind.Projected, projected.HorizontalReference.Kind);
        Assert.Equal(wgs84, roundTripped.HorizontalReference);

        // Self-consistent Forward-then-Inverse round trip only, bounded by the documented geographic
        // reversibility tolerance (see docs/architecture/coordinate-transformation-and-units.md); this never
        // compares against the separately-loaded example-site-synthetic-parcel.wkt fixture at all (see
        // Fixtures/README.md for how that fixture relates to this one).
        IReadOnlyList<Coordinate2D> originalShell = parcel.Polygons.Single().Shell;
        IReadOnlyList<Coordinate2D> roundTrippedShell = roundTripped.Polygons.Single().Shell;
        Assert.Equal(originalShell.Count, roundTrippedShell.Count);
        for (int i = 0; i < originalShell.Count; i++)
        {
            double dLon = roundTrippedShell[i].X - originalShell[i].X;
            double dLat = roundTrippedShell[i].Y - originalShell[i].Y;
            double distance = Math.Sqrt((dLon * dLon) + (dLat * dLat));
            Assert.True(
                distance <= ProjNetHorizontalCoordinateTransformFactory.GeographicRoundTripToleranceDegrees,
                $"Shell vertex {i} round-tripped {distance} deg away from its original position, exceeding the documented geographic tolerance.");
        }
    }

    private static RecordingBidirectionalTransform CreateTranslationTransform() => new(
        forward: c => new Coordinate2D(c.X + 10000d, c.Y + 20000d),
        inverse: c => new Coordinate2D(c.X - 10000d, c.Y - 20000d),
        definition: TestTransformationDefinition());

    private static HorizontalTransformationDefinition TestTransformationDefinition() => new(
        SourceReference(),
        TargetReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "test-double-engine",
        "1.0");

    private static Geometry ReadWkt(string wkt)
    {
        NtsGeometryServices services = new(new PrecisionModel(PrecisionModels.Floating), 0);
        WKTReader reader = new(services);
        return reader.Read(wkt);
    }

    private static string ReadFixture(string fileName) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName));

    private static HorizontalReference SourceReference() => new(
        "TEST:SOURCE", "Test Source Datum", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static HorizontalReference TargetReference() => new(
        "TEST:TARGET", "Test Target Datum", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static HorizontalReference MismatchedReference() => new(
        "TEST:MISMATCHED", "A Differently Spelled Datum", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    /// <summary>
    /// A bidirectional test double modeled on <c>AoiNormalizerTests.RecordingHorizontalTransform</c>, but
    /// supporting both directions with two independent recording lists.
    /// </summary>
    private sealed class RecordingBidirectionalTransform : IHorizontalCoordinateTransform
    {
        private readonly Func<Coordinate2D, Coordinate2D> forward;
        private readonly Func<Coordinate2D, Coordinate2D> inverse;
        private readonly List<Coordinate2D> forwardCalls = [];
        private readonly List<Coordinate2D> inverseCalls = [];

        public RecordingBidirectionalTransform(Func<Coordinate2D, Coordinate2D> forward, Func<Coordinate2D, Coordinate2D> inverse, HorizontalTransformationDefinition definition)
        {
            this.forward = forward;
            this.inverse = inverse;
            Definition = definition;
        }

        public HorizontalTransformationDefinition Definition { get; }
        public List<Coordinate2D> ForwardCalls => forwardCalls;
        public List<Coordinate2D> InverseCalls => inverseCalls;

        public Coordinate2D Forward(Coordinate2D source)
        {
            forwardCalls.Add(source);
            return forward(source);
        }

        public Coordinate2D Inverse(Coordinate2D target)
        {
            inverseCalls.Add(target);
            return inverse(target);
        }
    }
}
