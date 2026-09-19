# SolidGround

SolidGround is a planned Revit 2027 add-in for turning 1-meter USGS bare-earth elevation data from OpenTopography into a native Revit toposolid clipped to a single parcel. It is designed for the overall form of a residential lot: fetch a DEM, remove missing cells, transform and localize coordinates, preserve the parcel boundary, simplify the surface to a Revit-safe point budget, and retain enough provenance to reverse every transform.

The repository is currently in **Phase 1: Core contracts, AAIGrid parsing, the OpenTopography USGS 1 m source, and AOI normalization and parcel clipping established**. Core, CLI, and offline test projects compile on .NET 10; horizontal coordinate transformations, simplification algorithms, and exports remain future Phase 1 work. See the [Phase 1 contract design note](docs/architecture/phase-1-contracts.md), the [OpenTopography USGS 1 m source design note](docs/architecture/opentopography-usgs1m-source.md), and the [AOI normalization and clipping design note](docs/architecture/aoi-normalization-and-clipping.md). The Revit add-in project intentionally does not exist yet; it starts only after Phase 1 is complete and the owner's established Revit add-in conventions have been supplied.

## Scope

The intended workflow is:

1. Accept a WGS 84 bounding box, a latitude/longitude and radius, or a parcel polygon in GeoJSON or WKT.
2. Request the USGS 1 m DEM from OpenTopography as Arc/Info ASCII Grid (`AAIGrid`).
3. Reject malformed grids and remove every `NODATA_value` cell.
4. Transform horizontal coordinates and make the output unit explicit.
5. Clip to the parcel with an optional buffer.
6. Shift the surface to a recorded local origin.
7. Reduce the grid to an approximately 15,000-point budget with a terrain-aware method that preserves ridges and swales.
8. Export development artifacts from the CLI; in Phase 2, create the bounded toposolid through the Revit API.
9. Attach source, datum, quality, statistics, simplification, units, and local-origin provenance to the created element.

The primary fixture is Lot 191 at 12922 Robandee Ln, Saint Louis, Missouri, centered at `38.700186, -90.477652`. The approximately 14,496-square-foot lot has extensive mature tree canopy, so sparse ground returns and interpolation roughness are expected source-quality concerns.

## Accuracy

SolidGround is for overall lot form and site context. QL2 bare-earth lidar is roughly 10 cm vertical RMSE under favorable conditions, with poorer and less uniform results under canopy. The resulting surface is not suitable for foundation-perimeter grading or construction layout. Those tasks need field measurement, such as a rotary laser, or a professional survey.

SolidGround will preserve source resolution and quality metadata, but it cannot recover terrain that was never observed or remove interpolation artifacts without also changing the measured surface.

## Verified technical baseline

The following points were checked during setup on 2026-09-15:

- Revit 2027 uses .NET 10. The local Revit API assemblies are version `27.0.10.13`.
- OpenTopography's current OpenAPI definition exposes `GET /API/usgsdem`, accepts `datasetName=USGS1m`, and still lists `AAIGrid`. `GTiff` remains the default, so SolidGround will request `AAIGrid` explicitly.
- The parser follows Esri's ASCII raster contract: positive dimensions and cell size, matched lower-left corner or center origins, optional `NODATA_VALUE` defaulting to `-9999`, and north-to-south row-major samples. It converts NODATA to missing cells before returning an elevation grid.
- The same OpenAPI definition states that USGS 1 m access currently requires academic authorization or an enterprise API key. SolidGround will report access errors and will not substitute lower-resolution data silently.
- The OpenTopography catalog still contains `USGS_LPC_MO_StLouis_2017_LAS_2018`, collected 2017-02-17 through 2017-02-27, with NAVD88 Geoid12B vertical metadata. Catalog metadata is provenance context; the raster response's own coordinate reference information remains authoritative for processing.
- Revit 2027's installed `SiteDB.dll` contains `NativeToposolidMaxPointThreshold` and `LinkToposolidMaxPointThreshold`. Autodesk's public 2027 documentation found during setup did not restate their numeric limits, so the project uses a conservative application default near 15,000 and will re-check limits before the Revit phase.
- Revit 2027 added explicit isolated-context manifest settings and dependency declarations. The planned add-in will use a private context and will not share managed geospatial assemblies by default.
- Revit 2027 Extended Properties represent externally supplied, linked property data. SolidGround provenance is owned with the created element, so Extensible Storage is the current recommendation.

Primary references are the [Revit 2027 API changes](https://help.autodesk.com/view/RVT/2027/ENU/?guid=f7165618-24c9-4160-a7a4-09979fe4a981), [Revit 2027 Extensible Storage guide](https://help.autodesk.com/view/RVT/2027/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Storing_Data_in_the_Revit_model_Extensible_Storage_html), [Revit SDK downloads](https://aps.autodesk.com/developer/overview/revit-api), [OpenTopography API documentation](https://portal.opentopography.org/apidocs/), its [OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json), and the [Esri ASCII raster format](https://desktop.arcgis.com/en/arcmap/latest/manage-data/raster-and-images/esri-ascii-raster-format.htm).

## Architecture

```text
SolidGround.slnx
+-- src/
|   +-- SolidGround.Core/     pure .NET 10; no Revit reference
|   `-- SolidGround.Cli/      console host over Core
`-- tests/
    `-- SolidGround.Tests/    xUnit tests against Core

Phase 2 adds:
`-- src/SolidGround.Revit/    Revit 2027 host adapter and add-in manifest
```

The central design rule is that acquisition, parsing, geometry, transformations, simplification, provenance models, and exports remain testable without Revit installed. [Groundit](https://github.com/lewismconte/groundit) demonstrates the useful architectural pattern of a pure core with offline tests and a thin Revit-specific build step. SolidGround does not adopt Groundit's Python, pyRevit, browser, multi-version, or data-source choices.

Phase 1 may add managed geospatial packages, each only with a pinned version:

- **NetTopologySuite 2.6.0** is referenced for robust topology, buffered polygons, holes, multipolygons, and clipping (`SolidGround.Core.Aois.PolygonalRegion`, `ParcelGeometryParser`'s WKT reader, and `SolidGround.Core.Clipping`). These operations are complex enough that a hand-written substitute would create unnecessary geometry risk. GeoJSON parcel geometry is parsed separately, by a bounded, hand-written reader over the inbox `System.Text.Json.JsonDocument` rather than a further NetTopologySuite.IO package — see [the AOI normalization and clipping design note](docs/architecture/aoi-normalization-and-clipping.md) for the full rationale.
- **ProjNet** for horizontal coordinate transformations remains deferred, after its current API and coordinate-system coverage are verified. Vertical datum conversion is outside that justification.

No native dependency may be loaded into the Revit process. There is no Python or GDAL path.

## Build

Install the .NET 10 SDK listed in [`global.json`](global.json). Visual Studio users need Visual Studio 2026 version 18.0 or later to target .NET 10. Revit is not required for the current solution.

```powershell
dotnet restore SolidGround.slnx --locked-mode
dotnet build SolidGround.slnx --configuration Release --no-restore
dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release --no-build
```

Run the scaffolded CLI with:

```powershell
dotnet run --project src/SolidGround.Cli/SolidGround.Cli.csproj
```

Feature tests are offline by default. Parser tests use a small inspected synthetic fixture near the Robandee Lane scenario; it is not represented as measured terrain or an OpenTopography response. An opt-in end-to-end fetch test against the live OpenTopography endpoint runs only when both the `SOLIDGROUND_OPENTOPOGRAPHY_LIVE` environment variable is set to `1` and `OPENTOPOGRAPHY_API_KEY` is set to a non-empty value; otherwise it skips rather than failing the offline suite. Copy [`.env.example`](.env.example) only for local tooling that deliberately loads dotenv files; `.env` is ignored and SolidGround will not commit or log the key.

The current test dependencies are pinned: `Microsoft.NET.Test.Sdk` supplies the .NET test host, `xunit.v3` supplies the test framework, and `xunit.runner.visualstudio` enables discovery from `dotnet test` and Visual Studio. No coverage package is included because the initial CI does not publish coverage.

## Continuous integration and the Revit project

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) is plain GitHub Actions. It restores locked packages, builds Core and CLI, and runs the offline Core tests on the repository-scoped `solidground-pve2` ephemeral runner in the Homelab. Actions are pinned to immutable commit SHAs, and the workflow does not use GitHub-hosted cache storage.

SolidGround is public, so the self-hosted workflow accepts only trusted pushes to `main`. It has no pull-request trigger, checks the repository, owner, event, ref, and runner identity before checkout, and has no GitHub-hosted or pve1 fallback. Pull requests therefore do not run this workflow. If pve2 is unavailable, the job remains visibly queued instead of moving to another runner.

The trust-boundary conditions this lane must keep true, the authorizing Homelab issue number, and the Homelab never-list it must honor are recorded in [`AGENTS.md`](AGENTS.md)'s "Build and CI" section, mirroring Homelab ADR 0029. Homelab Issue #42 authorized and audited the `solidground-pve2` lane under that ADR, and Homelab Issue #45 recorded the owner's decision to harden this repository's Actions settings, which now require full-commit-SHA pinning and limit the allow-list to exactly `actions/checkout` and `actions/setup-dotnet`.

`SolidGround.Revit` can compile in CI only when the runner has lawful access to the Revit 2027 reference assemblies. The current Linux Homelab runner does not include them, and this repository will not commit Autodesk binaries or quietly depend on an unofficial repackaging. The recommended Phase 2 choices are an approved reproducible SDK/reference source or a suitable Windows self-hosted runner, selected alongside the owner's add-in conventions.

## Agent portability

[`AGENTS.md`](AGENTS.md) is the sole canonical instruction file. Current primary documentation produced this repository layout:

| T3 provider | Current project-instruction behavior | Repository file |
| --- | --- | --- |
| Codex | Reads root and nested `AGENTS.md` files directly. | `AGENTS.md` |
| Claude Code | Reads `CLAUDE.md`, explicitly recommends `@AGENTS.md` on Windows for an AGENTS-based repository. | `CLAUDE.md` containing one line: `@AGENTS.md` |
| Cursor | Reads root and nested `AGENTS.md`; `.cursor/rules` is an alternative. | `AGENTS.md` |
| Grok Build | Reads the `AGENTS.md` family while walking the project tree. | `AGENTS.md` |
| OpenCode | Uses `AGENTS.md` as its native custom-instruction file. | `AGENTS.md` |

Sources: [Codex AGENTS.md guidance](https://developers.openai.com/codex/agent-configuration/agents-md), [Claude Code memory files](https://code.claude.com/docs/en/memory), [Cursor rules](https://cursor.com/docs/rules), [Grok Build project rules](https://docs.x.ai/build/features/project-rules), and [OpenCode rules](https://opencode.ai/docs/rules/).

Only `CLAUDE.md` is needed as a vendor-specific pointer for the five requested providers. The repository therefore has no `.cursor/rules`, `.github/copilot-instructions.md`, or `GEMINI.md`. T3 Code's upstream repository now also advertises Google Antigravity, but that additional provider is outside the five-provider scope established for SolidGround.

## License

SolidGround is available under the [MIT License](LICENSE).
