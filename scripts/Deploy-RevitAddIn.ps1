#Requires -Version 5.1
<#
.SYNOPSIS
    Deploys a locally built SolidGround.Revit add-in to a per-user Revit 2027 Add-Ins folder.

.DESCRIPTION
    Deploy-RevitAddIn.ps1 implements the deployment mechanism required by AGENTS.md's
    "Revit add-in conventions" section and docs/architecture/revit-add-in-conventions.md
    section 7: it never hand-copies files, it stages and hash-verifies the full managed
    dependency closure, it publishes by an atomic versioned-folder rename plus an atomic
    manifest replace, and it keeps a bounded number of previous versions for rollback.

    The script never writes to any all-user (%ProgramData% or Program Files) add-in
    location. The only supported target is the per-user Revit Add-Ins folder
    (-AddinsDirectory, default %APPDATA%\Autodesk\Revit\Addins\2027).

    Dependency closure. The script reads SolidGround.Revit.deps.json from
    -SourceDirectory. For the single build target it declares, every library of type
    "project" or "package" contributes the file names under its "runtime" section to
    the deployment closure. Libraries of type "reference" (RevitAPI/RevitAPIUI, which
    Revit itself supplies at runtime) are excluded and must never be copied. The
    script also refuses outright if any RevitAPI*.dll or Nice3point*.dll file is
    present anywhere in -SourceDirectory, and it fails closed, before copying
    anything, if any file the closure names is missing from -SourceDirectory.

    Safety. By default the script refuses to run while any Revit.exe process is
    running, anywhere. Pass -AllowOtherRevitVersions to relax that to "refuse only if
    the running Revit.exe's path is under -RevitInstallDir", so a different Revit
    version (for example Revit 2026, kept open for unrelated work) does not block a
    Revit 2027 add-in deployment. If a running Revit.exe's path cannot be determined,
    the script treats it as blocking either way, since it cannot prove the process is
    a different version.

    Dev loop. Build SolidGround.Revit, run this script to deploy it, fully restart
    Revit 2027 (add-ins are not hot-reloaded), then run this script again with
    -Verify to confirm the deployed bytes still match the build output. See
    scripts/README.md for the full walkthrough.

.PARAMETER Configuration
    The build configuration used only to compute the default -SourceDirectory
    (<repo>\src\SolidGround.Revit\bin\<Configuration>\net10.0-windows). Ignored if
    -SourceDirectory is supplied explicitly. Default: Release.

.PARAMETER SourceDirectory
    The build output directory containing SolidGround.Revit.dll,
    SolidGround.Revit.deps.json, SolidGround.addin, and the rest of the managed
    dependency closure. Default: <repo>\src\SolidGround.Revit\bin\<Configuration>\net10.0-windows,
    computed relative to this script's own location.

.PARAMETER AddinsDirectory
    The per-user Revit 2027 Add-Ins folder to deploy into. Default:
    $env:APPDATA\Autodesk\Revit\Addins\2027. Never point this at an all-user
    location; the script does not support all-user deployment.

.PARAMETER RevitInstallDir
    The Revit 2027 install directory used only to recognize a running Revit 2027
    process when -AllowOtherRevitVersions is set. Default:
    C:\Program Files\Autodesk\Revit 2027.

.PARAMETER AllowOtherRevitVersions
    Relaxes the running-Revit refusal so only a Revit.exe whose path is under
    -RevitInstallDir blocks deployment, instead of any running Revit.exe. Use this
    when a different Revit version is intentionally kept open for other work.

.PARAMETER KeepPreviousVersions
    How many previous versioned deployment folders to retain for rollback, in
    addition to the newest one. Default: 2 (three folders total survive a steady
    stream of deploys). The folder the live manifest currently references is never
    pruned, even if it would otherwise fall outside this window.

.PARAMETER Verify
    Instead of deploying, re-hashes the files in the deployment the live manifest
    currently references and compares them against -SourceDirectory. Prints a table
    and exits with a non-zero code if any file is missing or its hash does not match.

.EXAMPLE
    .\Deploy-RevitAddIn.ps1
    Deploys the Release build to the default per-user Revit 2027 Add-Ins folder.
    Refuses if any Revit.exe is running.

.EXAMPLE
    .\Deploy-RevitAddIn.ps1 -AllowOtherRevitVersions
    Deploys even though a different Revit version (for example Revit 2026) is open,
    refusing only if Revit 2027 itself is running.

.EXAMPLE
    .\Deploy-RevitAddIn.ps1 -WhatIf
    Reports what would be staged, published, and pruned without writing anything.

.EXAMPLE
    .\Deploy-RevitAddIn.ps1 -Verify
    Re-hashes the currently deployed version against the current build output and
    reports any mismatch or missing file.

.NOTES
    Follows docs/architecture/revit-add-in-conventions.md section 7 and
    docs/architecture/revit-2027-verification-and-host-design.md's "Thin-host design"
    and "Dependency closure the deploy script must enumerate" sections. Owned
    entirely under scripts/; see scripts/README.md for the full dev loop.
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$Configuration = 'Release',

    [string]$SourceDirectory,

    [string]$AddinsDirectory = (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2027'),

    [string]$RevitInstallDir = 'C:\Program Files\Autodesk\Revit 2027',

    [switch]$AllowOtherRevitVersions,

    [int]$KeepPreviousVersions = 2,

    [switch]$Verify
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# A script that declares SupportsShouldProcess leaves the automatic
# $WhatIfPreference variable set to $true for its whole top-level scope when
# invoked with -WhatIf. Left alone, that ambient preference silently turns
# unrelated, read-only calls (Get-FileHash, Get-Content, Get-ChildItem, and
# similar cmdlets that resolve a provider path) into no-ops that return
# nothing, which is not what -WhatIf is supposed to affect here: only the
# explicit $PSCmdlet.ShouldProcess() gates below should react to -WhatIf.
# $PSCmdlet.ShouldProcess() itself is unaffected by this override; it tracks
# the bound -WhatIf switch separately from the $WhatIfPreference variable.
$WhatIfPreference = $false

# ----------------------------------------------------------------------------
# Constants
# ----------------------------------------------------------------------------
$ManifestFileName        = 'SolidGround.addin'
$DepsJsonFileName        = 'SolidGround.Revit.deps.json'
$PrimaryAssemblyFileName = 'SolidGround.Revit.dll'
$SolidGroundFolderName   = 'SolidGround'
$VersionFolderPattern    = '^\d{8}-\d{6}-[0-9a-f]{8}$'

# ----------------------------------------------------------------------------
# Helper functions
# ----------------------------------------------------------------------------

function Test-HasProperty {
    <#
        Returns $true when $InputObject has a property named $Name. Safe to call
        under Set-StrictMode -Version Latest against PSCustomObjects produced by
        ConvertFrom-Json, where accessing a genuinely absent property throws.
    #>
    param(
        $InputObject,
        [Parameter(Mandatory)][string]$Name
    )
    if ($null -eq $InputObject) { return $false }
    return [bool]($InputObject.PSObject.Properties.Name -contains $Name)
}

function Get-Sha256Hex {
    param([Parameter(Mandatory)][string]$Path)
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-DeploymentClosure {
    <#
        Parses a SolidGround.Revit.deps.json file and returns the sorted,
        de-duplicated set of runtime file names contributed by every library of
        type "project" or "package" in the single build target it declares.
        Libraries of type "reference" (RevitAPI/RevitAPIUI) are excluded
        deliberately: Revit itself supplies these at runtime.

        A target library that has a non-empty "runtime" section but whose type
        cannot be positively confirmed as "project" or "package" from the
        "libraries" section (missing section, missing entry for that library,
        or missing "type" field) is a fail-closed error, not a silent skip:
        this function must never quietly drop a real runtime file from the
        deployment closure just because its type could not be determined.
    #>
    param([Parameter(Mandatory)][string]$DepsJsonPath)

    if (-not (Test-Path -LiteralPath $DepsJsonPath -PathType Leaf)) {
        throw "Dependency manifest not found: $DepsJsonPath"
    }

    $deps = Get-Content -LiteralPath $DepsJsonPath -Raw | ConvertFrom-Json

    if (-not (Test-HasProperty $deps 'targets')) {
        throw "'$DepsJsonPath' has no 'targets' section."
    }
    $targets = $deps.targets
    $targetNames = @($targets.PSObject.Properties.Name)
    if ($targetNames.Count -eq 0) {
        throw "'$DepsJsonPath' declares an empty 'targets' section."
    }

    $targetName = $null
    if ((Test-HasProperty $deps 'runtimeTarget') -and (Test-HasProperty $deps.runtimeTarget 'name')) {
        $candidate = $deps.runtimeTarget.name
        if ($targetNames -contains $candidate) {
            $targetName = $candidate
        }
    }
    if (-not $targetName) {
        if ($targetNames.Count -gt 1) {
            Write-Warning "'$DepsJsonPath' declares $($targetNames.Count) targets and 'runtimeTarget.name' matched none of them; using '$($targetNames[0])'."
        }
        $targetName = $targetNames[0]
    }

    $target = $targets.$targetName
    $libraries = if (Test-HasProperty $deps 'libraries') { $deps.libraries } else { $null }

    $fileNames = [System.Collections.Generic.SortedSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

    foreach ($libProp in $target.PSObject.Properties) {
        $libKey = $libProp.Name
        $libValue = $libProp.Value

        if (-not (Test-HasProperty $libValue 'runtime')) { continue }
        $runtimeEntries = $libValue.runtime
        if ($null -eq $runtimeEntries) { continue }
        $runtimeNames = @($runtimeEntries.PSObject.Properties.Name)
        if ($runtimeNames.Count -eq 0) { continue }

        $libType = $null
        $libTypeKnown = $false
        if ($libraries -and (Test-HasProperty $libraries $libKey)) {
            $libraryInfo = $libraries.$libKey
            if (Test-HasProperty $libraryInfo 'type') {
                $libType = $libraryInfo.type
                $libTypeKnown = $true
            }
        }

        if ($libTypeKnown -and $libType -ne 'project' -and $libType -ne 'package') {
            # A positively-identified excluded type (for example "reference": RevitAPI/RevitAPIUI,
            # which Revit itself supplies at runtime) is skipped deliberately.
            continue
        }

        if (-not $libTypeKnown) {
            throw "'$DepsJsonPath' target library '$libKey' has a non-empty 'runtime' section but no resolvable 'type' in the 'libraries' section (missing section, missing entry for '$libKey', or missing 'type' field). Refusing to silently drop its runtime file(s) from the deployment closure."
        }

        foreach ($runtimeProp in $runtimeEntries.PSObject.Properties) {
            $relativePath = $runtimeProp.Name
            if ([string]::IsNullOrWhiteSpace($relativePath)) { continue }

            # Real dotnet-generated deps.json files always use forward slashes here. Normalize any
            # backslash first so a hand-edited or corrupted deps.json cannot smuggle a Windows-style
            # traversal path (for example "..\..\..\Windows\win.ini") through untouched: taking only
            # the text after the *last* '/' would otherwise return the whole string unchanged when it
            # contains no '/' at all. Then reject any remaining '..' path segment and any leaf
            # containing a character Windows does not allow in a file name, rather than silently
            # accepting it — this file name is later joined onto -SourceDirectory and the staging
            # directory, so it must never be trusted to carry directory traversal.
            $normalizedRelativePath = $relativePath.Replace('\', '/')
            if ($normalizedRelativePath -match '(^|/)\.\.(/|$)') {
                throw "'$DepsJsonPath' target library '$libKey' declares a 'runtime' entry '$relativePath' containing a parent-directory traversal segment ('..'), which is not a safe deployment closure entry."
            }

            $leaf = $normalizedRelativePath.Substring($normalizedRelativePath.LastIndexOf('/') + 1)
            if ([string]::IsNullOrWhiteSpace($leaf) -or $leaf.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
                throw "'$DepsJsonPath' target library '$libKey' declares a 'runtime' entry '$relativePath' that does not resolve to a safe file name."
            }
            [void]$fileNames.Add($leaf)
        }
    }

    return ,@($fileNames)
}

function Get-PublishedAssemblyPath {
    <#
        Reads the <Assembly> element out of a SolidGround.addin manifest file, XML-decoded. The
        published manifest's <Assembly> value is XML-escaped on write (a deploy path can legitimately
        contain a character like '&'); parsing through the real XML API here, rather than a raw-text
        regex, decodes it back to the literal path the filesystem actually uses. Returns $null if the
        manifest does not exist.
    #>
    param([Parameter(Mandatory)][string]$ManifestPath)

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        return $null
    }
    [xml]$manifestXml = Get-Content -LiteralPath $ManifestPath -Raw
    $assemblyNode = $manifestXml.SelectSingleNode('//Assembly')
    if ($null -eq $assemblyNode) {
        throw "Could not find an <Assembly> element in '$ManifestPath'."
    }
    return $assemblyNode.InnerText.Trim()
}

function Assert-RevitNotRunning {
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
        # A plain StartsWith would also match an unrelated sibling install whose directory name
        # merely shares $normalizedInstallDir as a literal text prefix (for example "...\Revit 2027
        # Preview\Revit.exe" against -RevitInstallDir "...\Revit 2027"), which is not actually "under"
        # that directory. Require either an exact match or a path-separator boundary right after the
        # prefix so only a genuine child path counts.
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
        throw "Refusing to deploy: $reason.`n$($blocking -join "`n")"
    }
}

function Get-VersionFolders {
    param([Parameter(Mandatory)][string]$SolidGroundRoot)
    if (-not (Test-Path -LiteralPath $SolidGroundRoot -PathType Container)) {
        return @()
    }
    Get-ChildItem -LiteralPath $SolidGroundRoot -Directory |
        Where-Object { $_.Name -match $VersionFolderPattern } |
        Sort-Object Name -Descending
}

# ----------------------------------------------------------------------------
# Resolve defaults
# ----------------------------------------------------------------------------

if ([string]::IsNullOrWhiteSpace($SourceDirectory)) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $SourceDirectory = Join-Path $repoRoot "src\SolidGround.Revit\bin\$Configuration\net10.0-windows"
}

if ($KeepPreviousVersions -lt 0) {
    throw '-KeepPreviousVersions must be zero or greater.'
}

$depsJsonSourcePath = Join-Path $SourceDirectory $DepsJsonFileName
$manifestSourcePath = Join-Path $SourceDirectory $ManifestFileName
$finalManifestPath  = Join-Path $AddinsDirectory $ManifestFileName
$solidGroundRoot    = Join-Path $AddinsDirectory $SolidGroundFolderName

# ----------------------------------------------------------------------------
# Verify mode
# ----------------------------------------------------------------------------

if ($Verify) {
    $referencedAssemblyPath = Get-PublishedAssemblyPath -ManifestPath $finalManifestPath
    if (-not $referencedAssemblyPath) {
        throw "No deployment found to verify: '$finalManifestPath' does not exist or has no <Assembly> element."
    }
    $versionedPath = Split-Path -Parent $referencedAssemblyPath
    if (-not (Test-Path -LiteralPath $versionedPath -PathType Container)) {
        throw "The manifest at '$finalManifestPath' references '$versionedPath', which does not exist."
    }

    if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw "Source directory not found: $SourceDirectory"
    }

    $closureFileNames = Get-DeploymentClosure -DepsJsonPath $depsJsonSourcePath
    $filesToCheck = [System.Collections.Generic.List[string]]::new()
    foreach ($f in $closureFileNames) { [void]$filesToCheck.Add($f) }
    foreach ($f in @($ManifestFileName, $DepsJsonFileName, $PrimaryAssemblyFileName)) {
        if (-not $filesToCheck.Contains($f)) { [void]$filesToCheck.Add($f) }
    }

    $results = foreach ($f in ($filesToCheck | Sort-Object)) {
        $srcPath = Join-Path $SourceDirectory $f
        $depPath = Join-Path $versionedPath $f

        $srcExists = Test-Path -LiteralPath $srcPath -PathType Leaf
        $depExists = Test-Path -LiteralPath $depPath -PathType Leaf

        $srcHash = if ($srcExists) { Get-Sha256Hex -Path $srcPath } else { $null }
        $depHash = if ($depExists) { Get-Sha256Hex -Path $depPath } else { $null }

        $status = if (-not $srcExists -and -not $depExists) { 'MissingBoth' }
                  elseif (-not $srcExists) { 'MissingSource' }
                  elseif (-not $depExists) { 'MissingDeployed' }
                  elseif ($srcHash -eq $depHash) { 'OK' }
                  else { 'Mismatch' }

        [pscustomobject]@{
            File         = $f
            # Format-Table -AutoSize silently drops trailing columns instead
            # of wrapping them when the two full 64-character SHA-256 hashes
            # would overflow the host's reported console width (routinely
            # very narrow, or 0, under a redirected/non-interactive host) --
            # which would hide the Status column entirely. A short prefix
            # (matching git's short-hash convention) keeps every row well
            # under any realistic console width while still being enough to
            # eyeball a mismatch; -Verify's pass/fail exit code is what
            # actually gates automation, not this display.
            SourceHash   = if ($srcHash) { $srcHash.Substring(0, 12) } else { $null }
            DeployedHash = if ($depHash) { $depHash.Substring(0, 12) } else { $null }
            Status       = $status
        }
    }

    $results | Format-Table -AutoSize | Out-Host

    $failures = @($results | Where-Object { $_.Status -ne 'OK' })
    if ($failures.Count -gt 0) {
        Write-Host "Verification FAILED for $($failures.Count) of $($results.Count) file(s) against $versionedPath" -ForegroundColor Red
        exit 1
    }

    Write-Host "Verification passed for all $($results.Count) file(s) against $versionedPath" -ForegroundColor Green
    exit 0
}

# ----------------------------------------------------------------------------
# Deploy mode
# ----------------------------------------------------------------------------

Assert-RevitNotRunning -RevitInstallDir $RevitInstallDir -AllowOtherRevitVersions:$AllowOtherRevitVersions

if (-not (Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
    throw "Source directory not found: $SourceDirectory"
}

$closureFileNames = Get-DeploymentClosure -DepsJsonPath $depsJsonSourcePath

$requiredFiles = [System.Collections.Generic.List[string]]::new()
foreach ($f in $closureFileNames) { [void]$requiredFiles.Add($f) }
foreach ($f in @($ManifestFileName, $DepsJsonFileName, $PrimaryAssemblyFileName)) {
    if (-not $requiredFiles.Contains($f)) { [void]$requiredFiles.Add($f) }
}

$missing = @($requiredFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $SourceDirectory $_) -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw "Refusing to deploy: the following required file(s) are missing from '$SourceDirectory':`n  - $($missing -join "`n  - ")"
}

$forbidden = @(Get-ChildItem -LiteralPath $SourceDirectory -File | Where-Object {
    $_.Name -like 'RevitAPI*.dll' -or $_.Name -like 'Nice3point*.dll'
})
if ($forbidden.Count -gt 0) {
    throw "Refusing to deploy: '$SourceDirectory' contains Revit-supplied or CI-only reference assemblies that must never ship: $(($forbidden.Name) -join ', ')"
}

# Optional: carry along any .pdb sitting next to a .dll already in the closure.
$filesToStage = [System.Collections.Generic.List[string]]::new()
foreach ($f in $requiredFiles) { [void]$filesToStage.Add($f) }
foreach ($f in @($requiredFiles)) {
    if ($f -like '*.dll') {
        $pdbName = [System.IO.Path]::ChangeExtension($f, '.pdb')
        if (-not $filesToStage.Contains($pdbName) -and (Test-Path -LiteralPath (Join-Path $SourceDirectory $pdbName) -PathType Leaf)) {
            [void]$filesToStage.Add($pdbName)
        }
    }
}

$dllHash = Get-Sha256Hex -Path (Join-Path $SourceDirectory $PrimaryAssemblyFileName)
$stamp = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), $dllHash.Substring(0, 8)

$stagingPath   = Join-Path $solidGroundRoot ".staging-$stamp"
$versionedPath = Join-Path $solidGroundRoot $stamp

$deployDescription = "stage and publish SolidGround.Revit build $stamp ($($filesToStage.Count) file(s)) to $versionedPath"
if ($PSCmdlet.ShouldProcess($AddinsDirectory, $deployDescription)) {

    if (Test-Path -LiteralPath $versionedPath) {
        throw "A deployment folder already exists at '$versionedPath'. Wait a second so the timestamp changes, then retry."
    }

    try {
        New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null

        $sourceHashes = @{}
        foreach ($f in $filesToStage) {
            $srcPath = Join-Path $SourceDirectory $f
            $sourceHashes[$f] = Get-Sha256Hex -Path $srcPath
            Copy-Item -LiteralPath $srcPath -Destination (Join-Path $stagingPath $f) -Force
        }

        $copyMismatches = @($filesToStage | Where-Object {
            (Get-Sha256Hex -Path (Join-Path $stagingPath $_)) -ne $sourceHashes[$_]
        })
        if ($copyMismatches.Count -gt 0) {
            throw "Copy verification failed for: $($copyMismatches -join ', ')."
        }

        [System.IO.Directory]::Move($stagingPath, $versionedPath)
    } catch {
        if (Test-Path -LiteralPath $stagingPath) {
            Remove-Item -LiteralPath $stagingPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw
    }

    $templateText = Get-Content -LiteralPath $manifestSourcePath -Raw
    $assemblyPattern = '<Assembly>\s*SolidGround\.Revit\.dll\s*</Assembly>'
    $assemblyMatch = [regex]::Match($templateText, $assemblyPattern)
    if (-not $assemblyMatch.Success) {
        throw "Manifest template '$manifestSourcePath' does not contain the expected <Assembly>SolidGround.Revit.dll</Assembly> element; refusing to publish an unverified manifest."
    }
    $deployedDllPath = Join-Path $versionedPath $PrimaryAssemblyFileName
    # $deployedDllPath is derived from -AddinsDirectory, which defaults to $env:APPDATA but is
    # caller-overridable and can legitimately contain XML-significant characters (for example '&').
    # Escape it before splicing it into XML text content so the published manifest always stays
    # well-formed instead of silently becoming invalid XML that Revit's own manifest loader rejects.
    $escapedDeployedDllPath = [System.Security.SecurityElement]::Escape($deployedDllPath)
    $newElement = "<Assembly>$escapedDeployedDllPath</Assembly>"
    $publishedText = $templateText.Substring(0, $assemblyMatch.Index) + $newElement + $templateText.Substring($assemblyMatch.Index + $assemblyMatch.Length)

    $tempManifestPath = Join-Path $AddinsDirectory "SolidGround.addin.tmp-$stamp"
    try {
        [System.IO.File]::WriteAllText($tempManifestPath, $publishedText, [System.Text.UTF8Encoding]::new($false))
        if (Test-Path -LiteralPath $finalManifestPath -PathType Leaf) {
            # [NullString]::Value, not $null: PowerShell's own $null does not
            # marshal to a true .NET null string through this overload and
            # File.Replace throws "The path is not of a legal form" instead.
            [System.IO.File]::Replace($tempManifestPath, $finalManifestPath, [NullString]::Value)
        } else {
            [System.IO.File]::Move($tempManifestPath, $finalManifestPath)
        }
    } catch {
        if (Test-Path -LiteralPath $tempManifestPath) {
            Remove-Item -LiteralPath $tempManifestPath -Force -ErrorAction SilentlyContinue
        }
        throw
    }

    Write-Host "Deployed SolidGround.Revit build $stamp to $versionedPath" -ForegroundColor Green
    Write-Host "Manifest $finalManifestPath now points at $deployedDllPath"
}

# ----------------------------------------------------------------------------
# Prune old versions (never the one the live manifest references)
# ----------------------------------------------------------------------------

$referencedAssemblyPath = Get-PublishedAssemblyPath -ManifestPath $finalManifestPath
$referencedFolder = if ($referencedAssemblyPath) { (Split-Path -Parent $referencedAssemblyPath).TrimEnd('\', '/') } else { $null }

$allVersionFolders = @(Get-VersionFolders -SolidGroundRoot $solidGroundRoot)
$keepCount = $KeepPreviousVersions + 1
$toKeep = @($allVersionFolders | Select-Object -First $keepCount)
$toKeepPaths = @($toKeep | ForEach-Object { $_.FullName })

$toPrune = @($allVersionFolders | Where-Object {
    ($toKeepPaths -notcontains $_.FullName) -and
    (-not $referencedFolder -or $_.FullName -ne $referencedFolder)
})

if ($toPrune.Count -gt 0) {
    $pruneDescription = "remove $($toPrune.Count) old deployment folder(s): $(($toPrune.Name) -join ', ')"
    if ($PSCmdlet.ShouldProcess($solidGroundRoot, $pruneDescription)) {
        foreach ($folder in $toPrune) {
            Remove-Item -LiteralPath $folder.FullName -Recurse -Force
            Write-Host "Pruned old deployment: $($folder.FullName)"
        }
    }
}
