<#
.SYNOPSIS
    One-time, per-workstation, elevated import of the SolidGround signing certificate into the
    machine's trusted-root and trusted-publisher stores.

.DESCRIPTION
    Import-SigningTrust.ps1 implements docs/architecture/revit-release-packaging-and-signing.md
    section 3, "Trust-import procedure per workstation": the one mechanism this project's own
    research has found durably suppresses Revit's unsigned/untrusted-publisher add-in prompt for a
    SolidGround build, at the cost of adding SolidGround's self-signed certificate to this machine's
    systemwide trusted-root list -- comparable to trusting a new certificate authority, not merely
    "trust this one app." Skipping this script entirely is a supported, documented, lower-trust
    alternative: an operator who never runs it simply keeps seeing Revit's own per-session prompt.

    This script requires an already-elevated (Run as Administrator) PowerShell session and fails
    closed with a clear message if it is not elevated, rather than silently trying to self-elevate --
    an operator should never be surprised by an unexpected UAC prompt from a downloaded script.

    Because this script declares SupportsShouldProcess with ConfirmImpact 'High', PowerShell itself
    tries to show an interactive Y/N confirmation prompt before actually adding the certificate to
    either store. In a non-interactive session -- for example, automation whose console input is
    redirected -- that prompt cannot be shown, and $PSCmdlet.ShouldProcess() throws instead of
    prompting. Pass -Confirm:$false to run this script from such a session (this only suppresses the
    confirmation prompt; the elevation check and the signature/pin cross-check above still run and
    can still fail closed); pass -WhatIf to preview instead. This script converts that failure into a
    clear, actionable error rather than letting the underlying exception surface uninformatively.

    Before touching either certificate store, it reads the signing certificate's identity from the
    already-signed -DllPath and cross-checks its SHA-256 hash against the pinned value in
    -PinFilePath (scripts/signing-certificate.json in a repository checkout; the same file's copy
    shipped inside install/ when run from an extracted release zip) -- the same
    NotSigned/HashMismatch/NotSupportedFileFormat/signer-hash-mismatch gate every other SolidGround
    signing/packaging/install script uses. -Force overrides only the signer-hash cross-check, for the
    rare, deliberate case of trusting a differently-signed DLL; it prints a loud warning when used.
    It never overrides -DllPath having no signature at all.

    Both the -Thumbprint (SHA-1, informational only -- X509Certificate2.Thumbprint always uses
    SHA-1) and the SHA-256 hash are printed either way, so an operator can also cross-check them
    manually against the value published over HTTPS in
    docs/architecture/revit-release-packaging-and-signing.md before trusting anything locally.

    Adding to Cert:\LocalMachine\Root and Cert:\LocalMachine\TrustedPublisher is idempotent: each
    store is checked by thumbprint first and left alone (not treated as an error) if the certificate
    is already present.

    Removal is a deliberately manual, two-line reverse, not a switch this script implements:

        Remove-Item "Cert:\LocalMachine\Root\<thumbprint>"
        Remove-Item "Cert:\LocalMachine\TrustedPublisher\<thumbprint>"

    (both also require an elevated session). This keeps the initial scope to only what
    docs/architecture/revit-release-packaging-and-signing.md actually calls for; a -Remove switch is
    the natural home for this if it is ever needed more than rarely.

.PARAMETER DllPath
    Path to an already-signed SolidGround file to read the certificate from. Default: the sibling
    payload\SolidGround.Revit.dll when run from inside an extracted release zip's install\ folder;
    falls back to the local dev build output (src\SolidGround.Revit\bin\<Configuration>\net10.0-windows)
    when no such sibling exists, so this script is also usable directly from a repository checkout.

.PARAMETER PinFilePath
    Path to the pin file recording the signing certificate's pinned subject and SHA-256 hash.
    Default: signing-certificate.json next to this script.

.PARAMETER Force
    Overrides a signer-hash mismatch between -DllPath's actual signer and -PinFilePath's pinned
    value (never overrides a missing signature). Prints a loud warning when used. Use only to
    deliberately trust a differently-signed DLL.

.PARAMETER Configuration
    Used only to compute -DllPath's local-dev-build fallback default. Ignored if -DllPath is
    supplied explicitly, or if the extracted-zip sibling payload\SolidGround.Revit.dll exists.
    Default: Release.

.EXAMPLE
    .\Import-SigningTrust.ps1
    Run from inside an extracted release zip's install\ folder, in an elevated, interactive
    PowerShell session: imports trust for payload\SolidGround.Revit.dll, prompting once to confirm.

.EXAMPLE
    .\Import-SigningTrust.ps1 -Confirm:$false
    Same, but for a non-interactive/automated invocation (for example, a redirected console) that
    cannot service the interactive confirmation prompt above and would otherwise see
    $PSCmdlet.ShouldProcess() throw instead of prompting.

.EXAMPLE
    .\Import-SigningTrust.ps1 -WhatIf
    Reports which store(s) would be added to, without writing anything. Read-only checks (elevation,
    signature, pin cross-check) still run and can still report a problem.

.NOTES
    Follows docs/architecture/revit-release-packaging-and-signing.md section 3. Requires elevation;
    the resulting systemwide trust change is deliberate and durable across this machine's future
    SolidGround add-in updates signed by the same certificate. Owned entirely under scripts/; see
    scripts/README.md.
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'High')]
param(
    [string]$DllPath,

    [string]$PinFilePath = (Join-Path $PSScriptRoot 'signing-certificate.json'),

    [switch]$Force,

    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# See Deploy-RevitAddIn.ps1's own identical comment: a script that declares SupportsShouldProcess
# leaves $WhatIfPreference set to $true for its whole top-level scope when invoked with -WhatIf,
# which would otherwise silently turn unrelated read-only calls (Get-AuthenticodeSignature,
# Get-ChildItem Cert:\..., Test-Path) into no-ops. Only $PSCmdlet.ShouldProcess() below should react
# to -WhatIf; it is unaffected by this override.
$WhatIfPreference = $false

$Sha256AlgorithmName = [System.Security.Cryptography.HashAlgorithmName]::SHA256

# ----------------------------------------------------------------------------
# Helper functions
# ----------------------------------------------------------------------------

function Test-IsElevatedAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-SolidGroundSigningCertificatePin {
    param([Parameter(Mandatory)][string]$PinFilePath)

    if (-not (Test-Path -LiteralPath $PinFilePath -PathType Leaf)) {
        throw "Signing certificate pin file not found: '$PinFilePath'. This file ships inside every release zip's install\ folder and is committed at scripts/signing-certificate.json in a repository checkout; it cannot be reconstructed by this script."
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

function Add-SolidGroundTrustedCertificate {
    <#
        Adds $Certificate to the named LocalMachine store via the X509Store API, checking by
        thumbprint first and treating "already present" as a no-op, not an error.
    #>
    param(
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate,
        [Parameter(Mandatory)][System.Security.Cryptography.X509Certificates.StoreName]$StoreName
    )

    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $StoreName, [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
    try {
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

        $alreadyPresent = [bool]($store.Certificates | Where-Object { $_.Thumbprint -eq $Certificate.Thumbprint })
        if ($alreadyPresent) {
            Write-Host "Cert:\LocalMachine\$StoreName already contains thumbprint $($Certificate.Thumbprint); nothing to do."
            return
        }

        $description = "add certificate (subject '$($Certificate.Subject)', thumbprint $($Certificate.Thumbprint)) to Cert:\LocalMachine\$StoreName"

        # $PSCmdlet.ShouldProcess() itself throws (a bare NullReferenceException on Windows PowerShell
        # 5.1, "Windows PowerShell is in NonInteractive mode" on PowerShell 7) when it needs to show
        # its own interactive confirmation prompt but this session's console input is redirected --
        # for example, non-interactive automation. Converting that into a clear, actionable error here
        # (rather than letting either cryptic exception surface directly) is this script's own
        # supported behavior for that case; -Confirm:$false avoids it entirely by skipping the prompt.
        try {
            $shouldProcess = $PSCmdlet.ShouldProcess("Cert:\LocalMachine\$StoreName", $description)
        } catch {
            throw "Import-SigningTrust.ps1 could not show its own confirmation prompt to $description, most likely because this session's console input is redirected (a non-interactive/automated invocation cannot service an interactive Y/N prompt). Re-run with -Confirm:`$false to proceed without prompting -- the elevation, signature, and pin cross-checks above already ran and still fail closed either way -- or with -WhatIf to preview. Original error: $($_.Exception.Message)"
        }

        if ($shouldProcess) {
            $store.Add($Certificate)
            Write-Host "Added thumbprint $($Certificate.Thumbprint) to Cert:\LocalMachine\$StoreName." -ForegroundColor Green
        }
    } finally {
        $store.Close()
    }
}

# ----------------------------------------------------------------------------
# Elevation check -- fail closed, never self-elevate
# ----------------------------------------------------------------------------

if (-not (Test-IsElevatedAdministrator)) {
    throw 'Import-SigningTrust.ps1 requires an elevated (Run as Administrator) PowerShell session. Re-open PowerShell as Administrator and run this script again; it never tries to elevate itself.'
}

# ----------------------------------------------------------------------------
# Resolve -DllPath's default
# ----------------------------------------------------------------------------

if (-not $DllPath) {
    $siblingPayloadDll = Join-Path $PSScriptRoot '..\payload\SolidGround.Revit.dll'
    if (Test-Path -LiteralPath $siblingPayloadDll -PathType Leaf) {
        $DllPath = $siblingPayloadDll
    } else {
        $repoRoot = Split-Path -Parent $PSScriptRoot
        $DllPath = Join-Path $repoRoot "src\SolidGround.Revit\bin\$Configuration\net10.0-windows\SolidGround.Revit.dll"
    }
}

if (-not (Test-Path -LiteralPath $DllPath -PathType Leaf)) {
    throw "Cannot import trust: '$DllPath' not found."
}

# ----------------------------------------------------------------------------
# Read the signer certificate and cross-check it against the pin
# ----------------------------------------------------------------------------

$pin = Get-SolidGroundSigningCertificatePin -PinFilePath $PinFilePath

$signature = Get-AuthenticodeSignature -FilePath $DllPath
if ($null -eq $signature.SignerCertificate) {
    throw "'$DllPath' has Authenticode signature status '$($signature.Status)' and no signer certificate. Refusing to import trust for an unsigned file."
}

$certificate = $signature.SignerCertificate
$actualSha256 = $certificate.GetCertHashString($Sha256AlgorithmName)

Write-Host "Signer certificate read from '$DllPath':"
Write-Host "  Subject:                                $($certificate.Subject)"
Write-Host "  Thumbprint (SHA-1, informational only): $($certificate.Thumbprint)"
Write-Host "  SHA-256:                                 $actualSha256"
Write-Host 'Cross-check these values against docs/architecture/revit-release-packaging-and-signing.md'
Write-Host '(viewable over HTTPS from GitHub) before proceeding.'

if ($actualSha256 -ne $pin.sha256) {
    if (-not $Force) {
        throw "'$DllPath' was signed by a certificate whose SHA-256 hash ($actualSha256) does not match the pinned SolidGround signing certificate ($($pin.sha256)) from '$PinFilePath'. Refusing to import trust for a differently-signed file. Pass -Force only if you have independently verified this is expected (for example, a deliberate certificate rotation)."
    }
    Write-Warning "Proceeding despite a signer-hash mismatch because -Force was specified: expected $($pin.sha256), found $actualSha256."
}

# ----------------------------------------------------------------------------
# Import into both LocalMachine stores, idempotently
# ----------------------------------------------------------------------------

Add-SolidGroundTrustedCertificate -Certificate $certificate -StoreName Root
Add-SolidGroundTrustedCertificate -Certificate $certificate -StoreName TrustedPublisher

Write-Host ''
Write-Host 'Trust import complete. Fully restart Revit for this to take effect; SolidGround builds signed'
Write-Host 'by this certificate should no longer show an unsigned/untrusted-publisher prompt.'
