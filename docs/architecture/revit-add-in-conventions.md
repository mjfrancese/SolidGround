# Revit add-in conventions

Issue #12 records the owner-approved conventions for `SolidGround.Revit`, the Phase 2 Revit host adapter. Phase 1 was signed off on 2026-09-20 (Issue #10). This note is the decision record Phase 2 must follow; it is not implementation work, and no `SolidGround.Revit` code exists yet. Issue #13 verifies the Revit 2027 API details this note leaves open. Issue #14 scaffolds `SolidGround.Revit` once that verification lands.

## Basis

(detail about the owner's other add-in withheld)

the owner answered seven open decisions from the proposal on 2026-09-20; those answers are recorded below and are normative. Every other item the proposal marked Unchanged or Adapted is accepted as proposed; every item it marked Not applicable is not adopted.

## Owner decisions, 2026-09-20

1. **Reference assemblies.** Option A plus option B. Local and deploy builds reference `RevitAPI.dll`/`RevitAPIUI.dll` from the installed Revit 2027 via HintPath, never committed. A CI-only compile gate, activated once `SolidGround.Revit` exists, uses the pinned `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` 2027.x packages. See "10. Revit reference assemblies for local builds and CI" below for the full mechanism and the licensing position.
2. **ContextName.** `"SolidGround"`. See "3. Manifest and isolated add-in context" below for the full manifest shape.
3. **Code signing.** Accept Revit's unsigned-add-in prompt for now; no signing in the initial milestone. Revisit with Issue #17 after verification item 14 (Revit 2027 unsigned-add-in prompt behavior) is checked; verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 14.
4. **Settings and log location.** Machine-wide, following the owner's other add-in, under `%ProgramData%\SolidGround\Revit\` (a settings file and a `Logs\` folder), not per-user. This is a deliberate, scoped exception: the add-in itself still installs per-user (see decision 6 and "7. Deployment and per-user install" below); only settings and logs are machine-wide. See "5. Settings" and "6. Logging and diagnostics" below.
5. **Ribbon icon.** Ship an icon for `CreateToposolidCommand` in the initial milestone; Issue #19 designs it. See "4. Ribbon and command structure" below.
6. **Deployment.** Write a deploy script in the initial milestone, not a by-hand copy loop. See "7. Deployment and per-user install" below.
7. **Provenance.** Add build identity, informational version, MVID, and SHA-256, to the Extensible Storage entity, beyond the field list `AGENTS.md`'s Provenance decision section already requires. See "11. Provenance and Extensible Storage" below.

## 1. Project layout and naming

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- `src/SolidGround.Revit/SolidGround.Revit.csproj`, one project, inherits the repository's existing root `Directory.Build.props` rather than repeating the owner's other add-in's per-project property blocks.
- `src/SolidGround.Revit/SolidGroundApplication.cs`, implements `IExternalApplication` directly, no static `Instance` for the initial milestone.
- `src/SolidGround.Revit/Commands/CreateToposolidCommand.cs`, `sealed class CreateToposolidCommand : IExternalCommand`, file name matches class name.
- `ProjectReference` to `src/SolidGround.Core/SolidGround.Core.csproj` only. No second Revit-free module: `AGENTS.md`'s architecture table names exactly four projects.
- No Revit abstraction-interface or Fakes layer for the initial milestone. `CreateToposolidCommand` is the only Revit call site, so there is no seam to protect yet; the owner's other add-in's [withheld] precedent is revisited once a second call site appears.
- (detail about the owner's other add-in withheld)

## 2. Target framework and reference assemblies (build-time mechanism)

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- `TargetFramework` = `net10.0-windows`, matching `AGENTS.md`'s architecture table.
- `RevitInstallDir` MSBuild property, default `C:\Program Files\Autodesk\Revit 2027` (confirmed present on the local reference machine, API version `27.0.10.13`), overridable the same way the owner's other add-in's is.
- `RevitAPI.dll`/`RevitAPIUI.dll` via `HintPath` against `$(RevitInstallDir)`, `Private=false`, never committed.
- No `UseWPF` until a WPF surface is designed; the initial milestone uses a plain `TaskDialog` (see section 6).
- `CopyLocalLockFileAssemblies=true` from the project's creation, since `SolidGround.Revit` takes a `ProjectReference` to `SolidGround.Core`, which already carries `PackageReference` entries (NetTopologySuite, ProjNET) that must be copied into `SolidGround.Revit`'s own output for the isolated add-in context to load them at runtime.
- Inherit `LangVersion`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, and `RestorePackagesWithLockFile` from the root `Directory.Build.props`; add a `packages.lock.json` for `SolidGround.Revit` once it has any `PackageReference`.

## 3. Manifest and isolated add-in context

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- One `.addin` manifest, one `Application` entry: `Name` "SolidGround", `Assembly` "SolidGround.Revit.dll" (bare filename), `FullClassName` "SolidGround.Revit.SolidGroundApplication", `AddInId` a freshly minted GUID pinned once, `VendorId` "SolidGround", `VendorDescription` "SolidGround" (owner decision 2). No separate `Command` manifest entry: `CreateToposolidCommand` is registered as a ribbon `PushButtonData` in `OnStartup` instead.
- `UseRevitContext=false`, `ContextName="SolidGround"` (owner decision 2), `UseAllContextsForDependencyResolution=false` set explicitly rather than left unset, so the manifest states the disabled state directly. No `PublicAssemblies` and no `Dependencies` unless a specific, reviewed integration needs them.
- No temporary all-users manifest for live testing, and no repeat of the owner's other add-in's ProgramData fixture-manifest drop. If a live-test harness is needed later, it gets its own `ContextName` inside the isolated-context model.
- Exact element names and manifest syntax for the isolation settings were verified for Revit 2027 on 2026-09-20; see `revit-2027-verification-and-host-design.md`, item 1.

## 4. Ribbon and command structure

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- One tab "SolidGround", one panel, one `PushButtonData` for `CreateToposolidCommand`. An icon ships in the initial milestone (owner decision 5; Issue #19 designs it), embedded resource, `BitmapCacheOption.OnLoad`, frozen; a missing or broken icon logs a warning and the button still appears, matching the owner's other add-in's non-fatal contract.
- A full-sentence tooltip stating what the command needs (an active parcel or AOI, network access, an API key) and what it does.
- No `IExternalCommandAvailability`; the button stays enabled. `CreateToposolidCommand.Execute` runs a read-only Preflight step (AOI present, budget configured, API key resolvable) before opening a transaction, and returns `Result.Cancelled` on a Preflight rejection, pending verification item 2; verified-with-caveat by Issue #13's 2026-09-20 pass (the `Result`/`IExternalCommand` shape and the general Failed/Cancelled-reverses-changes semantics are confirmed; the specific zero-transaction Undo-stack side effect remains a runtime-only question deferred to manual test step 5), see `revit-2027-verification-and-host-design.md`, item 2.
- `[Transaction(TransactionMode.Manual)]` plus `[Regeneration(RegenerationOption.Manual)]` on `CreateToposolidCommand`, fixed by convention rather than left to per-command judgment, pending verification item 10; verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 10.
- `catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)` as the command's top-level boundary; report via one native `TaskDialog` (section 6) and return `Result.Failed` only when the document was actually left in a bad state.

## 5. Settings

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- Settings file location is machine-wide, under `%ProgramData%\SolidGround\Revit\` (owner decision 4), a deliberate departure from `AGENTS.md`'s general per-user-by-default rule, which governs the add-in's own install location, not its settings or logs.
- `System.Text.Json`, no `Microsoft.Extensions.*` dependency, matching `AGENTS.md`'s standard-library preference.
- Strict, fail-loud decoding: a corrupted settings file must fail rather than silently change effective settings.
- Mutex plus digest-conflict check plus atomic write whenever a settings file exists, adopted unchanged from the owner's other add-in: low cost, reusable for any future settings or cache file.
- One read pattern only, not the owner's other add-in's two coexisting cached-versus-fresh patterns (the owner's other add-in's own study flags this as an unintentional inconsistency, not a considered convention). Point budget, output unit, and buffer distance stay Core-level configuration under `AGENTS.md`'s Data and numeric contracts section for the initial milestone; a settings file is added only when a concrete need for persisted Revit-side state appears.

## 6. Logging and diagnostics

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- `System.Diagnostics.Trace`, matching `AGENTS.md`'s standard-library preference, but with an actual `TraceListener` attached and writing to a real log file, unlike the owner's other add-in's unconfigured `Trace` calls.
- Log location is machine-wide, under `%ProgramData%\SolidGround\Revit\Logs\` (owner decision 4), alongside the settings file from section 5.
- (detail about the owner's other add-in withheld)
- User-facing failures: one native `TaskDialog` per failure, full-sentence body, no custom WPF dialog shell for the initial milestone.
- A long problem list (for example, many NODATA cells) caps the `TaskDialog` body and writes the complete list to the log folder, naming the path in the dialog, adapted from the owner's other add-in's capped-dialog/full-list-on-disk pattern.
- All diagnostic and event-handler code wraps in try/catch that logs and swallows; it never throws back into Revit's pipeline.

## 7. Deployment and per-user install

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- A deploy script is built in the initial milestone, not a by-hand copy loop (owner decision 6).
- Default per-user deploy target: `%APPDATA%\Autodesk\Revit\Addins\2027`, folder name confirmed against the local Revit 2027 install at implementation time (see "Items that need Revit 2027 verification" item 3); verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 3.
- The deploy script refuses to run while `Revit.exe` is running.
- It enumerates the full dependency closure and fails closed if any file is missing, hash-verifies every file, and publishes by atomic versioned-folder rename plus atomic manifest replace, keeping the previous two versions for rollback, following the owner's other add-in's stage/hash-verify/atomic-rename/atomic-manifest-swap shape.
- Any all-user path, at runtime or in a future all-user deploy/install script, is read from `Application.AllUsersAddinsLocation`, never hardcoded, consistent with section 3's rejection of the owner's other add-in's ProgramData fixture-manifest drop as the forbidden case; a script that cannot call the Revit API fails closed pending an explicit reviewed value (see "Items that need Revit 2027 verification" item 4); verified on 2026-09-20 and flagged as a conflict with the exact wording used here, see `revit-2027-verification-and-host-design.md` item 4 and section 4.
- (detail about the owner's other add-in withheld)
- Code signing: accept Revit's unsigned-add-in prompt for now; no Authenticode signing in the initial milestone (owner decision 3, see "3. Code signing" in Owner decisions above, and verification item 14; verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 14).

## 8. Debugging

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- Dev loop: `dotnet build`, deploy via the deploy script from section 7, restart Revit, verify by hash that the deployed DLL matches the source build.
- No pyRevit, IronPython, or MCP live-driving harness. `AGENTS.md` forbids Python outright, not only for Phase 2's current scope, so this is out of scope permanently regardless of size or need.
- No RevitLookup or Add-In Manager dependency; add narrow, purpose-built diagnostics only when a specific need appears.
- Capture the executing `SolidGround.Revit` assembly's informational version, MVID, and SHA-256 at toposolid-creation time and store it in the Extensible Storage provenance entity (owner decision 7; see section 11), extending `AGENTS.md`'s Provenance decision field list, so a created toposolid always traces back to the exact build that made it.

## 9. Packaging, versioning, and release

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- No formal versioning scheme for the initial milestone. When one is needed, prefer a simple, idiomatic `<Version>` over the owner's other add-in's bespoke git-HEAD-plus-hash ledger, unless a specific reason calls for that stronger guarantee.
- No committed or git-ignored "release-staging" folder. If a distributable package is needed before the deploy script from section 7 covers it, keep the steps in an explicit, reviewed script.

## 10. Revit reference assemblies for local builds and CI

(detail about the owner's other add-in withheld)

**Reference-assembly decision (owner decision 1).** Local and deploy builds are settled: `HintPath` against `$(RevitInstallDir)`, default `C:\Program Files\Autodesk\Revit 2027`, `Private=false`, never committed. The CI compile path stays disabled until `SolidGround.Revit` exists; when activated:

- One CI-only MSBuild condition, using the owner's other add-in's own name, `UseRevitReferenceAssemblies`, swaps `HintPath` references for `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` 2027.x, exact-pinned, with a `packages.lock.json`. Neither local builds nor the shipped add-in ever use this condition.
- `EnableWindowsTargeting` is scoped to the same condition.
- The gate runs on the existing push-to-main `self-hosted` lane, with all nine conditions in `AGENTS.md`'s Build and CI section intact: no pull-request trigger, the job-level guard and first-step check carried over unchanged, no new actions added (`actions/cache` is not on the allow-list and must not be added), full-commit-SHA pinning, and every item on the never-list preserved. The owner's other add-in's anti-regression guard is `pull_request`-gated and diffs a PR base ref; `self-hosted` must never see a `pull_request` event, so SolidGround's version instead runs on every push and asserts the condition string directly rather than diffing a base ref.
- **Licensing position.** Checked on nuget.org and github.com/Nice3point/revit-api, 2026-09-20: the `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` packages are MIT-licensed (Copyright (c) 2022 Nice3point); the repository describes itself as "Reference assemblies for Revit plugin development." MIT covers the packaging only; the API surface itself is Autodesk's, and its SDK terms for a redistributed reference assembly are accepted by the owner for CI compile-checking only, matching the owner's other add-in's 2026-07-10 ruling.
- **Version availability.** nuget.org listed `2027.0.0`, `2027.0.10`, `2027.0.20`, `2027.1.0`, and `2027.2.0` as of 2026-09-20; one of these is pinned exactly when the gate is activated.

## 11. Provenance and Extensible Storage

(detail about the owner's other add-in withheld)

**Adopted for `SolidGround.Revit`:**

- One Extensible Storage schema, its field-list contract defined in `SolidGround.Core`, consumed by a `SchemaBuilder` call in `SolidGround.Revit`, matching the owner's other add-in's Core/Addin split.
- A freshly minted, stable schema GUID and an explicit `schemaVersion` field, matching `AGENTS.md`'s "stable GUID and explicit schema version" requirement; SolidGround picks the explicit-field mechanism consistently from the first schema rather than the owner's other add-in's mix of both approaches across different schemas.
- `AccessLevel.Public` read and `AccessLevel.Vendor` write, matching the owner's other add-in's default: any add-in can read the provenance, only SolidGround's own vendor id can write it.
- Field list: source dataset, collection date, quality level, horizontal datum, vertical datum, original and retained point counts, elevation minimum and maximum, output unit and foot definition, the complete local-origin offset, plus the build-identity fields the owner added (owner decision 7): the `SolidGround.Revit` assembly's informational version, MVID, and SHA-256.
- The exact `SchemaBuilder`/`Element.GetEntity()`/`DeleteEntity()`/`AccessLevel` API shape was verified for Revit 2027 on 2026-09-20; see `revit-2027-verification-and-host-design.md`, item 8.

## Items that need Revit 2027 verification before implementation

These 14 items are the work of Issue #13. Issue #13 verified all 14 of them on 2026-09-20; the results, the locked thin-host design, and the manual integration test plan are recorded in `revit-2027-verification-and-host-design.md`. Item 2's specific `Result.Failed`/`Result.Cancelled` Undo-stack side effect remains a runtime-only open question deferred to that plan's manual test step 5, matching several other items' residual runtime-only gaps. `SolidGround.Revit` must not be scaffolded against an assumption these items leave open.

1. Isolated add-in context manifest syntax: exact element names for `UseRevitContext`, `ContextName`, `Dependencies`, `PublicAssemblies`, `UseAllContextsForDependencyResolution`. The owner's other add-in's manifest predates the feature; cite the Revit 2027 SDK or Autodesk's manifest docs directly.
2. Whether returning `Result.Failed` clears Revit's native Undo stack even with no transaction open, and `Result.Cancelled` avoids this. Observed in a Revit 2026 add-in; re-confirm against Revit 2027 before `CreateToposolidCommand` relies on it.
3. The exact per-user Add-Ins folder name for Revit 2027, `%APPDATA%\Autodesk\Revit\Addins\2027`. The owner's other add-in confirms only the 2026 pattern; confirm the `2027` segment against the installed product.
4. `Application.AllUsersAddinsLocation`, required for any all-user scenario. The owner's other add-in has no example; verify the property's shape against the Revit 2027 API before any code reads it.
5. Whether a Revit add-in class library still fails to copy `PackageReference`/lock-file assemblies to `bin/` by default under `net10.0-windows`. The owner's other add-in's `CopyLocalLockFileAssemblies` workaround was observed under `net8.0-windows`; SDK behavior can change between target frameworks.
6. WPF-in-Revit threading rules: a WPF dispatcher callback is not Revit API context, and modeless work must go through `ExternalEvent`. Cited from a Revit 2026 add-in and an unimplemented the owner's other add-in spec; re-verify before any modeless UI.
7. Current `PushButtonData`/`RibbonPanel` API signatures (`CreateRibbonTab`, `CreateRibbonPanel`, `RibbonPanel.AddItem`, `PushButtonData.LargeImage`/`Image`). Confirm directly from the Revit 2027 SDK when implementing.
8. Extensible Storage API shape: `SchemaBuilder`, `Element.GetEntity()`/`DeleteEntity()`, `AccessLevel.Public`/`Vendor`. `AGENTS.md` already cites the 2027 guide; the owner's other add-in's own usage is 2026-era and must be checked for signature changes.
9. `NativeToposolidMaxPointThreshold`/`LinkToposolidMaxPointThreshold` numeric defaults, already flagged in `AGENTS.md`; the owner's other add-in has no toposolid precedent to help confirm either number.
10. `TransactionAttribute`/`TransactionMode.Manual` and `RegenerationAttribute`/`RegenerationOption.Manual`: confirm unchanged in the Revit 2027 SDK before fixing them as convention in section 4. Observed only in a Revit 2026 add-in.
11. The `.addin` manifest's base required elements proposed in section 3 (`Name`, `Assembly`, `FullClassName`, `AddInId`, `VendorId`, `VendorDescription`) are sourced only from the owner's other add-in's Revit 2026 manifest, not yet checked against the 2027 SDK.
12. (detail about the owner's other add-in withheld)
13. `IExternalApplication`, `IExternalCommand`, and `TaskDialog`: confirm these retain their Revit 2027 shape; currently asserted only from the owner's other add-in's 2026 usage, though risk is low given long API stability.
14. Whether Revit 2027 still shows an unsigned-add-in prompt on every new DLL build, and whether Authenticode publisher trust still suppresses it as in 2026. Re-verify before owner decision 3 (code signing) is acted on.

## What this note does not do

This note records accepted conventions and open verification items; it does not scaffold `SolidGround.Revit`, write a manifest, or implement a command. Issue #13 performs the Revit 2027 SDK verification the 14 items above require. Issue #14 scaffolds `SolidGround.Revit` following the conventions this note records, once that verification exists and an explicit implementation task approves it, per `AGENTS.md`'s "Mission and current boundary" section.
