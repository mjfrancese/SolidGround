# Usability feedback and placement follow-up

Date: 2026-10-01. Scope: follow-up to issue #60 and draft PR #61 after the owner's first use of the installed development build.

## Observed feedback and diagnosis

The owner could generate terrain using existing configuration, but could not comfortably edit Settings or find the terrain extension. Settings scrolling and resizing failed; creation windows also felt poorly sized. The parcel preview was not centered. These reports supersede any inference of usability acceptance from the earlier source reviews.

A local Windows WPF test reproduced a zero-height page viewport. The custom `TabItem` template presented the entire page's `Content` as the header instead of its `Header`. The page consequently occupied the tab strip rather than a bounded scrollable content region. Changing only those template bindings makes the resize/scroll reproduction pass.

Creation-stage content used the default left content alignment. The preview fitted its drawing to the lower-left corner and placed legends in the same canvas as the parcel. Actual WPF arrangement tests reproduce the alignment and centering defects independently of pixel rendering.

## Changes within the approved usability scope

1. Keep one finite vertical viewport per settings page and creation stage, with reachable fixed actions. Test shrinking, growing, tab changes, and scrolling to the bottom.
2. Make terrain extension the first Settings group, show its display units and default, and explain that the legal property boundary does not move.
3. Distinguish preferences with useful defaults from conditional source requirements. Disclose local-raster metadata and parcel registration when relevant; keep recovery actions out of the ordinary footer until a conflict occurs.
4. Use readable choice names and show which provider requires a session key. Key availability is a boolean status; credentials remain session-only and masked.
5. Stretch creation content, keep coordinate fields within its available width, and adapt parcel columns to actual content width.
6. Center the aspect-preserving parcel drawing in a clipped plot. Put wrapping legends outside the plot.
7. Default new Revit UI settings to `AreaCentroid`. This exact projected area centroid accounts for holes and multiparts, and is computed from the unbuffered legal parcel. Terrain extension cannot change the legal frame. Existing stored placement choices remain intact and can be changed through the visible Model position preference. Historical Core/CLI origin modes retain their semantics.

## Building zero and real-world elevations

The owner approved the legal parcel's area centroid at Revit's internal X/Y origin. They asked for research before choosing a default vertical placement: an architectural first-floor datum should remain zero while site contours retain their real-world elevations.

The [Revit 2027 datum research](../research/revit-2027-building-and-survey-datums.md) establishes the proposed native approach and cites Autodesk documentation and the installed SDK. Building levels use the project datum; site contour labels and spot elevations can use the Survey Point. The add-in must retain the source datum and complete inverse transform, and must never infer a design floor elevation from bare-earth terrain.

Recommended next implementation:

- Add **Building datum** to Review. Choose the unfinished first-floor level and its known real-world elevation, with the selected length units and declared vertical datum visible.
- Translate terrain and the legal property line together so that the confirmed floor datum corresponds to the chosen level's model elevation. Do not move existing levels or building geometry.
- If that elevation is unknown, allow a clearly labelled provisional datum; the owner accepted this option. Select a ground point beside the front door and add the operator's measured vertical rise from that grade point to the unfinished first floor. Record the selected point, DEM value and sampling method, entered rise and units, and estimated datum. Building outlines can help locate the point but remain optional; see the [outline research](../research/building-outlines-and-grade-point-selection.md). The proposed automatic source is Microsoft's versioned Global ML GeoJSONL tiles under CDLA-Permissive 2.0, read with managed HTTP/gzip/JSON and strict transfer/decompression caps. No operator file preparation belongs in the normal flow. Prototype tile-size limits before choosing a future fine-tile service. Allow later correction of SolidGround-owned elements together. This must never imply that a design floor's absolute elevation was surveyed.
- In a new, uncoordinated model, keep establishing survey coordinates an explicit per-run, default-off choice. Confirm the CRS, datum, units, and floor mapping before writing; verify and roll back under the existing transaction discipline.
- Preserve existing shared coordinates. Arbitrarily centering a parcel cannot also honor an unrelated established horizontal transform. Coordinated models need a deliberate placement-in-existing-coordinates or separate linked-site-model path; a floor offset alone cannot resolve that conflict.
- Provide SolidGround-owned annotation types or clear native instructions for survey-based site labels without modifying types already used by the building. Verify contour interval phase separately from label elevation base.
- Sample the unsimplified acquired grid at the selected ground pin. Use a documented cell-center/interpolation rule with valid support; reject NODATA or out-of-coverage support and ask the operator to move the pin. Do not silently extrapolate, bridge missing cells, or substitute a different grade point. Preserve the entered rise's physical unit separately from source/output coordinate units.
- Do not ship a promise that shifted geometry has correct native contour labels until the synthetic Revit 2027 matrix in the research note passes, including save/reopen and both foot definitions.

Vertical behavior is not changed by the layout/centering fix. The current source-height and explicit-origin modes retain their existing meaning while this design is completed and validated.

### Cohesive ground-selection experience

The owner's subsequent acceptance explicitly requires that ground selection feel comfortable to a human and that the whole tool remain cohesive and approachable. The [ground-selection UX requirements](../design-specs/revit-60-usability.md#11-owner-directed-ground-selection-ux-requirements--2026-10-01) make that requirement observable: a focused floor-reference task within the same Review shell, automatic placement readiness after an explicitly requested ground preview, an adjustable unsnapped pin, one clearly required measurement, visible provisional status, and a return to the final summary without stacked wizards or generic Next clicks. Preferences remain in Settings; this property's point and measurement remain run data.

Acceptance includes keyboard/coordinate alternatives, pan/zoom/resize stability, no-outline recovery, loading/cancellation without stale results, and novice observation of point correction and measurement interpretation. Existing first-use, repeat-use, theme, scaling, and Settings tasks remain part of the same formative round. The new UX criteria do not substitute for the acquisition/provenance design review or the native datum/annotation matrix, and this update implements no vertical or outline behavior.

## Terrain extension and subdivision

The extension changes only terrain acquisition, support, and its outer profile. The legal property line is invariant. Automatic subdivision and hiding have not shipped: the owner's manual method remains a separate native experiment. Validate whether hiding the host preserves the subdivision, whether view-specific hiding survives save/reopen, and whether subdivision changes the retained terrain surface before exposing a one-click parcel-only display option.

## Verification boundary

Computer Use is now authorized by the owner. The Windows native helper could not connect: its pipe was unavailable after initialization and one reset/retry. No Revit GUI inspection has therefore been performed for this follow-up. Local WPF arrangement and control tests do not establish native Revit, pixel-render, accessibility, or human usability acceptance. Keep those checks open and bind any later runtime session to the signed build identity.
