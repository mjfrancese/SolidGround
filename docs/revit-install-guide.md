# Installing SolidGround for Revit 2027

This guide is for installing a downloaded SolidGround release into Revit 2027 on an ordinary Windows
workstation. It assumes no development tools, no source checkout, and no prior familiarity with this
repository — only a working Revit 2027 installation and (for live terrain fetches) an OpenTopography API
key. If you are building SolidGround from source instead, see [`README.md`](../README.md) and
[`scripts/README.md`](../scripts/README.md).

A copy of this guide, `INSTALL.md`, ships inside every release zip; the two are identical.

## What you need

- Windows, with Revit 2027 already installed.
- The SolidGround release zip, downloaded from the project's
  [GitHub Releases page](https://github.com/mjfrancese/SolidGround/releases).
- For a live terrain fetch (the `fetch` acquisition mode): an OpenTopography API key with USGS 1-meter
  access. `process` mode, which reads a local elevation file instead, needs no key.
- Administrator rights are needed only for the optional trust-import step below; everything else installs
  per-user, with no administrator prompt.

## 1. Download and verify

Download the release zip and its checksum file from the Releases page:

- `SolidGround-Revit2027-v<version>.zip` — the release itself.
- `SolidGround-Revit2027-v<version>.zip.sha256` — a one-line SHA-256 hash of the zip, for verifying the
  download before you extract anything.

To verify the download in PowerShell, compute the zip's own hash and compare it against the value in the
`.sha256` file — not by eye (the two commands' raw output is not directly comparable: `Get-FileHash` prints
an uppercase hash inside a table, while the `.sha256` file holds a lowercase hash followed by the file name
on one line), but with a command that actually performs the comparison and reports the result:

```powershell
$expectedHash = (Get-Content .\SolidGround-Revit2027-v<version>.zip.sha256).Split(' ')[0]
$actualHash = (Get-FileHash .\SolidGround-Revit2027-v<version>.zip -Algorithm SHA256).Hash
$actualHash -eq $expectedHash.ToUpper()
```

`True` confirms the zip itself was not corrupted or altered in transit; it does not depend on trusting
anything about SolidGround's own signing certificate yet. `False` means re-download the zip before going any
further.

Once extracted (next step), the zip also contains a `SHA256SUMS` file listing every individual file's own
hash, in the conventional `sha256sum`/`Get-FileHash` format — useful if you want to verify individual files
rather than the archive as a whole.

## 2. Extract

Extract the zip to any folder you like (for example, your Downloads folder, or a folder under
`C:\SolidGround\`). You should see:

```
SolidGround-Revit2027-v<version>\
├── INSTALL.md              (this guide)
├── THIRD-PARTY-NOTICES
├── LICENSE
├── SHA256SUMS
├── install\
│   ├── install.cmd
│   ├── Install-SolidGround.ps1
│   ├── Uninstall-SolidGround.ps1
│   ├── Deploy-RevitAddIn.ps1
│   ├── Import-SigningTrust.ps1
│   └── signing-certificate.json
└── payload\
    (the add-in's own DLLs and manifest — you do not need to touch this folder directly)
```

**Mark of the Web.** Windows marks every file extracted from a downloaded zip as coming from the internet
(a hidden "Zone.Identifier" marker). This is normal and expected; the install script in the next step clears
it automatically from every extracted file. You do not need to right-click each file and choose "Unblock"
yourself.

## 3. Install

**Close Revit 2027 before installing; other Revit versions can stay open.** The installer always refuses to
run while Revit 2027 itself is open (add-ins cannot be safely replaced underneath a running session), but it
no longer needs every other Revit version closed too — if you keep Revit 2026, for example, open for unrelated
work, the double-click path below still proceeds.

Open the extracted folder's `install\` subfolder and double-click **`install.cmd`**.

If you would rather run it from a terminal, the equivalent command, run from inside the `install\` folder, is:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install-SolidGround.ps1 -AllowOtherRevitVersions
```

`-AllowOtherRevitVersions` is what lets this command proceed while a *different* Revit version is open; it
never overrides the refusal while Revit 2027 itself is running. `install.cmd` already passes this switch for
you, so the double-click path above needs nothing extra — this terminal command matches it exactly. Omit the
switch only if you specifically want the stricter behavior of refusing while any Revit version at all is open.

`-ExecutionPolicy Bypass` here only affects this one command; it does not change any setting on your
computer. (If your organization enforces PowerShell execution policy through Group Policy, that policy can
still block this — contact your administrator in that case.)

**If Windows shows an "Open File - Security Warning"** that says "The publisher could not be verified. Are
you sure you want to run this software?", with Publisher "Unknown Publisher" and Type "Windows Command
Script": this is Windows' standard prompt for a downloaded `.cmd` file. A `.cmd` file cannot carry a digital
signature, so it always reads "Unknown Publisher", even though the PowerShell scripts and DLLs it runs are
signed and checked. Click **Run**. (Observed on Windows 11 when `install.cmd` from a downloaded zip was
opened through the Windows shell.)

**If Windows shows a "Windows protected your PC" SmartScreen warning** when you run `install.cmd`: this is a
Windows feature that flags newly downloaded, unsigned `.cmd`/`.bat` files with no track record yet — it is
not specific to SolidGround, and it is a different mechanism entirely from Revit's own add-in security prompt
described below. Click **More info**, then **Run anyway**. This prompt may not appear at all if this
particular file has already built up some reputation with Windows.

The install script will:

1. Clear the Mark-of-the-Web marker from every extracted file.
2. Check that every signed file (the two SolidGround DLLs and the install scripts themselves) is genuinely
   signed by the SolidGround certificate — it stops with a clear error if anything looks tampered with or
   was never signed at all, before it changes anything on your computer.
3. Tell you whether the SolidGround signing certificate is already trusted on this machine, and print the
   command to trust it (see "Trusting the certificate" below) if it is not — this is informational only and
   never blocks the install.
4. Deploy the add-in into your per-user Revit 2027 Add-Ins folder
   (`%APPDATA%\Autodesk\Revit\Addins\2027`).

When it finishes, **fully restart Revit 2027** if it is currently running — add-ins load once at startup and
are not hot-reloaded.

## 4. First launch and the security prompt

Because SolidGround is signed with a self-signed certificate rather than one issued by a public certificate
authority, Revit does not trust it out of the box, unless you have already trusted the certificate (next
section). Which exact prompt you see depends on whether the build is signed at all — these are two visibly
different dialogs, not two variants of the same one:

- **Unsigned build** (only during ordinary development, never a real release): a 3-choice prompt —
  **Always Load**, **Load Once**, **Do Not Load**.
- **Signed release build, certificate not yet trusted** (the normal state on a first install, before you run
  the trust import below): a dialog titled **"Security - Invalid Signature"**, reading "This signed add-in
  has a security problem. What do you want to do?", followed by the add-in's Name, Publisher, Location,
  Issuer, and Date, and "This could mean that this add-in has been tampered with, or that the publisher's
  certificate has been revoked. We recommend that you do not load it." This dialog has exactly two buttons,
  **Load** and **Do Not Load** — there is no "Always Load" button here. (Observed on Windows 11 / Revit 2027
  build 27.0.10.13 against a genuinely signed SolidGround release, before importing trust.)

**What to click:** on the unsigned 3-choice prompt, click **Load Once** if you need to load a development
build without importing trust; do not click **Always Load** there either, since what it actually persists has
not been confirmed by a live Revit 2027 session. On the signed "Security - Invalid Signature" dialog, click
**Do Not Load** unless you specifically want SolidGround to run for that one session anyway — **importing the
certificate (next section) is the recommended way to stop this dialog from appearing**, rather than answering
it on every launch.

**The practical consequence of "Do Not Load":** on the signed "Security - Invalid Signature" dialog, "Do Not
Load" means SolidGround simply does not load for that Revit session, and you will see the same dialog again
the next time Revit loads this build. This is a real, honestly disclosed limitation of skipping the
trust-import step below, not a bug — if you want to actually use SolidGround without repeating this decision
every launch, import trust once as described next.

**The practical consequence of "Load" instead:** SolidGround loads for that Revit session. Whether choosing
"Load" also suppresses the dialog the *next* time Revit loads the same build has not been verified in a live
session — only "Do Not Load" has been directly exercised end to end. Until that is confirmed, treat "Load" as
a per-session choice only; use the trust import below for a durable fix.

## 5. Trusting the certificate (optional, one time per workstation)

Importing SolidGround's signing certificate into your machine's trusted-root store is what makes the
"Security - Invalid Signature" dialog described above stop appearing for SolidGround builds signed by this
certificate, on this workstation, from then on.

**What this actually does, in plain terms:** it adds SolidGround's certificate to your computer's
**systemwide** list of trusted certificate authorities and trusted publishers (`Cert:\LocalMachine\Root` and
`Cert:\LocalMachine\TrustedPublisher`). This is comparable to telling Windows to trust a new certificate
authority, not merely "trust this one app" — it is a machine-wide change, not a per-user or per-application
one. It requires an administrator prompt (UAC) because of that scope.

To import it, open a PowerShell window **as Administrator**, `cd` into the extracted zip's `install\` folder,
and run:

```powershell
.\Import-SigningTrust.ps1
```

The script reads the certificate directly from the signed `SolidGround.Revit.dll` in the same release, prints
its SHA-1 thumbprint and SHA-256 hash, and cross-checks that hash against the pinned value in
`signing-certificate.json` (the same file that ships alongside it) before touching anything. Cross-check the
printed values yourself against the ones recorded in
[`docs/architecture/revit-release-packaging-and-signing.md`](architecture/revit-release-packaging-and-signing.md)
(viewable on GitHub over HTTPS) if you want an independent confirmation before approving the UAC prompt. This
cross-check catches transport corruption or accidental tampering; it does not protect against a compromised
publisher account, since both the zip and the published thumbprint would come from the same account in that
case.

After this runs successfully, fully restart Revit 2027. From then on, SolidGround builds signed by this same
certificate load without the "Security - Invalid Signature" dialog.

**Skipping this step is a fully supported, documented alternative** for anyone unwilling to make a
systemwide trust change: you simply keep seeing the dialog described in "First launch" above every time
Revit loads a new SolidGround build.

## 6. Set up sources and preferences

Open **SolidGround → Settings**. Setup takes place in this window; you do not need to edit a settings file.

For live elevation, choose **Fetch** and use the [OpenTopography portal](https://portal.opentopography.org/)
to request a key through **myOpenTopo**. USGS 1 m requires academic authorization or enterprise access.
Enter your key in the masked OpenTopography field and choose **Use for this Revit session**, or save Settings.
The explicit session key works immediately and lasts until you clear it or exit Revit. It is never saved to
settings, exports, or provenance. An existing `OPENTOPOGRAPHY_API_KEY` environment value remains available
when no session override is active; changing that environment value requires restarting Revit.

For offline elevation, choose **Process** and browse to your AAIGrid raster, projection sidecar, and optional
source metadata. This mode does not require an OpenTopography key.

Choose either a licensed local parcel file or configure an authorized county parcel service in **Sources**.
County setup reads the service's metadata so you can choose its polygon layer and parcel fields. Review its
attribution and license/disclaimer, then explicitly acknowledge authorized use. No county service is
silently selected for you. Census is the default keyless address geocoder; Geocodio and Esri are optional
providers with their own masked session keys.

In **Terrain**, set your output unit, maximum point count, and **Terrain beyond property line** distance.
The distance uses your selected display format, including feet, inches, feet-and-inches, U.S. survey feet,
and metres. Converting the display format preserves the physical distance. This extension changes only
the terrain; the original legal property line stays in place. Advanced options hold the simplification
method, coverage fraction, timeout, and placement preferences. Choose your export folder in **Files**.

## 7. Saved preferences and recovery

Preferences are saved per operator under `%LOCALAPPDATA%\SolidGround\Revit\`. Keys and the shared-coordinate
opt-in are not saved. You can reopen Settings from the ribbon or from the creation dialog.

If an older `%ProgramData%\SolidGround\Revit\settings.json` exists, SolidGround offers **Review/import legacy
settings** or **Start new settings**. Import leaves the original file untouched and resolves its relative
input paths against its original folder. Review the imported settings and save when ready.

Malformed or unsupported settings open a repair choice instead of silently resetting preferences.
If another session changed saved settings while your draft was open, reload the saved version or explicitly
reapply your draft. Cancel preserves the saved bytes. These actions are available in the UI.

## 8. Create your first terrain

1. Open a Revit 2027 project and choose **SolidGround → Create Toposolid**.
2. Enter a street address or switch to the separately labeled latitude and longitude fields, then click
   **Find** once. Successful lookup advances to Parcel automatically.
3. Compare any location alternatives. Select the parcel and inspect its boundary, containment/nearby
   status, area, and source details. **Use this parcel** confirms the displayed location and legal boundary.
   If a source needs setup, open Settings and return to the preserved location.
4. In Review, check the terrain estimate, extension, point budget, export destination, Level, and toposolid
   type. Shared-coordinate changes start off for every run; enable them only when you intend that write.
5. Click **Create toposolid**. Preflight checks the model and effective settings before acquisition and
   before a transaction. Creation failures roll back the transaction. A duplicate or stale SolidGround
   terrain is disclosed before any new element is created.
6. On success, use **Show terrain** or **Open export folder** if helpful, then save your project normally.
   Source, unit, local-origin, and parcel provenance remain attached to the terrain after save/reopen.

Full context is the default display. Expanded terrain can reduce boundary effects, but it is not a promise
of survey accuracy. Automatic parcel subdivision/hiding is awaiting Revit 2027 validation.

## 9. Logs

SolidGround writes a daily log file to:

```
%ProgramData%\SolidGround\Revit\Logs\SolidGround.Revit-<yyyy-MM-dd>.log
```

If that machine-wide folder is not writable on your account, SolidGround falls back automatically to
`%LOCALAPPDATA%\SolidGround\Revit\Logs\` and logs that it did so. A long problem list is capped inside any
on-screen dialog; the full list is always written to this log folder. Query strings and API keys are never
written to these logs.

## 10. Troubleshooting

| Symptom | Likely cause | Fix |
| --- | --- | --- |
| The security prompt reappears every time you launch Revit, even after importing trust | You updated to a new SolidGround build (a rebuild changes the file's bytes, which resets Revit's own per-build trust decision even under an unchanged certificate), or the trust import did not actually complete. | Re-run `Import-SigningTrust.ps1` as Administrator; confirm it reports success and prints the expected thumbprint. |
| No OpenTopography key is available | Neither a session override nor an environment key is available. | Open Settings → Sources, enter the masked key, and use it for this session. No restart is needed for a session key. |
| "pointBudget ... exceeds this machine's NativeToposolidMaxPointThreshold ..." | Your saved budget exceeds the running machine's threshold. | Open Settings → Terrain and lower Maximum terrain points to the value named in the message. |
| Saved settings changed while the editor was open | Another session saved preferences after this draft was loaded. | Use Reload saved settings or explicitly reapply the draft in Settings; an ordinary Save cannot overwrite the competing changes. |
| Nothing happens when you double-click `install.cmd`, or it closes immediately | PowerShell's execution policy is enforced by Group Policy at a scope `-ExecutionPolicy Bypass` cannot override. | Run `Install-SolidGround.ps1` directly from a PowerShell window to see the actual error, or contact your system administrator about the enforced policy. |
| "Refusing to deploy: a Revit.exe process is running" (or, via `install.cmd`'s own `-AllowOtherRevitVersions`, "...a Revit.exe process under '...\Revit 2027' is running") | Revit 2027 itself is open. This refusal is never overridden by `-AllowOtherRevitVersions` (which `install.cmd` already passes for you) — it only narrows the check to ignore a *different* Revit version. | Close Revit 2027 fully, then run `install.cmd` again. Other Revit versions (for example Revit 2026) can stay open. |
| The ribbon button is greyed out or the tab is missing | The add-in did not load — check whether a security prompt was answered "Do Not Load," or whether Revit was ever restarted after installing. | Relaunch Revit; if a prompt appears, follow "First launch" above. |
| Revit closes unexpectedly while opening a document, right after starting up | An intermittent Revit 2027.0.1 (build 27.0.10.13) crash (`ntdll.dll` exception `0xc0000374`, heap corruption) has been observed during document-open at startup. It is not specific to SolidGround or to signing — it has also been seen with SolidGround not loaded at all, and with an unsigned build of the same code. | Simply relaunch Revit; every observed occurrence succeeded on a retry. If it persists, install the latest available Revit 2027 update and try again; report it if it still persists after updating. |

## 11. Upgrading

Download and extract the new release zip, then run its own `install.cmd`/`Install-SolidGround.ps1` the same
way as a first install — it deploys the new version alongside the previous two, atomically switches the live
manifest over to it, and prunes older versions automatically. Your `settings.json` and logs under
`%ProgramData%\SolidGround\Revit\` are untouched by an upgrade. If the new build is signed by the same
certificate you have already trusted, you may still see the security prompt once for the new build's own
bytes (see "Troubleshooting" above) — this is expected, not a sign that the trust import failed.

**Downgrading**: the simplest path is to extract an *older* release zip and run its own installer the same
way; it redeploys that release's exact signed bytes as a new versioned folder and repoints the manifest to
it. If you need to fall back to a specific already-deployed folder instead, edit `<Assembly>` in
`%APPDATA%\Autodesk\Revit\Addins\2027\SolidGround.addin` to point at one of the retained
`SolidGround\<stamp>\SolidGround.Revit.dll` folders.

## 12. Uninstalling

From the extracted zip's `install\` folder (or a repository checkout's `scripts\` folder):

```powershell
.\Uninstall-SolidGround.ps1
```

This removes the SolidGround manifest and every retained versioned deployment folder. By default it leaves
your `settings.json` and logs in place under `%ProgramData%\SolidGround\Revit\` (pass
`-RemoveSettingsAndLogs` to remove those too), and it never touches the certificate-trust registry value
Revit itself maintains — removing trust is a separate, manual step (see
[`docs/architecture/revit-release-packaging-and-signing.md`](architecture/revit-release-packaging-and-signing.md)'s
"Trust-import procedure per workstation" if you also want to remove that). It refuses to run while Revit 2027
itself is open; close Revit 2027 first. **If you keep another Revit version open** (for example Revit 2026,
for unrelated work), add `-AllowOtherRevitVersions`:

```powershell
.\Uninstall-SolidGround.ps1 -AllowOtherRevitVersions
```

Without that switch, the uninstaller refuses while *any* Revit version is running, even one that has nothing
to do with SolidGround. It prints exactly what it removed and what it deliberately left in place.

## Accuracy

SolidGround is a site-form tool, not a survey instrument. See [`README.md`](../README.md)'s
[`## Accuracy`](../README.md#accuracy) section for the full statement of what the resulting surface is — and
is not — suitable for; this guide does not repeat or paraphrase it, so the two stay in agreement.
