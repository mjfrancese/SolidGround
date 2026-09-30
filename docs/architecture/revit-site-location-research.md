# Revit SiteLocation CRS-identity and geolocation research

**Issue:** #51 (PH4-9)
**Date:** 2026-09-30
**Status:** Decision memo; no SiteLocation write is authorized by this note.

## Decision

The installed Revit 2027 API permits a narrowly-scoped future CRS-identity write through `SiteLocation.SetGeoCoordinateSystem(string)`, with read-back through the public `GeoCoordinateSystemId` or `GeoCoordinateSystemDefinition` properties. It does **not** permit a third-party add-in to call `SiteLocation.GetEPSGCode()` or `ProjectLocation.SetProjectedSpaceToLocalTrf`; both are compiled assembly-internal despite appearing in the installed XML documentation.

If the owner later authorizes a SiteLocation feature, treat the CRS identity and geographic latitude/longitude as separate opt-ins. This memo recommends the address geocode pin, not the parcel centroid, as the source for a future latitude/longitude write. It also recommends that no latitude/longitude write ship until an explicit non-overwrite policy has been accepted, because Revit offers no public, reliable "SiteLocation was customized" bit in this installed build. `TimeZone` remains read-only for the proposed slice.

## Metadata-only verification

On 2026-09-30, a temporary `System.Reflection.Metadata` / `PEReader` probe read the installed `C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll` directly. It did not load that assembly and did not launch Revit. The file reports `FileVersion=27.0.10.13`; its ECMA-335 assembly version is `27.0.10.0`; SHA-256 is `BB4A5B3DEC4E140527C311FBAFC2E676E34594330748BE79C0654A46D521CB89`. The same probe checked the installed `RevitAPI.xml` only for the named documentation-member entry, separately from compiled accessibility.

| Member | XML member entry | Compiled accessibility | Consequence |
| --- | --- | --- | --- |
| `SiteLocation.SetGeoCoordinateSystem(string)` | Present | `Public` | Callable by SolidGround; accepts an ID, EPSG-prefixed ID, WKT, or Autodesk XML according to its XML remarks. |
| `SiteLocation.GetEPSGCode()` | Present | `Assembly` | Not callable by a third-party add-in. Do not use it for verification. |
| `ProjectLocation.SetProjectedSpaceToLocalTrf(string, XYZ, double, XYZ, double, double)` | Present | `Assembly` | Not callable. The combined-scale-factor write remains out of scope. |
| `ProjectLocation.GetProjectPosition(XYZ)` / `.SetProjectPosition(XYZ, ProjectPosition)` | Present | `Public` | Existing shared-coordinate mechanism remains callable. |
| `SiteLocation.GeoCoordinateSystemId` / `.GeoCoordinateSystemDefinition` getters | Present | `Public` | Use one of these for a future CRS identity read-back. |
| `SiteLocation.Latitude`, `.Longitude`, `.TimeZone` getters and setters | Present | `Public` | Callable, but a future write needs a separately accepted overwrite policy. |
| `SiteLocation.SetBasicGeolocation`, `.IsGeolocated`, `.GetGeolocationBase` | Present | `Assembly` | Not callable and cannot be used as a customization detector. |

The installed XML exposes an additional documentation defect relevant to deferred work: `SetProjectedSpaceToLocalTrf` has six typed parameters in metadata, but its XML supplies descriptions for five. The undocumented position is the third parameter. Its inaccessibility is independently sufficient to keep the method out of scope; this memo makes no inference about that parameter's meaning.

## Geographic point recommendation

Use the selected address-geocode pin if, and only if, a future owner-approved geographic-location opt-in asks SolidGround to set latitude and longitude. It is the point the operator selected as the address match and has the semantics of a site address. A parcel polygon centroid is derived geometry, can materially differ from the street-address location, and says less about the intended site label.

The current dialog does retain `SelectedGeocodeCandidate.Latitude` and `.Longitude`, but its final `SolidGroundDialogResult` contains no coordinate fields. `AddressParcelProvenance` / `GeocodeProvenance` retain provider, query text, and attribution, not the raw selected coordinates. A future geocode-pin write therefore requires new, deliberately reviewed plumbing from the confirmed candidate to the transaction. It should not recover a coordinate from provenance text or silently fall back to a parcel centroid. A parcel-only path with no confirmed address pin should leave SiteLocation geographic coordinates untouched unless the owner later chooses a separate policy for it.

## Non-overwrite guard

`SharedCoordinatesDetector` only reads `ProjectPosition`, the survey-point position, and the project-location count. It never inspects `SiteLocation`, so it cannot protect a SiteLocation write.

For a CRS-identity-only feature, a conservative SiteLocation-aware guard can refuse when the public `GeoCoordinateSystemId` or `GeoCoordinateSystemDefinition` is nonempty, and verify the value after the write. That gives a concrete no-overwrite policy for a prior CRS assignment.

Latitude and longitude need a different decision. The accessible values themselves are insufficient to prove that an operator customized them: a new Revit template can already contain nonzero/default site coordinates, and the inaccessible `IsGeolocated` API cannot supply that evidence. Therefore a generic boolean detector would claim more certainty than the available API supports. Before any geolocation write ships, the owner should choose one explicit policy, such as an operator-facing confirmation that displays the current site values and permits replacement, or a CRS-only first slice that leaves latitude/longitude untouched. The existing shared-coordinate heuristic must not be reused as proof of SiteLocation ownership.

## Time zone

The installed XML says setting latitude or longitude causes Revit to attempt to update the time zone and notes that boundary cases can be incorrect. Although `TimeZone` is public, do not set it in a future first slice. It should remain read-only and, if latitude/longitude is later authorized, be logged/read back so an operator can see Revit's resulting value. Any correction policy is a separate owner decision.

## Owner follow-up required

The owner still needs to decide whether to authorize: (1) the small, CRS-identity-only opt-in alongside the existing shared-coordinate gate; (2) any latitude/longitude write at all; and, if so, (3) the explicit SiteLocation replacement/confirmation policy. This research issue does not make that decision, open a feature issue, or change code. `SetProjectedSpaceToLocalTrf` remains explicitly out of scope regardless of the choice.

## Sources

- Installed Revit 2027 SDK: `C:\Program Files\Autodesk\Revit 2027\RevitAPI.dll` and `RevitAPI.xml`, inspected 2026-09-30 by the metadata-only probe described above.
- Existing Revit 2027 version and API evidence: `docs/architecture/revit-2027-verification-and-host-design.md` and `docs/architecture/revit-property-line-and-shared-coordinates.md`.
- Current design context: `docs/architecture/phase-4-comparative-research.md` and Issue #51.
