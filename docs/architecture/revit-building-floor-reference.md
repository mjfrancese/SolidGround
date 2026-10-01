# Building floor reference and exterior ground selection

Date: 2026-10-01. Issue #60 / draft PR #61. Source implementation; native and human acceptance remain open.

The owner authorized the [reviewed plan](../planning/prd-building-floor-reference.md) after accepting the [ground-selection UX](../design-specs/revit-60-usability.md#11-owner-directed-ground-selection-ux-requirements--2026-10-01). Review offers the floor reference in the same creation window and footer. The primary loads terrain, then confirms the reference and returns to Review for explicit creation. A usable exterior-ground pin reveals the required rise to the unfinished first floor. Known elevations display their input units and source datum. A provisional legal-ground median leaves the first-floor elevation unknown. Original source elevations remain an explicit alternative.

The optional vector outlines, pan/zoom/Fit, unsnapped movable pin, coordinate entry and keyboard movement support point selection. Enter in the map accepts the point; it cannot create terrain. Escape cancels a map gesture. Display approximations never enter source transforms or native geometry. Outlines do not identify a door, measure grade, establish floor height or provide legal building boundaries.

## Source and model elevations

The legal parcel area centroid remains the horizontal origin. Final source-frame height equals the chosen real-world floor/reference height minus the selected level's project elevation converted to the source unit. Terrain and the legal property line consume that same frame. The original elevation range, datum, raw ground sample and weighted support remain source data. The inverse reconstructs source elevations. Existing levels/building elements are not moved.

Strict bilinear sampling uses the unsimplified cell-centre lattice and only nonzero-weight support. Missing support, NODATA and points outside its domain are refused without snapping or extrapolation. Provisional references use valid unsimplified legal-clip cells, otherwise ordinary AOI cells; extension-only terrain and simplification do not determine the median. The population and odd/even arithmetic have versioned policies.

Prepared terrain is reused for pin, measurement and mode edits. Its private in-memory binding covers source file contents/sidecar presence, request/AOI/provenance, extension, units, simplification, document/target identities and plane, native tolerances and credential revision. Cancellation/revision guards reject late completions. Preparation and creation run authoritative Preflight, including the actual Revit.ini threshold. Preview opens no transaction. Managed acquisition and file hashing run away from the UI thread; SDK values are captured on its owning thread.

## Revit 2027 verification

Installed `RevitAPI.dll` file version `27.0.10.13` and `RevitAPI.xml` were inspected on 2026-10-01 before changing level reads. `P:Autodesk.Revit.DB.Level.ProjectElevation` returns project-origin elevation independently of Elevation Base; `P:Autodesk.Revit.DB.Level.Elevation` can report shared-origin elevation. Selection, target validation and placement metadata use `ProjectElevation`. Level IDs and `Element.UniqueId` retain durable identity. See [Autodesk's SDK entry point](https://aps.autodesk.com/developer/overview/revit-api) and the [2027 datum research](../research/revit-2027-building-and-survey-datums.md) for other verified members and primary product documentation.

Shared-coordinate writes remain explicit and default off. Provisional previews cannot establish them; estimated mappings require acknowledgement. Already-coordinated documents reject centered floor placement because floor height cannot establish a horizontal relation. The original-source path remains available. This increment does not modify contour/spot types or intervals, SiteLocation or existing coordinate authority. Native source-elevation labels still require the research note's contour/spot matrix; preserved source data alone is not native label proof.

## Outline context and persistence

The managed reader uses [Microsoft Global ML Building Footprints](https://github.com/microsoft/GlobalMLBuildingFootprints), release `2026-08-13`, under [CDLA Permissive 2.0](https://cdla.dev/permissive-2-0/). The [outline research](../research/building-outlines-and-grade-point-selection.md) records primary licence/manifest retrievals. Production pins manifest SHA-256 `8E479A213F6B9C4670CD80B036B06D6DEFEA613F1BB6B14417DB26E82AA137F2`, validates exact HTTPS hosts/release paths and disables redirects. Static coarse level-9 requests contain no address, parcel ID, API key or geometry.

Manifest, tile count, actual compressed/decompressed bytes, lines, features and geometry complexity are bounded. Gzip magic/decoding are checked independently of optional transport headers. Failure yields no partial outlines and a recoverable inline status. Source/release/licence/attribution/digest/tile identity is retained independently, including after choosing original source elevations; geometry remains transient. Fatal runtime exceptions propagate.

Export schema 6 adds strict floor/context data and retains earlier export meanings. Floor-aware identity v2 preserves the old stem and fingerprints floor semantics plus geometry, excluding retrieval dates, display names and build identity. Native schema v3 uses fresh GUID `ffeca007-c97b-4283-9ad1-c2c2f14d4141`, name `SolidGround_Provenance_Toposolid_V3`, and 66 fields: immutable v2 plus `floorReferenceJson` and `buildingOutlineJson`. At least one record is required; original-source context-only runs keep their existing frame and make no floor claim. Attachment retains recognition, readability, complete field comparison and inverse source-coordinate read-back before commit. Placement records are version 4.

The read-only scanner validates schemas, identity version/algorithm pairs, canonical enum tokens, hashes, stored identities, floor/frame correlation and native vertices before duplicate filtering. Multiply-versioned, malformed, copied, edited or conflicting terrain refuses before transaction. Legacy v1 needs explicit duplicate-risk acknowledgement. Scanning rewrites no entity.

## Verification boundary

Plan review passed after three rounds. Independent code reviews corrected malformed native identities, fatal outline recovery, historic-mode access, outline metadata retention and known-elevation unit labels. Core and arranged-WPF tests cover frame reversal, legal median, strict sampling, source limits, input binding, cancellation, actual bindings, measurements and pin/view behavior.

Final source checks on 2026-10-01: locked solution and Windows-test restores passed; the installed-SDK Release solution build passed with zero warnings/errors; Core passed 1,785 tests with 7 expected opt-in skips (1,792 total); Windows WPF passed 72 with 1 explicit renderer skip (73 total). The full Core lane also passed its personal-information checks. Final independent Core/native and host/UI reviews passed after the context-only and stale-edit corrections. The unpublished v3 shape was finalized before any native load; published v1/v2 were unchanged.

The local reference renderer is transparent, so its pixel test is explicitly skipped. The authorized Computer Use helper could not connect after initialization and a reset/retry. No Revit GUI, contour/spot, transaction/Undo, save/reopen, theme/scaling or novice observation result is claimed. Keep #60 / PR #61 open for those checks against the signed build.

Post-creation reference correction remains a separate atomic terrain/property-line/frame/fingerprint/identity/export/audit transaction. No inoperative edit action is shown. Automatic subdivision/hiding remains separately gated by native evidence.
