using SolidGround.Core.Accuracy;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>Optional local verification of a previously captured reference; it performs no HTTP request.</summary>
public sealed class ComparisonReferenceOptInTests
{
    [Fact]
    public void ScoresTheAlreadyCapturedReferenceOnlyWhenExplicitlyEnabled()
    {
        if (Environment.GetEnvironmentVariable("SOLIDGROUND_COMPARISON_REFERENCE") != "1")
        {
            Assert.Skip("Set SOLIDGROUND_COMPARISON_REFERENCE=1 and SOLIDGROUND_COMPARISON_REFERENCE_DIRECTORY to score a local, already captured reference.");
        }

        string? directory = Environment.GetEnvironmentVariable("SOLIDGROUND_COMPARISON_REFERENCE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            Assert.Skip("SOLIDGROUND_COMPARISON_REFERENCE_DIRECTORY is not configured.");
        }

        string asc = Path.Combine(directory, "reference.asc");
        string prj = Path.Combine(directory, "reference.prj");
        if (!File.Exists(asc) || !File.Exists(prj))
        {
            Assert.Skip("The opted-in comparison reference files are absent.");
        }

        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(File.ReadAllText(prj));
        using StringReader reader = new(File.ReadAllText(asc));
        ElevationGrid grid = AaiGridParser.Parse(reader, reference.Horizontal, new VerticalReference("NAVD88", LengthUnit.Meter));
        TerrainSample[] samples = [.. Enumerable.Range(0, grid.RowCount).SelectMany(row => Enumerable.Range(0, grid.ColumnCount)
            .Where(column => grid.GetElevation(row, column) is not null)
            .Select(column =>
            {
                Coordinate2D center = grid.GetCellCenter(row, column);
                return new TerrainSample(new Coordinate3D(center.X, center.Y, grid.GetElevation(row, column)!.Value));
            }))];

        TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(grid, samples, LengthUnit.Meter);
        Assert.Equal(LengthUnit.Meter, report.Unit);
        Assert.Equal(0, report.UncoveredCellCount);
        Assert.Equal(0d, report.MaximumAbsoluteResidual);
    }
}
