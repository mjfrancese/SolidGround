using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ElevationStatisticsTests
{
    [Fact]
    public void ValidOnlyMinimumAndMaximumIgnoreNullCellsEvenWhenTheExtremesSitNextToAHole()
    {
        double?[,] elevations =
        {
            { 5d, null, 12d },
            { null, 7d, null },
            { 9d, null, -3d },
        };
        ElevationGrid grid = Grid(elevations);

        Assert.Equal(5, ElevationStatistics.CountValidCells(grid));

        ElevationRange range = ElevationStatistics.ComputeRange(grid);
        Assert.Equal(-3d, range.Minimum);
        Assert.Equal(12d, range.Maximum);
    }

    [Fact]
    public void ASingleValidCellMakesTheMinimumEqualTheMaximum()
    {
        double?[,] elevations =
        {
            { null, null },
            { null, 42.5d },
        };
        ElevationGrid grid = Grid(elevations);

        Assert.Equal(1, ElevationStatistics.CountValidCells(grid));

        ElevationRange range = ElevationStatistics.ComputeRange(grid);
        Assert.Equal(42.5d, range.Minimum);
        Assert.Equal(42.5d, range.Maximum);
    }

    [Fact]
    public void AnAllNoDataGridHasZeroValidCellsAndComputeRangeThrows()
    {
        double?[,] elevations =
        {
            { null, null },
            { null, null },
        };
        ElevationGrid grid = Grid(elevations);

        Assert.Equal(0, ElevationStatistics.CountValidCells(grid));
        Assert.Throws<TerrainProvenanceException>(() => ElevationStatistics.ComputeRange(grid));
    }

    [Fact]
    public void ARealElevationEqualToATypicalAaiGridNoDataSentinelMagnitudeIsCountedAndCanBeTheMinimum()
    {
        // -9999 is AaiGridParser's default NODATA sentinel magnitude, but once a cell holds a non-null double
        // it is a genuine elevation: ElevationStatistics performs no magnitude comparison of its own, only the
        // grid's own null/non-null representation. See docs/architecture/provenance-and-deterministic-exports.md's
        // "NODATA, empty candidate sets, and statistics" section.
        double?[,] elevations =
        {
            { -9999d, 10d },
            { 20d, null },
        };
        ElevationGrid grid = Grid(elevations);

        Assert.Equal(3, ElevationStatistics.CountValidCells(grid));

        ElevationRange range = ElevationStatistics.ComputeRange(grid);
        Assert.Equal(-9999d, range.Minimum);
        Assert.Equal(20d, range.Maximum);
    }

    [Fact]
    public void ComputeRangeTagsTheResultWithTheGridsVerticalUnit()
    {
        double?[,] elevations = { { 1d, 2d } };
        ElevationGrid grid = Grid(elevations, LengthUnit.InternationalFoot);

        ElevationRange range = ElevationStatistics.ComputeRange(grid);
        Assert.Equal(LengthUnit.InternationalFoot, range.Unit);
    }

    [Fact]
    public void CountValidCellsAndComputeRangeRejectANullGrid()
    {
        Assert.Throws<ArgumentNullException>(() => ElevationStatistics.CountValidCells(null!));
        Assert.Throws<ArgumentNullException>(() => ElevationStatistics.ComputeRange(null!));
    }

    private static ElevationGrid Grid(double?[,] elevations, LengthUnit verticalUnit = LengthUnit.Meter) =>
        new(
            ProjectedReference(), VerticalReference(verticalUnit), new Coordinate2D(0d, 0d), 1d, 1d,
            GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, elevations);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit) => new("NAVD88", unit, "Geoid12B");
}
