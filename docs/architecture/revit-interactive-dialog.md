# Revit interactive dialog

## Purpose and boundary

Issue #31 (PH3-4) replaces today's file/settings-only AOI input for `SolidGround.Revit` with one modal
`SolidGroundDialog` (code-behind only, zero `.xaml`/BAML), shown from inside `CreateToposolidCommand.Execute`
before today's Preflight/transaction. No new command, ribbon button, or panel (PH3-7 unchanged).
`SolidGround.Core` and `SolidGround.Cli` are otherwise untouched.

**Status: Stages A and B only, landed; the dialog is not yet wired.** This note records the whole accepted
design so later stages' code comments can cite its section titles verbatim, but only two of its five stages
have been implemented so far:

- **Stage A (Core only):** `RevitIniToposolidThresholds.ExceedsNativeThreshold`/`.DescribeExceedance`
  (`src/SolidGround.Core/Hosting/RevitIniToposolidThresholds.cs`) and `TerrainProcessingPipeline.RunAsync`'s
  new optional trailing `AddressParcelProvenance? addressParcel` parameter
  (`src/SolidGround.Core/Processing/TerrainProcessingPipeline.cs`), both already used by `SolidGround.Cli`'s
  and `SolidGround.Revit`'s existing callers unchanged (the new parameter defaults to `null`). See "AOI and
  provenance" and "Result-code mapping" below for what these two additions are for.
- **Stage B (package + `UseWPF` + empty dialog shell):** this stage. `CommunityToolkit.Mvvm` 8.4.2 is now an
  unconditional `PackageReference` of `SolidGround.Revit.csproj`; `UseWPF=true` replaces the project's old
  explicit `Microsoft.WindowsDesktop.App.WPF` `FrameworkReference`; `src/SolidGround.Revit/Dialog/` contains a
  minimal `SolidGroundDialog : Window` (title bar and a Cancel button only) and an empty
  `SolidGroundDialogViewModel : ObservableObject` shell carrying one placeholder generator-backed property.
  **Neither type is constructed anywhere.** `SolidGroundDialog` is not shown from `CreateToposolidCommand`,
  the ribbon, or any other command; the shell is unreachable from a running add-in.
- **Stage C (full dialog UI + view-model, still unwired)**, **Stage D (wiring into
  `CreateToposolidCommand.Execute`)**, and **Stage E (manual evidence + docs update)** have **not** been
  implemented. Every section below describes the accepted design for that future work, not code that exists
  today.

## Content model and sections

Ten sections, fixed order (owner decision 9: ship the full content model in the first milestone, not a
reduced slice):

1. **Address entry** — one `TextBox` (`AddressText`) plus one "Find" button (`GeocodeCommand`, a
   `[RelayCommand]`). The address field also accepts a `"latitude, longitude"` pair (like the CLI's
   `--point`), so a site with no street address still works and the dialog can be exercised offline against
   the local parcel file source.
2. **Geocode candidates** — a list bound to `GeocodeCandidates` (`ObservableCollection<AddressGeocodeCandidate>`),
   pre-selecting index 0 (`AddressGeocodeAcquisition`'s own constructor already guarantees "non-empty,
   best-match-first").
3. **Parcel candidates with legal-description preview** — a list bound to `ParcelCandidates`
   (`ObservableCollection<ParcelBoundaryCandidate>`), no default selection (`ParcelBoundaryAcquisition.Candidates`
   carries no rank). Zero candidates is a normal, non-exceptional outcome, shown inline with Back enabled and
   Next disabled.
4. **AOI buffer** — one numeric input (`BufferMeters`, default `0`), validated inline by attempting
   `LinearDistance.Meters(BufferMeters)`.
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
   `.ToposolidTypeName`.
8. **Shared-coordinates opt-in checkbox** (PH3-3), default off — one `CheckBox`
   (`WriteSharedCoordinatesIfAbsent`). Disabled, with inline explanatory text, when
   `SharedCoordinatesDetector.LooksAlreadyCoordinated(document, vertexToleranceInternal)` is already true at
   dialog-construction time.
9. **Provenance/accuracy preview** — read-only text only: the confirmed candidates' `Attribution` (geocode)
   and `LicenseDisclaimerText`/`AccuracyLabel` (parcel), plus AGENTS.md's fixed "site-form tool, not a survey
   instrument" sentence.
10. **Preflight summary with Create/Cancel** — a read-only recap of every prior choice plus the two buttons.
    This page previews the operator's own choices; it does not replace Stage 1 Preflight (see "Result-code
    mapping" below), which still runs afterward and can still reject.

Every control in every section will get `AutomationProperties.SetName(control, "...")` (and `SetHelpText`
where a control's purpose is not obvious from its adjacent label alone) — see "Theming and accessibility"
below. **None of this section model exists in code yet** (Stage C).

## Flow/state model

A `SolidGroundDialogStep` enum (`SolidGround.Revit.Dialog`, Revit-side) with ten values in the order above.
`SolidGroundDialogViewModel.CurrentStep` will drive which panel `SolidGroundDialog`'s content area shows (a
`switch` in code, no `DataTemplateSelector`, since there is no XAML). "Next" is enabled only when the current
step's own completion rule holds; "Back" is always enabled except on step 1; "Create" replaces "Next" only on
step 10 and is where `SolidGroundDialogResult` is finally constructed and `DialogResult = true` is set before
`Close()`. Any other close path (window X button, Cancel button, Esc) leaves `DialogResult` `false`/`null` and
constructs no result.

No Core-level "flow state" type is introduced: every piece of non-trivial decision logic this flow needs
(best-geocode-candidate defaulting, AOI construction, provenance assembly, buffer validation, point-budget-vs-threshold
comparison, level/type defaulting) already exists in Core, built by Issues #28/#29/#30/#33. **Not implemented
yet** (Stage C).

## AOI and provenance: the dialog becomes the sole Revit-host AOI source

**Decision, flagged for the owner and accepted:** once this dialog ships (Stage D), it becomes the only way
`CreateToposolidCommand` builds an `AreaOfInterest`; `settings.json`'s `areaOfInterest.boundingBox`/`.radius`/
`.parcel` sub-objects stop being read by the Revit host (they remain fully valid, unchanged, for
`SolidGround.Cli`).

**Construction, entirely in memory, no new file (Stage D):** on parcel confirmation, the dialog will call the
existing, unchanged `SolidGround.Core.Processing.ParcelBoundaryAoiFactory.FromCandidate(candidate, buffer)`.
The resulting `ParcelGeometryAoi` is threaded straight into `TerrainProcessingPipeline.RunAsync`'s existing
`AreaOfInterest? aoi` parameter, never written to a `.wkt`/`.aoi.json` file.

**Provenance, also entirely in memory (Stage D):** on Create, the view-model will build a `GeocodeProvenance`/
`ParcelProvenance`/`AddressParcelProvenance` triple (all three types already exist, unchanged, in
`SolidGround.Core.Provenance`) from the confirmed candidates.

**The one Core signature change this issue needs, already landed (Stage A):**
`TerrainProcessingPipeline.RunAsync` (`src/SolidGround.Core/Processing/TerrainProcessingPipeline.cs`) gained
one new, optional, trailing parameter, `AddressParcelProvenance? addressParcel = null`, threaded into its
existing `TerrainExportPayloadAssembler.Assemble(...)` call's own already-existing optional 8th (and last) parameter.
Every existing caller (`process`, `run`, and both of `CreateToposolidCommand`'s own
`RunFetchPipelineAsync`/`RunProcessPipelineAsync`) keeps compiling unchanged, always passing `null`, until
Stage D edits those two Revit-side call sites to pass `dialogResult.AddressParcel` through instead.

**The other new Core surface, already landed (Stage A):**
`src/SolidGround.Core/Hosting/RevitIniToposolidThresholds.cs` gained two `public static` methods beside the
existing `Parse`: `ExceedsNativeThreshold(int pointBudget, Thresholds thresholds)` and
`DescribeExceedance(int pointBudget, int nativeThreshold, string revitIniPath)`. `DescribeExceedance`'s text is
byte-identical to `CreateToposolidCommand`'s pre-existing inline `CheckRevitIniPointThreshold` message — a pure
extract, not a reword — so existing behavior is unchanged; only its location (now Core, shared) changes.
`CheckRevitIniPointThreshold` becomes a two-line call into these once Stage D lands; the dialog's own
`Revit.ini` read will call the identical pair for its inline warning (see "Content model and sections" step 5
above).

## Settings interaction: prefill, not override

**Reads only; no write-back in this milestone.** The dialog will read the already-loaded `RevitSettings`
purely to prefill controls: `Request.OutputUnit`, `Request.Simplification.PointBudget`,
`Target.LevelName`/`.ToposolidTypeName`, `SharedCoordinates.WriteIfAbsent`, plus a new
`RevitAddressAndParcelSettings` section (`GeocoderProvider`, `CountyRegistryPath`, `CountyGeoidOverride`,
`LocalParcelFilePath`, `LocalParcelFileSourceLabel`, `LocalParcelFileLicenseDisclaimerText`) configuring which
`IAddressGeocoder`/`IParcelBoundarySource` the dialog queries. The operator's in-dialog choice always wins for
the run about to happen; nothing is written back to `%ProgramData%\SolidGround\Revit\settings.json`.
"Last-used value" persistence across sessions is a named non-goal (see "Non-goals" below), not silently
dropped. **Not implemented yet** (Stage D).

## Threading and the network bridge

`SolidGroundDialogViewModel`'s `[RelayCommand]` methods for Find-address/Find-parcel will be **synchronous
`void` methods** (plain `RelayCommand`, never `AsyncRelayCommand`), using the same blocking-bridge pattern
Issue #15 already locked in for Stage 2 acquisition (`Task.Run(...).GetAwaiter().GetResult()`), with one
difference: `IAddressGeocoder.GeocodeAsync`/`IParcelBoundarySource.FindAsync` return `ValueTask<T>`, so the
bridge needs an extra `.AsTask()` call. Both methods will catch their own `AddressGeocoderException`/
`ParcelBoundarySourceException` **and** the raw, unwrapped `OperationCanceledException` every shipped
geocoder/parcel source rethrows on the caller's own timeout, so a network timeout is shown inline, never
reaching `Execute`'s top-level catch. This deliberately never `await`s across the Revit UI thread mid-call,
matching `docs/architecture/phase-3-interactive-add-in-research.md`'s own reasoning: the dialog blocking/freezing
during a lookup is accepted, existing UX precedent (`CreateToposolidCommand` already logs "Revit will be
unresponsive for up to N second(s)" for the identical reason during Stage 2 fetch-mode acquisition). **Not
implemented yet** (Stage C).

## Result-code mapping

`CreateToposolidCommand.ExecuteCore` will gain a new stage sequence: Stage 0 (load settings, open-document
check, and the geometry-tolerance read, hoisted out of `RunDocumentPreflight` so the dialog can use them
before Preflight formally runs), Stage 0.5 (`SolidGroundDialogHost.ShowModal`, returning `null` on Cancel), a
slimmed Stage 1 Preflight (AOI/level/type now dialog-supplied, not derived), then Stages 2-6 unchanged in
shape. Mapping (extends, does not replace, today's existing policy):

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

An in-dialog lookup failure never reaching `Execute`'s top-level catch is satisfied by construction: the
`[RelayCommand]` methods catch `AddressGeocoderException`/`ParcelBoundarySourceException` **and**
`OperationCanceledException` internally and never rethrow. **Not implemented yet** (Stage D); see "Tests"
below for the offline structural tests planned to guard this mapping once it lands.

## MVVM shape (and why no messenger)

`SolidGroundDialogViewModel : ObservableObject` (plain, **not** `ObservableRecipient`) — deliberately no
`IMessenger`/`WeakReferenceMessenger.Default` usage anywhere in this feature. `WeakReferenceMessenger.Default`
is a process-wide static singleton; a view-model that registered with it would need to reliably unregister on
close or a second `CreateToposolidCommand.Execute` in the same session would accumulate a second live
registration against the first, disposed window. This design avoids the risk by never introducing the
messenger at all: nothing in the content model needs cross-component publish/subscribe (one modal window, one
view-model, plain property binding and direct method calls suffice throughout).

Full content model will add `[ObservableProperty]`-backed properties for every bound value (`AddressText`,
`IsBusy`, `ErrorText`, `GeocodeCandidates`, `SelectedGeocodeCandidate`, `ParcelCandidates`,
`SelectedParcelCandidate`, `BufferMeters`, `PointBudget`, `SelectedOutputUnit`, `Levels`, `SelectedLevel`,
`ToposolidTypes`, `SelectedToposolidType`, `WriteSharedCoordinatesIfAbsent`, `CurrentStep`) and
`[RelayCommand]` methods (`Geocode`, `FindParcel`, `Next`, `Back`, `Create`, `Cancel`).

**Landed today (Stage B):** `SolidGroundDialogViewModel` is a plain `ObservableObject` subclass carrying one
placeholder `[ObservableProperty]`-backed field, `_placeholderStatusText`, whose only purpose is proving the
source generator actually runs against this project's real `net10.0-windows7.0`/`UseWPF=true` build. A later
stage replaces this placeholder with the full property/command set above.

## Theming and accessibility

Read once, at `SolidGroundDialog`'s constructor, never subscribed (`UIApplication.ThemeChanged`/
`UIControlledApplication.ThemeChanged`/`SystemParameters.StaticPropertyChanged` are all confirmed-present but
deliberately unused — "modal only"):
`UIThemeManager.CurrentTheme` (`Autodesk.Revit.UI`, static property since 2014) and
`SystemParameters.HighContrast` (`System.Windows`, `PresentationFramework`). A `DialogTheme.Resolve(revitTheme,
highContrast)` helper (new file, `src/SolidGround.Revit/Dialog/DialogTheme.cs`, planned) will return a small
`DialogPalette` record of `Brush` properties; `highContrast` wins over the `Light`/`Dark` choice — when true,
every brush comes from `SystemColors.*Brush` members (`WindowBrush`, `WindowTextBrush`, `ControlTextBrush`,
`HighlightBrush`, `GrayTextBrush`, `ActiveBorderBrush`); otherwise a small fixed Dark/Light palette. Revit's own
API carries no HighContrast concept at all; this branch is necessarily WPF/OS-level only. Every control-building
method will call `AutomationProperties.SetName(control, "...")` immediately after constructing each interactive
control, plus `SetHelpText` for the point-budget warning, the shared-coordinates checkbox, and the buffer
input. **Not implemented yet** (Stage C).

## Package: CommunityToolkit.Mvvm 8.4.2

**Landed today (Stage B).** `src/SolidGround.Revit/SolidGround.Revit.csproj` gained an unconditional
`PackageReference Include="CommunityToolkit.Mvvm" Version="8.4.2"` (both local and CI/-p:UseRevitReferenceAssemblies=true
builds; unlike the two `Nice3point.Revit.Api.*` CI-only compile stand-ins, this is a real, shipped runtime
dependency, never scoped to that condition), and its existing `<ItemGroup><FrameworkReference
Include="Microsoft.WindowsDesktop.App.WPF" /></ItemGroup>` was deleted in the same change in favor of
`<UseWPF>true</UseWPF>`: leaving both in place emits `NETSDK1086`, a hard build failure under this project's
inherited `TreatWarningsAsErrors=true`.

**Why this package is needed** (AGENTS.md dependency-policy documentation requirement): the dialog's ten
content-model sections need `INotifyPropertyChanged` boilerplate, command wiring, and generator-backed
observable properties across roughly fifteen bound values; hand-writing that boilerplate is exactly the
"small, well-bounded" case the standard library alone does not cover as concisely, and `CommunityToolkit.Mvvm`
is Microsoft/.NET Foundation's own reference MVVM toolkit (MIT licensed).

**Package facts, verified before implementation:** zero transitive package dependencies and no native code
under this project's real `net10.0-windows7.0` target moniker — the resolved compile/runtime asset is the
plain `lib/net8.0/CommunityToolkit.Mvvm.dll`, not the Windows-flavored `lib/net8.0-windows10.0.17763` asset
(the project's platform version, `7.0`, is below that asset's `10.0.17763` floor). Manifest resources on both
assets are exactly `{ "ILLink.Substitutions.xml" }` — no `.baml`, no `.g.resources`, no pack-URI-addressable
resource of any kind, so the package itself cannot trigger the isolated-add-in-context XAML/BAML double-load
bug (`Nice3point/RevitToolkit#7`, the still-open `dotnet/wpf#1700`) independent of this issue's own
zero-`.xaml` mitigation. The package's source generators/analyzers (`CommunityToolkit.Mvvm.CodeFixers.dll`,
`CommunityToolkit.Mvvm.SourceGenerators.dll`) are wired in only as build-time Roslyn analyzers; the built
output contains only `CommunityToolkit.Mvvm.dll`. Confirmed empirically during this stage's own restore and
build (`src/SolidGround.Revit/packages.lock.json`/`packages.ci.lock.json` both gained exactly one new entry
each, content hash `WadCzGEc2U+3e20avRLng4qNtt4zoOGWrdUISqJWrHe3/FSnrYjuM5Sb4yQb09LhkBXrrI4Zt3dLKgRMbItsrg==`,
under the existing `net10.0-windows7.0` target key, with zero other entries changed).

**A previously undiscovered build-time regression, found and fixed during this stage:** the installed .NET 10
SDK's own `Sdks\Microsoft.NET.Sdk.WindowsDesktop\targets\Microsoft.NET.Sdk.WindowsDesktop.WPF.props` removes
the `System.IO`/`System.Net.Http` implicit global usings the moment `UseWPF=true` (its own comment: "Generates
implicit global namespace imports file `<projectname>.ImplicitGlobalNamespaceImports.cs`"). Every pre-existing
file in this project that relies on `File`/`Directory`/`Path`/`Stream`/`HttpClient`/`IOException` resolving as
plain, unqualified `Microsoft.NET.Sdk` implicit usings stopped compiling the moment `UseWPF=true` was added,
unrelated to anything else this issue changes. `SolidGround.Revit.csproj` now restores both namespaces via two
explicit `<Using Include="..." />` items, confirmed to compile every existing file unchanged (no source-line
edits) and to introduce no live ambiguity (no file in this project uses `System.Windows.Shapes`, whose own
`Path` type is the usual reason WPF projects omit this implicit using).

**THIRD-PARTY-NOTICES** gained a `CommunityToolkit.Mvvm 8.4.2` entry reproducing both the top-level MIT license
text and the package's own bundled `ThirdPartyNotices.txt` in full (conservative choice: most of that bundled
file's entries are attributed to sibling CommunityToolkit packages this project does not reference, and one
entry to the build-time-only source-generator assembly this project never ships, but nothing is lost by
reproducing it anyway). Both lock files' single new entry is self-checked by
`scripts/New-ReleasePackage.ps1`'s `Assert-ThirdPartyNoticesCoversLockedPackages` and
`tests/SolidGround.Tests/ReleasePackagingTests.ThirdPartyNoticesFileNamesEveryPackageInTheLockFileWithItsExactVersion`
— both are self-updating drift guards that already pass with no test-file edit required.

## Tests

**Landed today (Stage B):** `tests/SolidGround.Tests/RevitInteractiveDialogTests.cs` (new file) covers the
zero-`.xaml` repository check and every package-placement/version/lock-file check this stage's csproj change
needs (`RevitProjectContainsNoXamlFiles`, `CsprojPinsCommunityToolkitMvvmToTheApprovedVersionUnconditionally`,
`CsprojEnablesUseWpf`, `CsprojNoLongerDeclaresAnExplicitWpfFrameworkReference`,
`CommunityToolkitMvvmPackageReferenceAppearsInExactlyOneCsprojInTheRepository`,
`BothRevitPackageLockFilesContainCommunityToolkitMvvmAtTheApprovedVersion`).
`tests/SolidGround.Tests/RevitHostFilesTests.cs`'s pre-existing
`CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackages` was renamed to
`CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackagesAndOneUnconditionalCommunityToolkitMvvm`
and its count assertion moved from two to three, since it would otherwise fail the moment this stage's
unconditional `PackageReference` was added.

**Landed in Stage A (already present before this stage):**
`RevitIniToposolidThresholdsTests.ExceedsNativeThresholdMatchesPointBudgetAgainstTheParsedNativeThreshold`/
`.DescribeExceedanceNamesTheBudgetTheThresholdAndTheRevitIniPath` and
`TerrainProcessingPipelineTests.RunAsyncThreadsAddressParcelProvenanceIntoTheAssembledPayloadWhenSupplied`.

**Not implemented yet** (Stage C/D), planned:
`TerrainRequestSettingsTests.ShippedTemplateText`'s update for the new `"addressAndParcel"` settings section;
`SolidGroundDialogSourceBranchesOnHighContrastAndOnTheRevitUiTheme`;
`SolidGroundDialogSourceSetsAutomationPropertiesNameOnEveryDeclaredInteractiveControl` (a zero-slack,
hand-maintained expected-count match, not a minimum-tolerance check);
`SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies`; and
`CreateToposolidCommandReturnsCancelledWithNoTaskDialogWhenTheDialogIsCancelled`.

## Manual evidence plan

Not started (Stage E). The accepted plan runs against a real Revit 2027 process, following this repository's
existing numbered-step convention: an end-to-end run through all ten sections confirming the busy indicator's
visual timing; modal ownership; the shared-coordinates checkbox's no-session-persistence default; the Light,
Dark, and Windows High Contrast palettes; screen-reader `AutomationProperties.Name` announcements for every
control; running the command twice in one session with no leaked state; dialog Cancel at several points; an
in-dialog geocoder failure both by an unmatchable address and by a real network timeout; a point budget above
the machine's real `NativeToposolidMaxPointThreshold`; the shared-coordinates checkbox's already-coordinated
disabled state; and one private end-to-end run against a real property, recorded here only as having been done
(no location, parcel, or other identifying detail is ever recorded in this repository).

## Non-goals

- No bounding-box/radius AOI path through the Revit dialog (superseded for the Revit host; unaffected for
  `SolidGround.Cli`).
- No settings write-back / "remembers last address, candidate, or point budget across sessions".
- No `UIControlledApplication.ThemeChanged`/`UIApplication.ThemeChanged` subscription, and no
  `SystemParameters.StaticPropertyChanged` subscription — both read once at construction ("modal only").
- No CLI change of any kind (a separate issue's own scope).
- No Extensible Storage schema version bump for the address/parcel provenance fields — that is a future
  schema version, deferred explicitly (AGENTS.md "Provenance decision";
  `docs/architecture/address-parcel-provenance.md`).
- No ribbon/command/manifest change (one tab/panel/button, unchanged, PH3-7).
- No `async`/`await`-yielding WPF commands for any network call (deliberately synchronous).
- No `WeakReferenceMessenger`/`ObservableRecipient` usage anywhere in this feature.
- No pre-dialog `OPENTOPOGRAPHY_API_KEY` check reordering (stays a Preflight-only concern, unchanged position).
- **This stage specifically:** no wiring into `CreateToposolidCommand.Execute`; the dialog shell is not shown
  from anywhere and has no effect on any command's runtime behavior.
