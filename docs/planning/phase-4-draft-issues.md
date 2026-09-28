PUBLISHED 2026-09-27 on the owner's approval ("Publish"): milestone "Phase 4: Accuracy, integrity, geolocation, and site context" (#4), issues #40–#59, and the Issue #18 changes below. The GitHub issues are now the source of truth; this file records the plan as approved.

| Draft ID | Issue |
|---|---|
| Epic 4A | #40 |
| Epic 4B | #41 |
| PH4-0 | #42 |
| PH4-1 | #43 |
| PH4-2 | #44 |
| PH4-3 | #45 |
| PH4-4 | #46 |
| PH4-5 | #47 |
| PH4-6 | #48 |
| PH4-7 | #49 |
| PH4-8 | #50 |
| PH4-9 | #51 |
| PH4-10 | #52 |
| PH4-11 | #53 |
| PH4-12 | #54 |
| PH4-13 | #55 |
| PH4-14 | #56 |
| PH4-15 | #57 |
| PH4-16 | #58 |
| PH4-17 | #59 |
| Issue #18 changes | applied to #18 |

# Phase 4 draft issues — accuracy, integrity, geolocation, and site context

Drafted from the Phase 4 review's verified findings (F01-F40, both skeptic lenses, corrected claims applied where a verdict was partially-confirmed) and the owner's 2026-09-27 decisions. Read against SolidGround's committed history through `ea96afb`, with every repository citation re-verified against `9c1cb0a` before publishing (`git show HEAD:<path>`; nothing here reads the working tree, which another session is actively editing). Modeled on `docs/planning/phase-3-draft-issues.md` and the real Phase 3 issues (#26, #27, #31, #34).

Numbering (`PH4-0`..`PH4-17`, plus two `[Epic]` issues) stays a working label only — nothing has been filed, so no real issue number exists yet. Labels are drawn only from this repository's existing label set (`gh label list`, retrieved 2026-09-27). Every issue proposes milestone **"Phase 4: Accuracy, integrity, geolocation, and site context"**, a new milestone (today's milestones are #1 Phase 1, #2 Phase 2, #3 Phase 3; this would be the next one, exact number assigned at creation time). Every Revit API member an issue relies on is named and flagged "verify against RevitAPI.xml (Revit 2027)" (or RevitAPIUI.xml, where the member lives in that assembly) before implementation, per `AGENTS.md`'s Revit 2027 rule.

20 issues are drafted in total: one gate issue, two epics, and 17 numbered children. A separate, non-issue section proposes edits to the existing Issue #18.

## Milestone

**Title:** Phase 4: Accuracy, integrity, geolocation, and site context

**Description:** Close silent-failure classes in the Toposolid transaction path Issue #15 built; measure the curvature-aware simplifier's real vertical error before deciding whether to build a certified-bound TIN method; complete CRS-identity, geolocation, and provenance coverage; ship the small, already-identified dialog polish items; and — gated on this milestone's own AGENTS.md amendment (PH4-0) — add a US-sourced building-footprint, building-height, and road context-layer epic to the host model. Also proposes a scope addendum to the existing classified-point-cloud investigation, Issue #18. Epic 4B (site context) is, by the owner's own characterization, comparable in size to Phase 3; this draft specifies only its first research/design/implementation cut, and expects further child issues once PH4-13/PH4-14 conclude.

## Owner decisions this draft rests on (2026-09-27)

Restated briefly so each issue below can be checked against the decision it implements, without re-reading the full review:

- Coverage stays US-only, 1 m USGS data, with no coarser or keyless fallback elevation source; geometry goes into the host model, not a linked site model; the owner is a single, non-commercial user who wants "the most versatile (but still accurate as hell) tool possible"; Revit 2027 and later only.
- Site-context layers ("Full context epic"): plan buildings with heights, plus roads, as its own large epic, comparable in size to Phase 3 — Epic 4B below.
- Vertical-error accuracy: "Measure first" — ship the achieved worst-case/RMS vertical-error diagnostic (PH4-3) before deciding on `SimplificationMethod.TinError` (PH4-4).
- Issue #18 (classified ground-return point-cloud source): "Yes, investigate" — an investigation-first track, no build until the owner separately approves a scope.
- Future comparisons: "Add benchmark" — one fixed test site, one real 1 m reference, one yardstick, a dated scorecard, and a five-project watch list, as one issue (PH4-12).
- Still open, kept open by this draft, not answered here: whether and how to write Revit `SiteLocation` CRS identity and geocoded latitude/longitude/time zone (PH4-9, opt-in design still undecided); an acquisition disk cache (not drafted, see below); whether to revisit the "no live-driving harness" debugging convention for a local, never-in-CI Revit journal-replay smoke test (not drafted, see below).

---

## PH4-0 — [Phase 4] Propose AGENTS.md amendments for Phase 4 (proposal only)

**Labels:** documentation, type: gate, area: core, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Propose the exact `AGENTS.md` wording Phase 4 needs so the owner can accept, edit, or reject each change before any is merged — matching Issue #34's own proposal-only precedent for Phase 3's conventions note.

### Scope

- Draft proposed wording only, in a `docs/architecture/` note; do not edit `AGENTS.md` here, per `AGENTS.md`'s own rule that the agent must "update AGENTS.md only with the user's accepted direction."
- Cover at minimum:
  - **Mission/boundary sentence.** An addition to "Mission and current boundary" naming Phase 4's scope, mirroring the shape of the existing Phase 3 sentence, and stating that no Phase 4 work begins without an explicit implementation task (same convention as Phases 2 and 3).
  - **The Groundit data-source clause.** A targeted rewrite of "Groundit is an architecture reference only... The agent must not copy its Python, pyRevit, browser, data-source, or multi-version design," precise enough to still forbid copying Groundit's specific data-source choices (AWS Terrain Tiles; OpenStreetMap via Overpass) and its "link a separate document" design, while allowing a pluggable US-federal context-layer source (for example USA Structures) that Epic 4B needs. Must not blanket-permit OSM: any OSM-derived geometry actually entering the host model needs its own later, separately cited decision (see PH4-13's licensing research), not a side effect of this wording.
  - **Architecture table / Core responsibilities.** New rows or sentences naming pluggable context-layer sources (building footprint, building height, road) and a future classified point-cloud source, alongside today's terrain-only list for `SolidGround.Core`; and naming host-model context-layer element creation for `SolidGround.Revit`.
  - **Data-contract extensions.** Extend the existing NODATA rule (today scoped to "every AAIGrid `NODATA_value`"; "a sentinel must never become an elevation") to any future context-layer per-point drape/height lookup and to a future point-cloud return (F16); extend the existing reversible local-origin-transform and provenance-labeling rules to context-layer and point-cloud data the same way they already apply to terrain.

### Acceptance criteria

- [ ] The proposal quotes the exact current `AGENTS.md` text next to the exact proposed replacement/insertion for every passage it touches; both stay unchanged in the repository at this issue's close.
- [ ] Every proposed change traces to a specific Phase 4 issue or finding (F-number) that needs it.
- [ ] The Groundit-clause rewrite explicitly still forbids copying Groundit's own OSM/Overpass ingestion code and its linked-document design, and does not by itself authorize placing OSM-derived geometry in the host model.
- [ ] The owner's disposition (accept/edit/reject) is recorded per change before any Epic 4B implementation issue (PH4-15, PH4-16, PH4-17) or the proposed Issue #18 addendum applies it.
- [ ] The proposal states explicitly, in writing, that Epic 4A (PH4-1 through PH4-12) does not depend on this issue, because it fits inside today's already-accepted terrain/toposolid/CRS boundary.

### AGENTS.md rules to keep

- The agent must update `AGENTS.md` only with the user's accepted direction — this issue enforces exactly that gate, matching Issue #34/PH3-7's own proposal-only precedent.
- Every proposed change traces to a specific Phase 4 issue or finding; nothing is proposed for its own sake.

### Relationships

- Parent: none (Phase 4's own gate, not a child of either epic).
- Blocks: PH4-15, PH4-16, PH4-17 (Epic 4B implementation only) and the proposed Issue #18 addendum's licensing/scope language.
- Does not block: Epic 4A (PH4-1 through PH4-12), or PH4-13/PH4-14 (research and design may start once selected; PH4-14's conclusions should assume this gate's eventual outcome).

### Start condition

Owner selection required; matches the phase-gate convention Issues #10 and #34 already established for this repository.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## [Epic] Phase 4A — Model integrity, measured accuracy, and CRS/geolocation completeness

**Labels:** type: epic, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Close the transaction-path silent-failure classes and CRS/geolocation gaps this review found in the shipped Phase 2/3 product, without waiting on Epic 4B's own AGENTS.md gate.

### Scope

- Track twelve independently schedulable children (PH4-1 through PH4-12): per-vertex post-create verification; a reuse/refuse/create duplicate-run guard; a measured vertical-error diagnostic; the `SimplificationMethod.TinError` decision that diagnostic feeds; coverage-floor and collection-date provenance; address/parcel provenance in Extensible Storage; a State Plane process-mode fixture; grid convergence/scale-factor display; SiteLocation/geolocation research; grouped dialog polish; a real WPF-binding runtime test project for future dialog defects; and a repeatable comparison benchmark against a real 1 m USGS reference.
- None of these children requires the site-context AGENTS.md amendment (PH4-0); all fit inside today's already-accepted terrain/toposolid/CRS boundary.

### Acceptance criteria

- [ ] Every child closes with a cited finding (F0x), matching the evidence discipline used since Issue #12.
- [ ] None of Epic 4A's children required an AGENTS.md text change beyond what Issue #34/Phase 3 already accepted.
- [ ] The owner accepts a Revit 2027 end-to-end result for at least the guard/verification children (PH4-1, PH4-2) before this epic closes, matching Phase 2/3's own evidence bar.

### Relationships

- Blocked by: none.
- Children: PH4-1 through PH4-12.

### Start condition

Owner selection required per child issue.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-1 — [Phase 4] Verify individual vertex position and elevation after Toposolid.Create

**Labels:** type: feature, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Close a real silent-failure class: today's `PostCreationVerification` can't catch a shifted or swapped vertex if the aggregate count and bounding box still look right (F02).

### Scope

- Add a nearest-neighbor, tolerance-bounded per-vertex position/elevation cross-check to `PostCreationVerification.Verify` (`src/SolidGround.Revit/Transactions/PostCreationVerification.cs:73-116`), which today performs only an aggregate bounding-box `Contains` check plus a `vertexCount < points.Count` check.
- Read `Autodesk.Revit.DB.SlabShapeEditor.SlabShapeVertices` / `Autodesk.Revit.DB.SlabShapeVertex.Position` for at least every structural/boundary point — verify against RevitAPI.xml (Revit 2027) before writing the call; `docs/architecture/revit-toposolid-creation.md:1827,1829` already records both as reachable but unused for this purpose today.
- Revit does not guarantee returned vertex order matches input order, so matching must be nearest-neighbor (or an equivalent stable correspondence), never positional.
- Must not weaken the existing fail-closed rollback behavior: a verification failure still rolls back the transaction exactly as it does today.

### Acceptance criteria

- [ ] An offline unit test constructs a scenario where an aggregate count/bounding-box check would pass but an individual vertex's position or elevation is wrong, and asserts the new check catches it.
- [ ] A Revit 2027 manual-evidence session confirms the new check runs against a real created toposolid without a false positive on a correct run, and correctly rolls back an intentionally corrupted one.
- [ ] Existing `PostCreationVerification` tests continue to pass, extended rather than weakened.
- [ ] The verification failure message names which vertex/index failed and by how much.

### AGENTS.md rules to keep

- Every Revit API member this issue reads (`SlabShapeEditor.SlabShapeVertices`, `SlabShapeVertex.Position`) is verified against RevitAPI.xml (Revit 2027) and cited in the design note before merge.
- The existing fail-closed rollback rule stays unchanged: a verification failure still rolls back the whole transaction, never a partial element.

### Relationships

- Parent: Epic 4A. Blocked by: none.
- Related: PH4-2 also touches `Transactions/*.cs` and may share a live-evidence session.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-2 — [Phase 4] Add a reuse/refuse/create duplicate-run guard with copied-element provenance detection

**Labels:** type: feature, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Stop a second run of `CreateToposolidCommand` against the same open document from silently creating an unrelated second Toposolid (and PropertyLine), and let a Revit-native copy of a created toposolid be recognized as stale instead of silently keeping its original's provenance (F01).

### Scope

- Mint a new Extensible Storage schema (new GUID — this repository's schema-evolution model does not republish an existing GUID with a different field shape) carrying a stable identity string: Issue #33's `AddressParcelProvenance.Parcel.StableParcelId` (`src/SolidGround.Core/Provenance/AddressParcelProvenance.cs:130`) as "stem," plus a small content signature as "build" — mirroring mantleplace-dcc's `TerrainIdentity.Decide` precedent, but resolving a multi-match collision by refusing and naming every colliding element id, never mantleplace-dcc's own silent first-match pick.
- Before the transaction opens, scan existing Toposolid entities in the document (`Autodesk.Revit.DB.FilteredElementCollector` — verify against RevitAPI.xml (Revit 2027) — reading the new schema's `AccessLevel.Public` entity) and decide Reuse / Refuse / Create; surface the decision through the existing capped `ProblemReportDialog`.
- Two named hardening outcomes, built in from the start: (1) a stored identity that no longer resolves to any element (deleted/reopened/copied document) falls back to Create with a logged note, never a crash; (2) more than one existing element matching the same identity is its own named, refusing outcome.
- A bbox/radius AOI (or a raw parcel-polygon AOI with no address/parcel lookup) has no `StableParcelId` — decide and document an explicit fallback identity or an explicit, disclosed exclusion from this guard's first cut; never silently skip the guard without saying so in the dialog.
- Scoped to detect-and-refuse/reuse only; true point-level update-in-place stays out of scope.

### Acceptance criteria

- [ ] Running the command twice against the same open document and the same resolved parcel identity produces Reuse or Refuse, never a second unrelated Toposolid, verified in a Revit 2027 manual-evidence session.
- [ ] A Revit-native copy of a created toposolid is detected (content-signature/build mismatch or duplicate stem) and flagged rather than silently treated as an original.
- [ ] The two named hardening outcomes (stale identity, multi-match collision) each have their own offline test and are exercised in the Revit 2027 evidence session.
- [ ] The bbox/radius/raw-polygon fallback behavior is explicit and documented, not a silent gap.
- [ ] The new schema's GUID, version, and field list are recorded in a design note alongside the existing Issue #16 schema note.

### AGENTS.md rules to keep

- The new Extensible Storage schema mints its own new GUID; this repository's schema-evolution model never republishes an existing GUID with a different field shape.
- `Autodesk.Revit.DB.FilteredElementCollector` and every other named Revit API member are verified against RevitAPI.xml (Revit 2027) before merge; the scan itself runs read-only, before the transaction opens.

### Relationships

- Parent: Epic 4A. Blocked by: none.
- Related: PH4-6 also needs a new Extensible Storage schema GUID; if the two are scheduled together, one live-evidence session and one combined schema version can cover both bumps rather than two sequential GUID changes — a scheduling opportunity, not a hard dependency.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-3 — [Phase 4] Measure and report the simplifier's achieved worst-case and RMS vertical error

**Labels:** type: feature, area: core, area: cli, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Turn today's acknowledged generalization-quality proxy (`MaxRemovedCurvatureMagnitude`/`MaxRemovedElevationResidual`, documented at `src/SolidGround.Core/Simplification/SimplificationContracts.cs:224` as "a generalization-quality proxy, not a certified geometric error bound") into an actual measured number for every run of the curvature-aware simplifier already shipping — independent of, and a required input to, PH4-4's later `TinError` decision.

### Scope

- Triangulate the retained (post-simplification) points with `NetTopologySuite.Triangulate.DelaunayTriangulationBuilder` (`SetSites`/`GetTriangles(GeometryFactory)`), already available in the pinned NetTopologySuite 2.6.0 (`src/SolidGround.Core/SolidGround.Core.csproj`) with no version change and no new dependency.
- For every non-NODATA cell in the full-resolution clipped grid, locate its containing triangle and compute the barycentric-interpolated surface elevation; compare to the true full-resolution value; report the worst-case (max absolute) and RMS residual.
- Full-resolution point-location cost argues for an explicit, opt-in diagnostic (CLI `verify`-style tooling first) rather than inline on every ordinary Revit run; document the cost/benefit tradeoff rather than silently defaulting it on.
- New test coverage matching `GridTerrainSimplifierTests.cs`'s existing bar: ridges, swales, edges, NODATA holes, determinism.

### Acceptance criteria

- [ ] A new diagnostic (CLI verb or `--verify` flag) reports worst-case and RMS vertical error in the run's chosen output unit for a completed simplification.
- [ ] The diagnostic never fabricates a value for a NODATA cell it cannot triangulate against; a coverage gap is reported as a gap, never silently skipped or defaulted.
- [ ] Tests cover ridges, swales, edges, NODATA holes, and determinism.
- [ ] The diagnostic is opt-in; an ordinary Revit `CreateToposolidCommand` run's timing is unaffected unless requested.
- [ ] The design note explicitly states how the implementation handles NetTopologySuite's incremental Delaunay triangulator defaulting to a convex bounding frame, against SolidGround's routinely concave, NODATA-holed grid shapes.

### AGENTS.md rules to keep

- A sentinel must never become an elevation: the diagnostic reports a coverage gap as a gap, never a fabricated or interpolated value for a cell it cannot triangulate against.
- No new package: NetTopologySuite stays at its already-pinned, exact-versioned `2.6.0`; no Python/GDAL/native-binary dependency is added.

### Relationships

- Parent: Epic 4A. Blocked by: none.
- Blocks: PH4-4 (the `TinError` decision needs this diagnostic's real numbers first, per the owner's "measure first" decision).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-4 — [Phase 4] Decide SimplificationMethod.TinError: greedy exact-sample TIN vs. geometry3Sharp QEM

**Labels:** type: research, area: core, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Resolve, from PH4-3's real measured numbers, which (if either) candidate implementation should fill SolidGround's already-reserved `SimplificationMethod.TinError` enum member (`src/SolidGround.Core/Simplification/GridTerrainSimplifier.cs`, today throws `TerrainSimplificationException` for this method) (F07).

### Scope

- Do not start implementation until PH4-3 has produced real worst-case/RMS numbers for the shipping curvature-aware simplifier at representative point budgets; use those numbers to decide whether a certified worst-case bound is worth its own multi-week effort at all, and if so, which family.
- **Candidate A:** a from-scratch, hand-built greedy worst-pixel-error insertion policy (mapbox/delatin's Garland-Heckbert algorithm family as public-algorithm-design reference only — its ISC-licensed source must never be copied) layered over NetTopologySuite's tested Delaunay/triangulation types, with an explicit NODATA-safe bootstrap (delatin's own unconditional 4-corner bootstrap is unsafe for SolidGround's typically NODATA/clip-cornered grids) and a coverage-floor equivalent. Every inserted vertex is an exact original sample, never blended.
- **Candidate B:** `gradientspace/geometry3Sharp`'s `Reducer` (BSL-1.0), usable today with zero source-porting work, but (a) needs a new grid-to-triangle-mesh conversion layer that does not exist in Core, (b) blends a new vertex position on every ordinary interior edge collapse, (c) has had no functional commit since 2018-06-20 and zero automated upstream tests, and (d) would need a `THIRD-PARTY-NOTICES` entry and exact version pin.
- Whichever is chosen needs its own full ridge/swale/edge/NODATA-hole/determinism/budget-enforcement test suite plus mesh-consistency invariants — new-issue-sized work either way.

### Acceptance criteria

- [ ] A short decision memo cites PH4-3's actual measured numbers and states, from them, whether the accuracy gain from a certified bound is worth the effort at all.
- [ ] If the owner proceeds, the memo names the chosen candidate (A or B) with a stated reason tied to SolidGround's exact-sample-retention philosophy versus implementation cost, recorded as its own dated decision.
- [ ] If geometry3Sharp is chosen, the memo records the required `THIRD-PARTY-NOTICES` entry and exact version pin before any implementation issue opens.
- [ ] No code implementing either candidate is written under this issue; a follow-on implementation issue opens only after this decision is recorded.

### AGENTS.md rules to keep

- Candidate A's use of mapbox/delatin (ISC) and Candidate B's use of geometry3Sharp (BSL-1.0) are both ideas-and-patterns references only; neither project's source is ever copied, per this repository's licensing rule.
- Every package version is pinned and justified; if geometry3Sharp is chosen, its `THIRD-PARTY-NOTICES` entry and exact version pin are recorded before any implementation issue opens.

### Relationships

- Parent: Epic 4A. Blocked by: PH4-3.

### Start condition

Owner selection required, and PH4-3 must be closed with real measured numbers first.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-5 — [Phase 4] Record the point-budget coverage-floor fraction and investigate elevation collection-date provenance

**Labels:** type: feature, area: core, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Close two related, currently-silent provenance gaps: the coverage-floor fraction that changes which points survive simplification is invisible after the fact (F19); `ElevationSourceMetadata.CollectionPeriod` is always null because OpenTopography's `usgsdem` response never carries a collection date, and no one has checked whether a different OpenTopography endpoint can supply one (F17).

### Scope

- **Coverage floor (F19, do now):** thread `coverageFloorFraction` (today constructor-only on `GridTerrainSimplifier`, `src/SolidGround.Core/Simplification/GridTerrainSimplifier.cs:39` (default `0.2d` at line 35), never a field of `SimplificationRequest`) into the export document's `provenance.simplification` object (today only `pointBudget`/`method`, `src/SolidGround.Core/Exports/TerrainExportBundleRenderer.cs:309-315`) as an additive change. Also log it via `AddInLog` in `SolidGround.Revit.CreateToposolidCommand` (not logged there today) so the value used for a given toposolid is recoverable even before any schema bump lands.
- **Collection date (F17, investigate first):** check OpenTopography's `/otCatalog` endpoint (its `detail=true` mode and `productFormat=PointCloud` filter are confirmed to exist) for whether it can return a per-AOI collection-date range for the `usgsdem` product, without a third-plus live HTTP call per fetch and without guessing across overlapping collection projects. Investigating this (S effort) and wiring it in if feasible are not the same size of task and must not be scoped as one: the vertical reference is a single, static, location-invariant constant declared once in code, whereas a collection date would be AOI-specific and most plausibly obtainable only via a second, per-request, ambiguity-prone catalog call (a new daily-quota cost on top of the existing two calls per fetch). Close this issue with the investigation and its written recommendation; if the recommendation is "wire it in," open a separate, later-scoped follow-on issue for the implementation, mirroring PH4-3/PH4-4's own measure-first-then-decide split. If the investigation instead concludes no safe mechanism exists, record an explicit "not available from this source" provenance value rather than a silently blank field — never infer a date from the HTTP response's own `Date` header.

### Acceptance criteria

- [ ] `provenance.simplification` in the export document carries `coverageFloorFraction`; a round-trip test covers reading it back.
- [ ] `SolidGround.Revit`'s `AddInLog` records the coverage-floor fraction used for a given run.
- [ ] The `/otCatalog` investigation is written up as a decision memo (feasible, or not feasible) with citations to OpenTopography's own current API documentation; this issue closes with that recommendation, not with an implementation.
- [ ] If the recommendation is that a safe mechanism exists, a separate, later-scoped follow-on issue is opened for the implementation before any code wiring `CollectionPeriod` from a catalog call is written; if the recommendation is that none exists, this issue itself records an explicit "not reported by this source" value replacing today's silent null in provenance and in Extensible Storage's `HasCollectionPeriod` flag.
- [ ] Neither half weakens the existing "fabricating catalog values would violate 'fail rather than assume'" rule recorded in `docs/architecture/opentopography-usgs1m-source.md:158`.

### AGENTS.md rules to keep

- A sentinel must never become an elevation, and fabricating a catalog value would violate "fail rather than assume": an unavailable collection date is recorded as explicitly unavailable, never guessed or inferred from an HTTP header.
- Query strings stay redacted before logging, including any `/otCatalog` call this issue's investigation half makes.

### Relationships

- Parent: Epic 4A. Blocked by: none.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-6 — [Phase 4] Wire Issue #33's address/parcel provenance into a new Extensible Storage schema version

**Labels:** type: feature, area: core, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Close the gap where `AddressParcelProvenance` already reaches `CreateToposolidCommand.cs` and the JSON export document but is silently dropped by `ExtensibleStorageProvenanceValues.From`/`ProvenanceEntityWriter.Attach`, so today's on-element provenance can't answer "what address/parcel produced this toposolid" (F23).

### Scope

- Mint a new Extensible Storage schema (new GUID) adding the roughly 15 fields `docs/architecture/address-parcel-provenance.md:134-181,192-198` already specs: geocoder provider/query/date, parcel-source identity, parcel/locator id, legal description when available, and license/attribution text. Comfortably under Revit's 256-field `SchemaBuilder` ceiling — verify against RevitAPI.xml (Revit 2027).
- Update `ExtensibleStorageProvenanceValues.From` and `ProvenanceEntityWriter.Attach` (`src/SolidGround.Core/Provenance/ExtensibleStorageProvenanceValues.cs`, `src/SolidGround.Revit/Provenance/ProvenanceEntityWriter.cs`) to read `provenance.AddressParcel` and write/read back the new fields, following the existing four-part read-back discipline Issue #16 established.
- Field-list contract lives in Core; the `SchemaBuilder` call stays in Revit.

### Acceptance criteria

- [ ] The new schema's field list, GUID, version, and access levels (`AccessLevel.Public` read / `AccessLevel.Vendor` write, matching the existing schema) are recorded in a design note alongside `docs/architecture/revit-extensible-storage-provenance.md` and `docs/architecture/address-parcel-provenance.md`.
- [ ] A created-then-reopened toposolid's on-element entity round-trips address/parcel identity, verified in a Revit 2027 manual-evidence session (save/reopen).
- [ ] No key, authorization header, or owner/occupant personal information reaches the new fields — only the identity/attribution data already flowing into the JSON export today.
- [ ] Existing schema-version-1 entities and their 36 fields are unaffected; the new schema is additive, not a breaking replacement.

### AGENTS.md rules to keep

- The new schema keeps `AccessLevel.Public` read / `AccessLevel.Vendor` write, matching the existing schema; the field-list contract lives in Core, and the `SchemaBuilder` call stays in Revit.
- No key, authorization header, or personal information reaches a stored field — only the identity/attribution data already flowing into the JSON export today.

### Relationships

- Parent: Epic 4A. Blocked by: none.
- Related: PH4-2 also needs a new schema GUID; consider scheduling together (see PH4-2's Relationships note).

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-7 — [Phase 4] Add a US State Plane / Lambert Conformal Conic process-mode fixture

**Labels:** type: feature, area: core, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Close a twice-flagged "untested" gap: `process` mode's WKT parser and transform factory are already projection-method-agnostic, and the pinned ProjNET 2.1.0 already contains a working `LambertConformalConic2SP` implementation, but no committed fixture or test exercises it (F06).

### Scope

- Add one committed, cited NAD83 State Plane `.prj` fixture (a real, publicly documented zone; never a real client/site location) plus a matching test fixture, following the same cited-defining-parameters discipline as the existing 40 UTM rows.
- Add a `process`-mode integration/reversibility test exercising it end to end, at the same tolerance rigor as the existing UTM zone-15N fixture.
- Explicitly decide, and document, whether the existing zero-datum-shift NAD83-as-WGS84 approximation applies unchanged to this fixture's realization (plain NAD83 vs. NAD83(2011)), or needs its own stated caveat.
- `fetch` mode stays UTM-only; this issue changes nothing there.

### Acceptance criteria

- [ ] A committed NAD83 State Plane `.prj` fixture and matching test pass, with a cited public source for the fixture's defining parameters.
- [ ] The reversibility test's round-trip residual is reported and compared against the existing UTM fixture's own tolerance.
- [ ] The datum-approximation decision is written down, not left implicit.
- [ ] No new package is added; ProjNET stays the sole pinned horizontal-transform dependency.

### AGENTS.md rules to keep

- The fixture is a real, publicly documented zone; never a real client/site location or other personal information.
- No new package: ProjNET stays the sole pinned, exact-versioned horizontal-transform dependency.

### Relationships

- Parent: Epic 4A. Blocked by: none.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-8 — [Phase 4] Compute and display grid convergence and point scale factor at the local origin

**Labels:** type: feature, area: core, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Make the size of SolidGround's existing, accepted zero-rotation approximation visible, without reversing the owner's 2026-09-26 north-lock decision (`ProjectPosition.Angle` stays hardcoded to `0d`, `SharedCoordinatesGate.cs:119`) (F08).

### Scope

- Add a pure `SolidGround.Core` function computing grid convergence and point scale factor at a site by numerical differentiation of the already-built `IHorizontalCoordinateTransform.Forward(Coordinate2D)`, generic over any transform (UTM today, State Plane once PH4-7 lands) — no new package.
- Decide and document the display surface: the `fetch` path only learns its authoritative EPSG/UTM-zone code from GeoTIFF GeoKeys returned after the dialog's provenance-preview step has already shown, so surface the computed value on a post-acquisition surface (creation summary or log) once the fetch's own GeoKeys have resolved the real transform; the offline `process`/sidecar path (CRS already known before the pipeline runs) may show it earlier if simpler to implement consistently.
- Purely additive; does not touch `ProjectPosition.Angle` or reopen the north-lock decision.

### Acceptance criteria

- [ ] The new function's convergence/scale-factor output is covered by tests analogous to closed-form cross-checks, not just self-consistency.
- [ ] The chosen display surface is implemented and a Revit 2027 manual-evidence step confirms it renders a real, non-placeholder value for a real fetch.
- [ ] `ProjectPosition.Angle`'s value and the existing north-lock behavior are unchanged; existing shared-coordinates tests still pass.
- [ ] The function works unmodified against a State Plane transform once PH4-7 lands.

### AGENTS.md rules to keep

- Purely additive: does not touch `ProjectPosition.Angle` or reopen the owner's own accepted north-lock decision.
- No new package: a pure `SolidGround.Core` function, Revit-free, keeping every Revit reference out of Core.

### Relationships

- Parent: Epic 4A. Blocked by: none. Benefits from (not blocked by) PH4-7.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-9 — [Phase 4] Research Revit SiteLocation CRS-identity and optional geocoded lat/long/time-zone writes

**Labels:** type: research, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Resolve the still-open design questions around writing a CRS-identity tag and/or geocoded latitude/longitude/time zone into the model, and settle a real conflict this review's own two verification passes left unresolved about which members are actually callable (F05, F21).

### Scope

- **Confirm compiled accessibility before scoping implementation.** This review's own evidence-lens verification ran a from-scratch, read-only ECMA-335 metadata probe directly against the installed `RevitAPI.dll` (no assembly load, no Revit process) and found `SiteLocation.GetEPSGCode()`, `ProjectLocation.SetProjectedSpaceToLocalTrf`, `SiteLocation.SetBasicGeolocation`, `.IsGeolocated`, and `.GetGeolocationBase` all compiled `Attributes=Assembly` (C# `internal`) — uncallable by any third-party add-in — while `SiteLocation.SetGeoCoordinateSystem(string)`, `ProjectLocation.GetProjectPosition`/`SetProjectPosition`, and `SiteLocation.GeoCoordinateSystemId`/`GeoCoordinateSystemDefinition` are Public. A second verification pass did not independently check compiled accessibility (it read `RevitAPI.xml` doc-comment presence only, which does not by itself prove public accessibility) and so is not a competing confirmation. A second, independent metadata read of the same installed `RevitAPI.dll` (27.0.10.13) on 2026-09-27 reproduced it: `GetEPSGCode` and `SetProjectedSpaceToLocalTrf` are `Assembly`; `SetGeoCoordinateSystem`, `GeoCoordinateSystemId`, `GeoCoordinateSystemDefinition`, `Latitude`, and `TimeZone` are `Public`. The accessibility question is therefore settled for 27.0.10.13; this issue re-checks it only against whatever Revit 2027 build is installed when the issue starts. Verify every member named here against RevitAPI.xml (Revit 2027) and confirm accessibility, not documentation presence alone.
- If `SiteLocation.SetGeoCoordinateSystem(string)` is confirmed callable, scope a first slice: call it with the terrain source's own resolved EPSG code, alongside the existing opt-in `ProjectPosition` write, same transaction, same gate.
- `ProjectLocation.SetProjectedSpaceToLocalTrf` actually has 6 typed parameters (`String, XYZ, Double, XYZ, Double, Double`), not the 5 `RevitAPI.xml` documents — the truly undocumented parameter sits at position 3, not the scale-factor parameter as an earlier pass guessed. Do not attempt to call this member until its accessibility and the undocumented parameter's meaning are both independently confirmed; defer the combined-scale-factor write to a later, separately-scoped decision regardless.
- Separately, decide whether to extend the existing opt-in shared-coordinates write to also set `Document.SiteLocation.Latitude/.Longitude` from a resolved point on the "find a parcel" path. Note a real plumbing gap: the dialog's resolved geocode point (`SolidGroundDialogViewModel`'s `SelectedGeocodeCandidate.Latitude/.Longitude`) is dropped before it reaches `CreateToposolidCommand` — `SolidGroundDialogResult` and `GeocodeProvenance` both carry only derived/non-coordinate data today — so this would need new plumbing, or use of the parcel polygon's centroid already in scope at the write call site (`CreateToposolidCommand.cs:931-938`). A parcel centroid and a street-address geocode pin are not the same point; the design must say which one it uses and why.
- `SharedCoordinatesDetector` (`SharedCoordinatesGate.cs:43-66`) inspects only `ProjectPosition`/survey-point/`ProjectLocations` state and never reads `SiteLocation`; decide whether an "already customized" detector is needed for `SiteLocation` too, so a later run doesn't silently overwrite an operator's own prior edit.
- Revit recalculates `TimeZone` as a side effect of setting `Latitude`/`Longitude`; leave `TimeZone` read-only in this issue unless the owner later decides otherwise.

### Acceptance criteria

- [ ] This issue's own independent reflection/metadata probe against the installed `RevitAPI.dll` reproduces the review's evidence-lens result for each of `SiteLocation.SetGeoCoordinateSystem`, `SiteLocation.GetEPSGCode`, and `ProjectLocation.SetProjectedSpaceToLocalTrf` — confirming which are actually callable by an isolated-context third-party add-in, independent of `RevitAPI.xml` doc-comment presence — before any implementation issue opens.
- [ ] The design memo states which point (parcel centroid vs. address geocode pin) a `SiteLocation` write would use, and why, and whether new plumbing is needed to carry the raw geocode point that far.
- [ ] The design memo states whether `SharedCoordinatesDetector` needs a `SiteLocation`-aware "already customized" check before any write ships.
- [ ] No implementation ships under this issue; a follow-on feature issue opens only once the owner has answered the SiteLocation/geolocation opt-in question using this issue's findings.
- [ ] The combined-scale-factor write (`SetProjectedSpaceToLocalTrf`) stays explicitly out of scope pending its own separate decision, regardless of this issue's other conclusions.

### AGENTS.md rules to keep

- Every named Revit API member is verified against RevitAPI.xml (Revit 2027) and its actual compiled accessibility confirmed, not assumed from documentation presence alone, before any implementation issue opens.
- `AGENTS.md` stays unedited: no implementation ships under this issue, and the still-open SiteLocation/geolocation opt-in question is answered by the owner, not by this research issue.

### Relationships

- Parent: Epic 4A. Blocked by: none technically, but this issue's scope is itself gated on the owner's still-open decision (whether and how to write `SiteLocation`/geocode data at all) — "owner selection" here means selecting the research, not pre-committing to ship a write.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-10 — [Phase 4] Dialog polish: visible help text, map and key-request links, pre-fetch estimate, reachability probe

**Labels:** type: feature, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Ship the cheap, safe, additive polish items this review found already sitting in the codebase's own known-limitations lists, grouped together because none touches an accuracy-affecting code path — kept last in this milestone's own priority ordering on purpose, since cheapness is not the same as importance to this owner (F24, F25, F10, F29).

### Scope

- **Visible help text (F24):** `SolidGroundDialog.cs` already writes explanatory `AutomationProperties.SetHelpText` prose for its three least-obvious controls (AOI buffer, point budget, shared-coordinates opt-in — lines 459-461, 491-494, 617-621) but that text reaches only screen readers/UI Automation clients, never a sighted user. Bind the same three strings to a visible `ToolTipService.SetToolTip` (or an added caption `TextBlock`); confirmed not to perturb `SolidGroundDialogAutomationInventory`'s `ExpectedControlCount=18` invariant.
- **Outbound map link (F25, part a):** an outbound hyperlink/button next to the confirmed address/parcel step, opening the operator's own default browser (a stateless OS shell-out, e.g. `Process.Start`, not an embedded browser) to a public map viewer centered on the resolved coordinate.
- **OpenTopography key-request link (F25, part c):** a direct hyperlink to OpenTopography's account/key-request page in `docs/revit-install-guide.md`'s key-setup section and/or the Preflight "key not set" problem text (confirmed absent from both today). A clickable in-dialog link needs Revit `TaskDialog`'s `FooterText`/`MainInstruction` (verify against RevitAPIUI.xml (Revit 2027)), not the `MainContent` slot `CreateToposolidCommand.cs` uses today; plain link text needs no new API.
- **Pre-fetch size/point-count estimate (F10):** an offline, `SolidGround.Core`-computed estimate at the dialog's Buffer/Point Budget steps, reusing `AoiNormalizer.Normalize` and `Wgs84Ellipsoid`. Explicitly decide and disclose whether the shown point count approximates the rectangular fetch envelope or the smaller buffered-polygon area.
- **Reachability probe, probe half only (F29):** a short, synchronous reachability check before Stage 2's fetch, targeting a lightweight, always-available resource other than the real, rate-limited, keyed OpenTopography endpoint. Do not add automatic retry-with-backoff for the fetch itself — that conflicts with `OpenTopographyUsgs1mSource`'s existing, twice-documented "never sends more than the two documented requests" invariant (`OpenTopographyUsgs1mSource.cs:236-237`) and is explicitly excluded here.
- **Deliberately excluded:** F25's third idea, an in-model parcel-ID `TextNote` next to the `PropertyLine` — needs real address/parcel data attached to an element to name (only exists once PH4-6 wires Extensible Storage that far) and a deliberate view-target decision (`TextNote` is view-specific; verify `View3D.IsLocked`/`TagsCannotBePlacedInUnlocked3dViews` against RevitAPI.xml (Revit 2027) before assuming an arbitrary 3D view works) — deferred to a follow-on once PH4-6 lands, not declined.

### Acceptance criteria

- [ ] All three `AutomationProperties.SetHelpText` strings are also visible to a sighted user; the existing automation-inventory test still passes unchanged.
- [ ] The map-link button opens the operator's default browser via a stateless one-way link with no embedded preview; a test confirms no WebView2/embedded-browser dependency is introduced.
- [ ] The OpenTopography key-request link is present and correct in both the install guide and Preflight text; a doc/text test asserts its presence.
- [ ] The pre-fetch estimate appears before any network call and is covered by an offline test; its envelope-vs-buffered-polygon choice is documented.
- [ ] The reachability probe runs before Stage 2's fetch, targets a resource other than the real OpenTopography endpoint, and adds no retry to the fetch itself; the existing "never more than two requests" test still passes unchanged.
- [ ] A Revit 2027 manual-evidence session confirms all three visible tooltips/captions render in both ribbon themes, the map-link button opens the operator's default browser without blocking Revit's UI thread, and the OpenTopography key-request link renders correctly in the TaskDialog's FooterText/MainInstruction slot.

### AGENTS.md rules to keep

- The map-link button stays a stateless OS shell-out, never an embedded browser/WebView2 control — the native-binary rule applies to any new dependency, not only a full map picker.
- No automatic retry is added to the real OpenTopography fetch; the existing "never sends more than the two documented requests" invariant stays intact.

### Relationships

- Parent: Epic 4A. Blocked by: none.
- Related: the deferred `TextNote` idea should be picked up as a follow-on once PH4-6 lands.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-11 — [Phase 4] Add a real WPF-binding runtime test project for future dialog defects

**Labels:** type: feature, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Close a structural verification gap Issue #31's live Revit 2027 session exposed: SolidGround.Tests cannot reference SolidGround.Revit (enforced by its own architecture test), and no test anywhere constructs a real `System.Windows.Data.Binding` against a live `SolidGroundDialogViewModel`, so a *future*, different runtime-only binding failure would still reach only a live Revit session, undetected by any offline test (F03). Three of Issue #31's four historical defects are already guarded by a general text-pattern test added in the same commit, and the fourth already has its own regression test — this issue is about the next one, not a retroactive fix.

### Scope

- Add a small, local-only, Windows-only xUnit project (`net10.0-windows`, `UseWPF=true`) that constructs the real `SolidGroundDialogViewModel` on a dedicated STA thread and attaches a real `System.Windows.Data.Binding` via `BindingOperations.SetBinding` for each property named in `SolidGroundDialog.cs`'s own `Binding(nameof(...))` calls.
- Name the access mechanism: no `InternalsVisibleTo` grant from `SolidGround.Revit` to any test assembly exists today (the only existing grant is `SolidGround.Cli` -> `SolidGround.Tests`).
- State explicitly that referencing `SolidGround.Revit.csproj` directly still transitively needs Revit 2027 installed at `$(RevitInstallDir)`, same as any local `SolidGround.Revit` build — do not claim this project is Revit-independent.
- This project must never be added to the Linux self-hosted CI job (it cannot run WPF); propose the small AGENTS.md "Debugging" dev-loop wording addition this needs (today a closed list) either as its own one-line amendment or as a fifth PH4-0 bullet — do not add it silently.
- Do not also scope extracting `SolidGroundDialogViewModel`/`SolidGroundDialogInputs` into a Revit-free, WPF-free project in this same issue: `SolidGroundDialogViewModel.cs` has a real, load-bearing WPF/Dispatcher dependency today, so that extraction needs its own separate refactor-scoping decision first.

### Acceptance criteria

- [ ] The project constructs a real binding for every dialog property bound via `Binding(nameof(...))` and fails if a bound property is not public or does not raise the expected change notification.
- [ ] Confirmed absent from the Linux self-hosted CI job's steps.
- [ ] The design note states this closes the gap for future binding defects, not that it retroactively re-catches Issue #31's four historical defects.
- [ ] The AGENTS.md "Debugging" wording addition is proposed, not silently added.

### AGENTS.md rules to keep

- The new project must never be added to the Linux self-hosted CI job, since it needs WPF and the runner cannot provide it; none of AGENTS.md's nine CI conditions changes.
- No pyRevit/Python/IronPython/MCP live-driving harness/RevitLookup/Add-In Manager dependency; the dev loop stays build/deploy/restart/verify by hash.
- `AGENTS.md` is updated only with the owner's accepted direction — the CI-wording addition this issue needs is proposed, not applied silently.

### Relationships

- Parent: Epic 4A. Blocked by: none.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-12 — [Phase 4] Add a repeatable comparison benchmark against a real 1 m USGS reference

**Labels:** type: feature, area: core, area: cli, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Make "how does SolidGround compare with similar tools" a repeatable measurement, not a one-off research run. One fixed test site, one real 1 m reference, one yardstick (worst-case and RMS vertical error), one short scorecard, and a watch list that says when to re-score. This answers the owner's 2026-09-27 request to be able to compare in the future. The 2026-09-27 research could not produce a head-to-head accuracy number: no real 1 m raster of the public example site was saved, and the committed `example-site-synthetic.asc` fixture is synthetic.

### Scope

1. **Reference file.** Capture one real USGS 1 m raster set of the public example site with the existing CLI `fetch` verb. It writes `*.asc` plus `*.prj` plus `*.source.json` (`docs/architecture/cli-workflow.md:47`). Store the set under the already git-ignored `artifacts/` folder (`.gitignore:4`); it is never committed by default. Record its SHA-256 and acquisition date in the benchmark note, so later runs can prove they use the same bytes. The capture costs two OpenTopography calls: a bare AAIGrid request plus the GeoTIFF GeoKeys request (`AGENTS.md`, "Data and numeric contracts"). Committing the reference as a fixture is a separate owner decision under the fixture-inspection rules.
2. **Yardstick.** Add a CLI capability that scores a terrain surface against the reference. It accepts either a SolidGround export bundle or an external point set: CSV `x,y,z` with a declared EPSG code and declared horizontal and vertical units, where any undeclared unit is a hard error. It reuses PH4-3's triangulate-and-interpolate measurement. It reports worst-case and RMS vertical error, the number of reference cells compared, and every coverage gap as a gap, never filled. Results state the exact unit definition: meter, U.S. survey foot, or international foot. A source that declares no vertical datum (for example Groundit's terrain) is scored as-is and labeled "vertical datum undeclared" in the result.
3. **Outside tools stay outside.** Another tool's terrain is produced in that tool's own environment and exported to the CSV point set. SolidGround reads only the exported points. No third-party code, Python, GDAL, or native binary enters this repository.
4. **Scorecard.** Add a dated benchmark note, `docs/architecture/comparison-benchmark.md`. It holds the seven-row scorecard from the 2026-09-27 research: terrain detail, safe handling of data gaps, model safety, ease of use, beyond-terrain layers, coverage, and automated tests. Each row names the evidence that scores it. The first entry records the 2026-09-27 result. Each re-score adds a new dated entry instead of overwriting.
5. **Watch list.** Record the five watched projects and how each is watched: Groundit, Mantle Place, Archi Topography for Revit, BlenderGIS, and Heron. The alert mechanism is the owner's choice and lives outside this repository, because `AGENTS.md` forbids adding tool-specific automation and the CI conditions forbid new workflow triggers. Options are GitHub "Watch, Releases" from the owner's account for the four GitHub-hosted projects, and a web-page change monitor for Archi Topography, which has no public repository. Mantle Place publishes only Unreal releases so far, so its entry also watches for a first Revit release.

### Acceptance criteria

- [ ] The reference raster set is captured once at the public example site with `fetch`, kept under `artifacts/` (git-ignored), and its SHA-256 and acquisition date are recorded in the benchmark note. No key, signed URL, or place name appears in any committed text.
- [ ] The yardstick scores both a SolidGround export bundle and an external CSV point set. It reports worst-case error, RMS error, compared-cell count, and coverage gaps in an exactly defined unit.
- [ ] Offline tests use synthetic fixtures only: a synthetic reference, plus synthetic external point sets with known injected errors, a NODATA hole reported as a gap, and a missing or mismatched unit rejected with an actionable error.
- [ ] Scoring against the real reference is opt-in only. An explicit environment flag gates it, and it skips rather than fails when the flag or the file is absent, matching the existing live-test pattern (`tests/SolidGround.Tests/CensusCountyLookupLiveTests.cs:17-26`).
- [ ] The first scorecard entry records SolidGround's own score at the default point budget. It also records Groundit's score when its terrain can be exported outside the repository; otherwise the entry says "not measured" with the reason.
- [ ] The watch list is recorded with its mechanism and start date, and nothing is added to `.github/workflows` or any other repository automation.

### AGENTS.md rules to keep

- A real OpenTopography fetch requires an explicit opt-in and the environment key. Lack of a key skips; it never fails the offline suite. The key never appears in a log, file, URL, or exception message.
- Downloaded rasters stay git-ignored by default. Use only the public example site, and never record a street address or place name for it.
- No Python, GDAL, native binary, or copied third-party code enters the repository; outside tools contribute exported points only.
- CI stays unchanged: no new workflow, trigger, action, secret, cache, or artifact storage (the nine self-hosted-runner conditions).
- Every horizontal and vertical reference in a score carries its origin label, as for every other SolidGround export.

### Relationships

- Parent: Epic 4A. Blocked by: PH4-3 (reuses its error measurement).
- Informs: PH4-4 (benchmark numbers also feed the `TinError` decision). Related: Issue #18 (a future point-cloud source would be scored with the same yardstick).

### Start condition

Owner selection required. The reference capture spends two OpenTopography calls from the owner's daily quota, and runs only after the owner starts this issue.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## [Epic] Phase 4B — Site-context layers: US building footprints, heights, and roads

**Labels:** type: epic, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Answer the owner's "show evidence first" site-context question with a bounded research-then-design-then-implementation sequence, adding US-sourced building footprints, building heights where accurately derivable, and roads to the host model — comparable in size to Phase 3, per the owner's own characterization, and gated on PH4-0's AGENTS.md amendment before any host-model context element is created.

### Scope

- Research US context data sources, licenses, and heights first (PH4-13); design the host-model element strategy second (PH4-14); implement footprints, heights, and roads as separate, appropriately-blocked children (PH4-15, PH4-16, PH4-17). This is a first-cut breakdown: PH4-15/14/15 will likely each split further (Core source, Revit host-model creation, CLI parity, provenance) once PH4-13/PH4-14 conclude, matching Phase 3's own PH3-0-through-PH3-8 granularity.
- Extend the existing NODATA-never-becomes-a-value discipline (today scoped to AAIGrid terrain) and the existing reversible-local-origin-transform/provenance-labeling rules to every context-layer point, per F16 and PH4-0's data-contract amendment — every child issue's acceptance criteria says so explicitly, not inherited silently.
- Every context-layer fixture is synthetic; no real building/road data tied to a real, identifiable location is ever committed.

### Acceptance criteria

- [ ] PH4-0's architecture-table and data-contract amendments are accepted before any implementation child (PH4-15, PH4-16, PH4-17) begins.
- [ ] PH4-13's licensing conclusions are cited by name in every later child that touches a licensed data source.
- [ ] No OSM-derived geometry enters the host model without its own explicit, separately-cited owner decision, even once PH4-0's amendment is accepted.
- [ ] Every implementation child's fixtures are synthetic and pass this repository's existing fixture-security test discipline.

### Relationships

- Blocked by: PH4-0 (implementation children only; research and design may start once selected).
- Children: PH4-13, PH4-14, PH4-15, PH4-16, PH4-17.

### Start condition

Owner selection required per child issue; PH4-0 must additionally be accepted before PH4-15/14/15 start.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-13 — [Phase 4] Research US building/road context data sources, licenses, and heights

**Labels:** type: research, area: core, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Produce a cited, licensing-checked recommendation for which US data sources feed a future building-footprint, building-height, and road context layer, before any design or implementation work begins (F11, F12, F15).

### Scope

- **Buildings:** confirm USA Structures (FEMA/ORNL) as the default footprint source — CC BY 4.0, attribution-only, no share-alike, confirmed directly against the dataset's own hosted ArcGIS item page's `licenseInfo` — served from a live, public, GeoJSON-capable ArcGIS `FeatureServer` (`.../USA_Structures_View/FeatureServer/0`, `access: public`, `maxRecordCount: 2000`), the same ingestion shape SolidGround already uses for county parcels. Confirm and document the `HEIGHT` field's real limitation: populated (meters) only for structures sourced from the dataset's own in-house NGA 133-city LiDAR layer (`SOURCE='NGA'`), null elsewhere — footprint-and-orientation is the realistic first use nationwide; height is not generally available from this source (F11).
- **Roads:** research and license-check a non-OSM candidate such as Census TIGER/Line or the USGS National Transportation Dataset — verify licenses and whether either carries usable width data before claiming anything. If no non-OSM source carries adequate width/classification data, document that honestly rather than defaulting to OSM without a separate, explicit, later decision.
- **Height (harder problem):** OSM height tags are present on under 20% of US buildings nationally, and were present on 0 of 8 buildings in a live sample at the public example site — too sparse to trust as a default (F12). The accurate alternative, deriving height from the same USGS 3DEP lidar program the terrain pipeline already depends on, depends entirely on Issue #18's own classified-point-cloud investigation; do not propose a second, less-accurate height mechanism (an OSM-tag fallback) as a substitute — cite this dependency explicitly.
- **Licensing risk:** document, with citation, that placing OSM-derived (or Microsoft/Overture, both OSM-inclusive) building/road vertex geometry directly into the host model as measurable elements is plausibly an ODbL "Derivative Database" under OSMF's Attribution Guideline (`osmfoundation.org/wiki/Licence/Attribution_Guidelines`) rather than a mere "Produced Work," which would trigger ODbL's full share-alike obligation once "publicly used" (ODbL 1.0 §4.4, `opendatacommons.org/licenses/odbl/1-0/`) — conditional on public use (§4.5(c) exempts strictly internal use), but this repository's own public, detail-narrating conventions make "never public" a fragile assumption in practice (F11).
- **Size/guard evidence:** a live Overpass sample near the public example site found 522 highway-way elements against only 8 buildings within a 300 m radius (of which 400, or 76.6%, are footway/steps classes that a typical vehicular-roads default filter excludes, leaving 122 in that narrower class set — service, pedestrian, residential, tertiary, and secondary ways), and per-feature size tags nearly absent in practice (0% width, 1.3% lanes on highways) — real evidence that any future road layer needs its own pre-fetch size guard sized to whichever class set it actually queries, plus a class-informed width/height estimate rather than trusting a per-feature tag (F15). This issue's recommendation must state explicitly whether the road layer includes or excludes footway/steps classes, since that decision changes the guard's sizing evidence by roughly 4x.

### Acceptance criteria

- [ ] A written recommendation names the buildings source (USA Structures) and states its exact height-field limitation (NGA-133-city-only) rather than implying nationwide height coverage.
- [ ] A written recommendation names a road source candidate (or states that none avoids both the OSM-license question and a width-data gap), with citations and retrieval dates.
- [ ] The height dependency on Issue #18 is stated explicitly, including that no implementation begins on either #18 or a height feature without the owner's own separate scope approval.
- [ ] The ODbL/share-alike risk is documented with its actual conditional trigger (public use), not an unconditional one.
- [ ] The Overpass size-guard evidence (522 vs. 8 at 300 m) is cited as the basis for PH4-17's pre-fetch guard requirement.

### AGENTS.md rules to keep

- Every candidate source's license and any share-alike/attribution obligation is documented with citation before a later child relies on it; no OSM-derived geometry is authorized for the host model by this research alone.
- No Python/GDAL/native-binary dependency; a bare Overpass/FeatureServer HTTP call this research reproduces stays read-only and managed.

### Relationships

- Parent: Epic 4B. Blocked by: none (research does not itself place data in the host model, so it is not blocked by PH4-0).
- Blocks: PH4-14, PH4-15, PH4-16, PH4-17.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-14 — [Phase 4] Design the host-model context-layer element strategy

**Labels:** type: research, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Decide, before any implementation code is written, how context-layer elements are represented, transacted, and made reusable/undoable in the host model — matching the owner's explicit "host model, not linked" direction, unlike Groundit's separate-linked-document design (F13).

### Scope

- Adopt a chunked-transaction pattern (not one giant transaction, and not Groundit's own confirmed nested-transaction bug in its "Update Site" flow) — mantleplace-dcc's 200-elements-per-transaction precedent is the closest working example, though even it assigns no `Workset` to anything it creates; decide whether SolidGround should.
- Decide the element category: `DirectShape`/`BuiltInCategory.OST_GenericModel` for buildings (matching both Groundit's and mantleplace-dcc's independent convergence, avoiding `OST_Mass`'s hidden-by-default-view bug both separately flagged) — verify `OST_GenericModel` against RevitAPI.xml (Revit 2027); note it is not individually documented as an enum entry there (only present as a literal metadata string in the installed `RevitAPI.dll`) and confirm its usability by a direct compiled probe, not doc presence alone. For trees/vegetation, if ever added, prefer native `Planting` `FamilyInstance`s (`Document.Create.NewFamilyInstance(XYZ, FamilySymbol, Level, StructuralType)` — verify against RevitAPI.xml (Revit 2027); confirmed reachable only via an inherited `Autodesk.Revit.Creation.ItemFactoryBase` cross-reference, not a direct `Document`-class entry) with `DirectShape` as an explicit fallback when a family fails to load.
- Generalize the reuse/refuse/create identity guard this milestone also proposes for the toposolid path (PH4-2, or the underlying F01 design if PH4-2 has not yet shipped) to context-layer elements, so a second import doesn't silently duplicate buildings/roads either.
- Decide visibility/undo/provenance/attribution mechanics: every created context element must carry a source/attribution tag reachable without opening a separate document; say which mechanism is chosen (Extensible Storage entity vs. a parameter breadcrumb) and why it satisfies PH4-13's licensing conclusions — a bare id breadcrumb is not, by itself, OSMF-compliant attribution text if OSM-derived data is ever used.
- Explicitly reject embedding a live map/browser control for any preview surface — blocked on two independent AGENTS.md grounds, not one: the general dependency-policy rule ("The agent must not add a package that loads native binaries into the Revit process") and, separately, the isolated add-in context's own rule that SolidGround.Revit "must bundle only managed dependencies" (F14); if a visual preview is ever wanted, the compliant shape is a from-scratch, code-behind-only WPF vector rendering of geometry `SolidGround.Core` already resolves, no basemap imagery.

### Acceptance criteria

- [ ] The design memo names the exact category/creation API for buildings and (if ever built) trees, each flagged "verify against RevitAPI.xml (Revit 2027)" and each independently confirmed callable by a direct compiled probe, not documentation presence alone.
- [ ] The chunked-transaction size and any workset decision are stated explicitly, with a reason.
- [ ] The reuse/refuse/create guard's extension to context-layer elements is specified concretely enough for PH4-15/14/15 to implement without a second design pass.
- [ ] The attribution mechanism is named and checked against PH4-13's licensing conclusions before this issue closes.
- [ ] The memo states, in writing, that no embedded map/browser control is proposed, and why.

### AGENTS.md rules to keep

- No embedded map/browser control: blocked on the general native-binary rule and, separately, the isolated add-in context's managed-dependencies-only rule.
- Every named Revit API member is flagged "verify against RevitAPI.xml (Revit 2027)" and independently confirmed callable by a direct compiled probe, not assumed from documentation presence alone.

### Relationships

- Parent: Epic 4B. Blocked by: PH4-13. Its conclusions assume PH4-0's architecture-table amendment is accepted before implementation follows, but drafting the design itself may proceed once selected.
- Benefits from (not blocked by) PH4-2: if PH4-2 has already shipped when this design work starts, generalize its actual implementation; if not, design from F01's specification directly rather than waiting.

### Start condition

Owner selection required.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-15 — [Phase 4] Implement building footprints from USA Structures

**Labels:** type: feature, area: core, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Add a first, lower-risk context layer: US building footprints from USA Structures, following PH4-14's element strategy (F11).

### Scope

- New pluggable Core source (mirroring `IElevationSource`/`IParcelBoundarySource`) reading USA Structures' public `FeatureServer` GeoJSON output for footprint geometry, `SOURCE`, and (where populated) `HEIGHT`/adjacent-elevation fields; label every returned footprint with its source and, when `HEIGHT` is null, an explicit "not available for this structure" flag — never a fabricated or defaulted value (F16).
- Host-model creation follows PH4-14's chosen category/chunking/identity-guard/attribution design exactly; no ad hoc deviation.
- CC BY 4.0 attribution text is shown in the dialog once per run (not only in logs) and stored in provenance, per PH4-13's licensing conclusions.
- A synthetic, fabricated fixture only — no real building footprint tied to the public example site's real structures is committed; the fixture is shaped like USA Structures' schema but invented.

### Acceptance criteria

- [ ] Footprints resolve for a synthetic offline fixture end to end (source query to host-model elements), covered by an offline test.
- [ ] A missing/null `HEIGHT` is recorded as "not available," never fabricated, asserted by a test.
- [ ] CC BY 4.0 attribution is asserted present in both the dialog and provenance export by a test.
- [ ] A Revit 2027 manual-evidence session confirms chunked-transaction creation, the reuse/refuse/create guard firing on a repeat run, and survives save/reopen.
- [ ] The fixture-security test discipline (no real, identifiable structure data) passes before the fixture is committed.

### AGENTS.md rules to keep

- The fixture is synthetic and fabricated; no real building footprint tied to a real, identifiable location is ever committed.
- A sentinel must never become a value: a missing/null `HEIGHT` is recorded as "not available," never fabricated or defaulted.

### Relationships

- Parent: Epic 4B. Blocked by: PH4-0, PH4-13, PH4-14.

### Start condition

Owner selection required; PH4-0 must be accepted first.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-16 — [Phase 4] Implement building heights linked to Issue #18's classified point-cloud source

**Labels:** type: feature, area: core, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Add real, measured building heights (top-of-building lidar return minus the already-fetched bare-earth DEM) where Issue #18's classified ground-return/point-cloud capability makes that derivation possible — not an OSM-tag-based fallback, since OSM height tags are too sparse to trust nationally (F12).

### Scope

- Depends entirely on Issue #18 reaching its own separately-approved implementation scope; this issue applies that future capability to buildings, it is not an independent height mechanism.
- If Issue #18 is not yet implemented when PH4-15 ships, footprints ship without height (matching USA Structures' own real limitation) rather than blocking on this issue or fabricating a height from OSM tags or a fixed per-story estimate.
- When available, height is computed as first-lidar-return minus bare-earth DEM at each footprint, flagged with its own measured-vs-unavailable state per building — never a silent guess.

### Acceptance criteria

- [ ] This issue's acceptance criteria are not satisfiable until Issue #18 has its own accepted implementation scope; until then this issue stays open and blocked, not silently reinterpreted into an OSM-tag fallback.
- [ ] When implemented, a per-building height carries an explicit measured/unavailable flag; no default or averaged height is ever presented as measured.
- [ ] A synthetic fixture (invented point-cloud-plus-footprint data, not tied to any real location) covers both the measured and unavailable cases.

### AGENTS.md rules to keep

- No implementation begins on Issue #18 or this issue without the owner's own separate, explicit scope approval.
- The fixture is synthetic, invented point-cloud-plus-footprint data; nothing is tied to a real, identifiable location.

### Relationships

- Parent: Epic 4B. Blocked by: PH4-0, PH4-15, and Issue #18 reaching its own owner-approved implementation scope.

### Start condition

Owner selection required; additionally blocked on Issue #18's own separate scope approval (see the proposed #18 changes below).

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## PH4-17 — [Phase 4] Implement a roads context layer

**Labels:** type: feature, area: core, area: revit, status: blocked
**Milestone:** Phase 4: Accuracy, integrity, geolocation, and site context

### Outcome

Add a roads context layer using whichever source PH4-13 recommends, with an explicit pre-fetch size guard this review's own live evidence shows is necessary (F15).

### Scope

- Use PH4-13's recommended road source; if that recommendation is OSM/Overpass (because no non-OSM source has adequate width/classification data), this issue additionally needs PH4-0's Groundit-clause amendment accepted with OSM-for-roads specifically in scope, not just the general architecture-table change — state this dependency explicitly.
- Add an explicit pre-fetch guard (a hard per-layer element cap and/or query-level size hint checked before parsing, not only post-fetch truncation) — sized using whichever figure PH4-13's recommendation selects (522 total highway ways, or 122 restricted to a typical vehicular-roads class set, at a 300 m radius near the public example site), not a bare, unqualified "522."
- Default to a class-informed width/classification estimate rather than trusting a per-feature tag, since width tags were present on 0% of sampled highways and lane tags on only 1.3%; flag any estimate as an estimate, never a measured width.
- Host-model creation follows PH4-14's element strategy; attribution follows PH4-13's licensing conclusions for whichever source is actually used.

### Acceptance criteria

- [ ] The pre-fetch guard is checked before any large parse, covered by a test using a synthetic oversized fixture.
- [ ] Every road element's width/classification is flagged measured vs. estimated; no estimated value is presented as measured.
- [ ] If the chosen source is OSM-based, the issue's Start condition names PH4-0's OSM-for-roads-specific acceptance as a precondition, not just the general architecture-table amendment.
- [ ] A synthetic, non-real-location fixture covers the offline test path; a Revit 2027 manual-evidence session confirms chunked creation and the guard firing on an oversized AOI.

### AGENTS.md rules to keep

- If the recommended source is OSM/Overpass, this issue does not proceed without PH4-0's OSM-for-roads-specific disposition accepted first, not just the general architecture-table amendment.
- The fixture is synthetic and non-real-location; every estimated (as opposed to measured) width/classification value is flagged as an estimate, never presented as measured.

### Relationships

- Parent: Epic 4B. Blocked by: PH4-0, PH4-13, PH4-14; additionally by PH4-0's OSM-for-roads-specific disposition if PH4-13 recommends OSM.

### Start condition

Owner selection required; PH4-0 must be accepted first, with the roads-specific disposition resolved if the recommended source needs it.

Repository contract: [AGENTS.md](https://github.com/mjfrancese/SolidGround/blob/main/AGENTS.md).

---

## Proposed changes to existing Issue #18 (not a new issue)

Today: title "[Future] Evaluate a classified ground-return point-cloud source"; labels `area: core, future, status: blocked, type: research`; no milestone; blocked by #17; start condition "This is post-MVP work. Do not start until Phase 2 is accepted and the owner explicitly chooses to investigate the point-cloud source."

Proposed changes, for the owner to apply or decline:

- **Milestone:** add "Phase 4: Accuracy, integrity, geolocation, and site context" (today has none).
- **Labels:** keep `type: research`, `area: core`. Consider dropping `future` now that the owner has moved this from "someday" into an active Phase 4 investigation track (2026-09-27). Keep `status: blocked` until the owner reviews this proposal; flip it to `status: ready` in the same action as accepting the rest of this file, not before.
- **Scope addendum** (append, do not replace, the existing Scope bullets): this redirection has not been reconciled against Issue #18's own existing, unedited scope item 1 ("Verify current OpenTopography point-cloud endpoints, output formats, classification guarantees, licensing, and key requirements"): whether OpenTopography itself already re-hosts this same 3DEP data as EPT/COPC has not been checked by this research and must be the investigation's first step, not a settled premise that bypasses OpenTopography entirely. Subject to that reconciliation, point the investigation at reading **EPT/COPC over USGS's own public `s3://usgs-lidar-public` bucket**, not raw LASzip over the Requester-Pays `s3://usgs-lidar` bucket, and not a generic "accept any user-uploaded LAS/LAZ file" design (the model goto.archi's commercial add-in uses). Record that the one from-scratch managed (zero-P/Invoke) LAZ codec found in this research, `shintadono/laszip.net` — note its only released/NuGet form (the `master` branch) cannot open a compressed LAS 1.4 (LAZ) file at all; only its unreleased, untagged `develop` branch can, and that is the branch the three bug-fix commits and the still-unfixed third bug both refer to — has three confirmed bug-fix commits in exactly its LAS 1.4 classification/RGB+NIR decompression path; only two are present in the actively-maintained fork (`Unofficial.laszip.netstandard`) a real shipping open-source add-in (Heron) depends on, and the third (a 2025 RGB+NIR buffer-length bug) is still unfixed there. `bertt/copc` is a real, unverified (0-star), not-yet-deep-dived MIT/.NET 8 candidate that could sidestep this dependency question if it proves viable, and should be evaluated on its own merits, not adopted sight-unseen. USGS's own AWS distribution's access terms are separate from OpenTopography's academic/enterprise-key DEM gate — confirm this distinction directly before assuming either bucket needs a key (F18).
- **Start condition:** replace "Do not start until Phase 2 is accepted and the owner explicitly chooses to investigate the point-cloud source" with: "Phase 2 is accepted (2026-09-25). The owner explicitly chose to investigate this source on 2026-09-27. The investigation may begin; no implementation begins without a separate, later owner-approved scope, unchanged from this issue's existing acceptance criteria."
- **Relationships:** add "Related: PH4-16 (Phase 4) depends on this issue reaching its own approved implementation scope before building measured building heights."

Acceptance criteria for this proposed change itself:

- [ ] The owner reviews and either accepts, edits, or rejects each bullet above before Issue #18 is edited.
- [ ] None of #18's own existing acceptance criteria (no implementation without a separate owner-approved scope; all-managed, no native binaries) are weakened by this addendum.

---

## Not drafted as Phase 4 issues this round

These findings support real ideas but are intentionally left out of this draft; the review already places most of them in "Consider later" or "Considered and declined." Listed here so the owner sees the full picture in one place.

- **F04 (acquisition disk cache):** real value, but ties directly to the still-open acquisition-caching question the owner's 2026-09-27 decisions explicitly left open. Draft only once that question is answered; a cache's key/staleness design depends on the answer.
- **F09 (a shipped rollback-only diagnostic probe command):** refuted by both verification lenses — SolidGround already runs this exact style of probing through a throwaway, out-of-repository add-in, and shipping one permanently would contradict this repository's own recorded "no probe or test-only code ships in the add-in" decision.
- **F20 (manually-drawn high-density zones):** real but modest value for a single user; not urgent; no dependency on anything else in this milestone.
- **F22 (a per-datum identity check for non-NAD83 `.prj` input):** the gap is real, but the specific fix this review evaluated (a literal 4-name allow-list) would falsely reject genuinely-NAD83 `.prj` files this repository's own committed tests already parse correctly. Needs a dialect-aware redesign before it is issue-ready; `VerifyCommand` already prints the parsed datum today as a cheap, existing partial mitigation.
- **F26/F27 (a third externally-anchored coordinate-transform test; an independent GeoTIFF GeoKey cross-check):** real and worthwhile, but each already has a cheaper, already-identified remedy recorded in this repository's own architecture notes — better done opportunistically than as a scheduled Phase 4 issue.
- **F28 (unattended, journal-replay Revit smoke testing):** ties directly to the still-open "no live-driving harness" question the owner's 2026-09-27 decisions explicitly left open. The underlying journal-playback API surface is real and documented, but building on it needs the owner's own explicit research decision and an AGENTS.md amendment first — a process/tooling decision, not a routine issue.
- **F30 (a reflection test forcing a future elevation source through "the same" null-propagation contract):** declined by both lenses on re-verification — the real protection is already structural plus an existing regression test, and there is no single "same" contract shape to check once a second data shape (a future point cloud) exists.
- **F31-F38 (confirmatory/parity notes: decimation never fabricates points unlike Groundit's density inflation, with F31's own idea limited to a future regression test once a pluggable source lands under Issue #18, not a Phase 4 item on its own; SolidGround's typed shared-coordinates mechanism already exceeds every comparator; terrain stays in the host document already, matching the owner's own direction; SolidGround's transaction/rollback discipline already exceeds every comparator; SolidGround already declares NAVD88 explicitly where Groundit never declares any vertical datum; foot-unit modeling; boundary-retention design; architecture-boundary testing):** no Phase-4-scheduled action proposed by the review itself; these document existing SolidGround strengths, not gaps.
- **F39 (OSM "Simple 3D Buildings" massing detail):** conditional on a future building-massing (not just flat footprint) feature well beyond PH4-15's scope; revisit only if the owner later wants more than flat extrusion.
- **F40 (nearby-parcel-tier status note):** the "geocoded point misses every parcel" UX gap this dimension would otherwise flag is already closed by committed work (the nearby-parcel fallback merged for Issue #31); a status note, not an action item.
- **Multi-version Revit support, a coarser/keyless fallback elevation source, and a separate linked site model:** explicitly ruled out by `AGENTS.md` and the owner's 2026-09-27 direction; no finding proposed any of these, and none needs a Phase 4 issue.

---

## Dependency and ordering

- PH4-0 (the gate) has no hard technical dependency and can start immediately; it does not need to precede Epic 4A's children, which fit inside today's already-accepted terrain/toposolid/CRS boundary.
- Within Epic 4A: PH4-1 and PH4-2 are independent of each other and of everything else in the epic, but share a scheduling opportunity (a live-evidence session, and possibly a combined schema bump for PH4-2 and PH4-6). PH4-3 must land, with real numbers, before PH4-4 starts. PH4-12 (the comparison benchmark) also needs PH4-3, because it reuses that error measurement. PH4-8 benefits from PH4-7 landing first (a second projection method to prove genericness against) but is not blocked by it. PH4-9 is gated on the owner's own still-open SiteLocation/geolocation decision, not on any other Phase 4 issue. PH4-5, PH4-6, PH4-10, and PH4-11 are each independent of the rest of the epic.
- Epic 4B is strictly sequential at the top: PH4-13 (research) before PH4-14 (design) before any of PH4-15/14/15 (implementation). All three implementation children are additionally blocked by PH4-0's acceptance. PH4-16 further depends on Issue #18 reaching its own approved scope. PH4-17 may additionally depend on PH4-0's disposition toward OSM specifically, if PH4-13 recommends an OSM road source.
- The proposed Issue #18 changes are independent of everything else and can be applied as soon as the owner reviews them; they do not require PH4-0.
- Suggested order if the owner wants one: PH4-1 and PH4-2 first (closes the two highest-ranked, already-scoped model-integrity gaps); PH4-3 next (needed before PH4-4 can even be considered); PH4-0 and PH4-13 can run in parallel with the above, since neither blocks nor is blocked by Epic 4A; PH4-14 once PH4-13 and PH4-0 are both settled; the remaining Epic 4A items (PH4-5 through PH4-11, and PH4-12 once PH4-3 has landed) fit wherever convenient.

## Table of all drafted issues

| ID | Title | Labels | Blocked by |
|---|---|---|---|
| PH4-0 | Propose AGENTS.md amendments for Phase 4 (proposal only) | documentation, type: gate, area: core, status: blocked | none |
| — | [Epic] Phase 4A — Model integrity, measured accuracy, and CRS/geolocation completeness | type: epic, area: revit, status: blocked | none |
| PH4-1 | Verify individual vertex position/elevation after Toposolid.Create | type: feature, area: revit, status: blocked | none |
| PH4-2 | Add a reuse/refuse/create duplicate-run guard with copied-element provenance detection | type: feature, area: revit, status: blocked | none |
| PH4-3 | Measure and report the simplifier's achieved worst-case/RMS vertical error | type: feature, area: core, area: cli, status: blocked | none |
| PH4-4 | Decide SimplificationMethod.TinError: greedy TIN vs. geometry3Sharp QEM | type: research, area: core, status: blocked | PH4-3 |
| PH4-5 | Record coverage-floor fraction; investigate collection-date provenance | type: feature, area: core, status: blocked | none |
| PH4-6 | Wire address/parcel provenance into a new Extensible Storage schema version | type: feature, area: core, area: revit, status: blocked | none |
| PH4-7 | Add a US State Plane/Lambert Conformal Conic process-mode fixture | type: feature, area: core, status: blocked | none |
| PH4-8 | Compute and display grid convergence and point scale factor | type: feature, area: core, area: revit, status: blocked | none (benefits from PH4-7) |
| PH4-9 | Research SiteLocation CRS-identity and geocode/time-zone writes | type: research, area: revit, status: blocked | none (gated on an open owner decision) |
| PH4-10 | Dialog polish: visible help text, map/key links, pre-fetch estimate, reachability probe | type: feature, area: revit, status: blocked | none |
| PH4-11 | Add a real WPF-binding runtime test project for future dialog defects | type: feature, area: revit, status: blocked | none |
| PH4-12 | Add a repeatable comparison benchmark against a real 1 m USGS reference | type: feature, area: core, area: cli, status: blocked | PH4-3 |
| — | [Epic] Phase 4B — Site-context layers: US building footprints, heights, and roads | type: epic, area: revit, status: blocked | PH4-0 (implementation children only) |
| PH4-13 | Research US building/road context data sources, licenses, and heights | type: research, area: core, status: blocked | none |
| PH4-14 | Design the host-model context-layer element strategy | type: research, area: revit, status: blocked | PH4-13 |
| PH4-15 | Implement building footprints from USA Structures | type: feature, area: core, area: revit, status: blocked | PH4-0, PH4-13, PH4-14 |
| PH4-16 | Implement building heights linked to Issue #18 | type: feature, area: core, area: revit, status: blocked | PH4-0, PH4-15, Issue #18 |
| PH4-17 | Implement a roads context layer | type: feature, area: core, area: revit, status: blocked | PH4-0, PH4-13, PH4-14 |
| (#18) | Proposed changes only — not a new issue | existing: area: core, future, status: blocked, type: research | n/a |

## Drafting notes

- Treated the review's single "CRS and geolocation" grouping as three separate issues (PH4-7, PH4-8, PH4-9) rather than one omnibus issue, because the underlying review ranks and scopes them separately (different effort, different blocking) and this repository's own Phase 3 precedent is one issue per schedulable unit.
- Scoped PH4-0 (the gate) narrowly to what Epic 4B and the Issue #18 addendum actually need (architecture-table rows, the Groundit-clause rewrite, and context/point-cloud data-contract sentences), and stated explicitly that Epic 4A does not depend on it — tying Epic 4A to a site-context-specific amendment would block it needlessly.
- The review's two verification passes disagreed over whether `SiteLocation.GetEPSGCode`/`ProjectLocation.SetProjectedSpaceToLocalTrf` are public; a direct metadata read on 2026-09-27 settled it (both are compiled `internal`). PH4-9 stays a research issue because its geolocation half still depends on the open owner question about opt-in design.
- Combined F19+F17 into one issue (PH4-5) and F24+F25(partial)+F10+F29(partial) into one issue (PH4-10), matching how the review itself already grouped these for ranking; kept F01/F02/F06/F07/F08 and the SiteLocation research as separate issues since the review scores and blocks each differently.
- Added PH4-11 for F03 (an evidence-lens "adopt-phase4"/high-value finding the earlier draft omitted entirely from both the drafted issues and the "Not drafted" accounting), scoped to the fit-lens's corrected framing: only the standalone WPF-binding test project (idea part (a)); the view-model extraction (idea part (b)) is not drafted, since it needs its own production-code refactor first.
- Did not draft F04, F09, F20, F22, F26, F27, F28, F30, F31-F40 as issues; see "Not drafted as Phase 4 issues this round" for why, tied explicitly to the owner's own still-open questions where relevant.
- For Issue #18, followed the task instruction literally: proposed edits to the existing issue, not a new issue.
- Epic 4B is specified only to its first research/design/implementation cut, consistent with the owner's own "comparable in size to Phase 3" characterization implying further children will follow once PH4-13/PH4-14 conclude, rather than fabricating detailed sub-issues for work that has not been scoped yet.

## Sources

1. This research batch's findings register (F01-F40), both verification lenses, corrected claims applied where a verdict was partially-confirmed, refuted claims excluded.
2. Repository paths cited above are read at commit `ea96afb` (`git show HEAD:<path>`), the repository's `HEAD` at drafting time.
3. [OSMF Attribution Guideline — Derivative Database test](https://osmfoundation.org/wiki/Licence/Attribution_Guidelines)
4. [Open Database License 1.0](https://opendatacommons.org/licenses/odbl/1-0/)
5. USA Structures dataset item metadata and FeatureServer schema (ArcGIS-hosted; `licenseInfo`: CC BY 4.0; retrieved 2026-09-27 by this research batch).
6. [hobuinc/usgs-lidar](https://github.com/hobuinc/usgs-lidar) — public EPT bucket vs. Requester-Pays LASzip bucket.
7. `gh issue view 18`, `gh issue view 26`, `gh issue view 31`, `gh issue view 34`; `gh label list`; `gh api repos/mjfrancese/SolidGround/milestones` — retrieved 2026-09-27 for label, milestone, and format precedent.
8. `docs/planning/phase-3-draft-issues.md` at `HEAD` — format and phase-gate-convention precedent.
