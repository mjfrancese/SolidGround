using SolidGround.Core.Accuracy;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class TerrainErrorAnalyzerTests
{
    [Fact]
    public void ExactPlaneHasZeroMeasuredErrorAndFullCoverage()
    {
        ElevationGrid grid = Grid(4, 4, (row, column) => (2d * column) + (3d * row) + 7d);
        TerrainSample[] retained = Samples(grid);

        TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(grid, retained, LengthUnit.Meter);

        Assert.Equal(16, report.ComparedCellCount);
        Assert.Equal(0, report.UncoveredCellCount);
        Assert.Equal(0d, report.MaximumAbsoluteResidual);
        Assert.Equal(0d, report.RootMeanSquareResidual);
        Assert.Equal(LengthUnit.Meter, report.Unit);
    }

    [Fact]
    public void IndependentlySuppliedSurfaceReportsKnownInjectedErrorInRequestedUnit()
    {
        ElevationGrid grid = Grid(3, 3, (_, _) => 10d);
        TerrainSample[] surface = Samples(grid);
        surface[0] = new TerrainSample(new Coordinate3D(surface[0].Position.X, surface[0].Position.Y, 11d));

        TerrainErrorReport report = TerrainErrorAnalyzer.AnalyzeSurface(grid, surface, LengthUnit.UsSurveyFoot);

        Assert.Equal(1d * 3937d / 1200d, report.MaximumAbsoluteResidual, precision: 10);
        Assert.True(report.RootMeanSquareResidual > 0d);
        Assert.Equal(9, report.ComparedCellCount);
    }

    [Fact]
    public void NoDataHoleIsReportedAsCoverageGapInsteadOfInterpolated()
    {
        ElevationGrid grid = Grid(5, 5, (row, column) => row == 2 && column == 2 ? null : 0d);
        TerrainSample[] corners =
        [
            Sample(grid, 0, 0), Sample(grid, 0, 4), Sample(grid, 4, 0), Sample(grid, 4, 4),
        ];

        TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(grid, corners, LengthUnit.Meter);

        Assert.True(report.UncoveredCellCount > 0);
        Assert.True(report.UncoveredByReason[TerrainCoverageGapReason.OutsideRetainedDomain] > 0);
        Assert.Equal(0d, report.MaximumAbsoluteResidual);
    }

    private static ElevationGrid Grid(int rows, int columns, Func<int, int, double?> elevation)
    {
        double?[,] values = new double?[rows, columns];
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                values[row, column] = elevation(row, column);
            }
        }

        HorizontalReference horizontal = new(
            "EPSG:26915", "NAD83", HorizontalReferenceKind.Projected,
            HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        return new ElevationGrid(horizontal, new VerticalReference("NAVD88", LengthUnit.Meter), new Coordinate2D(0, 0),
            1, 1, GridAnchorConvention.LowerLeftCorner, GridRowOrder.SouthToNorth, values);
    }

    private static TerrainSample[] Samples(ElevationGrid grid) =>
        [.. Enumerable.Range(0, grid.RowCount).SelectMany(row => Enumerable.Range(0, grid.ColumnCount)
            .Select(column => new TerrainSample(new Coordinate3D(grid.GetCellCenter(row, column).X, grid.GetCellCenter(row, column).Y, grid.GetElevation(row, column)!.Value))))];

    private static TerrainSample Sample(ElevationGrid grid, int row, int column)
    {
        Coordinate2D center = grid.GetCellCenter(row, column);
        return new TerrainSample(new Coordinate3D(center.X, center.Y, grid.GetElevation(row, column)!.Value));
    }
}
