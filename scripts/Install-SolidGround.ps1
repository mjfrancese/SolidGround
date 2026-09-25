<#
.SYNOPSIS
    Installs SolidGround from an extracted release zip into a per-user Revit 2027 Add-Ins folder.

.DESCRIPTION
    Install-SolidGround.ps1 implements docs/architecture/revit-release-packaging-and-signing.md
    section 5, "Install-SolidGround.ps1 -- thin wrapper": a short, reviewable script that does four
    things Deploy-RevitAddIn.ps1 itself is never modified to do, because they are downloaded-zip-
    specific operator concerns, not developer-source-tree concerns:

      1. Unconditionally clears the Zone.Identifier mark-of-the-web stream from every extracted file
         (scripts and DLLs alike) under this zip, via Unblock-File. Belt and suspenders: modern managed
         runtimes removed CAS/zone-based assembly-load restrictions, so a MOTW-marked DLL should load
         identically to Revit either way, but unblocking is free and moots the question.
      2. The fail-closed integrity gate: every signed file under payload\ and install\ is checked
         with Get-AuthenticodeSignature against the SHA-256 hash pinned in this zip's own copy of
         signing-certificate.json. Fails closed on NotSigned, HashMismatch, NotSupportedFileFormat,
         Incompatible, or a signer certificate hash that does not match the pinned value -- a deny
         list, not an allow list of Valid/NotTrusted, since an untrusted self-signed certificate
         (the state of every SolidGround certificate before Import-SigningTrust.ps1 has run)
         empirically reports status UnknownError, not NotTrusted. This check never requires local
         trust to already be established.
      3. An informational-only trust check, never blocking: if the signing certificate is not yet
         present in both Cert:\LocalMachine\Root and Cert:\LocalMachine\TrustedPublisher, prints the
         next step (running Import-SigningTrust.ps1, elevated) and continues regardless. Trust import
         is not a precondition for installing or for Revit loading the add-in: an operator who skips
         it simply keeps seeing Revit's own signed-but-not-yet-trusted-publisher prompt on every
         launch -- a real, documented, supported alternative, not a silent gap.
      4. Forwards every argument this script itself did not otherwise consume (any switch or
         parameter Deploy-RevitAddIn.ps1 accepts, for example -AddinsDirectory, -WhatIf,
         -AllowOtherRevitVersions, -KeepPreviousVersions) to
         install\Deploy-RevitAddIn.ps1 -SourceDirectory <this zip's payload\> -- so this script never
         duplicates, and can never drift from, that already-evidenced script's own parameter set.

    This script takes no parameters of its own; anything you pass reaches Deploy-RevitAddIn.ps1
    unchanged. Run it from inside the extracted zip's install\ folder (double-click the sibling
    install.cmd in that same folder instead if you would rather not open a terminal).

.EXAMPLE
    .\Install-SolidGround.ps1
    Installs into the default per-user Revit 2027 Add-Ins folder
    ($env:APPDATA\Autodesk\Revit\Addins\2027).

.EXAMPLE
    .\Install-SolidGround.ps1 -WhatIf
    Reports what Deploy-RevitAddIn.ps1 would do without writing anything. Unblocking and the
    signature/trust checks above still run for real; only the final deploy step is simulated.

.EXAMPLE
    .\Install-SolidGround.ps1 -AddinsDirectory 'C:\Scratch\Addins\2027'
    Installs into a non-default Add-Ins folder, forwarded straight through to Deploy-RevitAddIn.ps1.

.NOTES
    Follows docs/architecture/revit-release-packaging-and-signing.md section 5. Never modifies
    Deploy-RevitAddIn.ps1; always forwards to it. Ships inside every release zip's install\ folder
    (copied there, then signed, by New-ReleasePackage.ps1) and is also committed at
    scripts/Install-SolidGround.ps1.
#>
#Requires -Version 5.1

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$Sha256AlgorithmName = [System.Security.Cryptography.HashAlgorithmName]::SHA256
# See Sign-RevitAddIn.ps1's own identical constant and comment: a deny list, not an allow list of
# ('Valid', 'NotTrusted') -- an untrusted self-signed certificate reports UnknownError, not NotTrusted.
$FailClosedSignatureStatuses = @('NotSigned', 'HashMismatch', 'NotSupportedFileFormat', 'Incompatible')
$PinFilePath = Join-Path $PSScriptRoot 'signing-certificate.json'
$PayloadDirectory = Join-Path $PSScriptRoot '..\payload'
$DeployScriptPath = Join-Path $PSScriptRoot 'Deploy-RevitAddIn.ps1'

# ----------------------------------------------------------------------------
# Helper functions
# ----------------------------------------------------------------------------

function Get-SolidGroundSigningCertificatePin {
    param([Parameter(Mandatory)][string]$PinFilePath)

    if (-not (Test-Path -LiteralPath $PinFilePath -PathType Leaf)) {
        throw "Signing certificate pin file not found: '$PinFilePath'. This extracted zip appears incomplete; re-download and re-extract the release before installing."
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
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string]$PinnedSha256
    )

    if (-not (Test-Path -LiteralPath $FilePath -PathType Leaf)) {
        throw "Cannot verify signature: '$FilePath' not found. This extracted zip appears incomplete; re-download and re-extract the release before installing."
    }

    $signature = Get-AuthenticodeSignature -FilePath $FilePath

    if ($signature.Status -in $FailClosedSignatureStatuses) {
        throw "'$FilePath' has Authenticode signature status '$($signature.Status)' (fails closed on $($FailClosedSignatureStatuses -join '/')): $($signature.StatusMessage). Refusing to install."
    }
    if ($null -eq $signature.SignerCertificate) {
        throw "'$FilePath' reports signature status '$($signature.Status)' but has no signer certificate. Refusing to install."
    }

    $actualSha256 = $signature.SignerCertificate.GetCertHashString($Sha256AlgorithmName)
    if ($actualSha256 -ne $PinnedSha256) {
        throw "'$FilePath' was signed by a certificate whose SHA-256 hash ($actualSha256) does not match the pinned SolidGround signing certificate ($PinnedSha256). Refusing to install a differently-signed file."
    }
}

# ----------------------------------------------------------------------------
# Step 1: unblock every extracted file (belt and suspenders; never fail the install over this)
# ----------------------------------------------------------------------------

$zipRoot = Join-Path $PSScriptRoot '..'
Get-ChildItem -LiteralPath $zipRoot -Recurse -File -ErrorAction SilentlyContinue |
    Unblock-File -ErrorAction SilentlyContinue

# ----------------------------------------------------------------------------
# Step 2: the fail-closed integrity gate
# ----------------------------------------------------------------------------

$pin = Get-SolidGroundSigningCertificatePin -PinFilePath $PinFilePath

$signedPayloadFiles = @('SolidGround.Revit.dll', 'SolidGround.Core.dll') | ForEach-Object { Join-Path $PayloadDirectory $_ }
$signedInstallFiles = @('Install-SolidGround.ps1', 'Uninstall-SolidGround.ps1', 'Deploy-RevitAddIn.ps1', 'Import-SigningTrust.ps1') | ForEach-Object { Join-Path $PSScriptRoot $_ }

foreach ($file in (@($signedPayloadFiles) + @($signedInstallFiles))) {
    Test-SolidGroundFileSignature -FilePath $file -PinnedSha256 $pin.sha256
}
Write-Host 'Every signed file matches the pinned SolidGround signing certificate.' -ForegroundColor Green

# ----------------------------------------------------------------------------
# Step 3: informational-only trust check -- never blocks, never self-elevates
# ----------------------------------------------------------------------------

$rootStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::Root,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
$trustedPublisherStore = [System.Security.Cryptography.X509Certificates.X509Store]::new(
    [System.Security.Cryptography.X509Certificates.StoreName]::TrustedPublisher,
    [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine)
try {
    $rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    $trustedPublisherStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)

    $inRoot = [bool]($rootStore.Certificates | Where-Object { $_.Thumbprint -eq $pin.thumbprint })
    $inTrustedPublisher = [bool]($trustedPublisherStore.Certificates | Where-Object { $_.Thumbprint -eq $pin.thumbprint })

    if ($inRoot -and $inTrustedPublisher) {
        Write-Host 'The SolidGround signing certificate is already trusted on this machine; Revit should load this build without a security prompt.' -ForegroundColor Green
    } else {
        Write-Host ''
        Write-Host 'The SolidGround signing certificate is not yet trusted on this machine.' -ForegroundColor Yellow
        Write-Host 'This is a supported, lower-trust alternative: Revit will show a one-time,'
        Write-Host 'signed-but-not-yet-trusted-publisher prompt each session until you either'
        Write-Host 'answer it or import trust once, as Administrator:'
        Write-Host ''
        Write-Host "    $(Join-Path $PSScriptRoot 'Import-SigningTrust.ps1')" -ForegroundColor Cyan
        Write-Host ''
        Write-Host 'That command adds the SolidGround certificate to this machine''s systemwide'
        Write-Host 'trusted-root and trusted-publisher stores -- comparable to trusting a new'
        Write-Host 'certificate authority, not merely "trust this one app." See INSTALL.md.'
        Write-Host ''
    }
} finally {
    $rootStore.Close()
    $trustedPublisherStore.Close()
}

# ----------------------------------------------------------------------------
# Step 4: forward every caller-supplied argument to Deploy-RevitAddIn.ps1, unchanged
# ----------------------------------------------------------------------------

& $DeployScriptPath -SourceDirectory $PayloadDirectory @args

Write-Host ''
Write-Host 'Install complete.' -ForegroundColor Green
Write-Host 'Fully restart Revit 2027 for this to take effect (add-ins are not hot-reloaded).'
