using SolidGround.Core.Processing;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ElevationGridSamplerTests
{
    [Fact]
    public void PublicSamplerExposesTheStrictBilinearContract()
    {
        Type? sampler = typeof(TerrainProcessingPipeline).Assembly.GetType("SolidGround.Core.Processing.ElevationGridSampler");

        Assert.NotNull(sampler);
        Assert.NotNull(sampler!.GetMethod("SampleStrictBilinear"));
    }

    [Theory]
    [InlineData(GridAnchorConvention.LowerLeftCorner, GridRowOrder.SouthToNorth)]
    [InlineData(GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth)]
    [InlineData(GridAnchorConvention.CellCenter, GridRowOrder.SouthToNorth)]
    [InlineData(GridAnchorConvention.CellCenter, GridRowOrder.NorthToSouth)]
    public void SamplesAnAnalyticPlaneAcrossEveryAnchorAndRowOrder(GridAnchorConvention anchor, GridRowOrder rowOrder)
    {
        ElevationGrid grid = Grid(anchor, rowOrder, new double?[,] { { 0d, 10d }, { 20d, 30d } });
        Coordinate2D southwest = grid.GetCellCenter(rowOrder == GridRowOrder.SouthToNorth ? 0 : 1, 0);

        ElevationGridSample sample = ElevationGridSampler.SampleStrictBilinear(grid, new Coordinate2D(southwest.X + 0.5d, southwest.Y + 0.5d));

        Assert.Equal(15d, sample.Elevation);
        Assert.Equal(4, sample.Support.Count);
        Assert.All(sample.Support, support => Assert.Equal(0.25d, support.Weight));
    }

    [Fact]
    public void ExactCentreAndEndpointRequireOnlyTheirNonzeroWeightCells()
    {
        ElevationGrid grid = Grid(GridAnchorConvention.LowerLeftCorner, GridRowOrder.SouthToNorth,
            new double?[,] { { 10d, null }, { null, null } });
        Coordinate2D centre = grid.GetCellCenter(0, 0);

        ElevationGridSample sample = ElevationGridSampler.SampleStrictBilinear(grid, centre);

        Assert.Equal(10d, sample.Elevation);
        Assert.Single(sample.Support);
        Assert.Equal(1d, sample.Support[0].Weight);
        Assert.Throws<ElevationGridSamplingException>(() => ElevationGridSampler.SampleStrictBilinear(grid, new Coordinate2D(centre.X + 0.5d, centre.Y)));
    }

    [Fact]
    public void NormalizesOnlyEpsilonNearCentreAndRejectsOutsideTheClosedLattice()
    {
        ElevationGrid grid = Grid(GridAnchorConvention.CellCenter, GridRowOrder.NorthToSouth,
            new double?[,] { { 30d, 40d }, { 10d, 20d } });
        Coordinate2D centre = grid.GetCellCenter(1, 0);

        ElevationGridSample epsilon = ElevationGridSampler.SampleStrictBilinear(
            grid, new Coordinate2D(centre.X - (ElevationGridSampler.IndexTolerance / 2d), centre.Y));

        Assert.Equal(10d, epsilon.Elevation);
        Assert.Throws<ElevationGridSamplingException>(() => ElevationGridSampler.SampleStrictBilinear(
            grid, new Coordinate2D(centre.X - (ElevationGridSampler.IndexTolerance * 2d), centre.Y)));
    }

    private static ElevationGrid Grid(GridAnchorConvention anchor, GridRowOrder order, double?[,] elevations) => new(
        new HorizontalReference("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing),
        new VerticalReference("NAVD88", LengthUnit.Meter), new Coordinate2D(0d, 0d), 1d, 1d, anchor, order, elevations);
}
