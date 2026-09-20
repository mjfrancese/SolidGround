using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using SolidGround.Cli;
using SolidGround.Core.Exports;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Units;

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
            Assert.Equal(2, sidecar.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("OpenTopography", sidecar.RootElement.GetProperty("sourceName").GetString());

            // The zip response carries its own .prj sidecar, so both references came straight from the
            // response itself (SourceResponse/SourceResponse), and no metadata request was ever made.
            Assert.Equal("SourceResponse", sidecar.RootElement.GetProperty("horizontalReferenceOrigin").GetString());
            Assert.Equal("SourceResponse", sidecar.RootElement.GetProperty("verticalReferenceOrigin").GetString());
            Assert.Equal(JsonValueKind.Null, sidecar.RootElement.GetProperty("acquisition").GetProperty("metadataRequest").ValueKind);

            Assert.Contains(
                "fetch: horizontal reference EPSG:26915 from the response sidecar; vertical reference NAVD88 (Meter) from the response sidecar.",
                stdout, StringComparison.Ordinal);

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

            (int exitCode, string stdout, _) = await RunAsync(host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            Assert.True(File.Exists(documentPath));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix)));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.asc")));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.prj")));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.source.json")));

            // The zip response carries its own .prj sidecar, so the exported provenance's origins are both
            // SourceResponse -- unchanged from before SolidGround Issue #21's hybrid GeoTIFF-GeoKeys flow.
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(documentPath));
            JsonElement provenance = document.RootElement.GetProperty("provenance");
            Assert.Equal("SourceResponse", provenance.GetProperty("sourceHorizontalReferenceOrigin").GetString());
            Assert.Equal("SourceResponse", provenance.GetProperty("sourceVerticalReferenceOrigin").GetString());

            Assert.Contains(
                "run: horizontal reference EPSG:26915 from the response sidecar; vertical reference NAVD88 (Meter) from the response sidecar.",
                stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RunWithNoUnitOptionWritesLengthConverterDefaultOutputUnitToTheLocalFrame()
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
            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(documentPath));
            string outputUnit = document.RootElement.GetProperty("provenance").GetProperty("localFrame").GetProperty("outputUnit").GetString()!;
            Assert.Equal(LengthConverter.DefaultOutputUnit.ToString(), outputUnit);
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
            string prjPath = Path.Combine(tempDirectory.FullName, "terrain.prj");
            string sourceJsonPath = Path.Combine(tempDirectory.FullName, "terrain.source.json");
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix)));
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.asc")));
            Assert.True(File.Exists(prjPath));
            Assert.True(File.Exists(sourceJsonPath));

            // This response carries its own .prj sidecar, so the raster set's own .prj must be that exact
            // text, and both references came straight from the response (SourceResponse/SourceResponse) with
            // no metadata request -- mirroring FetchWritesARasterSetFromAFakeZipResponse's assertions on the
            // equivalent fetch --save-raster-less sidecar, since run's own --save-raster path wires its
            // acquisition evidence into this sidecar write independently of fetch's. The fixture is itself
            // byte-identical to NorthAmericanUtmWellKnownText.Create(26915, NAVD88/Meter) (see that type's own
            // doc comment), so both checks hold simultaneously.
            Assert.Equal(File.ReadAllBytes(FixturePath("example-site-synthetic.prj")), File.ReadAllBytes(prjPath));
            Assert.Equal(NorthAmericanUtmWellKnownText.Create(26915, new VerticalReference("NAVD88", LengthUnit.Meter)), File.ReadAllText(prjPath));

            using JsonDocument sidecar = JsonDocument.Parse(File.ReadAllBytes(sourceJsonPath));
            Assert.Equal(2, sidecar.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("SourceResponse", sidecar.RootElement.GetProperty("horizontalReferenceOrigin").GetString());
            Assert.Equal("SourceResponse", sidecar.RootElement.GetProperty("verticalReferenceOrigin").GetString());
            Assert.Equal(JsonValueKind.Null, sidecar.RootElement.GetProperty("acquisition").GetProperty("metadataRequest").ValueKind);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task HybridRunWritesTheRasterSetTooWithSaveRaster()
    {
        // The one acquisition-evidence branch Issue #21 actually adds -- a bare AAIGrid data response whose
        // horizontal/vertical references come from a second metadata request, not the response itself -- was
        // never exercised through run --save-raster's own sidecar write before (only through fetch, and
        // through run's own bundle document, never run's raster-set sidecar). Reuses the same
        // BuildHybridTiffBytes/HybridResponder helpers HybridFetchWritesARasterSetFromABareAaiGridBodyAndAMetadataTiffResponse
        // already uses for fetch.
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string aaiGridText = ReadFixture("example-site-synthetic.asc");
            byte[] tiffBytes = BuildHybridTiffBytes();
            FakeHttpMessageHandler handler = new(HybridResponder(aaiGridText, tiffBytes));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, _) = await RunAsync(
                host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName, "--save-raster"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(2, handler.Requests.Count);

            string prjPath = Path.Combine(tempDirectory.FullName, "terrain.prj");
            string sourceJsonPath = Path.Combine(tempDirectory.FullName, "terrain.source.json");
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.asc")));
            Assert.True(File.Exists(prjPath));
            Assert.True(File.Exists(sourceJsonPath));

            string expectedWkt = NorthAmericanUtmWellKnownText.Create(26915, new VerticalReference("NAVD88", LengthUnit.Meter));
            Assert.Equal(expectedWkt, File.ReadAllText(prjPath));

            using JsonDocument sidecar = JsonDocument.Parse(File.ReadAllBytes(sourceJsonPath));
            Assert.Equal(2, sidecar.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("SourceMetadataResponse", sidecar.RootElement.GetProperty("horizontalReferenceOrigin").GetString());
            Assert.Equal("DatasetDocumentation", sidecar.RootElement.GetProperty("verticalReferenceOrigin").GetString());

            JsonElement acquisition = sidecar.RootElement.GetProperty("acquisition");
            Assert.Equal("GeoTiffGeoKeys", acquisition.GetProperty("referenceSource").GetString());
            JsonElement metadataRequest = acquisition.GetProperty("metadataRequest");
            Assert.NotEqual(JsonValueKind.Null, metadataRequest.ValueKind);
            Assert.Equal(26915, metadataRequest.GetProperty("projectedCoordinateSystemCode").GetInt32());
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

    // ---- hybrid GeoTIFF-GeoKeys flow (SolidGround Issue #21): a bare AAIGrid body triggers a second,
    // metadata-only GTiff request, built here with TiffBuilder (widened to internal for exactly this reuse
    // by GeoTiffMetadataReaderTests) so it matches example-site-synthetic.asc's header exactly. ------------------

    [Fact]
    public async Task HybridFetchWritesARasterSetFromABareAaiGridBodyAndAMetadataTiffResponse()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string aaiGridText = ReadFixture("example-site-synthetic.asc");
            byte[] tiffBytes = BuildHybridTiffBytes();
            FakeHttpMessageHandler handler = new(HybridResponder(aaiGridText, tiffBytes));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, string stdout, string stderr) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", tempDirectory.FullName, "--verbose"], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(2, handler.Requests.Count);

            string ascPath = Path.Combine(tempDirectory.FullName, "terrain.asc");
            string prjPath = Path.Combine(tempDirectory.FullName, "terrain.prj");
            string sourceJsonPath = Path.Combine(tempDirectory.FullName, "terrain.source.json");
            Assert.True(File.Exists(ascPath));
            Assert.True(File.Exists(prjPath));
            Assert.True(File.Exists(sourceJsonPath));

            string expectedWkt = NorthAmericanUtmWellKnownText.Create(26915, new VerticalReference("NAVD88", LengthUnit.Meter));
            Assert.Equal(expectedWkt, File.ReadAllText(prjPath));

            using JsonDocument sidecar = JsonDocument.Parse(File.ReadAllBytes(sourceJsonPath));
            Assert.Equal(2, sidecar.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("SourceMetadataResponse", sidecar.RootElement.GetProperty("horizontalReferenceOrigin").GetString());
            Assert.Equal("DatasetDocumentation", sidecar.RootElement.GetProperty("verticalReferenceOrigin").GetString());

            JsonElement acquisition = sidecar.RootElement.GetProperty("acquisition");
            Assert.Equal("GeoTiffGeoKeys", acquisition.GetProperty("referenceSource").GetString());
            JsonElement metadataRequest = acquisition.GetProperty("metadataRequest");
            Assert.NotEqual(JsonValueKind.Null, metadataRequest.ValueKind);
            Assert.Contains("API_Key=REDACTED", metadataRequest.GetProperty("redactedRequestUri").GetString(), StringComparison.Ordinal);
            Assert.Equal(26915, metadataRequest.GetProperty("projectedCoordinateSystemCode").GetInt32());
            Assert.Equal(3, metadataRequest.GetProperty("imageWidth").GetInt64());
            Assert.Equal(3, metadataRequest.GetProperty("imageLength").GetInt64());

            Assert.Contains(
                "fetch: horizontal reference EPSG:26915 from the GeoTIFF GeoKeys of the metadata request; " +
                "vertical reference NAVD88 (Meter) from dataset documentation.",
                stdout, StringComparison.Ordinal);

            Assert.Contains("fetch: metadata request uri:", stdout, StringComparison.Ordinal);
            Assert.Contains("fetch: metadata status: 200", stdout, StringComparison.Ordinal);
            Assert.Contains("fetch: metadata content type:", stdout, StringComparison.Ordinal);
            Assert.Contains("fetch: metadata content-disposition file name:", stdout, StringComparison.Ordinal);
            Assert.Contains("fetch: metadata response bytes:", stdout, StringComparison.Ordinal);
            Assert.Contains("fetch: metadata geokeys: EPSG:26915", stdout, StringComparison.Ordinal);

            // Round-trips the version 2 sidecar's non-null metadataRequest through RasterSourceSidecarIo.Read
            // (internal to SolidGround.Cli, so exercised only indirectly here): process must successfully
            // read it back as the default sibling --source-json when none is given explicitly.
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
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task HybridRunWritesABundleWithGeoTiffDerivedOriginsAndVerifyAcceptsIt()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string aaiGridText = ReadFixture("example-site-synthetic.asc");
            byte[] tiffBytes = BuildHybridTiffBytes();
            FakeHttpMessageHandler handler = new(HybridResponder(aaiGridText, tiffBytes));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int exitCode, _, _) = await RunAsync(host, ["run", "--bbox", Bbox, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(2, handler.Requests.Count);

            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            string pointsPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(documentPath));
            JsonElement provenance = document.RootElement.GetProperty("provenance");
            Assert.Equal("SourceMetadataResponse", provenance.GetProperty("sourceHorizontalReferenceOrigin").GetString());
            Assert.Equal("DatasetDocumentation", provenance.GetProperty("sourceVerticalReferenceOrigin").GetString());

            (int verifyExitCode, _, string verifyStderr) = await RunAsync(
                host, ["verify", "--document", documentPath, "--points", pointsPath], TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, verifyExitCode);
            Assert.Equal(string.Empty, verifyStderr);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    // ---- process's vertical-reference-origin precedence (VerticalReferenceResolution.Resolve, exercised
    // only through the CLI's public surface -- see docs/architecture/cli-workflow.md's "Raster set
    // persistence" section) --------------------------------------------------------------------------------

    [Fact]
    public async Task ProcessWithAV2SidecarAndNoVerticalFlagsReportsTheSidecarsVerticalOrigin()
    {
        DirectoryInfo fetchOutput = Directory.CreateTempSubdirectory();
        DirectoryInfo processOutput = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int fetchExitCode, _, _) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", fetchOutput.FullName], TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, fetchExitCode);

            string ascPath = Path.Combine(fetchOutput.FullName, "terrain.asc");

            (int processExitCode, string processStdout, _) = await RunAsync(
                host, ["process", "--asc", ascPath, "--output", processOutput.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, processExitCode);
            Assert.Contains(
                "process: horizontal reference EPSG:26915 from the operator; vertical reference NAVD88 (Meter) from the response sidecar.",
                processStdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fetchOutput.FullName, recursive: true);
            Directory.Delete(processOutput.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessWithoutASidecarReportsOperatorForBothReferences()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("process must never perform an HTTP call."));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", tempDirectory.FullName],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains(
                "process: horizontal reference EPSG:26915 from the operator; vertical reference NAVD88 (Meter) from the operator.",
                stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessWithAnExplicitVerticalDatumReportsOperatorForTheVertical()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) => throw new InvalidOperationException("process must never perform an HTTP call."));
            CliHost host = CreateHost(handler, _ => null);

            (int exitCode, string stdout, _) = await RunAsync(
                host,
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--vertical-datum", "Other", "--vertical-unit", "meter", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains(
                "process: horizontal reference EPSG:26915 from the operator; vertical reference Other (Meter) from the operator.",
                stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessWithAV2SidecarAndAnExplicitVerticalOverrideReportsOperatorForTheVertical()
    {
        // Precedence branch (c): a --source-json sidecar is present, and its own VerticalReferenceOrigin is
        // not Operator (SourceResponse, exactly like ProcessWithAV2SidecarAndNoVerticalFlagsReportsTheSidecarsVerticalOrigin
        // above), but an explicit --vertical-datum/--vertical-unit override still wins. Unlike
        // ProcessWithAnExplicitVerticalDatumReportsOperatorForTheVertical above, which never supplies a
        // sidecar at all, this proves the override actually beats a present, non-Operator sidecar rather than
        // just filling in for the sidecar's absence.
        DirectoryInfo fetchOutput = Directory.CreateTempSubdirectory();
        DirectoryInfo processOutput = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int fetchExitCode, _, _) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", fetchOutput.FullName], TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, fetchExitCode);

            string ascPath = Path.Combine(fetchOutput.FullName, "terrain.asc");

            (int processExitCode, string processStdout, _) = await RunAsync(
                host,
                [
                    "process", "--asc", ascPath, "--vertical-datum", "Other", "--vertical-unit", "meter",
                    "--output", processOutput.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, processExitCode);
            Assert.Contains(
                "process: horizontal reference EPSG:26915 from the operator; vertical reference Other (Meter) from the operator.",
                processStdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fetchOutput.FullName, recursive: true);
            Directory.Delete(processOutput.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ProcessWithAV2SidecarAndOnlyAGeoidOverrideReportsOperatorForTheVertical()
    {
        // Precedence branch (c)'s subtlest case, flagged by VerticalReferenceResolution.Resolve's own
        // comment: --geoid alone (no --vertical-datum/--vertical-unit) still counts as a CLI vertical flag,
        // so the origin must flip to Operator even though the printed datum/unit values are unchanged from
        // the sidecar's own NAVD88 (Meter) -- cliVerticalDatum/cliVerticalUnit stay null, so datum/unit still
        // fall through to the sidecar. This is the case most likely to silently regress if the precedence
        // check ever moves from "any of the three CLI vertical flags" to "datum or unit only".
        DirectoryInfo fetchOutput = Directory.CreateTempSubdirectory();
        DirectoryInfo processOutput = Directory.CreateTempSubdirectory();
        try
        {
            byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
                ("example-site-synthetic.asc", ReadFixture("example-site-synthetic.asc")), ("example-site-synthetic.prj", ReadFixture("example-site-synthetic.prj")));
            FakeHttpMessageHandler handler = new((_, _) => ZipResponse(zipBytes, "usgs1m.zip"));
            CliHost host = CreateHost(handler, name => name == "OPENTOPOGRAPHY_API_KEY" ? FakeKey : null);

            (int fetchExitCode, _, _) = await RunAsync(
                host, ["fetch", "--bbox", Bbox, "--output", fetchOutput.FullName], TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, fetchExitCode);

            string ascPath = Path.Combine(fetchOutput.FullName, "terrain.asc");

            (int processExitCode, string processStdout, _) = await RunAsync(
                host,
                ["process", "--asc", ascPath, "--geoid", "GEOID18", "--output", processOutput.FullName],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, processExitCode);
            Assert.Contains(
                "process: horizontal reference EPSG:26915 from the operator; vertical reference NAVD88 (Meter) from the operator.",
                processStdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(fetchOutput.FullName, recursive: true);
            Directory.Delete(processOutput.FullName, recursive: true);
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

    /// <summary>
    /// A GeoTIFF metadata response matching <c>example-site-synthetic.asc</c>'s header exactly (3x3, cellsize 1,
    /// lower-left corner ([withheld], [withheld]), NODATA -32768): PixelIsArea, so the tiepoint's model-space
    /// coordinate is directly the upper-left corner ([withheld], [withheld] + 3*1 = [withheld]), and EPSG:26915.
    /// </summary>
    private static byte[] BuildHybridTiffBytes() =>
        new TiffBuilder(bigEndian: false)
            .WithShort(256, 3)
            .WithShort(257, 3)
            .WithDoubles(33550, 1d, 1d, 0d)
            .WithDoubles(33922, 0d, 0d, 0d, [withheld], [withheld], 0d)
            .WithAscii(42113, "-32768")
            .WithGeoKeyDirectory(
                (1024, 0, 1, 1),
                (1025, 0, 1, 1),
                (3072, 0, 1, 26915),
                (3076, 0, 1, 9001))
            .Build();

    /// <summary>
    /// Builds a responder that discriminates on the request's <c>outputFormat</c> query value: an
    /// <c>AAIGrid</c> request always gets <paramref name="aaiGridText"/>; a <c>GTiff</c> request always gets
    /// a 200 GeoTIFF response built from <paramref name="tiffBytes"/>. Mirrors
    /// <see cref="OpenTopographyUsgs1mSourceTests"/>'s own private hybrid responder, rebuilt here because that
    /// one is private to its own test class.
    /// </summary>
    private static Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> HybridResponder(string aaiGridText, byte[] tiffBytes) =>
        (request, _) => GetQueryValue(request, "outputFormat") == "GTiff"
            ? BinaryResponse(HttpStatusCode.OK, tiffBytes, "image/tiff")
            : TextResponse(HttpStatusCode.OK, aaiGridText);

    private static Task<HttpResponseMessage> BinaryResponse(HttpStatusCode statusCode, byte[] bytes, string contentType)
    {
        HttpResponseMessage response = new(statusCode) { Content = new ByteArrayContent(bytes) };
        response.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        return Task.FromResult(response);
    }

    private static string? GetQueryValue(HttpRequestMessage request, string name)
    {
        string query = request.RequestUri!.Query.TrimStart('?');
        foreach (string pair in query.Split('&'))
        {
            int equalsIndex = pair.IndexOf('=');
            string key = equalsIndex < 0 ? pair : pair[..equalsIndex];
            if (key == name)
            {
                return equalsIndex < 0 ? null : pair[(equalsIndex + 1)..];
            }
        }

        return null;
    }
}
