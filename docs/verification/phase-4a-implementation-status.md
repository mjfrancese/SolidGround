# Phase 4A and Revit usability implementation status

Date: 2026-09-30. Scope: #60, #36, #38, and all twelve Phase 4A children #43–#54. The owner accepted the combined scope and the D1–D4 instruction amendments. Phase 4B and #42 remain outside this change.

## Implemented source and research

| Issues | Result | Durable evidence |
| --- | --- | --- |
| #36, #38 | Containment wording distinguishes inside from nearby; settings-relative source paths share a resolver. | Core formatting/path tests; [Settings](../architecture/revit-settings.md) |
| #43, #44, #48 | Injective XYZ verification inside the transaction; read-only duplicate/copy/edit guards; self-contained v2 provenance with immutable v1 compatibility. Exact repeat is refused with existing element IDs; automatic reuse is not shipped. | [Integrity design](../architecture/revit-v2-provenance-and-integrity-guards.md); matcher/identity/schema tests |
| #45, #54 | Opt-in CLI scorer measures maximum/RMS residual and every coverage gap. Triangles cannot bridge NODATA, holes, concavities, or disconnected support. Public reference capture and hash-verified 15k/8k/4k measurements are recorded. | [Benchmark](../architecture/comparison-benchmark.md); scorer/analyzer/reference tests |
| #46 | Retain curvature-aware simplification based on current measurements. Neither TIN candidate nor a new dependency was introduced. | [Decision memo](../architecture/tin-error-decision.md) |
| #47 | Coverage-floor fraction is exported/logged/stored. Collection-date availability is explicit; no ambiguous catalog attribution or third acquisition request. | [Date investigation](../architecture/elevation-collection-date-investigation.md); provenance/export tests |
| #49, #50 | Synthetic public State Plane LCC process fixture; measured grid convergence and point scale after authoritative CRS resolution. Unsupported/unstable measurement is omitted with a reason, without blocking otherwise valid terrain. | [Projection design](../architecture/projection-characteristics.md); UTM/LCC, unit, origin, and nonconformal tests |
| #51 | Installed Revit 2027 API accessibility and overwrite-policy research. No SiteLocation or new angle/scale write. Any opt-in geolocation feature needs a separate owner decision. | [SiteLocation research](../architecture/revit-site-location-research.md) |
| #52, #53, #60 | Location → Parcel → Review; one Find; separate Settings; per-user strict atomic preferences; session-only masked keys; source setup and repair; theme resources; actual local WPF binding/state checks; terrain-only extension and separate preview. | [Usability design](../architecture/revit-usability-settings-and-workflow.md), [WPF lane](../revit-wpf-runtime-tests.md), [extent contracts](../architecture/parcel-terrain-extent-core.md) |

The public reference has 12,099 valid samples, so the default 15,000 budget retains all of them. The reduced-budget results are measured against that raster, not certified survey accuracy or a general simplifier error bound. No comparable external-tool export was supplied.

## Automated verification and review

Final source checks on 2026-09-30 used the installed Revit 2027 SDK, never the CI-only reference-package condition. Locked solution restore and Release build passed with zero warnings/errors. Core tests passed 1,723/1,730: six live-service cases and the separate captured-reference opt-in case were skipped by default. The captured-reference case was also explicitly run against the recorded hashes and reproduced its measurements without HTTP.

The separate local Windows WPF project restored in locked mode and built with zero warnings/errors. Its full lane passed 38/39 with one explicit all-transparent reference-render skip. Tests exercise actual binding roots, notifications, accessible peers, malformed/future recovery, cancel/conflict byte preservation, process/origin edits, local-source terms, source resumption, and stale asynchronous completions. Tests sharing the process-static session credential store are serialized and take the real credential revision in their fake service snapshots.

Independent implementation review cleared the Core/command contracts, Settings D1–D4 contracts, and final guided-flow corrections. The final guided-flow review target was `0f38a58`; subsequent evidence edits do not change production behavior. The baseline-to-review diff passes `git diff --check`. The existing trusted-main-only CI workflow and its runner/security guards are unchanged; no pull-request CI result is implied by the local checks.

## Acceptance still required

The [runtime checklist](revit-60-runtime-checklist.md) binds a manual Revit 2027 session to signed DLL hashes, MVIDs, informational versions, and the source commit. Native transaction/Undo, vertex pass/rollback, duplicate/copy/edit detection, v2 save/reopen, property-line invariance, actual CRS readout, accessibility, themes/scaling, and completion actions remain unverified for this implementation until that session is recorded.

The owner authorized Computer Use on 2026-10-01 to investigate reported usability defects. The native Windows helper's pipe was unavailable after initialization and a reset/retry, so no GUI validation was performed in this follow-up. The WPF reference visual remains entirely transparent in this test environment; the pixel-render case is explicitly skipped. Binding/control-property checks supply no pixel-render or native Revit proof.

The owner's subdivision/hide method remains a manual experiment in the checklist. Full terrain context is the default; no subdivision/hiding automation ships without native surface/visibility evidence. The planned 3–5 formative-user rounds also remain part of #60 acceptance. Runtime-gated issues, the epic, and release acceptance must remain open until their evidence and owner acceptance are recorded.

## First-use feedback and follow-up, 2026-10-01

The owner reported successful terrain creation but rejected the Settings and creation-window usability. Local WPF tests reproduced the defects: the tab template used page content for the header, the creation content was left-aligned, and the parcel preview anchored to a corner with overlapping legends. These are corrected with bounded resize/scroll checks, fixed-action bounds, progressive source disclosure, human-readable choices, conditional key controls with non-secret status, and a centered clipped plot with separate legends.

New Revit UI settings use the exact unbuffered legal-area centroid for horizontal placement. This accounts for holes and multiparts and keeps terrain and PropertyLine in the same frame as extension changes. Existing saved choices and historical Core/CLI origin modes remain intact. Other AOIs use the clipped-grid center and the UI states that distinction.

Fresh locked restores, the installed-SDK Release solution build, and both complete test lanes passed: Core 1,730 passed / 7 opt-in skips / 1,737 total; local WPF 47 passed / 1 explicit render skip / 48 total. WPF tests now serialize desktop windows to preserve real keyboard-focus assertions; independent STA dispatchers did not isolate the shared Windows desktop.

The [feedback plan](../planning/revit-usability-runtime-feedback.md) records the fixes and next placement design. The [native datum research](../research/revit-2027-building-and-survey-datums.md) establishes project-zero versus survey-based labels. The owner accepted a provisional preview and proposed selecting a ground point beside the front door plus a measured rise to the unfinished first floor. [Building-outline research](../research/building-outlines-and-grade-point-selection.md) assesses automatic navigation context. These vertical/outline features are researched and planned, not shipped by this layout fix. Native Revit validation and subdivision acceptance remain open.
