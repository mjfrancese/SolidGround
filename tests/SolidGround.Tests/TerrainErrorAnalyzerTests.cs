using SolidGround.Core.Accuracy;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
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
    public void IndependentlySuppliedSurfaceMayUseVerticesInsideValidCellFootprintsRatherThanGridCenters()
    {
        ElevationGrid grid = Grid(4, 4, (row, column) => (2d * column) + (3d * row) + 7d);
        TerrainSample[] surface =
        [
            new(new Coordinate3D(0.1d, 0.1d, 5d)), new(new Coordinate3D(3.9d, 0.1d, 12.6d)),
            new(new Coordinate3D(0.1d, 3.9d, 16.4d)), new(new Coordinate3D(3.9d, 3.9d, 24d)),
        ];

        TerrainErrorReport report = TerrainErrorAnalyzer.AnalyzeSurface(grid, surface, LengthUnit.Meter);

        Assert.Equal(16, report.ComparedCellCount);
        Assert.Equal(0, report.UncoveredCellCount);
        Assert.Equal(0d, report.MaximumAbsoluteResidual, precision: 10);
    }

    [Theory]
    [InlineData(LengthUnit.UsSurveyFoot)]
    [InlineData(LengthUnit.InternationalFoot)]
    public void UnitRoundTripAtOuterBoundaryIsNormalizedWithoutExpandingSupport(LengthUnit externalUnit)
    {
        ElevationGrid grid = Grid(4, 4, (_, _) => 10d);
        double Convert(double value) => LengthConverter.Convert(LengthConverter.Convert(value, LengthUnit.Meter, externalUnit), externalUnit, LengthUnit.Meter);
        TerrainSample[] surface =
        [new(new Coordinate3D(Convert(0d), Convert(0d), 10d)), new(new Coordinate3D(Convert(4d), Convert(0d), 10d)),
         new(new Coordinate3D(Convert(0d), Convert(4d), 10d)), new(new Coordinate3D(Convert(4d), Convert(4d), 10d))];

        TerrainErrorReport report = TerrainErrorAnalyzer.AnalyzeSurface(grid, surface, LengthUnit.Meter);

        Assert.Equal(16, report.ComparedCellCount);
        Assert.Equal(0, report.UncoveredCellCount);
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

    [Fact]
    public void DisconnectedValidIslandsNeverBecomeAnInterpolatedBridge()
    {
        ElevationGrid grid = Grid(3, 7, (_, column) => column == 3 ? null : 10d);
        TerrainSample[] retained = [Sample(grid, 0, 0), Sample(grid, 2, 0), Sample(grid, 0, 6), Sample(grid, 2, 6)];

        TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(grid, retained, LengthUnit.Meter);

        Assert.True(report.UncoveredCellCount > 0);
        Assert.True(report.UncoveredByReason.Values.Sum() > 0);
    }

    [Fact]
    public void ConstantElevationInjectionIsReportedAtEveryMeasuredCell()
    {
        ElevationGrid grid = Grid(4, 4, (_, _) => 10d);
        TerrainSample[] surface =
        [
            new(new Coordinate3D(0.1, 0.1, 11d)), new(new Coordinate3D(3.9, 0.1, 11d)),
            new(new Coordinate3D(0.1, 3.9, 11d)), new(new Coordinate3D(3.9, 3.9, 11d)),
        ];

        TerrainErrorReport report = TerrainErrorAnalyzer.AnalyzeSurface(grid, surface, LengthUnit.Meter);

        Assert.Equal(16, report.ComparedCellCount);
        Assert.Equal(1d, report.MaximumAbsoluteResidual);
        Assert.Equal(1d, report.RootMeanSquareResidual);
    }

    [Fact]
    public void RepeatedAnalysisIsDeterministic()
    {
        ElevationGrid grid = Grid(5, 5, (row, column) => row * row - column);
        TerrainSample[] surface = [Sample(grid, 0, 0), Sample(grid, 0, 4), Sample(grid, 4, 0), Sample(grid, 4, 4)];

        TerrainErrorReport first = TerrainErrorAnalyzer.Analyze(grid, surface, LengthUnit.Meter);
        TerrainErrorReport second = TerrainErrorAnalyzer.Analyze(grid, surface, LengthUnit.Meter);

        Assert.Equal(first.MaximumAbsoluteResidual, second.MaximumAbsoluteResidual);
        Assert.Equal(first.RootMeanSquareResidual, second.RootMeanSquareResidual);
        Assert.Equal(first.ComparedCellCount, second.ComparedCellCount);
        Assert.Equal(first.UncoveredCellCount, second.UncoveredCellCount);
        Assert.Equal(first.UncoveredByReason.OrderBy(pair => pair.Key), second.UncoveredByReason.OrderBy(pair => pair.Key));
    }

    [Fact]
    public void PointInsideNoDataHoleIsRejectedRatherThanSnappedAcrossItsBoundary()
    {
        ElevationGrid grid = Grid(5, 5, (row, column) => row == 2 && column == 2 ? null : 0d);
        TerrainSample[] surface =
        [new(new Coordinate3D(0.1, 0.1, 0)), new(new Coordinate3D(4.9, 0.1, 0)), new(new Coordinate3D(2.5, 2.5, 0))];

        ArgumentException error = Assert.Throws<ArgumentException>(() => TerrainErrorAnalyzer.AnalyzeSurface(grid, surface, LengthUnit.Meter));

        Assert.Contains("support footprint", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConcaveClipMaskRejectsTheDelaunayBridgeAcrossTheMissingCorner()
    {
        ElevationGrid source = Grid(5, 5, (_, _) => 0d);
        NetTopologySuite.IO.WKTReader reader = new();
        PolygonalRegion lShape = PolygonalRegion.FromGeometry(reader.Read("POLYGON ((0 0, 5 0, 5 2, 2 2, 2 5, 0 5, 0 0))"), source.HorizontalReference);
        GridClipResult clipped = GridClipper.Clip(source, ClipRegion.FromRegion(lShape, LinearDistance.Zero));
        TerrainSample[] corners = [Sample(clipped.Grid, 0, 0), Sample(clipped.Grid, 0, 4), Sample(clipped.Grid, 4, 0)];

        TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(clipped, corners, LengthUnit.Meter);

        Assert.Equal(16, clipped.RetainedElevationCount);
        Assert.Equal(0, report.ComparedCellCount);
        Assert.Equal(16, report.UncoveredCellCount);
        Assert.Equal(16, report.UncoveredByReason.Values.Sum());
    }

    [Theory]
    [InlineData(1d)]
    [InlineData(-1d)]
    public void RidgeAndSwaleResidualsAreMeasuredAtAConstrainedBudget(double sign)
    {
        ElevationGrid grid = Grid(5, 5, (_, column) => sign * (10d - Math.Abs(column - 2)));
        TerrainSample[] corners = [Sample(grid, 0, 0), Sample(grid, 0, 4), Sample(grid, 4, 0), Sample(grid, 4, 4)];

        TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(grid, corners, LengthUnit.Meter);

        Assert.Equal(25, report.ComparedCellCount);
        Assert.True(report.MaximumAbsoluteResidual > 0d);
        Assert.True(report.RootMeanSquareResidual > 0d);
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
            .Where(column => grid.GetElevation(row, column) is not null)
            .Select(column => new TerrainSample(new Coordinate3D(grid.GetCellCenter(row, column).X, grid.GetCellCenter(row, column).Y, grid.GetElevation(row, column)!.Value))))];

    private static TerrainSample Sample(ElevationGrid grid, int row, int column)
    {
        Coordinate2D center = grid.GetCellCenter(row, column);
        return new TerrainSample(new Coordinate3D(center.X, center.Y, grid.GetElevation(row, column)!.Value));
    }
}
