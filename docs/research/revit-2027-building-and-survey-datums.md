# Revit 2027 building datum and survey-elevation placement

**Date:** 2026-10-01
**Status:** research and recommended design; no runtime test or product change is authorized by this note.

## Decision

Use two deliberately different vertical references in one model.

1. **Model datum:** the unfinished first-floor plane is `0` in the building's local/project geometry.  It is the reference for the building and for the imported terrain's *model-space* Z values.
2. **Source datum:** the terrain source's declared NAVD88 elevations remain intact in the terrain export and Extensible Storage provenance.  When the operator supplies the NAVD88 elevation of the intended first-floor plane, SolidGround can reconstruct each source elevation exactly from its model-space coordinate.

The approved legal-parcel-area centroid is the horizontal local origin.  More precisely, SolidGround should use the projected `(E_c, N_c)` of that centroid as the local X/Y origin; a polygon centroid has no inherent elevation, so it must not silently choose the vertical origin.  The vertical origin is the real-world NAVD88 elevation of the model datum, `H_F`, when known.

This produces a compact, useful model: the building can stay at a familiar unfinished first-floor elevation of zero, while terrain geometry is correctly above or below that floor and its actual NAVD88 values are retained and recoverable.  A DEM does **not** reveal the intended unfinished-first-floor elevation.  It may describe ground, roof, vegetation-removal artifacts, or a surface after grading; none establishes a design floor datum.

Do not represent this as a vertical-datum conversion.  Subtracting an operator-supplied NAVD88 height is a reversible translation within NAVD88.  Converting NAVD88 to another vertical datum requires a separately identified, authoritative transformation and its applicable geoid model; no Revit coordinate setting supplies that transformation.

### Approved provisional front door

The normal preview path may estimate the unfinished-first-floor height from a selected grade point and a user-entered rise.  Let `p_g` be a selected point on the acquired DEM grid, `H_g = DEM_NAVD88(p_g)` be the sampled/interpolated ground elevation, and `d_gf` be the signed measured rise from that grade point to the unfinished first-floor plane.  Then:

```text
H_F_estimated = H_g + d_gf
```

The operator enters `d_gf` in feet/inches, with a positive value for a floor above the selected grade and a negative value for a floor below it.  The UI records the entered value and unit, the selected source coordinate, the grid sampling/interpolation method, the sampled `H_g`, its available source uncertainty/quality statement, and `H_F_estimated` in provenance.  Set `verticalPlacementMode` to `estimated-grade-rise`; do not call it a surveyed or certified floor elevation.

The preferred selection is a grade point immediately beside the building footprint, on visible parcel background.  Bare-earth DEM data under a building can be artificially interpolated, void-filled, or represent terrain that no longer corresponds to finished grading, so it is poor evidence for the building floor.  Building outlines may assist selection when an authoritative outline becomes available, but they are not a creation prerequisite: the operator can select a parcel-background point or an existing Revit geometry reference.  Missing outlines must not block a provisional preview.

This estimate gives a practical building-relative terrain preview.  It does not alter the source heights: the inverse transform still reproduces DEM NAVD88 elevations, and a survey-based label can still report those source elevations only when the document's Survey Point mapping is explicitly established from the source.  The unproven part is the relationship of model `0` to the real unfinished floor.  A documented known-survey NAVD88 height remains the advanced `confirmed-first-floor-navd88` alternative.

## What Revit 2027 establishes

Autodesk distinguishes a project coordinate system for measurements near the model from a survey coordinate system for the real-world site.  It identifies the project base point as the project-system origin and the survey point as a real-world location near the model.  The internal origin underlies both systems.  These are coordinate frames and reference points; Autodesk does not describe them as named vertical datums such as NAVD88.  [About Coordinate Systems](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-E67ED082-2556-475B-84A7-4605329F612F) and [Project Base Point and Survey Point](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-68611F67-ED48-4659-9C3B-59C5024CE5F2) are the applicable Revit 2027 product documentation.

| Revit concept | Evidence-backed role | SolidGround consequence |
| --- | --- | --- |
| Internal origin | Basis of Revit's internal coordinate system.  Autodesk says its location never moves and all geometry should be within a 16 km (10 mi) radius of it, because far-away geometry becomes less reliable. | In a new local/site model, put the parcel centroid at internal X/Y `(0,0)`.  Do not place projected easting/northing directly in geometry. |
| Project base point | Origin/reference for the project coordinate system, appropriate for measurement near the building. | Keep the building's first-floor model datum at `0`; do not move the base point to manufacture a survey datum. |
| Survey point/shared coordinates | A real-world/survey reference.  Shared positioning is for the mutual positions of linked models and files. | It may expose this run's source origin in a new, uncoordinated model only under an explicit opt-in.  It must never overwrite an established project transform. |
| Level | A finite horizontal plane used as a reference for hosted building elements.  The API lets a level report/display from either the project or shared origin. | The selected level remains a host/reference, not evidence that its numeric elevation is NAVD88. |
| Toposolid | `Toposolid.Create` accepts 3-D points plus a planar profile and a level id. | The source-to-model Z translation controls the terrain; the level id does not establish source vertical-datum meaning. |

The distance limit is particularly important here.  The internal origin itself never moves; changing a base-point or survey-point reference changes the coordinate relationship, never the internal-origin location.  A geometry translation is therefore the appropriate way to keep a new local site small, rather than carrying projected easting/northing into model geometry.  This is directly stated by [About the Internal Origin](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-363D008B-69DF-4FAA-AA01-6BE9C10267A8), including its 16 km (10 mi) radius limit.

### Installed API evidence

The following was checked against the installed `C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll` and its shipped `RevitAPI.xml` on 2026-10-01: FileVersion `27.0.10.13`, assembly SHA-256 `BB4A5B3DEC4E140527C311FBAFC2E676E34594330748BE79C0654A46D521CB89`.  This was metadata/XML inspection only; no Revit process was launched.

| API member | Installed XML statement relevant here | Meaning |
| --- | --- | --- |
| `ProjectLocation.GetProjectPosition(XYZ)` | At `XYZ.Zero`, it returns the transformation values; for a transformed point it reports north/south, east/west, and elevation like **Report Shared Coordinates**. | A shared transform includes a vertical value as well as horizontal offsets and rotation. |
| `ProjectLocation.SetProjectPosition(XYZ, ProjectPosition)` | It is the API counterpart of **Specify Coordinates at Point**. | It is a consequential coordinate-system write, never a hidden import detail. |
| `ProjectPosition.EastWest`, `.NorthSouth`, `.Elevation`, `.Angle` | E/W, N/S, and elevation are decimal feet; angle is project-north versus true-north radians. | A SolidGround shared-coordinate write is four-dimensional placement metadata, not merely an X/Y label. |
| `Level.Elevation` | With Elevation Base **Project**, it is relative to the project origin; with **Shared**, it is relative to the shared origin.  Value is decimal feet. | The numeric level label can change its base.  It cannot, by itself, certify a vertical datum. |
| `Level.ProjectElevation` | Always relative to the project origin. | Use this when the task specifically needs model Z independent of the level's display base. |
| `Toposolid.Create(Document, IList&lt;CurveLoop&gt;, IList&lt;XYZ&gt;, ElementId, ElementId)` | Creates a toposolid from planar profile loops, top-face points, type id, and level id. | The API surface gives no NAVD88/NGVD29 datum argument. |
| `Document.NewSpotElevation` / `NewSpotCoordinate` | Both evaluate a reference point (projecting to it if necessary). | They annotate geometry; the source datum represented by the displayed number depends on the selected annotation base/settings. |

The current add-in already follows the mechanical half of this design: `LocalCoordinateFrame.ToLocal` subtracts an explicit X/Y/Z origin then converts each axis; `BoundaryGeometryBuilder.BuildPoints` converts all three local components through `UnitUtils`; its boundary profile is placed at the minimum retained terrain Z, independently of the selected level.  Its default-off shared-coordinate path writes the frame origin through `ProjectPosition` and verifies it.  See `src/SolidGround.Core/Transformations/LocalCoordinateFrame.cs`, `src/SolidGround.Revit/Geometry/BoundaryGeometryBuilder.cs`, and `src/SolidGround.Revit/Transactions/SharedCoordinatesGate.cs`.

## Recommended coordinate contract

Let:

- `(E, N, H)` be one retained terrain sample in its declared projected horizontal CRS and NAVD88 vertical datum;
- `(E_c, N_c)` be the legal parcel area's centroid in that same projected CRS;
- `H_F` be the selected NAVD88 elevation used to align the unfinished-first-floor model datum: either a documented known-survey value or `H_F_estimated`; and
- `u_h` and `u_v` be metres per source horizontal and vertical unit, while `u_o` is metres per SolidGround output unit.

The source-to-local conversion is:

```text
x_local = ((E - E_c) * u_h) / u_o
y_local = ((N - N_c) * u_h) / u_o
z_local = ((H - H_F) * u_v) / u_o
```

These equations describe a new model whose chosen floor plane is internal Z zero. If an existing chosen level has a different geometric height, include that target height in the transform rather than moving the level or building: `z_internal = target_floor_z_internal + (H - H_F) * u_v / metres_per_internal_unit`. Its project-based level label may be zero while its internal Z is nonzero. Read and verify the actual level plane in Revit; a displayed level number or a Survey Point-based `Level.Elevation` is not a safe substitute. Persist both the target plane and the resolved source-origin translation.

`UnitUtils.ConvertToInternalUnits` then converts each local coordinate using the selected explicit output unit before it enters Revit geometry.  The inverse is:

```text
E = E_c + (x_local * u_o) / u_h
N = N_c + (y_local * u_o) / u_h
H_NAVD88 = H_F + (z_local * u_o) / u_v
```

Persist `E_c`, `N_c`, `H_F`, projected CRS identity and units, the source vertical datum (`NAVD88`), geoid/source declaration, output-unit definition, transformation version, and a clear `verticalPlacementMode`.  The latter should distinguish `confirmed-first-floor-navd88` from `estimated-grade-rise`; it is not cosmetic provenance because the same mesh Z values have materially different interpretation under the two modes.

The unit definitions are exact: one U.S. survey foot is `1200/3937 m`; one international foot is `0.3048 m`.  The installed API identifies `UnitTypeId.UsSurveyFeet` separately from `UnitTypeId.Feet` and documents that `UnitUtils.ConvertToInternalUnits` converts a value from its supplied unit to Revit internal units.  That supports explicit conversion of terrain geometry; it does **not** by itself prove that an external CRS coordinate, converted by `UnitUtils`, is the correct absolute value for an existing shared-coordinate system.  A shared coordinate system first needs a named source-to-shared transform and project authority.  A Revit 2027 runtime test must cover both feet: identical physical offsets expressed in either output unit must create equal internal XYZ values, reconstruct the same metre/NAVD88 source sample, and round-trip through `GetProjectPosition` without a 2-ppm substitution.

### New model: explicit, optional shared coordinates

For a demonstrably new/uncoordinated document, present a separate opt-in after the operator has selected `H_F` through either path.  It is valid only when the operator accepts the source CRS as the project's shared-coordinate authority and its physical units and orientation are known:

```text
geometry origin (internal):  (E_c, N_c, H_F) -> (0, 0, 0)
shared position at XYZ.Zero: source-to-shared transform maps (E_c, N_c, H_F) to selected ProjectPosition values
```

This makes the building's unfinished first floor zero in project geometry while the shared position can report the real-world point represented by that zero.  The present identity/zero-angle mapping is a proposed new-model convenience, not a general coordinate transformation: it is defensible only when the horizontal source CRS and vertical datum are displayed in the confirmation, the source is accepted as the project's coordinate authority, and the write/read-back succeeds.  Zero angle retains SolidGround's existing no-grid-convergence policy; it must be stated as such, never implied to be a survey-grade orientation.

The current `SharedCoordinatesDetector` is intentionally a conservative heuristic, not Autodesk-certified proof that a model has never been coordinated.  That is already the correct baseline: it defaults off, refuses a model that appears coordinated, and prior live evidence showed `SetProjectPosition` also moves the survey point.  This research does not widen that write permission.

### Existing shared coordinates: preserve them

Do not call `SetProjectPosition`, move either point, reset shared coordinates, acquire/publish coordinates, or alter `SiteLocation` when the project already has a custom/shared transform.  Autodesk explicitly describes shared coordinates as the relationship needed to keep linked files mutually positioned, and recommends deriving them from one authoritative file.  [Shared Positioning](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-049BE99D-249F-4D1F-A79C-A348955AB49C) and [About Shared Coordinates](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-B82147D6-7EAB-48AB-B0C3-3B160E2DCD17) support that restriction.  Autodesk also warns that a Forma geolocation action removes shared-coordinate relationships, a reason not to treat a geolocation change as an innocent supplement.  [About Geolocation and Shared Coordinates](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-1E6748DE-0C9E-4D5D-97FE-777F14EB6D87).

An already coordinated building file cannot in general both preserve its authoritative current placement and put an arbitrary parcel centroid at internal `(0,0)`.  The latter would move the imported terrain relative to the building unless that centroid already maps to the internal origin under the established transform.  `H_F` resolves only the vertical offset; it does not establish the missing horizontal source-CRS-to-host transform.

For an existing model, SolidGround needs either (a) an authoritative transform or surveyed tie points from the terrain source CRS to the existing Revit frame, then it must create terrain at that resulting host placement without changing project coordinates, or (b) a dedicated site model/link whose relationship is established by the project team.  Autodesk's prescribed coordination workflow is consistent with that division: a building model acquires coordinates from a linked site model, while a site model publishes to linked buildings; acquiring makes the linked model's coordinates the host shared system.  [Acquiring and Publishing Coordinates](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-DD00B14D-891D-42A1-BE79-E75B0C69E122).  Autodesk also describes named positions as placements of a model instance and explicitly gives importing a building into a site model and moving it there as the route for assessing positions.  [Defining Named Positions](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-DB7B3853-AE5B-47CC-921C-5C962ACA8C22).  A dedicated site file is not a substitute for the missing authority; it is the appropriate model boundary once that relationship is supplied.

If neither source-to-host authority nor tie points exist, do not claim an aligned existing-model terrain result and do not create the terrain in the building file.  Preserve the declared NAVD88 source elevation in provenance.  A Revit annotation configured to use the existing survey base can be called NAVD88 only when project authority has established that its survey reference is NAVD88.

## Labels, contours, and spot elevations

The Revit 2027 product help answers the display-base question directly.  **Contour Label Type Properties** says a contour label's **Elevation Base** is either **Project Base Point**, for an elevation with respect to the project origin, or **Survey Point**, for an elevation with respect to the fixed survey point.  [Contour Label Type Properties](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-945D511C-CE83-41B6-88FC-2CD940EB2A6C).  **Spot Elevation Type Properties** adds **Relative**, whose value is with respect to the level selected by that instance's **Relative Base**, alongside the Project Base Point and Survey Point choices.  [Spot Elevation Type Properties](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-7FD921FB-4B58-4D8B-9F43-545657447220).  A spot elevation is supported on a toposolid.  [About Spot Elevations](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-91F9D5B3-5C83-4FA2-B40B-8329874071F4).

Consequently, lowering terrain so the unfinished first floor is model `0` does **not** prevent native survey-based labels.  With a confirmed shared/survey mapping that declares the fixed survey point's elevation in NAVD88, select **Survey Point** for the contour-label type and spot-elevation type: their reported elevations are then the model values expressed with respect to that fixed survey reference.  The displayed number may be captioned NAVD88 only when that mapping is a documented NAVD88 mapping.  Revit's Survey Point is not inherently NAVD88; without that evidence, the same type reports only the project's survey-reference elevation.  Project-base labels instead describe the local building datum, and Relative spot elevations can intentionally describe the unfinished-floor/level datum.

Contour **placement** and contour **label base** are separate concerns.  The installed 2027 XML shows that a `ToposolidType` owns a `ContourSetting`; each `ContourSettingItem` has start, stop, and step elevations, and range/single-item modes.  These are the settings used to draw contours.  The product help's **Toposurface Settings** page documents the analogous legacy interval behavior: `Passing Through Elevation` chooses the contour-series origin (for example, interval 10 with pass-through 5 gives ..., -5, 5, 15, ...), while the label-type page determines how those contour elevations are reported.  [Toposurface Settings](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-F56B56F0-E729-4A8B-A4CD-9664EF0EAC6D).  Do not transfer the legacy `Passing Through Elevation` UI to toposolids without a Revit 2027 runtime check; use the toposolid `ContourSetting` API/UI for the actual type.

This resolves the documented label-base choice, but a small native check is still required before committing default intervals, rounding, or a NAVD88 caption.  It must confirm the exact sign/read-back of the selected shared mapping and the actual toposolid contour series after a nonzero `H_F` translation.  This is runtime confirmation of the selected project mapping, not an unresolved question about the documented label bases.

Before a contour/spot setting is exposed as a SolidGround guarantee, run this small Revit 2027 manual evidence matrix in a disposable model:

| Test | Expected observation to record, not assume |
| --- | --- |
| Create a synthetic toposolid with two known model Z values, an unfinished-first-floor level at project `0`, and project/shared transforms initially equal. | Contour label value, spot elevation value, spot coordinate elevation, and `Level.Elevation`/`.ProjectElevation`. |
| Change only the shared `ProjectPosition.Elevation` by a known nonzero amount. | The documented survey-based contour/spot labels change by the expected direction and amount while geometry stays fixed; project-base and Relative labels have their documented bases. |
| Set contour labels to Project Base Point and Survey Point; use Project Base Point, Survey Point, and Relative spot types. | Displayed values, each saved setting, and the Relative Base level. |
| Save/reopen; then repeat with a U.S.-survey-foot and international-foot display/project-unit configuration. | Labels remain consistent in physical metres; no 2-ppm foot-definition substitution. |
| Attach SolidGround provenance that declares NAVD88 and a synthetic `H_F`. | The inverse equation recreates the two known source heights; only a label whose tested base is the matching shared mapping may be captioned "NAVD88". |

This is intentionally a native runtime evidence task, not a file-inspection claim.  No such run was performed for this note.

## Operator experience

Use one short **Vertical placement** step after parcel confirmation and before acquisition/transaction.

**New or uncoordinated project**

- Show that the parcel centroid will become local X/Y `(0,0)` and the unfinished first floor will remain model elevation `0`.
- Default to **Estimated grade-rise preview**: select a nearby parcel-background grade point, show its source NAVD88 grid elevation, and collect a signed measured grade-to-unfinished-floor rise in feet/inches.  Show the resulting `H_F_estimated` and provenance/uncertainty disclosure before creation.
- Offer **Known survey elevation** as the advanced alternative for a documented unfinished-first-floor NAVD88 height.
- Keep “write this origin as shared coordinates” separate and initially off.  An estimated floor height alone does not establish coordinate authority for that write.

**Existing coordinated project**

- Show the current `ProjectPosition` values as read-only context and state that SolidGround will not change shared coordinates, project base point, survey point, SiteLocation, or links.
- Require an authoritative source-to-host horizontal transform or surveyed tie points before creating terrain.  Show `H_F` as a separate vertical input only after this placement authority exists; it cannot repair an unknown horizontal relation.
- After horizontal placement authority exists, offer the same estimated grade-rise preview or the documented known-survey elevation.  Record the estimate and warning in the placement record/export.
- Do not ask the operator to identify an arbitrary survey-point number as NAVD88.  Instead allow an explicit attestation such as “the existing survey reference is NAVD88 and this model's Level 1 equals the unfinished-first-floor datum,” if a later feature needs NAVD88-formatted native labels.  That is a meaningful project-authority decision, not a recoverable fact from a DEM.
The UI should say "source elevation: NAVD88" and "model elevation: relative to unfinished First Floor 0" side by side.  It should never state "converted to NAVD88," "survey accurate," or "absolute" solely because it wrote an elevation into `ProjectPosition`.

## Tests and acceptance checks for a later implementation

1. **Centroid:** legal parcel centroid maps to local `(0,0)` within the Core coordinate tolerance; holes/multipart centroid behavior uses the existing legal-area rule and is recorded.
2. **Floor:** a synthetic NAVD88 sample exactly at `H_F` maps to model `Z=0`; samples above/below map with sign preserved.  For an estimated value, prove `H_F_estimated = H_g + d_gf` for signed feet/inches input and retain the sampling/interpolation record.
3. **Inverse/provenance:** for varied points, `ToSource(ToLocal(point))` returns the original projected coordinates and NAVD88 elevation under metre, U.S.-survey-foot, and international-foot output choices.
4. **No false datum claim:** `estimated-grade-rise` cannot be described as surveyed/certified unfinished-floor elevation and cannot enable a shared-coordinate write without independent coordinate authority.  It preserves source NAVD88 values and their provenance.
5. **Existing transform:** a coordinated document produces no changes to `ProjectLocation`, survey point, base point, SiteLocation, or link placement.  It rejects creation without a source-to-host transform/tie points, and with those inputs its terrain obeys the selected confirmed or estimated unfinished-floor offset.
6. **New-project opt-in:** accepted write is transaction-scoped, read back through `GetProjectPosition(XYZ.Zero)`, logs/persists the source-to-shared transform, units, CRS/datum, and angle; refusal/failure rolls back.
7. **Annotations:** complete the runtime matrix above before committing intervals/rounding or a NAVD88 caption.  The Project Base Point, Survey Point, and Relative base semantics are already documented.

## Subdivision display note

No subdivision behavior is proposed here.  The installed 2027 API exposes `Toposolid.CreateSubDivision`, and existing usability research observed a host/subdivision display question, but this review found no authoritative Revit 2027 product/API statement that establishes a general "hide host when subdivision exists" rule.  Treat that as a separate visual/runtime research item; do not couple it to datum placement.

## Decisions still requiring owner input

1. **New-project coordinate authority:** Is the source projected CRS plus declared NAVD88 elevation authoritative enough to allow the existing, default-off shared-coordinate write after explicit confirmation, or should the first implementation retain provenance only?  Recommendation: keep the current default-off write, gated by a confirmation that names CRS, NAVD88, units, the selected `H_F` source, and zero rotation.
2. **Native label guarantee:** Should SolidGround offer NAVD88 contour/spot labels only after the operator/project authority attests that the Survey Point mapping is NAVD88 and the runtime matrix records the configured result?  Recommendation: yes; use Survey Point types for that explicit path, keep the default label/caption neutral, and always retain true source values in provenance/export.

## Sources

- Autodesk, [About Coordinate Systems, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-E67ED082-2556-475B-84A7-4605329F612F), retrieved 2026-10-01.
- Autodesk, [Project Base Point and Survey Point, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-68611F67-ED48-4659-9C3B-59C5024CE5F2), retrieved 2026-10-01.
- Autodesk, [Best Practices: Project Base Point and Survey Point, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-AFCA59C6-9E00-4576-BCA0-63EB3342B68C), retrieved 2026-10-01.
- Autodesk, [About the Internal Origin, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-363D008B-69DF-4FAA-AA01-6BE9C10267A8), retrieved 2026-10-01.
- Autodesk, [About the Maximum Distance Limit, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-3F79BF5A-F051-49F3-951E-D3E86F51BECC), retrieved 2026-10-01.
- Autodesk, [Shared Positioning, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-049BE99D-249F-4D1F-A79C-A348955AB49C), [About Shared Coordinates](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-B82147D6-7EAB-48AB-B0C3-3B160E2DCD17), and [Reporting Shared Coordinates](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-0E5B4C8E-27CE-4EBC-9EFF-6E238100DA00), retrieved 2026-10-01.
- Autodesk, [Acquiring and Publishing Coordinates, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-DD00B14D-891D-42A1-BE79-E75B0C69E122), [Workflow: Using Shared Coordinates, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-70C407EB-AFCF-4EB3-A959-CDC5BFBFA97C), and [Defining Named Positions, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-DB7B3853-AE5B-47CC-921C-5C962ACA8C22), retrieved 2026-10-01.
- Autodesk, [About Levels](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-10A920FD-1A4C-457B-8827-D36AD7EA2D23) and [Level Instance Properties](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-5F22D076-345B-4539-9362-483CF898A77E), retrieved 2026-10-01.
- Autodesk, [Contour Label Type Properties, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-945D511C-CE83-41B6-88FC-2CD940EB2A6C), [Spot Elevation Type Properties, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-7FD921FB-4B58-4D8B-9F43-545657447220), and [About Spot Elevations, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-91F9D5B3-5C83-4FA2-B40B-8329874071F4), retrieved 2026-10-01.
- Autodesk, [Label Contour Lines, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-1F31024B-6209-4FFD-8B68-515E701620AD) and legacy [Toposurface Settings, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-F56B56F0-E729-4A8B-A4CD-9664EF0EAC6D), retrieved 2026-10-01.
- Autodesk, [About Geolocation and Shared Coordinates, Revit 2027](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-1E6748DE-0C9E-4D5D-97FE-777F14EB6D87), retrieved 2026-10-01.
- Installed Autodesk Revit 2027 SDK, `RevitAPI.dll` FileVersion `27.0.10.13` and `RevitAPI.xml`, members named in the installed-API table, inspected 2026-10-01 (metadata and XML only).
- Existing SolidGround evidence: `docs/architecture/revit-property-line-and-shared-coordinates.md`, `docs/architecture/revit-toposolid-creation.md`, and `docs/architecture/coordinate-transformation-and-units.md`.

### Retrieval record

Primary Autodesk pages were retrieved with Firecrawl on 2026-10-01.  The local, ignored evidence captures are `.firecrawl/sg-contour-types-2027.md` (title **Contour Label Type Properties**, GUID `945D511C-CE83-41B6-88FC-2CD940EB2A6C`, Elevation Base at line 97), `.firecrawl/sg-spot-types-2027.md` (title **Spot Elevation Type Properties**, GUID `7FD921FB-4B58-4D8B-9F43-545657447220`, Elevation Origin at line 125), `.firecrawl/sg-label-contours-2027.md`, `.firecrawl/sg-define-site-settings-2027.md`, `.firecrawl/autodesk-2027-acquire-publish-coordinates.md`, `.firecrawl/autodesk-2027-defining-named-positions.md`, and `.firecrawl/autodesk-2027-internal-origin.md`.  GUIDs and page titles above were taken from those retrieved Revit 2027 pages, not inferred from an older URL.
