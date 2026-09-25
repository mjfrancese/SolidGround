using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class GridClipperTests
{
    [Fact]
    public void ClipRejectsNullArguments()
    {
        ElevationGrid grid = UniformGrid(3, 3, 1d);
        ClipRegion region = SquareClipRegion(0d, 0d, 3d, 3d);

        Assert.Throws<ArgumentNullException>(() => GridClipper.Clip(null!, region));
        Assert.Throws<ArgumentNullException>(() => GridClipper.Clip(grid, null!));
    }

    [Fact]
    public void InteriorCellsAreRetainedAndExteriorCellsBecomeNullWithRegionExcludedStatus()
    {
        ElevationGrid grid = UniformGrid(rowCount: 5, columnCount: 5, elevation: 100d, rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = SquareClipRegion(1d, 1d, 4d, 4d);

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });

        Assert.Equal(5, result.OutputRowCount);
        Assert.Equal(5, result.OutputColumnCount);
        for (int row = 0; row < 5; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                bool interior = row is 1 or 2 or 3 && column is 1 or 2 or 3;
                Assert.Equal(interior ? 100d : null, result.Grid.GetElevation(row, column));
                Assert.Equal(interior ? GridCellStatus.Retained : GridCellStatus.RegionExcluded, result.GetCellStatus(row, column));
            }
        }

        Assert.Equal(25, result.SourceCellCount);
        Assert.Equal(9, result.IncludedCellCount);
        Assert.Equal(9, result.RetainedElevationCount);
        Assert.Equal(0, result.NoDataCellsInsideRegion);
        Assert.Equal(16, result.ExcludedCellCount);
    }

    [Fact]
    public void ClippingWorksWithACellCenterAnchoredGridIncludingCropping()
    {
        // With CellCenter anchoring (unlike LowerLeftCorner) GetCellCenter applies no half-cell offset, so
        // anchor (0,0) puts cell (row,column) centers exactly at the integers (column,row).
        ElevationGrid grid = UniformGrid(
            rowCount: 5, columnCount: 5, elevation: 42d, rowOrder: GridRowOrder.SouthToNorth,
            anchorConvention: GridAnchorConvention.CellCenter, anchor: new Coordinate2D(0d, 0d));
        ClipRegion region = SquareClipRegion(0.5d, 0.5d, 3.5d, 3.5d); // covers integer centers 1, 2, 3 on each axis.

        GridClipResult uncropped = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });
        for (int row = 0; row < 5; row++)
        {
            for (int column = 0; column < 5; column++)
            {
                bool interior = row is 1 or 2 or 3 && column is 1 or 2 or 3;
                Assert.Equal(interior ? 42d : null, uncropped.Grid.GetElevation(row, column));
            }
        }

        Assert.Equal(9, uncropped.IncludedCellCount);
        Assert.Equal(GridAnchorConvention.CellCenter, uncropped.Grid.AnchorConvention);

        GridClipResult cropped = GridClipper.Clip(grid, region); // default CropToRegionEnvelope = true.
        Assert.Equal(3, cropped.OutputRowCount);
        Assert.Equal(3, cropped.OutputColumnCount);
        Assert.Equal(GridAnchorConvention.CellCenter, cropped.Grid.AnchorConvention);
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                Assert.Equal(42d, cropped.Grid.GetElevation(row, column));
                Coordinate2D croppedCenter = cropped.Grid.GetCellCenter(row, column);
                Coordinate2D sourceCenter = grid.GetCellCenter(row + 1, column + 1);
                Assert.Equal(sourceCenter.X, croppedCenter.X, 6);
                Assert.Equal(sourceCenter.Y, croppedCenter.Y, 6);
            }
        }
    }

    [Fact]
    public void CellCenterCoveredIncludesTheVertexCellsAndCellFootprintIntersectsIncludesAStrictlySuperset()
    {
        ElevationGrid grid = UniformGrid(rowCount: 7, columnCount: 7, elevation: 5d, rowOrder: GridRowOrder.SouthToNorth);

        // A triangle whose hypotenuse has slope -1/2 (not 0, infinite, or +-1), built entirely from
        // grid.GetCellCenter. A horizontal, vertical, or 45-degree edge through cell centers on a unit grid
        // always re-crosses another cell's own center at the next grid step, so it can never clip a
        // same-footprint-only cell; an oblique slope like this one genuinely does.
        Coordinate2D vertexA = grid.GetCellCenter(1, 1);
        Coordinate2D vertexB = grid.GetCellCenter(1, 5);
        Coordinate2D vertexC = grid.GetCellCenter(3, 1);
        ClipRegion region = TriangleClipRegion(vertexA, vertexB, vertexC);

        GridClipResult centerResult = GridClipper.Clip(
            grid, region, new GridClipOptions { Inclusion = GridCellInclusion.CellCenterCovered, CropToRegionEnvelope = false });
        GridClipResult footprintResult = GridClipper.Clip(
            grid, region, new GridClipOptions { Inclusion = GridCellInclusion.CellFootprintIntersects, CropToRegionEnvelope = false });

        // The 3 vertices sit exactly at these cells' centers, so a boundary-inclusive Covers must include them.
        Assert.Equal(GridCellStatus.Retained, centerResult.GetCellStatus(1, 1));
        Assert.Equal(GridCellStatus.Retained, centerResult.GetCellStatus(1, 5));
        Assert.Equal(GridCellStatus.Retained, centerResult.GetCellStatus(3, 1));

        HashSet<(int Row, int Column)> centerIncluded = IncludedCells(centerResult);
        HashSet<(int Row, int Column)> footprintIncluded = IncludedCells(footprintResult);

        Assert.True(centerIncluded.IsSubsetOf(footprintIncluded), "Every CellCenterCovered cell must also be CellFootprintIntersects-included.");
        Assert.True(
            footprintIncluded.Count > centerIncluded.Count,
            $"Expected CellFootprintIntersects ({footprintIncluded.Count}) to include strictly more cells than CellCenterCovered ({centerIncluded.Count}).");
    }

    [Fact]
    public void ARectangleThroughARingOfCellCentersShowsIdenticalInclusionUnderBothRules()
    {
        ElevationGrid grid = UniformGrid(rowCount: 7, columnCount: 7, elevation: 5d, rowOrder: GridRowOrder.SouthToNorth);

        // An axis-aligned rectangle, built entirely from grid.GetCellCenter like the oblique triangle above,
        // but with every edge horizontal or vertical. Each edge lands exactly on a full line of cell centers
        // (column 1, column 5, row 1, row 5): such an edge always re-crosses another cell's own center at the
        // very next grid step, so it can never clip a same-footprint-only cell without that neighbor's center
        // already being on the boundary too. Both inclusion rules must therefore agree here.
        Coordinate2D corner1 = grid.GetCellCenter(1, 1);
        Coordinate2D corner2 = grid.GetCellCenter(1, 5);
        Coordinate2D corner3 = grid.GetCellCenter(5, 5);
        Coordinate2D corner4 = grid.GetCellCenter(5, 1);
        ClipRegion region = RectangleClipRegion(corner1, corner2, corner3, corner4);

        GridClipResult centerResult = GridClipper.Clip(
            grid, region, new GridClipOptions { Inclusion = GridCellInclusion.CellCenterCovered, CropToRegionEnvelope = false });
        GridClipResult footprintResult = GridClipper.Clip(
            grid, region, new GridClipOptions { Inclusion = GridCellInclusion.CellFootprintIntersects, CropToRegionEnvelope = false });

        HashSet<(int Row, int Column)> centerIncluded = IncludedCells(centerResult);
        HashSet<(int Row, int Column)> footprintIncluded = IncludedCells(footprintResult);

        Assert.Equal(25, centerIncluded.Count);
        Assert.True(
            centerIncluded.SetEquals(footprintIncluded),
            "A rectangle whose edges pass through a ring of cell centers must produce identical inclusion under both rules.");
    }

    [Fact]
    public void HoleCellsAreExcludedWhileShellCellsAreRetained()
    {
        ElevationGrid grid = UniformGrid(rowCount: 7, columnCount: 7, elevation: 50d, rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = ClipRegionFromWkt("POLYGON ((1 1, 6 1, 6 6, 1 6, 1 1), (3 3, 3 4, 4 4, 4 3, 3 3))");

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });

        // Cell (3,3) center (3.5,3.5) is inside the hole [3,4]x[3,4].
        Assert.Equal(GridCellStatus.RegionExcluded, result.GetCellStatus(3, 3));
        Assert.Null(result.Grid.GetElevation(3, 3));
        // Cell (2,2) center (2.5,2.5) is inside the shell but outside the hole.
        Assert.Equal(GridCellStatus.Retained, result.GetCellStatus(2, 2));
        Assert.Equal(50d, result.Grid.GetElevation(2, 2));
    }

    [Fact]
    public void MultiPolygonRegionRetainsBothLotsAndExcludesTheGapBetweenThem()
    {
        ElevationGrid grid = UniformGrid(rowCount: 7, columnCount: 12, elevation: 20d, rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = ClipRegionFromWkt("MULTIPOLYGON (((1 1, 4 1, 4 6, 1 6, 1 1)), ((8 1, 11 1, 11 6, 8 6, 8 1)))");

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });

        Assert.Equal(GridCellStatus.Retained, result.GetCellStatus(3, 2));
        Assert.Equal(GridCellStatus.Retained, result.GetCellStatus(3, 9));
        Assert.Equal(GridCellStatus.RegionExcluded, result.GetCellStatus(3, 6));
    }

    [Fact]
    public void BufferInMetersAndTheEquivalentUsSurveyFeetProduceIdenticalGridsAndAddExactlyTheExpectedExtraCells()
    {
        ElevationGrid grid = UniformGrid(rowCount: 9, columnCount: 9, elevation: 10d, rowOrder: GridRowOrder.SouthToNorth);
        PolygonalRegion baseRegion = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((3 3, 6 3, 6 6, 3 6, 3 3))"), ProjectedReference());

        GridClipResult noBuffer = GridClipper.Clip(
            grid, ClipRegion.FromRegion(baseRegion, LinearDistance.Zero), new GridClipOptions { CropToRegionEnvelope = false });
        GridClipResult bufferMeters = GridClipper.Clip(
            grid, ClipRegion.FromRegion(baseRegion, LinearDistance.Meters(1d)), new GridClipOptions { CropToRegionEnvelope = false });
        GridClipResult bufferFeet = GridClipper.Clip(
            grid,
            ClipRegion.FromRegion(baseRegion, new LinearDistance(3937d / 1200d, LengthUnit.UsSurveyFoot)),
            new GridClipOptions { CropToRegionEnvelope = false });

        AssertElevationsEqual(bufferMeters.Grid.ToArray(), bufferFeet.Grid.ToArray());
        AssertStatusesEqual(bufferMeters.GetCellStatuses(), bufferFeet.GetCellStatuses());

        Assert.Equal(9, noBuffer.IncludedCellCount);
        Assert.Equal(25, bufferMeters.IncludedCellCount);
        Assert.Equal(25, bufferFeet.IncludedCellCount);
        Assert.Equal(16, bufferMeters.IncludedCellCount - noBuffer.IncludedCellCount);
    }

    [Fact]
    public void PositiveBufferOnAGeographicReferenceIsRejected()
    {
        ElevationGrid grid = UniformGrid(rowCount: 5, columnCount: 5, elevation: 1d, reference: GeographicReference());
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((1 1, 4 1, 4 4, 1 4, 1 1))"), GeographicReference());
        ClipRegion region = ClipRegion.FromRegion(polygonalRegion, LinearDistance.Meters(1d));

        GridClipException exception = Assert.Throws<GridClipException>(() => GridClipper.Clip(grid, region));
        Assert.Contains("degree", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReferenceMismatchIsRejectedWithAMessageNamingIssueSix()
    {
        ElevationGrid grid = UniformGrid(rowCount: 5, columnCount: 5, elevation: 1d);
        HorizontalReference otherReference = new(
            "EPSG:6350", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt("POLYGON ((1 1, 4 1, 4 4, 1 4, 1 1))"), otherReference);
        ClipRegion region = ClipRegion.FromRegion(polygonalRegion, LinearDistance.Zero);

        GridClipException exception = Assert.Throws<GridClipException>(() => GridClipper.Clip(grid, region));
        Assert.Contains("Issue #6", exception.Message);
    }

    [Fact]
    public void SourceNoDataCellInsideRegionStaysNullAndCountsAsIncludedButNotRetained()
    {
        ElevationGrid grid = BuildGrid(5, 5, (row, column) => row == 2 && column == 2 ? null : 5d, rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = SquareClipRegion(1d, 1d, 4d, 4d);

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });

        Assert.Null(result.Grid.GetElevation(2, 2));
        Assert.Equal(GridCellStatus.SourceNoData, result.GetCellStatus(2, 2));
        Assert.Equal(1, result.NoDataCellsInsideRegion);
        Assert.Equal(9, result.IncludedCellCount);
        Assert.Equal(8, result.RetainedElevationCount);
    }

    [Theory]
    [InlineData(GridRowOrder.SouthToNorth)]
    [InlineData(GridRowOrder.NorthToSouth)]
    public void CroppingToAnAsymmetricWindowGivesTheMinimalWindowAndPreservesCellCentersForBothRowOrders(GridRowOrder rowOrder)
    {
        ElevationGrid sourceGrid = UniformGrid(rowCount: 10, columnCount: 10, elevation: 7d, rowOrder: rowOrder);
        ClipRegion region = SquareClipRegion(2d, 1d, 5d, 8d); // x in [2,5] -> cols 2-4; y in [1,8] -> rows 1-7.

        GridClipResult uncropped = GridClipper.Clip(sourceGrid, region, new GridClipOptions { CropToRegionEnvelope = false });
        GridClipResult cropped = GridClipper.Clip(sourceGrid, region);

        int rowStart = int.MaxValue, rowEndInclusive = int.MinValue, colStart = int.MaxValue, colEndInclusive = int.MinValue;
        for (int row = 0; row < sourceGrid.RowCount; row++)
        {
            for (int column = 0; column < sourceGrid.ColumnCount; column++)
            {
                if (uncropped.GetCellStatus(row, column) == GridCellStatus.RegionExcluded)
                {
                    continue;
                }

                rowStart = Math.Min(rowStart, row);
                rowEndInclusive = Math.Max(rowEndInclusive, row);
                colStart = Math.Min(colStart, column);
                colEndInclusive = Math.Max(colEndInclusive, column);
            }
        }

        Assert.Equal(7, rowEndInclusive - rowStart + 1);
        Assert.Equal(3, colEndInclusive - colStart + 1);
        Assert.Equal(rowEndInclusive - rowStart + 1, cropped.OutputRowCount);
        Assert.Equal(colEndInclusive - colStart + 1, cropped.OutputColumnCount);

        for (int row = 0; row < cropped.OutputRowCount; row++)
        {
            for (int column = 0; column < cropped.OutputColumnCount; column++)
            {
                Coordinate2D croppedCenter = cropped.Grid.GetCellCenter(row, column);
                Coordinate2D sourceCenter = sourceGrid.GetCellCenter(rowStart + row, colStart + column);
                Assert.Equal(sourceCenter.X, croppedCenter.X, 6);
                Assert.Equal(sourceCenter.Y, croppedCenter.Y, 6);
            }
        }
    }

    [Fact]
    public void CropToRegionEnvelopeFalseKeepsTheSourceGridShapeAndAnchor()
    {
        ElevationGrid grid = UniformGrid(rowCount: 6, columnCount: 8, elevation: 3d);
        ClipRegion region = SquareClipRegion(2d, 2d, 4d, 4d);

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });

        Assert.Equal(6, result.OutputRowCount);
        Assert.Equal(8, result.OutputColumnCount);
        Assert.Equal(grid.SouthwestAnchor, result.Grid.SouthwestAnchor);
    }

    [Fact]
    public void AnEffectiveRegionCoveringNoCellThrowsGridClipException()
    {
        ElevationGrid grid = UniformGrid(rowCount: 4, columnCount: 4, elevation: 1d); // spans x,y in [0,4].
        ClipRegion region = SquareClipRegion(100d, 100d, 101d, 101d);

        GridClipException exception = Assert.Throws<GridClipException>(() => GridClipper.Clip(grid, region));
        Assert.Contains("no cell", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClippingIsDeterministicAcrossRepeatedRuns()
    {
        ElevationGrid grid = BuildGrid(6, 6, (row, column) => (row * 6 + column) % 5 == 0 ? null : (double)((row * 6) + column), rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = SquareClipRegion(1d, 1d, 5d, 5d);

        GridClipResult first = GridClipper.Clip(grid, region);
        GridClipResult second = GridClipper.Clip(grid, region);

        AssertElevationsEqual(first.Grid.ToArray(), second.Grid.ToArray());
        AssertStatusesEqual(first.GetCellStatuses(), second.GetCellStatuses());
        Assert.Equal(first.Grid.SouthwestAnchor, second.Grid.SouthwestAnchor);
    }

    [Fact]
    public void CircleRegionClipsADiskOfApproximatelyTheExpectedCellCount()
    {
        ElevationGrid grid = UniformGrid(
            rowCount: 41, columnCount: 41, elevation: 1d, anchor: new Coordinate2D(-20.5d, -20.5d), rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = ClipRegion.Circle(new Coordinate2D(0d, 0d), LinearDistance.Meters(10d), ProjectedReference());

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });

        double expectedArea = Math.PI * 10d * 10d;
        double relativeDifference = Math.Abs(result.IncludedCellCount - expectedArea) / expectedArea;
        Assert.True(
            relativeDifference <= 0.05,
            $"Expected approximately {expectedArea} cells, got {result.IncludedCellCount} (relative difference {relativeDifference}).");
    }

    [Fact]
    public void ASyntheticRectangleRetainsTheExpectedCellCountAndABufferGrowsIt()
    {
        // A 2,000 m^2 (40 m by 50 m) illustrative rectangle -- deliberately not the size of any real
        // parcel -- placed within a 60x60 1 m synthetic grid.
        const double minX = 10d;
        const double maxX = 50d;
        const double minY = 5d;
        const double maxY = 55d;
        ElevationGrid grid = UniformGrid(rowCount: 60, columnCount: 60, elevation: 100d, rowOrder: GridRowOrder.SouthToNorth);
        ClipRegion region = SquareClipRegion(minX, minY, maxX, maxY);
        ClipRegion bufferedRegion = SquareClipRegion(minX, minY, maxX, maxY, buffer: LinearDistance.Meters(3d));

        GridClipResult result = GridClipper.Clip(grid, region, new GridClipOptions { CropToRegionEnvelope = false });
        GridClipResult bufferedResult = GridClipper.Clip(grid, bufferedRegion, new GridClipOptions { CropToRegionEnvelope = false });

        Assert.InRange(result.IncludedCellCount, 1950, 2050);
        Assert.True(
            bufferedResult.IncludedCellCount > result.IncludedCellCount,
            "A 3 m buffer must retain strictly more cells than the unbuffered rectangle.");
        Assert.InRange(bufferedResult.IncludedCellCount, 2450, 2700);
    }

    private static HashSet<(int Row, int Column)> IncludedCells(GridClipResult result)
    {
        HashSet<(int Row, int Column)> included = [];
        for (int row = 0; row < result.OutputRowCount; row++)
        {
            for (int column = 0; column < result.OutputColumnCount; column++)
            {
                if (result.GetCellStatus(row, column) != GridCellStatus.RegionExcluded)
                {
                    included.Add((row, column));
                }
            }
        }

        return included;
    }

    private static void AssertElevationsEqual(double?[,] expected, double?[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        for (int row = 0; row < expected.GetLength(0); row++)
        {
            for (int column = 0; column < expected.GetLength(1); column++)
            {
                Assert.Equal(expected[row, column], actual[row, column]);
            }
        }
    }

    private static void AssertStatusesEqual(GridCellStatus[,] expected, GridCellStatus[,] actual)
    {
        Assert.Equal(expected.GetLength(0), actual.GetLength(0));
        Assert.Equal(expected.GetLength(1), actual.GetLength(1));
        for (int row = 0; row < expected.GetLength(0); row++)
        {
            for (int column = 0; column < expected.GetLength(1); column++)
            {
                Assert.Equal(expected[row, column], actual[row, column]);
            }
        }
    }

    private static ElevationGrid UniformGrid(
        int rowCount,
        int columnCount,
        double elevation,
        double cellSize = 1d,
        GridRowOrder rowOrder = GridRowOrder.NorthToSouth,
        GridAnchorConvention anchorConvention = GridAnchorConvention.LowerLeftCorner,
        Coordinate2D? anchor = null,
        HorizontalReference? reference = null) =>
        BuildGrid(rowCount, columnCount, (_, _) => elevation, cellSize, rowOrder, anchorConvention, anchor, reference);

    private static ElevationGrid BuildGrid(
        int rowCount,
        int columnCount,
        Func<int, int, double?> valueAt,
        double cellSize = 1d,
        GridRowOrder rowOrder = GridRowOrder.NorthToSouth,
        GridAnchorConvention anchorConvention = GridAnchorConvention.LowerLeftCorner,
        Coordinate2D? anchor = null,
        HorizontalReference? reference = null)
    {
        double?[,] values = new double?[rowCount, columnCount];
        for (int row = 0; row < rowCount; row++)
        {
            for (int column = 0; column < columnCount; column++)
            {
                values[row, column] = valueAt(row, column);
            }
        }

        return new ElevationGrid(
            reference ?? ProjectedReference(), VerticalReference(), anchor ?? new Coordinate2D(0d, 0d),
            cellSize, cellSize, anchorConvention, rowOrder, values);
    }

    private static ClipRegion SquareClipRegion(double minX, double minY, double maxX, double maxY, LinearDistance? buffer = null, HorizontalReference? reference = null)
    {
        HorizontalReference effectiveReference = reference ?? ProjectedReference();
        string wkt = FormattableString.Invariant($"POLYGON (({minX} {minY}, {maxX} {minY}, {maxX} {maxY}, {minX} {maxY}, {minX} {minY}))");
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt(wkt), effectiveReference);
        return ClipRegion.FromRegion(polygonalRegion, buffer ?? LinearDistance.Zero);
    }

    private static ClipRegion TriangleClipRegion(Coordinate2D a, Coordinate2D b, Coordinate2D c, LinearDistance? buffer = null, HorizontalReference? reference = null)
    {
        HorizontalReference effectiveReference = reference ?? ProjectedReference();
        string wkt = FormattableString.Invariant($"POLYGON (({a.X} {a.Y}, {b.X} {b.Y}, {c.X} {c.Y}, {a.X} {a.Y}))");
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt(wkt), effectiveReference);
        return ClipRegion.FromRegion(polygonalRegion, buffer ?? LinearDistance.Zero);
    }

    private static ClipRegion RectangleClipRegion(Coordinate2D a, Coordinate2D b, Coordinate2D c, Coordinate2D d, LinearDistance? buffer = null, HorizontalReference? reference = null)
    {
        HorizontalReference effectiveReference = reference ?? ProjectedReference();
        string wkt = FormattableString.Invariant($"POLYGON (({a.X} {a.Y}, {b.X} {b.Y}, {c.X} {c.Y}, {d.X} {d.Y}, {a.X} {a.Y}))");
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt(wkt), effectiveReference);
        return ClipRegion.FromRegion(polygonalRegion, buffer ?? LinearDistance.Zero);
    }

    private static ClipRegion ClipRegionFromWkt(string wkt, LinearDistance? buffer = null, HorizontalReference? reference = null)
    {
        HorizontalReference effectiveReference = reference ?? ProjectedReference();
        PolygonalRegion polygonalRegion = PolygonalRegion.FromGeometry(ReadWkt(wkt), effectiveReference);
        return ClipRegion.FromRegion(polygonalRegion, buffer ?? LinearDistance.Zero);
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

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");
}
