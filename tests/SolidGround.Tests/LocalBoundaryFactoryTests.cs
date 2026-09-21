using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="LocalBoundaryFactory"/>: projecting a <see cref="PolygonalRegion"/> or an
/// <see cref="ElevationGrid"/>'s corner envelope into a <see cref="LocalBoundary"/>, and the tolerance-aware
/// <see cref="LocalBoundaryFactory.ContainsWithTolerance"/> query.
/// </summary>
public sealed class LocalBoundaryFactoryTests
{
    [Fact]
    public void FromPolygonalRegionProducesOneLocalBoundaryPolygonPerSourcePolygon()
    {
        PolygonalRegion region = RegionFromWkt(
            "MULTIPOLYGON (((0 0, 4 0, 4 4, 0 4, 0 0)), ((100 100, 104 100, 104 104, 100 104, 100 100)))");

        LocalBoundary boundary = LocalBoundaryFactory.FromPolygonalRegion(region, IdentityFrame());

        Assert.Equal(2, boundary.Polygons.Count);
        Assert.All(boundary.Polygons, p => Assert.Empty(p.Holes));
    }

    [Fact]
    public void FromPolygonalRegionAppliesTheLocalFrameToEveryVertex()
    {
        PolygonalRegion region = RegionFromWkt("POLYGON ((10 20, 14 20, 14 24, 10 24, 10 20))");
        LocalCoordinateFrame frame = new(new Coordinate3D(10d, 20d, 0d), ProjectedReference(), VerticalReference(), LengthUnit.Meter);

        LocalBoundary boundary = LocalBoundaryFactory.FromPolygonalRegion(region, frame);

        LocalBoundaryPolygon polygon = Assert.Single(boundary.Polygons);
        // The WKT's own ring repeats (10, 20) as its trailing closing coordinate; FromPolygonalRegion strips
        // it, so exactly 4 vertices (not 5) survive, each shifted by the frame's origin.
        Assert.Equal(4, polygon.Shell.Vertices.Count);
        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(4, 0), new LocalCoordinate2D(4, 4), new LocalCoordinate2D(0, 4)],
            polygon.Shell.Vertices);
    }

    [Fact]
    public void FromPolygonalRegionThrowsOnHorizontalReferenceMismatch()
    {
        PolygonalRegion region = RegionFromWkt("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))", ProjectedReference("EPSG:26915"));
        LocalCoordinateFrame frame = new(new Coordinate3D(0d, 0d, 0d), ProjectedReference("EPSG:26916"), VerticalReference(), LengthUnit.Meter);

        Assert.Throws<ArgumentException>(() => LocalBoundaryFactory.FromPolygonalRegion(region, frame));
    }

    [Fact]
    public void FromGridEnvelopeProducesAClosedFourCornerRectangle()
    {
        ElevationGrid grid = BuildGrid(new Coordinate2D(0d, 0d), cellSize: 2d, rowCount: 3, columnCount: 5);
        LocalCoordinateFrame frame = new(new Coordinate3D(0d, 0d, 0d), ProjectedReference(), VerticalReference(), LengthUnit.Meter);

        LocalBoundary boundary = LocalBoundaryFactory.FromGridEnvelope(grid, frame);

        LocalBoundaryPolygon polygon = Assert.Single(boundary.Polygons);
        Assert.Empty(polygon.Holes);
        // Exactly one vertex per corner -- no repeated closing vertex -- and the ring is nonetheless a
        // well-formed closed loop once LocalBoundaryValidator interprets it cyclically.
        Assert.Equal(4, polygon.Shell.Vertices.Count);
        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 6), new LocalCoordinate2D(0, 6)],
            polygon.Shell.Vertices);
        Assert.True(LocalBoundaryValidator.Validate(boundary, [], pointBudget: 1).IsValid);
    }

    [Fact]
    public void ContainsWithToleranceAcceptsAPointOnTheBoundary()
    {
        LocalBoundary boundary = SquareBoundary();

        bool contained = boundary.ContainsWithTolerance(new LocalCoordinate2D(0d, 5d), tolerance: 0.001);

        Assert.True(contained);
    }

    [Fact]
    public void ContainsWithToleranceRejectsAPointOutsideByMoreThanTolerance()
    {
        LocalBoundary boundary = SquareBoundary();

        bool contained = boundary.ContainsWithTolerance(new LocalCoordinate2D(-1d, 5d), tolerance: 0.5);

        Assert.False(contained);
    }

    [Fact]
    public void ContainsWithToleranceExcludesPointsInsideAHole()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0, 10));
        LocalBoundaryRing hole = Ring((4, 4), (6, 4), (6, 6), (4, 6));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [hole])]);

        // The hole's own center: well inside it, far (2 units) from the hole's own edge -- outside any
        // reasonable containment tolerance.
        bool contained = boundary.ContainsWithTolerance(new LocalCoordinate2D(5d, 5d), tolerance: 0.01);

        Assert.False(contained);
    }

    private static LocalBoundary SquareBoundary()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundaryRing Ring(params (double X, double Y)[] vertices) =>
        new([.. vertices.Select(v => new LocalCoordinate2D(v.X, v.Y))]);

    private static ElevationGrid BuildGrid(Coordinate2D anchor, double cellSize, int rowCount, int columnCount)
    {
        double?[,] values = new double?[rowCount, columnCount];
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                values[row, column] = 1d;
            }
        }

        return new ElevationGrid(
            ProjectedReference(), VerticalReference(), anchor, cellSize, cellSize,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, values);
    }

    private static LocalCoordinateFrame IdentityFrame() =>
        new(new Coordinate3D(0d, 0d, 0d), ProjectedReference(), VerticalReference(), LengthUnit.Meter);

    private static PolygonalRegion RegionFromWkt(string wkt, HorizontalReference? reference = null)
    {
        NtsGeometry geometry = ReadWkt(wkt);
        return PolygonalRegion.FromGeometry(geometry, reference ?? ProjectedReference());
    }

    private static NtsGeometry ReadWkt(string wkt)
    {
        NtsGeometryServices services = new(new PrecisionModel(PrecisionModels.Floating), 0);
        WKTReader reader = new(services);
        return reader.Read(wkt);
    }

    private static HorizontalReference ProjectedReference(string crs = "EPSG:26915") => new(
        crs, "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
