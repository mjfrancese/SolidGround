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

## Acceptance still required

The [runtime checklist](revit-60-runtime-checklist.md) binds a manual Revit 2027 session to signed DLL hashes, MVIDs, informational versions, and the source commit. Native transaction/Undo, vertex pass/rollback, duplicate/copy/edit detection, v2 save/reopen, property-line invariance, actual CRS readout, accessibility, themes/scaling, and completion actions remain unverified for this implementation until that session is recorded.

Computer use remains off. The WPF reference visual is entirely transparent in this test environment; the pixel-render case is explicitly skipped. Binding/control-property checks supply no pixel-render or native Revit proof.

The owner's subdivision/hide method remains a manual experiment in the checklist. Full terrain context is the default; no subdivision/hiding automation ships without native surface/visibility evidence. The planned 3–5 formative-user rounds also remain part of #60 acceptance. Runtime-gated issues, the epic, and release acceptance must remain open until their evidence and owner acceptance are recorded.
