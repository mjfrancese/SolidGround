# SolidGround

SolidGround is a free, open-source Revit 2027 add-in that turns real, measured ground elevation data into a native Revit terrain surface for one property — clipped to the parcel, simplified to a safe size, and labeled with a record of exactly where the data came from. It's built for architects, designers, and builders who want a quick, real terrain surface instead of a flat pad or hand-traced contours.

**Status:** version 0.1.0 was released 2026-09-25. See [Status and roadmap](#status-and-roadmap) below.

## Contents

- [What it does](#what-it-does)
- [Who it's for, and why use it](#who-its-for-and-why-use-it)
- [What you need](#what-you-need)
- [Getting started](#getting-started)
- [Accuracy](#accuracy)
- [Limitations](#limitations)
- [Status and roadmap](#status-and-roadmap)
- [For developers](#for-developers)
- [License](#license)

## What it does

A few terms used below, explained once:

- **Lidar** — a way of measuring ground height from an aircraft using laser pulses.
- **Bare-earth data** — lidar with trees, buildings, and other clutter already removed, leaving just the ground.
- **Toposolid** — Revit's own name (since Revit 2024) for a native, built-in terrain surface object.
- **OpenTopography** — the public data service SolidGround downloads elevation data from.
- **API key** — a private code that lets SolidGround talk to OpenTopography on your behalf.
- **Provenance** — a record of exactly where a piece of data came from, kept alongside it.

From your point of view, using SolidGround looks like this:

1. Install SolidGround as a Revit 2027 add-in (see [Getting started](#getting-started)). It adds one tab to the ribbon, "SolidGround," with one button, "Create Toposolid."
2. The first time you click the button, SolidGround writes a starting configuration file and stops, asking you to edit it.
3. In that file, you choose:
   - Where the terrain data comes from — a live download, or a file you already have.
   - The area you want — a parcel boundary file, a map point plus a radius, or a rectangular box.
   - Your preferred unit of measurement.
   - Optionally, a target point budget (15,000 by default).
   - Optionally, which Level and terrain type in your Revit project to use.
4. Click the button again. SolidGround first runs a read-only check: it confirms a project is open, your settings make sense, your area is reachable, and (for a live download) that your API key is set. Nothing in your model changes yet — if anything is wrong, one message lists every problem at once.
5. If that check passes, SolidGround gets the terrain data and removes any cells with no data. It clips the result to your chosen area and shifts the coordinates to line up cleanly with your project. Finally, it reduces the number of points to a safe amount, using a method that tries to preserve ridges and low spots rather than flattening them out.
6. SolidGround creates one native toposolid. If anything about the result looks wrong, the whole thing is undone automatically and your model is left exactly as it was — you never end up with a half-built surface.
7. On success, you'll see a confirmation message with the element's ID and how much of the available detail was kept. The same source information is also written directly onto the new toposolid, so it stays with your Revit file even after you close and reopen it.

There's also a command-line version of the same tool, mainly for developers and batch work — see [For developers](#for-developers).

## Who it's for, and why use it

SolidGround is for architects, designers, and builders working in Revit 2027 who want a quick, real terrain surface for a site — instead of starting from a flat pad, hand-tracing contours from a PDF, or building a surface manually from a separate survey file.

What it offers:

- **Free and open source**, under the MIT license.
- **Fine-grained U.S. elevation data** — 1-meter resolution, fetched automatically once you give it an area, with no manual file-hunting required.
- **Clips to your actual parcel shape**, not just a rectangle.
- **Keeps the terrain's real shape** — ridges, low spots, and slopes — when it reduces the data down to a size Revit can handle, instead of thinning it out evenly and blurring those features away.
- **Records where the data came from, on the element itself** — the source, its date, its accuracy tier, and the exact math needed to trace a point back to its real-world location, all stored with the toposolid so it survives save and reopen.
- **Nothing extra to install** — no separate GIS (mapping) software, no Python, no scripting environment. Just Revit and the SolidGround add-in.

### How it compares to other ways to get terrain into Revit

Revit has no built-in way to fetch real-world elevation data on its own — by itself, it can only build a surface from a file you already have (survey CAD, or a points file). Here is how SolidGround compares to a few other tools that fill that gap, based on each tool's own published information, as of 2026-09-25:

| Tool | What you need | Elevation detail (published figures) | Cost |
| --- | --- | --- | --- |
| **SolidGround** | An OpenTopography account with U.S. 1-meter access | 1 meter (U.S. only) | Free, open source |
| Autodesk Forma + its Revit add-in | A Forma subscription, or the AEC Collection | About 30 meters worldwide by default (about 25 meters in Europe); finer where a regional dataset applies | $700/year for a standalone Forma Site Design subscription, or $3,675/year via the AEC Collection |
| Groundit (free, open-source pyRevit extension) | pyRevit and IronPython installed; no account or key | About 10–30 meters worldwide | Free, open source |

As of the Revit 2027 release, Autodesk includes some Forma access — including Forma Site Design — directly with a Revit subscription itself, so an existing Revit 2027 subscriber may not need a separate purchase for at least part of what's shown above. (Source: Autodesk's own ["What's New in Revit 2027"](https://www.autodesk.com/blogs/aec/2026/04/07/whats-new-in-revit-2027/) post, 2026-04-07.)

*(Already sold? Skip to [What you need](#what-you-need).)*

A few more points of comparison, all drawn from each product's own materials:

- **Add-ins that turn a LiDAR file you already have into a Revit surface** (for example, "Topography" by archi, or the point-cloud tools built into "Environment" by arch-intelligence) can exceed 1-meter detail if you already own a high-density scan — but you have to find and supply that file yourself. SolidGround fetches U.S. 1-meter data automatically once you've set up access.
- **Free, no-account tools** like Groundit work worldwide with no sign-up at all, at a coarser resolution than SolidGround's U.S. data, and without clipping to a parcel shape.
- Of the tools we compared, none is documented as clipping to an arbitrary parcel outline, simplifying while preserving terrain shape, or writing this kind of traceable record onto the created element — all three of which SolidGround does.

Where other tools may currently serve you better:

- **Anywhere in the world.** SolidGround only covers the continental United States. Forma, Groundit, and several others work worldwide, at a coarser resolution than SolidGround's U.S. data.
- **No special account needed.** Groundit needs no account or key at all. SolidGround requires an OpenTopography account with 1-meter access, which is not automatic (see [Limitations](#limitations)).
- **Typing an address or drawing a box in the tool itself.** Several other tools let you search or draw the area directly. SolidGround has this planned but not yet built (see [Status and roadmap](#status-and-roadmap)).
- **Older Revit versions.** Some tools support Revit versions going back to 2015. SolidGround targets Revit 2027 only.
- **More than terrain.** Forma, Groundit, and others also bring in buildings, roads, and imagery. SolidGround is deliberately scoped to terrain alone.

## What you need

- **Windows**, with **Revit 2027** already installed. No earlier Revit version is supported.
- **The SolidGround release zip**, from the [Releases page](https://github.com/mjfrancese/SolidGround/releases). No programming tools or source code are needed for ordinary use.
- **An OpenTopography account and API key with U.S. 1-meter access** — but only if you want to download terrain live. If you already have an elevation grid file (the plain-text `.asc` grid format OpenTopography also delivers), no key is required.
  - This access is not automatic: as of 2026-09-25, OpenTopography's own documentation states that 1-meter data is restricted to academic users, or to anyone who separately requests an "enterprise" key. An ordinary free key without that access is rejected — SolidGround reports this plainly rather than silently using lower-quality data instead.
- **An area to model**: a parcel boundary file — a shape file in GeoJSON or WKT format (two common plain-text ways of describing a boundary; a county GIS/assessor site, or a surveyor or civil engineer, can often supply one) — or a map center point plus a radius, or a rectangular box of coordinates.
- **Administrator rights**, but only for the one-time certificate trust step described next (optional, but recommended). Everything else installs for your Windows user only, with no admin prompt.

## Getting started

1. Download the latest release from the [Releases page](https://github.com/mjfrancese/SolidGround/releases).
2. Follow [`docs/revit-install-guide.md`](docs/revit-install-guide.md) — it walks through verifying the download, installing, and your first run, step by step.

   *About Revit's security warning:* SolidGround is signed with its own certificate rather than one bought from a public certificate authority. Until your computer trusts that certificate, Revit shows a **"Security - Invalid Signature"** warning when it starts, saying the add-in may have been tampered with and recommending you don't load it. That wording comes from the certificate not being publicly issued, not from anything being wrong with the files (the installer checks every file's signature before it installs anything). The install guide includes a one-time step, run as administrator, that tells your computer to trust the SolidGround certificate; after that, Revit loads SolidGround with no warning. We recommend doing it. The guide explains exactly what trusting a certificate means before you decide.

3. If you plan to download terrain live, set your OpenTopography API key as described in the guide.

## Accuracy

**SolidGround is a site-form tool, not a survey instrument.** The underlying lidar data is roughly 10 centimeters (about 4 inches) of typical vertical error under good conditions, and worse under thick tree cover, where fewer laser pulses reach the ground. The resulting surface is a good guide to a site's overall shape — high points, low points, slopes, and drainage — but it is not suitable for foundation-perimeter grading or construction layout. Those need an actual field survey or a rotary laser.

## Limitations

Other real limitations, honestly listed:

- **United States only** — and not the entire country. SolidGround only supports the lower 48 states. Alaska, Hawaii, Puerto Rico, and other U.S. territories are deliberately excluded, because SolidGround cannot confirm the correct elevation reference system for those areas and won't guess.
- **Special access required.** As noted above, 1-meter data needs an academic-authorized account or a separately requested enterprise key — an ordinary free account isn't enough.
- **A download counts as two requests, not one**, against your daily limit, because the data provider's response doesn't include its own coordinate-system information, so SolidGround has to ask a second time just to learn that.
- **Daily and per-request limits apply**, set by the data provider rather than SolidGround: as of 2026-09-25, 200 requests per day for academic accounts (50 for others), and a single request can't cover more than 250 square kilometers.
- **Revit has its own ceiling on terrain detail.** Revit quietly stops adding points to a terrain surface once it nears its own configured limit (20,000 by default, adjustable between 10,000 and 50,000). SolidGround targets a more conservative 15,000 points by default, and checks your Revit setting before it starts, so you get a clear message instead of a silently cut-off surface.
- **No "type an address" feature yet.** You currently provide the parcel file, map point and radius, or bounding box yourself. See [Status and roadmap](#status-and-roadmap).
- **Doesn't touch your project's shared coordinates.** SolidGround only reads them, to confirm it hasn't changed anything by accident — it never moves your project's base point or survey point.
- **A security warning until you trust the certificate.** Without the one-time trust step, Revit warns that the add-in's signature is invalid every time it starts — see [Getting started](#getting-started).
- **An occasional Revit crash at startup, cause not yet known.** In testing on Revit 2027 update 2027.0.1, Revit sometimes crashed while opening a document right after it started: 3 of 7 test launches with SolidGround loaded, and none of 2 launches without it. The crash happens inside Revit itself, it also happened with an unsigned build, and a relaunch has always worked. Installing the latest Revit 2027 update is recommended. If it keeps happening to you, please open an issue.
- **Elevations are used exactly as delivered.** SolidGround does not adjust or convert between different vertical measurement systems; you get the source data's own reference height, unchanged.
- **A smooth-looking surface under trees isn't proof of accuracy.** Where the ground was hard to see from the air, the delivered surface fills the gap by estimating between nearby points — and that estimate can look just as smooth as a directly measured area. SolidGround can't tell the difference from the data alone, and doesn't pretend otherwise.

## Status and roadmap

**Version 0.1.0** was released on 2026-09-25 — the first installable version for Revit 2027. See the [release notes](https://github.com/mjfrancese/SolidGround/releases/tag/v0.1.0).

Everything described in [What it does](#what-it-does) above is built and working today.

**Planned, not yet built:** typing in a street address and having SolidGround find and let you confirm the matching parcel boundary automatically, instead of supplying one yourself. This has been researched but not implemented.

## For developers

SolidGround is a .NET 10 solution. [`AGENTS.md`](AGENTS.md) is this repository's canonical instructions file, for both human contributors and AI coding assistants.

```powershell
dotnet restore SolidGround.slnx --locked-mode
dotnet build SolidGround.slnx --configuration Release --no-restore
dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release --no-build
```

Building the whole solution needs the Revit 2027 SDK installed locally, since `SolidGround.Revit` is part of it. `SolidGround.Core`, `SolidGround.Cli`, and their tests do not — build or test those individually if you don't have Revit installed:

```powershell
dotnet build src/SolidGround.Core/SolidGround.Core.csproj --configuration Release
dotnet build src/SolidGround.Cli/SolidGround.Cli.csproj --configuration Release
dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release
```

**Command-line tool.** `SolidGround.Cli` offers the same fetch/clip/simplify/export pipeline outside Revit — useful for development, batch runs, or inspecting results without opening a Revit project. Four commands: `process` (use a local terrain file, no key needed), `fetch` (download a raster from OpenTopography), `run` (fetch and process in one step), and `verify` (re-check a previously written result). Example:

```powershell
dotnet run --project src/SolidGround.Cli --configuration Release -- run --center 41.591194,-93.603806 --radius 60 --output out --name example-site
```

**Project layout:**

```text
src/SolidGround.Core     Revit-free domain logic: data parsing, geometry, coordinate transforms, provenance
src/SolidGround.Cli      Console tool over Core
src/SolidGround.Revit    Revit 2027 add-in host: ribbon, command, toposolid creation
tests/SolidGround.Tests  Offline xUnit tests against Core (no Revit required)
```

Design notes for each major piece of work live under [`docs/architecture/`](docs/architecture/), and the install guide referenced above is at [`docs/revit-install-guide.md`](docs/revit-install-guide.md).

## License

SolidGround is available under the [MIT License](LICENSE). It uses a small number of third-party open-source packages; see [`THIRD-PARTY-NOTICES`](THIRD-PARTY-NOTICES) for their licenses.
