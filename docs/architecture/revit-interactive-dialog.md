# Revit interactive dialog

## Purpose and boundary

Issue #31 (PH3-4) replaces today's file/settings-only AOI input for `SolidGround.Revit` with one modal
`SolidGroundDialog` (code-behind only, zero `.xaml`/BAML), shown from inside `CreateToposolidCommand.Execute`
before today's Preflight/transaction. No new command, ribbon button, or panel (PH3-7 unchanged).
`SolidGround.Core` and `SolidGround.Cli` are otherwise untouched except for the small, Revit-free Core
additions this note names below.

**Status: Stages A, B, and C landed; the dialog is fully built but still not wired.** This note records the
whole accepted design so later stages' code comments can cite its section titles verbatim; three of its five
stages have been implemented so far:

- **Stage A (Core only):** `RevitIniToposolidThresholds.ExceedsNativeThreshold`/`.DescribeExceedance`
  (`src/SolidGround.Core/Hosting/RevitIniToposolidThresholds.cs`) and `TerrainProcessingPipeline.RunAsync`'s
  new optional trailing `AddressParcelProvenance? addressParcel` parameter
  (`src/SolidGround.Core/Processing/TerrainProcessingPipeline.cs`), both already used by `SolidGround.Cli`'s
  and `SolidGround.Revit`'s existing callers unchanged (the new parameter defaults to `null`). See "AOI and
  provenance" and "Result-code mapping" below for what these two additions are for.
- **Stage B (package + `UseWPF` + empty dialog shell):** `CommunityToolkit.Mvvm` 8.4.2 is an unconditional
  `PackageReference` of `SolidGround.Revit.csproj`; `UseWPF=true` replaces the project's old explicit
  `Microsoft.WindowsDesktop.App.WPF` `FrameworkReference`.
- **Stage C (full dialog UI + view-model, still unwired):** this stage. Every one of the ten originally
  designed content-model sections, plus owner decision 1's own step 0 AOI-source choice (see "Content model
  and sections" below), is now real, code-built WPF, driven by a full `SolidGroundDialogViewModel`; theming
  (Light/Dark/High-Contrast), `AutomationProperties` accessibility naming, and the synchronous network bridge
  are all implemented. Two new Revit-free Core types
  (`SolidGround.Core.Sources.LatitudeLongitudePointParser`, `SolidGround.Core.Provenance.AddressParcelProvenanceFactory`)
  back the address field's "latitude, longitude" acceptance and the dialog's provenance assembly, respectively.
  **`SolidGroundDialog`/`SolidGroundDialogViewModel` are still never constructed anywhere in the shipped
  add-in.** Neither is shown from `CreateToposolidCommand`, the ribbon, or any other command, so the whole
  feature remains unreachable from a running add-in; it was exercised during this stage only via a throwaway,
  uncommitted local WPF host, never committed to this repository.
- **Stage D (wiring into `CreateToposolidCommand.Execute`)** and **Stage E (manual evidence + docs update)**
  have **not** been implemented. Every section below that is not explicitly marked "landed" describes the
  accepted design for that future work, not code that exists today.

## Content model and sections

Eleven sections, fixed order. Section 0 is owner decision 1's own addition (implemented with its recommended
default "keep the settings-file path available", kept isolated so it is easy to change or remove later);
sections 1-10 match the original design's owner decision 9 (ship the full content model in the first
milestone, not a reduced slice) exactly, unchanged in order or shape.

0. **AOI source choice** (owner decision 1) — two `RadioButton`s: "Find a parcel" (`DialogAoiSource.FindParcel`,
   the default) or "Use the area in the settings file" (`DialogAoiSource.UseSettingsFile`, today's
   settings-driven bounding-box/radius/parcel configuration, unchanged). This one choice lives in exactly one
   small, clearly named place: `SolidGroundDialogViewModel.AoiSource` (a `DialogAoiSource` value) and the two
   fixed `SolidGroundDialogStep[]` arrays (`FindParcelStepOrder`/`SettingsFileStepOrder`) its
   `ActiveStepOrder` property switches between. Choosing "Use the area in the settings file" skips sections
   1-4 below entirely (address entry through buffer): `Next` from section 0 goes straight to section 5.
1. **Address entry** — one `TextBox` (`AddressText`) plus one "Find" button (`GeocodeCommand`, a
   `[RelayCommand]`). The address field also accepts a `"latitude, longitude"` pair, parsed by the new,
   Revit-free `LatitudeLongitudePointParser.TryParse` (`src/SolidGround.Core/Sources/LatitudeLongitudePointParser.cs`;
   deliberately independent of `SolidGround.Cli.Commands.ParcelCommand`'s own, pre-existing `--point` parsing,
   which stays private to that command, unchanged, with its own tests). A successful parse builds a
   synthetic `AddressGeocodeCandidate` standing in for the point and never touches the network at all — this
   is what lets the dialog be exercised offline against the local parcel file source. A value that looks like
   an attempted coordinate pair but fails WGS 84 range validation
   (`LatitudeLongitudePointParser.LooksLikeAttemptedCoordinatePair`) shows a fixed, non-echoing inline message
   instead of being sent to the geocoder.
2. **Geocode candidates** — a list bound to `GeocodeCandidates` (`ObservableCollection<AddressGeocodeCandidate>`),
   pre-selecting index 0 (`AddressGeocodeAcquisition`'s own constructor already guarantees "non-empty,
   best-match-first"; a direct point entry produces exactly one synthetic candidate here instead).
3. **Parcel candidates with legal-description preview** — its own "Find parcel" button
   (`FindParcelCommand`, enabled once a geocode candidate is confirmed) plus a list bound to `ParcelCandidates`
   (`ObservableCollection<ParcelBoundaryCandidate>`), no default selection (`ParcelBoundaryAcquisition.Candidates`
   carries no rank). Zero candidates is a normal, non-exceptional outcome, shown inline (only after a real
   lookup attempt, via `ShowNoParcelCandidatesMessage`) with Back enabled and Next disabled.
4. **AOI buffer** — one numeric input (`BufferMeters`, default `0`), validated inline by attempting
   `LinearDistance.Meters(BufferMeters)` and showing that constructor's own exception message
   (`BufferErrorText`) on failure.
5. **Point budget (`Revit.ini` guard warning)** — one numeric input (`PointBudget`, prefilled from
   `settings.Request.Simplification.PointBudget`), with an inline warning banner visible exactly when
   `RevitIniToposolidThresholds.ExceedsNativeThreshold(PointBudget, thresholds)` is true, showing
   `RevitIniToposolidThresholds.DescribeExceedance(...)`'s exact text — the same sentence Preflight's own
   rejection uses (see "Result-code mapping" below), so ignoring the warning always leads to the identical
   predicted Preflight rejection.
6. **Unit choice** — `LengthUnit.UsSurveyFoot`/`LengthUnit.InternationalFoot` only (never `LengthUnit.Meter`),
   prefilled from `settings.Request.OutputUnit`, falling back to `UsSurveyFoot` if the settings file had
   `Meter` configured.
7. **Level/toposolid-type** — two `ComboBox`es bound to `IReadOnlyList<NamedElevationCandidate>`/
   `IReadOnlyList<NamedCandidate>` (`SolidGround.Core.Processing`), populated by the same
   `LevelAndTypeResolver` queries `CreateToposolidCommand` already runs, defaulted by the same
   `NamedElevationSelector`/`NamedSelector` Core selectors applied to `settings.Target.LevelName`/
   `.ToposolidTypeName`. `SolidGroundDialogViewModel` never touches `Document`/`Level`/`ToposolidType` itself:
   whatever constructs it (a later stage's `SolidGroundDialogHost.ShowModal`, or this stage's own throwaway
   local host) resolves the two full candidate lists first and passes them in through
   `SolidGroundDialogInputs`.
8. **Shared-coordinates opt-in checkbox** (PH3-3), default off — one `CheckBox`
   (`WriteSharedCoordinatesIfAbsent`). Disabled, with inline explanatory text, when
   `SolidGroundDialogInputs.DocumentAlreadyHasSharedCoordinates` is already true (computed once, before the
   dialog opens, from `SharedCoordinatesDetector.LooksAlreadyCoordinated(document, vertexToleranceInternal)` —
   see "Settings interaction" below for where that value comes from in this stage).
9. **Provenance/accuracy preview** — read-only text only: when the AOI source is a parcel lookup, the
   confirmed candidates' `Attribution` (geocode) and `LicenseDisclaimerText`/`AccuracyLabel` (parcel), plus one
   of two mutually exclusive intro sentences (review finding, major, fixed) depending on
   `AddressWasGeocoded`: when the operator's point came from an actual geocoded address, an explicit sentence
   that the confirmed address will be included in this run's exported provenance record (PRIVACY: the
   operator's own address becomes exported data, and this page says so plainly); when the operator instead
   entered coordinates directly (never geocoded), an equally explicit sentence that no address will be
   attached to this run's exported provenance record — matching
   `AddressParcelProvenanceFactory.Create`'s own contract, which omits `GeocodeProvenance` entirely in that
   sub-case. Both sentences require the AOI source to be a parcel lookup; when the AOI source is the settings
   file instead, a separate, third statement says plainly that no address/parcel lookup was performed and no
   such provenance will be attached. Either way, AGENTS.md's fixed "site-form tool, not a survey instrument"
   sentence is always shown.
10. **Preflight summary with Create/Cancel** — a read-only recap of every prior choice (area of interest,
    buffer, point budget with its warning state, unit, level name, toposolid type name, checkbox state) plus
    a note that Preflight still runs afterward and can still reject. The Create/Cancel buttons themselves live
    in the dialog's persistent navigation bar (see "Flow/state model"), not inside this step's own panel.

Every control in every section gets `AutomationProperties.SetName(control, "...")` (and `SetHelpText` for the
buffer input, the point-budget input, and the shared-coordinates checkbox — the three whose purpose is not
obvious from a short adjacent label alone) — see "Theming and accessibility" below.
`SolidGroundDialogAutomationInventory.ExpectedControlCount` (`src/SolidGround.Revit/Dialog/SolidGroundDialogAutomationInventory.cs`)
is the hand-maintained total (18 today: 2 + 2 + 1 + 2 + 1 + 1 + 2 + 2 + 1 across sections 0-8, plus the
persistent navigation bar's own Cancel/Back/Next/Create, 4), bumped by hand alongside the matching
`AutomationProperties.SetName` call exactly when a control is added or removed; see "Tests" below for the
self-consistency check this backs.

## Flow/state model

A `SolidGroundDialogStep` enum (`SolidGround.Revit.Dialog`, Revit-side — it orders WPF panels, not a
Core-portable decision) with eleven values in the order above. `SolidGroundDialogViewModel.CurrentStep` drives
which panel `SolidGroundDialog`'s content area shows — not a `switch` in code as originally planned, but a
`StepToPanelConverter` (a small `IValueConverter`) bound to a `ContentControl`, so the content host swaps
automatically whenever `CurrentStep` changes, with no manual event-handler wiring. `Next`'s own `CanExecute`
(`CanGoNext`) is a `switch` over `CurrentStep` naming each step's own completion rule; `Back` is enabled except
on the first step of whichever array `ActiveStepOrder` currently returns; `Create` (a separate,
always-present button, not "Next" relabeled) is enabled only on the last step and is where
`SolidGroundDialogResult` is finally constructed and `CloseRequested` fires. `Cancel` (also always present in
the navigation bar, on every step, not only the last one) sets `Result` to `null` and fires the same event.
`SolidGroundDialog` translates `CloseRequested` into `DialogResult = (Result is not null); Close();` — any
close path that never fires `Create` (window X button, Esc, or the explicit Cancel button) leaves `Result`
`null` and therefore `DialogResult` `false`.

No Core-level "flow state" type is introduced. The reason is concrete, not a shortcut: every piece of
non-trivial decision logic this flow needs — best-geocode-candidate defaulting (trivial, `Candidates[0]`,
already guaranteed by `AddressGeocodeAcquisition`'s own constructor), AOI construction
(`ParcelBoundaryAoiFactory.FromCandidate`), provenance assembly (`AddressParcelProvenanceFactory.Create`,
new this stage), buffer validation (`LinearDistance.Meters`), point-budget-vs-threshold comparison
(`RevitIniToposolidThresholds`'s two Stage A methods), and level/type defaulting
(`NamedElevationSelector`/`NamedSelector`) — already exists in Core, built by Issues #28/#29/#30/#33 plus this
stage's own two small additions. `SolidGroundDialogViewModel` itself never references the Revit API at all
(no `Document`, `Level`, `ToposolidType`, or `UIThemeManager`): every Revit-sourced input arrives once, at
construction, through `SolidGroundDialogInputs` (`src/SolidGround.Revit/Dialog/SolidGroundDialogInputs.cs`) —
a plain record of Core types and primitives — and the view-model's own decisions call only Core types.

## AOI and provenance: two AOI paths, dialog-resolved or settings-driven

**Decision, flagged for the owner, refined by a later owner decision (see below).** The original design's
decision 1 made this dialog the *only* way `CreateToposolidCommand` would build an `AreaOfInterest` once it
shipped. Owner decision 1 (asked and answered before this stage) instead kept both paths available side by
side: section 0's "Use the area in the settings file"
choice means `settings.json`'s `areaOfInterest.boundingBox`/`.radius`/`.parcel` sub-objects are **not** made
dead code for the Revit host after all. `SolidGroundDialogResult.AoiSource` carries which path the operator
took; when it is `DialogAoiSource.UseSettingsFile`, `SolidGroundDialogResult.Aoi` and `.AddressParcel` are
always `null`, and a later stage's caller is expected to fall back to today's existing, unchanged
`AoiSettingsFactory.Build` path — exactly as it already worked before this issue. `SolidGround.Cli`'s own
`AoiSelection`/`AoiSettingsFactory` paths are untouched by this issue either way.

**Construction, entirely in memory, no new file (landed this stage, in `SolidGroundDialogViewModel.Create`).**
Once a parcel candidate is confirmed (end of section 3) and the operator reaches "Create", the view-model
calls the existing, unchanged `SolidGround.Core.Processing.ParcelBoundaryAoiFactory.FromCandidate(candidate,
buffer)`. The resulting `ParcelGeometryAoi` becomes `SolidGroundDialogResult.Aoi`; a later stage threads it
into `TerrainProcessingPipeline.RunAsync`'s existing `AreaOfInterest? aoi` parameter, never writing it to a
`.wkt`/`.aoi.json` file the way `SolidGround.Cli`'s `ParcelCommand` does.

**Provenance, also entirely in memory (landed this stage).** On Create, when the AOI source is a parcel
lookup, the view-model calls the new `AddressParcelProvenanceFactory.Create`
(`src/SolidGround.Core/Provenance/AddressParcelProvenanceFactory.cs`), Revit-free and directly tested:

```csharp
public static AddressParcelProvenance? Create(
    DateOnly retrievalDate,
    bool addressWasGeocoded,
    AddressGeocoderProvider geocoderProvider,
    string? addressQueryText,
    AddressGeocodeCandidate? selectedGeocodeCandidate,
    ParcelBoundaryCandidate? selectedParcelCandidate)
```

`addressWasGeocoded` is false whenever the operator entered a "latitude, longitude" pair directly (section 1):
in that case no `GeocodeProvenance` is built at all, even though a synthetic `AddressGeocodeCandidate` stood
in for the point through the rest of the flow — the factory never attributes a real geocoding provider to a
point nobody actually geocoded. `SolidGroundDialogViewModel.Create` supplies `DateOnly.FromDateTime(DateTime.UtcNow)`
itself (mirroring `CreateToposolidCommand.ReportSuccess`'s identical existing convention) — the factory itself
never reads a clock, matching `AddressParcelProvenance.RetrievalDate`'s own documented
caller-supplies-the-clock-value contract.

**The one Core signature change this issue needs, already landed (Stage A):**
`TerrainProcessingPipeline.RunAsync` (`src/SolidGround.Core/Processing/TerrainProcessingPipeline.cs`) gained
one new, optional, trailing parameter, `AddressParcelProvenance? addressParcel = null`, threaded into its
existing `TerrainExportPayloadAssembler.Assemble(...)` call's own already-existing optional parameter.
Every existing caller (`process`, `run`, and both of `CreateToposolidCommand`'s own
`RunFetchPipelineAsync`/`RunProcessPipelineAsync`) keeps compiling unchanged, always passing `null`, until a
later stage edits those two Revit-side call sites to pass `dialogResult.AddressParcel` through instead.

**The other new Core surface, already landed (Stage A):**
`src/SolidGround.Core/Hosting/RevitIniToposolidThresholds.cs` gained two `public static` methods beside the
existing `Parse`: `ExceedsNativeThreshold(int pointBudget, Thresholds thresholds)` and
`DescribeExceedance(int pointBudget, int nativeThreshold, string revitIniPath)`. `DescribeExceedance`'s text is
byte-identical to `CreateToposolidCommand`'s pre-existing inline `CheckRevitIniPointThreshold` message — a pure
extract, not a reword — so existing behavior is unchanged; only its location (now Core, shared) changes.
`CheckRevitIniPointThreshold` becomes a two-line call into these once a later stage lands; this stage's own
`SolidGroundDialogViewModel.PointBudgetWarningText`/`.ShowPointBudgetWarning` already call the identical pair
for section 5's inline warning.

## Settings interaction: prefill, not override

**Reads only; no write-back in this milestone (owner decision 2).** `SolidGroundDialogViewModel` never reads
`RevitSettings`/`RevitSettingsIo` directly — it has no dependency on `SolidGround.Revit.Settings` at all.
Instead, `SolidGroundDialogInputs` carries whatever a later stage's caller already resolved from the
already-loaded settings: `PrefilledOutputUnit`, `PrefilledPointBudget`, `ConfiguredLevelName`/
`ConfiguredToposolidTypeName`, `PrefilledWriteSharedCoordinatesIfAbsent`, `DocumentAlreadyHasSharedCoordinates`,
and `ConfiguredAreaOfInterest` (today's settings-driven `AoiSettings`, shown in section 0/10's own summary
text). The operator's in-dialog choice always wins for the run about to happen; nothing is written back to
`%ProgramData%\SolidGround\Revit\settings.json`. "Last-used value" persistence across sessions is a named,
explicit non-goal (see "Non-goals" below), not silently dropped.

**A genuinely new settings surface is still required, not yet added.** Today's `RevitSettings`
(`src/SolidGround.Revit/Settings/RevitSettings.cs`) still has no field for which `IAddressGeocoder` provider or
`IParcelBoundarySource` to use. Rather than add that settings section in this stage, `SolidGroundDialogInputs`
instead takes an already-constructed `IAddressGeocoder Geocoder` and `IParcelBoundarySource? ParcelSource`
(null when not configured — section 3's "Find parcel" then shows a clear inline configuration message the
first time it is attempted, never at construction time) plus the `AddressGeocoderProvider GeocoderProvider`
that identifies the first for provenance purposes. This keeps Stage C's whole surface decoupled from the
settings file's on-disk schema; a later stage still owns adding `RevitAddressAndParcelSettings` and
constructing the real `IAddressGeocoder`/`IParcelBoundarySource` from it (via the existing, unchanged
`AddressGeocoderFactory.Create` and a settings-driven choice of `CountyParcelRegistrySource`/
`LocalParcelFileSource`) before building `SolidGroundDialogInputs` and showing the dialog.

## Threading and the network bridge

**Landed this stage.** `SolidGroundDialogViewModel`'s `[RelayCommand]` methods for Find-address
(`Geocode`)/Find-parcel (`FindParcel`) are **synchronous `void` methods** (plain `RelayCommand`, never
`AsyncRelayCommand`), using the same blocking-bridge *pattern* Issue #15 already locked in for Stage 2
acquisition (`Task.Run(...).GetAwaiter().GetResult()`) — but, since `IAddressGeocoder.GeocodeAsync`/
`IParcelBoundarySource.FindAsync` return `ValueTask<T>`, not `Task<T>`, the bridge needs one extra `.AsTask()`
call the Stage-2 precedent does not (`Task.Run(Func<ValueTask<T>>)` would otherwise bind to
`Task.Run<TResult>(Func<TResult>)` with `TResult` inferred as `ValueTask<T>` itself, which does not compile
back down to a plain `T`). Both methods catch their own `AddressGeocoderException`/`ParcelBoundarySourceException`
**and** `catch (OperationCanceledException) when (cts.IsCancellationRequested)` — every shipped
geocoder/parcel source rethrows a raw, unwrapped `OperationCanceledException`/`TaskCanceledException`, never
its own wrapped exception type, when the caller's own timeout fires, so this second clause is required for a
genuine network timeout to ever be shown inline instead of escaping uncaught. Neither method ever rethrows
(`SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies`, see "Tests" below,
guards this by reading each method's real body). This deliberately never `await`s across the Revit UI thread
mid-call, matching `docs/architecture/phase-3-interactive-add-in-research.md`'s own reasoning: the dialog
blocking/freezing during a lookup is accepted, existing UX precedent (`CreateToposolidCommand` already logs
"Revit will be unresponsive for up to N second(s)" for the identical reason during Stage 2 fetch-mode
acquisition; both `Geocode`/`FindParcel` log an analogous line through `AddInLog.Info`). `IsBusy` is set
immediately before the blocking call, followed by one explicit `Dispatcher.CurrentDispatcher.Invoke(() => { },
DispatcherPriority.Render)` so the busy indicator has a chance to actually paint before the same (only) UI
thread blocks with no message loop pumping — whether it is actually legible before the freeze is a genuinely
visual question left to Manual Evidence step 1 (Stage E), not asserted as fact here.

A direct "latitude, longitude" entry (section 1) never reaches this bridge at all: `LatitudeLongitudePointParser.TryParse`
runs first, synchronously, with no network call, which is what lets the whole dialog be exercised offline.

## Result-code mapping

`CreateToposolidCommand.ExecuteCore` will gain a new stage sequence: Stage 0 (load settings, open-document
check, and the geometry-tolerance read, hoisted out of `RunDocumentPreflight` so the dialog can use them
before Preflight formally runs), Stage 0.5 (`SolidGroundDialogHost.ShowModal`, returning `null` on Cancel), a
slimmed Stage 1 Preflight (AOI/level/type dialog-supplied when `AoiSource` is `FindParcel`; derived from
settings exactly as today when it is `UseSettingsFile`), then Stages 2-6 unchanged in shape. Mapping (extends,
does not replace, today's existing policy):

| Outcome | `Result` |
| --- | --- |
| Settings/document load problem (Stage 0) | `Cancelled` (unchanged shape) |
| Dialog Cancel / closed without Create (Stage 0.5) | `Cancelled` (new; no `TaskDialog`) |
| In-dialog geocoder/parcel lookup failure | *(never reaches `Result` at all — caught inline, dialog stays open)* |
| Preflight rejection, including PH3-3's shared-coordinates refusal (Stage 1) | `Cancelled` (unchanged) |
| Acquisition failure (Stage 2) | `Cancelled` (unchanged) |
| Geometry Preflight rejection (Stage 3) | `Cancelled` (unchanged) |
| Transaction rolled back (Stage 5) | `Cancelled` (unchanged) |
| Unconfirmed transaction end status (Stage 5) | `Failed` (unchanged — the only `Failed` path) |
| `Execute`'s top-level catch | `Cancelled` (unchanged — no transaction can be open) |

An in-dialog lookup failure never reaching `Execute`'s top-level catch is satisfied by construction and
already guarded offline this stage: `Geocode`/`FindParcel` catch `AddressGeocoderException`/
`ParcelBoundarySourceException` **and** `OperationCanceledException` internally and never rethrow (see
"Threading and the network bridge" above; `SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies`
in "Tests" below). The command-side half of this table (the actual `ShowModal` call site and its `Result`
mapping) is **not implemented yet** (Stage D); `CreateToposolidCommandReturnsCancelledWithNoTaskDialogWhenTheDialogIsCancelled`
remains planned for that stage.

## MVVM shape (and why no messenger)

**Landed this stage.** `SolidGroundDialogViewModel : ObservableObject` (plain, **not** `ObservableRecipient`) —
deliberately no `IMessenger`/`WeakReferenceMessenger.Default` usage anywhere in this feature (owner decision 6,
accepting the weakened AC5 reading: this milestone never introduces a messenger at all, so there is no
messenger state for a second same-session invocation to leak). `WeakReferenceMessenger.Default` is a
process-wide static singleton; a view-model that registered with it would need to reliably unregister on
close or a second `CreateToposolidCommand.Execute` in the same session would accumulate a second live
registration against the first, disposed window. This design avoids the risk by never introducing the
messenger at all: nothing in the content model needs cross-component publish/subscribe (one modal window, one
view-model, plain property binding and direct method calls suffice throughout).

`[ObservableProperty]`-backed properties exist for every bound value: `CurrentStep`, `AoiSource`, `AddressText`,
`IsBusy`, `ErrorText`, `GeocodeCandidates`, `SelectedGeocodeCandidate`, `ParcelCandidates`,
`SelectedParcelCandidate`, `BufferMeters`, `BufferErrorText`, `PointBudget`, `SelectedOutputUnit`,
`SelectedLevel`, `SelectedToposolidType`, `WriteSharedCoordinatesIfAbsent`, `Result`. `[RelayCommand]` methods:
`Geocode`, `FindParcel`, `Next`, `Back`, `Create`, `Cancel`. `Levels`/`ToposolidTypes` (from
`SolidGroundDialogInputs`, never reassigned after construction) and the computed
`ShowPointBudgetWarning`/`PointBudgetWarningText`/`SharedCoordinatesCheckboxEnabled`/`ShowNoParcelCandidatesMessage`/
`AddressWasGeocoded`/`ShowFindParcelGeocodedIntro`/`ShowFindParcelDirectPointIntro` properties round out the
bound surface without needing their own backing fields.

**Stale-parcel invalidation (review finding, major, fixed).** `partial void
OnSelectedGeocodeCandidateChanged(AddressGeocodeCandidate? value)` clears `ParcelCandidates`,
`SelectedParcelCandidate`, and `_parcelLookupAttempted` every time `SelectedGeocodeCandidate` is reassigned --
CommunityToolkit.Mvvm's source generator invokes this automatically, both from `Geocode`'s own two assignments
and from the operator picking a different existing item in the bound `GeocodeCandidates` list. Without it, a
parcel found for one confirmed address/point could silently survive an operator going Back and confirming a
different address/point, letting `Create` pair a stale parcel boundary with a self-contradictory
`AddressParcelProvenance` record and no error shown anywhere.

**Stale-address invalidation (review finding, blocker, fixed).** The stale-parcel fix above only fires when
`SelectedGeocodeCandidate` is itself reassigned -- it does nothing if the operator edits `AddressText` after a
candidate is already confirmed without a fresh *successful* `Geocode` (for example a re-geocode that fails or
times out, or simply never clicking Find again before Next). `CanGoNext`'s `AddressEntry` case now also
requires `AddressText.Trim()` to equal `_confirmedAddressText`, a new field set alongside both of `Geocode`'s
success branches (a real geocode, or a validated direct "latitude, longitude" entry) to the exact trimmed text
that produced the currently confirmed candidate. Kept as its own field rather than reusing
`_geocodedAddressText` (which is deliberately `null` on the direct-point-entry path, by
`AddressParcelProvenanceFactory.Create`'s own contract) so a valid direct-point confirmation does not
permanently disable Next. `AddressText`'s own `[NotifyCanExecuteChangedFor]` now also names `NextCommand`, so
Next's enabled state updates immediately as the operator types rather than only at the next unrelated command
re-evaluation. Without this fix, editing the address after confirming a candidate could silently carry the
stale `SelectedGeocodeCandidate`/`SelectedParcelCandidate` pairing through to `Create`, building the AOI and
`AddressParcelProvenance` for a different, earlier-confirmed location than what the address box currently
displays. `SolidGroundDialogViewModelRequiresAddressTextToStillMatchTheConfirmedCandidateBeforeAllowingNext`
(see "Tests" below) guards this.

## Theming and accessibility

**Landed this stage.** Read once, at `SolidGroundDialog`'s constructor, never subscribed
(`UIApplication.ThemeChanged`/`UIControlledApplication.ThemeChanged`/`SystemParameters.StaticPropertyChanged`
are all confirmed-present but deliberately unused — "modal only", matching "Non-goals" below):
`UIThemeManager.CurrentTheme` (`Autodesk.Revit.UI`, static property since 2014) and
`SystemParameters.HighContrast` (`System.Windows`, `PresentationFramework`).
`DialogTheme.Resolve(revitTheme, highContrast)` (`src/SolidGround.Revit/Dialog/DialogTheme.cs`) returns a small
`DialogPalette` record of eight `Brush` properties (`Window`, `WindowText`, `ControlText`, `ControlBackground`,
`Highlight`, `Error`, `GrayText`, `ActiveBorder`); `highContrast` wins over the `Light`/`Dark` choice — when
true, every brush comes from a `SystemColors.*Brush` member (`WindowBrush`, `WindowTextBrush`,
`ControlTextBrush`, `HighlightBrush`, `GrayTextBrush`, `ActiveBorderBrush` — not `ControlTextBrushKey`, whose
declared type is `ResourceKey`, not `Brush`; `ControlBackground` and `Error` reuse `WindowBrush`/
`WindowTextBrush` respectively, since WPF's `SystemColors` has no dedicated control-surface or error/danger
brush of its own); otherwise a small fixed Dark/Light palette. Revit's own API carries no HighContrast concept
at all; this branch is necessarily WPF/OS-level only.
`SolidGroundDialogSourceBranchesOnHighContrastAndOnTheRevitUiTheme` (see "Tests" below) guards that all three
signals stay present in source.

**`ControlBackground` (review finding, blocker, fixed).** WPF's `Control.Background` is not an inherited
dependency property the way `Foreground` is, so `SolidGroundDialog`'s own themed `Background` on the `Window`
never reached a descendant `TextBox`/`ComboBox`/`ListBox` on its own — every one of those seven controls
(`addressTextBox`, the geocode and parcel `ListBox`es, `bufferTextBox`, `pointBudgetTextBox`,
`levelComboBox`, `toposolidTypeComboBox`) rendered with its unthemed default (light) chrome, which under the
Dark or High Contrast palette meant white-on-light — effectively invisible — input text and candidate lists.
Each now sets `Background = palette.ControlBackground` explicitly, alongside its existing
`Foreground = palette.ControlText` (the two `ComboBox`es previously set neither and now set both).

**`Button` background (review finding, major, fixed).** The same non-inherited-`Background` gap applied
identically to every `Button` — `findButton`, `findParcelButton`, and the navigation bar's `cancelButton`/
`backButton`/`nextButton`/`createButton` all set `Foreground = palette.ControlText` but had no `Background`
of their own, so under the Dark or High Contrast palette each button's themed (for example white) label text
rendered over WPF's unthemed default (light) button face. All six now also set
`Background = palette.ControlBackground`, bringing the total to thirteen controls with an explicit
`Background`. `SolidGroundDialogSourceSetsControlBackgroundOnEveryButtonTextBoxComboBoxAndListBox` (renamed
from `...OnEveryTextBoxComboBoxAndListBox`; see "Tests" below) now asserts thirteen, not seven.

**`ComboBox` dropdown popup (review finding, major, fixed).** Setting `Background` on a `ComboBox` themes only
its closed selection box. WPF's stock (non-Fluent) `ComboBox` control template renders the dropdown popup from
a separately templated `Border` whose own `Background` is bound to the `SystemColors.WindowBrushKey` dynamic
resource, never to the control's own `Background` — so, unthemed, opening either `levelComboBox`'s or
`toposolidTypeComboBox`'s dropdown under the Dark palette showed the OS's plain light popup surface while the
popup's item text still inherited the ComboBox's own (correctly themed) white `Foreground`: white-on-white,
effectively invisible. Both now locally override that one resource key —
`comboBox.Resources[SystemColors.WindowBrushKey] = palette.ControlBackground` — immediately after
construction, re-theming only that ComboBox's own popup without touching any other control's use of the same
system key. `SolidGroundDialogSourceThemesEveryComboBoxDropdownPopupBackground` (see "Tests" below) guards
this.

**`Error` (review finding, major, fixed).** The top-level error banner and the buffer step's inline validation
message used to hardcode `Foreground = Brushes.Firebrick`, so neither ever varied with the resolved
`DialogPalette` — under the Dark palette this measured roughly 2:1 contrast against the window background,
short of WCAG AA's 4.5:1 text threshold, and under High Contrast it ignored the operator's own OS color scheme
entirely. Both now bind to `palette.Error`, which is `Brushes.Firebrick` in `LightPalette` (roughly 6:1
against that palette's near-white background), a lighter red in `DarkPalette` (roughly 4.9:1 against
`#2D2D30`), and `SystemColors.WindowTextBrush` in `HighContrastPalette`.
`SolidGroundDialogSourceNeverHardcodesABrushInsteadOfDrawingFromThePalette`,
`SolidGroundDialogSourceSetsControlBackgroundOnEveryButtonTextBoxComboBoxAndListBox`, and
`SolidGroundDialogSourceSetsControlTextForegroundOnEveryButtonComboBoxTextBoxAndListBox` (see "Tests" below)
guard all of the above.

**`Highlight` contrast (review finding, major, fixed).** `LightPalette`'s `Highlight` — used as `Foreground`
for the busy banner and both point-budget warning texts (none large or bold enough for WCAG's lower 3:1
"large text" allowance, and none sitting on any background besides the plain `Window` brush) — paired
`#0078D4` against this palette's `#F3F3F3` `Window`, measuring roughly 4.08:1: short of the same WCAG AA
4.5:1 text threshold `Error` above was already held to. `Highlight` is now `#005A9E` in `LightPalette`
(roughly 6.4:1 against `#F3F3F3`); `DarkPalette`'s `Highlight` (`#3B9CF2` against `#2D2D30`, roughly 4.72:1)
already passed and is unchanged. `DialogPaletteHighlightMeetsWcagAaTextContrastAgainstWindow` (see "Tests"
below) guards this for both palettes.

**Numeric-binding culture (review finding, minor, fixed).** `bufferTextBox`/`pointBudgetTextBox`'s two-way
`TextBox.Text` bindings to the `double`-typed `BufferMeters` and `int`-typed `PointBudget` view-model
properties carry no `Converter`/`ConverterCulture`, so WPF's own implicit numeric conversion resolved its
`CultureInfo` from this `Window`'s `Language` property — whose default metadata is hardcoded `en-US`
regardless of the OS's configured culture — rather than from `CultureInfo.CurrentCulture`. On a
comma-decimal locale (for example `de-DE`), a naturally typed value such as "7,5" was parsed under en-US
numeric rules instead of the operator's own locale, so the bound value could silently differ from what the
operator saw and intended, with no error surfaced anywhere in this dialog. `SolidGroundDialog`'s constructor
now also sets `Language = XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)` once,
alongside the theme/high-contrast reads above; `Language` is an inherited dependency property, so this one
assignment reaches both numeric `TextBox`es (and any future one) without a per-binding change.
`SolidGroundDialogSourceSetsWindowLanguageFromCurrentCultureOnce` (see "Tests" below) guards this.

Every control-building method calls `AutomationProperties.SetName(control, "...")` immediately after
constructing each interactive control (eighteen call sites total today — see "Content model and sections"
above for the exact inventory and `SolidGroundDialogAutomationInventory.ExpectedControlCount`), plus
`AutomationProperties.SetHelpText` for the buffer input, the point-budget input, and the shared-coordinates
checkbox.

## Package: CommunityToolkit.Mvvm 8.4.2

**Landed in Stage B; unchanged by Stage C.** `src/SolidGround.Revit/SolidGround.Revit.csproj` carries an
unconditional `PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2"` (both local and
CI/-p:UseRevitReferenceAssemblies=true builds; unlike the two `Nice3point.Revit.Api.*` CI-only compile
stand-ins, this is a real, shipped runtime dependency, never scoped to that condition), and `<UseWPF>true</UseWPF>`
replaced the project's old explicit `Microsoft.WindowsDesktop.App.WPF` `FrameworkReference`.

**Why this package is needed** (AGENTS.md dependency-policy documentation requirement): the dialog's eleven
content-model sections need `INotifyPropertyChanged` boilerplate, command wiring, and generator-backed
observable properties across roughly sixteen bound values; hand-writing that boilerplate is exactly the
"small, well-bounded" case the standard library alone does not cover as concisely, and `CommunityToolkit.Mvvm`
is Microsoft/.NET Foundation's own reference MVVM toolkit (MIT licensed). Stage C's real, full-size view-model
(not Stage B's one-property placeholder) confirms this at scale: the source generator produced every
`[ObservableProperty]`/`[RelayCommand]` member listed under "MVVM shape" above with no hand-written
boilerplate, compiling clean under this project's `TreatWarningsAsErrors=true` (including this repository's
own analyzer rules -- CA1822/CA1859 -- which the panel-builder methods above satisfy by being `static` or by
returning their own concrete panel type wherever they do not need instance state).

**Package facts, verified before implementation:** zero transitive package dependencies and no native code
under this project's real `net10.0-windows7.0` target moniker — the resolved compile/runtime asset is the
plain `lib/net8.0/CommunityToolkit.Mvvm.dll`, not the Windows-flavored `lib/net8.0-windows10.0.17763` asset
(the project's platform version, `7.0`, is below that asset's `10.0.17763` floor). Manifest resources on both
assets are exactly `{ "ILLink.Substitutions.xml" }` — no `.baml`, no `.g.resources`, no pack-URI-addressable
resource of any kind, so the package itself cannot trigger the isolated-add-in-context XAML/BAML double-load
bug (`Nice3point/RevitToolkit#7`, the still-open `dotnet/wpf#1700`) independent of this issue's own
zero-`.xaml` mitigation. The package's source generators/analyzers (`CommunityToolkit.Mvvm.CodeFixers.dll`,
`CommunityToolkit.Mvvm.SourceGenerators.dll`) are wired in only as build-time Roslyn analyzers; the built
output contains only `CommunityToolkit.Mvvm.dll`.

**THIRD-PARTY-NOTICES** carries a `CommunityToolkit.Mvvm 8.4.2` entry reproducing both the top-level MIT
license text and the package's own bundled `ThirdPartyNotices.txt` in full. Both lock files' single entry is
self-checked by `scripts/New-ReleasePackage.ps1`'s `Assert-ThirdPartyNoticesCoversLockedPackages` and
`tests/SolidGround.Tests/ReleasePackagingTests.ThirdPartyNoticesFileNamesEveryPackageInTheLockFileWithItsExactVersion`.

## Tests

**Landed in Stage A:**
`RevitIniToposolidThresholdsTests.ExceedsNativeThresholdMatchesPointBudgetAgainstTheParsedNativeThreshold`/
`.DescribeExceedanceNamesTheBudgetTheThresholdAndTheRevitIniPath` and
`TerrainProcessingPipelineTests.RunAsyncThreadsAddressParcelProvenanceIntoTheAssembledPayloadWhenSupplied`.

**Landed in Stage B:** `tests/SolidGround.Tests/RevitInteractiveDialogTests.cs` (new file) covers the
zero-`.xaml` repository check and every package-placement/version/lock-file check that stage's csproj change
needed (`RevitProjectContainsNoXamlFiles`, `CsprojPinsCommunityToolkitMvvmToTheApprovedVersionUnconditionally`,
`CsprojEnablesUseWpf`, `CsprojNoLongerDeclaresAnExplicitWpfFrameworkReference`,
`CommunityToolkitMvvmPackageReferenceAppearsInExactlyOneCsprojInTheRepository`,
`BothRevitPackageLockFilesContainCommunityToolkitMvvmAtTheApprovedVersion`).
`tests/SolidGround.Tests/RevitHostFilesTests.cs`'s pre-existing
`CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackages` was renamed to
`CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackagesAndOneUnconditionalCommunityToolkitMvvm`.

**Landed in Stage C:**

- `tests/SolidGround.Tests/LatitudeLongitudePointParserTests.cs` (new file): culture-invariant parsing
  (regression-tested against a `de-DE` current culture), WGS 84 range validation at and outside the boundary,
  telling an ordinary comma-bearing street address apart from an attempted coordinate pair, non-finite
  (`NaN`/`Infinity`) rejection, and the fixed, non-echoing out-of-range message text.
- `tests/SolidGround.Tests/AddressParcelProvenanceFactoryTests.cs` (new file): both halves built together, the
  geocode half omitted for a direct point entry, the parcel half omitted with no candidate, and the two
  `ArgumentException`/`ArgumentNullException` guard paths.
- `tests/SolidGround.Tests/RevitInteractiveDialogTests.cs` gained twelve more checks, all reading
  `src/SolidGround.Revit/Dialog/*.cs`'s committed source text directly (this test project never references
  `SolidGround.Revit` or loads its assembly):
  - `SolidGroundDialogSourceBranchesOnHighContrastAndOnTheRevitUiTheme` — `SystemParameters.HighContrast`,
    `SystemColors.`, and `UIThemeManager.CurrentTheme` all appear somewhere in the concatenated source.
  - `SolidGroundDialogSourceSetsAutomationPropertiesNameOnEveryDeclaredInteractiveControl` — a zero-slack,
    self-consistent match (not a minimum-count tolerance): reads both the actual
    `AutomationProperties.SetName(` call-site count and `SolidGroundDialogAutomationInventory.ExpectedControlCount`'s
    own declared value straight from source text and asserts they are equal, so a control's accessibility name
    cannot silently drop (or the constant silently drift) without the other catching it.
  - `SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies` — locates each of
    `Geocode()`/`FindParcel()`'s real method body by counting balanced braces from its own signature (not a
    fixed-size character window), and asserts each body contains its own source exception catch
    (`AddressGeocoderException`/`ParcelBoundarySourceException`) alongside a `catch (OperationCanceledException)`,
    with no bare `throw;`/`throw ex;` anywhere in that same extent.
  - `SolidGroundDialogViewModelClearsStaleParcelStateWhenTheSelectedGeocodeCandidateChanges` (review finding,
    major) — locates `OnSelectedGeocodeCandidateChanged`'s real method body the same way, and asserts it
    clears `ParcelCandidates`, `SelectedParcelCandidate`, and `_parcelLookupAttempted`.
  - `SolidGroundDialogViewModelRequiresAddressTextToStillMatchTheConfirmedCandidateBeforeAllowingNext` (review
    finding, blocker) — locates `CanGoNext`'s real switch-expression body and asserts its `AddressEntry` case
    references `_confirmedAddressText`, and locates `Geocode()`'s real method body and asserts both of its
    success branches assign `_confirmedAddressText = addressText;`.
  - `SolidGroundDialogSourceNeverHardcodesABrushInsteadOfDrawingFromThePalette` (review finding, major) —
    `SolidGroundDialog.cs` alone (not the whole concatenated Dialog source, since `DialogTheme.cs` legitimately
    defines its palettes from literal `Brushes.*` values) contains zero occurrences of `Brushes.`.
  - `SolidGroundDialogSourceSetsControlBackgroundOnEveryButtonTextBoxComboBoxAndListBox` (review finding,
    blocker, renamed from `...OnEveryTextBoxComboBoxAndListBox` when the same fix was extended to `Button`) —
    exactly thirteen `Background = palette.ControlBackground` call sites.
  - `SolidGroundDialogSourceThemesEveryComboBoxDropdownPopupBackground` (review finding, major) — exactly two
    `Resources[SystemColors.WindowBrushKey] = palette.ControlBackground` call sites (`levelComboBox`,
    `toposolidTypeComboBox`).
  - `SolidGroundDialogSourceSetsControlTextForegroundOnEveryButtonComboBoxTextBoxAndListBox` (review finding,
    minor) — exactly thirteen `Foreground = palette.ControlText` call sites.
  - `SolidGroundDialogSourceShowsDistinctProvenancePreviewWordingForAGeocodedAddressVersusADirectPointEntry`
    (review finding, major) — asserts `ShowFindParcelGeocodedIntro`/`ShowFindParcelDirectPointIntro` exist on
    the view-model and are referenced from the dialog source, the direct-entry sentence is present, and the
    old blanket "confirmed address" claim's own distinguishing text is gone.
  - `DialogPaletteHighlightMeetsWcagAaTextContrastAgainstWindow` (review finding, major) — parses each of
    `LightPalette`'s and `DarkPalette`'s own `Window`/`Highlight` `Color.FromRgb` literals straight from
    `DialogTheme.cs`'s committed source text and independently computes the WCAG relative-luminance contrast
    ratio, asserting at least 4.5:1 for both.
  - `SolidGroundDialogSourceSetsWindowLanguageFromCurrentCultureOnce` (review finding, minor) — asserts the
    exact `Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);`
    call site is present.

**Not implemented yet** (Stage D), planned:
`TerrainRequestSettingsTests.ShippedTemplateText`'s update for the new `"addressAndParcel"` settings section
(once that section is added); the `CheckRevitIniPointThreshold` call-site migration's paired test update
(`CreateToposolidCommandReadsRevitIniAndGuardsThePointBudgetAtPreflight`); and
`CreateToposolidCommandReturnsCancelledWithNoTaskDialogWhenTheDialogIsCancelled`.

## Manual evidence plan

Not started (Stage E). The accepted plan runs against a real Revit 2027 process, following this repository's
existing numbered-step convention: an end-to-end run through every section confirming the busy indicator's
visual timing; modal ownership; the shared-coordinates checkbox's no-session-persistence default; the Light,
Dark, and Windows High Contrast palettes; screen-reader `AutomationProperties.Name` announcements for every
control; running the command twice in one session with no leaked state; dialog Cancel at several points; an
in-dialog geocoder failure both by an unmatchable address and by a real network timeout; a point budget above
the machine's real `NativeToposolidMaxPointThreshold`; the shared-coordinates checkbox's already-coordinated
disabled state; both AOI-source choices (a parcel lookup, and the settings-file fallback); and one private
end-to-end run against a real property, recorded here only as having been done (no location, parcel, or other
identifying detail is ever recorded in this repository).

## Non-goals

- No settings write-back / "remembers last address, candidate, or point budget across sessions".
- No `UIControlledApplication.ThemeChanged`/`UIApplication.ThemeChanged` subscription, and no
  `SystemParameters.StaticPropertyChanged` subscription — both read once at construction ("modal only").
- No CLI change of any kind (a separate issue's own scope); `LatitudeLongitudePointParser` is deliberately
  independent of `SolidGround.Cli.Commands.ParcelCommand.ParsePoint`, not a shared replacement for it.
- No Extensible Storage schema version bump for the address/parcel provenance fields — that is a future
  schema version, deferred explicitly (AGENTS.md "Provenance decision";
  `docs/architecture/address-parcel-provenance.md`).
- No ribbon/command/manifest change (one tab/panel/button, unchanged, PH3-7).
- No `async`/`await`-yielding WPF commands for any network call (deliberately synchronous).
- No `WeakReferenceMessenger`/`ObservableRecipient` usage anywhere in this feature.
- No pre-dialog `OPENTOPOGRAPHY_API_KEY` check reordering (stays a Preflight-only concern, unchanged position).
- No `RevitAddressAndParcelSettings`/settings-file schema addition yet (deferred to Stage D, see "Settings
  interaction" above); `SolidGroundDialogInputs` takes an already-constructed geocoder/parcel source instead.
- **This stage specifically:** no wiring into `CreateToposolidCommand.Execute`; `SolidGroundDialog`/
  `SolidGroundDialogViewModel` are not shown from anywhere and have no effect on any command's runtime
  behavior.
