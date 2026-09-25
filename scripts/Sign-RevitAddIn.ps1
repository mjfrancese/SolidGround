<#
.SYNOPSIS
    Mints the SolidGround Revit add-in code-signing certificate, or signs one or more files with it.

.DESCRIPTION
    Sign-RevitAddIn.ps1 implements AGENTS.md's Issue #17 signing decision (self-signed,
    non-exportable Cert:\CurrentUser\My key, PowerShell-native, $0) and
    docs/architecture/revit-release-packaging-and-signing.md section 3.

    This script has two mutually exclusive modes:

    -NewCertificate mode mints the SolidGround signing certificate: it never assumes one is already
    present in Cert:\CurrentUser\My, and refuses outright, rather than rotating anything, if either
    the pin file already exists or a certificate with the same subject is already present. On
    success it writes the non-secret pin file
    (scripts/signing-certificate.json by default) that every other SolidGround signing/packaging/
    install script reads as the one source of truth for this certificate's identity -- no script in
    this repository parses a markdown document to learn it. Rotation (minting a replacement
    certificate once one already exists) is a documented manual procedure, not something this
    switch does.

    Default (sign) mode reads the pin file, finds the matching certificate in Cert:\CurrentUser\My,
    and signs every file named by -Path with Set-AuthenticodeSignature, always passing
    -HashAlgorithm SHA256 explicitly (Windows PowerShell 5.1's own cmdlet default is SHA1). Every
    signed file is independently re-verified afterward against the pinned certificate identity: the
    gate fails closed only on Authenticode status NotSigned, HashMismatch, NotSupportedFileFormat, or
    Incompatible, or on a signer certificate whose SHA-256 hash does not match the pinned value --
    this machine's own local trust-store state is never this gate's concern (trust import is a
    separate, later, elevated step). This is a deny list, not an allow list restricted to Valid or
    NotTrusted: empirically, a freshly minted, not-yet-trust-imported SolidGround certificate reports
    status UnknownError, not NotTrusted (NotTrusted is reserved for TRUST_E_EXPLICIT_DISTRUST, a
    different condition), so an allow list of only Valid/NotTrusted would reject every one of this
    project's own certificates until Import-SigningTrust.ps1 has run -- exactly the dependency on
    local trust-store state this gate must never have.

    Timestamping (the legacy Authenticode/PKCS#7 protocol Set-AuthenticodeSignature's own
    -TimestampServer parameter actually drives -- never RFC 3161) is always attempted against
    -TimestampServer. -RequireTimestamp makes a timestamp failure a fail-closed error (release
    packaging, via New-ReleasePackage.ps1); without it, a timestamp failure is a logged warning and
    the file is re-signed without one (ordinary dev-loop signing).

    This script never creates a .pfx file, never asks for or stores a password, and never exports
    the private key: the certificate's key is minted NonExportable and stays in the Windows-managed
    CNG key store for this Windows profile only.

.PARAMETER Path
    One or more files to sign (PE files such as .dll, or PowerShell .ps1 script files). Required
    unless -NewCertificate is specified. Sign the staged copy of a git-tracked .ps1 file outside the
    working tree -- never the committed source under scripts/ itself, which must stay plain,
    unsigned text.

.PARAMETER NewCertificate
    Mints the SolidGround signing certificate and writes -PinFilePath. Refuses if the pin file
    already exists, or if Cert:\CurrentUser\My already contains a certificate with the same subject.
    Mutually exclusive with -Path.

.PARAMETER RequireTimestamp
    Makes a failed or missing Authenticode timestamp a fail-closed error instead of a best-effort
    warning. Pass this for release packaging; omit it for ordinary dev-loop signing.

.PARAMETER PinFilePath
    Path to the committed, non-secret pin file recording the signing certificate's subject, SHA-256
    hash, SHA-1 thumbprint (informational only), and validity window. Default: signing-certificate.json
    next to this script.

.PARAMETER TimestampServer
    The legacy-protocol Authenticode timestamp server to use. Default: http://timestamp.digicert.com
    (must answer the legacy application/octet-stream request shape; a modern RFC-3161-only responder
    does not work with Set-AuthenticodeSignature).

.EXAMPLE
    .\Sign-RevitAddIn.ps1 -NewCertificate
    Mints the certificate (first-ever run only) and writes scripts/signing-certificate.json.

.EXAMPLE
    .\Sign-RevitAddIn.ps1 -Path .\src\SolidGround.Revit\bin\Release\net10.0-windows\SolidGround.Revit.dll, .\src\SolidGround.Revit\bin\Release\net10.0-windows\SolidGround.Core.dll
    Ordinary dev-loop signing before running Deploy-RevitAddIn.ps1: best-effort timestamp.

.EXAMPLE
    .\Sign-RevitAddIn.ps1 -Path $stagedDll -RequireTimestamp
    Release-packaging signing: a missing or failed timestamp is a fail-closed error.

.EXAMPLE
    .\Sign-RevitAddIn.ps1 -Path $stagedDll -WhatIf
    Reports which file(s) would be signed (or which certificate would be minted, for -NewCertificate
    -WhatIf) without writing anything. SupportsShouldProcess with ConfirmImpact 'Medium' means an
    ordinary call with neither -WhatIf nor -Confirm proceeds without prompting, matching this script's
    existing non-interactive callers (for example New-ReleasePackage.ps1).

.NOTES
    Follows docs/architecture/revit-release-packaging-and-signing.md section 3. The certificate is
    never committed or exported anywhere; only this non-secret pin file is. Owned entirely under
    scripts/; see scripts/README.md.
#>
#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Medium')]
param(
    [string[]]$Path,

    [switch]$NewCertificate,

    [switch]$RequireTimestamp,

    [string]$PinFilePath = (Join-Path $PSScriptRoot 'signing-certificate.json'),

    [string]$TimestampServer = 'http://timestamp.digicert.com'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# See Deploy-RevitAddIn.ps1's own identical comment: a script that declares SupportsShouldProcess
# leaves the automatic $WhatIfPreference variable set to $true for its whole top-level scope when
# invoked with -WhatIf, which would otherwise silently turn unrelated read-only calls into no-ops.
# Only the explicit $PSCmdlet.ShouldProcess() calls below should react to -WhatIf; it is unaffected
# by this override.
$WhatIfPreference = $false

# ----------------------------------------------------------------------------
# Constants
# ----------------------------------------------------------------------------
$CertificateSubject      = 'CN=SolidGround Revit Add-in Signing'
$CertificateFriendlyName = 'SolidGround Revit Add-in Signing'
$CodeSigningEkuOid       = '1.3.6.1.5.5.7.3.3'
$Sha256AlgorithmName     = [System.Security.Cryptography.HashAlgorithmName]::SHA256

# Deny list, not an allow list of ('Valid', 'NotTrusted'): empirically (Set-AuthenticodeSignature /
# Get-AuthenticodeSignature against this project's own not-yet-trust-imported certificate shape), an
# untrusted self-signed certificate's chain terminates as Authenticode status UnknownError, never
# NotTrusted -- so this gate must never depend on the checking machine's own local trust-store state,
# and must fail closed only on a status that actually indicates a real problem with the file or
# signature itself.
$FailClosedSignatureStatuses = @('NotSigned', 'HashMismatch', 'NotSupportedFileFormat', 'Incompatible')

# ----------------------------------------------------------------------------
# Argument validation
# ----------------------------------------------------------------------------

if ($NewCertificate -and $Path) {
    throw '-Path and -NewCertificate are mutually exclusive: -NewCertificate only mints the certificate and writes the pin file, and never signs anything in the same run.'
}
if (-not $NewCertificate -and (-not $Path -or @($Path).Count -eq 0)) {
    throw '-Path is required unless -NewCertificate is specified.'
}

# ----------------------------------------------------------------------------
# Helper functions
# ----------------------------------------------------------------------------

function Get-SolidGroundSigningCertificatePin {
    <#
        Reads and shape-validates the committed, non-secret pin file that every SolidGround signing/
        packaging/install script treats as the one source of truth for the signing certificate's
        identity. Fails closed with a message naming the remedy (-NewCertificate) when the file does
        not exist yet, rather than silently proceeding with no cross-check.
    #>
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
        Checks one file's Authenticode signature against the pinned certificate identity
        (docs/architecture/revit-release-packaging-and-signing.md section 3, "Signature verification
        gates"): fails closed on Status NotSigned, HashMismatch, NotSupportedFileFormat, Incompatible,
        a missing signer certificate, or a signer certificate whose SHA-256 hash does not equal the
        pinned value. Never depends on this machine's own local trust-store state -- see
        $FailClosedSignatureStatuses above for why this is a deny list, not an allow list of Valid/
        NotTrusted.
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
        throw "'$FilePath' was signed by a certificate whose SHA-256 hash ($actualSha256) does not match the pinned SolidGround signing certificate ($PinnedSha256). Refusing to trust a differently-signed file."
    }

    return [pscustomobject]@{
        Path   = $FilePath
        Status = [string]$signature.Status
    }
}

function Set-SolidGroundFileSignature {
    <#
        Signs one file with the pinned SolidGround signing certificate, always passing
        -HashAlgorithm SHA256 explicitly. Always attempts an Authenticode timestamp from
        -TimestampServer (the legacy Authenticode/PKCS#7 protocol this cmdlet actually drives, never
        RFC 3161). When $RequireTimestamp is set, a timestamp that cannot be obtained is a
        fail-closed error. When it is not set, a timestamp failure is a logged warning and the file
        is re-signed without one.
    #>
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)]$Certificate,
        [Parameter(Mandatory)][string]$TimestampServer,
        [Parameter(Mandatory)][bool]$RequireTimestamp
    )

    $signature = $null
    $timestampError = $null

    try {
        $signature = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $Certificate `
            -HashAlgorithm 'SHA256' -TimestampServer $TimestampServer -ErrorAction Stop
    } catch {
        $timestampError = $_
    }

    # Whether a timestamp was actually obtained is independent of the certificate's own trust status:
    # the timestamp authority (DigiCert) uses its own, separately trusted chain, so a signature can be
    # genuinely timestamped while $signature.Status is still UnknownError for the not-yet-trusted
    # SolidGround certificate itself. $signature is $null here only if Set-AuthenticodeSignature threw
    # (caught above), in which case $timestamped is correctly $false regardless of $timestampError.
    $timestamped = [bool]($signature -and $signature.TimeStamperCertificate)

    if (-not $timestamped) {
        if ($RequireTimestamp) {
            $detail = if ($timestampError) {
                $timestampError.Exception.Message
            } elseif ($signature) {
                "signature status '$($signature.Status)', no timestamp certificate present"
            } else {
                'no signature result was produced'
            }
            throw "Signing '$FilePath' with a required Authenticode timestamp from '$TimestampServer' failed: $detail. A release build must not ship an untimestamped signature (docs/architecture/revit-release-packaging-and-signing.md, 'Timestamp decision')."
        }

        $reason = if ($timestampError) { $timestampError.Exception.Message } else { "signature status '$($signature.Status)', no timestamp certificate present" }
        Write-Warning "Timestamping '$FilePath' via '$TimestampServer' did not succeed ($reason); re-signing without a timestamp. Dev-loop signing is best-effort per docs/architecture/revit-release-packaging-and-signing.md."
        $signature = Set-AuthenticodeSignature -FilePath $FilePath -Certificate $Certificate -HashAlgorithm 'SHA256' -ErrorAction Stop
    }

    if ($signature.Status -in $FailClosedSignatureStatuses) {
        throw "Signing '$FilePath' produced Authenticode status '$($signature.Status)' (fails closed on $($FailClosedSignatureStatuses -join '/')): $($signature.StatusMessage)"
    }

    return [pscustomobject]@{
        Path        = $FilePath
        Status      = [string]$signature.Status
        Timestamped = $timestamped
    }
}

# ----------------------------------------------------------------------------
# -NewCertificate mode
# ----------------------------------------------------------------------------

if ($NewCertificate) {
    if (Test-Path -LiteralPath $PinFilePath -PathType Leaf) {
        throw "Refusing to mint a new certificate: pin file '$PinFilePath' already exists. Rotating the signing certificate is a documented manual procedure (docs/architecture/revit-release-packaging-and-signing.md, 'Key loss, rotation, and revocation'); this script never overwrites an existing pin file."
    }

    $existing = @(Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $CertificateSubject })
    if ($existing.Count -gt 0) {
        throw "Refusing to mint a new certificate: Cert:\CurrentUser\My already contains a certificate with subject '$CertificateSubject' (thumbprint $($existing[0].Thumbprint)). Remove it first, or treat this as a documented manual rotation rather than a fresh mint."
    }

    if ($PSCmdlet.ShouldProcess($PinFilePath, "mint the SolidGround signing certificate in Cert:\CurrentUser\My and write $PinFilePath")) {
        $cert = New-SelfSignedCertificate `
            -Subject $CertificateSubject `
            -Type CodeSigningCert `
            -KeyAlgorithm RSA -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy NonExportable `
            -KeyUsage DigitalSignature `
            -CertStoreLocation Cert:\CurrentUser\My `
            -NotAfter (Get-Date).AddYears(10) `
            -FriendlyName $CertificateFriendlyName

        try {
            $hasCodeSigningEku = [bool](@($cert.EnhancedKeyUsageList) | Where-Object { $_.ObjectId -eq $CodeSigningEkuOid -or $_.FriendlyName -match 'Code Signing' })
            if (-not $hasCodeSigningEku) {
                throw "Newly minted certificate (thumbprint $($cert.Thumbprint)) does not carry the Code Signing EKU ($CodeSigningEkuOid) as expected."
            }

            # X509Certificate2 has no .BasicConstraints property -- reading it under
            # Set-StrictMode -Version Latest throws PropertyNotFoundException ("The property
            # 'BasicConstraints' cannot be found on this object"). The real check for "is this a CA
            # certificate" is an X509BasicConstraintsExtension inside .Extensions; a code-signing
            # end-entity certificate may legitimately have no such extension at all, so its absence is
            # fine and only its presence with CertificateAuthority = $true is the failure condition.
            # The inner @() wraps the Where-Object result before .Count is read: a single match
            # collapses to a bare scalar extension object with no .Count property of its own, which
            # would itself throw under strict mode if read directly (confirmed empirically while
            # building this fix).
            $basicConstraintsExtensions = @(@($cert.Extensions) | Where-Object {
                $_ -is [System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]
            })
            if ($basicConstraintsExtensions.Count -gt 0 -and $basicConstraintsExtensions[0].CertificateAuthority) {
                throw "Newly minted certificate (thumbprint $($cert.Thumbprint)) is marked as a certificate authority, which is not expected for an end-entity code-signing certificate."
            }

            $sha256 = $cert.GetCertHashString($Sha256AlgorithmName)

            $pin = [ordered]@{
                subject    = $cert.Subject
                sha256     = $sha256
                thumbprint = $cert.Thumbprint
                notBefore  = $cert.NotBefore.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
                notAfter   = $cert.NotAfter.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            }

            $pinJson = $pin | ConvertTo-Json
            # Matches Deploy-RevitAddIn.ps1's own WriteAllText/UTF8Encoding($false) convention: writes plain
            # UTF-8 with no BOM in a way that behaves identically on Windows PowerShell 5.1 and PowerShell 7,
            # unlike Set-Content -Encoding (whose accepted values differ between those two editions).
            [System.IO.File]::WriteAllText($PinFilePath, $pinJson, [System.Text.UTF8Encoding]::new($false))
        } catch {
            # Any failure past this point -- a failed post-creation check, or a failed pin-file write --
            # must never leave an orphaned certificate that no pin file records. Remove the just-created
            # certificate, and its private key, from Cert:\CurrentUser\My before rethrowing.
            #
            # Cert:\CurrentUser\CA is also checked, because a public-only copy of a freshly minted
            # certificate landing there is expected, observed behavior on this workstation, not a
            # defensive edge case: a real -NewCertificate mint on 2026-09-24 (subject "CN=SolidGround
            # Revit Add-in Signing", thumbprint EEEAD0AD06069A56C44E09C1FBB26B59FA902270) placed the
            # certificate in Cert:\CurrentUser\My as requested AND a public-only copy in Cert:\CurrentUser\CA
            # (before/after store snapshots recorded this session), despite Microsoft's New-SelfSignedCertificate
            # documentation stating -CertStoreLocation "does not support other certificate stores." This
            # CA-store copy is harmless: it carries no private key (Windows' own certificate-store
            # plumbing mirrors an issuer/self-signer's public certificate into CA on this workstation
            # independently of what -CertStoreLocation names), and Cert:\CurrentUser\CA confers no root
            # trust of its own -- only Cert:\LocalMachine\Root and Cert:\LocalMachine\TrustedPublisher
            # (via Import-SigningTrust.ps1) do that. Cleanup still removes it below so a failed mint never
            # leaves any trace behind, but its presence on a successful mint is not itself a problem.
            Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($cert.Thumbprint)" -DeleteKey -Force -ErrorAction SilentlyContinue
            $caCopy = @(Get-ChildItem -Path Cert:\CurrentUser\CA -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq $cert.Thumbprint })
            if ($caCopy.Count -gt 0) {
                Remove-Item -LiteralPath "Cert:\CurrentUser\CA\$($cert.Thumbprint)" -Force -ErrorAction SilentlyContinue
            }
            Write-Warning "Removed the just-minted certificate (thumbprint $($cert.Thumbprint)) from Cert:\CurrentUser\My (and Cert:\CurrentUser\CA if a copy was found there) because: $($_.Exception.Message)"
            throw
        }

        Write-Host 'Minted the SolidGround signing certificate:' -ForegroundColor Green
        Write-Host "  Subject:                       $($pin.subject)"
        Write-Host "  SHA-256:                       $($pin.sha256)"
        Write-Host "  Thumbprint (SHA-1, informational only): $($pin.thumbprint)"
        Write-Host "  Not before:                    $($pin.notBefore)"
        Write-Host "  Not after:                     $($pin.notAfter)"
        Write-Host "Pin file written to '$PinFilePath'. Commit this file so Sign-RevitAddIn.ps1,"
        Write-Host 'New-ReleasePackage.ps1, Install-SolidGround.ps1, and Import-SigningTrust.ps1 can all'
        Write-Host 'verify against it.'
    }

    return
}

# ----------------------------------------------------------------------------
# Default (sign) mode
# ----------------------------------------------------------------------------

$pin = Get-SolidGroundSigningCertificatePin -PinFilePath $PinFilePath

$candidates = @(Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $pin.subject })
if ($candidates.Count -eq 0) {
    throw "No certificate with subject '$($pin.subject)' was found in Cert:\CurrentUser\My. Run '.\Sign-RevitAddIn.ps1 -NewCertificate' once on this workstation, or import the existing SolidGround signing certificate into this profile's store before signing."
}

$matching = @($candidates | Where-Object { $_.GetCertHashString($Sha256AlgorithmName) -eq $pin.sha256 })
if ($matching.Count -eq 0) {
    throw "Cert:\CurrentUser\My has $($candidates.Count) certificate(s) with subject '$($pin.subject)', but none match the pinned SHA-256 hash '$($pin.sha256)' from '$PinFilePath'. Refusing to sign with an unpinned certificate."
}
if ($matching.Count -gt 1) {
    throw "Cert:\CurrentUser\My has more than one certificate matching both the pinned subject and the pinned SHA-256 hash from '$PinFilePath'. Refusing to guess which one to sign with."
}
$signingCertificate = $matching[0]

$results = foreach ($file in $Path) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Cannot sign '$file': file not found."
    }
    $resolvedPath = (Resolve-Path -LiteralPath $file).ProviderPath

    if ($PSCmdlet.ShouldProcess($resolvedPath, 'sign with the pinned SolidGround signing certificate')) {
        $signResult = Set-SolidGroundFileSignature -FilePath $resolvedPath -Certificate $signingCertificate `
            -TimestampServer $TimestampServer -RequireTimestamp ([bool]$RequireTimestamp)

        # Post-sign gate (Draft 3 ruling R1): re-read the signature independently of
        # Set-AuthenticodeSignature's own return value and re-check the signer's identity against the
        # pinned certificate hash, exactly like every other gate in this design.
        $verify = Test-SolidGroundFileSignature -FilePath $resolvedPath -PinnedSha256 $pin.sha256

        Write-Host "Signed $resolvedPath (status $($verify.Status); timestamped: $($signResult.Timestamped))"

        [pscustomobject]@{
            Path        = $resolvedPath
            Status      = $verify.Status
            Timestamped = $signResult.Timestamped
        }
    }
}

$results
