# scripts/

Operational scripts for SolidGround: deploying, signing, packaging, installing, and uninstalling the
`SolidGround.Revit` add-in. Nothing here is invoked by CI; these are local developer/operator-machine tools
only. Every script targets both Windows PowerShell 5.1 and PowerShell 7 (no version-specific language
features), matching `Deploy-RevitAddIn.ps1`'s own established convention.

| Script | Purpose |
| --- | --- |
| [`Deploy-RevitAddIn.ps1`](#deploy-revitaddinps1) | Deploys a local build to your per-user Revit 2027 Add-Ins folder. |
| [`Sign-RevitAddIn.ps1`](#sign-revitaddinps1) | Mints the SolidGround signing certificate (once), or signs one or more files with it. |
| [`Import-SigningTrust.ps1`](#import-signingtrustps1) | One-time, elevated, per-workstation import of the signing certificate into the trusted-root/trusted-publisher stores. |
| [`New-ReleasePackage.ps1`](#new-releasepackageps1) | Builds, signs, and stages a versioned release zip under `artifacts/release/`. |
| [`Install-SolidGround.ps1`](#install-solidgroundps1) | Installs SolidGround from an extracted release zip (see also [`install.cmd`](#installcmd)). |
| [`install.cmd`](#installcmd) | The double-click entry point for `Install-SolidGround.ps1`, shipped inside every release zip. |
| [`Uninstall-SolidGround.ps1`](#uninstall-solidgroundps1) | Removes the SolidGround manifest and every retained versioned deployment folder. |

See [`docs/architecture/revit-release-packaging-and-signing.md`](../docs/architecture/revit-release-packaging-and-signing.md)
for the full design behind the signing, packaging, and install/uninstall scripts, and
[`docs/revit-install-guide.md`](../docs/revit-install-guide.md) for the operator-facing install walkthrough.

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

1a. **Optional**: sign the freshly built DLLs before deploying, so Revit stops re-prompting for this build
    once you have imported trust once (see [`Sign-RevitAddIn.ps1`](#sign-revitaddinps1) and
    [`Import-SigningTrust.ps1`](#import-signingtrustps1) below):

   ```powershell
   .\scripts\Sign-RevitAddIn.ps1 -Path .\src\SolidGround.Revit\bin\Release\net10.0-windows\SolidGround.Revit.dll, .\src\SolidGround.Revit\bin\Release\net10.0-windows\SolidGround.Core.dll
   ```

   `Deploy-RevitAddIn.ps1` itself is never modified to require this step — skip it and you keep seeing
   today's unsigned-add-in prompt, exactly as before this issue.

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
and does not write anything under `%ProgramData%` or `Program Files`. It
only reads `-SourceDirectory`; it never modifies anything under `src/`. It
does not perform code signing itself — see [`Sign-RevitAddIn.ps1`](#sign-revitaddinps1)
below for the separate, optional, prior step that does, adopted for Issue #17.
This script is never modified to support signing, packaging, or install/
uninstall (Issue #17 ruling R10); every script below either reuses its
`-WhatIf` validation or forwards to it unchanged.

## Sign-RevitAddIn.ps1

Mints the SolidGround Revit add-in code-signing certificate (once), or signs one or more files with it —
implements `AGENTS.md`'s Issue #17 signing decision (self-signed, non-exportable `Cert:\CurrentUser\My` key,
PowerShell-native, $0) and
[`docs/architecture/revit-release-packaging-and-signing.md`](../docs/architecture/revit-release-packaging-and-signing.md)
section "Code signing". Run `Get-Help .\Sign-RevitAddIn.ps1 -Full` for the complete parameter and example
reference.

Two mutually exclusive modes:

- **`-NewCertificate`** mints the certificate in `Cert:\CurrentUser\My` and writes the committed, non-secret
  pin file every other signing/packaging/install script reads as its one source of truth for the
  certificate's identity (`scripts/signing-certificate.json` by default; Issue #17 ruling R12). Refuses if
  the pin file already exists or a certificate with the same subject already exists — rotation is a
  documented manual procedure, not something this switch does.
- **Default (sign) mode** reads the pin file, finds the matching certificate, and signs every file named by
  `-Path` with `Set-AuthenticodeSignature -HashAlgorithm SHA256` (explicit, since Windows PowerShell 5.1's
  own default is SHA1), then independently re-verifies each signed file against the pinned certificate
  identity.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-Path` | — | One or more files to sign. Required unless `-NewCertificate`. Sign a staged copy of a `.ps1` outside the working tree — never the committed source under `scripts/` itself. |
| `-NewCertificate` | off | Mints the certificate and writes `-PinFilePath`. Mutually exclusive with `-Path`. |
| `-RequireTimestamp` | off | Makes a failed or missing Authenticode timestamp a fail-closed error instead of a best-effort warning. Pass this for release packaging; omit it for ordinary dev-loop signing. |
| `-PinFilePath` | `signing-certificate.json` next to this script | The committed pin file's location. |
| `-TimestampServer` | `http://timestamp.digicert.com` | Must answer the legacy Authenticode/PKCS#7 request shape `Set-AuthenticodeSignature` actually drives — never RFC 3161. |
| `-WhatIf` / `-Confirm` | — | `SupportsShouldProcess`, `ConfirmImpact 'Medium'`. `-WhatIf` reports which certificate would be minted or which file(s) would be signed, without writing anything. An ordinary call with neither switch proceeds without prompting (`Medium` is below the default `High` confirmation threshold), matching this script's existing non-interactive callers such as `New-ReleasePackage.ps1`. |

Never creates a `.pfx` file, never asks for or stores a password, and never exports the private key: the key
is minted `NonExportable` and stays in the Windows-managed CNG key store for this Windows profile only.

### Not in scope

Does not perform trust import (see [`Import-SigningTrust.ps1`](#import-signingtrustps1) below) and does not
build, package, or deploy anything.

## Import-SigningTrust.ps1

One-time, per-workstation, elevated import of the SolidGround signing certificate into
`Cert:\LocalMachine\Root` and `Cert:\LocalMachine\TrustedPublisher` — the one mechanism this project's own
research has found durably suppresses Revit's unsigned/untrusted-publisher add-in prompt for a SolidGround
build. See
[`docs/architecture/revit-release-packaging-and-signing.md`](../docs/architecture/revit-release-packaging-and-signing.md)
section "Code signing" for what this actually grants and the documented, lower-trust alternative of skipping
it. Run `Get-Help .\Import-SigningTrust.ps1 -Full` for the complete parameter and example reference.

Requires an already-elevated (Run as Administrator) PowerShell session and fails closed with a clear message
if not elevated, rather than silently self-elevating. Reads the signing certificate's identity from an
already-signed `-DllPath`, cross-checks it against the pinned value in `-PinFilePath`, then adds it to both
stores idempotently (a store already containing the certificate is left alone, not an error).

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-DllPath` | Sibling `..\payload\SolidGround.Revit.dll` inside an extracted zip, else the local dev build output | The already-signed file to read the certificate from. |
| `-PinFilePath` | `signing-certificate.json` next to this script | The pinned certificate identity to cross-check against. |
| `-Force` | off | Overrides only a signer-hash mismatch (never a missing signature). Prints a loud warning when used. |
| `-Configuration` | `Release` | Used only to compute `-DllPath`'s local-dev-build fallback default. |
| `-WhatIf` / `-Confirm` | — | `SupportsShouldProcess`, `ConfirmImpact 'High'`. Read-only checks (elevation, signature, pin cross-check) still run under `-WhatIf` and can still report a problem. |

**Non-interactive/automated invocation** (for example, a redirected console): pass `-Confirm:$false`.
Without it, `$PSCmdlet.ShouldProcess()` cannot show its own interactive confirmation prompt and throws; this
script converts that into a clear, actionable error rather than a bare exception, but running with
`-Confirm:$false` avoids hitting it at all — the elevation, signature, and pin cross-checks above still run
and can still fail closed either way.

Removal is a documented manual two-line reverse (`Remove-Item Cert:\LocalMachine\Root\<thumbprint>`, same
under `TrustedPublisher`), not a switch this script implements.

### Not in scope

Never self-elevates. Never signs anything itself (see [`Sign-RevitAddIn.ps1`](#sign-revitaddinps1) above).

## New-ReleasePackage.ps1

Builds, signs, and stages a versioned `SolidGround.Revit` release zip under `artifacts/release/` (a
subfolder of the pre-existing, already git-ignored `artifacts/` bucket). Implements
[`docs/architecture/revit-release-packaging-and-signing.md`](../docs/architecture/revit-release-packaging-and-signing.md)
section "Release package" — see that note for the full, numbered, fail-closed precondition list, in the
script's own actual execution order (cheap file-existence checks first, then the working tree and its push
status, then restore/build/test, then checks against the real build output, with signing last). Run
`Get-Help .\New-ReleasePackage.ps1 -Full` for the complete parameter and example reference.

**Does not require closing Revit.** Unlike `Deploy-RevitAddIn.ps1` and `Install-SolidGround.ps1`, packaging
never touches a real Add-Ins folder, so it never refuses just because a real Revit session happens to be
open on the packaging machine (it reuses `Deploy-RevitAddIn.ps1`'s own validation against a disposable temp
directory instead, with that script's running-Revit refusal deliberately neutralized for that one call — see
the design note's precondition 8).

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-Configuration` | `Release` | Build configuration to package. |
| `-OutputDirectory` | `<repo>\artifacts\release` | Where the zip and its sibling `.sha256` file are written. |
| `-Version` | Read from `Directory.Build.props` | Override only for a local dry run; a real release always uses the committed value. |
| `-Sign` | on | `-Sign:$false` produces an explicitly `UNSIGNED-DRY-RUN`-named artifact for local iteration; never use this for a real release. |
| `-SkipTests` | off | Skips the offline test suite precondition. Local iteration only. |
| `-SkipCleanTreeCheck` | off | Skips the clean-working-tree precondition. Local iteration only. |
| `-WhatIf` / `-Confirm` | — | `SupportsShouldProcess`, `ConfirmImpact 'Medium'`. Preconditions 1-7 (cheap checks, git status, restore/build/test) still run for real under `-WhatIf`; preconditions 8-10 (`Deploy-RevitAddIn.ps1`'s own validation, the native-binary check, and signing) are interleaved with this script's actual writes closely enough that they are gated together with staging/zipping, not run separately. An ordinary call with neither switch proceeds without prompting. |

Reuses `Deploy-RevitAddIn.ps1`'s own `-WhatIf` validation and `Sign-RevitAddIn.ps1` rather than reimplementing
either's logic; never passes `-p:UseRevitReferenceAssemblies=true` (CI-only) to `dotnet restore`/`build`.

**Always** passes `-p:ContinuousIntegrationBuild=true` to its `dotnet restore`/`build`/`test` calls (Issue #17
dry-run defect fix). `Directory.Build.props` only turns `<ContinuousIntegrationBuild>` on automatically under
CI (`'$(CI)' == 'true'`); without this explicit flag, a local packaging run's own `dotnet build` embeds the
packaging operator's real machine path — including their Windows account name — into
`SolidGround.Revit.dll`/`.pdb` and `SolidGround.Core.dll`/`.pdb`, instead of mapping it to `/_/` the way
`Deterministic=true` (already unconditional repository-wide) is meant to. This was found by the Issue #17
release dry run inspecting the shipped zip's own binaries.

As a fail-closed backstop for that flag — not a substitute for it — the script also scans every staged file
(after `install\`/`payload\`/root-level staging, before signing or zipping) for the build machine's own
`$env:USERPROFILE`, the repository's absolute root path, and `\Users\<username>`, decoded both as UTF-8/ASCII
and UTF-16LE, case-insensitively. Packaging refuses, naming the offending file, if any staged file still
contains one — whether from a future SDK behavior change, a new staged file type, or a build run without the
flag above.

### Not in scope

Does not publish a GitHub Release itself (a separate, `AC6`-gated, orchestrator-run step — see
[`docs/architecture/revit-release-packaging-and-signing.md`](../docs/architecture/revit-release-packaging-and-signing.md)
section "GitHub Release"). Never runs under the CI-only `UseRevitReferenceAssemblies` condition.

## Install-SolidGround.ps1

A short, parameter-less wrapper for installing SolidGround from an extracted release zip: unblocks every
extracted file, checks every signed file's Authenticode signature against the pinned certificate hash
(failing closed before anything deploys), prints an informational-only trust-status message, then forwards
every argument you pass straight through to the sibling `install\Deploy-RevitAddIn.ps1`. Run from inside an
extracted zip's `install\` folder — see [`install.cmd`](#installcmd) below for the double-click alternative,
and [`docs/revit-install-guide.md`](../docs/revit-install-guide.md) for the full operator walkthrough.

Takes no parameters of its own; any switch or parameter `Deploy-RevitAddIn.ps1` accepts (for example
`-AddinsDirectory`, `-WhatIf`, `-AllowOtherRevitVersions`, `-KeepPreviousVersions`) reaches it unchanged.

### Not in scope

Never modifies `Deploy-RevitAddIn.ps1`; always forwards to it. Never self-elevates and never runs
`Import-SigningTrust.ps1` on your behalf — it only prints that command as a next step.

## install.cmd

The double-click entry point for [`Install-SolidGround.ps1`](#install-solidgroundps1) — a bare `.ps1` opens
in an editor rather than running when double-clicked, which does not work for a non-developer extracting a
release zip. Forwards every argument to the sibling `Install-SolidGround.ps1` with a process-scoped
`-ExecutionPolicy Bypass` (this invocation only; never changes your machine's persistent execution policy,
and needs no administrator rights), then captures PowerShell's own exit code and pauses before the window
closes, so a non-developer who double-clicked this file can actually read the result — or an error — instead
of watching the window vanish immediately:

```bat
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SolidGround.ps1" %*
set "SOLIDGROUND_INSTALL_EXITCODE=%ERRORLEVEL%"
echo.
pause
exit /b %SOLIDGROUND_INSTALL_EXITCODE%
```

An automated/non-interactive run should redirect stdin from `NUL` so `pause` returns immediately instead of
waiting for a keypress: `cmd /c install.cmd < NUL`.

`New-ReleasePackage.ps1` copies this file into the release zip's `install\` folder alongside
`Install-SolidGround.ps1`, since `%~dp0` only resolves correctly when both files sit in the same folder at
run time. The installer guide (`INSTALL.md` in the zip, authored from `docs/revit-install-guide.md`) ships one
level up, at the zip's own root, not inside `install\`.

### Not in scope

Does nothing itself beyond forwarding; see [`Install-SolidGround.ps1`](#install-solidgroundps1) above.

## Uninstall-SolidGround.ps1

Removes the live `SolidGround.addin` manifest and the entire versioned `SolidGround\` deployment folder tree
from a per-user Revit 2027 Add-Ins folder. Deliberately a new, small, self-contained script rather than a
`-Uninstall` switch on the already-proven `Deploy-RevitAddIn.ps1` (Issue #17 ruling R10:
`Deploy-RevitAddIn.ps1` itself is never modified). Run `Get-Help .\Uninstall-SolidGround.ps1 -Full` for the
complete parameter and example reference.

Refuses outright while any `Revit.exe` process is running, of any version — a simpler, always-refuse check
than `Deploy-RevitAddIn.ps1`'s own `-AllowOtherRevitVersions` path-matching nuance, matching the safer
default a destructive operation should have.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-AddinsDirectory` | `%APPDATA%\Autodesk\Revit\Addins\2027` | Same default as `Deploy-RevitAddIn.ps1`. Never point this at an all-user path. |
| `-RemoveSettingsAndLogs` | off | Also removes `%ProgramData%\SolidGround\Revit\settings.json` and its `Logs\` folder. |
| `-Force` | off | Suppresses this destructive operation's interactive confirmation prompt (`ConfirmImpact 'High'`). |
| `-WhatIf` / `-Confirm` | — | `SupportsShouldProcess`. |

**Always leaves, regardless of any switch**: the `HKCU:\...\CodeSigning` registry value Revit itself writes
for the add-in's `AddInId` — that is Revit's own trust record, not something an uninstaller should clear as a
side effect. Prints exactly what was removed and what was deliberately left, by path. Running it when there
is nothing to remove is not an error: it reports that plainly and exits successfully, so
uninstall-then-reinstall cycles stay idempotent.

### Not in scope

Never removes certificate trust (see [`Import-SigningTrust.ps1`](#import-signingtrustps1) above for the
separate, manual, documented removal procedure). Never modifies `Deploy-RevitAddIn.ps1`.
