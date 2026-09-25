<#
.SYNOPSIS
    Removes the SolidGround Revit add-in manifest and all versioned deployment folders from a
    per-user Revit 2027 Add-Ins folder.

.DESCRIPTION
    Uninstall-SolidGround.ps1 implements docs/architecture/revit-release-packaging-and-signing.md
    section 5, "Uninstall": a new, small, deliberately self-contained script rather than a
    -Uninstall switch bolted onto Deploy-RevitAddIn.ps1 -- mixing "publish" and "remove" into that
    already-proven, already-evidenced script would raise the risk of a mistake reaching its existing,
    working behavior. Deploy-RevitAddIn.ps1 itself is never modified by this script or in support of
    it.

    Removes the live SolidGround.addin manifest and the entire versioned SolidGround\ folder tree
    under -AddinsDirectory (every retained version, not only the live one).

    Leaves by default: %ProgramData%\SolidGround\Revit\ (settings.json and Logs\) -- useful
    diagnostic history, and so an operator who reinstalls later does not lose their configured area
    of interest. Pass -RemoveSettingsAndLogs to remove that state too.

    Always leaves, regardless of any switch: the HKCU:\...\CodeSigning registry trust value Revit
    itself writes for the add-in's AddInId. That is Revit's own trust record, not something an
    add-in uninstaller should clear as a side effect; removing or rotating trust is
    Import-SigningTrust.ps1's own, separately-invoked job.

    Refuses outright while a Revit.exe process under -RevitInstallDir (Revit 2027 by default) is
    running -- always, regardless of -AllowOtherRevitVersions, since that is the exact version this
    uninstaller's own per-user Addins\2027 folder belongs to. By default (no -AllowOtherRevitVersions)
    it also refuses while any *other* Revit.exe process runs, anywhere -- the original, simpler,
    always-refuse default. Pass -AllowOtherRevitVersions to tolerate a different Revit version kept
    open for unrelated work (for example Revit 2026), matching Deploy-RevitAddIn.ps1's own switch of
    the same name and identical path-matching semantics (Issue #17 follow-up: this uninstaller
    originally had no such override, which meant a user who simply kept an older Revit version open --
    common -- could not remove the Revit 2027 add-in even though this script only ever touches the
    2027 per-user Addins folder).

    Prints exactly what was removed and what was deliberately left, by path -- no silent surprises.

    Running this script when there is nothing to remove (for example, immediately after a previous
    run) is not an error: it reports that plainly and exits successfully, so uninstall-then-reinstall
    cycles stay idempotent.

.PARAMETER AddinsDirectory
    The per-user Revit 2027 Add-Ins folder to remove SolidGround from. Default:
    $env:APPDATA\Autodesk\Revit\Addins\2027 (matching Deploy-RevitAddIn.ps1's own default). Never
    point this at an all-user location.

.PARAMETER RevitInstallDir
    The Revit 2027 install directory used to recognize a running Revit 2027 process, which always
    blocks uninstall regardless of -AllowOtherRevitVersions. Default:
    C:\Program Files\Autodesk\Revit 2027 (matching Deploy-RevitAddIn.ps1's own default).

.PARAMETER AllowOtherRevitVersions
    Relaxes the running-Revit refusal so only a Revit.exe whose path is under -RevitInstallDir blocks
    uninstall, instead of any running Revit.exe. Use this when a different Revit version is
    intentionally kept open for other work. Matches Deploy-RevitAddIn.ps1's own switch of the same
    name and identical semantics, including its "path unavailable" fail-closed behavior.

.PARAMETER RemoveSettingsAndLogs
    Also removes %ProgramData%\SolidGround\Revit\settings.json and its Logs\ folder. Off by default.
    Never removes the HKCU:\...\CodeSigning registry value regardless of this switch.

.PARAMETER Force
    Suppresses this destructive operation's interactive confirmation prompt (this script declares
    ConfirmImpact 'High'). Equivalent to answering "Yes to All" at every prompt this run would
    otherwise show.

.EXAMPLE
    .\Uninstall-SolidGround.ps1
    Removes the manifest and versioned folder tree from the default per-user Add-Ins folder,
    prompting for confirmation once (ConfirmImpact 'High'). Leaves settings and logs.

.EXAMPLE
    .\Uninstall-SolidGround.ps1 -Force -RemoveSettingsAndLogs
    Removes everything SolidGround owns, including settings and logs, without prompting.

.EXAMPLE
    .\Uninstall-SolidGround.ps1 -WhatIf -AddinsDirectory 'C:\Scratch\Addins\2027'
    Reports what would be removed from a scratch Add-Ins folder without writing anything.

.EXAMPLE
    .\Uninstall-SolidGround.ps1 -AllowOtherRevitVersions
    Uninstalls even though a different Revit version (for example Revit 2026) is open, refusing only
    if Revit 2027 itself is running.

.NOTES
    Follows docs/architecture/revit-release-packaging-and-signing.md section 5. Owned entirely under
    scripts/; see scripts/README.md.
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$AddinsDirectory = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'),

    [string]$RevitInstallDir = 'C:\Program Files\Autodesk\Revit 2027',

    [switch]$AllowOtherRevitVersions,

    [switch]$RemoveSettingsAndLogs,

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# See Deploy-RevitAddIn.ps1's own identical comment: a script that declares SupportsShouldProcess
# leaves $WhatIfPreference set to $true for its whole top-level scope when invoked with -WhatIf,
# which would otherwise silently turn unrelated read-only calls (Get-Process, Test-Path) into
# no-ops. Only $PSCmdlet.ShouldProcess() below should react to -WhatIf; it is unaffected by this
# override.
$WhatIfPreference = $false

if ($Force) {
    # Standard PowerShell -Force idiom: suppress this run's own ConfirmImpact-driven prompts without
    # touching the caller's own $ConfirmPreference elsewhere.
    $ConfirmPreference = 'None'
}

$ManifestFileName      = 'SolidGround.addin'
$SolidGroundFolderName = 'SolidGround'

# ----------------------------------------------------------------------------
# Helper functions
# ----------------------------------------------------------------------------

function Assert-RevitNotRunningForUninstall {
    <#
        Identical semantics to Deploy-RevitAddIn.ps1's own Assert-RevitNotRunning: without
        -AllowOtherRevitVersions, any running Revit.exe process (of any version) blocks; with it, only
        a Revit.exe process whose path is confirmed under -RevitInstallDir blocks, and a process whose
        path cannot be determined is still treated as blocking either way (it cannot be proven to be a
        different version). This logic is duplicated here, not dot-sourced from Deploy-RevitAddIn.ps1,
        because that script is a full top-level script whose own deploy/verify actions would run
        unconditionally if dot-sourced -- something an uninstall script must never trigger as a side
        effect. Deploy-RevitAddIn.ps1 itself is never modified by this script or in support of it.
    #>
    param(
        [Parameter(Mandatory)][string]$RevitInstallDir,
        [switch]$AllowOtherRevitVersions
    )

    $processes = Get-Process -Name 'Revit' -ErrorAction SilentlyContinue
    if (-not $processes) { return }

    $normalizedInstallDir = $RevitInstallDir.TrimEnd('\', '/')
    $blocking = [System.Collections.Generic.List[string]]::new()

    foreach ($proc in $processes) {
        $procPath = $null
        $pathKnown = $true
        try {
            $procPath = $proc.Path
        } catch {
            $pathKnown = $false
        }
        if ([string]::IsNullOrEmpty($procPath)) { $pathKnown = $false }

        if (-not $AllowOtherRevitVersions) {
            $suffix = if ($pathKnown) { " at $procPath" } else { ' (path unavailable)' }
            [void]$blocking.Add("PID $($proc.Id)$suffix")
            continue
        }

        if (-not $pathKnown) {
            [void]$blocking.Add("PID $($proc.Id) (path unavailable; cannot confirm this is a different Revit version)")
            continue
        }
        # See Deploy-RevitAddIn.ps1's own identical comment: a plain StartsWith would also match an
        # unrelated sibling install whose directory name merely shares $normalizedInstallDir as a
        # literal text prefix, which is not actually "under" that directory. Require either an exact
        # match or a path-separator boundary right after the prefix so only a genuine child path counts.
        $isExactMatch = $procPath.Equals($normalizedInstallDir, [System.StringComparison]::OrdinalIgnoreCase)
        $isUnderInstallDir = $procPath.StartsWith(
            $normalizedInstallDir + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)
        if ($isExactMatch -or $isUnderInstallDir) {
            [void]$blocking.Add("PID $($proc.Id) at $procPath")
        }
    }

    if ($blocking.Count -gt 0) {
        $reason = if ($AllowOtherRevitVersions) {
            "a Revit.exe process under '$RevitInstallDir' is running"
        } else {
            'a Revit.exe process is running (pass -AllowOtherRevitVersions to tolerate a different Revit version, for example Revit 2026 kept open for other work)'
        }
        throw "Refusing to uninstall: $reason.`n$($blocking -join "`n")"
    }
}

# ----------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------

Assert-RevitNotRunningForUninstall -RevitInstallDir $RevitInstallDir -AllowOtherRevitVersions:$AllowOtherRevitVersions

$removed = [System.Collections.Generic.List[string]]::new()
$left = [System.Collections.Generic.List[string]]::new()

$manifestPath = Join-Path $AddinsDirectory $ManifestFileName
$solidGroundRoot = Join-Path $AddinsDirectory $SolidGroundFolderName

if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    if ($PSCmdlet.ShouldProcess($manifestPath, 'Remove SolidGround add-in manifest')) {
        Remove-Item -LiteralPath $manifestPath -Force
        $removed.Add($manifestPath)
    }
} else {
    Write-Host "No manifest found at '$manifestPath' (nothing to remove)."
}

if (Test-Path -LiteralPath $solidGroundRoot -PathType Container) {
    if ($PSCmdlet.ShouldProcess($solidGroundRoot, 'Remove SolidGround versioned deployment folder tree')) {
        Remove-Item -LiteralPath $solidGroundRoot -Recurse -Force
        $removed.Add($solidGroundRoot)
    }
} else {
    Write-Host "No versioned deployment folder found at '$solidGroundRoot' (nothing to remove)."
}

$programDataRoot = Join-Path $env:ProgramData 'SolidGround\Revit'
$settingsPath = Join-Path $programDataRoot 'settings.json'
$logsPath = Join-Path $programDataRoot 'Logs'

if ($RemoveSettingsAndLogs) {
    foreach ($path in @($settingsPath, $logsPath)) {
        if (Test-Path -LiteralPath $path) {
            if ($PSCmdlet.ShouldProcess($path, 'Remove SolidGround settings/log state')) {
                Remove-Item -LiteralPath $path -Recurse -Force
                $removed.Add($path)
            }
        }
    }
} else {
    $left.Add("$settingsPath (pass -RemoveSettingsAndLogs to remove)")
    $left.Add("$logsPath (pass -RemoveSettingsAndLogs to remove)")
}

$left.Add('HKCU:\Software\Autodesk\Revit\Autodesk Revit 2027\CodeSigning (Revit''s own trust record; never touched by this script -- see Import-SigningTrust.ps1)')

Write-Host ''
if ($removed.Count -gt 0) {
    Write-Host 'Removed:' -ForegroundColor Green
    $removed | ForEach-Object { Write-Host "  $_" }
} else {
    Write-Host 'Removed: (nothing)'
}
Write-Host ''
Write-Host 'Left in place (by design):' -ForegroundColor Yellow
$left | ForEach-Object { Write-Host "  $_" }
