using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// The confirmed, unbuffered legal parcel and the horizontal margin used only for its terrain context.
/// This opt-in Core contract is for the Revit parcel workflow; existing CLI callers continue to use their
/// ordinary AOI path without constructing it.
/// </summary>
public sealed class ParcelExtentGeometry
{
    private readonly PolygonalRegion terrainClipRegion;

    public ParcelExtentGeometry(
        PolygonalRegion legalParcelRegion,
        LinearDistance terrainMargin,
        LinearDistance? minimumLegalEdgeLength = null)
    {
        ArgumentNullException.ThrowIfNull(legalParcelRegion);
        if (legalParcelRegion.HorizontalReference.Kind != HorizontalReferenceKind.Projected)
        {
            throw new ArgumentException("A parcel extent requires a projected legal parcel region.", nameof(legalParcelRegion));
        }

        LegalParcelRegion = legalParcelRegion;
        TerrainMargin = terrainMargin;
        MinimumLegalEdgeLength = minimumLegalEdgeLength ?? LinearDistance.Zero;
        terrainClipRegion = GridClipper.ResolveEffectiveRegion(TerrainClipRequest);
    }

    /// <summary>The original parcel used for legal identity and PropertyLine geometry.</summary>
    public PolygonalRegion LegalParcelRegion { get; }

    /// <summary>The horizontal context margin. It never changes <see cref="LegalParcelRegion"/>.</summary>
    public LinearDistance TerrainMargin { get; }

    /// <summary>
    /// A host-supplied minimum legal edge length. A distinct edge below this value is rejected because moving
    /// it to repair a host tolerance would change the legal parcel; zero leaves that host-specific check to
    /// the caller.
    /// </summary>
    public LinearDistance MinimumLegalEdgeLength { get; }

    /// <summary>The terrain-only clip request. Its effective region is resolved by the planner.</summary>
    public ClipRegion TerrainClipRequest => ClipRegion.FromRegion(LegalParcelRegion, TerrainMargin);

    /// <summary>The buffered terrain-only region, with the legal parcel still unchanged.</summary>
    public PolygonalRegion TerrainClipRegion => terrainClipRegion;

    /// <summary>The terrain-only region's projected envelope for acquisition planning.</summary>
    public PlanarEnvelope FetchEnvelope => terrainClipRegion.Envelope;
}

/// <summary>One structured warning exposed before the host decides whether to continue a parcel terrain run.</summary>
public enum TerrainExtentWarningKind
{
    LegalParcelHasMultiplePolygons,
    LegalParcelHasHoles,
    TerrainMarginChangesTopology,
    LegalParcelContainsNoData,
    TerrainExtentContainsNoData,
}

/// <summary>A typed, nonzero count warning about legal or terrain extent geometry/data.</summary>
public sealed record TerrainExtentWarning
{
    public TerrainExtentWarning(TerrainExtentWarningKind kind, int count)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        Kind = kind;
        Count = count;
    }

    public TerrainExtentWarningKind Kind { get; }
    public int Count { get; }
}

/// <summary>
/// The bounded Core output for an opted-in parcel run. All geometry and the legal plane are in the same
/// local frame used by the terrain payload; <see cref="FetchEnvelope"/> remains in source projected units.
/// </summary>
public sealed record TerrainExtentPlan(
    PolygonalRegion LegalParcelRegion,
    PolygonalRegion TerrainClipRegion,
    PlanarEnvelope FetchEnvelope,
    LocalBoundary LegalLocalBoundary,
    double LegalPlaneZ,
    GridClipResult LegalClipResult,
    GridClipResult TerrainClipResult,
    IReadOnlyList<TerrainExtentWarning> Warnings);

/// <summary>A named pre-transaction rejection while resolving the legal and terrain parcel extents.</summary>
public sealed class ParcelExtentPlanningException : InvalidOperationException
{
    public ParcelExtentPlanningException(string message)
        : base(message)
    {
    }
}

internal static class TerrainExtentPlanner
{
    public static TerrainExtentPlan Resolve(ElevationGrid grid, ParcelExtentGeometry extent, LocalCoordinateFrame frame)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(extent);
        ArgumentNullException.ThrowIfNull(frame);

        if (extent.LegalParcelRegion.HorizontalReference != grid.HorizontalReference)
        {
            throw new ParcelExtentPlanningException("The legal parcel reference does not match the elevation grid reference.");
        }

        PolygonalRegion terrainClipRegion = extent.TerrainClipRegion;
        if (extent.TerrainMargin.Value > 0d && !IsCoveredByGridEnvelope(terrainClipRegion.Envelope, grid.GetCornerEnvelope()))
        {
            throw new ParcelExtentPlanningException(
                "The requested terrain margin is not fully covered by the actual elevation-grid extent; acquire a larger source envelope before creation.");
        }

        GridClipResult legalClipResult = GridClipper.Clip(grid, ClipRegion.FromRegion(extent.LegalParcelRegion, LinearDistance.Zero));
        if (legalClipResult.RetainedElevationCount == 0)
        {
            throw new ParcelExtentPlanningException(
                "The legal parcel contains no valid elevation samples; SolidGround cannot derive its legal boundary plane from terrain outside the parcel.");
        }

        GridClipResult terrainClipResult = GridClipper.Clip(grid, ClipRegion.FromRegion(terrainClipRegion, LinearDistance.Zero));
        LocalBoundary legalLocalBoundary = LegalBoundaryFactory.FromPolygonalRegion(
            extent.LegalParcelRegion,
            frame,
            extent.MinimumLegalEdgeLength.In(frame.OutputUnit));
        double legalMinimumElevation = FindMinimumElevation(legalClipResult.Grid);
        double legalPlaneZ = frame.ToLocal(new Coordinate3D(frame.Origin.X, frame.Origin.Y, legalMinimumElevation)).Elevation;

        return new TerrainExtentPlan(
            extent.LegalParcelRegion,
            terrainClipRegion,
            extent.FetchEnvelope,
            legalLocalBoundary,
            legalPlaneZ,
            legalClipResult,
            terrainClipResult,
            BuildWarnings(extent, terrainClipRegion, legalClipResult, terrainClipResult));
    }

    private static bool IsCoveredByGridEnvelope(PlanarEnvelope requested, PlanarEnvelope actual) =>
        requested.MinX >= actual.MinX && requested.MaxX <= actual.MaxX &&
        requested.MinY >= actual.MinY && requested.MaxY <= actual.MaxY;

    private static double FindMinimumElevation(ElevationGrid grid)
    {
        double? minimum = null;
        for (int row = 0; row < grid.RowCount; row++)
        {
            for (int column = 0; column < grid.ColumnCount; column++)
            {
                double? elevation = grid.GetElevation(row, column);
                if (elevation is double value && (minimum is null || value < minimum.Value))
                {
                    minimum = value;
                }
            }
        }

        return minimum ?? throw new ParcelExtentPlanningException(
            "The legal parcel contains no valid elevation samples; SolidGround cannot derive its legal boundary plane from terrain outside the parcel.");
    }

    private static List<TerrainExtentWarning> BuildWarnings(
        ParcelExtentGeometry extent,
        PolygonalRegion terrainClipRegion,
        GridClipResult legalClipResult,
        GridClipResult terrainClipResult)
    {
        List<TerrainExtentWarning> warnings = [];
        if (extent.LegalParcelRegion.PolygonCount > 1)
        {
            warnings.Add(new TerrainExtentWarning(TerrainExtentWarningKind.LegalParcelHasMultiplePolygons, extent.LegalParcelRegion.PolygonCount));
        }

        if (extent.LegalParcelRegion.HoleCount > 0)
        {
            warnings.Add(new TerrainExtentWarning(TerrainExtentWarningKind.LegalParcelHasHoles, extent.LegalParcelRegion.HoleCount));
        }

        int topologyChangeCount = Math.Abs(terrainClipRegion.PolygonCount - extent.LegalParcelRegion.PolygonCount)
            + Math.Abs(terrainClipRegion.HoleCount - extent.LegalParcelRegion.HoleCount);
        if (topologyChangeCount > 0)
        {
            warnings.Add(new TerrainExtentWarning(TerrainExtentWarningKind.TerrainMarginChangesTopology, topologyChangeCount));
        }

        if (legalClipResult.NoDataCellsInsideRegion > 0)
        {
            warnings.Add(new TerrainExtentWarning(TerrainExtentWarningKind.LegalParcelContainsNoData, legalClipResult.NoDataCellsInsideRegion));
        }

        if (terrainClipResult.NoDataCellsInsideRegion > 0)
        {
            warnings.Add(new TerrainExtentWarning(TerrainExtentWarningKind.TerrainExtentContainsNoData, terrainClipResult.NoDataCellsInsideRegion));
        }

        return warnings;
    }
}
