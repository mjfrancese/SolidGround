using SolidGround.Core.Units;

namespace SolidGround.Core.Provenance;

/// <summary>
/// Describes one field of the <see cref="ExtensibleStorageProvenanceSchema"/> Extensible Storage schema,
/// independent of any Autodesk/Revit type. <c>SolidGround.Revit.Provenance.ProvenanceSchemaAdapter</c>
/// (SolidGround Issue #16 Stage 2) is the only consumer that turns this into a real
/// <c>Autodesk.Revit.DB.ExtensibleStorage.SchemaBuilder</c> call; nothing in <c>SolidGround.Core</c> ever
/// references the Revit API itself. <see cref="Name"/> must be looked up by name, never by list position:
/// <c>Schema.ListFields()</c>'s return order is not documented to match insertion order (design record §2).
/// </summary>
/// <param name="Name">The exact field name passed to <c>SchemaBuilder.AddSimpleField</c>/read back via <c>Entity.Get</c>/<c>Set</c>.</param>
/// <param name="ClrType">The field's simple Extensible Storage value type: <see cref="int"/>, <see cref="string"/>, <see cref="bool"/>, or <see cref="double"/>.</param>
/// <param name="IsLengthSpec">
/// <see langword="true"/> for the five fields that need <c>FieldBuilder.SetSpec(SpecTypeId.Length)</c> plus
/// the three-argument <c>Entity.Set</c>/<c>Get</c> overload with an explicit <c>UnitTypeId.Meters</c>; every
/// other field gets no spec at all, per <c>SchemaBuilder.Finish()</c>'s documented rule that only a
/// spec-carrying field can have an "invalid units" problem (design record §2).
/// </param>
/// <param name="Documentation">One sentence describing the field, passed to <c>FieldBuilder.SetDocumentation</c>.</param>
public readonly record struct ProvenanceFieldDefinition(string Name, Type ClrType, bool IsLengthSpec, string Documentation);

/// <summary>
/// The field-list contract for the Extensible Storage schema SolidGround attaches to a created toposolid
/// (AGENTS.md "Provenance decision"; SolidGround Issue #16). This type is the Revit-free half of the split
/// AGENTS.md requires: "The field-list contract must live in <c>SolidGround.Core</c>; the
/// <c>SchemaBuilder</c> call that consumes it belongs in <c>SolidGround.Revit</c>." See
/// docs/architecture/revit-extensible-storage-provenance.md for the full design and
/// docs/architecture/revit-add-in-conventions.md §11 for the originally adopted field list.
/// </summary>
public static class ExtensibleStorageProvenanceSchema
{
    /// <summary>
    /// The schema's permanent GUID, minted once (SolidGround Issue #16, 2026-09-21) and never regenerated:
    /// changing the field set requires a brand-new GUID and a new schema version, never mutating this one in
    /// place (<c>SchemaBuilder.Finish()</c> itself throws if a different-shaped schema is republished under
    /// the same GUID -- design record §3).
    /// </summary>
    public const string SchemaGuidText = "bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e";

    /// <summary><see cref="SchemaGuidText"/>, parsed once.</summary>
    public static readonly Guid SchemaGuid = Guid.Parse(SchemaGuidText);

    /// <summary>
    /// The schema version this build of SolidGround writes and expects to read back, independent of
    /// <see cref="TerrainProvenance.CurrentSchemaVersion"/> and <see cref="PlacementRecordDraft.SchemaVersion"/>
    /// -- none of the three describe the same artifact, so no numeric alignment between them is load-bearing
    /// (design record §1, Q1).
    /// </summary>
    public const int CurrentVersion = 1;

    /// <summary>
    /// The schema's own name, underscores only and no punctuation: the owner's other add-in's own <c>SchemaBuilder.SetSchemaName</c>
    /// call sites never pass a dotted name, and no source states <c>AcceptableName</c>'s exact rule, so a
    /// dotted display name is avoided here as a real rejection risk (design record §2's "Schema-name rationale").
    /// </summary>
    public const string SchemaName = "SolidGround_Provenance_Toposolid";

    /// <summary>
    /// Passed to <c>SchemaBuilder.SetVendorId</c>. Revit stores vendor ids upper-cased, so this need not
    /// match <c>SolidGround.addin</c>'s <c>&lt;VendorId&gt;</c> byte-for-byte; the Revit-side schema-drift
    /// check compares it case-insensitively for this reason (design record §2, round 2 finding 1/4).
    /// </summary>
    public const string VendorId = "SolidGround";

    /// <summary>Passed to <c>SchemaBuilder.SetDocumentation</c>.</summary>
    public const string Documentation =
        "Reversible SolidGround terrain provenance and Revit add-in build identity attached to a created toposolid.";

    /// <summary>
    /// Bounds both the in-hook field-level read-back compare and the source-coordinate reconstruction
    /// compare (design record §2, §3 step 8). A first-principles placeholder pending Issue #16's manual
    /// evidence Step 9 multi-sample calibration (design record §12 item 5): the mechanics of whether
    /// <c>Entity.Set</c>/<c>Get&lt;double&gt;(name, value, UnitTypeId.Meters)</c> round-trips through an
    /// internal-unit conversion are unverified against any 2027 API source, so this value is not yet backed
    /// by observed evidence and must not be lowered without re-deriving it from that evidence.
    /// </summary>
    public static readonly LinearDistance ExtensibleStorageRoundTripTolerance = LinearDistance.Meters(1e-6);

    /// <summary>
    /// The 36 fields in the design record's own canonical field-table order. This order is documentation-only:
    /// every consumer (<see cref="ExtensibleStorageProvenanceValues"/>, the Revit-side schema adapter and
    /// entity writer) looks fields up by <see cref="ProvenanceFieldDefinition.Name"/>, never by list position.
    /// </summary>
    public static IReadOnlyList<ProvenanceFieldDefinition> Fields { get; } =
    [
        new("schemaVersion", typeof(int), false, "The Extensible Storage schema version this entity was written under."),
        new("sourceDatasetName", typeof(string), false, "The elevation source's display name."),
        new("sourceDatasetIdentifier", typeof(string), false, "The source's own dataset identifier."),
        new("hasCollectionPeriod", typeof(bool), false, "Whether the source reported a collection period."),
        new("collectionPeriodStartIso", typeof(string), false, "The collection period's start date, ISO 8601 (yyyy-MM-dd), or empty when absent."),
        new("collectionPeriodEndIso", typeof(string), false, "The collection period's end date, ISO 8601 (yyyy-MM-dd), or empty when absent."),
        new("hasQualityLevel", typeof(bool), false, "Whether the source reported a catalog quality level."),
        new("qualityLevel", typeof(string), false, "The source's catalog quality level, or empty when absent."),
        new("horizontalDatum", typeof(string), false, "The datum of the projected coordinate reference system named by horizontalCrsIdentifier."),
        new("horizontalCrsIdentifier", typeof(string), false, "The projected horizontal coordinate reference system identifier (for example \"EPSG:26915\")."),
        new("sourceHorizontalReferenceOrigin", typeof(string), false, "Where the source horizontal reference actually came from (SolidGround.Core.Metadata.ReferenceOrigin)."),
        new("verticalDatum", typeof(string), false, "The source vertical reference's datum."),
        new("hasVerticalGeoidModel", typeof(bool), false, "Whether the source vertical reference names a geoid model."),
        new("verticalGeoidModel", typeof(string), false, "The source vertical reference's geoid model, or empty when absent."),
        new("sourceVerticalReferenceOrigin", typeof(string), false, "Where the source vertical reference actually came from (SolidGround.Core.Metadata.ReferenceOrigin)."),
        new("originalPointCount", typeof(int), false, "The number of valid source samples before simplification."),
        new("retainedPointCount", typeof(int), false, "The number of samples retained after simplification."),
        new("simplificationMethod", typeof(string), false, "The simplification algorithm requested (SolidGround.Core.Simplification.SimplificationMethod)."),
        new("simplificationPointBudget", typeof(int), false, "The requested point budget the simplifier targeted."),
        new("hasElevationRange", typeof(bool), false, "Whether an elevation range was computed (false only when the original data was empty)."),
        new("elevationMinimumMeters", typeof(double), true, "The minimum retained elevation, in meters, or zero when hasElevationRange is false."),
        new("elevationMaximumMeters", typeof(double), true, "The maximum retained elevation, in meters, or zero when hasElevationRange is false."),
        new("outputUnitToken", typeof(string), false, "The local coordinate frame's output unit, tokenized identically to the placement record's own unit field."),
        new("metersPerOutputUnit", typeof(double), false, "The exact number of meters represented by one output unit. A ratio, not a length: carries no length spec."),
        new("localOriginXMeters", typeof(double), true, "The local frame origin's source X ordinate, in meters."),
        new("localOriginYMeters", typeof(double), true, "The local frame origin's source Y ordinate, in meters."),
        new("localOriginElevationMeters", typeof(double), true, "The local frame origin's source elevation, in meters."),
        new("horizontalForwardOperationFormat", typeof(string), false, "The geographic-to-projected coordinate operation definition's format (for example \"WKT1\")."),
        new("horizontalForwardOperationDefinition", typeof(string), false, "The geographic-to-projected coordinate operation's own definition text."),
        new("horizontalInverseOperationFormat", typeof(string), false, "The projected-to-geographic coordinate operation definition's format."),
        new("horizontalInverseOperationDefinition", typeof(string), false, "The projected-to-geographic coordinate operation's own definition text."),
        new("horizontalTransformEngineName", typeof(string), false, "The named engine that performed the horizontal coordinate transformation (for example \"ProjNET\")."),
        new("horizontalTransformEngineVersion", typeof(string), false, "The named engine's version, needed to reproduce its exact numeric result."),
        new("buildInformationalVersion", typeof(string), false, "The SolidGround.Revit assembly's informational version at the time this element was created."),
        new("buildModuleVersionId", typeof(string), false, "The SolidGround.Revit assembly's module version ID (MVID) at the time this element was created."),
        new("buildSha256", typeof(string), false, "The SolidGround.Revit assembly file's SHA-256 hash at the time this element was created."),
    ];
}
