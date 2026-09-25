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
authority, Revit will show a security prompt the first time it loads a new SolidGround build, unless you have
already trusted the certificate (next section). Which exact prompt you see depends on whether the build is
signed at all:

- **Unsigned build** (only during ordinary development, never a real release): a 3-choice prompt —
  **Always Load**, **Load Once**, **Do Not Load**.
- **Signed release build, certificate not yet trusted**: a 2-choice prompt — **Always Load**, **Do Not
  Load** (no "Load Once" option on this shape).

**What to click:** click **Load Once** if the dialog offers it. If it does not (the 2-choice shape above),
click **Do Not Load**. Do not click **Always Load** on either shape. We deliberately do not recommend "Always
Load" here: what it actually persists for a *signed* SolidGround build has not yet been confirmed by a live
Revit 2027 session (see `docs/architecture/revit-release-packaging-and-signing.md`'s "Known limitations"),
and clicking it could leave registry state on your machine whose effect we cannot yet describe accurately.

**The practical consequence of "Do Not Load":** on the signed-but-not-yet-trusted 2-choice prompt, "Do Not
Load" means SolidGround simply does not load for that Revit session, and you will see the same prompt again
the next time you launch Revit. This is a real, honestly disclosed limitation of skipping the trust-import
step below, not a bug — if you want to actually use SolidGround without repeating this decision every launch,
import trust once as described next.

## 5. Trusting the certificate (optional, one time per workstation)

Importing SolidGround's signing certificate into your machine's trusted-root store is what makes the
security prompt above stop appearing for SolidGround builds signed by this certificate, on this workstation,
from then on.

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
certificate should load without any security prompt.

**Skipping this step is a fully supported, documented alternative** for anyone unwilling to make a
systemwide trust change: you simply keep seeing the prompt described in "First launch" above every time
Revit loads a new SolidGround build.

## 6. Setting your OpenTopography API key

`fetch` mode (a live terrain download from OpenTopography) needs an API key with USGS 1-meter access. `process`
mode, which reads a local elevation file instead, does not need one.

Set `OPENTOPOGRAPHY_API_KEY` as a **persistent** environment variable for your Windows user account — not a
one-off `$env:` assignment in a single PowerShell window, which disappears as soon as that window closes and
is never visible to Revit at all. In PowerShell:

```powershell
[Environment]::SetEnvironmentVariable('OPENTOPOGRAPHY_API_KEY', '<your key>', 'User')
```

Or, without a terminal: open **Edit environment variables for your account** from the Start menu, add a new
user variable named `OPENTOPOGRAPHY_API_KEY` with your key as its value, and click OK.

Either way, **fully restart Revit 2027** afterward. Revit (and every other already-running program) only
sees environment variables as they were at the moment it started; setting the variable does not change
anything for a process that is already running. This is different from `SolidGround.Cli`'s own
`dotnet user-secrets` option, which is a developer-only mechanism this Revit add-in does not use.

Never share this key, paste it into `settings.json`, or paste it into a chat, log, or issue — SolidGround
itself never writes it to a file or logs it, and you should not either.

## 7. `settings.json`

SolidGround reads its configuration from one machine-wide file:

```
%ProgramData%\SolidGround\Revit\settings.json
```

The first time you click **Create Toposolid** with no `settings.json` present, SolidGround writes a starting
template there for you and stops, asking you to edit it and try again. The template looks like this:

```jsonc
// %ProgramData%\SolidGround\Revit\settings.json
// SolidGround edits this file only to create it; it never rewrites an existing one.
// Delete or rename this file to have SolidGround regenerate this template on the next run.
{
  // "fetch": call OpenTopography live (needs OPENTOPOGRAPHY_API_KEY in Revit's own process environment).
  // "process": read a local AAIGrid .asc/.prj pair (and optional .source.json sidecar) from disk, no network.
  "mode": "process",

  "areaOfInterest": {
    // "boundingBox" | "radius" | "parcel" -- give exactly the matching object below.
    "kind": "parcel",
    "boundingBox": null,
    "radius": null,
    "parcel": { "path": "C:\\SolidGround\\parcel.geojson", "format": "geojson", "bufferMeters": 0.0 }
  },

  // Required when mode is "process"; ignored (may be omitted) when mode is "fetch".
  "process": {
    "asc": "C:\\SolidGround\\terrain.asc",
    "prj": null,
    "sourceJson": null,
    "sourceName": null, "dataset": null,
    "verticalDatum": null, "verticalUnit": null, "geoid": null,
    "collectionStart": null, "collectionEnd": null, "qualityLevel": null
  },

  // "southwest" | "centroid" | "explicit". x/y/z are only read when kind is "explicit".
  "localOrigin": { "kind": "southwest", "x": 0.0, "y": 0.0, "z": 0.0 },

  // "usSurveyFoot" | "internationalFoot" | "meter" -- exact 1200/3937 m and 0.3048 m definitions.
  "outputUnit": "usSurveyFoot",

  "simplification": { "method": "curvatureAware", "pointBudget": 15000, "coverageFloorFraction": 0.2 },

  // Blank/null means: pick the existing Level with the lowest elevation (ties by name).
  "level": { "name": null },
  // Blank/null means: pick the first existing ToposolidType by name.
  "toposolidType": { "name": null },

  "output": { "directory": "C:\\ProgramData\\SolidGround\\Revit\\Exports", "baseName": "terrain" },

  "networkTimeoutSeconds": 300
}
```

To fetch live from OpenTopography instead of reading a local file, change `"mode"` to `"fetch"` and fill in
`areaOfInterest.boundingBox` or `.radius` (or keep `.parcel`) instead of relying on `process`, which is
ignored once `mode` is `"fetch"`. For the full field-by-field reference — every accepted value, every
default, and every validation rule — see
[the toposolid creation design note](architecture/revit-toposolid-creation.md)'s "Settings file reference"
section. SolidGround re-reads this file fresh every time you click **Create Toposolid**; you never need to
restart Revit after editing it.

## 8. First run

1. Open or start a Revit 2027 project (not a family document).
2. Find the **SolidGround** tab on the ribbon, with one panel and one button, **Create Toposolid**.
3. Click it. SolidGround first runs a read-only check (no changes to your model yet): it confirms a project
   is open, that `settings.json` exists and is valid, that your area of interest is reachable, and — for
   `fetch` mode — that `OPENTOPOGRAPHY_API_KEY` is set. If anything is wrong, a dialog titled **SolidGround**
   reports every problem it found at once (for example, "SolidGround Preflight found a problem... Nothing
   changed. Correct every problem below and run this command again.") and nothing in your model changes.
4. If everything checks out, SolidGround acquires the terrain (this can take up to the configured
   `networkTimeoutSeconds` for a live fetch, during which Revit is briefly unresponsive), then creates one
   native Toposolid element. On success you will see "SolidGround created the toposolid."
5. Save your project normally. The created Toposolid carries its own provenance (source, datum, units, and
   the offset needed to reverse the coordinate transform) attached directly to the element, so it survives
   save and reopen.

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
| "The OPENTOPOGRAPHY_API_KEY environment variable is not set (or is empty)" | The key is missing, was set in the wrong scope (a one-off `$env:` assignment, or a different user account), or Revit was not restarted after you set it. | Set it as a **persistent, User-scope** variable (see "Setting your OpenTopography API key" above), then fully restart Revit. |
| "pointBudget ... exceeds this machine's NativeToposolidMaxPointThreshold ..." | Your `settings.json`'s `simplification.pointBudget` is higher than this machine's own `Revit.ini` `NativeToposolidMaxPointThreshold` setting. | Lower `pointBudget` in `settings.json` to at most the value the message names, or raise the `Revit.ini` setting within Autodesk's documented 10,000–50,000 range and restart Revit. |
| "A starting template was written to '...\settings.json'. Edit it and run this command again." | This is the very first run on this machine; no settings file existed yet. | Not an error. Edit the newly written template (see "settings.json" above) and click **Create Toposolid** again. |
| Nothing happens when you double-click `install.cmd`, or it closes immediately | PowerShell's execution policy is enforced by Group Policy at a scope `-ExecutionPolicy Bypass` cannot override. | Run `Install-SolidGround.ps1` directly from a PowerShell window to see the actual error, or contact your system administrator about the enforced policy. |
| "Refusing to deploy: a Revit.exe process is running" (or, via `install.cmd`'s own `-AllowOtherRevitVersions`, "...a Revit.exe process under '...\Revit 2027' is running") | Revit 2027 itself is open. This refusal is never overridden by `-AllowOtherRevitVersions` (which `install.cmd` already passes for you) — it only narrows the check to ignore a *different* Revit version. | Close Revit 2027 fully, then run `install.cmd` again. Other Revit versions (for example Revit 2026) can stay open. |
| The ribbon button is greyed out or the tab is missing | The add-in did not load — check whether a security prompt was answered "Do Not Load," or whether Revit was ever restarted after installing. | Relaunch Revit; if a prompt appears, follow "First launch" above. |

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
