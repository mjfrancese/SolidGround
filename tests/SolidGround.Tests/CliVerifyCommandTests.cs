using SolidGround.Cli;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Transformations;

namespace SolidGround.Tests;

/// <summary>
/// The offline `verify` command: provenance summary printing, the source-coordinate round trip check, and
/// `verify`'s own closed <c>{0, 2, 5, 130}</c> exit-code set with its file-reading convention reversed from
/// every other command's. See docs/architecture/cli-workflow.md's "Commands" and "Exit codes and error
/// classes" sections; <see cref="CliCancellationTests"/> exercises the shared `130` case.
/// </summary>
public sealed class CliVerifyCommandTests
{
    [Fact]
    public async Task VerifiesTheCommittedGoldenBundleAsValid()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = CopyFixture("example-site-synthetic.solidground.json", tempDirectory.FullName);
            CopyFixture("example-site-synthetic.points.csv", tempDirectory.FullName);

            (int exitCode, string stdout, string stderr) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("OpenTopography", stdout, StringComparison.Ordinal);
            Assert.Contains("USGS1m", stdout, StringComparison.Ordinal);
            Assert.Contains("NAD83", stdout, StringComparison.Ordinal);
            Assert.Contains("NAVD88", stdout, StringComparison.Ordinal);
            Assert.Contains("elevation range", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultsThePointsPathFromTheDocumentsOwnBaseName()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = Path.Combine(tempDirectory.FullName, "foo" + TerrainExportBundleRenderer.DocumentFileSuffix);
            string pointsPath = Path.Combine(tempDirectory.FullName, "foo" + TerrainExportBundleRenderer.PointsFileSuffix);
            File.Copy(FixturePath("example-site-synthetic.solidground.json"), documentPath);
            File.Copy(FixturePath("example-site-synthetic.points.csv"), pointsPath);

            (int exitCode, _, string stderr) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsADocumentThatDoesNotEndWithTheDocumentSuffix()
    {
        (int exitCode, _, string stderr) = await RunAsync(["verify", "--document", "terrain.json"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.Contains("--document", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TamperedDocumentHashFailsWithExitTwo()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = CopyFixture("example-site-synthetic.solidground.json", tempDirectory.FullName);
            CopyFixture("example-site-synthetic.points.csv", tempDirectory.FullName);

            string text = File.ReadAllText(documentPath);
            int shaIndex = text.IndexOf("\"sha256\": \"", StringComparison.Ordinal);
            Assert.True(shaIndex >= 0);
            int hashStart = shaIndex + "\"sha256\": \"".Length;
            char originalCharacter = text[hashStart];
            char replacementCharacter = originalCharacter == '0' ? '1' : '0';
            text = string.Concat(text.AsSpan(0, hashStart), replacementCharacter.ToString(), text.AsSpan(hashStart + 1));
            File.WriteAllText(documentPath, text);

            (int exitCode, _, string stderr) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task MissingDocumentFileExitsFive()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = Path.Combine(tempDirectory.FullName, "does-not-exist" + TerrainExportBundleRenderer.DocumentFileSuffix);

            (int exitCode, _, string stderr) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Processing, exitCode);
            Assert.Contains("error (processing):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task PrintsTheSourceCoordinateEnvelopeAndElevationRange()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = CopyFixture("example-site-synthetic.solidground.json", tempDirectory.FullName);
            string pointsPath = CopyFixture("example-site-synthetic.points.csv", tempDirectory.FullName);

            TerrainExportPayload payload = TerrainExportBundleReader.Read(File.ReadAllBytes(documentPath), File.ReadAllBytes(pointsPath));

            (int exitCode, string stdout, _) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("source coordinate envelope", stdout, StringComparison.Ordinal);
            Assert.Contains("reconstructed elevation range", stdout, StringComparison.Ordinal);

            LocalCoordinateFrame frame = payload.Provenance.LocalFrame;
            Coordinate3D first = frame.ToSource(payload.Samples[0].Position);
            Assert.Contains(first.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture), stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ConfirmsToLocalOfToSourceRoundTripsBitExactly()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = CopyFixture("example-site-synthetic.solidground.json", tempDirectory.FullName);
            string pointsPath = CopyFixture("example-site-synthetic.points.csv", tempDirectory.FullName);

            TerrainExportPayload payload = TerrainExportBundleReader.Read(File.ReadAllBytes(documentPath), File.ReadAllBytes(pointsPath));
            LocalCoordinateFrame frame = payload.Provenance.LocalFrame;
            foreach (LocalTerrainSample sample in payload.Samples)
            {
                Coordinate3D source = frame.ToSource(sample.Position);
                LocalCoordinate roundTripped = frame.ToLocal(source);
                Assert.Equal(BitConverter.DoubleToInt64Bits(sample.Position.X), BitConverter.DoubleToInt64Bits(roundTripped.X));
                Assert.Equal(BitConverter.DoubleToInt64Bits(sample.Position.Y), BitConverter.DoubleToInt64Bits(roundTripped.Y));
                Assert.Equal(BitConverter.DoubleToInt64Bits(sample.Position.Elevation), BitConverter.DoubleToInt64Bits(roundTripped.Elevation));
            }

            (int exitCode, string stdout, _) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("the local-to-source coordinate round trip is bit-exact for every sample.", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task VerboseAlsoPrintsTheCoordinateOperationDefinitionText()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = CopyFixture("example-site-synthetic.solidground.json", tempDirectory.FullName);
            CopyFixture("example-site-synthetic.points.csv", tempDirectory.FullName);

            TerrainExportPayload payload = TerrainExportBundleReader.Read(
                File.ReadAllBytes(documentPath), File.ReadAllBytes(Path.Combine(tempDirectory.FullName, "example-site-synthetic.points.csv")));
            string forwardDefinition = payload.Provenance.HorizontalTransformation.ForwardOperation.Definition;

            (int nonVerboseExitCode, string nonVerboseStdout, _) = await RunAsync(["verify", "--document", documentPath], TestContext.Current.CancellationToken);
            (int verboseExitCode, string verboseStdout, _) = await RunAsync(["verify", "--document", documentPath, "--verbose"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, nonVerboseExitCode);
            Assert.Equal(CliExitCodes.Success, verboseExitCode);
            Assert.DoesNotContain(forwardDefinition, nonVerboseStdout, StringComparison.Ordinal);
            Assert.Contains(forwardDefinition, verboseStdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string CopyFixture(string fileName, string destinationDirectory)
    {
        string destination = Path.Combine(destinationDirectory, fileName);
        File.Copy(FixturePath(fileName), destination);
        return destination;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(
            _ => null,
            () => throw new InvalidOperationException("verify must never perform an HTTP call."),
            stdout,
            stderr);

        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
