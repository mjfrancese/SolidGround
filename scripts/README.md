# scripts/

Operational scripts for SolidGround. This directory currently holds one
script: the Revit add-in deploy tool. Nothing here is invoked by CI; these
are local developer-machine tools only.

## Deploy-RevitAddIn.ps1

Deploys a locally built `SolidGround.Revit` add-in to a per-user Revit 2027
Add-Ins folder, following the deployment mechanism required by `AGENTS.md`'s
"Revit add-in conventions" section and
`docs/architecture/revit-add-in-conventions.md` section 7: stage the full
managed dependency closure, hash-verify every file, publish by an atomic
versioned-folder rename plus an atomic manifest replace, and retain a bounded
number of previous versions for rollback. It never writes to any all-user
(`%ProgramData%` or `Program Files`) add-in location; the only supported
target is the per-user Revit Add-Ins folder.

Run `Get-Help .\Deploy-RevitAddIn.ps1 -Full` for the complete parameter and
example reference (comment-based help lives in the script itself). PowerShell
5.1 (`powershell.exe`) and PowerShell 7 (`pwsh`) are both supported; the
script uses no version-specific language features.

### Dev loop

1. Build the add-in:

   ```powershell
   dotnet build SolidGround.slnx --configuration Release
   ```

2. Deploy the build output to your per-user Revit 2027 Add-Ins folder:

   ```powershell
   .\scripts\Deploy-RevitAddIn.ps1
   ```

   By default this refuses if *any* `Revit.exe` is running. If you
   intentionally keep a different Revit version open for other work (for
   example Revit 2026), pass `-AllowOtherRevitVersions`; the script then
   refuses only when a Revit.exe whose path is under `-RevitInstallDir`
   (default the installed Revit 2027) is running. It never launches, kills,
   or otherwise touches a Revit process itself.

3. Fully restart Revit 2027. Add-ins are loaded once at startup and are not
   hot-reloaded; a running session will not see a new deployment.

4. Confirm the deployed bytes actually match what you built:

   ```powershell
   .\scripts\Deploy-RevitAddIn.ps1 -Verify
   ```

   This re-hashes the version the live manifest currently references
   against `-SourceDirectory` and prints a table of every file with its
   status (`OK`, `Mismatch`, `MissingSource`, `MissingDeployed`). It exits
   with a non-zero code if anything other than `OK` appears, and performs no
   writes either way.

### What gets deployed

The script never hand-picks files. It parses
`SolidGround.Revit.deps.json` from `-SourceDirectory` and, for the single
build target it declares, collects the runtime file names from every
library of type `project` or `package`. Libraries of type `reference`
(`RevitAPI`/`RevitAPIUI`, which Revit itself supplies) are excluded from the
closure by construction and must never appear in the deployed folder. A
library that has runtime files but whose type cannot be positively confirmed
from `deps.json`'s `libraries` section (missing section, missing entry, or
missing `type` field) makes the script throw, naming that library, instead of
silently dropping its files — the same fail-closed posture applies to the
per-file relative path itself, which is rejected if it contains a `..`
traversal segment or an unsafe file name after normalizing backslash and
forward slash separators. The script additionally refuses outright, before
touching the filesystem, if any `RevitAPI*.dll` or `Nice3point*.dll` file is
present anywhere in `-SourceDirectory` — those belong only to the CI compile
gate (`UseRevitReferenceAssemblies`, see `AGENTS.md`'s "Build and CI" section)
and must never ship. The manifest template (`SolidGround.addin`) and the
`deps.json` file itself are always added to the deployed set, and any `.pdb`
sitting next to a `.dll` already in the closure is copied best-effort (a
missing `.pdb` is not an error).

If any required file is missing from `-SourceDirectory`, the script fails
closed — with every missing file name listed — before it creates any
directory or copies anything.

### Where it deploys

| Item | Value |
| --- | --- |
| Default per-user target | `%APPDATA%\Autodesk\Revit\Addins\2027` (`-AddinsDirectory`) |
| Manifest | `<AddinsDirectory>\SolidGround.addin` |
| Versioned payloads | `<AddinsDirectory>\SolidGround\<stamp>\` |
| Stamp format | `yyyyMMdd-HHmmss-<first 8 hex chars of SolidGround.Revit.dll's SHA-256>` |

Each deploy stages files into `<AddinsDirectory>\SolidGround\.staging-<stamp>`,
re-hashes every staged file against the source before trusting it, then
publishes with `[System.IO.Directory]::Move` (an atomic rename on the same
volume, never a delete-then-copy). The manifest is rewritten so its
`<Assembly>` element points at the absolute path of the newly deployed DLL —
XML-escaped first, since `-AddinsDirectory` can contain a character like `&`
that would otherwise publish invalid XML — written to a temp file beside the
live manifest, then swapped in atomically with `[System.IO.File]::Replace`
(or `[System.IO.File]::Move` on the very first deploy, when no manifest
exists yet). `-Verify` and the retention logic below read that path back
through the real XML API, not a raw-text regex, so it decodes correctly
instead of comparing unequal to the real folder on disk.

By default the two previous versioned folders are kept alongside the newest
one (`-KeepPreviousVersions 2`, three folders total); older folders are
pruned automatically on the next successful deploy. The folder the live
manifest currently references is never pruned, even if it would otherwise
fall outside that window. Rollback is manual: to fall back to an older
build, edit `<Assembly>` in `SolidGround.addin` to point at one of the
retained `<AddinsDirectory>\SolidGround\<stamp>\SolidGround.Revit.dll`
folders (the deploy script does not automate this).

### Safety switches

- **Running-Revit refusal.** By default the script refuses to run at all
  while any `Revit.exe` process is running, regardless of which version.
  `-AllowOtherRevitVersions` narrows that to "refuse only if the running
  Revit.exe's path is under `-RevitInstallDir`". If a running process's path
  cannot be determined (for example, access denied), the script treats it as
  blocking either way, since it cannot prove the process is a different
  version.
- **`-WhatIf`.** The script supports `-WhatIf`/`-Confirm` via
  `SupportsShouldProcess`. `-WhatIf` performs no writes: no directory is
  created, no file is copied, no manifest is written, and nothing is pruned.
  Read-only validation (dependency-closure parsing, missing-file and
  forbidden-file checks) still runs and can still report a problem, since
  those are not writes.
- **`-Verify`.** Read-only. Does not deploy, does not require Revit to be
  stopped.

### Parameters

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-Configuration` | `Release` | Used only to compute the default `-SourceDirectory`. |
| `-SourceDirectory` | `<repo>\src\SolidGround.Revit\bin\<Configuration>\net10.0-windows` | Build output to deploy from. |
| `-AddinsDirectory` | `%APPDATA%\Autodesk\Revit\Addins\2027` | Per-user deploy target. Never point this at an all-user path. |
| `-RevitInstallDir` | `C:\Program Files\Autodesk\Revit 2027` | Used only to recognize a Revit 2027 process for the running-Revit check. |
| `-AllowOtherRevitVersions` | off | Narrows the running-Revit refusal (see above). |
| `-KeepPreviousVersions` | `2` | Previous versioned folders retained in addition to the newest. |
| `-Verify` | off | Check mode instead of deploy mode (see above). |

### Not in scope

This script does not build the project, does not install or launch Revit,
does not write anything under `%ProgramData%` or `Program Files`, and does
not perform code signing (accepted as an open item for Issue #17 per
`AGENTS.md`). It only reads `-SourceDirectory`; it never modifies anything
under `src/`.
