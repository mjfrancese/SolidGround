# Phase 3 issues as created on 2026-09-21

## Phase 3 draft issues — address-to-parcel interactive workflow

Filed on GitHub 2026-09-21: milestone "Phase 3: Interactive add-in" (#3), the epic issue, and PH3-0 through PH3-8. Numbering (`PH3-0`..`PH3-8`) stays a working label only; the table below maps each label to its real issue number and title.

| Label | Issue | Title |
| --- | --- | --- |
| Epic | #26 | [Epic] Phase 3 — Turn an address into a bounded parcel with imported property lines |
| PH3-0 | #27 | Extract a shared HTTP redaction and key-resolution helper in Core |
| PH3-1 | #28 | Add a pluggable address geocoder with three implementations |
| PH3-2 | #29 | Add two parcel-boundary sources with legal-description resolution and a stripped fixture |
| PH3-3 | #30 | Create the PropertyLine and gate an opt-in shared-coordinates write |
| PH3-4 | #31 | Build the WPF dialog, activate UseWPF, and adopt CommunityToolkit.Mvvm |
| PH3-5 | #32 | Add CLI address and parcel commands with offline fixtures |
| PH3-6 | #33 | Extend provenance to TerrainProvenance v3 |
| PH3-7 | #34 | Propose AGENTS.md and conventions amendments for Phase 3 (proposal only) |
| PH3-8 | #35 | Resolve licensing and attribution obligations for geocoding and parcel sources |

Format follows Issues #15 and #16. This revision applies the owner's decisions below — decisions, not options — to the prior draft: every item they settle is now fact in the affected issue, and each issue keeps an "Owner decisions depended on" heading only where a genuinely open item remains. Content is drawn from `docs/architecture/phase-3-interactive-add-in-research.md` (the committed research note) and the vendor-licensing pass behind decision 3; this file's Sources section cites only public URLs with retrieval dates, never a local evidence-file path.

Per decision 13, the "Proposed AGENTS.md amendment" section below was a proposal when these issues were filed; the owner accepted it as written on 2026-09-21 and it was applied to `AGENTS.md` in the same commit as this paragraph. PH3-7 (#34) now covers only the conventions-note amendments. Phase 3 implementation still needs its own explicit task regardless; filing these issues is not that task selection — every issue above stays `status: blocked` until the owner explicitly selects it.

## Owner decisions (2026-09-21, final)

1. **Geocoder default.** Census Bureau Geocoder, keyless, default. Geocodio and Esri `forStorage=true` are keyed opt-ins (`GEOCODIO_API_KEY`, `ARCGIS_API_KEY`), off unless set.
2. **Parcels.** A pluggable `IParcelBoundarySource` with a per-county registry keyed by Census GEOID; the area County's `AGS_Parcels` is the first entry. Never the county's Esri geocode proxy; never auto-wire Nominatim.
3. **Commercial parcel slot.** Not a keyed API: a vendor-neutral local parcel-file source reads a user-purchased county file from disk, GeoJSON first. The Regrid Data Store the area County Standard Schema purchase ($300 one-time, one-year term, internal-tools use) is the reference purchase; Regrid's live API, ATTOM, LightBox, Cotality, Living Atlas, and ReportAll all fail on self-serve storage, derivation, or price terms (PH3-2/PH3-8). Models/exports stay internal, but third-party terms are still recorded.
4. **Regrid trial.** A trial token authenticated but is fixed to seven non-the area counties, so it cannot exercise the ExampleSite fixture; a trial-county query confirmed the Standard schema's field shape (PH3-2).
5. **Boundary in Revit.** `PropertyLine.Create(Document, IList<CurveLoop>)` in the same transaction as the toposolid.
6. **Shared coordinates.** An explicit dialog option, default off; Preflight refuses to write when the model already has shared coordinates set.
7. **Provenance.** `TerrainProvenance` moves to schema v3 with an optional address/parcel record; Issue #16's Extensible Storage entity gains matching fields in its own later schema version.
8. **AOI plumbing.** Resolves into today's parcel GeoJSON/WKT AOI shape; no new `AreaOfInterestKind`.
9. **Dialog.** The full content model ships in the first milestone.
10. **Fixture.** A stripped the reference parcel/[withheld] parcel from the county's open layer may be committed, carrying the county's no-warranty notice verbatim, after a fixture-security test passes.
11. **MVVM.** Adopt `CommunityToolkit.Mvvm` 8.4.2 now; the dialog stays pure code-behind, no XAML/BAML; `UseWPF` turns on there.
12. **Redaction.** Extract a shared, provider-agnostic HTTP redaction/key-resolution helper into Core as its own issue (PH3-0), before the geocoder issue.
13. **Process.** The research note is documentation; this file is the record of the issues as created; Phase 3 implementation still needs its own explicit task; the AGENTS.md sentence below is proposed, never applied.

---

## Proposed AGENTS.md amendment — ACCEPTED 2026-09-21, APPLIED

Wording proposed for `AGENTS.md`'s "Mission and current boundary" paragraph, appended after its Phase 2 sentences. Accepted by the owner on 2026-09-21 without changes and applied to `AGENTS.md` in the same commit as this line; kept here verbatim as the record of what was proposed.

> Phase 3 adds an interactive Revit 2027 add-in workflow that turns a street address into geocoded coordinates, resolves a matching parcel boundary, and lets the operator confirm both — together with an optional shared-coordinates write and a native property line — before today's terrain pipeline runs, as researched in `docs/architecture/phase-3-interactive-add-in-research.md` (2026-09-21). The agent must not begin Phase 3 without an explicit implementation task.

---

## [Epic] Phase 3 — Turn an address into a bounded parcel with imported property lines

### Outcome

Let an operator enter a street address, choose among geocode and parcel candidates in a WPF dialog, and have SolidGround create the bounded Toposolid plus a native PropertyLine, with reversible provenance, without loosening AGENTS.md.

### Scope

- Track a shared HTTP redaction/key helper, geocoding, parcel-boundary lookup, Revit boundary representation, the WPF dialog, CLI parity, provenance extension, an AGENTS.md-amendment proposal, and licensing/attribution as separate child issues (PH3-0 through PH3-8).
- Every new HTTP-backed source follows the existing `IElevationSource`/OpenTopography pattern: pluggable interface, env-var key when needed, redaction through PH3-0's helper, offline-by-default tests.

### Acceptance criteria

- [ ] Every child issue closes with citations against current provider/API documentation, matching Phase 2's evidence standard.
- [ ] The reference parcel fixture resolves end-to-end: address -> geocoded point -> matched parcel -> Toposolid + PropertyLine created in Revit 2027, reproducible offline through the CLI.
- [ ] Every shipped source's license/attribution obligation is documented and, where required, shown in the dialog and stored in provenance, including the Regrid purchase's one-year term.
- [ ] No AGENTS.md text changes except through a separate owner-accepted amendment (PH3-7); the owner accepts the Revit 2027 end-to-end result for at least one real the area County address.

### Relationships

- Blocked by: none formally. #33 (PH3-6) only documents, not implements, Issue #16's future follow-up, so #16's status gates nothing here directly.
- Children: #27 (PH3-0), #28 (PH3-1), #29 (PH3-2), #30 (PH3-3), #31 (PH3-4), #32 (PH3-5), #33 (PH3-6), #34 (PH3-7), #35 (PH3-8).

### Owner decisions depended on (resolved 2026-09-21)

- Whether Phase 3 child issues may proceed alongside the still-open Phase 2 issues (#16, #17, #19) or must wait for their closure, mirroring #10's role for Phase 1. Resolved 2026-09-21: every Phase 3 issue is status: blocked until the AGENTS.md amendment is accepted; Phase 2 issues #16, #17, and #19 keep priority.

### Start condition

Owner selection required per child issue, per AGENTS.md's phase-gate convention.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-0 — [Phase 3] Extract a shared HTTP redaction and key-resolution helper in Core

### Outcome

Generalize the OpenTopography-only redaction pattern into one provider-agnostic Core component so every current and future HTTP source shares one implementation, before a second source is added (decision 12).

### Scope

- Generalize today's OpenTopography-specific redaction logic into a Core component that redacts a configurable set of sensitive query-parameter names from any URL or log line, not hardcoded to one provider's parameter name.
- A matching key-resolution helper reading a named environment variable first, then .NET user secrets, mirroring `OPENTOPOGRAPHY_API_KEY`'s lookup order but parameterized by variable name.
- Migrate existing OpenTopography call sites onto the shared helper with no behavior change; existing redaction/key tests pass unchanged against the generalized code.
- Add no new provider integration here; PH3-1, PH3-2, and PH3-5 are the first callers.

### Acceptance criteria

- [ ] One component redacts a configurable set of sensitive query parameters from a URL/log line, replacing the OpenTopography-only implementation, with existing tests passing unchanged; a test asserts redaction per known provider family (OpenTopography's parameter, a generic `api_key=` parameter, an Esri `token=` parameter).
- [ ] One component resolves a named key from an environment variable, then user secrets; a missing key for a provider that needs one is a caught, actionable error before any HTTP call.
- [ ] No key value appears in any log, trace, exception, or test fixture.
- [ ] `SolidGround.Tests` passes with no coverage reduction; the Core-never-references-Revit test passes.

### AGENTS.md rules to keep

- Keys come only from an environment variable or .NET user secret, are never hardcoded, logged, or embedded in a fixture or URL shown in logs.
- Query strings are redacted before logging "not only in a subsystem" — this issue makes that literally true across every current and future source.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: none. Blocks #28 (PH3-1), #29 (PH3-2), and #31 (PH3-4) directly, and #32 (PH3-5)/#35 (PH3-8) transitively (via PH3-1/PH3-2).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-1 — [Phase 3] Add a pluggable address geocoder with three implementations

### Outcome

Turn a street address into WGS84 coordinates through a pluggable Core abstraction: Census as the default, keyless implementation, with Geocodio and Esri's `forStorage=true` path shipped as keyed opt-ins, off unless their env var is set (decision 1).

### Scope

- New `IAddressGeocoder` interface in `SolidGround.Core`, sibling to `IElevationSource`.
- Three implementations on `HttpClient`/`System.Text.Json` and PH3-0's helper: `CensusGeocoder` (default, keyless); `GeocodioGeocoder` (`GEOCODIO_API_KEY`); Esri World Geocoding with `forStorage=true` (`ARCGIS_API_KEY`) — both names fixed here. A settings field selects the provider, defaulting to Census; a keyed provider without its key set fails loud, before any HTTP call.
- Never default to the county's Esri geocode proxy (no published terms; risks its paid credits) or public Nominatim (needs a deliberate, disclosed choice); document both, with citation, in the design note only.
- Surface every returned candidate, not just the first match, with its attribution string.

### Acceptance criteria

- [ ] `GeocodeAsync` returns ranked candidates with coordinates, matched address, and attribution for the reference parcel fixture, for Census; Geocodio and Esri each pass an equivalent contract test under `FakeHttpMessageHandler`.
- [ ] Selecting Geocodio or Esri without its key set produces a caught, actionable error before any HTTP call; Census needs no key end to end.
- [ ] All three redact query strings and resolve keys only through PH3-0's helper; none duplicates its own redaction logic.
- [ ] Default runs make no live network call; an opt-in live test needs a flag plus a key for keyed providers, and skips rather than fails without one.
- [ ] Zero-candidate and HTTP-failure both surface as caught, actionable errors for all three.

### AGENTS.md rules to keep

- Managed .NET dependencies only, no native code; standard-library HTTP client, no new package. Key handling and redaction go through PH3-0's helper; results described as approximate, not survey-grade. Offline-by-default, deterministic tests.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #27 (PH3-0).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-2 — [Phase 3] Add two parcel-boundary sources with legal-description resolution and a stripped fixture

### Outcome

Obtain a parcel's boundary and identifying attributes through a pluggable Core abstraction with two implementations: a per-county REST registry keyed by GEOID (the area County's `AGS_Parcels` as the first entry), and a vendor-neutral local parcel-file reader for a user-purchased export, with the Regrid Data Store purchase as the reference purchase (decisions 2-4, 10).

### Scope

- New `IParcelBoundarySource` interface in `SolidGround.Core`, mirroring `IElevationSource`.
- **County registry**: keyed by the Census Geocoder's `returntype=geographies` GEOID (the area County = `[withheld]`), supplied by the caller so this source stays independently testable of PH3-1, against a small registry (base URL, layer index, field map) with `AGS_Parcels` `MapServer/0` as the first entry, queryable by point-in-polygon and `PROP_ADD` substring. Surface `SUBDIVISION`, `LOTNUM`, `LOCATOR`, acreage, `LEGAL`; label `DEEDBKPG`/`ASRBKPG` as unconfirmed book/page proxies (no literal plat-book field exists here). An unregistered county is an actionable error, never silent-empty. Never the county's Esri geocode proxy or Nominatim (PH3-1).
- **Local parcel-file source** (the commercial slot, not a keyed API): reads a user-purchased county file from a configured disk path, no network call, GeoJSON-only unless a reader already in the dependency set (NetTopologySuite/ProjNet) parses GeoPackage/Shapefile without a new package. Field map targets Regrid's Standard schema (`parcelnumb`, `address`, `legaldesc`, `subdivision`, `zoning`, `ll_gisacre`, `ll_uuid` — all confirmed present in the 2026-09-21 Dallas County trial response; `lot`/`block`/`plat`/`book`/`page` are documented in Regrid's schema reference but were not returned for that parcel and must be treated as optional/nullable); owner/mailing fields and `enhanced_ownership` are read only to be dropped. Reference purchase: Regrid's Data Store, the area County Standard Schema, $300 one-time, distinct from Regrid's restrictive live-API terms (details in PH3-8).
- Label every returned boundary a cadastral/assessor tax-map representation, not a survey.

### Acceptance criteria

- [ ] `FindAsync` resolves the reference parcel/[withheld] parcel by point and by address against a committed offline fixture, area matching ~[withheld] sq ft within tolerance.
- [ ] A fixture-security test asserting the absence of `OWNER_NAME`, `OWN_ADD`, `OWN_CITY`, `OWN_STATE`, `OWN_ZIP`, `CAREOF`, and any other owner field passes before the fixture is committed; the fixture carries the county's no-warranty notice verbatim.
- [ ] The local-file source's tests use a synthetic, fabricated fixture shaped like Regrid's schema, never real purchased data, since the licence restricts use to internal tools.
- [ ] A documented registry format lets a second county be added without an interface change; no commercial vendor's live-API data is a fixture or reachable by default, and the local-file source never calls the network.
- [ ] Both implementations carry their source's license/disclaimer text for provenance; a live-endpoint test for the REST source is opt-in and skips, never fails, offline.

### AGENTS.md rules to keep

- Managed .NET dependencies only; plain REST/`HttpClient` and a local file reader, never the ArcGIS Maps SDK for .NET or a new geometry package without justification. Redaction/key resolution for the REST source go through PH3-0's helper.
- Fixtures inspected before commit, free of keys/tokens/owner PII; accuracy language stays "not a survey."

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #27 (PH3-0).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-3 — [Phase 3] Create the PropertyLine and gate an opt-in shared-coordinates write

### Outcome

Create a native `PropertyLine` alongside the Toposolid in the same transaction, and add a default-off shared-coordinates write gated by a Preflight refusal when the model already has shared coordinates set (decisions 5-6).

### Scope

- `PropertyLine.Create(document, profiles)` (new in Revit 2027; confirmed absent pre-2027) from `BoundaryGeometryBuilder.BuildProfiles`'s existing `IList<CurveLoop>`, in the same transaction as `Toposolid.Create`. `IsValidBoundary` runs before creation; a rejected boundary rolls back the whole transaction, no partial element.
- Defensive cleanup of externally sourced polygon geometry (dedupe vertices, drop collinear/sub-tolerance segments) before `CurveLoop`, unit-tested against a messy fixture.
- A new Preflight check reads whether shared coordinates are already set — the exact member verified/cited before merge, per AGENTS.md's API rule — and refuses (`Result.Cancelled`, no transaction) when the operator opted in this run and the model already has them.
- The opt-in is a dialog checkbox (PH3-4 hosts it; default off); this issue owns the refusal logic and the `ProjectLocation`/`BasePoint` write when on and none exist yet. Off, behavior is unchanged from Issue #15.

### Acceptance criteria

- [ ] A valid boundary creates both a `Toposolid` and a `PropertyLine` in one transaction, verified against installed Revit 2027 SDK signatures before merge; a rejected boundary rolls back the whole transaction with no partial element.
- [ ] Geometry cleanup is unit-tested against at least one intentionally messy fixture.
- [ ] Off, no `ProjectLocation`/`BasePoint` write occurs; on, Preflight refuses against existing shared coordinates and otherwise writes, verified post-creation.
- [ ] A Revit 2027 manual-evidence step confirms the `PropertyLine` survives save/reopen, behaves correctly on Undo, and that the refusal fires against a real document with existing shared coordinates.

### AGENTS.md rules to keep

- Verify every Revit API call, including the shared-coordinates detection member, against the Revit 2027 SDK before writing it, cited in the design note. `[Transaction(TransactionMode.Manual)]`/manual regeneration; no partial element on failure; Preflight stays read-only, returning `Result.Cancelled` on rejection.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #29 (PH3-2).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-4 — [Phase 3] Build the WPF dialog, activate UseWPF, and adopt CommunityToolkit.Mvvm

### Outcome

Replace today's file/settings-only AOI input with one modal, code-behind-only WPF dialog spanning address entry through a Preflight summary, built on CommunityToolkit.Mvvm 8.4.2, activating `UseWPF` for the first time (decisions 9, 11).

### Scope

- One modal `SolidGroundDialog`, zero `.xaml`/BAML files — avoiding the confirmed isolated-context XAML double-load bug (Nice3point/RevitToolkit#7, dotnet/wpf#1700, both open) — matching the owner's other add-in; UI is built in code, so "code-behind only" means no markup, not no MVVM helpers.
- Add `CommunityToolkit.Mvvm` `8.4.2` (exact-pinned, MIT, 2026-03-25) as an unconditional `PackageReference` in `SolidGround.Revit.csproj` only, never `Core`; its `net8.0` asset under `net10.0-windows` adds zero transitive dependencies, no native code. Both lock files gain the entry.
- Sections, in order: address entry -> geocode candidates -> parcel candidates with legal-description preview -> AOI buffer -> point budget (`Revit.ini` guard warning) -> unit choice (the two AGENTS.md foot definitions) -> level/toposolid-type -> a shared-coordinates opt-in checkbox, default off (PH3-3) -> provenance/accuracy preview -> Preflight summary with Create/Cancel.
- Owned via `WindowInteropHelper` against `commandData.Application.MainWindowHandle`; shown from inside `CreateToposolidCommand.Execute`, before today's Preflight/transaction — no new command or ribbon entry (PH3-7). Any network call runs through the existing synchronous `Task.Run(...).GetAwaiter().GetResult()` bridge; no Revit API off the Revit thread. Read `UIThemeManager.CurrentTheme` once at construction; defer `ThemeChanged` (modal only).
- Result-code mapping: dialog Cancel, Preflight rejection, and PH3-3's shared-coordinates refusal map to `Result.Cancelled`; an in-dialog lookup failure is caught and shown inline, never reaching `Execute`'s top-level catch; only an uncaught failure reaches `Result.Failed`.

### Acceptance criteria

- [ ] `UseWPF` is set true and the project still compiles under the CI Nice3point reference-assembly gate unchanged; the dialog contains zero `.xaml` files, verified by a repository check.
- [ ] `CommunityToolkit.Mvvm` `8.4.2` is referenced only from `SolidGround.Revit.csproj`; both lock files are updated; the Core-never-references-Revit test passes.
- [ ] The Result-code mapping above holds: only an uncaught failure reaches `Result.Failed`, and an in-dialog lookup failure never reaches `Execute`'s top-level catch.
- [ ] `AutomationProperties` and a HighContrast/SystemColors branch are present on every control.
- [ ] A Revit 2027 manual-evidence step confirms modal ownership, both theme branches, and the checkbox's default-off state render without exception, and runs `CreateToposolidCommand` twice in one session to confirm the toolkit's messenger/view-model teardown on repeat invocation inside the isolated context.

### AGENTS.md rules to keep

- One ribbon tab/panel/button unchanged (PH3-7); `TaskDialog` still used for the top-level failure path. Every package version pinned and justified; diagnostics never throw back into Revit.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #27 (PH3-0), #28 (PH3-1), #29 (PH3-2), #30 (PH3-3).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-5 — [Phase 3] Add CLI address and parcel commands with offline fixtures

### Outcome

Give the CLI the same address-to-boundary capability as the dialog, so Phase 3 sources work without Revit.

### Scope

- New `geocode` and `parcel` verbs, following the existing `Commands/*.cs` + options-record + `OptionTable.cs` shape, dispatched through `CliApplication.RunAsync`; reuse `IAddressGeocoder`/`IParcelBoundarySource` from PH3-1/PH3-2 with no duplicated logic.
- Tests through `FakeHttpMessageHandler` + `CliHost`; a live call needs an explicit opt-in flag plus any required key, and skips, never fails, otherwise.
- Resolve an address/parcel selection into today's `areaOfInterest.parcel` GeoJSON/WKT shape; no new `AreaOfInterestKind` is added (decision 8).

### Acceptance criteria

- [ ] `geocode`/`parcel` verbs produce deterministic, byte-identical JSON across repeated runs under `FakeHttpMessageHandler`.
- [ ] Missing-key or offline-mode short-circuits happen before any HTTP call; scripted provider errors map to specific, documented exit codes.
- [ ] A resolved parcel round-trips into `areaOfInterest.parcel` and back, covered by a settings round-trip test.
- [ ] `packages.lock.json` stays current if a package is added; the Core-never-references-Revit test passes.

### AGENTS.md rules to keep

- Offline-by-default, deterministic tests; explicit opt-in and key requirement for any live call.
- Restore/build/test pass before the change is presented complete; new packages pinned and justified.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #28 (PH3-1), #29 (PH3-2).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-6 — [Phase 3] Extend provenance to TerrainProvenance v3

### Outcome

Bump `TerrainProvenance` to schema v3 with an optional address/parcel record, feeding the JSON export now, and document the mapping Issue #16's Extensible Storage entity needs in its own later schema version once #16 lands (decision 7).

### Scope

- Add an optional `AoiProvenance`-shaped record to `TerrainProvenance` (bump `CurrentSchemaVersion` to 3), updating assembler, renderer, and reader together, as prior additions (#21, #23) did. Thread the fields through the existing, currently-null `ToposolidCreatedHook` so this one record can later feed Issue #16's `SchemaBuilder` without a second field-list definition.
- Store: geocoder provider/query text, retrieval date (distinct from `CollectionPeriod`), parcel-source identity, parcel/locator id, legal description when available, and the license/attribution text PH3-8 resolves.
- Document, in an architecture note, which fields #16's future schema version must add and how they map from this record; do not modify #16's approved design or write Extensible Storage code here.

### Acceptance criteria

- [ ] `TerrainProvenance`'s schema version is bumped to 3 with a strict-decode round-trip test covering v1, v2, and v3 documents, carrying geocoder provider/query/date, parcel-source identity, parcel/locator id, legal description when available, and license/attribution text.
- [ ] The exported JSON bundle derives from this one record; no second, divergent representation is added; a schema/version mismatch on read fails safely and diagnostically.
- [ ] No key or authorization header from any Phase 3 source reaches stored provenance or the export.
- [ ] An architecture note names the future Extensible Storage fields and their source, so Issue #16's later schema version needs no new field-list decision.

### AGENTS.md rules to keep

- Field-list contract lives in Core; `SchemaBuilder` stays in Revit. Never persist a key; redact query strings before storing as evidence.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #28 (PH3-1), #29 (PH3-2).

### Owner decisions depended on

- Whether the placement-record writer also carries the new fields, or stays limited to geometry/unit fields. Not addressed by decision 7.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH3-7 — [Phase 3] Propose AGENTS.md and conventions amendments for Phase 3 (proposal only)

### Outcome

Produce a reviewable, non-applied list of AGENTS.md/conventions-note wording changes Phase 3 needs — the boundary sentence verbatim plus a ribbon/command recommendation — so the owner accepts, edits, or rejects each before any is merged.

### Scope

- Draft proposed wording only, in a `docs/architecture/` note; do not edit `AGENTS.md` or the conventions note here.
- Reproduce, unchanged, this document's "Proposed AGENTS.md amendment" sentences:

  > Phase 3 adds an interactive Revit 2027 add-in workflow that turns a street address into geocoded coordinates, resolves a matching parcel boundary, and lets the operator confirm both — together with an optional shared-coordinates write and a native property line — before today's terrain pipeline runs, as researched in `docs/architecture/phase-3-interactive-add-in-research.md` (2026-09-21). The agent must not begin Phase 3 without an explicit implementation task.

- Cover at minimum: the sentence above; a dependency-and-key policy naming PH3-0's helper, PH3-1's providers/env vars, PH3-2's sources, and the Regrid purchase; the `UseWPF`/`CommunityToolkit.Mvvm` addition; PH3-3's `PropertyLine`/shared-coordinates decision; PH3-8's never-default list.
- Recommend keeping the single `CreateToposolidCommand`/push button — PH3-4 shows the dialog inside `Execute`, before Preflight/transaction, no new `IExternalCommand` — with a one-sentence conventions clarification. Decision 8 (AOI reuses `areaOfInterest.parcel`) needs no `AreaOfInterestKind` change, closing that gap.

### Acceptance criteria

- [ ] The proposal lists every AGENTS.md/conventions-note passage it would touch, old text next to proposed text; both stay unchanged at this issue's close.
- [ ] Every proposed change traces to a specific decision in a named Phase 3 issue (PH3-0 through PH3-6, PH3-8).
- [ ] The push-button-versus-second-command question is explicitly resolved with a stated recommendation and rationale.
- [ ] The owner's disposition (accept/edit/reject) is recorded per change before any dependent issue applies it.

### AGENTS.md rules to keep

- "The agent must ... update AGENTS.md only with the user's accepted direction" — this issue enforces exactly that gate.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: none formally; fuller wording is best drafted after #27–#33 (PH3-0 through PH3-6) and #35 (PH3-8), since each supplies a decision to cite.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md) and [revit-add-in-conventions.md](https://github.com/mjfrancese/SolidGround/blob/main/docs/architecture/revit-add-in-conventions.md).

---

## PH3-8 — [Phase 3] Resolve licensing and attribution obligations for geocoding and parcel sources

### Outcome

Document the attribution/license text Phase 3 must surface for every shipped source, and record the never-default list, feeding PH3-4's dialog and PH3-6's provenance.

### Scope

- Census Geocoder: public-domain federal data; whether the Data API's general attribution line applies to the separate keyless geocoder endpoint is unconfirmed and should be checked directly. Geocodio: storable "permanently without restrictions." Esri: needs the `premium:user:geocode:stored` privilege, made explicit by `forStorage=true`.
- Reproduce the area County's `AGS_Parcels` license text verbatim wherever used, parallel to existing OpenTopography attribution:

  > "the area County makes no warranty for fitness of use for a particular purpose express or implied... further agrees to hold the area County Government harmless... Copyright 2019 the area County. All rights reserved."

- Regrid Data Store License (PH3-2's reference purchase): one-time export, no updates, a one-year Term after which the licensee must cease use or delete the data, permission to load into internal tools, and a public-webpage attribution credit — inapplicable given internal-only use, but recorded for any future reader (decision 3).
- Never-default list, each with citation: the county's `GeocodeServer` proxy (no published terms; risks its paid credits); public Nominatim (bars a silent default); Regrid's live API without written consent (bars caching/derivatives, unlike the Data Store); ATTOM (evaluation-only, 24-hour cap); LightBox (no production tier); Cotality (bars derivatives, assigns any created to CoreLogic); Living Atlas "USA Parcels" (republishes Regrid's own restricted data). Any provider beyond these three needs its own documentation before it ships.

### Acceptance criteria

- [ ] A documented attribution string exists for every shipped source and is asserted present in the provenance export by a test.
- [ ] The note records, with citation and date, why the county's `GeocodeServer` proxy, public Nominatim, the Regrid live API without consent, ATTOM, LightBox, Cotality, and Living Atlas are excluded as defaults; no public fixture or default path contains their data.
- [ ] The WPF dialog displays the relevant attribution once per run, not only in logs.
- [ ] The Regrid purchase's Term and cease-use-or-delete obligation are recorded where an operator will see them before lapse.
- [ ] Every claim states its source URL and retrieval date.

### AGENTS.md rules to keep

- Describe SolidGround as a site-form tool, not a survey instrument, in attribution/accuracy text. Never embed a key, token, or signed URL in logs, documentation, or fixtures.

### Relationships

- Parent: #26 ([Epic] Phase 3). Blocked by: #28 (PH3-1), #29 (PH3-2).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## Notes for the owner

Unresolved gaps to track, not assume away: the county Locator Number and [withheld] [withheld]'s recorded book/page were never obtained (behind a paid Tapestry/Laredo search or an in-person Recorder request); `DEEDBKPG`/`ASRBKPG` semantics remain unconfirmed as a book/page equivalent; no rate limit is published for the keyless Census geocoder; Google's point-in-polygon restriction stays unresolved but moot, since PH3-1 does not adopt Google. Confirm the Regrid export's file format at purchase against PH3-2's GeoJSON-first requirement. ReportAll's cheaper $200 file was found licensing-ambiguous (its storage right seems to need a sales-arranged tier; no archived analysis of the Data Store product itself resolves this); Regrid's $300 purchase was chosen as the more confidently-licensed pick (decision 3).

---

## Sources

Retrieved 2026-09-21 unless noted. See the research note's own Sources section for the trail behind claims carried over unchanged from it.

1. [Census Geocoder docs](https://geocoding.geo.census.gov/geocoder/Geocoding_Services_API.html); [Census Data API ToS](https://www.census.gov/data/developers/about/terms-of-service.html)
2. [Esri findAddressCandidates (`forStorage`)](https://developers.arcgis.com/rest/geocode/find-address-candidates); [Esri geocoding credits](https://doc.arcgis.com/en/arcgis-online/reference/geocode.htm)
3. [Geocodio storage terms](https://www.geocod.io/geocoding-terms-of-use-comparison); [Geocodio pricing](https://www.geocod.io/pricing)
4. [Nominatim usage policy](https://operations.osmfoundation.org/policies/nominatim/); [OSMF Geocoding Guideline](https://osmfoundation.org/wiki/Licence/Community_Guidelines/Geocoding_-_Guideline)
5. [the area County `AGS_Parcels`](https://maps.[withheld]/hosting/rest/services/Maps/AGS_Parcels/MapServer/0); [item metadata/license](https://www.arcgis.com/sharing/rest/content/items/fd4893ca99244279adb2ffa206e09ec7?f=json); [Assessor Real Estate Search](https://assessor.[withheld]/realestate/searchinput.aspx); [Recorder Deed Search](https://[withheld]/the area-county-departments/revenue/recorder-of-deeds/deed-search/); [Open Data Disclaimer](https://[withheld]/open-data/)
6. [Regrid API ToS](https://regrid.com/terms/api); [Regrid Data Store License](https://app.regrid.com/store/license); [trial coverage](https://support.regrid.com/reference/list-of-restricted-counties); [trial limits](https://support.regrid.com/reference/getting-started-with-your-api); [parcel schema](https://support.regrid.com/docs/regrid-parcel-schemas); [ownership fields](https://support.regrid.com/docs/ownership); [legal-description fields](https://support.regrid.com/docs/legal-description-subdivision)
7. [ATTOM legal terms](https://api.developer.attomdata.com/legal); [ATTOM parcel boundaries](https://www.attomdata.com/data/boundaries-data/parcel-boundaries/)
8. [LightBox MSA (PDF)](https://www.lightboxre.com/wp-content/uploads/2026/08/2026.04.01-Master-Services-Agreement-SECURED.pdf); [LightBox trial terms](https://developer.lightboxre.com/terms)
9. [Cotality ToU](https://www.cotality.com/legal/terms-of-use); [Cotality Evaluation Terms](https://www.cotality.com/legal/evaluation-terms-and-conditions)
10. [Esri Living Atlas "Regrid USA Parcel Boundaries" item metadata](https://www.arcgis.com/sharing/rest/content/items/a2050b09baff493aa4ad7848ba2fac00?f=json)
11. [ReportAll ToS](https://reportallusa.com/terms-of-service)
12. [Autodesk forum: PropertyLine programmatically](https://forums.autodesk.com/t5/revit-api-forum/create-property-line-programmatically/td-p/5600736); Autodesk "What's New in Revit 2027" (help.autodesk.com, `guid=GUID-DC8BFD57-09A4-4792-995C-AECA5855D1B4`)
13. [Nice3point/RevitToolkit#7](https://github.com/Nice3point/RevitToolkit/issues/7); [dotnet/wpf#1700](https://github.com/dotnet/wpf/issues/1700)
14. [CommunityToolkit.Mvvm 8.4.2](https://www.nuget.org/packages/CommunityToolkit.Mvvm); [license](https://raw.githubusercontent.com/CommunityToolkit/dotnet/main/License.md); [TFM resolution](https://learn.microsoft.com/en-us/dotnet/standard/frameworks)
