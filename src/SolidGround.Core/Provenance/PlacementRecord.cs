namespace SolidGround.Core.Provenance;

/// <summary>One XYZ point, already in Revit-internal units, for <see cref="PlacementRevitCoordinatesRecord"/>.</summary>
public sealed record PlacementPointRecord(double X, double Y, double Z);

/// <summary>The created element and the Level/ToposolidType it was assigned to. See design record §9.</summary>
public sealed record PlacementToposolidRecord(
    long ElementId,
    string LevelName,
    long LevelId,
    string ToposolidTypeName,
    long ToposolidTypeId,
    string CreationStrategy);

/// <summary>
/// The unit conversion this run applied (orchestrator decision 6). <see cref="RoundTripDelta"/> is an
/// automatic, every-run self-consistency check -- <c>ConvertFromInternalUnits(ConvertToInternalUnits(1.0,
/// forgeTypeId), forgeTypeId) - 1.0</c> -- always expected near zero; it is distinct from manual test step
/// 8a item 3's one-time probe of which of <c>UnitTypeId.Feet</c>/<c>UnitTypeId.UsSurveyFeet</c> is
/// numerically Revit's own internal foot (Appendix A UNVERIFIED item 3), which that manual procedure logs
/// separately.
/// </summary>
public sealed record PlacementUnitConversionRecord(
    string OutputUnit,
    string ForgeTypeId,
    double MetersPerOutputUnit,
    double RoundTripDelta);

/// <summary>The vertical reference recorded alongside the local origin.</summary>
public sealed record PlacementVerticalReferenceRecord(string Datum, string Unit, string? GeoidModel);

/// <summary>
/// The complete local-origin offset needed to reverse the transform (AGENTS.md "Provenance decision"): the
/// original source coordinate the local frame's origin subtracts, plus the horizontal and vertical
/// references that offset is expressed in.
/// </summary>
public sealed record PlacementLocalOriginRecord(
    double SourceX,
    double SourceY,
    double SourceElevation,
    string HorizontalReference,
    PlacementVerticalReferenceRecord VerticalReference);

/// <summary>
/// Records the one constant, Revit-internal-unit Z every boundary <c>CurveLoop</c> vertex shares (design
/// record §7.2's planarity fix), and, for reference only, the resolved Level's own elevation -- never used
/// for boundary geometry itself.
/// </summary>
public sealed record PlacementBoundaryPlaneElevationRecord(
    double ConstantZInternal,
    string Source,
    double LevelElevationInternal,
    string Note);

/// <summary>
/// A read-only snapshot of the shared-coordinate state at commit time, proving SolidGround left it
/// untouched (<c>OrphanCheck</c> is the runtime check this describes).
/// </summary>
public sealed record PlacementRevitCoordinatesRecord(
    bool InternalOriginIsZero,
    PlacementPointRecord BasePointPosition,
    PlacementPointRecord BasePointSharedPosition,
    PlacementPointRecord SurveyPointPosition,
    PlacementPointRecord SurveyPointSharedPosition,
    string ActiveProjectLocationName);

public sealed record PlacementPointCountsRecord(int Original, int Retained, int Budget);

/// <summary>
/// The full, final placement record written next to the export bundle only after a confirmed
/// <c>Autodesk.Revit.DB.TransactionStatus.Committed</c> status (design record §9). Assembled from a
/// <see cref="PlacementRecordDraft"/> plus the confirmed element id (§6.6 step 1). Revit-free: kept in
/// <c>SolidGround.Core</c> (SolidGround Issue #15 review fix) so <c>SolidGround.Core.Provenance.PlacementRecordRenderer</c>'s
/// determinism is directly, offline testable -- only <c>SolidGround.Revit.Provenance.PlacementRecordWriter</c>'s
/// thin <c>Directory.CreateDirectory</c>/<c>File.WriteAllBytes</c> wrapper stays in the Revit host project.
/// </summary>
public sealed record PlacementRecord(
    string Schema,
    int SchemaVersion,
    DateTime CreatedUtc,
    string ExportDocument,
    string ExportPoints,
    PlacementToposolidRecord Toposolid,
    PlacementUnitConversionRecord UnitConversion,
    PlacementLocalOriginRecord LocalOrigin,
    PlacementBoundaryPlaneElevationRecord BoundaryPlaneElevation,
    PlacementRevitCoordinatesRecord RevitCoordinates,
    string SharedCoordinatesStatement,
    PlacementPointCountsRecord PointCounts);

/// <summary>
/// Every placement-record value computed before <c>transaction.Commit()</c> (design record §5): the created
/// element already exists mid-transaction, so every field below is already known -- only the serialized,
/// on-disk write is deferred until a confirmed <c>Committed</c> status, so a rolled-back run never leaves an
/// orphaned placement-record file. <see cref="ToRecord"/> supplies the one value this design calls out as
/// coming from "the confirmed <c>toposolid.Id</c>" once <c>Commit()</c> has actually returned
/// <c>Committed</c> (§6.6 step 1).
/// </summary>
public sealed record PlacementRecordDraft(
    string ExportDocument,
    string ExportPoints,
    string LevelName,
    long LevelId,
    string ToposolidTypeName,
    long ToposolidTypeId,
    string CreationStrategy,
    PlacementUnitConversionRecord UnitConversion,
    PlacementLocalOriginRecord LocalOrigin,
    PlacementBoundaryPlaneElevationRecord BoundaryPlaneElevation,
    PlacementRevitCoordinatesRecord RevitCoordinates,
    string SharedCoordinatesStatement,
    PlacementPointCountsRecord PointCounts)
{
    public const string Schema = "solidground.revit-placement";
    public const int SchemaVersion = 1;

    public PlacementRecord ToRecord(long confirmedElementId, DateTime createdUtc) => new(
        Schema,
        SchemaVersion,
        createdUtc,
        ExportDocument,
        ExportPoints,
        new PlacementToposolidRecord(confirmedElementId, LevelName, LevelId, ToposolidTypeName, ToposolidTypeId, CreationStrategy),
        UnitConversion,
        LocalOrigin,
        BoundaryPlaneElevation,
        RevitCoordinates,
        SharedCoordinatesStatement,
        PointCounts);
}
