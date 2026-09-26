# Address and parcel provenance (SolidGround Issue #33)

Issue #33 (PH3-6) bumps `TerrainProvenance` to schema version 3, adding one new, optional, Core-only record
group -- `AddressParcelProvenance`, with two nested optional sub-records, `GeocodeProvenance` and
`ParcelProvenance` -- that records how an export's area of interest was located when an operator-entered
address (Issue #28) and/or a parcel boundary lookup (Issue #29) actually resolved it, rather than a raw
bounding box or radius supplied directly. The new field is threaded through the assembler, renderer, and
reader only; no CLI command, `TerrainProcessingPipeline`, or `SolidGround.Revit` code changes, and nothing yet
populates it (see "Population path"). This note also names the field list a future Issue #16 Extensible
Storage schema version will need, so that later work makes no new field-list decision.

## Purpose and boundary

This note covers the new `AddressParcelProvenance`/`GeocodeProvenance`/`ParcelProvenance` record group and the
future Extensible Storage mapping those types imply. It does not change Issue #16's shipped schema
(`SolidGround_Provenance_Toposolid`, version 1, 36 fields, `docs/architecture/revit-extensible-storage-provenance.md`):
that note and its `SchemaBuilder` call are untouched, and the new field is silently ignored by
`ExtensibleStorageProvenanceValues.From`/`ProvenanceEntityWriter.Attach` today, since neither reflects over
`TerrainProvenance`'s properties. This note only documents the future version that Extensible Storage mapping
will need whenever Issue #16 is revisited. See
docs/architecture/provenance-and-deterministic-exports.md's "Export document manifest, schema version 3" for
the full JSON manifest this change adds; it is not duplicated here.

## Why a new type instead of "AoiProvenance"

Earlier Phase 3 planning called this concept `AoiProvenance`. `SolidGround.Core.Aois.AreaOfInterest` (and its
`Wgs84BoundingBoxAoi`/`ParcelGeometryAoi` implementations) already own "Aoi" for the clip-region-*shape*
concept -- a bounding box, radius, or polygon describing *where* to clip. "How an address/parcel lookup located
that shape" is a distinct concept, and reusing "Aoi" for both would read as the same thing at a glance. This
design instead uses `AddressParcelProvenance`/`GeocodeProvenance`/`ParcelProvenance`, matching the existing
`TerrainProvenance`/`ElevationSourceMetadata`/`ReferenceOrigin` naming family.

## Schema version 3 shape

```csharp
public sealed record AddressParcelProvenance
{
    public AddressParcelProvenance(DateOnly retrievalDate, GeocodeProvenance? geocode, ParcelProvenance? parcel);

    public DateOnly RetrievalDate { get; }
    public GeocodeProvenance? Geocode { get; }
    public ParcelProvenance? Parcel { get; }
}

public sealed record GeocodeProvenance
{
    public GeocodeProvenance(AddressGeocoderProvider provider, string queryText, string attribution);

    public AddressGeocoderProvider Provider { get; }
    public string QueryText { get; }
    public string Attribution { get; }
}

public sealed record ParcelProvenance
{
    public ParcelProvenance(
        ParcelBoundarySourceKind sourceKind,
        string sourceIdentity,
        string parcelId,
        string? stableParcelId,
        string? legalDescription,
        string licenseDisclaimerText);

    public ParcelBoundarySourceKind SourceKind { get; }
    public string SourceIdentity { get; }
    public string ParcelId { get; }
    public string? StableParcelId { get; }
    public string? LegalDescription { get; }
    public string LicenseDisclaimerText { get; }
}
```

`AddressParcelProvenance`'s own constructor rejects a value with both `geocode` and `parcel` null: construct no
`AddressParcelProvenance` at all when neither a geocode nor a parcel lookup contributed to the area of
interest. Beyond that, at least one of `Geocode`/`Parcel` is always non-null whenever the outer record itself
is non-null, and each side is independently optional so a plain geocoded-address radius (no parcel confirmed)
and a parcel resolved directly from a latitude/longitude (no geocode) both have a home. `GeocodeProvenance` and
`ParcelProvenance` mirror `AddressGeocodeCandidate`/`ParcelBoundaryCandidate`'s own required/optional-string
split and enum-validity checks exactly (`docs/architecture/address-geocoding.md`, `docs/architecture/parcel-boundary-sources.md`).

The nested-optional-record shape reuses this codebase's existing precedent: `ElevationSourceMetadata.CollectionPeriod`
(`src/SolidGround.Core/Sources/ElevationAcquisition.cs`) is already a nullable nested record grouping two
fields (`Start`/`End`) that must travel together, rather than two independently-nullable flat fields with a
hand-rolled "both or neither" check. `AddressParcelProvenance` applies the identical idiom to two
independently-optional groups instead of one.

**One shared `RetrievalDate`, not one per sub-record.** Phase 3's interactive dialog
(`docs/architecture/phase-3-interactive-add-in-research.md`'s "Goal (d): WPF dialog") is a single, synchronous,
operator-driven session: geocode and parcel confirmation happen together, so one shared date for the whole
resolution episode is both sufficient and simpler, and avoids leaving a parcel-only-from-lat/long record with
no retrieval date at all. `RetrievalDate` is deliberately distinct from `ElevationSourceMetadata.CollectionPeriod`
(the elevation dataset's own vintage): it is wall-clock "when did SolidGround itself do the lookup," supplied
by the caller -- nothing in `SolidGround.Core` reads the clock itself, the same precedent
`ExtensibleStorageProvenanceValues` and `PlacementRecordDraft.ToRecord` already establish.

**Both `ParcelId` and `StableParcelId` are carried, not `ParcelId` alone.** Both are already validated,
zero-extra-cost fields on `ParcelBoundaryCandidate` today; `StableParcelId` is specifically documented as a
durable id meant to survive the source's own `ParcelId` churn -- exactly the kind of long-term reversible
provenance AGENTS.md's Provenance decision cares about. Storing only `ParcelId` risked a second field-list
decision later, which is the exact thing AC4 exists to avoid.

**`ParcelBoundaryCandidate.AccuracyLabel`/`NotASurveyDisclaimer` is not carried into `ParcelProvenance` at
all.** It is a fixed constant, identical on every candidate from every source, already implied the moment a
`ParcelProvenance` exists at all; this issue's own scope does not ask for it. See "Open questions" below for
this disposition recorded against the future Extensible Storage version too.

**AC1's "covering v1, v2, and v3" interpretation.** This repository's documented no-migration-path policy
(`docs/architecture/provenance-and-deterministic-exports.md`'s "Versioning and compatibility policy" section,
first exercised at Issue #21's version 2 bump) is what AC1's "strict-decode round-trip test covering v1, v2,
and v3 documents" means in practice: a real round trip at the current schema version 3, plus diagnostic
rejection of hand-built version 1 and version 2 documents -- each failing
`TerrainExportBundleReader.ParseDocument`'s existing `schemaVersion` check with a `TerrainExportException`
naming the actual and expected version. There is no reader-side multi-version support and no migration path
from a version 1 or version 2 document to version 3.

## Population path

`TerrainExportPayloadAssembler.Assemble` gained one new optional parameter, `AddressParcelProvenance? addressParcel = null`,
threaded straight into `TerrainProvenance`'s own new optional 12th constructor parameter. Every existing
caller -- `TerrainProcessingPipeline.RunAsync`'s one call to `Assemble`, and through it both CLI commands
(`process`, `run`) and both `SolidGround.Revit` call sites (`CreateToposolidCommand.cs`) -- keeps calling it
exactly as before, implicitly passing `null`. Nothing in this issue's scope wires a live value in: today,
`ParcelCommand`'s own `<name>.aoi.json` sidecar carries only `{kind, parcel: {path, format, bufferMeters}}` --
no provider, query text, GEOID, parcel id, or license text -- so a live CLI `process`/`run` invocation cannot
yet produce a non-null `AddressParcelProvenance`.

The natural future populator is Issue #31's interactive Revit dialog (once implemented): it will call
`IAddressGeocoder`/`IParcelBoundarySource` directly in-process and can construct an `AddressParcelProvenance`
in memory from the operator's own confirmed selections, with no JSON sidecar round trip at all. A CLI-side
channel (a new sidecar file, a new `TerrainRequestSettings` section, or new flags) so a live `process`/`run`
invocation could populate this field today is a separate, currently-undesigned follow-up task -- see "Open
questions" below.

## Field mapping table (for Issue #16's future Extensible Storage schema version)

Every field a future Extensible Storage schema version needs, its CLR type, and its Core source. None of the
15 rows below is a `double`, so unlike several of the existing 36 fields, none of them needs a
`ProvenanceFieldSpec.Length`/`.Number` decision.

| # | Field | CLR type | Absent representation | Core source |
| - | - | - | - | - |
| 1 | `hasAddressParcel` | `bool` | n/a -- always written | `provenance.AddressParcel is not null` |
| 2 | `addressParcelRetrievalDateIso` | `string` | empty when `hasAddressParcel` is false | `provenance.AddressParcel?.RetrievalDate`, `"yyyy-MM-dd"` |
| 3 | `hasGeocode` | `bool` | n/a -- always written | `provenance.AddressParcel?.Geocode is not null` |
| 4 | `geocodeProvider` | `string` | empty when `hasGeocode` is false | `provenance.AddressParcel?.Geocode?.Provider.ToString()` |
| 5 | `geocodeQueryText` | `string` | empty when `hasGeocode` is false | `provenance.AddressParcel?.Geocode?.QueryText` |
| 6 | `geocodeAttribution` | `string` | empty when `hasGeocode` is false | `provenance.AddressParcel?.Geocode?.Attribution` |
| 7 | `hasParcel` | `bool` | n/a -- always written | `provenance.AddressParcel?.Parcel is not null` |
| 8 | `parcelSourceKind` | `string` | empty when `hasParcel` is false | `provenance.AddressParcel?.Parcel?.SourceKind.ToString()` |
| 9 | `parcelSourceIdentity` | `string` | empty when `hasParcel` is false | `provenance.AddressParcel?.Parcel?.SourceIdentity` |
| 10 | `parcelId` | `string` | empty when `hasParcel` is false | `provenance.AddressParcel?.Parcel?.ParcelId` |
| 11 | `hasStableParcelId` | `bool` | n/a -- always written | `provenance.AddressParcel?.Parcel?.StableParcelId is not null` |
| 12 | `stableParcelId` | `string` | empty when `hasStableParcelId` is false | `provenance.AddressParcel?.Parcel?.StableParcelId` |
| 13 | `hasLegalDescription` | `bool` | n/a -- always written | `provenance.AddressParcel?.Parcel?.LegalDescription is not null` |
| 14 | `legalDescription` | `string` | empty when `hasLegalDescription` is false | `provenance.AddressParcel?.Parcel?.LegalDescription` |
| 15 | `parcelLicenseDisclaimerText` | `string` | empty when `hasParcel` is false | `provenance.AddressParcel?.Parcel?.LicenseDisclaimerText` |

Every "Core source" expression above dereferences `provenance.AddressParcel` itself with a leading `?.`: every
payload this issue's own scope actually produces today has `AddressParcel == null` (see "Population path"), so
omitting that leading `?` would throw `NullReferenceException` for the ordinary case.

**Why the Extensible Storage table needs `has*` bool flags but the JSON schema does not.** JSON's own `null` is
already the "is this present" signal (`"geocode": null` in the export document). A flat Extensible Storage
schema has no null concept for a simple field, so each optional group's presence must be its own explicit
`bool` field -- exactly mirroring how the *existing* 36 fields already handle `CollectionPeriod`/`QualityLevel`/
`ElevationRange`'s own optionality (`hasCollectionPeriod`, `hasQualityLevel`, `hasElevationRange`; see
`docs/architecture/revit-extensible-storage-provenance.md`'s "Field table").

36 existing fields plus these 15 is 51, comfortably under Revit's 256-field `SchemaBuilder.Finish()` ceiling
(the existing `FieldCountStaysUnderRevitsTwoHundredAndFiftySixFieldLimit` test's own margin).

## Privacy

Every fixture, example, and test in this repository uses only synthetic values (`GEOID 99999`, "Synthetic
County (fixture only)", "100 Example Loop", parcel id `99-99-999-999`), per AGENTS.md's privacy rule for this
public repository, and no key, authorization header, or request query string reaches any of the fields above:
`QueryText` is the operator's own plain address text (`AddressGeocodeRequest.Address`), never a request URI on
any shipped geocoder provider, and `Attribution`/`SourceIdentity`/`LicenseDisclaimerText` are host-supplied,
non-secret strings. `TerrainExportBundleRendererTests.RenderedDocumentContainingAddressParcelProvenanceContainsNoKeyAuthorizationHeaderOrQueryStringMarker`
proves this at the rendered-bytes level.

That said, once something *does* populate this record (see "Population path"), the record is, by design,
identifying: a populated `AddressParcelProvenance` puts the operator's own query text -- the address they
typed -- and the resolved parcel's identifiers (`ParcelId`, `StableParcelId`, `SourceIdentity`, and any
`LegalDescription`) into the exported JSON document verbatim. An export produced from a real address/parcel
lookup is therefore location data about a real property, and should be shared with that in mind, in the same
spirit as AGENTS.md's own rule that no real person's or client's location may be committed, used in a fixture,
or otherwise disclosed from this public repository. This is expected and correct behavior for a provenance
record whose whole purpose is to preserve how the area of interest was located; it is not a leak to guard
against, only a fact to disclose here plainly.

## Open questions the future schema version must still resolve

- **GUID strategy.** `bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e` (Issue #16's schema GUID) cannot be reused for a
  differently-shaped schema: `SchemaBuilder.Finish()` throws on republishing under the same GUID with a
  different shape (`ExtensibleStorageProvenanceSchema.SchemaGuidText`'s own doc comment). A new GUID constant
  is needed; whether it coexists with the v1 GUID (migrating/merging old and new toposolids) or replaces it
  outright is Issue #16's own future decision, not made by this note.
- **Version-number choice.** `ExtensibleStorageProvenanceSchema.CurrentVersion`'s own next value is independent
  of `TerrainProvenance.CurrentSchemaVersion` -- no numeric alignment between the two is load-bearing, per that
  type's own doc comment.
- **`ParcelBoundaryCandidate.AccuracyLabel`/`NotASurveyDisclaimer` disposition.** Not carried into the future
  Extensible Storage version at all, for the same reason it is not carried into `ParcelProvenance` today (see
  "Schema version 3 shape" above): it is a fixed constant already implied by `ParcelBoundarySourceKind`'s very
  presence.
- **A CLI-side population channel.** Whether a future issue adds an opt-in sidecar or flag (mirroring
  `RasterSourceSidecar`/`--source-json`'s precedent) so a live CLI `process`/`run` invocation can populate this
  field without the interactive Revit dialog is undecided; see "Population path" above.

## Cross-references

- `docs/architecture/revit-extensible-storage-provenance.md` -- "Schema", "Field table", "Failure semantics":
  the GUID/version discipline and fail-closed drift behavior the future Extensible Storage version must keep.
- `docs/architecture/provenance-and-deterministic-exports.md` -- "Export document manifest, schema version 3",
  "Versioning and compatibility policy".
- `docs/architecture/address-geocoding.md` -- "Provider comparison", "Attribution handling per provider".
- `docs/architecture/parcel-boundary-sources.md` -- "Core types and files", "Not a survey".
- `docs/architecture/phase-3-interactive-add-in-research.md` -- "Owner decisions (2026-09-21)", decision 7 (not
  the same-numbered decision 7 in `docs/architecture/revit-add-in-conventions.md`, which is unrelated).
