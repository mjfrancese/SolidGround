using System.Globalization;
using SolidGround.Cli.Options;
using SolidGround.Cli.Processing;
using SolidGround.Cli.Rasters;
using SolidGround.Cli.Secrets;
using SolidGround.Core.Aois;
using SolidGround.Core.Exports;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Commands;

/// <summary>
/// The online `run` command: acquires exactly as `fetch` does, then processes the acquired grid exactly as
/// `process` does (the same <see cref="TerrainProcessingPipeline"/> call), writing the export bundle and,
/// with <c>--save-raster</c>, the raster set too. See docs/architecture/cli-workflow.md's "Commands" section.
/// </summary>
internal static class RunCommand
{
    internal static async Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(host);

        // --output is validated up front, before any file read, key check, or pipeline work -- see
        // docs/architecture/cli-workflow.md's "Options and defaults" section.
        string outputDirectory = invocation.GetValue("output")!;
        ProcessCommand.ValidateOutputDirectory(outputDirectory);

        AoiSelection aoi = AoiSelection.Bind(invocation, OptionTable.Run, required: true)!;
        LocalOriginSelection origin = ProcessCommand.ParseOrigin(invocation);
        LengthUnit outputUnit = ProcessCommand.ParseLengthUnitValue("unit", invocation.GetValue("unit") ?? "us-survey-foot");
        SimplificationMethod method = ProcessCommand.ParseMethod(invocation);
        int budget = ProcessCommand.ParseBudget(invocation);
        double coverageFloor = ProcessCommand.ParseCoverageFloor(invocation);
        CollectionPeriod? cliCollectionPeriod = ProcessCommand.ParseCollectionPeriod(invocation);
        string? cliQualityLevel = invocation.GetValue("quality-level");

        string baseName = invocation.GetValue("name") ?? "terrain";
        ProcessCommand.ValidateBaseName(baseName);
        bool overwrite = invocation.HasOption("overwrite");
        bool saveRaster = invocation.HasOption("save-raster");
        bool verbose = invocation.HasOption("verbose");
        int timeoutSeconds = FetchCommand.ParseTimeout(invocation);

        string documentPath = Path.Combine(outputDirectory, baseName + TerrainExportBundleRenderer.DocumentFileSuffix);
        string pointsPath = Path.Combine(outputDirectory, baseName + TerrainExportBundleRenderer.PointsFileSuffix);
        RasterSetPaths rasterPaths = RasterSetIo.ResolvePaths(outputDirectory, baseName);
        bool alreadyExists = File.Exists(documentPath) || File.Exists(pointsPath) || (saveRaster && RasterSetIo.Exists(rasterPaths));
        if (!overwrite && alreadyExists)
        {
            throw new CliUsageException(
                $"'{documentPath}' or its raster set already exists in '{outputDirectory}'; pass --overwrite to replace it.");
        }

        CliOpenTopographyApiKeyProvider keyProvider = new(host.GetEnvironmentVariable);
        OpenTopographyApiKey? key = keyProvider.GetApiKey();
        if (key is null)
        {
            host.StandardError.WriteLine(FetchCommand.MissingApiKeyMessage);
            return CliExitCodes.Authorization;
        }

        using HttpClient httpClient = new(host.HttpMessageHandlerFactory(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        OpenTopographyUsgs1mSource source = new(httpClient, new StaticOpenTopographyApiKeyProvider(key));

        HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
        Wgs84BoundingBoxAoi fetchEnvelope = ClipRegionFactory.BuildFetchEnvelope(aoi, wgs84Reference);

        host.StandardOutput.WriteLine("run: requesting OpenTopography...");
        OpenTopographyUsgs1mAcquisition acquisition = await source.AcquireDetailedAsync(
            new ElevationSourceRequest(fetchEnvelope), cancellationToken).ConfigureAwait(false);

        if (verbose)
        {
            FetchCommand.PrintAcquisitionEvidence(host, "run", acquisition);
        }

        FetchCommand.PrintReferenceLine(
            host, "run",
            acquisition.Acquisition.Data.HorizontalReference, acquisition.Evidence.HorizontalReferenceOrigin,
            acquisition.Acquisition.Data.VerticalReference, acquisition.Evidence.VerticalReferenceOrigin);

        ElevationGrid grid = (ElevationGrid)acquisition.Acquisition.Data;
        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, acquisition.Evidence.WellKnownText);

        // Step 2 of the shared pipeline (docs/architecture/cli-workflow.md's "AOI and clip derivation"
        // section): confirm the coordinate reference the fetched grid actually reports agrees with the one
        // the transform was built from. Both are parsed from the identical acquisition text, so this is
        // expected to always hold; a mismatch is a processing error, never a silent wrong-reference clip.
        if (transform.Definition.TargetReference != grid.HorizontalReference)
        {
            throw new CliProcessingException(
                "The fetched grid's horizontal reference does not match the coordinate reference the WGS 84 " +
                $"transform was built from: transform target '{transform.Definition.TargetReference}' versus grid " +
                $"reference '{grid.HorizontalReference}'.");
        }

        ElevationSourceMetadata sourceMetadata = new(
            acquisition.Acquisition.Source.SourceName,
            acquisition.Acquisition.Source.DatasetIdentifier,
            cliCollectionPeriod ?? acquisition.Acquisition.Source.CollectionPeriod,
            cliQualityLevel ?? acquisition.Acquisition.Source.QualityLevel);

        ReferenceOrigins referenceOrigins = new(acquisition.Evidence.HorizontalReferenceOrigin, acquisition.Evidence.VerticalReferenceOrigin);
        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid, transform, grid.VerticalReference, referenceOrigins, sourceMetadata, aoi, origin, outputUnit, method, budget, coverageFloor, cancellationToken)
            .ConfigureAwait(false);

        ProcessCommand.PrintClipStage(host, "run", verbose, aoi, grid, outcome.ClipResult);
        host.StandardOutput.WriteLine(
            $"run: simplified to {outcome.Payload.Provenance.RetainedPointCount.ToString(CultureInfo.InvariantCulture)} of " +
            $"{outcome.Payload.Provenance.OriginalPointCount.ToString(CultureInfo.InvariantCulture)} points ({method}).");
        if (verbose && outcome.SimplificationDiagnostics is { } diagnostics)
        {
            ProcessCommand.PrintSimplificationDiagnostics(host, "run", diagnostics);
        }

        host.StandardOutput.WriteLine($"run: coverage floor {coverageFloor.ToString("R", CultureInfo.InvariantCulture)}.");

        FileSystemTerrainExporter exporter;
        try
        {
            exporter = new FileSystemTerrainExporter(outputDirectory, baseName);
        }
        catch (ArgumentException ex)
        {
            // Defensive: --output and --name are already validated above, so this should never fire; never
            // ex.Message regardless -- mirrors ProcessCommand's identical catch, per docs/architecture/
            // cli-workflow.md's "Diagnostics and redaction" section.
            throw new CliUsageException("--output or --name is invalid.", ex);
        }

        TerrainExportReceipt receipt = await exporter.ExportAsync(outcome.Payload, cancellationToken).ConfigureAwait(false);
        host.StandardOutput.WriteLine($"run: wrote '{receipt.DestinationIdentifier}'.");

        if (saveRaster)
        {
            RasterSourceSidecar sidecar = FetchCommand.BuildSidecar(acquisition);
            await RasterSetIo.WriteAsync(rasterPaths, grid, acquisition.Evidence.WellKnownText, sidecar, cancellationToken).ConfigureAwait(false);
            host.StandardOutput.WriteLine($"run: wrote '{rasterPaths.GridPath}', '{rasterPaths.ReferencePath}', '{rasterPaths.SourcePath}'.");
        }

        if (verbose)
        {
            ProcessCommand.PrintProvenanceSummary(host, "run", outcome.Payload.Provenance);
        }

        return CliExitCodes.Success;
    }
}
