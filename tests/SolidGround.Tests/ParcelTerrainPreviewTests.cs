using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ParcelTerrainPreviewTests
{
    private static readonly HorizontalReference Wgs84Reference =
        WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;

    [Fact]
    public void BuildKeepsLegalGeometryInvariantAcrossZeroAndThreeMeterMarginsAndShowsTheTerrainExtension()
    {
        PolygonalRegion legal = ParseSyntheticDonut();

        ParcelTerrainPreview zeroMargin = ParcelTerrainPreview.Build(legal, LinearDistance.Zero);
        ParcelTerrainPreview threeMeterMargin = ParcelTerrainPreview.Build(legal, LinearDistance.Meters(3d));

        Assert.True(zeroMargin.LegalRegion.Geometry.EqualsExact(threeMeterMargin.LegalRegion.Geometry));
        Assert.True(zeroMargin.LegalRegion.Geometry.EqualsExact(zeroMargin.TerrainRegion.Geometry));
        Assert.Equal(1, threeMeterMargin.LegalPolygonCount);
        Assert.Equal(1, threeMeterMargin.LegalHoleCount);
        Assert.Equal(1, threeMeterMargin.TerrainPolygonCount);
        Assert.Equal(0, threeMeterMargin.TerrainHoleCount);
        Assert.Equal(1, threeMeterMargin.TopologyChangeCount);
        Assert.True(threeMeterMargin.TerrainRegion.Envelope.MinX < threeMeterMargin.LegalRegion.Envelope.MinX);
        Assert.True(threeMeterMargin.TerrainRegion.Envelope.MinY < threeMeterMargin.LegalRegion.Envelope.MinY);
        Assert.True(threeMeterMargin.TerrainRegion.Envelope.MaxX > threeMeterMargin.LegalRegion.Envelope.MaxX);
        Assert.True(threeMeterMargin.TerrainRegion.Envelope.MaxY > threeMeterMargin.LegalRegion.Envelope.MaxY);

        Coordinate2D firstLegalVertex = legal.Polygons[0].Shell[0];
        Coordinate2D firstDisplayVertex = threeMeterMargin.ToDisplay(firstLegalVertex);
        Coordinate2D previewVertex = threeMeterMargin.LegalRegion.Polygons[0].Shell[0];
        Assert.Equal(previewVertex.X, firstDisplayVertex.X, 10);
        Assert.Equal(previewVertex.Y, firstDisplayVertex.Y, 10);
    }

    private static PolygonalRegion ParseSyntheticDonut()
    {
        double metersPerLongitudeDegree = Wgs84Ellipsoid.MetersPerDegreeLongitude(0d);
        double metersPerLatitudeDegree = Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);
        string Point(double xMeters, double yMeters) => string.Create(
            CultureInfo.InvariantCulture,
            $"{xMeters / metersPerLongitudeDegree:R} {yMeters / metersPerLatitudeDegree:R}");

        string geometry = $"POLYGON (({Point(-10d, -10d)}, {Point(10d, -10d)}, {Point(10d, 10d)}, {Point(-10d, 10d)}, {Point(-10d, -10d)}), " +
            $"({Point(-2d, -2d)}, {Point(-2d, 2d)}, {Point(2d, 2d)}, {Point(2d, -2d)}, {Point(-2d, -2d)}))";
        return ParcelGeometryParser.Parse(new ParcelGeometryAoi(ParcelGeometryFormat.Wkt, geometry, Wgs84Reference));
    }
}
