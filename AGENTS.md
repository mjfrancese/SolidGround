# SolidGround agent instructions

`AGENTS.md` is the only canonical instruction file in this repository. The agent must treat any tool-specific file as a pointer to this file, never as a second instruction source. The agent must keep the repository usable from T3 Code with Claude Code, Codex, Cursor, Grok Build, and OpenCode. The agent must not add tool-specific servers, commands, hooks, automation, or `.claude/` and `.codex/` directories.

## Mission and current boundary

SolidGround is a Revit 2027 add-in that obtains 1-meter USGS bare-earth elevation data through OpenTopography, clips it to one parcel, simplifies it without erasing important terrain form, and creates a native Revit toposolid with reversible provenance.

Phase 0 establishes the repository. Phase 1 implements `SolidGround.Core`, `SolidGround.Cli`, and offline tests. Phase 2 implements the Revit add-in. The agent must not begin Phase 1 without an explicit implementation task. The agent must not scaffold or implement `SolidGround.Revit` until Phase 1 is complete, the user has approved it, and the user has supplied the add-in conventions used in their other repositories.

## Architecture

| Project | Target | Responsibility |
| --- | --- | --- |
| `src/SolidGround.Core` | `net10.0` | Revit-free domain logic: AOIs, OpenTopography requests, AAIGrid parsing, coordinate transforms, clipping, unit conversion, local-origin transforms, decimation, provenance, and exports. |
| `src/SolidGround.Cli` | `net10.0` | Thin development and batch interface over Core. |
| `tests/SolidGround.Tests` | `net10.0` | xUnit tests against Core using committed offline fixtures. |
| `src/SolidGround.Revit` | `net10.0-windows` in Phase 2 | Thin Revit host adapter, ribbon application, external command, boundary conversion, toposolid creation, and provenance attachment. |

The agent must keep every Revit reference out of Core, CLI, and Tests. The agent must keep source acquisition pluggable so a future classified point-cloud source can coexist with the initial gridded DEM source. The agent must not use Python, GDAL, native geospatial binaries, or the Forma Connected Client.

Groundit is an architecture reference only: its useful pattern is a host-independent core with offline tests and a thin Revit build step. The agent must not copy its Python, pyRevit, browser, data-source, or multi-version design.

## Revit 2027 rules

The agent must target Revit 2027 and .NET 10 only. The agent must not add Revit 2024, 2025, or 2026 compatibility. Before writing or changing a Revit API call, manifest feature, installation path, or Revit-specific setting, the agent must verify it against the Revit 2027 SDK or Autodesk's 2027 API documentation and cite the source in the relevant design note or pull request. Earlier-version examples are not sufficient evidence.

The local reference installation inspected during setup is Revit 2027 API version `27.0.10.13`. Autodesk's 2027 API changes document confirms the .NET 10 migration and the all-user add-in move from `ProgramData` to `Program Files`. The agent must never hardcode an all-user add-in directory. The add-in should install per-user unless the later installer design requires all-user installation; runtime code that needs the all-user path must read `Application.AllUsersAddinsLocation`.

Revit 2027 add-in isolation is real and must be used deliberately in Phase 2. The installed `RevitAddInUtility.dll` exposes:

- `Autodesk.RevitAddIns.AddInDependencyBase.AssemblyNames`
- `Autodesk.RevitAddIns.ClientIdDependency`
- `Autodesk.RevitAddIns.ContextNameDependency`
- `Autodesk.RevitAddIns.RevitAddInManifestSettings.UseRevitContext`
- `Autodesk.RevitAddIns.RevitAddInManifestSettings.ContextName`
- `Autodesk.RevitAddIns.RevitAddInManifestSettings.UseAllContextsForDependencyResolution`
- `Autodesk.RevitAddIns.RevitAddInManifestSettings.PublicAssemblies`
- `Autodesk.RevitAddIns.RevitAddInManifestSettings.Dependencies`

The Phase 2 default is an isolated add-in context: set `UseRevitContext` to `false`, use a stable SolidGround context name, leave all-context dependency resolution disabled, and expose or consume no shared assemblies unless a reviewed integration explicitly requires it. The agent must bundle only managed dependencies and keep the geospatial dependency graph private to that context.

The installed Revit 2027 `SiteDB.dll` contains both `NativeToposolidMaxPointThreshold` and `LinkToposolidMaxPointThreshold`, confirming that the settings still exist in 2027. Autodesk documents a 10,000–50,000 range and 20,000 default for the native setting in Revit 2026; no 2027 Autodesk page found during setup restated those numeric defaults. The agent must retain a conservative application default near 15,000, make the budget configurable, and re-verify the numeric limits before Phase 2 behavior depends on them.

## Provenance decision

The agent must use Extensible Storage for SolidGround-owned provenance on the created toposolid unless new 2027 evidence changes the tradeoff. Revit 2027 Extended Properties are built around `Autodesk.Revit.ExternalData.ExtendedPropertiesLink`, external-server load content, link data, bindings, and externally authored values. That lifecycle is useful for linked external property providers but unnecessary for metadata created and owned with one element. Revit 2027 Extensible Storage directly supports a schema-backed `Entity` attached to an `Element`, retrieved with `Element.GetEntity()` and removed with `Element.DeleteEntity()`.

The Extensible Storage schema must have a stable GUID and explicit schema version. It must preserve, at minimum, source dataset, collection date, quality level, horizontal datum, vertical datum, original and retained point counts, elevation minimum and maximum, output unit and foot definition, and the complete local-origin offset needed to reverse the transform.

## Data and numeric contracts

The first source is OpenTopography's `GET /API/usgsdem` endpoint with `datasetName=USGS1m`. The OpenAPI definition checked on 2026-09-15 still lists `AAIGrid`; the agent must request it explicitly because its default is `GTiff`. The same definition says USGS 1 m access is restricted to academic users or an enterprise API key. The agent must surface authorization failures accurately and must not silently fall back to a coarser dataset.

The agent must support these AOIs: WGS 84 bounding box, latitude/longitude plus radius, and parcel polygon supplied as GeoJSON or WKT. Polygon clipping must support a configurable buffer. The agent must treat every AAIGrid `NODATA_value` as missing data before conversion, interpolation, clipping, statistics, or export. A sentinel must never become an elevation.

The agent must read and preserve the actual raster coordinate reference system and vertical metadata returned for each request. The agent must not assume that the USGS 1 m mosaic uses the native coordinate system of a named source collection. The known St. Louis point-cloud collection is `USGS_LPC_MO_StLouis_2017_LAS_2018`, with collection dates 2017-02-17 through 2017-02-27 and NAVD88 Geoid12B metadata in the current OpenTopography catalog; this is provenance context, not permission to assume the downloaded raster's CRS.

All transformations must be explicit and reversible. The agent must centralize length conversion in one tested component. Supported output units must state the exact definition. The default is U.S. survey foot, exactly `1200 / 3937` meters per foot. International foot, exactly `0.3048` meters, may be selected explicitly. Revit consumes decimal-foot coordinates; the local-origin and unit metadata must make the numeric interpretation unambiguous.

The agent must subtract a chosen projected local origin before coordinates enter Revit and must carry the original offset, CRS, datum, and unit through exports and provenance. The agent must reject any workflow that cannot reconstruct source coordinates from local coordinates plus metadata.

The default point budget is approximately 15,000. The agent must provide a terrain-aware simplifier, such as curvature-aware selection or TIN error simplification, and may also provide a clearly labeled simple sampler for comparison. The agent must not make every-Nth sampling the only or default algorithm. Tests must cover ridges, swales, edges, NODATA holes, determinism, and budget enforcement.

## Dependency policy

The agent must pin every package version. The agent must prefer the standard library for small, well-bounded functions and must document why each package is needed when it is added.

ProjNet is acceptable in Phase 1 for managed horizontal coordinate transformations after its exact API and supported definitions are verified. It does not by itself justify claims about vertical datum transformations. NetTopologySuite is acceptable because robust GeoJSON/WKT parsing, polygon buffering, holes, multipolygons, and clipping are substantive geometry operations; the agent must avoid adding it if the accepted Phase 1 scope narrows to a simpler geometry contract. Neither package is part of Phase 0.

The agent must not add a package that loads native binaries into the Revit process. The agent must not commit Autodesk Revit assemblies. A later Revit project may reference the locally installed 2027 SDK or a reviewed reference-assembly source, following the user's add-in conventions.

## Secrets, downloads, and logs

The OpenTopography key must come from `OPENTOPOGRAPHY_API_KEY` in the process environment or .NET user secrets. `.env` files are local conveniences only and are ignored. The agent must never print, persist, commit, embed in a fixture, place in a URL shown in logs, or include the key in an exception message. The agent must redact request query strings before logging.

Downloaded rasters and generated bulk data are ignored by default. Small deterministic fixtures may be committed only after inspection proves that they contain no key, authorization header, signed URL, or personal token.

## Accuracy and product claims

The agent must describe SolidGround as a site-form tool, not a survey instrument. QL2 bare-earth data is roughly 10 cm vertical RMSE in favorable conditions and can be worse under mature canopy. Sparse ground returns and interpolation artifacts at the Robandee Lane fixture are expected quality limitations and must remain visible in diagnostics and documentation. The agent must never claim suitability for foundation-perimeter grading; that work requires site measurement such as a rotary laser or professional survey.

## Test fixture and verification

The primary scenario is 12922 Robandee Ln, Saint Louis, Missouri 63146, centered at `38.700186, -90.477652`: Lot 191, Seven Pines Plat No. 4, approximately 14,496 square feet. The lot has mature tree canopy. The agent must keep default tests offline and deterministic. A real OpenTopography fetch must require an explicit opt-in flag and the environment key; lack of a key must skip or clearly decline the online test, not fail the offline suite.

The agent must add tests for every observable Core behavior, especially header validation, row/column counts, NODATA, coordinate orientation, units, local-origin reversibility, clipping, decimation, and deterministic export. The agent must not require Revit to run Core tests.

## Build and CI

The supported commands from the repository root are:

```powershell
dotnet restore SolidGround.slnx --locked-mode
dotnet build SolidGround.slnx --configuration Release --no-restore
dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration Release --no-build
```

The agent must keep `packages.lock.json` files current and committed when package references change. The agent must run restore, build, and tests before presenting a code change as complete.

GitHub Actions must remain ordinary .NET CI with no AI service. During Phase 1 it builds Core and CLI and runs offline tests on the repository-scoped `solidground-pve2` ephemeral Homelab runner. Because the repository is public, this self-hosted workflow must accept trusted pushes to `main` only: no pull-request trigger, no fork code, and no automatic GitHub-hosted or pve1 fallback. It must retain its repository, owner, event, ref, and runner-identity guards and must not use GitHub-hosted cache or artifact storage.

The `solidground-pve2` lane was provisioned and audited under Homelab Issue #42 and Homelab ADR 0029, the decision naming this repository for a public-repository trusted-push ARC exception. Homelab Issue #45 recorded the owner's 2026-09-16 decision to harden this repository's own GitHub Actions settings to satisfy that ADR's ninth condition. Any change to the conditions below is a new Homelab security decision, not a routine workflow edit, and the agent must keep each one true:

1. The agent must trigger the workflow only on `push` to `main` in the canonical repository.
2. The agent must not add a pull-request, fork, issue, comment, release, external-dispatch, or reusable-workflow entry path.
3. The agent must keep a job-level guard checking the canonical repository, owner, push event, and `refs/heads/main` before runner allocation.
4. The agent must keep a first step checking those same fields plus the `solidground-pve2-*` runner name before checkout.
5. The agent must assume the GitHub App is installed on this repository alone, with Administration read/write and Metadata read only, no webhook, user permissions, or subscribed events.
6. The agent must assume the runner pod is unprivileged and runner-only, with no Docker daemon or service container.
7. The agent must not add repository secrets, deployment credentials, GitHub-hosted cache, or artifact storage to the job.
8. The agent must not add a GitHub-hosted or pve1 fallback.
9. The agent must keep the repository's GitHub Actions settings at full-commit-SHA pinning required and an allow-list narrowed to exactly the actions the workflow uses, with fork pull-request contributor approval tightened beyond GitHub's stock default.

The agent must also keep the Homelab runbook's never-list true here: never let this lane see a pull-request, fork, issue, comment, release, or external-dispatch event; never add a GitHub-hosted or pve1 fallback; never give the job repository secrets, deployment credentials, hosted cache, or artifact storage beyond the default read-only `GITHUB_TOKEN`; never treat the `solidground-pve2` runner label itself as a security boundary, and never assume ADR 0029 covers any repository beyond the one it names; never let untrusted or fork code reach the runner under any label; and never perform Homelab infrastructure changes (GitHub App, Kubernetes Secret, Helm release, or other pve2/VM400 state) from within this repository, since that is Homelab-session work under Homelab's own protocol, not SolidGround's.

Condition 9 is implemented today as: `sha_pinning_required=true`; `allowed_actions` narrowed to exactly `actions/checkout` and `actions/setup-dotnet`; fork pull-request contributor approval required for all outside collaborators; and the default workflow token permission read-only. If a run ever fails with an "action not allowed" message, the agent must report it and must not widen the allow-list without the owner.

The current Linux Homelab runner does not include Autodesk's Revit reference assemblies. The agent must not make Phase 1 CI depend on unofficial repackaged Autodesk binaries. Phase 2 CI is a separate design decision: it can compile the add-in only after the user chooses a lawful, reproducible reference-assembly source or a suitable Windows self-hosted runner.

## Authoritative references

- [Autodesk Revit 2027 API changes](https://help.autodesk.com/view/RVT/2027/ENU/?guid=f7165618-24c9-4160-a7a4-09979fe4a981)
- [Autodesk Revit 2027 Extensible Storage guide](https://help.autodesk.com/view/RVT/2027/ENU/?guid=Revit_API_Revit_API_Developers_Guide_Advanced_Topics_Storing_Data_in_the_Revit_model_Extensible_Storage_html)
- [Autodesk Revit SDK downloads](https://aps.autodesk.com/developer/overview/revit-api)
- [OpenTopography API documentation](https://portal.opentopography.org/apidocs/)
- [OpenTopography OpenAPI definition](https://portal.opentopography.org/apidocs/openapi.json)
- [Groundit architecture reference](https://github.com/lewismconte/groundit)

The agent must prefer these primary sources and current package documentation. If current evidence conflicts with this file, the agent must stop the affected design, report the conflict, and update `AGENTS.md` only with the user's accepted direction.
