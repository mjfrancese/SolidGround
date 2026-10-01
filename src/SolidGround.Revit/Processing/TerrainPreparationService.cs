using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Rasters;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using SolidGround.Core.Workflow;
using SolidGround.Revit.Diagnostics;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Processing;

/// <summary>
/// Performs the managed, non-Revit portion of terrain preparation for both the ground preview and final
/// creation. The caller is responsible for validating document/settings state and for running this work away
/// from the Revit API thread.
/// </summary>
internal static class TerrainPreparationService
{
    private static readonly Uri OpenTopographyReachabilityEndpoint = new("https://portal.opentopography.org/apidocs/openapi.json");

    /// <summary>
    /// Mirrors <c>SolidGround.Cli.Rasters.RasterSetIo.SourceFileExtension</c>'s value: this project cannot
    /// reference the CLI assembly, so the one literal is duplicated here instead.
    /// </summary>
    private const string DefaultSourceJsonExtension = ".source.json";

    internal static async Task<PreparedTerrainSnapshot> PrepareAsync(
        TerrainRequestSettings request,
        HorizontalReference wgs84Reference,
        AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel,
        double terrainExtensionMeters,
        LinearDistance minimumLegalEdgeLength,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(wgs84Reference);
        ArgumentNullException.ThrowIfNull(aoi);

        return request.Mode == TerrainAcquisitionMode.Fetch
            ? await PrepareFetchAsync(request, wgs84Reference, aoi, addressParcel, terrainExtensionMeters, minimumLegalEdgeLength, cancellationToken).ConfigureAwait(false)
            : await PrepareProcessAsync(request, aoi, addressParcel, terrainExtensionMeters, minimumLegalEdgeLength, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<PreparedTerrainSnapshot> PrepareFetchAsync(
        TerrainRequestSettings request,
        HorizontalReference wgs84Reference,
        AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel,
        double terrainExtensionMeters,
        LinearDistance minimumLegalEdgeLength,
        CancellationToken cancellationToken)
    {
        // Source CRS metadata is unavailable until acquisition. For a legal parcel, union the terrain-only
        // projected buffers across every verified NAD83 UTM candidate; this conservative request encloses the
        // actual post-metadata terrain region without ever buffering the legal property-line geometry.
        Wgs84BoundingBoxAoi fetchEnvelope = aoi is ParcelGeometryAoi parcel
            ? ParcelFetchEnvelopePlanner.Build(parcel, LinearDistance.Meters(terrainExtensionMeters))
            : ClipRegionFactory.BuildFetchEnvelope(aoi).Envelope;

        using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(request.NetworkTimeoutSeconds) };
        string? reachabilityProblem = await ReachabilityProbe.ProbeAsync(
            httpClient,
            OpenTopographyReachabilityEndpoint,
            TimeSpan.FromSeconds(5),
            cancellationToken).ConfigureAwait(false);
        if (reachabilityProblem is not null)
        {
            throw new ReachabilityProbeException(
                $"The keyless OpenTopography reachability check failed before terrain fetch: {reachabilityProblem} Check network access and try again.");
        }

        OpenTopographyUsgs1mSource source = new(httpClient, SessionApiKeyOverrides.OpenTopographyProvider());

        OpenTopographyUsgs1mAcquisition acquisition;
        try
        {
            acquisition = await source.AcquireDetailedAsync(
                new ElevationSourceRequest(fetchEnvelope), cancellationToken).ConfigureAwait(false);
        }
        catch (OpenTopographyException ex)
        {
            // OpenTopographyException carries only a query-redacted URI. Logging that evidence keeps the
            // failure diagnosable without exposing an API key.
            AddInLog.Info($"Fetch mode acquisition request (failed): '{ex.RedactedRequestUri}'.");
            throw;
        }

        // The source's evidence URI is redacted before acquisition returns and is therefore safe for logs.
        AddInLog.Info($"Fetch mode acquisition request (succeeded): '{acquisition.Evidence.RedactedRequestUri}'.");

        ElevationGrid grid = (ElevationGrid)acquisition.Acquisition.Data;
        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText,
            acquisition.Evidence.WellKnownText);

        if (transform.Definition.TargetReference != grid.HorizontalReference)
        {
            throw new InvalidOperationException(
                "The fetched grid's horizontal reference does not match the coordinate reference the WGS 84 transform was built from.");
        }

        ElevationSourceMetadata sourceMetadata = new(
            acquisition.Acquisition.Source.SourceName,
            acquisition.Acquisition.Source.DatasetIdentifier,
            acquisition.Acquisition.Source.CollectionPeriod,
            acquisition.Acquisition.Source.QualityLevel,
            acquisition.Acquisition.Source.Attribution);
        ReferenceOrigins referenceOrigins = new(
            acquisition.Evidence.HorizontalReferenceOrigin,
            acquisition.Evidence.VerticalReferenceOrigin);

        ParcelExtentGeometry? parcelExtent = TerrainRunComposition.BuildParcelExtent(
            aoi,
            transform,
            LinearDistance.Meters(terrainExtensionMeters),
            minimumLegalEdgeLength);
        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid,
                transform,
                grid.VerticalReference,
                referenceOrigins,
                sourceMetadata,
                aoi,
                request.LocalOrigin,
                request.OutputUnit,
                request.Simplification.Method,
                request.Simplification.PointBudget,
                request.Simplification.CoverageFloorFraction,
                cancellationToken,
                addressParcel,
                parcelExtent)
            .ConfigureAwait(false);

        return CreateSnapshot(grid, outcome, transform);
    }

    private static async Task<PreparedTerrainSnapshot> PrepareProcessAsync(
        TerrainRequestSettings request,
        AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel,
        double terrainExtensionMeters,
        LinearDistance minimumLegalEdgeLength,
        CancellationToken cancellationToken)
    {
        ProcessInputSettings process = request.Process!;
        string ascPath = process.Asc;
        string prjPath = process.Prj ?? Path.ChangeExtension(ascPath, ".prj");
        string? explicitSourceJsonPath = process.SourceJson;
        string defaultSourceJsonPath = Path.ChangeExtension(ascPath, DefaultSourceJsonExtension);

        string ascText = ReadTextFile(ascPath);
        string prjText = ReadTextFile(prjPath);

        RasterSourceSidecar? sidecar = null;
        if (explicitSourceJsonPath is not null)
        {
            sidecar = RasterSourceSidecarIo.Read(ReadBytesFile(explicitSourceJsonPath), explicitSourceJsonPath);
        }
        else if (File.Exists(defaultSourceJsonPath))
        {
            sidecar = RasterSourceSidecarIo.Read(ReadBytesFile(defaultSourceJsonPath), defaultSourceJsonPath);
        }

        WellKnownTextReference parsedPrj = WellKnownTextReferenceParser.Parse(prjText);
        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText,
            prjText);

        VerticalReferenceResolution.ResolvedVerticalReference resolvedVertical;
        try
        {
            resolvedVertical = VerticalReferenceResolution.Resolve(
                process.VerticalDatum,
                process.VerticalUnit,
                process.Geoid,
                sidecar,
                parsedPrj.Vertical);
        }
        catch (FormatException)
        {
            // The Revit UI owns local-input guidance. Do not expose the CLI's JSON-path vocabulary here.
            throw new FormatException(
                "SolidGround could not determine this terrain's vertical reference. Open SolidGround Settings and set the terrain's vertical datum and vertical unit, provide a source sidecar, or use a compound .prj with a VERT_CS.");
        }

        VerticalReference verticalReference = resolvedVertical.Reference;

        ElevationGrid grid;
        using (StringReader ascReader = new(ascText))
        {
            grid = AaiGridParser.Parse(ascReader, transform.Definition.TargetReference, verticalReference);
        }

        string sourceName = process.SourceName ?? sidecar?.SourceName ?? "local-file";
        string datasetIdentifier = process.Dataset ?? sidecar?.DatasetIdentifier ?? Path.GetFileNameWithoutExtension(ascPath);
        CollectionPeriod? collectionPeriod = ParseCollectionPeriod(process) ?? sidecar?.CollectionPeriod;
        string? qualityLevel = process.QualityLevel ?? sidecar?.QualityLevel;
        ElevationSourceMetadata sourceMetadata = new(sourceName, datasetIdentifier, collectionPeriod, qualityLevel, sidecar?.Attribution);

        ReferenceOrigins referenceOrigins = new(ReferenceOrigin.Operator, resolvedVertical.Origin);

        ParcelExtentGeometry? parcelExtent = TerrainRunComposition.BuildParcelExtent(
            aoi,
            transform,
            LinearDistance.Meters(terrainExtensionMeters),
            minimumLegalEdgeLength);
        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid,
                transform,
                verticalReference,
                referenceOrigins,
                sourceMetadata,
                aoi,
                request.LocalOrigin,
                request.OutputUnit,
                request.Simplification.Method,
                request.Simplification.PointBudget,
                request.Simplification.CoverageFloorFraction,
                cancellationToken,
                addressParcel,
                parcelExtent)
            .ConfigureAwait(false);

        return CreateSnapshot(grid, outcome, transform);
    }

    private static PreparedTerrainSnapshot CreateSnapshot(
        ElevationGrid grid,
        TerrainProcessingOutcome outcome,
        IHorizontalCoordinateTransform transform)
    {
        ProjectionCharacteristics.TryMeasure(
            transform,
            outcome.Payload.Provenance.LocalFrame,
            out ProjectionCharacteristicsMeasurement? projection,
            out string? unavailableReason);
        if (unavailableReason is not null)
        {
            AddInLog.Warning($"Authoritative grid projection characteristics unavailable: {unavailableReason}");
        }

        return new PreparedTerrainSnapshot(grid, outcome, transform, projection);
    }

    private static CollectionPeriod? ParseCollectionPeriod(ProcessInputSettings process)
    {
        if (process.CollectionStart is not { } startText || process.CollectionEnd is not { } endText)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start)
            || !DateOnly.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly end))
        {
            // TerrainRequestSettings.Validate() already rejected this before the caller accepted the run.
            return null;
        }

        return new CollectionPeriod(start, end);
    }

    private static string ReadTextFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not read '{path}'.", ex);
        }
    }

    private static byte[] ReadBytesFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not read '{path}'.", ex);
        }
    }
}

/// <summary>Classifies a bounded, keyless OpenTopography reachability probe failure for the Revit command.</summary>
internal sealed class ReachabilityProbeException(string message) : InvalidOperationException(message);
