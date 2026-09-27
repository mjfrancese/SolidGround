# Revit interactive dialog

## Purpose and boundary

Issue #31 (PH3-4) replaces today's file/settings-only AOI input for `SolidGround.Revit` with one modal
`SolidGroundDialog` (code-behind only, zero `.xaml`/BAML), shown from inside `CreateToposolidCommand.Execute`
before today's Preflight/transaction. No new command, ribbon button, or panel (PH3-7 unchanged).
`SolidGround.Core` and `SolidGround.Cli` are otherwise untouched except for the small, Revit-free Core
additions this note names below.

**Status: complete.** Stages A, B, C, and D landed; the dialog is wired into `CreateToposolidCommand.Execute`.
Stage E's live Revit 2027 session then ran and found four defects, all fixed in Stage F. The full manual
evidence plan, the nearby-parcel-fallback follow-up, and a private real-property confirmation all finished
2026-09-27; see "Manual evidence (Revit 2027, 2026-09-27)" below for the executed session's full results and
the acceptance-criteria table (AC1-AC5, all met). This note records the whole accepted
design so later stages' code comments can cite its section titles verbatim; every stage below, including Stage
F's fix pass, has now been carried out:

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
  Stage C itself left `SolidGroundDialog`/`SolidGroundDialogViewModel` unconstructed anywhere in the shipped
  add-in; it was exercised during that stage only via a throwaway, uncommitted local WPF host, never
  committed to this repository.
- **Stage D (wiring into `CreateToposolidCommand.Execute`, 2026-09-27):** this stage. `CreateToposolidCommand.ExecuteCore`
  now runs a `LoadDocumentAndSettings` Stage 0 (document/settings load plus the read-once geometry-tolerance
  read, hoisted out of `RunDocumentPreflight`), a Stage 0.5 `SolidGroundDialogHost.ShowModal` call (owned via
  `WindowInteropHelper` against `UIApplication.MainWindowHandle`), and a slimmed Stage 1 `RunDocumentPreflight`
  that takes the dialog's confirmed result. `SolidGroundDialogHost` (new file,
  `src/SolidGround.Revit/Dialog/SolidGroundDialogHost.cs`) builds `SolidGroundDialogInputs` from the already-open
  `Document` and already-loaded `RevitSettings`: it lists every real `Level`/`ToposolidType` via
  `LevelAndTypeResolver.ListLevels`/`.ListToposolidTypes` (new; replaces the former `ResolveLevel`/
  `ResolveToposolidType`, which applied the "configured name wins, else default" rule itself -- now the dialog's
  own job, against the raw candidate list), constructs the real `IAddressGeocoder`/`IParcelBoundarySource` from
  the new `RevitAddressAndParcelSettings` settings section (see "Settings interaction" below), and re-reads
  `Revit.ini` a second, independent time for its own inline point-budget warning. `SolidGroundDialog.cs` gained
  `cancelButton.IsCancel = true` (Esc cancels from anywhere in the dialog) and a dynamic `UpdateDefaultButton`
  that sets `IsDefault` on exactly one of Find/Find parcel/Next/Create at a time -- the step's own real primary
  action -- recomputed whenever `CurrentStep` or `NextCommand`'s own `CanExecute` result might have changed (a
  fixed, always-true `IsDefault` on more than one at once would be genuinely ambiguous during the one real
  overlap window this dialog has: after a successful search, before Next is pressed). See "Result-code mapping",
  "AOI and provenance", and "Settings interaction" below for the full behavior-changing detail; "Tests" below
  for what backstops each claim.
- **Stage E (manual evidence + docs update):** this stage. A live Revit 2027 session ran the manual evidence plan
  below and found four defects, all now fixed in **Stage F**; see "Stage E live-session findings and Stage F
  fixes" below for the full write-up.

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
   predicted Preflight rejection. Re-check finding, minor, fixed (Stage D): this step also shows a second,
   always-on inline error (`PointBudgetRangeErrorText`), visible exactly when `PointBudget` falls outside
   `SimplificationSettings.MinPointBudget`/`.MaxPointBudget`'s own inclusive bound (the identical bound
   `TerrainRequestSettings.Validate()` already enforces for every settings-file-sourced value) — unlike the
   `Revit.ini`-native warning above, this check is never tolerant-skipped (it depends on nothing but the
   entered number), and `CanGoNext`'s own `PointBudget` case now requires it to be absent before allowing
   `Next`. Before this fix, `CanGoNext` enforced only `PointBudget > 0`, so an operator who entered a value
   above 50,000 learned about it only after completing the remaining steps and pressing Create, when
   `CreateToposolidCommand`'s post-merge `effectiveSettings.Request.Validate()` call (see "Result-code mapping")
   rejected the whole run with no retained dialog state.
6. **Unit choice** — `LengthUnit.UsSurveyFoot`/`LengthUnit.InternationalFoot` only (never `LengthUnit.Meter`),
   prefilled from `settings.Request.OutputUnit`, falling back to `UsSurveyFoot` if the settings file had
   `Meter` configured.
7. **Level/toposolid-type** — two `ComboBox`es bound to `IReadOnlyList<NamedElevationCandidate>`/
   `IReadOnlyList<NamedCandidate>` (`SolidGround.Core.Processing`), populated by
   `LevelAndTypeResolver.ListLevels`/`.ListToposolidTypes` (Stage D; replaces the former `ResolveLevel`/
   `ResolveToposolidType`, which applied the selection rule itself), defaulted by the same
   `NamedElevationSelector`/`NamedSelector` Core selectors applied to `settings.Target.LevelName`/
   `.ToposolidTypeName`. `SolidGroundDialogViewModel` never touches `Document`/`Level`/`ToposolidType` itself:
   `SolidGroundDialogHost.ShowModal` (Stage D) resolves the two full candidate lists first and passes them in
   through `SolidGroundDialogInputs`; `CreateToposolidCommand`'s own Preflight then maps the dialog's confirmed
   candidate back to the real element by id (`LevelAndTypeResolver.FindLevelById`/`.FindToposolidTypeById`).
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
always `null`, and `CreateToposolidCommand.RunDocumentPreflight` (Stage D) falls back to the existing, unchanged
`AoiSettingsFactory.Build` path in that case — exactly as it already worked before this issue. `SolidGround.Cli`'s
own `AoiSelection`/`AoiSettingsFactory` paths are untouched by this issue either way.

**Construction, entirely in memory, no new file (landed in `SolidGroundDialogViewModel.Create`, Stage C).**
Once a parcel candidate is confirmed (end of section 3) and the operator reaches "Create", the view-model
calls the existing, unchanged `SolidGround.Core.Processing.ParcelBoundaryAoiFactory.FromCandidate(candidate,
buffer)`. The resulting `ParcelGeometryAoi` becomes `SolidGroundDialogResult.Aoi`; **landed, Stage D:**
`CreateToposolidCommand.RunDocumentPreflight` uses it directly as `context.Aoi` on the `FindParcel` path, which
then reaches `TerrainProcessingPipeline.RunAsync`'s existing `AreaOfInterest? aoi` parameter exactly as any
other AOI does — never written to a `.wkt`/`.aoi.json` file the way `SolidGround.Cli`'s `ParcelCommand` does.

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
`SolidGround.Cli`'s own `process`/`run` callers keep compiling unchanged, always passing `null`. **Landed, Stage
D:** `CreateToposolidCommand.RunFetchPipelineAsync`/`RunProcessPipelineAsync` (and the `RunPipelineAsync`
dispatcher between them) each gained a matching `AddressParcelProvenance? addressParcel` parameter, threaded
straight through to their own `TerrainProcessingPipeline.RunAsync` call site; `ExecuteCore`'s Stage 2 acquisition
call passes `dialogResult.AddressParcel` (always `null` on the `UseSettingsFile` path, by
`SolidGroundDialogResult`'s own contract; populated on the `FindParcel` path).

**The other new Core surface, already landed (Stage A):**
`src/SolidGround.Core/Hosting/RevitIniToposolidThresholds.cs` gained two `public static` methods beside the
existing `Parse`: `ExceedsNativeThreshold(int pointBudget, Thresholds thresholds)` and
`DescribeExceedance(int pointBudget, int nativeThreshold, string revitIniPath)`. `DescribeExceedance`'s text is
byte-identical to `CreateToposolidCommand`'s pre-existing inline `CheckRevitIniPointThreshold` message — a pure
extract, not a reword — so existing behavior is unchanged; only its location (now Core, shared) changes.
**Landed, Stage D:** `CheckRevitIniPointThreshold` is now a two-line call into these (the literal problem-line
prose no longer appears in `CreateToposolidCommand.cs` at all); `SolidGroundDialogViewModel.PointBudgetWarningText`/
`.ShowPointBudgetWarning` (Stage C) already called the identical pair for section 5's inline warning, so the two
have never drifted apart.

## Settings interaction: prefill, not override

**Reads only; no write-back (owner decision 2).** `SolidGroundDialogViewModel` never reads
`RevitSettings`/`RevitSettingsIo` directly — it has no dependency on `SolidGround.Revit.Settings` at all.
Instead, `SolidGroundDialogInputs` carries whatever `SolidGroundDialogHost.ShowModal` (Stage D) already resolved
from the already-loaded settings: `PrefilledOutputUnit`, `PrefilledPointBudget`, `ConfiguredLevelName`/
`ConfiguredToposolidTypeName`, `PrefilledWriteSharedCoordinatesIfAbsent`, `DocumentAlreadyHasSharedCoordinates`,
and `ConfiguredAreaOfInterest` (today's settings-driven `AoiSettings`, shown in section 0/10's own summary
text). The operator's in-dialog choice always wins for the run about to happen; nothing is written back to
`%ProgramData%\SolidGround\Revit\settings.json` —
`RevitHostFilesTests.CreateToposolidCommandNeverWritesBackToTheSettingsFile`/
`.RevitSettingsIoDeclaresExactlyOneFileWriteCallSiteTheTemplateCreationItself` guard this structurally. "Last-used
value" persistence across sessions is a named, explicit non-goal (see "Non-goals" below), not silently dropped.

**The new settings surface, landed Stage D.** `RevitSettings` (`src/SolidGround.Revit/Settings/RevitSettings.cs`)
gained a fourth constructor parameter, `RevitAddressAndParcelSettings AddressAndParcel` (new file,
`src/SolidGround.Revit/Settings/RevitAddressAndParcelSettings.cs`): `GeocoderProvider`, `CountyRegistryPath`,
`CountyGeoidOverride`, `LocalParcelFilePath`, `LocalParcelFileSourceLabel`, `LocalParcelFileLicenseDisclaimerText`.
`RevitSettingsIo.TemplateJson`/`TryLoad` gained a fifth hand-decoded top-level `"addressAndParcel"` section,
following the exact existing pattern the `level`/`toposolidType`/`sharedCoordinates` sections already use: read
via `JsonNode` indexers (an absent section decodes to every field at its documented default -- an existing
settings file written before this section existed still loads unchanged), then `root.Remove("addressAndParcel")`
before the rest decodes as `TerrainRequestSettings`. `"census"|"geocodio"|"esri"` matches
`SolidGround.Cli.Commands.GeocodeCommand.ParseProvider`'s own existing token spelling exactly, as a small,
independent switch inside `RevitSettingsIo.cs` (`SolidGround.Revit` cannot reference `SolidGround.Cli`).
`SolidGroundDialogHost.ShowModal` (new file, `src/SolidGround.Revit/Dialog/SolidGroundDialogHost.cs`) constructs
the real `IAddressGeocoder` via the existing, unchanged `AddressGeocoderFactory.Create`, and the real
`IParcelBoundarySource?`: `CountyRegistryPath` wins when non-blank (a new Core type,
`SolidGround.Core.Sources.CountyParcels.AutoGeoidCountyParcelSource`, wraps `CountyParcelRegistrySource` with the
same auto-GEOID behavior `SolidGround.Cli.Commands.ParcelCommand` already gives the CLI — `CountyGeoidOverride`
wins when non-blank, else the GEOID is resolved from the confirmed geocode candidate's own coordinates via
`CensusCountyLookup.FindCountyGeoidAsync`, since — unlike the CLI's own `ParcelCommand` — the Revit host must
construct one `IParcelBoundarySource` before the first query's own coordinates are known); else
`LocalParcelFilePath` wins when non-blank (`LocalParcelFileSource`); else `null` (`SolidGroundDialogViewModel.FindParcel`,
Stage C, already shows a clear inline configuration message the first time a parcel lookup is attempted with no
source configured). **Review finding, major, fixed:** `BuildParcelSource`'s own `CountyParcelRegistry.Load` call
is wrapped in a `try`/`catch (CountyParcelRegistryFormatException)` — a missing/unreadable file, invalid JSON, or
any other documented content problem no longer escapes `ShowModal` uncaught (which would otherwise have blocked
the *entire* dialog, including the unrelated "Use the area in the settings file" path, and reached only
`CreateToposolidCommand.Execute`'s generic top-level catch). The caught failure is instead deferred to a small
`FailedParcelSource` stand-in whose own `FindAsync` raises `AutoGeoidCountyParcelSourceException`, reported
inline the first time `FindParcel` actually attempts a lookup — exactly the same never-at-construction-time
contract this method already gave the "neither path configured" case. `ShowModal` also lists every real
`Level`/`ToposolidType` via the refactored
`LevelAndTypeResolver.ListLevels`/`.ListToposolidTypes` (replacing the former `ResolveLevel`/`ResolveToposolidType`,
which applied the "configured name wins, else default" selection itself; the dialog's own view-model constructor
now calls `NamedElevationSelector`/`NamedSelector` directly against the raw candidate list, exactly as Stage C
already assumed), and re-reads `Revit.ini` a second, independent time (`ReadRevitIniThresholds`) for its own
inline point-budget warning — "no shared cache needed, `Revit.ini` is not expected to change mid-session",
matching this repository's own established convention for this exact file.

**Re-validation, review finding fixed.** At the time this fix landed, the Point Budget step's own `CanGoNext`
case enforced only `PointBudget > 0` — it had no upper bound, unlike `TerrainRequestSettings.Validate()`'s
1-50000 rule, which every settings-file-sourced `pointBudget` was already held to before this issue
(`RevitSettingsIo.TryLoad`'s own call, at Stage 0). Rather than duplicate that bound as a second literal into
this WPF step, `CreateToposolidCommand.ExecuteCore` calls `effectiveSettings.Request.Validate()` immediately
after building `effectiveSettings` (still before Stage 1 Preflight begins) and routes any problem through the
identical `ShowProblemList`/`Result.Cancelled` path Stage 0/Stage 1 already use — restoring, for the
dialog-overridden path too, the invariant that every `TerrainRequestSettings` reaching Stage 2 satisfies
`Validate()`. See "Result-code mapping" and "Tests" below.

**Re-check finding, minor, fixed.** The command-layer fix above closed the data-integrity gap but left the
Point Budget step itself with no matching inline feedback: an operator who entered an out-of-range value still
had to complete every remaining step — including a live address/parcel network round trip on the `FindParcel`
path — before Create finally bounced them to `Result.Cancelled`, discarding the whole interactive session with
no retained state. `SolidGroundDialogViewModel` now exposes `PointBudgetRangeErrorText`, non-null exactly when
`PointBudget` falls outside `SimplificationSettings.MinPointBudget`/`.MaxPointBudget`'s own inclusive bound —
the identical constants `TerrainRequestSettings.Validate()` reads, extracted from that method's own former 1/
50,000 literals specifically so the two could never drift apart. Unlike `PointBudgetWarningText` (the
`Revit.ini`-native threshold, tolerant-by-design whenever that file could not be read this session),
`PointBudgetRangeErrorText` is always on: it depends on nothing but the entered number. `CanGoNext`'s
`PointBudget` case now requires it to be `null` (subsuming the old bare positivity check), and
`BuildPointBudgetPanel` binds a visible error `TextBlock` to it, mirroring the buffer panel's own
`BufferErrorText`/`TextPresenceToVisibility` idiom. The command-layer `effectiveSettings.Request.Validate()`
call is kept regardless, as defense-in-depth. See "Result-code mapping" and "Tests" below.

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

**Landed, Stage D.** `CreateToposolidCommand.ExecuteCore` now runs this exact stage sequence: Stage 0
(`LoadDocumentAndSettings`: load settings, the open-document check, and the geometry-tolerance read, hoisted out
of `RunDocumentPreflight` so the dialog can use them before Preflight formally runs — also guards "this document
has at least one Level/ToposolidType" before the dialog ever opens, since the dialog itself always resolves a
specific one from whatever candidates exist and has no way to report "none at all" on its own), Stage 0.5
(`SolidGroundDialogHost.ShowModal`, returning `null` on Cancel), a slimmed Stage 1 Preflight
(`RunDocumentPreflight`: AOI dialog-supplied when `AoiSource` is `FindParcel`, derived from settings exactly as
before this issue when it is `UseSettingsFile`; Level/ToposolidType always dialog-supplied, mapped back to the
real element by id, with no defensive re-verification per owner decision 4), then Stages 2-6 unchanged in shape
except that Stage 2's acquisition threads `dialogResult.AddressParcel` into the pipeline call (see "AOI and
provenance" above). Mapping (extends, does not replace, the policy already in place before this issue):

| Outcome | `Result` |
| --- | --- |
| Settings/document load problem (Stage 0) | `Cancelled` (unchanged shape) |
| Dialog Cancel / closed without Create (Stage 0.5) | `Cancelled` (new; no `TaskDialog`) |
| In-dialog geocoder/parcel lookup failure | *(never reaches `Result` at all — caught inline, dialog stays open)* |
| Dialog-merged `effectiveSettings.Request.Validate()` finds a problem (after Stage 0.5, before Stage 1) | `Cancelled` (new; shared `ShowProblemList` dialog shown — see "Settings interaction" above) |
| Preflight rejection, including PH3-3's shared-coordinates refusal (Stage 1) | `Cancelled` (unchanged) |
| Acquisition failure (Stage 2) | `Cancelled` (unchanged) |
| Geometry Preflight rejection (Stage 3) | `Cancelled` (unchanged) |
| Transaction rolled back (Stage 5) | `Cancelled` (unchanged) |
| Unconfirmed transaction end status (Stage 5) | `Failed` (unchanged — the only `Failed` path) |
| `Execute`'s top-level catch | `Cancelled` (unchanged — no transaction can be open) |

An in-dialog lookup failure never reaching `Execute`'s top-level catch is satisfied by construction and
guarded offline: `Geocode`/`FindParcel` catch `AddressGeocoderException`/`ParcelBoundarySourceException`
**and** `OperationCanceledException` internally and never rethrow (see "Threading and the network bridge"
above; `SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies` in "Tests" below).
That construction argument originally covered only `Geocode`/`FindParcel`'s own internal catches — it did not
account for `BuildParcelSource`'s separate, earlier, construction-time `CountyParcelRegistry.Load` call, whose
own `CountyParcelRegistryFormatException` used to propagate straight out of `ShowModal` uncaught (review
finding, major, fixed; see "Settings interaction" above for the `FailedParcelSource` deferral that closes this
gap). With that fix, every reachable in-dialog/parcel-source-configuration failure is deferred the same way.
The command-side half of this table — `if (dialogResult is null) { return Result.Cancelled; }`, no `TaskDialog`
shown — is now landed and guarded by
`RevitHostFilesTests.CreateToposolidCommandReturnsCancelledWithNoTaskDialogWhenTheDialogIsCancelled`;
`.CreateToposolidCommandShowsTheInteractiveDialogBeforePreflightAndBeforeAnyTransaction` guards the stage
ordering itself (`ShowModal(` before `RunDocumentPreflight(` before `transaction.Start()`). The new
dialog-merged-settings row above (review finding, major, fixed) is guarded by
`.CreateToposolidCommandBuildsEffectiveSettingsFromTheDialogsOutputUnitPointBudgetAndSharedCoordinatesChoice`
(the merge itself sources each field from the right `dialogResult` property) and
`.CreateToposolidCommandRevalidatesTheEffectiveSettingsAfterTheDialogMergeAndBeforePreflight` (the
re-validation call exists, runs in the right order, and maps a problem to `Cancelled` through the shared dialog).

**Re-check finding, minor, fixed.** At the time the paragraph above was written, `effectiveSettings.Request.Validate()`
was the *only* place an out-of-range dialog-entered `PointBudget` was ever caught: the dialog's own Point
Budget step (section 5 above) enforced merely `PointBudget > 0`, so a value above
`SimplificationSettings.MaxPointBudget` sailed through every remaining step and was rejected only when the
operator finally pressed Create — discarding the whole interactive session, with no retained state, for a
mistake the Point Budget step itself could have caught immediately. `SolidGroundDialogViewModel.CanGoNext`'s
`PointBudget` case now also requires the new `PointBudgetRangeErrorText` property to be `null` (see section 5
above), reusing `SimplificationSettings.MinPointBudget`/`.MaxPointBudget` rather than a second, dialog-local
literal, so the two bounds can never drift apart. This row in the mapping table is therefore no longer
reachable by entering an out-of-range `PointBudget` through the dialog's own normal navigation; it remains as
defense-in-depth against any future dialog change (or any other future caller of `ExecuteCore`) that supplies
an `effectiveSettings` value the dialog itself never validated. Guarded by
`SolidGroundDialogViewModelCanGoNextEnforcesTheFullInclusivePointBudgetRangeNotJustPositive`,
`.PointBudgetRangeErrorTextReusesSimplificationSettingsBoundsNotADuplicatedLiteral`, and
`.SourceBindsAnAlwaysOnPointBudgetRangeErrorAlongsideTheRevitIniWarning` (all `RevitInteractiveDialogTests`,
see "Tests" below).

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
requires `AddressText.Trim()` to equal `ConfirmedAddressText` (backed by the `_confirmedAddressText` field --
see "Stale-address invalidation, follow-up" immediately below for why that field is now
`[ObservableProperty]`-backed with its own `NotifyCanExecuteChangedFor(nameof(NextCommand))`), set alongside
both of `Geocode`'s success branches (a real geocode, or a validated direct "latitude, longitude" entry) to the
exact trimmed text that produced the currently confirmed candidate. Kept as its own field rather than reusing
`_geocodedAddressText` (which is deliberately `null` on the direct-point-entry path, by
`AddressParcelProvenanceFactory.Create`'s own contract) so a valid direct-point confirmation does not
permanently disable Next. `AddressText`'s own `[NotifyCanExecuteChangedFor]` now also names `NextCommand`, so
Next's enabled state updates immediately as the operator types rather than only at the next unrelated command
re-evaluation. Without this fix, editing the address after confirming a candidate could silently carry the
stale `SelectedGeocodeCandidate`/`SelectedParcelCandidate` pairing through to `Create`, building the AOI and
`AddressParcelProvenance` for a different, earlier-confirmed location than what the address box currently
displays. `SolidGroundDialogViewModelRequiresAddressTextToStillMatchTheConfirmedCandidateBeforeAllowingNext`
(see "Tests" below) guards this.

**Stale-address invalidation, follow-up (Stage E live-session finding, defect A, fixed in Stage F).** The fix
immediately above was necessary but not sufficient: Next itself stayed *disabled* after a successful Find (for
both an address and a "latitude, longitude" pair), because `_confirmedAddressText` was a bare field, and both of
`Geocode`'s success branches assigned `SelectedGeocodeCandidate` (which re-evaluates `NextCommand` through its
own `NotifyCanExecuteChangedFor`) *before* assigning `_confirmedAddressText` -- so `CanGoNext`'s `AddressEntry`
case was re-checked one statement too early, against the *previous* confirmed value, and nothing re-evaluated
`NextCommand` a second time afterward. See "Stage E live-session findings and Stage F fixes" below for the fix
and every other defect that same session found.

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
    finding, blocker; corrected in Stage F — see "Stage E live-session findings and Stage F fixes" below) — as
    landed in Stage C, located `CanGoNext`'s real switch-expression body and asserted its `AddressEntry` case
    referenced `_confirmedAddressText`, and located `Geocode()`'s real method body and asserted both of its
    success branches assigned `_confirmedAddressText = addressText;`. Both checks now instead reference the
    generated `ConfirmedAddressText` property, not the bare field (see the Stage F entry below).
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

**Landed in Stage D:**

- `TerrainRequestSettingsTests.ShippedTemplateText` gained the matching `"addressAndParcel"` block;
  `JsonOptionsDecodesTheShippedTemplateTextVerbatim` strips that fourth key alongside the existing three.
- `RevitHostFilesTests.CreateToposolidCommandReadsRevitIniAndGuardsThePointBudgetAtPreflight`'s prose-content
  assertion was replaced with `Assert.Contains("RevitIniToposolidThresholds.ExceedsNativeThreshold", ...)` and
  `.DescribeExceedance` (the two new call sites); the other three assertions are unchanged.
- `RevitHostFilesTests.CreateToposolidCommandReturnsCancelledWithNoTaskDialogWhenTheDialogIsCancelled` —
  locates `if (dialogResult is null)`, asserts a short following window contains `return Result.Cancelled;` and
  not `TaskDialog.Show(`.
- `RevitHostFilesTests.CreateToposolidCommandShowsTheInteractiveDialogBeforePreflightAndBeforeAnyTransaction` —
  the stage-ordering guard named above.
- `RevitHostFilesTests.CreateToposolidCommandDerivesTheAoiFromSettingsOnlyWhenTheDialogChoseTheSettingsFile` —
  both AOI paths (owner decision 1's refinement) stay real: `dialogResult.Aoi` is used directly on the
  `FindParcel` branch; `AoiSettingsFactory.Build`/the parcel-file read are still present, gated behind the
  opposite branch of the identical `DialogAoiSource.FindParcel` check.
- `RevitHostFilesTests.CreateToposolidCommandThreadsTheDialogsAddressParcelProvenanceIntoTheAcquisitionPipeline` —
  `dialogResult.AddressParcel` reaches the `RunPipelineAsync` call site, and the new parameter/argument appear
  at every expected call site (three method signatures, two `TerrainProcessingPipeline.RunAsync` call sites).
- `RevitHostFilesTests.CreateToposolidCommandNeverWritesBackToTheSettingsFile`/
  `.RevitSettingsIoDeclaresExactlyOneFileWriteCallSiteTheTemplateCreationItself` — the "prefill, not override"
  contract, guarded both at `CreateToposolidCommand.cs`'s own one caller and at `RevitSettingsIo.cs` itself.
- `RevitHostFilesTests.CreateToposolidCommandBuildsEffectiveSettingsFromTheDialogsOutputUnitPointBudgetAndSharedCoordinatesChoice`
  (review finding, coverage gap, major, fixed) — the `effectiveSettings` `with` expression sources `OutputUnit`/
  `PointBudget`/`SharedCoordinates` from the right `dialogResult` property; previously the only piece of Stage D
  wiring with no dedicated test.
- `RevitHostFilesTests.CreateToposolidCommandRevalidatesTheEffectiveSettingsAfterTheDialogMergeAndBeforePreflight`
  (review finding, major, fixed) — the new `effectiveSettings.Request.Validate()` call (see "Settings
  interaction" above) runs after the merge and before `RunDocumentPreflight(`, and a problem maps to
  `ShowProblemList`/`Result.Cancelled`.
- `RevitInteractiveDialogTests.SolidGroundDialogSourceSetsIsCancelOnTheCancelButton`/
  `.UpdateDefaultButtonSetsIsDefaultOnExactlyOnePerStepPrimaryActionAtATime`/
  `.SolidGroundDialogRecomputesTheDefaultButtonWheneverCurrentStepOrNextCommandCanExecuteChanges` — the
  Esc/Enter keyboard behaviour (IsCancel/IsDefault; see "Purpose and boundary" above).
- A new Core test file, `AutoGeoidCountyParcelSourceTests.cs`, covers `AutoGeoidCountyParcelSource`: a
  configured GEOID override skips the Census lookup entirely; a blank override resolves one from the Census
  county lookup then queries the registry; an address query with no override throws before any network call;
  a `CensusCountyLookupException` is wrapped, never rethrown raw; a genuine Census-side network timeout is
  likewise wrapped (mirroring `CountyParcelRegistrySourceTests`' own "general `OperationCanceledException`
  branch" pattern, since the caller's own token is never cancelled in this specific test).
- `RevitInteractiveDialogTests.BuildParcelSourceCatchesARegistryLoadFailureAndNeverLetsItEscapeShowModal`/
  `.FailedParcelSourceThrowsAutoGeoidCountyParcelSourceExceptionFromFindAsync` (review finding, major, fixed) —
  locate `BuildParcelSource`'s and `FailedParcelSource`'s own real bodies (balanced-brace extraction, not a
  fixed-size window) and assert the `CountyParcelRegistry.Load` call site sits inside a
  `catch (CountyParcelRegistryFormatException ...)`-guarded `try`, and that the stand-in's `FindAsync` raises
  `AutoGeoidCountyParcelSourceException`.
- `RevitInteractiveDialogTests.SolidGroundDialogClassRemarksDescribeStageDWiringNotStageCsUnreachableState`
  (review finding, minor, fixed) — `SolidGroundDialog.cs`'s own class-level doc comment no longer claims the
  type is unconstructed/unreachable/a later stage's own scope.
- `RevitInteractiveDialogTests.SolidGroundDialogViewModelCanGoNextEnforcesTheFullInclusivePointBudgetRangeNotJustPositive`/
  `.SolidGroundDialogViewModelPointBudgetRangeErrorTextReusesSimplificationSettingsBoundsNotADuplicatedLiteral`/
  `.SolidGroundDialogSourceBindsAnAlwaysOnPointBudgetRangeErrorAlongsideTheRevitIniWarning` (re-check finding,
  minor, fixed — see "Content model and sections" step 5 and "Result-code mapping" above) — `CanGoNext`'s
  `PointBudget` case now depends on `PointBudgetRangeErrorText` rather than a bare positivity check;
  `PointBudgetRangeErrorText` itself reuses `SimplificationSettings.MinPointBudget`/`.MaxPointBudget` rather
  than a duplicated literal; `BuildPointBudgetPanel` binds a visible `TextBlock` to it, mirroring the buffer
  panel's own `BufferErrorText`/`TextPresenceToVisibility` idiom.
- `TerrainRequestSettingsTests.ValidateAcceptsPointBudgetAtEitherInclusiveBoundaryConstant` (Core; additive) —
  `SimplificationSettings.MinPointBudget`/`.MaxPointBudget` themselves stay genuinely inclusive after this
  extraction; the pre-existing `ValidateReportsOutOfRangePointBudget` theory already proves one past each end
  is still rejected.

**Landed in Stage F** (see "Stage E live-session findings and Stage F fixes" below for the full defect writeup;
every test below was confirmed to fail against the pre-Stage-F source and pass after the fix):

- `RevitInteractiveDialogTests.SolidGroundDialogViewModelConfirmedAddressTextIsObservableAndReEvaluatesNextCommandItself`
  (new, defect A) — asserts `[ObservableProperty]`/`[NotifyCanExecuteChangedFor(nameof(NextCommand))]` both
  immediately precede the `_confirmedAddressText` field declaration.
- `.SolidGroundDialogViewModelRequiresAddressTextToStillMatchTheConfirmedCandidateBeforeAllowingNext` (corrected,
  not weakened) — its assignment-count check now looks for `ConfirmedAddressText = addressText;` (the generated
  property, which defect A's fix requires both of `Geocode`'s success branches to write through instead of the
  bare field), and its `CanGoNext` check now looks for `ConfirmedAddressText` (the property read, required by
  CommunityToolkit.Mvvm's own `MVVMTK0034` analyzer once the field is `[ObservableProperty]`-annotated) rather
  than the bare `_confirmedAddressText` field name either previously pinned.
- `.SolidGroundDialogViewModelShowNoParcelCandidatesMessageIsPublicNotInternal` (new, defect C) and
  `.SolidGroundDialogViewModelPointBudgetWarningPropertiesArePublicNotInternal` (new, defect D) — each asserts
  its own named property is declared `public`, not `internal`.
- `.SolidGroundDialogViewModelPointBudgetRangeErrorTextReusesSimplificationSettingsBoundsNotADuplicatedLiteral`
  and `.SolidGroundDialogSourceShowsDistinctProvenancePreviewWordingForAGeocodedAddressVersusADirectPointEntry`
  (both corrected, not weakened) — now look for `public`, not `internal`, on the properties they were already
  asserting exist (`PointBudgetRangeErrorText`; `ShowFindParcelGeocodedIntro`/`ShowFindParcelDirectPointIntro`).
- `.SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal` (new; general,
  forward-looking) — reads every `nameof(SolidGroundDialogViewModel.X)` argument to a `new Binding(...)` call in
  `SolidGroundDialog.cs`, reads every hand-written `internal`/`public <Type> X => ...`-shaped property
  declaration in `SolidGroundDialogViewModel.cs`, and asserts none of the bound names is declared `internal` —
  catching any future instance of this same mistake, not only the six named above.

## Manual evidence plan

**Status: complete (2026-09-27).** See "Manual evidence (Revit 2027, 2026-09-27)" below for the executed
session's full results, including the four Stage E/F defects' live re-confirmation, the nearby-parcel-fallback
verification (step 15's own private finding and its synthetic reproduction), and the private real-property
confirmation itself (step 15).

**Finalized for AC5 (Stage D); run against a real Revit 2027 process (Stage E), which found four defects fixed
in Stage F -- see "Stage E live-session findings and Stage F fixes" below.** Follows this repository's existing
numbered-step convention (for example `docs/architecture/revit-toposolid-creation.md`'s
own "Manual evidence plan"). Every step below needs Stage D's real wiring to be reachable at all; none of them
can run before this stage lands.

1. Build and deploy `SolidGround.Revit` (`UseWPF=true`, `CommunityToolkit.Mvvm` 8.4.2); launch Revit 2027; run
   `CreateToposolidCommand` once end-to-end against the public example site via the **`FindParcel`** AOI-source
   path (address or "latitude, longitude" entry through parcel confirmation), configured against the local
   parcel file source (`addressAndParcel.localParcelFilePath`, no network dependency for the parcel half) so
   this step is reproducible offline apart from Revit itself; confirm all eleven content-model sections appear
   in order and Create produces the same toposolid/`PropertyLine` outcome Issues #15/#30 already verified, with
   `AddressParcelProvenance` now populated in the exported provenance record. While performing the lookup,
   confirm the busy indicator (the disabled Find button/banner) actually becomes visible before the UI freezes,
   not only after it unfreezes.
2. Confirm modal ownership: while the dialog is open, attempt to click back into Revit's main window; confirm
   it is blocked/inactive and the dialog stays on top (`WindowInteropHelper.Owner` against
   `UIApplication.MainWindowHandle`).
3. **The coordinate-pair input, offline, with the local parcel file source.** Repeat step 1's `FindParcel` path,
   but type a `"latitude, longitude"` pair into the address field instead of a street address; confirm
   `LatitudeLongitudePointParser` accepts it with no network call at all (Revit stays responsive through that
   step), the synthetic geocode candidate and its own attribution text appear correctly, and the provenance
   preview's direct-point-entry sentence (not the geocoded-address sentence) is shown.
4. **The settings-file AOI path.** With `settings.json` configured with a `boundingBox`/`radius`/`parcel` area of
   interest, run the command again and choose "Use the area in the settings file" at step 0; confirm steps 1-4
   (address through buffer) are skipped entirely, the settings-file AOI summary text is accurate, no
   address/parcel provenance is attached to the run, and the created toposolid matches what a pre-Issue-#31 run
   against the identical settings file would have produced.
5. With Revit set to its Light UI theme (`UIThemeManager.CurrentTheme == Light`), open the dialog; confirm the
   light palette renders legibly on every control.
6. Repeat step 5 with Revit's Dark UI theme; confirm the dark palette branch, including both `ComboBox` dropdown
   popups and every themed `Button`.
7. Enable Windows High Contrast (for example "High Contrast Black"); reopen Revit/the dialog under that OS
   setting (`SystemParameters.HighContrast` is read once at construction, so a fresh dialog open is required
   after toggling it); confirm every control renders from `SystemColors` brushes, legible against the OS
   palette, and visually distinct from both step 5's and step 6's branches.
8. With Windows Narrator (or another screen reader) running, tab through every control; confirm each announced
   name matches its `AutomationProperties.Name` — spot-check the address `TextBox`, both candidate lists, the
   buffer and point-budget numeric inputs, the unit choice, both dropdowns, the shared-coordinates checkbox,
   and the Cancel/Back/Next/Create buttons.
9. **`IsCancel`/`IsDefault`.** Confirm Esc cancels the dialog from any control (no `TaskDialog`, no document
   change) — the same outcome as clicking Cancel. On the address-entry step, confirm Enter (with focus in the
   address `TextBox`, before any search) triggers Find; after a successful search, confirm Enter now advances
   (Next) instead of re-searching. Repeat the same before/after-confirmation check on the parcel-candidates
   step (Find parcel, then Next). On the final step, confirm Enter triggers Create.
10. **The shared-coordinates checkbox's default-off state, and two runs in one session.** Confirm the checkbox
    is unchecked on first open; complete one run with it checked; run `CreateToposolidCommand` a second time in
    the same Revit session (same process, no restart) immediately afterward; confirm the checkbox is unchecked
    again (no session persistence, per decision 2) and the dialog opens cleanly with no exception, no
    leaked/duplicated UI state, and no repeated/duplicate-registration warning in the trace log (the "MVVM shape (and why
    no messenger)" section's design, confirmed live rather than assumed).
11. **Cancel at several points.** Exercise dialog Cancel mid-address-entry; after selecting a geocode candidate;
    after selecting a parcel candidate; and on the final Preflight-summary page; confirm `CreateToposolidCommand`
    returns with no `TaskDialog` and no document change (`Result.Cancelled`) every time.
12. **An in-dialog lookup failure**, three ways, each shown inline, never as a `TaskDialog`, never via `Execute`'s
    top-level catch, with the operator able to correct and retry without closing the dialog: (a) type an address
    the configured geocoder cannot match; (b) temporarily block network access so a real lookup times out
    (exercises the `OperationCanceledException` catch, the one path every shipped geocoder/parcel source
    rethrows raw rather than wrapped); (c) configure `addressAndParcel.countyRegistryPath` to a missing or
    malformed file, then confirm the dialog still opens normally (including "Use the area in the settings
    file") and the captured load-failure message appears inline only once "Find parcel" is actually attempted
    (`FailedParcelSource`, review finding, major, fixed).
13. **The point-budget warning.** Enter a point budget above the machine's real
    `NativeToposolidMaxPointThreshold`; confirm the dialog's inline warning appears with the exact
    `RevitIniToposolidThresholds.DescribeExceedance` wording, and that proceeding anyway still gets the
    identical wording from Preflight's own rejection afterward (defense-in-depth, unchanged policy). Then enter
    a point budget above `SimplificationSettings.MaxPointBudget` (50,000) — or at/below zero — and confirm the
    second, always-on inline error (`PointBudgetRangeErrorText`) appears and `Next` stays disabled until the
    value is corrected back within range (re-check finding, fixed: this used to be reachable only after
    completing the whole wizard and pressing Create).
14. Against a document that already has shared coordinates set, open the dialog and confirm the checkbox is
    disabled with inline explanatory text rather than only failing at Preflight after the operator finishes the
    whole wizard.
15. **Private real-property run**: perform one complete dialog run — address through
    toposolid/`PropertyLine` creation — against a real, non-synthetic property, confirming the full flow works
    on real-world data end to end. No address, coordinates, parcel id, or other identifying detail from this run
    is ever recorded in any repository-facing document; only the fact that this step was performed is recorded.

## Stage E live-session findings and Stage F fixes

**Stage E** ran the manual evidence plan above against a real Revit 2027 process. Pixel capture of the dialog's
own client area was not available in that session (a WPF-rendering limitation of the capture tooling used, not
of the dialog itself), so evidence there took the form of UI Automation state — control names, `IsEnabled`,
`IsOffscreen`, values, and bounding rectangles — rather than screenshots. That session found four defects, all
now fixed in **Stage F**:

- **Defect A.** Next stayed disabled after a successful Find, for both a street address and a "latitude,
  longitude" pair, even though every real precondition (a confirmed candidate; the address box still matching
  what was confirmed) was satisfied. See "MVVM shape (and why no messenger)" above ("Stale-address invalidation,
  follow-up") for the exact root cause: `_confirmedAddressText` was a bare field, and both of `Geocode`'s success
  branches happened to assign `SelectedGeocodeCandidate` — which re-evaluates `NextCommand` through its own
  `NotifyCanExecuteChangedFor` — *before* assigning `_confirmedAddressText`, so `CanGoNext`'s `AddressEntry` case
  was re-checked one statement too early, against the stale, previously confirmed value, and nothing
  re-evaluated `NextCommand` a second time afterward.

  **Fix (structural, not a mere statement reorder):** `_confirmedAddressText` is now itself an
  `[ObservableProperty]` carrying its own `[NotifyCanExecuteChangedFor(nameof(NextCommand))]`, so whichever
  success branch assigns it — in whatever order relative to `SelectedGeocodeCandidate` — re-evaluates
  `NextCommand` itself, at the point both pieces of state are already final. Both of `Geocode`'s success
  branches, and `CanGoNext`'s own read, now go through the generated `ConfirmedAddressText` property rather than
  the bare field directly (CommunityToolkit.Mvvm's own analyzer, `MVVMTK0034`, disallows referencing an
  `[ObservableProperty]`-annotated field directly once it is one). An audit of every other `CanExecute`-guarded
  command (`Next`, `Back`, `Create`, `Geocode`, `FindParcel`) found no sibling instance of this same
  ordering hazard: every other predicate's dependencies either belong to the identical property whose own
  change triggers the re-evaluation (no cross-property ordering possible), or are independent properties that
  each carry their own correct `NotifyCanExecuteChangedFor`/`NotifyPropertyChangedFor`.

- **Defects B, C, and D (one shared root cause).** Step 9's provenance-preview panel showed every conditional
  sentence at once regardless of which path was actually taken (defect B); step 3's "no parcel boundary was
  found" message showed even when a parcel candidate was listed, alongside an inline registry error (defect C);
  and step 5's point-budget warning and always-on out-of-range error text never appeared at all, even though the
  range rule itself correctly disabled Next (defect D) — as did that same warning's step 10 preflight-summary
  sibling.

  **Root cause.** `SolidGroundDialogViewModel` is an `internal` type, and six of its hand-written computed
  properties — `ShowNoParcelCandidatesMessage`, `ShowPointBudgetWarning`, `PointBudgetWarningText`,
  `PointBudgetRangeErrorText`, `ShowFindParcelGeocodedIntro`, `ShowFindParcelDirectPointIntro` — were themselves
  also declared `internal`, each reached from `SolidGroundDialog.cs` only through a string-`Path`
  `new Binding(nameof(...))`. WPF resolves that kind of binding source against a plain CLR object at runtime by
  reflection, and that reflection only ever discovers **public** members — an internal member is invisible to
  it, regardless of the enclosing type's own accessibility (a `public` member on an `internal` type is still
  found; an `internal` member is not, even on a `public` type). Binding to an inaccessible property fails
  silently: WPF logs a "`BindingExpression path error: '<Name>' property not found`" trace line the operator
  never sees, and leaves the bound dependency property at its own default value — `Visibility.Visible` for a
  `Visibility` binding (defects B and C: the affected text stayed visible unconditionally, since Visible is
  `UIElement.VisibilityProperty`'s own default), or an empty string for a `Text` binding (defect D: the affected
  text never appeared at all, indistinguishable from "no warning applies", since empty is
  `TextBlock.TextProperty`'s own default). This was confirmed with a standalone, non-committed WPF reproduction
  built and run outside this repository (this test project cannot construct a real
  `SolidGroundDialogViewModel`/WPF `Binding` itself — see "Tests" below): an otherwise-identical pair of
  internal/public `bool` properties, and internal/public `string?` properties, each bound the same way, showed
  exactly this split — the public ones tracked their real value on every change; the internal ones stuck at
  Visible/empty regardless of it, with the trace log confirming the exact "property not found" failure. The same
  reproduction also confirmed that a *public* property whose own *type* is an `internal` enum (matching
  `AoiSource`'s `DialogAoiSource`) binds and updates correctly — reflection's accessibility check is on the bound
  *member*, not on its declared type — so the step 9/10 sentences gated on `AoiSource` (`geocodeAttribution`,
  `parcelAccuracy`, `parcelDisclaimer`, `settingsFileNote`, `parcelSummary`, `settingsFileSummary`) were never
  affected by this defect; step 9's own "settings-file sentence" appearing alongside the other two in the
  live-session evidence is accounted for entirely by `ShowFindParcelGeocodedIntro`/`ShowFindParcelDirectPointIntro`
  being stuck permanently visible regardless of `AoiSource`, next to whichever `AoiSource`-gated sentence was
  genuinely, correctly active at that moment.

  **Fix.** All six properties are now declared `public`. `SolidGroundDialogViewModel`'s own remaining accessibility
  is unaffected (still `internal sealed partial class`; a `public` member on an `internal` type is ordinary,
  unremarkable C#, and does not widen this type's reach outside the assembly). The buffer panel's own,
  visually-identical step 4 error text (`BufferErrorText`) was checked and confirmed **not** affected: it is an
  `[ObservableProperty]`-backed, generator-emitted `public` property, never a hand-written `internal` one, so it
  was never exposed to this failure mode.

**Tests (Stage F, additive/corrective; see "Tests" below for the full list):**
`SolidGroundDialogViewModelConfirmedAddressTextIsObservableAndReEvaluatesNextCommandItself` (new, defect A);
`SolidGroundDialogViewModelShowNoParcelCandidatesMessageIsPublicNotInternal` (new, defect C);
`SolidGroundDialogViewModelPointBudgetWarningPropertiesArePublicNotInternal` (new, defect D);
`SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal` (new; a general,
forward-looking check that flags *any* future hand-written computed property bound this way without also being
public, not only the six named above). Three pre-existing tests were corrected, not weakened, because their own
assertions happened to pin the exact defective pattern this session found:
`SolidGroundDialogViewModelRequiresAddressTextToStillMatchTheConfirmedCandidateBeforeAllowingNext` (its
assignment-count check now looks for the `ConfirmedAddressText` property write, and its `CanGoNext` check now
looks for the `ConfirmedAddressText` read, both of which superseded the bare-field forms once defect A's fix
required going through the generated property);
`SolidGroundDialogViewModelPointBudgetRangeErrorTextReusesSimplificationSettingsBoundsNotADuplicatedLiteral` and
`SolidGroundDialogSourceShowsDistinctProvenancePreviewWordingForAGeocodedAddressVersusADirectPointEntry` (both
now look for `public`, not `internal`, on the properties they were already asserting exist). Every one of these
seven tests was confirmed to fail against the pre-Stage-F source and pass after the fix.

## Nearby-parcel fallback tier (follow-up)

A live Revit 2027 session performing "Content model and sections" step 3's own "Manual evidence plan" step 15
(a private, real-property run) found a real defect this note's own "Stage E live-session findings and Stage F
fixes" did not yet cover: the US Census geocoder interpolates along the street centreline, so its own point
commonly lands a few meters into the street frontage or right-of-way, just outside the confirmed address's
true parcel. `FindParcel`'s exact point-in-parcel query then resolved zero candidates even though a parcel was
right there, and the operator had no way to confirm one. `docs/architecture/parcel-boundary-sources.md`'s own
new "Nearby-parcel fallback tier" section records the Revit-free fix in full (`NearbyParcelBoundaryFinder`,
`ParcelNearbyQuery`, `ParcelProximityCandidate`/`ParcelProximityAcquisition`, `ParcelBoundaryProximity`, and
each source's own nearby-tier support); this section records only what changed here, in `SolidGround.Revit`.

**`FindParcel` (see "Threading and the network bridge" above).** Calls
`SolidGround.Core.Sources.NearbyParcelBoundaryFinder.FindAsync(parcelSource, geocodeCandidate.Latitude,
geocodeCandidate.Longitude, _inputs.NearbySearchRadiusMeters, cts.Token)` instead of calling
`parcelSource.FindAsync` directly -- the identical `Task.Run(...).AsTask().GetAwaiter().GetResult()` bridge,
the identical two catch clauses (`ParcelBoundarySourceException`, `OperationCanceledException` on the
caller's own token), never rethrowing, exactly as before this change.

**"Content model and sections" step 3, extended.** `ParcelCandidates`/`SelectedParcelCandidate` are now typed
`ParcelProximityCandidate`, not `ParcelBoundaryCandidate` -- the wrapper record carrying each candidate's own
`DistanceMeters` alongside the resolved `Candidate`. The parcel candidate `DataTemplate`'s five existing
bindings each gained a `Candidate.` path segment (what each one displays is unchanged); a new sixth line binds
`DistanceMeters` directly (`"{0:N1} m from this point"` -- 0 for a parcel that actually contains the point,
the ordinary case). A new banner, visible exactly when a lookup actually fell back to the nearby tier and
found at least one candidate (`NearbyTierNoticeText`, non-null in that case only, following the buffer/
point-budget panels' own `BufferErrorText`/`PointBudgetRangeErrorText`/`TextPresenceToVisibility` idiom rather
than a second, separate bool property), reads "No parcel contains this point. These nearby parcels are within
`{radius}` m, nearest first; confirm the right one." -- naming the actual configured radius, never a
re-hardcoded "30". The operator must still explicitly select a parcel: `FindParcel` never auto-selects
`SelectedParcelCandidate` after a lookup, exactly as before this change, including for a nearby-tier result.
The zero-candidates message (`ShowNoParcelCandidatesMessage`) is unchanged in logic and now correctly reads
"both tiers found nothing" for free, since `ParcelCandidates` is empty in that case by
`NearbyParcelBoundaryFinder`'s own contract. A new `UsedNearbyTier` property (`[ObservableProperty]`-backed,
so automatically public) is reset to `false` alongside `ParcelCandidates`/`SelectedParcelCandidate`/
`_parcelLookupAttempted` whenever `SelectedGeocodeCandidate` changes ("MVVM shape (and why no messenger)"
above, "Stale-parcel invalidation"), so a stale notice from an earlier confirmed point can never linger for a
search that has not yet run against the newly confirmed one. `Create` reads
`SelectedParcelCandidate.Candidate` (the wrapped `ParcelBoundaryCandidate`) when building the AOI and calling
`AddressParcelProvenanceFactory.Create` -- provenance itself is recorded exactly as before this change; neither
`DistanceMeters` nor `UsedNearbyTier` is ever threaded into it.

**Public-binding rule (see "MVVM shape (and why no messenger)" above).** `NearbyTierNoticeText` is a
hand-written computed property, so it is `public`, not `internal`, exactly like `ShowNoParcelCandidatesMessage`/
`PointBudgetWarningText`/etc. above -- `SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal`
(see "Tests" above) already covers this generically, with no change needed to that test itself.

**`ResultSetTruncated` caveat (re-check finding, minor, fixed).** `NearbyTierNoticeText`'s own banner used to
say nothing about the possibility that the shown nearby-parcel list might be incomplete:
`NearbyParcelBoundaryFinder` discarded the nearby tier's own `ResultSetTruncated` signal entirely (Esri's
`exceededTransferLimit`, docs/architecture/parcel-boundary-sources.md's "Request construction and response
handling"), so a county layer configured with an unusually small `maxRecordCount` could truncate the envelope
query with no indication anywhere that the operator's true closest parcel might not even be in the list Create
was about to accept. `ParcelProximityAcquisition` now carries its own `ResultSetTruncated` (see
docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier" -- "Re-check findings, fixed"); a
new `[ObservableProperty]`-backed `ResultSetTruncated` view-model property (reset to `false` alongside
`UsedNearbyTier` on the same `SelectedGeocodeCandidate` change, and set from `acquisition.ResultSetTruncated` in
`FindParcel` before `ParcelCandidates` is assigned, mirroring `UsedNearbyTier`'s own ordering requirement) feeds
one short caveat sentence that `NearbyTierNoticeText` appends to its existing banner text when true -- not a
second, separate banner -- since that existing banner is already the one place this dialog asks the operator to
look closely before confirming a nearby-tier candidate.

**"Settings interaction: prefill, not override", extended.** `RevitAddressAndParcelSettings` gains
`NearbySearchRadiusMeters` (nullable `double`); `RevitSettingsIo.ParseAddressAndParcel` decodes
`addressAndParcel.nearbySearchRadiusMeters`, strictly (must be finite and positive when present, thrown as the
same kind of `AddressAndParcelDecodeException` an unrecognized `geocoderProvider` token already is), defaulting
to `null` -- an existing settings file written before this key existed still loads unchanged. `null` means "use
`NearbyParcelBoundaryFinder.DefaultRadiusMeters` (30 m)"; `SolidGroundDialogHost.ShowModal` resolves
`settings.AddressAndParcel.NearbySearchRadiusMeters ?? NearbyParcelBoundaryFinder.DefaultRadiusMeters` once,
into a new `SolidGroundDialogInputs.NearbySearchRadiusMeters` field the view-model reads at lookup time. The
shipped template documents the new key at its own default (`null`).

**CLI unchanged.** `SolidGround.Cli.Commands.ParcelCommand`'s output and exit codes are untouched by this
follow-up; it does not call `NearbyParcelBoundaryFinder` (a later follow-up may adopt it).

**Tests.** `RevitInteractiveDialogTests.cs` gained source-text checks (this project never references
`SolidGround.Revit`, so every "Revit host" check reads committed source text directly, per "Purpose and
boundary" above): `FindParcel`'s own body calls `NearbyParcelBoundaryFinder.FindAsync`, not
`parcelSource.FindAsync` directly; `NearbyTierNoticeText` is declared `public`; the parcel candidate template
references `DistanceMeters`; `ParcelCandidates`/`SelectedParcelCandidate` are declared with the
`ParcelProximityCandidate` type; `RevitAddressAndParcelSettings.cs`/`RevitSettingsIo.cs` declare and decode
`nearbySearchRadiusMeters` and ship it in the template; `SolidGroundDialogHost.cs` resolves the configured
value or the Core default.

**Tests (re-check finding fix, additive).** `FindParcel` sets the new `ResultSetTruncated` property from
`acquisition.ResultSetTruncated` before `ParcelCandidates` is assigned, exactly mirroring `UsedNearbyTier`'s own
existing ordering check; `OnSelectedGeocodeCandidateChanged`'s existing stale-parcel-state test now also
asserts `ResultSetTruncated = false` alongside its existing `_parcelLookupAttempted = false` assertion;
`NearbyTierNoticeText`'s own expression body (extracted by a new balanced-parenthesis helper,
`ExtractExpressionBodyPropertyText`, since it has no `{ }` block for the existing `ExtractMethodBody` helper to
find) references `ResultSetTruncated`; `NearbyParcelBoundaryFinder.cs` itself is read directly to confirm both
`new ParcelProximityAcquisition(...)` call sites forward `resultSetTruncated:` from the tier that actually
produced its `Candidates`. `NearbyParcelBoundaryFinderTests.cs`'s own runtime coverage is recorded in
docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier" -- "Test evidence (this
follow-up)".

## Manual evidence (Revit 2027, 2026-09-27)

Executed against the installed Revit 2027 application (build `27.0.10.13`), the same reference installation
this repository's other Phase 2/3 manual-evidence sessions cite. Three commits were tested as the session's own
findings were fixed forward: the pre-Stage-F build (commit `5559ec3`, deployed build id
`20260927-095754-42dbc8bf`, `SolidGround.Revit.dll` SHA-256 prefix `42DBC8BF...`, `SolidGround.Core.dll` SHA-256
prefix `B05E3134...`, `CommunityToolkit.Mvvm.dll` SHA-256 prefix `19B2144F...`, signed Valid, `deploy -Verify`
OK on 7 files); the Stage F fix (commit `149163d`, CI run `36339343482` green, 1585 tests (1579 passing, 6
skipped, 0 failed), deployed build id `20260927-130531-d9f00d57`, `SolidGround.Revit.dll` SHA-256 prefix
`D9F00D57...`, `SolidGround.Core.dll` SHA-256 prefix `544B4563...`, signed, `deploy -Verify` OK on 7 files); and
the nearby-parcel-fallback follow-up (commit `ae12f7c`, CI run `36346923151` green, 1645 tests (1639 passing, 6
skipped, 0 failed), deployed build id `20260927-150843-98868f29`, `SolidGround.Revit.dll` SHA-256 prefix
`98868F29...`, `SolidGround.Core.dll` SHA-256 prefix `86D87159...`, signed, `deploy -Verify` OK on 7 files). All
six of this issue's commits (`348fc71`, `19acf6d`, `bb66acf`, `5559ec3`, `149163d`, `ae12f7c`) are CI-green.

**Method.** Automation acted on Revit's own window and control handles directly (a handle-only method, matching
`docs/architecture/revit-property-line-and-shared-coordinates.md`'s own Step 14 method), using purpose-built UI
Automation helpers plus a throwaway, non-repository probe add-in for helper commands (state dumps and a
"Set Coordinated State" helper), in the same spirit as Issue #30's own Step 14 probe. **Capture limitation.**
Unlike a native Revit element, this dialog's own WPF client area could not be captured as an image from the
automation tooling used: `PrintWindow` rendered it either solid black (`PW_RENDERFULLCONTENT`) or solid white
(no flag), and `CopyFromScreen` failed ("The handle is invalid") from the same process. Every claim below is
therefore UI Automation state -- control names, `IsEnabled`, `IsOffscreen`, values, and bounding rectangles --
never a pixel, matching the same limitation "Stage E live-session findings and Stage F fixes" above already
records. Revit's own licensing reminder was dismissed at each launch during the session; it is unrelated to
SolidGround or to signing. One Revit process was closed from outside the session during a pause (Revit's own
journal recorded `ID_APP_EXIT`); no document was modified, and the session continued after relaunching Revit.

**Owner-approved skips.** The owner approved this session on 2026-09-27 ("Go") and accepted three of the manual
evidence plan's system-wide checks as skipped this session, each with a stand-in: step 7's Windows High
Contrast toggle stands on `SolidGroundDialogSourceBranchesOnHighContrastAndOnTheRevitUiTheme` ("Tests" above)
plus the Stage C-era design review, not a fresh live render -- the capture limitation above means this session
could not have visually confirmed High Contrast legibility either way; step 8's audible Narrator pass stands on
the same UI Automation name/state tree a screen reader itself consumes, read directly during this session, plus
`SolidGroundDialogSourceSetsAutomationPropertiesNameOnEveryDeclaredInteractiveControl`'s zero-slack count check;
and step 12(b)'s live network-disable timeout stands on the existing offline test,
`SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies`, which reads
`Geocode()`/`FindParcel()`'s own real method bodies rather than exercising a real timeout live. Manual evidence
plan steps 5 and 6 (the Light/Dark theme palettes) are not among these owner-approved skips: a dedicated
closeout pass exercised both live (see "Theme branches" below); the same capture limitation above means only
that pass's pixel/color legibility remains unconfirmed, not the theme branches themselves.

**Exploratory pass (commit `5559ec3`) -- four defects found, all fixed in Stage F.** This pass confirmed,
alongside the four defects "Stage E live-session findings and Stage F fixes" above already records in full:
modal ownership (Revit's main window `IsWindowEnabled=False` while the dialog is open, `True` again after it
closes); section 0 defaults to "Find a parcel," with "Use the area in the settings file" offered and its own
summary accurate; the `FindParcel` path end to end against the committed synthetic example-site fixtures,
through Create ("SolidGround created the toposolid."), creating `PropertyLine` `317352` (17222.25 sq ft) with
`sharedCoordinatesWrite.attempted=false` and the exported `addressParcel` populated (geocode half null for a
direct coordinate entry; parcel half carrying the synthetic fixture's own stable id, legal description, and
disclaimer); a second run in the same session opening to fresh state (AC5); Cancel closing quietly, with no
`TaskDialog`, from both the address step and the parcel step after a full forward walk; a misconfigured county
registry path surfacing its error inline, with no `TaskDialog` and the dialog staying open; a point budget of
60,000 disabling Next; an already-coordinated document showing the shared-coordinates checkbox disabled, off,
with its explanation visible; and Esc closing the dialog with no `TaskDialog`.

**Formal run (commit `149163d`, Stage F fix) -- every defect confirmed fixed, no workaround needed.** Repeating
the `FindParcel` coordinate-entry path: Next enabled immediately after a successful Find (defect A fixed); the
parcel step showed no zero-candidates message once a candidate was listed (defect C fixed); the provenance
preview showed only the direct-coordinates sentence, not every conditional sentence at once (defect B fixed);
the Preflight summary was correct; Create produced "SolidGround created the toposolid." with the same exported
`addressParcel`/`PropertyLine 317352` shape as the exploratory pass. A second run in the same session (AC5)
again opened to fresh state; posting Enter to the dialog triggered each step's own primary action (Next on
step 0, Find on step 1, then Next), confirming `IsDefault` tracks the current step. The point-budget step
(defect D): 25,000 showed the `Revit.ini` warning text with Next still enabled; 60,000 showed "pointBudget must
be between 1 and 50000 inclusive." alongside the warning, with Next disabled; 15,000 showed neither (defect D
fixed); Cancel from this step closed quietly. The settings-file AOI path showed only its own settings-file
sentence in the provenance preview, and Create produced a toposolid with `propertyLine.created=false` and
`addressParcel` null. The misconfigured registry again surfaced only its inline error, with no zero-candidates
message and no `TaskDialog`. The already-coordinated-document checkbox again showed disabled, off, with its
explanation shown.

**Nearby-parcel-fallback verification (commit `ae12f7c`).** A synthetic point roughly 2.5 m east of the
synthetic parcel fixture (N1) -- the offline reproduction of the private finding "Nearby-parcel fallback tier
(follow-up)" above records -- showed the parcel step's new notice, "No parcel contains this point. These
nearby parcels are within 30 m, nearest first; confirm the right one." -- with the listed candidate reading
"2.6 m from this point"; the operator still had to select a candidate explicitly, and Create produced
"SolidGround created the toposolid." A second synthetic point placed exactly inside the parcel fixture showed
no nearby-tier notice, confirming the fallback engages only when the exact point-in-parcel query finds
nothing; its own listed candidate read "0.0 m from this point" rather than a phrase that says outright that it
contains the point -- see "Known limitations" below.

**Private real-property confirmation (manual evidence plan step 15).** Private end-to-end runs against a real
property (coordinates, and a county-recorded address through the Census geocoder; live terrain fetch; a
machine-local county registry) passed on `ae12f7c`; no identifying detail is recorded.

**Theme branches (commit `ae12f7c`, same deployed build as above), run last as a closeout gap-check.** A review
at closeout found that manual evidence plan steps 5 and 6 (Revit's own Light/Dark UI theme) had not yet been
exercised live, so they were run under the owner's same "Go" for this session, using the synthetic
example-site scenario and no Create. Toggling Revit's theme needed the Issue #19 theme probe, which is not
signed, so it was installed for this pass only (its load prompt answered "Load Once," leaving no lasting
trust), and the machine's `Revit.ini` and `UIState.dat` were backed up first. The pass's first launch crashed
during Revit's own startup, before any document opened or any SolidGround command ran, with the same
`ntdll.dll` `0xc0000374` signature as the intermittent Revit 2027 startup crash already documented in
`docs/architecture/revit-extensible-storage-provenance.md` and
`docs/architecture/revit-release-packaging-and-signing.md`; a relaunch continued the pass. A theme report taken
before any change read Dark, following the Windows apps theme, which indicates that the earlier runs in this
session, made under the same Windows setting, also used the Dark branch. Under Dark, a fresh dialog walked all
eleven sections forward with no exception: step 3's, step 9's, and step 10's own texts read correctly, and the
shared-coordinates checkbox showed enabled and off. After stepping back to the level and toposolid-type step,
both `ComboBox` dropdowns (Level and Toposolid type) opened, listed their item, and collapsed. Cancel from that
step then closed the dialog quietly, with no `TaskDialog` and Revit's main window re-enabled. Revit's theme was
then switched to Light, and an identical walk in a fresh dialog produced identical results, again ending in a
quiet Cancel. The theme was restored afterward and read back as the pre-pass Dark state. Revit had added an
explicit theme section to `Revit.ini` and had changed `UIState.dat`; both were restored from the pre-pass
backup and re-verified equal to it. The pass's closing checks matched the rest of the session: Revit closed
with no save prompt, the probe was uninstalled, the settings file was restored to its original content, the
default template's hash was unchanged, and `deploy -Verify` passed on all 7 files again. As elsewhere in this
session, pixel/color legibility was not captured (the capture limitation described above), and Windows High
Contrast remained an owner-approved skip.

**Session hygiene.** Every dialog/document close answered "Save changes to `Default_I_ENU.rte`?" with No; the
default template's own hash was unchanged throughout. The probe add-in was uninstalled afterward. The
machine's settings file, temporarily altered for the session, was restored to its original content, confirmed
by `deploy -Verify` passing on 7 files at the session's close. The session's own log was checked for secret
leakage afterward: the API key value itself appeared zero times, and every logged query-string parameter that
should be redacted was confirmed redacted.

### Acceptance criteria (PH3-4 / Issue #31)

All five of `docs/planning/phase-3-draft-issues.md`'s own PH3-4 acceptance criteria are met, with evidence from
this note's own sections plus this session:

| # | Criterion | Evidence |
| --- | --- | --- |
| AC1 | `UseWPF` is true, the project still compiles under the CI Nice3point reference-assembly gate unchanged, and the dialog contains zero `.xaml` files, verified by a repository check. | Landed Stage B (`UseWPF` true; `RevitProjectContainsNoXamlFiles`, "Tests" above); every one of this issue's six commits, through `ae12f7c`, is CI-green (CI run `36339343482` for `149163d`, `36346923151` for `ae12f7c`, both above). |
| AC2 | `CommunityToolkit.Mvvm` 8.4.2 is referenced only from `SolidGround.Revit.csproj`; both lock files are updated; the Core-never-references-Revit test passes. | Landed Stage B; `CommunityToolkitMvvmPackageReferenceAppearsInExactlyOneCsprojInTheRepository`, `BothRevitPackageLockFilesContainCommunityToolkitMvvmAtTheApprovedVersion`, `CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackagesAndOneUnconditionalCommunityToolkitMvvm` ("Tests" above). |
| AC3 | Only an uncaught failure reaches `Result.Failed`; an in-dialog lookup failure never reaches `Execute`'s top-level catch. | "Result-code mapping" above (structural guarantee plus tests); live-confirmed this session: the misconfigured registry surfaced inline with no `TaskDialog` on both `5559ec3` and `149163d`, and Cancel at every exercised point (the address step, the parcel step, the point-budget step, and the level and toposolid-type step) closed the dialog quietly with no `TaskDialog`. |
| AC4 | `AutomationProperties` and a HighContrast/SystemColors branch are present on every control. | `SolidGroundDialogSourceSetsAutomationPropertiesNameOnEveryDeclaredInteractiveControl`, `SolidGroundDialogSourceBranchesOnHighContrastAndOnTheRevitUiTheme` ("Tests" above). The High Contrast toggle itself was an owner-approved skip this session ("Manual evidence (Revit 2027, 2026-09-27)" above); it rests on this source check plus the Stage C-era design review, not a fresh live render. |
| AC5 | A manual-evidence step confirms modal ownership, both theme branches, and the checkbox's default-off state render without exception, and running `CreateToposolidCommand` twice in one session shows clean teardown on repeat invocation. | Met under the owner-accepted reading recorded in "MVVM shape (and why no messenger)" above: this design introduces no messenger at all, so there is no messenger state for a second invocation to leak. Modal ownership, the checkbox's default-off state, and its disabled/explained state on an already-coordinated document are all confirmed above; a second run in the same session opened to fresh state (Find a parcel selected, address empty, Next disabled) on both `5559ec3` and `149163d`. Both theme branches were exercised live in a dedicated closeout pass (see "Theme branches" above): a fresh dialog walked all eleven sections with no exception under both Dark and Light, and the theme was restored and verified afterward; only pixel/color legibility is unconfirmed, because of the capture limitation above. |

## Known limitations

- **Cosmetic (2026-09-27, manual evidence): a parcel that actually contains the queried point reads "0.0 m from
  this point."** The nearby-parcel-fallback verification's exact-hit regression (see "Manual evidence (Revit
  2027, 2026-09-27)" above) confirmed the containing case shows no nearby-tier notice, but its own listed
  candidate still carries the same `DistanceMeters` template text used for a genuine nearby-tier result,
  reading "0.0 m from this point" rather than a phrase that says outright that it contains the point (for
  example "contains this point"). Not fixed in this closeout; a low-priority wording follow-up.

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
- No defensive re-verification that the dialog's chosen Level/ToposolidType still belong to `document` before
  Preflight uses them (owner decision 4: same open `Document`, same synchronous call, no transaction opened on
  any path that could invalidate an element reference).
