# Revit usability, settings, and terrain extents

Date: 2026-09-30. Issues #60, #36, #38, #52, and #53. Implementation/evidence record; runtime results are pending.

The owner accepted the researched usability plan and all Phase 4A children on this date. The accepted D1–D4 changes are now in `AGENTS.md`: two ribbon commands, per-user UI settings with read-only legacy import, masked session-only key overrides, and a local Windows WPF test lane. Legal property geometry is unbuffered; only terrain receives the horizontal context margin. No automatic SiteLocation write or new TIN algorithm is authorized by the decision-only issues.

UI settings reside under the caller's LocalApplicationData folder. Logs stay in CommonApplicationData. Revit installation/add-in paths remain unchanged. Saving uses strict UTF-8/System.Text.Json, a named mutex, expected SHA-256 digest comparison, and same-directory atomic replacement. Cancel leaves persisted bytes unchanged. Corrupt/future settings require explicit recovery; a legacy import rebases paths against its own folder and does not modify legacy bytes. Shared-coordinate opt-in is run-only and starts false. Settings contain no keys, address history, or document element IDs.

## Verified Revit 2027 API surfaces

Before implementation, the installed Revit 2027 SDK XML files (API 27.0.10.13) were inspected on 2026-09-30. `RevitAPIUI.xml` declares `PushButtonData(string,string,string,string)`, `RibbonPanel.AddItem`, `UIApplication.MainWindowHandle`, `UIThemeManager.CurrentTheme`, `TaskDialog.FooterText`, and `UIDocument.ShowElements(ElementId)`/collection overloads. Existing manual transaction/command policy is retained. `RevitAPI.xml` declares `SlabShapeEditor.SlabShapeVertices` and `SlabShapeVertex.Position`, permitting individual point verification after regeneration.

The same SDK declares combined planar-profile `Toposolid.Create`, independent `PropertyLine.Create`, `Toposolid.CreateSubDivision(Document,ElementId,IList<CurveLoop>)`, `Toposolid.HostTopoId`, `View.HideElements`/`UnhideElements`, and `Element.CanBeHidden`/`IsHidden`. These declarations establish API availability, not successful subdivision visibility or surface fidelity. Subdivision automation remains validation-gated and Full context remains the default.

The authoritative entry points are [Autodesk's Revit SDK](https://aps.autodesk.com/developer/overview/revit-api) and [Revit 2027 help](https://help.autodesk.com/view/RVT/2027/ENU/). No earlier-version example substitutes for 2027 verification.

The completion presenter also uses the verified `TaskDialog.AddCommandLink` overload, `TaskDialogResult.CommandLink1/2`, `TaskDialogCommonButtons.Close`, and `UIDocument(Document)` constructor from that same 2027 SDK. Optional framing and folder actions run after commit and cannot turn a committed creation into a failed result. Native failure dialogs link to the OpenTopography portal's myOpenTopo key workflow; [OpenTopography's OpenAPI contract](https://portal.opentopography.org/apidocs/openapi.json) identifies that workflow and the distinct academic/enterprise USGS 1 m entitlement requirement. Native link rendering/framing is pending the manual runtime checks, an explicit alternative to offline tests of Revit-only calls.

## Runtime evidence boundary

Computer use remains off at the owner's direction. Automated local WPF checks verify actual bindings, control properties, and state changes without driving Revit. This environment's reference visual renders entirely transparent, so the pixel-render check is explicitly skipped and supplies no visual proof. Model transaction, Undo, native geometry, subdivision, and save/reopen evidence must use the documented signed build/deploy/restart/hash verification and a Revit 2027 manual session. Code inspection and offline geometry tests do not satisfy that runtime boundary. No such runtime result is claimed here.

The parcel preview uses a separate display-only WGS 84 local metric approximation: the solid legal outline stays fixed while the dashed terrain outline reflects the configured margin. Parts, holes, and topology count changes are shown. This approximation never enters acquisition, clipping, native placement, or provenance. The exact terrain boundary is derived in the authoritative raster CRS after download; the pre-fetch estimate separately uses the conservative source-request envelope.
