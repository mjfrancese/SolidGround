# Source licensing and attribution (SolidGround Issue #35)

Issue #35 (PH3-8) closes four gaps left after Issues #28/#29/#31/#33 shipped address geocoding, parcel boundary
lookup, and the interactive dialog: OpenTopography's USGS 1 m dataset had no attribution/citation text anywhere
in this codebase; three already-shipped attribution/license fields (Esri, Geocodio, Local Parcel File) were
exercised in tests only for null-propagation or redaction, never asserted against their real rendered value;
the interactive dialog never displayed OpenTopography's attribution on any path; and the Regrid Data Store
License's Term/cease-use-or-delete obligation had no operator-facing record anywhere in the repository. This
issue adds one new, optional field end-to-end -- `ElevationSourceMetadata.Attribution` -- bumping
`TerrainProvenance.CurrentSchemaVersion` and `RasterSourceSidecarIo.CurrentSchemaVersion` to 4; adds a fixed
`OpenTopographyUsgs1mSource.AttributionNotice` constant; adds two `Mode`-gated sentences to the dialog's
provenance/accuracy preview step; documents (without implementing) the Regrid Term obligation at the two
points an operator who configures a real purchase will actually see it; and records, with citation and
retrieval date, why seven vendors are excluded from every default path. It changes no Extensible Storage
schema and adds no new CLI flag.

## Purpose and boundary

This note covers every licensing/attribution obligation SolidGround's shipped sources carry, what SolidGround
does about each one today, and why. It is the one place this repository satisfies and cross-references, for
Issue #35: an attribution string for every shipped source, present in the JSON provenance export and proven by
a test; why seven parcel/geocoding vendors are excluded from every default path, with no public fixture or
default path ever carrying their data; the WPF dialog showing the relevant attribution once per run; and the
Regrid Data Store License's Term and cease-use-or-delete obligation, recorded where an operator will see it
before lapse. It does not restate the full type definitions or test evidence already recorded in the notes it
cross-references (`docs/architecture/address-geocoding.md`, `docs/architecture/parcel-boundary-sources.md`,
`docs/architecture/address-parcel-provenance.md`, `docs/architecture/provenance-and-deterministic-exports.md`,
`docs/architecture/opentopography-usgs1m-source.md`, `docs/architecture/revit-interactive-dialog.md`).

This issue does not change:
- `ExtensibleStorageProvenanceSchema` (still 36 fields, version 1) -- see "Extensible Storage: deliberately
  unchanged" below.
- Any CLI flag or command surface: no `--attribution` override exists on `fetch`/`process`/`run`, matching
  today's `--quality-level`/`--source-name` precedent of not needing a per-invocation override for every field
  -- see "Provenance export: schema version 4".
- The Regrid Data Store License itself, or any purchase: no Regrid data has ever been purchased for this
  repository, and none ships here -- see "Regrid Data Store obligations".
- Any real county's name, GEOID, service URL, or license text: the owner's 2026-09-24 privacy adjustment
  (`docs/architecture/parcel-boundary-sources.md`) stays in force; this note adds no county-identifying fact.

## Attribution catalogue

Every source SolidGround can acquire elevation, a geocoded address, or a parcel boundary from, and how its
attribution/license text reaches an operator and an export. "Carried by" names the Core type/field the text
travels through; "Rendered at" names the JSON export property (see "Provenance export: schema version 4") and,
where applicable, the dialog text (see "Dialog: attribution shown once per run") that shows it.

| Source | Attribution text origin | Carried by (Core type/field) | Rendered at |
| --- | --- | --- | --- |
| OpenTopography USGS 1 m DEM | Fixed constant `OpenTopographyUsgs1mSource.AttributionNotice` | `ElevationSourceMetadata.Attribution` | `provenance.source.attribution`; dialog fetch-mode `TextBlock` |
| Microsoft Global ML Building Footprints, release 2026-08-13 | Reviewed upstream CDLA Permissive 2.0 notice and release metadata; see [outline research](../research/building-outlines-and-grade-point-selection.md) | `BuildingOutlineProvenance`, independently retained as `TerrainProvenance.BuildingOutline` and optionally in the floor record | Schema-6 `provenance.buildingOutline`, native v3 `buildingOutlineJson`, and ground-preview attribution; retained even after choosing original source elevations |
| Census Geocoder | Fixed constant `CensusGeocoder.AttributionNotice` (`src/SolidGround.Core/Sources/Census/CensusGeocoder.cs:31`) | `AddressGeocodeCandidate.Attribution` -> `GeocodeProvenance.Attribution` | `provenance.addressParcel.geocode.attribution`; dialog `geocodeAttribution` |
| Geocodio | Wire-sourced per-candidate `source` field, no fallback constant (`src/SolidGround.Core/Sources/Geocodio/GeocodioGeocoder.cs:202-224`) | same path | same path |
| Esri | Fixed constant `EsriGeocoder.AttributionNotice` = `"Powered by Esri"` (`src/SolidGround.Core/Sources/Esri/EsriGeocoder.cs:29`) | same path | same path |
| Census county lookup (TIGERweb `layers=Counties`) | No new text: reuses `CensusGeocoder.AttributionNotice` by documentation only -- same `geocoding.geo.census.gov` host (see "Open questions and owner decisions") | Not carried into any export field of its own: it only selects which `CountyParcelRegistryEntry` `AutoGeoidCountyParcelSource` uses -- that entry's own `LicenseDisclaimerText` is what is actually exported | n/a (selection only) |
| County ArcGIS parcel registry | Host-supplied per-county field, `CountyParcelRegistryEntry.LicenseDisclaimerText` (`src/SolidGround.Core/Sources/CountyParcels/CountyParcelRegistry.cs:46`) -- never shipped with repository content | `ParcelBoundaryCandidate.LicenseDisclaimerText` -> `ParcelProvenance.LicenseDisclaimerText` | `provenance.addressParcel.parcel.licenseDisclaimerText`; dialog `parcelDisclaimer` |
| Local parcel file (Regrid-Standard-shaped) | Host-supplied per-file field, `LocalParcelFileOptions.LicenseDisclaimerText` (`src/SolidGround.Core/Sources/LocalParcelFile/LocalParcelFileOptions.cs:88`) | same path | same path |

Only two of these seven rows were, before this issue, actually proven to survive `TerrainExportBundleRenderer.Render`
in a test: Census's geocode attribution and a generic, non-source-specific parcel-disclaimer placeholder, both
inside `TerrainExportBundleRendererTests.AddressParcelWritesGeocodeAndParcelSubObjectsInFixedPropertyOrderWhenPresent`.
Esri's, Geocodio's, and Local Parcel File's real/realistic values were each used elsewhere in that same file
only for null-propagation or forbidden-marker checks, never asserted against the rendered
`attribution`/`licenseDisclaimerText` property. Three new tests close that gap:
`TerrainExportBundleRendererTests.GeocodeAttributionRendersEsrisRealFixedAttributionNoticeVerbatim`,
`...GeocodeAttributionRendersGeocodiosRealisticPerCandidateWireValueVerbatim`, and
`...ParcelLicenseDisclaimerRendersALocalParcelFileRegridShapedDisclaimerVerbatim` (the last uses a disclaimer
string shaped like a real Regrid Standard export's terms, carrying the Term/cease-use-or-delete language quoted
in "Regrid Data Store obligations" below, distinct from the generic county-registry placeholder used
elsewhere). `TerrainExportBundleReaderTests.ReadOfARenderedPayloadRoundTripsAPopulatedSourceAttributionByteForByte`
closes the same gap for OpenTopography, using the real, shipped `OpenTopographyUsgs1mSource.AttributionNotice`
text, not a placeholder, so it proves the actual production string round-trips through render and read
unchanged.

## OpenTopography USGS 1 m attribution

`OpenTopographyUsgs1mSource.AttributionNotice` (`src/SolidGround.Core/Sources/OpenTopography/OpenTopographyUsgs1mSource.cs`,
placed immediately after the existing `DatasetName` constant) is a fixed, two-sentence `public const string`:

```text
Data source: U.S. Geological Survey (USGS) 3D Elevation Program (3DEP), 1-meter Digital Elevation Model,
accessed through the OpenTopography Facility. This work is based on API services provided by the
OpenTopography Facility with support from the National Science Foundation under NSF Award Numbers 2410799,
2410800 & 2410801.
```

The **second sentence is verbatim**, OpenTopography's own required acknowledgment text. It was fetched
directly from https://opentopography.org/usageterms on 2026-09-27 (saved byte-for-byte; the page's own
"Last Updated: October 8, 2025" banner is present in that saved copy) and appears under that page's "API
Agreement" section: "If you use our API service in your project, research, or open source software please
acknowledge OpenTopography: 'This work is based on API services provided by the OpenTopography Facility with
support from the National Science Foundation under NSF Award Numbers 2410799, 2410800 & 2410801.' Acknowledgement
helps ensure the service remains free and sustainable for the community." SolidGround's own `AttributionNotice`
reproduces the quoted sentence exactly, including the ampersand.

The **first sentence is SolidGround's own honest, non-fabricated description of the dataset**, not a verbatim
per-dataset citation string. OpenTopography's separate citation-format page, https://opentopography.org/citations
(also fetched and saved byte-for-byte on 2026-09-27), does **not** contain the NSF acknowledgment sentence at
all; it instead states a general citation *scheme* for high-resolution topographic datasets ("Author/Organization
(PublicationYear). Title, Version. Collector, Distributor. Identifier. Date Accessed.") and notes that
OpenTopography assigns a DOI to each hosted dataset "as a means to facilitate data citation," with "Data license,
citation, and acknowledgment information ... found on a dataset's access page and through a dataset's metadata."
Filling that scheme in for the USGS 1 m dataset specifically would require the dataset landing page's own
dynamic "Data Citation" copy-button text, which a plain page fetch cannot extract (it is populated client-side);
see "Open questions and owner decisions" below. `AttributionNotice`'s first sentence therefore names the
dataset and its access path in SolidGround's own words -- USGS 3DEP 1-meter DEM, "accessed through the
OpenTopography Facility" -- rather than presenting an unverified string as if it were that dynamic citation.
The dataset identity itself (USGS 3DEP, 1-meter, NAVD88 vertical reference) is already independently documented,
with its own citation and 2026-09-19 retrieval date, in `docs/architecture/opentopography-usgs1m-source.md`'s
"Declared vertical reference" section, citing OpenTopography's
[USGS 1 m dataset page](https://portal.opentopography.org/raster?opentopoID=OTNED.012021.4269.3).

`AttributionNotice` **deliberately contains no literal URL** -- matching `CensusGeocoder.AttributionNotice`'s
and `EsriGeocoder.AttributionNotice`'s own shape -- so it can never trip
`FixtureSecurityTests.CommittedFixturesContainNoCredentialsOrRequestUrls`'s `https://` ban once it reaches a
committed golden fixture (it now does; see "Fixture and default-path guarantees" below). `AttributionConstantsContainNoRequestUrls`
(same file) guards this at the constant's own definition site, not only at fixture-commit time.

Both of `OpenTopographyUsgs1mSource`'s acquisition-construction sites (the bare-AAIGrid-plus-GeoKeys path and
the zip-sidecar path) now pass `attribution: AttributionNotice` to every successful `ElevationSourceMetadata`
they build; `OpenTopographyUsgs1mSourceTests` asserts this for both paths.

## Provenance export: schema version 4

`ElevationSourceMetadata` (`src/SolidGround.Core/Sources/ElevationAcquisition.cs`) gained a fifth, optional,
trailing constructor parameter, `string? attribution = null`, mirroring `qualityLevel`'s own "cannot be blank
when supplied" validation exactly, plus a new `public string? Attribution { get; }`. Because the parameter is
optional and trailing, every pre-existing call site -- eleven test call sites across six test files, plus every
production call site -- kept compiling unchanged, passing no fifth argument and defaulting to `null`.

Four call sites that rebuild `ElevationSourceMetadata` field-by-field from a sidecar or a live acquisition,
rather than reusing the acquisition's own `Source` object, each gained one more argument:

- `SolidGround.Cli.Commands.ProcessCommand` (rebuilds from a `.source.json` sidecar) -- passes `sidecar?.Attribution`.
- `SolidGround.Revit.Commands.CreateToposolidCommand`, process mode (mirrors `ProcessCommand` exactly) -- passes
  `sidecar?.Attribution`.
- `SolidGround.Revit.Commands.CreateToposolidCommand`, fetch mode (rebuilds from a live acquisition, not a
  sidecar) -- passes `acquisition.Acquisition.Source.Attribution`.
- `SolidGround.Cli.Commands.RunCommand` (rebuilds from the same live-acquisition shape as the Revit fetch-mode
  site) -- passes `acquisition.Acquisition.Source.Attribution`. This is the value that reaches `run`'s own
  exported `provenance.source`, not only its separate, optional `--save-raster` sidecar write (which reads the
  acquisition's own `Attribution` directly and was already correct);
  `CliFetchAndRunCommandTests.RunAppliesMetadataOverridesToTheExportedProvenanceRatherThanTheAcquisitionsOwnValues`
  now also asserts the primary exported document carries `OpenTopographyUsgs1mSource.AttributionNotice` in
  `source.attribution`, proving this call site's argument actually reaches the export and not only the sidecar.

No CLI command and no `SolidGround.Revit` dialog input gained a new flag or field for this: there is no way to
override or supply an attribution value per invocation, by design (see "Purpose and boundary" above).

`RasterSourceSidecar` (`src/SolidGround.Core/Sources/RasterSourceSidecar.cs`) gained a new positional field,
`string? Attribution`, inserted immediately after `QualityLevel` and before `Vertical`, so a later `process` run
(CLI or Revit) can reproduce the same attribution a `fetch` run recorded. `RasterSourceSidecarIo.CurrentSchemaVersion`
bumped from `3` to `4`; see `docs/architecture/cli-workflow.md`'s "Raster set persistence" section for the full
version 4 sidecar shape and its own "Update, Issue #35" paragraph. `RasterSourceSidecarIoTests.WriteThenReadRoundTripsANullAttributionAsExplicitJsonNull`
covers the null case; the pre-existing `WriteThenReadRoundTripsEveryFieldUnchanged` now covers the populated
case, since its own `BuildSidecar` helper defaults to a non-null attribution.

`TerrainExportBundleRenderer.WriteSource` writes `attribution` (a string, or explicit JSON `null`) as the new,
final property of the rendered `source` object; `TerrainExportBundleReader.ParseSource` requires it in the same
position. `TerrainProvenance.CurrentSchemaVersion` bumped from `3` to `4`, per this repository's own strict,
no-migration versioning policy -- not optional even though the new field is nullable and empty for every
export this repository's own current scope actually produces except OpenTopography's, because the `source`
object's own shape changes for every document. See `docs/architecture/provenance-and-deterministic-exports.md`'s
new "Export document manifest, schema version 4" section for the full manifest text; it is not duplicated here.

The version bump forced a golden-fixture regeneration:
`tests/SolidGround.Tests/Fixtures/example-site-synthetic.solidground.json` now carries `"schemaVersion": 4` and
`OpenTopographyUsgs1mSource.AttributionNotice`'s exact text as `source.attribution` (its ampersand rendered as
the JSON escape `\u0026`, so the committed file still contains no literal `&` glyph the writer's own default
encoder would otherwise have to decide how to escape); its `.points.csv` sibling is unaffected in content.
`TerrainExportGoldenFileTests.GoldenSourceMetadata()` now builds its `ElevationSourceMetadata` with
`attribution: OpenTopographyUsgs1mSource.AttributionNotice`, so the regenerated fixture uses the real
production constant, not a placeholder string.

Every other change this bump required was mechanical fallout of the two literal schema-version bumps
(`RasterSourceSidecarIo.CurrentSchemaVersion` and `TerrainProvenance.CurrentSchemaVersion`, both now `4`), not
of the `attribution` field itself: hardcoded `"schemaVersion": 3` baselines in `RasterSourceSidecarIoTests.ValidJson`
and `CliProcessCommandTests.ValidSourceJson` became `4`; `CliProcessCommandTests`' version-rejection tests were
retargeted (`SourceJsonWithSchemaVersionFourExitsWithUsageErrorNamingTheSidecarPath` became
`SourceJsonWithSchemaVersionFiveExitsWithUsageErrorNamingTheSidecarPath`, testing a genuinely future version; a
new `SourceJsonWithSchemaVersionThreeExitsWithUsageErrorNamingTheSidecarPath` regression test was added for the
newly-retired version 3, mirroring the pre-existing version 1 and version 2 regression tests); and four
hardcoded `Assert.Equal(3, sidecar...schemaVersion...)` assertions on genuinely-produced sidecars across
`CliFetchAndRunCommandTests` became `4`. Two hand-built JSON literals in `TerrainExportBundleReaderTests`
(`version1Document`/`version2Document`) needed **no** edit: both are rejected by `TerrainExportBundleReader.ParseDocument`'s
`schemaVersion` guard before their nested `source` object is ever parsed, so adding or omitting `attribution`
there changes neither test's outcome.

New/updated tests proving the field itself: `ContractModelTests.SourceMetadataRejectsABlankAttributionAndAcceptsNullOrANonBlankValue`;
`TerrainExportBundleRendererTests.DocumentPropertyOrderMatchesTheManifestAtEveryNestingLevel` (the `source`
property-name array now ends `..., "qualityLevel", "attribution"`) and the new
`APopulatedAttributionRendersAsItsLiteralStringAndAnAbsentOneRendersAsExplicitNull`;
`TerrainExportBundleReaderTests.ReadProvenanceRejectsADocumentMissingTheSourceAttributionProperty`;
`CliProcessCommandTests.ProcessCarriesForwardTheSourceJsonSidecarsAttributionIntoTheExportedProvenance`.

## Extensible Storage: deliberately unchanged

AGENTS.md's Provenance decision lists what the Extensible Storage schema must carry at minimum: source
dataset, collection date, quality level, horizontal/vertical datum, original and retained point counts,
elevation range, output unit and local-origin offset, coordinate operation text, and the assembly's own build
identity. Attribution/license text is not on that list, and this issue's own AC1 ("an attribution string ...
asserted present in the JSON provenance export") is read as the JSON export document specifically (see "Open
questions and owner decisions", decision 8) -- exactly the same split Issue #33 already applied to
`AddressParcelProvenance` itself: it feeds the JSON export now, with its own Extensible Storage mapping
deferred to a documented future schema version, never implemented today.

`ExtensibleStorageProvenanceSchema` is unchanged by this issue: still 36 fields, schema GUID
`bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e`, `CurrentVersion = 1`. `ExtensibleStorageProvenanceSchemaTests.cs` needed
zero changes.

This issue adds two documentation-only rows to `docs/architecture/address-parcel-provenance.md`'s existing
15-row "Field mapping table (for Issue #16's future Extensible Storage schema version)": `hasSourceAttribution`
(`bool`) and `sourceAttribution` (`string`, empty when absent), sourced from `provenance.Source.Attribution`,
mirroring the existing `hasQualityLevel`/`qualityLevel` pair's own shape exactly. That makes 36 + 15 + 2 = 53
fields planned for that future schema version, still comfortably under `SchemaBuilder.Finish()`'s 256-field
ceiling. Not implemented now, exactly like the other 15 rows in that same table.

## Dialog: attribution shown once per run

The provenance/accuracy preview step (`SolidGroundDialogStep.ProvenancePreview`, step 9 of 11) runs *before*
any elevation fetch or process happens, so it cannot show a fetched acquisition's own attribution string -- it
can only show what is already known from settings at dialog-construction time. `TerrainRequestSettings.Mode`
(`TerrainAcquisitionMode.Fetch` or `.Process`) is exactly that: read from the settings file before the dialog
opens, independent of which AOI source (`AoiSource`) the operator chose. Before this issue, nothing threaded
`Mode`, or any other elevation-source fact, into the dialog at all, so OpenTopography's attribution was never
shown anywhere in the dialog, on either AOI path.

Plumbing, mirroring the existing `ShowFindParcelGeocodedIntro`/`ShowFindParcelDirectPointIntro` pattern:

- `SolidGroundDialogHost.ShowModal` passes `settings.Request.Mode` as a new `SolidGroundDialogInputs`
  constructor argument (a new, trailing `TerrainAcquisitionMode Mode` field).
- `SolidGroundDialogViewModel` stores it (`internal TerrainAcquisitionMode Mode => _inputs.Mode;`) and exposes
  two `public bool` properties, both read once at construction and never changed afterward: `ShowFetchModeSourceAttribution
  => Mode == TerrainAcquisitionMode.Fetch` and `ShowProcessModeSourceNote => Mode == TerrainAcquisitionMode.Process`.
  Both are `public`, not `internal`, matching every other WPF-bound view-model property in this dialog (pinned
  generically by `RevitInteractiveDialogTests.SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal`,
  and specifically by the new `SolidGroundDialogSourceShowsTheOpenTopographyAttributionInFetchModeAndAConditionalSourceNoteInProcessMode`
  test).
- `SolidGroundDialog.BuildProvenancePreviewPanel` inserts two new `TextBlock`s after the existing
  `settingsFileNote` block and before `siteFormDisclaimer`, each bound to its own view-model property through
  the dialog's existing `BooleanToVisibility` converter (a thin wrapper over WPF's built-in
  `BooleanToVisibilityConverter`), the same converter every other conditional `TextBlock` in this panel already
  uses:
  - `fetchModeSourceAttribution`: `Text = OpenTopographyUsgs1mSource.AttributionNotice` directly (a compile-time
    constant, so only `Visibility` needs a `Binding`), visible when `ShowFetchModeSourceAttribution`.
  - `processModeSourceNote`: a fixed sentence, visible when `ShowProcessModeSourceNote`:

    > "This run processes an already-downloaded elevation file. Its own source attribution, when its optional
    > source sidecar carries one, will be included in this run's exported provenance record."

    This wording is **deliberately conditional**, not a promised attribution string: a `process`-mode run's
    actual source sidecar is not read at dialog-construction time (step 9 runs before any file or network I/O
    today; reading a sidecar there would give this step a new failure mode it has never had).

Neither new `TextBlock` sets an explicit `AutomationProperties.SetName`/`SetHelpText`, matching the existing,
undecorated `geocodeAttribution`/`parcelAccuracy`/`parcelDisclaimer`/`settingsFileNote` blocks in the same
panel -- plain `TextBlock.Text` is already sufficient for a screen reader, and this issue does not introduce an
inconsistent new pattern in one panel.

**Not a defect, and not changed by this issue:** geocode/parcel attribution (`geocodeAttribution`/`parcelDisclaimer`)
shows only on the `FindParcel` AOI path, never on `UseSettingsFile`. `UseSettingsFile` truly performs no
address/parcel lookup, so `settingsFileNote`'s existing sentence stating that plainly ("no address or parcel
lookup was performed, so no address/parcel provenance will be attached") is already accurate. OpenTopography's
own attribution is gated on `Mode`, not on `AoiSource`, so it is unaffected by which AOI path the operator
chose: a `UseSettingsFile` run in `Fetch` mode still shows `fetchModeSourceAttribution`.

Tests: `RevitInteractiveDialogTests.SolidGroundDialogSourceShowsTheOpenTopographyAttributionInFetchModeAndAConditionalSourceNoteInProcessMode`
and `...SolidGroundDialogHostThreadsTheConfiguredModeIntoSolidGroundDialogInputs`. Both extend this file's
existing plain-text-scrape idiom (it greps `src/SolidGround.Revit/Dialog/*.cs` as text and never loads
`SolidGround.Revit.dll`, so it can run on the Linux self-hosted CI runner without WPF/Revit).

### Manual evidence (Revit 2027, 2026-09-27)

The owner approved a short live check ("Go", 2026-09-27). It ran against the installed Revit 2027 application
(build `27.0.10.13`) on commit `3186138` (CI run `36360892079` green; deployed build id
`20260927-190748-fdddfc27`, `SolidGround.Revit.dll` SHA-256 prefix `FDDDFC27...`, `SolidGround.Core.dll`
SHA-256 prefix `63A37B28...`, both signed Valid, `deploy -Verify` OK on 7 files). It used the same handle-only
UI Automation method and the same image-capture limitation as `docs/architecture/revit-interactive-dialog.md`'s
"Manual evidence (Revit 2027, 2026-09-27)" section, so the evidence is the text UI Automation reports as
displayed, not pixels. Every run used the committed synthetic example-site fixtures, stopped at the summary
without Create, and made no OpenTopography request.

- **Process mode, Find a parcel by coordinates.** The provenance step showed the process-mode sentence above
  and did not show the OpenTopography notice.
- **Fetch mode, Find a parcel by coordinates.** The provenance step showed the full OpenTopography notice,
  word for word as `OpenTopographyUsgs1mSource.AttributionNotice`, once, and did not show the process-mode
  sentence.
- **Fetch mode, Use the area in the settings file.** The provenance step showed the settings-file sentence and
  the OpenTopography notice once, with no geocode or parcel attribution, as described above.

Cancel closed the dialog quietly each time, with no `TaskDialog`. Revit closed without a save prompt. The
settings file was restored to its original content, the default template's hash was unchanged, no output was
written, and the session log showed no errors and no unredacted key parameter.

## Regrid Data Store obligations

The Regrid Data Store License (https://app.regrid.com/store/license, retrieved 2026-09-27) states, verbatim:

> "The Term of this license begins on the date LICENSEE purchases the selected data and, unless terminated
> earlier pursuant to this Agreement's provisions, will continue in effect until the one year anniversary of
> this date (the "Term")."

> "Upon expiration of the Term the LICENSEE will promptly either cease all use of the data or delete the data
> entirely."

The same license also requires crediting "LOVELAND Technologies as a Data Source with a link to regrid.com on
each public facing webpage that uses this data or any written work, private or published" (broader than a
public-webpage-only credit), bars resale/sublicensing/redistribution to third parties, grants only a one-time
export with no further data updates, and -- a clause not previously recorded in any note in this repository --
bars inputting the licensed data into an AI model "to customize, train, fine-tune, or improve" it, except for a
narrow carve-out for a model used solely for the licensee's own internal purposes whose outputs do not
reproduce or make available any substantial portion of the data to third parties. SolidGround does not
reproduce Regrid's actual license text anywhere in code or in a shipped fixture; the quotations above exist
only in this note, for citation purposes, exactly like every other license excerpt this note quotes.

**Nothing has ever actually been purchased.** Regrid Data Store's Standard tier is described in
`docs/architecture/parcel-boundary-sources.md`'s "Regrid Data Store as the reference purchase" section purely
as a factual design target for `LocalParcelFileFieldMap.RegridStandardDefault`'s field-name shape -- a
reference/design-target product, not something this repository or SolidGround has bought. There is
consequently no live purchase date and no real lapse date to track today, so this issue records the Term and
cease-use-or-delete obligation as **operator guidance for a future real purchase**, not as a live countdown,
schema field, or dialog timer:

1. `LocalParcelFileOptions.LicenseDisclaimerText`'s doc-comment (`src/SolidGround.Core/Sources/LocalParcelFile/LocalParcelFileOptions.cs`)
   instructs an operator who configures a real Regrid Data Store export to include their own purchase date and
   this Term/cease-use-or-delete obligation directly in that string -- because this exact string is what the
   dialog's provenance/accuracy preview panel already displays verbatim, every run (the `parcelDisclaimer`
   `TextBlock` in `SolidGroundDialog.BuildProvenancePreviewPanel`, visible whenever the AOI source is a parcel
   lookup, rendering whichever selected candidate's `LicenseDisclaimerText` is populated -- for a Local Parcel
   File candidate, that is this operator-supplied Regrid disclaimer text), which is precisely "where an
   operator will see it before lapse."
2. The settings JSON template (`SolidGround.Revit/Settings/RevitSettingsIo.cs`) carries a `//` comment
   immediately beside `"localParcelFileLicenseDisclaimerText": null,` restating the same instruction at the
   point an operator actually edits the file -- the template's `JsonDocumentOptions { CommentHandling =
   JsonCommentHandling.Skip }` already supports `//` comments, unchanged by this issue.

**Rejected:** a new `PurchaseDate`/computed-expiry field on `LocalParcelFileOptions` plus a dialog countdown.
This would add schema and test surface -- a new settings field, a new `ContractModelTests` construction case, a
new JSON template key -- to track a purchase that has never happened. Straightforward to add later, the day a
real purchase actually occurs.

## Never-default sources

Seven vendors/services are deliberately excluded from every default path in this repository. None of their
data, domains, or identifiers ships in any fixture or reaches any default code path; see "Fixture and
default-path guarantees" below for how that is enforced going forward, not merely asserted today.

| Vendor | Why excluded as a default | Citation, retrieved 2026-09-27 unless noted |
| --- | --- | --- |
| County `GeocodeServer` proxy | An anonymous call's credit-consumption against a publicly-shared, organization-hosted proxy is unconfirmed by any primary source found -- genuinely unresolved, not merely undocumented. Separately, carried forward from 2026-09-21 research and not re-verified this session (re-verifying it would require the real county service URL this repository's privacy rule forbids using): the proxy publishes no terms (`licenseInfo`/`accessInformation` both null) | Credit-consumption question: https://doc.arcgis.com/en/arcgis-online/reference/geocode.htm and https://doc.arcgis.com/en/arcgis-online/administer/credits.htm both describe general ArcGIS Online credit mechanics, not this specific anonymous-shared-proxy case; the one on-point Esri Support KB article returned HTTP 403 on independent attempts. "No published terms" fact: `docs/architecture/phase-3-interactive-add-in-research.md`, retrieved 2026-09-21 there (its own source URL is redacted from this repository per the 2026-09-24 privacy adjustment) |
| Public Nominatim | Usage policy bars a silent/generic default outright: "The public Nominatim API must not be built into, offered through, suggested by, or automatically generated by no-code, low-code, or vibe-coding platforms as a generic geocoding, address lookup, place search, or map search service." | https://operations.osmfoundation.org/policies/nominatim/ |
| Regrid live API without written consent | Bars offline storage and derivative works without Loveland/Regrid's written approval ("Customer may not ... cache the Data or otherwise store Data offline without written approval from Company," and creating a derivative work needs written approval and "additional fees may be applicable"); it does separately permit caching the API's own retrieved parcel records, revocably, destroyed on termination or on Regrid's own notice -- a materially weaker position than the Data Store's fixed one-year Term, which is why the Data Store purchase, not the live API, is this repository's reference acquisition (see "Regrid Data Store obligations" above) | https://regrid.com/terms/api |
| ATTOM | Evaluation-only ("Customer may utilize the ATTOM Products solely to test and evaluate ... for the purpose of determining whether to enter into a subsequent data license agreement"); a 24-hour caching cap; bars commercial exploitation; ATTOM retains ownership of derivative works | https://api.developer.attomdata.com/legal |
| LightBox | Public developer/trial terms: a 30-day term from download, no commercial or production use, no third-party access, no resale/redistribution, must delete/destroy the information on expiration | https://developer.lightboxre.com/terms |
| Cotality | Terms of Use bar using the services "as a basis for a derivative work"; separate Evaluation Terms assign any derivative's ownership to CoreLogic, bar creating derivative works at all during evaluation, and additionally bar uploading the services to any AI/LLM platform | https://www.cotality.com/legal/terms-of-use, https://www.cotality.com/legal/evaluation-terms-and-conditions |
| Living Atlas "Regrid USA Nationwide Parcel Boundaries" | Republishes Regrid's own restricted data under the identical Data Store license (`licenseInfo`: "All of Regrid's datasets, including the data layer published on ArcGIS Living Atlas and the data purchased on the Regrid data store are governed by their data use license agreement and terms," linking to the Data Store License above); this item's current title differs from the issue body's colloquial "USA Parcels" shorthand, which is not confirmed as any current, official title | https://www.arcgis.com/sharing/rest/content/items/a2050b09baff493aa4ad7848ba2fac00?f=json |

## Fixture and default-path guarantees

Three tests prove AC2's "no public fixture or default path contains their data" going forward, not merely
today, plus reaffirm two guarantees this issue did not need to change:

- `FixtureSecurityTests.AttributionConstantsContainNoRequestUrls` scans every shipped attribution constant
  (`OpenTopographyUsgs1mSource.AttributionNotice`, `CensusGeocoder.AttributionNotice`, `EsriGeocoder.AttributionNotice`)
  for `http://`/`https://` at its own definition site, not only at fixture-commit time -- guarding the landmine
  described in "OpenTopography USGS 1 m attribution" above before a literal URL baked into a constant could
  ever reach a committed golden export document and trip the pre-existing `CommittedFixturesContainNoCredentialsOrRequestUrls`.
- `FixtureSecurityTests.CommittedFixturesContainNoNeverDefaultVendorDataOrIdentifiers` scans every file under
  `tests/SolidGround.Tests/Fixtures/`, case-insensitive, for each never-default vendor's own domain or
  identifier: `nominatim`, `attomdata.com`, `lightboxre.com`, `cotality`, `corelogic`, `regrid.com`, and the
  Living Atlas item id `a2050b09baff493aa4ad7848ba2fac00`. `corelogic` is checked separately from `cotality`
  because Cotality's own Evaluation Terms use "CoreLogic" as the operative legal name in the derivative-ownership
  clause ("will be owned by CoreLogic and become the intellectual property of CoreLogic") -- a fixture carrying
  that name but not the brand name would otherwise pass this scan undetected.
- `ContractModelTests.AddressGeocoderProviderHasExactlyTheThreeApprovedMembersCensusGeocodioEsri` and
  `...ParcelBoundarySourceKindHasExactlyTheTwoApprovedMembersCountyRegistryAndLocalParcelFile` are
  enum-completeness guards: a fourth geocoder provider (for example a future Nominatim adapter) or a third
  parcel-source kind cannot be added without a deliberate, reviewed test change.

Reaffirmed, not newly established, by this issue: the county parcel registry ships zero real entries (`docs/architecture/parcel-boundary-sources.md`),
and the regenerated golden fixture's own new `attribution` text contains no `http://`/`https://` (its ampersand
renders as the JSON escape `\u0026`, confirmed by the pre-existing
`FixtureSecurityTests.CommittedFixturesContainNoCredentialsOrRequestUrls` test, which already scans
every file under `Fixtures/` for this marker).

**Rejected:** a blunter "no vendor name string anywhere in `src/`" scan. It would false-positive on
`LocalParcelFileFieldMap.RegridStandardDefault`'s own legitimate field-name-compatibility doc comments (which
must name Regrid to explain the field-name shape they mirror) and on this very note's own prose explaining the
exclusions.

## Citations

Every claim above states its own URL and retrieval date inline; this section consolidates them in one place.
All retrieved 2026-09-27 unless noted.

- OpenTopography, Terms of Use ("API Agreement" section, NSF acknowledgment sentence, "Last Updated: October 8,
  2025"): https://opentopography.org/usageterms
- OpenTopography, Citation Policy (citation-format scheme; does not itself contain the NSF acknowledgment
  sentence): https://opentopography.org/citations
- OpenTopography, USGS 1 m dataset landing page (dataset identity, NAVD88 vertical reference), retrieved
  2026-09-19: https://portal.opentopography.org/raster?opentopoID=OTNED.012021.4269.3
- US Census Bureau, Data API Terms of Service (required attribution sentence), retrieved 2026-09-25,
  re-verified 2026-09-27: https://www.census.gov/data/developers/about/terms-of-service.html
- US Census Bureau, Geocoding Services API documentation (no terms/license/attribution section of its own;
  TIGERweb-layers language for the county-lookup row above): https://geocoding.geo.census.gov/geocoder/Geocoding_Services_API.html
- Geocodio, API documentation and Terms of Use Section 8.4, retrieved 2026-09-25, re-verified 2026-09-27:
  https://www.geocod.io/docs/, https://www.geocod.io/terms-of-use/
- Esri, "Powered by Esri" no-map attribution requirement, retrieved 2026-09-25, re-verified 2026-09-27:
  https://developers.arcgis.com/documentation/esri-and-data-attribution/no-map/
- Esri, geocoding credit-consumption documentation (does not resolve the county-proxy question; see the table
  above): https://doc.arcgis.com/en/arcgis-online/reference/geocode.htm, https://doc.arcgis.com/en/arcgis-online/administer/credits.htm
- Regrid, Data Store License (Term, cease-use-or-delete, attribution, resale bar, AI-training bar):
  https://app.regrid.com/store/license
- Regrid, live API Terms of Service (caching/derivative-works terms distinct from the Data Store License
  above): https://regrid.com/terms/api
- OSM Foundation, Nominatim usage policy: https://operations.osmfoundation.org/policies/nominatim/
- ATTOM, Developer/API legal terms: https://api.developer.attomdata.com/legal
- LightBox, Developer Portal trial terms: https://developer.lightboxre.com/terms
- Cotality, Terms of Use and Evaluation Terms and Conditions: https://www.cotality.com/legal/terms-of-use,
  https://www.cotality.com/legal/evaluation-terms-and-conditions
- Esri/ArcGIS Living Atlas, "Regrid USA Nationwide Parcel Boundaries" item metadata:
  https://www.arcgis.com/sharing/rest/content/items/a2050b09baff493aa4ad7848ba2fac00?f=json
- `docs/architecture/phase-3-interactive-add-in-research.md`, county `GeocodeServer` proxy's "no published
  terms" finding, retrieved 2026-09-21 there (carried forward, not re-dated, per this note's own "Open
  questions and owner decisions" below); its own source URL is redacted from this repository.
- `docs/architecture/parcel-boundary-sources.md`, the 2026-09-24 privacy adjustment (no real county name,
  GEOID, service URL, or license text committed anywhere in this repository).

## Open questions and owner decisions

**Design decisions**, each already applied above. The owner selected this issue as part of Phase 3's "Continue in order" sequence; each choice below took the recommended option, backed by the repository precedent cited with it, and the owner may revise any of them:

1. **OpenTopography attribution wording.** The two-sentence constant in "OpenTopography USGS 1 m attribution"
   above: the NSF sentence verbatim, plus SolidGround's own honest dataset description, not a fabricated
   per-dataset dynamic citation string. Rejected for now: also capturing OpenTopography's exact dynamic "Data
   Citation" copy-button text, which needs a live browser session to extract -- a possible future manual-evidence
   follow-up, not a blocker (see Open question 1 below).
2. **Schema version 4 bump.** Not really discretionary: this repository's own strict, no-migration versioning
   policy requires incrementing `TerrainProvenance.CurrentSchemaVersion` for any document-shape change,
   including a nullable field. Proceeding was necessary for AC1 to be met for OpenTopography at all.
3. **Census county lookup's attribution.** Document-only reuse of `CensusGeocoder.AttributionNotice`, no new
   constant or field, since it is the same host and is never itself carried into the export as a distinct
   source.
4. **AC4 mechanism.** Pure documentation (doc-comment plus settings-template comment plus this note's own
   "Regrid Data Store obligations" section), given no live purchase or lapse date exists to compute against
   today. See "Regrid Data Store obligations" above for what was rejected instead.
5. **Never-default guard precision.** The two-pronged approach in "Fixture and default-path guarantees" above
   (fixture-content scan plus enum-completeness guards), including `corelogic` in the scan alongside `cotality`,
   over a blunter "no vendor name string anywhere in `src/`" scan that would false-positive on legitimate
   doc-comment prose.
6. **Process-mode dialog wording.** The fixed, conditional sentence in "Dialog: attribution shown once per run"
   above, rather than eagerly reading the sidecar file at dialog-construction time.
7. **Census Data API Terms of Service's scope over the geocoder host.** Keep displaying `CensusGeocoder.AttributionNotice`
   regardless, "out of caution," preserving today's behavior -- see Open question 2 below.
8. **Scope of AC1's "provenance export."** The JSON `TerrainExportBundle` document only, not the Extensible
   Storage entity `SolidGround_Provenance_Toposolid` -- see "Extensible Storage: deliberately unchanged" above,
   following the identical split Issue #33 already applied to `AddressParcelProvenance` itself.

**Open questions**, unresolved by any primary source found and not decided by this issue:

1. OpenTopography's dataset landing page renders its exact "Data Citation" text client-side; a plain page fetch
   cannot extract it. A live browser session could capture the dynamic citation string for the USGS 1 m dataset
   specifically, if a future issue wants `AttributionNotice`'s first sentence to match that exact dynamic text
   rather than SolidGround's own description of the dataset.
2. Whether the general census.gov Data API Terms of Service (https://www.census.gov/data/developers/about/terms-of-service.html)
   contractually binds the separate, keyless `geocoding.geo.census.gov` host remains unresolved by any primary
   source, even after a fresh 2026-09-27 check -- this mirrors `docs/architecture/address-geocoding.md`'s own
   pre-existing Open Question 2. `CensusGeocoder.AttributionNotice` is shown regardless (design decision 7
   above).
3. No Census-issued (as opposed to a Data.gov catalog metadata record) TIGERweb-specific terms/license page was
   found; if a future note wants a Census-issued TIGERweb license statement rather than the catalog record
   already cited, a further search of `tigerweb.geo.census.gov` itself may be needed.
4. The Living Atlas item this repository cites (`a2050b09baff493aa4ad7848ba2fac00`) is titled "Regrid USA
   Nationwide Parcel Boundaries" today; whether "USA Parcels" is, or ever was, a current official Esri title
   for the same or a related item is unconfirmed. The underlying exclusion reason (Regrid-sourced,
   Regrid-licensed, restricted) is unaffected either way.
