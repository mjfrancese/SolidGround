using SolidGround.Core.Exports;
using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;

namespace SolidGround.Core.Provenance;

/// <summary>
/// Builds a self-describing <see cref="TerrainExportPayload"/> from a post-clip candidate grid and a
/// simplifier's result: computes the point counts and elevation range AGENTS.md's provenance decision
/// requires, cross-checks them against any diagnostics the simplifier reported, and carries the simplifier's
/// own sample order through to the local frame unchanged. See
/// docs/architecture/provenance-and-deterministic-exports.md's "Provenance record and AGENTS.md field
/// coverage" and "NODATA, empty candidate sets, and statistics" sections for the rules this enforces.
/// </summary>
public static class TerrainExportPayloadAssembler
{
    /// <summary>
    /// Assembles a <see cref="TerrainExportPayload"/> from <paramref name="candidateGrid"/> (the grid handed
    /// to the simplifier, after clipping) and <paramref name="simplification"/> (the simplifier's result,
    /// whose own <see cref="SimplificationResult.Request"/> becomes the payload's recorded
    /// <see cref="TerrainProvenance.SimplificationRequest"/>). Samples are carried through in
    /// <see cref="SimplificationResult.RetainedSamples"/>'s order, converted to
    /// <paramref name="localFrame"/>'s local coordinates; SolidGround never re-sorts, groups, or deduplicates
    /// them here. <paramref name="referenceOrigins"/> is carried unchanged into
    /// <see cref="TerrainProvenance.SourceHorizontalReferenceOrigin"/>/<see cref="TerrainProvenance.SourceVerticalReferenceOrigin"/>
    /// -- this method never inspects or infers it. See docs/architecture/provenance-and-deterministic-exports.md's
    /// "Export document manifest, schema version 2" section.
    /// </summary>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    /// <exception cref="TerrainProvenanceException">
    /// <paramref name="candidateGrid"/> has no valid elevation; its elevation unit does not match
    /// <paramref name="sourceVerticalReference"/>'s unit; its horizontal reference does not match
    /// <paramref name="localFrame"/>'s projected horizontal reference; its vertical reference does not match
    /// <paramref name="sourceVerticalReference"/>; or <paramref name="simplification"/>'s diagnostics disagree
    /// with the grid's own computed point count or elevation range.
    /// </exception>
    public static TerrainExportPayload Assemble(
        ElevationSourceMetadata source,
        HorizontalTransformationDefinition horizontalTransformation,
        VerticalReference sourceVerticalReference,
        ReferenceOrigins referenceOrigins,
        LocalCoordinateFrame localFrame,
        ElevationGrid candidateGrid,
        SimplificationResult simplification)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(horizontalTransformation);
        ArgumentNullException.ThrowIfNull(sourceVerticalReference);
        ArgumentNullException.ThrowIfNull(referenceOrigins);
        ArgumentNullException.ThrowIfNull(localFrame);
        ArgumentNullException.ThrowIfNull(candidateGrid);
        ArgumentNullException.ThrowIfNull(simplification);

        int originalPointCount = ElevationStatistics.CountValidCells(candidateGrid);
        if (originalPointCount == 0)
        {
            throw new TerrainProvenanceException(
                "The candidate set has no valid elevation: every cell handed to the simplifier is NODATA, and " +
                "NODATA cells are excluded from export.");
        }

        ElevationRange elevationRange = ElevationStatistics.ComputeRange(candidateGrid);
        if (elevationRange.Unit != sourceVerticalReference.Unit)
        {
            throw new TerrainProvenanceException(
                $"The candidate grid's elevation unit '{elevationRange.Unit}' does not match the source " +
                $"vertical reference's unit '{sourceVerticalReference.Unit}'.");
        }

        if (candidateGrid.HorizontalReference != localFrame.ProjectedHorizontalReference)
        {
            throw new TerrainProvenanceException(
                $"The candidate grid's horizontal reference '{candidateGrid.HorizontalReference}' does not " +
                $"match the local frame's projected horizontal reference '{localFrame.ProjectedHorizontalReference}'.");
        }

        if (candidateGrid.VerticalReference != sourceVerticalReference)
        {
            throw new TerrainProvenanceException(
                $"The candidate grid's vertical reference '{candidateGrid.VerticalReference}' does not match " +
                $"the source vertical reference '{sourceVerticalReference}'.");
        }

        ValidateDiagnostics(simplification.Diagnostics, originalPointCount, elevationRange);

        LocalTerrainSample[] samples = [.. simplification.RetainedSamples
            .Select(retained => new LocalTerrainSample(localFrame.ToLocal(retained.Position)))];

        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            source,
            horizontalTransformation,
            sourceVerticalReference,
            referenceOrigins.Horizontal,
            referenceOrigins.Vertical,
            localFrame,
            simplification.Request,
            originalPointCount,
            samples.Length,
            elevationRange);

        return new TerrainExportPayload(samples, provenance);
    }

    private static void ValidateDiagnostics(SimplificationDiagnostics? diagnostics, int originalPointCount, ElevationRange elevationRange)
    {
        if (diagnostics is null)
        {
            return;
        }

        if (diagnostics.CandidatePointCount != originalPointCount)
        {
            throw new TerrainProvenanceException(
                $"Simplification diagnostics {nameof(SimplificationDiagnostics.CandidatePointCount)} does not " +
                "match the candidate grid's valid cell count.");
        }

        if (diagnostics.MinElevation != elevationRange.Minimum)
        {
            throw new TerrainProvenanceException(
                $"Simplification diagnostics {nameof(SimplificationDiagnostics.MinElevation)} does not match " +
                "the candidate grid's minimum elevation.");
        }

        if (diagnostics.MaxElevation != elevationRange.Maximum)
        {
            throw new TerrainProvenanceException(
                $"Simplification diagnostics {nameof(SimplificationDiagnostics.MaxElevation)} does not match " +
                "the candidate grid's maximum elevation.");
        }

        if (diagnostics.ElevationUnit != elevationRange.Unit)
        {
            throw new TerrainProvenanceException(
                $"Simplification diagnostics {nameof(SimplificationDiagnostics.ElevationUnit)} does not match " +
                "the candidate grid's elevation unit.");
        }
    }
}
