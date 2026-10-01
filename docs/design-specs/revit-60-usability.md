# Revit 2027 usability overhaul: visual and interaction specification

Date: 2026-09-30
Status: implementation design for approved Issue [#60](https://github.com/mjfrancese/SolidGround/issues/60) and its Phase 4A children. This document specifies the proposed UI. It is not runtime evidence, a claim that a Revit API behavior has been observed, or a replacement for the PRD's planned validation.

## 1. Scope and design contract

This specification realizes the approved path in [the usability PRD](../planning/prd-revit-usability.md): **Location → Parcel → Review**, with a separate **Settings** tool. It applies to the existing managed WPF Revit 2027 add-in only. It does not add a terrain algorithm, a source provider, persistent credential storage, browser/map content, automatic model save, or a background document mutation.

All examples below are synthetic. `Sample site`, `Parcel SG-017`, `Example County`, and all displayed values are placeholder UI content, never sample customer or property data.

The following rules are invariant throughout the design:

| Rule | UI consequence |
| --- | --- |
| The legal parcel and terrain extent are different objects | Parcel identity, legal area, PropertyLine, and parcel preview use the unbuffered parcel. Terrain extension changes only the terrain outline, fetch estimate, and terrain summary. |
| Find is a lookup, not a creation action | It makes no elevation request and never changes the Revit document. |
| A candidate is not a confirmation | A user explicitly chooses an ambiguous location and explicitly uses a parcel. A nearby parcel is never silently accepted. |
| Settings are preferences; Review is a run confirmation | Settings owns terrain extension and other saved preferences. Review reports their effective values, links back to Settings, and owns document-dependent choices and the current-run shared-coordinate opt-in. |
| Creation remains authoritative in the command | The UI may warn about invalid settings or native thresholds, but command-time Preflight and transaction/rollback behavior remain the final gate. |
| Sources and rights are truthful | Attribution, license/disclaimer, coverage, and USGS 1 m access limits stay visible. A denial does not become a lower-resolution success. |

## 2. Window frame and responsive layout

Create and Settings share one shell and one semantic resource set.

| Metric | Specification |
| --- | --- |
| Default size | 760 × 640 device-independent pixels (DIP). |
| Minimum size | 640 × 480 DIP. The window is resizable and constrained to the active work area. |
| Outer padding | 24 DIP horizontally and 20 DIP vertically; use 16 DIP in the compact layout. |
| Control rhythm | 8 DIP between related controls, 16 DIP between groups, 24 DIP between major sections. Minimum interactive height: 32 DIP. |
| Header | 72 DIP: title, a plain-language current-stage label, and a compact stage indicator. |
| Content | One vertical scroll region. The footer never scrolls and always stays reachable without horizontal scrolling. |
| Footer | 64 DIP, top border, 16 DIP inner padding. Back/Cancel stay left; the single enabled primary command stays right. Primary buttons are at least 132 DIP wide. |
| Wide content | At 700 DIP or wider, Parcel's candidate list and details/preview use a 55/45 two-column split with a 16 DIP gap. |
| Compact content | Below 700 DIP, details and preview stack below the candidate list. Below 640 DIP, long summaries wrap; no essential label, unit, or command moves into a tooltip. |
| Display scaling | At 1280 × 720 at 100%, and 1920 × 1080 at 150% and 200%, the footer remains visible without horizontal scrolling. Content can scroll vertically. |

Shared creation shell:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│ SolidGround — Create terrain                              [Settings]     │  72
│  ● Location ───── ○ Parcel ───── ○ Review                               │
├──────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  Scrollable stage content                                               │
│                                                                          │
├──────────────────────────────────────────────────────────────────────────┤
│ [Back]  [Cancel]                                      [Primary action]  │  64
└──────────────────────────────────────────────────────────────────────────┘
  24 px inset
```

`Back` is hidden on Location rather than disabled. `Cancel` is always available before creation and cancels a pending lookup before closing the command. The primary command changes only with the current stage: **Find**, **Use this location**, **Use this parcel**, or **Create toposolid**. There is never a generic `Next` command.

## 3. Create-terrain flow

### 3.1 Location

Location defaults to Address. Coordinates is an explicit alternative; a pasted coordinate pair remains accepted through the existing parser. The form displays fields only for the chosen mode, so an operator is never asked to reconcile address and coordinate inputs.

```text
┌ SolidGround — Create terrain                              [Settings] ┐
│  ● Location ───── ○ Parcel ───── ○ Review                            │
├──────────────────────────────────────────────────────────────────────┤
│ Find a location                                                      │
│ A location is used only to find and confirm a parcel.                │
│                                                                      │
│  (•) Address     ( ) Coordinates                                    │
│                                                                      │
│  Street address                                                      │
│  [ Sample site                                                ]      │
│  Enter an address or switch to coordinates.                          │
│                                                                      │
│  Other area options ▸                                               │
│  Use a bounding box, point and radius, or a local polygon instead.  │
│                                                                      │
│  Setup status: Parcel source needs configuration. [Configure source]│
├──────────────────────────────────────────────────────────────────────┤
│ [Cancel]                                                    [Find]   │
└──────────────────────────────────────────────────────────────────────┘
```

Coordinates mode replaces the address input with separately labeled **Latitude** and **Longitude** fields, each with an adjacent validation message. A valid pasted pair fills those same fields and remains reviewable. Address, coordinate, or source changes invalidate confirmed location, selected parcel, preview, and Create readiness immediately; unrelated run choices remain intact.

The collapsed **Other area options** exposes the existing bounding box, point/radius, and local GeoJSON/WKT area paths. Its language says that it is an alternative area, not a parcel, and Review omits PropertyLine creation for a nonparcel area. It is never selected automatically after a geocode or parcel lookup failure.

### 3.2 Parcel

Successful unambiguous geocoding advances directly to Parcel. When there are multiple geocode candidates, the first decision in this stage is location selection; no parcel query starts until **Use this location** is chosen. With one resolved location, parcel lookup starts automatically and the stage announces the change.

```text
┌ SolidGround — Create terrain                              [Settings] ┐
│  ○ Location ───── ● Parcel ───── ○ Review                            │
├──────────────────────────────────────────────────────────────────────┤
│ Confirm a parcel                                                     │
│ Location: Sample site · coordinates resolved by selected geocoder    │
│ [Edit location]                                                      │
│                                                                      │
│ Select a parcel                                                      │
│ ┌──────────────────────────────┐  ┌────────────────────────────────┐│
│ │ ● Parcel SG-017              │  │ Boundary preview               ││
│ │   Contains selected location │  │  ┌──────────────────────────┐  ││
│ │   1.24 synthetic area units  │  │  │     terrain outline      │  ││
│ │                              │  │  │    ┌──────────────┐      │  ││
│ │ ○ Parcel SG-018              │  │  │    │ legal parcel ●│      │  ││
│ │   Nearby — 18 display units  │  │  │    └──────────────┘      │  ││
│ └──────────────────────────────┘  └────────────────────────────────┘│
│                                                                      │
│ Selected: Parcel SG-017                                              │
│ Identifier, legal area, source, and actual attribution/license text │
│ [Details and source terms ▸]                                         │
│                                                                      │
│ [Configure parcel source]  [Choose local boundary]                   │
├──────────────────────────────────────────────────────────────────────┤
│ [Back]  [Cancel]                                  [Use this parcel] │
└──────────────────────────────────────────────────────────────────────┘
```

Candidate rows are keyboard-selectable radio-style choices. Each row supplies a concise text equivalent of the preview: candidate identifier, containment or nearby status, nearby distance when applicable, legal area, and source. The preview is a small vector confirmation aid with a text alternative; it has no map tiles, browser, or survey accuracy implication. When terrain extension is positive, it renders the legal parcel as a solid outline and terrain extent as a clearly different dashed outline, with an explicit legend. It must show holes, multipart geometry, or expanded-topology changes in details and text; it must not silently simplify their legal meaning.

The footer command says **Use this parcel** and its nearby help text says, “This confirms the displayed location and selected boundary.” It is disabled until one location and one parcel have been explicitly confirmed. An absent, unregistered, malformed, denied, timed-out, or no-match parcel source replaces the list with the corresponding recovery panel in section 5 without discarding a good location.

### 3.3 Review and create

Review contains no editable terrain-extension field. It summarizes effective preferences, provides a **Change in Settings** link, and asks only for document-bound/current-run choices. The shared-coordinate option starts unchecked for every new run and is not persisted.

```text
┌ SolidGround — Create terrain                              [Settings] ┐
│  ○ Location ───── ○ Parcel ───── ● Review                            │
├──────────────────────────────────────────────────────────────────────┤
│ Review before creating                                               │
│                                                                      │
│ Location and parcel                                                  │
│ Sample site · Parcel SG-017 · legal boundary: original parcel        │
│ [Change parcel]                                                      │
│                                                                      │
│ Terrain and source                                                   │
│ USGS 1 m · Curvature-aware · 15,000 maximum points                  │
│ Terrain beyond property line: 0 display units [Change in Settings]  │
│ Source attribution and accuracy limits ▸                             │
│                                                                      │
│ Revit placement                                                       │
│ Level [ Level 1                                      v ]             │
│ Toposolid type [ Toposolid 1                             v ]         │
│ Export folder: C:\Example\SolidGround [Change folder]               │
│ [ ] Write shared coordinates for this run                            │
│     This changes the current Revit document when creation succeeds.  │
│                                                                      │
│ Run options ▸  Fetch-envelope estimate and optional run overrides   │
├──────────────────────────────────────────────────────────────────────┤
│ [Back]  [Cancel]                             [Create toposolid]     │
└──────────────────────────────────────────────────────────────────────┘
```

Visible source material contains a concise, truthful attribution and the site-form accuracy limitation. The disclosure contains complete source/license text and provenance details without sending the user to another page. The fetch estimate identifies whether it covers the fetch envelope or buffered terrain; it does not imply an entitlement check or request elevation. A warning for a missing key, coverage gap, internal NODATA, topology change, or native threshold gives the reason and a relevant action. An internal NODATA warning requires the specified acknowledgment before Create; a source envelope that cannot cover terrain blocks creation and offers **Browse for a larger raster** or **Change extension in Settings**.

The call to create begins only after the footer command. It returns from the WPF modal and follows existing command-time Preflight, acquisition, transaction, verification, provenance, and rollback rules. No setting of a checkbox, opening Settings, or cancelling the modal changes the document.

## 4. Settings

The ribbon **Settings** button and the in-dialog **Settings** button open the same modal editor. It can open with no active document and never starts a transaction. It has a draft, Save/Cancel semantics, a versioned nonsecret per-user settings document, conflict-aware atomic save, and the approved legacy-import path. Reopening its parent resumes the originating stage and preserves the creation draft.

```text
┌ SolidGround — Settings                                              ┐
│ Preferences apply to future runs after Save.                         │
├───────────────┬────────────────────────────────────────────────────┤
│ Terrain       │ Terrain                                              │
│ Sources       │ Output unit [ U.S. survey foot                 v ]   │
│ Files         │ Distance display [ Feet and inches             v ]   │
│ Advanced      │                                                      │
│               │ Maximum terrain points [ 15000 ]                     │
│               │ Machine native threshold: 20000.                     │
│               │                                                      │
│               │ Terrain beyond property line [ 0 ] [display units]  │
│               │ Affects terrain only; legal parcel remains unchanged.│
│               │                                                      │
│               │ Simplification [ Curvature-aware               v ]   │
│               │ Terrain display [ Full context                 v ]   │
│               │ Property only remains unavailable until validated.   │
│               │                                                      │
│               │ [Restore defaults…]                                  │
├───────────────┴────────────────────────────────────────────────────┤
│ [Cancel]                                      [Save settings]        │
└────────────────────────────────────────────────────────────────────┘
```

The left navigation is a tab list with four logical pages: **Terrain**, **Sources**, **Files**, and **Advanced**. It remains keyboard-operable and announced as a tab/list selection. A page does not save independently. Required validation appears beside its field and the first invalid field receives focus on Save.

| Page | Content and interaction |
| --- | --- |
| Terrain | Foot definition/output unit, display format, point budget, terrain extension, simplification method, and the post-validation terrain-display preference. Change display units by converting presentation only; store the canonical finite nonnegative extension in metres with full precision. Displayed rounding never replaces that value. |
| Sources | Default Census geocoder and explicit keyed alternatives; parcel source registrations; elevation mode/local raster and sidecars; session-only credential status and entry. County setup exposes permitted service URL, metadata load, selected layer/fields, required real attribution/license, and an authorized-use acknowledgment. Local data exposes Browse, source label, actual disclaimer, and validation result. |
| Files | Writable export folder and naming preferences, with Browse and an effective-path summary. Output destination validation occurs before elevation acquisition. |
| Advanced | Existing validated timeout, nearby-distance, origin, sampler coverage, and source details. Advanced is disclosure-oriented; it does not introduce a second meaning for a setting. |

Session API-key entry uses a masked text control labelled with provider and lifetime: “Available only until this Revit session ends.” It offers no reveal, copy, export, persistence, log, provenance, or clipboard path. Its explicit session override has the approved precedence over an environment key for that provider; otherwise existing environment resolution applies. The CLI is unchanged.

**Restore defaults** opens a small confirmation panel listing only fields that would change. It does not erase source registrations or their license text. Nothing is persisted until **Save settings**. On an expected-digest conflict, Save leaves the persisted file and draft unchanged and presents **Reload saved settings** and **Keep my draft to reapply**.

## 5. Setup, migration, and recoverable-error states

The tool opens even when setup needs repair. It does not create a template failure that prevents Location from appearing.

### First use and absent parcel source

```text
┌ Setup needed ───────────────────────────────────────────────────────┐
│ Location lookup is available. A parcel source is needed to select   │
│ a property boundary.                                                 │
│                                                                      │
│ [Configure parcel source]  [Choose a local boundary]                 │
│                                                                      │
│ You can continue to resolve a location and return here.              │
└─────────────────────────────────────────────────────────────────────┘
```

**Configure parcel source** opens Sources in Settings and returns to the preserved Parcel state after Save. **Choose a local boundary** opens the existing supported local-file flow. The recovery language differentiates “no source is configured,” “the selected source does not cover this location,” “the local file is malformed,” “the service denied the request,” and “the request timed out.” It does not imply that a valid address must have a parcel match.

### Legacy settings import and corrupt settings

```text
┌ Previous settings found ─────────────────────────────────────────────┐
│ A prior SolidGround settings file is available for import.           │
│ It will remain unchanged. Review the values before saving a new      │
│ per-user settings file.                                              │
│                                                                      │
│ • Process mode and selected source settings can be imported.         │
│ • Relative paths will be rebased from the legacy file's folder.      │
│ • Shared-coordinate opt-in is not imported.                          │
│                                                                      │
│ [Review and import]  [Start new settings]  [Cancel]                  │
└─────────────────────────────────────────────────────────────────────┘

┌ Settings need repair ───────────────────────────────────────────────┐
│ The saved settings cannot be used: “maximum terrain points” must be  │
│ a positive whole number. The original file has not been changed.     │
│                                                                      │
│ [Repair in Settings]  [Start new settings]  [Cancel]                 │
└─────────────────────────────────────────────────────────────────────┘
```

Unknown schema versions and malformed input fail visibly. **Start new settings** is explicit and begins a clean draft; it never overwrites unreadable bytes. Imported source registrations are recreated only after the user selects them and their existing attribution is shown. Missing legacy files, denied per-user destinations, invalid local files, metadata failures, and unsupported fields all name the affected field or action and keep draft values available for repair.

### Lookup, source, and creation errors

Lookup errors stay in their originating stage, preserve editable input, focus the first actionable field, and use a non-modal inline alert before any native result dialog. Examples:

| Condition | Stage message and action |
| --- | --- |
| Invalid address/coordinates | “Check the highlighted value before finding a location.” Focus the invalid input; send no request. |
| No geocode match | “No location matched this entry.” Offer **Edit address**, **Use coordinates**, and **Retry**. |
| Ambiguous geocode | “Choose a location before parcels are searched.” The primary action is **Use this location**. |
| Parcel source unavailable | “A parcel source is not ready for this location.” Offer **Configure parcel source** and **Choose a local boundary**. |
| Key missing or USGS authorization denied | State that USGS 1 m access needs authorized credentials; offer source setup/key-request guidance. Never offer a coarser fallback. |
| Process source does not cover terrain extent | “The selected raster does not cover the requested terrain extent.” Offer **Browse for a larger raster** and **Change extension in Settings**. |
| Native threshold/budget issue | State the configured and detected threshold, retain the current selection, and link to Terrain settings. Command Preflight still decides. |
| Creation fails after the modal | Existing native result dialog presents the authoritative failure. It does not report success based on a request that failed or rolled back. |

## 6. Visual system

### 6.1 Semantic palette

Controls consume semantic resources; feature code does not choose one-off brush literals. Color alone never conveys state. Values below are the implementation targets for normal light/dark themes. The listed text combinations meet the PRD's 4.5:1 normal-text target; meaningful component boundaries/state cues meet at least 3:1. Actual rendered templates require the #53 lane or manual rendered-state evidence before a pass is claimed.

| Semantic token | Light target | Dark target | Intended use |
| --- | --- | --- | --- |
| `WindowBackground` | `#F8F9FA` | `#1B1B1F` | Window/frame background |
| `Surface` | `#FFFFFF` | `#242428` | Inputs, cards, popups, footer |
| `SurfaceRaised` | `#F1F3F4` | `#303034` | Hover/secondary panels |
| `TextPrimary` | `#1B1B1F` | `#F4F0F4` | Normal text and headings |
| `TextSecondary` | `#4A4E54` | `#C9C5CA` | Help and metadata, never required action text alone |
| `Border` | `#747775` | `#A9A4AA` | Input/card boundaries |
| `Accent` | `#0B57D0` | `#A8C7FA` | Primary action, link, selected indicator |
| `OnAccent` | `#FFFFFF` | `#002B5C` | Text/icons on Accent |
| `Selection` | `#D3E3FD` | `#174A7C` | Selected row/closed selection background |
| `OnSelection` | `#102A43` | `#FFFFFF` | Selected text |
| `Error` | `#B3261E` | `#F2B8B5` | Error icon, text, and invalid outline |
| `OnErrorSurface` | `#5F1110` | `#FFDAD6` | Error-panel text |
| `Focus` | `#005FCC` | `#A8C7FA` | 2 DIP focus ring, never hidden by selection |
| `DisabledText` | `#55595D` | `#CBC7CC` | Disabled labels; no opacity-only treatment |
| `DisabledSurface` | `#E2E5E8` | `#38383D` | Disabled control background |

In Windows High Contrast, resource keys map to Windows system colors (`Window`, `WindowText`, `Highlight`, `HighlightText`, `GrayText`, and system focus/highlight resources). Branding colors are not forced, and the semantic roles still govern layout, visible focus, selection, errors, and disabled explanations.

### 6.2 Control-state matrix

| Control | Required visible states |
| --- | --- |
| Text/numeric input | Normal surface/border; hover border; focused 2 DIP focus ring; invalid Error border plus text; disabled surface and 4.5:1 disabled label; read-only uses normal readable text and a textual “Read-only” cue where a user may expect editability. |
| ComboBox | Closed selected value uses `TextPrimary` on `Surface`; popup uses `Surface`; selected popup row uses `Selection`/`OnSelection`; focused/hovered/pressed states remain separately visible. Closed selection and popup selection are independently verified. |
| Candidate list/radio row | Full-row focus ring, selected background, selected marker, containment/nearby text, and checked state. A nearby warning is text plus icon, never a color-only signal. |
| Primary button | Accent/OnAccent normal; darker/lighter pressed/hover treatment that retains contrast; 2 DIP focus ring with an offset; disabled surface/text plus adjacent explanatory reason. |
| Secondary button/link | Surface/Border with TextPrimary or Accent text; hover/pressed/focus indicators; link remains underlined on focus and is not an icon-only action. |
| CheckBox/RadioButton | 3:1 or greater visible boundary; checked indicator and label; focused ring; disabled label meets product 4.5:1 target. |
| Validation/alert | Error icon, Error text, field association, and summary/status announcement. Do not rely on a red outline alone. |
| Preview | Surface/card border, legal/terrain legend with line style and labels, focusable textual alternative, and a fixed placeholder state when no geometry is available. |

## 7. Keyboard, focus, and asynchronous behavior

### 7.1 Keyboard and assistive technology

The tab order follows the visual task order, not construction order. The footer is last in stage content. Shift+Tab reverses this order. Suggested access keys are localized in the displayed label: `Alt+S` Settings, `Alt+F` Find, `Alt+P` Use this parcel, `Alt+C` Create, and `Alt+N` Cancel; an access-key collision is resolved in the localized control set, never by hiding a command.

| Context | Key behavior |
| --- | --- |
| Any stage | `Enter` invokes exactly the visible, enabled primary action. It cannot invoke hidden navigation or Create during lookup. `Escape` cancels lookup and returns a cancelled command before creation. |
| Text fields | Standard text editing first. `Enter` only invokes Find when focus is in the Location form and it is valid; multiline legal/source text never triggers the footer action. |
| Candidate list | Arrow keys move selection; Space selects; Enter uses the visible primary action only after the required selection is present. The candidate name, containment/nearby state, area, and selected state are announced. |
| Settings pages | Tab/Shift+Tab traverse page navigation and current page controls; arrow keys change the selected navigation page. `Ctrl+S` invokes Save when valid, `Escape` invokes Cancel/close, and Save validation focuses the first invalid field. |
| Disclosure | Enter/Space expands or collapses it; state is exposed as expanded/collapsed. |

Each stage change announces “Location,” “Parcel,” or “Review,” including why it changed where useful (“Parcel: location found”). The status region announces lookup start, completion, cancellation, and field errors without moving focus away from the current control. The vector preview exposes a concise text summary and is not the only way to determine parcel status. Labels, roles, validation association, selected candidate state, and visible focus must be available to Windows assistive technology; the final automation inventory is updated to the actual controls instead of retaining the obsolete 18-control count.

### 7.2 Request/version transitions

The UI snapshots inputs and assigns an increasing request identity before a geocode, county, or parcel operation. Core I/O is awaited without blocking the WPF dispatcher; only UI updates are marshalled to it. Revit API/document access remains on Revit's owning UI thread.

```text
Idle ── Find ──> Finding location ── current result ──> Parcel
  ▲                    │                                  │
  │                    ├─ invalid/no match ────────────────┘
  │                    ├─ edit/cancel ── discard result ───┘
  │                    └─ ambiguous ── choose location ─> Finding parcel
  │                                                        │
  └── Back/Edit/source change <── discarded stale result ──┘
```

| Trigger | Immediate UI state | Completion rule |
| --- | --- | --- |
| Find with valid input | Status shows **Finding location** within 100 ms in the delayed-service test; primary is disabled while lookup is pending. | Only a matching current request ID advances to Parcel. |
| Single resolved location | Enter Parcel and show **Finding parcel**. | Parcel results may populate but do not confirm a parcel. |
| Multiple locations | Enter Parcel with a location-choice list. | Parcel lookup starts only after **Use this location**. |
| Edit address/coordinates, select another location, change parcel source, Back, or Cancel | Cancel request token; invalidate affected selection/preview/readiness; restore editable draft. | Completion with an old ID is ignored and cannot re-enable Create or revive a selection. |
| Settings Save changing unit/budget | Preserve confirmed parcel; refresh summaries/warnings. | Authoritative Preflight rechecks before acquisition. |
| Settings Save changing extension | Preserve legal parcel/placement anchor; rebuild terrain extent/preview/estimate only. | Show topology/coverage warnings before Create; old terrain result is discarded. |
| Settings Save changing source | Retain location but invalidate parcel candidates/confirmation and immediately offer/restart lookup. | No old result can enable Create. |
| Modal closes for creation | Lookup work has completed or is cancelled. | Existing command runs Preflight/acquisition/transaction; no UI Cancel is offered for a creation operation that cannot safely cancel. |

The validation targets are behavioral requirements, not observed runtime evidence: the delayed synthetic service must render status within 100 ms, synthetic cancellation returns control within one second, and a stale completion never updates UI. Elevation fetch retains the existing no-automatic-retry and at-most-two-request policy.

## 8. Completion and presentation states

The existing native success/failure TaskDialog remains the authoritative outcome. On success it identifies the created terrain and property line where applicable, retained point count, export folder, and the ordinary Revit save action. **Open folder** opens the committed export location. **Show terrain** is offered only if the document/view is valid for the verified host operation; framing failure is reported as a viewing limitation and cannot convert successful creation into a failure.

The Settings option **Terrain display: Full context / Property only** saves a preference only. `Full context` remains effective until the proposed subdivision workflow is live-validated and selected. If `Property only` is later available, completion explains that exterior support remains in the model and the action applies only to the selected view; **Show surrounding terrain** is reversible. It never hides categories, unrelated terrain, or every view. This specification does not claim that child visibility, host relationships, zero subdivision offset, export behavior, or surface fidelity have been proven.

## 9. Source basis and verification boundary

This design applies the sources already recorded in the PRD rather than introducing new research:

- [Nielsen Norman Group on progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/) and [visibility of system status](https://www.nngroup.com/articles/visibility-system-status/) support separating stable preferences and visibly advancing Find.
- [Microsoft Wizards guidance](https://learn.microsoft.com/en-us/windows/win32/uxguide/win-wizards), [command buttons](https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-command-buttons), and [property windows](https://learn.microsoft.com/en-us/windows/win32/uxguide/win-property-win) support coherent stages, specific commands, and a separate settings surface.
- [W3C text contrast](https://www.w3.org/WAI/WCAG22/Understanding/contrast-minimum.html), [non-text contrast](https://www.w3.org/WAI/WCAG22/Understanding/non-text-contrast.html), and [focus visible](https://www.w3.org/WAI/WCAG22/Understanding/focus-visible.html), together with [WPF styles and templates](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/styles-templates-overview) and [ComboBox](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/combobox), support the semantic palette and rendered-state matrix.
- [Autodesk's Revit Publisher Guidelines](https://aps.autodesk.com/marketplace/publisher-center/revit-publisher-guidelines) supports guided setup; [Autodesk's Revit 2027 terrain guide](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-E325CDE7-5468-43C8-BEAD-EC5AF13231F0) and [subdivision guide](https://help.autodesk.com/view/RVT/2027/ENU/?guid=GUID-BA7C38A7-7E8B-45CC-B4A3-950D19B48C66) inform the separate terrain/property boundary and future presentation validation.

These sources support the design rationale. They do not prove that the WPF templates render these values, that every listed control is accessible, that async timing is met, or that a Revit 2027 creation/presentation path succeeds. Those claims require the PRD's acceptance evidence: Core flow/settings tests, Issue #53 rendered-control and binding coverage or equivalent manual evidence, and signed deploy/restart/hash-verified Revit 2027 sessions using synthetic content or the public example site.

## 10. Review handoff

The specification is ready for adversarial plan review. Review should check it against PRD acceptance criteria AC01–AC23, particularly: exactly three routine primary actions; no stale/implicit parcel confirmation; safe settings migration and nonpersistent keys; full rendered control-state coverage; UI-only repair without document changes; and legal-boundary invariance when terrain extension changes. Implementation remains sequenced by the PRD's A–F increments, beginning with shared styles and settings/setup, while the parcel/terrain separation lands before any nonzero extension is exposed as a released option.

## 11. Owner-directed ground-selection UX requirements — 2026-10-01

The owner accepted the provisional ground-plus-measured-rise direction and explicitly required a cohesive, approachable experience throughout SolidGround. This section records requirements for the proposed building-datum feature. It does not claim that the feature is implemented or that its acquisition, provenance, or native annotation behavior has passed review. Sections 1–10 describe the earlier implementation scope; this follow-up adds optional building context and a focused floor-reference task within Review. It preserves Location → Parcel → Review and the separate Settings tool.

### One continuous task

Keep the same window, typography, spacing, semantic colors, parcel preview, stage indicator, and fixed footer. A **Building floor reference** summary in Review opens a focused task in that window's content area. Do not stack a second wizard or a sequence of popup dialogs over it. While editing the reference, show its task-specific primary action in the existing footer; Create is available only on the final Review summary.

Initially show one question: **How would you like to set the first-floor reference?** The choices are **Estimate from ground beside the entrance**, **Enter a known floor elevation**, and **Use a provisional preview for now**. Each choice reveals only its own inputs. The measured point, floor height, chosen document level, and provisional status belong to this property/run, not global Settings. Units and terrain preferences remain in Settings, with a concise effective-unit label beside each measurement.

The ground-estimate path is:

1. Explain the task in one sentence: **Select the ground beside your entrance, then enter the vertical distance up to the unfinished first floor.** Show the source/transfer notice and terrain-request estimate before the explicit **Load ground preview** action. Preparing this preview is a new acquisition path that needs authoritative Preflight, cancellation, request identity, bounded requests, and reuse of the acquired data for creation. Find and parcel selection still make no elevation request. Preview preparation makes no Revit document change.
2. Keep the parcel visible while data loads. Once usable ground data is ready, activate point placement automatically; no additional Select, Find, or Next click is needed to enable the map. A click places one visible pin and shows its sampling status. It never confirms a floor, chooses a door automatically, or creates geometry.
3. Reveal the single required **Measured rise to unfinished first floor** field when a valid point is selected. Show the preferred length format, a synthetic example such as `2 ft 3 in`, and a small explanatory sketch identifying exterior grade and top of subfloor/structural slab. Do not silently use a door threshold, finished floor, or an empty field as zero. Support an explicit below-grade direction without asking users to guess a signed-number convention.
4. Show a compact, plainly marked estimated floor result. **Use this floor reference** confirms the point and measurement and returns to the final Review summary in the same window. There is no additional generic Next action. Review offers **Change floor reference** and shows the selected level, the estimated/known/provisional status, and the real-world elevation reference. A user can return to the edit without re-entering unrelated information.

Unknown measurement is a normal condition. **Use a provisional preview for now** remains available without a fabricated rise or a forced building-outline download. The ground-only provisional policy and its native elevation treatment must be specified and verified in the datum implementation plan before that option can create geometry.

### A selection surface people can control

- Initially fit and center the whole legal parcel with orientation and sufficient padding. Use one stable view transform for the legal boundary, muted approximate building outlines, terrain support, and pin. Distinguish them with line style, symbols, and text as well as color. Outlines provide orientation; never require a separate building-selection task or pretend to locate the doorway.
- Place the pin at the operator's chosen coordinate. Do not snap it to a building, parcel edge, geocoded point, or another valid grid cell. A click anywhere inside the displayed data view moves the existing pin; dragging the pin also moves it. Give its handle at least a 32-DIP hit target without enlarging or shifting the underlying sample location. Show a focus ring and a clear placing/selected/invalid state.
- Provide visible Zoom in, Zoom out, Pan, and Fit parcel controls. State the active interaction mode. Panning cannot accidentally place a pin. Wheel movement must not trap page scrolling or unexpectedly zoom an unfocused preview; zoom controls and keyboard operation are sufficient without hidden gestures. Resize and late-arriving outlines preserve the selected world coordinate and current view; only an explicit Fit or a newly confirmed parcel resets the view.
- A click gives immediate selected/pending feedback; local sampling does not wait for another remote request. An invalid sample stays visible with **No usable ground height here. Move the point.** Do not move it silently or manufacture an elevation. A point inside an approximate outline gets a non-blocking warning about bare-earth interpolation and can be corrected without discarding the measurement.
- Offer **Enter coordinates** as an accessible alternative, with named latitude/longitude fields and validation. A focused pin supports arrow-key movement with the increment displayed. Announce point validity, units, and the estimated result through an accessible status region. While the preview owns point-placement focus, Enter accepts the point and Escape cancels the current placement/drag; neither key can trigger creation or unexpectedly close the window. Returning to the Review summary restores the ordinary footer-key contract.
- Missing or delayed outlines leave the parcel-and-ground selection usable. Show a brief inline status and an optional Retry; never block the task with a modal outline error. Required source attribution is visible once per run, with longer source/accuracy detail available in a disclosure.

### Cohesion and information hierarchy

Use the same control styles and error conventions in Create and Settings. Each task has one visually dominant primary action, one plain-language instruction, and errors beside the affected input. Explicitly label required measurements and optional context. Show essential accuracy/provisional status before confirmation; keep raw coordinates, interpolation details, CRS diagnostics, and full provenance in an expandable detail area. Progressive disclosure must never hide a missing requirement or a reason that prevents continuation.

Keep the shared header/footer stable. At compact sizes, stack the preview and measurement area in one bounded vertical viewport; do not introduce nested page scrollbars. Enlarging the window should give the preview more usable space. The pin, units, field labels, warnings, and footer stay usable across repeated resize, light/dark/High Contrast, and the existing display-scaling matrix. This applies to the entire tool, not just the new map interaction.

### Observable acceptance and verification

| ID | Required outcome | Verification before acceptance |
| --- | --- | --- |
| UX01 | Floor-reference editing uses the existing shell/stage/preview and exactly one visible primary action; confirmation returns to Review with unrelated draft choices intact | Actual WPF state/navigation tests for each reference path, Back/Change, and cancellation; inspect the visible controls in Revit |
| UX02 | Valid ground data enables placement automatically; one click produces one pin at the selected coordinate; adjusting it needs no extra confirmation dialog | Pointer and keyboard walkthrough; local preview-to-source round-trip tests at multiple zooms and window sizes; assert no snap/substitute and no Revit mutation |
| UX03 | Loading is visible, cancellable, and cannot apply stale data; moving the pin samples cached unsimplified data without further HTTP calls | Delayed fake services/request counters, cancellation and out-of-order completion tests; reuse the acquisition on final creation rather than fetching the same DEM again |
| UX04 | A valid point plus an explicit measured rise enables confirmation; blank, malformed, unknown, and below-grade inputs have unambiguous behavior | Length-format/unit tests, empty versus explicit-zero tests, below-grade direction, NODATA/out-of-coverage support, and actual field-validation/focus checks |
| UX05 | Pan, zoom, drag, Fit, resize, and outline arrival never silently change the pin or mistake navigation for selection | Actual arranged-WPF interaction tests plus a native mouse walkthrough; check selected source coordinates before and after every operation |
| UX06 | Keyboard users can place/adjust/confirm a point or enter coordinates, understand invalid samples, and recover; point-mode Enter/Escape cannot invoke Create/close | Keyboard-only and screen-reader walkthrough, accessible-name/status/focus tests, field editing and pointer-capture cancellation |
| UX07 | Missing/capped/failed outlines leave the same usable ground-selection task; required ground-data failures offer recovery without losing the parcel or entered measurement | Fake source failure/cap cases, no-outline native walkthrough, retry/cancel and provider-denial tasks; no silent source/resolution fallback |
| UX08 | Preview-only/estimated/known status is visible on editing, final Review, and result; no approximate outline or provisional value is presented as surveyed | Source/provenance and UI-state tests, native survey/project annotation checks from the datum research, and save/reopen evidence |
| UX09 | The pin, input, unit, error, and primary action remain readable/reachable across the existing size/theme/scaling matrix; no nested scroll trap appears | Actual WPF viewport/control checks plus native rendered-control, resize, wheel, focus, contrast, and assistive-technology observations |
| UX10 | First-time users can identify the intended exterior point, correct a mistaken click, enter the intended measurement, and explain the provisional result without coaching or settings-file work | Extend the existing 3–5-user formative round with these tasks, including absent outlines, keyboard and dark mode. Record assistance, wrong turns, mistaken point/physical reference, recovery, and result interpretation; revise and repeat failures |

Automated tests can validate states and transforms; they cannot establish that selection feels comfortable or that a novice understands the physical measurement. UX10 and native observation remain acceptance gates. This documentation update supplies interaction requirements, not a completed acquisition/provenance implementation plan or runtime evidence. See the [follow-up plan](../planning/revit-usability-runtime-feedback.md), [building-outline research](../research/building-outlines-and-grade-point-selection.md), and [native datum research](../research/revit-2027-building-and-survey-datums.md) for the remaining implementation boundaries.
