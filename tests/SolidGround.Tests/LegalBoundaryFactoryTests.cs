using NetTopologySuite.Geometries;
using SolidGround.Core.Aois;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class LegalBoundaryFactoryTests
{
    [Fact]
    public void FromPolygonalRegionRemovesOnlyAnExactlyCollinearLegalVertex()
    {
        PolygonalRegion region = Region(
            (0d, 0d), (5d, 0d), (10d, 0d), (10d, 10d), (0d, 10d));

        LocalBoundary boundary = LegalBoundaryFactory.FromPolygonalRegion(region, Frame());

        Assert.Equal(
            [new LocalCoordinate2D(0d, 0d), new LocalCoordinate2D(10d, 0d), new LocalCoordinate2D(10d, 10d), new LocalCoordinate2D(0d, 10d)],
            Assert.Single(boundary.Polygons).Shell.Vertices);
    }

    [Fact]
    public void FromPolygonalRegionRejectsAShortLegalEdgeInsteadOfMovingIt()
    {
        PolygonalRegion region = Region(
            (0d, 0d), (0.01d, 0.01d), (10d, 0d), (10d, 10d), (0d, 10d));

        ParcelExtentPlanningException error = Assert.Throws<ParcelExtentPlanningException>(
            () => LegalBoundaryFactory.FromPolygonalRegion(region, Frame(), minimumEdgeLength: 0.1d));

        Assert.Contains("will not move or snap", error.Message, StringComparison.Ordinal);
    }

    private static PolygonalRegion Region(params (double X, double Y)[] vertices)
    {
        Coordinate[] coordinates = new Coordinate[vertices.Length + 1];
        for (int index = 0; index < vertices.Length; index++)
        {
            coordinates[index] = new Coordinate(vertices[index].X, vertices[index].Y);
        }

        coordinates[^1] = coordinates[0];
        return PolygonalRegion.FromGeometry(new GeometryFactory().CreatePolygon(coordinates), Reference());
    }

    private static LocalCoordinateFrame Frame() => new(
        new Coordinate3D(0d, 0d, 0d), Reference(), new VerticalReference("NAVD88", LengthUnit.Meter), LengthUnit.Meter);

    private static HorizontalReference Reference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
}
