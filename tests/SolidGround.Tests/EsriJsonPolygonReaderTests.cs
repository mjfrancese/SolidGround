using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Exercises <see cref="EsriJsonPolygonReader"/> directly, against inline small ring arrays -- never a raw NTS
/// <c>Polygon</c>/<c>MultiPolygon</c> comparison, only <see cref="PolygonalRegion"/>'s own package-neutral
/// members. Coordinates are small synthetic local numbers, not real-world latitude/longitude.
/// </summary>
public sealed class EsriJsonPolygonReaderTests
{
    // A clockwise (Esri exterior-ring convention) unit square: right-hand traversal up-left, across-top,
    // down-right, across-bottom yields a negative signed area.
    private static readonly (double X, double Y)[] ClockwiseShell = [(0, 0), (0, 10), (10, 10), (10, 0), (0, 0)];

    [Fact]
    public void SingleClockwiseShellProducesOnePolygonWithNoHoles()
    {
        PolygonalRegion region = EsriJsonPolygonReader.Read([ClockwiseShell], GeographicReference(), featureIndex: 0);

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(0, region.HoleCount);
    }

    [Fact]
    public void ShellPlusCounterclockwiseHoleProducesOnePolygonWithOneContainedHole()
    {
        (double X, double Y)[] hole = [(3, 3), (7, 3), (7, 7), (3, 7), (3, 3)];

        PolygonalRegion region = EsriJsonPolygonReader.Read([ClockwiseShell, hole], GeographicReference(), featureIndex: 0);

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(1, region.HoleCount);
        Assert.Single(region.Polygons.Single().Holes);
    }

    [Fact]
    public void AHoleWhoseFirstVertexCoincidesExactlyWithAShellVertexIsStillAcceptedAsContained()
    {
        // The hole's first vertex (0,0) is a shell corner; every other hole vertex is strictly interior.
        // Esri's own documentation states rings can touch at a vertex -- this must be accepted (boundary-
        // inclusive Covers), not wrongly rejected under a strict interior-only Contains test.
        (double X, double Y)[] touchingHole = [(0, 0), (6, 2), (6, 6), (2, 6), (0, 0)];

        PolygonalRegion region = EsriJsonPolygonReader.Read([ClockwiseShell, touchingHole], GeographicReference(), featureIndex: 0);

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(1, region.HoleCount);
    }

    [Fact]
    public void TwoDisjointClockwiseShellsProduceAMultipartResult()
    {
        (double X, double Y)[] secondShell = [(20, 20), (20, 30), (30, 30), (30, 20), (20, 20)];

        PolygonalRegion region = EsriJsonPolygonReader.Read([ClockwiseShell, secondShell], GeographicReference(), featureIndex: 0);

        Assert.Equal(2, region.PolygonCount);
        Assert.Equal(0, region.HoleCount);
    }

    [Fact]
    public void ALoneCounterclockwiseRingWithNoEnclosingShellThrowsWithAnActionableIndexNamingMessage()
    {
        (double X, double Y)[] counterclockwiseRing = [(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)];

        CountyParcelRegistryUnexpectedResponseException error = Assert.Throws<CountyParcelRegistryUnexpectedResponseException>(
            () => EsriJsonPolygonReader.Read([counterclockwiseRing], GeographicReference(), featureIndex: 2));

        Assert.Contains("ring 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("Feature 2", error.Message, StringComparison.Ordinal);
        Assert.Contains("no enclosing clockwise shell", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnclosedRingThrows()
    {
        (double X, double Y)[] unclosedRing = [(0, 0), (10, 0), (10, 10), (0, 10)];

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(
            () => EsriJsonPolygonReader.Read([unclosedRing], GeographicReference(), featureIndex: 0));

        Assert.Contains("not closed", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARingWithFewerThanFourPointsThrows()
    {
        (double X, double Y)[] tooShortRing = [(0, 0), (5, 5), (0, 0)];

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(
            () => EsriJsonPolygonReader.Read([tooShortRing], GeographicReference(), featureIndex: 0));

        Assert.Contains("at least 4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEmptyRingsList()
    {
        Assert.Throws<ParcelGeometryException>(() => EsriJsonPolygonReader.Read([], GeographicReference(), featureIndex: 0));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => EsriJsonPolygonReader.Read(null!, GeographicReference(), featureIndex: 0));
        Assert.Throws<ArgumentNullException>(() => EsriJsonPolygonReader.Read([ClockwiseShell], null!, featureIndex: 0));
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
