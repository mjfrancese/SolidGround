# Building floor reference and ground-selection implementation

Date: 2026-10-01. Authorization: the owner accepted the research and UX requirements and instructed “Make it so.” Scope is architectural: a reusable acquisition snapshot, a managed outline source, new reversible elevation/provenance data, and an interactive WPF surface. Baseline source commit: `372db75d7547ee4625e7f88089788f8385530102`. Work continues on issue #60 / draft PR #61.

## Outcome

An operator can keep the unfinished first-floor plane on an existing Revit level while terrain and its legal property line use the same centered, reversible frame. They can supply a known source-datum floor elevation, estimate it from an exterior ground pin plus a measured rise, or explicitly create a provisional ground-centered preview. The operator never needs to prepare building-outline files, edit settings JSON, or understand raw projected coordinates to finish the ordinary task.

The visual/interaction acceptance contract is [UX01–UX10](../design-specs/revit-60-usability.md#11-owner-directed-ground-selection-ux-requirements--2026-10-01). The source and native references are [building outlines](../research/building-outlines-and-grade-point-selection.md) and [project/survey datums](../research/revit-2027-building-and-survey-datums.md).

## Placement and data contract

- Keep the unbuffered legal area centroid as the horizontal origin for the recommended parcel mode. An outline or selected pin cannot move that origin or change the legal parcel, terrain extension, or point budget.
- Read the selected level's `ProjectElevation` on Revit's UI thread, in internal international feet. Persist its identity and actual plane, not its potentially survey-based displayed `Elevation`.
- Record the physical source floor height `H_F` independently of the coordinate-frame origin. Resolve frame-origin elevation as `H_F - Convert(targetLevelProjectElevationInternal, InternationalFoot, sourceVerticalUnit)`. Existing geometry/export/identity services then consume one final local frame; no Revit-only Z correction is appended after export. Inverse native/source reconstruction remains the existing frame transform.
- `KnownElevation` requires an explicit finite floor height, entered unit, source-datum acknowledgement, and source description. No vertical-datum conversion is inferred. `EstimatedGradeRise` requires a selected valid ground sample and an explicit nonnegative measured distance plus above/below-grade direction; zero is allowed only when entered. Preserve physical measurement units separately from output-coordinate units.
- `ProvisionalGround` uses exactly the non-null elevations in `TerrainExtentPlan.LegalClipResult.Grid`, before simplification, as its temporary reference. For a nonparcel area use `Outcome.ClipResult.Grid`, or the raw grid when no clip applies. These are the existing `GridClipper` cell-center inclusion/masking rules; a partial-boundary cell contributes only when that rule includes its centre. Sort ascending; an odd population uses its middle value and an even population uses the arithmetic mean of its two middle values, computed as `a/2 + b/2` to avoid overflow. Calculate in the declared source vertical unit, reject an empty or nonfinite result, and never use terrain extension or simplifier-selected cells. Map it to the selected level but label it **Ground-centered preview; first-floor elevation not set**. Record policy `legal-valid-cell-median-v1` (or `aoi-valid-cell-median-v1` for nonparcel) and value. It is not a guessed floor or surveyed elevation.
- `SourceElevations` preserves the historical source-height/origin path as an explicit alternative. New floor-reference choices are run data and are not silently migrated into saved preferences.
- Reject floor-centered placement in an already coordinated document until a verified source-to-host horizontal placement path exists. Offer the historical placement option or a new uncoordinated site document with clear in-UI explanation; a floor height cannot resolve an unknown horizontal transform.
- Real source heights remain in provenance/ranges and are exactly reconstructible. Survey-coordinate writing stays a separate per-run default-off choice and never overwrites an established transform. A provisional floor is not survey authority; prevent that mode from writing shared coordinates. For estimated placement, any explicit mapping is labeled estimated and requires acknowledgement. Known elevation mapping requires the operator's declared source reference. Native real-elevation label/contour promises require the existing runtime matrix; no user annotation type is changed silently.

## Acquisition and sampling

Preview uses one immutable prepared snapshot: raw grid, source-to-WGS84 transform, the existing full pipeline outcome, source evidence, and an input/credential revision. It prepares terrain only after **Load ground preview** passes the same document/settings/output/point-budget/source Preflight as creation. Revit API stays on the owning thread; HTTP/file I/O and managed processing run off the dispatcher. Final creation rechecks Preflight and consumes the current prepared snapshot; it does not issue duplicate elevation requests or resimplify solely because the pin/rise changed.

Bind the snapshot to the confirmed AOI/location/provenance, effective request settings, extension, geometry tolerance, selected level/type, and credential revision. Changed source settings, parcel, units, budget, or target invalidate it. Late canceled/stale completion cannot restore it. Editing only the pin/rise/known height uses the current grid.

Sample the unsimplified raw grid using strict cell-center bilinear support. Honor declared row order and anchor convention. Require valid supporting cells, reject out-of-center-lattice and NODATA support, and never clamp, extrapolate, bridge a hole, or snap to another point. Normalize the interpolation index to an integer only within `1e-9` of a cell-index unit (to absorb horizontal-transform floating point noise); this does not move the displayed/stored operator point. The closed centre lattice is the valid domain; coordinates outside it beyond that same tolerance reject. A centre needs its one valid cell, a row/column edge needs its two nonzero-weight cells, and an interior needs four. Persist projected/WGS84 point, source height/unit/datum, method, support cells, and operator measurement. Tests cover all anchor/row orders, endpoints, zero-weight NODATA corners, epsilon-near centres, and transformed WGS84 points.

## Automatic building context

Use pinned Microsoft Global ML release `2026-08-13` under CDLA Permissive 2.0. Load the reviewed manifest (SHA-256 `8E479A213F6B9C4670CD80B036B06D6DEFEA613F1BB6B14417DB26E82AA137F2`), select up to four level-9 quadkey tiles intersecting the display parcel bbox, and stream managed gzip GeoJSONL. The exact manifest host is `bfppub.blob.core.windows.net`; tile host is `bfppub.z5.web.core.windows.net`, under `/2026-08-13/global-buildings.geojsonl/`. Require HTTPS, default port, no credentials/query/fragment, and disable/reject redirects. Enforce 25 MiB per compressed tile, 40 MiB total, 128 MiB decompressed per tile/160 MiB total, 256 KiB per JSON line, 250,000 parsed features per tile, 5,000 retained display features, 16,384 coordinates per feature, 32 rings and 16 components per feature, and a 30-second outline timeout. Manifest is at most 8 MiB; cache its validated index for the process/release. Inspect actual streaming byte counts, not only declared sizes.

Outline fetch is optional and independent of required terrain readiness. Show provider/location-disclosure notice before requesting coarse tiles. No address, parcel identifier, API key, or private geometry is sent to this source. Invalid/oversized/unavailable data produces a concise no-outline status with Retry and leaves point placement usable. Keep attribution/licence/release/tile identity in the run's export/provenance, with no invented accuracy, height, door, or survey claims. Fixtures are entirely synthetic. No source hosting, paid provider, native GIS package, or whole-state fallback is added.

## Interaction

Review's **Building floor reference** card edits the reference in the same content area and footer, retaining Location/Parcel/Review. Hide unrelated review controls while editing. One mode reveals only its necessary fields. Loading preserves the confirmed parcel; valid terrain automatically enables pin placement. A click places/moves one unsnapped pin, and its field/status appears without a second Next click. **Use this floor reference** returns to the concise Review summary. Create is never the active default while pin editing owns focus.

The preview supports visible zoom/pan/Fit controls, a 32-DIP pin handle, dragging, coordinate entry, keyboard nudging with a disclosed physical increment, and selection status/focus. Resize and late outlines preserve world coordinates and user view. In pin focus Enter accepts selection and Escape cancels placement/drag; neither invokes Create or closes the dialog. No nested page scrollbar or mouse-wheel trap is introduced. Shared styles and the existing theme/scaling matrix apply.

## File responsibility map

| Domain | Files to add/change | Responsibility |
| --- | --- | --- |
| Core datum | `Processing/ElevationGridSampler.cs`, `Processing/TerrainPlacementReframer.cs`, `Provenance/BuildingFloorReference.cs`, corresponding offline tests | Sampling, mode validation, median preview reference, unit-aware final frame, legal-plane translation, serializable floor record |
| Core provenance/export | `Provenance/TerrainProvenance.cs`, `Exports/TerrainExportBundleReader.cs`, `Exports/TerrainExportBundleRenderer.cs`, export/golden/compatibility tests | Export schema 6 adds floor/context data; strict readers retain v4/v5; deterministic round trip and unknown/malformed rejection |
| Core outline source | `Sources/BuildingOutlines/` managed source/contracts/parser/quadkeys and focused tests | Pinned manifest, bounded transport/parse, display geometry, attribution, recoverable availability |
| WPF preview | `Dialog/GroundPointPreview.cs` and actual arranged-WPF tests | Coordinate-to-display mapping, pin/pan/zoom/resize/focus/keyboard behavior, no source I/O or Revit API |
| Floor task | `Dialog/BuildingFloorReferenceViewModel.cs`, `Dialog/BuildingFloorReferencePanel.cs`, WPF state/binding tests | One-question mode selection, preview request lifecycle, local sampling, measured input, summary and confirmation |
| Host/command | `Dialog/SolidGroundDialogHost.cs`, `SolidGroundDialogInputs.cs`, `SolidGroundDialogResult.cs`, `SolidGroundDialogViewModel.cs`, `SolidGroundDialog.cs`, `Commands/CreateToposolidCommand.cs`, new `Processing/TerrainPreparationService.cs`, `Elements/LevelAndTypeResolver.cs` | UI-thread Preflight callback, prepared snapshot reuse, final level-plane check, current request binding, creation/export handoff |
| Native provenance | new Core v3 schema/values contract, new Revit v3 adapter/writer; `Provenance/ExistingTerrainScanner.cs`, placement-record contract/renderer | Fresh GUID, v2 fields plus floor/context JSON, full field/source read-back, v1/v2 retained readable, duplicate/copy/edit protection for v3, matching placement export |
| Evidence | this PRD, design/research notes and runtime checklist | Verified 2027 API/source references, native/human acceptance boundaries, build identity and test evidence |

Core remains Revit-free; no package/version/CI/installer changes are intended. Native v1/v2 schemas are immutable. The selected level and existing building geometry remain unchanged.

## Acceptance and test plan

| ID | Observable result | Required evidence |
| --- | --- | --- |
| BF01 | Ground pin samples an analytic grid correctly at centres/interior, independent of row order/anchor, and rejects missing weighted support/outside coverage | Focused Core sample tests with synthetic grids and zero remote calls on movement |
| BF02 | Known/estimated/provisional reference puts its declared source plane on the chosen project plane in metres, both feet definitions, and nonzero/negative target levels; every source point reverses within existing tolerance | Core frame/payload/legal-plane tests; extension-invariance and source-height-range checks |
| BF03 | Empty measurement cannot become zero; below-grade direction, explicit zero, unknown height, invalid units/datum and missing target are clear | Core mode/unit validation plus actual WPF state/field/focus tests |
| BF04 | Median preview ignores NODATA and terrain outside the legal AOI, reports no known floor, and cannot write survey coordinates | Synthetic odd/even/tied/empty populations, NODATA, extension-only cells and partial-boundary cell-centre cases; summary and command safeguard tests |
| BF05 | Preview requires Preflight; cancel/settings/key/parcel/target changes invalidate stale completion; final creation uses the current snapshot without reacquisition | Fake preparation counters and delayed completion/revision tests; authoritative threshold/export checks |
| BF06 | Auto outlines are bounded, cancelable, attributed and nonessential; source failures/invalid geometry/cap violations do not block terrain | Synthetic manifest/gzip/GeoJSONL and HTTP handler tests, URL/redirect/actual-byte cap cases, no-outline UI task |
| BF07 | Pin click/drag/keyboard/coordinates agree across pan/zoom/resize; UI remains cohesive/readable/reachable | UX01–UX10 automated checks plus native theme/keyboard/scaling and novice observation |
| BF08 | Schema6 exports and nativev3 preserve floor/pin/context/target/frame data; older exports/native entities retain their meanings; malformed v3 fails closed | Deterministic export/golden/legacy tests, Core v3 field/value tests, Revit entity full-read-back and save/reopen matrix |
| BF09 | Existing shared transforms/levels/building geometry are never altered by preview or rejected floor placement; explicit accepted writes remain transaction-verified | Existing guard tests, command binding checks, disposable Revit model/Undo/native-coordinate checks |
| BF10 | Source grades remain reconstructible and provisional status is truthful; native contour/spot display claims are made only after the specified native matrix | Export/source inverse tests, Revit labels/contour phase/save-reopen for zero and nonzero model levels/both feet definitions |

Run locked restore, installed-SDK Release build, full offline Core tests, and the separate Windows WPF lane. Meaningful new behaviors use red/green tests. Native-only assertions remain open if the authorized Computer Use helper cannot connect; source inspection is not native evidence. Formative users must complete the specified tasks unaided before human-usability acceptance.

## Correction and release boundary

### Versioned identity and native scan outcomes

For floor-reference runs, introduce `terrain-identity-v2` / `sha256-canonical-run-v2`: keep the existing stable stem and point/frame hash, and extend canonical content with mode, physical `H_F`/source unit/datum, target level durable identity and project plane, estimated projected pin/sample/method/weighted support, signed measured rise and its physical unit, provisional policy/value, and outline source/release/licence descriptor. Retrieval times and build identity remain excluded. Identical geometry with different floor semantics is a different content signature and must refuse as a conflicting same-stem run. Runs without floor/context retain the old canonical identity contract. Tests compare the same resolved origin produced from different floor/target pairs, known versus estimated mode, and differing measurements.

| Existing native data | Outcome before a new creation transaction |
| --- | --- |
| No matching stem and no legacy v1 | Create |
| v1 only, with no modern matching stem | Explicit legacy duplicate-risk acknowledgement before Create; existing record untouched |
| v2 or v3 matching stem and exact identity/signature/native fingerprint/document+element identity | Refuse duplicate and identify existing element; never silently reuse or stamp over it |
| v2 matching stem against a floor-aware v3 candidate | Refuse conflicting/older placement; a new signature version cannot bypass the stem guard |
| v3 matching stem but different mode/floor/target/pin/measurement/content | Refuse conflicting placement and name existing element |
| Multiple matching modern records | Refuse ambiguous duplicates |
| Copied element/document or stale original identity | Refuse; no automatic reidentification |
| Edited native vertices, malformed/unreadable recognized entity, or bad schema shape | Refuse before transaction, even if a different new area was requested |
| One toposolid carrying multiple recognized native versions | Refuse ambiguous provenance; never preferentially ignore an old/new entity |

The scanner retains its existing return seam for modern records and validates the newest schema without rewriting older versions. Core decision tests cover every row; installed-SDK source checks cover scanner dispatch, and native save/reopen/copy/edit observations remain required.

The initial source implementation covers creation with a chosen/estimated/provisional reference and durable metadata. Replacing a reference **before creation** preserves the grid and draft. A model created provisionally must retain sufficient floor/frame/pin/source data for later correction. A post-creation correction cannot be a bare MoveElements: it must atomically update the SolidGround terrain and matching property line, native fingerprint/frame/duplicate identity, exports and audit trail while preserving other elements and coordinate authority. That follow-on transaction requires a separately enumerated design/native acceptance check before exposing an edit action; never display an inoperative promised button. This does not label that later correction capability shipped.

Release/issue closure still requires native/human acceptance. No automatic subdivision/hiding, annotation-type mutation, vertical-datum conversion, source hosting, or existing-coordinate horizontal transform is added by this increment.

## Review

Independent review must challenge frame/target semantics, provisional policy, strict sampling, snapshot binding/reuse, source bounds/licensing, v3/legacy duplicate protection, and the separation between source reconstruction and native label authority before source work begins.

Round 1, independent GPT-6-Luna review: corrected the omitted semantic identity contract, explicit v1/v2/v3 conflict matrix, and numerical bilinear lattice rule. A new review checks these changes before source work.

Round 2: all first-round findings resolved. Specified exact raw legal-clip median population, cell inclusion, odd/even arithmetic, source unit, policy token, and missing/finite rejection; expanded BF04 cases. Final review checks this bounded correction.

Round 3, 2026-10-01: independent GPT-6-Luna review passed with no remaining P1/P2 findings. Approved for the authorized source implementation; native and human acceptance remain required.

Implementation review clarified that optional outline context must survive a later choice of original source elevations without inventing a floor claim. Schema 6 therefore also carries an independent nullable `buildingOutline` record. The unpublished native v3 contract has 66 fields, adding `buildingOutlineJson` beside `floorReferenceJson`; it requires at least one valid record, validates both when present, and uses identity v2 for floor or context runs. Old schemas/identity remain unchanged for runs with neither. Context-only runs retain their original local frame. This corrects the same source-attribution requirement; it does not introduce another elevation mode or a null floor workaround.

Independent review passed this bounded correction. Final code review also required strict serialized agreement when a floor embeds outline metadata and cleared the stale edit baseline after upstream invalidation. Both serialized trust boundaries and the arranged-WPF cancellation tests now enforce those rules. Source validation results and open native/human gates are recorded in the [implementation note](../architecture/revit-building-floor-reference.md).
