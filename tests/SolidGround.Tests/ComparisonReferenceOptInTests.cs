using SolidGround.Core.Accuracy;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;
using System.Security.Cryptography;

namespace SolidGround.Tests;

/// <summary>Optional local verification of a previously captured reference; it performs no HTTP request.</summary>
public sealed class ComparisonReferenceOptInTests
{
    [Fact]
    public async Task ScoresTheAlreadyCapturedReferenceOnlyWhenExplicitlyEnabled()
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
        string source = Path.Combine(directory, "reference.source.json");
        if (!File.Exists(asc) || !File.Exists(prj) || !File.Exists(source))
        {
            Assert.Skip("The opted-in comparison reference files are absent.");
        }

        Assert.Equal("F5D71C8D496628144626BF3588C107D55EFEA5E8476404BB82BD36A5103BD3D5", Hash(asc));
        Assert.Equal("9423E96198C5EB06477BA59B0C6E758B7D19D06030CA7BB5C80FD5D98BE37D17", Hash(prj));
        Assert.Equal("90A7A0D018B07FB4CA9D708C34D79326BFE028EA6FF8D53AF8D83755E1195E5D", Hash(source));
        RasterSourceSidecar sidecar = RasterSourceSidecarIo.Read(File.ReadAllBytes(source), "reference.source.json");
        Assert.Equal("NAVD88", sidecar.Vertical.Datum);
        Assert.Equal(LengthUnit.Meter, sidecar.Vertical.Unit);
        Assert.Equal(26915, sidecar.Acquisition.MetadataRequest!.ProjectedCoordinateSystemCode);
        WellKnownTextReference reference = WellKnownTextReferenceParser.Parse(File.ReadAllText(prj));
        Assert.Equal("EPSG:26915", reference.Horizontal.CoordinateReferenceSystem);
        using StringReader reader = new(File.ReadAllText(asc));
        ElevationGrid grid = AaiGridParser.Parse(reader, reference.Horizontal, new VerticalReference("NAVD88", LengthUnit.Meter));
        (int Budget, int Retained, double Max, double Rms)[] expected =
        [(15000, 12099, 0d, 0d), (8000, 7615, 0.013635253906272737d, 0.0013126492280712805d), (4000, 3770, 0.05400828826122961d, 0.0074652241614992535d)];
        foreach ((int budget, int retained, double max, double rms) in expected)
        {
            SimplificationResult simplified = await new GridTerrainSimplifier().SimplifyAsync(grid, new SimplificationRequest(budget), TestContext.Current.CancellationToken);
            TerrainErrorReport report = TerrainErrorAnalyzer.Analyze(grid, simplified.RetainedSamples, LengthUnit.Meter);
            Assert.Equal(retained, simplified.RetainedPointCount);
            Assert.Equal(12099, report.ComparedCellCount);
            Assert.Equal(0, report.UncoveredCellCount);
            Assert.Equal(max, report.MaximumAbsoluteResidual, 12);
            Assert.Equal(rms, report.RootMeanSquareResidual, 12);
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
