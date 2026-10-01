using SolidGround.Cli;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Transformations;

namespace SolidGround.Tests;

public sealed class StatePlaneProcessFixtureTests
{
    [Fact]
    public async Task ProcessExportsAndReconstructsThePublicNad83StatePlaneLccFixtureWithinTheUtmTolerance()
    {
        DirectoryInfo output = Directory.CreateTempSubdirectory();
        try
        {
            using StringWriter stdout = new();
            using StringWriter stderr = new();
            CliHost host = new(
                _ => null,
                () => throw new InvalidOperationException("process must never perform an HTTP call."),
                stdout,
                stderr);

            int exitCode = await CliApplication.RunAsync(
                [
                    "process", "--asc", FixturePath("nad83-texas-south-central-synthetic.asc"),
                    "--prj", FixturePath("nad83-texas-south-central-synthetic.prj"),
                    "--vertical-datum", "NAVD88", "--vertical-unit", "meter",
                    "--unit", "meter", "--output", output.FullName,
                ],
                host,
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Empty(stderr.ToString());

            TerrainExportPayload payload = TerrainExportBundleReader.Read(
                File.ReadAllBytes(Path.Combine(output.FullName, "terrain.solidground.json")),
                File.ReadAllBytes(Path.Combine(output.FullName, "terrain.points.csv")));
            IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
                payload.Provenance.HorizontalTransformation.InverseOperation.Definition,
                payload.Provenance.HorizontalTransformation.ForwardOperation.Definition);

            Assert.Equal("EPSG:32140", transform.Definition.TargetReference.CoordinateReferenceSystem);
            Assert.Equal("North_American_Datum_1983", transform.Definition.TargetReference.Datum);
            Assert.NotEmpty(payload.Samples);

            foreach (LocalTerrainSample sample in payload.Samples)
            {
                Coordinate3D source = payload.Provenance.LocalFrame.ToSource(sample.Position);
                Coordinate2D recoveredWgs84 = transform.Inverse(new Coordinate2D(source.X, source.Y));
                Coordinate2D roundTrippedProjected = transform.Forward(recoveredWgs84);
                double residualMeters = Math.Sqrt(
                    Math.Pow(roundTrippedProjected.X - source.X, 2d) +
                    Math.Pow(roundTrippedProjected.Y - source.Y, 2d));

                // Uses the existing, documented UTM projected-round-trip acceptance bound. This checks the
                // real process export/local-frame/reconstruction path, not just ProjNET in isolation.
                Assert.InRange(residualMeters, 0d, ProjNetHorizontalCoordinateTransformFactory.ProjectedRoundTripTolerance.ToMeters());
            }
        }
        finally
        {
            Directory.Delete(output.FullName, recursive: true);
        }
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
