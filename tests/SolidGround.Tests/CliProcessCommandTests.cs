using System.Globalization;
using System.Text.Json;
using SolidGround.Cli;
using SolidGround.Core.Exports;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// The offline `process` command: parsing, vertical-reference precedence, AOI clipping, overwrite/base-name
/// rules, local origin, and units. See docs/architecture/cli-workflow.md's "Commands", "AOI and clip
/// derivation", "Local origin selection and its consequences", and "Units" sections. Every test here uses an
/// offline host whose HTTP factory throws if invoked, since `process` never performs network I/O.
/// </summary>
public sealed class CliProcessCommandTests
{
    // A bare (non-compound) PROJCS with no VERT_CS, taken verbatim from inside example-site-synthetic.prj's own
    // COMPD_CS wrapper, so it describes the identical EPSG:26915 horizontal reference without a vertical one.
    private const string NonCompoundPrjText = """
        PROJCS["NAD_1983_UTM_Zone_15N",
            GEOGCS["GCS_North_American_1983",
                DATUM["NAD83",
                    SPHEROID["GRS_1980",6378137.0,298.257222101]],
                PRIMEM["Greenwich",0.0],
                UNIT["Degree",0.0174532925199433]],
            PROJECTION["Transverse_Mercator"],
            PARAMETER["False_Easting",500000.0],
            PARAMETER["False_Northing",0.0],
            PARAMETER["Central_Meridian",-93.0],
            PARAMETER["Scale_Factor",0.9996],
            PARAMETER["Latitude_Of_Origin",0.0],
            UNIT["Meter",1.0],
            AUTHORITY["EPSG","26915"]]
        """;

    // A well-formed `.source.json` sidecar, shaped exactly like RasterSourceSidecarIo.Write's own output, so
    // each strict-reader test below can mutate exactly one property and know the rest of the document is
    // valid. See docs/architecture/cli-workflow.md's "Raster set persistence" section for this fixed shape.
    private const string ValidSourceJson = """
        {
          "schema": "solidground.raster-source",
          "schemaVersion": 3,
          "sourceName": "OpenTopography",
          "datasetIdentifier": "USGS1m",
          "collectionPeriod": {
            "start": "2024-05-01",
            "end": "2024-05-02"
          },
          "qualityLevel": "QL2",
          "vertical": {
            "datum": "NAVD88",
            "unit": "UsSurveyFoot",
            "geoidModel": "Geoid12B"
          },
          "horizontalReferenceOrigin": "SourceResponse",
          "verticalReferenceOrigin": "SourceResponse",
          "acquisition": {
            "redactedRequestUri": "https://example.test/api?API_Key=REDACTED",
            "statusCode": 200,
            "contentType": "application/zip",
            "contentDispositionFileName": "USGS1m.zip",
            "archiveEntryNames": ["USGS1m.asc", "USGS1m.prj"],
            "referenceSource": "PrjSidecar",
            "responseByteCount": 12345,
            "metadataRequest": null,
            "fetchEnvelope": {
              "west": -93.6045,
              "south": 41.5906,
              "east": -93.6031,
              "north": 41.5917,
              "minimumSideMeters": 110.0,
              "expanded": false,
              "widthBeforeMeters": 121.8,
              "heightBeforeMeters": 154.8,
              "widthAfterMeters": 121.8,
              "heightAfterMeters": 154.8
            }
          }
        }
        """;

    [Fact]
    public async Task ProcessesTheCommittedFixtureEndToEndAndWritesABundle()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", tempDirectory.FullName],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);

            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            string pointsPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix);
            Assert.True(File.Exists(documentPath));
            Assert.True(File.Exists(pointsPath));

            TerrainExportPayload payload = TerrainExportBundleReader.Read(File.ReadAllBytes(documentPath), File.ReadAllBytes(pointsPath));
            Assert.True(payload.Provenance.RetainedPointCount > 0);
            Assert.True(payload.Provenance.OriginalPointCount > 0);
            Assert.True(payload.Provenance.OriginalPointCount <= 9);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task DefaultsThePrjPathToTheAscsSiblingFile()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string ascPath = Path.Combine(tempDirectory.FullName, "foo.asc");
            string prjPath = Path.Combine(tempDirectory.FullName, "foo.prj");
            File.Copy(FixturePath("example-site-synthetic.asc"), ascPath);
            File.Copy(FixturePath("example-site-synthetic.prj"), prjPath);

            (int exitCode, _, string stderr) = await RunAsync(["process", "--asc", ascPath, "--output", tempDirectory.FullName], TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RequiresVerticalDatumAndUnitForANonCompoundPrjWithNoSidecar()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string prjPath = Path.Combine(tempDirectory.FullName, "non-compound.prj");
            File.WriteAllText(prjPath, NonCompoundPrjText);

            (int exitCode, _, string stderr) = await RunAsync(
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", prjPath, "--output", tempDirectory.FullName],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("--vertical-datum", stderr, StringComparison.Ordinal);
            Assert.Contains("--vertical-unit", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitVerticalOptionsOverrideACompoundPrjsOwnVertical()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--vertical-datum", "Other", "--vertical-unit", "meter", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            string datum = document.RootElement.GetProperty("provenance").GetProperty("sourceVerticalReference").GetProperty("datum").GetString()!;
            Assert.Equal("Other", datum);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CollectionStartAndEndParseIdenticallyRegardlessOfTheCurrentCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");

            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--collection-start", "2024-05-01", "--collection-end", "2024-05-02", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            JsonElement collectionPeriod = document.RootElement.GetProperty("provenance").GetProperty("source").GetProperty("collectionPeriod");
            Assert.Equal("2024-05-01", collectionPeriod.GetProperty("start").GetString());
            Assert.Equal("2024-05-02", collectionPeriod.GetProperty("end").GetString());
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ClipsToABoundingBoxAoi()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            // Covers only the grid's westernmost column (cell centers near x=449674.5); the eastern boundary
            // -93.603810 is the WGS84 longitude of the vertical line x=449675 (the column 0/1 cell edge).
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--bbox", "-93.603854,41.590998,-93.603810,41.591218", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            int originalPointCount = document.RootElement.GetProperty("provenance").GetProperty("originalPointCount").GetInt32();
            Assert.True(originalPointCount > 0);
            Assert.True(originalPointCount < 9);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ClipsToARadiusAoi()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--center", "41.591194,-93.603806", "--radius", "5", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            int originalPointCount = document.RootElement.GetProperty("provenance").GetProperty("originalPointCount").GetInt32();
            Assert.True(originalPointCount > 0);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ClipsToAParcelAoiWithABuffer()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--parcel", FixturePath("example-site-synthetic-parcel.geojson"), "--buffer", "100", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            int originalPointCount = document.RootElement.GetProperty("provenance").GetProperty("originalPointCount").GetInt32();

            // Reproduces TerrainExportGoldenFileTests.RunRealParcelPipelineAsync's own 8-original-point result
            // for the identical fixture inputs and buffer.
            Assert.Equal(8, originalPointCount);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ABlankParcelFileFailsAfterTheAscReadIsAlreadyReportedOnStdout()
    {
        // Regression test for the stdout-ordering fix in commit ef5aa53: AoiSelection.Bind/BindParcel applies
        // zero content validation to a `--parcel` file's raw text (only ParcelGeometryAoi's constructor, built
        // from ProcessCommand.RunAsync's `aoi?.ToAreaOfInterest(...)` call immediately before the pipeline
        // call, rejects blank geometry). That construction must stay placed after the `--asc` file is read and
        // reported on stdout, not hoisted back up next to where `aoi` is parsed -- otherwise this exact
        // ordering silently regresses with no other test catching it.
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string ascPath = FixturePath("example-site-synthetic.asc");
            string blankParcelPath = Path.Combine(tempDirectory.FullName, "blank-parcel.geojson");
            File.WriteAllText(blankParcelPath, "   ");

            (int exitCode, string stdout, string stderr) = await RunAsync(
                [
                    "process", "--asc", ascPath, "--prj", FixturePath("example-site-synthetic.prj"),
                    "--parcel", blankParcelPath, "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("Parcel geometry is required", stderr, StringComparison.Ordinal);
            Assert.Contains($"process: read '{ascPath}'.", stdout, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAnEmptyClipWithASourceQualityExitCode()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            // ~5.5 km north of the fixture grid: comfortably outside the tiny 3x3 metre grid, still well
            // inside valid, non-singular UTM 15N territory.
            (int exitCode, _, string stderr) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--bbox", "-93.6045,41.6405,-93.6031,41.6419", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.SourceQuality, exitCode);
            Assert.Contains("error (source-quality):", stderr, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFileSystemEntries(tempDirectory.FullName));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RefusesToOverwriteAnExistingBundleWithoutTheFlag()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string[] args =
            [
                "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", tempDirectory.FullName,
            ];
            (int firstExitCode, _, _) = await RunAsync(args, TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, firstExitCode);

            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            string pointsPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix);
            byte[] documentBytesBefore = File.ReadAllBytes(documentPath);
            byte[] pointsBytesBefore = File.ReadAllBytes(pointsPath);

            (int secondExitCode, _, string stderr) = await RunAsync(args, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, secondExitCode);
            Assert.Contains("--overwrite", stderr, StringComparison.Ordinal);
            Assert.Equal(documentBytesBefore, File.ReadAllBytes(documentPath));
            Assert.Equal(pointsBytesBefore, File.ReadAllBytes(pointsPath));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task OverwritesWithTheFlag()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string[] firstArgs =
            [
                "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", tempDirectory.FullName,
            ];
            (int firstExitCode, _, _) = await RunAsync(firstArgs, TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, firstExitCode);

            string[] secondArgs = [.. firstArgs, "--overwrite"];
            (int secondExitCode, _, _) = await RunAsync(secondArgs, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, secondExitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAnExistingBaseNameEarlyBeforeAnyProcessing()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix);
            string pointsPath = Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix);
            File.WriteAllText(documentPath, "pre-existing");

            (int exitCode, _, string stderr) = await RunAsync(
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", tempDirectory.FullName],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("--overwrite", stderr, StringComparison.Ordinal);
            Assert.Equal("pre-existing", File.ReadAllText(documentPath));
            Assert.False(File.Exists(pointsPath));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task RejectsAnUnwritableOutputDirectoryWithAProcessingExitCode()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string outputAsAFile = Path.Combine(tempDirectory.FullName, "not-a-directory");
            File.WriteAllText(outputAsAFile, "occupies the output path");

            (int exitCode, _, string stderr) = await RunAsync(
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", outputAsAFile],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Processing, exitCode);
            Assert.Contains("error (processing):", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task SouthwestOriginPlacesTheGridsCornerAtTheLocalOrigin()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--unit", "meter", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);

            (double X, double Y, double Elevation)[] points = ReadPoints(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix));
            double minX = points.Min(point => point.X);
            double minY = points.Min(point => point.Y);

            // Cell size is exactly 1 metre and --unit meter means one cell size in output units is 1.0.
            Assert.True(minX is >= 0d and < 1d);
            Assert.True(minY is >= 0d and < 1d);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CentroidOriginCentersTheLocalCoordinates()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--origin", "centroid", "--unit", "meter", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);

            (double X, double Y, double Elevation)[] points = ReadPoints(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix));

            Assert.True(points.Min(point => point.X) < 0d);
            Assert.True(points.Max(point => point.X) > 0d);
            Assert.True(points.Min(point => point.Y) < 0d);
            Assert.True(points.Max(point => point.Y) > 0d);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ExplicitOriginIsUsedWithNoSnapping()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--origin", "449674.25,4604563.75,183.5", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            JsonElement origin = document.RootElement.GetProperty("provenance").GetProperty("localFrame").GetProperty("origin");
            Assert.Equal(449674.25d, origin.GetProperty("x").GetDouble());
            Assert.Equal(4604563.75d, origin.GetProperty("y").GetDouble());
            Assert.Equal(183.5d, origin.GetProperty("elevation").GetDouble());
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task BudgetAndMethodAreHonored()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--budget", "3", "--method", "uniform", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            JsonElement provenance = document.RootElement.GetProperty("provenance");
            Assert.True(provenance.GetProperty("retainedPointCount").GetInt32() <= 3);
            Assert.Equal("UniformSampler", provenance.GetProperty("simplification").GetProperty("method").GetString());
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CoverageFloorIsAcceptedAndPrintedButNeverRecordedInTheDocument()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, string stdout, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--coverage-floor", "0.5", "--verbose", "--output", tempDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Contains("0.5", stdout, StringComparison.Ordinal);

            string documentText = File.ReadAllText(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix));
            Assert.DoesNotContain("coverageFloor", documentText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task UnitOptionChangesThePointsFileScaleButNotTheOrigin()
    {
        DirectoryInfo usSurveyFootDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo meterDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int usSurveyFootExitCode, _, _) = await RunAsync(
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", usSurveyFootDirectory.FullName],
                TestContext.Current.CancellationToken);
            (int meterExitCode, _, _) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--unit", "meter", "--output", meterDirectory.FullName,
                ],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, usSurveyFootExitCode);
            Assert.Equal(CliExitCodes.Success, meterExitCode);

            (double X, double Y, double Elevation)[] usSurveyFootPoints =
                ReadPoints(Path.Combine(usSurveyFootDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix));
            (double X, double Y, double Elevation)[] meterPoints =
                ReadPoints(Path.Combine(meterDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix));

            Assert.Equal(usSurveyFootPoints.Length, meterPoints.Length);

            // LengthConverter.Convert(delta, Meter, UsSurveyFoot) = delta * 1 / (1200/3937) = delta * 3937/1200.
            double expectedRatio = 3937d / 1200d;
            for (int index = 0; index < usSurveyFootPoints.Length; index++)
            {
                AssertWithinRelativeTolerance(expectedRatio, usSurveyFootPoints[index].X / meterPoints[index].X);
                AssertWithinRelativeTolerance(expectedRatio, usSurveyFootPoints[index].Y / meterPoints[index].Y);
            }

            using JsonDocument usSurveyFootDocument = ReadDocument(usSurveyFootDirectory.FullName, "terrain");
            using JsonDocument meterDocument = ReadDocument(meterDirectory.FullName, "terrain");
            JsonElement usSurveyFootOrigin = usSurveyFootDocument.RootElement.GetProperty("provenance").GetProperty("localFrame").GetProperty("origin");
            JsonElement meterOrigin = meterDocument.RootElement.GetProperty("provenance").GetProperty("localFrame").GetProperty("origin");
            Assert.Equal(meterOrigin.GetProperty("x").GetDouble(), usSurveyFootOrigin.GetProperty("x").GetDouble());
            Assert.Equal(meterOrigin.GetProperty("y").GetDouble(), usSurveyFootOrigin.GetProperty("y").GetDouble());
        }
        finally
        {
            Directory.Delete(usSurveyFootDirectory.FullName, recursive: true);
            Directory.Delete(meterDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task NoUnitOptionWritesLengthConverterDefaultOutputUnitToTheLocalFrame()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, _) = await RunAsync(
                ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", tempDirectory.FullName],
                TestContext.Current.CancellationToken);
            Assert.Equal(CliExitCodes.Success, exitCode);

            using JsonDocument document = ReadDocument(tempDirectory.FullName, "terrain");
            string outputUnit = document.RootElement.GetProperty("provenance").GetProperty("localFrame").GetProperty("outputUnit").GetString()!;
            Assert.Equal(LengthConverter.DefaultOutputUnit.ToString(), outputUnit);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    // ---- --source-json strict validation (RasterSourceSidecarIo.Read rejects every malformed class) -----

    [Fact]
    public async Task SourceJsonWithAnExtraPropertyExitsWithUsageErrorNamingTheSidecarPath()
    {
        string json = ValidSourceJson.Replace(
            "\"qualityLevel\": \"QL2\",", "\"qualityLevel\": \"QL2\", \"zzzSentinelExtra\": \"zzz-should-not-appear\",", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, "zzz-should-not-appear", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithAMissingPropertyExitsWithUsageErrorNamingTheSidecarPath()
    {
        string json = ValidSourceJson.Replace("\"datasetIdentifier\": \"USGS1m\",", "", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, expectedAbsentText: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithADuplicatePropertyExitsWithUsageErrorNamingTheSidecarPath()
    {
        string json = ValidSourceJson.Replace(
            "\"sourceName\": \"OpenTopography\",",
            "\"sourceName\": \"OpenTopography\", \"sourceName\": \"zzz-duplicate-should-not-appear\",",
            StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, "zzz-duplicate-should-not-appear", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithSchemaVersionFourExitsWithUsageErrorNamingTheSidecarPath()
    {
        // An unsupported future schemaVersion (newer than RasterSourceSidecarIo.CurrentSchemaVersion) is
        // rejected the same way any other wrong schemaVersion is.
        string json = ValidSourceJson.Replace("\"schemaVersion\": 3,", "\"schemaVersion\": 4,", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, expectedAbsentText: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithSchemaVersionTwoExitsWithUsageErrorNamingTheSidecarPath()
    {
        // A genuine version 2 sidecar (from before SolidGround Issue #23's fetchEnvelope addition) is now
        // rejected as an old version, exactly like version 1 below -- there is no migration path from version
        // 2 to version 3.
        string json = ValidSourceJson.Replace("\"schemaVersion\": 3,", "\"schemaVersion\": 2,", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, expectedAbsentText: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithSchemaVersionOneExitsWithUsageErrorNamingTheSidecarPath()
    {
        // A genuine version 1 sidecar (from before SolidGround Issue #21) is rejected the same way any other
        // wrong schemaVersion is -- there is no migration path from version 1 to version 3.
        string json = ValidSourceJson.Replace("\"schemaVersion\": 3,", "\"schemaVersion\": 1,", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, expectedAbsentText: null, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithANonStringSourceNameExitsWithUsageErrorNamingTheSidecarPath()
    {
        string json = ValidSourceJson.Replace("\"sourceName\": \"OpenTopography\",", "\"sourceName\": 424242,", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, "424242", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithAnUnrecognizedVerticalUnitExitsWithUsageErrorNamingTheSidecarPath()
    {
        string json = ValidSourceJson.Replace("\"unit\": \"UsSurveyFoot\",", "\"unit\": \"Furlongs\",", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, "Furlongs", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SourceJsonWithAMalformedCollectionStartDateExitsWithUsageErrorNamingTheSidecarPath()
    {
        string json = ValidSourceJson.Replace("\"start\": \"2024-05-01\",", "\"start\": \"not-a-date\",", StringComparison.Ordinal);
        await AssertSourceJsonRejectedAsync(json, "not-a-date", TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Writes <paramref name="sidecarJson"/> beside the committed fixture, points <c>--source-json</c> at it
    /// explicitly, and asserts `process` exits 2 with a stderr line naming the sidecar's own path. When
    /// <paramref name="expectedAbsentText"/> is given, also asserts that raw text never reaches stderr --
    /// RasterSourceSidecarIo.Read never echoes file content, only the sidecar path and a structural JSON
    /// pointer (docs/architecture/cli-workflow.md's "Raster set persistence" section).
    /// </summary>
    private static async Task AssertSourceJsonRejectedAsync(string sidecarJson, string? expectedAbsentText, CancellationToken cancellationToken)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string sidecarPath = Path.Combine(tempDirectory.FullName, "terrain.source.json");
            File.WriteAllText(sidecarPath, sidecarJson);

            (int exitCode, _, string stderr) = await RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--source-json", sidecarPath, "--output", tempDirectory.FullName,
                ],
                cancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
            Assert.Contains(sidecarPath, stderr, StringComparison.Ordinal);
            if (expectedAbsentText is not null)
            {
                Assert.DoesNotContain(expectedAbsentText, stderr, StringComparison.Ordinal);
            }

            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.PointsFileSuffix)));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(
            _ => null,
            () => throw new InvalidOperationException("process must never perform an HTTP call."),
            stdout,
            stderr);

        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    private static JsonDocument ReadDocument(string directory, string baseName) =>
        JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, baseName + TerrainExportBundleRenderer.DocumentFileSuffix)));

    private static (double X, double Y, double Elevation)[] ReadPoints(string path)
    {
        string[] lines = File.ReadAllLines(path);
        var points = new (double X, double Y, double Elevation)[lines.Length];
        for (int index = 0; index < lines.Length; index++)
        {
            string[] parts = lines[index].Split(',');
            points[index] = (
                double.Parse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture),
                double.Parse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture),
                double.Parse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture));
        }

        return points;
    }

    private static void AssertWithinRelativeTolerance(double expected, double actual, double relativeTolerance = 1e-9)
    {
        double relativeError = Math.Abs(actual - expected) / Math.Abs(expected);
        Assert.True(
            relativeError <= relativeTolerance,
            $"Expected a ratio within {relativeTolerance.ToString("G17", CultureInfo.InvariantCulture)} of " +
            $"{expected.ToString("G17", CultureInfo.InvariantCulture)}, but observed {actual.ToString("G17", CultureInfo.InvariantCulture)}.");
    }
}
