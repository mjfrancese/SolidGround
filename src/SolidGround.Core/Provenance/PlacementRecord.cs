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
/// The Extensible Storage cross-reference (SolidGround Issue #16 design record §5): the schema GUID and
/// schema version SolidGround attached to the created toposolid, so an operator reading only the placement
/// record JSON can tell whether/which Extensible Storage entity is attached, with no separate "attached"
/// boolean and no digest (design record D8). Both values are Core compile-time constants
/// (<see cref="ExtensibleStorageProvenanceSchema.SchemaGuidText"/>/<see cref="ExtensibleStorageProvenanceSchema.CurrentVersion"/>),
/// so <c>CreateToposolidCommand.BuildPlacementDraft</c> -- which runs before the Extensible Storage attach
/// hook -- can reference them directly. A placement JSON file can only exist once attachment has succeeded
/// (attachment is fatal, design record D1), so this pair alone tells a reader "provenance was attached."
/// </summary>
public sealed record PlacementExtensibleStorageRecord(string SchemaGuid, int SchemaVersion);

/// <summary>
/// The <c>PropertyLine</c> element SolidGround Issue #30 (PH3-3) creates alongside the <c>Toposolid</c> for a
/// parcel area of interest only (owner decision 3, 2026-09-26): <see cref="Created"/> is always present;
/// <see cref="ElementId"/>/<see cref="AreaInternal"/> are non-null exactly when <see cref="Created"/> is
/// <see langword="true"/>. <see cref="AreaInternal"/> is Revit-internal (<c>PropertyLine.Area</c>'s own doc
/// comment states only its closed-loop-detection meaning, not an explicit unit), matching this schema's
/// existing <c>*Internal</c> suffix convention. See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Placement record schema" section.
/// </summary>
public sealed record PlacementPropertyLineRecord(bool Created, long? ElementId, double? AreaInternal);

/// <summary>
/// The opt-in shared-coordinates write SolidGround Issue #30 (PH3-3) performs when
/// <c>sharedCoordinates.writeIfAbsent</c> is enabled and Preflight's detection proxy found no existing shared
/// coordinates: <see cref="Attempted"/> is always present; every other field is non-null exactly when
/// <see cref="Attempted"/> is <see langword="true"/>. <see cref="EastWest"/>/<see cref="NorthSouth"/>/
/// <see cref="Elevation"/> are the terrain's own local-frame origin, in its own native unit -- named by the
/// paired <see cref="HorizontalUnit"/>/<see cref="VerticalUnit"/> tokens, NOT always meters (a process-mode run
/// can select a non-metric horizontal or vertical unit; see
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Unit convention for the shared-coordinates
/// value" section) -- the identical values already recorded in <see cref="PlacementLocalOriginRecord"/>'s own
/// <c>SourceX</c>/<c>SourceY</c>/<c>SourceElevation</c> -- never the raw Revit-internal <see langword="double"/>
/// actually passed to <c>ProjectPosition</c>'s constructor. <see cref="AngleInternal"/> is the one exception,
/// recorded Revit-internal (radians): an angle has no length unit to convert into. Whenever
/// <see cref="Attempted"/> is <see langword="true"/>,
/// <see cref="Verified"/> is always <see langword="true"/> (never <see langword="false"/>) in any placement
/// record that reaches disk -- a failed verification rolls back the whole transaction, so no placement record is
/// ever written for that run; whenever <see cref="Attempted"/> is <see langword="false"/> (the shipped default),
/// <see cref="Verified"/> is <see langword="null"/>, like every other field here. See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Shared-coordinates detection, write, and
/// verification" and "Placement record schema" sections.
/// </summary>
public sealed record PlacementSharedCoordinatesWriteRecord(
    bool Attempted,
    double? EastWest,
    double? NorthSouth,
    double? Elevation,
    double? AngleInternal,
    string? HorizontalUnit,
    string? VerticalUnit,
    bool? Verified);

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
    PlacementPointCountsRecord PointCounts,
    PlacementExtensibleStorageRecord ExtensibleStorage,
    PlacementPropertyLineRecord PropertyLine,
    PlacementSharedCoordinatesWriteRecord SharedCoordinatesWrite);

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
    PlacementPointCountsRecord PointCounts,
    PlacementExtensibleStorageRecord ExtensibleStorage,
    PlacementPropertyLineRecord PropertyLine,
    PlacementSharedCoordinatesWriteRecord SharedCoordinatesWrite)
{
    public const string Schema = "solidground.revit-placement";

    /// <summary>
    /// Bumped 2 -> 3 for SolidGround Issue #30 (PH3-3): the record's required shape changed with the addition
    /// of <see cref="PlacementPropertyLineRecord"/>/<see cref="PlacementSharedCoordinatesWriteRecord"/>. A
    /// counter independent of <c>TerrainProvenance.CurrentSchemaVersion</c> and of the Extensible Storage
    /// schema's own version -- three separate counters that share a field name. See
    /// docs/architecture/revit-property-line-and-shared-coordinates.md's "Placement record schema" section.
    /// </summary>
    public const int SchemaVersion = 3;

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
        PointCounts,
        ExtensibleStorage,
        PropertyLine,
        SharedCoordinatesWrite);
}
