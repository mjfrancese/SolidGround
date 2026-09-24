# Revit ribbon icons

**Status.** The design, refinement, polish, and recognition-testing record below is complete for both the
32×32 and 16×16 assets and their generation notes, and the final PNG bytes now ship at
`src/SolidGround.Revit/Resources/SolidGround.{32,16}.png`. Two items remain open: the Revit 2027 light/dark
evidence session below has not run, and the owner has not yet reviewed either asset (see "Manual evidence plan"
and "Known limitations" below). This note follows
`docs/architecture/revit-add-in-host-scaffold.md` and
`docs/architecture/revit-extensible-storage-provenance.md`'s own structure, tone, and citation style, and
continues `docs/architecture/revit-add-in-conventions.md`'s owner decision 5 ("Ship an icon for
`CreateToposolidCommand` in the initial milestone; Issue #19 designs it").

Issue #19 replaces the two placeholder PNGs Issue #14 shipped
(`docs/architecture/revit-add-in-host-scaffold.md`, "Placeholder icons") with a designed terrain-and-parcel
icon pair, at the exact 16×16/32×32 sizes `PushButtonData.Image`/`.LargeImage` expect. It changes no code
path: the embedded-resource mechanism, the fixed logical names, and `SolidGroundApplication`'s loading code
all stay exactly as Issue #14 built them (see "Resource convention" below). This note synthesizes the
Issue #19 design record — four parallel concept designers, two rounds of refinement and adversarial
critique, two post-polish revisions, a blind recognition test, a position-counterbalanced forced-choice
test, two further purpose-drawn 16×16 companions, and a third, 16px-only forced-choice round, all produced
by Claude Sonnet subagents orchestrated by Claude Fable across a temporary workflow session and not
committed to this repository, the same relationship
`docs/architecture/revit-extensible-storage-provenance.md` has to Issue #16's own out-of-repo design record
— plus a Revit-free `MetadataLoadContext` reflection pass over the installed Revit 2027 (`27.0.10.13`)
`RevitAPIUI.dll`/`AdWindows.dll` and Autodesk's current Ribbon Guidelines and Icon Design Guidelines
material.

## Purpose and boundary

Issue #19's six acceptance criteria (GitHub Issue #19, "Acceptance criteria"): exact 16×16/32×32 files with
correct alpha transparency; the same recognizable terrain/parcel concept at both sizes, each independently
legible at 100%; clarity in Revit 2027's light and dark ribbon contexts; loading through the owner-approved
resource convention with no absolute paths; generation notes naming the tool, prompt intent, seed where
applicable, and human cleanup, without credentials; and only final reviewed assets plus useful source/provenance
material entering the repository. In scope here: confirming the resource convention needs no change, the
theme-robustness decision and its API evidence, the PNG-format contract, the ribbon background colours and
the contrast ceiling they impose, the full design lineage from four concepts to the chosen 32×32 and 16×16
candidates, the generation-notes record, and a manual evidence plan for the one acceptance criterion no
offline check can settle. Out of scope: any change to `SolidGroundApplication.cs`,
`SolidGround.Revit.csproj`, or `scripts/Deploy-RevitAddIn.ps1` — Issue #14 already documented all three as
needing zero change for a bytes-only icon replacement, and nothing in this design changes that — and
running the manual evidence plan below. (The offline test suite did need a change; unlike those three
files, that change is in scope and lands alongside this note — see "PNG format" below.)

## Resource convention (unchanged)

Issue #19 replaces only the bytes of two files already in place. Confirmed directly against the current
source:

- `src/SolidGround.Revit/SolidGround.Revit.csproj:133-134`: two fixed `EmbeddedResource` items —
  `LogicalName="SolidGround.Revit.Resources.SolidGround.16.png"` and `...32.png"` — deliberately not a
  `%(Filename)` glob, so `SolidGroundApplication.cs` can name them as literal string constants (the csproj's
  own doc comment, ~line 128). `UseWPF` is not set anywhere in this project; a narrower
  `<FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" />` (~line 120) supplies just enough WPF
  (`System.Windows.Media.Imaging`) for the icon loader, with `UseWPF` reserved for when an actual XAML
  surface exists.
- `src/SolidGround.Revit/SolidGroundApplication.cs:36-37`: `SmallIconResourceName`/`LargeIconResourceName`
  constants naming those same two logical names.
- `SolidGroundApplication.cs:119-120`: `PushButtonData.Image = LoadIcon(SmallIconResourceName)` and
  `.LargeImage = LoadIcon(LargeIconResourceName)`. `ToolTipImage` is never assigned anywhere in this project
  (see "Theme robustness" below for why it stays that way).
- `SolidGroundApplication.cs:134` (`LoadIcon`): `typeof(SolidGroundApplication).Assembly
  .GetManifestResourceStream(logicalResourceName)` → `BitmapFrame.Create(stream, BitmapCreateOptions.None,
  BitmapCacheOption.OnLoad)` → `.Freeze()` on success; any exception except `OutOfMemoryException`/
  `StackOverflowException` logs an `AddInLog.Warning` and returns `null`, which degrades the button to
  text-only rather than failing ribbon creation — matching the owner's other add-in's own non-fatal contract
  (`docs/architecture/revit-add-in-conventions.md`'s basis paragraph, citing the owner's other add-in's shipped
  (detail about the owner's other add-in withheld)
- `scripts/Deploy-RevitAddIn.ps1` has no icon-specific code path: because the two PNGs are embedded resources
  compiled into `SolidGround.Revit.dll`, replacing their bytes only changes that one assembly's own hash,
  which the deploy script's dependency-closure hashing already tracks generically.

A bytes-only icon replacement needs the final PNGs to land at exactly
`src/SolidGround.Revit/Resources/SolidGround.16.png` and `SolidGround.32.png`, at exactly 16×16 and 32×32
pixels, and nothing else in the add-in.

## Theme robustness: one icon pair, no runtime swap

Revit 2027's ribbon is WPF-based, built on the shared Autodesk `AdWindows.dll` component (a direct dependency
of the installed `RevitAPIUI.dll`, confirmed by `Assembly.GetReferencedAssemblies()`), and does expose a real
theme-detection surface an add-in could use to swap images at runtime:

| Revit API member | Verified by | Note |
| --- | --- | --- |
| `Autodesk.Revit.UI.ButtonData.Image` / `.LargeImage` : `System.Windows.Media.ImageSource` | Installed `RevitAPIUI.xml` (`P:...ButtonData.Image`/`.LargeImage`) | "Best size 16×16... will NOT be adjusted to fit" / "best size 32×32... will be adjusted to fit" — the one documented asymmetry between the two slots. |
| `Autodesk.Revit.UI.RibbonButton.Image` / `.LargeImage` (live control mirror) | Installed `RevitAPIUI.xml` | Near-identical text, same single-`ImageSource`-per-role shape. |
| `Autodesk.Revit.UI.RibbonItemData.ToolTipImage` : `ImageSource` | Installed `RevitAPIUI.xml` | Not used by this milestone (max 355px, unrelated to the 16/32 ribbon assets). |
| `Autodesk.Revit.UI.UIThemeManager.CurrentTheme` / `.CurrentCanvasTheme` : `UITheme` (public static, get/set) | Installed `RevitAPIUI.dll` reflection (`MetadataLoadContext`) + `RevitAPIUI.xml` (`<since>2014</since>` / `<since>2024</since>`) | Real, callable theme state. |
| `UIThemeManager.FollowSystemColorTheme` : `bool` (public static, get/set) | Same | `<since>2024</since>`. |
| `UIThemeManager.GetCurrentFrameBackgroundColor()` | Same | Documented in `RevitAPIUI.xml` beside the four public members, but reflection shows it `IsAssembly=true, IsPublic=false` — **internal; an add-in cannot call it.** |
| `Autodesk.Revit.UI.UITheme { Dark = 0, Light = 1 }` | Installed `RevitAPIUI.dll` reflection | — |
| `Autodesk.Revit.UI.Events.ThemeChangedEventArgs.ThemeChangedType` : `ThemeType` | Same | `<since>2024</since>`. |
| `UIApplication.ThemeChanged` : `EventHandler<ThemeChangedEventArgs>` | Same | `<since>2024</since>` — available at command time. |
| `UIControlledApplication.ThemeChanged` (same signature) | Same | `<since>2022</since>` — available two years earlier, at `OnStartup` time. |

No `DarkImage`, `LightImage`, theme-indexed overload, or "supply two images" constructor exists anywhere on
`ButtonData`, `PushButtonData`, `RibbonButton`, `RibbonItem`, or `RibbonItemData` (reflection-confirmed
absence). A working per-theme icon is therefore only reachable by an add-in reassigning the same
`Image`/`LargeImage` slot itself at runtime — a real, Autodesk-corroborated pattern (Jeremy Tammik,
`blog.autodesk.io`, "Dark Theme Possibility Looming," Jan 2023: subscribe to `ThemeChanged`, read
`UIThemeManager.CurrentTheme`, reassign `Image`/`LargeImage`; community add-ins such as `ricaun.Revit.UI`
implement the same pattern) — but it is new code with no precedent anywhere in `SolidGround.Revit` today
(`grep` for `UITheme`/`ThemeChanged`/`CurrentTheme` under `src/` returns nothing) and none in the owner's other add-in's own
(detail about the owner's other add-in withheld)
(detail about the owner's other add-in withheld)
black/white; dark variants only if the dark-ribbon mock fails)."**

Issue #19 adopts the same rule: one PNG per size, no `ThemeChanged` subscription, no runtime image swap
anywhere in `SolidGround.Revit`, and `ToolTipImage` stays unset. Theme robustness for this milestone is a
**design-time** discipline — one bitmap tuned to read on both ribbon skins, verified by mocking it against
both before it ships — not a runtime code path. Autodesk's own Icon Design Guidelines describe a genuine
multi-DPI mechanism (a 5-frame TIFF carrying 100%/150%/200%/300%/400% variants, decoded to one `ImageSource`
by the add-in itself, since `Image`/`LargeImage` accept only one `ImageSource` each and Revit does not choose
among several registered files); that mechanism is out of scope for this milestone, and only 100% Windows
display scaling (`AppliedDPI=96`) has ever been observed for any SolidGround ribbon icon.

## PNG format

Every candidate and the shipped pair must be 8-bit-per-channel RGBA (PNG colour type 6), non-interlaced,
filter type 0 on every scanline, with exactly three chunks in file order — `IHDR`, `IDAT`, `IEND` — and no
`pHYs`, `gAMA`, `sRGB`, `cHRM`, or `iCCP` chunk. This is a deliberate departure from the placeholder pair
Issue #14 shipped and this issue's own final bytes now replace: re-parsed byte-for-byte from git history
before that replacement, the placeholders were both 8-bit, colour type 6, chunk order
`IHDR, sRGB, gAMA, pHYs, IDAT, IEND`, SHA-256 `c95e014da5956ab4fcc22b1800dbe573609cd0ea5137c51705a3c9b243aefc4a`
(16px) and `f331628e1bfcd12e83ad8ce5d959ba37c40ffccb34d1793d4ec4ec18d765ed44` (32px). Both carried a `pHYs`
chunk of `ppux=ppuy=3779`, unit 1 (metre) — `3779 ÷ 39.3700787401575 in/m ≈ 95.9866 DPI`, not exactly 96.

The `pHYs` chunk matters because WPF's `BitmapFrame` reads it as the image's authored DPI and derives
`Width`/`Height` (device-independent units) as `PixelWidth × 96 / DpiX`. The throwaway `wpfcheck` tool (a
`net10.0-windows` project kept outside this repository, not committed) loads a PNG exactly the way
`SolidGroundApplication.LoadIcon` does (`BitmapFrame.Create(stream, BitmapCreateOptions.None,
BitmapCacheOption.OnLoad)`, then `.Freeze()`) — confirms the practical effect: a PNG rendered with no `pHYs`
chunk loads at `DpiX = 96` and `Width` exactly equal to its pixel count (16 or 32); the placeholders, with
their inexact `pHYs` chunk, loaded at `DpiX ≈ 95.9866` and `Width ≈ 16.0022` / `32.0045` device-independent
units instead — a sub-half-percent size drift that Revit does not warn about but that is exactly the hazard
(detail about the owner's other add-in withheld)
a code comment: "the 32x32 files deliberately carry NO pHYs chunk so WPF decodes them at exactly 96 DPI, and
any re-encode silently reintroduces one and breaks icon sizing"). Issue #19 follows the owner's other add-in's rule rather than
inheriting the placeholders' inexact chunk: the throwaway `icontool` renderer's `render` command (not
committed) — the only tool that ever wrote a deliverable PNG's bytes anywhere in the design record — emits
only `IHDR`/`IDAT`/`IEND` by construction, and this note independently re-parsed the current V1/V2/V3 candidate files (see "Design
process" below) to confirm all six are 8-bit, colour type 6, exactly 16×16/32×32, chunks exactly
`IHDR,IDAT,IEND`, no `pHYs`.

`tests/SolidGround.Tests/RevitHostFilesTests.cs` originally checked only width and height (the old
`RibbonIconHasTheExpectedPixelDimensions` theory), which would still have passed a 16×16/32×32 file that
regressed to palette-indexed colour or picked up a `pHYs` chunk. The same commit that lands this note's
final PNG bytes also lands a companion test-file change strengthening that check:
`RibbonIconIhdrDeclaresEightBitNonInterlacedRgbaAtTheExpectedPixelDimensions` (line 328),
`RibbonIconContainsOnlyCriticalPngChunks` (line 346), and
`RibbonIconHasATransparentBackgroundBehindAnOpaqueGlyph` (line 377) assert colour type 6, bit depth 8, the
exact three-chunk list, and real corner transparency, using the same dependency-free raw-byte parsing style
the old test already used (now `ReadPngIhdr`, line 777), so `SolidGround.Tests` keeps running on the
Revit-free `self-hosted` CI runner with no new package dependency. `dotnet test` against the final
bytes this note documents: **950 tests, 949 passing, 1 skipped** (the online-only OpenTopography test,
unrelated to this issue), **0 failed**.

## Background colours and the contrast ceiling

| Ribbon surface | Hex | Status | Source |
| --- | --- | --- | --- |
| Dark panel (selected tab / the surface immediately behind a button) | `#3B4453` | **MEASURED** | Three independent Revit 2027 sessions (Issues #14 and #15), two capture methods (`PrintWindow`, full-desktop), two screen configurations, cross-checked against installed `AdWindows.dll`'s own `TabTheme_1` dark-dictionary string (`#3b4453`). |
| Dark chrome (tab strip / unselected tab) | `#222933` | **MEASURED** | Same three sessions; matches `AdWindows.dll`'s `RibbonTheme_1` dark-dictionary string. |
| Light panel | `#F5F5F5` | **DOCUMENTED, not yet measured live** | `AdWindows.dll`'s `TabTheme_1` light-dictionary string; no live Revit 2027 light-theme capture exists anywhere in this repository's own evidence history (Issues #14-#16 all show Dark). |
| Light chrome | `#D9D9D9` | **DOCUMENTED, not yet measured live** | `AdWindows.dll`'s `RibbonTheme_1` light-dictionary string. |

All four values come directly from `C:\Program Files\Autodesk\Revit 2027\AdWindows.dll`'s own printable-ASCII
resource strings (the shared Autodesk ribbon-control assembly Revit's WPF ribbon is built on), not from
Autodesk's own published help screenshots: those (2022-2023-era) show a half-dark theme with a still-light
panel, categorically different from Revit 2027's fully dark, navy-tinted rendering measured above, so they are
documented as stale rather than used as a colour source. The manual evidence plan below exists specifically
to promote the light-theme row from DOCUMENTED to MEASURED.

**Why one colour cannot pass both panels comfortably, and cannot pass a panel and its own chrome at all.**
WCAG's contrast ratio is `(L1 + 0.05) / (L2 + 0.05)`, using relative luminance `L` from linearized sRGB (the
design record's own reference implementation, the throwaway `icontool` tool's colour-math module (not
committed), uses the WCAG Success
Criterion 1.4.3 sRGB breakpoint 0.03928, not the metrologically exact 0.04045, matching virtually every
contrast-checker tool). The four surfaces' own relative luminances: light-panel `#F5F5F5` ≈ 0.9131,
light-chrome `#D9D9D9` ≈ 0.6939, dark-panel `#3B4453` ≈ 0.0569, dark-chrome `#222933` ≈ 0.0216.

Solving for the luminance `L` a flat foreground colour needs to clear 3:1 against each surface (an icon reads
as the darker figure against the two light surfaces and the lighter figure against the two dark surfaces):

- Against light-panel: `L ≤ (0.9131 + 0.05) / 3 − 0.05 ≈ 0.27103`.
- Against dark-panel: `L ≥ 3 × (0.0569 + 0.05) − 0.05 ≈ 0.27070`.

These two requirements overlap in a band only about `0.0003` wide (`0.27070` to `0.27103`) — a real but
razor-thin window where a flat colour's luminance, regardless of hue, clears 3:1 against both panels at once.
Every "rim" (true silhouette-edge) colour across every candidate in the design record was independently tuned
into this same band and measures almost exactly 3.00:1 against both backgrounds — confirmed by the throwaway
`icontool metrics` command (not committed) on the rendered PNGs, not merely calculated.

- Against light-chrome: `L ≤ (0.6939 + 0.05) / 3 − 0.05 ≈ 0.19797`.
- Against dark-chrome: `L ≥ 3 × (0.0216 + 0.05) − 0.05 ≈ 0.1648`.

Light-chrome's own ceiling (`≈0.198`) sits below the panel band's floor (`≈0.2707`) — the two requirements are
disjoint for every hue, so no flat colour that passes both panels can ever also pass light-chrome at 3:1;
dark-chrome's own floor (`≈0.1648`) sits below the panel band, so the same knife-edge colour clears dark-chrome
"for free" whenever it clears dark-panel. This is a structural property of these four colours, not a defect in
any one candidate's palette — every designer in the record who needed a rim colour rediscovered it
independently, with the same ~0.0003-0.0004-wide band to within rounding. Issue #19's own acceptance criterion
only requires clarity against the two **panel** tones numerically; chrome legibility is satisfied instead by a
deliberate technique — an interior dark contour and/or an interior light bevel one layer inside the knife-edge
rim, which never touches transparency and so is exempt from the background-contrast metric entirely, while
still reading as an edge cue by local (simultaneous) contrast — confirmed by eye against the mock sheets in
every candidate that used it.

## Design process

Every candidate's "silhouette score" below — the fraction of edge pixels (opaque, 4-adjacent to a transparent
pixel or the image border) whose colour clears 3:1 against a given background — is computed by the throwaway
`icontool metrics` command (not committed), part of a bespoke, offline, standard-library-only .NET 10 tool
built for this issue and kept outside the repository (also `inspect`/`lint`/`preview`/`mock`/`grid`/`render`);
no NuGet imaging package and no AI tool ever touches a deliverable pixel.

**Tooling note.** The throwaway `icontool` and `wpfcheck` tools above were not the only tooling the out-of-repo
design record used. That record's own throwaway tooling also included small Python helper scripts written by
the design subagents — grid-geometry generators, colour/contrast searches, and critique/verification checks,
including the script that laid out the winning V3 candidate's final 32px and 16px grid text. The final PNGs
were rendered from those grid files by the throwaway `icontool` renderer, and every measurement, hash, and
pixel value this note reports was independently re-derived and re-verified directly from the shipped PNG
bytes and this repository's own committed `.NET` tooling and tests, not by re-running any Python script.
Nothing in this repository uses or depends on Python. AGENTS.md's "Architecture" section states plainly: "The
agent must not use Python, GDAL, native geospatial binaries, or the Forma Connected Client." That sentence
governs SolidGround's own implementation and toolchain; the orchestrator reads throwaway scratch scripts that
ran outside the repository and were never shipped or committed as outside that rule, and records their use
here so the owner can overrule that reading (see "Known limitations").

### Round 1 — four concepts, two judge lenses

Four designers worked in parallel from the same brief (a terrain-and-parcel metaphor, both sizes
purpose-drawn rather than a mechanical downscale, a silhouette-contrast target against both `#F5F5F5` and
`#3B4453`, a modest ~8-10 colour budget, no pure black/white, deuteranopia/greyscale checked, PixelLab
optional and capped):

- **A-toposolid-block**: a squat, three-face isometric block — a green terrain top over a two-face earth
  cross-section — with the parcel boundary as four gold corner pins on the top face.
- **B-contour-plan**: a top-down plan view — nested, hand-wobbled organic contour rings for terrain, a
  dead-straight polygonal parcel boundary with corner pins as the accent.
- **C-section-profile**: a side-elevation cut — a rolling-hill silhouette over an earth-fill mass on a ground
  datum line, the parcel marked by survey stakes or a CAD-style dimension bracket.
- **D-autodesk-composition**: a literal reading of Autodesk's own "main body + accent [+ badge]"
  icon-composition pattern — a single irregular polygon that is simultaneously the terrain's low-poly TIN
  silhouette and the parcel line itself.

A round-1 judge pass (craft and legibility lenses only; a `theme-contrast` lens folder exists in the record
but was never populated this round) picked **A-toposolid-block** as the strongest overall candidate, with
three named weaknesses carried into round 2: the parcel accent (1-2px corner pins) read weakly, especially at
16px; the flat two-tone top face risked reading as a generic "grass block"; and the gold accent hue
(`#CE7A12`, chosen for the same knife-edge contrast band above) could not be shown to separate from the
green/brown terrain hues in light-chrome, greyscale, or deuteranopia terms — its own hue sits only ~1° from
the earth tone's hue, distinguished mainly by saturation and lightness, a weak deuteranopia cue.

### Round 2 — three refinements, three judge lenses

Three treatments refined A's silhouette in parallel:

- **R1-parcel-outline**: an inset traced/dashed boundary plus a re-tuned coral accent (`#E46858`, ~27° of hue
  from the earth tone, versus gold's ~1°).
- **R2-profile-block**: a shallower block, four bold buffered "survey stake" corners in a
  deuteranopia-optimised safety-orange (`#FD5307`, chosen by maximising simulated-colour distance from both
  terrain hues inside the same knife-edge band), an undulating fold line, and — because light-chrome is
  structurally unreachable at 3:1 — an interior dark contour plus an interior light bevel as the "must still
  read" technique.
- **R3-parcel-shaped-solid**: "the solid IS the parcel" — an irregular, non-rectilinear quadrilateral
  (verified convex and genuinely irregular by its own edge cross-products, not a disguised parallelogram)
  extruded straight down, whose entire top-face perimeter — not just its corners — is the traced property
  line.

A three-lens judges2 panel scored A (baseline) and all three treatments independently:

| Lens | A | R1 | R2 | R3 | Winner |
| --- | --- | --- | --- | --- | --- |
| Legibility | 3 | 5.5 | **8** | 7.5 | R2 |
| Craft | — | — | — | cleanest outline logic; a "distortion ratio" of 1.04× between its own 16px/32px shapes, against 1.37×-1.58× for the others | **R3** |
| Theme-contrast | — | — | best-or-tied-best on every sub-check | "a close, legitimate second choice... a reasonable alternative winner," losing mainly on accent-area discipline | **R2** |

Reported plainly, as the design record's own `PROVENANCE.md` reports it: the three lenses split 2-to-1 toward
**R2-profile-block** (legibility and theme-contrast) against 1 toward **R3-parcel-shaped-solid** (craft), with
R3 named an explicit, reasonable near-winner by the other two lenses. The orchestrator selected **R3** to
carry into polish; no memo reconciling that 2-1 split exists in the record, and this note repeats that fact
rather than inventing a reconciling rationale — the only documented grounds available are craft's own stated
basis (cleanest outline logic, by far the best 16px/32px shape consistency) and the other two lenses' own
"close/legitimate alternative" framing of R3.

### Polish — three critique rounds produce "V1"

Starting from R3's own shipped grid files, an initial four-iteration baseline pass fixed seam connectivity (a
naive one-column thinning broke 8-adjacency; rebuilt as a Bresenham-style line), removed a highlight patch
that contradicted the icon's own upper-left light source, added a front-left bevel and a small dark grounding
anchor, and split the 32px property line into a lit/far two-tone pair. Three further rounds, each against an
independent three-lens critique report (native-read, pixel-craft, theme-contrast), fixed: a solid-orange top
wedge before any green appeared, at both sizes; a missing separating edge between the property line and the
terrain fill at 32px (rejected at 16px, with a documented reason — the only safe pixels there form a
disconnected 2px fleck, not a hairline); a disconnected grounding anchor; a handful of shadow-side highlight
pixels; and a colour collapse between the terrain "hollow" tone and the property line's new shadow tone under
simulation (round 1); a 16px lower body that read as a "tree trunk/lollipop stick" rather than a tapering
earth wedge, and a fold line present on the top face's lit corner but missing on its shadow corner (round 2);
and a 16px taper-slope/aspect-ratio mismatch against 32px (a 31.4% slope gap closed to 1.9%) plus a
body-vs-terrain area imbalance, at both sizes (round 3). Every fix was verified against the same hard floor —
silhouette ≥90% on both `#F5F5F5` and `#3B4453`, zero orphan pixels, zero partial-alpha pixels, ≤10 colours,
no pure black/white, a ≥1px transparent margin — usually by a direct edge-pixel-set comparison, not just the
aggregate percentage, and by opening the rendered preview/mock/simulation sheets, never accepted from numbers
alone.

The result — `SolidGround.32.png` (SHA-256 `798c451443ce5797e2382b50887b5edc3c9e38c28082c7045e33e1bece355d63`,
independently re-hashed for this note) and `SolidGround.16.png` (SHA-256
`a0a749b6946998c326bcafe04488e019ad0d95a83da69515e6fd061ef45c053e`, likewise re-hashed) — is referred to below
as **"V1."** Both files are 8-bit, colour type 6, exactly 32×32/16×16, chunks exactly `IHDR,IDAT,IEND` (also
independently re-verified for this note). Its full lineage, every intermediate hash, and its final
10-colour (32px) / 7-colour (16px) palette are recorded in the design record's own `PROVENANCE.md` and
`POLISH-LOG.md`.

### V1's rejection, and two independent revisions

The orchestrator — not yet the owner, who has not reviewed any candidate — judged V1, at native size, as
reading like "a cardboard box/planter with a green lid," for four reasons: the untextured tan earth faces
read as cardboard or wood; the dark seam lines around the top's front edges, plus the vertical front crease,
read as box edges rather than terrain; the top reads as a flat lid, not landform; and orange appearing only
on the back edges reads as a rim or flame rather than a boundary line. Two independent revisions followed,
each starting from V1's own grid files rather than a fresh redesign:

- **V2 ("soil-hill-loop," the "bold" variant)**: darker, richer soil browns with one visible stratum band
  across both earth faces; a hill crest that breaks the back silhouette with a gentle convex bulge; a
  **closed** orange loop around the entire top perimeter (not just the back edges); the dark seam and bottom
  anchor dropped in favour of a plain lit/shadow tone change; and a shrunk earth body so terrain and boundary
  dominate the glyph (earth-body share of opaque pixels fell from V1's ~52% to 22.3%). SHA-256 (independently
  re-hashed for this note): 32px `05ee92515f31a4e5744ce4e0532ea13b20c3799642825127a255ba79d8be18bc`, 16px
  `ac43c6f3e035538f56c4e0a3f5b676335469d75ccadb239e644c09e9b826385c`.
- **V3 ("soil-loop-minimal," the "minimal" variant)**: the smallest edit that answers the same four
  complaints without rebuilding geometry — the same darker/richer earth recolouring and one stratum line; the
  property line redrawn as a single **closed 1px loop** around all four top edges, as a Bresenham polyline
  through V1's own real corner/fold coordinates (a naive "thin to the outermost pixel per row" attempt
  produced 12/5 orphan pixels at 32px/16px; naively bridging those gaps reintroduced the original thick-wedge
  defect; the Bresenham redraw is connected and thin by construction); the vertical crease removed for a
  direct lit/shadow tone change; and the bottom anchor removed. Its silhouette score against the two hard
  target backgrounds is stronger than V1's own at both sizes (100%/100% vs. V1's 97.4%/93.9%). SHA-256
  (independently re-hashed for this note): 32px
  `1be0c1d3a230fa931385609eeb072bc3ec69705d302a6c1edc753916cef073b5`, 16px
  `31d64b88be1d286898a11c4b90d21389187aa9ab142683432af1de9d588f3e44`.

All six of V1/V2/V3's PNGs were independently re-verified for this note: 8-bit, colour type 6, exactly
16×16/32×32, chunks exactly `IHDR,IDAT,IEND`, no `pHYs`.

### Recognition testing

Three reader-test workflow runs back this section (the design record's own `reader-tests` folder, not
committed to this repository). Their "readers" are Claude Explore-agent personas given only the images and, in the
forced-choice rounds, the product description — not human usability-test participants — a distinction this
note keeps explicit throughout. Every figure below was independently re-derived for this note directly from
each file's own per-reading records (not merely read off its precomputed tally), matching the same
independent-re-derivation discipline this note already applies to hashes and pixel censuses above; the
underlying tallies are now stored outside this repository rather than unrecorded, and this note says so
explicitly rather than repeating an earlier draft's hedge that no such record existed. The raw per-reading
transcripts (each reader's full quoted description) live only in those same JSON files and stay out of the
repository; only the aggregate figures below are recorded here.

A **context-free blind recognition test** relabelled three candidates — V1 (the polish-round result), V2
("soil-hill-loop"), and V3 ("soil-loop-minimal") — as anonymous letters, with no project or product framing
given to the reader: 3 personas × 3 candidates × 2 sizes = 18 readings. Every one of the 18 readings
identified terrain first (each candidate/size cell is 3-of-3 on both the `top1Terrain` and `anyTerrain`
counts), confirming the base metaphor reads independently of which revision is shown, before any preference
judgment. Mean self-rated clarity (1-10): V1 7.0/5.7, V2 6.3/5.3, V3 6.3/6.0 (32px/16px).

A follow-up **position-counterbalanced forced-choice test** supplied the actual product description and
asked for a preference score (1-10) at each size: 3 rotations × 2 sizes × 2 personas = 12 readings. At 32px,
V3 was the top choice in 5 of 6 comparisons (mean 7.83, against V1's 6.33 and V2's 4.67; V2 was the bottom
choice in all 6). At 16px, V1 was the top choice in 4 of 6 comparisons (mean 6.58, against V2's 5.92 and
V3's own then-current 16px at 5.17); V3's 16px specifically lost readings that described it as "jagged" or
"torn."

A third, later **16px-only forced-choice round** (see "Final 16px asset" below) compared four 16px
candidates — V1, V3's own original 16px ("V3old"), and two new purpose-built replacements, V3a and V3b — 4
rotations × 2 personas = 8 readings, plus 8 size-consistency readings pairing each candidate's mock with
V3's 32px. V3b scored highest (mean 7.13; best in 3 of 8, worst in 0) — the only one of the four never rated
worst — ahead of V3old (6.5), V3a (5.75), and V1 (4.88; worst in 5 of 8); size-consistency with the 32px
asset was rated 8.5 of 10 for every candidate alike.

V1's 16px result is reported rather than smoothed over: it was the outright winner of the second test above
and the outright loser of this third one. That reversal is evidence these are small-n (3-8 readings per
round), self-rated, agent-simulated reader panels, not a statistically powered study — used here as one
input alongside the orchestrator's own direct review of every candidate's rendered, mocked, and simulated
output (see "Generation notes" above and the iteration logs cited under "Final 16px asset" below), not as a
number strong enough to stand alone.

### Decision

V3's 32×32 asset ships as the large ribbon icon. Its own 16×16 companion does not ship alongside it: the
forced-choice test's "jagged/torn" reading is a 16px-specific craft defect in that particular redraw, not a
rejection of V3's underlying concept (which the same test preferred decisively at 32px, and which every
reading in the context-free test still recognized as terrain), and reusing V1's own 16px would break the
"same recognizable concept at both sizes" acceptance criterion against a 32px asset drawn from a different
revision's geometry. A clean 16×16 companion, purpose-drawn from V3's own 32px concept rather than a
mechanical downscale of either V3's rejected 16px or V1's differently-shaped one, was the one asset this
design still needed; see "Final 16px asset" below for the two purpose-drawn approaches that were tried and
the reader-tested result.

## Generation notes

Nine `create_image_pixen` calls were made across the design record, all with `no_background=true`, by five of
the seven round-1/round-2 designers (A-toposolid-block and D-autodesk-composition made none, judging the tool
unsuited to exact-geometry/exact-contrast work at this pixel budget before calling it):

| # | Designer | Size | Seed | Prompt intent (trimmed) |
| --- | --- | --- | --- | --- |
| 1 | B-contour-plan | 32×32 | 1901 | Three concentric organic contour rings forming a hill, a crisp quadrilateral property boundary with square corner markers, muted stone/slate. |
| 2 | B-contour-plan | 32×32 | 2702 | Two concentric contour rings plus a plus-shaped survey marker, a crisp five-sided property boundary, muted terracotta/slate. |
| 3 | B-contour-plan | 16×16 | 1901 | Same intent as #1, at 16px. |
| 4 | B-contour-plan | 16×16 | 2702 | Same intent as #2, at 16px. |
| 5 | C-section-profile | 32×32 | 19042 | Flat side cross-section of a grassy hill over brown earth, two survey stakes at the base. |
| 6 | C-section-profile | 16×16 | 19043 | Flat side-elevation silhouette of a small hill, one survey stake, bold simple shapes. |
| 7 | R1-parcel-outline | 32×32 | 19190001 | Isometric block of terrain over an earth cross-section, a thin orange-red property line inset on the top surface. |
| 8 | R2-profile-block | 128×128 (reference size, not a deliverable size) | 190219 | Isometric earth chunk, a gently rolling wavy grassy top, an uneven undulating earth cut line, four bright-orange survey stakes at the corners. |
| 9 | R3-parcel-shaped-solid | 32×32 | 19419 | Isometric block of rolling terrain on an earth cross-section, a bright orange surveyor's property line traced around the top edge. |

Every one of the nine was used for concept reference or a sanity-check gut-check only, viewed inline and
never downloaded, traced, quantized, or otherwise incorporated into any deliverable grid or PNG — a judgment
call each designer reached independently, and one the design record's own tooling research had already
anticipated: AI output at 16-32px needs heavy manual cleanup for exact-geometry, exact-contrast work, and
several of the nine calls concretely confirmed that expectation (round badge/medallion compositions instead
of a plan-view boundary; a generic "grass block" despite an explicit "not flat, undulating" prompt; isometric
mounds instead of a flat side elevation). No PixelLab URL, job id, asset id, or account identifier is recorded
in this repository. The later 16px-companion work (V3a, V3b — see "Final 16px asset" below) made zero further
PixelLab calls of any kind; both were built entirely from V1's and V3's own existing grid geometry.

**Every final pixel in every shipped or near-shipped candidate — A/B/C/D's own finalists, R1/R2/R3, V1, V2,
V3, and the two 16px companions built against V3 (V3a, not shipped; V3b, shipped as `SolidGround.16.png`) —
was hand-authored in text pixel grids by Claude Sonnet subagents orchestrated by Claude Fable, either directly
or through small deterministic geometry generators the same designers wrote and controlled (see "Design
process" above), then rendered exclusively by the throwaway `icontool` renderer's `render` command (not
committed), a bespoke standard-library-only tool built for this issue.** No generated pixels needed cleanup:
no PixelLab output was ever incorporated into a deliverable pixel, so there was no AI generation to clean up.
The owner's own review, expected next, is the human step this design still has ahead of it.

## Palette

| Key | Hex (RGBA) | Role | Sizes | vs `#F5F5F5` | vs `#D9D9D9` | vs `#3B4453` | vs `#222933` |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `G` | `#1CA438FF` | Terrain top fill (base); a true silhouette-rim colour | 32px + 16px | 3.00:1 PASS | 2.32:1 FAIL | 3.00:1 PASS | 4.48:1 PASS |
| `L` | `#57CE68FF` | Terrain top sunlit highlight (interior) | 32px + 16px | 1.85:1 FAIL | 1.43:1 FAIL | 4.88:1 PASS | 7.28:1 PASS |
| `D` | `#158C30FF` | Terrain top shaded hollow (interior) | 32px only | 3.99:1 PASS | 3.08:1 PASS | 2.26:1 FAIL | 3.37:1 PASS |
| `P` | `#FC5414FF` | Property loop, lit side; a true silhouette-rim colour | 32px + 16px | 3.00:1 PASS | 2.32:1 FAIL | 3.00:1 PASS | 4.48:1 PASS |
| `S` | `#CA4310FF` | Property loop, shadow side (interior at this width) | 32px only | 4.46:1 PASS | 3.44:1 PASS | 2.02:1 FAIL | 3.02:1 PASS |
| `E` | `#C28029FF` | Earth rim; a true silhouette-rim colour | 32px + 16px | 3.00:1 PASS | 2.32:1 FAIL | 3.00:1 PASS | 4.48:1 PASS |
| `F` | `#8A5A30FF` | Earth face, lit (interior) | 32px + 16px | 5.37:1 PASS | 4.15:1 PASS | 1.68:1 FAIL | 2.50:1 FAIL |
| `R` | `#4A2E18FF` | Earth face, shadow (interior) | 32px + 16px | 11.36:1 PASS | 8.78:1 PASS | 1.26:1 FAIL | 1.18:1 FAIL |
| `H` | `#A8703CFF` | Earth bevel highlight (interior) | 32px only | 3.82:1 PASS | 2.95:1 FAIL | 2.36:1 FAIL | 3.52:1 PASS |
| `T` | `#6E5C46FF` | Soil stratum line (interior) | 32px only | 5.87:1 PASS | 4.53:1 PASS | 1.54:1 FAIL | 2.29:1 FAIL |

`SolidGround.32.png` uses all 10 colours; `SolidGround.16.png` uses the 6 marked "32px + 16px" (`G`, `L`,
`P`, `E`, `F`, `R`) at the same hex values, byte-for-byte, matching every earlier 16px asset in this record's
own precedent of dropping the shading-only roles (`D`, `S`, `H`, `T`) rather than approximating them in fewer
pixels. Every value above is the throwaway `icontool metrics` command's own output (not committed) against the shipped
`src/SolidGround.Revit/Resources/SolidGround.{32,16}.png` bytes (WCAG contrast, sRGB threshold 0.03928, per
"Background colours and the contrast ceiling" above) — computed by that tool, not by hand. `G`, `P`, and `E`
are the three true silhouette-rim colours, each independently tuned into the same razor-thin luminance band
that clears 3:1 against both `#F5F5F5` and `#3B4453` at once (see above), which is also why each fails
`#D9D9D9` at exactly the same ratio (2.32:1) — the same colour, the same arithmetic, not three separate
near-misses. No pure black or white appears in either size.

## Final 16px asset

V3's own 16×16 render — the only 16px asset built against V3's geometry at the time of the "Decision" above
— lost the forced-choice test specifically on jaggedness ("jagged/torn," see "Recognition testing" above)
and does not ship. Two independent purpose-drawn replacements were then built and reader-tested against it
and against V1's own (differently-shaped) 16px:

- **V3a ("V3-16a-from-v1", Approach A — not shipped)**: started from V1's own 16px silhouette
  (its own 16px grid file, kept outside this repository), recoloured to V3's exact palette roles/hexes (`G`, `L`, `P`, `E`,
  `F`, `R` — the same 6 colours V3b uses below, no `K` seam), then repaired two measured defects in that
  silhouette rather than rebuilding it: the outer-silhouette "stepped notch" readers had disliked in V1 (its
  back-right edge jumps +3, +3, +1 columns/row instead of an even stair-step — measured directly from the
  grid text, row by row) was rebuilt as an even Bresenham-derived progression, and the property loop was
  closed around all four top edges using new Bresenham-derived runs through V1's own fold-seam anchor points
  in place of the dark seam and central vertical cleft.
- **V3b ("V3-16b-from-v3-32", Approach B — shipped)**: purpose-drawn from scratch against V3's own 32px
  proportions and palette rather than V1's silhouette — corners planned first, then two explicit Bresenham
  funnel paths (the rows where the loop closes into the front vertex) checked for 8-connectivity by hand
  before rendering, then fills. Diagnosing V3's own failed 16px found the defect was specifically the loop's
  path — the two edges converge on the front vertex at uneven rates (one abrupt 2-column jump instead of a
  smooth 1-column-per-row staircase), reading as a "notch" once the seam turned orange and became
  silhouette-adjacent — not the outer silhouette, which was already smooth. V3b therefore keeps V1/V3's
  already-good top vertex, back edges, and earth taper unchanged, and rebuilds only the front-left/front-right
  funnel.

The later 16px-only reader-test round (see "Recognition testing" above: V1, V3old, V3a, V3b, 8 forced-choice
readings) scored V3b highest — mean 7.13, best in 3 of 8, worst in 0, the only candidate of the four never
rated worst — ahead of V3old (6.5), V3a (5.75), and V1 (4.88, worst in 5 of 8). Independent verification
inside `V3-16b-from-v3-32`'s own record supports the same conclusion: a standalone 8-adjacency union-find
check found the property loop (23 pixels) and the earth rim (21 pixels) each form exactly one connected
component; an outer-silhouette smoothness check found the leftmost/rightmost opaque column per row moves in
one direction at a time with no reversal anywhere (no notch, no bite, by the objective form of that
requirement); and a render→grid→render round trip reproduced a byte-identical PNG, confirming no
hand-authoring/rendering drift. Combined with the orchestrator's own direct review of both candidates'
renders, mocks, and simulations at native size and at zoom, **V3b ships as the 16×16 ribbon icon.**

`SolidGround.16.png`'s final, shipped bytes — independently re-parsed for this note the same way every other
candidate in this record was — are 16×16, 8-bit, colour type 6 (RGBA), non-interlaced, 6 distinct opaque
colours, 111 opaque / 145 fully-transparent pixels (of 256), 0 partial-alpha pixels, 0 orphan pixels, 0
border-touching pixels. Chunks, in file order: `IHDR` (13 bytes) → `IDAT` (113 bytes) → `IEND` (0 bytes) —
exactly these three, no `pHYs` or any other ancillary chunk (170 bytes total). `OpaqueBoundingBox`
`x:1-13, y:2-13`; all four corner pixels alpha 0. Silhouette (edge-pixel WCAG ≥3:1 pass rate, via
the throwaway `icontool metrics` command, not committed): `#F5F5F5` **100.0%** (32/32), `#D9D9D9` 0.0% (0/32), `#3B4453` **100.0%** (32/32),
`#222933` 100.0% (32/32) — both required backgrounds clear the ≥90% floor with margin to spare, matching its
32px sibling; the 0% light-chrome score is the same structural ceiling described in "Background colours and
the contrast ceiling" above, not a defect (checked visually instead — see V3b's own design-record notes (not
committed to this repository), "Verification" section: the loop, top, and two-tone earth all stay clearly legible on light-chrome at zoom,
just below the formal ratio). Mean WCAG luminance 0.2140.

**SHA-256: `529a5b02b0161927cfc53bebe433977b9611f50e24995ea7a922e808e4cfe26d`** — independently computed for
this note by two separate methods (a direct file hash and the throwaway `icontool inspect` command's own
reported hash, not committed, which
agree), and confirmed identical to the file now shipped at
`src/SolidGround.Revit/Resources/SolidGround.16.png`. `SolidGround.32.png`'s own final bytes are V3's, per
"V1's rejection, and two independent revisions" above: SHA-256
`1be0c1d3a230fa931385609eeb072bc3ec69705d302a6c1edc753916cef073b5`, likewise independently confirmed against
`src/SolidGround.Revit/Resources/SolidGround.32.png` (32×32, 8-bit, colour type 6, 10 distinct opaque
colours, 577 opaque / 447 fully-transparent pixels of 1024, chunks `IHDR` (13 bytes) → `IDAT` (301 bytes) →
`IEND` (0 bytes), 358 bytes total, `OpaqueBoundingBox` `x:1-29, y:2-29`, silhouette `#F5F5F5`/`#3B4453`/`#222933`
all **100.0%** (76/76), `#D9D9D9` 0.0% (0/76), mean WCAG luminance 0.2128).

## Manual evidence plan (Revit 2027 session)

A prepared, not-yet-run runbook exists in the design record (not committed to this repository) to settle the one
acceptance criterion no offline check reaches: "The icons remain clear in the supported Revit 2027 light and
dark ribbon contexts." It follows this repository's own established shape
(`docs/architecture/revit-add-in-host-scaffold.md`'s Steps 1-4/6/10 and
`docs/architecture/revit-toposolid-creation.md`'s Steps 5, 7, and 8a-8b, continued by
`docs/architecture/revit-extensible-storage-provenance.md`'s own "Manual evidence plan (continues Steps
1-8b)") — numbered steps, a pass criterion, and a blank
"Evidence" paragraph per step, filled in only from a real session. It drives a dedicated throwaway diagnostic
add-in (kept entirely outside this repository, following Issues #15 and #16's own precedent for throwaway
probes) with two ribbon tabs: the real "SolidGround" tab carrying the shipped `Create Toposolid` button
(loaded through the exact mechanism this note documents, with the session's final icon bytes already
deployed), and an "SG Icon Probe" tab exposing one large button plus three stacked small buttons (Theme
Light / Theme Dark / Theme Restore / Theme Report) so both large- and small-button rendering, at two adjacent
visual densities, can be captured and measured in one session.

Step summary (full detail, script names, and every owner safety rule live in the design record's own runbook,
not reproduced here):

| Steps | Purpose |
| --- | --- |
| S0 | Preconditions: interference check; **the owner's explicit "go" before any Revit launch**; a safe launch window relative to any concurrent Revit 2026/pyRevit session; the launcher template's SHA-256 hashed *before* launch; the shipped add-in's deployed icon bytes verified against the approved final PNGs; an older, crash-prone probe manifest moved aside; `Revit.ini`/`UIState.dat` backed up. |
| S1 | Install the throwaway probe with the session's final 16×16/32×32 PNGs (the install script itself refuses a size mismatch). |
| S2 | Launch; answer the unsigned-add-in prompt ("Load Once," never "Always Load"); read a Theme Report **before** assuming the machine starts in Dark (every existing capture in this repository's own evidence history happens to be Dark, but nothing forces the next session to start there) and force it to Dark first if it does not. |
| S3 | Capture and measure the **dark** theme: the real SolidGround tab and the probe tab, sampling the panel background immediately around each button (expected near `#3B4453`) and the tab-strip chrome (expected near `#222933`) — both already MEASURED, so this step is mainly a fresh cross-check against the final (not placeholder) icon bytes. |
| S4 | Re-confirm still Dark, then switch to **Light**, and repeat the same captures and measurements — the step that promotes light-panel/light-chrome from DOCUMENTED to MEASURED. |
| S5 | Theme Restore; confirm via a second Theme Report that the machine's original theme came back exactly; capture once more as a visual cross-check against S3. |
| S6 | Close **without saving** (nothing in this session ever modifies a document); re-hash the launcher template and confirm it matches S0's pre-launch hash. |
| S7 | Uninstall the probe; restore the older probe manifest moved aside at S0, byte-identical; hash the template a final time; diff `Revit.ini`/`UIState.dat` against the S0 backup and restore verbatim if either changed. |
| S8 | Write up the session: a per-step table, measured colours, a PASS/FAIL/INCONCLUSIVE verdict on the acceptance criterion citing specific S3/S4 files, and an Anomalies section (populated explicitly, never left silently empty). |

## Evidence

*Pending. This section is filled in once a live Revit 2027 session runs the manual evidence plan above,
following this repository's own "Decisions recorded from evidence" convention — see
`docs/architecture/revit-extensible-storage-provenance.md` for the shape that section takes once real session
data exists.*

## Acceptance criteria

| # | Acceptance criterion | Status | Notes |
| --- | --- | --- | --- |
| AC1 | Final files exactly 16×16 and 32×32 with correct alpha transparency. | **Met** | Both files independently re-verified for this note (the throwaway `icontool inspect` command, not committed, and this repository's own offline test suite): 8-bit, colour type 6, exactly 16×16/32×32, chunks exactly `IHDR,IDAT,IEND`, all four corners alpha 0. Both now ship at `src/SolidGround.Revit/Resources/SolidGround.{16,32}.png`. |
| AC2 | Both sizes use the same recognizable terrain/parcel concept, each independently legible at 100%. | **Supported, with a small-n caveat** | Backed by the context-free blind test (terrain identified in all 18 readings), both forced-choice tests' own results, and the design record's own repeated native-size (100%, no zoom) review at every iteration (see "Design process" and "Final 16px asset"). The reader tests themselves are small-n, self-rated, agent-simulated panels, not a statistically powered study — see "Recognition testing" and "Known limitations" — so this is a supported design judgment, not a proven statistic. |
| AC3 | Clear in Revit 2027's light and dark ribbon contexts. | **Pending Revit 2027 evidence** | Dark: colours MEASURED live; both final assets' rim colours measure ~3.00:1 against `#3B4453`, giving a 100% silhouette pass rate at both sizes. Light: contrast math only, against DOCUMENTED (not yet live-measured) colours; the manual evidence plan above has not run. |
| AC4 | Loaded through the owner-approved resource convention, no absolute paths. | **Satisfied by design and by the current test suite** | The mechanism (`EmbeddedResource`, fixed `LogicalName`s, `LoadIcon`) needed no code change for this bytes-only replacement (see "Resource convention" above); `SolidGround.Tests`'s LogicalName-matching and allow-list tests, and the full offline suite (950 tests: 949 passing, 1 skipped, 0 failed), now pass against the final bytes. End-to-end confirmation in a running Revit 2027 process is folded into the manual evidence plan's S0.5/S1. |
| AC5 | Generation notes name the tool, prompt intent, seed where applicable, and human cleanup, without credentials. | **Met by this note and `Resources/README.md`** | See "Generation notes" above, which names every tool actually used, including the throwaway Python helper scripts disclosed under "Design process" above. No PixelLab output was incorporated into any deliverable pixel, so there was no AI generation to clean up; every pixel was hand-authored by Claude agents, and the owner's own review is the step still ahead of this design. |
| AC6 | Only final reviewed assets and useful source/provenance files enter the repository. | **Partially met, pending the owner's review** | The offline suite's allow-list test (`RevitResourcesDirectoryContainsOnlyTheApprovedRibbonIconFiles`) keeps `Resources/` to exactly the two PNGs and this repository's own `README.md`; no design-exploration file, PixelLab identifier, or `%TEMP%` path enters the repository (see "Generation notes"). The two PNGs are the orchestrator's selected final candidates, landed as uncommitted local changes; the owner's own review is pending. |

## Known limitations

- The out-of-repo design record's own throwaway tooling included small Python helper scripts (grid-geometry
  generators, colour/contrast searches, verification checks), including the script that laid out the final
  32px and 16px grid text — see "Design process" above. Nothing in this repository uses or depends on Python.
- Light-theme ribbon colours are DOCUMENTED (installed `AdWindows.dll` resource strings) but not yet MEASURED
  against a live Revit 2027 render; the manual evidence plan above exists to close exactly this gap.
- The 16×16 `Image` slot (Quick Access Toolbar rendering) has never been observed rendering in a real Revit
  2027 session for either `SolidGround.Revit` or the owner's other add-in's own shipped icons (the owner's other add-in deferred, and
  apparently never wired, its own 16×16 asset) — the manual evidence plan's probe tab is the first
  opportunity to observe it.
- Only 100% Windows display scaling has ever been observed for any SolidGround ribbon icon; Autodesk's own
  material states Revit does not itself rescale a single supplied `ImageSource` at higher display scaling,
  and a multi-frame-TIFF high-DPI approach is explicitly out of scope for this milestone (see "Theme
  robustness" above).
- The reader tests behind "Recognition testing" and "Final 16px asset" above are small-n (3-8 readings per
  round), self-rated, agent-simulated reader panels, not a statistically powered study. V1's own 16px result
  flipped from best (the 12-reading forced-choice round) to worst (the later 8-reading 16px-only round); this
  note reports that reversal as evidence of the tests' own noise rather than smoothing it into one confident
  number. They were used as one input alongside the orchestrator's own direct review of every candidate's
  rendered, mocked, and simulated output, not as a substitute for it.
- the owner has not reviewed either final asset described in this note; that review and the manual evidence
  plan above are the two items this note still marks as open (see "What this note does not do").

## Sources

- AGENTS.md, "Revit add-in conventions" and "Mission and current boundary."
- `docs/architecture/revit-add-in-conventions.md`, owner decision 5 and section 4 ("Adopted for
  SolidGround.Revit").
- `docs/architecture/revit-2027-verification-and-host-design.md`, item 7 and the "Thin-host design" ribbon
  section.
- `docs/architecture/revit-add-in-host-scaffold.md`, "Placeholder icons" and "Revit 2027 API members used."
- GitHub Issue #19, "[Phase 2] Design and validate the Revit ribbon icons" (Scope and Acceptance criteria).
- Installed `RevitAPIUI.dll`/`RevitAPIUI.xml`, version `27.0.10.13`,
  `C:\Program Files\Autodesk\Revit 2027\` — `ButtonData`/`RibbonButton`/`RibbonItemData` image members;
  `UIThemeManager`, `UITheme`, `ThemeType`, `ThemeChangedEventArgs`, `UIApplication.ThemeChanged`,
  `UIControlledApplication.ThemeChanged` — reflected via `System.Reflection.MetadataLoadContext`.
- Installed `AdWindows.dll`, version `5.4.2.2`, same path — `RibbonTheme_1`/`TabTheme_1` resource-dictionary
  colour strings for both the Dark and Light skins.
- Autodesk, Ribbon Guidelines (Revit 2027 API Developers Guide, "Icons" subsection) and the Autodesk-authored
  Icon Design Guidelines material (republished by Jeremy Tammik, `blog.autodesk.io`, 2023 — not part of the
  versioned 2027 reference doc set, cited as corroborating industrial-design guidance only).
- Jeremy Tammik / Autodesk Developer Network, `blog.autodesk.io`, "Dark Theme Possibility Looming" (Jan 2023)
  and "Back Again to Unit Test Icons, Viewports and More" (Jan 2025) — the manual per-theme image-swap
  pattern and the multi-frame-TIFF high-DPI pattern, both corroborating, not contradicting, this note's own
  reflection-confirmed API findings.
- (detail about the owner's other add-in withheld)
  (detail about the owner's other add-in withheld)
- The Issue #19 design record (not committed to this repository; produced by Claude Sonnet subagents
  orchestrated by Claude Fable, 2026-09-23 to 2026-09-24): four round-1 concept designers and a two-lens
  judge pass; three round-2 refinement treatments and a three-lens judges2 panel; a three-round
  polish/critique pass producing "V1"; two independent post-polish revisions ("V2", "V3"); two purpose-drawn
  16px companions to V3 ("V3a", "V3b"); a context-free blind recognition test; two position-counterbalanced
  forced-choice tests (one comparing V1/V2/V3, one comparing the four 16px candidates); the throwaway
  `icontool` and `wpfcheck` tools (not committed to this repository; the render/inspect/metrics/lint tooling
  and the WPF load-path check); small Python helper scripts used for concept generation, colour/contrast
  search, and critique verification throughout the record, including the shipped V3 candidate's own
  grid-geometry build script — see "Design process" above; a prepared manual evidence-plan runbook (not
  committed to this repository).
- The Issue #19 reader-test workflow exports (three workflow JSON exports plus their own README) — not
  committed to this repository; every reader-test figure in "Recognition testing" and "Final 16px asset"
  above was independently re-derived from these files' own per-reading records for this note, not merely read
  off their precomputed tallies.
- The 16px-companion design record's own notes for Approach A (not shipped) and Approach B (shipped as
  `SolidGround.16.png`) — not committed to this repository.

## What this note does not do

This note records Issue #19's design decisions, its API and colour evidence, and its still-open items. Its
own writing is part of Issue #19's explicitly authorized implementation task — the same task that also
replaces `src/SolidGround.Revit/Resources/{SolidGround.16.png,SolidGround.32.png,README.md}`'s bytes/content
and lands the companion test-file change described in "PNG format" above — so it does not need a further,
separate authorization the way a from-scratch change to `SolidGroundApplication.cs`,
`SolidGround.Revit.csproj`, or `scripts/Deploy-RevitAddIn.ps1` would (see "Purpose and boundary": this design
changes none of those three). This note's own text does not rewrite any other architecture note's history;
the same implementation task instead appends a short, dated pointer to this note in
`docs/architecture/revit-add-in-host-scaffold.md`, `docs/architecture/revit-2027-verification-and-host-design.md`,
and `docs/architecture/revit-add-in-conventions.md`, matching this repository's own established per-issue
closeout pattern (Issues #14, #15, and #16 each appended a dated record rather than rewriting an earlier
note). It does not run the manual evidence plan above or obtain the owner's review; both remain open, marked
by name above (the
"Evidence" section and "Known limitations") rather than assumed complete. Once both land, this note's
"Evidence" section should be filled in place — matching how
`docs/architecture/revit-extensible-storage-provenance.md`'s own "Manual evidence plan" placeholder was
filled from a real Revit 2027 session — rather than rewritten from scratch.
