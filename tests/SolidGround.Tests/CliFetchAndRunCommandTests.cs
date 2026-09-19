using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SolidGround.Cli;
using SolidGround.Core.Exports;

namespace SolidGround.Tests;

/// <summary>
/// The online `fetch` and `run` commands: key resolution before any HTTP call, acquisition evidence
/// printing, raster-set persistence, and the shared processing pipeline `run` reuses from `process`. See
/// docs/architecture/cli-workflow.md's "Commands", "Secrets and key resolution", and "Raster set
/// persistence" sections. Reuses <see cref="OpenTopographyUsgs1mSourceTests.CreateZipArchive"/> (widened to
/// <see langword="internal"/> for this file) and <see cref="FakeHttpMessageHandler"/> so no test in this file
/// ever performs a real network call.
/// </summary>
public sealed class CliFetchAndRunCommandTests
{
    private const string FakeKey = "fixture-fake-key-0123456789";
    private const string Bbox = "[withheld],[withheld],[withheld],[withheld]";

    [Fact]
    public async Task MissingKeyOnFetchExitsWithAuthorizationAndNeverInvokesTheHandler()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo emptyAppData = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            CliHost host = CreateHost(handler, name => name is "APPDATA" or "HOME" ? emptyAppData.FullName : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Authorization, exitCode);
            Assert.Empty(handler.Requests);
            Assert.Contains("OPENTOPOGRAPHY_API_KEY", stderr, StringComparison.Ordinal);
            Assert.Contains("dotnet user-secrets set", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(emptyAppData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task MissingKeyOnRunExitsWithAuthorizationAndNeverInvokesTheHandler()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo emptyAppData = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
            CliHost host = CreateHost(handler, name => name is "APPDATA" or "HOME" ? emptyAppData.FullName : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Authorization, exitCode);
            Assert.Empty(handler.Requests);
            Assert.Contains("OPENTOPOGRAPHY_API_KEY", stderr, StringComparison.Ordinal);
            Assert.Contains("dotnet user-secrets set", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(emptyAppData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task Returns401AsAuthorizationExitCode()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => TextResponse(HttpStatusCode.Unauthorized, "Not a valid format API Key"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Authorization, exitCode);
            Assert.Contains("error (authorization):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task FetchWritesARasterSetFromAFakeZipResponse()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string ascText = ReadFixture("example-site-synthetic.asc");
            string prjText = ReadFixture("example-site-synthetic.prj");
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ascText), ("example-site-synthetic.prj", prjText));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", tempDirectory.FullName, "--verbose"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);

            string ascPath = Path.Combine(tempDirectory.FullName, "terrain.asc");
            string prjPath = Path.Combine(tempDirectory.FullName, "terrain.prj");
            string sourceJsonPath = Path.Combine(tempDirectory.FullName, "terrain.source.json");
            Assert.True(File.Exists(ascPath));
            Assert.True(File.Exists(prjPath));
            Assert.True(File.Exists(sourceJsonPath));

            Assert.Equal(File.ReadAllBytes(FixturePath("example-site-synthetic.prj")), File.ReadAllBytes(prjPath));

            using JsonDocument sidecar = JsonDocument.Parse(File.ReadAllBytes(sourceJsonPath));
            Assert.Equal("solidground.raster-source", sidecar.RootElement.GetProperty("schema").GetString());
            Assert.Equal(1, sidecar.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("OpenTopography", sidecar.RootElement.GetProperty("sourceName").GetString());

            // Proves the sidecar round-trips through RasterSourceSidecarIo.Read (internal to SolidGround.Cli,
            // so exercised only indirectly here): process must successfully read it back as the default
            // sibling --source-json when none is given explicitly.
            DirectoryInfo processOutput = Directory.CreateTempSubdirectory();
            try
            {
                (int processExitCode, _, _) = await RunAsync(
                    host, ["process", "--asc", ascPath, "--output", processOutput.FullName], TestContext.Current.CancellationToken);
                Assert.Equal(CliExitCodes.Success, processExitCode);
            }
            finally
            {
                Directory.Delete(processOutput.FullName, recursive: true);
            }

            Assert.DoesNotContain(FakeKey, stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(sourceJsonPath), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(prjPath), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(ascPath), StringComparison.Ordinal);

            Assert.Contains("API_Key=REDACTED", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunWritesOnlyTheBundleByDefault()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, _) = await RunAsync(host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix)));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.asc")));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.prj")));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.source.json")));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunWritesTheRasterSetTooWithSaveRaster()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, _) = await RunAsync(
                host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName, "--save-raster"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix)));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.asc")));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.prj")));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.source.json")));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunVerboseSummaryPrintsTheRedactedUriAndNeverLeaksTheKey()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host,
                ["run", "--bbox", Bbox, "--output", tempDirectory.FullName, "--save-raster", "--verbose"],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("API_Key=REDACTED", stdout, StringComparison.Ordinal);

            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            string pointsPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix);
            string ascPath = Path.Combine(tempDirectory.FullName, "terrain.asc");
            string prjPath = Path.Combine(tempDirectory.FullName, "terrain.prj");
            string sourceJsonPath = Path.Combine(tempDirectory.FullName, "terrain.source.json");

            Assert.DoesNotContain(FakeKey, stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(documentPath), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(pointsPath), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(ascPath), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(prjPath), StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, File.ReadAllText(sourceJsonPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunAppliesMetadataOverridesToTheExportedProvenanceRatherThanTheAcquisitionsOwnValues()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, _) = await RunAsync(
                host,
                [
                    "run", "--bbox", Bbox, "--output", tempDirectory.FullName,
                    "--quality-level", "QL2", "--collection-start", "2024-05-01", "--collection-end", "2024-05-02",
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
            JsonElement source = document.RootElement.GetProperty("provenance").GetProperty("source");

            // The fake zip's acquisition reports no quality level or collection period of its own (only
            // "OpenTopography"/"USGS1m" for source name/dataset), so a non-null match here proves the CLI
            // options actually override the acquisition rather than being silently ignored.
            Assert.Equal("QL2", source.GetProperty("qualityLevel").GetString());
            JsonElement collectionPeriod = source.GetProperty("collectionPeriod");
            Assert.Equal("2024-05-01", collectionPeriod.GetProperty("start").GetString());
            Assert.Equal("2024-05-02", collectionPeriod.GetProperty("end").GetString());
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunClipsAParcelAgainstTheFetchedGrid()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, _) = await RunAsync(
                host,
                ["run", "--parcel", FixturePath("example-site-synthetic-parcel.geojson"), "--buffer", "100", "--output", tempDirectory.FullName],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = JsonDocument.Parse(
                File.ReadAllBytes(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
            int originalPointCount = document.RootElement.GetProperty("provenance").GetProperty("originalPointCount").GetInt32();
            Assert.Equal(8, originalPointCount);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task FetchRejectsAProcessOnlyOption()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("must not perform an HTTP call"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", tempDirectory.FullName, "--unit", "meter"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("--unit", stderr, StringComparison.Ordinal);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task TaskCanceledExceptionDuringAcquisitionMapsToSourceQuality()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            // Simulates an HttpClient.Timeout expiry without a real clock: the caller's own ambient
            // cancellation token is never cancelled, so this synchronous TaskCanceledException takes Core's
            // unguarded OperationCanceledException clause (mapped to OpenTopographyNetworkException, exit 4),
            // never the guarded one CliCancellationTests exercises for a genuine caller cancellation.
            FakeHttpMessageHandler handler = new((Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>)((_, _) =>
                throw new TaskCanceledException("simulated timeout")));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", tempDirectory.FullName, "--timeout", "1"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.SourceQuality, exitCode);
            Assert.Contains("error (source-quality):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task SaveRasterRefusesToOverwriteAnExistingRasterSetWithoutTheFlag()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            File.WriteAllText(Path.Combine(tempDirectory.FullName, "terrain.asc"), "pre-existing");
            FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("must not perform an HTTP call"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName, "--save-raster"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("--overwrite", stderr, StringComparison.Ordinal);
            Assert.Empty(handler.Requests);
            Assert.Equal("pre-existing", File.ReadAllText(Path.Combine(tempDirectory.FullName, "terrain.asc")));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task FetchRejectsAnOutputPathOccupiedByAFileWithAProcessingExitCode()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string outputAsAFile = Path.Combine(tempDirectory.FullName, "not-a-directory");
            File.WriteAllText(outputAsAFile, "occupies the output path");

            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", outputAsAFile], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Processing, exitCode);
            Assert.Contains("error (processing):", stderr, StringComparison.Ordinal);
            Assert.Contains("Could not create the raster set output directory", stderr, StringComparison.Ordinal);
            Assert.Equal("occupies the output path", File.ReadAllText(outputAsAFile));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string ReadFixture(string fileName) => File.ReadAllText(FixturePath(fileName));

    private static CliHost CreateHost(FakeHttpMessageHandler handler, Func<string, string?> getEnvironmentVariable) => new(
        getEnvironmentVariable,
        () => handler,
        new StringWriter(),
        new StringWriter());

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(CliHost host, string[] args, CancellationToken cancellationToken)
    {
        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, host.StandardOutput.ToString() ?? string.Empty, host.StandardError.ToString() ?? string.Empty);
    }

    private static Task<HttpResponseMessage> TextResponse(HttpStatusCode statusCode, string body)
    {
        HttpResponseMessage response = new(statusCode) { Content = new StringContent(body) };
        return Task.FromResult(response);
    }

    private static Task<HttpResponseMessage> ZipResponse(byte[] zipBytes, string fileName)
    {
        HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = fileName };
        return Task.FromResult(response);
    }
}
