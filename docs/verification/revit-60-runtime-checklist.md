# Revit 2027 usability and Phase 4A runtime checks

Status: prepared; no runtime result is recorded. Issues #43, #44, #48, #50, #52, and #60 require this evidence before release/closure. The local WPF lane does not prove native Revit transaction or geometry behavior.

Computer use remains off. A person operates Revit using the normal signed build/deploy/restart loop. Use a disposable project and synthetic fixtures or the public example site. Keep private model paths, addresses, parcel identifiers, screenshots, and full local logs out of this public repository and GitHub.

## Bind the run to a build

1. Record source commit, informational version, MVID, SHA-256 of both first-party DLLs, Revit version, and test results.
2. Sign the local Release DLLs with `scripts/Sign-RevitAddIn.ps1`. Never sign tracked source scripts in place.
3. Save and close Revit before `scripts/Deploy-RevitAddIn.ps1`. Its running-process refusal remains authoritative; do not bypass it or terminate a user's model.
4. Restart Revit and run the deploy script's `-Verify` mode against the same signed build directory. Record matching hashes. A changed build invalidates affected runtime evidence.

## UI and setup

| Scenario | Observe and record |
| --- | --- |
| No active document | Settings opens from the ribbon without a model change. Create gives the appropriate project requirement. |
| Fresh settings | Configure elevation and either a local parcel or authorized county service entirely in the UI. No external JSON editor, environment editor, or elevated settings write is needed. |
| Legacy settings | Explicit import preserves legacy bytes, rebases relative raster/parcel paths, transfers terrain extension, and keeps shared coordinates off. Cancel preserves bytes. |
| Malformed/future settings | Repair/start-new choices are explicit. Cancel preserves the original bytes; no silent reset. |
| Competing save | Open two drafts; save the first, then save the second. Conflict preserves the second draft and first saved file. Exercise explicit reload/reapply recovery. |
| Session keys | Masked provider-specific entry works immediately. Clear restores environment precedence. Settings Cancel does not activate unsaved text. Shutdown clears explicit session overrides. Do not record key values. |
| One-click Find | Address or valid coordinates advance to Parcel with one Find. One location begins lookup without confirming the parcel; ambiguous locations require a choice. |
| Parcel confirmation | Rows/details explain containment versus nearby distance, area, attribution, license, and legal/terrain outlines. Use this parcel explicitly confirms both displayed location and boundary. |
| Source repair | Preserve successful location while configuring a source. Saving rebuilds the source and resumes lookup. Old parcel results cannot enable Create. Unit/budget changes preserve a valid confirmation. |
| Cancellation/stale completion | Edit location/source during lookup, cancel, and retry. Delayed old results do not revive selection or Create. No transaction or Undo entry is created. |
| Other area options | Exercise bounding box, radius, pasted polygon, and file Browse. Each path shows its actual extent and settings in Review. |
| Help/links/estimate | Sighted help matches accessible help. Map opens in the default browser. Key link is clickable in native error footer. Estimate labels fetch-envelope approximation; probe failure is actionable and causes no fetch retry. |
| Keyboard/accessibility | Tab order, Enter's single visible primary action, Escape, focus cues, candidate details, stage/progress/error announcements, and screen reader names work. |
| Themes/scaling | Light, dark, and Windows High Contrast at 100%, 150%, and 200%. Inspect normal/hover/pressed/focus/selected/invalid/disabled states, open/closed dropdowns, tabs, masked fields, scrollbars, and fixed footer. No clipped controls or unreadable selected values. |

## Native integrity

Run with default-off shared coordinates first. Record element counts and Undo state before and after each case.

| Scenario | Required result |
| --- | --- |
| Ordinary creation | Toposolid and original PropertyLine commit together; every expected point passes injective XYZ matching. Save/reopen retains readable v2 provenance and reversible first-sample coordinates. |
| Point threshold rejection | A budget above the running machine's native threshold is rejected before acquisition/transaction. Lower it through Settings. |
| Vertex corruption | A bounded diagnostic build/probe changes one expected vertex while count/bounds remain plausible. Verification reports index/XYZ delta and rolls back. Do not infer this result from a Core test. |
| Exact repeat | Existing element IDs are disclosed; no extra Toposolid or PropertyLine and no overwritten export. Refusal is valid; unsupported automatic reuse must not be claimed. |
| Changed content | Same parcel with changed points/extension/unit/budget/coverage is refused with actionable existing IDs. |
| Copy and edit | Same-document copy, copy to a new project, edited terrain, and multiple matching stems are refused. Stored original UniqueId/document lineage and native vertex fingerprint must distinguish stale geometry. |
| Save As | Record CreationGUID/UniqueId behavior. Same lineage remains valid only while identity/content/physical geometry still agree. |
| Legacy v1 | Explicit current-run duplicate-risk acknowledgement is required. Acknowledgement does not bypass a v2 conflict. V1 remains readable and unchanged. |
| Shared-coordinate opt-in | Starts off on every invocation. Deliberate absent-coordinate write commits with verified values; existing/customized state is not overwritten. No SiteLocation write or new north rotation occurs. |
| Failure recovery | Invalid legal short edge, insufficient raster extent, no legal valid samples, NODATA/topology warnings, export failure, and transaction failure leave no partial model. Failed/cancelled run has the expected Undo behavior. |
| CRS display | Fetch resolves actual grid CRS before showing convergence in radians/degrees and dimensionless scale. Repeat process smoke with the public LCC fixture. No placeholder value or angle mutation. |
| Completion | Show terrain and Open export folder operate after commit. Optional presentation failure cannot misreport a committed model as failed. Save reminder is visible. |

## Terrain edge experiment

Use one unbuffered legal parcel and the same captured raster, origin policy, unit, and Level. Compare extensions **0, 1, 3, and 6 metres**, entered through the corresponding preferred display units. Run once with all valid samples retained and once with the same reduced budget. Record legal world XY/Z and point counts for every run; duplicate guards mean use separate disposable projects for alternatives.

Compare full context against the owner's manual subdivision method: on the expanded host, subdivide with the original legal boundary at zero subdivision offset. Investigate which host/subdivision can actually be hidden in each view, whether contours/export/section/schedules retain the intended terrain, and whether hide/unhide is reversible. Do not assume the exterior is an independently hideable element.

Measure surface elevations at common interior and near-edge sample locations against the source raster. Record coverage gaps and the visible edge, keeping full-retention and reduced-budget results separate. A visual improvement alone does not establish interior fidelity. Inspect holes, multipart parcels, concave edges, neighbouring parcels, and multiple views. **Full context remains the default; no subdivision/hiding automation ships based only on API availability or this prepared checklist.**

## Acceptance record

Record sanitized outcomes, build identity, and failures in an evidence note with each case marked passed/failed/not run. Obtain owner acceptance for native integrity and usability results. Perform the planned 3–5 formative-user rounds before claiming the usability acceptance criteria met. Pending cases keep their issues and release acceptance open; do not replace them with source inspection or synthetic test claims.
