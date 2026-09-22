# Revit Extensible Storage provenance

**Status.** Stages 1 (Core), 2 (Revit and the placement record), and 3 (this note) all landed 2026-09-21; only
Stage 4 (manual evidence) remains. This note was originally drafted in Stage 3 ahead of Stages 1 and 2's own
commits (design record §9); it has since been reconciled line by line against the shipped code, which is
authoritative over both this note's original draft and the design record wherever the three differ. The
"Manual evidence plan" section's "Evidence" paragraphs stay blank — Pending Stage 4 — until a real Revit 2027
session fills them in. Unlike `revit-toposolid-creation.md`'s dedicated "Basis" and "Design decisions with
reasons" headings, this note compresses that same material — the multi-proposal/judge synthesis and the
per-decision rationale — into this Status paragraph and into bolded inline asides inside the sections below
(for example "Schema-name rationale," "VendorId comparison," "Per-axis meter normalization," "Tolerance,
provisional"); this is a deliberate word-budget choice, not an omitted section.

Issue #16 attaches SolidGround-owned provenance to the `Toposolid` `CreateToposolidCommand` creates, via one
Revit 2027 Extensible Storage schema: a fixed-GUID, versioned, flat `Entity` carrying 36 fields drawn from
`TerrainExportPayload.Provenance` and `SolidGround.Revit`'s own build identity, written and read back inside
the same transaction Issue #15 already opens, at the exact extension point that issue left for it
(`docs/architecture/revit-toposolid-creation.md`, "The Issue #16 extension point"). This note is the design
and error-catalogue record for that mechanism; it follows `docs/architecture/revit-toposolid-creation.md`'s
structure and tone and is synthesized from Issue #16's own multi-proposal, three-judge design record (not
committed to this repository) plus a Revit-free `MetadataLoadContext` reflection pass over the installed
Revit 2027 (`27.0.10.13`) `RevitAPI.dll` and Autodesk's current Extensible Storage guide.

Issue #16 ships no code-signing or packaging change (Issue #17's), no ribbon-icon redesign (Issue #19's), and
no worksharing/central-model behavior (unexplored; see "Known limitations").

## Purpose and boundary

AGENTS.md's Provenance decision requires SolidGround-owned metadata on the created toposolid to survive
independently of the placement-record JSON sidecar Issue #15 already writes, using Extensible Storage rather
than Extended Properties, because "that lifecycle is useful for linked external property providers but
unnecessary for metadata created and owned with one element." Issue #16 builds exactly that: a schema-backed
`Entity` attached with `Element.SetEntity()`, retrieved with `Element.GetEntity()`, removable with
`Element.DeleteEntity()` — alongside, not instead of, Issue #15's placement-record JSON, which gains a small
cross-reference to this schema (see "Placement-record change").

In scope: the Core field-list contract, the Revit-side schema build/attach/read-back, the hook wiring, failure
semantics, the placement-record cross-reference, and a manual evidence plan for every runtime behavior no
offline test can reach. Out of scope: worksharing/central-model behavior, toposolid editing (so no
provenance-goes-stale-after-edit handling), and a same-vendor-different-case write-**success** scenario
(deferred; see "Known limitations").

## The provenance decision, restated

AGENTS.md requires Extensible Storage "for SolidGround-owned provenance on the created toposolid unless new
2027 evidence changes the tradeoff," and no such evidence surfaced during this design: the installed
`RevitAPI.dll` and Autodesk's 2027 guide describe `Autodesk.Revit.ExternalData.ExtendedPropertiesLink` as
built around external-server load content and link data, not one-element-owned metadata. AGENTS.md's field
list requires, at minimum, source dataset, collection date, quality level, horizontal datum, vertical datum,
original and retained point counts, elevation minimum and maximum, output unit and foot definition, the
complete local-origin offset, and the `SolidGround.Revit` assembly's build identity (informational version,
MVID, SHA-256); "the field-list contract must live in `SolidGround.Core`; the `SchemaBuilder` call that
consumes it belongs in `SolidGround.Revit`... the schema must grant `AccessLevel.Public` read and
`AccessLevel.Vendor` write."

`docs/architecture/revit-add-in-conventions.md` section 11 (owner-approved 2026-09-20) adds owner decision 7's
build-identity fields to that minimum and picks, from the two the owner's other add-in precedents it surveyed (a version baked
into the schema name vs. an explicit `schemaVersion` field), "the explicit-field mechanism consistently from
the first schema." `docs/architecture/revit-2027-verification-and-host-design.md` item 8 confirms the exact
API shape this decision relies on is present in the installed 2027 build (see "Revit 2027 API surface used"
below) and that "no native schema-version concept exists anywhere in this API or its own guide" — the reason
section 11 already requires an explicit field rather than relying on one.

## Revit 2027 API surface used

Every member below was confirmed by `System.Reflection.MetadataLoadContext` against the installed
`RevitAPI.dll` (`27.0.10.13`, SHA-256 `BB4A5B3DEC4E140527C311FBAFC2E676E34594330748BE79C0654A46D521CB89`,
`C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll`); where Autodesk's 2027 Extensible Storage guide
(`https://help.autodesk.com/view/RVT/2027/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Storing_Data_in_the_Revit_model_Extensible_Storage_html`,
firecrawl-fetched 2026-09-21 — WebFetch was not used against this host, matching this repository's own known
WebFetch/Autodesk false-404 issue) separately names a member in its own prose, that citation is given too;
several rows below cite "Reflection" alone, with no guide citation, because the guide's own prose never names
that member individually. "Reflection" below means member existence and static signature only, never
runtime behavior, matching this note's style model's own caveat; runtime behavior is the manual evidence
plan's job.

| Member | Signature | Citation |
| --- | --- | --- |
| `AccessLevel` | enum: `Public = 1, Vendor = 2, Application = 3` | Reflection; guide L81 |
| `Schema.Lookup(Guid)` | `static Schema Lookup(Guid guid)` | Reflection; guide L118-124 |
| `Schema.ListFields()` | `IList<Field> ListFields()` | Reflection |
| `Schema.{SchemaName,VendorId,ReadAccessLevel,WriteAccessLevel,GUID}` | get-only properties | Reflection |
| `Field.{FieldName,ValueType}` | get-only properties | Reflection |
| `Field.GetSpecTypeId()` | `ForgeTypeId GetSpecTypeId()` | Reflection (the guide L131 code sample demonstrates only the write-side counterpart, `FieldBuilder.SetSpec`) |
| `SchemaBuilder(Guid)` | constructor, no parameterless overload | Reflection; guide L66-73 |
| `SchemaBuilder.GUIDIsValid`/`.VendorIdIsValid` | `static bool GUIDIsValid(Guid)`, `static bool VendorIdIsValid(string)` | Reflection |
| `SchemaBuilder.AcceptableName(string)` | `bool AcceptableName(string name)` | Reflection |
| `SchemaBuilder.SetSchemaName/.SetVendorId/.SetReadAccessLevel/.SetWriteAccessLevel/.SetDocumentation` | each `SchemaBuilder SetX(...)`, fluent | Reflection; guide L66-73 |
| `SchemaBuilder.AddSimpleField(string, Type)` | `FieldBuilder AddSimpleField(string fieldName, Type fieldType)` | Reflection; guide L87-104 (allowed simple types include `int`/`double`/`bool`/`string`, the four this schema uses), L106 (16 MB string limit) |
| `SchemaBuilder.Finish()` | `Schema Finish()` | Reflection; installed `RevitAPI.xml`: "The SchemaBuilder has already finished building the Schema. -or- A different Schema with a matching identity already exists. -or- Two fields with the same name are detected. -or- At least one field has invalid units. -or- SchemaName is not set. -or- VendorId is not set for a restricted access level. -or- ApplicationGUID is not set for an application access level. -or- More than 256 fields were added to the schema."; guide L77 (post-`Finish()` immutability) |
| `FieldBuilder.SetSpec(ForgeTypeId)` | `FieldBuilder SetSpec(ForgeTypeId specTypeId)` | Reflection; guide L131 code sample |
| `new Entity(Schema)` | constructor; throws `Autodesk.Revit.Exceptions.ArgumentNullException` for a null schema and `Autodesk.Revit.Exceptions.InvalidOperationException` ("Writing of Entities of this Schema is not allowed to the current add-in.") when the calling add-in lacks write access | Reflection; guide L118-124; installed `RevitAPI.xml` |
| `Entity.Set<T>` | `Set<FieldType>(string, FieldType[, ForgeTypeId])` | Reflection; guide L131 demonstrates the sibling `Field`-keyed overload (`entity.Set<XYZ>(field, value, UnitTypeId.Meters)`) — the string-keyed overload used here is confirmed by reflection only |
| `Entity.Get<T>` | `FieldType Get<FieldType>(string[, ForgeTypeId])` | Reflection; guide L131 demonstrates the sibling `Field`-keyed overload — the string-keyed overload used here is confirmed by reflection only |
| `Entity.IsValid()` | `bool IsValid()` — method, not property | Reflection; guide L118-124 paraphrases the flow as `Element.GetEntity(schema)` returning an invalid `Entity` if none was stored (not an Autodesk verbatim quote) |
| `Entity.ReadAccessGranted()` | `bool ReadAccessGranted()` | Reflection |
| `Element.SetEntity(Entity)` | `void SetEntity(Entity entity)` | Reflection (`Autodesk.Revit.DB.Element.txt`); guide L118-124 |
| `Element.GetEntity(Schema)` | `Entity GetEntity(Schema schema)` | Reflection; guide L118-124 |
| `Element.GetEntitySchemaGuids()` | `IList<Guid> GetEntitySchemaGuids()` | Reflection; guide L118-124 |
| `Element.DeleteEntity(Schema)` | `bool DeleteEntity(Schema schema)` | Reflection; installed `RevitAPI.xml`: "True if entity was deleted, false if entity didn't exist" |
| `Document.EraseSchemaAndAllEntities(Schema)` | `void EraseSchemaAndAllEntities(Schema schema)` — a `Document` member, **not** a `Schema` member | Reflection (cited for completeness; unused by this design) |
| `SpecTypeId.Length` | `static ForgeTypeId Length { get; }` | Reflection |
| `UnitTypeId.Meters` | `static ForgeTypeId Meters { get; }` | Reflection |
| `ForgeTypeId.Empty()`/`.TypeId` | `bool Empty()`, `string TypeId { get; set; }` | Reflection (`Autodesk.Revit.DB.ForgeTypeId.txt`, this issue's own apidump/reflection notes, not committed to this repository); `revit-toposolid-creation.md`'s own "Revit API members used" table only confirms the bare member names ([ForgeTypeId Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/d9fcf276-9566-de83-2b0b-d89b65ccc8af.htm)), not a declared signature; re-cited here because `RequireExactSchema` (see "Revit side") is this design's own new call site |

`SetVendorId`'s own `RevitAPI.xml` remark — "Since vendor IDs are not case sensitive, the string will be
converted to upper case before it is stored in the schema" — has no stated bearing on write-access
*enforcement* against a calling add-in's own manifest identity; that comparison's exact algorithm is
undocumented beyond the guide's one sentence at L81, which is exactly what manual evidence Step 10 below
observes.

**Write-access enforcement point.** Installed `RevitAPI.xml` documents two distinct exception types for the
identical write-access denial, both carrying the exact same message text ("Writing of Entities of this Schema
is not allowed to the current add-in."): the `Entity(Schema)` constructor itself throws
`Autodesk.Revit.Exceptions.InvalidOperationException`, while `Element.SetEntity`/`Element.DeleteEntity` each
separately throw `Autodesk.Revit.Exceptions.ArgumentException`. Because `Attach` (see "Revit side" below) and
any well-behaved caller alike construct a fresh `Entity` before calling `SetEntity`, a foreign-vendor write
attempt is expected to fail at construction, with `InvalidOperationException`, before `SetEntity` is ever
reached — see manual evidence Step 10 below, which corrects `revit-2027-verification-and-host-design.md`'s
manual test step 9 on this specific point (that step predicted the `SetEntity`-level `ArgumentException`
instead).

## Schema

Namespace `SolidGround.Core.Provenance`, static class `ExtensibleStorageProvenanceSchema`:

| Member | Value |
| --- | --- |
| `SchemaGuidText` (`const string`) | `bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e` — minted once at Stage 1's implementing commit (2026-09-21) and never regenerated |
| `SchemaGuid` (`static readonly Guid`) | `Guid.Parse(SchemaGuidText)` |
| `CurrentVersion` (`const int`) | `1` |
| `SchemaName` (`const string`) | `"SolidGround_Provenance_Toposolid"` |
| `VendorId` (`const string`) | `"SolidGround"` |
| `Documentation` (`const string`) | one sentence, passed to `SetDocumentation` |
| `ExtensibleStorageRoundTripTolerance` (`static readonly LinearDistance`) | `LinearDistance.Meters(1e-6)` — provisional, see "Core contract" |
| `Fields` (`static IReadOnlyList<ProvenanceFieldDefinition>`) | documentation-only order; every consumer looks fields up **by name** |

Sibling type `ProvenanceFieldDefinition` is a `readonly record struct(string Name, Type ClrType, bool
IsLengthSpec, string Documentation)`. Access levels are `AccessLevel.Public` read / `AccessLevel.Vendor` write,
restating the Provenance decision above.

**Schema-name rationale.** the owner's other add-in's own `SchemaBuilder.SetSchemaName` call sites never pass a dotted name
(`CenteredModelIdentityContract.cs:110-131` keeps a dotted, Core-only display name distinct from the
underscore-only name passed to the API). No source states `AcceptableName`'s exact rule, but this is strong
circumstantial evidence a dotted name risks rejection; `SchemaName` therefore uses underscores only, with no
dotted display name anywhere.

**VendorId comparison.** Revit stores `SetVendorId`'s argument upper-cased (cited above), so a schema this
add-in itself already published always reads back `schema.VendorId == "SOLIDGROUND"`. The exact-schema check
in "Revit side" below therefore compares `VendorId` case-insensitively and `SchemaName` ordinally — Revit does
not similarly normalize the schema name.

### Field table

36 fields, canonical order (documentation order only; every consumer keys by name). All are simple fields
(`ContainerType.Simple`; no array or map field is used). Fields 21, 22, 25, 26, and 27 carry
`FieldBuilder.SetSpec(SpecTypeId.Length)` and use the three-argument `Entity.Set<double>`/`Get<double>`
overload with `UnitTypeId.Meters`; every other field carries no spec, matching `SchemaBuilder.Finish()`'s rule
that only a spec-carrying field can have an "invalid units" problem. Field 9 (`horizontalDatum`) sources from
the **projected/target** datum, not the source geographic datum — a synthesizer correction the design record
originally flagged for owner review (design record §12 item 6); the orchestrator has since confirmed this
reading as final: `horizontalDatum` is the projected CRS datum paired with field 10's `horizontalCrsIdentifier`,
both from the same `TargetReference`.

| # | Field | CLR type | ES field type | Spec | Unit | Absent representation | Core source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | `schemaVersion` | int | Simple int | — | — | never absent | `ExtensibleStorageProvenanceSchema.CurrentVersion` |
| 2 | `sourceDatasetName` | string | Simple string | — | — | never absent (required) | `provenance.Source.SourceName` |
| 3 | `sourceDatasetIdentifier` | string | Simple string | — | — | never absent (required) | `provenance.Source.DatasetIdentifier` |
| 4 | `hasCollectionPeriod` | bool | Simple bool | — | — | n/a | `provenance.Source.CollectionPeriod is not null` |
| 5 | `collectionPeriodStartIso` | string | Simple string | — | — | `""` | `CollectionPeriod?.Start`, `"yyyy-MM-dd"` invariant |
| 6 | `collectionPeriodEndIso` | string | Simple string | — | — | `""` | `CollectionPeriod?.End`, `"yyyy-MM-dd"` invariant |
| 7 | `hasQualityLevel` | bool | Simple bool | — | — | n/a | `provenance.Source.QualityLevel is not null` |
| 8 | `qualityLevel` | string | Simple string | — | — | `""` | `provenance.Source.QualityLevel` |
| 9 | `horizontalDatum` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.TargetReference.Datum` (projected/local datum) |
| 10 | `horizontalCrsIdentifier` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.TargetReference.CoordinateReferenceSystem` |
| 11 | `sourceHorizontalReferenceOrigin` | string | Simple string | — | — | never absent | `provenance.SourceHorizontalReferenceOrigin.ToString()` |
| 12 | `verticalDatum` | string | Simple string | — | — | never absent | `provenance.SourceVerticalReference.Datum` |
| 13 | `hasVerticalGeoidModel` | bool | Simple bool | — | — | n/a | `provenance.SourceVerticalReference.GeoidModel is not null` |
| 14 | `verticalGeoidModel` | string | Simple string | — | — | `""` | `provenance.SourceVerticalReference.GeoidModel` |
| 15 | `sourceVerticalReferenceOrigin` | string | Simple string | — | — | never absent | `provenance.SourceVerticalReferenceOrigin.ToString()` |
| 16 | `originalPointCount` | int | Simple int | — | — | never absent | `provenance.OriginalPointCount` |
| 17 | `retainedPointCount` | int | Simple int | — | — | never absent | `provenance.RetainedPointCount` |
| 18 | `simplificationMethod` | string | Simple string | — | — | never absent | `provenance.SimplificationRequest.Method.ToString()` |
| 19 | `simplificationPointBudget` | int | Simple int | — | — | never absent | `provenance.SimplificationRequest.PointBudget` |
| 20 | `hasElevationRange` | bool | Simple bool | — | — | n/a (always true post-three-sample floor) | `provenance.ElevationRange is not null` |
| 21 | `elevationMinimumMeters` | double | Simple double | Length | meters | `0.0` | `ElevationRange.Minimum × LengthConverter.MetersPerUnit(SourceVerticalReference.Unit)` |
| 22 | `elevationMaximumMeters` | double | Simple double | Length | meters | `0.0` | `ElevationRange.Maximum × LengthConverter.MetersPerUnit(SourceVerticalReference.Unit)` |
| 23 | `outputUnitToken` | string | Simple string | — | — | never absent | `LengthUnitTokens.SettingsToken(LocalFrame.OutputUnit)` |
| 24 | `metersPerOutputUnit` | double | Simple double | — | — (ratio) | never absent | `LengthConverter.MetersPerUnit(LocalFrame.OutputUnit)` |
| 25 | `localOriginXMeters` | double | Simple double | Length | meters | never absent | `Origin.X × LengthConverter.MetersPerUnit(ProjectedHorizontalReference.Unit.LinearUnit!.Value)` |
| 26 | `localOriginYMeters` | double | Simple double | Length | meters | never absent | `Origin.Y ×` same horizontal factor |
| 27 | `localOriginElevationMeters` | double | Simple double | Length | meters | never absent | `Origin.Elevation × LengthConverter.MetersPerUnit(SourceVerticalReference.Unit)` |
| 28 | `horizontalForwardOperationFormat` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.ForwardOperation.Format` |
| 29 | `horizontalForwardOperationDefinition` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.ForwardOperation.Definition` |
| 30 | `horizontalInverseOperationFormat` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.InverseOperation.Format` |
| 31 | `horizontalInverseOperationDefinition` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.InverseOperation.Definition` |
| 32 | `horizontalTransformEngineName` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.EngineName` |
| 33 | `horizontalTransformEngineVersion` | string | Simple string | — | — | never absent | `provenance.HorizontalTransformation.EngineVersion` |
| 34 | `buildInformationalVersion` | string | Simple string | — | — | `"<unavailable>"` passthrough | ctor param, `BuildIdentity.Current.InformationalVersion` |
| 35 | `buildModuleVersionId` | string | Simple string | — | — | same | ctor param, `BuildIdentity.Current.ModuleVersionId` |
| 36 | `buildSha256` | string | Simple string | — | — | same | ctor param, `BuildIdentity.Current.Sha256` |

**Why fields 28-33 exist.** `docs/architecture/provenance-and-deterministic-exports.md` maps AGENTS.md's
"reversible local-origin offset" phrase to `LocalCoordinateFrame.Origin` **plus** the four
`HorizontalTransformationDefinition` values, flattened to six strings since `ForwardOperation`/
`InverseOperation` are each a `CoordinateOperationDefinition(Format, Definition)` record. Without them the
entity could reconstruct only the projected coordinate, never the geographic one. All six are plain,
non-nullable strings, well under Revit's 16 MB string limit. The mandatory in-hook check (see "Revit side")
still verifies only the **projected** leg; the manual evidence plan's Step 10 `GeoCheck` cross-check reads the
geographic leg from these fields instead.

**`outputUnitToken` naming fix.** Field 23 uses the same camelCase settings/JSON token as the placement
record's own `unitConversion.outputUnit` field (`"usSurveyFoot"`, etc., via `LengthUnitTokens.SettingsToken`)
— distinct from the CLI's own kebab-case `--unit`/`--vertical-unit` flag token (`"us-survey-foot"`,
`LengthUnitTokens.TokenOf`) and from the export bundle's PascalCase `enum.ToString()`. This design adds
`LengthUnitTokens.SettingsToken(LengthUnit)` to Core (extracted from `CreateToposolidCommand`'s former
private `LengthUnitToken` helper's identical one-line `JsonSerializer.Serialize` body), and both the placement
record and field 23 call it — locking those two to one spelling. The export bundle's own spelling is
unaffected; see "Deviations disclosure" below.

## Core contract

Three new files in `SolidGround.Core.Provenance`:

- **`ExtensibleStorageProvenanceSchema.cs`** — the static class above.
- **`ExtensibleStorageProvenanceValues.cs`** — `sealed record ExtensibleStorageProvenanceValues` carrying the
  36 properties, plus:
  - `static ExtensibleStorageProvenanceValues From(TerrainProvenance provenance, string
    buildInformationalVersion, string buildModuleVersionId, string buildSha256)` — the write path: unit
    conversion, ISO-date formatting, has-flag computation, and a defensive `double.IsFinite` guard on every
    Length field, throwing `ProvenanceFieldValueException` on failure. This guard is a real hazard, not
    defensive theater: a record `with`-expression bypasses any hand-written validating constructor, so
    `origin with { X = double.NaN }` can smuggle a non-finite value past an already-"validated"
    `TerrainProvenance`.
  - a plain public constructor for the read-back path — `SolidGround.Revit` calls `Entity.Get<T>` 36 times and
    builds this shape directly, never through `From`.
  - `static IReadOnlyList<string> Diff(ExtensibleStorageProvenanceValues expected, actual)` — exact compare
    for string/int/bool, `ExtensibleStorageRoundTripTolerance`-bounded for the five Length doubles, bit-exact
    for `metersPerOutputUnit`, collecting every mismatch and never short-circuiting, every numeric value
    `CultureInfo.InvariantCulture`-formatted matching `CreateToposolidCommand.cs`'s `"R"`-format convention.
  - `static IReadOnlyList<(string Field, double Delta)> ComputeDeltas(expected, actual)` — unconditionally
    reports every Length field's and the reconstruction check's actual delta, pass or fail, so a thrown
    exception always has a number to cite and Step 9 below always has a number to read.
  - `static Coordinate3D ReconstructSourceCoordinate(ExtensibleStorageProvenanceValues values, LocalCoordinate
    local)` — reimplements `LocalCoordinateFrame.ToSource`'s componentwise arithmetic from the flattened
    primitive fields. It deliberately never builds a real `HorizontalReference`/`LocalCoordinateFrame` (that
    would need `HorizontalReferenceKind`/`HorizontalAxisOrder`, neither stored in the entity) — confirmed
    unnecessary because `ToSource` never consults `AxisOrder`. Do not "fix" this with a typed reference object.
  - `static Coordinate3D ToSourceMeters(LocalCoordinateFrame frame, LocalCoordinate local)` — converts
    `frame.ToSource(local)`'s result to meters via `LengthConverter.MetersPerUnit`, so the in-hook
    reconstruction compare (see "Revit side") stays unit-consistent regardless of the source's own
    projected/vertical unit.
- **`ProvenanceFieldValueException.cs`** — 3-constructor style, matching `TerrainProvenanceException`.

**Per-axis meter normalization.** `LocalCoordinateFrame.ToSource` converts X/Y using the horizontal source
linear unit and Elevation using the vertical reference's own unit — two independently chosen units. Rather than
add a per-axis unit-token field, `From` converts once, in Core, using the correct per-axis factor
(`LengthConverter.MetersPerUnit(ProjectedHorizontalReference.Unit.LinearUnit!.Value)` for X/Y,
`LengthConverter.MetersPerUnit(SourceVerticalReference.Unit)` for Elevation). The entity is meters-in,
meters-out by construction, a deliberate sidestep rather than an oversight.

**Tolerance, provisional.** `ExtensibleStorageRoundTripTolerance = LinearDistance.Meters(1e-6)`. Rationale: if
`Entity.Set`/`Get<double>(name, value, UnitTypeId.Meters)` round-trips through an internal-unit conversion the
way `UnitUtils.ConvertToInternalUnits` does elsewhere — **unconfirmed by any 2027 API source**, only analogized
from a different surface — two chained IEEE-754 multiplications lose about 1 ULP each; at UTM-easting
magnitudes (roughly 1e5-1e6 m) that is about 1.1e-10 m absolute, so 1e-6 m leaves about four orders of margin,
roughly 20,000x tighter than the unrelated `ProjectedRoundTripTolerance` (0.02 m, a different ProjNET leg this
check never invokes). **This value is a placeholder, not a shipped constant**: Step 9 below must calibrate it
from more than one observed delta across at least two sessions, shipping an explicit 10x-100x multiple of the
largest delta actually observed.

**Canonical order.** `Fields`'s declared order is documentation-only. Both the schema build and every
compare — `RequireExactSchema`, the four-part read discipline, `Diff` — key every field **by name**, since
`Schema.ListFields()`'s return order is not documented to match insertion order.

## Revit side

Namespace `SolidGround.Revit.Provenance`, two classes plus one exception type:

- **`ProvenanceSchemaAdapter.cs`** (`internal static class`) — `EnsurePublishedSchema(): Schema` calls
  `Schema.Lookup(ExtensibleStorageProvenanceSchema.SchemaGuid)`.
  - If non-null: `RequireExactSchema(schema)` (private) compares `SchemaName` (ordinal, case-sensitive),
    `VendorId` (`OrdinalIgnoreCase`, see "Schema" above), `ReadAccessLevel`, `WriteAccessLevel`, and the full
    field set — **by name**, via `ListFields()`, never by position — checking each field's `ValueType` and its
    spec via `GetSpecTypeId().Empty()` for "no spec" (a confirmed sentinel) and `.TypeId` string equality
    (`GetSpecTypeId().TypeId == SpecTypeId.Length.TypeId`) for "has spec X" (matching this codebase's only
    other `ForgeTypeId`-identity precedent, `RevitUnitConversion.cs`/`CreateToposolidCommand.cs:164`); throws
    `ProvenanceAttachmentException` naming the first drift.
  - If null: the two static checks `SchemaBuilder.GUIDIsValid(guid)`/`.VendorIdIsValid("SolidGround")`;
    construct `new SchemaBuilder(guid)`; call `.AcceptableName(schemaName)`; chain
    `.SetSchemaName(...)`/`.SetVendorId("SolidGround")`/`.SetReadAccessLevel(AccessLevel.Public)`/
    `.SetWriteAccessLevel(AccessLevel.Vendor)`/`.SetDocumentation(...)` on that retained `SchemaBuilder`; per
    field, `.AddSimpleField(field.Name, field.ClrType)` returns a `FieldBuilder`, calling
    `.SetSpec(SpecTypeId.Length)` only where `IsLengthSpec`; finally `.Finish()` runs on the retained
    `SchemaBuilder`, never a per-field `FieldBuilder`.
- **`ProvenanceEntityWriter.cs`** (`internal static class`) — `internal static void Attach(Document document,
  Toposolid toposolid, TerrainExportPayload payload, PlacementRecordDraft placementDraft)`, matching
  `ToposolidCreatedHook` exactly (a bare method group, no lambda, no signature change):
  1. `expected = ExtensibleStorageProvenanceValues.From(payload.Provenance, BuildIdentity.Current
     .InformationalVersion, .ModuleVersionId, .Sha256)` — may throw `ProvenanceFieldValueException`.
  2. `schema = ProvenanceSchemaAdapter.EnsurePublishedSchema()`.
  3. Build `new Entity(schema)`; write all 36 fields via 36 explicit, individually statically-typed
     `Set<T>(name, value[, UnitTypeId.Meters])` call sites — not a reflective loop, since `Entity.Set`/`Get`
     are compile-time generic with no `Type`-parameterized overload, so `Fields`/`ClrType` drives only
     `AddSimpleField`/`RequireExactSchema`, never write or read; then `toposolid.SetEntity(entity)`.
  4. The **four-part read discipline**, applied to `toposolid` itself, immediately after `SetEntity`, still
     pre-commit: (a) `toposolid.GetEntitySchemaGuids()` contains `schema.GUID` — checked before trusting
     absence, since `Schema.Lookup(guid) == null` only proves the schema is unregistered in *this* session;
     (b) `Entity readBack = toposolid.GetEntity(schema); if (!readBack.IsValid()) throw ...`; (c) `if
     (!readBack.ReadAccessGranted()) throw ...`; (d) read the `schemaVersion` field **first** and compare to
     `CurrentVersion` before trusting any other field's shape.
  5. Read the remaining 35 fields, one explicit `Get<T>` call each, into a second
     `ExtensibleStorageProvenanceValues` (raw constructor, not `From`).
  6. `deltas = ComputeDeltas(expected, actual)`, computed **once, before either throwing step below**,
     `AddInLog.Info`-logged unconditionally (`InvariantCulture`-formatted) — computing it only after a
     passing `Diff`/reconstruction would mean a failure never reaches it.
  7. `Diff(expected, actual)` — throw `ProvenanceAttachmentException` citing `deltas` and naming every
     mismatch if non-empty.
  8. `ReconstructSourceCoordinate(actual, payload.Samples[0].Position)` vs.
     `ExtensibleStorageProvenanceValues.ToSourceMeters(payload.Provenance.LocalFrame,
     payload.Samples[0].Position)` — both meters-denominated regardless of source unit — componentwise, within
     `ExtensibleStorageRoundTripTolerance`; throw otherwise, citing `deltas` too (index 0 suffices: `ToSource`
     is the same closed-form affine formula for every sample, no per-point branching to miss). On success, log
     "Attached Extensible Storage provenance (schema {schema.GUID:D} v{CurrentVersion}) to element
     {toposolid.Id.Value}." after `deltas`.
- **`ProvenanceAttachmentException.cs`** — `internal sealed class : InvalidOperationException`, 3-constructor
  style matching `TerrainProvenanceException`/`ProvenanceFieldValueException`; used for schema drift,
  invalid/inaccessible entity, field mismatch, and reconstruction mismatch alike, each with its own specific
  `.Message`.

**Hook wiring**, `CreateToposolidCommand.cs:669-670` (the only place a runtime value changes hands outside the
new `Provenance/` files):

```csharp
ToposolidCreatedHook? postCreationHook = ProvenanceEntityWriter.Attach; // Issue #16.
postCreationHook?.Invoke(document, toposolid, outcome.Payload, draft);
```

`BuildPlacementDraft`'s `PlacementRecordDraft(...)` call (still before the hook) is a second, compile-time-
constant-only edit: it gains `ExtensibleStorageProvenanceSchema.SchemaGuidText`/`.CurrentVersion`, and its
private `LengthUnitToken` helper is replaced by `LengthUnitTokens.SettingsToken` (see "Field table" above).
Neither edit reorders the call sequence or changes the hook's signature.

**Transaction order** (unchanged from Issue #15, `RunTransaction`): `ToposolidCreationService.Create` ->
`document.Regenerate()` -> `PostCreationVerification.Verify` -> rollback-return on failure ->
`BuildPlacementDraft` -> **`ProvenanceEntityWriter.Attach` (this hook)** -> `transaction.Commit()`. `Attach`
therefore runs, and can roll back, entirely inside the still-open transaction, before the placement-record
file write or the final `OrphanCheck.Unchanged` comparison — both of those happen later, in `ReportSuccess`,
only after a confirmed `Committed` status.

**Isolated-context note.** `UseRevitContext=false` governs each add-in's own managed-dependency resolution,
not Revit's single, process-wide `RevitAPI.dll` (`Private=false` everywhere) — every add-in shares one copy of
`Schema`/`Entity`/`AccessLevel`, so no cross-context split is expected here. Manual evidence Steps 10 and 13
below are the tripwire if that assumption is ever wrong.

## Failure semantics

No new catch clause, no new dialog, and no `ToposolidCreatedHook` signature change: every new exception throws
from inside the existing hook call site, caught by `RunTransaction`'s general catch
(`CreateToposolidCommand.cs:708`), which already derives `Result` from `TransactionStatus` per that note's
row 21 — only `ex.Message` differs per failure. Rows 21a-21c below are sub-cases of that note's own row 21
("`Regenerate()`, the Issue #16 hook, or `Commit()` itself throwing"), sharing its catch, its `Result`
derivation, and its dialog headline ("SolidGround hit a problem while finishing the toposolid.").

| # | Scenario | Stage | `Result` | What is shown |
| --- | --- | --- | --- | --- |
| 21a | `EnsurePublishedSchema` fails (`Finish()` throws, or an existing schema fails `RequireExactSchema`), or the four-part read discipline finds the entity missing, invalid, unreadable, or version-mismatched right after `SetEntity` | Transaction (hook) | Cancelled if `RolledBack`, else Failed | row 21's headline + which aspect drifted, precondition failed, or read-discipline check failed |
| 21b | A length field's computed value is not finite (`ProvenanceFieldValueException`, including a `with`-expression bypass), detected in Core before the entity is built | Transaction (hook) | same | same headline + "field '\<name\>' is not a finite number." |
| 21c | Entity attached, but the read-back field compare or the source-coordinate reconstruction fails `ExtensibleStorageRoundTripTolerance` | Transaction (hook) | same | same headline + field name(s)/delta, or the reconstructed-vs-expected delta in meters, always drawn from the already-computed `deltas`, `CultureInfo.InvariantCulture`-formatted, matching `CreateToposolidCommand.cs`'s `"R"`-format convention |

Every one of 21a-21c is **fatal**: a correctly fetched and simplified toposolid is rolled back over a
provenance-attachment problem alone. This is a stronger trade than the non-fatal placement-JSON/orphan-check
precedent (`revit-toposolid-creation.md` rows 23-24, both "logged only" on an otherwise-`Succeeded` run) — the
recorded reason is AGENTS.md's "reversible provenance" mission plus a free pre-commit guarantee, upheld
unanimously by all three judges and all four proposals in Issue #16's design record. The owner should
consciously accept this: it means a genuinely good terrain fetch and simplification can still be destroyed by
a transient metadata bug.

## Placement-record change

New nested record `PlacementExtensibleStorageRecord(string SchemaGuid, int SchemaVersion)` — no "attached"
boolean, no digest field. A placement JSON file can only exist once attachment has succeeded (21a-21c are
fatal), so the schema-GUID/version pair alone already tells a reader "provenance was attached"; a redundant
boolean was considered and dropped 2-1 in review (API-correctness/Compliance: redundant under a fatal
attach-or-rollback design; Evidence: keep it anyway) — a one-line add-back remains available if a future
non-fatal-attach path ever needs one. No digest field either: a capturing-lambda digest mechanism from a
losing proposal was not adopted.

Added to `PlacementRecordDraft`/`PlacementRecord` (`SolidGround.Core.Provenance`), rendered by
`PlacementRecordRenderer` as `"extensibleStorage": { "schemaGuid": "bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e", "schemaVersion": 1 }`,
appended after `pointCounts` — the placement record's last existing top-level key.
`PlacementRecordDraft.SchemaVersion`/`PlacementRecord`'s constant bumps **1 -> 2**, since the record's required
shape changed (this is the Revit placement record's own schema version, distinct from
`TerrainProvenance.CurrentSchemaVersion`, which stays 2 and is untouched by this issue). Both new values are
Core compile-time constants, so `BuildPlacementDraft` — which runs before the hook — references them directly
with no reordering or signature change.

## `DeleteEntity`, host delete/Undo, and host copy

`Element.DeleteEntity(Schema): bool` — RevitAPI.xml: "True if entity was deleted, false if entity didn't
exist". A first call against a toposolid carrying the SolidGround entity is expected to return `true`; an
immediate second call on the same element, `false`. Deleting the entity does not delete the host `Toposolid`
itself, and `Attach` never calls `DeleteEntity` anywhere in the shipped add-in — it exists on `Element` for a
future maintenance/repair path, exercised here only by the manual evidence plan's `EsDeleteHost` probe command
(Step 12(c) below).

**Host delete and Undo.** Deleting the host `Toposolid` element is expected to remove its attached entity with
it; pressing Undo in Revit's own UI is expected to restore the element and the entity together, byte for byte,
since Extensible Storage data is ordinary element data subject to the same Undo mechanism as any other
property. Step 12(a) below is the first evidence-gathering opportunity for this specific schema — not a new
discovery, since Undo's general element-restore behavior is already well established elsewhere in this
codebase (`revit-toposolid-creation.md`, "Step 5").

**Host copy.** The Issue #16 design record's own round-3 API review (finding 2) states that `Entity`'s
`RevitAPI.xml` remarks document a full duplicate of an element's Extensible Storage entities on copy; this
note carries that finding forward as-is and does not itself independently reflect or quote the remark. If it
holds, a copied toposolid keeps its original's exact provenance verbatim, unedited — Step 12(b) below is this
specific schema's first opportunity to confirm (or correct) it. Consequence, if confirmed: a copy's provenance
would describe its *original's* acquisition, not the copy's own (nonexistent) one; see "Known limitations."

## Deviations disclosure

The entity's five Length fields (21, 22, 25-27) are always stored and read back in **meters**, via the
per-axis normalization in "Core contract" above. The placement record's `localOrigin.sourceX/sourceY/
sourceElevation` and the terrain export bundle's own provenance fields keep their **raw source units**
(meters today, for the ExampleSite fixture, but not guaranteed for a future non-metric source). For a metric
source the entity and the placement record/export bundle agree numerically; for a non-metric source they will
legitimately differ by the source's own meters-per-unit factor. Each artifact stays internally consistent —
this is a disclosed, deliberate unit boundary, not a bug, and `ToSourceMeters` (see "Core contract") exists
specifically so the in-hook reconstruction check compares like-for-like despite it.

Separately, `outputUnitToken` (field 23) and the placement record's `unitConversion.outputUnit` now share one
spelling via `LengthUnitTokens.SettingsToken` (see "Field table" above); `TerrainExportBundleRenderer.cs:285`
still writes a bare `enum.ToString()` (PascalCase) for the same concept and is **not** touched by this issue —
a third, uncoordinated spelling for the same idea persists there, disclosed rather than fixed, to hold this
issue's scope.

## Tests

All 26 tests are offline, Linux-CI-runnable, `sealed <Subject>Tests` in `tests/SolidGround.Tests/`, plain
`Assert.*`; the full offline suite (`SolidGround.Tests`) stood at 939 tests once Stage 2 landed (938 passed, 1
skipped by design — the live OpenTopography test). Six of the rows below have no counterpart in the design
record or this note's original draft — Stage 1's own review rounds added them, and the shipped code is
authoritative here. Revit-side code (`ProvenanceSchemaAdapter`, `ProvenanceEntityWriter`) has zero automated
tests — `SolidGround.Tests` is architecturally barred from referencing `RevitAPI`, matching
`ToposolidCreationService`/`PostCreationVerification` precedent; it is compile-checked only by the Nice3point
CI gate, with all real behavior deferred to the manual evidence plan below.

| Test | Proves |
| --- | --- |
| `ExtensibleStorageProvenanceSchemaTests.FieldListHasNoDuplicateNamesOrTypes` | the 36-entry `Fields` list has no name collision `AddSimpleField` would reject, and every field's CLR type is one of the four supported simple types |
| `...EveryLengthSpecFieldIsADoubleField` | only `double` fields carry `IsLengthSpec`, and exactly 5 fields do |
| `...FieldCountStaysUnderRevitsTwoHundredAndFiftySixFieldLimit` | 36 stays far under `Finish()`'s 256-field exception |
| `...SchemaNameContainsNoPunctuationRevitMightReject` | regression-locks the owner's other add-in dotted-name lesson via an exact-literal assertion plus a per-character check (a bare per-character loop alone would still pass an accidentally-emptied constant) |
| `ExtensibleStorageProvenanceValuesTests.FromProducesExpectedValuesForAExampleSiteLikeProvenance` | `From` against the real pipeline and the committed `example-site-synthetic.*` fixture, not a hand-built object |
| `...FromThrowsWhenAWithExpressionSmugglesANonFiniteOrigin` | the `with`-expression bypass hazard is caught for the local origin's X ordinate |
| `...FromThrowsWhenAWithExpressionSmugglesANonFiniteOriginY` | sibling coverage for the Y ordinate (only the X call site was originally exercised; a swapped field-name literal on another `RequireFinite` call site would otherwise compile and pass) |
| `...FromThrowsWhenAWithExpressionSmugglesANonFiniteOriginElevation` | sibling coverage for the elevation ordinate |
| `...FromThrowsWhenAnElevationRangeMinimumIsSmuggledNonFinite` | the same bypass hazard for `ElevationRange.Minimum` |
| `...FromThrowsWhenAnElevationRangeMaximumIsSmuggledNonFinite` | the same bypass hazard for `ElevationRange.Maximum` |
| `...FromRepresentsAbsentCollectionPeriodQualityLevelAndGeoidModelExplicitly` | the false branch of every `hasX` flag |
| `...FromRepresentsPresentCollectionPeriodQualityLevelAndGeoidModelExplicitly` | the true branch |
| `...CollectionPeriodDatesAreCultureInvariant` | de-DE vs. invariant date formatting |
| `...OutputUnitTokenMatchesThePlacementRecordsOwnTokenForEveryLengthUnit` | field 23 and the placement record share one spelling |
| `...HorizontalDatumIsTheProjectedTargetDatumNotTheSourceGeographicDatum` | field 9's corrected source expression |
| `...DiffReportsMismatchesAtAndJustPastTheRoundTripTolerance` | the tolerance boundary itself, inclusive at exactly the tolerance |
| `...DiffDetectsMismatchesFromStringIntBoolAndExactDoubleComparators` | one mismatch case each for `CompareString`/`CompareInt`/`CompareBool`/`CompareExactDouble` (a broken comparator for any of the other 31 fields would previously go undetected) |
| `...DiffAndComputeDeltasMessagesAreCultureInvariant` | mirrors the date test for `Diff`/row 21c's delta text |
| `...ComputeDeltasReportsEveryLengthFieldRegardlessOfTolerance` | `ComputeDeltas` never short-circuits |
| `...ReconstructSourceCoordinateMatchesLocalCoordinateFrameToSourceForARetainedExampleSiteSample` | the flattened reconstruction matches real `ToSource`, fixture-anchored (all-meters case) |
| `...ReconstructSourceCoordinateMatchesToSourceMetersForSyntheticNonMeterUnits` | a synthetic non-fixture US-survey-foot/international-foot case proving `ToSourceMeters` still matches when the per-axis conversion factors are not 1.0, and separately checks the two elevation Length fields the reconstruction path itself never reads |
| `...RawConstructorMatchesFromForAnEquivalentInstance` | the read-back-only raw constructor agrees with `From` (`Diff` is empty) |
| `ArchitectureTests.CoreProvenanceContractNeverReferencesAnyAutodeskOrRevitType` | the new leak guard, mirrors the NetTopologySuite/ProjNET template |
| `PlacementRecordRendererTests.WritesTheExtensibleStorageCrossReferenceAfterPointCounts` | the new JSON block's position and field values |
| `...DocumentPropertyOrderMatchesTheManifestAtEveryNestingLevel` | existing test, literal array updated |
| `...SchemaVersionIsNowTwo` | the 1 -> 2 bump |

`RevitHostFilesTests.NoRevitSourceFileReferencesExtensibleStorageTypesYet` — Issue #15's design record
§1.2/§10.3 backstop for "#15 ships zero Extensible Storage code" — stood in `RevitHostFilesTests.cs` until
this Stage 2 commit attached real Extensible Storage code to `SolidGround.Revit`; its own doc comment called
it "deliberately removed when #16 lands," so it was removed rather than updated. `RevitHostFilesTests.cs`
keeps an explanatory comment in its place recording why.

## Verification

`dotnet restore SolidGround.slnx --locked-mode`, `dotnet build SolidGround.slnx --configuration Release
--no-restore`, and `dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration
Release --no-build` were run against the code this note describes and all pass: 0 warnings, 0 errors, and the
full offline suite green (939 tests: 938 passed, 1 skipped by design — the live OpenTopography test, which
requires `SOLIDGROUND_OPENTOPOGRAPHY_LIVE=1` and a real key). The CI-shaped compile gate —
`dotnet restore SolidGround.slnx --locked-mode -p:UseRevitReferenceAssemblies=true` then `dotnet build
SolidGround.slnx --configuration Release --no-restore -p:UseRevitReferenceAssemblies=true`, matching
`.github/workflows/ci.yml` — was also run and passes with 0 warnings, and `src/SolidGround.Revit/obj/project.assets.json`
confirms the resulting build actually resolved the CI-only, exact-pinned `Nice3point.Revit.Api.RevitAPI`/
`RevitAPIUI` packages rather than the local `RevitInstallDir` HintPath references, with no change to the gate
mechanism itself. None of this is a substitute for the manual evidence plan below: every check here is
offline and Revit-free by construction (`SolidGround.Tests` never launches Revit, and neither compile gate
exercises runtime behavior), which is exactly why the manual plan exists as a separate, later step.

## Sources

- Autodesk, "Extensible Storage" (Revit 2027 API Developers Guide):
  `https://help.autodesk.com/view/RVT/2027/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Storing_Data_in_the_Revit_model_Extensible_Storage_html`,
  firecrawl-fetched 2026-09-21.
- Autodesk, Revit 2027 API changes: `https://help.autodesk.com/view/RVT/2027/ENU/?guid=f7165618-24c9-4160-a7a4-09979fe4a981`.
- Installed `RevitAPI.dll`, version `27.0.10.13`, SHA-256
  `BB4A5B3DEC4E140527C311FBAFC2E676E34594330748BE79C0654A46D521CB89`, reflected via
  `System.Reflection.MetadataLoadContext`, `C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll`.
- Installed `RevitAPI.xml` doc comments for `SchemaBuilder.Finish()`, `Element.DeleteEntity`,
  `Element.SetEntity`, the `Entity(Schema)` constructor, and `SetVendorId`.
- AGENTS.md, "Provenance decision" section.
- `docs/architecture/revit-add-in-conventions.md`, section 11 ("Provenance and Extensible Storage") and owner
  decision 7.
- `docs/architecture/revit-2027-verification-and-host-design.md`, item 8 and its manual test step 9.
- `docs/architecture/revit-toposolid-creation.md`: "The Issue #16 extension point," "Error catalogue,"
  "Manual evidence plan," "Revit API members used."
- `docs/architecture/provenance-and-deterministic-exports.md`, "Reconstructing source coordinates."
- Issue #16 design record (not committed to this repository; synthesized 2026-09-21 from four independent
  proposals and three independent judge reviews across three review rounds).

## Manual evidence plan (continues Steps 1-8b)

Steps 9 through 13 below continue `revit-toposolid-creation.md`'s own Step 1-8b sequence — not a new,
separately numbered plan — and collectively settle `revit-2027-verification-and-host-design.md`'s item 8
(Extensible Storage API shape), whose own manual test step 9 named the round trip, `DeleteEntity`, and
cross-vendor access as its unexercised acceptance criteria. Preconditions restated once: the owner's go-ahead
before any Revit launch; the fixed interference check (`MainWindowTitle -match 'using your computer'` plus
`Get-RevitProcesses.ps1`) before every launch and click; the OpenTopography key, if needed, only via
`Start-Revit2027.ps1 -EnvironmentVariable`, never typed or logged; never read `.env`; never print a key. Artifacts land
under `...\solidground-issue16\evidence\`, `EVIDENCE-ES.md`, `NN-description.ext`. Every "**Evidence.**"
paragraph below is a placeholder; Stage 4 fills each in from a real Revit 2027 session.

Steps 10, 12, and 13 below drive `SolidGroundProbe` (Issue #15's same throwaway add-in, kept entirely outside
this repository), which gains a new "ES Probes" ribbon panel with six commands for this issue: `EsDump` (the
four-part read discipline from "Revit side" above, plus a full field dump to a text file, deliberately
Core-free); `EsSpotCheck` (a side-by-side comparison of an `EsDump` output against Step 9's known inputs);
`EsForeignWrite` (one rolled-back attempt to construct the entity under `SolidGroundProbe`'s own, different
`VendorId`); `EsCopyHost` (copies the host toposolid and dumps the copy's entity); `EsDeleteHost` (calls
`Element.DeleteEntity(Schema)` on the host toposolid, run under the vendor-matching manifest identity below,
since `DeleteEntity` shares `SetEntity`'s `AccessLevel.Vendor` gate); and `EsPoisonSchema` (registers a
same-GUID, differently-shaped schema, used by both Step 13 scenarios). `GeoCheck`, used only by Step 10's own
required geographic cross-check, is a separate throwaway console application — not a `SolidGroundProbe`
ribbon command and not a shipped `SolidGround.Cli` verb, an explicit orchestrator ruling, since this
cross-check is a one-off manual-evidence tool, not a durable product capability.

### Step 9 — first write, `process` mode

1. Deploy the Issue #16 build; run `CreateToposolidCommand` against `example-site-synthetic.*`; confirm the
   success dialog with none of rows 21a-21c triggered.
2. Save the document for the first time in this project's history, to the evidence folder, by the owner's own
   direct interaction with Revit's Save-As dialog, not the scripted harness, which every prior session used
   specifically to avoid this exact prompt.
3. Check the add-in's `%ProgramData%` log for the new "Attached Extensible Storage provenance..." line and its
   always-logged per-field `ComputeDeltas` output; confirm the placement JSON's new
   `extensibleStorage{schemaGuid, schemaVersion}` block.
4. Record the deltas. If row 21c fires instead, a real possibility under a fatal attach-or-rollback design,
   read the logged deltas, raise `ExtensibleStorageRoundTripTolerance` above the observed number with margin,
   rebuild, redeploy, retry.
5. **Control run.** With the document still open, run `CreateToposolidCommand` a second, unmodified time
   against the same document, confirming `RequireExactSchema`'s non-null branch accepts SolidGround's own
   already-published schema.
6. **Relaunch sample (required).** Close Revit, redeploy the unchanged build, relaunch, and run
   `CreateToposolidCommand` once more, purely for one cross-process delta sample; same-session samples alone
   cannot finalize the tolerance calibration.

**Pass criteria.** Every field round-trips within `ExtensibleStorageRoundTripTolerance`; the control run and
the relaunch sample both succeed with none of rows 21a-21c firing; the shipped tolerance is set from at least
two independent sessions' deltas with a 10x-100x margin over the largest one observed.

**Evidence.** Pending Stage 4.

### Step 10 — same-session cross-vendor read via the probe, plus the required `GeoCheck` geographic cross-check

1. `SolidGroundProbe`'s manifest `VendorId` is already `"SolidGroundProbe"` (no edit needed). Run `EsDump`,
   which applies the four-part read discipline from "Revit side" above, then `ListFields()` plus a switch on
   `Field.ValueType`/`GetSpecTypeId()` dumping every field to a text file with no `SolidGround.Core`
   reference.
2. Run `EsSpotCheck`, which presents the `EsDump` output side by side with Step 9's known inputs: exact match
   for string/int/bool fields, same order of magnitude by eye for the five Length doubles, not
   tolerance-precise, since the probe is deliberately Core-free and cannot call `Diff`/`ComputeDeltas`; only
   Step 9 stays tolerance-precise.
3. Run `EsForeignWrite`: in a trivial transaction, always rolled back, never committed, construct
   `new Entity(schema)` from `SolidGroundProbe`'s own, different `VendorId`, to observe `AccessLevel.Vendor`
   enforcement. Expect the documented `Autodesk.Revit.Exceptions.InvalidOperationException` ("Writing of
   Entities of this Schema is not allowed to the current add-in.") at construction itself, before `SetEntity`
   is ever reached — installed `RevitAPI.xml` documents this exception on the `Entity(Schema)` constructor
   directly, distinct from (though textually identical to) `Element.SetEntity`/`Element.DeleteEntity`'s own
   `Autodesk.Revit.Exceptions.ArgumentException` for the same denial (see "Revit 2027 API surface used" and
   "Write-access enforcement point" above). `docs/architecture/revit-2027-verification-and-host-design.md`'s
   own manual test step 9 predicted the `SetEntity`-level `ArgumentException` specifically; this note corrects
   that prediction for the construction-time path now that the exact `Entity` constructor's own documented
   exception is known. Read back and diff every field against Step 9's dump before Step 11.
4. **Required: geographic cross-check via `GeoCheck`.** `GeoCheck` (see the "ES Probes" paragraph above)
   references `SolidGround.Core` directly — unlike the deliberately Core-free `SolidGroundProbe` — to
   independently run the full geographic reconstruction leg against dumped fields 28-33 (the horizontal
   forward/inverse operation format, definition, engine name, and version), compared within
   `GeographicRoundTripToleranceDegrees`; the fuller reconstruction the fatal in-hook check (row 21c)
   deliberately never attempts (see "Revit side" and "Known limitations"). The design record itself scored
   this recommended and non-blocking; this plan promotes it to a required step.

**Pass criteria.** The probe's dump matches Step 9 to the stated precision; `EsForeignWrite`'s attempt throws
the documented `InvalidOperationException` at `Entity` construction and is confirmed rolled back, never
committed; `GeoCheck`'s delta is within `GeographicRoundTripToleranceDegrees`.

**Evidence.** Pending Stage 4.

### Step 11 — save/reopen, `SolidGround.Revit` absent

1. Close Revit; disable or rename the shipped manifest so only the probe loads; reopen the file Step 9 saved;
   re-run `EsDump`, recording whether `Schema.Lookup` is null on the first call before it succeeds, the
   false-negative risk the read discipline's `GetEntitySchemaGuids()`-first check exists for.
2. Confirm every field matches Step 9's dump to the same eyeballed precision as Step 10.

**Pass criteria.** The schema and every field are readable with `SolidGround.Revit` entirely absent from the
session, confirming `AccessLevel.Public` read does not depend on the publishing add-in's own presence.

**Evidence.** Pending Stage 4.

### Step 12 — deletion, Undo, and copy

Against the still-intact Step 9/11 host, in this order, since a delete-first order would destroy the entity
before the later sub-tests could observe it intact:

1. **(a) Host delete and Undo.** The owner deletes the host toposolid via Revit's own UI, then presses Undo
   (both owner actions between two probe commands, not new probe commands, per Step 9's Save-As precedent);
   run `EsDump` again, proving the entity undoes with the element byte-identically.
2. **(b) Host copy.** The owner copies the host toposolid via Revit's own UI; run `EsCopyHost` to record the
   copy's entity verbatim, confirming or correcting the carried-over `RevitAPI.xml` full-duplicate claim
   discussed under "`DeleteEntity`, host delete/Undo, and host copy" above for this specific schema, and
   still recording the duplicate-provenance caveat (see "Known limitations").
3. **(c) Vendor-mismatched delete.** From a second probe manifest, `SolidGroundProbeVendorMatch.addin` (a
   fresh `AddInId`,
   `d3095691-b922-4287-9746-3a13099cc2e2`, and `VendorId="SOLIDGROUND"` alongside `SolidGroundProbe`'s own
   differing `VendorId`), needed because `DeleteEntity` shares `SetEntity`'s `AccessLevel.Vendor` gate and
   `SolidGroundProbe`'s own manifest cannot call it, run `EsDeleteHost` (`toposolid.DeleteEntity(schema)`)
   once (expect `true`), then again on the same element (expect `false`).

**Pass criteria.** The entity survives host delete plus Undo byte-for-byte; the copy carries a verbatim
duplicate; `DeleteEntity` returns `true` then `false` exactly as its `RevitAPI.xml` remark states.

**Evidence.** Pending Stage 4.

### Step 13 — forced schema mismatch, two scenarios with opposite preconditions

The two scenarios never run back-to-back; each needs the opposite session precondition.

1. **Scenario 1 (Revit's own protection).** After Step 9 or 10, with the schema already registered
   in-session, run `EsPoisonSchema` to build a same-GUID schema with a different field set. Confirm
   `Finish()`'s "a different Schema with a matching identity already exists" exception fires there, before
   `CreateToposolidCommand` runs again.
2. **Scenario 2 (`RequireExactSchema`'s own check).** In a fresh session where nothing is registered under
   this GUID, run `EsPoisonSchema` to pre-register a drifted schema first; then `CreateToposolidCommand` runs,
   so its own `Schema.Lookup` finds the drift and row 21a fires via `RequireExactSchema`, not an uncaught
   exception.

**Pass criteria.** Scenario 1 reproduces `Finish()`'s documented identity-conflict exception; Scenario 2
reproduces row 21a's own dialog text, not a raw unhandled exception.

**Evidence.** Pending Stage 4.

**Deferred, disclosed.** The same-vendor-different-case write-success scenario (a third add-in whose manifest
`VendorId` is "SolidGround" in a different letter case, for example "SOLIDGROUND", successfully writing the
entity) stays out of scope for this issue's acceptance criteria; Step 10's cross-vendor refusal test already
answers the corresponding read/refusal half. Left for a future issue, reusing Step 12(c)'s vendor-matching
manifest (`SolidGroundProbeVendorMatch.addin`) rather than minting a third add-in.

### Decisions recorded from evidence

**Not yet available.** This documentation stage did not launch Revit and did not run Steps 9-13 above, so no
decision below is yet settled by observation. Once Stage 4's manual evidence lands, this section is filled in
with one bold-titled bullet per settled decision — at minimum, the calibrated `ExtensibleStorageRoundTripTolerance`
value and its margin (Step 9), whether `AccessLevel.Vendor` write enforcement matches the predicted
`InvalidOperationException` at `Entity` construction and `GeoCheck`'s own observed delta (Step 10), the `Schema.Lookup`
false-negative outcome with `SolidGround.Revit` absent (Step 11), the host delete/Undo/copy outcomes (Step
12), and the two forced-schema-mismatch scenarios' outcomes (Step 13) — each with a "See Step N above"
backreference, mirroring `revit-toposolid-creation.md`'s own "Decisions recorded from evidence (2026-09-21)"
section exactly in shape.

## Known limitations

- **Worksharing/central-model behavior is unexplored.** `SetEntity`/`AccessLevel.Vendor` behavior in a
  workshared, central-model document is out of scope here; flagged for a future issue before targeting
  workshared models.
- **A copied toposolid is expected to keep its original's provenance unchanged.** Carried over from the design
  record's own API review (`Entity`'s `RevitAPI.xml` remarks, not independently reflected or quoted by this
  note; see "`DeleteEntity`, host delete/Undo, and host copy" above), to be confirmed or corrected by Step
  12(b): the copy's entity is expected to describe the original's acquisition, not its own. No mitigation
  ships in this milestone.
- **Provenance goes stale after an edit.** This issue documents the toposolid at creation time only; once
  toposolid editing exists elsewhere in the product, an edited element's unchanged provenance becomes an
  AGENTS.md accuracy question this issue does not solve.
- **The in-hook reconstruction check is projected-only.** The mandatory, fatal check inside `Attach` verifies
  only the projected leg (`ToSourceMeters`/`ReconstructSourceCoordinate`); the full geographic leg (fields
  28-33's forward/inverse operation definitions) is verified only by Step 10's `GeoCheck` cross-check, not
  gated on every run. A future issue may promote it into the fatal hook itself via
  `ProjNetHorizontalCoordinateTransformFactory`.
- **`ExtensibleStorageRoundTripTolerance` mechanics are unconfirmed.** No 2027 API source states whether
  `Entity.Set`/`Get<double>(..., UnitTypeId.Meters)` round-trips through an internal-unit conversion at all;
  the 1e-6 m value is a first-principles estimate pending Step 9's multi-sample calibration.
- **The array-field container type remains unresolved** (inherited from
  `revit-2027-verification-and-host-design.md` item 8), irrelevant here since every field is
  `AddSimpleField`, but still open for any future schema that needs one.
- **A third, uncoordinated `outputUnit` spelling persists in the terrain export bundle**
  (`TerrainExportBundleRenderer.cs:285`'s bare `enum.ToString()`), disclosed under "Deviations disclosure" and
  not fixed by this issue.
- **The same-vendor-different-case write-success scenario stays deferred**, disclosed rather than dropped;
  see "Manual evidence plan," Step 13's closing note.

## What this note does not do

This note records Issue #16's design and error catalogue; it does not itself implement Stage 1 (Core) or
Stage 2 (Revit and the placement record) — those stages' own commits do, and this Stage 3 reconciliation pass
has been brought in line with them line by line, the shipped code treated as authoritative wherever it
differed from this note's original draft or the design record (see "Status" above). Both invented-name
placeholders originally left in the manual evidence plan above (Steps 10 and 12(c)) are now resolved:
`EsDump`/`EsSpotCheck`/`EsForeignWrite`/`EsCopyHost`/`EsDeleteHost`/`EsPoisonSchema` on `SolidGroundProbe`'s
new "ES Probes" panel, and the `SolidGroundProbeVendorMatch.addin` vendor-matching manifest. Still pending:
Stage 4 fills in every blank "Evidence" paragraph from a real Revit 2027 session, and populates "Decisions
recorded from evidence" above, left as a pending stub here for the same reason: no evidence yet exists to
record. It does not change any decision recorded in
`docs/architecture/revit-add-in-conventions.md`, `docs/architecture/revit-2027-verification-and-host-design.md`,
or `docs/architecture/revit-toposolid-creation.md`. It does not begin Issue #17 (signing, packaging, or a
clean-install validation) or Issue #19 (the real ribbon icon design). It does not close Issue #16 itself;
closure is recorded on GitHub, once Stage 4 lands, not in this note.
