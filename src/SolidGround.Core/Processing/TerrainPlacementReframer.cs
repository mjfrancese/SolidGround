using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Provenance;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>A declared provisional ground reference and the exact unsimplified population policy that produced it.</summary>
public sealed record ProvisionalGroundReference(double Elevation, string Policy);

/// <summary>
/// Rebuilds immutable final terrain coordinates after a floor datum is chosen, without requesting terrain
/// again or changing the clip/simplification population used by the prepared outcome.
/// </summary>
public static class TerrainPlacementReframer
{
    public static TerrainProcessingOutcome Reframe(
        TerrainProcessingOutcome outcome, ElevationGrid rawGrid, BuildingFloorReference floorReference)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(rawGrid);
        ArgumentNullException.ThrowIfNull(floorReference);

        TerrainProvenance oldProvenance = outcome.Payload.Provenance;
        if (floorReference.SourceReference != oldProvenance.SourceVerticalReference)
        {
            throw new ArgumentException("The floor reference vertical datum and unit must equal the terrain source reference.", nameof(floorReference));
        }

        LocalCoordinateFrame oldFrame = oldProvenance.LocalFrame;
        double targetLevelInSourceUnit = LengthConverter.Convert(
            floorReference.TargetLevel.ProjectElevationInternal,
            LengthUnit.InternationalFoot,
            oldProvenance.SourceVerticalReference.Unit);
        double referenceElevation = floorReference.ResolveFrameReferenceElevation();
        LocalCoordinateFrame newFrame = new(
            new Coordinate3D(oldFrame.Origin.X, oldFrame.Origin.Y, referenceElevation - targetLevelInSourceUnit),
            oldFrame.ProjectedHorizontalReference,
            oldFrame.VerticalReference,
            oldFrame.OutputUnit);
        double localVerticalDelta = LengthConverter.Convert(
            oldFrame.Origin.Elevation - newFrame.Origin.Elevation,
            oldFrame.VerticalReference.Unit,
            oldFrame.OutputUnit);

        LocalTerrainSample[] translated = [.. outcome.Payload.Samples.Select(sample =>
            new LocalTerrainSample(new LocalCoordinate(sample.Position.X, sample.Position.Y, sample.Position.Elevation + localVerticalDelta)))];
        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            oldProvenance.Source,
            oldProvenance.HorizontalTransformation,
            oldProvenance.SourceVerticalReference,
            oldProvenance.SourceHorizontalReferenceOrigin,
            oldProvenance.SourceVerticalReferenceOrigin,
            newFrame,
            oldProvenance.SimplificationRequest,
            oldProvenance.OriginalPointCount,
            oldProvenance.RetainedPointCount,
            oldProvenance.ElevationRange,
            oldProvenance.AddressParcel,
            floorReference,
            oldProvenance.BuildingOutline);
        TerrainExportPayload payload = new(translated, provenance);

        TerrainExtentPlan? reframedPlan = outcome.TerrainExtentPlan is { } plan
            ? plan with { LegalPlaneZ = plan.LegalPlaneZ + localVerticalDelta }
            : null;
        return outcome with { Payload = payload, TerrainExtentPlan = reframedPlan };
    }

    /// <summary>
    /// Resolves the temporary ground reference from legal parcel cells, otherwise the ordinary AOI clip, and
    /// finally the raw acquisition grid. No terrain extension or simplifier selection participates.
    /// </summary>
    public static ProvisionalGroundReference ResolveProvisionalGroundReference(TerrainProcessingOutcome outcome, ElevationGrid rawGrid)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(rawGrid);
        ElevationGrid population;
        string policy;
        if (outcome.TerrainExtentPlan is { } extent)
        {
            population = extent.LegalClipResult.Grid;
            policy = BuildingFloorReference.LegalValidCellMedianPolicy;
        }
        else
        {
            population = outcome.ClipResult?.Grid ?? rawGrid;
            policy = BuildingFloorReference.AoiValidCellMedianPolicy;
        }

        List<double> elevations = [];
        for (int row = 0; row < population.RowCount; row++)
        {
            for (int column = 0; column < population.ColumnCount; column++)
            {
                if (population.GetElevation(row, column) is double elevation)
                {
                    elevations.Add(elevation);
                }
            }
        }

        if (elevations.Count == 0)
        {
            throw new InvalidOperationException("The selected ground-reference population contains no valid elevation.");
        }

        elevations.Sort();
        int middle = elevations.Count / 2;
        double median = elevations.Count % 2 == 1
            ? elevations[middle]
            : (elevations[middle - 1] / 2d) + (elevations[middle] / 2d);
        if (!double.IsFinite(median))
        {
            throw new InvalidOperationException("The selected ground-reference median is not finite.");
        }

        return new ProvisionalGroundReference(median, policy);
    }

    public static double ResolveProvisionalGroundElevation(TerrainProcessingOutcome outcome, ElevationGrid rawGrid) =>
        ResolveProvisionalGroundReference(outcome, rawGrid).Elevation;
}
