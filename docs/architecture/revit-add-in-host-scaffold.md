# Revit add-in host scaffold

Issue #14 scaffolds `SolidGround.Revit` on 2026-09-21: the project, its manifest, `SolidGroundApplication`
(ribbon), `Commands/CreateToposolidCommand` (a read-only Preflight-and-report command, not yet a toposolid
creator), file diagnostics, two placeholder ribbon icons, `scripts/Deploy-RevitAddIn.ps1`, and the CI-only
compile gate `docs/architecture/revit-add-in-conventions.md` section 10 left inactive. It adds no new
`SolidGround.Core` member and no new package reference beyond the CI-only, gated
`Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI` pair. `SolidGround.Core`, `SolidGround.Cli`, and their tests
are unchanged. This note follows `docs/architecture/revit-add-in-conventions.md`'s owner-approved decisions
and `docs/architecture/revit-2027-verification-and-host-design.md`'s locked thin-host design; it changes
neither.

## Basis

`docs/architecture/revit-add-in-conventions.md` (Issue #12, approved 2026-09-20) is the decision record;
`docs/architecture/revit-2027-verification-and-host-design.md` (Issue #13, 2026-09-20) verified every API
those decisions needed against the installed Revit 2027 SDK (`27.0.10.13`) and locked the "Thin-host
design" section this scaffold implements almost verbatim. Issue #14 mints one new, permanent artifact
those notes could only describe in the abstract: the manifest `AddInId`,
`bb4d7576-b0fb-432b-a2c5-15c7ea59147d`, pinned in `src/SolidGround.Revit/SolidGround.addin` and never to be
changed. Every Revit API member this scaffold calls was additionally compiled, for this task, against the
same installed `27.0.10.13` assemblies at `C:\Program Files\Autodesk\Revit 2027\` that Issue #13 inspected;
see "Revit 2027 API members used" below for the member-by-member citations, including a few call sites
Issue #13's 14 items did not separately enumerate.

## What Issue #14 built

- `src/SolidGround.Revit/SolidGround.Revit.csproj`: `net10.0-windows`, inherits the root
  `Directory.Build.props`, `CopyLocalLockFileAssemblies=true` from creation, a `RevitInstallDir` MSBuild
  property (default `C:\Program Files\Autodesk\Revit 2027`, overridable), the CI-only reference-assembly
  block, and a fail-fast target that errors out with an actionable message when `RevitAPI.dll`/
  `RevitAPIUI.dll` are missing at `RevitInstallDir` in local/deploy mode. Its only `ProjectReference` is to
  `SolidGround.Core`.
- `src/SolidGround.Revit/SolidGround.addin`: one hand-authored `Application` manifest entry plus an
  explicit `<ManifestSettings>` block. See "Manifest and isolated context" below.
- `src/SolidGround.Revit/SolidGroundApplication.cs`: `IExternalApplication`, no static `Instance`, builds
  the "SolidGround" ribbon tab, panel, and button. See "Ribbon and command shell flow" below.
- `src/SolidGround.Revit/Commands/CreateToposolidCommand.cs`: `sealed class ... : IExternalCommand`, the
  one shipped command, a read-only Preflight-and-report flow. See "Ribbon and command shell flow" below.
- `src/SolidGround.Revit/Diagnostics/{AddInLog,BuildIdentity,ProblemReportDialog}.cs`: file logging, build
  identity capture, and capped-list problem reporting. See "Diagnostics design" below.
- `src/SolidGround.Revit/Resources/{SolidGround.16.png,SolidGround.32.png,README.md}`: placeholder ribbon
  icons. See "Placeholder icons" below.
- `scripts/Deploy-RevitAddIn.ps1` and `scripts/README.md`: the per-user deploy script. See "Deploy script"
  below.
- The CI-only compile gate: `UseRevitReferenceAssemblies`-conditioned `PackageReference`s, a second lock
  file, and an activated `.github/workflows/ci.yml` step. See "CI compile gate" below.
- `SolidGround.slnx` gained one line, `<Project Path="src/SolidGround.Revit/SolidGround.Revit.csproj" />`,
  under the existing `/src/` folder.
- This note, and the "Version availability" line `docs/architecture/revit-add-in-conventions.md` section
  10 left open, are Issue #14's documentation deliverable.

## What Issue #14 deliberately does not do

- **No toposolid creation.** `CreateToposolidCommand.Execute` never opens a `Transaction` and never calls
  `Toposolid.Create`. Converting a `SolidGround.Core` export into a bounded native `Toposolid`, per the
  verification note's "Thin-host design" command-flow and Level/type/coordinate-mapping rules, is Issue
  #15.
- **No Extensible Storage.** No `Schema`, `SchemaBuilder`, or `Entity` exists in this milestone. Attaching
  reversible provenance to the created element, per `AGENTS.md`'s Provenance decision and the verification
  note item 8, is Issue #16.
- **No settings file.** Point budget, output unit, and buffer distance stay `SolidGround.Core`-level
  configuration, per conventions note section 5 ("a settings file is added only when a concrete need for
  persisted Revit-side state appears"). None appeared during this scaffold.
- **Placeholder icons only.** The two embedded PNGs are deterministically generated placeholders, not a
  designed icon; Issue #19 replaces their bytes. See "Placeholder icons" below.

## Manifest and isolated context

`src/SolidGround.Revit/SolidGround.addin` is a static, hand-authored template, not
`RevitAddInManifest.SaveAs()` output: that API silently omits any `<ManifestSettings>` child equal to its
own default (verification note item 1), which would drop
`<UseAllContextsForDependencyResolution>False</UseAllContextsForDependencyResolution>` from the file and
violate `AGENTS.md`'s "set explicitly" requirement. The manifest carries one `Application` entry:

| Element | Value |
| --- | --- |
| `Name` | `SolidGround` |
| `Assembly` | `SolidGround.Revit.dll` (bare file name; the deploy script rewrites this to an absolute path when it publishes — see "Deploy script") |
| `AddInId` | `bb4d7576-b0fb-432b-a2c5-15c7ea59147d` (freshly minted for this task; pinned permanently) |
| `FullClassName` | `SolidGround.Revit.SolidGroundApplication` |
| `VendorId` | `SolidGround` |
| `VendorDescription` | `SolidGround` |

and one explicit `<ManifestSettings>` block: `UseRevitContext=False`, `ContextName=SolidGround`,
`UseAllContextsForDependencyResolution=False`. No `<PublicAssemblies>`/`<Dependencies>` and no `<Command>`
entry: `CreateToposolidCommand` registers only through ribbon `PushButtonData`, confirmed invocable with no
manifest `Command` entry by verification note item 12.

## Ribbon and command shell flow

`SolidGroundApplication.OnStartup` initializes diagnostics first (`AddInLog.Initialize()`), logs the
build-identity line and both add-ins locations (never hardcoded — read from
`application.ControlledApplication.CurrentUserAddinsLocation`/`.AllUsersAddinsLocation`), then calls
`application.CreateRibbonTab("SolidGround")` followed by `CreateRibbonPanel("SolidGround", "SolidGround")`
and one `PushButtonData` for `CreateToposolidCommand`, added via `RibbonPanel.AddItem`. `CreateRibbonTab`
throws `Autodesk.Revit.Exceptions.ArgumentException` when a tab of that name already exists (read directly
from the installed `RevitAPIUI.xml` doc comments for this task, not one of Issue #13's 14 pre-verified
items); `CreateRibbon` catches exactly that type and reuses the existing tab rather than failing
`OnStartup`, which matters for repeated `OnStartup` calls inside isolated-context testing. The button text
is `"Create\nToposolid"`; its `ToolTip`/`LongDescription` are full-sentence prose that names today's actual,
narrower scope (a read-only check, not a toposolid creation) and calls out the parcel/AOI, network access,
and `OPENTOPOGRAPHY_API_KEY` a later milestone will need. The icon loads through `LoadIcon`, matching
the owner's other add-in's `LoadRibbonIcon` contract: `Assembly.GetManifestResourceStream`, `BitmapFrame.Create(stream,
BitmapCreateOptions.None, BitmapCacheOption.OnLoad)`, `Freeze()` when `CanFreeze`; a missing or broken icon
logs a warning and returns `null`, leaving the button text-only rather than failing ribbon creation. A
top-level `catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)` in
`OnStartup` logs and returns `Result.Failed` rather than letting a ribbon-creation failure propagate into
Revit.

`CreateToposolidCommand` carries `[Transaction(TransactionMode.Manual)]` plus
`[Regeneration(RegenerationOption.Manual)]` and implements
`Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)`. Its Preflight
(`RunPreflight`) is read-only by construction — it opens no `Transaction` — and checks: an active project
document exists (`commandData.Application.ActiveUIDocument?.Document`); the document is not a family
document (`Document.IsFamilyDocument`); and the `OPENTOPOGRAPHY_API_KEY` environment variable resolves
through `EnvironmentOpenTopographyApiKeyProvider.GetApiKey()`, checked only for non-null — the raw key value
is never read into any string this command touches. It also reads `SolidGround.Core`'s defaults purely for
display: `new SimplificationRequest().PointBudget` (15,000) and `LengthConverter.DefaultOutputUnit` (U.S.
survey foot). A Preflight rejection shows one `TaskDialog` (capped problem list via
`ProblemReportDialog.BuildRejectionBody`, full list written to the log folder) and returns
`Result.Cancelled` — not `Result.Failed` — because no `Transaction` ever opened and `Result.Cancelled` is
believed, pending the still-open runtime question in verification note item 2, to leave Revit's native Undo
stack untouched. A Preflight pass shows a different `TaskDialog` summarizing the checks, states that
toposolid creation arrives with Issue #15, and returns `Result.Succeeded`. On either path, and on the
top-level catch-all's `Result.Cancelled` path (no `Transaction` ever opened and the document is unchanged, so
`Result.Failed` is not returned here; that return is reserved for a document actually left in a bad state,
which Issue #15's transactional code will need to decide per path), `message` (the `ref string` parameter) is
deliberately left at its caller-provided empty value: Revit only shows its own automatic result dialog when
`message` is non-empty, so leaving it empty is what keeps every outcome to exactly the one `TaskDialog` this
code constructs itself.

## Diagnostics design

`AddInLog` is a process-wide `System.Diagnostics.Trace` listener. `Initialize()` (called once, from
`OnStartup`) builds its log directory from `Environment.SpecialFolder.CommonApplicationData` — never a
literal `%ProgramData%` path — landing at `...\SolidGround\Revit\Logs\`, matching AGENTS.md's
"Revit add-in conventions" section 5/6 machine-wide location; if that directory cannot be created (for
example, a locked-down machine), it falls back to the per-user
`Environment.SpecialFolder.LocalApplicationData` equivalent and logs that it did so. It attaches a daily
`TextWriterTraceListener` over a `FileStream` opened with `FileShare.ReadWrite`. Every public method
(`Info`/`Warning`/`Error`/`Shutdown`) wraps its own body in try/catch and never throws back into Revit's
pipeline, per AGENTS.md's "Diagnostics must never throw back into Revit." `AddInLog` does not redact on a
caller's behalf; any URL or query string a caller wants to log must already be redacted through
`SolidGround.Core.Sources.OpenTopography.OpenTopographyRedaction` before it reaches `AddInLog`.

`BuildIdentity` captures the loaded `SolidGround.Revit` assembly's informational version
(`AssemblyInformationalVersionAttribute`), MVID (`Assembly.ManifestModule.ModuleVersionId`), and a SHA-256
of the assembly file on disk, each captured independently (a `TryGet` wrapper) so one unavailable value
cannot suppress the others. `SolidGroundApplication.OnStartup` logs `BuildIdentity.Current.ToLogLine()` once
per session. Owner decision 7 (2026-09-20) adds these same three fields to the eventual Extensible Storage
provenance entity; this milestone only logs them.

`ProblemReportDialog.BuildRejectionBody` caps a Preflight's problem list at 8 inline lines (adopted from
the owner's other add-in's own 2026-08-04 incident, where an unbounded 103-item `TaskDialog` grew too tall to dismiss),
appends an "... and N more." line when truncated, and always attempts to write the complete numbered list
to a timestamped file under the log folder, naming that file's path in the dialog body either way (or
stating that the write failed, if it did).

## CI compile gate

The gate `docs/architecture/revit-add-in-conventions.md` section 10 designed is now active. Mechanism, in
`src/SolidGround.Revit/SolidGround.Revit.csproj`:

- A `PropertyGroup`/`ItemGroup` pair conditioned on `'$(UseRevitReferenceAssemblies)' == 'true'` adds
  `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI`, exact-pinned to `2027.0.10` (SHA-256-byte-identical to the
  installed `27.0.10.13` build; see the conventions note's now-filled section 10 "Version availability"
  line), with `PrivateAssets="all"` (never flow to a consumer) and `ExcludeAssets="runtime"` (CI only ever
  compiles this project; excluding the runtime asset keeps the stubbed `RevitAPI*.dll` bytes out of every
  build output).
- The same condition sets `EnableWindowsTargeting=true`, because `net10.0-windows` otherwise refuses to
  build (`NETSDK1100`) on the Linux `self-hosted` runner.
- The same condition also sets `<NuGetLockFilePath>packages.ci.lock.json</NuGetLockFilePath>` — a
  **second, separate** lock file. This is why: the default `packages.lock.json` (used by every local
  restore and by CI's own restore of `SolidGround.Core`/`Cli`/`Tests`) must never contain the Nice3point
  entries, since a local build never sets `UseRevitReferenceAssemblies`; scoping the alternate lock-file
  path to the same condition means a local `dotnet restore` of `SolidGround.Revit` never even looks at, let
  alone rewrites, `packages.ci.lock.json`, and CI's `-p:UseRevitReferenceAssemblies=true` restore never
  touches `packages.lock.json`. Both files are committed. Verified directly: `packages.lock.json` contains
  zero `Nice3point` entries; `packages.ci.lock.json` contains both `Nice3point.Revit.Api.RevitAPI` and
  `RevitAPIUI` as direct dependencies, plus the same `SolidGround.Core` transitive graph
  (`NetTopologySuite`, `ProjNET`).
- Everything else in `SolidGround.Revit.csproj` — the `RevitInstallDir`-based `HintPath` references and the
  `SolidGroundEnsureRevitApiPresent` fail-fast target — is conditioned the opposite way
  (`'$(UseRevitReferenceAssemblies)' != 'true'`), so local and deploy builds keep referencing the installed
  Revit 2027 SDK directly and never see the Nice3point packages.

`.github/workflows/ci.yml`'s existing "Restore locked dependencies" and "Build" steps now pass
`-p:UseRevitReferenceAssemblies=true` in addition to their existing flags:

```powershell
dotnet restore SolidGround.slnx --locked-mode -p:UseRevitReferenceAssemblies=true
dotnet build SolidGround.slnx --configuration Release --no-restore -p:UseRevitReferenceAssemblies=true
```

A new step, "Assert the CI compile gate ships no Revit reference assembly", runs immediately after Build
and fails the job if any `RevitAPI*.dll` or `Nice3point*.dll` file exists anywhere under
`src/SolidGround.Revit/bin`. It also fails closed, rather than passing vacuously, if
`src/SolidGround.Revit/bin` does not exist at all: a missing build-output directory means the Build step
did not produce anything for this guard to inspect, which must not read as "no Revit reference assembly
found." This is a defense-in-depth proof, not the primary control: `PrivateAssets="all"`/
`ExcludeAssets="runtime"` should already keep those bytes out of build output; verified directly on this
task's own local Windows build in both modes, where neither pattern ever appears under either
`bin/Release/net10.0-windows/` layout. The "Run offline tests" step is unchanged.
Every one of `AGENTS.md`'s nine Build-and-CI conditions and its never-list stays true: no new trigger, no
new `uses:` action (the allow-list stays exactly `actions/checkout` and `actions/setup-dotnet`), no cache,
no artifact storage, no secret, the job-level guard and first-step identity check untouched, and the job
still runs only on a trusted push to `main` on the existing `self-hosted` lane.

This task's own local build additionally carries one project-scoped, commented suppression not part of the
CI-gate mechanism itself: `<MSBuildWarningsAsMessages>...;MSB3277</MSBuildWarningsAsMessages>`, because
`RevitAPI.dll`/`RevitAPIUI.dll`'s own sibling-DLL dependency closure references `Microsoft.VisualBasic`
`10.1.0.0` where the `net10.0` reference pack carries `10.0.0.0`; MSBuild already resolves this
deterministically (the ref-pack copy wins) and neither this project nor any real call site uses
`Microsoft.VisualBasic`, so the suppression only silences an informational warning. It mirrors the owner's other add-in
add-in's own recorded precedent for the identical conflict and applies only to local-mode
`ResolveAssemblyReferences`; it has no effect on `UseRevitReferenceAssemblies=true` builds, which never
resolve against `RevitInstallDir` at all.

## Deploy script

`scripts/Deploy-RevitAddIn.ps1` implements conventions note section 7 exactly: stage the full managed
dependency closure, hash-verify every file, publish by atomic versioned-folder rename plus atomic manifest
replace, and keep two previous versions for rollback. It targets PowerShell 5.1 and 7 with no
version-specific syntax (`pwsh` was not available to actually exercise on the machine that built this
script; its cross-edition compatibility rests on construction, not an observed `pwsh` run). Full usage and
design detail live in `scripts/README.md`; the summary below is the part this note's own citation
requirements cover.

**Closure derivation.** The script never hand-picks files: it parses `SolidGround.Revit.deps.json` from
`-SourceDirectory`, and for the single build target it declares, collects the `runtime` file names from
every library of type `project` or `package`. Libraries of type `reference` (`RevitAPI`/`RevitAPIUI`,
which Revit itself supplies) contribute nothing, by construction — their expected count in the deployed
folder is always zero. A target library that has a non-empty `runtime` section but whose type cannot be
positively confirmed from the `deps.json` `libraries` section (a missing `libraries` section, a missing
entry for that library, or a missing `type` field) is a fail-closed error naming that library, not a
silent skip, so a malformed or unexpectedly-shaped `deps.json` can never quietly drop a real runtime file
from the closure. Each `runtime` entry's relative path is also normalized (backslash treated the same as
forward slash) and rejected outright if it contains a `..` traversal segment or resolves to a leaf file
name Windows would reject, so only the trusted per-package leaf file name — never a directory-traversal
string — is ever joined onto `-SourceDirectory` or the staging directory. The script also refuses outright,
before touching the filesystem, if any `RevitAPI*.dll` or `Nice3point*.dll` file is present anywhere in
`-SourceDirectory`. Any file the derived closure names but does not find in `-SourceDirectory` fails the
deploy closed, with every missing name listed, before any directory is created or any file copied.

**Publish mechanism.** Files are staged into `<AddinsDirectory>\SolidGround\.staging-<stamp>\`, every
staged file is re-hashed against its source, then published with an atomic directory rename (never a
delete-then-copy) into `<AddinsDirectory>\SolidGround\<stamp>\`. The live manifest at
`<AddinsDirectory>\SolidGround.addin` is rewritten so its `<Assembly>` element points at the absolute path
of the newly deployed `SolidGround.Revit.dll` — XML-escaped via `[System.Security.SecurityElement]::Escape`
before it is spliced into the manifest text, since `-AddinsDirectory` is caller-overridable and can
legitimately contain an XML-significant character such as `&`, and an unescaped path would silently
publish invalid XML that Revit's own manifest loader rejects — written to a temp file, then swapped in
atomically via `[System.IO.File]::Replace` (or a plain move on the very first deploy). Reading the
currently-published path back (`-Verify`, and the prune step's "never delete the folder the live manifest
references" guard) goes through the real XML API rather than a raw-text regex, so the same escaped
character decodes back to the literal path the filesystem uses instead of silently comparing unequal to
it. The stamp format is `yyyyMMdd-HHmmss-<first 8 hex chars of SolidGround.Revit.dll's SHA-256>`. By
default the two previous versioned folders are kept alongside the newest (`-KeepPreviousVersions 2`); the
folder the live manifest currently references is never pruned regardless of age.

**Safety switches.** By default the script refuses to run while *any* `Revit.exe` process is running,
anywhere. `-AllowOtherRevitVersions` narrows that to "refuse only if the running `Revit.exe`'s path is
under `-RevitInstallDir`" — the owner's own stated reason for wanting this is keeping Revit 2026 open for
unrelated work while deploying a Revit 2027 add-in. If a running process's path cannot be read (for
example, access denied), the script always treats it as blocking, fail closed, even under
`-AllowOtherRevitVersions`. `-WhatIf` (via `[CmdletBinding(SupportsShouldProcess)]`) performs no writes.
`-Verify` re-hashes the currently deployed version against `-SourceDirectory` and exits non-zero on any
mismatch or missing file, performing no writes either way. The script never writes to any all-user
(`%ProgramData%`/`Program Files`) location; its only supported target is the per-user
`-AddinsDirectory` (default `%APPDATA%\Autodesk\Revit\Addins\2027`).

**Usage (dev loop).**

```powershell
dotnet build SolidGround.slnx --configuration Release
.\scripts\Deploy-RevitAddIn.ps1                    # deploy; add -AllowOtherRevitVersions if Revit 2026 is open
# fully restart Revit 2027 -- add-ins are not hot-reloaded
.\scripts\Deploy-RevitAddIn.ps1 -Verify            # confirm the deployed bytes match the build output
```

This script has not yet been run against a real per-user Revit Add-Ins folder or a live Revit session in
this task; see "Manual integration test steps this milestone inherits" below.

## Revit 2027 API members used

Every member below was compiled, for this task, against the installed `27.0.10.13`
`RevitAPI.dll`/`RevitAPIUI.dll` at `C:\Program Files\Autodesk\Revit 2027\` — the same installed build
`docs/architecture/revit-2027-verification-and-host-design.md` inspected. "Item N" cites that note's
verification items; "this task" marks a call site, exception type, or member grouping Issue #13's 14 items
did not separately enumerate. The verification note cites Autodesk guide and reference-page *titles*, not
captured URLs, for almost every item (its own method note: the class-reference tree is JavaScript-rendered
and a bare `WebFetch` returns a false 404, so every citation instead names the page). The one exception
below is the URL `AGENTS.md`'s "Authoritative references" section already gives this note's item 4.

| Revit API member | Verified by | Autodesk 2027 reference |
| --- | --- | --- |
| `Autodesk.Revit.UI.IExternalApplication` (`OnStartup`/`OnShutdown`) | Item 13; this task's own installed-sdk probe | — |
| `Autodesk.Revit.UI.UIControlledApplication` | Item 13; this task's probe | — |
| `UIControlledApplication.ControlledApplication` (property) | Item 4; this task's probe | — |
| `Autodesk.Revit.ApplicationServices.ControlledApplication.CurrentUserAddinsLocation` / `.AllUsersAddinsLocation` | Item 4; this task's probe | [Revit 2027 API changes](https://help.autodesk.com/view/RVT/2027/ENU/?guid=f7165618-24c9-4160-a7a4-09979fe4a981) (T12 "Add-in Installation folder changes" table) |
| `UIControlledApplication.CreateRibbonTab(string)` | Item 7; this task's own reading of `RevitAPIUI.xml` for the duplicate-tab exception below | — |
| `UIControlledApplication.CreateRibbonPanel(string, string)` | Item 7 | — |
| `RibbonPanel.AddItem(RibbonItemData)` | Item 7 | — |
| `PushButtonData(string, string, string, string)` constructor | Item 7 | — |
| `PushButtonData.ToolTip` / `.LongDescription` (declared on the `ItemData`/`RibbonItemData` base classes) | This task's own full-inheritance-chain probe; not separately named in item 7's prose | — |
| `PushButtonData.Image` / `.LargeImage` : `System.Windows.Media.ImageSource` (declared on `ButtonData`) | Item 7 | — |
| `Autodesk.Revit.Exceptions.ArgumentException` | This task's own `RevitAPIUI.xml` reading plus a probe of its base-type chain (`Autodesk.Revit.Exceptions.ApplicationException` → `System.Exception`); not one of Issue #13's 14 items | — |
| `Autodesk.Revit.UI.Result` | Items 2, 13 | — |
| `IExternalCommand.Execute(ExternalCommandData, ref string, ElementSet)` | Items 12, 13; this task's own local compile experiment (0 errors) settled the `ref`-vs-`out` question a reflection dump alone left ambiguous | — |
| `Autodesk.Revit.Attributes.TransactionAttribute` / `TransactionMode.Manual` | Item 10 | — |
| `Autodesk.Revit.Attributes.RegenerationAttribute` / `RegenerationOption.Manual` | Item 10 | — |
| `ExternalCommandData.Application` → `UIApplication.ActiveUIDocument` → `UIDocument.Document` | This task's own probe; implied but not separately itemized by Issue #13 | — |
| `Autodesk.Revit.DB.Document.IsFamilyDocument` | Named in the verification note's "Level and type selection rule" section; this task's own probe | — |
| `Document.Title` | This task's own probe, not previously cited | — |
| `Autodesk.Revit.DB.ElementSet` | This task's own probe (namespace confirmation; the parameter is never populated in this milestone) | — |
| `Autodesk.Revit.UI.TaskDialog` constructor(string), `.MainInstruction`, `.MainContent`, `.Show()`, static `.Show(title, content)` | Item 13; this task's own full member-dump probe | — |
| `System.Windows.Media.Imaging.BitmapFrame` / `BitmapCreateOptions` / `BitmapCacheOption` | Item 7's WPF-exposure finding; this task's own successful `FrameworkReference`-only build | — |

## Placeholder icons

`src/SolidGround.Revit/Resources/SolidGround.16.png` (16×16) and `SolidGround.32.png` (32×32) are
placeholders: a simple green terrain-hill silhouette with a darker green parcel outline, generated
deterministically with `System.Drawing` from PowerShell (32-bit ARGB, transparent background). They exist
so this milestone ships a real, working icon-loading path end to end — embedded resources with fixed
`LogicalName`s (`SolidGround.Revit.Resources.SolidGround.{16,32}.png`, literal string constants in
`SolidGroundApplication.cs`, not a `%(Filename)` glob) — per owner decision 5 ("Ship an icon for
`CreateToposolidCommand` in the initial milestone; Issue #19 designs it"). Issue #19 replaces these PNG
bytes; the embedded-resource mechanism, the logical names, and `SolidGroundApplication`'s loading code do
not need to change when it does.

**2026-09-24 (Issue #19).** The final, designed terrain-and-parcel icon pair described above has replaced
these two files' bytes; the embedded-resource mechanism, the logical names, and `SolidGroundApplication`'s
loading code needed no change, as predicted. See `docs/architecture/revit-ribbon-icons.md` for the full
design record, palette, contrast evidence, and manual evidence plan, and
`src/SolidGround.Revit/Resources/README.md` for the shipped files' own short record.

## Manual integration test steps this milestone inherits

Every step below is copied, in summary, from `docs/architecture/revit-2027-verification-and-host-design.md`'s
"Manual integration test plan" (full text and expected observations there). None of them is a gate this
document, or Issue #14 itself, could pass offline: all six require a real, running Revit 2027 session, and
no Revit process was launched to produce this scaffold.

**Manual test harness notes.** Two environment quirks are worth knowing before repeating any of these steps
by hand. First, native Win32 TaskDialog command buttons — both the "Security - Unsigned Add-In" prompt and
any SolidGround Preflight dialog — are not exposed to UI Automation as invokable `ControlType.Button`
elements on this Revit 2027 build; they appear as `ControlType.Pane` elements with no supported patterns, so
an automation approach built only around `InvokePattern` cannot click them, while the WPF-based SolidGround
ribbon tab and button work normally as real `Button` controls with a working `InvokePattern`. Second, a
full-desktop or window-rectangle screen capture (`Graphics.CopyFromScreen`) of one of these dialogs returned
a flat placeholder colour instead of real pixels in this environment; capturing via `PrintWindow` with
full-content rendering against the specific window handle produced a correct image and is the more reliable
method for evidence screenshots.

### Step 1 — First 2027 launch and folder creation

Deploy the manifest and full dependency closure to a machine where
`%APPDATA%\Autodesk\Revit\Addins\2027` does not yet exist and Revit 2027 has never launched under the test
profile; launch Revit 2027 for the first time, accepting the unsigned-add-in prompt if shown. Expected
observation: the "SolidGround" ribbon entry appears; also record whether
`%APPDATA%\Autodesk\Revit\Autodesk Revit 2027\Revit.ini` gets seeded with a `[Misc]` section at all.
Settles verification items 3, 9.

**Evidence (Run 1, 2026-09-21).** A read-only environment audit earlier the same day recorded that the
per-user `%APPDATA%\Autodesk\Revit\Addins\2027` folder, the 2027 journal folder, and the 2027 `Revit.ini`
did not yet exist, and that Revit 2027 had never been launched under this profile.
`scripts/Deploy-RevitAddIn.ps1`'s real deploy run then created the `Addins\2027` leaf, the versioned
payload folder, and the live `SolidGround.addin` manifest, confirmed by the script's own console output and
an immediate directory listing. The first Revit 2027 launch under this profile then created the rest of
the expected 2027 profile tree (including the journal folder the Step 2/4 journal-tail evidence below was
read from) with no separate provisioning step and no error. This settles the folder-creation half of
verification items 3/9; whether `Revit.ini` gets a seeded `[Misc]` section was not separately checked and
stays open.

### Step 2 — Manifest load in the isolated context

With the deployed manifest's `UseRevitContext=False`/`ContextName="SolidGround"` in place, install a second,
differently-named add-in referencing a conflicting version of a same-named dependency under its own
distinct `ContextName`, launch Revit 2027, and log each add-in's resolved dependency version. Repeat with a
manifest omitting `<ManifestSettings>` entirely, and separately with `<ClientId>` in place of `<AddInId>`
on the primary `<AddIn>` element. Settles verification items 1, 11.

**Evidence (Run 1, 2026-09-21).** The single-add-in isolated-context load was confirmed directly from the
Revit 2027 journal. The assembly-resolution line shows the add-in loading into its own named context:

```
The requested assembly 'SolidGround.Revit, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null' was
loaded to the context 'SOLIDGROUND' from
'%APPDATA%\Autodesk\Revit\Addins\2027\SolidGround\20260921-014055-1b5b6341\SolidGround.Revit.dll'.
```

the external application start line:

```
API_SUCCESS { Starting External Application: SolidGround, Class: SolidGround.Revit.SolidGroundApplication,
Vendor : SolidGround(SolidGround), Assembly:
%APPDATA%\Autodesk\Revit\Addins\2027\SolidGround\20260921-014055-1b5b6341\SolidGround.Revit.dll,
Assembly Version: 1.0.0.0 }
```

and the manifest-load line:

```
[Jrn.AddInManifest] Rvt.Attr.AddInManifest: SolidGround.addin Rvt.Attr.AddInType: ExternalApplication
Rvt.Attr.AddInName: SolidGround Rvt.Attr.AddInId: bb4d7576-b0fb-432b-a2c5-15c7ea59147d
Rvt.Attr.CommandClassName: SolidGround.Revit.SolidGroundApplication Rvt.Attr.AddInVersion: 1.0.0.0
Rvt.Attr.CommandVendorId: SolidGround Rvt.Attr.CommandVendorDescription: SolidGround
Rvt.Attr.AddInCodeSigningStatus: Unsigned 1Rvt.Attr.AddInLoadFailureMessage: NoError , 0.018000 , 0 , 1
```

confirming the manifest's `AddInId` matches the pinned GUID exactly, code-signing status loaded as
`Unsigned` with `AddInLoadFailureMessage: NoError`, and no `exception` line anywhere in a focused journal
search. This settles the single-add-in half of verification items 1/11. The deliberate two-add-in
conflicting-dependency scenario (a second, differently named add-in under its own `ContextName` referencing
a conflicting dependency version) was not set up in Run 1 — only the already-installed, unrelated
third-party pyRevit add-in coexisted on the ribbon — so that half of the step stays open.

**Evidence (Run 2, 2026-09-21).** After the rebuild-and-redeploy described in Step 6 below, the journal
again showed the rebuilt assembly loading into the same isolated `'SOLIDGROUND'` context, this time from the
new versioned folder, with the same `AddInId` and `AddInLoadFailureMessage: NoError`. The two-add-in
conflicting-dependency scenario remains unexercised.

### Step 3 — Ribbon appearance and ordering

Build `SolidGround.Revit` once under `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />`
(this scaffold's default) and once under `<UseWPF>true</UseWPF>`; launch Revit 2027 with each build in
turn. Expected observation: the icon renders identically under both settings with no
`FileNotFoundException`/`TypeLoadException`; the 16×16 `Image` renders correctly on the Quick Access
Toolbar; removing or corrupting the icon resource leaves the button visible, text-only, with a warning in
the log. Settles verification item 7 and confirms this scaffold's `FrameworkReference`-only default is
sufficient inside Revit's isolated add-in context.

**Evidence (Run 1, 2026-09-21).** The default `FrameworkReference`-only build (no `UseWPF`) was deployed
and launched. The "SolidGround" ribbon tab appeared in its own position (after the third-party pyRevit tab,
before Modify), with one panel titled "SolidGround" holding the one `CreateToposolidCommand` button. The
placeholder icon rendered correctly — a green terrain-hill glyph above the button — and the button label
rendered as the intended two lines ("Create" / "Toposolid"). No `FileNotFoundException`/`TypeLoadException`
or icon-load warning appeared in the add-in log. This confirms the `FrameworkReference`-only default is
sufficient inside Revit's isolated add-in context, so the `UseWPF=true` fallback was not needed. The
comparison build under `UseWPF=true`, and the Quick Access Toolbar / corrupted-icon-resource sub-cases, were
not exercised in Run 1 and remain open.

### Step 4 — Command invocation without a manifest Command entry

With the manifest's single `Application` entry and no `Command` entry, click the SolidGround ribbon button
and separately open the Add-Ins tab's External Tools pulldown. Expected observation: the command runs
(Preflight, then the success or rejection dialog); no SolidGround entry appears in External Tools. Settles
verification item 12.

**Evidence (Run 1, 2026-09-21).** With the manifest's single `Application` entry and no `Command` entry,
clicking the SolidGround ribbon button ran the command. The journal's ribbon-invocation line:

```
Jrn.RibbonEvent "Execute external command:CustomCtrl_%CustomCtrl_%SolidGround%SolidGround%CreateToposolidCommand:SolidGround.Revit.Commands.CreateToposolidCommand"
```

Preflight then rejected (no `OPENTOPOGRAPHY_API_KEY` was set in this environment) and showed exactly one
Revit dialog, window-titled "SolidGround - SolidGround" with main instruction "SolidGround Preflight found a
problem.", body text beginning "Nothing changed. Correct every problem below and run this command again."
and naming the missing `OPENTOPOGRAPHY_API_KEY` environment variable as the one problem. No other Revit
dialog appeared alongside
it, and no document modification occurred — consistent with the code's `Result.Cancelled` path (no
`Transaction` was ever opened). This settles verification item 12's ribbon-invocation half. The Add-Ins tab
External Tools pulldown was not separately re-checked for a stray SolidGround entry in Run 1.

**Evidence (Run 2, 2026-09-21).** With `OPENTOPOGRAPHY_API_KEY` set to a placeholder value scoped only to
the process that launched Revit 2027 (never persisted to the user or machine environment, and removed from
that process immediately after launch), invoking the ribbon button a second time showed exactly one dialog,
using the same window title Revit gave the Run 1 rejection dialog, "SolidGround - SolidGround", with
verbatim body text:

```
SolidGround Preflight passed.
Project: default
OpenTopography API key: present. (This check never reads, displays, or logs the key's value.)
Default point budget: 15,000 points.
Default output unit: U.S. survey foot.

Creating a toposolid from a parcel or other area of interest arrives with a later SolidGround milestone
(Issue #15). SolidGround is a site-form tool, not a survey instrument, and this check did not modify the
model.
```

consistent with the code's `Result.Succeeded` path, and the journal recorded the matching line
`TaskDialog "SolidGround Preflight passed."`. A literal-string search of both the add-in's trace log and the
active Revit journal for the placeholder key value found zero matches in either file — its presence was
reported without the value ever being written anywhere. Combined with Run 1's rejection-path evidence above,
both outcomes of `CreateToposolidCommand`'s Preflight are now directly observed.

### Step 6 — Unsigned-add-in prompt behavior across two builds

Deploy an unsigned build with the pinned `AddInId` to a profile where Revit 2027 has never launched;
confirm the baseline three-choice unsigned-add-in dialog; sign the DLL and import the certificate into
`LocalMachine\Root`/`LocalMachine\TrustedPublisher`, relaunch, and confirm the dialog no longer appears;
with the unsigned build, click "Always Load," close Revit, rebuild with one trivial byte-level change
(same `AddInId`, same deploy path), redeploy, relaunch, and record whether the dialog reappears. Settles
verification item 14, and the result should be written back into the conventions note's item 14 entry once
observed.

**Evidence.** Run 1 (2026-09-21) confirmed the baseline case: on first launch under a profile where Revit
2027 had never run, the "Security - Unsigned Add-In" dialog appeared naming SolidGround, publisher "Unknown
Publisher", pointing at the deployed `SolidGround.Revit.dll` path, and "Always Load" was chosen, per owner
decision 3 (Issue #12, 2026-09-20). Run 1 also confirmed the add-in's own logging around this event: the log
directory under `%ProgramData%\SolidGround\Revit\Logs\` was created machine-wide as designed, and the trace
log recorded `CurrentUserAddinsLocation` and `AllUsersAddinsLocation` exactly as read from the live API at
runtime — `%APPDATA%\Autodesk\Revit\Addins\2027` and `C:\Program Files\Autodesk\Revit\Addins\2027`
respectively — with no API key value appearing anywhere in the log.

Run 2 (2026-09-21, immediately after Run 1) answered the rebuild half of this step. A one-line rebuild was
redeployed to a new versioned folder (`20260921-015905-d32dd639`, `SolidGround.Revit.dll` SHA-256
`D32DD639AA67ADDCEF76B6F98CE416025562E69A2DB9BFA98B4872D3F8300BFD`) under the same pinned `AddInId`. The
"Security - Unsigned Add-In" prompt reappeared: verbatim identical to Run 1's dialog text except for the
updated `Location:` (the new versioned folder) and `Date:` lines; "Always Load" was chosen again. Trust
granted to the first build's file location therefore did not carry over to the rebuilt file at its new path,
even though the `AddInId`, vendor, and class name were all unchanged — confirming the owner's other add-in's own
already-observed finding and contradicting a same-location-plus-`AddInId` persistence assumption this note
had otherwise left open: Revit's unsigned-add-in trust is keyed to the assembly's file location (or file
identity), not the `AddInId` alone. Because `scripts/Deploy-RevitAddIn.ps1` deploys every build to a new
versioned folder by design, the practical dev-loop consequence is that every `dotnet build` + redeploy cycle
re-prompts in Revit, not just the first one, until Issue #17 revisits signing. The signed-certificate half
of this step (importing a code-signing certificate into `LocalMachine\Root`/`LocalMachine\TrustedPublisher`
and confirming the prompt no longer appears) remains unexercised — no signing exists in this milestone.

### Step 10 — Hash verification after deploy

With `SolidGround.Revit` built (`CopyLocalLockFileAssemblies=true`) and deployed by
`scripts/Deploy-RevitAddIn.ps1`, confirm `NetTopologySuite.dll`/`ProjNET.dll` sit physically beside
`SolidGround.Revit.dll`/`SolidGround.Core.dll` in the deployed folder; run the command far enough to
exercise `SolidGround.Core` code using those packages; then, as a negative control, remove only those two
DLLs from the deployed folder and re-run. Expected observation: the positive run succeeds with no
`FileNotFoundException`; the negative control fails with a missing-assembly error, confirming Revit's
isolated loader needs the physical co-located copies. Settles verification item 5.

**Evidence (Run 1, 2026-09-21).** `Deploy-RevitAddIn.ps1 -Verify` passed for all 6 deployed files both
immediately after the deploy (before Revit 2027 was launched) and again after Revit 2027 was closed, with
identical hashes both times. The published version folder's stamp was `20260921-014055-1b5b6341`, and
`SolidGround.Revit.dll`'s SHA-256 was
`1B5B6341C1D07EF12CC1E7E03AD605FA36005029AE13BB78912EF017C5D06D7C` — the same value independently reported
by the deploy script's verify table, the deployed manifest's assembly path, and the add-in's own trace-log
build-identity line, all four agreeing. `NetTopologySuite.dll`/`ProjNET.dll` were confirmed physically
co-located beside `SolidGround.Revit.dll`/`SolidGround.Core.dll` in the deployed folder, and the command ran
far enough (through Preflight, which reads `SolidGround.Core` types) to exercise that dependency closure
with no `FileNotFoundException`. This settles the positive-run half of verification item 5. The negative
control — removing `NetTopologySuite.dll`/`ProjNET.dll` and re-running to confirm a missing-assembly
failure — was not exercised in Run 1 and stays open.

**Evidence (Run 2, 2026-09-21).** The second deploy (the Step 6 rebuild) retained the first version folder
(`20260921-014055-1b5b6341`) alongside the new one (`20260921-015905-d32dd639`) as designed rollback
retention, and rewrote the live manifest's `<Assembly>` element to point at the new folder's
`SolidGround.Revit.dll`. `Deploy-RevitAddIn.ps1 -Verify` passed for all 6 files again after Revit 2027
closed, this time against the new build's hashes, and the add-in's trace log recorded the new build
identity SHA-256 (`D32DD639AA67ADDCEF76B6F98CE416025562E69A2DB9BFA98B4872D3F8300BFD`), matching the deployed
DLL exactly — the same three-way agreement (file hash, deploy `-Verify`, trace log) observed in Run 1. The
negative-control DLL removal still was not exercised.

## Known limitations and follow-ups

- The CI compile gate was verified by building both MSBuild modes (`UseRevitReferenceAssemblies` true and
  false) on this Windows development machine; it has not yet been exercised on the real
  `self-hosted` Linux self-hosted runner. The `EnableWindowsTargeting`-driven cross-compile path
  mirrors the owner's other add-in's own already-working mechanism closely, but that is not the same as a real run.
  `.github/workflows/ci.yml`'s next trusted push to `main` is the first real exercise of this gate.
- `scripts/Deploy-RevitAddIn.ps1`'s PowerShell 7 compatibility rests on avoiding version-specific syntax,
  not on an observed `pwsh` run: `pwsh` is not installed anywhere on the machine that built and tested it.
- The deploy script has now been run twice against a live per-user Revit Add-Ins folder and a running Revit
  2027 session (Run 1 and Run 2, both 2026-09-21); see "Manual integration test steps this milestone
  inherits" above. All six inherited steps now carry at least partial evidence. Step 6's rebuild half is
  settled (the unsigned-add-in prompt reappears on every new versioned-folder deploy, even with the same
  `AddInId`); its signed-certificate half stays open, along with a handful of other steps' negative-control
  or sub-case remainders noted inline above.
- `SolidGround.Revit.csproj` carries one project-scoped, commented `MSBuildWarningsAsMessages` entry for
  `MSB3277` (see "CI compile gate" above), mirroring an identical, already-accepted the owner's other add-in precedent; it
  affects only local-mode `ResolveAssemblyReferences` output, not CI mode or any Roslyn/CA diagnostic.
- Per `AGENTS.md`'s "Mission and current boundary," this milestone intentionally ships no toposolid
  creation, no Extensible Storage, and no settings file; nothing in this note should be read as having
  started Issues #15, #16, or a settings-file need.

## What this note does not do

This note records what Issue #14 already built; it does not itself scaffold anything further, and it
changes no decision recorded in `docs/architecture/revit-add-in-conventions.md` or
`docs/architecture/revit-2027-verification-and-host-design.md`. It does not perform the manual integration
test plan: every "Evidence" entry above is a placeholder for the orchestrator to fill in after a real
Revit 2027 session, not a claim this note makes itself. It does not begin Issue #15 (toposolid creation),
Issue #16 (Extensible Storage provenance), or Issue #19 (the real ribbon icon design); those stay explicit,
separately approved implementation tasks per `AGENTS.md`'s "Mission and current boundary" section.
