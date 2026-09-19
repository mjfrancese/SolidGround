using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class FileSystemTerrainExporterTests
{
    [Fact]
    public async Task ExportAsyncWritesBothFilesWithBytesIdenticalToTheRendererAndReturnsTheFullDocumentPathAsReceipt()
    {
        string outputDirectory = CreateUniqueTempDirectoryPath();
        try
        {
            TerrainExportPayload payload = CreatePayload();
            TerrainExportBundle expectedBundle = TerrainExportBundleRenderer.Render(payload, "fs-export-test");
            FileSystemTerrainExporter exporter = new(outputDirectory, "fs-export-test");

            TerrainExportReceipt receipt = await exporter.ExportAsync(payload, TestContext.Current.CancellationToken);

            string expectedDocumentPath = Path.GetFullPath(Path.Combine(outputDirectory, expectedBundle.DocumentFileName));
            Assert.Equal(expectedDocumentPath, receipt.DestinationIdentifier);

            byte[] writtenDocumentBytes = await File.ReadAllBytesAsync(
                Path.Combine(outputDirectory, expectedBundle.DocumentFileName), TestContext.Current.CancellationToken);
            byte[] writtenPointsBytes = await File.ReadAllBytesAsync(
                Path.Combine(outputDirectory, expectedBundle.PointsFileName), TestContext.Current.CancellationToken);

            Assert.Equal(expectedBundle.DocumentBytes.ToArray(), writtenDocumentBytes);
            Assert.Equal(expectedBundle.PointsBytes.ToArray(), writtenPointsBytes);
        }
        finally
        {
            DeleteDirectoryIfExists(outputDirectory);
        }
    }

    [Fact]
    public async Task ExportAsyncCreatesTheOutputDirectoryWhenItDoesNotAlreadyExist()
    {
        string outputDirectory = CreateUniqueTempDirectoryPath();
        Assert.False(Directory.Exists(outputDirectory));
        try
        {
            TerrainExportPayload payload = CreatePayload();
            FileSystemTerrainExporter exporter = new(outputDirectory, "fs-export-missing-dir");

            await exporter.ExportAsync(payload, TestContext.Current.CancellationToken);

            Assert.True(Directory.Exists(outputDirectory));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "fs-export-missing-dir.solidground.json")));
            Assert.True(File.Exists(Path.Combine(outputDirectory, "fs-export-missing-dir.points.csv")));
        }
        finally
        {
            DeleteDirectoryIfExists(outputDirectory);
        }
    }

    [Fact]
    public async Task ExportAsyncWrapsAFailureToCreateTheOutputDirectoryInATerrainExportException()
    {
        // A path already occupied by a file (not a directory) makes Directory.CreateDirectory throw a raw
        // IOException; ExportAsync must wrap it in TerrainExportException like every other I/O failure here.
        string outputPath = CreateUniqueTempDirectoryPath();
        await File.WriteAllBytesAsync(outputPath, "not a directory"u8.ToArray(), TestContext.Current.CancellationToken);
        try
        {
            TerrainExportPayload payload = CreatePayload();
            FileSystemTerrainExporter exporter = new(outputPath, "fs-export-blocked-directory");

            TerrainExportException exception = await Assert.ThrowsAsync<TerrainExportException>(
                () => exporter.ExportAsync(payload, TestContext.Current.CancellationToken).AsTask());

            Assert.Contains(outputPath, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task ExportAsyncOverwritesExistingFilesSilently()
    {
        string outputDirectory = CreateUniqueTempDirectoryPath();
        try
        {
            Directory.CreateDirectory(outputDirectory);
            string documentPath = Path.Combine(outputDirectory, "fs-export-overwrite.solidground.json");
            string pointsPath = Path.Combine(outputDirectory, "fs-export-overwrite.points.csv");
            await File.WriteAllBytesAsync(documentPath, "stale document"u8.ToArray(), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(pointsPath, "stale points"u8.ToArray(), TestContext.Current.CancellationToken);

            TerrainExportPayload payload = CreatePayload();
            TerrainExportBundle expectedBundle = TerrainExportBundleRenderer.Render(payload, "fs-export-overwrite");
            FileSystemTerrainExporter exporter = new(outputDirectory, "fs-export-overwrite");

            await exporter.ExportAsync(payload, TestContext.Current.CancellationToken);

            byte[] writtenDocumentBytes = await File.ReadAllBytesAsync(documentPath, TestContext.Current.CancellationToken);
            byte[] writtenPointsBytes = await File.ReadAllBytesAsync(pointsPath, TestContext.Current.CancellationToken);
            Assert.Equal(expectedBundle.DocumentBytes.ToArray(), writtenDocumentBytes);
            Assert.Equal(expectedBundle.PointsBytes.ToArray(), writtenPointsBytes);
        }
        finally
        {
            DeleteDirectoryIfExists(outputDirectory);
        }
    }

    [Fact]
    public async Task ExportAsyncWithAnAlreadyCanceledTokenThrowsAndWritesNothing()
    {
        string outputDirectory = CreateUniqueTempDirectoryPath();
        try
        {
            TerrainExportPayload payload = CreatePayload();
            FileSystemTerrainExporter exporter = new(outputDirectory, "fs-export-canceled");
            using CancellationTokenSource cts = new();
            cts.Cancel();

            // This test deliberately controls its own cancellation token (it must already be canceled) rather
            // than using TestContext.Current.CancellationToken, so xUnit1051 does not apply here.
#pragma warning disable xUnit1051
            await Assert.ThrowsAsync<OperationCanceledException>(() => exporter.ExportAsync(payload, cts.Token).AsTask());
#pragma warning restore xUnit1051

            Assert.False(Directory.Exists(outputDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(outputDirectory);
        }
    }

    private static string CreateUniqueTempDirectoryPath() =>
        Path.Combine(Path.GetTempPath(), "solidground-tests-" + Guid.NewGuid().ToString("N"));

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static TerrainExportPayload CreatePayload()
    {
        VerticalReference vertical = VerticalReference();
        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            new ElevationSourceMetadata("OpenTopography", "USGS1m"),
            Transformation(),
            vertical,
            new LocalCoordinateFrame(new Coordinate3D(10d, 20d, 30d), ProjectedReference(), vertical, LengthUnit.UsSurveyFoot),
            new SimplificationRequest(15000, SimplificationMethod.CurvatureAware),
            2,
            2,
            new ElevationRange(1d, 3d, LengthUnit.Meter));

        return new TerrainExportPayload(
            [
                new LocalTerrainSample(new LocalCoordinate(1d, 2d, 3d)),
                new LocalTerrainSample(new LocalCoordinate(4d, 5d, 6d)),
            ],
            provenance);
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference() => new("NAVD88", LengthUnit.Meter, "Geoid12B");

    private static HorizontalTransformationDefinition Transformation() => new(
        GeographicReference(),
        ProjectedReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "candidate-engine",
        "1.0");
}
