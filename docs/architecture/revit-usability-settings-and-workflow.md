# Revit usability, settings, and terrain extents

Date: 2026-09-30. Issues #60, #36, #38, #52, and #53. Implementation/evidence record; runtime results are pending.

The owner accepted the researched usability plan and all Phase 4A children on this date. The accepted D1–D4 changes are now in `AGENTS.md`: two ribbon commands, per-user UI settings with read-only legacy import, masked session-only key overrides, and a local Windows WPF test lane. Legal property geometry is unbuffered; only terrain receives the horizontal context margin. No automatic SiteLocation write or new TIN algorithm is authorized by the decision-only issues.

UI settings reside under the caller's LocalApplicationData folder. Logs stay in CommonApplicationData. Revit installation/add-in paths remain unchanged. Saving uses strict UTF-8/System.Text.Json, a named mutex, expected SHA-256 digest comparison, and same-directory atomic replacement. Cancel leaves persisted bytes unchanged. Corrupt/future settings require explicit recovery; a legacy import rebases paths against its own folder and does not modify legacy bytes. Shared-coordinate opt-in is run-only and starts false. Settings contain no keys, address history, or document element IDs.

## Verified Revit 2027 API surfaces

Before implementation, the installed Revit 2027 SDK XML files (API 27.0.10.13) were inspected on 2026-09-30. `RevitAPIUI.xml` declares `PushButtonData(string,string,string,string)`, `RibbonPanel.AddItem`, `UIApplication.MainWindowHandle`, `UIThemeManager.CurrentTheme`, `TaskDialog.FooterText`, and `UIDocument.ShowElements(ElementId)`/collection overloads. Existing manual transaction/command policy is retained. `RevitAPI.xml` declares `SlabShapeEditor.SlabShapeVertices` and `SlabShapeVertex.Position`, permitting individual point verification after regeneration.

The same SDK declares combined planar-profile `Toposolid.Create`, independent `PropertyLine.Create`, `Toposolid.CreateSubDivision(Document,ElementId,IList<CurveLoop>)`, `Toposolid.HostTopoId`, `View.HideElements`/`UnhideElements`, and `Element.CanBeHidden`/`IsHidden`. These declarations establish API availability, not successful subdivision visibility or surface fidelity. Subdivision automation remains validation-gated and Full context remains the default.

The authoritative entry points are [Autodesk's Revit SDK](https://aps.autodesk.com/developer/overview/revit-api) and [Revit 2027 help](https://help.autodesk.com/view/RVT/2027/ENU/). No earlier-version example substitutes for 2027 verification.

## Runtime evidence boundary

Computer use remains off at the owner's direction. Automated local WPF checks can verify real binding and rendering behavior without driving Revit. Model transaction, Undo, native geometry, subdivision, and save/reopen evidence must use the documented signed build/deploy/restart/hash verification and a Revit 2027 manual session. Code inspection and offline geometry tests do not satisfy that runtime boundary. No such runtime result is claimed here.
