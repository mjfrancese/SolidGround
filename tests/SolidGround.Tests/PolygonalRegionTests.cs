using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class PolygonalRegionTests
{
    [Fact]
    public void FromGeometryRejectsNullArguments()
    {
        Geometry square = ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 0))");

        Assert.Throws<ArgumentNullException>(() => PolygonalRegion.FromGeometry(null!, ProjectedReference()));
        Assert.Throws<ArgumentNullException>(() => PolygonalRegion.FromGeometry(square, null!));
    }

    [Fact]
    public void FromGeometryAcceptsASinglePolygonAndExposesItsShellRoundTripped()
    {
        Geometry square = ReadWkt("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))");

        PolygonalRegion region = PolygonalRegion.FromGeometry(square, ProjectedReference());

        Assert.Same(square, region.Geometry);
        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(0, region.HoleCount);
        Assert.Equal(16d, region.Area);
        PolygonRings polygon = Assert.Single(region.Polygons);
        Assert.Empty(polygon.Holes);
        Assert.Equal(
            [new Coordinate2D(0, 0), new Coordinate2D(4, 0), new Coordinate2D(4, 4), new Coordinate2D(0, 4), new Coordinate2D(0, 0)],
            polygon.Shell);
    }

    [Fact]
    public void FromGeometryExposesEnvelopeFromEnvelopeInternal()
    {
        Geometry square = ReadWkt("POLYGON ((1 2, 5 2, 5 7, 1 7, 1 2))");

        PolygonalRegion region = PolygonalRegion.FromGeometry(square, ProjectedReference());

        Assert.Equal(1d, region.Envelope.MinX);
        Assert.Equal(2d, region.Envelope.MinY);
        Assert.Equal(5d, region.Envelope.MaxX);
        Assert.Equal(7d, region.Envelope.MaxY);
    }

    [Fact]
    public void FromGeometryAcceptsAPolygonWithAHoleAndRoundTripsItsCoordinates()
    {
        Geometry withHole = ReadWkt("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 3, 3 3, 3 2, 2 2))");

        PolygonalRegion region = PolygonalRegion.FromGeometry(withHole, ProjectedReference());

        Assert.Equal(1, region.HoleCount);
        PolygonRings polygon = Assert.Single(region.Polygons);
        IReadOnlyList<Coordinate2D> hole = Assert.Single(polygon.Holes);
        Assert.Equal(
            [new Coordinate2D(2, 2), new Coordinate2D(2, 3), new Coordinate2D(3, 3), new Coordinate2D(3, 2), new Coordinate2D(2, 2)],
            hole);
    }

    [Fact]
    public void FromGeometryFlattensAMultiPolygonIntoIndependentPolygons()
    {
        Geometry multi = ReadWkt("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))");

        PolygonalRegion region = PolygonalRegion.FromGeometry(multi, ProjectedReference());

        Assert.Equal(2, region.PolygonCount);
        Assert.Equal(0, region.HoleCount);
    }

    [Fact]
    public void FromGeometryFlattensANestedGeometryCollection()
    {
        Geometry nested = ReadWkt(
            "GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 0)), " +
            "GEOMETRYCOLLECTION (MULTIPOLYGON (((5 5, 6 5, 6 6, 5 5)), ((8 8, 9 8, 9 9, 8 8)))))");

        PolygonalRegion region = PolygonalRegion.FromGeometry(nested, ProjectedReference());

        Assert.Equal(3, region.PolygonCount);
    }

    [Theory]
    [InlineData("POINT (0 0)")]
    [InlineData("LINESTRING (0 0, 1 1)")]
    public void FromGeometryRejectsNonPolygonalGeometryTypes(string wkt)
    {
        Geometry geometry = ReadWkt(wkt);

        Assert.Throws<ParcelGeometryException>(() => PolygonalRegion.FromGeometry(geometry, ProjectedReference()));
    }

    [Fact]
    public void FromGeometryRejectsAnEmptyGeometry()
    {
        Geometry empty = ReadWkt("POLYGON EMPTY");

        Assert.Throws<ParcelGeometryException>(() => PolygonalRegion.FromGeometry(empty, ProjectedReference()));
    }

    [Fact]
    public void FromGeometryRejectsAMixedCollectionContainingANonPolygonalMember()
    {
        Geometry mixed = ReadWkt("GEOMETRYCOLLECTION (POINT (0 0), POLYGON ((0 0, 1 0, 1 1, 0 0)))");

        Assert.Throws<ParcelGeometryException>(() => PolygonalRegion.FromGeometry(mixed, ProjectedReference()));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FromGeometryRejectsANonFiniteCoordinate(double nonFiniteOrdinate)
    {
        // Built directly with NTS's GeometryFactory, bypassing both parsers: WKT text has no NaN/Infinity
        // literal syntax, and GeoJSON's ParsePosition already filters non-finite numbers before a Geometry is
        // built. This exercises FromGeometry's own finite-coordinate guard directly, as Issue #6's seam for
        // an already-reprojected geometry (a degenerate or singular projection could legitimately emit one).
        GeometryFactory factory = new NtsGeometryServices(new PrecisionModel(PrecisionModels.Floating), 0).CreateGeometryFactory();
        LinearRing shell = factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d),
            new Coordinate(1d, 0d),
            new Coordinate(1d, 1d),
            new Coordinate(nonFiniteOrdinate, 1d),
            new Coordinate(0d, 0d),
        ]);
        Polygon polygon = factory.CreatePolygon(shell);

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => PolygonalRegion.FromGeometry(polygon, ProjectedReference()));

        Assert.Contains("non-finite", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static Geometry ReadWkt(string wkt)
    {
        NtsGeometryServices services = new(new PrecisionModel(PrecisionModels.Floating), 0);
        WKTReader reader = new(services);
        return reader.Read(wkt);
    }

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915",
        "NAD83(2011)",
        HorizontalReferenceKind.Projected,
        HorizontalUnit.Linear(LengthUnit.Meter),
        HorizontalAxisOrder.EastingNorthing);
}
