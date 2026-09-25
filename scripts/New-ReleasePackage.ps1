<#
.SYNOPSIS
    Builds, signs, and stages a versioned SolidGround.Revit release zip under artifacts/release/.

.DESCRIPTION
    New-ReleasePackage.ps1 implements docs/architecture/revit-release-packaging-and-signing.md
    section 4. It runs every precondition below and fails closed on the first one that does not
    hold -- nothing is written to -OutputDirectory until every precondition has already passed. The
    numbering below is the actual execution order (cheap file-existence/name checks first, then the
    working tree and its push status, then restore/build/test, then checks against the real build
    *output*, which can only run after that build -- signing runs last because it signs that same
    build output):

      1. The computed zip file name does not already exist under -OutputDirectory, and (for a signed
         release) no local git tag already names this version.
      2. THIRD-PARTY-NOTICES exists and names every package in
         src/SolidGround.Revit/packages.lock.json whose "type" is not "Project".
      3. The working tree is clean (git status --porcelain is empty), unless -SkipCleanTreeCheck.
      4. HEAD is pushed and canonical: after `git fetch origin main --quiet` (so the objects needed
         to evaluate this are actually present locally), HEAD must be an ancestor of the live
         origin/main tip read via `git ls-remote`, never a possibly-stale local ref.
      5. `dotnet restore <solution> --locked-mode` (never -p:UseRevitReferenceAssemblies=true -- that
         flag is CI-only and must never reach a local packaging run).
      6. `dotnet build <solution> --configuration <Configuration> --no-restore`.
      7. `dotnet test <tests project> --configuration <Configuration> --no-build`, unless -SkipTests.
      8. Deploy-RevitAddIn.ps1 itself is reused, never reimplemented, for its own fail-closed
         dependency-closure and forbidden-file validation: this script dot-sources
         Deploy-RevitAddIn.ps1's own default (non-Verify) code path under -WhatIf against a
         throwaway, disposable Add-Ins directory, so its Get-DeploymentClosure output and
         missing-file/forbidden-RevitAPI*/Nice3point*-file checks run for real and this script never
         re-transcribes that logic into a separately hand-maintained list that could drift.
         Deploy-RevitAddIn.ps1 is never modified to support this -- including its own unconditional
         "no Revit.exe may be running" refusal, which this script neutralizes for this call only by
         also passing -AllowOtherRevitVersions with a -RevitInstallDir no real Revit installation can
         ever be under, since packaging never touches a real Add-Ins folder and so has no reason to
         care whether Revit happens to be running on the packaging machine.
      9. A native-binary / runtimes\ folder / unexplained-extra-file re-check runs directly against
         the real build output directory, using precondition 8's own derived file set as "explained"
         -- never a separately hand-maintained list.
     10. Unless -Sign:$false: every DLL and first-party script staged for the zip is signed
         (Sign-RevitAddIn.ps1) and independently re-verified here -- fails closed only on
         Authenticode status NotSigned/HashMismatch/NotSupportedFileFormat/Incompatible or a signer
         certificate hash that does not match the pinned value (a deny list: this machine's own local
         trust-store state is not this gate's concern, and an untrusted self-signed certificate
         empirically reports UnknownError, never NotTrusted, so an allow list of Valid/NotTrusted
         would reject every SolidGround certificate before Import-SigningTrust.ps1 has run). 10a.
         Every one of those files was also successfully Authenticode-timestamped (the legacy
         Authenticode/PKCS#7 protocol, never RFC 3161) -- a release build fails closed if
         timestamping did not succeed.

    Signing stages every DLL and first-party script into a temporary folder outside the git working
    tree before ever calling Set-AuthenticodeSignature -- the committed .ps1 source under scripts/
    is never itself signed, so the working tree never gets dirtied by an appended signature block.

    SHA256SUMS (in-zip, over the final signed bytes) and a sibling <zip>.sha256 (outside the zip, so
    the archive itself can be verified before extraction) are both written using the SHA-256
    algorithm. -Sign:$false produces an explicitly UNSIGNED-DRY-RUN-named artifact, never mistakable
    for a real release asset.

.PARAMETER Configuration
    The build configuration to package. Default: Release.

.PARAMETER OutputDirectory
    Where the zip, SHA256SUMS sibling hash file are written. Default:
    <repo>\artifacts\release (a subfolder of the existing, already git-ignored artifacts\ bucket).

.PARAMETER Version
    Overrides the version used in the zip's file name. Default: read from the repository's
    Directory.Build.props <Version> element. Override only for a local dry run; a real release
    always uses the committed value.

.PARAMETER Sign
    On by default. Pass -Sign:$false to produce an explicitly UNSIGNED-DRY-RUN-named artifact for
    local iteration; a real release never sets this.

.PARAMETER SkipTests
    Skips precondition 7 (the offline test suite). Local iteration only; a real release never sets
    this.

.PARAMETER SkipCleanTreeCheck
    Skips precondition 3 (the clean-working-tree check). Local iteration only; a real release never
    sets this.

.EXAMPLE
    .\New-ReleasePackage.ps1
    Runs every precondition, signs, and packages a real release zip from the committed version.

.EXAMPLE
    .\New-ReleasePackage.ps1 -Sign:$false -SkipCleanTreeCheck -SkipTests
    Fast local dry run: produces an UNSIGNED-DRY-RUN zip without touching git-cleanliness or the
    test suite. Never use this combination for a real release.

.EXAMPLE
    .\New-ReleasePackage.ps1 -WhatIf
    Runs preconditions 1-7 for real (the cheap checks, git status, and restore/build/test) but only
    reports, rather than performs, staging, Deploy-RevitAddIn.ps1's own validation, the native-binary
    check, signing, and zipping (preconditions 8-10 plus the actual writes) -- those are interleaved
    closely enough with this script's real writes that they are gated together. SupportsShouldProcess
    with ConfirmImpact 'Medium' means an ordinary call with neither -WhatIf nor -Confirm proceeds
    without prompting.

.NOTES
    Follows docs/architecture/revit-release-packaging-and-signing.md section 4. Reuses
    Deploy-RevitAddIn.ps1 and Sign-RevitAddIn.ps1 rather than reimplementing their logic. Owned
    entirely under scripts/; see scripts/README.md.
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string]$Configuration = 'Release',

    [string]$OutputDirectory,

    [string]$Version,

    [switch]$Sign = $true,

    [switch]$SkipTests,

    [switch]$SkipCleanTreeCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# See Deploy-RevitAddIn.ps1's own identical comment: a script that declares SupportsShouldProcess
# leaves the automatic $WhatIfPreference variable set to $true for its whole top-level scope when
# invoked with -WhatIf, which would otherwise silently turn unrelated read-only calls into no-ops.
# Only the explicit $PSCmdlet.ShouldProcess() call below should react to -WhatIf; it is unaffected by
# this override.
$WhatIfPreference = $false

# ----------------------------------------------------------------------------
# Constants and path resolution
# ----------------------------------------------------------------------------
$RepoRoot            = Split-Path -Parent $PSScriptRoot
$SolutionPath         = Join-Path $RepoRoot 'SolidGround.slnx'
$TestsProjectPath     = Join-Path $RepoRoot 'tests\SolidGround.Tests\SolidGround.Tests.csproj'
$RevitLockFilePath    = Join-Path $RepoRoot 'src\SolidGround.Revit\packages.lock.json'
$DirectoryBuildPropsPath = Join-Path $RepoRoot 'Directory.Build.props'
$InstallGuidePath     = Join-Path $RepoRoot 'docs\revit-install-guide.md'
$ThirdPartyNoticesPath = Join-Path $RepoRoot 'THIRD-PARTY-NOTICES'
$LicensePath          = Join-Path $RepoRoot 'LICENSE'
$PinFilePath          = Join-Path $PSScriptRoot 'signing-certificate.json'
$DeployScriptPath     = Join-Path $PSScriptRoot 'Deploy-RevitAddIn.ps1'
$SignScriptPath       = Join-Path $PSScriptRoot 'Sign-RevitAddIn.ps1'
$TimestampServerUrl   = 'http://timestamp.digicert.com'
$Sha256AlgorithmName  = [System.Security.Cryptography.HashAlgorithmName]::SHA256
# See Sign-RevitAddIn.ps1's own identical constant and comment: a deny list, not an allow list of
# ('Valid', 'NotTrusted') -- an untrusted self-signed certificate reports UnknownError, not NotTrusted.
$FailClosedSignatureStatuses = @('NotSigned', 'HashMismatch', 'NotSupportedFileFormat', 'Incompatible')

$InstallScriptNames = @('Install-SolidGround.ps1', 'Uninstall-SolidGround.ps1', 'Deploy-RevitAddIn.ps1', 'Import-SigningTrust.ps1')

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $RepoRoot 'artifacts\release'
}

$BuildOutputDirectory = Join-Path $RepoRoot "src\SolidGround.Revit\bin\$Configuration\net10.0-windows"

# ----------------------------------------------------------------------------
# Helper functions
# ----------------------------------------------------------------------------

function Invoke-CheckedProcess {
    <#
        Invokes a native executable (never a PowerShell script) and throws with the real exit code
        if it did not succeed. Deliberately never merges stderr into the captured output stream:
        redirecting a native command's stderr under Windows PowerShell 5.1 wraps each line in an
        ErrorRecord and can make $? false even on a real exit code 0, so stdout/stderr are left to
        flow to the host directly and $LASTEXITCODE alone decides success.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$ArgumentList
    )
    Write-Host "> $FilePath $($ArgumentList -join ' ')"
    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "'$FilePath $($ArgumentList -join ' ')' exited with code $LASTEXITCODE."
    }
}

function Get-PinnedVersionFromDirectoryBuildProps {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Cannot resolve -Version automatically: '$Path' not found."
    }
    [xml]$propsXml = Get-Content -LiteralPath $Path -Raw
    $versionNode = $propsXml.SelectSingleNode('//Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw "'$Path' has no non-empty <Version> element."
    }
    return $versionNode.InnerText.Trim()
}

function Get-SolidGroundSigningCertificatePin {
    param([Parameter(Mandatory)][string]$PinFilePath)

    if (-not (Test-Path -LiteralPath $PinFilePath -PathType Leaf)) {
        throw "Signing certificate pin file not found: '$PinFilePath'. Run '.\Sign-RevitAddIn.ps1 -NewCertificate' once on this workstation to mint the SolidGround signing certificate and create this file (see docs/architecture/revit-release-packaging-and-signing.md section 3)."
    }

    $pin = Get-Content -LiteralPath $PinFilePath -Raw | ConvertFrom-Json

    foreach ($requiredField in @('subject', 'sha256', 'thumbprint', 'notBefore', 'notAfter')) {
        $hasField = [bool]($pin.PSObject.Properties.Name -contains $requiredField)
        if (-not $hasField -or [string]::IsNullOrWhiteSpace([string]$pin.$requiredField)) {
            throw "'$PinFilePath' is missing a non-empty '$requiredField' field."
        }
    }
    if ($pin.sha256 -cnotmatch '^[0-9A-F]{64}$') {
        throw "'$PinFilePath' field 'sha256' must be 64 uppercase hexadecimal characters; found '$($pin.sha256)'."
    }

    return $pin
}

function Test-SolidGroundFileSignature {
    <#
        Same gate as Sign-RevitAddIn.ps1's own Test-SolidGroundFileSignature (duplicated here
        deliberately -- this design does not factor gate logic into a shared module; see
        Deploy-RevitAddIn.ps1 reuse note below for why): fails closed on Status NotSigned,
        HashMismatch, NotSupportedFileFormat, Incompatible, or a signer certificate whose SHA-256
        hash does not equal the pinned value. Deny list, not an allow list of Valid/NotTrusted -- see
        $FailClosedSignatureStatuses above.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$PinnedSha256
    )

    $signature = Get-AuthenticodeSignature -FilePath $FilePath

    if ($signature.Status -in $FailClosedSignatureStatuses) {
        throw "'$FilePath' has Authenticode signature status '$($signature.Status)' (fails closed on $($FailClosedSignatureStatuses -join '/')): $($signature.StatusMessage)"
    }
    if ($null -eq $signature.SignerCertificate) {
        throw "'$FilePath' reports signature status '$($signature.Status)' but has no signer certificate."
    }

    $actualSha256 = $signature.SignerCertificate.GetCertHashString($Sha256AlgorithmName)
    if ($actualSha256 -ne $PinnedSha256) {
        throw "'$FilePath' was signed by a certificate whose SHA-256 hash ($actualSha256) does not match the pinned SolidGround signing certificate ($PinnedSha256). Refusing to package a differently-signed file."
    }

    return [pscustomobject]@{ Path = $FilePath; Status = [string]$signature.Status }
}

function Get-ReleasePayloadFileNames {
    <#
        Reuses Deploy-RevitAddIn.ps1's own Get-DeploymentClosure function and its missing-file/
        forbidden-file validation verbatim (precondition 8), by dot-sourcing the script's own
        default (non-Verify) code path under -WhatIf against a throwaway AddinsDirectory --
        Deploy-RevitAddIn.ps1 itself is never modified (ruling R10). Dot-sourcing happens inside
        this function's own scope, never the caller's script scope, so Deploy-RevitAddIn.ps1's
        param defaults, helper functions, and constants (many sharing names with this script's own,
        for example -Configuration) cannot collide with or leak into New-ReleasePackage.ps1's own
        variables. -WhatIf guarantees the deploy/prune ShouldProcess blocks in that script perform no
        writes; -Verify is never passed, so its own `exit` statements are never reached.

        This call never touches a real Add-Ins folder -- -AddinsDirectory is always the disposable,
        never-created "$deployWhatIfAddinsDirectory" temp path below -- so whether a real Revit.exe
        happens to be running on the packaging machine is irrelevant to this read-only closure/
        validation pass. Deploy-RevitAddIn.ps1 itself is never modified to skip its own unconditional
        Assert-RevitNotRunning check (ruling R10); instead this reuses that script's own
        -AllowOtherRevitVersions relaxation together with a -RevitInstallDir no real Revit
        installation can ever be "under" (a path inside this same disposable AddinsDirectory), so
        Assert-RevitNotRunning's "is a running Revit.exe's path under -RevitInstallDir" test can never
        match a real process and this validation pass is never blocked by an unrelated, real,
        live Revit session on the packaging machine -- see
        docs/architecture/revit-release-packaging-and-signing.md, "Packaging never requires closing
        Revit". (A running Revit.exe whose own path cannot be determined -- for example, access
        denied -- still blocks even under -AllowOtherRevitVersions; that pre-existing
        Deploy-RevitAddIn.ps1 edge case is unchanged and vanishingly rare.)

        Returns the exact file-name set (payload/'s DLLs/manifest/deps.json/pdb files) precondition
        9's own "unexplained extra file" check treats as explained -- Deploy-RevitAddIn.ps1's own
        $filesToStage variable, read back directly rather than re-derived, so it can never drift from
        what that script itself actually considers the deployment closure.
    #>
    param(
        [Parameter(Mandatory)][string]$DeployScriptPath,
        [Parameter(Mandatory)][string]$SourceDirectory,
        [Parameter(Mandatory)][string]$AddinsDirectory
    )

    $noRealRevitCanBeUnderThisPath = Join-Path $AddinsDirectory 'unreachable-revit-install-sentinel'
    . $DeployScriptPath -SourceDirectory $SourceDirectory -AddinsDirectory $AddinsDirectory `
        -AllowOtherRevitVersions -RevitInstallDir $noRealRevitCanBeUnderThisPath -WhatIf

    return ,@($filesToStage)
}

function Assert-ThirdPartyNoticesCoversLockedPackages {
    <#
        Mirrors tests/SolidGround.Tests/ReleasePackagingTests.cs's own
        ThirdPartyNoticesFileNamesEveryPackageInTheLockFileWithItsExactVersion exactly: every
        non-"Project"-type package in $LockFilePath must appear in $NoticesPath together with its
        exact resolved version on the same line -- not merely the package name somewhere in the
        file -- so a future version bump without a matching notices update fails closed here too,
        not only in the offline test suite that precondition 7 already runs.
    #>
    param(
        [Parameter(Mandatory)][string]$NoticesPath,
        [Parameter(Mandatory)][string]$LockFilePath
    )

    if (-not (Test-Path -LiteralPath $NoticesPath -PathType Leaf)) {
        throw "Refusing to package: '$NoticesPath' not found. Every locked third-party package needs a notice (AGENTS.md dependency policy)."
    }
    if (-not (Test-Path -LiteralPath $LockFilePath -PathType Leaf)) {
        throw "Cannot verify THIRD-PARTY-NOTICES coverage: '$LockFilePath' not found."
    }

    $noticesLines = (Get-Content -LiteralPath $NoticesPath -Raw) -split '\r?\n'
    $lock = Get-Content -LiteralPath $LockFilePath -Raw | ConvertFrom-Json

    # Every other JSON-property access in this file's scripts checks PSObject.Properties.Name first
    # (see the pin-file reader functions); this lock file is expected to always have a 'dependencies'
    # object, but reading it unguarded would throw an unhelpful PropertyNotFoundException under
    # Set-StrictMode -Version Latest if a hand-edited or corrupted lock file ever lacked it.
    $hasDependencies = [bool]($lock.PSObject.Properties.Name -contains 'dependencies') -and ($null -ne $lock.dependencies)
    if (-not $hasDependencies) {
        throw "'$LockFilePath' has no non-null 'dependencies' section."
    }

    $packages = [System.Collections.Generic.List[object]]::new()
    foreach ($targetProperty in $lock.dependencies.PSObject.Properties) {
        foreach ($packageProperty in $targetProperty.Value.PSObject.Properties) {
            $package = $packageProperty.Value
            $hasType = [bool]($package.PSObject.Properties.Name -contains 'type')
            $type = if ($hasType) { $package.type } else { $null }
            if ($type -eq 'Project') { continue }

            $hasResolved = [bool]($package.PSObject.Properties.Name -contains 'resolved')
            $resolved = if ($hasResolved) { [string]$package.resolved } else { $null }
            if ([string]::IsNullOrWhiteSpace($resolved)) {
                throw "'$LockFilePath' package '$($packageProperty.Name)' has no resolved version."
            }

            [void]$packages.Add([pscustomobject]@{ Name = $packageProperty.Name; Version = $resolved })
        }
    }

    $missing = @($packages | Sort-Object -Property Name, Version -Unique | Where-Object {
        $name = $_.Name; $version = $_.Version
        -not [bool]($noticesLines | Where-Object { $_.Contains($name) -and $_.Contains($version) })
    })
    if ($missing.Count -gt 0) {
        $missingDescriptions = $missing | ForEach-Object { "$($_.Name) $($_.Version)" }
        throw "Refusing to package: '$NoticesPath' does not name every locked third-party package together with its exact resolved version on the same line: missing $($missingDescriptions -join ', ')."
    }
}

# ----------------------------------------------------------------------------
# Resolve version, zip name, and the version-freshness precondition (1) -- runs first because it is
# the cheapest possible check and needs neither git status nor a build.
# ----------------------------------------------------------------------------

if (-not $Version) {
    $Version = Get-PinnedVersionFromDirectoryBuildProps -Path $DirectoryBuildPropsPath
}

$zipBaseName = if ($Sign) { "SolidGround-Revit2027-v$Version" } else { "SolidGround-Revit2027-v$Version-UNSIGNED-DRY-RUN" }
$zipFileName = "$zipBaseName.zip"
$zipPath = Join-Path $OutputDirectory $zipFileName

if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
    throw "Refusing to overwrite existing release artifact '$zipPath'. Bump <Version> in Directory.Build.props before packaging a new release."
}
if ($Sign) {
    $existingTag = git -C $RepoRoot tag -l "v$Version"
    if ($existingTag) {
        throw "Refusing to package: a local git tag 'v$Version' already exists. Bump <Version> in Directory.Build.props before packaging a new release."
    }
}

# ----------------------------------------------------------------------------
# Precondition 2 (cheap; run before the expensive restore/build/test steps)
# ----------------------------------------------------------------------------

foreach ($required in @($InstallGuidePath, $ThirdPartyNoticesPath, $LicensePath)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Cannot package: '$required' not found."
    }
}
Assert-ThirdPartyNoticesCoversLockedPackages -NoticesPath $ThirdPartyNoticesPath -LockFilePath $RevitLockFilePath

# ----------------------------------------------------------------------------
# Precondition 3: clean working tree
# ----------------------------------------------------------------------------

if (-not $SkipCleanTreeCheck) {
    $statusOutput = @(git -C $RepoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw "git status failed with exit code $LASTEXITCODE." }
    if ($statusOutput.Count -gt 0) {
        throw "Refusing to package: working tree is not clean.`n$($statusOutput -join [System.Environment]::NewLine)"
    }
} else {
    Write-Warning '-SkipCleanTreeCheck set: skipping the clean-working-tree precondition. Never use this for a real release.'
}

# ----------------------------------------------------------------------------
# Precondition 4: HEAD is pushed and canonical (Draft 4: fetch first, then check the live tip)
# ----------------------------------------------------------------------------

git -C $RepoRoot fetch origin main --quiet
if ($LASTEXITCODE -ne 0) { throw "git fetch origin main failed with exit code $LASTEXITCODE." }

$remoteTipLine = git -C $RepoRoot ls-remote origin refs/heads/main
if ($LASTEXITCODE -ne 0) { throw "git ls-remote origin refs/heads/main failed with exit code $LASTEXITCODE." }
if (-not $remoteTipLine) { throw 'git ls-remote origin refs/heads/main returned no output; cannot determine the live origin/main tip.' }
$remoteTipSha = (@($remoteTipLine)[0] -split '\s+')[0]

$headSha = git -C $RepoRoot rev-parse HEAD
if ($LASTEXITCODE -ne 0) { throw "git rev-parse HEAD failed with exit code $LASTEXITCODE." }

git -C $RepoRoot merge-base --is-ancestor $headSha $remoteTipSha
if ($LASTEXITCODE -ne 0) {
    throw "Refusing to package: HEAD ($headSha) is not an ancestor of the live origin/main tip ($remoteTipSha). Push and merge to main first."
}

# ----------------------------------------------------------------------------
# Preconditions 5-7: restore, build, test
# ----------------------------------------------------------------------------

Invoke-CheckedProcess -FilePath 'dotnet' -ArgumentList @('restore', $SolutionPath, '--locked-mode')
Invoke-CheckedProcess -FilePath 'dotnet' -ArgumentList @('build', $SolutionPath, '--configuration', $Configuration, '--no-restore')

if (-not $SkipTests) {
    Invoke-CheckedProcess -FilePath 'dotnet' -ArgumentList @('test', '--project', $TestsProjectPath, '--configuration', $Configuration, '--no-build')
} else {
    Write-Warning '-SkipTests set: skipping the offline test suite. Never use this for a real release.'
}

# ----------------------------------------------------------------------------
# Staging: everything below is confined to a temp folder outside the git working tree
# ----------------------------------------------------------------------------

# Preconditions 1-7 above (the cheap checks, git status, and restore/build/test) already ran for
# real by this point regardless of -WhatIf. Preconditions 8-10 (Deploy-RevitAddIn.ps1's own
# validation, the native-binary check, and signing) are interleaved with this script's actual
# staging writes below closely enough that splitting "validate" from "write" the way
# Deploy-RevitAddIn.ps1 itself does would be a materially larger change than this fix calls for; they
# are gated behind this same ShouldProcess along with the writes. Under -WhatIf this returns before
# creating anything, including $stagingRoot itself; PowerShell's own automatic "What if: Performing
# the operation..." message reports the intended target.
if (-not $PSCmdlet.ShouldProcess($zipPath, 'build, sign, and package a SolidGround release zip')) {
    return
}

$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) "solidground-release-staging-$([System.Guid]::NewGuid().ToString('N'))"
$installStagingDir = Join-Path $stagingRoot 'install'
$payloadStagingDir = Join-Path $stagingRoot 'payload'
$deployWhatIfAddinsDirectory = Join-Path $stagingRoot 'deploy-whatif-addins'

try {
    New-Item -ItemType Directory -Path $installStagingDir -Force | Out-Null
    New-Item -ItemType Directory -Path $payloadStagingDir -Force | Out-Null

    foreach ($name in $InstallScriptNames) {
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $installStagingDir $name) -Force
    }
    # install.cmd ships as a sibling of Install-SolidGround.ps1 inside install\ (matching this
    # design's own zip-tree diagram and its "four at the root, five under install\" file count --
    # never the zip root itself), since its own "%~dp0Install-SolidGround.ps1" reference depends on
    # both files sitting in the same folder at run time.
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.cmd') -Destination (Join-Path $installStagingDir 'install.cmd') -Force

    # Precondition 8 + the data source for precondition 9: reuse Deploy-RevitAddIn.ps1's own
    # -WhatIf pass rather than reimplementing its closure/validation logic.
    if (-not (Test-Path -LiteralPath $BuildOutputDirectory -PathType Container)) {
        throw "Build output directory not found: '$BuildOutputDirectory'. Did precondition 6's build actually target this -Configuration?"
    }
    $explainedFiles = @(Get-ReleasePayloadFileNames -DeployScriptPath $DeployScriptPath -SourceDirectory $BuildOutputDirectory -AddinsDirectory $deployWhatIfAddinsDirectory)

    # Precondition 9: native-binary / runtimes\ folder / unexplained-extra-file check against the
    # real build output directory (never our own staging copy, which is tautologically limited to
    # the explained set by construction below).
    $buildOutputItems = @(Get-ChildItem -LiteralPath $BuildOutputDirectory -Recurse)
    $runtimesFolders = @($buildOutputItems | Where-Object { $_.PSIsContainer -and $_.Name -eq 'runtimes' })
    if ($runtimesFolders.Count -gt 0) {
        throw "Refusing to package: a 'runtimes\' folder exists under '$BuildOutputDirectory' ($((@($runtimesFolders.FullName)) -join ', ')). Native runtime assets must never ship."
    }

    $buildOutputFiles = @($buildOutputItems | Where-Object { -not $_.PSIsContainer })
    $unexplained = @($buildOutputFiles | Where-Object { $explainedFiles -notcontains $_.Name })
    if ($unexplained.Count -gt 0) {
        throw "Refusing to package: '$BuildOutputDirectory' contains file(s) not explained by Deploy-RevitAddIn.ps1's own deployment closure (plus its manifest/deps.json/pdb set): $((@($unexplained.Name)) -join ', '). Either the closure needs updating or this is a stray build artifact."
    }

    foreach ($file in $buildOutputFiles) {
        if ($file.Extension -ieq '.dll') {
            try {
                [void][System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
            } catch {
                throw "Refusing to package: '$($file.FullName)' does not look like a managed assembly ($($_.Exception.Message)). Native binaries must never ship."
            }
        }
    }

    foreach ($name in $explainedFiles) {
        $src = Join-Path $BuildOutputDirectory $name
        if (-not (Test-Path -LiteralPath $src -PathType Leaf)) {
            throw "Cannot stage release payload: '$src' not found even though Deploy-RevitAddIn.ps1's own closure/validation pass named it."
        }
        Copy-Item -LiteralPath $src -Destination (Join-Path $payloadStagingDir $name) -Force
    }

    # Precondition 10 + 10a: sign, then independently re-verify the staged copies.
    $signResults = @()
    if ($Sign) {
        $pin = Get-SolidGroundSigningCertificatePin -PinFilePath $PinFilePath

        # The pin file's own copy travels inside install\ so Install-SolidGround.ps1 and
        # Import-SigningTrust.ps1 can both read it from inside an extracted zip (R12) -- this is
        # data, never itself Authenticode-signed.
        Copy-Item -LiteralPath $PinFilePath -Destination (Join-Path $installStagingDir 'signing-certificate.json') -Force

        $dllsToSign = @('SolidGround.Revit.dll', 'SolidGround.Core.dll') | ForEach-Object { Join-Path $payloadStagingDir $_ }
        $scriptsToSign = $InstallScriptNames | ForEach-Object { Join-Path $installStagingDir $_ }
        $filesToSign = @($dllsToSign) + @($scriptsToSign)

        $signResults = @(& $SignScriptPath -Path $filesToSign -RequireTimestamp -PinFilePath $PinFilePath -TimestampServer $TimestampServerUrl)

        foreach ($result in $signResults) {
            $null = Test-SolidGroundFileSignature -FilePath $result.Path -PinnedSha256 $pin.sha256
            if (-not $result.Timestamped) {
                throw "Refusing to package a release build: '$($result.Path)' was signed without an Authenticode timestamp. A real release must be fully timestamped (docs/architecture/revit-release-packaging-and-signing.md, 'Timestamp decision')."
            }
        }
    } else {
        Write-Warning '-Sign:$false set: producing an UNSIGNED-DRY-RUN artifact. This must never be published as a release.'
    }

    # Root-level files: INSTALL.md (verbatim copy of docs/revit-install-guide.md),
    # THIRD-PARTY-NOTICES, LICENSE -- one authored source each, copied at package time.
    Copy-Item -LiteralPath $InstallGuidePath -Destination (Join-Path $stagingRoot 'INSTALL.md') -Force
    Copy-Item -LiteralPath $ThirdPartyNoticesPath -Destination (Join-Path $stagingRoot 'THIRD-PARTY-NOTICES') -Force
    Copy-Item -LiteralPath $LicensePath -Destination (Join-Path $stagingRoot 'LICENSE') -Force

    # SHA256SUMS: every shipped file except itself, computed over the final (signed, if -Sign) bytes.
    $allStagedFiles = @(Get-ChildItem -LiteralPath $stagingRoot -Recurse -File)
    $sumsLines = foreach ($file in ($allStagedFiles | Sort-Object FullName)) {
        $relative = $file.FullName.Substring($stagingRoot.Length + 1).Replace('\', '/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        "$hash  $relative"
    }
    $sumsPath = Join-Path $stagingRoot 'SHA256SUMS'
    [System.IO.File]::WriteAllText($sumsPath, (($sumsLines -join "`n") + "`n"), [System.Text.UTF8Encoding]::new($false))

    if (-not (Test-Path -LiteralPath $OutputDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
    }
    if (Test-Path -LiteralPath $zipPath -PathType Leaf) {
        throw "Refusing to overwrite existing '$zipPath'."
    }

    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $zipPath -CompressionLevel Optimal

    $zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipHashPath = "$zipPath.sha256"
    [System.IO.File]::WriteAllText($zipHashPath, "$zipHash  $zipFileName`n", [System.Text.UTF8Encoding]::new($false))

    Write-Host ''
    Write-Host "Packaged $zipPath" -ForegroundColor Green
    Write-Host "SHA-256: $zipHash"
    if ($Sign) {
        $allTimestamped = -not [bool](@($signResults) | Where-Object { -not $_.Timestamped })
        Write-Host "Every signed file successfully Authenticode-timestamped (legacy protocol, never RFC 3161): $allTimestamped"
    } else {
        Write-Host 'UNSIGNED DRY RUN: this artifact is not signed and must never be published as a release.' -ForegroundColor Yellow
    }
} finally {
    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
