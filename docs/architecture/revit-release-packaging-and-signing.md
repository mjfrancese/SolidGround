# Revit release packaging and signing

**Status.** DESIGN-stage, SCRIPTS-stage, and the live Revit 2027 manual evidence plan are all complete.
`scripts/Sign-RevitAddIn.ps1 -NewCertificate` was run for real on 2026-09-24 local time (2026-09-25T02:35Z) and
minted the SolidGround signing certificate; see "Certificate creation parameters" and "The signing certificate
pin file" below for its recorded identity. A 2026-09-25 live Revit 2027 session then built, signed, and
packaged a release candidate from commit `a515f0e` (zip SHA-256
`6456AFFE8C83223EA67DCFBA83C58276153B36F224EBE222A02455EE2BF8B36D`) and ran the full "Manual evidence plan"
below end to end against a real Revit 2027 (build 27.0.10.13, i.e. update 2027.0.1) process on Windows 11; its
results are recorded in "Evidence" below and in each numbered step's own Evidence paragraph.
`Directory.Build.props` now declares `<Version>0.1.0</Version>`; `scripts/Sign-RevitAddIn.ps1`,
`scripts/Import-SigningTrust.ps1`, `scripts/Uninstall-SolidGround.ps1`, `scripts/New-ReleasePackage.ps1`,
`scripts/Install-SolidGround.ps1`, and `scripts/install.cmd` all exist and are described below directly from
their own committed source, and have now all been run for real (see "Evidence" below).
`scripts/signing-certificate.json` — the pin file every signing/packaging/install script treats as its one
source of truth for the certificate's identity — now exists, written by that same mint; its committed values
are quoted directly in "The signing certificate pin file" below. **What remains open**: AC6 (the owner's
acceptance of this end-to-end result) and, gated on it, the GitHub Release publication described in "GitHub
Release" below — neither has happened yet. It follows the structure, tone, and citation style of
`docs/architecture/revit-toposolid-creation.md`, `docs/architecture/revit-extensible-storage-provenance.md`,
and `docs/architecture/revit-ribbon-icons.md`, and is synthesized from Issue #17's own multi-proposal,
four-review design record (`design-record.md`, Draft 4; not committed to this repository) plus a read-only
research pass over Autodesk's current Revit 2027 documentation and Microsoft's PowerShell documentation
(`understand/05-revit2027-signing-install-research.md`, not committed).

## Scope and acceptance criteria

GitHub Issue #17, "[Phase 2] Package, install, and validate the Revit 2027 release": produce a repeatable
Revit 2027 installation and validation path, then demonstrate the complete lot-to-Toposolid workflow in the
supported host. Its six acceptance criteria:

| # | Acceptance criterion |
| --- | --- |
| AC1 | A clean supported workstation can install and load SolidGround in Revit 2027 using documented steps. |
| AC2 | The example-site scenario creates a bounded Toposolid and retains readable reversible provenance after save/reopen. |
| AC3 | The reviewed 16×16 and 32×32 icons render correctly in the supported Revit ribbon contexts. |
| AC4 | No native geospatial binaries or Autodesk assemblies are committed or bundled unlawfully. |
| AC5 | README retains the site-form accuracy limit and does not claim survey-grade output. |
| AC6 | The owner accepts the Revit 2027 end-to-end result. |

Scope, per the issue: package only managed dependencies and install per-user by default; use API-reported
add-in locations whenever runtime path discovery is needed; run the approved Revit build path and the full
Revit-independent suite; validate loading, command use, ribbon assets, Toposolid geometry, provenance,
save/reopen behavior, and useful failure paths. See "What this note does not do" below for what stays out of
scope.

## Decisions

The owner and the orchestrator made the following decisions and rulings on 2026-09-24, before any of this
issue's implementation work began. They are the accepted direction this note and the shipped scripts follow;
nothing below revisits them.

| # | Decision | This note's answer |
| --- | --- | --- |
| D1 | Signing: **"Self-signed (Recommended)"** — a non-exportable `Cert:\CurrentUser\My` key, PowerShell-native, one-time admin trust import per workstation, $0. | "Code signing" below. |
| D2 | Packaging: **"Zip + GitHub Release (Recommended)"** — a local script builds/signs/stages a versioned zip; publish as a GitHub Release, tag `v0.1.0`, only after end-to-end acceptance. | "Release package" and "GitHub Release" below. |
| D3 | Versioning: **"0.1.0 + commit (Recommended)"** — one `<Version>` in `Directory.Build.props`; the informational version carries the git commit SHA through the .NET SDK's own automatic source-revision embedding. | "Versioning" below. |
| D4 | Clean-PC validation: **"Simulated clean here (Recommended)"** — hash and back up only SolidGround-owned state, remove it, install from the zip by the written guide, then restore; Revit and pyRevit themselves are not reinstalled. | "Manual evidence plan" below. |
| Ruling | All-user install stays out of scope; any all-user path still comes from the runtime API, never hardcoded. | No new all-user code path exists anywhere in this issue's scripts; the existing `NoRevitProjectOrScriptFileHardcodesAnAllUserAddInPath` guardrail already scans every new script. |
| Ruling | Ship `THIRD-PARTY-NOTICES` (NetTopologySuite BSD-3-Clause, ProjNET LGPL-2.1-or-later) without outside counsel review. | Drafted at the repository root; see "Notices, license, install guide" below. |
| R1 | Mint → sign → package order; the packaging/install signature gate fails closed only on `NotSigned`/`HashMismatch`/`NotSupportedFileFormat`/`Incompatible`/a signer-hash mismatch — a deny list, never an allow list restricted to `Valid`/`NotTrusted` (see "Signature verification gates" below for why that allow list does not actually work). | "Signature verification gates" below. |
| R2 | Certificate minting is ordinary, D1-authorized, non-elevated work; the elevated trust import is orchestrator-run with the owner approving the UAC prompt personally (a secure-desktop prompt cannot be automated). | "Who mints, who imports trust, and when" below. |
| R3 | The clean-machine snapshot covers all five relevant certificate stores, not registry state alone. | "Manual evidence plan," Stage 1.2. |
| R4 | The untrusted-first-launch dialog is required, live-evidenced runbook content, sequenced before trust import; one unified button rule everywhere: "Load Once" if offered, else "Do Not Load," never "Always Load." | "Manual evidence plan," Stages 2.2–2.5; `docs/revit-install-guide.md`. |
| R5 | Both Preflight failure paths (missing key, over-threshold `pointBudget`) are exercised together in one `fetch`-mode launch, because `RunDocumentPreflight` already accumulates every problem into one list before returning. | "Manual evidence plan," Stage 2.10. |
| R6 | The example-site demonstration is a live `fetch`-mode OpenTopography call, with a documented CLI/process-mode fallback on a 401/403. | "Manual evidence plan," Stage 2.7. |
| R7 | The reader add-in used for the provenance read-back is a throwaway `Type="Command"` add-in with its own `VendorId`; that manifest claim is verified against a primary Revit 2027 source before the add-in is built. | "Manual evidence plan," Stage 1.5. |
| R8 | Release output goes to `artifacts/release/` (a subfolder of the pre-existing, already git-ignored `artifacts/` bucket); no xUnit test may ever shell out to PowerShell. | "Release package" below; "Tests and offline verification" below. |
| R9 | Fail-closed Authenticode timestamping (the legacy Authenticode/PKCS#7 protocol — confirmed **not** RFC 3161) for release packages, using `http://timestamp.digicert.com` and explicit `-HashAlgorithm SHA256`; best-effort for dev-loop signing. | "Timestamp decision" below. |
| R10 | `scripts/Deploy-RevitAddIn.ps1` stays unchanged; dev-loop signing is an optional step before it. | "Reuse, never wrap, Deploy-RevitAddIn.ps1" below. |
| R11 | A full final-machine-state checklist is confirmed item by item, not "restore succeeded" generically. | "Manual evidence plan," Stage 2.13. |
| R12 | The certificate's non-secret identity is pinned in a committed, machine-readable `scripts/signing-certificate.json`, minted by `Sign-RevitAddIn.ps1 -NewCertificate`; every signing/packaging/install script reads that file, never a markdown document. | "The signing certificate pin file" below. |
| R15 | The release tag targets the exact commit the validated zip was built from. | "GitHub Release" below. |
| R16 | `scripts/Deploy-RevitAddIn.ps1` stays unchanged; every new script matches its comment-based-help, `Set-StrictMode`, and `$ErrorActionPreference = 'Stop'` conventions, and runs under both Windows PowerShell 5.1 and PowerShell 7. | Confirmed directly against the shipped scripts below. |

## Versioning

One line added to `Directory.Build.props`'s existing `<PropertyGroup>`:

```xml
<Version>0.1.0</Version>
```

Placed at the root, inherited by all four projects (`SolidGround.Core`, `SolidGround.Cli`,
`SolidGround.Tests`, `SolidGround.Revit`) the same way `TreatWarningsAsErrors` already is. The .NET SDK
embeds the full 40-character git commit SHA into `AssemblyInformationalVersion` automatically, with no
`SourceRevisionId`/SourceLink/`<Version>` configuration beyond this one line — so a built
`SolidGround.Revit.dll`'s informational version now reads `0.1.0+<40-char commit SHA>`. The owner's own D3
illustration used a short SHA (`0.1.0+11195a1`); the SDK's actual behavior produces the full 40-character
form, which is strictly more precise and needs no extra configuration — the short, human-scale form still
appears in the release zip's filename and the git tag, computed separately at package time via
`git rev-parse --short HEAD`. `scripts/New-ReleasePackage.ps1` reads `<Version>` directly from
`Directory.Build.props` (via `Get-PinnedVersionFromDirectoryBuildProps`, an `[xml]` parse of the file) unless
`-Version` overrides it for a local dry run.

Whether a *dirty* working tree changes, suffixes, or leaves unchanged the embedded SHA is not resolved by
inspection alone; this does not block adopting `<Version>`, because `New-ReleasePackage.ps1`'s own clean-tree
precondition (below) is what actually guarantees a shipped binary's embedded commit hash is truthful,
independent of whatever the SDK's own git-dirty detection does. **Evidenced (2026-09-25):** the real build
produced for the live Revit 2027 session (Stage 1.1) resolved to `FileVersion` `0.1.0.0` when read back live at
Stage 2.12's final-machine-state check, confirming `<Version>0.1.0</Version>` reaches the shipped binary as
designed; this session's evidence did not separately capture the full `AssemblyInformationalVersion` string
with its embedded commit SHA in writing.

`tests/SolidGround.Tests/ArchitectureTests.cs`'s `DirectoryBuildPropsDeclaresThePinnedVersion` parses
`Directory.Build.props` as XML and asserts exactly one `<Version>` element with the literal, hardcoded value
`0.1.0` — pinned the same way `RevitHostFilesTests.cs`'s `ExpectedAddInId` constant is pinned, so an
accidental revert or deletion of `<Version>` is caught immediately rather than silently reverting to the
SDK's implicit `1.0.0` default. `BuildIdentity.cs`'s `InformationalVersion` capture and the Issue #16
Extensible Storage schema's `informationalVersion` field need no code change: the value they carry simply
starts reading `0.1.0+<sha>` instead of `1.0.0+<sha>`, a value change, not a schema change.

**Bump procedure**, manual, by hand, per release: edit `<Version>` in `Directory.Build.props`, commit, run
the offline suite, then run `scripts/New-ReleasePackage.ps1` against that commit. No automated bump tool
(Nerdbank.GitVersioning, GitVersion, MinVer) — matching this project's single-operator, infrequent-release
cadence and `docs/architecture/revit-add-in-conventions.md`'s own stated preference for "a simple, idiomatic
`<Version>`."

## Code signing

### Certificate creation parameters

`scripts/Sign-RevitAddIn.ps1 -NewCertificate` mints the certificate once, in `Cert:\CurrentUser\My`:

```powershell
New-SelfSignedCertificate `
    -Subject "CN=SolidGround Revit Add-in Signing" `
    -Type CodeSigningCert `
    -KeyAlgorithm RSA -KeyLength 2048 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy NonExportable `
    -KeyUsage DigitalSignature `
    -CertStoreLocation Cert:\CurrentUser\My `
    -NotAfter (Get-Date).AddYears(10) `
    -FriendlyName "SolidGround Revit Add-in Signing"
```

- **Subject**: `CN=SolidGround Revit Add-in Signing` — distinct from, but mirroring, the same subject-naming
  convention already used for the owner's other Revit add-in's own code-signing certificate.
- **`KeyExportPolicy = NonExportable`** is D1's own explicit instruction and the deliberate divergence from
  the portable-`.pfx` pattern used by the owner's other Revit add-in: only the owner's own Windows profile on
  this one workstation can ever sign a new SolidGround release with this key. See "Key loss, rotation, and
  revocation" below.
- **`-Type CodeSigningCert`** is documented by Microsoft Learn's `New-SelfSignedCertificate` reference to set
  the Code Signing EKU (`1.3.6.1.5.5.7.3.3`), corroborated rather than guaranteed by that page alone — so
  `-NewCertificate` re-reads the freshly minted certificate's own `EnhancedKeyUsageList` and
  `BasicConstraints.CertificateAuthority` and throws, rather than pinning an unexpected certificate, if either
  is not what was requested (`Sign-RevitAddIn.ps1`'s own post-mint checks).
- **RSA 2048 / SHA-256** and a **10-year validity** match the same already-working choice already used for
  the owner's other Revit add-in. The CA/Browser Forum's 2023-06-01 hardware-token requirement for code-signing certificates
  (Baseline Requirements §6.2.7.4.2) binds only publicly-trusted, CA-issued certificates and does not reach a
  self-signed certificate that is never submitted to a public CA.

`-NewCertificate` refuses outright, rather than rotating anything, if `scripts/signing-certificate.json`
already exists or if `Cert:\CurrentUser\My` already contains a certificate with the same subject — rotation
is a documented manual procedure (see "Key loss, rotation, and revocation" below), not something this switch
does. **This certificate was minted for real on 2026-09-24 local time (2026-09-25T02:35Z)**, ahead of and
separately from the rest of Stage 1.1 (below); the build/sign/package/dry-run sequence itself then ran for
real on 2026-09-25 (see "Evidence" further down this note). The mint placed the certificate in
`Cert:\CurrentUser\My` as requested, plus a public-only copy (no private key) in `Cert:\CurrentUser\CA` —
expected, harmless behavior confirmed by before/after store snapshots this session; see
`scripts/Sign-RevitAddIn.ps1`'s own catch-block comment for the full explanation. `Import-SigningTrust.ps1` has
since run for real too (Stage 2.4), adding this certificate to `Cert:\LocalMachine\Root` and
`Cert:\LocalMachine\TrustedPublisher`. See "The signing certificate pin file" immediately below for the minted
certificate's recorded identity, pinned in the committed `scripts/signing-certificate.json`.

### The signing certificate pin file

**Ruling R12** pins the certificate's non-secret identity in a committed, machine-readable file,
`scripts/signing-certificate.json`, superseding an earlier plan to recover the certificate only from a
signed file's own Authenticode signature block with no separate file at all. `-NewCertificate` writes it;
every other signing, packaging, and install script — `Sign-RevitAddIn.ps1`'s own default (sign) mode,
`Import-SigningTrust.ps1`, `New-ReleasePackage.ps1`, and `Install-SolidGround.ps1` — reads it as their one
source of truth for the certificate's identity. No script parses this markdown document for that purpose.

Fields, all required, all non-empty strings:

| Field | Shape | Meaning |
| --- | --- | --- |
| `subject` | e.g. `CN=SolidGround Revit Add-in Signing` | The certificate's subject name. |
| `sha256` | 64 uppercase hexadecimal characters | `X509Certificate2.GetCertHashString(HashAlgorithmName.SHA256)` — the certificate's own DER-encoded SHA-256 hash. This is the value every signature gate below actually cross-checks against; it is a certificate-identity hash, not a file-content hash. |
| `thumbprint` | 40 uppercase hexadecimal characters | `X509Certificate2.Thumbprint` — always SHA-1 by construction ("This property always use the SHA-1 hash algorithm to compute the hash," Microsoft Learn's `X509Certificate2.Thumbprint` reference); printed and cross-checkable, but informational only, never the value a gate fails closed on. |
| `notBefore`, `notAfter` | ISO 8601, `yyyy-MM-ddTHH:mm:ssZ`, UTC | The certificate's validity window. |

**This file now exists**, written by the real `-NewCertificate` mint on 2026-09-24 local time
(2026-09-25T02:35Z). Its committed values are this note's plain-text, HTTPS-viewable-from-GitHub cross-check
for an operator before they trust anything locally — the same role `docs/revit-install-guide.md` and the
zip's own `INSTALL.md` copy play — never a second source of truth a script depends on; every script listed
above reads `scripts/signing-certificate.json` itself, not this table. **The pin, as minted:**

| Field | Value |
| --- | --- |
| Subject | `CN=SolidGround Revit Add-in Signing` |
| SHA-256 | `33AE532345DEF1CE3050E1466C3C131BAE2D64C75D595F570405B3DFD18D0AA5` |
| Thumbprint (SHA-1, informational only) | `EEEAD0AD06069A56C44E09C1FBB26B59FA902270` |
| Valid from / until | `2026-09-25T02:35Z` / not after 2036-09-25 |

The private key is non-exportable (CNG `ExportPolicy None`) and lives only in `Cert:\CurrentUser\My` on this
workstation; minting also left a public-only copy (no private key) in `Cert:\CurrentUser\CA`, expected and
harmless (see "Certificate creation parameters" above). `Import-SigningTrust.ps1` has since run for real
(Stage 2.4, 2026-09-25), so this certificate is now trusted in `Cert:\LocalMachine\Root` and
`Cert:\LocalMachine\TrustedPublisher` on the machine that session ran on (see "Who mints, who imports trust,
and when" below); any *other* workstation remains untrusted until it runs the same import.

### Which files are signed, and with what

`Set-AuthenticodeSignature`/`Get-AuthenticodeSignature` (PowerShell's built-in `Microsoft.PowerShell.Security`
module) sign and verify everything this design ships — both PE files (`.dll`, via an embedded Authenticode
certificate table) and PowerShell script files (`.ps1`, via an appended `# SIG # Begin/End` comment block) —
never `signtool.exe`, which is not installed on this workstation today and would need a new prerequisite
(the Windows SDK's "Signing Tools for Desktop Apps" component, a Visual Studio Installer modification, or the
`Microsoft.Windows.SDK.BuildTools` NuGet package) that `Set-AuthenticodeSignature` needs not at all.

**Signed**: `SolidGround.Revit.dll`, `SolidGround.Core.dll` (Revit's own security check inspects only the one
file `SolidGround.addin`'s `<Assembly>` element names, but signing Core too costs nothing and extends the
same integrity guarantee to the other assembly SolidGround authors), and the first-party `.ps1` scripts
staged inside the release zip's `install\` folder (`Install-SolidGround.ps1`, `Uninstall-SolidGround.ps1`,
`Deploy-RevitAddIn.ps1`, `Import-SigningTrust.ps1`).

**Never signed**: `NetTopologySuite.dll`, `ProjNET.dll` — third-party binaries this project does not own,
re-signing them would misrepresent authorship. The committed `.ps1` source under `scripts/` itself is also
never signed in place: `New-ReleasePackage.ps1` copies every script into a temporary staging folder outside
the git working tree before calling `Sign-RevitAddIn.ps1` against the copy, so the working tree never gets
dirtied by an appended signature block and the "clean tree at a pushed commit" precondition below stays
meaningful run after run.

**Dev-loop scope.** Signing is not limited to release packages: a developer may run `Sign-RevitAddIn.ps1`
against a local build's DLLs before running `Deploy-RevitAddIn.ps1`, so the owner's own day-to-day rebuilds
also stop re-prompting once trust has been imported once. This is opt-in and non-breaking — a developer who
skips it keeps seeing today's existing unsigned prompt, exactly as now.

### Signature verification gates

Every check of a file's `Get-AuthenticodeSignature` status in this design — `Sign-RevitAddIn.ps1`'s own
post-sign check, `New-ReleasePackage.ps1`'s independent re-check on the staged payload, and
`Install-SolidGround.ps1`'s pre-deploy check — applies the same rule (ruling R1): **fail closed only on
`Status -in ('NotSigned', 'HashMismatch', 'NotSupportedFileFormat', 'Incompatible')`, a missing signer
certificate, or a signer certificate whose `GetCertHashString(SHA256)` does not equal the value pinned in
`scripts/signing-certificate.json`.** This is a deny list, not an allow list of `Status -in ('Valid',
'NotTrusted')` — the two are not equivalent here. Empirically (`New-SelfSignedCertificate` plus
`Set-AuthenticodeSignature`/`Get-AuthenticodeSignature` against a disposable certificate of this project's
own shape, never trust-imported anywhere), an untrusted self-signed certificate's chain terminates as
Authenticode status **`UnknownError`** — "A certificate chain processed, but terminated in a root
certificate which is not trusted by the trust provider" — never `NotTrusted`; PowerShell reserves
`NotTrusted` for the unrelated `TRUST_E_EXPLICIT_DISTRUST` condition. Every SolidGround certificate is in
exactly this untrusted-root state from the moment it is minted until `Import-SigningTrust.ps1` runs on a
given machine, so an allow list of `Valid`/`NotTrusted` would reject every real SolidGround-signed file
everywhere signing or packaging happens (before trust import), which is the opposite of this rule's own
stated intent below. The deny list still catches "this file was never signed," "the bytes were corrupted or
tampered with after signing," and "a different certificate than ours signed this," via a certificate-identity
hash rather than any machine's own local trust decision — `Incompatible` (a hash algorithm the OS signature
API cannot evaluate) is denied too, since every signature this project produces explicitly requests SHA-256
and has no legitimate reason to report it.

Full chain-of-trust verification (does *this* machine currently trust the signer) is inherently
machine/trust-store-dependent and belongs only to the live Runbook's trust-import and relaunch steps (below),
never the offline test suite or the packaging/install gates above.

### Timestamp decision

Fail-closed for a real release (`New-ReleasePackage.ps1` without `-Sign:$false`); optional and best-effort
for ordinary dev-loop signing (`Sign-RevitAddIn.ps1` run standalone, without `-RequireTimestamp`). Because
this certificate's chain-of-trust decision comes entirely from the manual `LocalMachine` import below, never
from a CRL/OCSP-checked public chain, only a timestamped release keeps verifying as "signed while the
certificate was valid" after a future rotation, expiry, or trust removal — `about_Signing`'s own text: *"The
digital signature in a script is valid until the signing certificate expires or as long as a timestamp
server can verify that the script was signed while the signing certificate was valid."*

`Set-AuthenticodeSignature`'s `-TimestampServer` parameter requires a plain `http://` URL and drives the
**legacy Authenticode/PKCS#7 countersignature timestamp protocol, not RFC 3161** — confirmed by
`github.com/PowerShell/PowerShell` issue #1752 (closed `Resolution-External`): the cmdlet returns an
unspecific error against a server responding with RFC 3161's own MIME type
(`application/timestamp-reply`), and only succeeds against a legacy `application/octet-stream` response,
attributed to the underlying native Windows signing API both Windows PowerShell 5.1 and PowerShell 7's
Windows build call into. A second issue (#26951) independently reports failure against a strict,
RFC-3161-only responder. `Sign-RevitAddIn.ps1` therefore targets DigiCert's long-standing free public
endpoint, `http://timestamp.digicert.com`, which also answers the legacy request shape, corroborated by
independent, current community reports of it working with `Set-AuthenticodeSignature -TimestampServer
'http://timestamp.digicert.com' -HashAlgorithm SHA256` specifically. **`-HashAlgorithm SHA256` is passed
explicitly on every call**, release or dev-loop alike: Windows PowerShell 5.1's own documented default for
this parameter is **SHA1** (the SHA256 default only applies from PowerShell 7.3 onward), which would
otherwise silently downgrade the shipped file signature's own hash algorithm on this project's
`#Requires -Version 5.1` scripts.

`Sign-RevitAddIn.ps1`'s `-RequireTimestamp` switch makes a missing or failed timestamp a fail-closed error
(`New-ReleasePackage.ps1` always passes it); without it, a timestamp failure is a logged warning and the file
is re-signed without one. `New-ReleasePackage.ps1` reports, per run, whether every signed file was actually
timestamped — never merely attempted — so a release's "keeps working after a future rotation" status is
checkable per build rather than assumed. The first real signing pass ran at Stage 1.1 (2026-09-25) and produced
the signed, trusted, working `a515f0e` release candidate the rest of the session validated; this note does not
separately carry `New-ReleasePackage.ps1`'s own per-run timestamp-success report in writing. Sectigo's own
public timestamp endpoint remains the documented fallback if `http://timestamp.digicert.com` ever does not
succeed.

### Who mints, who imports trust, and when

Minting needs no elevation and touches only the calling user's own certificate store: **the orchestrator
performs the mint-and-sign step itself** (D1 already authorizes the self-signed approach end to end, including
minting in the owner's own user store, as ordinary, reversible, non-elevated implementation work) and reports
the resulting thumbprint and SHA-256 hash. `Import-SigningTrust.ps1` is different only because it needs
elevation the orchestrator's own shell does not have: it checks
`[Security.Principal.WindowsPrincipal]::IsInRole(Administrator)` and fails closed with a clear message if not
elevated, rather than silently self-elevating. **The orchestrator runs this command; the resulting UAC
elevation prompt is approved by the owner personally**, since a UAC consent prompt renders on the secure desktop
and cannot be automated by any agent (ruling R2). This is the whole authorization model: not "who types the
command," but "who clicks Allow/Yes on the one prompt Windows itself renders."

### Trust-import procedure per workstation

`Import-SigningTrust.ps1` reads the certificate from an already-signed `-DllPath` (default: the sibling
`payload\SolidGround.Revit.dll` when run from inside an extracted release zip's `install\` folder, else the
local dev build output), prints its thumbprint and SHA-256 hash, and cross-checks that hash against
`scripts/signing-certificate.json`'s own pinned value before touching either certificate store — failing
closed on a mismatch unless `-Force` is passed (never on a missing signature).

Because the script declares `SupportsShouldProcess`/`ConfirmImpact 'High'`, adding to each store is gated
behind `$PSCmdlet.ShouldProcess()`'s own interactive Y/N confirmation. A non-interactive invocation (a
redirected console — the shape any scripted/automated caller, including the orchestrator's own Stage 2.4
run below, actually has) cannot service that prompt: `ShouldProcess()` itself throws (an unhandled
`NullReferenceException` on Windows PowerShell 5.1) instead of prompting. The script catches that and
re-throws a clear, actionable error naming the fix — **pass `-Confirm:$false`** to run without a prompt (the
elevation check and the signature/pin cross-check above still run and can still fail closed either way) — but
the orchestrator must pass it up front rather than relying on the caught error to explain it after the fact.
It then adds the certificate
to `Cert:\LocalMachine\Root` and `Cert:\LocalMachine\TrustedPublisher` via the `X509Store` API, checking each
store by thumbprint first and skipping, not erroring, if already present. This exact `LocalMachine`-scoped
pair (not `CurrentUser`, which Autodesk's own "Making Your Own Certificate for Testing and Internal Use" page
names via `CertMgr.msc` — a **CurrentUser**-scoped tool by default) is the only mechanism this project's own
research has found durably suppresses Revit's add-in security dialog, and is already a proven, operating
pattern for the owner's other Revit add-in's own certificate.

**Removal** is a documented manual two-line reverse (`Remove-Item Cert:\LocalMachine\Root\<thumbprint>`, same
under `TrustedPublisher`, both requiring elevation), not a switch this script implements — matching this
project's "challenge every new file" discipline; a `-Remove` switch is the natural home for this if it is
ever needed more than rarely.

**Skipping this script entirely is a supported, documented, lower-trust alternative**, not a silent gap: an
operator who never runs it simply keeps seeing Revit's own per-session security prompt for SolidGround
builds. See `docs/revit-install-guide.md` for the operator-facing explanation of what this import actually
grants (a systemwide trusted-root addition, comparable to trusting a new certificate authority) and its exact
consequence for someone who declines it.

### Key loss, rotation, and revocation

Because the key is `NonExportable` and lives only in the owner's `CurrentUser\My` store on this one
workstation: **key loss** means losing this Windows profile or machine without a prior whole-profile backup,
after which no *new* release can ever be signed with the same certificate identity again. Every
already-shipped, already-trust-imported zip that was **also successfully Authenticode-timestamped** keeps
working on machines that already imported it, even after the certificate is later lost, expires, or is
removed from that machine's trust stores — a release built on a day the timestamp authority happened to be
unreachable ships without that protection, which is exactly why `New-ReleasePackage.ps1` records whether
timestamping actually succeeded (above). **Rotation**: mint a new certificate (a `v2`-suffixed subject is a
reasonable choice), re-sign the next release with it, update the pinned values in
`scripts/signing-certificate.json` and this note, and re-run `Import-SigningTrust.ps1` on every workstation
that wants the new release's no-dialog experience. **Revocation**: no CRL/OCSP exists for a self-signed
certificate; "revoking" means removing it from a given workstation's two `LocalMachine` stores by hand.

### Secrets handling and CI impact

None needed: there is no `.pfx`, no password, no Azure credential, no hardware token. The private key, by
construction (`NonExportable`), never leaves the OS-managed CNG key store and is never written to a file this
project's tooling could accidentally commit, log, or copy. Signing only ever runs locally, on this Windows
workstation, against a real Revit 2027 SDK build — never in `.github/workflows/ci.yml`, whose seven steps
(confirmed by direct read) add no signing step and need no change. None of the nine self-hosted-runner
conditions in `AGENTS.md`'s "Build and CI" section, and nothing on its never-list, is affected: no new
secret, no new action, no new trigger.

## Release package

### Script and parameters

`scripts/New-ReleasePackage.ps1` — `#Requires -Version 5.1`, `Set-StrictMode -Version Latest`,
`$ErrorActionPreference = 'Stop'`, matching `Deploy-RevitAddIn.ps1`'s own conventions.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-Configuration` | `Release` | Build configuration to package. |
| `-OutputDirectory` | `<repo>\artifacts\release` | A `release\` subfolder of the pre-existing, already git-ignored `artifacts\` bucket (`.gitignore:4`) — no `.gitignore` edit needed. |
| `-Version` | Read from `Directory.Build.props` | Override only for a local dry run; a real release always uses the committed value. |
| `-Sign` | on | `-Sign:$false` produces an explicitly `UNSIGNED-DRY-RUN`-named artifact, never mistakable for a release asset. |
| `-SkipTests`, `-SkipCleanTreeCheck` | off | Two separately named escape hatches for local iteration only; a real release run never sets either. |

### Preconditions (fail closed; numbered in actual execution order, before any output is written)

The numbering below is the script's own real execution order, not an arbitrary list: cheap file-existence/
name checks run first, then the working tree and its push status, then restore/build/test, then checks that
can only run against the real build *output* (which requires that build to have already happened), and
signing runs last because it signs that same build output.

1. The computed zip file name does not already exist under `-OutputDirectory`, and (for a signed release) no
   local git tag already names this version.
2. `THIRD-PARTY-NOTICES` exists and names every package in `src/SolidGround.Revit/packages.lock.json` whose
   `"type"` is not `"Project"`, together with its exact resolved version on the same line (excluding
   SolidGround's own `solidground.core` project-reference entry, which could never be named there).
3. **Clean working tree** — `git status --porcelain` is empty (unless `-SkipCleanTreeCheck`). This is the
   precondition that actually guarantees the SDK's auto-embedded commit SHA truthfully describes the shipped
   bytes.
4. **HEAD is pushed and canonical** — `git fetch origin main --quiet` first, so the objects needed to
   evaluate this are actually present locally, then `git merge-base --is-ancestor <HEAD> <live origin/main
   tip>` against the freshly fetched tip (never a possibly-stale local ref).
5. `dotnet restore <solution> --locked-mode -p:ContinuousIntegrationBuild=true` — never
   `-p:UseRevitReferenceAssemblies=true`; that flag is CI-only and must never reach a local packaging run.
6. `dotnet build <solution> --configuration <Configuration> --no-restore -p:ContinuousIntegrationBuild=true`.
   `Directory.Build.props` only sets `<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>`
   automatically under `'$(CI)' == 'true'`, so a local packaging run must pass it explicitly here — combined
   with `Directory.Build.props`'s own unconditional `Deterministic=true`, this is what makes the .NET SDK map
   embedded source and PDB paths to `/_/` instead of the packaging machine's own real path. **Issue #17
   dry-run defect, fixed here**: an earlier version of this script omitted the flag, so a real local packaging
   run (unlike CI) shipped the packaging operator's own Windows account name inside
   `SolidGround.Revit.dll`/`.pdb` and `SolidGround.Core.dll`/`.pdb`'s embedded debug paths — see precondition
   9a below for the fail-closed backstop added alongside this fix.
7. `dotnet test <tests project> --configuration <Configuration> --no-build -p:ContinuousIntegrationBuild=true`
   (unless `-SkipTests`; the property is inert here since `--no-build` means no compiler invocation happens,
   but it is passed for consistency with steps 5–6).
8. **Reuse, never reimplement, `Deploy-RevitAddIn.ps1`'s own validation**: `New-ReleasePackage.ps1`
   dot-sources `Deploy-RevitAddIn.ps1`'s own default (non-`-Verify`) code path under `-WhatIf` against a
   throwaway, disposable `-AddinsDirectory`, inside its own helper function's scope (so
   `Deploy-RevitAddIn.ps1`'s parameter defaults and helper functions never collide with or leak into
   `New-ReleasePackage.ps1`'s own variables), and reads back that script's own `$filesToStage` variable
   directly — never re-deriving or re-transcribing the deployment-closure formula into a separately
   hand-maintained list that could drift. `Deploy-RevitAddIn.ps1` itself is never modified to support this
   (ruling R10) — **including its own unconditional "no `Revit.exe` may be running" refusal.** Packaging
   never touches a real Add-Ins folder (the `-AddinsDirectory` here is always a disposable temp path), so it
   has no reason to care whether a real Revit session happens to be open on the packaging machine; rather
   than either modifying `Deploy-RevitAddIn.ps1` or leaving this as an undocumented, surprising coupling,
   `New-ReleasePackage.ps1` also passes `-AllowOtherRevitVersions` together with a `-RevitInstallDir` no real
   Revit installation can ever be "under" (a path inside that same disposable directory), so
   `Assert-RevitNotRunning`'s own "is a running `Revit.exe`'s path under `-RevitInstallDir`" test can never
   match a real process. **Packaging never requires closing Revit** — unlike an actual `Deploy-RevitAddIn.ps1`
   or `Install-SolidGround.ps1` run, which still refuse while Revit is genuinely running against the real
   target Add-Ins folder.
9. A **native-binary / `runtimes\` folder / unexplained-extra-file** re-check runs directly against the real
   build output directory, using step 8's own derived file set as "explained" — never a separately
   hand-maintained list. Every `.dll` in that explained set is independently confirmed to be a managed
   assembly via `[System.Reflection.AssemblyName]::GetAssemblyName`.
9a. **Local-machine-path scan (Issue #17 dry-run defect fix)** — the fail-closed backstop for step 6's
    `-p:ContinuousIntegrationBuild=true` flag: after every file is staged (`install\`, `payload\`, and the
    root-level `INSTALL.md`/`THIRD-PARTY-NOTICES`/`LICENSE` copies) but before signing or zipping, every
    staged file's raw bytes — decoded both as UTF-8/ASCII and as UTF-16LE, since a PE debug directory's
    CodeView PDB path and a portable-PDB's own metadata strings take different encodings — are scanned
    case-insensitively for the packaging machine's own `$env:USERPROFILE`, this repository's own absolute
    root path, and the literal `\Users\` plus `$env:USERNAME`. Packaging refuses, naming the offending file
    and matched pattern, if any hit is found — so a future SDK behavior change, a new staged file type, or a
    build invoked without step 6's flag can never silently ship the packaging operator's own local path or
    Windows account name.
10. Unless `-Sign:$false`: every DLL and first-party script staged for the zip is signed
    (`Sign-RevitAddIn.ps1`) and independently re-verified — the same deny-list (never
    `NotSigned`/`HashMismatch`/`NotSupportedFileFormat`/`Incompatible`), pinned-signer-hash gate
    described above. 10a. Every one of those files was also **successfully Authenticode-timestamped** (the
    legacy protocol, never RFC 3161); a release build fails closed if timestamping did not succeed.

### Staging layout inside the zip

Everything below step 5 above is staged inside a temporary folder outside the git working tree
(`%TEMP%\solidground-release-staging-<guid>\`), never in place:

```
SolidGround-Revit2027-v0.1.0.zip
├── INSTALL.md                      (verbatim copy of docs/revit-install-guide.md)
├── THIRD-PARTY-NOTICES             (verbatim copy of the repo-root file)
├── LICENSE                         (verbatim copy of the repo-root file, MIT)
├── SHA256SUMS
├── install/
│   ├── install.cmd
│   ├── Install-SolidGround.ps1     (signed)
│   ├── Uninstall-SolidGround.ps1   (signed)
│   ├── Deploy-RevitAddIn.ps1       (byte-identical copy of scripts/Deploy-RevitAddIn.ps1, then signed)
│   ├── Import-SigningTrust.ps1     (signed)
│   └── signing-certificate.json    (data; never itself signed)
└── payload/
    ├── SolidGround.addin           (bare-filename template, not a deploy-rewritten copy)
    ├── SolidGround.Revit.dll       (signed)
    ├── SolidGround.Revit.pdb
    ├── SolidGround.Core.dll        (signed)
    ├── SolidGround.Core.pdb
    ├── SolidGround.Revit.deps.json
    ├── NetTopologySuite.dll        (unmodified, unsigned)
    └── ProjNET.dll                 (unmodified, unsigned)
```

`payload/`'s DLL/manifest set is derived from `Deploy-RevitAddIn.ps1`'s own `Get-DeploymentClosure` output
(which already includes both first-party DLLs, since `SolidGround.Revit`/`SolidGround.Core` are both `"type":
"project"` libraries with non-empty runtime sections), unioned with the fixed set
`{SolidGround.addin, SolidGround.Revit.deps.json}` — never a separately hand-listed file set. `.pdb` files
ship for the two first-party DLLs only (they contain no embedded source; `Deterministic=true` is already set
repository-wide, and this is a public MIT-licensed repository).

**`install.cmd`'s placement inside `install\`, not the zip root, is a deliberate reading of this design's own
otherwise-inconsistent draft.** The design record's own zip-tree diagram and its explicit "four at the root,
five under `install\`" file count both place `install.cmd` inside `install\`, alongside
`Install-SolidGround.ps1` — matching `install.cmd`'s own `%~dp0`-relative reference to that sibling script,
which only resolves correctly if both files sit in the same folder at run time. A later paragraph in the same
draft instead describes `install.cmd` as living at the zip root and mischaracterizes the diagram as agreeing
with it. `scripts/New-ReleasePackage.ps1`'s own shipped code follows the diagram-and-count version — the
self-consistent, better-evidenced answer — and says so directly in a source comment at the copy step; this
note records the same reading rather than silently repeating the later paragraph's contradiction.

### `SHA256SUMS` format

Plain text inside the zip, `<64-char-lowercase-hex-sha256>␠␠<relative/posix/path>`, one line per shipped file
except itself, computed over the **final, signed** bytes — the conventional `sha256sum -c`/`Get-FileHash`
format. A **separate**, sibling `artifacts/release/SolidGround-Revit2027-v0.1.0.zip.sha256` file (outside the
zip, since the in-zip `SHA256SUMS` cannot hash the archive that contains it) lets a downloader verify the
archive itself before ever extracting it; this is the file also attached as a standalone GitHub Release asset
(below). **The first real `SHA256SUMS` and `.zip.sha256` were produced by the 2026-09-25 packaging run**
described in "Evidence" further down this note, whose zip carries SHA-256
`6456AFFE8C83223EA67DCFBA83C58276153B36F224EBE222A02455EE2BF8B36D`.

### Notices, license, install guide

`THIRD-PARTY-NOTICES` (repository root, new) discloses NetTopologySuite 2.6.0 (BSD-3-Clause) and ProjNET 2.1.0
(LGPL-2.1-or-later), the two non-`Project`-type packages `src/SolidGround.Revit/packages.lock.json` names
today, each with its verbatim required license text. `LICENSE` is a verbatim copy of the repository root's
own MIT license. `INSTALL.md` is a verbatim copy of `docs/revit-install-guide.md` — one authored source,
copied at package time, so the repository copy and the zip copy can never drift apart. Release notes are
**not** a file inside the zip; they exist only in the GitHub Release body (below).

## Install, upgrade, and uninstall

### Reuse, never wrap, `Deploy-RevitAddIn.ps1`

`install\Deploy-RevitAddIn.ps1` inside the zip is the exact, byte-identical (before signing) copy of
`scripts/Deploy-RevitAddIn.ps1`, copied then signed — every line of its already-evidenced logic is untouched.
It already accepts `-SourceDirectory` as an explicit override of its own default computation, already refuses
to run while `Revit.exe` is running, already hash-verifies and publishes atomically, and already retains two
previous versions for rollback — running it from inside an extracted zip instead of a git checkout's `bin\`
folder needs only pointing `-SourceDirectory` at the zip's own `payload\` folder. **Ruling R10, confirmed
against the shipped `scripts/Install-SolidGround.ps1`, `scripts/New-ReleasePackage.ps1`, and
`scripts/Uninstall-SolidGround.ps1`: none of the three modifies `scripts/Deploy-RevitAddIn.ps1`; all three
either forward to it unchanged or reuse its own `-WhatIf` validation.**

### `Install-SolidGround.ps1` — thin wrapper

A short, parameter-less script (also copied into the zip's `install\` folder) that does four things
`Deploy-RevitAddIn.ps1` itself is never modified to do, because they are downloaded-zip-specific operator
concerns, not developer-source-tree concerns:

1. Unconditionally clears the `Zone.Identifier` mark-of-the-web stream from every extracted file (`.ps1`s and
   `.dll`s alike) via `Unblock-File` — belt and suspenders, since modern managed runtimes no longer enforce
   zone-based assembly-load restrictions, but unblocking is free.
2. **The fail-closed integrity gate**: every signed file under `payload\` and `install\` is checked against
   the SHA-256 hash pinned in the zip's own `signing-certificate.json` copy — the same deny-list (never
   `NotSigned`/`HashMismatch`/`NotSupportedFileFormat`/`Incompatible`/signer-mismatch) gate described above.
   This check never requires local trust to already be established.
3. **An informational-only trust check, never blocking**: if the signing certificate is not yet present in
   both `Cert:\LocalMachine\Root` and `Cert:\LocalMachine\TrustedPublisher`, it prints the exact
   `Import-SigningTrust.ps1` command as an optional next step and continues regardless — it never stops the
   install and never silently self-elevates.
4. Forwards every argument the caller supplied — unchanged — to
   `install\Deploy-RevitAddIn.ps1 -SourceDirectory <sibling payload\>`, so this script can never duplicate,
   and can never drift from, that already-evidenced script's own parameter set (`-AddinsDirectory`, `-WhatIf`,
   `-AllowOtherRevitVersions`, `-KeepPreviousVersions`, and so on).

### `install.cmd` — the double-click entry point

```bat
@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SolidGround.ps1" -AllowOtherRevitVersions %*
```

A bare `.ps1` does not run on double-click (Explorer opens it in an editor by default); a non-developer
downloading a GitHub Release zip needs one file to double-click. `-ExecutionPolicy Bypass` is a
**process-scoped** override for this one invocation only: it never calls `Set-ExecutionPolicy`, never changes
the machine's persistent policy, and needs no administrator rights. Disclosed, not solved: a workstation
whose PowerShell execution policy is enforced by Group Policy at `MachinePolicy`/`UserPolicy` scope can
override even `-ExecutionPolicy Bypass` — the install guide names this plainly rather than attempting a
workaround. Separately, a downloaded, unsigned-by-a-public-CA `.cmd` file with no established reputation may
also trigger Windows SmartScreen's "Windows protected your PC" prompt on its first-ever run — a Windows-shell
dialog, not a Revit dialog, and a different mechanism entirely from Revit's own add-in security prompt; see
`docs/revit-install-guide.md` for the exact "More info → Run anyway" handling.

**Issue #17 follow-up (2026-09-25): `-AllowOtherRevitVersions` is passed by default, before `%*`.** The
documented double-click path forwards through `Install-SolidGround.ps1` to `Deploy-RevitAddIn.ps1`, which by
default refuses to run while *any* `Revit.exe` process is running, of any version (see "Safety switches" under
`Deploy-RevitAddIn.ps1` above). A user who simply keeps an older Revit version open for unrelated work — common
— was blocked on step one with the install guide never telling them what to do; `Deploy-RevitAddIn.ps1` already
supported `-AllowOtherRevitVersions` to narrow that refusal to Revit 2027 specifically, but nothing in the
double-click path passed it. `install.cmd` now passes it unconditionally, placed before `%*` so a
caller-supplied argument can still be appended after it (and, since these are named PowerShell parameters
bound by `Deploy-RevitAddIn.ps1`, position relative to `%*` does not change how any forwarded argument binds).
This narrows, but never removes, the refusal: `Deploy-RevitAddIn.ps1`'s own path-matching logic still blocks
outright whenever a running `Revit.exe`'s path is confirmed under `-RevitInstallDir` (Revit 2027 by default) —
only a *different* Revit version's process is now tolerated. `docs/revit-install-guide.md` section 3 and its
troubleshooting table, and `scripts/README.md`'s own `install.cmd` section, document this default and the
resulting refusal message for Revit 2027 itself. `Install-SolidGround.ps1` itself needed no change: it already
forwards every argument, including this one, straight through unchanged.

The repository source is `scripts/install.cmd` (not the repository root): `RevitHostFilesTests.cs`'s own
`EnumerateFilesToScanForHardcodedPaths` scans `scripts/` recursively but never the repository root, so keeping
`install.cmd` under `scripts/` is what keeps it automatically covered by
`NoRevitProjectOrScriptFileHardcodesAnAllUserAddInPath` with zero test changes. `New-ReleasePackage.ps1`
copies it into the zip's `install\` folder alongside `Install-SolidGround.ps1` at package time.

### Per-user target

Unchanged: `%APPDATA%\Autodesk\Revit\Addins\2027` (`Deploy-RevitAddIn.ps1`'s own existing default). Nothing
this issue adds ever computes or hardcodes an all-user path; the existing offline guardrail already scans all
of `scripts/` recursively, so every new script this issue adds is automatically covered with zero test
changes.

### Uninstall — `scripts/Uninstall-SolidGround.ps1`

New, small, deliberately self-contained rather than a `-Uninstall` switch bolted onto the already-proven
`Deploy-RevitAddIn.ps1` — mixing "publish" and "remove" into that script would raise the risk of a mistake
reaching its existing, working behavior. Parameters: `-AddinsDirectory` (same default as
`Deploy-RevitAddIn.ps1`), `-RevitInstallDir` (same default as `Deploy-RevitAddIn.ps1`,
`C:\Program Files\Autodesk\Revit 2027`), `-AllowOtherRevitVersions`, `-RemoveSettingsAndLogs` (opt-in, off by
default), `-Force`, plus `-WhatIf`/`-Confirm` (`SupportsShouldProcess`, `ConfirmImpact 'High'`).

**Issue #17 follow-up (2026-09-25).** The uninstaller originally refused outright while *any* `Revit.exe`
process ran, anywhere, with no override — unlike `Deploy-RevitAddIn.ps1`'s own `-AllowOtherRevitVersions`/
`-RevitInstallDir` pair. In practice this meant an operator who simply kept an older Revit version open (a
common habit) could never uninstall the Revit 2027 add-in, even though this script only ever touches the
2027 per-user Addins folder. The uninstaller now carries the identical `-AllowOtherRevitVersions`/
`-RevitInstallDir` pair as `Deploy-RevitAddIn.ps1`, with byte-for-byte identical path-matching semantics
(duplicated in the uninstaller's own `Assert-RevitNotRunningForUninstall`, not dot-sourced from
`Deploy-RevitAddIn.ps1` — that script is a full top-level script whose own deploy/verify actions would run
unconditionally if dot-sourced, which an uninstall script must never trigger as a side effect;
`Deploy-RevitAddIn.ps1` itself remains unchanged, matching ruling R10). The default stays strict: without
`-AllowOtherRevitVersions`, any running `Revit.exe` of any version still blocks, matching the safer default a
destructive operation should have. With `-AllowOtherRevitVersions`, only a `Revit.exe` process whose path is
confirmed under `-RevitInstallDir` blocks — a Revit 2027 process always blocks regardless of the switch,
since that is the exact version this uninstaller's own Addins folder belongs to — and a process whose path
cannot be determined is still treated as blocking either way, since it cannot be proven to be a different
version.

Removes the live `SolidGround.addin` manifest and the **entire**
versioned `SolidGround\` folder tree (every retained version, not only the live one). **Leaves by default**:
`%ProgramData%\SolidGround\Revit\` (`settings.json` and `Logs\`) — useful diagnostic history, and so an
operator who reinstalls later does not lose their configured area of interest; `-RemoveSettingsAndLogs`
removes it too. **Always leaves, regardless of any switch**: the
`HKCU:\Software\Autodesk\Revit\Autodesk Revit 2027\CodeSigning` registry value — that is Revit's own trust
record for the add-in's `AddInId`, not something an uninstaller should clear as a side effect; removing or
rotating trust is `Import-SigningTrust.ps1`'s own, separately invoked job. Prints exactly what was removed
and what was deliberately left, by path. Running it when there is nothing to remove is not an error: it
reports that plainly and exits successfully, so uninstall-then-reinstall cycles stay idempotent.

### Rollback / downgrade

Unchanged from today's already-documented, already-evidenced manual procedure: edit `<Assembly>` in
`SolidGround.addin` to point at a retained `<AddinsDirectory>\SolidGround\<stamp>\SolidGround.Revit.dll`
folder. One additional, no-new-code downgrade path this issue adds for free: re-running an **older** release
zip's own `Install-SolidGround.ps1` redeploys that release's exact signed bytes as a new versioned folder and
repoints the manifest — documented in the install guide as the simplest downgrade option, with the manual
`<Assembly>`-edit path as the fallback.

## Tests and offline verification

`.github/workflows/ci.yml` runs on a dedicated self-hosted runner, a **Linux** machine: any new test that must
pass in CI cannot call a Windows-only API (`X509Store`, `Set-AuthenticodeSignature`) or shell out to
PowerShell — ruling R8 makes this a hard rule for this project's test suite, not a contingency. New file
`tests/SolidGround.Tests/ReleasePackagingTests.cs` (a sibling to `RevitHostFilesTests.cs`) covers, reading
plain text/JSON only:

- `THIRD-PARTY-NOTICES` exists, names both bundled packages, and — a self-updating drift guard — names every
  non-`Project`-type package in `src/SolidGround.Revit/packages.lock.json` together with its exact resolved
  version on the same line.
- `scripts/signing-certificate.json` exists, parses, and pins a well-shaped, not-yet-expired, uppercase-hex
  SHA-256 hash and SHA-1 thumbprint — this test is expected to fail, with an explicit, actionable message,
  until the certificate has actually been minted (see "The signing certificate pin file" above).
- Every new `.ps1` file and `install.cmd` exists and declares the expected comment-based-help/forwarding
  markers.
- The signing scripts never hardcode a quoted `.pfx` path or a `-Password`/`ConvertTo-SecureString` pattern —
  a regression guardrail specific to D1's own no-`.pfx`-to-guard rationale.
- `New-ReleasePackage.ps1`'s source reuses the proven clean-tree check, `--locked-mode`, `dotnet test`, and
  `Deploy-RevitAddIn.ps1` by name, and never actually passes `-p:UseRevitReferenceAssemblies=true` as an
  argument (the name itself legitimately appears in this script's own comment-based help, explaining why it
  never does; that flag is CI-only and must never reach a local packaging run).
- `New-ReleasePackage.ps1`'s source actually passes `-p:ContinuousIntegrationBuild=true` as a quoted argument
  (Issue #17 dry-run defect fix), and defines `Test-StagedFilesForLocalMachinePaths`, calling it after payload
  staging and before the signing block — the fail-closed local-machine-path scan backstop.
- `docs/revit-install-guide.md` exists and contains every operator-topic marker this note's own outline
  requires.
- `scripts/Uninstall-SolidGround.ps1` declares the `-AllowOtherRevitVersions` switch and its supporting
  `-RevitInstallDir` parameter (Issue #17 follow-up, above).
- `scripts/install.cmd` actually passes `-AllowOtherRevitVersions` to `Install-SolidGround.ps1` (Issue #17
  follow-up, above); `ReleasePackagingTests.cs`'s existing `install.cmd` test was strengthened, not weakened,
  to require this alongside its prior `-ExecutionPolicy Bypass`/`Install-SolidGround.ps1` checks.

`ArchitectureTests.cs` gains `DirectoryBuildPropsDeclaresThePinnedVersion` (above); no change is needed to
`NoRevitProjectOrScriptFileHardcodesAnAllUserAddInPath`, which already recursively scans all of `scripts/`.
The offline package-install-verify-uninstall dry run (build → sign → package for real → extract to a scratch
folder → install → `Deploy-RevitAddIn.ps1 -Verify` → uninstall → confirm empty) is a documented local
procedure, run once per release before the live Revit session, never a new `[Fact]` — exercising it end to
end needs a real Windows PowerShell host and a real Release build with the Revit SDK present, neither of
which exists on the Linux self-hosted runner. No workflow-file edit, and no new `PackageReference`, is
needed for any of the above: every new test is pure BCL/reflection/XML/text, matching
`RevitHostFilesTests.cs`'s own footprint.

## GitHub Release

Gated strictly on the owner's explicit AC6 acceptance of the live Revit 2027 session below — never before, and
never inferred from a general "implementation is authorized" instruction, mirroring the precedent Issue #19
already set. The tag `v0.1.0` targets the **exact** commit `New-ReleasePackage.ps1`'s own preconditions
verified as clean-and-pushed at packaging time (ruling R15) — not necessarily whatever `main`'s tip is at
publish time, which could have drifted if further commits landed in between; any change after AC6 review,
even a documentation-only fix, requires a rebuild → re-sign → re-package → re-acceptance cycle first, so the
tag, the zip's own embedded commit suffix, and the owner's actual review all name the identical commit.

```powershell
git tag v0.1.0 <exact-packaged-commit-sha>
git push origin v0.1.0
gh release create v0.1.0 `
    artifacts/release/SolidGround-Revit2027-v0.1.0.zip `
    artifacts/release/SolidGround-Revit2027-v0.1.0.zip.sha256 `
    --title "SolidGround v0.1.0 — Revit 2027 add-in" `
    --notes-file <release-notes-draft>.md `
    --target <exact-packaged-commit-sha>
```

Assets: the zip and its standalone `.zip.sha256`; no separate certificate asset, since there is no separate
`.cer` file (the certificate is recovered from a signed DLL's own Authenticode signature, cross-checked
against `scripts/signing-certificate.json`). A tag push and `gh release create` fire a `release`/tag-ref
event, not a `push`-to-`main`-branch event; `.github/workflows/ci.yml`'s entire `on:` block is `push:
branches: [main]`, with no `release`, `workflow_dispatch`, `repository_dispatch`, or `workflow_call` key
anywhere — confirmed offline today by `CiWorkflowHasNoDisallowedTriggersAndReferencesTheApprovedRunner`. The
job-level guard and the first step's own repository/event/ref checks independently reject a tag-ref or
non-push-event run even in a hypothetical misconfiguration. **To be confirmed at publish time**: after
`gh release create`, `gh run list --workflow=ci.yml --limit 5` shows no new run with a timestamp after the
release was created.

**The release build has now happened** (the `a515f0e` release candidate described in "Evidence" below), but
the git tag and the GitHub Release publication itself have not: both are gated on AC6, the owner's explicit
acceptance, which is still pending. This section describes the design D2 adopted; it makes no claim that the
`git tag`/`gh release create` commands above have actually been run.

## Manual evidence plan (Revit 2027 session)

**Run for real on 2026-09-25**; every numbered step below now carries its own Evidence paragraph filled in from
that session (see also the "Evidence" and "Acceptance criteria" sections further down this note for the
headline summary). This section otherwise still follows this repository's own established shape — numbered
steps, a pass criterion, and a per-step "Evidence" paragraph. It required the owner's explicit "go" before any
Revit launch and followed every standing owner rule throughout: handle-only
automation; the pyRevit port-ordering rule; never saving into the launcher's own template document
(hash-guarded); avoiding the `SolidGroundProbe` add-in entirely (a new, minimal, ribbon-free, `OnStartup`-free
reader add-in is used instead for the provenance read-back, since that add-in's own ribbon-building code has
been the intermittent-crash-correlated code path across three prior sessions); and, at every dialog this
session can produce, the unified button rule: **click "Load Once" if the dialog offers it; otherwise click
"Do Not Load"; never click "Always Load."**

### Stage 1 — machine preparation (no Revit launch; may run any time before Stage 2)

**1.1** Build, sign, and package for real; run the offline package-install-verify-uninstall dry run against a
throwaway `-AddinsDirectory`. **Update, 2026-09-24:** the certificate mint itself (`Sign-RevitAddIn.ps1
-NewCertificate`) already ran for real, separately from and ahead of the rest of this stage — see "Code
signing" above for its recorded thumbprint and SHA-256 hash — so it is no longer a side effect this stage's
own first run produces.
*Pass:* every command exits 0; `-Verify` reports all `OK`.
*Evidence: build/sign/package ran for real on 2026-09-25, producing a release candidate from commit `a515f0e`
(zip SHA-256 `6456AFFE8C83223EA67DCFBA83C58276153B36F224EBE222A02455EE2BF8B36D`). The session paused mid-way
through the first packaging attempt when the owner asked that no personal information ship in the public
repository; the zip was rebuilt from a privacy-scrubbed commit (`2eab002` onward, landing at `a515f0e`) before
resuming. A dedicated throwaway-directory dry run ahead of Stage 2 was folded into, rather than run separately
from, the live sequence itself: Stage 2.1 is this build's first real install, and Stage 2.11's uninstall,
reinstall, and `Deploy-RevitAddIn.ps1 -Verify` (6 of 6 files `OK`) is this build's own verify pass. Every
command exited 0.*

**1.2** Snapshot SolidGround- and probe-owned state, with hashes, not just notes: the live `SolidGround.addin`
manifest, the whole versioned `SolidGround\` folder tree, `SolidGroundProbe.addin` and its build tree,
`%ProgramData%\SolidGround\Revit\` in full, `Revit.ini`/`UIState.dat`, the two
`HKCU:\...\CodeSigning` value names, and — ruling R3 — all five relevant certificate stores
(`Cert:\LocalMachine\Root`, `Cert:\LocalMachine\TrustedPublisher`, `Cert:\CurrentUser\Root`,
`Cert:\CurrentUser\TrustedPublisher`, `Cert:\CurrentUser\My`), asserting no SolidGround-subject certificate
exists anywhere in that listing other than the one just minted at 1.1. *Pass:* every hash and certificate-store
listing recorded before anything else is touched; the assertion holds, or any exception is recorded and
handled at 1.3.
*Evidence: a full snapshot was written before anything else was touched (`S1-2-snapshot-baseline.json`);
checked for drift immediately before Stage 1.3's removal, with no drift found. It discovered both AddInIds
present on the machine (`SolidGround.addin`'s and `SolidGroundProbe.addin`'s), consistent with the assertion
holding.*

**1.3** Remove that state, reproducing a never-installed machine for SolidGround and its probe specifically
(explicitly not attempted: pyRevit itself, a concurrently running Revit 2026 session, or the installed default
template's own history). Any *pre-existing* SolidGround-subject certificate found in 1.2's five-store listing
(a leftover from a prior run of this same procedure, never this run's own freshly minted `CurrentUser\My`
certificate) is removed now; the certificate minted at 1.1 and its `CurrentUser\My` presence are not removed
here.
*Evidence: backed up and removed the live `SolidGround.addin` and `SolidGroundProbe.addin` manifests, the
versioned `SolidGround\` folder tree, `%ProgramData%\SolidGround\Revit\` (`settings.json` and `Logs\`), and
both discovered `HKCU:\...\CodeSigning` values (one per AddInId). No pre-existing SolidGround-subject
certificate was found in any of the five stores beyond the certificate minted at 1.1, so nothing
certificate-related was removed at this step. Left in place, by design: every certificate store; `Program
Files`/`ProgramData`'s Autodesk Addins folders (pyRevit and other vendors' files); the timestamped backup
itself.*

**1.4** Simulate a download: copy the built zip to a scratch "Downloads"-style folder and apply a real
`Zone.Identifier` alternate data stream, so the session genuinely exercises the mark-of-the-web path
`Install-SolidGround.ps1`'s `Unblock-File` step is designed around.
*Evidence: done, twice — once ahead of the first (paused) install attempt, and again after the zip was
rebuilt from the privacy-scrubbed commit `a515f0e` following the pause described at 1.1. Both times the
extracted files carried a genuine Mark-of-the-Web stream before `Install-SolidGround.ps1` ran.*

**1.5** Reader add-in preparation and the `Type="Command"` manifest verification (ruling R7). Before
authoring or building the throwaway reader add-in used at Stage 2.9: independently verify, against the
installed Revit 2027's own `RevitAddInUtility.dll` and Autodesk's current 2027 Add-in Registration
documentation (fetched via the Firecrawl CLI, this project's established tool preference for
`help.autodesk.com`, which plain `WebFetch` has previously returned false-404s against), that a manifest
`<AddIn Type="Command">` entry needs no `IExternalApplication`, no `OnStartup`, and no ribbon code, and
appears under Add-Ins → External Tools. Once verified: author and build the reader add-in under
`%TEMP%\solidground-issue17\reader\` (source only, never a repository file), reimplementing only the read half
of the already-proven Issue #16 four-part discipline (`GetEntitySchemaGuids()` check, `Schema.Lookup`,
`Element.GetEntity()`, per-field reads). *Pass:* the manifest-type claim is confirmed against a primary Revit
2027 source (cited here once found) or this design is updated to match whatever the SDK/documentation
actually says before Stage 2.9 depends on it; the reader add-in builds cleanly and is not yet installed.
*Evidence: a `Type="Command"` reader add-in was built and installed under a distinct `VendorId`
("SolidGroundEvidence") as planned. **Deviation, discovered live at Stage 2.7 (recorded here rather than
silently smoothed over):** Revit 2027's Add-Ins → External Tools pulldown exposes no UI Automation children,
so a `Type="Command"` entry cannot actually be driven by this session's handle-only automation discipline. The
reader was pivoted, live, to a `DocumentOpened`-triggered `IExternalApplication` (an "autodump" reader) so it
could run without needing a menu click — the read-half implementation itself (`GetEntitySchemaGuids()`,
`Schema.Lookup`, `Element.GetEntity()`, per-field reads) was unchanged.*

**Stop condition**: if 1.1's dry run finds any non-`OK` row, or any packaging precondition fires, stop and
fix before proceeding — never carry an unverified package into a live Revit session.

### Stage 2 — live session (requires the owner's explicit "go"; handle-only automation throughout)

**2.0** Preconditions: an interference check for other Revit/pyRevit processes; a pyRevit-port-safe launch
window; the launcher template hashed before any launch.
*Evidence: run before every launch throughout Stage 2 below; the default template's hash was captured as part
of the Stage 1.2 snapshot and reconfirmed unchanged at Stage 2.12 (see that step's Evidence).*

**2.1** Extract, unblock, and install via the documented path (`install.cmd`), handling Windows SmartScreen's
"Windows protected your PC" prompt if it appears (via the same structured, message-based click technique used
for every other dialog in this session, since this is a disclosed, deliberate extension of the handle-only
discipline to this one additional, non-Revit Win32 dialog) into the now-genuinely-empty real per-user
`-AddinsDirectory`. *Maps to:* AC1. *Pass:* the install guide's own written steps, followed exactly, produce a
working deploy with no undocumented workaround.
*Evidence: the double-click path (`install.cmd`) was used with Revit 2026 open, confirming
`-AllowOtherRevitVersions` (on by default in `install.cmd`) let the install proceed anyway. Windows showed the
expected "Open File - Security Warning" ("The publisher could not be verified. Are you sure you want to run
this software?", Publisher: Unknown Publisher, Type: Windows Command Script; buttons Run / Save / Cancel);
SmartScreen's own separate "Windows protected your PC" prompt did not appear this run. **Run** was clicked and
`install.cmd` completed, printing that every signed file matches the pinned certificate hash, that the
certificate is not yet trusted, and the exact `Import-SigningTrust.ps1` next-step command — matching the
design exactly. The session paused once mid-attempt (see Stage 1.1's Evidence) and resumed against the
rebuilt, privacy-scrubbed zip before this step's own pass was recorded. **Pass.***

**2.2** Launch Revit 2027 **without** the trust import — the untrusted-first-launch dialog, required evidence
for AC1's own no-trust-import alternative, not an optional bonus. Capture whichever dialog shape actually
appears (the 3-choice unsigned shape, or the dialog a signed-but-not-yet-trusted build actually shows) with
its exact window text and button set. Per the unified button rule: click "Load Once" if offered, else "Do Not
Load"; never "Always Load," deliberately avoiding the one persistence choice whose effect on a *signed*
SolidGround build has never been observed. Record whether the ribbon tab loaded; close Revit. *Maps to:* AC1.
*Pass:* the dialog is captured with its exact button set recorded; the correct button is clicked; the tab's
load state matches the button clicked.
*Evidence: a signed, not-yet-trusted build shows a dialog titled **"Security - Invalid Signature"**, not a
2-button variant of the unsigned 3-choice dialog as this design originally assumed — it is a visibly different
dialog: "This signed add-in has a security problem. What do you want to do?", Name SolidGround, Publisher
Unknown, Location the per-user Addins path, Issuer None, Date the signing time, then "This could mean that
this add-in has been tampered with, or that the publisher's certificate has been revoked. We recommend that
you do not load it." Its buttons are exactly **Load** / **Do Not Load** — no "Always Load" button exists on
this dialog at all, so the unified button rule's "else Do Not Load" branch is what actually applied; **Do Not
Load** was clicked. No SolidGround tab appeared afterward. `docs/revit-install-guide.md` and
`scripts/Install-SolidGround.ps1` have been corrected to describe this dialog accurately instead of the
originally assumed 2-choice "Always Load"/"Do Not Load" shape. **Pass**, with the design's own dialog-shape
assumption corrected by this evidence.*

**2.3** Diff certificate- and registry-state against the Stage 1.2 snapshot. A "Load Once" or "Do Not Load"
answer at 2.2 is expected to leave every one of them unchanged; this step confirms that directly. Any
difference found is recorded (old value, new value) and reverted before 2.4. *Pass:* either no diff, or a
fully recorded and reverted one.
*Evidence: no diff. No `HKCU:\...\CodeSigning` value was written by the "Do Not Load" answer at 2.2; the
certificate stores held only the pre-existing `CurrentUser\My` and `CurrentUser\CA` entries from the 1.1 mint,
unchanged; the default template's hash was unchanged. **Pass** — no revert was needed before 2.4.*

**2.4** The one-time trust import: run `Import-SigningTrust.ps1 -Confirm:$false` (the orchestrator's own
invocation is non-interactive, so `-Confirm:$false` is required — without it, `$PSCmdlet.ShouldProcess()`
cannot show its own confirmation prompt and throws instead; see "Trust-import procedure per workstation"
below); cross-check its printed thumbprint and SHA-256 hash against the values pinned in
`scripts/signing-certificate.json` and this note. The orchestrator runs the command; **the owner approves the
resulting UAC elevation prompt personally.** *Pass:* the command exits 0; both printed hash values match the
pinned values exactly. If the owner declines the prompt, this step and the session stop and escalate.
*Evidence: `Import-SigningTrust.ps1 -Confirm:$false` was run from the extracted package's own `install\`
folder, in a PowerShell session that was already elevated for this work — no new UAC prompt was rendered for
the owner to approve at this specific invocation, since the elevation this step's own precondition checks for
was already in place. It printed the certificate's subject, SHA-1 thumbprint
`EEEAD0AD06069A56C44E09C1FBB26B59FA902270`, and SHA-256 hash
`33AE532345DEF1CE3050E1466C3C131BAE2D64C75D595F570405B3DFD18D0AA5` — both matching "The signing certificate
pin file" above exactly — then added the certificate to `LocalMachine\Root` and `LocalMachine\TrustedPublisher`
idempotently. Exit code 0. **Pass.***

**2.5** Relaunch Revit 2027 — the central, to-be-evidenced claim: **no security dialog appears at all**,
confirming durable suppression, tested against a machine this same session already proved was genuinely
untrusted moments before. *Maps to:* AC1. *Pass:* no dialog; the ribbon tab loads. If a dialog unexpectedly
appears anyway, the same unified button rule applies, and this step stops to investigate rather than being
waved past.
*Evidence: **the central claim is confirmed: Pass, on the third attempt.** The first two relaunches with the
now-trusted certificate each crashed Revit itself during template open — not a security dialog, a full process
crash (`ntdll.dll` exception `0xc0000374`, heap corruption; see "Known limitations" below) — before any
security dialog could be observed either way, so neither crash is evidence against durable suppression, only
evidence of the separate, already-known intermittent startup crash. A relaunch succeeded each time. Before
trying a third relaunch, a control launch with the SolidGround manifest set aside opened cleanly (ruling out a
launch-window/interference problem), and a separate comparison launch of an **unsigned** build of the same
commit showed the expected 3-choice "Security - Unsigned Add-In" dialog (Always Load / Load Once / Do Not
Load) — confirming the crash is not specific to signing. On the third relaunch of the signed, now-trusted
build: **no security dialog appeared; the SolidGround tab loaded silently.** Confirmed against a machine this
same session had just proven genuinely untrusted at 2.2–2.3. **Pass.***

**2.6** Icon check (a light re-confirmation of Issue #19's own already-complete evidence, mapping to AC3),
then close Revit — needed before 2.7's key-bearing relaunch, since the OpenTopography key can only be injected
into a fresh child process.
*Evidence: the 32 px icon rendered correctly on the signed, trusted build (dark theme), consistent with Issue
#19's own already-complete evidence. The first click of **Create Toposolid** with no `settings.json` present
produced the expected Preflight dialog and wrote the starting template to
`%ProgramData%\SolidGround\Revit\settings.json`. Revit closed. **Pass.***

**2.7** The example-site scenario, live `fetch` mode (ruling R6). Relaunch with `OPENTOPOGRAPHY_API_KEY` injected
only through the launcher's own `-EnvironmentVariable` parameter — never written to `settings.json`, never
logged. Point `settings.json` at `"mode": "fetch"`, the example-site parcel-polygon AOI, `pointBudget` 15000, and
U.S. survey foot. Run "Create Toposolid." This is a real, live OpenTopography request, expected to cost two
API calls against the daily quota (the bare AAIGrid body, then a second GTiff-only GeoKeys request). *Maps
to:* AC2 (first half). *Pass:* success, no error-catalogue row fires, both API calls are accounted for. **On a
401/403**: run one fetch through `SolidGround.Cli`'s own `fetch` command, outside Revit, with the same key,
before concluding the add-in is at fault; if the CLI fetch also fails, the key itself is the problem and this
step falls back to `"mode": "process"` against the already-committed example-site fixture set — AC2 is still met
via the process-mode path, but the live-fetch demonstration did not complete this session, and that limitation
is recorded plainly rather than smoothed over.
*Evidence: the live fetch succeeded — no 401/403 fallback was needed. `OPENTOPOGRAPHY_API_KEY` was injected
only through the launcher's own `-EnvironmentVariable` parameter into this one child process, never written to
`settings.json` and never logged (the log records the request with `API_Key` redacted). **Deviation recorded
honestly:** Stage 1.5's planned `Type="Command"` reader add-in turned out not to be drivable handle-only,
because Revit 2027's Add-Ins → External Tools pulldown exposes no UI Automation children (see 1.5's own
Evidence above); this was discovered live at this step, and the plan pivoted to saving the created element
into a document, closing, and reopening it under a `DocumentOpened`-triggered reader instead (see 2.8-2.9
below) rather than reading it back in the same session via a menu click. With that pivot in place, **Create
Toposolid** was run with `"mode": "fetch"`, the example-site parcel-polygon AOI, `pointBudget` 15000, and U.S.
survey foot: it created element id 317345, 1600 of 1600 points retained (within the 15000 budget). **Pass**
for the live-fetch half of AC2.*

**2.8** Create a fresh evidence Revit project by API, before any Save — never saving into the launcher's own
template document. Hash the template before and after this whole stage as a hard-stop guard.
*Evidence: **deviation recorded honestly:** rather than an API-created fresh project, the save target actually
used was a disposable working copy of the default template (`template-copy\Default_I_ENU.rte`, 4,710,400
bytes) created in the evidence scratch folder — the element created at 2.7 was saved into that copy, never
into the installed template or the launcher's own template document. The installed template's own hash
(`C:\ProgramData\Autodesk\RVT 2027\Templates\Default_I_ENU.rte`) was confirmed unchanged both before this
stage and again at 2.12. Revit closed. **Pass**, via this disclosed substitute save target rather than the
originally planned API-created project.*

**2.9** Install the reader add-in's manifest (built at Stage 1.5) into `%APPDATA%\Autodesk\Revit\Addins\2027\`
for the first time this session, mirroring `SolidGroundProbe.addin`'s own already-observed
manifest-in-Addins/`<Assembly>`-in-`%TEMP%` pattern. Save the evidence project, close Revit, relaunch, and
reopen. Because the reader is unsigned and has never been loaded before, this relaunch also shows its own
3-choice unsigned-add-in dialog (it will never be signed; it is not part of the release signing pipeline) —
click "Load Once," since the reader add-in must actually load this session for the read-back below to run at
all. Read the Extensible Storage entity back using the reader add-in — never `SolidGroundProbe` — reimplementing
only the read half of the proven Issue #16 four-part discipline, and independently cross-check reversibility
with the existing, unmodified Issue #16 `GeoCheck` console against the recorded local-origin offset/CRS/datum.
*Maps to:* AC2 (second half). *Pass:* all 36 provenance fields match pre-save values within tolerance;
`GeoCheck`'s reconstructed source coordinate matches within its own tolerance.
*Evidence: as described at 2.7's own Evidence, the reader was installed as a `DocumentOpened`-triggered
`IExternalApplication` (its own `VendorId`, "SolidGroundEvidence") rather than the originally planned
`Type="Command"` entry. Reopening the Stage 2.8 evidence document in a fresh Revit process triggered the
reader automatically: it found the schema, read 1 toposolid, and reported all 36 fields with 0 errors —
confirming `AccessLevel.Public` read works correctly for a foreign `VendorId` after a genuine save/reopen
cycle, not merely within the authoring session. The existing, unmodified Issue #16 `GeoCheck` console then
rebuilt the EPSG:4326 ↔ EPSG:26915 (NAD83) transform with ProjNET 2.1.0 and reconstructed the local origin with
a projected round-trip delta of 0.009098 m (tolerance 0.02 m): **overall PASS.** This reopen also needed two
retries of its own: the first reopen attempt (Launch 4) surfaced the reader's own recorded field values but the
combined-failure scenario at 2.10 (below) was interleaved before this step's final confirmed pass, and the
document reopen that finally produced this passing read (Launch 5) followed one of the intermittent startup
crashes described at 2.5 and in "Known limitations" — a relaunch succeeded. **Pass.***

**2.10** Both Preflight failure paths, exercised together in one launch (ruling R5, confirmed directly against
`CreateToposolidCommand.cs`: the missing-key check at line 250-253 does not return early, and the
`Revit.ini`-threshold check invoked at line 333 runs unconditionally afterward — both append to the same
`problems` list, which is only tested as a whole at line 336/line 85, so a single "Create Toposolid" run with
`mode` temporarily set to `"fetch"`, `OPENTOPOGRAPHY_API_KEY` removed from the launch environment entirely,
and `pointBudget` set above this machine's real `NativeToposolidMaxPointThreshold`, is expected to surface both
exact rejection messages together in one dialog and one `Result.Cancelled`, never two separate launches).
Restore `settings.json` to its Stage 2.7 state immediately afterward — `RunDocumentPreflight` re-reads
`settings.json` fresh on every single `Execute()` invocation, with no static cache, so this restore is both
necessary and sufficient for the very next run to pick it up. Close Revit — `Uninstall-SolidGround.ps1` (next)
refuses outright while any `Revit.exe` process is running. *Pass:* one `Result.Cancelled`; both exact messages
appear together in the same problem list; no element is created; no transaction is ever opened.
*Evidence: **deviation recorded honestly:** the first attempt at this step surfaced only the `pointBudget`
problem, not the missing-key problem, because the launched Revit process inadvertently inherited
`OPENTOPOGRAPHY_API_KEY` from the orchestrator's own tool shell (which still carried the key from 2.7) rather
than genuinely lacking it — a key-inheritance leak in the test setup, not a defect in `RunDocumentPreflight`
itself. This was caught rather than reported as a pass; a further relaunch with the environment variable
explicitly forced empty for that one child process reproduced the intended dual-failure scenario. That
relaunch was also one of the sessions affected by the intermittent startup crash described at 2.5 (retried
successfully). With the key genuinely absent: one "Create Toposolid" run surfaced both exact messages together
in a single dialog — "The OPENTOPOGRAPHY_API_KEY environment variable is not set (or is empty)..." and
"pointBudget 25000 exceeds this machine's NativeToposolidMaxPointThreshold of 20000 in ... Revit.ini..." — one
`Result.Cancelled`, no element created, no transaction opened. `settings.json` was restored to its Stage 2.7
state immediately afterward. **Pass.***

**2.11** With Revit closed: `Uninstall-SolidGround.ps1`; confirm the Addins folder carries no SolidGround entry
and `%ProgramData%` is untouched; reinstall once more, confirming an idempotent, identical result. *Maps to:*
AC1.
*Evidence: **deviation/technique note recorded honestly:** calling the uninstaller as
`powershell -File Uninstall-SolidGround.ps1 -Confirm:$false` fails, because `-File` passes `"$false"` through
as a literal string rather than a switch value; the working invocation for a non-interactive caller is
in-process (`& .\Uninstall-SolidGround.ps1 -Confirm:$false`). Run that way, with `-AllowOtherRevitVersions`,
the uninstall passed: the Addins folder was left free of any SolidGround entry, and `settings.json`/`Logs\`
under `%ProgramData%` were kept (the default, since `-RemoveSettingsAndLogs` was not passed). Reinstalling via
`install.cmd` then passed, and `Deploy-RevitAddIn.ps1 -Verify` reported 6 of 6 files `OK` — an idempotent,
identical result. **Pass.***

**2.12** Restore every hash from 1.2, byte-for-byte, including the five certificate stores' listings and
`settings.json` (already restored at the end of 2.10; re-verified here as part of the full set, not restored a
second time). Any mismatch is a hard stop — escalate to the owner, never silently re-copy over the top. Also
remove the reader add-in entirely: its manifest (installed at 2.9) never existed at 1.2, so it is deleted
outright from the Addins folder, and its `%TEMP%\solidground-issue17\reader\` build folder is moved into the
same hashed backup the probe's own retained state already uses — nothing about the reader is part of the
final machine state.
*Evidence: a full R11 final-machine-state checklist was run item by item against the Stage 1.2 baseline, not
"restore succeeded" generically — 9 of 9 items recorded **PASS**: `settings.json` restored byte-identical
(SHA-256 confirmed equal to the backup); both pre-existing `HKCU:\...\CodeSigning` values restored; 0 of 24
baseline `SolidGround\` files still present live (old versioned deploy folders gone); the probe manifest
(`SolidGroundProbe.addin`) absent; the live `SolidGround.addin` resolves to a `0.1.0.0` build; the signing
certificate confirmed trusted in both `LocalMachine\Root` and `LocalMachine\TrustedPublisher` (thumbprint
`EEEAD0AD06069A56C44E09C1FBB26B59FA902270`) and its public copy present in `CurrentUser\CA`; the default Revit
template's hash unchanged; and 0 unexplained differences against the baseline snapshot outside those items.
The reader add-in's manifest and build folder were removed from their live locations and moved into the same
hashed backup the probe's own retained state uses. **Pass.***

**2.13** Write this note's "Evidence" section from this session's real results; present the full sequence to
the owner for explicit AC6 acceptance. Confirm the full final-machine-state checklist item by item (ruling R11),
not "restore succeeded" generically: SolidGround `v0.1.0` installed per-user from the release zip; the
SolidGround certificate trusted in `LocalMachine\Root` and `LocalMachine\TrustedPublisher` (deliberately **not**
reverted by 2.12, since the owner's own ongoing dev-loop signing depends on it staying trusted); the pre-existing
`%ProgramData%\SolidGround\Revit\settings.json` restored byte-identical; pre-existing
`HKCU:\...\CodeSigning` values restored; old versioned deploy folders, the probe manifest and folder, the
reader manifest and its build folder, and old logs kept only in a hashed backup, not restored to their live
locations; the installed Revit template hash unchanged. Only after the owner's AC6 acceptance does GitHub Release
publication (above) proceed.
*Evidence: this note's "Evidence" section and "Acceptance criteria" table below were written from this
session's real results, and this manual evidence plan's own per-step Evidence paragraphs above were filled in
directly from the session's recorded output. The final-machine-state checklist (2.12) passed all 9 items. **AC6
itself — the owner's explicit acceptance of this full sequence — is pending the owner's review as of this
writing; GitHub Release publication does not proceed until that acceptance is given.***

**Stop conditions throughout**: no launch without the owner's "go"; re-check before every click; the unified
button rule at every dialog; any diff found at 2.3 is recorded and reverted before 2.4 proceeds; any hash
mismatch on restore (2.12) is a hard stop and an escalation, never a silent fix; any elevation prompt declined
during trust import (2.4) stops that step and escalates; a template-hash mismatch at any point is an immediate
hard stop; `git tag`/`gh release create` never runs before the owner's explicit AC6 acceptance.

## Evidence

**Complete.** A live Revit 2027 session ran the full "Manual evidence plan" above end to end on 2026-09-25
against a release candidate built from commit `a515f0e` (zip SHA-256
`6456AFFE8C83223EA67DCFBA83C58276153B36F224EBE222A02455EE2BF8B36D`), Revit 2027 build 27.0.10.13 (update
2027.0.1), Windows 11. Every stage's own numbered Evidence paragraph above is filled in from that session's
real results; this section summarizes the headline outcomes and every honestly-disclosed deviation from the
plan as originally written.

**AC1 (install and load).** The documented double-click install path worked with a different Revit version
(2026) open, confirming the `-AllowOtherRevitVersions` default. Without trust import, Revit showed a dialog
titled **"Security - Invalid Signature"** (Load / Do Not Load only — not the 2-choice "Always Load"/"Do Not
Load" variant this design originally assumed; see Stage 2.2's own Evidence for the exact text). "Do Not Load"
left no SolidGround tab and no registry/certificate-store change. `Import-SigningTrust.ps1 -Confirm:$false`
imported the certificate (thumbprint and SHA-256 matched the pin exactly); on the next relaunch **no security
dialog appeared at all** and the ribbon loaded silently — the design's central, most-important previously
unevidenced claim is now confirmed. Uninstall and reinstall were also exercised and passed, including
`Deploy-RevitAddIn.ps1 -Verify` reporting 6 of 6 files `OK`.

**AC2 (example-site scenario, provenance).** A live OpenTopography `fetch` at the public example site created
element id 317345 with 1600 of 1600 points retained (budget 15000). After save, close, and reopen in a fresh
Revit process, a throwaway reader add-in (different `VendorId`) read back all 36 provenance fields with 0
errors, and the existing Issue #16 `GeoCheck` console independently reconstructed the local origin with a
0.009098 m round-trip delta (tolerance 0.02 m) — overall PASS. Both Preflight failure paths (missing key,
over-threshold `pointBudget`) surfaced together in one dialog and one `Result.Cancelled`, as designed, once a
test-setup key-inheritance issue (below) was caught and corrected.

**AC3 (icons)** — met by Issue #19's own already-complete evidence; this session's Stage 2.6 icon check was a
light re-confirmation (32 px icon rendered correctly, dark theme).

**AC4 (no unlawfully bundled native/Autodesk binaries)** — the packaging run that actually exercised
`New-ReleasePackage.ps1`'s preconditions 2 and 9 against a real built zip has now happened (producing the
`a515f0e` release candidate above); no native binary, `runtimes\` folder, or Autodesk assembly is bundled.

**AC5 (accuracy statement)** — met; unaffected by this issue's changes.

**AC6 (owner acceptance)** — **pending the owner's review.** GitHub Release publication does not proceed until
this is given.

**Deviations from the plan, disclosed rather than smoothed over** (see each stage's own Evidence paragraph for
full detail):

- **Save target (Stage 2.8).** The created toposolid was saved into a disposable working copy of the default
  template (`template-copy\Default_I_ENU.rte`), not an API-created fresh project as originally planned. The
  installed template's own hash was confirmed unchanged throughout.
- **Reader mechanism (Stages 1.5, 2.7, 2.9).** The planned `Type="Command"` reader add-in cannot actually be
  driven handle-only: Revit 2027's Add-Ins → External Tools pulldown exposes no UI Automation children. The
  reader was pivoted, live, to a `DocumentOpened`-triggered `IExternalApplication` instead; its read-half
  implementation (`GetEntitySchemaGuids()`, `Schema.Lookup`, `Element.GetEntity()`, per-field reads) was
  unchanged.
- **Key-inheritance workaround (Stage 2.10).** The first attempt at the combined-failure scenario
  inadvertently inherited `OPENTOPOGRAPHY_API_KEY` from the orchestrator's own tool shell (left over from
  Stage 2.7), masking the missing-key problem. This was caught, not reported as a pass; a retry with the
  variable explicitly forced empty for that one child process reproduced the intended dual-failure dialog.
- **Retries.** Three of seven Revit launches with SolidGround loaded crashed during document-open at startup
  (an intermittent Revit 2027.0.1 process crash, not a SolidGround defect — see "Known limitations" below);
  every one succeeded on a relaunch. The uninstaller also required an in-process invocation
  (`& .\Uninstall-SolidGround.ps1 -Confirm:$false`) rather than `powershell -File ... -Confirm:$false`, which
  fails because `-File` passes `"$false"` through as a literal string.
- **Elevation (Stage 2.4).** The orchestrator's PowerShell session was already elevated for this work, so no
  new UAC prompt was rendered for the owner to approve at this specific invocation; the elevation ruling R2
  calls for was already in place going in.

The final-machine-state checklist (Stage 2.12) confirmed 9 of 9 items restored or intentionally left changed
(the certificate trust) exactly as designed.

## Acceptance criteria

| # | Acceptance criterion | Status | Notes |
| --- | --- | --- | --- |
| AC1 | A clean supported workstation can install and load SolidGround in Revit 2027 using documented steps. | **Met** | Live-evidenced end to end 2026-09-25: install (Stage 2.1), the no-trust-import "Security - Invalid Signature" dialog and its "Do Not Load" consequence (Stages 2.2-2.3), trust import (Stage 2.4), durable dialog suppression after trust (Stage 2.5), and uninstall/reinstall (Stage 2.11). See "Evidence" above. |
| AC2 | The example-site scenario creates a bounded Toposolid and retains readable reversible provenance after save/reopen. | **Met** | Live-evidenced 2026-09-25 through the packaged, signed, installed release itself (Stages 2.7-2.9): element 317345, 1600/1600 points, 36/36 provenance fields read back with 0 errors after save/reopen, `GeoCheck` round-trip PASS (0.009098 m, tolerance 0.02 m). Issue #15 and Issue #16 had already live-evidenced the underlying capability directly; this run re-demonstrates it through this release path specifically. |
| AC3 | The reviewed 16×16 and 32×32 icons render correctly in the supported Revit ribbon contexts. | **Met (by Issue #19)** | `docs/architecture/revit-ribbon-icons.md`'s own Evidence section already carries a 2026-09-24 Revit 2027 session finding both sizes pixel-exact in both ribbon themes; Stage 2.6 above is a light re-confirmation (32 px, dark theme), not new evidence this issue depends on. |
| AC4 | No native geospatial binaries or Autodesk assemblies are committed or bundled unlawfully. | **Met** | Nothing native or Autodesk-owned is committed to this repository today, and the 2026-09-25 packaging run that produced the `a515f0e` release candidate exercised `New-ReleasePackage.ps1`'s preconditions 2 and 9 (`THIRD-PARTY-NOTICES` completeness; native-binary/`runtimes\` rejection) against real build output without tripping either. |
| AC5 | README retains the site-form accuracy limit and does not claim survey-grade output. | **Met** | `README.md`'s `## Accuracy` section is untouched by this issue's own edits (see `docs/revit-install-guide.md`'s own `## Accuracy` section, which points at it rather than repeating or paraphrasing it, and `README.md` itself); this note adds no claim of survey-grade output anywhere. |
| AC6 | The owner accepts the Revit 2027 end-to-end result. | **Pending the owner's review** | Stages 1-2 have run and this note's "Evidence" section above is filled in from that real session; the owner's own explicit acceptance has not yet been given. GitHub Release publication (see "GitHub Release" above) does not proceed until it is. |

## Known limitations

- This certificate is self-signed, not publicly trusted: a workstation that never runs
  `Import-SigningTrust.ps1` will still see Revit's own "Security - Invalid Signature" dialog every time it
  loads a new SolidGround build, documented as a supported, lower-trust alternative rather than a defect.
- **Resolved by live evidence (2026-09-25):** the trust-import mechanism (`LocalMachine\Root`/
  `LocalMachine\TrustedPublisher`) has now been exercised for a real SolidGround-signed build; the central
  "durably suppresses the dialog" claim (Stage 2.5 above) is confirmed — no security dialog appeared on
  relaunch after trust import, tested against a machine this same session had just proven genuinely untrusted.
- The signed-but-not-yet-trusted dialog's actual shape ("Security - Invalid Signature," buttons **Load** /
  **Do Not Load** only) turned out to differ from this design's original assumption of a 2-choice "Always
  Load"/"Do Not Load" variant of the unsigned prompt — there is no "Always Load" button on this dialog at all.
  Whether clicking **Load** (rather than "Do Not Load," the only button this session exercised) also suppresses
  the dialog on a *future* launch of the same build remains unknown; this design continues to recommend "Do
  Not Load" (or the trust import) over "Load" until that is observed.
- **New, found live (2026-09-25): an intermittent Revit 2027.0.1 (build 27.0.10.13) startup crash.** Revit can
  crash (`ntdll.dll` exception `0xc0000374`, heap corruption, offset `0x117eb5`) while opening a document
  shortly after launch. In this session: 3 of 7 launches with SolidGround loaded crashed (two with only
  SolidGround and pyRevit, one with SolidGround plus the evidence reader); 0 of 2 launches without SolidGround
  loaded crashed; 1 of 1 launch of an unsigned build of the same commit did not crash — so the crash is not
  specific to SolidGround or to signing. Root cause unknown (no dump analysis performed). A relaunch succeeded
  every time. Recommended mitigation: install the latest available Revit 2027 update and retest; report if it
  persists. Documented in `docs/revit-install-guide.md`'s troubleshooting table.
- A dirty working tree's effect (if any) on the SDK's own auto-embedded commit SHA is not yet confirmed by a
  real build; the clean-tree packaging precondition, not the SDK's own git-dirty detection, is what actually
  guarantees a shipped binary's embedded commit hash is truthful either way.
- **Resolved** (Issue #17 dry-run defect, found and fixed after this note's initial drafting): a real local
  packaging run originally omitted `-p:ContinuousIntegrationBuild=true` from its `dotnet restore`/`build`/
  `test` calls, so — unlike CI, which sets it via `'$(CI)' == 'true'` — the packaging operator's own Windows
  account name shipped inside `SolidGround.Revit.dll`/`.pdb` and `SolidGround.Core.dll`/`.pdb`'s embedded
  debug paths. Fixed by passing the flag explicitly (step 6 above) plus a fail-closed scan of every staged
  file for a build-machine-local path (step 9a above) as a backstop that does not depend on the flag alone.
- All-user installation, a publicly trusted (CA-issued) certificate, and MSI-style packaging remain explicitly
  out of scope for this milestone; see "What this note does not do" below.

## Sources

- GitHub Issue #17, "[Phase 2] Package, install, and validate the Revit 2027 release" (Scope and Acceptance
  criteria).
- `AGENTS.md`, "Mission and current boundary," "Revit add-in conventions," and "Dependency policy."
- `docs/architecture/revit-add-in-conventions.md`, owner decision 3 and sections 7 ("Deployment and per-user
  install") and 9 ("Packaging, versioning, and release").
- Autodesk, Revit 2027 API Developers Guide: "Digitally Signing Your Revit Add-in," "Digitally Signing Your
  App," "Making Your Own Certificate for Testing and Internal Use"
  (`help.autodesk.com/view/RVT/2027/ENU/`).
- Autodesk, Revit 2027 product help: "Security: Unsigned File or Add-In" (`GUID-36D29367-...`), "Security:
  Signed File or Add-In" (`GUID-900A3EBF-...`), "About Digital Signatures" (`GUID-1C5947F2-...`).
- Autodesk, "Major changes and renovations to the Revit API," 2027 section (`guid=f7165618-...`) — the
  all-user add-in path relocation; unaffected by, and unaffecting, this issue's per-user-only scope.
- Microsoft Learn: `New-SelfSignedCertificate`, `Set-AuthenticodeSignature` (both the `powershell-5.1`- and
  `powershell-7.6`-scoped pages), `Get-AuthenticodeSignature`, the `SignatureStatus` enum, `about_Signing`,
  `X509Certificate2.Thumbprint`/`GetCertHashString`, and the `signtool.exe`/`.ps1`-corruption troubleshooting
  article.
- `github.com/PowerShell/PowerShell` issues #1752 and #26951 (the legacy Authenticode timestamp protocol, not
  RFC 3161) and #25130 (`-TimestampServer` and `https://`).
- CA/Browser Forum, Code Signing Baseline Requirements §6.2.7.4.2 (the 2023-06-01 hardware-token requirement,
  which does not reach a self-signed certificate).
- `understand/05-revit2027-signing-install-research.md` (not committed to this repository) — the read-only
  research pass this note's citations above are drawn from, including the local, non-secret system inspection
  confirming the exact `LocalMachine\Root`/`LocalMachine\TrustedPublisher` pattern is already operating for
  the owner's other Revit add-in.
- `design-record.md` (Draft 4; not committed to this repository) — Issue #17's own multi-proposal, four-review
  design record; the source for the owner decisions and rulings quoted above, the manual evidence plan's
  numbered steps, and the reasoning behind the `install.cmd` zip-placement correction.
- `scripts/Sign-RevitAddIn.ps1`, `scripts/Import-SigningTrust.ps1`, `scripts/Uninstall-SolidGround.ps1`,
  `scripts/New-ReleasePackage.ps1`, `scripts/Install-SolidGround.ps1`, `scripts/install.cmd`,
  `tests/SolidGround.Tests/ReleasePackagingTests.cs` — read directly for this note.
- `src/SolidGround.Revit/Commands/CreateToposolidCommand.cs` — read directly to confirm the Stage 2.10
  single-launch, accumulated-problem-list behavior.

## What this note does not do

This note records Issue #17's design decisions and the scripts that implement them; it does not itself mint
a certificate, build or publish a release, or run any Revit session — all of that is the orchestrator's own
later, explicitly authorized work (Stages 1.1 onward above), not this documentation unit's. It does not
change `scripts/Deploy-RevitAddIn.ps1` (ruling R10) and does not add all-user installation, a publicly
trusted certificate, or MSI-style packaging — any of those would need their own explicit implementation task,
per `AGENTS.md`'s "Mission and current boundary." Its own "Evidence" section and the "Manual evidence plan"
above's per-step Evidence paragraphs have now been filled in from the real 2026-09-25 Revit 2027 session
described there; this note does not itself grant AC6 (the owner's acceptance) or publish the GitHub Release,
both of which remain the owner's and the orchestrator's own separate, later steps.
