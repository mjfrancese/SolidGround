# Phase 3 AGENTS.md and conventions-note amendment proposal (Issue #34 / PH3-7)

## Purpose and status

This note is the deliverable for SolidGround Issue #34 (PH3-7): a reviewable, non-applied list of
`AGENTS.md` and `docs/architecture/revit-add-in-conventions.md` wording changes that Phase 3's now-closed
child issues make true. **It is a proposal only.** Neither `AGENTS.md` nor
`docs/architecture/revit-add-in-conventions.md` is edited by this issue; this note is the only file Issue #34
creates or changes. Both files stay exactly as they are at this issue's close — verifiable by a `git diff`
showing no changes to either path.

Every change below carries a stable ID (`A1`, `A2`, ... for `AGENTS.md`; `C1`, `C2`, ... for the conventions
note) and its own `Disposition` field. The owner recorded a disposition for every change on 2026-09-27 (see
"Disposition record" below): every change was accepted, and `A1` was accepted in a condensed form whose exact
accepted text is given under `A1`. The "Disposition record" table at the end of this note is the single place every
change's disposition is tracked going forward. No dependent issue may apply any wording proposed here until
that specific change's disposition is recorded as something other than `Pending owner`; accepting this note's
existence is not the same as accepting any individual change inside it.

One sentence in `AGENTS.md`'s "Mission and current boundary" section is deliberately **not** proposed below,
because it is already applied, and this issue's own scope asks that this fact be recorded plainly rather than
re-proposed. `docs/planning/phase-3-draft-issues.md`'s "Proposed AGENTS.md amendment" section records it as
"ACCEPTED 2026-09-21, APPLIED," and commit `e88e3cd` ("Add the accepted Phase 3 boundary sentence to
AGENTS.md") applied it the same day, without changes, to the end of that section's paragraph:

> Phase 3 adds an interactive Revit 2027 add-in workflow that turns a street address into geocoded
> coordinates, resolves a matching parcel boundary, and lets the operator confirm both — together with an
> optional shared-coordinates write and a native property line — before today's terrain pipeline runs, as
> researched in `docs/architecture/phase-3-interactive-add-in-research.md` (2026-09-21). The agent must not
> begin Phase 3 without an explicit implementation task.

Every change proposed below is additional to that sentence, never a replacement for it; `A1` proposes text
that continues the very same paragraph immediately after it.

Every proposed change below traces to a specific decision in one of Phase 3's eight completed child issues:
PH3-0 (#27), PH3-1 (#28), PH3-2 (#29), PH3-3 (#30), PH3-4 (#31), PH3-5 (#32), PH3-6 (#33), and PH3-8 (#35).
PH3-7 is this issue (#34) and cites nothing from itself. No change below proposes a new rule, only wording
that records a decision or a shipped fact that already exists.

## How to read a change

- Every change gets a stable ID: `A1`, `A2`, ... for `AGENTS.md`; `C1`, `C2`, ... for
  `docs/architecture/revit-add-in-conventions.md`. IDs are assigned in the order their target section appears
  in the file, top to bottom.
- Each change states:
  - **Target section** — the exact heading, table row, or bullet the change touches, named exactly as it
    appears in the file today.
  - **Old text** — the current wording, quoted verbatim, or "None — new passage" when nothing existing covers
    the subject at all.
  - **Proposed text** — the exact wording this proposal recommends, either replacing the old text or inserted
    at the stated place. This is the only place new wording appears; nothing here is applied by this issue.
  - **Trace** — the Phase 3 issue and decision that makes the proposed text true, plus the exact design-note
    section title that records it, so the owner or a later reader can verify the claim independently of this
    note.
  - **Rationale** — one to two sentences on why the change is needed.
  - **Disposition** — the owner's recorded decision for that change; see "Disposition record" below.
- Every "Old text" quote below was checked verbatim against `AGENTS.md` and
  `docs/architecture/revit-add-in-conventions.md` as they stand at commit `ace27ba` (neither file changed
  between `9c1cb0a` and `ace27ba`). Neither file is edited by this issue; both stay byte-identical to that
  commit at this issue's close.
- A decision is cited by document name and exact section title, never by a bare number. Two different
  documents each carry their own numbered "Owner decisions" list from two different dates, and each list
  happens to reuse item numbers for unrelated decisions — see "Push button versus a second command" below for
  the specific collision this proposal had to avoid.
- Every design-note citation below names the exact section title in that note. No citation below points at a
  scratch file, a generic "design.md," or a section symbol.

## Proposed changes to AGENTS.md

Listed in file order (top to bottom).

### A1. Mission and current boundary — Phase 3 status paragraph

- **Target section:** "Mission and current boundary"
- **Old text:** None — new passage. Inserted at the very end of the section's second paragraph, immediately
  after the already-applied sentence quoted in "Purpose and status" above ("...The agent must not begin
  Phase 3 without an explicit implementation task."), continuing the same paragraph.
- **Proposed text:**

  > Issue #27 (PH3-0) generalized the OpenTopography-only key-resolution and query-redaction logic into a
  > provider-agnostic `SolidGround.Core.Http` component (`ApiKey`, `SensitiveQueryParameterNames`,
  > `SensitiveQueryRedactor`, `ApiKeyResolver`, `UserSecretsFileLocator`) with no behavior change for
  > OpenTopography itself, recorded in `docs/architecture/shared-http-redaction-and-key-resolution.md` (commit
  > `b78ecc2`). Issue #28 (PH3-1) added a pluggable `IAddressGeocoder` with three implementations — the
  > keyless Census Bureau Geocoder as the default, and two keyed opt-ins, Geocodio (`GEOCODIO_API_KEY`) and
  > Esri (`ARCGIS_API_KEY`, `forStorage=true`) — recorded in `docs/architecture/address-geocoding.md` (commit
  > `e110d7a`); a county's own `GeocodeServer` proxy and public Nominatim were deliberately never wired as a
  > default. Issue #29 (PH3-2) added a pluggable `IParcelBoundarySource` with two implementations, a
  > per-county ArcGIS REST registry keyed by 5-digit Census GEOID and loaded only from a machine-local,
  > never-committed registry file with no built-in real county entry, and a local file reader for a
  > user-purchased parcel export, plus a three-layer owner/mailing-field exclusion guard, recorded in
  > `docs/architecture/parcel-boundary-sources.md` (commit `2c91cc6`). Issue #30
  > (PH3-3) added `PropertyLine` creation, restricted to parcel areas of interest and created in the same
  > transaction as the toposolid, and an opt-in, default-off shared-coordinates write gated by a
  > live-evidence-corrected detector, bumping the placement-record schema to version 3; recorded in
  > `docs/architecture/revit-property-line-and-shared-coordinates.md` (commits `c81abb1`, `842cadc`, corrected
  > by `2ffc101`, evidenced by `8e85a53`). Issue #31 (PH3-4) turned on `UseWPF` and added
  > `CommunityToolkit.Mvvm` 8.4.2 (exact-pinned, MIT-licensed, referenced only from `SolidGround.Revit.csproj`)
  > to build one modal, code-only `SolidGroundDialog` — zero `.xaml`/BAML — opened from inside
  > `CreateToposolidCommand.Execute` before Preflight and before any transaction, with no new command, ribbon
  > button, or panel, and added a Revit-free nearby-parcel fallback tier (`NearbyParcelBoundaryFinder`) so a
  > geocoded point that lands just outside its true parcel still resolves; recorded in
  > `docs/architecture/revit-interactive-dialog.md` (commits `19acf6d`, `5559ec3`, `ae12f7c`, evidenced by
  > `ea96afb`, whose "Acceptance criteria (PH3-4 / Issue #31)" table records that same 2026-09-27 Revit 2027
  > session confirming acceptance criteria AC1 through AC5) and `docs/architecture/parcel-boundary-sources.md`'s
  > own "Nearby-parcel fallback tier" section.
  > Issue #32 (PH3-5) added `geocode` and `parcel` CLI verbs reusing the same interfaces, with the county GEOID
  > resolved automatically through a new keyless `CensusCountyLookup` unless overridden, recorded in
  > `docs/architecture/census-county-lookup.md` (commit `68a0f33`). Issue #33 (PH3-6) bumped
  > `TerrainProvenance.CurrentSchemaVersion` to 3, adding an optional `AddressParcelProvenance` record to the
  > JSON export only — the placement-record writer and the Extensible Storage schema stay unchanged — recorded
  > in `docs/architecture/address-parcel-provenance.md` (commit `dacdcc9`). Issue #35 (PH3-8) added
  > OpenTopography's required attribution notice end to end into the provenance export, bumping
  > `TerrainProvenance.CurrentSchemaVersion` to 4, and documented a never-default policy, with citations, for
  > seven vendors and services whose own terms bar a silent default; recorded in
  > `docs/architecture/source-licensing-and-attribution.md` (commit `3186138`, Revit 2027 evidence in commit
  > `9c1cb0a`). Issues #27 through #33 and #35 are complete as of 2026-09-27. Issue #34 (PH3-7) is this
  > proposal: it recommends `AGENTS.md` and conventions-note wording, pending the owner's own accept/edit/reject
  > decision per change, recorded in `docs/architecture/phase-3-agents-md-amendment-proposal.md`.

- **Trace:** PH3-0 #27 (`shared-http-redaction-and-key-resolution.md`, "Purpose and boundary"); PH3-1 #28
  (`address-geocoding.md`, "Provider comparison"); PH3-2 #29 (`parcel-boundary-sources.md`, "Source
  comparison"); PH3-3 #30 (`revit-property-line-and-shared-coordinates.md`, "Owner decisions (2026-09-26)");
  PH3-4 #31 (`revit-interactive-dialog.md`, "Purpose and boundary"; `parcel-boundary-sources.md`,
  "Nearby-parcel fallback tier"); PH3-5 #32 (`cli-workflow.md`'s "### geocode" and "### parcel" subsections;
  `census-county-lookup.md`, "Purpose and boundary"); PH3-6 #33 (`address-parcel-provenance.md`, "Schema
  version 3 shape"); PH3-8 #35 (`source-licensing-and-attribution.md`, "Never-default sources").
- **Rationale:** This paragraph already narrates every completed Phase 2 issue through Issue #17's 0.1.0
  release before stating the Phase 3 boundary sentence, but stops narrating there even though all eight Phase
  3 issues that sentence authorized have since shipped and closed. Continuing the same paragraph in the same
  style keeps it an accurate, current record instead of leaving readers to piece Phase 3's status together
  from closed-issue comments.
- **Accepted text (condensed, per the owner's 2026-09-27 disposition "Accept, but condense": keep the Phase 3
  status in a few sentences that name each issue's outcome and design note, without commit hashes or type
  names).** This, not the longer proposed text above, is the wording to apply:

  > Phase 3's child issues were completed by 2026-09-27, each recorded in its own design note. Issue #27
  > (PH3-0) moved API-key resolution and query redaction into one shared Core component
  > (`docs/architecture/shared-http-redaction-and-key-resolution.md`). Issue #28 (PH3-1) added address
  > geocoding: the keyless Census Geocoder by default, with Geocodio and Esri as keyed opt-ins
  > (`docs/architecture/address-geocoding.md`). Issue #29 (PH3-2) added parcel-boundary sources: a
  > machine-local county registry, which ships with no real county, and a user-supplied local parcel file
  > (`docs/architecture/parcel-boundary-sources.md`). Issue #30 (PH3-3) added a native property line for
  > parcel areas of interest and an opt-in, default-off shared-coordinates write
  > (`docs/architecture/revit-property-line-and-shared-coordinates.md`). Issue #31 (PH3-4) added the
  > interactive dialog inside the Create Toposolid command, including a nearby-parcel fallback when a geocoded
  > point misses its parcel (`docs/architecture/revit-interactive-dialog.md`). Issue #32 (PH3-5) added the CLI
  > `geocode` and `parcel` commands with an automatic Census county lookup (`docs/architecture/cli-workflow.md`
  > and `docs/architecture/census-county-lookup.md`). Issue #33 (PH3-6) added address and parcel provenance to
  > the JSON export (`docs/architecture/address-parcel-provenance.md`). Issue #35 (PH3-8) recorded every
  > source's attribution and the providers that must never be a default
  > (`docs/architecture/source-licensing-and-attribution.md`). Issue #34 (PH3-7) recorded the owner's
  > disposition of each resulting `AGENTS.md` and conventions-note change
  > (`docs/architecture/phase-3-agents-md-amendment-proposal.md`).

- **Disposition:** Accepted with edit (condensed; accepted text above), 2026-09-27.

### A2. Architecture table — `SolidGround.Core` row

- **Target section:** "Architecture" table, `src/SolidGround.Core` row
- **Old text:**

  ```
  | `src/SolidGround.Core` | `net10.0` | Revit-free domain logic: AOIs, OpenTopography requests, AAIGrid parsing, coordinate transforms, clipping, unit conversion, local-origin transforms, decimation, provenance, and exports. |
  ```

- **Proposed text:**

  ```
  | `src/SolidGround.Core` | `net10.0` | Revit-free domain logic: AOIs, OpenTopography requests, AAIGrid parsing, coordinate transforms, clipping, unit conversion, local-origin transforms, decimation, provenance, and exports; address geocoding, parcel-boundary resolution, county lookup, shared HTTP redaction and key resolution, and local-boundary cleanup. |
  ```

- **Trace:** PH3-0 #27 (`shared-http-redaction-and-key-resolution.md`, "The five new types"); PH3-1 #28
  (`address-geocoding.md`, "Core types and files"); PH3-2 #29 (`parcel-boundary-sources.md`, "Core types and
  files"); PH3-3 #30 (`revit-property-line-and-shared-coordinates.md`, "Geometry cleanup contract (Core,
  Revit-free)"); PH3-5 #32 (`census-county-lookup.md`, "Purpose and boundary"); PH3-6 #33
  (`address-parcel-provenance.md`, "Schema version 3 shape").
- **Rationale:** Core gained five Phase 3 capabilities (two pluggable-source families, shared HTTP plumbing,
  county lookup, and boundary cleanup) that this row's description doesn't name, even though it is still
  correct that none of them are Revit-specific.
- **Disposition:** Accepted as written, 2026-09-27.

### A3. Architecture table — `SolidGround.Revit` row

- **Target section:** "Architecture" table, `src/SolidGround.Revit` row
- **Old text:**

  ```
  | `src/SolidGround.Revit` | `net10.0-windows` in Phase 2 | Thin Revit host adapter, ribbon application, external command, boundary conversion, toposolid creation, and provenance attachment. |
  ```

- **Proposed text:**

  ```
  | `src/SolidGround.Revit` | `net10.0-windows` | Thin Revit host adapter, ribbon application, external command, boundary conversion, toposolid creation, provenance attachment, an interactive address/parcel confirmation dialog, property-line creation, and the opt-in shared-coordinates write. |
  ```

- **Trace:** PH3-3 #30 (`revit-property-line-and-shared-coordinates.md`, "PropertyLine creation (Revit)" and
  "Shared-coordinates detection, write, and verification (Revit)"); PH3-4 #31 (`revit-interactive-dialog.md`,
  "Purpose and boundary").
- **Rationale:** The row still says "in Phase 2" even though Phase 3 code now lives in the same project, and
  names neither the dialog nor the two Revit-side capabilities Issues #30 and #31 added.
- **Disposition:** Accepted as written, 2026-09-27.

### A4. Revit 2027 rules — new API surface paragraph

- **Target section:** "Revit 2027 rules" (new paragraph, placed after the existing point-budget paragraph)
- **Old text:** None — new passage.
- **Proposed text:**

  > Issue #30 (PH3-3) verified and began using a further Revit 2027 API surface, beyond the toposolid-creation
  > table already verified for Issue #15: `PropertyLine.Create(Document, IList<CurveLoop>)`,
  > `PropertyLine.IsValidBoundary(IList<CurveLoop>)`, `PropertyLine.IsClosedLoop()`/`.Area`,
  > `ProjectLocation.SetProjectPosition`/`.GetProjectPosition`, the `ProjectPosition` constructor and its
  > `EastWest`/`NorthSouth`/`Elevation`/`Angle` members, `BasePoint.GetSurveyPoint`/`.Position`,
  > `Document.ProjectLocations`, `Application.ShortCurveTolerance`, and `Application.VertexTolerance`, each
  > verified against the installed Revit 2027 SDK and cited in
  > `docs/architecture/revit-property-line-and-shared-coordinates.md`'s "Revit 2027 API surface used (new
  > members, beyond `revit-toposolid-creation.md`'s existing table)". No Revit API member directly answers
  > whether a document's shared coordinates have already been set; the agent must keep treating the documented
  > detection proxy in that note's "Shared-coordinates detection" section as owner-accepted evidence, not
  > certified Autodesk behavior, and must re-verify it before depending on it further.

- **Trace:** PH3-3 #30 (`revit-property-line-and-shared-coordinates.md`, "Revit 2027 API surface used (new
  members, beyond `revit-toposolid-creation.md`'s existing table)" and "Shared-coordinates detection").
- **Rationale:** "Revit 2027 rules" is where this repository already records every verified, cited Revit API
  member (see the point-budget and add-in-isolation paragraphs already there); Issue #30 verified a new
  surface this section says nothing about yet, plus a documented negative finding worth stating in
  `AGENTS.md`'s own voice.
- **Disposition:** Accepted as written, 2026-09-27.

### A5. Revit 2027 rules — point-budget paragraph, one-clause addition

- **Target section:** "Revit 2027 rules", the existing point-budget paragraph (the one beginning "The
  installed Revit 2027 `SiteDB.dll` contains both `NativeToposolidMaxPointThreshold`...")
- **Old text:** (final sentence, for anchoring) "This finding and guard do not change the conservative ~15,000
  application default named above."
- **Proposed text:** "This finding and guard do not change the conservative ~15,000 application default named
  above. Issue #31 (PH3-4) extracted this rule's own threshold check into a shared
  `RevitIniToposolidThresholds.ExceedsNativeThreshold`/`.DescribeExceedance` pair so the interactive dialog can
  show the same warning inline, before Preflight authoritatively re-checks it; Preflight stays the sole gate."
- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Purpose and boundary", Stage A bullet).
- **Rationale:** Issue #31 shared this exact rule's own implementation with the new dialog; naming that
  consumer keeps the paragraph complete with no change to the rule itself.
- **Disposition:** Accepted as written, 2026-09-27.

### A6. Revit add-in conventions (AGENTS.md section) — Ribbon and command bullet

- **Target section:** "Revit add-in conventions", "Ribbon and command" bullet
- **Old text:** "- Ribbon and command: one tab \"SolidGround\" (owner decision 8, 2026-09-20: the dedicated tab
  is kept with Autodesk's Add-Ins-tab guideline in view; see the conventions note), one panel, one
  `PushButtonData` for `CreateToposolidCommand`, shipped with an icon in the initial milestone. The tooltip is
  full-sentence prose. No `IExternalCommandAvailability`; a read-only Preflight runs before any transaction and
  returns `Result.Cancelled` on rejection. `[Transaction(TransactionMode.Manual)]` plus
  `[Regeneration(RegenerationOption.Manual)]` is fixed by convention. The top-level catch filter excludes
  `OutOfMemoryException` and `StackOverflowException`."
- **Proposed text:** append one sentence at the end: "...The top-level catch filter excludes
  `OutOfMemoryException` and `StackOverflowException`. Issue #31 (PH3-4) added one modal dialog inside this
  same command, opened from `Execute` before Preflight and any transaction; it is not a second command."
- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Purpose and boundary": "No new command, ribbon button,
  or panel (PH3-7 unchanged)"). See "Push button versus a second command" below for the full recommendation
  and rationale, including why decision 8's AOI-plumbing point needs no companion clause here.
- **Rationale:** This is the `AGENTS.md`-side twin of the conventions note's own "Ribbon and command structure"
  bullet (`C5` below); both need the identical one-sentence fact so a reader of either file learns the dialog
  did not add a second command.
- **Disposition:** Accepted as written, 2026-09-27.

### A7. Data and numeric contracts — new never-default source-licensing paragraph

- **Target section:** "Data and numeric contracts" (new paragraph, placed immediately after the existing AOI
  paragraph — "The agent must support these AOIs: ... A sentinel must never become an elevation." — and before
  the GeoKeys paragraph)
- **Old text:** None — new passage.
- **Proposed text:**

  > The agent must never wire a county's own `GeocodeServer` proxy, the public Nominatim service, Regrid's live
  > API without written consent, ATTOM, LightBox, Cotality, or Esri Living Atlas's "Regrid USA Nationwide
  > Parcel Boundaries" layer into any default code path: each one's own published terms bar a silent default,
  > bar offline caching or derivative works, are evaluation- or trial-only, republish another vendor's own
  > restricted data, or — for the county's own `GeocodeServer` proxy — publish no terms at all, leaving
  > authorized use and its credit-consumption cost unconfirmed; each reason is cited with retrieval dates in
  > `docs/architecture/source-licensing-and-attribution.md`'s "Never-default sources" table. A local parcel
  > file sourced from a real purchase (for example, the Regrid Data Store's one-year, cease-use-or-delete
  > license) must carry its own license disclaimer text through to the operator and the export; the agent must
  > never invent a purchase date or a lapse countdown that has not actually occurred. Every source the agent
  > adds must show its own attribution or license text to the operator at least once per run and must carry it
  > into the provenance export; the agent must never fabricate, omit, or silently substitute another source's
  > attribution.

- **Trace:** PH3-8 #35 (`source-licensing-and-attribution.md`, "Never-default sources", "Regrid Data Store
  obligations", "Dialog: attribution shown once per run"); the underlying "never wire as default" decisions
  originate at PH3-1 #28 (`address-geocoding.md`, "Why no county-proxy or public-Nominatim default") for the
  county proxy and Nominatim, and at `docs/architecture/phase-3-interactive-add-in-research.md`'s "Owner
  decisions (2026-09-21)" (decisions 2 and 3) and its "Commercial parcel sources: terms and cost" section for
  Regrid's live API, ATTOM, LightBox, Cotality, and Living Atlas.
- **Rationale:** `AGENTS.md`'s existing OpenTopography paragraph already states a "must surface authorization
  failures... must not silently fall back" rule for one source; Phase 3 added seven more sources whose own
  terms need the identical never-default discipline, formalized and cited by Issue #35, with nothing in
  `AGENTS.md` recording it yet.
- **Disposition:** Accepted as written, 2026-09-27.

### A8. Dependency policy — new `CommunityToolkit.Mvvm` paragraph

- **Target section:** "Dependency policy" (new paragraph, placed after the existing ProjNet/NetTopologySuite
  paragraph and before the native-binary paragraph)
- **Old text:** None — new passage.
- **Proposed text:**

  > `CommunityToolkit.Mvvm` 8.4.2 is acceptable, exact-pinned, MIT-licensed, and referenced only from
  > `SolidGround.Revit.csproj`, never from `SolidGround.Core`. It supplies `INotifyPropertyChanged`
  > boilerplate, command wiring, and generator-backed observable properties across the interactive dialog's
  > whole content model (eleven sections, roughly sixteen bound values) — a cross-cutting UI-binding concern
  > broader than the small, well-bounded functions the dependency policy reserves for the standard library
  > alone, the same substantive-scope reasoning already used for NetTopologySuite; it carries zero transitive
  > package dependencies and no native code, verified before adoption.

- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Package: CommunityToolkit.Mvvm 8.4.2").
- **Rationale:** This section documents why every other named package is acceptable (ProjNet, NetTopologySuite,
  Nice3point); `CommunityToolkit.Mvvm` is a real, shipped, direct dependency with no equivalent paragraph yet,
  even though the dependency-policy rule it must satisfy ("must document why each package is needed") is
  already met by the design note.
- **Disposition:** Accepted as written, 2026-09-27.

### A9. Dependency policy — ProjNet/NetTopologySuite hedge cleanup

- **Target section:** "Dependency policy", the ProjNet/NetTopologySuite paragraph
- **Old text:** "ProjNet is acceptable in Phase 1 for managed horizontal coordinate transformations after its
  exact API and supported definitions are verified. It does not by itself justify claims about vertical datum
  transformations. NetTopologySuite is acceptable because robust GeoJSON/WKT parsing, polygon buffering,
  holes, multipolygons, and clipping are substantive geometry operations; the agent must avoid adding it if the
  accepted Phase 1 scope narrows to a simpler geometry contract. Neither package is part of Phase 0."
- **Proposed text:** "ProjNet is acceptable in Phase 1 for managed horizontal coordinate transformations after
  its exact API and supported definitions are verified. It does not by itself justify claims about vertical
  datum transformations. NetTopologySuite is acceptable because robust GeoJSON/WKT parsing, polygon buffering,
  holes, multipolygons, and clipping are substantive geometry operations; Phase 3's parcel-boundary
  ring/hole/multipart geometry and local-boundary cleanup depend on it too. Neither package is part of Phase
  0."
- **Trace:** PH3-2 #29 (`parcel-boundary-sources.md`, "`EsriJsonPolygonReader` -- rings, holes, multipart,
  orientation"); PH3-3 #30 (`revit-property-line-and-shared-coordinates.md`, "Geometry cleanup contract (Core,
  Revit-free)").
- **Rationale:** The "must avoid adding it if... Phase 1 scope narrows" clause is a dead Phase-1-era
  conditional that has not applied for some time; Phase 3 now depends on NetTopologySuite for real, so the
  sentence should state why, not carry a hedge about a decision already settled two phases ago.
- **Disposition:** Accepted as written, 2026-09-27.

### A10. Secrets, downloads, and logs — key paragraph generalization

- **Target section:** "Secrets, downloads, and logs", first paragraph
- **Old text:** "The OpenTopography key must come from `OPENTOPOGRAPHY_API_KEY` in the process environment or
  .NET user secrets. `.env` files are local conveniences only and are ignored. The agent must never print,
  persist, commit, embed in a fixture, place in a URL shown in logs, or include the key in an exception
  message. The agent must redact request query strings before logging."
- **Proposed text:** "Every API key — `OPENTOPOGRAPHY_API_KEY`, and Phase 3's `GEOCODIO_API_KEY` and
  `ARCGIS_API_KEY` — is resolved through the shared `SolidGround.Core.Http.ApiKeyResolver`, which checks the
  process environment first. Only `OPENTOPOGRAPHY_API_KEY`, and only for `SolidGround.Cli`, additionally falls
  back to .NET user secrets when the environment variable is absent; `SolidGround.Revit` stays environment-only
  for it. `GEOCODIO_API_KEY` and `ARCGIS_API_KEY` are environment-only on every host, `SolidGround.Cli`
  included, with no user-secrets fallback anywhere yet. `.env` files are local conveniences only and are
  ignored. The agent must never print, persist, commit, embed in a fixture, place in a URL shown in logs, or
  include a key in an exception message. The agent must redact every request query string before logging,
  through the shared `SensitiveQueryRedactor`, never only in one subsystem."
- **Trace:** PH3-0 #27 (`shared-http-redaction-and-key-resolution.md`, "The five new types", "Per-host
  resolution order -- before and after (equal)"); PH3-1 #28 (`address-geocoding.md`, "Redaction and
  key-resolution reuse (AC3)").
- **Rationale:** The rule's substance (environment variable then user secrets, never logged, redact query
  strings) is unchanged and must stay exactly as strict, but the OpenTopography-only framing is now
  incomplete: two more keyed providers resolve through the same shared component.
- **Disposition:** Accepted as written, 2026-09-27.

### A11. Secrets, downloads, and logs — downloaded rasters and fixtures paragraph

- **Target section:** "Secrets, downloads, and logs", second paragraph
- **Old text:** "Downloaded rasters and generated bulk data are ignored by default. Small deterministic
  fixtures may be committed only after inspection proves that they contain no key, authorization header,
  signed URL, or personal token."
- **Proposed text:** "Downloaded rasters, generated bulk data, a machine-local county parcel registry file, and
  a user-supplied local parcel file are all ignored by default and must never be committed. Small deterministic
  fixtures derived from any of them may be committed only after inspection proves that they contain no key,
  authorization header, signed URL, personal token, or real county, parcel, or address identifier."
- **Trace:** PH3-2 #29 (`parcel-boundary-sources.md`, "How a user adds their own county", "Fixtures").
- **Rationale:** The county parcel registry file and a user's local parcel file are new classes of local,
  never-committed data this paragraph doesn't name yet, even though the existing "clearly labeled synthetic
  data" rule in the next paragraph already covers fixtures derived from them.
- **Disposition:** Accepted as written, 2026-09-27.

### A12. Test fixture and verification — example-site paragraph clarification

- **Target section:** "Test fixture and verification", first paragraph
- **Old text:** (relevant clause) "No street address, lot, plat, ZIP, or place name is ever recorded for it in
  this repository."
- **Proposed text:** append one sentence immediately after it: "No street address, lot, plat, ZIP, or place
  name is ever recorded for it in this repository. A clearly labeled, fabricated address, county, or parcel
  identifier — one that never resolves to or names a real place — may be used in a geocoding or
  parcel-boundary fixture without violating this rule, as long as it plainly states that it is synthetic."
- **Trace:** PH3-1 #28 (`address-geocoding.md`, "Fixture plan and guard constraints"); PH3-2 #29
  (`parcel-boundary-sources.md`, "Fixtures").
- **Rationale:** Testing address geocoding and parcel lookup needs some address-shaped and county-shaped
  input; without this clarification a reader could misread the existing sentence as barring any address-shaped
  test data at all, including clearly labeled synthetic data, which is not what Issues #28 and #29 actually
  needed or did.
- **Disposition:** Accepted as written, 2026-09-27.

### A13. Authoritative references — optional additions

- **Target section:** "Authoritative references" (list)
- **Old text:** (anchor — the last two existing bullets before the closing sentence)

  ```
  - [OpenTopography OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json)
  - [Groundit architecture reference](https://github.com/lewismconte/groundit)
  ```

- **Proposed text:** insert three bullets between the two shown above:

  ```
  - [OpenTopography OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json)
  - [Census Geocoder API documentation](https://geocoding.geo.census.gov/geocoder/Geocoding_Services_API.html)
  - [Regrid Data Store License](https://app.regrid.com/store/license)
  - [Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/)
  - [Groundit architecture reference](https://github.com/lewismconte/groundit)
  ```

- **Trace:** PH3-1 #28 (`address-geocoding.md`, "Citations"); PH3-8 #35
  (`source-licensing-and-attribution.md`, "Citations").
- **Rationale:** Minor, optional. These three sources are now load-bearing for citations in Phase 3
  design-note and pull-request work, matching this section's own "prefer these primary sources" framing;
  every citation already lives correctly in the Phase 3 design notes regardless of whether this list is
  extended.
- **Disposition:** Accepted as written, 2026-09-27.

## Proposed changes to the Revit add-in conventions note

Listed in file order (top to bottom). All IDs below target
`docs/architecture/revit-add-in-conventions.md`.

### C1. Section 1, "Project layout and naming" — stale "only Revit call site" sentence

- **Target section:** Section 1, "Project layout and naming", the "No Revit abstraction-interface or Fakes
  layer" bullet (fifth of six in that section's "Adopted for `SolidGround.Revit`" list)
- **Old text:** "- No Revit abstraction-interface or Fakes layer for the initial milestone.
  `CreateToposolidCommand` is the only Revit call site, so there is no seam to protect yet; the equivalent
  precedent from the owner's other Revit add-in is revisited once a second call site appears."
- **Proposed text:** "- No Revit abstraction-interface or Fakes layer for the initial milestone. Issue #31
  (PH3-4) added three more Revit call sites — `Dialog/SolidGroundDialogHost.cs`, `Dialog/SolidGroundDialog.cs`,
  and `Dialog/DialogTheme.cs` — alongside `CreateToposolidCommand`, so "the only Revit call site" is no longer
  true. The adopted answer is a source-text test suite (`RevitInteractiveDialogTests`) that
  reads these files as text and asserts against them without instantiating or mocking any Revit type, so no
  Revit abstraction-interface or Fakes layer was added for this. The equivalent precedent from the owner's
  other Revit add-in stays revisited only if that testing strategy stops working."
- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Tests" — the Stage B/C/D entries; "Flow/state model"
  for why `SolidGroundDialogViewModel` itself stays Revit-free).
- **Rationale:** The sentence's own stated trigger condition ("revisited once a second call site appears") has
  fired — three new files call the Revit API directly — but the note never revisits it; the real, working
  answer already adopted (text-scrape tests, no Revit instantiation) belongs here since this section's own job
  is to record the Revit-free-testing strategy. Unlike this proposal's other changes, no named Phase 3 issue
  explicitly decided against a Revit abstraction-interface or Fakes layer — Issue #31's own design note and
  comments record only the adopted test suite, never a stated rejection of an abstraction layer — so the
  owner's disposition on this change is a real design decision, not merely a wording sign-off.
- **Disposition:** Accepted as written, 2026-09-27.

### C2. Section 2, "Target framework and reference assemblies" — stale "No `UseWPF`" sentence

- **Target section:** Section 2, "Target framework and reference assemblies (build-time mechanism)", "Adopted
  for `SolidGround.Revit`" bullets
- **Old text:** "- No `UseWPF` until a WPF surface is designed; the initial milestone uses a plain `TaskDialog`
  (see section 6)."
- **Proposed text:** "- `UseWPF` is on, since Issue #31 (PH3-4), for the interactive dialog;
  `<UseWPF>true</UseWPF>` replaced the project's prior explicit `Microsoft.WindowsDesktop.App.WPF`
  `FrameworkReference`. See "Package: CommunityToolkit.Mvvm 8.4.2" in
  `docs/architecture/revit-interactive-dialog.md` and section 6 below."
- **Trace:** PH3-4 #31 Stage B (`revit-interactive-dialog.md`, "Package: CommunityToolkit.Mvvm 8.4.2").
- **Rationale:** The WPF surface this sentence said didn't exist yet now ships; the gate it described has
  fired.
- **Disposition:** Accepted as written, 2026-09-27.

### C3. Section 2 — `CommunityToolkit.Mvvm` direct-dependency clause

- **Target section:** Section 2, "Target framework and reference assemblies", `CopyLocalLockFileAssemblies`
  bullet
- **Old text:** "- `CopyLocalLockFileAssemblies=true` from the project's creation, since `SolidGround.Revit`
  takes a `ProjectReference` to `SolidGround.Core`, which already carries `PackageReference` entries
  (NetTopologySuite, ProjNET) that must be copied into `SolidGround.Revit`'s own output for the isolated
  add-in context to load them at runtime."
- **Proposed text:** append one sentence: "...for the isolated add-in context to load them at runtime. The
  same setting also copies `CommunityToolkit.Mvvm.dll`, `SolidGround.Revit`'s own direct `PackageReference`
  since Issue #31 (PH3-4), not merely a transitive one from Core."
- **Trace:** PH3-4 #31 Stage B (`revit-interactive-dialog.md`, "Package: CommunityToolkit.Mvvm 8.4.2").
- **Rationale:** The bullet's own rationale is specific to Core's transitive packages and is still true, but
  silent on the new package's direct reference; nothing stated is wrong, only incomplete.
- **Disposition:** Accepted as written, 2026-09-27.

### C4. Section 3, "Manifest and isolated add-in context" — optional cross-reference

- **Target section:** Section 3, "Manifest and isolated add-in context", "Adopted for `SolidGround.Revit`"
  bullets (new bullet, appended last)
- **Old text:** None — new passage.
- **Proposed text:** "- `CommunityToolkit.Mvvm` 8.4.2 (Issue #31, PH3-4) was verified before adoption to carry
  no `.baml`, pack-URI-addressable, or other manifest-declarable resource, so it cannot trigger the
  isolated-context XAML/BAML double-load bug independent of the dialog's own zero-`.xaml` mitigation; it
  needed no `PublicAssemblies` or `Dependencies` manifest entry."
- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Package: CommunityToolkit.Mvvm 8.4.2").
- **Rationale:** Optional. Documents why a new direct dependency needed no manifest change; this section is
  still accurate without it, only silent on a question a future reader might otherwise have to re-derive.
- **Disposition:** Accepted as written, 2026-09-27.

### C5. Section 4, "Ribbon and command structure" — dialog-ordering clarification (AC3)

- **Target section:** Section 4, "Ribbon and command structure", "Adopted for `SolidGround.Revit`" bullets,
  the `IExternalCommandAvailability`/Preflight bullet
- **Old text:** "- No `IExternalCommandAvailability`; the button stays enabled.
  `CreateToposolidCommand.Execute` runs a read-only Preflight step (AOI present, budget configured, API key
  resolvable) before opening a transaction, and returns `Result.Cancelled` on a Preflight rejection, pending
  verification item 2; verified-with-caveat by Issue #13's 2026-09-20 pass (the `Result`/`IExternalCommand`
  shape and the general Failed/Cancelled-reverses-changes semantics are confirmed; the specific
  zero-transaction Undo-stack side effect remains a runtime-only question deferred to manual test step 5), see
  `revit-2027-verification-and-host-design.md`, item 2."
- **Proposed text:** append one sentence: "...see `revit-2027-verification-and-host-design.md`, item 2. Issue
  #31 (PH3-4) added one modal dialog (`SolidGroundDialogHost.ShowModal`) inside this same `Execute`, before
  this Preflight step and before any transaction; cancelling it, or an in-dialog lookup failure, maps to
  `Result.Cancelled` with no `TaskDialog` shown; it is not a second command and does not change the ribbon."
- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Result-code mapping"; "Purpose and boundary": "No new
  command, ribbon button, or panel (PH3-7 unchanged)").
- **Rationale:** This is the AC3 item named in Issue #34's own scope: the sentence is still true as far as it
  goes, but now incomplete about the dialog's place in the command's flow. See "Push button versus a second
  command" below for the full recommendation.
- **Disposition:** Accepted as written, 2026-09-27.

### C6. Section 5, "Settings" — stale settings-file-hypothetical sentence

- **Target section:** Section 5, "Settings", last "Adopted for `SolidGround.Revit`" bullet
- **Old text:** "- One read pattern only, not the two coexisting cached-versus-fresh patterns found in the
  owner's other Revit add-in (that project's own study flags this as an unintentional inconsistency, not a
  considered convention). Point budget, output unit, and buffer distance stay Core-level configuration under
  `AGENTS.md`'s Data and numeric contracts section for the initial milestone; a settings file is added only
  when a concrete need for persisted Revit-side state appears."
- **Proposed text:** "- One read pattern only, not the two coexisting cached-versus-fresh patterns found in the
  owner's other Revit add-in (that project's own study flags this as an unintentional inconsistency, not a
  considered convention); Issue #31 (PH3-4)'s dialog reads this same settings file once, to prefill its own
  fields, and never writes back to it. The settings file (`RevitSettings`, since Issue #15) now has four
  top-level sections: `request` (point budget, output unit, buffer distance, and the rest of
  `TerrainRequestSettings`), `target` (`level`/`toposolidType`, Issue #15), `sharedCoordinates`
  (`writeIfAbsent`, default `false`, Issue #30/PH3-3), and `addressAndParcel` (`geocoderProvider`,
  `countyRegistryPath`, `countyGeoidOverride`, `localParcelFilePath`, `localParcelFileSourceLabel`,
  `localParcelFileLicenseDisclaimerText`, `nearbySearchRadiusMeters`, Issue #31/PH3-4)."
- **Trace:** PH3-3 #30 (`revit-property-line-and-shared-coordinates.md`, "Settings: the shared-coordinates
  opt-in"); PH3-4 #31 (`revit-interactive-dialog.md`, "Settings interaction: prefill, not override" and
  "Nearby-parcel fallback tier (follow-up)").
- **Rationale:** This sentence still reads as though no Revit-side settings file exists yet, but a real one
  has existed since Issue #15 and gained two more real sections in Phase 3 that this bullet never named,
  unlike several other sections in this same note that carry an explicit "Update, Issue #NN" annotation for a
  change of this kind.
- **Disposition:** Accepted as written, 2026-09-27, then corrected on application. The accepted text named
  the C# `RevitSettings` record's four parts (`request`, `target`, ...) as if they were the JSON file's own
  top-level sections; the settings file actually keeps the terrain-request keys and `level`/`toposolidType` at
  its top level, with only `sharedCoordinates` and `addressAndParcel` as nested objects
  (`RevitSettingsIo.TryLoad`). The applied sentence names the same four groups accurately; the rest of C6 is
  applied as written, and the owner was told of the correction.

### C7. Section 6, "Logging and diagnostics" — stale "no custom WPF dialog shell" sentence

- **Target section:** Section 6, "Logging and diagnostics", "Adopted for `SolidGround.Revit`" bullets, the
  user-facing-failures bullet
- **Old text:** "- User-facing failures: one native `TaskDialog` per failure, full-sentence body, no custom
  WPF dialog shell for the initial milestone."
- **Proposed text:** "- Terminal, outside-dialog failures: one native `TaskDialog` per failure, full-sentence
  body — for example, the dialog-merged settings validation failure, which reuses the same `ShowProblemList`
  `TaskDialog` path unchanged. Issue #31 (PH3-4) added one custom, code-only WPF dialog shell
  (`SolidGroundDialog`) for address/parcel confirmation; an in-dialog geocoder or parcel-source lookup failure
  is shown inline inside that shell and never reaches a `TaskDialog` at all, so this bullet's original claim
  now holds only for failures outside the dialog, not universally."
- **Trace:** PH3-4 #31 (`revit-interactive-dialog.md`, "Result-code mapping": "In-dialog geocoder/parcel
  lookup failure | *(never reaches `Result` at all -- caught inline, dialog stays open)*").
- **Rationale:** This is a flat factual claim now contradicted by a shipped, named class (`SolidGroundDialog`
  is exactly a "custom WPF dialog shell"); the narrower, still-true half — terminal/outside-dialog failures
  still surface through one native `TaskDialog` — needs restating, not just a clause.
- **Disposition:** Accepted as written, 2026-09-27.

## Push button versus a second command

**Question (Issue #34's AC3):** should Issue #31's address/parcel confirmation dialog have been a second
`IExternalCommand`/ribbon button, separate from `CreateToposolidCommand`, instead of living inside it?

**Recommendation: keep the single `CreateToposolidCommand` push button.** The dialog
(`SolidGroundDialogHost.ShowModal`) opens modally inside `Execute`, before Preflight and before any
transaction. No new `IExternalCommand`, ribbon button, or panel.

**Rationale:**

1. This is the already-shipped design, not a hypothetical choice being made now. `revit-interactive-dialog.md`'s
   "Purpose and boundary" states plainly: "No new command, ribbon button, or panel (PH3-7 unchanged)."
2. Confirmed directly against the shipped source: `CreateToposolidCommand.ExecuteCore` calls
   `LoadDocumentAndSettings` → `SolidGroundDialogHost.ShowModal` → `RunDocumentPreflight`, all inside the one
   existing `Execute` method. No second `IExternalCommand` exists anywhere under
   `src/SolidGround.Revit/Commands/`.
3. A second command or button would need its own Preflight, its own transaction boundary, and its own way to
   hand confirmed AOI/level/toposolid-type state to the first command — real plumbing this design avoids
   entirely by staying inside one `Execute`.
4. Confirmed live: the 2026-09-27 Revit 2027 evidence session exercised "modal ownership" and confirmed a
   second run in the same session opens fresh, with no leaked state between runs
   (`revit-interactive-dialog.md`, "Manual evidence (Revit 2027, 2026-09-27)").

**The one-sentence conventions clarification this needs** (applied by `A6` to `AGENTS.md` and by `C5` to the
conventions note, each in that file's own voice):

> The dialog (Issue #31, PH3-4) opens modally inside `Execute`, owned by Revit's main window, before Preflight
> and any transaction; it is not a second command and does not change the ribbon.

**Decision 8's no-`AreaOfInterestKind`-change point:** `docs/architecture/phase-3-interactive-add-in-research.md`'s
"Owner decisions (2026-09-21)" section, decision 8, states: "An address/parcel choice resolves into today's
parcel GeoJSON/WKT AOI shape; no new `AreaOfInterestKind`. Basis: the existing shape already fits a parcel
polygon." Because the dialog resolves into the existing `areaOfInterest.parcel` GeoJSON/WKT shape, it needs no
new `AreaOfInterestKind` member and no related conventions-note passage at all —
`docs/architecture/revit-add-in-conventions.md` never mentions `AreaOfInterestKind` in the first place, so
this is a gap closed by absence, not a change to propose.

**Caution — decision-number collision:** `docs/architecture/revit-add-in-conventions.md`'s own "Owner
decisions, 2026-09-20" list and `docs/architecture/phase-3-interactive-add-in-research.md`'s "Owner decisions
(2026-09-21)" list are two independent numbered lists from two different dates. Each list's own item 8 is an
unrelated decision (the conventions note's decision 8 is the ribbon-tab-placement choice; the research note's
decision 8 is the AOI-plumbing choice quoted above) — the same kind of collision
`docs/architecture/address-parcel-provenance.md` already had to call out once for item 7 ("not the
same-numbered decision 7 in `docs/architecture/revit-add-in-conventions.md`, which is unrelated"). Any reader
applying this proposal must cite a decision by document name and date, never by a bare number, to avoid
repeating that collision.

## Passages reviewed and left unchanged

### AGENTS.md

| Section / passage | Why left unchanged |
| --- | --- |
| Architecture table — CLI row, Tests row | Generic wording ("Thin development and batch interface over Core"; "xUnit tests against Core using committed offline fixtures") already covers the new `geocode`/`parcel` verbs (PH3-5 #32) and their offline fixtures with no wording change needed. |
| Architecture — pluggable-source / Groundit paragraphs | Unaffected; "a future classified point-cloud source can coexist with the initial gridded DEM source" refers specifically to `IElevationSource` and remains true as written. Phase 3 added sibling pluggable interfaces for geocoding/parcels following the same pattern, but did not change this sentence's claim. |
| Revit add-in conventions (AGENTS.md section) — intro narrative paragraph | `A1` already appends the full Phase 3 status narrative to "Mission and current boundary." Duplicating it in this section's own intro paragraph (which narrates Issues #12, #13, #15, #16, #19 specifically as conventions-adherence) would repeat the same facts twice for no added clarity; recommend no separate append here. |
| Revit add-in conventions (AGENTS.md section) — Deployment and signing bullet | `CommunityToolkit.Mvvm.dll` is a real shipped runtime dependency, but no Phase 3 issue decided whether or how `Sign-RevitAddIn.ps1` should sign a bundled third-party DLL, and the bullet's existing text does not claim otherwise. **Open question for the owner, not proposed as a change:** nothing traces to a decision yet, per this issue's own AC2 discipline. |
| Provenance decision (entire section) | Deliberately deferred, not changed. Both `address-parcel-provenance.md` ("This issue does not change: `ExtensibleStorageProvenanceSchema` (still 36 fields, version 1)") and `source-licensing-and-attribution.md`'s "Extensible Storage: deliberately unchanged" state this explicitly. A future schema-version issue will need a real addition to this section's minimum-field list then, not now. |
| Data and numeric contracts — OpenTopography/AAIGrid, GeoKeys, unit-definition, local-origin paragraphs | Core-side elevation-pipeline rules Phase 3 does not touch (`AaiGridParser`, the GeoKeys fallback, `LengthConverter` are all unmodified). The new shared-coordinates write explicitly reuses the existing centralized `RevitUnitConversion.ToInternal` path rather than inventing a parallel one (`revit-property-line-and-shared-coordinates.md`, "Unit convention for the shared-coordinates value") — affirmative evidence the existing rule was followed, not evidence it needs new text. |
| Data and numeric contracts — AOI paragraph | "WGS 84 bounding box, latitude/longitude plus radius, and parcel polygon supplied as GeoJSON or WKT" remains literally true: decision 8 in both `phase-3-interactive-add-in-research.md` and PH3-5 #32's own scope state address/parcel resolution "resolves into today's parcel GeoJSON/WKT AOI shape; no new `AreaOfInterestKind`." |
| Data and numeric contracts — point budget/simplifier paragraph | Unaffected in substance; `A5` above adds the one clause this paragraph's own dialog-sharing update needs, in "Revit 2027 rules" rather than here. |
| Dependency policy — native-binary paragraph, THIRD-PARTY-NOTICES paragraph, Nice3point paragraph | All confirmed consistent with Phase 3: `CommunityToolkit.Mvvm` was verified to add no native code before adoption; `THIRD-PARTY-NOTICES` already carries its entry, self-checked by `Assert-ThirdPartyNoticesCoversLockedPackages`; the Nice3point paragraph is untouched by any Phase 3 commit. |
| Secrets — no-personal-information paragraph | Already the rule PH3-2 #29's three-layer owner-field defense and the synthetic-fixture convention used throughout Phase 3 were built to satisfy; a correct application of the existing text, not evidence it is wrong. |
| Accuracy and product claims (entire section) | Already the rule PH3-1 #28 and PH3-2 #29 built their own "not a survey" disclaimers to match (`parcel-boundary-sources.md`, "Not a survey": "Consistent with AGENTS.md's accuracy rule..."). |
| Test fixture and verification — observable-behavior paragraph | Generic wording ("every observable Core behavior") already covers address matching, parcel polygon parsing, and owner-field stripping without needing named examples. |
| Build and CI (entire section) | Verified directly against `.github/workflows/*.yml` and `SolidGround.Revit.csproj`: the nine numbered conditions, the never-list, and condition 9's concrete implementation are all untouched by any Phase 3 commit. No pull-request trigger was added; no new action was allow-listed; `EnableWindowsTargeting`/the Nice3point CI-only gate needed no change for `UseWPF`/`CommunityToolkit.Mvvm` (confirmed by passing CI runs cited in Issue #31's own closing comment). |

### The conventions note

| Section / passage | Why left unchanged |
| --- | --- |
| "Owner decisions, 2026-09-20" list (top of file) | Kept as the historical record of that one 2026-09-20 conversation. Phase 3's own owner decisions are recorded inline, in each numbered section, as "Update, Issue #NN" notes — the same precedent sections 3, 7, 9, 11, and 12 already use — not as new numbered entries appended to this list; `C1` through `C7` above all follow that existing inline-update pattern rather than proposing new numbered entries here. See "Push button versus a second command" above for the decision-number-collision caution this list's own item 8 requires. |
| Section 2, `packages.lock.json` sentence | Now fulfilled, not stale: its own trigger condition ("once it has any `PackageReference`") already fired — `SolidGround.Revit` now has a `packages.lock.json` — and nothing in the sentence is wrong. |
| Section 4, tab/panel/button/icon sentence (line 67) | Unaffected: `revit-interactive-dialog.md`'s "Purpose and boundary" section states "No new command, ribbon button, or panel (PH3-7 unchanged)," confirming Phase 3 added no new ribbon tab, panel, or button, so line 67's own tab/panel/`PushButtonData`/icon description stays accurate as written. |
| Section 4, tooltip sentence (line 68) | The rule (full-sentence tooltip) is still met by the shipped `ButtonToolTip` constant. The tooltip's own wording still describing only the pre-Phase-3, settings-file-only flow is a product-text drift, not a conventions-rule break — flagged for a future issue, not proposed here, since conventions.md's own rule text does not need to name every current tooltip word for word. |
| Section 4, Transaction/Regeneration/catch-filter sentences (lines 70-71) | Unaffected; `PropertyLine.Create` lands in the same transaction as the toposolid per owner decision 5, so no new transaction or attribute rule is needed. |
| Section 5, settings-location and mutex/digest/atomic-write sentences (lines 79, 82) | Unaffected. Issue #31's dialog explicitly "reads only; no write-back," reusing the existing read path rather than adding a second one, so these sentences stay correct even though the settings-file-hypothetical sentence beside them (`C6`) needed a rewrite. |
| Section 7, "Deployment and per-user install" | The existing "enumerates the full dependency closure and fails closed if any file is missing" clause already covers `CommunityToolkit.Mvvm.dll` with no wording change needed. |
| Section 8, "Debugging" | Unaffected: no pyRevit/IronPython/MCP/RevitLookup/Add-In Manager dependency was added. The dialog's live evidence used Windows UI Automation state only (`revit-interactive-dialog.md`, "Known limitations"), which is evidence-gathering, not a new build/runtime tool dependency. |
| Section 9, "Packaging, versioning, and release"; Section 12, "Code signing and release packaging" | Unaffected: `Directory.Build.props` still declares `<Version>0.1.0</Version>`; no Phase 3 issue cut a release or changed a signing/packaging script. |
| Section 10, "Revit reference assemblies for local builds and CI" | Unaffected: `CommunityToolkit.Mvvm` is a real, unconditional runtime dependency, never scoped to the `UseRevitReferenceAssemblies` CI-only condition; no version-pin change is reported or needed. |
| Section 11, "Provenance and Extensible Storage" | Not yet touched. `address-parcel-provenance.md` states explicitly that the Extensible Storage schema is unchanged by Phase 3; a future schema version's field list is documented only, not implemented — flag for that later issue, not this one. |
| "Items that need Revit 2027 verification," item 6 (WPF-in-Revit threading / modeless `ExternalEvent` rule) | Partially exercised, not resolved: the 2026-09-27 live session exercised only the modal WPF half successfully; the modeless/`ExternalEvent` half this item names remains untouched and still fully open. Worth a one-clause progress note if the owner wants it, but nothing currently stated is false, so nothing is proposed here. |
| "What this note does not do" (close) | Historical Issue #13/#14 scope statement, unaffected. |

## Disposition record

The owner recorded these dispositions on 2026-09-27, answering four questions: `A1` "Accept, but condense";
`A2` through `A13` "Accept all"; `C1` through `C7` "Accept all" (which includes keeping the single push button,
see "Push button versus a second command" above); and "Apply after #34 closes" for applying the accepted
wording to `AGENTS.md` and the conventions note in a separate commit once this issue closes.

| ID | Summary | Disposition | Date |
| --- | --- | --- | --- |
| A1 | Append a Phase 3 status paragraph to "Mission and current boundary" narrating Issues #27-#33 and #35 | Accepted with edit (condensed) | 2026-09-27 |
| A2 | Update the Architecture table's Core row to name Phase 3's new capabilities | Accepted | 2026-09-27 |
| A3 | Update the Architecture table's Revit row to drop "in Phase 2" and name the dialog/PropertyLine/shared-coordinates write | Accepted | 2026-09-27 |
| A4 | Add a new "Revit 2027 rules" paragraph recording the `PropertyLine`/`ProjectLocation`/`BasePoint` API surface | Accepted | 2026-09-27 |
| A5 | Add one sentence to the point-budget paragraph noting the dialog now shares the same threshold check | Accepted | 2026-09-27 |
| A6 | Add one sentence to the AGENTS.md "Ribbon and command" bullet stating the dialog is not a second command | Accepted | 2026-09-27 |
| A7 | Add a new "Data and numeric contracts" paragraph recording the seven-vendor never-default policy | Accepted | 2026-09-27 |
| A8 | Add a new "Dependency policy" paragraph naming `CommunityToolkit.Mvvm` 8.4.2 | Accepted | 2026-09-27 |
| A9 | Replace the dead Phase-1-only hedge in the ProjNet/NetTopologySuite paragraph with Phase 3's real justification | Accepted | 2026-09-27 |
| A10 | Generalize the OpenTopography-only key paragraph to name `GEOCODIO_API_KEY`/`ARCGIS_API_KEY` and the shared resolver | Accepted | 2026-09-27 |
| A11 | Extend the downloaded-rasters/fixtures paragraph to name the county registry file and local parcel file | Accepted | 2026-09-27 |
| A12 | Add one sentence to the example-site paragraph permitting clearly labeled synthetic address/county fixtures | Accepted | 2026-09-27 |
| A13 | Add three optional citations (Census, Regrid License, Nominatim policy) to "Authoritative references" | Accepted | 2026-09-27 |
| C1 | Rewrite Section 1's stale "only Revit call site" sentence and name the adopted testing strategy | Accepted | 2026-09-27 |
| C2 | Rewrite Section 2's stale "No `UseWPF`" sentence | Accepted | 2026-09-27 |
| C3 | Add one sentence to Section 2's `CopyLocalLockFileAssemblies` bullet naming `CommunityToolkit.Mvvm`'s direct reference | Accepted | 2026-09-27 |
| C4 | Add one optional bullet to Section 3 cross-referencing `CommunityToolkit.Mvvm`'s manifest-safety verification | Accepted | 2026-09-27 |
| C5 | Add one sentence to Section 4's Preflight bullet describing the dialog's place before Preflight (AC3) | Accepted | 2026-09-27 |
| C6 | Rewrite Section 5's stale settings-file-hypothetical sentence to name the real four settings sections | Accepted; corrected on application (see C6) | 2026-09-27 |
| C7 | Rewrite Section 6's stale "no custom WPF dialog shell" sentence | Accepted | 2026-09-27 |
