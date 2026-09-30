# Phase 4A combined implementation plan

Date: 2026-09-30
Status: approved implementation plan for #40, #43–#54, #60, and linked #36/#38. Source implementation and automated verification are in progress. The public-reference benchmark is recorded; native Revit acceptance remains pending.

## Purpose and boundaries

Phase 4A closes integrity, measurable-accuracy, CRS, provenance, dialog, and repeatable-comparison gaps without changing the product boundary: Revit 2027/.NET 10, a Revit-free Core, managed dependencies only, and USGS 1 m through OpenTopography. The approved #60 design specification is the UI contract: [revit-60-usability.md](../design-specs/revit-60-usability.md). The PRD remains the usability acceptance contract: [prd-revit-usability.md](prd-revit-usability.md).

All fixtures, examples, screenshots, and committed inputs use synthetic data or the already-approved public example site. No real address, local filesystem path, API key, authorization header, signed URL, or downloaded raster is committed. No Python, GDAL, native geospatial binary, WebView, embedded map, new default parcel provider, background document mutation, or automatic save is in scope.

`SimplificationMethod.TinError` (#46) and SiteLocation/geolocation (#51) are decision-only. Neither issue authorizes an algorithm implementation or a SiteLocation write. A result that does not justify either feature is a valid closeout.

## Combined scope and non-negotiable contracts

| Work | Delivered result | Contract retained |
| --- | --- | --- |
| #43 vertex verification | A post-create injective, tolerance-bounded comparison of expected points to `SlabShapeEditor.SlabShapeVertices`, with location/elevation tolerances and an actionable failing vertex report | It runs inside the existing transaction path; any discrepancy rolls back the whole creation. Aggregate count/bounds checks remain as cheap preliminary checks. |
| #44 duplicate-run guard | Read-only pre-transaction decision: `Create`, `Reuse`, or `Refuse`, including stale and multi-match outcomes | Same logical terrain never silently creates a second Toposolid/PropertyLine. A copied element is reported as stale/ambiguous, never accepted as the original. |
| #45 measured error | Opt-in Core/CLI diagnostic that triangulates retained exact samples and reports maximum absolute and RMS vertical residual plus coverage gaps | NODATA and uncovered cells are reported, never interpolated or silently excluded. Ordinary Revit creation incurs none of this cost unless explicitly requested. |
| #46 decision | Dated memo choosing no work, Candidate A (new exact-sample greedy TIN), or Candidate B (geometry3Sharp QEM) after #45 results | No candidate code or dependency is introduced by #46. A package decision requires exact pin, notice, and a separate implementation issue. |
| #47 provenance research | Export and log the actual `coverageFloorFraction`; publish an OpenTopography collection-date feasibility memo | A missing collection date becomes an explicit “not reported by this source” value, never an HTTP-date guess. A feasible catalog path is a later issue, not an unreviewed extra HTTP call. |
| #48 on-element provenance | Address/parcel provenance reaches Extensible Storage through the combined v2 schema | Existing v1 entities remain readable and untouched; secrets and private fixture data never enter fields. |
| #49/#50 CRS completeness | Public State Plane LCC process fixture and Core convergence/point-scale calculation, displayed only once authoritative CRS metadata exists | Fetch stays UTM-only. `ProjectPosition.Angle` remains `0d`; this makes approximation visible and does not change shared-coordinate behavior. |
| #51 research | Revit 2027 API accessibility and policy memo for CRS identity and optional geolocation | No `SiteLocation` or combined-scale-factor write, and no owner choice is presumed. |
| #52/#53/#60 usability | Approved Location → Parcel → Review flow; Settings; bounded UI adapters; accessibility/state coverage; real binding lane | Find makes no elevation request or document change. Legal parcel/PropertyLine stay unbuffered; only terrain/fetch extent grows. Preflight remains authoritative. |
| #54 benchmark | Opt-in reference capture and comparison yardstick, synthetic offline coverage, dated scorecard/watch list | Outside tools supply only exported CSV. Real raster capture remains git-ignored and is never silently replaced by synthetic input. |
| #36/#38 | Correct containment wording and settings-relative input resolution | “Contains this point” is distinct from nearby distance; all related relative paths share the documented settings-directory rule. |

## Dependency order and reviewable increments

The increments are ordered to minimize simultaneous edits to `CreateToposolidCommand`. The command remains orchestration only. New services expose bounded, Revit-free or narrow-Revit APIs; it must not absorb triangulation, entity scanning, schema serialization, WPF persistence, or coordinate analytics.

| Increment | Issues | Main ownership and bounded seams | Prerequisites / completion gate |
| --- | --- | --- | --- |
| A. Contracts and small Core corrections | #36, #38, #47 coverage floor, #49 | `ParcelBoundaryProximity` display formatter; settings-path resolver passed the settings-file directory; `SimplificationRequest`/export model records coverage floor; public LCC fixture and transform tests | Offline tests and documentation pass. No Revit runtime evidence required for this increment. |
| B. Independent accuracy measurement | #45 | `TerrainErrorAnalyzer` consumes a clipped full grid plus retained samples and returns a typed `TerrainErrorReport`; CLI owns rendering/exit policy | A’s export contract is settled. Tests prove triangle interpolation, holes, concavity/convex-frame exclusions, ridges, swales, edges, determinism, and unit labeling. |
| C. Provenance and integrity transaction safeguards | #43, #44, #48 | `PostCreationVerification` receives an expected-point set; `ExistingTerrainScanner` returns `ExistingTerrainDecision`; `TerrainIdentity` is Core-only; one `ProvenanceSchemaV2Adapter` performs entity mapping | Verify named 2027 API members before edits. Offline decision/schema tests then one shared signed/deployed Revit session for normal, duplicate, copy, stale, collision, corrupt-vertex, save/reopen paths. |
| D. CRS observability and research | #50, #51, #47 collection-date research | Core `ProjectionCharacteristics` accepts `IHorizontalCoordinateTransform`; a post-acquisition presentation model owns display. Research memo owns API metadata findings | #49 helps validate LCC but does not block #50. #51 ends at owner decision; #47 ends at cited recommendation. |
| E. Usability foundation | #53 then #60 settings/shell/styles, followed by #52 and flow changes | Windows local WPF test project; common controls/resources; `SettingsService`; `LocationWorkflow`/`ParcelWorkflow` adapters; `TerrainExtentPlan` separates legal, terrain, and fetch geometry | The accepted policy amendment governs settings/key behavior. Binding lane is Windows local only and absent from Linux CI. |
| F. Legal-boundary and creation-flow integration | #60, #36, #38, plus #44 decision presentation | The command receives an immutable `CreateTerrainRequest` containing confirmed geometry and effective preferences; it invokes Preflight and bounded adapters | Offline geometry tests first, then Revit 2027 manual evidence for property-line invariance, cancellation, Undo, provenance, threshold guard, and recovery. |
| G. Comparison and decision closeout | #54 then #46 | CLI scorer uses `TerrainErrorAnalyzer`; `comparison-benchmark.md` has append-only dated entries. #46 consumes reports and benchmark results | #45 complete. Real reference capture is separately opt-in and credential-gated. #46 closes with a decision memo only. |

Increments C and F are serialized where they alter command orchestration. Work within A, B, and D may proceed independently once their interfaces are agreed. A clean package/release is not implied by any increment; it follows the repository’s ordinary signed-build, deploy, restart, hash, and Revit evidence process.

## Design decisions

### One combined Extensible Storage v2 schema (#44 + #48)

Create one new self-contained schema with a newly minted GUID at implementation time, a distinct name such as `SolidGround_Provenance_Toposolid_V2`, explicit version `2`, `AccessLevel.Public` read and `AccessLevel.Vendor` write. This plan selects the self-contained-v2 policy: the v1 schema and all v1 entities remain immutable/readable through a legacy adapter, and every new terrain writes v2 only. It does not write a v1 entity plus an additive identity entity. A single combined change avoids a v2→v3 churn when duplicate detection and address/parcel round-trip are scheduled together.

V2 repeats v1’s 36 required provenance fields so each new entity is self-contained, then adds the 17 address/parcel and source-attribution fields already defined by Issue #33:

`hasAddressParcel`, `addressParcelRetrievalDateIso`, `hasGeocode`, `geocodeProvider`, `geocodeQueryText`, `geocodeAttribution`, `hasParcel`, `parcelSourceKind`, `parcelSourceIdentity`, `parcelId`, `hasStableParcelId`, `stableParcelId`, `hasLegalDescription`, `legalDescription`, `parcelLicenseDisclaimerText`, `hasSourceAttribution`, and `sourceAttribution`.

It also adds the two #47 provenance fields, `coverageFloorFraction` (a Number-spec double) and `collectionPeriodAvailability` (`reported` or `notReportedBySource`, independent of the retained v1-compatible `hasCollectionPeriod`/dates), plus these guard fields:

| Field | Representation and purpose |
| --- | --- |
| `terrainIdentityVersion` | Nonempty version token, initially `terrain-identity-v1`, so later canonicalization changes do not masquerade as matches. |
| `terrainIdentityKind` | `stableParcel`, `bbox`, `radius`, or `polygon`; makes fallback behavior visible. |
| `terrainIdentityStem` | SHA-256 of canonical identity material. For a resolved parcel it includes the source identity and stable parcel id; for other AOIs it includes canonical WGS84 bounds/radius or normalized polygon topology. Raw source text, operator address, and filesystem path are excluded. |
| `terrainContentSignatureAlgorithm` | Initially `sha256-canonical-run-v1`. |
| `terrainContentSignature` | SHA-256 of canonical legal geometry, terrain-extension setting, CRS/reference identity, output-unit definition, simplification request, and source/dataset identity. It distinguishes a changed proposed run from an exact repeat without storing mutable UI text. |
| `storedOriginalUniqueId` | The source element's Revit `UniqueId` at entity attachment. It is a copy/staleness signal only, never the identity itself. |
| `storedOriginalDocumentCreationGuid` | The creating document's `CreationGUID`. It distinguishes a copied element in a new document from a Save As of the same document history. |
| `terrainPointFrameHash` | SHA-256 of the canonical final expected point set and complete local-coordinate frame/origin. It detects a changed raster or point realization even when settings text and parcel identity are unchanged. |

Decision rules are deterministic:

`terrainPointFrameHash` is calculated from the actual completed pipeline output before creation, not from settings alone. Its canonical serializer, ordered fields, culture-invariant numeric format, topology/ring normalization, and test vectors are Core contract code. Entity scanning reads v2 for matching and inventories v1 through a legacy adapter. A Save As can reuse only when document history and every remaining identity check agree. An element copied into a new document refuses on `CreationGUID` mismatch. Before implementation, verify `Document.CreationGUID` against the installed Revit 2027 SDK XML and compiled public accessibility. The mapping carries only address/parcel identity and attribution fields already owned by `AddressParcelProvenance`; it adds no owner, occupant, or other personal fields.

| Matches on `terrainIdentityStem` | Signature result | Outcome |
| --- | --- | --- |
| None | n/a | `Create`, with an informational log entry. |
| One | stored document CreationGUID differs | `Refuse`: identify a copied/new-document element before considering an otherwise matching signature. |
| One | stored `UniqueId` differs from current element or point/frame hash differs | `Refuse`: name the element id and report copied/stale or changed physical terrain content. |
| One | identity, content signature, point/frame hash, document CreationGUID, and stored `UniqueId` all agree | `Reuse`: return the existing element id and associated known placement/export references; do not create a second terrain or PropertyLine. |
| One | content signature is different or missing | `Refuse`: name the element id and explain that the same logical area has different run content. |
| More than one | any | `Refuse`: name every colliding id; never choose the first. |
| Entity claims an id but the element is absent/unresolvable | n/a | `Create` with a stale-identity log entry. |
| Any legacy v1 entities exist | n/a | `RequiresLegacyAcknowledgement`: list the unverifiable v1 ids and require an explicit, current-run “Create despite possible duplicate” confirmation; a warning followed by automatic Create is forbidden. |

The scanner and mapper return plain decision/value objects. Only a thin Revit adapter uses `FilteredElementCollector`, `Schema`, or `ElementId`; all Revit API calls are reverified against installed 2027 API 27.x XML and compiled accessibility before implementation. A Revit-native copy produces at least a duplicate-stem collision and therefore refuses.

### Accuracy and the TinError decision (#45 → #46)

`TerrainErrorAnalyzer` triangulates retained original samples, then evaluates every valid full-resolution clipped-grid cell once. It reports maximum absolute vertical residual, RMS residual, compared count, uncovered count/reasons, units, and algorithm/coverage-floor metadata. It must reject or separately report cells outside the retained-domain triangulation; NetTopologySuite’s convex frame must never imply coverage for a concave AOI, hole, or NODATA region. The analyzer is opt-in through a CLI `verify`-style operation. It changes no normal Revit timing or created terrain.

Matching is injective and bijection-aware: an actual SlabShape vertex can satisfy at most one expected input point. The verifier reports unmatched expected points, unmatched actual vertices that are material to the supplied profiles, and a candidate collision where two expected points are within tolerance of the same actual vertex. This prevents a count/bounds pass from accepting a many-to-one nearest-neighbor match. Tolerances, profile-generated extra vertices, and the matching algorithm are isolated in a tested policy object.

The analyzer's valid-domain mask is mandatory, not merely a diagnostic refinement. A triangle is ineligible when it bridges an AOI concavity, hole, NODATA area, or disconnected retained-support component. Every resulting unmeasurable full-grid cell is counted and reported with its reason. It may not enter maximum or RMS calculation.

The implementation predicate is deterministic and exact: `triangleCoveredBy = union(valid clipped cell footprints) ∩ singleConnectedSupportComponent`. It operates on the full triangle, never a sampled interpolation path or a point-in-polygon guess. A triangle that crosses a component boundary, a concavity exterior, a hole, or a NODATA footprint is uncovered and makes affected reference cells unmeasurable.

After measurements at representative budgets and the #54 public-reference run where available, #46 records one of: retain curvature-aware only; open a Candidate A exact-sample TIN implementation issue; or open a Candidate B geometry3Sharp QEM implementation issue. Candidate A may take public algorithm ideas but cannot copy delatin source. Candidate B requires owner acceptance of blended vertex positions, a pinned package, BSL-1.0 notice text, and an implementation issue. Neither decision is preselected.

### Collection date and coverage floor (#47)

`coverageFloorFraction` becomes a required provenance simplification field and is logged by the Revit host for each run. The collection-date research checks current OpenTopography documentation and `/otCatalog` semantics before any network design. The memo must answer whether a per-AOI, unambiguous result can be obtained without a third fetch-path request or false attribution across overlapping projects. If no safe source exists, JSON and v2 storage explicitly say `notReportedBySource`; `CollectionPeriod` is not fabricated. If it is feasible, a new issue specifies endpoint, quota/call accounting, ambiguity policy, redaction, and opt-in behavior.

### CRS and geolocation (#49–#51)

Use a publicly documented NAD83 State Plane two-standard-parallel LCC zone with citations in its fixture header and design note; never derive the fixture from a customer site. The process-mode test parses WKT, transforms and reverses coordinates, reports residual against existing UTM tolerance, and documents the NAD83-versus-WGS84 approximation for that realization. Fetch mode remains UTM-only.

`ProjectionCharacteristics` numerically differentiates the existing forward transform near the local origin and returns grid convergence and point scale with precision/step-size validity checks. Closed-form or trusted published values must cross-check UTM and the LCC fixture. Fetch mode displays it only after GeoTIFF metadata supplies authoritative CRS; process can display it after sidecar validation. It never writes `ProjectPosition.Angle`.

#51 separately rechecks installed Revit 2027 metadata and compiled accessibility for `SiteLocation.SetGeoCoordinateSystem`, `SiteLocation.GetEPSGCode`, `ProjectLocation.SetProjectedSpaceToLocalTrf`, and relevant public properties. Its memo selects the source point (parcel centroid or address pin), documents any new plumbing and overwrite detector required, and leaves the combined-scale-factor API deferred. The owner then decides whether to authorize a separate opt-in implementation issue.

### Usability, settings, and geometry (#36, #38, #52, #53, #60)

Implement the accepted Settings policy and visual specification through a separate ribbon button and shared WPF resource dictionary. Settings owns persistent preferences and source setup; Review owns document choices and a default-off, current-run-only shared-coordinate option. Any accepted session-only key stays in memory, is masked, and is excluded from JSON, provenance, logs, errors, clipboard actions, diagnostics, exports, and restart state. CLI key resolution remains unchanged.

`UiSettingsDocument` is a strict, versioned per-user contract beginning at `schemaVersion: 1`. It rejects unknown fields, missing required fields, wrong token types, nonfinite or out-of-range values, and every unsupported future or retired version with field-specific recovery text. At Load or legacy Import, strict decode produces an immutable draft and captures an expected target token: the SHA-256 digest of the exact target bytes or an explicit `missing` sentinel. Save uses one named mutex and a digest-bound sequence: acquire mutex; read the current target bytes/digest or `missing`; compare it to the draft-captured expected token and fail visibly on any difference before staging; validate the already-loaded immutable draft without decoding newly observed bytes; stage a complete replacement in the destination directory; immediately re-read and compare the target digest/sentinel again to catch a noncooperating writer; atomically publish only on the second match; update the draft token only after success. On either conflict, preserve both file and draft, report conflict, and require explicit Reload or Reload and reapply draft. No partial settings document is observable and Save never decodes new bytes and overwrites them.

Legacy import is separate from normal Save. It reads legacy bytes without altering them, strict-decodes them, resolves every relative parcel/raster/PRJ/source-sidecar path against the legacy settings file's own directory, presents the exact import summary, and writes the new per-user snapshot only after explicit operator selection. Corrupt legacy bytes remain intact; the UI offers field-specific repair or explicit Start new settings and never silently substitutes defaults. The normal per-user document, once present, is authoritative.

For each provider, a nonpersistent in-memory session key has precedence over that provider's existing environment resolution for the full current Revit session. It survives Settings close, Cancel, and reopening; only an explicit Clear action or `IExternalApplication.OnShutdown`/process exit removes the session reference. A newly typed key becomes active only through an explicit Use key action or an explicit Settings Save that validates and activates that key; Cancel never activates an uncommitted value and leaves any already-active session key unchanged. The persistent settings serializer never receives a key. Synthetic sentinel-key tests exercise precedence, dialog-close persistence, explicit clearing, shutdown clearing, Use key/Save activation, and Cancel non-activation, and inspect settings serialization, export/provenance, logs, errors, diagnostics, issue fixtures, and clipboard-copy paths to prove the sentinel never appears. They also pin the existing CLI resolution behavior.

Every lookup begins from an immutable input/source snapshot with monotonically increasing `inputRevision` and `sourceRevision`. Any address, coordinate, selected source, settings, or local-file change increments the relevant revision and invalidates dependent confirmation. Completion applies on the dispatcher only when its cancellation token is live and both revisions still equal the latest snapshot; otherwise it has no UI side effect. This prevents a late geocode, parcel query, reachability probe, or source reload from overwriting newer selections.

The command receives a validated immutable request rather than ViewModel internals. Suggested adapters are `ISettingsStore`, `ILocationLookup`, `IParcelLookup`, `ITerrainExtentPlanner`, `IExistingTerrainDecisionService`, and `ICreationCompletionPresenter`. Their contracts use Core records and cancellation tokens; each is bounded to a single responsibility. `CreateToposolidCommand` remains the sole coordinator of final Preflight, decision presentation, native transaction, rollback, and TaskDialog failure boundary.

`TerrainExtentPlan` carries separate `LegalParcelRegion`, `TerrainClipRegion`, `FetchEnvelope`, stable local origin, and legal plane Z. It calculates the local origin from the legal parcel before terrain clipping/buffering, and resolves legal plane Z only from valid legal-area samples. If no valid legal sample exists, it returns a named pre-transaction failure; it must not borrow the expanded terrain's minimum Z. PropertyLine and legal identity always use the first and fixed legal elevation basis. Buffer alters only terrain/fetch members. Positive-buffer coverage, NODATA, multipart/hole, short-edge, and topology-change messages are disclosed and require the planned confirmation; insufficient envelope coverage blocks before creation. Relative parcel/raster/PRJ/source-sidecar paths resolve against the settings file directory, consistently for migration, Browse, and process mode.

Legal-boundary cleaning is lossless only: it may deduplicate exactly coincident vertices and remove provably redundant collinear vertices while preserving ring topology and the represented legal shape. A short edge that would require moving, snapping, buffering, or otherwise changing the legal boundary is a named pre-transaction rejection, never “cleanup.” Terrain-only geometry can be simplified only under its separately disclosed terrain rules.

The #53 test project is `net10.0-windows`, `UseWPF=true`, local Windows only, and uses a dedicated STA thread to bind every `Binding(nameof(...))` used by the actual dialog. It can reference the local Revit install as the host project does, but is not presented as Revit-independent and is never added to Linux CI. Its accepted workflow/instruction amendment must be present before landing the project.

### Benchmark and reproducibility (#54)

The scorer accepts either a SolidGround bundle or external CSV `x,y,z` plus declared EPSG, horizontal units, and vertical units. It rejects absent/mismatched declarations; labels undeclared external vertical datum as such; reports comparison count and every coverage gap. Synthetic fixtures inject known errors and NODATA holes for the offline suite.

Before comparison, bundle input reconstructs projected source coordinates from its local-origin metadata. External CSV declares CRS axis order and horizontal units; the scorer performs explicit checked axis and unit conversion before point location and never assumes easting/northing order. A declared vertical-datum mismatch is non-comparable unless a separately implemented, cited transform is available. An undeclared external datum is geometry-only and labelled `vertical datum undeclared`; it is never reported as datum-aligned. Tests cover reconstruction, axis swap, unit conversion, declared mismatch, undeclared datum, and NODATA gaps.

The real reference is captured only after explicit opt-in with an available `OPENTOPOGRAPHY_API_KEY`, at the public example site, through the existing two-call CLI fetch path. It remains under ignored `artifacts/`; the benchmark note records acquisition date and SHA-256 but no key, URL, address, place name, or local path. Current evidence: the authorized bounded capture succeeded using the existing current-user environment in its own subprocess, without printing, persisting, or changing that environment. Its acquisition/hash set and 15k/8k/4k measurements are recorded in the benchmark note. The opt-in local reference test verifies all hashes and reproduces those measurements without HTTP; synthetic evidence remains separate. The scorecard is append-only and says “not measured” where a comparable export has not been supplied. Watch mechanisms remain outside repository automation.

## Acceptance and evidence matrix

Current benchmark capture status: the authorized bounded CLI capture succeeded through the existing current-user environment in its own subprocess. It did not print, persist, or change the user environment; it stored only ignored reference artifacts. Acquisition time is `2026-09-30T19:58:50.957Z`. The captured set's SHA-256 values are raster `F5D71C8D496628144626BF3588C107D55EFEA5E8476404BB82BD36A5103BD3D5`, projection `9423E96198C5EB06477BA59B0C6E758B7D19D06030CA7BB5C80FD5D98BE37D17`, and source metadata `90A7A0D018B07FB4CA9D708C34D79326BFE028EA6FF8D53AF8D83755E1195E5D`. The benchmark note records this acquisition and hash set alongside the measured scores. Synthetic fixtures remain offline-test evidence only.

“Offline” is required before merge for code it covers. “Runtime” means a signed build/deploy/restart/hash-verified Revit 2027 session and is not satisfied by source inspection. Research rows require cited, reproducible findings but no model mutation.

| ID | Acceptance result | Offline-required evidence | Runtime/research-required evidence |
| --- | --- | --- | --- |
| 43 | Detect an individual shifted/swapped vertex missed by aggregate checks; name index/delta; rollback | Pure injective/bijection-aware matching tests, including two expected points competing for one actual point, wrong-vertex fixture, existing verification tests | Correct created toposolid and intentionally corruptible probe both exercise pass/rollback on Revit 2027 |
| 44 | Exact repeat reuses/refuses; copy/new-document, Save As, stale identity, multi-match, legacy acknowledgement, and fallback AOI are explicit | Canonical identity/signature/point-frame vectors, CreationGUID/UniqueId cases, and all decision branches | Repeat/copy/stale/collision session shows ids and no unwanted new element |
| 45 | Opt-in maximum/RMS error and gaps are truthful | Plane/ridge/swale/edge/hole/determinism/concavity/unit tests, including triangles that would bridge concavities, holes, NODATA, or disconnected support | None required for diagnostic correctness; real data is evidence for #46 |
| 46 | Dated, evidence-based choice or no-go | N/A—decision-only | Review #45 and, if available, #54 reports; record follow-on only if chosen |
| 47 | Export/log coverage fraction; date recommendation or explicit unavailable value | Render/read round trip and log-surface/redaction tests | Cited current OpenTopography API research; no extra acquisition call without follow-on |
| 48 | V2 address/parcel fields round-trip and v1 remains intact | Core schema/values, privacy/redaction, field-count, v1 compatibility tests | Create/save/reopen/read-back V2 entity in Revit 2027 |
| 49 | Public LCC process-mode path is reversible | Fixture provenance, WKT parse, forward/inverse residual tests | None beyond ordinary Revit process-mode smoke if integrated later |
| 50 | Correct convergence/scale display without north-lock change | Closed-form/trusted-reference UTM/LCC tests and existing shared-coordinate suite | Fetch session displays real resolved non-placeholder value |
| 51 | API accessibility and write policy decided | Metadata-probe repeatability artifacts | Research-only: installed-API inspection and owner decision; no SiteLocation write |
| 52 | Help, links, estimate, and probe are accessible and safe | Estimate, no-WebView, URL/document text, request-count/no-retry tests | Tooltips/captions in both themes, browser shell-out responsiveness, TaskDialog link rendering |
| 53 | Every actual binding is constructed and change-notifying | Local Windows STA WPF test project | No Revit process needed; a live dialog smoke remains part of #60 evidence |
| 54 | Repeatable scorer and scorecard distinguish synthetic vs real evidence | Synthetic reference/CSV, injected errors, NODATA, local-origin reconstruction, CRS axis/unit conversion, vertical-datum mismatch/undeclared cases, missing-unit rejection | Captured public reference hash/acquisition record and explicit opted-in real score |
| 60 | First/repeat workflow meets approved Location→Parcel→Review and Settings contract | Extent/origin/plane-Z invariance, lossless legal-cleaner/rejected-short-edge cases, cancellation/stale-revision cases, strict settings/mutex/digest/import/corrupt-byte cases, session-key sentinel/clear cases, accessibility binding, source-attribution tests | Keyboard/theme/scaling/screen-reader, legal/terrain outline, property-line invariance, preflight/Undo/provenance/save-reopen, 3–5 formative-user rounds |
| 36/38 | Containment wording and settings-relative paths are correct | Candidate formatting and path-resolution tests | Covered naturally in #60 dialog/process-mode smoke; no separate session claim needed |

The mandatory live closeout for the Phase 4A integrity core is #43/#44/#48 together. #50, #52, and #60 add the listed UI/host evidence before release. Failure of an offline test blocks its increment; failure or absence of live evidence blocks release/issue closure for runtime rows, not unrelated Core planning or research.

## Known gaps, blockers, and stop conditions

1. Reference capture and its separately recorded hash-verified measurements are complete in [comparison-benchmark.md](../architecture/comparison-benchmark.md). Synthetic fixtures remain a separate offline evidence source; no comparable external-tool export has been supplied.
2. #45 is a hard prerequisite for #46; #54 informs but does not replace its measurement basis. #46 must not turn into a stealth algorithm implementation.
3. #51 requires current installed-Revit metadata plus owner direction after its memo. Ambiguous or internal APIs, including the combined-scale-factor call, stop at research.
4. #47's [collection-date decision](../architecture/elevation-collection-date-investigation.md) retains “not reported by source.” The catalog does not unambiguously identify the returned mosaic pixels; no third acquisition request was added.
5. A V2 schema cannot retroactively identify v1 terrain. The scanner must show a visible legacy-provenance warning before the documented `Create` outcome; that conservative outcome does not prevent a duplicate of a v1 element. Migrating legacy entities is explicitly out of scope and needs a separate owner decision.
6. Revit API behavior is not established by XML documentation alone. Every new call is checked against the installed 2027 SDK and compiled accessibility, then demonstrated in the required runtime rows.
7. The approved usability design changes persistence and command layout. Its security, migration, accessibility, display-scaling, external-link privacy, cancellation, and legal-boundary invariants remain release gates, not cosmetic follow-up work.

## Review history

Round 1 adversarial review, 2026-09-30: accepted and incorporated. The plan now requires strict `UiSettingsDocument` versioning and mutex/digest/atomic staging; legacy-byte preservation and legacy-relative path rebasing; session-key precedence/clear/sentinel tests; immutable monotonic input/source revisions for all asynchronous completions; an exact component-and-footprint TIN coverage predicate; lossless-only legal cleaning with short-edge rejection; and v2 document CreationGUID plus element UniqueId plus actual point/frame content for copied-element detection. It also makes legacy v1 creation require an explicit duplicate-risk acknowledgement rather than allowing automatic Create. No review item authorizes source code, a TinError algorithm, or a SiteLocation write.

Round 2 adversarial review, 2026-09-30: accepted and incorporated. Load/import captures the draft's expected digest or `missing` sentinel. Save compares it under mutex before staging and immediately before publish, preserves file and draft on conflict, and offers only explicit reload/reapply resolution. Session keys persist across Settings close/Cancel for the declared Revit-session lifetime and clear only on an explicit action or add-in shutdown; a newly entered key activates only through Use key or validated Settings Save, never Cancel.

## Definition of ready for implementation review

Before source work begins, reviewers must confirm: the combined v2 field/identity contract and legacy behavior; matching tolerances and signature canonicalization; #45 coverage semantics; the collection-date stop rule; the exact public LCC citation; the accepted #60 settings policy and #53 workflow amendment; and each runtime evidence owner. Each implementation PR records the affected design-note/API citations, runs locked restore/Release build/offline tests, and states which matrix rows are still pending runtime or research evidence.

### Independent plan review ? approved

On 2026-09-30, a separate GPT-6-Luna reviewer cleared the revised PRD, visual specification, and combined Phase 4A plan after two review rounds and their corrections. No critical or significant findings remained. Source implementation may proceed; Revit runtime and formative-user evidence remain required before the corresponding issue/release acceptance claims.
