using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ClipRegionTests
{
    [Fact]
    public void FromRegionRejectsANullRegion()
    {
        Assert.Throws<ArgumentNullException>(() => ClipRegion.FromRegion(null!, LinearDistance.Zero));
    }

    [Fact]
    public void FromRegionWrapsTheSuppliedRegionAndBuffer()
    {
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((0 0, 1 0, 1 1, 0 1, 0 0))"), ProjectedReference());

        ClipRegion region = ClipRegion.FromRegion(polygonalRegion, LinearDistance.Meters(2d));

        Assert.Same(polygonalRegion, region.Region);
        Assert.Equal(LinearDistance.Meters(2d), region.Buffer);
    }

    [Fact]
    public void CircleRejectsANullReference()
    {
        Assert.Throws<ArgumentNullException>(() => ClipRegion.Circle(new Coordinate2D(0d, 0d), LinearDistance.Meters(1d), null!));
    }

    [Fact]
    public void CircleRejectsAGeographicReference()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => ClipRegion.Circle(new Coordinate2D(0d, 0d), LinearDistance.Meters(1d), GeographicReference()));
        Assert.Contains("projected", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CircleRejectsAZeroRadius()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => ClipRegion.Circle(new Coordinate2D(0d, 0d), LinearDistance.Zero, ProjectedReference()));
        Assert.Equal("radius", exception.ParamName);
    }

    [Fact]
    public void CircleBuildsAZeroBufferRegionCenteredAtTheRequestedPoint()
    {
        ClipRegion region = ClipRegion.Circle(new Coordinate2D(10d, 20d), LinearDistance.Meters(5d), ProjectedReference());

        Assert.Equal(LinearDistance.Zero, region.Buffer);
        Assert.True(region.Region.Envelope.MinX < 10d && region.Region.Envelope.MaxX > 10d);
        Assert.True(region.Region.Envelope.MinY < 20d && region.Region.Envelope.MaxY > 20d);
        // An 8-segments-per-quadrant approximation is always slightly inside the ideal circle.
        Assert.True(region.Region.Area < Math.PI * 5d * 5d);
        Assert.True(region.Region.Area > Math.PI * 5d * 5d * 0.9d);
    }

    private static Geometry ReadWkt(string wkt)
    {
        NtsGeometryServices services = new(new PrecisionModel(PrecisionModels.Floating), 0);
        WKTReader reader = new(services);
        return reader.Read(wkt);
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
}
