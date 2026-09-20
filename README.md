# SolidGround

SolidGround is a planned Revit 2027 add-in for turning 1-meter USGS bare-earth elevation data from OpenTopography into a native Revit toposolid clipped to a single parcel. It is designed for the overall form of a residential lot: fetch a DEM, remove missing cells, transform and localize coordinates, preserve the parcel boundary, simplify the surface to a Revit-safe point budget, and retain enough provenance to reverse every transform.

The repository is currently in **Phase 1: Core contracts, AAIGrid parsing, the OpenTopography USGS 1 m source, AOI normalization and parcel clipping, coordinate/unit/local-origin transformation, terrain-aware decimation, provenance with deterministic exports, and the end-to-end CLI workflow established**. Core, CLI, and offline test projects compile on .NET 10; the `process`/`fetch`/`run`/`verify` CLI workflow is implemented and tested — see [Usage](#usage) below. See the [Phase 1 contract design note](docs/architecture/phase-1-contracts.md), the [OpenTopography USGS 1 m source design note](docs/architecture/opentopography-usgs1m-source.md), the [AOI normalization and clipping design note](docs/architecture/aoi-normalization-and-clipping.md), the [coordinate transformation and units design note](docs/architecture/coordinate-transformation-and-units.md), the [terrain-aware decimation design note](docs/architecture/terrain-aware-decimation.md), the [provenance and deterministic exports design note](docs/architecture/provenance-and-deterministic-exports.md), and the [CLI workflow design note](docs/architecture/cli-workflow.md). The Revit add-in project intentionally does not exist yet; it starts only after Phase 1 is complete and the owner's established Revit add-in conventions have been supplied.

## Scope

The intended workflow is:

1. Accept a WGS 84 bounding box, a latitude/longitude and radius, or a parcel polygon in GeoJSON or WKT.
2. Request the USGS 1 m DEM from OpenTopography as Arc/Info ASCII Grid (`AAIGrid`).
3. Reject malformed grids and remove every `NODATA_value` cell.
4. Transform horizontal coordinates and make the output unit explicit.
5. Clip to the parcel with an optional buffer.
6. Shift the surface to a recorded local origin.
7. Reduce the grid to an approximately 15,000-point budget with a terrain-aware method that preserves ridges and swales.
8. Export development artifacts from the CLI (implemented today by the `process`/`fetch`/`run` commands); in Phase 2, create the bounded toposolid through the Revit API.
9. Attach source, datum, quality, statistics, simplification, units, and local-origin provenance to the created element.

The primary fixture is [withheld] at [withheld] the reference parcel, the area, the area, centered at `[withheld], [withheld]`. The approximately [withheld]-square-foot lot has extensive mature tree canopy; a live capture over this lot showed the 1-meter surface responding to that canopy with smoothness rather than visible roughness — see "Terrain quality observed under canopy" in the [Phase 1 validation note](docs/architecture/phase-1-validation.md).

## Accuracy

SolidGround is for overall lot form and site context. QL2 bare-earth lidar is roughly 10 cm vertical RMSE under favorable conditions, with poorer and less uniform results under canopy. The resulting surface is not suitable for foundation-perimeter grading or construction layout. Those tasks need field measurement, such as a rotary laser, or a professional survey.

SolidGround preserves source resolution and quality metadata, but it cannot recover terrain that was never observed or remove interpolation artifacts without also changing the measured surface. At the reference parcel fixture, the observed 1-meter surface is smooth and fully populated, with no NODATA holes and millimeter-scale neighbor residuals almost everywhere; that smoothness is a same-surface proxy for internal consistency, not a measure of accuracy against the true ground beneath the canopy — see "Terrain quality observed under canopy" in the [Phase 1 validation note](docs/architecture/phase-1-validation.md).

## Verified technical baseline

The following points were checked during setup on 2026-09-15, unless otherwise dated below:

- Revit 2027 uses .NET 10. The local Revit API assemblies are version `27.0.10.13`.
- OpenTopography's current OpenAPI definition exposes `GET /API/usgsdem`, accepts `datasetName=USGS1m`, and still lists `AAIGrid`. `GTiff` remains the default, so SolidGround requests `AAIGrid` explicitly.
- The parser follows Esri's ASCII raster contract: positive dimensions and cell size, matched lower-left corner or center origins, optional `NODATA_VALUE` defaulting to `-9999`, and north-to-south row-major samples. It converts NODATA to missing cells before returning an elevation grid.
- The same OpenAPI definition states that USGS 1 m access currently requires academic authorization or an enterprise API key. SolidGround reports access errors and does not substitute lower-resolution data silently. Verified live on 2026-09-19: a no-key run observed exit code 3 with the exact authorization message.
- The OpenTopography catalog still contains `[withheld]`, collected 2017-02-17 through 2017-02-27, with NAVD88 Geoid12B vertical metadata. Catalog metadata is provenance context; the raster response's own coordinate reference information remains authoritative for processing.
- Verified live on 2026-09-19: the `usgsdem` `AAIGrid` response is packaged as a bare `.asc` body with no `.prj`/`.aux.xml` sidecar and no reference metadata of any kind; a same-box `GTiff` response for the identical request carries `EPSG:26915` (NAD83 / UTM zone 15N) as its projected coordinate system but no vertical GeoKeys. See "Response packaging and metadata observed" in the [Phase 1 validation note](docs/architecture/phase-1-validation.md).
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
- **ProjNET 2.1.0** (LGPL-2.1-or-later) is referenced for managed WKT1 horizontal coordinate transformations (`SolidGround.Core.Transformations.ProjNetHorizontalCoordinateTransformFactory`, built on `CoordinateSystemFactory.CreateFromWkt` and `CoordinateTransformationFactory.CreateFromCoordinateSystems`). It performs no vertical datum conversion; vertical reference metadata is preserved unchanged — see [the coordinate transformation and units design note](docs/architecture/coordinate-transformation-and-units.md) for the full rationale and the accepted NAD83↔WGS84 zero-datum-shift caveat.

No native dependency may be loaded into the Revit process. There is no Python or GDAL path.

## Exports and provenance

`SolidGround.Core.Exports` and `SolidGround.Core.Provenance` turn a clipped, simplified terrain sample set into a deterministic, byte-reproducible export bundle: a `*.solidground.json` document carrying full reversible provenance (source, datums, units, the local-origin offset, and point counts) alongside a bare `*.points.csv` file shaped for Revit's toposolid points-file import, bound together by a recorded sample count and SHA-256 hash. `TerrainProvenance.CurrentSchemaVersion` (currently `1`) names the one manifest `TerrainExportBundleRenderer` and `TerrainExportBundleReader` write and strictly read; see [the provenance and deterministic exports design note](docs/architecture/provenance-and-deterministic-exports.md) for the full manifest, the determinism rules, and how a local coordinate is reconstructed back to its source datum.

## Build

Install the .NET 10 SDK listed in [`global.json`](global.json). Visual Studio users need Visual Studio 2026 version 18.0 or later to target .NET 10. Revit is not required for the current solution.

```powershell
dotnet restore SolidGround.slnx --locked-mode
dotnet build SolidGround.slnx --configuration Release --no-restore
dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release --no-build
```

Run the CLI with a real command; see [Usage](#usage) below for one example per command:

```powershell
dotnet run --project src/SolidGround.Cli --configuration Release -- verify --document out/[withheld].solidground.json
```

Feature tests are offline by default. Parser tests use a small inspected synthetic fixture near the reference parcel scenario; it is not represented as measured terrain or an OpenTopography response. An opt-in end-to-end fetch test against the live OpenTopography endpoint runs only when both the `SOLIDGROUND_OPENTOPOGRAPHY_LIVE` environment variable is set to `1` and `OPENTOPOGRAPHY_API_KEY` is set to a non-empty value; otherwise it skips rather than failing the offline suite. Copy [`.env.example`](.env.example) only for local tooling that deliberately loads dotenv files; `.env` is ignored and SolidGround will not commit or log the key.

The current test dependencies are pinned: `Microsoft.NET.Test.Sdk` supplies the .NET test host, `xunit.v3` supplies the test framework, and `xunit.runner.visualstudio` enables discovery from `dotnet test` and Visual Studio. No coverage package is included because the initial CI does not publish coverage.

## Usage

Each command prints one line per stage by default; add `--verbose` for full diagnostics. Every command's
own `--help` lists its complete option set, and `--version` prints the CLI's version.

`--verbose` prints the redacted acquisition request evidence for `fetch`/`run`, and, whenever an AOI is
given, the clip's NODATA and region-excluded cell counts. `run --save-raster` keeps the raster set
(`.asc`, `.prj`, `.source.json`) alongside the export bundle instead of discarding it after processing.
Until the source-packaging decision recorded in the [Phase 1 validation note](docs/architecture/phase-1-validation.md)
lands, a live `fetch` or `run` against a real-coverage area large enough to clear OpenTopography's
undocumented area minimum ends with exit code 4; a smaller request — for example, the parcel-plus-5-meter-buffer
case recorded in that note — instead ends with exit code 2 before any raster is returned, and `process` against
a local `.asc` file with its own `.prj` runs end to end.

Process a local AAIGrid file offline:

```powershell
dotnet run --project src/SolidGround.Cli --configuration Release -- process --asc terrain.asc --parcel lot.geojson --buffer 3 --output out --name [withheld]
```

Fetch a raster set from OpenTopography:

```powershell
dotnet run --project src/SolidGround.Cli --configuration Release -- fetch --bbox [withheld],[withheld],[withheld],[withheld] --output out --name [withheld]
```

Fetch and process in one step:

```powershell
dotnet run --project src/SolidGround.Cli --configuration Release -- run --center [withheld],[withheld] --radius 60 --output out --name [withheld]
```

Verify a written export bundle:

```powershell
dotnet run --project src/SolidGround.Cli --configuration Release -- verify --document out/[withheld].solidground.json
```

`fetch` and `run` acquire data online and require an OpenTopography API key. Set `OPENTOPOGRAPHY_API_KEY`
in the process environment, or register one with the .NET user-secrets tool:

```powershell
dotnet user-secrets set OPENTOPOGRAPHY_API_KEY "<key>" --id solidground-cli
```

See the [CLI workflow design note](docs/architecture/cli-workflow.md) for the full option table, exit
codes, secrets resolution order, and known limitations.

## Continuous integration and the Revit project

[`.github/workflows/ci.yml`](.github/workflows/ci.yml) is plain GitHub Actions. It restores locked packages, builds Core and CLI, and runs the offline Core tests on the repository-scoped `self-hosted` ephemeral runner in the infrastructure project. Actions are pinned to immutable commit SHAs, and the workflow does not use GitHub-hosted cache storage.

SolidGround is public, so the self-hosted workflow accepts only trusted pushes to `main`. It has no pull-request trigger, checks the repository, owner, event, ref, and runner identity before checkout, and has no GitHub-hosted or a second self-hosted runner fallback. Pull requests therefore do not run this workflow. If self-hosted is unavailable, the job remains visibly queued instead of moving to another runner.

The trust-boundary conditions this lane must keep true, the authorizing the infrastructure project issue number, and the infrastructure project never-list it must honor are recorded in [`AGENTS.md`](AGENTS.md)'s "Build and CI" section, mirroring a prior infrastructure decision. a prior infrastructure decision authorized and audited the `self-hosted` lane under that ADR, and a prior infrastructure decision recorded the owner's decision to harden this repository's Actions settings, which now require full-commit-SHA pinning and limit the allow-list to exactly `actions/checkout` and `actions/setup-dotnet`.

`SolidGround.Revit` can compile in CI only when the runner has lawful access to the Revit 2027 reference assemblies. The current Linux the infrastructure project runner does not include them, and this repository will not commit Autodesk binaries or quietly depend on an unofficial repackaging. The recommended Phase 2 choices are an approved reproducible SDK/reference source or a suitable Windows self-hosted runner, selected alongside the owner's add-in conventions.

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
