# Revit add-in conventions

Issue #12 records the owner-approved conventions for `SolidGround.Revit`, the Phase 2 Revit host adapter. Phase 1 was signed off on 2026-09-20 (Issue #10). This note is the decision record Phase 2 must follow; it is not implementation work, and no `SolidGround.Revit` code exists yet. Issue #13 verifies the Revit 2027 API details this note leaves open. Issue #14 scaffolds `SolidGround.Revit` once that verification lands.

## Basis

A proposal studied the owner's other Revit add-in, read-only, on 2026-09-20, and posted it to [Issue #12](https://github.com/mjfrancese/SolidGround/issues/12#issuecomment-5752790944). Every verdict in the proposal was judged against this repository's own `AGENTS.md`. References below to that other add-in are descriptive only; its internal file paths, commit identity, and exact checkout location are not reproduced here, and none of them are SolidGround paths. That other add-in's own instruction-file layout is CLAUDE.md-primary with nested per-directory CLAUDE.md files and a tracked `.claude/` directory. That shape is not imported here: `AGENTS.md` stays SolidGround's single canonical instruction file, and `.claude/`/`.codex/` directories stay forbidden.

The owner answered seven open decisions from the proposal on 2026-09-20; those answers are recorded below and are normative. Every other item the proposal marked Unchanged or Adapted is accepted as proposed; every item it marked Not applicable is not adopted.

## Owner decisions, 2026-09-20

1. **Reference assemblies.** Option A plus option B. Local and deploy builds reference `RevitAPI.dll`/`RevitAPIUI.dll` from the installed Revit 2027 via HintPath, never committed. A CI-only compile gate, activated once `SolidGround.Revit` exists, uses the pinned `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` 2027.x packages. See "10. Revit reference assemblies for local builds and CI" below for the full mechanism and the licensing position.
2. **ContextName.** `"SolidGround"`. See "3. Manifest and isolated add-in context" below for the full manifest shape.
3. **Code signing.** Accept Revit's unsigned-add-in prompt for now; no signing in the initial milestone. Revisit with Issue #17 after verification item 14 (Revit 2027 unsigned-add-in prompt behavior) is checked; verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 14.

**Update, Issue #17 (2026-09-24):** revisited — self-signed Authenticode signing, non-exportable `CurrentUser\My` certificate, `LocalMachine\Root`/`LocalMachine\TrustedPublisher` trust import per workstation. See "12. Code signing and release packaging" below.
4. **Settings and log location.** Machine-wide, following the same pattern already used in the owner's other Revit add-in, under `%ProgramData%\SolidGround\Revit\` (a settings file and a `Logs\` folder), not per-user. This is a deliberate, scoped exception: the add-in itself still installs per-user (see decision 6 and "7. Deployment and per-user install" below); only settings and logs are machine-wide. See "5. Settings" and "6. Logging and diagnostics" below.
5. **Ribbon icon.** Ship an icon for `CreateToposolidCommand` in the initial milestone; Issue #19 designs it. See "4. Ribbon and command structure" below.
6. **Deployment.** Write a deploy script in the initial milestone, not a by-hand copy loop. See "7. Deployment and per-user install" below.
7. **Provenance.** Add build identity, informational version, MVID, and SHA-256, to the Extensible Storage entity, beyond the field list `AGENTS.md`'s Provenance decision section already requires. See "11. Provenance and Extensible Storage" below.
8. **Ribbon tab placement.** Keep the dedicated "SolidGround" tab. Autodesk's Ribbon Guidelines appendix states: "Applications that add and/or modify elements within Revit should be added to the Add-Ins tab." The Ribbon Panels and Controls page separately states that a new custom ribbon tab "should only be used if necessary." The owner kept the dedicated tab on 2026-09-20 with that guidance in view. The departure is deliberate and supportable: `CreateRibbonTab` is a fully supported, documented API, SolidGround is a single-purpose site tool whose one panel is easier to find on its own tab, and one tab is far below the platform's custom-tab cap. Raised and evidenced by Issue #13, item 12 (`revit-2027-verification-and-host-design.md`).

## 1. Project layout and naming

The owner's other Revit add-in's own Revit-free core module carries no Revit reference in its project file and no `using Autodesk` in its source. A second Revit-free module there states the same boundary in an explicit project-file comment. That boundary is also machine-enforced there, by an xUnit architecture-test project asserting the Revit-free core module never references the Revit-facing one. An accepted decision in that project adds Revit abstraction interfaces in its core module with fakes in tests, so its own large test suite runs without Revit. Its Revit-facing module references its Revit-free modules by plain `ProjectReference`. Its commands are one-file-per-class, mostly `sealed`, with no shared base class. Its `IExternalApplication` implementation is direct, and exposes a static, settable-private-set `Instance` singleton.

**Adopted for `SolidGround.Revit`:**

- `src/SolidGround.Revit/SolidGround.Revit.csproj`, one project, inherits the repository's existing root `Directory.Build.props` rather than repeating per-project property blocks the way the owner's other Revit add-in does.
- `src/SolidGround.Revit/SolidGroundApplication.cs`, implements `IExternalApplication` directly, no static `Instance` for the initial milestone.
- `src/SolidGround.Revit/Commands/CreateToposolidCommand.cs`, `sealed class CreateToposolidCommand : IExternalCommand`, file name matches class name.
- `ProjectReference` to `src/SolidGround.Core/SolidGround.Core.csproj` only. No second Revit-free module: `AGENTS.md`'s architecture table names exactly four projects.
- No Revit abstraction-interface or Fakes layer for the initial milestone. `CreateToposolidCommand` is the only Revit call site, so there is no seam to protect yet; the equivalent precedent from the owner's other Revit add-in is revisited once a second call site appears.
- A `SolidGround.Tests` assembly-reference test asserts `SolidGround.Core.dll` carries no reference to `RevitAPI`, `RevitAPIUI`, or `SolidGround.Revit`, via `Assembly.GetReferencedAssemblies()` only, mirroring the reflection half of the equivalent architecture test in the owner's other Revit add-in without adopting its pinned `NetArchTest.Rules` package. Runs on any CI agent without a Revit SDK.

## 2. Target framework and reference assemblies (build-time mechanism)

The owner's other Revit add-in pins one TFM, `net8.0-windows`, with no version branching. A `RevitInstallDir` MSBuild property defaults to its own local Revit 2026 install and is overridable via `-p:RevitInstallDir` or an environment variable of the same name. `RevitAPI.dll`/`RevitAPIUI.dll` are referenced via `<HintPath>`/`<Private>false</Private>`, never committed. `CopyLocalLockFileAssemblies` is set explicitly there too, with a comment explaining that a Revit add-in class library does not copy `PackageReference` assemblies to `bin/` by default.

**Adopted for `SolidGround.Revit`:**

- `TargetFramework` = `net10.0-windows`, matching `AGENTS.md`'s architecture table.
- `RevitInstallDir` MSBuild property, default `C:\Program Files\Autodesk\Revit 2027` (confirmed present on the local reference machine, API version `27.0.10.13`), overridable the same way the owner's other Revit add-in's own property is.
- `RevitAPI.dll`/`RevitAPIUI.dll` via `HintPath` against `$(RevitInstallDir)`, `Private=false`, never committed.
- No `UseWPF` until a WPF surface is designed; the initial milestone uses a plain `TaskDialog` (see section 6).
- `CopyLocalLockFileAssemblies=true` from the project's creation, since `SolidGround.Revit` takes a `ProjectReference` to `SolidGround.Core`, which already carries `PackageReference` entries (NetTopologySuite, ProjNET) that must be copied into `SolidGround.Revit`'s own output for the isolated add-in context to load them at runtime.
- Inherit `LangVersion`, `Nullable`, `ImplicitUsings`, `TreatWarningsAsErrors`, and `RestorePackagesWithLockFile` from the root `Directory.Build.props`; add a `packages.lock.json` for `SolidGround.Revit` once it has any `PackageReference`.

## 3. Manifest and isolated add-in context

The owner's other Revit add-in's own `.addin` manifest declares an `Application` entry (`Name`, `Assembly` as a bare filename, `FullClassName`, `AddInId`, `VendorId`, `VendorDescription`) and a `Command` fallback entry. No `UseRevitContext`, `ContextName`, `Dependencies`, `PublicAssemblies`, or `UseAllContextsForDependencyResolution` element appears anywhere in that add-in's manifests; the feature postdates its own Revit 2026 target, or was never adopted. A hardcoded all-users manifest path is exactly the "hardcode an all-user add-in directory" anti-pattern `AGENTS.md` forbids, and is avoided here for that reason.

**Adopted for `SolidGround.Revit`:**

- One `.addin` manifest, one `Application` entry: `Name` "SolidGround", `Assembly` "SolidGround.Revit.dll" (bare filename), `FullClassName` "SolidGround.Revit.SolidGroundApplication", `AddInId` a freshly minted GUID pinned once, `VendorId` "SolidGround", `VendorDescription` "SolidGround" (owner decision 2). No separate `Command` manifest entry: `CreateToposolidCommand` is registered as a ribbon `PushButtonData` in `OnStartup` instead.
- `UseRevitContext=false`, `ContextName="SolidGround"` (owner decision 2), `UseAllContextsForDependencyResolution=false` set explicitly rather than left unset, so the manifest states the disabled state directly. No `PublicAssemblies` and no `Dependencies` unless a specific, reviewed integration needs them.
- No temporary all-users manifest for live testing, and no repeat of that all-users manifest anti-pattern. If a live-test harness is needed later, it gets its own `ContextName` inside the isolated-context model.
- Exact element names and manifest syntax for the isolation settings were verified for Revit 2027 on 2026-09-20; see `revit-2027-verification-and-host-design.md`, item 1.

## 4. Ribbon and command structure

The owner's other Revit add-in creates one tab with panels left-to-right via `CreateRibbonTab`/`CreateRibbonPanel`, creation order (not an index) controlling layout, all buttons flat `PushButtonData`. Tooltips are usually full-sentence prose stating preconditions and side effects. Icons are embedded resources, decoded with `BitmapCacheOption.OnLoad` and frozen; a missing or broken icon logs a warning and returns null rather than breaking ribbon creation, "non-fatal by contract." `IExternalCommandAvailability` is never used there either; precondition logic runs inside `Execute()` and buttons stay always enabled. A read-only Preflight runs before any transaction opens, and only on success does the command mutate the document. `[Transaction(TransactionMode.Manual)]` plus `[Regeneration(RegenerationOption.Manual)]` is the majority pairing among its mutating commands but is applied inconsistently. `Result.Cancelled` is preferred over `Result.Failed` for a rejected read-only preflight, because Revit clears the native Undo stack on `Result.Failed` even with no open transaction. A uniform catch filter excludes `OutOfMemoryException` and `StackOverflowException` from an otherwise broad catch-all, used consistently across its own commands.

**Adopted for `SolidGround.Revit`:**

- One tab "SolidGround" (owner decision 8), one panel, one `PushButtonData` for `CreateToposolidCommand`. An icon ships in the initial milestone (owner decision 5; Issue #19 designs it), embedded resource, `BitmapCacheOption.OnLoad`, frozen; a missing or broken icon logs a warning and the button still appears, matching the same non-fatal contract already used in the owner's other Revit add-in. Issue #19's own icon design record, palette, contrast evidence, and manual evidence plan are recorded in `docs/architecture/revit-ribbon-icons.md`.
- A full-sentence tooltip stating what the command needs (an active parcel or AOI, network access, an API key) and what it does.
- No `IExternalCommandAvailability`; the button stays enabled. `CreateToposolidCommand.Execute` runs a read-only Preflight step (AOI present, budget configured, API key resolvable) before opening a transaction, and returns `Result.Cancelled` on a Preflight rejection, pending verification item 2; verified-with-caveat by Issue #13's 2026-09-20 pass (the `Result`/`IExternalCommand` shape and the general Failed/Cancelled-reverses-changes semantics are confirmed; the specific zero-transaction Undo-stack side effect remains a runtime-only question deferred to manual test step 5), see `revit-2027-verification-and-host-design.md`, item 2.
- `[Transaction(TransactionMode.Manual)]` plus `[Regeneration(RegenerationOption.Manual)]` on `CreateToposolidCommand`, fixed by convention rather than left to per-command judgment, pending verification item 10; verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 10.
- `catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)` as the command's top-level boundary; report via one native `TaskDialog` (section 6) and return `Result.Failed` only when the document was actually left in a bad state.

## 5. Settings

The owner's other Revit add-in's own settings component is a shared JSON settings POCO using `System.Text.Json`, no `Microsoft.Extensions` dependency, stored machine-wide under that project's own `%ProgramData%` location (path not reproduced here). It defines strict decoders that throw on invalid bytes instead of .NET's default lossy decoding, so a corrupted settings file fails loudly. Saving takes a named system `Mutex`, compares a SHA-256 digest of the current file against the digest recorded at load time and returns a conflict rather than clobbering, writes to a random temp file, then publishes atomically.

**Adopted for `SolidGround.Revit`:**

- Settings file location is machine-wide, under `%ProgramData%\SolidGround\Revit\` (owner decision 4), a deliberate departure from `AGENTS.md`'s general per-user-by-default rule, which governs the add-in's own install location, not its settings or logs.
- `System.Text.Json`, no `Microsoft.Extensions.*` dependency, matching `AGENTS.md`'s standard-library preference.
- Strict, fail-loud decoding: a corrupted settings file must fail rather than silently change effective settings.
- Mutex plus digest-conflict check plus atomic write whenever a settings file exists, adopted unchanged from the owner's other Revit add-in: low cost, reusable for any future settings or cache file.
- One read pattern only, not the two coexisting cached-versus-fresh patterns found in the owner's other Revit add-in (that project's own study flags this as an unintentional inconsistency, not a considered convention). Point budget, output unit, and buffer distance stay Core-level configuration under `AGENTS.md`'s Data and numeric contracts section for the initial milestone; a settings file is added only when a concrete need for persisted Revit-side state appears.

## 6. Logging and diagnostics

No logging framework exists in the owner's other Revit add-in's Revit-facing module or its Revit-facing dependencies; `Trace.TraceWarning`/`Trace.TraceError` calls appear across many files with no configured `TraceListener` anywhere in that repository, so without an attached debugger these writes go nowhere durable. Log files land machine-wide under that project's own `%ProgramData%` location, one timestamped file per run, no rotation, no cap. A rule repeated across multiple files there: diagnostic or logging code must never throw back into Revit's own pipeline. Long problem lists are capped in-dialog there too, with the full list written to a fixed, named log-folder file on a best-effort basis, adopted after a similar failure mode observed in that project (an unbounded problem list once rendered too tall to dismiss).

**Adopted for `SolidGround.Revit`:**

- `System.Diagnostics.Trace`, matching `AGENTS.md`'s standard-library preference, but with an actual `TraceListener` attached and writing to a real log file, unlike the unconfigured `Trace` calls in the owner's other Revit add-in.
- Log location is machine-wide, under `%ProgramData%\SolidGround\Revit\Logs\` (owner decision 4), alongside the settings file from section 5.
- Every query string logged anywhere in the pipeline, Core or Revit, stays redacted per `AGENTS.md`; never exempted, unlike the owner's other Revit add-in's own redaction, which is confined to a separate evidence-upload subsystem and never applied to plain log files there.
- User-facing failures: one native `TaskDialog` per failure, full-sentence body, no custom WPF dialog shell for the initial milestone.
- A long problem list (for example, many NODATA cells) caps the `TaskDialog` body and writes the complete list to the log folder, naming the path in the dialog, adapted from the same capped-dialog/full-list-on-disk pattern already used in the owner's other Revit add-in.
- All diagnostic and event-handler code wraps in try/catch that logs and swallows; it never throws back into Revit's pipeline.

## 7. Deployment and per-user install

The owner's other Revit add-in's own deploy script targets `%APPDATA%\Autodesk\Revit\Addins\2026`, a per-user, year-numbered folder, and refuses to run while `Revit.exe` is already running. The `2026` segment is a literal hardcoded string there, recurring in multiple scripts; no `Application.AllUsersAddinsLocation`-style dynamic lookup exists anywhere in that repository. Its current deploy design stages the whole dependency closure in a private folder, hash-verifies every file, and retains the previous two versioned folders for rollback, publishing by a single directory rename into a fresh versioned folder, never overwritten in place, then cutting Revit over with one atomic manifest-file replace. That project's own experience is that deploy snapshots must enumerate every lazily-loaded runtime dependency and fail closed if any is missing, and that Authenticode self-signing, with the certificate trusted machine-wide in `LocalMachine\Root` and `LocalMachine\TrustedPublisher`, is the only mechanism it found that durably suppresses Revit's unsigned-add-in prompt (a per-user registry allow-list did not work there). It also has a separate, much simpler, git-ignored fallback install script that does a flat file copy with no hashing, no rollback, and no signing.

**Adopted for `SolidGround.Revit`:**

- A deploy script is built in the initial milestone, not a by-hand copy loop (owner decision 6).
- Default per-user deploy target: `%APPDATA%\Autodesk\Revit\Addins\2027`, folder name confirmed against the local Revit 2027 install at implementation time (see "Items that need Revit 2027 verification" item 3); verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 3.
- The deploy script refuses to run while `Revit.exe` is running.
- It enumerates the full dependency closure and fails closed if any file is missing, hash-verifies every file, and publishes by atomic versioned-folder rename plus atomic manifest replace, keeping the previous two versions for rollback, following the same stage/hash-verify/atomic-rename/atomic-manifest-swap shape already used in the owner's other Revit add-in.
- Any all-user path, at runtime or in a future all-user deploy/install script, is read from `ControlledApplication.AllUsersAddinsLocation` in `OnStartup`/`OnShutdown` or `Application.AllUsersAddinsLocation` in command-time code, never hardcoded, consistent with section 3's rejection of a hardcoded all-users manifest path as the forbidden case; a script that cannot call the Revit API fails closed pending an explicit reviewed value (see "Items that need Revit 2027 verification" item 4); verified on 2026-09-20; the wording was corrected by owner direction the same day, see `revit-2027-verification-and-host-design.md`, item 4 and section 4.
- No ad hoc, git-ignored install script as the only distribution path, unlike the fallback approach used in the owner's other Revit add-in.
- Code signing: accept Revit's unsigned-add-in prompt for now; no Authenticode signing in the initial milestone (owner decision 3, see "3. Code signing" in Owner decisions above, and verification item 14; verified on 2026-09-20, see `revit-2027-verification-and-host-design.md`, item 14).

  **Update, Issue #17 (2026-09-24):** signing shipped, scoped to release packages and the ordinary dev-loop deploy path alike (once a developer has run the one-time trust import locally); it never runs in CI. See section 12.

## 8. Debugging

The owner's other Revit add-in has no `launchSettings.json` or IDE-attach workflow for its Revit-facing project; its own standing loop is build, deploy, restart Revit, then verify deployed hashes, since loaded add-ins do not hot-reload. Its primary live-inspection tooling is a third-party pyRevit MCP extension driving IronPython inside a live Revit session, plus per-issue PowerShell UI-Automation driver scripts. Neither RevitLookup nor Add-In Manager is referenced anywhere in that repository. A small helper there captures the executing assembly's informational version, MVID, on-disk path, and its own SHA-256, embedded into diagnostic and evidence writers.

**Adopted for `SolidGround.Revit`:**

- Dev loop: `dotnet build`, deploy via the deploy script from section 7, restart Revit, verify by hash that the deployed DLL matches the source build.
- No pyRevit, IronPython, or MCP live-driving harness. `AGENTS.md` forbids Python outright, not only for Phase 2's current scope, so this is out of scope permanently regardless of size or need.
- No RevitLookup or Add-In Manager dependency; add narrow, purpose-built diagnostics only when a specific need appears.
- Capture the executing `SolidGround.Revit` assembly's informational version, MVID, and SHA-256 at toposolid-creation time and store it in the Extensible Storage provenance entity (owner decision 7; see section 11), extending `AGENTS.md`'s Provenance decision field list, so a created toposolid always traces back to the exact build that made it.

## 9. Packaging, versioning, and release

The owner's other Revit add-in has no `<Version>`/`<AssemblyVersion>`/`<FileVersion>`/`<InformationalVersion>` MSBuild property and no Nerdbank.GitVersioning/GitVersion/MinVer package anywhere in it; deployed-artifact identity is git HEAD plus a SHA-256 hash instead. Its only packaged "release" is entirely git-ignored and unsigned, documented as a one-off internal fleet test.

**Adopted for `SolidGround.Revit`:**

- No formal versioning scheme for the initial milestone. When one is needed, prefer a simple, idiomatic `<Version>` over the bespoke git-HEAD-plus-hash ledger used in the owner's other Revit add-in, unless a specific reason calls for that stronger guarantee.

  **Update, Issue #17 (2026-09-24):** adopted — `<Version>0.1.0</Version>` in `Directory.Build.props`.
- No committed or git-ignored "release-staging" folder. If a distributable package is needed before the deploy script from section 7 covers it, keep the steps in an explicit, reviewed script.

  **Update, Issue #17 (2026-09-24):** `scripts/New-ReleasePackage.ps1` is that script; its output lands in `artifacts/release/`, a subfolder of the pre-existing, already git-ignored `artifacts/` bucket, not a new folder — the "no committed or git-ignored release-staging folder" sentence stays literally true.

## 10. Revit reference assemblies for local builds and CI

The owner's other Revit add-in's own local and deploy builds use `HintPath`/`Private=false` against its locally installed Revit 2026 product, never committed. Its CI instead compiles under a separate MSBuild condition, `UseRevitReferenceAssemblies=true`, swapping the HintPath references for `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI`, metadata-only NuGet stub packages pinned to `2026.4.10`. The same condition sets `EnableWindowsTargeting=true`, because `net8.0-windows` otherwise refuses to build (`NETSDK1100`) on its own non-Windows Linux self-hosted runner. Adopting this CI-only path there required an explicit, dated owner ruling recorded directly in its own project file. A dedicated CI guard there, gated to `pull_request` events, fails outright if its own source changed but the project file no longer contains the literal string `UseRevitReferenceAssemblies`.

**Reference-assembly decision (owner decision 1).** Local and deploy builds are settled: `HintPath` against `$(RevitInstallDir)`, default `C:\Program Files\Autodesk\Revit 2027`, `Private=false`, never committed. The CI compile path stays disabled until `SolidGround.Revit` exists; when activated:

- One CI-only MSBuild condition, using the same name already used in the owner's other Revit add-in, `UseRevitReferenceAssemblies`, swaps `HintPath` references for `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` 2027.x, exact-pinned, with a `packages.lock.json`. Neither local builds nor the shipped add-in ever use this condition.
- `EnableWindowsTargeting` is scoped to the same condition.
- The gate runs on the existing push-to-main self-hosted-runner lane, with all nine conditions in `AGENTS.md`'s Build and CI section intact: no pull-request trigger, the job-level guard and first-step check carried over unchanged, no new actions added (`actions/cache` is not on the allow-list and must not be added), full-commit-SHA pinning, and every item on the never-list preserved. The equivalent anti-regression guard in the owner's other Revit add-in is `pull_request`-gated and diffs a PR base ref; SolidGround's own lane must never see a `pull_request` event, so SolidGround's version instead runs on every push and asserts the condition string directly rather than diffing a base ref.
- **Licensing position.** Checked on nuget.org and github.com/Nice3point/revit-api, 2026-09-20: the `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` packages are MIT-licensed (Copyright (c) 2022 Nice3point); the repository describes itself as "Reference assemblies for Revit plugin development." MIT covers the packaging only; the API surface itself is Autodesk's, and its SDK terms for a redistributed reference assembly are accepted by the owner for CI compile-checking only, matching the equivalent ruling already made for the owner's other Revit add-in.
- **Version availability.** nuget.org listed `2027.0.0`, `2027.0.10`, `2027.0.20`, `2027.1.0`, and `2027.2.0` as of 2026-09-20. The gate pins exactly `2027.0.10`: `revit-2027-verification-and-host-design.md` item 17 confirms its `ref/net10.0-windows7.0/*.dll` is SHA-256-byte-identical to the installed `27.0.10.13` `RevitAPI.dll`/`RevitAPIUI.dll`, while `2027.1.0` and `2027.2.0` add documented members (`AddInId.RevitApplicationId`, `SiteLocation.SetLatitudeAndLongitude`) absent from the local install and from every Revit API `SolidGround.Revit` actually calls. Issue #14 activated the gate with this pin on 2026-09-21; see `docs/architecture/revit-add-in-host-scaffold.md` for what it scaffolded on top of this design.

## 11. Provenance and Extensible Storage

The owner's other Revit add-in's own Extensible Storage schemas follow a named idiom: a fixed schema GUID constant, a vendor id naming that project, `AccessLevel.Public` read plus `AccessLevel.Vendor` write access, and one schema per feature. A schema version is always present in some form: a smaller example bakes it into the schema name itself, while a larger, original schema instead stores an explicit `schemaVersion` data field. Its own schema field definitions are kept in its Revit-free core module, deliberately separated from the Revit-facing module's `SchemaBuilder` calls that consume them, so the schema shape is unit-testable before a Revit command exists.

**Adopted for `SolidGround.Revit`:**

- One Extensible Storage schema, its field-list contract defined in `SolidGround.Core`, consumed by a `SchemaBuilder` call in `SolidGround.Revit`, matching the same Core/Addin split already used in the owner's other Revit add-in.
- A freshly minted, stable schema GUID and an explicit `schemaVersion` field, matching `AGENTS.md`'s "stable GUID and explicit schema version" requirement; SolidGround picks the explicit-field mechanism consistently from the first schema rather than the mix of both approaches used across different schemas in the owner's other Revit add-in.
- `AccessLevel.Public` read and `AccessLevel.Vendor` write, matching the same default already used in the owner's other Revit add-in: any add-in can read the provenance, only SolidGround's own vendor id can write it.
- Field list: source dataset, collection date, quality level, horizontal datum, vertical datum, original and retained point counts, elevation minimum and maximum, output unit and foot definition, the complete local-origin offset, plus the build-identity fields the owner added (owner decision 7): the `SolidGround.Revit` assembly's informational version, MVID, and SHA-256.
- The exact `SchemaBuilder`/`Element.GetEntity()`/`DeleteEntity()`/`AccessLevel` API shape was verified for Revit 2027 on 2026-09-20; see `revit-2027-verification-and-host-design.md`, item 8.

**Update, Issue #16 (2026-09-21):** this field list is now the real schema
`SolidGround_Provenance_Toposolid` (GUID `bc03d923-8c8a-4a1e-bd2a-8e41f0a4ff6e`, `CurrentVersion = 1`, 36
fields), defined in `SolidGround.Core.Provenance.ExtensibleStorageProvenanceSchema`/
`ExtensibleStorageProvenanceValues` and consumed by a `SchemaBuilder` call in
`SolidGround.Revit.Provenance.ProvenanceSchemaAdapter`, attached by
`SolidGround.Revit.Provenance.ProvenanceEntityWriter`, exactly matching the Core/Addin split, the
freshly-minted-GUID-plus-explicit-`schemaVersion`-field mechanism, and the `AccessLevel.Public`
read/`AccessLevel.Vendor` write pair adopted above. See
`docs/architecture/revit-extensible-storage-provenance.md` for the full field table, the Revit-side
write/read-back design, and the manual evidence plan.

## 12. Code signing and release packaging

Issue #17 (begun 2026-09-24) designed and implemented versioning, self-signed Authenticode code signing,
release packaging, and install/uninstall for `SolidGround.Revit`. Full design, the certificate's pinned
identity once minted, the release-package script's fail-closed preconditions, the install/uninstall scripts,
and a written-in-advance manual evidence plan are recorded in
`docs/architecture/revit-release-packaging-and-signing.md`, whose Evidence section is still pending a live
Revit 2027 session as of this writing.

**Adopted for `SolidGround.Revit`:**

- Versioning: one `<Version>0.1.0</Version>` in `Directory.Build.props`, inherited by all four projects;
  bumped by hand per release.
- Signing: a non-exportable `Cert:\CurrentUser\My` code-signing certificate
  (`CN=SolidGround Revit Add-in Signing`), minted by `scripts/Sign-RevitAddIn.ps1 -NewCertificate` and pinned
  in the committed, non-secret `scripts/signing-certificate.json` — no `.pfx` or password anywhere.
  `scripts/Sign-RevitAddIn.ps1`'s default mode signs `SolidGround.Revit.dll`, `SolidGround.Core.dll`, and
  every first-party release/install script with `Set-AuthenticodeSignature -HashAlgorithm SHA256`,
  Authenticode-timestamped against `http://timestamp.digicert.com` (the legacy Authenticode/PKCS#7 protocol,
  never RFC 3161) — fail-closed for a real release, best-effort for ordinary dev-loop signing.
  `scripts/Import-SigningTrust.ps1` performs the one-time, elevated, per-workstation trust import into
  `Cert:\LocalMachine\Root` and `Cert:\LocalMachine\TrustedPublisher` — the mechanism this project's own
  research already found durably suppresses the dialog for the owner's other Revit add-in's own certificate.
- Release packaging: `scripts/New-ReleasePackage.ps1` runs a fail-closed precondition chain (clean tree, HEAD
  pushed to `origin/main`, restore/build/test, sign and re-verify, timestamp confirmed,
  `Deploy-RevitAddIn.ps1`'s own `-WhatIf` validation reused verbatim, a native-binary/`runtimes\`/
  unexplained-file check, `THIRD-PARTY-NOTICES` coverage, version freshness) before staging, signing, and
  zipping a versioned release into `artifacts/release/`.
- Install/uninstall: `scripts/Install-SolidGround.ps1`/`scripts/install.cmd` (the zip's double-click entry
  point) unblock, verify signatures, report trust status, and forward to `scripts/Deploy-RevitAddIn.ps1`
  unchanged; `scripts/Uninstall-SolidGround.ps1` removes the manifest and every versioned deployment folder
  while always leaving Revit's own `HKCU:\...\CodeSigning` trust record untouched. `scripts/Deploy-RevitAddIn.ps1`
  itself is never modified by any of this.
- Distribution: a GitHub Release, tag `v0.1.0`, gated on the owner's explicit acceptance of a live Revit 2027
  validation session; never triggers `.github/workflows/ci.yml` (its `on:` block is `push: branches: [main]`
  only).

## Items that need Revit 2027 verification before implementation

These 14 items are the work of Issue #13. Issue #13 verified all 14 of them on 2026-09-20; the results, the locked thin-host design, and the manual integration test plan are recorded in `revit-2027-verification-and-host-design.md`. Item 2's specific `Result.Failed`/`Result.Cancelled` Undo-stack side effect remains a runtime-only open question deferred to that plan's manual test step 5, matching several other items' residual runtime-only gaps. `SolidGround.Revit` must not be scaffolded against an assumption these items leave open.

1. Isolated add-in context manifest syntax: exact element names for `UseRevitContext`, `ContextName`, `Dependencies`, `PublicAssemblies`, `UseAllContextsForDependencyResolution`. The owner's other Revit add-in's manifest predates the feature; cite the Revit 2027 SDK or Autodesk's manifest docs directly.
2. Whether returning `Result.Failed` clears Revit's native Undo stack even with no transaction open, and `Result.Cancelled` avoids this. Observed in a Revit 2026 add-in; re-confirm against Revit 2027 before `CreateToposolidCommand` relies on it.
3. The exact per-user Add-Ins folder name for Revit 2027, `%APPDATA%\Autodesk\Revit\Addins\2027`. The owner's other Revit add-in confirms only the 2026 pattern; confirm the `2027` segment against the installed product.
4. `Application.AllUsersAddinsLocation`, required for any all-user scenario. The owner's other Revit add-in has no example; verify the property's shape against the Revit 2027 API before any code reads it.
5. Whether a Revit add-in class library still fails to copy `PackageReference`/lock-file assemblies to `bin/` by default under `net10.0-windows`. The owner's other Revit add-in's `CopyLocalLockFileAssemblies` workaround was observed under `net8.0-windows`; SDK behavior can change between target frameworks.
6. WPF-in-Revit threading rules: a WPF dispatcher callback is not Revit API context, and modeless work must go through `ExternalEvent`. Cited from a Revit 2026 add-in and an unimplemented spec in the owner's other Revit add-in; re-verify before any modeless UI.
7. Current `PushButtonData`/`RibbonPanel` API signatures (`CreateRibbonTab`, `CreateRibbonPanel`, `RibbonPanel.AddItem`, `PushButtonData.LargeImage`/`Image`). Confirm directly from the Revit 2027 SDK when implementing.
8. Extensible Storage API shape: `SchemaBuilder`, `Element.GetEntity()`/`DeleteEntity()`, `AccessLevel.Public`/`Vendor`. `AGENTS.md` already cites the 2027 guide; the owner's other Revit add-in's own usage is 2026-era and must be checked for signature changes.
9. `NativeToposolidMaxPointThreshold`/`LinkToposolidMaxPointThreshold` numeric defaults, already flagged in `AGENTS.md`; the owner's other Revit add-in has no toposolid precedent to help confirm either number.
10. `TransactionAttribute`/`TransactionMode.Manual` and `RegenerationAttribute`/`RegenerationOption.Manual`: confirm unchanged in the Revit 2027 SDK before fixing them as convention in section 4. Observed only in a Revit 2026 add-in.
11. The `.addin` manifest's base required elements proposed in section 3 (`Name`, `Assembly`, `FullClassName`, `AddInId`, `VendorId`, `VendorDescription`) are sourced only from the owner's other Revit add-in's Revit 2026 manifest, not yet checked against the 2027 SDK.
12. Whether a command registered only via ribbon `PushButtonData` is still invocable in Revit 2027 without a manifest `Command` entry, and whether panel/tab creation order still controls layout. Sourced from a Revit 2026 design note and the owner's other Revit add-in's own 2026 startup code, not 2027 evidence.
13. `IExternalApplication`, `IExternalCommand`, and `TaskDialog`: confirm these retain their Revit 2027 shape; currently asserted only from the owner's other Revit add-in's 2026 usage, though risk is low given long API stability.
14. Whether Revit 2027 still shows an unsigned-add-in prompt on every new DLL build, and whether Authenticode publisher trust still suppresses it as in 2026. Re-verify before owner decision 3 (code signing) is acted on. Observed 2026-09-21 (Issue #14, Run 2): the prompt reappeared after a rebuild was redeployed under the same `AddInId` to a new versioned folder, with only the dialog's `Location:`/`Date:` text changed — trust is bound to the assembly's file location, not the `AddInId` alone, so every `scripts/Deploy-RevitAddIn.ps1` deploy re-prompts the dev loop until Issue #17 revisits signing. See `docs/architecture/revit-add-in-host-scaffold.md`'s Step 6 evidence for the full record; the signed-certificate half of this item remains unexercised.

## What this note does not do

This note records accepted conventions and open verification items; it does not scaffold `SolidGround.Revit`, write a manifest, or implement a command. Issue #13 performs the Revit 2027 SDK verification the 14 items above require. Issue #14 scaffolds `SolidGround.Revit` following the conventions this note records, once that verification exists and an explicit implementation task approves it, per `AGENTS.md`'s "Mission and current boundary" section.
