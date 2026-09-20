using SolidGround.Core.Clipping;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Processing;

/// <summary>
/// <see cref="TerrainProcessingPipeline.RunAsync"/>'s result: the assembled export payload plus the
/// diagnostic-only intermediate values a command's own <c>--verbose</c> output needs (see
/// docs/architecture/cli-workflow.md's "Diagnostics and redaction" section: clip statistics and the full
/// simplification diagnostics) but that <see cref="TerrainExportPayload"/>/<see cref="TerrainProvenance"/>
/// do not themselves retain. <see cref="ClipResult"/> is <see langword="null"/> exactly when no AOI was
/// given (the whole grid was processed).
/// </summary>
internal sealed record TerrainProcessingOutcome(
    TerrainExportPayload Payload,
    GridClipResult? ClipResult,
    SimplificationDiagnostics? SimplificationDiagnostics);

/// <summary>
/// Clips (optionally), snaps a local origin, simplifies, and assembles a self-describing export payload from
/// an already-parsed grid and an already-built WGS84-to-grid transform. Shared verbatim by `process` and
/// `run` -- see docs/architecture/cli-workflow.md's "Commands" section: "share one internal processing
/// pipeline... so their clipping, local-origin, unit, and simplification behavior can never drift apart from
/// each other". Callers are responsible for building <paramref name="grid"/>/<paramref name="wgs84ToGridTransform"/>
/// such that <c>wgs84ToGridTransform.Definition.TargetReference == grid.HorizontalReference</c> already holds
/// (true by construction for `process`; explicitly checked by `RunCommand` before calling this) and that
/// <paramref name="verticalReference"/> is the exact same value used to parse <paramref name="grid"/>, so
/// `TerrainExportPayloadAssembler.Assemble`'s own equality checks hold trivially.
/// </summary>
internal static class TerrainProcessingPipeline
{
    internal static async Task<TerrainProcessingOutcome> RunAsync(
        ElevationGrid grid,
        IHorizontalCoordinateTransform wgs84ToGridTransform,
        VerticalReference verticalReference,
        ReferenceOrigins referenceOrigins,
        ElevationSourceMetadata sourceMetadata,
        AoiSelection? aoi,
        LocalOriginSelection origin,
        LengthUnit outputUnit,
        SimplificationMethod method,
        int pointBudget,
        double coverageFloorFraction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(wgs84ToGridTransform);
        ArgumentNullException.ThrowIfNull(verticalReference);
        ArgumentNullException.ThrowIfNull(referenceOrigins);
        ArgumentNullException.ThrowIfNull(sourceMetadata);
        ArgumentNullException.ThrowIfNull(origin);

        // Step 1: clip region (optional). No AOI means no clip -- the candidate grid is the source grid
        // itself and there is no GridClipResult to report clip statistics from.
        GridClipResult? clipResult = null;
        ElevationGrid candidateGrid = grid;
        if (aoi is not null)
        {
            ClipRegion clipRegion = ClipRegionFactory.Build(aoi, wgs84ToGridTransform);
            clipResult = GridClipper.Clip(grid, clipRegion);
            candidateGrid = clipResult.Grid;
        }

        // Step 2 (run only) is performed by the caller, before this method is ever invoked: see RunCommand's
        // own transform-versus-grid reference check, documented in docs/architecture/cli-workflow.md's "AOI
        // and clip derivation" section.

        // Step 3: local origin.
        Coordinate3D originPoint = LocalOriginFactory.ComputeOrigin(origin, candidateGrid, grid.HorizontalReference, verticalReference);

        // Step 4: local frame.
        LocalCoordinateFrame localFrame = new(originPoint, grid.HorizontalReference, verticalReference, outputUnit);

        // Step 5: simplify.
        GridTerrainSimplifier simplifier = new(coverageFloorFraction);
        SimplificationResult simplification = await simplifier.SimplifyAsync(
            candidateGrid, new SimplificationRequest(pointBudget, method), cancellationToken).ConfigureAwait(false);

        // Step 6: assemble.
        TerrainExportPayload payload = TerrainExportPayloadAssembler.Assemble(
            sourceMetadata, wgs84ToGridTransform.Definition, verticalReference, referenceOrigins, localFrame, candidateGrid, simplification);

        return new TerrainProcessingOutcome(payload, clipResult, simplification.Diagnostics);
    }
}
