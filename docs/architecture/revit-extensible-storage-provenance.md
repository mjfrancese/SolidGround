# Revit Extensible Storage provenance

**Status.** Stages 1 (Core), 2 (Revit and the placement record), 3 (this note), and 4 (manual evidence) have all
landed; **Issue #16's Revit 2027 evidence is complete as of 2026-09-23.** This note was originally drafted in
Stage 3 ahead of Stages 1 and 2's own commits (design record §9); it has since been reconciled line by line
against the shipped code, which is authoritative over both this note's original draft and the design record
wherever the three differ. The "Manual evidence plan" section's "Evidence" paragraphs stayed blank — Pending
Stage 4 — until real Revit 2027 sessions filled them in: a 2026-09-23 session filled in Steps 9, 10, 11, 12, and
13.1 below, and two further 2026-09-23 segments completed Step 13.2 and its recovery after an intermittent
crash (unrelated to Step 13.2 itself) delayed them (see "Decisions recorded from evidence").
Unlike `revit-toposolid-creation.md`'s dedicated "Basis" and "Design decisions with
reasons" headings, this note compresses that same material — the multi-proposal/judge synthesis and the
per-decision rationale — into this Status paragraph and into bolded inline asides inside the sections below
(for example "Schema-name rationale," "VendorId comparison," "Per-axis meter normalization," "Tolerance,
provisional"); this is a deliberate word-budget choice, not an omitted section.

**Correction, 2026-09-23 (Issue #16 round 1 fix).** The first real Revit 2027 run of the shipped build (deployed
from commit `9622ac8`) failed at Step 9.1 below: `SchemaBuilder.Finish()` rejected the schema with "Units are
required for field metersPerOutputUnit," because this note's original design — `metersPerOutputUnit` and every
non-Length double getting no spec at all — was wrong: Revit 2027 requires **every** floating-point field to
carry a spec, not only the five Length fields. No schema was ever published under this GUID (`Finish()` threw
before any document registered it), so this is a correction of an unpublished, never-shipped definition, not a
schema evolution; the GUID and `CurrentVersion` are unchanged. `metersPerOutputUnit` now carries
`ProvenanceFieldSpec.Number` (`SpecTypeId.Number`, read/written with `UnitTypeId.General`), a new
`FieldBuilder.NeedsUnits()`/`UnitUtils.IsValidUnit` publish-time assertion catches a bad spec/unit pairing
before `Finish()` would, and `Diff`'s `metersPerOutputUnit` comparison changed from bit-exact to
tolerance-bounded (the same `ExtensibleStorageRoundTripTolerance` the five Length fields use) because the
bit-exact treatment's own stated premise — "no spec, so no internal-unit round trip to absorb" — no longer
holds. This note's "Field table," "Core contract," "Revit side," and "Tests" sections below are updated in
place to describe the corrected, three-state (`None`/`Length`/`Number`) model directly, rather than being left
to describe the disproven two-state model with only this paragraph noting the difference.

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

Sibling type `ProvenanceFieldDefinition` is a `readonly record struct(string Name, Type ClrType,
ProvenanceFieldSpec Spec, string Documentation)` (2026-09-23: replaced a bool-only `IsLengthSpec` flag — see
"Correction" above). `ProvenanceFieldSpec` is a three-member enum: `None` (valid only for a non-`double`
field), `Length` (`SpecTypeId.Length`, `UnitTypeId.Meters`), and `Number` (`SpecTypeId.Number`,
`UnitTypeId.General` — a dimensionless ratio that Revit 2027 still requires a spec for, being a `double`).
Access levels are `AccessLevel.Public` read / `AccessLevel.Vendor` write, restating the Provenance decision
above.

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
overload with `UnitTypeId.Meters`; field 24 (`metersPerOutputUnit`) carries `FieldBuilder.SetSpec(SpecTypeId.Number)`
and uses the same three-argument overload with `UnitTypeId.General`; every other field is a non-`double` type
and carries no spec at all. **The rule is the opposite of what this note originally said** (see "Correction,
2026-09-23" above): `SchemaBuilder.Finish()` requires **every floating-point field** to carry a spec and throws
"At least one field has invalid units" (RevitAPI.xml) when one does not — it is a spec-less `double` field that
is the "invalid units" problem, not a spec-carrying one. `ProvenanceSchemaAdapter.PublishSchema()`'s
`FieldBuilder.NeedsUnits()` assertion (see "Revit side" below) now enforces this directly for every `None`-spec
field, not only for the six spec-carrying ones. Field 9 (`horizontalDatum`) sources from
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
| 24 | `metersPerOutputUnit` | double | Simple double | Number | General (dimensionless ratio) | never absent | `LengthConverter.MetersPerUnit(LocalFrame.OutputUnit)` |
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
    for string/int/bool, `ExtensibleStorageRoundTripTolerance`-bounded for the five Length doubles **and**
    `metersPerOutputUnit` (six doubles total; changed from a bit-exact `CompareExactDouble` call, now removed,
    2026-09-23 — see "Correction" above), collecting every mismatch and never short-circuiting, every numeric
    value `CultureInfo.InvariantCulture`-formatted matching `CreateToposolidCommand.cs`'s `"R"`-format
    convention.
  - `static IReadOnlyList<(string Field, double Delta)> ComputeDeltas(expected, actual)` — unconditionally
    reports all six tolerance-bounded doubles' actual delta (the five Length fields' plus
    `metersPerOutputUnit`'s, added 2026-09-23 by a round 2 review finding — see "Correction" above; the prior
    five-field-only version left `metersPerOutputUnit`'s own delta unlogged even though `Diff` had already
    started tolerance-comparing it), pass or fail, so a thrown exception always has a number to cite and Step 9
    below always has a number to read.
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

**2026-09-23 addition.** This same tolerance now also bounds `metersPerOutputUnit` (see "Correction" above),
reusing the identical unconfirmed-round-trip reasoning by analogy: whether `SpecTypeId.Number`/
`UnitTypeId.General` round-trips losslessly through `Entity.Set`/`Get<double>` is **its own separate open
question**, confirmed absent from every installed Revit 2027 doc source checked (no Number/General pairing
table anywhere in `RevitAPI.xml`). Step 9 below must therefore calibrate against an observed
`metersPerOutputUnit` delta too, not only the five Length fields' deltas, before this tolerance can be called
anything but a placeholder for that field as well.

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
    against **either** `SpecTypeId.Length.TypeId` **or** `SpecTypeId.Number.TypeId` (a three-way `None`/
    `Length`/`Number` compare since 2026-09-23, matching this codebase's only other `ForgeTypeId`-identity
    precedent, `RevitUnitConversion.cs`/`CreateToposolidCommand.cs:164`); throws `ProvenanceAttachmentException`
    naming the first drift.
  - If null: the two static checks `SchemaBuilder.GUIDIsValid(guid)`/`.VendorIdIsValid("SolidGround")`;
    construct `new SchemaBuilder(guid)`; call `.AcceptableName(schemaName)`; chain
    `.SetSchemaName(...)`/`.SetVendorId("SolidGround")`/`.SetReadAccessLevel(AccessLevel.Public)`/
    `.SetWriteAccessLevel(AccessLevel.Vendor)`/`.SetDocumentation(...)` on that retained `SchemaBuilder`; per
    field, `.AddSimpleField(field.Name, field.ClrType)` returns a `FieldBuilder`, dispatching on
    `field.Spec`: `.SetSpec(SpecTypeId.Length)` for `Length`, `.SetSpec(SpecTypeId.Number)` for `Number`
    (2026-09-23 addition — see "Correction" above), or no `SetSpec` call at all for `None`. Two defensive
    assertions, both added 2026-09-23: for a `None`-spec field, `fieldBuilder.NeedsUnits()` must be `false`,
    throwing a field-named `ProvenanceAttachmentException` otherwise (this is the exact check that would have
    caught the original bug at the offending field instead of only at `Finish()`); for a `Number`-spec field,
    `UnitUtils.IsValidUnit(SpecTypeId.Number, UnitTypeId.General)` must be `true`, throwing otherwise (no
    installed Revit 2027 source documents this specific pairing, so this turns an unconfirmed assumption into
    a fail-loud, attributable diagnostic instead of a later raw exception out of `Entity.Set`/`Get`). Finally
    `.Finish()` runs on the retained `SchemaBuilder`, never a per-field `FieldBuilder`.
- **`ProvenanceEntityWriter.cs`** (`internal static class`) — `internal static void Attach(Document document,
  Toposolid toposolid, TerrainExportPayload payload, PlacementRecordDraft placementDraft)`, matching
  `ToposolidCreatedHook` exactly (a bare method group, no lambda, no signature change):
  1. `expected = ExtensibleStorageProvenanceValues.From(payload.Provenance, BuildIdentity.Current
     .InformationalVersion, .ModuleVersionId, .Sha256)` — may throw `ProvenanceFieldValueException`.
  2. `schema = ProvenanceSchemaAdapter.EnsurePublishedSchema()`.
  3. Build `new Entity(schema)`; write all 36 fields via 36 explicit, individually statically-typed
     `Set<T>(name, value[, unitTypeId])` call sites — not a reflective loop, since `Entity.Set`/`Get` are
     compile-time generic with no `Type`-parameterized overload, so `Fields`/`ClrType` drives only
     `AddSimpleField`/`RequireExactSchema`, never write or read. The five Length fields pass `UnitTypeId.Meters`;
     field 24 (`metersPerOutputUnit`) passes `UnitTypeId.General` (2026-09-23 — see "Correction" above); the
     remaining 30 fields use the two-argument no-unit overload. Then `toposolid.SetEntity(entity)`.
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

All 29 tests are offline, Linux-CI-runnable, `sealed <Subject>Tests` in `tests/SolidGround.Tests/`, plain
`Assert.*`; the full offline suite (`SolidGround.Tests`) stood at 939 tests once Stage 2 landed (938 passed, 1
skipped by design — the live OpenTopography test). Six of the rows below have no counterpart in the design
record or this note's original draft — Stage 1's own review rounds added them, and the shipped code is
authoritative here. Three further rows (`EveryDoubleFieldCarriesALengthOrNumberSpec`,
`MetersPerOutputUnitCarriesTheNumberSpec`, `MetersPerOutputUnitIsToleranceBoundedNotExact`) were added
2026-09-23 by the Issue #16 round 1 fix (see "Correction" above) and likewise have no design-record
counterpart, for the same reason: they lock the Number-spec correction and its defensive tolerance change so
neither can silently regress. That fix brought the full offline suite to 942 tests (941 passed, 1 skipped by
design), still just the live OpenTopography test. Revit-side code (`ProvenanceSchemaAdapter`,
`ProvenanceEntityWriter`) has zero automated tests — `SolidGround.Tests` is architecturally barred from
referencing `RevitAPI`, matching `ToposolidCreationService`/`PostCreationVerification` precedent; it is
compile-checked only by the Nice3point CI gate, with all real behavior deferred to the manual evidence plan
below.

| Test | Proves |
| --- | --- |
| `ExtensibleStorageProvenanceSchemaTests.FieldListHasNoDuplicateNamesOrTypes` | the 36-entry `Fields` list has no name collision `AddSimpleField` would reject, and every field's CLR type is one of the four supported simple types |
| `...EveryLengthSpecFieldIsADoubleField` | only `double` fields carry `ProvenanceFieldSpec.Length`, and exactly 5 fields do |
| `...EveryDoubleFieldCarriesALengthOrNumberSpec` | *(2026-09-23)* every `double` field's `Spec` is `Length` or `Number`, never `None` — locks the fix so no double field can regress to spec-less again |
| `...MetersPerOutputUnitCarriesTheNumberSpec` | *(2026-09-23)* the exact field named in the "Units are required for field metersPerOutputUnit" failure carries `ProvenanceFieldSpec.Number` specifically |
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
| `...DiffDetectsMismatchesFromStringIntAndBoolComparators` | one mismatch case each for `CompareString`/`CompareInt`/`CompareBool` (a broken comparator for any of the other 30 such fields would previously go undetected); renamed 2026-09-23 after `CompareExactDouble` was removed (see next row) |
| `...MetersPerOutputUnitIsToleranceBoundedNotExact` | *(2026-09-23)* `metersPerOutputUnit`'s `Diff` comparison is tolerance-bounded like the five Length fields, not bit-exact — locks the fix that replaced the now-removed `CompareExactDouble` call for this field; also locks a round 2 fix that its mismatch message says "unitless ratio," not the generic Length-field " m" suffix `CompareLength`'s other five call sites use |
| `...DiffAndComputeDeltasMessagesAreCultureInvariant` | mirrors the date test for `Diff`/row 21c's delta text |
| `...ComputeDeltasReportsEveryToleranceBoundedFieldRegardlessOfTolerance` | *(renamed and extended 2026-09-23 by a round 2 review finding)* `ComputeDeltas` never short-circuits, across all six tolerance-bounded doubles including `metersPerOutputUnit`, not only the five Length fields |
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
mechanism itself. **2026-09-23 addendum (Issue #16 round 1 fix):** the three supported commands (`restore
--locked-mode`, `build --configuration Release --no-restore`, `test ... --configuration Release --no-build`)
were re-run against the corrected code and passed: 0 warnings, 0 errors, 942 tests (941 passed, 1 skipped by
design, same live OpenTopography test). The CI-shaped `-p:UseRevitReferenceAssemblies=true` gate variant was
not re-run in this round; it is unaffected by this fix (no package, TFM, or gate-mechanism change) but has not
been independently re-confirmed since. None of this is a substitute for the manual evidence plan below: every
check here is offline and Revit-free by construction (`SolidGround.Tests` never launches Revit, and neither
compile gate
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
this repository). The design record originally called for a second, dedicated "ES Probes" ribbon panel holding
six new commands for this issue. **2026-09-23 correction:** a Stage 4 runtime crash bisection (see "Decisions
recorded from evidence" below) implicated `SolidGroundProbe` as a whole — Revit crashed twice with the probe
installed and opened cleanly once its manifest was removed — without itself isolating any specific line of
code. A separate, subsequent static code review found no concrete memory-safety defect anywhere in the probe
by inspection and, by process of elimination rather than a confirmed mechanism, flagged a second same-tab
`CreateRibbonPanel` call — unique to the six new Extensible Storage buttons — as the only Revit-ribbon API call
novel to the probe's own previously-proven-safe startup code, at medium (one reviewer) to low (another
reviewer, "ranked #1 only by elimination of everything else, not on a specific found defect") confidence.
`probe-crash/fix.md` (kept outside this repository) folded all six buttons onto the pre-existing "Probes" panel
instead, with every button's name, caption, tooltip, and command class byte-for-byte unchanged; the resulting
single-panel build then launched cleanly 9 consecutive times, which is consistent with but does not prove that
specific cause, since a prepared bisection variant that omits the six new buttons entirely
(`probe-crash/variant-no-es-panel`) was built but never actually run — and the same intermittent crash recurred
twice more, against that same unchanged single-panel build, in two later 2026-09-23 sessions (see "Decisions
recorded from evidence" and "Known limitations" below); the cause remains unresolved. A seventh command,
`EsNewEvidenceProjectCommand` ("ES New Evidence Project"), was added to that same panel later the same day to
resolve a separate template-overwrite incident (see Step 9's Evidence below), bringing the single "Probes"
panel to 18 buttons total. The six original ES commands: `EsDump` (the four-part read discipline from "Revit
side" above, plus a full field dump to a text file, deliberately Core-free); `EsSpotCheck` (a side-by-side
comparison of an `EsDump` output against Step 9's known inputs); `EsForeignWrite` (one rolled-back attempt to
construct the entity under `SolidGroundProbe`'s own, different `VendorId`); `EsCopyHost` (copies the host
toposolid and dumps the copy's entity); `EsDeleteHost` (calls `Element.DeleteEntity(Schema)` on the host
toposolid, run under the vendor-matching manifest identity below, since `DeleteEntity` shares `SetEntity`'s
`AccessLevel.Vendor` gate); and `EsPoisonSchema` (registers a same-GUID, differently-shaped schema, used by
both Step 13 scenarios). `GeoCheck`, used only by Step 10's own required geographic cross-check, is a separate
throwaway console application — not a `SolidGroundProbe` ribbon command and not a shipped `SolidGround.Cli`
verb, an explicit orchestrator ruling, since this cross-check is a one-off manual-evidence tool, not a durable
product capability.

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

**Evidence.** Collected 2026-09-23 (`EVIDENCE-ES.md`, Segments 3-6). Plan item 2 above (the owner's own Save-As)
was superseded before it ran: the owner's standing 2026-09-23 ruling ("I'm not going to do the Save-As. You are
more than capable to do that.") put Save/Save As/Undo under scripted automation, and a same-day harness incident
(below) replaced the Save-As dialog itself with an API-created project.

The first live run of the shipped attach code against a real document (Segment 3, build
`20260921-230336-05cb759a`, pid `43880`) hit row 21a and rolled back cleanly: `ProvenanceAttachmentException`:
"SolidGround could not finish publishing its Extensible Storage schema 'SolidGround_Provenance_Toposolid' (GUID
'bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e'): Units are required for field metersPerOutputUnit." No element
persisted (QAT Undo confirmed disabled; journal and log agree no transaction committed). Art:
`S9-1-create-dialog.png`, `S9-1-create-dialog-controls.txt`, `S9-1-log-error.txt`, `S9-1d-rollback-proof.txt`.
Commit `e80a78c` fixed this the same day: `metersPerOutputUnit` now carries `ProvenanceFieldSpec.Number`
(`SpecTypeId.Number`/`UnitTypeId.General`), redeployed as build `20260923-111825-199ad874`. Segment 4's first
run against the fixed build (pid `45580`) log-confirmed success — element `317345`, six finite round-trip
deltas all exactly `0` including `metersPerOutputUnit` for the first time, schema `bc03d923-...` v1 attached —
though its own untitled document was never saved: Segment 5's 9.2 attempt (a plain QAT Save) produced no Save
As dialog and no file, traced to a harness design fact, not a product defect: `Start-Revit2027.ps1` opens the
installed default template (`Default_I_ENU.rte`) itself as its working document, so Save legitimately saved
back onto that installed file twice, both resulting saves landing at 4,608,000 bytes (the untouched original was
3,846,144 bytes). It was restored byte-identical
from Revit's own auto-rotated `.0001` backup at 11:40:12 on 2026-09-23
(`evidence/template-incident/restore-log.txt`, SHA-256
`1E7520D621D47EE49C6185AAC38D9AB740097E84C71632D910541C41D000C4CD` confirmed both on the preserved original and
after the restore). The fix, applied before Step 9 continued: a new probe command,
`EsNewEvidenceProjectCommand` ("ES New Evidence Project"), creates, saves, and activates a fresh evidence
project entirely by API, with no Save As dialog and never touching the launcher's own document; every later
relaunch reopens that saved project via Revit's own Recent Documents list (a read action, never a save) before
any further command runs.

With that fix in place, Segment 6 produced three samples against commit `e80a78c`'s build (redeployed,
re-verified, still `20260923-111825-199ad874`): **Sample 1** — "ES New Evidence Project" created
`issue16-es-evidence.rvt` (38,510,592 bytes; three-part Pass confirmed: active document path, launcher
path/`IsModified` unchanged, main window title), then Create Toposolid produced element `1245519` (Level L1,
ToposolidType "Generic - 20'", 1130 of 1130 points retained, budget 15000), all six deltas exactly `0`,
placement record confirms `schemaVersion:2`/`extensibleStorage{schemaGuid, schemaVersion:1}` (Art:
`S9-1-02-create-dialog-seg6.png`, `S9-1-04-log-tail-seg6.txt`, `S9-1-05-placement-seg6.json`). **Sample 2** — an
unmodified control run against the same document produced element `1245527`, a second Attached/deltas line (all
six again exactly `0`) plus a benign, expected "Highlighted toposolids overlap" warning (same footprint by
design), then a QAT Save with no Save As dialog (the document already had a path from Sample 1's own project
creation): file grew from 38,510,592 to 40,169,472 bytes; the launcher template re-hashed unchanged (Art:
`S9-3-01-control-dialog-seg6.png`, `S9-3-02-log-tail-seg6.txt`). **Sample 3** — a full close/redeploy (no-op,
same build)/relaunch (pid `51336`) created a separate throwaway `issue16-es-calibration.rvt` via "ES New
Evidence Project", then Create Toposolid once produced element `1245519` (cross-process — an independent id
sequence in the new document), a third Attached/deltas line (all six again exactly `0`); Revit was then closed
**without** saving that in-memory toposolid, confirmed by an actual "Do you want to save changes to
issue16-es-calibration.rvt?" prompt answered "No" (Art: `S9-4-07-calibration-dialog-seg6.png`,
`S9-4-08-log-tail-seg6.txt`, `S9-4-09-savefile-dialog-no-seg6.txt`).

**Outcome.** Every field round-tripped within `ExtensibleStorageRoundTripTolerance` in all three samples, the
control run's `RequireExactSchema` non-null branch accepted the already-published schema with no re-publish, and
the relaunch sample supplied the required cross-process delta. All three samples' six round-trip fields — the
five Length fields plus the newly-fixed `metersPerOutputUnit` — were exactly `0` in every case, not merely
within tolerance: `ExtensibleStorageRoundTripTolerance = 1e-6 m` is confirmed with a margin far beyond the
originally-anticipated 10x-100x, for `metersPerOutputUnit` as much as the five Length fields, so no tolerance
revision is warranted (see "Tolerance, provisional" above, both open questions).

### Step 10 — same-session cross-vendor read via the probe, plus the required `GeoCheck` geographic cross-check

1. `SolidGroundProbe`'s manifest `VendorId` is already `"SolidGroundProbe"` (no edit needed). Run `EsDump`,
   which applies the four-part read discipline from "Revit side" above, then `ListFields()` plus a switch on
   `Field.ValueType`/`GetSpecTypeId()` dumping every field to a text file with no `SolidGround.Core`
   reference.
2. Run `EsSpotCheck`, which presents the `EsDump` output side by side with Step 9's known inputs: exact match
   for string/int/bool fields, same order of magnitude by eye for the five Length doubles, not
   tolerance-precise, since the probe is deliberately Core-free and cannot call `Diff`/`ComputeDeltas`; only
   Step 9 stays tolerance-precise. **Round 2 review finding, 2026-09-23:** `EsDump`'s field-capture helper
   (`ProbeEsEntityDump.CaptureField`, kept outside this repository with the rest of `SolidGroundProbe`) reads
   every non-empty-spec field with the hardcoded `UnitTypeId.Meters`, a choice its own code comment already
   flagged as dormant for "non-Length spec" fields because none existed in this schema before this issue's
   round 1 fix gave `metersPerOutputUnit` `SpecTypeId.Number`. That dormant path is no longer dormant: reading
   a `SpecTypeId.Number` field with `UnitTypeId.Meters` does not pair with the `UnitTypeId.General` this note's
   own schema now requires for that spec (see "Correction" and "Revit side" above), so `EsDump` must be updated
   to read `metersPerOutputUnit` with `UnitTypeId.General` before this step next runs, or its dump will show
   `metersPerOutputUnit` as `(error)` instead of a real value (see "Known limitations").
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
4. **Required: geographic cross-check via `GeoCheck`.** `GeoCheck` (see the `SolidGroundProbe` paragraph above)
   references `SolidGround.Core` directly — unlike the deliberately Core-free `SolidGroundProbe` — to
   independently run the full geographic reconstruction leg against dumped fields 28-33 (the horizontal
   forward/inverse operation format, definition, engine name, and version), compared within
   `GeographicRoundTripToleranceDegrees`; the fuller reconstruction the fatal in-hook check (row 21c)
   deliberately never attempts (see "Revit side" and "Known limitations"). The design record itself scored
   this recommended and non-blocking; this plan promotes it to a required step.

**Pass criteria.** The probe's dump matches Step 9 to the stated precision; `EsForeignWrite`'s attempt throws
the documented `InvalidOperationException` at `Entity` construction and is confirmed rolled back, never
committed; `GeoCheck`'s delta is within `GeographicRoundTripToleranceDegrees`.

**Evidence.** Collected 2026-09-23 (`EVIDENCE-ES.md`, Segment 6), same session as Step 9 above, against
`issue16-es-evidence.rvt`. Reopening that file (required before any 10.x command, per the standing rule the
template incident produced — see Step 9 above) took three attempts: `WM_SETTEXT` on the classic Open dialog's
file-name field did not retain programmatically-set text, for either the real target path or a trivial test
string (dialog then cancelled, harmless); toggling the Application Menu button's `TogglePattern` opened an inert,
invisible "Hidden Window" with zero enumerable descendants (left untouched); enumerating the **main window's
own** automation tree found the Recent Documents flyout's `issue16-es-evidence.rvt_CommandButton` (matched by
`AutomationId`, distinct from the co-named pin button), whose invocation changed the title bar to
`issue16-es-evidence.rvt` — a read action, never a save (Art: `S10-00-launch-seg6.txt` through
`S10-04-select-sgprobe-tab-seg6.txt`).

**10.1 EsDump.** `entity.isValid`/`readAccessGranted` both `true`, schema
`bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e` found via `Schema.Lookup`, exactly 36 fields present.
`metersPerOutputUnit` carries `specTypeId autodesk.spec.aec:number-2.0.0`/`unitTypeId
autodesk.unit.unit:general-1.0.1`, confirming commit `e80a78c`'s Number-spec fix is live in the schema after a
real save and reopen, not only in-memory. The five Length fields (`elevationMinimumMeters=166.043`,
`elevationMaximumMeters=171.278`, `localOriginXMeters=[withheld]`, `localOriginYMeters=[withheld]`,
`localOriginElevationMeters=0`) match Step 9's known inputs (Art: `S10-1-es-dump.json/.txt`).

**10.2 EsSpotCheck.** `schemaGuid`/`schemaVersion` and `pointCounts.original`/`retained` (1130/1130) EXACT
MATCH; `localOrigin` deltas all `0`, within the probe's own loose 1e-3 m eyeball tolerance (Art:
`S10-2-es-spot-check.json/.txt`). **Scope caveat, not a failure** (see "Known limitations" below): the probe
resolves the placement record it compares against as the newest file under `output.directory`, not the
currently open document's own — this run actually cross-checked `issue16-es-evidence.rvt`'s entity against
Sample 3's calibration-run placement record instead of one written against `issue16-es-evidence.rvt` itself,
numerically correct here only because both runs' synthetic fixture inputs were identical.

**10.3 EsForeignWrite**, against element `1245519`. `new Entity(schema)` threw
`Autodesk.Revit.Exceptions.InvalidOperationException`: "Writing of Entities of this Schema is not allowed to
the current add-in." — as "Write-access enforcement point" above predicted. `Element.SetEntity` was then
**skipped**, not attempted, because entity construction had already failed and no `Entity` existed to pass to
it — also as that section predicted ("before `SetEntity` is ever reached"). `Element.DeleteEntity(schema)`
threw `Autodesk.Revit.Exceptions.ArgumentException` with the identical message text plus the standard
`Parameter name: schema` suffix. `Transaction.RollBack()` returned `RolledBack`; the read-back diff showed "no
field differences" (Art: `S10-3-es-foreign-write.json/.txt`).

**10.4 GeoCheck (REQUIRED).** `dotnet GeoCheck.dll --dump S10-1-es-dump.json` rebuilt the transform (EPSG:4326
geographic <-> EPSG:26915 projected, datum NAD83, engine ProjNET 2.1.0) and printed `[PASS] local origin (0,0):
projected round-trip delta = 0.008633304884933916 m (tolerance 0.02 m)`, exit code `0` (Art:
`S10-11-cli-cross-check-seg6.txt`, `S10-1-es-dump.json.geocheck.json`).

**Outcome.** Step 10 passes on every stated criterion. `EsForeignWrite`'s observed exception sequence confirms
"Write-access enforcement point" above precisely: `new Entity(schema)` threw `InvalidOperationException` first,
so `Element.SetEntity` was never reached at all — not a probe sequencing choice to skip a call it could
otherwise have made, but a hard consequence of construction failing before any `Entity` object existed to pass
to it. Only `Element.DeleteEntity`, called separately against the still-live host element (which needs no
`Entity` argument), went on to actually throw, and did so with `ArgumentException`. `Element.SetEntity`'s own
predicted `ArgumentException` remains documented in `RevitAPI.xml` but has still never been independently
exercised by any session, since no code path in `EsForeignWrite` calls it with a null or placeholder `Entity`
once construction has already failed.

### Step 11 — save/reopen, `SolidGround.Revit` absent

1. Close Revit; disable or rename the shipped manifest so only the probe loads; reopen the file Step 9 saved;
   re-run `EsDump`, recording whether `Schema.Lookup` is null on the first call before it succeeds, the
   false-negative risk the read discipline's `GetEntitySchemaGuids()`-first check exists for.
2. Confirm every field matches Step 9's dump to the same eyeballed precision as Step 10.

**Pass criteria.** The schema and every field are readable with `SolidGround.Revit` entirely absent from the
session, confirming `AccessLevel.Public` read does not depend on the publishing add-in's own presence.

**Evidence.** Collected 2026-09-23 (`EVIDENCE-ES.md`, Segment 7), continuing the same `issue16-es-evidence.rvt`
Steps 9-10 produced. `SolidGround.addin` (hash `6f8a3f26612b2c6a6b4be5b5ad825fd45291b6ce69239022db24461281e43ec4`)
was moved, not renamed in place, to `evidence\disabled\SolidGround.addin` — an orchestrator-directed variant of
this step's own plan item 1 above, which named either mechanism. Relaunch (pid `38176`) showed exactly one
security dialog (`SolidGroundProbe` only — `SolidGround` did not load) and only the SG Probe ribbon tab
(`SolidGround`'s own tab absent from the ribbon's button enumeration). `issue16-es-evidence.rvt` was reopened
via the Recent Documents technique Step 10 established, confirmed by the title-bar change. **ES Dump**
(`evidenceSequence:"S11-1"`) reported `entity.isValid: true`, `entity.readAccessGranted: true`,
`schemaFound: true`, exactly 36 fields present, with every spot-checked field (`elevationMinimumMeters`,
`elevationMaximumMeters`, `localOriginXMeters`, `localOriginYMeters`, `metersPerOutputUnit`) matching `S10-1`'s
own dump exactly. **Confirms `AccessLevel.Public` read does not depend on `SolidGround.Revit`'s presence — 11.1
PASSES.** The manifest was moved back afterward and re-hashed
`6f8a3f26612b2c6a6b4be5b5ad825fd45291b6ce69239022db24461281e43ec4` — byte-identical to the pre-move hash,
confirming the move-and-restore round trip left it unchanged. This run's own probe output did not separately
record whether `Schema.Lookup` returned null on an initial call before succeeding, so the specific
false-negative risk the four-part read discipline's `GetEntitySchemaGuids()`-first check exists for was not
independently distinguished this session. Art: `S11-00-launch-seg7.txt` through `S11-04-esdump-dialog-seg7.png`,
`S11-1-es-dump.json/.txt`.

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

**Evidence.** Collected 2026-09-23 (`EVIDENCE-ES.md`, Segments 7-8), against the still-open `issue16-es-evidence.rvt`.
`EsDeleteHost`, `EsCopyHost`, and `EsVendorMatchDeleteEntity` below each resolve their target element the same
newest-placement-record-on-disk way `EsForeignWrite` does in Step 10 above, not by which document is active in
Revit (see "Known limitations" below); this session's results were correct only because of that same coincidence.

**12.a Delete + scripted Undo.** `EsDeleteHost` (`evidenceSequence:"S12a-1"`) deleted the host toposolid and its
sketch/boundary dependents in one committed transaction: `deletedElementIds: [1245517, 1245518, 1245519, 1245520,
1245521, 1245522, 1245523, 1245524, 1245533]` (9 ids), `finalTransactionStatus: "Committed"`. Undo was invoked
via UI Automation `InvokePattern.Invoke()` on the QAT button matched by `AutomationId:
ID_Undo_HistoryButtonExecute` (disambiguated from the sibling `ID_Undo_HistoryButtonFlyout`, which shares the
`Name` "Undo"); its pre-invoke state (`IsEnabled: True`) matched Issue #15's own `R4-B4-08-undo-state-after.txt`
precedent for "a committed change is on the undo stack." A second `EsDump` (`S12a-2`) then reported
`entity.isValid: true`, 36 fields, and **zero field differences against `S10-1`** — host and entity both fully
restored. **12.a PASSES.**

**12.b Copy.** `EsCopyHost` (`S12b-1`) produced copy element `1245537`, `finalTransactionStatus: "RolledBack"`
(the probe's own transaction, so the copy was never persisted), `duplicated: "CONFIRMED: the copy carries its
own (duplicate) Extensible Storage entity for this schema."`, `fieldDiff: "identical (every field matches --
the copy is a verbatim duplicate)."` **12.b PASSES**, confirming rather than merely carrying forward the design
record's `RevitAPI.xml`-remark citation for this specific schema.

**12.c Vendor-matching `DeleteEntity`.** `SolidGroundProbeVendorMatch.addin` (`VendorId="SolidGround"`, mixed
case, byte-for-byte matching the shipped add-in's own manifest — not the all-caps `"SOLIDGROUND"` Revit stores
internally) was installed only for this step (source hash
`9bce52626849c08015c415c538d2d0656a863a764d8ced08451c4f97610f2844`, installed copy identical), loaded alongside
`SolidGroundProbe` (its button landed on a new "ES Vendor Match" panel on the same SG Probe tab rather than the
separate tab the runbook's own prose named — an implementation detail, not a defect). `EsVendorMatchDeleteEntity`
(`S12c-1`) reported `firstDeleteEntityResult: true`, `firstDeleteEntityException: null`,
`secondDeleteEntityResult: false`, `secondDeleteEntityException: null`, `finalTransactionStatus: "RolledBack"` —
no exception on either call, so the mixed-case `VendorId` alone satisfies `AccessLevel.Vendor`'s write-access
gate; the all-caps-manifest fallback this step's own plan described was not needed. **12.c PASSES.** The
vendor-match manifest was removed immediately afterward; no save was performed at any point in Step 12. Art:
`S12a-00` through `S12a-05` (`S12a-1-es-delete-host.json/.txt`, `S12a-2-es-dump.json/.txt`), `S12b-00/01`
(`S12b-1-es-copy-host.json/.txt`), `S12c-00` through `S12c-05` (`S12c-1-es-vendor-match-delete-entity.json/.txt`).

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

**Evidence.** **13.1 collected 2026-09-23** (`EVIDENCE-ES.md`, Segment 8). A fresh relaunch (pid `24268`) worked
directly in the launcher's own throwaway `Default_I_ENU.rte` document, never saved, matching this scenario's own
"close without saving" pass criterion. SolidGround tab → Create Toposolid registered the real schema (element
`317345`); SG Probe tab → `EsPoisonSchema` (`evidenceSequence:"S13-1"`) then reported:

```
"scenario": "Scenario 1 (Revit's own protection): a Schema is already registered under this GUID in this session -- Finish() is expected to THROW InvalidOperationException.",
"finishSucceeded": false,
"outcome": "Finish() THREW Autodesk.Revit.Exceptions.InvalidOperationException: A different Schema with the same identity already exists.",
"matchedDesignRecordPrediction": true
```

The probe's own exception text reads "the same identity," a harmless paraphrase difference from this note's own
"a matching identity" wording above; `matchedDesignRecordPrediction: true` is the field that actually gates this
step, and it is `true`. **13.1 PASSES.** Art: `S13-1-01` through `S13-1-05`
(`S13-1-es-poison-schema.json/.txt`).

**13.2 blocked before it could start (Segment 8).** Immediately after 13.1, closing the `EsPoisonSchema` dialog
and then closing Revit surfaced a second, mid-close detection of a concurrent Revit 2026 session (pid `2892`, a
different, genuinely in-use document than the one Segment 7 had already seen) — the same
explicitly-carried-forward "a Revit 2026 process starts while ours is open" stop condition firing a second time
(see Step 12's own stop, above, and "Decisions recorded from evidence" below). Unlike that first stop, this
session's own Revit 2027 (pid `24268`) was still open with a pending native "Save changes to
`Default_I_ENU.rte`?" prompt; rather than leave that one already-in-flight, already-safe prompt hanging
unattended against the launcher's own template — the exact hazard the standing save rule exists to prevent —
this task completed only that one resolution, confirming the dialog named `Default_I_ENU.rte` (not a real
evidence file) before answering **No** via pid-verified `BM_CLICK` (never Yes, never Cancel). No further action
was taken this segment. Art: `S13-1-06-savefile-dialog-no-seg8.txt`.

**13.2 collected 2026-09-23 (Segments 9-10).** A Segment 9 relaunch (pid `51940`) crashed on its own — the same
intermittent `SolidGroundProbe` crash bisected under Step 9's "Decisions recorded from evidence" below, at the
identical `ntdll.dll` `STATUS_HEAP_CORRUPTION` (`0xc0000374`, fault offset `0x117eb5`) signature — before any
Step 13.2 sub-step began (no throwaway project existed yet, so there was nothing to recover). Segment 10's own
first relaunch (pid `53112`) hit the identical recurrence a second time; the one authorized retry (pid `32376`)
then launched cleanly (see the crash bullet below for the full recurrence count). `EsNewEvidenceProjectCommand`
created a fresh throwaway project, `issue16-es-poison-throwaway.rvt` (three-part Pass: active document path,
launcher path/`IsModified` unchanged, main window title) — process pid `32376`'s first Extensible Storage
action of any kind, satisfying this scenario's own "nothing registered yet under this GUID in this session"
precondition. `EsPoisonSchema` (`evidenceSequence:"S13-2"`) ran first, verbatim:

```
guid=bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e [read from '...\schema-guid.txt']
nameAcceptable=True
Scenario 2 (RequireExactSchema's own check): nothing registered yet under this GUID in this session -- Finish() is expected to SUCCEED, poisoning the session.
Finish() SUCCEEDED: registered a poisoned Schema (guid=bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e, fields=[poisonProbeField]).
matchedDesignRecordPrediction=True
```

`CreateToposolidCommand` then ran against that now-poisoned session. Row 21a fired, verbatim (dialog and log
agree):

```
MainInstruction: SolidGround hit a problem while finishing the toposolid.
ContentText: The Extensible Storage schema already registered under GUID 'bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e' has 1 field(s), expected 36.
```

```
Error: 0 : [...] SolidGround hit a problem after the transaction started. ProvenanceAttachmentException: The Extensible Storage schema already registered under GUID 'bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e' has 1 field(s), expected 36.
Information: 0 : [...] Showing rollback dialog: SolidGround hit a problem while finishing the toposolid.
```

QAT Undo (`ID_Undo_HistoryButtonExecute`) reported `IsEnabled: False` both immediately before and immediately
after the attempt — matching Issue #15's own precedent reading for "no transaction was ever committed" — and,
combined with the log's own "after the transaction started"/"Showing rollback dialog" lines, confirms no
Toposolid element was persisted to `issue16-es-poison-throwaway.rvt`. `matchedDesignRecordPrediction` was
`true`, not `false`, so no stop condition fired. **13.2 PASSES.** Closed without saving (the throwaway document
was never modified, per this scenario's own "close without saving" rule). Art: `S13-2-04` through
`S13-2-12-rollback-proof-seg10.txt`.

**Recovery, collected 2026-09-23 (Segment 10).** A fresh Revit 2027 process (pid `7868`, with no Extensible
Storage action of its own yet) reopened the unrelated, already-saved `issue16-es-evidence.rvt` via the Recent
Documents technique Step 10 established. `EsDump` reported exactly 36 fields with **zero differences** against
`S10-1-es-dump.json` — the poisoned schema registered in the completely separate, already-exited process (pid
`32376`) had no observable effect on this fresh process or on the persisted document, confirming a plain Revit
restart is sufficient recovery. **Recovery PASSES.**

**Host-resolution caveat materialized, not merely theoretical (see "Known limitations" below).** The first
`EsDump` attempt in this recovery actually failed: the evidence harness's shared `probe-es-settings.json`
`elementId` default follows the same "newest file under `output.directory`" placement-record resolution
`EsSpotCheck`/`EsForeignWrite`/`EsDeleteHost`/`EsCopyHost`/`EsVendorMatchDeleteEntity` already use, and that
newest record was Segment 8's own 13.1 throwaway run, naming `toposolid.elementId=317345` — an id created in a
different, never-persisted document, not in `issue16-es-evidence.rvt`. Revit's own follow-on "cannot be
ignored" dialogs (queued behind the probe's already-closed, failed `TaskDialog`) were dismissed via their own
enabled `Cancel` button, never `OK`, performing no save. The fix: override `probe-es-settings.json`'s
`elementId` to `1245519` (the same element `S10-1` itself dumped in Step 10), after which the re-run succeeded
on one dialog. This is the first time this scope gap actually produced a wrong-element failure rather than a
coincidentally-correct pass. Closed without saving (`EsDump` is read-only). Art: `S13-2-recovery-01` through
`S13-2-recovery-09-seg10.png`, `S13-2-recovery-es-dump.json/.txt`.

**Environment restored.** The Revit 2027 default template (`Default_I_ENU.rte`) re-hashed byte-identical to its
own original (SHA-256 `1E7520D6...C4CD`) at every check from Segment 6 onward, including after Segment 10 —
except for the Segment 5 window, when the launcher's own template-overwrite incident (see "Template overwrite
incident" above) temporarily changed it via two QAT Save actions before the owner's 11:40 restore confirmed the
same original hash (`evidence/template-incident/restore-log.txt`); the orchestrator's own closeout separately
restored the Scenario B `settings.json` under
`%ProgramData%\SolidGround\Revit\` to its pre-session backup (SHA-256 `AD6FC819...A98F`), left untouched by
every segment above.

**Deferred, disclosed.** The same-vendor-different-case write-success scenario (a third add-in whose manifest
`VendorId` is "SolidGround" in a different letter case, for example "SOLIDGROUND", successfully writing the
entity) stays out of scope for this issue's acceptance criteria; Step 10's cross-vendor refusal test already
answers the corresponding read/refusal half. Left for a future issue, reusing Step 12(c)'s vendor-matching
manifest (`SolidGroundProbeVendorMatch.addin`) rather than minting a third add-in.

### Decisions recorded from evidence

Steps 9 and 10 above are settled by a 2026-09-23 Stage 4 evidence session (`EVIDENCE-ES.md`, Segments 3-6), and
Steps 11, 12(a-c), and 13.1 by that same session's later Segments 7-8. Step 13.2 and its recovery are settled by
two further 2026-09-23 segments (9-10): Segment 9 stopped before 13.2 could begin, on a recurrence of the
intermittent `SolidGroundProbe` crash (see the crash bullet below), and Segment 10 completed 13.2 and its
recovery after one authorized retry. Every step in design record §7 (Steps 9-13) has now passed.

- **Number-spec correction shipped and confirmed live (commit `e80a78c`), 2026-09-23.** The first live run
  against the shipped build hit row 21a ("Units are required for field metersPerOutputUnit") and rolled back
  cleanly with no element persisted. `e80a78c` gave `metersPerOutputUnit` `ProvenanceFieldSpec.Number`
  (`SpecTypeId.Number`/`UnitTypeId.General`), redeployed as build `20260923-111825-199ad874`, and Step 10's
  `EsDump` confirms the schema carries that spec/unit pairing on disk after a real save and reopen, not only
  in-memory. See Step 9 and Step 10 above.
- **`ExtensibleStorageRoundTripTolerance` (1e-6 m) confirmed, including the newly-fixed field; no revision
  needed, 2026-09-23.** All three Step 9 samples (elements `1245519` and `1245527` in `issue16-es-evidence.rvt`,
  and `1245519` cross-process in the separate, discarded `issue16-es-calibration.rvt` toposolid) report all six
  round-trip fields — the five Length fields plus `metersPerOutputUnit` — as exactly `0`, a lossless round trip
  with margin far beyond the originally-anticipated 10x-100x. See Step 9 above.
- **Write-access exception types observed, 2026-09-23.** `EsForeignWrite` found `new Entity(schema)` throws
  `InvalidOperationException` and `Element.DeleteEntity` throws `ArgumentException`, both exactly as "Write-access
  enforcement point" above predicted, but `Element.SetEntity` was never reached: the probe's own test sequence
  skipped it once `Entity` construction had already failed, so `SetEntity`'s own predicted `ArgumentException` was
  not independently observed this session. See Step 10 above.
- **Probe implicated by runtime bisection as a whole; a second ribbon panel flagged only by a subsequent static
  review, 2026-09-23 (a throwaway diagnostic tool outside this repository, not part of Issue #16's shipped
  product).** Revit 2027 crashed twice with `SolidGroundProbe` installed (`ntdll.dll` `STATUS_HEAP_CORRUPTION`,
  `0xc0000374`, while opening its own default template); an initial, pre-bisection hypothesis pass suspected
  pyRevit/MCP interop, but a direct runtime bisection (identical crash with the probe present at attempts 1-2,
  a clean open with its manifest removed at attempt 3) implicated `SolidGroundProbe` as a whole, not any specific
  line of its code. A separate, later static code review found no concrete memory-safety defect anywhere in the
  probe by inspection; by process of elimination, not a confirmed mechanism, it flagged a second same-tab
  `CreateRibbonPanel` call — unique to the six new Extensible Storage buttons — as the only Revit-ribbon API call
  novel to the probe's own previously-proven-safe startup code, at medium (`review-1.md`) to low (`review-3.md`,
  "ranked #1 only by elimination of everything else, not on a specific found defect") confidence. `probe-crash/fix.md`
  folded those six buttons onto the pre-existing "Probes" panel with no behavior change to any command; the
  resulting single-panel build then launched cleanly, repeatedly, across every later 2026-09-23 launch (Segments
  3-6) with pyRevit still loaded, unaffected — consistent with, but not proof of, the second-panel explanation,
  since the prepared bisection variant that omits the six new buttons entirely (`probe-crash/variant-no-es-panel`)
  was built but never actually run. This also confirms pyRevit's mere presence was not the differentiator. The
  shipped `SolidGround.Revit` add-in was independently cleared by code inspection: its Issue #16 delta is
  reachable only after a user clicks Create Toposolid, which had not happened before either crash. See "Manual
  evidence plan" above.

  **Recurrence, Segments 9-10 (2026-09-23): the second-ribbon-panel explanation is further weakened, and the
  cause remains unresolved.** After 9 consecutive clean launches of the single-panel build (pids `43880`,
  `45580`, `5216`, `51336`, `18812`, `38176`, `31464`, `21308`, `24268`, spanning Segments 3-4 and 6-8), the
  identical `ntdll.dll` `STATUS_HEAP_CORRUPTION` (`0xc0000374`, fault offset `0x117eb5`) signature recurred
  twice more against that same, unchanged single-panel DLL: Segment 9 (pid `51940`, ~13:26:33) and Segment 10's
  own first attempt (pid `53112`, ~14:14:12). Both cleared by simply relaunching — the second recurrence via the
  one authorized retry (pid `32376`), which launched cleanly and was followed by one further clean launch for
  the recovery check (pid `7868`) — for 11 clean launches of the single-panel build bracketing the two
  recurrences (13 total launches of that build across this evidence session; 16 total launches and 4 total
  crashes across the whole session, counting the two pre-fix, two-panel-build crashes above). This revises this
  bullet's own earlier "roughly 20 successful subsequent launches across Segments 6-8" estimate downward: Segments
  6-8 alone contain 7 launches, all clean, and the fuller count from the single-panel build's first use through
  the first recurrence is 9, not "roughly 20." Because the fix had already removed the only concrete code
  novelty either static review flagged, and the crash still recurred twice against that same fixed build with no
  further code change, the second-ribbon-panel hypothesis is further weakened rather than confirmed; no new
  hypothesis was tested this session, `probe-crash/variant-no-es-panel` still was never run, and **the cause
  remains unresolved**. As with the first two crashes, neither recurrence happened after a SolidGround command
  had run: both occurred while Revit opened its own default template, with every loaded add-in already reporting
  `AddInLoadFailureMessage: NoError`, before the SolidGround ribbon tab could be reached, and the shipped
  `SolidGround.Revit` add-in itself never faulted after loading in any of the sixteen launches this evidence
  session made. Both recurrences also happened alongside, not because of, a concurrent Revit 2026 process
  starting or already running — recorded as a timing fact, not a claimed cause, since the identical signature
  also occurred with no Revit 2026 involved at all (Segments 1-2) — and Segment 10's retry (pid `32376`) repeated
  the same handle-only coexistence pattern already confirmed below, on port 48885 alongside a Revit 2026 session
  holding port 48884, with no cross-session interference; the later recovery relaunch (pid `7868`) ran solo, after
  that Revit 2026 session had already exited on its own.
- **Harness template incident resolved; the probe gained an API-created evidence-project command,
  2026-09-23.** `Start-Revit2027.ps1` opens the installed default template (`Default_I_ENU.rte`) itself as its
  working document, so an early Step 9.2 attempt's QAT Save legitimately saved back onto that installed file
  twice; it was restored byte-identical from Revit's own auto-rotated `.0001` backup at 11:40
  (`evidence/template-incident/restore-log.txt`, SHA-256 `1E7520D6...C4CD` confirmed both before the pollution
  and after the restore). The fix: a new probe command, `EsNewEvidenceProjectCommand` ("ES New Evidence
  Project," on the same "Probes" panel), creates, saves, and activates a fresh evidence project entirely by API
  with no Save As dialog and never touches the launcher's template; every later relaunch reopens that saved
  project via Revit's own Recent Documents list (a read action) before any further command runs. See Step 9
  above.
- **Coexistence with a concurrent Revit 2026 session held throughout, handle-only, 2026-09-23.** Under the
  owner's "Run alongside, handle-only" ruling, a separate Revit 2026 session ran alongside these Revit 2027
  launches with every action pid-verified and no shared mouse/keyboard/focus. pyRevit's MCP bridge gives port
  48884 to whichever Revit process starts first across versions and 48885 to the second, observed consistently
  (Revit 2026 held 48884 while this session's Revit 2027 held 48885 when Revit 2026 was already running; this
  session's Revit 2027 held 48884 itself in Segment 6, when no Revit 2026 was running at launch). Segment 10
  repeated the first pattern once more (Revit 2026 held 48884, this session's own successful relaunches held
  48885), extending "no cross-session interference observed" across the full ten-segment evidence session.
- **`AccessLevel.Public` read confirmed independent of `SolidGround.Revit`'s presence, 2026-09-23.** With
  `SolidGround.addin` moved out of the Addins folder (only `SolidGroundProbe` loaded), `EsDump` against the saved
  `issue16-es-evidence.rvt` still reported `entity.isValid`/`entity.readAccessGranted` both `true` and all 36
  fields present, matching `S10-1`'s own dump exactly on every spot-checked field; the manifest was restored to
  the same SHA-256 hash it had before the move. See Step 11 above.
- **A copied host carries its own verbatim-duplicate entity, confirming the carried-over `RevitAPI.xml` remark
  for this specific schema, 2026-09-23.** `EsCopyHost` produced copy element `1245537` with
  `fieldDiff: "identical"` against the host and `duplicated: "CONFIRMED"` — the copy's Extensible Storage entity
  is a full, independent duplicate of the original's, not a reference to it, so a copy's provenance still
  describes its original's acquisition, not its own. See Step 12(b) above and "Known limitations" below.
- **`AccessLevel.Vendor` write access is case-insensitive on the calling add-in's declared `VendorId`,
  2026-09-23.** A probe manifest declaring `VendorId="SolidGround"` (mixed case, matching the shipped add-in's
  own manifest byte-for-byte, not the all-caps `"SOLIDGROUND"` Revit stores internally) called `DeleteEntity`
  successfully (`true` then `false`, no exception either call) via `EsVendorMatchDeleteEntity`; the all-caps
  manifest fallback this step's own plan described was not needed. See Step 12(c) above.
- **Revit's own same-session schema-identity protection confirmed, 2026-09-23.** With the real schema already
  registered this session (via a prior Create Toposolid), `EsPoisonSchema`'s `SchemaBuilder.Finish()` threw
  `Autodesk.Revit.Exceptions.InvalidOperationException` ("A different Schema with the same identity already
  exists" — the probe's own paraphrase of this note's "a matching identity" wording, not a substantive
  difference), with `matchedDesignRecordPrediction: true`. See Step 13 above.
- **`RequireExactSchema`'s own cross-session drift check confirmed (Scenario 2, Step 13.2), 2026-09-23.** In a
  fresh process with nothing yet registered under this GUID, `EsPoisonSchema` pre-registered a same-GUID,
  1-field schema (`Finish()` succeeded, `matchedDesignRecordPrediction: true`); `CreateToposolidCommand` then
  ran against that poisoned session and row 21a fired, not an uncaught exception, with a diagnostic message
  naming the exact drift ("has 1 field(s), expected 36"), and rolled back cleanly (QAT Undo disabled both
  before and after; log confirms no commit). A fresh, unrelated process then confirmed a plain Revit restart is
  sufficient recovery: `EsDump` against the untouched `issue16-es-evidence.rvt` showed zero field differences
  from `S10-1`. Both Step 13 scenarios — Revit's own same-session identity protection and `RequireExactSchema`'s
  own cross-session drift check — are now confirmed for this schema. See Step 13 above.

Steps 11, 12, and 13.1 landed in this same 2026-09-23 session (Segments 7-8) and are reflected above; 13.2 and
its recovery landed in the two further Segments 9-10 the same day and are reflected in the bullets above. 12(a)'s
delete-then-Undo outcome is recorded only in its own Evidence paragraph (Step 12 above), not as a separate
bullet here, since it confirms already-well-established Undo behavior rather than settling a new question, in
keeping with this note's own framing of that sub-step. This section now mirrors
`revit-toposolid-creation.md`'s own "Decisions recorded from evidence (2026-09-21)" section exactly in shape,
with every step's own bullet present.

### Acceptance criteria

Issue #16's own five GitHub acceptance criteria, mapped to the specific tests, steps, and commits that satisfy
each one:

| # | Acceptance criterion | Satisfied by | Gap |
| --- | --- | --- | --- |
| AC1 | A created Toposolid returns a valid Entity with every required field. | Steps 9.1 and 9.3 (36 fields written, `entity.isValid`), Step 10.1 `EsDump` (36 fields confirmed after a real save/reopen); Core tests `FieldListHasNoDuplicateNamesOrTypes`, `FieldCountStaysUnderRevitsTwoHundredAndFiftySixFieldLimit`. Commits `e63996b` (field list), `42e9fd8` (write), `e80a78c` (fixed the one field whose missing spec made the first live run fail `Finish()` outright). | None. |
| AC2 | The stored offset, CRS, units, and datum reproduce source coordinates within the Phase 1 tolerance. | The mandatory, fatal in-hook reconstruction check (every run, projected leg) plus Step 10.4 `GeoCheck` (REQUIRED, the geographic leg: 0.0086 m delta against a 0.02 m tolerance) and the three Step 9 calibration samples (all six round-trip fields exactly `0` across two processes, confirming `ExtensibleStorageRoundTripTolerance = 1e-6 m` with wide margin). Commits `42e9fd8`, `e80a78c`. | Honest gap: the geographic leg (fields 28-33) is confirmed only by the one-time manual `GeoCheck` cross-check, not by the fatal in-hook check every future run performs — see "Known limitations," "The in-hook reconstruction check is projected-only." |
| AC3 | Schema/version mismatches fail safely and diagnostically. | Step 13.1 (Scenario 1, same-session identity protection: `Finish()` throws when `EsPoisonSchema` tries to register a competing definition after Create Toposolid has already registered the real schema under that GUID) and Step 13.2 (Scenario 2, `RequireExactSchema`'s own drift check: row 21a fires with a diagnostic message and a clean rollback); error-catalogue rows 21a/21b/21c above ("Failure semantics"); Core tests covering `Diff`/`ComputeDeltas` mismatch detection and the five `From...Smuggles...NonFinite*` tests for row 21b's non-finite-value path. Commits `e63996b`, `42e9fd8`. | None. |
| AC4 | Revit 2027 manual tests cover write, read, document save/reopen, and element deletion behavior. | Step 9 (write, plus the document's first save via the API-created evidence project), Step 10 (read via a genuinely different-vendor add-in, plus the required `GeoCheck`), Step 11 (save/reopen with `SolidGround.Revit` itself absent from the session), Step 12 (deletion via `Element.DeleteEntity` under a vendor-matching manifest, plus host delete/Undo and host copy). | None. |
| AC5 | Primary 2027 API sources are cited in the implementation record. | "Revit 2027 API surface used" above (every member reflection-confirmed against the installed `RevitAPI.dll` 27.0.10.13, cross-cited to Autodesk's 2027 Extensible Storage guide and installed `RevitAPI.xml` where available) and "Sources" above. Commit `aae9da0`. | None. |

All five acceptance criteria are satisfied by the evidence recorded above. AC2's own gap — geographic-leg
reconstruction is confirmed only by a one-time manual cross-check, not gated on every future run — is disclosed
here and already tracked under "Known limitations," not hidden.

## Known limitations

- **Worksharing/central-model behavior is unexplored.** `SetEntity`/`AccessLevel.Vendor` behavior in a
  workshared, central-model document is out of scope here; flagged for a future issue before targeting
  workshared models.
- **A copied toposolid keeps its original's provenance unchanged — confirmed, not merely expected, 2026-09-23.**
  Carried over from the design record's own API review (`Entity`'s `RevitAPI.xml` remarks, not independently
  reflected or quoted by this note; see "`DeleteEntity`, host delete/Undo, and host copy" above), and now
  confirmed for this specific schema by Step 12(b): the copy's entity describes the original's acquisition, not
  its own. No mitigation ships in this milestone.
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
- **The out-of-repo probe's `metersPerOutputUnit` read path is broken until it is updated (round 2 review
  finding, 2026-09-23).** `SolidGroundProbe`'s `ProbeEsEntityDump.CaptureField` (see Step 10 above) reads every
  non-empty-spec field with a hardcoded `UnitTypeId.Meters`; that branch was harmless and dormant while this
  schema had no non-Length spec'd field at all (before this issue's round 1 fix, `metersPerOutputUnit` carried
  no spec, which was the original production bug). Round 1 gave that field `SpecTypeId.Number`/
  `UnitTypeId.General` instead (see "Correction" above), so the probe's blanket `Meters` fallback is now
  exercised for the first time and no longer matches this schema's own field. Left unfixed, Step 10's `EsDump`
  will report `metersPerOutputUnit` as `(error)` (caught gracefully by the probe's own try/catch, not a crash,
  but unreadable) instead of the real value `EsSpotCheck` needs to compare against Step 9's known inputs. This
  note does not itself change the probe, which lives entirely outside this repository; whoever next runs Step
  10 must special-case `SpecTypeId.Number` fields in `CaptureField` to read with `UnitTypeId.General` first.
  **Resolved before Step 10 ran (2026-09-23).** The live `EsDump` evidence (`S10-1-es-dump.json`, Step 10 above)
  shows `metersPerOutputUnit` read back as a real value (`0.3048...`) with `specTypeId
  autodesk.spec.aec:number-2.0.0`/`unitTypeId autodesk.unit.unit:general-1.0.1`, not `(error)`, so `CaptureField`
  was special-cased for `SpecTypeId.Number` before this session — an out-of-repo probe change not itself
  evidenced here.
- **Harness template risk.** `Start-Revit2027.ps1` opens the installed default template directly as its own
  working document; any future evidence session that invokes Save or Save As before first creating a dedicated
  evidence project (via `EsNewEvidenceProjectCommand`) risks overwriting `Default_I_ENU.rte` again, exactly as
  happened once on 2026-09-23 (restored from Revit's own `.0001` backup — see "Decisions recorded from evidence"
  above and `evidence/template-incident/restore-log.txt`). This is a harness limitation, not a defect in the
  shipped product, and nothing in the harness itself prevents a recurrence beyond the standing rule to reopen or
  create a real project first.
- **The intermittent `SolidGroundProbe` launch crash recurred twice more after its fix and remains unresolved,
  2026-09-23.** The single-panel fix (see "Decisions recorded from evidence" above) did not eliminate the
  crash: after 9 clean launches it recurred identically twice more (Segments 9-10), each cleared by a plain
  relaunch with no further code change. Because the fix already removed the only concrete code novelty either
  static review had flagged, and the crash still recurred against the fixed build, the second-ribbon-panel
  hypothesis alone does not explain it, and the cause remains unresolved. This is a risk to any future
  session driving `SolidGroundProbe` — a throwaway, out-of-repository diagnostic tool, not the shipped
  product — not a defect in `SolidGround.Revit` itself, which never faulted in any launch this evidence session
  made. A future session should expect and tolerate an occasional crash-and-relaunch during this probe's own
  startup, exactly as this session's own one-authorized-retry rule already did.
- **Two Segment 3 evidence artefacts were overwritten by a Segment 6 naming collision, 2026-09-23.**
  `S9-1-00-select-solidground-tab.txt` and `S9-1-01-click-create-toposolid.txt` were written once during
  Segment 3 (pid `43880`, 09:54) and, without a pre-write collision check, overwritten with Segment 6's own data
  at the identical filenames; Segment 3's original content at those two paths is unrecoverable, and Segment 6's
  own data now lives at `S9-1-00b-select-solidground-tab-seg6.txt`/`S9-1-01b-click-create-toposolid-seg6.txt`
  instead, with `S9-1-00-INCIDENT-overwritten-by-seg6.txt` recording the incident. Segment 3's own prose narrative
  in `EVIDENCE-ES.md` is the only surviving record of what those two files originally reported; every artefact
  written after this point in Segment 6 had its filename checked against a fresh directory listing first, and no
  further collision occurred. This is a disclosed evidence-integrity gap in the artefact trail, not a gap in the
  underlying Step 9 findings themselves, which the surviving narrative and log lines still support.
- **pyRevit's port assignment can flip which Revit session lands on port 48884 depending on launch order,
  2026-09-23.** Segments 3-4 saw a concurrent Revit 2026 session already running and holding port 48884 before
  this evidence session's own Revit 2027 launch, so pyRevit gave this session's Revit 2027 process port 48885
  both times (see "Decisions recorded from evidence" above). In Segments 7 and 8 the order reversed: this
  session's own Revit 2027 was already running when a separate Revit 2026 session started (pid `20736` at
  12:45:32 in Segment 7; pid `2892`, mid-close, in Segment 8) — under the same first-to-attach rule, the
  newly-started Revit 2026 session would be expected to take pyRevit's "second" port (48885) instead of its
  usual 48884, a port flip not independently confirmed by a port check in either segment, since both stopped
  (correctly, per the standing "a Revit 2026 process starts while ours is open" condition) before any such check
  ran. Neither stop was caused by the port itself, but a human-driven Revit 2026 session unexpectedly losing its
  usual pyRevit port could disrupt whatever tooling that session expects there. Future evidence-session launches
  should therefore be ordered so this session's own Revit 2027 starts only after confirming a concurrent Revit
  2026 session already holds port 48884 — never the reverse — which is also the precondition Step 13.2 is
  waiting on.
- **`EsSpotCheck`, `EsForeignWrite`, `EsDeleteHost`, `EsCopyHost`, and `EsVendorMatchDeleteEntity` all resolve
  their target element from the newest placement record on disk, not the open document's own.** The probe
  resolves the placement record — and, from it, the `toposolid.elementId` to act on — as "newest file under
  `output.directory`," not by which document is currently active in Revit. This is one shared mechanism, not a
  comparison quirk unique to `EsSpotCheck`: `S10-3-es-foreign-write.json`, `S12a-1-es-delete-host.json`,
  `S12b-1-es-copy-host.json`, and `S12c-1-es-vendor-match-delete-entity.json` each carry the identical
  `hostResolution` string, confirming `EsForeignWrite`, `EsDeleteHost`, `EsCopyHost`, and
  `EsVendorMatchDeleteEntity` resolve their target the same way `EsSpotCheck` does. In the 2026-09-23 session
  this meant `issue16-es-evidence.rvt`'s own host element was resolved this same way for every one of them —
  compared against (`EsSpotCheck`), constructed a foreign entity against (`EsForeignWrite`), deleted and
  Undo-restored (`EsDeleteHost`), copied (`EsCopyHost`), and deleted again under a vendor-matching manifest
  (`EsVendorMatchDeleteEntity`) — which produced correct results only because both candidate placement records'
  synthetic fixture inputs happened to resolve to the identical element (`1245519`). For a read-only command
  this scope gap can only cause a false pass or a spurious mismatch, as already true of `EsSpotCheck` alone; for
  a mutating command — `EsDeleteHost` above all — a future session with genuinely divergent candidate placement
  records could resolve to, and act on, the wrong element in the open document entirely, not merely mis-compare
  a read. See Step 10 and Step 12 above. **Materialized, not merely theoretical, and one command wider than
  listed above, 2026-09-23.** The Step 13.2 recovery check's own `elementId` default (`probe-es-settings.json`,
  read by `EsDump` too, not only the five commands named in this bullet's own title) resolved to
  `toposolid.elementId=317345` — a stale id from Segment 8's own non-persisted 13.1 throwaway document — and
  produced an actual `EsDump` failure against `issue16-es-evidence.rvt`, the first time this scope gap caused a
  wrong-element failure rather than a coincidentally-correct pass. The fix was a manual `elementId` override to
  `1245519` (the same element `S10-1` dumped in Step 10), not a code change to the probe. See Step 13's Recovery
  evidence above.
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
single "Probes" panel (originally planned as a separate "ES Probes" panel; folded into the pre-existing panel by
a 2026-09-23 fix that followed a runtime crash bisection and a subsequent static review — see "Decisions
recorded from evidence" above, including that review's own confidence caveats), and the
`SolidGroundProbeVendorMatch.addin` vendor-matching manifest. Stage 4 filled in every blank "Evidence" paragraph
from real Revit 2027 sessions and populated "Decisions recorded from evidence" above: a 2026-09-23 session did
this for Steps 9, 10, 11, 12, and 13.1, and two further 2026-09-23 segments completed Step 13.2 and its recovery
after an intermittent, still-unresolved crash (see "Known limitations" above) delayed them twice. It does not
change any decision recorded in
`docs/architecture/revit-add-in-conventions.md`, `docs/architecture/revit-2027-verification-and-host-design.md`,
or `docs/architecture/revit-toposolid-creation.md`. It does not begin Issue #17 (signing, packaging, or a
clean-install validation) or Issue #19 (the real ribbon icon design). It does not close Issue #16 itself;
closure is recorded on GitHub, once Stage 4 lands, not in this note.
