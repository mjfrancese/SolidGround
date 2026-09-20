# CLI workflow

Issue #9 implements `SolidGround.Cli`: a dependency-free, hand-rolled argument parser and four commands —
`process`, `fetch`, `run`, and `verify` — built on the Core pipeline the earlier design notes in this
directory already describe. It adds no package reference beyond what `SolidGround.Core` already carries,
and it adds exactly two members to Core: `SolidGround.Core.Rasters.AaiGridWriter` (a new file) and
`ElevationGrid.GetCornerEnvelope()` (additive, on the existing `Terrain/ElevationData.cs`). Acquisition,
AAIGrid parsing, AOI normalization and clipping, coordinate/unit/local-origin transformation, terrain-aware
decimation, and provenance and export are otherwise unchanged and covered by the design notes this one
builds on.

## Purpose and boundaries

This design covers the whole `SolidGround.Cli` executable: argument parsing, the four commands, the
processing pipeline that binds them to Core, raster-set persistence, OpenTopography key resolution, and
diagnostics.

- **What this adds.** The complete CLI surface: `Program.cs`, the public `CliApplication.RunAsync` entry
  point, the option table and parser, the four commands, the processing pipeline `process` and `run` both
  call, the `.source.json` raster-set sidecar and its reader and writer, the OpenTopography API key
  resolution chain, and the CLI's own exit-code and diagnostics conventions.
- **What this does not change.** Every Core contract the earlier design notes describe stays exactly as
  documented, with the two additive exceptions this note covers under "Raster set persistence" and "Local
  origin selection and its consequences" below: `AaiGridWriter` and `ElevationGrid.GetCornerEnvelope()`. The
  committed golden fixtures and `AGENTS.md` are untouched by this issue.
- **What Phase 2 still owns.** Nothing here creates a toposolid, attaches Extensible Storage provenance, or
  references the Revit API. The export bundle and raster set this CLI writes are the same on-disk artifacts
  a future Revit adapter will read; this issue does not add that adapter.

## Commands

Every command binds its own arguments to a strongly typed options record and then delegates to Core; none
of the four duplicates domain logic Core already owns.

| Command | Online or offline | Purpose |
| --- | --- | --- |
| `process` | Offline | Parses an already-downloaded AAIGrid raster (`--asc`/`--prj`, and an optional `--source-json` sidecar written by `fetch`), optionally clips it to an AOI, and writes an export bundle (`*.solidground.json` plus `*.points.csv`). |
| `fetch` | Online | Acquires a DEM from OpenTopography for exactly one AOI and writes a raster set (`*.asc` plus `*.prj` plus `*.source.json`) to `--output`. It performs no clipping, no local-origin placement, and no simplification. |
| `run` | Online | Acquires exactly as `fetch`, then processes the acquired grid exactly as `process` does, writing the export bundle in one invocation. `--save-raster` additionally writes the raster set beside the bundle. |
| `verify` | Offline | Reads a written export bundle strictly, prints its provenance summary, and confirms that reconstructing every sample's source coordinate and converting it back is bit-exact. |
| `help [verb]`, `--help`/`-h`, `--version` | Offline | Prints command help (the whole table, or one command's own options) or the CLI's version, and exits without touching any file. |

`process` and `run` share one internal processing pipeline (`Processing/TerrainProcessingPipeline.cs`)
rather than two independent implementations, so their clipping, local-origin, unit, and simplification
behavior can never drift apart from each other: given an already-parsed grid and an already-built WGS
84-to-grid transform, the pipeline (1) clips to the AOI when one was given, or processes the whole grid
otherwise; (2) computes the local origin (see "Local origin selection and its consequences"); (3) builds the
local coordinate frame from that origin, the grid's horizontal reference, the resolved vertical reference,
and the requested output unit; (4) simplifies the candidate grid to the requested point budget and method;
and (5) assembles the self-describing export payload the provenance design note documents. `fetch` never
calls this pipeline at all — it stops once the raster set is written. `run` additionally confirms, before
calling the pipeline, that the WGS 84-to-grid transform it just built from the acquisition's own coordinate
reference agrees with the acquired grid's own horizontal reference; a mismatch is treated as a processing
failure rather than silently proceeding, though it is not expected to occur in practice, since both are
built from the identical coordinate-reference text the same acquisition returned.

`verify`'s own exit behavior is a closed set — `0`, `2`, `5`, or `130` — because it performs no network I/O
and no simplification, but, like every command, can still be cancelled; see "Exit codes and error classes"
for why its `2`/`5` meanings are the reverse of every other command's file-reading convention.

## Options and defaults

Hand-rolled parsing over a fixed option table, rather than a parsing package, follows directly from
`AGENTS.md`'s preference for the standard library on well-bounded problems: the option set is around twenty
fixed flags across one verb level, help text needs to be checked byte-for-byte in tests, and a parsing
package would add a new pinned dependency and lock-file upkeep for a problem this table already solves
without one. `Options/OptionTable.cs` is the single source of truth for both parsing and the generated
`--help` text, so the two can never disagree with each other.

Every option below is parsed with `CultureInfo.InvariantCulture`; `--name value` and `--name=value` are both
accepted; an option not listed for a command is rejected (`unknown option '--x' for command '<verb>'.`); a
required option that is missing is rejected (`--x is required for command '<verb>'.`); and giving the same
option twice is rejected (`--x was specified more than once.`).

### `process`

| Option | Syntax | Requirement | Default | Validation |
| --- | --- | --- | --- | --- |
| `--asc` | `<file>` | required | — | must exist and be readable |
| `--prj` | `<file>` | optional | sibling `<asc-stem>.prj` | must exist and be readable; content must parse as WKT |
| `--source-json` | `<file>` | optional | sibling `<asc-stem>.source.json`, used only if it exists | if given explicitly, must exist; if defaulted, silently absent is fine |
| `--source-name` | `<text>` | optional | sidecar value, else `local-file` | non-blank |
| `--dataset` | `<text>` | optional | sidecar value, else the `.asc` file's own name without extension | non-blank |
| `--vertical-datum` | `<text>` | optional | sidecar value, else the `.prj`'s own compound vertical datum | non-blank |
| `--vertical-unit` | `us-survey-foot` or `international-foot` or `meter` | optional | sidecar value, else the `.prj`'s own compound vertical unit | one of the three values |
| `--geoid` | `<text>` | optional | sidecar value, else null | non-blank if given |
| `--collection-start` | `<yyyy-MM-dd>` | optional | sidecar value, else null | exact `yyyy-MM-dd`, `DateOnly.ParseExact` plus invariant culture; requires `--collection-end` |
| `--collection-end` | `<yyyy-MM-dd>` | optional | sidecar value, else null | same exact-format parsing; `>= --collection-start`; requires `--collection-start` |
| `--quality-level` | `<text>` | optional | sidecar value, else null | non-blank if given |
| `--bbox` | `<west>,<south>,<east>,<north>` | optional; at most one AOI form | none (whole grid) | 4 finite doubles; `west < east`; `south < north`; each within the WGS 84 range |
| `--center` | `<lat>,<lon>` | optional; requires `--radius` | — | 2 finite doubles in range |
| `--radius` | `<meters>` | optional; requires `--center` | — | finite, `> 0` |
| `--parcel` | `<file>` | optional; at most one AOI form | — | must exist and be readable |
| `--parcel-format` | `geojson` or `wkt` | optional | inferred from `--parcel`'s extension | one of the two values; required when the extension is not `.geojson`/`.json`/`.wkt` |
| `--buffer` | `<meters>` | optional | `0` | finite, `>= 0`; only with `--parcel` |
| `--origin` | `southwest` or `centroid` or `<x>,<y>` or `<x>,<y>,<z>` | optional | `southwest` | one of the two keywords, or 2-3 finite doubles |
| `--unit` | `us-survey-foot` or `international-foot` or `meter` | optional | `LengthConverter.DefaultOutputUnit` (`us-survey-foot` today) | one of the three values |
| `--method` | `curvature-aware` or `uniform` | optional | `curvature-aware` | one of the two values |
| `--budget` | `<int>` | optional | `15000` | integer, `> 0` |
| `--coverage-floor` | `<0..1>` | optional | `0.2` | finite, in `[0,1]` |
| `--output` | `<dir>` | required | — | non-blank |
| `--name` | `<baseName>` | optional | `terrain` | non-empty, no whitespace, `[A-Za-z0-9._-]` only, cannot start with `.`/`-`, cannot already end with `.solidground.json`/`.points.csv` |
| `--overwrite` | flag | optional | off | — |
| `--verbose` | flag | optional | off | — |

### `fetch`

| Option | Syntax | Requirement | Default | Validation |
| --- | --- | --- | --- | --- |
| `--bbox` / `--center`+`--radius` / `--parcel`(+`--parcel-format`) | (as `process`) | exactly one AOI form is required | — | (as `process`) |
| `--buffer` | `<meters>` | optional | `0` | finite, `>= 0`; only with `--parcel` |
| `--output` | `<dir>` | required | — | non-blank |
| `--name` | `<baseName>` | optional | `terrain` | same rule as `process`; names the raster set's three files (`<name>.asc`, `.prj`, `.source.json`) |
| `--overwrite` | flag | optional | off | — |
| `--timeout` | `<seconds>` | optional | `300` | integer, `> 0`; the `HttpClient.Timeout` applied to each OpenTopography request separately (see "Update, Issue #21" below) |
| `--verbose` | flag | optional | off | — |

`fetch` rejects every `process`-only processing and metadata option (`--origin`, `--unit`, `--method`,
`--budget`, `--coverage-floor`, `--source-name`, `--dataset`, `--vertical-datum`, `--vertical-unit`,
`--geoid`, `--collection-start`, `--collection-end`, `--quality-level`) as an unknown option for that
command: `fetch` does no provenance assembly at all, so an accepted-but-ignored option would let a typo
silently no-op instead of failing loudly.

### `run`

| Option | Syntax | Requirement | Default | Validation |
| --- | --- | --- | --- | --- |
| `--bbox` / `--center`+`--radius` / `--parcel`(+`--parcel-format`) | (as `process`) | exactly one AOI form is required | — | (as `process`) |
| `--buffer` | `<meters>` | optional | `0` | finite, `>= 0`; only with `--parcel` |
| `--origin` | (as `process`) | optional | `southwest` | (as `process`) |
| `--unit` | (as `process`) | optional | `LengthConverter.DefaultOutputUnit` (`us-survey-foot` today) | (as `process`) |
| `--method` | (as `process`) | optional | `curvature-aware` | (as `process`) |
| `--budget` | `<int>` | optional | `15000` | integer, `> 0` |
| `--coverage-floor` | `<0..1>` | optional | `0.2` | finite, in `[0,1]` |
| `--collection-start` | `<yyyy-MM-dd>` | optional | null | (as `process`); requires `--collection-end` |
| `--collection-end` | `<yyyy-MM-dd>` | optional | null | (as `process`); requires `--collection-start` |
| `--quality-level` | `<text>` | optional | null | non-blank if given |
| `--output` | `<dir>` | required | — | non-blank |
| `--name` | `<baseName>` | optional | `terrain` | the export bundle's base name; with `--save-raster`, also the raster set's |
| `--overwrite` | flag | optional | off | — |
| `--timeout` | `<seconds>` | optional | `300` | integer, `> 0`; the `HttpClient.Timeout` applied to each OpenTopography request separately (see "Update, Issue #21" below) |
| `--save-raster` | flag | optional | off | also writes the raster set beside the export bundle |
| `--verbose` | flag | optional | off | — |

`run` rejects `--source-name`, `--dataset`, `--vertical-datum`, `--vertical-unit`, and `--geoid`: an online
acquisition's source identity and vertical reference are always fully supplied by the source itself, never
partially or conditionally, so letting an operator override any of them would risk silently contradicting
what the source actually returned. Only the three fields the source never reports at all —
`--collection-start`, `--collection-end`, and `--quality-level` — are legitimately open to an operator
assertion on `run`, exactly as they are on `process`.

### `verify`

| Option | Syntax | Requirement | Default | Validation |
| --- | --- | --- | --- | --- |
| `--document` | `<file>` | required | — | must end with `.solidground.json`; must exist and be readable |
| `--points` | `<file>` | optional | sibling `<document-stem-without-suffix>.points.csv` | must exist and be readable |
| `--verbose` | flag | optional | off | also prints the full forward and inverse coordinate-operation definition text |

### Global

| Option | Syntax | Requirement | Notes |
| --- | --- | --- | --- |
| `--help`, `-h` | flag | optional | shows help for the given command, or the whole table |
| `--version` | flag | optional; must be the only argument | prints the CLI's version and exits |
| `help [verb]` | — | — | `verb`, if given, must be one of `process`, `fetch`, `run`, `verify` |

`--method uniform` is a plain, non-terrain-aware sampler kept only for comparison against the default
`curvature-aware` method; the third method Core defines, `TinError`, throws at runtime and is never an
accepted CLI value. `--budget` caps the retained point count; `--coverage-floor` reserves that fraction of
the budget for spatial spread rather than pure curvature ranking, and is always printed in a run's own
console output because it is not recorded in the export document (see "Known limitations and follow-ups").
The CLI validates `--name` against the base-name rule itself, up front, before doing any work, and also
catches the equivalent `ArgumentException` Core's own renderer and exporter would otherwise throw.
`--overwrite` is likewise checked up front, against the export document's and points file's own paths (or,
for `fetch`/`run --save-raster`, the raster set's three paths), before any acquisition, clipping, or
simplification runs — an existing file without `--overwrite` is a usage error before any work begins, not a
partially completed run.

Giving more than one of `--bbox`, `--center`/`--radius`, or `--parcel`/`--parcel-format` is rejected
(`--bbox, --center/--radius, and --parcel are mutually exclusive; give at most one AOI form.`); giving
`--buffer` together with `--bbox` or `--center`/`--radius` is rejected (`--buffer only applies to
--parcel.`); giving `--center` without `--radius`, or the reverse, is rejected (`--center and --radius must
be given together.`); and `fetch`/`run` given no AOI form at all is rejected (`fetch requires exactly one of
--bbox, --center/--radius, or --parcel.`, worded the same way for `run`).

`--collection-start`/`--collection-end` are parsed with a fixed exact format —
`DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly
date)` — never the bare `DateOnly.Parse`/`TryParse(string, out _)` overloads, whose accepted separator,
component order, and calendar all default to `CultureInfo.CurrentCulture` when no `IFormatProvider` is
given. Every other numeric or enum option above is parsed the same invariant way (for example
`double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, ...)`); the date options get their
own callout here only because `DateOnly`'s culture-sensitive overloads are an easy trap the numeric options
do not share.

## AOI and clip derivation

All three AOI-accepting commands (`process`, `fetch`, `run`) turn whichever single AOI form the operator
gave into a Core clip region through one shared factory (`Processing/ClipRegionFactory.cs`), so the
bounding-box, radius, and parcel paths can never diverge between commands. A parcel is always given in WGS
84 — there is no option that accepts a projected parcel — so the CLI never needs a second, operator-supplied
coordinate reference for the parcel itself; the only coordinate reference it ever has to build from scratch
is the WGS 84 reference every Core AOI type already expects.

- **Bounding box.** The four corners are reprojected into the grid's own coordinate reference with the
  already-built WGS 84-to-grid transform, assembled into a closed polygon in well-known text, parsed back
  into a polygon region, and clipped with a zero buffer. `fetch`/`run` instead pass the raw WGS 84 box
  straight through as the fetch request's own bounding box; the reprojected-polygon path only runs for the
  clip itself.
- **Radius.** The WGS 84 center point is reprojected into the grid's own reference and clipped as a circle
  around that point, at the requested radius. The fetch request derives its own WGS 84 bounding envelope
  from the normalized radius AOI, independently of the clip circle.
- **Parcel.** The WGS 84 parcel text (GeoJSON or WKT) is normalized and buffered first, then the resulting
  polygon is reprojected into the grid's own reference before it clips anything; the buffer is applied
  during the AOI's own normalization, not after reprojection, so a buffered parcel's edges are not distorted
  by reprojecting an already-buffered shape.

Clipping itself always uses Core's own default clip options (cell-center coverage, cropped to the region's
own envelope) — the CLI does not expose a flag for either setting. An AOI that excludes every cell is a
source-quality error (see "Exit codes and error classes"), not a silently empty bundle.

For `run`, the AOI is normalized once, before acquisition, to build the fetch request's own WGS 84 envelope;
the same normalized AOI is reused afterward to build the post-acquisition clip region, rather than being
parsed and normalized a second time. `run` additionally checks, immediately after acquisition and before any
clip or simplification runs, that the coordinate reference the fetched grid actually reports agrees with the
coordinate reference the pre-fetch transform was built from — both are parsed from the identical acquisition
text, so they are expected to always agree, but the check exists so a disagreement fails loudly as a
processing error instead of clipping against the wrong reference silently.

## Local origin selection and its consequences

Core supplies a way to snap a candidate origin point to a whole source unit, but it never chooses one on its
own; choosing the origin is entirely this CLI's own responsibility, through `--origin` (default
`southwest`, `Processing/LocalOriginSelection.cs` and `Processing/LocalOriginFactory.cs`):

```text
southwest (default): the lower-left corner of the clipped grid's envelope, snapped to a whole source unit.
centroid: the center of the clipped grid's envelope, snapped to a whole source unit.
<x>,<y> or <x>,<y>,<z>: an explicit projected coordinate in the grid's own coordinate reference system and
source units, used exactly with no snapping; z defaults to 0.
```

Both `southwest` and `centroid` read the clipped grid's own cell-corner envelope through
`ElevationGrid.GetCornerEnvelope()` — the one member this issue adds to `Terrain/ElevationData.cs` — rather
than the CLI re-deriving `SouthwestAnchor`/`CellSizeX`/`CellSizeY`/`RowCount`/`ColumnCount`/
`AnchorConvention` itself. The envelope already accounts for the grid's own anchor convention: a
lower-left-corner anchor names a corner directly, so no shift applies, while a cell-center anchor names the
southwest-most cell's own center, so its corner sits half a cell further out in each axis — the same
distinction `GetCellCenter` already applies in the opposite direction. Keeping this arithmetic in Core, next
to the convention it depends on, means the CLI never carries a second copy of that convention that could
quietly drift from Core's own if the convention ever changed.

The consequences of any of the three choices are the same, and are stated in full in the CLI's own
`--origin` help text:

> the origin is subtracted from every coordinate before unit conversion, so exported points are relative to
> it; the bundle records the origin, CRS, datum, and unit so source coordinates can be reconstructed;
> parcels that must be placed together in one Revit model must share one explicit origin, because southwest
> and centroid differ per parcel; a non-zero z shifts every elevation by that amount and is recorded;
> snapping keeps the offset a whole number of source units.

The practical effect of the third sentence is the sharpest: `southwest` and `centroid` are both computed
from the *clipped* grid's own envelope, so two different parcels almost never land on the same origin even
if their AOIs overlap. Placing more than one parcel's toposolid into the same Revit model therefore requires
choosing one explicit `<x>,<y>[,<z>]` origin (in the shared projected reference and source units) and
passing that same value to every invocation, rather than relying on either automatic choice.

## Units

`--unit` (on `process` and `run`) sets the output unit every retained sample's `x`/`y`/`elevation` is
converted into before it is written to the points file; it never changes the local origin itself, which
always stays in the grid's own source units (see "Local origin selection and its consequences" and the
provenance design note's own reconstruction algorithm). The CLI's help text for `--unit`, reused unedited on
both commands, restates `LengthConverter.MetersPerUnit`'s three exact values (`Units/LengthUnit.cs`) rather
than a second, independently maintained copy of them:

```text
us-survey-foot: exactly 1200/3937 metres per foot; international-foot: exactly 0.3048 metres per foot;
meter: 1 metre; us-survey-foot (the default)
```

**Update, Issue #25 (2026-09-20):** `--unit`'s syntax and help text are no longer two more hardcoded copies
of `us-survey-foot`. `Options/LengthUnitTokens.cs` is now the CLI's single source of truth for the three
tokens: `LengthUnitTokens.Syntax` (the `|`-joined token list) and `LengthUnitTokens.DefaultToken`
(`TokenOf(LengthConverter.DefaultOutputUnit)`) build the `Unit` `OptionSpec` in `OptionTable.cs`.
`ProcessCommand` parses `--unit` and `--vertical-unit`, and `RunCommand` parses `--unit`, both through
`LengthUnitTokens.Parse` (moved, unchanged in behavior, from the former `ProcessCommand.ParseLengthUnitValue`).
Both commands default `--unit` to `LengthUnitTokens.DefaultToken` rather than a hardcoded string. The default
is `LengthConverter.DefaultOutputUnit` (`us-survey-foot` today); see
docs/architecture/coordinate-transformation-and-units.md's "`LengthConverter.DefaultOutputUnit`" section for
the Core side of this.

`--vertical-unit` (on `process` only) is a different option for a different purpose: it names the unit the
*source* vertical reference is already in — used only while resolving provenance metadata for an offline
raster that has no compound `.prj` and no sidecar of its own — and is never applied to the exported points.
An online acquisition's vertical reference, unit included, always comes from the source itself, so `run` and
`fetch` do not accept `--vertical-unit` at all.

Resolving the vertical reference for `process` follows one precedence per field — an explicit CLI option
first, then the `--source-json` sidecar, then a compound `.prj`'s own `VERT_CS` — and datum and unit must
either both resolve or neither may: a raster with no sidecar, no compound `.prj`, and neither
`--vertical-datum` nor `--vertical-unit` given is a usage error naming both options, rather than half
resolving a reference with a datum from one source and a unit silently defaulted from another.

## Secrets and key resolution

There is no `--api-key` option anywhere in the CLI. `fetch` and `run` resolve the OpenTopography key
themselves (`Secrets/CliOpenTopographyApiKeyProvider.cs`, `Secrets/UserSecretsFileLocator.cs`), before
constructing anything that could make an HTTP call, in a fixed two-source order:

1. `OPENTOPOGRAPHY_API_KEY` in the process environment, trimmed; a null, empty, or all-whitespace value
   after trimming counts as no key, the same rule Core's own environment-based provider already applies for
   every other caller.
2. Otherwise, the `solidground-cli` .NET user-secrets file, read directly with `System.Text.Json` (no
   configuration package): if `APPDATA` is set and non-empty,
   `<APPDATA>\Microsoft\UserSecrets\solidground-cli\secrets.json`; otherwise, if `HOME` is set and
   non-empty, `<HOME>/.microsoft/usersecrets/solidground-cli/secrets.json`; otherwise the same
   `.microsoft/usersecrets/solidground-cli/secrets.json` suffix under the operating system's own
   `ApplicationData` special folder, then its `UserProfile` special folder, then
   `DOTNET_USER_SECRETS_FALLBACK_DIR`, in that order. This mirrors the .NET SDK's own user-secrets path
   resolution exactly, so the CLI reads the identical file the `dotnet user-secrets` tool writes. Only a
   top-level string property named exactly `OPENTOPOGRAPHY_API_KEY` is read; a missing file, a missing
   property, or a `null` property value all count as no key; a non-string property value or unparsable JSON
   is a usage error naming the file path, never its content.

Reading the key from user secrets was always deferred to this CLI on purpose: user secrets need a
`UserSecretsId` and a configuration surface that has no place in the Revit-free Core assembly, so
`SolidGround.Cli.csproj` declares `<UserSecretsId>solidground-cli</UserSecretsId>` and composes its own
provider over the two sources above rather than Core exposing one.

A missing key on `fetch` or `run` is an authorization failure returned before any `HttpClient` is even
constructed, naming both sources and the exact command that configures one:

```text
error (authorization): No OpenTopography API key is configured. Set OPENTOPOGRAPHY_API_KEY in the process
environment, or run: dotnet user-secrets set OPENTOPOGRAPHY_API_KEY "<key>" --id solidground-cli
```

Every lookup goes through one injected environment-lookup delegate on `CliHost`; no test ever calls
`Environment.SetEnvironmentVariable` or reads a developer's real `%APPDATA%`/`~/.microsoft/usersecrets` —
see "Testing strategy".

## Raster set persistence

`fetch` writes three files under `--output`: `<name>.asc`, `<name>.prj`, and `<name>.source.json`. `run`
writes the same three only when `--save-raster` is given, alongside the export bundle it always writes.

`<name>.prj` is the acquisition's own coordinate-reference text, byte for byte — never re-serialized — so a
later `process` run against that raster set reprojects with the exact text the source returned.

`<name>.asc` is written by `SolidGround.Core.Rasters.AaiGridWriter`, the one new Core writer this issue adds
to sit next to the existing `AaiGridParser`. It writes the same six-line header `AaiGridParser` already
reads back (`ncols`, `nrows`, the anchor pair — `xllcorner`/`yllcorner` or `xllcenter`/`yllcenter`, matching
the grid's own anchor convention — `cellsize`, and `NODATA_value`), then one data row per grid row,
single-space-separated, every number formatted with `"R"` and `CultureInfo.InvariantCulture`. It writes a
bare `'\n'` after every header line and every data row — never `TextWriter.WriteLine`, whose `NewLine`
defaults to `Environment.NewLine` — so its output is LF-only regardless of the caller's own `TextWriter`
configuration. A grid whose `CellSizeX` and `CellSizeY` differ cannot be written at all, because the format
has only one `cellsize` value; and a grid containing a valid elevation that happens to equal the chosen
NODATA sentinel is rejected outright, rather than silently writing a value indistinguishable from missing
data.

The one real limitation `AaiGridWriter` cannot avoid: the Esri ASCII raster format itself has no field for
row order. The writer always emits rows in the grid's own stored order; reading that text back always
reconstructs a north-to-south grid (the only row order `AaiGridParser` ever produces), so a grid that was
already north-to-south round-trips cell for cell, but a south-to-north grid's row order is not something
this text format can carry through a write and a re-parse.

`<name>.source.json` is a CLI-owned document — nothing in Core retains the raw acquisition response text —
written by hand with `Utf8JsonWriter`, `Indented`, `IndentSize = 2`, and `NewLine` set to `"\n"` explicitly
(the same options shape the provenance design note's own renderer uses), UTF-8 with no byte-order mark, and
one trailing `\n` appended after the writer is disposed. Its properties are always written in this fixed
order:

This is the version 1 shape, superseded below; it stays documented per the provenance design note's
versioning convention (`docs/architecture/provenance-and-deterministic-exports.md`'s "Versioning and
compatibility policy").

```text
schema                          string   constant "solidground.raster-source"
schemaVersion                    int     constant 1
sourceName                        string
datasetIdentifier                  string
collectionPeriod                    object { start, end } as "yyyy-MM-dd" strings, or JSON null
qualityLevel                         string | null
vertical                               object
  datum                                 string
  unit                                   string  LengthUnit member name
  geoidModel                             string | null
acquisition                               object
  redactedRequestUri                       string
  statusCode                                int
  contentType                                string | null
  contentDispositionFileName                  string | null
  archiveEntryNames                            array of string
  referenceSource                               string  "PrjSidecar" | "AuxXmlSidecar"
  responseByteCount                              long
```

Every acquisition field in that document is already redacted by Core's own evidence type before the CLI ever
sees it (see "Diagnostics and redaction"); the CLI adds no further redaction and never writes a timestamp or
the key itself anywhere in this file. `process` reads the sidecar back (`Rasters/RasterSourceSidecarIo.cs`)
with the same strict convention the provenance bundle reader already established: an unrecognized property,
a missing expected property, a duplicate property, or the wrong `schema`/`schemaVersion` is a usage error
naming the file path, never a best-effort guess at an unrecognized shape. When present, the sidecar supplies
every source and vertical-reference field `process` needs by default; any of the corresponding CLI options
overrides the sidecar's own value for that one field.

**Update, Issue #21 (2026-09-19):** `RasterSourceSidecarIo.CurrentSchemaVersion` is now `2`. A version 1
sidecar — including one `fetch` itself wrote before this change — is rejected by `process`'s strict reader
with the same usage error it always raised for a wrong `schemaVersion`, naming the sidecar's own path; there
is no migration path from version 1 to version 2. This is a breaking change for any raster set `fetch` wrote
before this update: the remedy is to re-run `fetch` against the same AOI to write a fresh version 2
`.source.json` (and `.asc`/`.prj`) before running `process` against it again, not to hand-edit the old
sidecar. The version 2 shape adds two top-level properties
immediately after `vertical`, and one property inside `acquisition`, immediately last, mirroring
`OpenTopographyResponseEvidence`'s own `HorizontalReferenceOrigin`, `VerticalReferenceOrigin`, and
`MetadataRequest` (SolidGround Issue #21's GeoTIFF-GeoKeys hybrid acquisition flow, documented in
`docs/architecture/opentopography-usgs1m-source.md`'s "Two-request contract, verified 2026-09-19" section):

```text
schema                          string   constant "solidground.raster-source"
schemaVersion                    int     constant 2
sourceName                        string
datasetIdentifier                  string
collectionPeriod                    object { start, end } as "yyyy-MM-dd" strings, or JSON null
qualityLevel                         string | null
vertical                               object  { datum, unit, geoidModel } -- unchanged from version 1
horizontalReferenceOrigin               string  ReferenceOrigin member name
verticalReferenceOrigin                  string  ReferenceOrigin member name
acquisition                               object
  redactedRequestUri                       string
  statusCode                                int
  contentType                                string | null
  contentDispositionFileName                  string | null
  archiveEntryNames                            array of string
  referenceSource                               string  "PrjSidecar" | "AuxXmlSidecar" | "GeoTiffGeoKeys"
  responseByteCount                              long
  metadataRequest                                 object | null
    redactedRequestUri                             string
    statusCode                                      int
    contentType                                      string | null
    contentDispositionFileName                        string | null
    responseByteCount                                  long
    projectedCoordinateSystemCode                       int
    citation                                             string | null
    rasterType                                            string  "PixelIsArea" | "PixelIsPoint"
    imageWidth                                             long
    imageLength                                             long
```

`horizontalReferenceOrigin`/`verticalReferenceOrigin` and `acquisition.metadataRequest` are all redacted (or
absent) exactly like every other acquisition field: `metadataRequest` is `null` for every single-request
acquisition (a zip response with its own `.prj`/`.aux.xml`) and present only for the hybrid GeoTIFF-GeoKeys
flow, where it also drives the `--verbose` metadata lines (see "Diagnostics and redaction").

**Update, Issue #21 (2026-09-19):** `fetch`/`run` sets `HttpClient.Timeout` from `--timeout` once, but the
hybrid GeoTIFF-GeoKeys flow sends that same `HttpClient` two sequential `GET` requests for one acquisition
(`docs/architecture/opentopography-usgs1m-source.md`'s "Two-request contract, verified 2026-09-19" section);
`--timeout` bounds each of those requests independently, not their sum, so a hybrid acquisition's data
request and metadata request may each individually take up to `--timeout` seconds before the whole
acquisition fails or succeeds. Because that flow only runs when the data response carries no reference
metadata of its own — the observed USGS 1 m behaviour — such an acquisition also costs two calls against the
configured API key's daily quota rather than one; a response that already carries its own `.prj`/`.aux.xml`
sidecar still costs only one.

## Exit codes and error classes

```text
0    success
1    unexpected internal error   (ex.Message on stderr; the full stack trace follows only with --verbose)
2    usage / validation
3    authorization
4    source quality
5    processing
130  cancelled
```

Every error line is exactly `error (<class>): <message>` on stderr — never a stack trace by default. Each
exception type Core or the CLI can raise maps to exactly one of the codes above, in this order (the first
match wins):

| Exception | Exit | Class |
| --- | --- | --- |
| `OperationCanceledException` (including `TaskCanceledException`) | 130 | `cancelled` |
| `CliUsageException` | 2 | `usage` |
| `OpenTopographyAuthorizationException` | 3 | `authorization` |
| `OpenTopographyRequestValidationException` | 2 | `usage` |
| Any other `OpenTopographyException` (quota, no-data, server, network, unexpected-response, or source-metadata) | 4 | `source-quality` |
| `ParcelGeometryException` | 2 | `usage` |
| `HorizontalCoordinateTransformException` | 2 | `usage` |
| `AoiNormalizationException` | 2 | `usage` |
| `GridClipException` | 4 | `source-quality` |
| `TerrainSimplificationException` | 5 | `processing` |
| `TerrainProvenanceException` | 4 | `source-quality` |
| `TerrainExportException` | 5 | `processing` |
| `CliProcessingException` (the CLI's own — a `run`/`fetch` coordinate-reference mismatch, or an `IOException`/`UnauthorizedAccessException` writing the raster set) | 5 | `processing` |
| A bare `FormatException` reaching the CLI unguarded | 2 | `usage` |
| A bare `ArgumentException`/`ArgumentOutOfRangeException`/`ArgumentNullException` (defensive — every option is already validated as a `CliUsageException` before this could fire) | 2 | `usage` |
| Anything else | 1 | `unexpected` |

The `cancelled` message is always the fixed text `the operation was cancelled.`, never an inner exception's
own `Message` — a caller-attached handler could otherwise embed request text there, which this CLI never
trusts verbatim from an inner exception, the same caution Core's own OpenTopography source already applies
to itself. The cancellation token `CliApplication.RunAsync` receives flows into every Core call completely
unchanged: the CLI never wraps it in a linked or derived token source of its own, so a caller-cancelled token
is the identical token object Core's own cancellation checks observe. `Program.cs` owns the only
`CancellationTokenSource` this executable ever creates, cancelled from `Console.CancelKeyPress`.

Two commands override the table above inside their own `try`/`catch`, before an exception can reach it:

- **`verify`** catches `TerrainExportException` itself and returns `2` ("invalid bundle"), not `5`; and it
  catches `IOException`/`UnauthorizedAccessException`/`FileNotFoundException`/`DirectoryNotFoundException`
  raised while reading `--document`/`--points` and returns `5` ("I/O"), not `2` — the reverse of the next
  bullet's convention. Because `verify` performs no network I/O and no simplification but, like every
  command, can still be cancelled, its own exit codes are the closed set `{0, 2, 5, 130}`.
- **`process`/`run`** wrap every read of an operator-supplied path (`--asc`, `--prj`, `--parcel`,
  `--source-json`) and turn an `IOException`/`UnauthorizedAccessException`/`FileNotFoundException`/
  `DirectoryNotFoundException` into a `CliUsageException` naming the option and path — an unreadable input
  file is a usage mistake for these two commands, the opposite of `verify`'s own rule for its bundle files.

One classification is a known, accepted imprecision: a `HorizontalCoordinateTransformException` on the
`fetch`/`run` parcel-reprojection path is built from the acquisition's own coordinate-reference text, not
from anything the operator typed directly, yet it is still classified as `usage` (2) for consistency with
every other coordinate-reference parse failure. This is expected to be unreachable in practice, because
Core's own acquisition path already validates that text before this transform is built a second time from
it.

## Diagnostics and redaction

By default, every command prints one line per stage to stdout and nothing else unless something fails.
`--verbose` adds full diagnostics: the acquisition evidence (`fetch`/`run`), clip statistics (input and
output row/column counts, retained count, source-NODATA count, and region-excluded count), the full
simplification diagnostics, and the provenance summary; `verify --verbose` additionally prints the forward
and inverse coordinate-operation definition text. Every field any of this prints is already redacted by
Core's own acquisition-evidence type before the CLI ever receives it — the CLI performs no redaction of its
own and never calls `.ToString()` on the key itself, so the same guarantee that the key never reaches a log
line, an exception message, or a URL applies to every `--verbose` output this CLI produces, not only to the
non-verbose path. `--coverage-floor` is the one processing input never recorded in the export document
itself (see "Known limitations and follow-ups"), so the CLI always prints it, verbose or not, as the only
record of which value produced a given run's result.

**Update, Issue #24 (2026-09-20):** the verbose `simplification diagnostics:` line gained one more token,
`retained-every-candidate True|False`, printed immediately after `interior-exhausted {bool}` and rendered from
`SimplificationDiagnostics.RetainedEveryCandidate`. It is `True` whenever the retained count equals the
candidate count — because `GridTerrainSimplifier`'s retain-all branch ran (candidate count at or below the
budget) or because `SimplifyUniform`'s own candidate-count-at-or-below-budget check retained everything — so the
console line never implies a curvature or coverage-floor selection dropped something when nothing was actually
dropped. See `docs/architecture/terrain-aware-decimation.md`'s "Budget invariant" section.

**Update, Issue #21 (2026-09-19):** `fetch`, `run`, and `process` now print one additional non-verbose line —
`{verb}: horizontal reference {crs} from {origin description}; vertical reference {datum} ({unit}) from
{origin description}.` — immediately after the acquisition stage line and before any "wrote" line for
`fetch`/`run`, or after the read/clip stage for `process`; the four origin descriptions are "the response
sidecar", "the GeoTIFF GeoKeys of the metadata request", "dataset documentation", and "the operator",
matching `SolidGround.Core.Metadata.ReferenceOrigin`'s four members one for one. In `--verbose` mode,
`fetch`/`run` print six further lines, in this order, immediately after the acquisition evidence block, but
only when the acquisition actually made a second, metadata-only request (see "Raster set persistence"'s
version 2 sidecar update); a single-request acquisition (a zip response with its own `.prj`/`.aux.xml`)
prints none of these six lines:

```text
{verb}: metadata request uri: '{redacted metadata request uri}'.
{verb}: metadata status: {status code} ({status code name}).
{verb}: metadata content type: '{content type or (none)}'.
{verb}: metadata content-disposition file name: '{file name or (none)}'.
{verb}: metadata response bytes: {byte count}.
{verb}: metadata geokeys: EPSG:{projected coordinate system code} "{citation or (none)}" {PixelIsArea|PixelIsPoint} {image width}x{image length}.
```

Every error, at any verbosity, is exactly one line: `error (<class>): <message>`. The one exception is the
`unexpected` class, where the full exception text follows the one-line message on later lines, and only when
`--verbose` was given — a plain run never spills a stack trace to the console.

The same discipline applies to usage errors specifically: a usage error names the option and the expected
syntax, never a supplied value or positional token, though a known option's own name may still be echoed
(from the option table, or, bounded to 64 characters, from an unrecognized `--...` token typed by the
operator).

## Determinism

Running `process` (or `run`, against the same acquired grid) twice against the same inputs, in the same
process or two different ones on the same machine, produces byte-identical `*.solidground.json` and
`*.points.csv` output, including under a non-invariant current culture such as `de-DE`: every number either
the pipeline or the CLI formats goes through `CultureInfo.InvariantCulture` explicitly, and both writers set
their JSON `NewLine` to `"\n"` rather than the platform-dependent `Environment.NewLine` default. This is the
same guarantee the provenance design note already proves for `TerrainExportBundleRenderer.Render` over an
already-built payload, extended here to a real, same-machine, end-to-end run of the whole CLI pipeline
across a real parcel or radius AOI.

`AaiGridWriter`'s output and the raster-set sidecar's output both carry the stronger, platform-independent
guarantee the provenance note reserves for `Render` alone: for a fixed `ElevationGrid` or a fixed
`RasterSourceSidecar` value, either writer produces identical bytes on any machine, because both perform
only fixed-order property writes, `"R"`-format or `Utf8JsonWriter` number formatting, and no reflection,
hash-set iteration, or locale-sensitive formatting of any kind.

What is not proven across machines is the same thing the provenance note already documents for Core:
building the *inputs* to a real parcel or radius payload from scratch — reprojecting a polygon, buffering it
— relies on transcendental functions whose last bit can differ between the Windows workstation's C runtime
and the Linux runner's C library. A full `process`/`run` invocation against a real parcel is therefore proven
deterministic only when run twice on one machine, exactly like the pipeline it wraps; reproducing identical
bytes on a second machine is expected, not proven.

**Update, Issue #10 (2026-09-19):** see "Determinism rules" in `docs/architecture/provenance-and-deterministic-exports.md` for the clean-checkout line-ending finding (a fresh Windows clone with `core.autocrlf=true` failed the golden export test) and its fix.

## Testing strategy

CLI tests live in the existing `SolidGround.Tests` project, reached through a new project reference to
`SolidGround.Cli`, rather than a second test project: the existing `dotnet test` step in continuous
integration already runs that one project, so no workflow change is needed for these tests to run, and the
existing architecture tests can inspect the CLI assembly from the same place they already inspect Core's.
`src/SolidGround.Cli/InternalsVisibleTo.cs`'s `[assembly: InternalsVisibleTo("SolidGround.Tests")]` —
the CLI's first use of this attribute — additionally grants `SolidGround.Tests` access to internal CLI
members (for example `Options.LengthUnitTokens`), letting `LengthUnitTokensTests` exercise such CLI-only
helpers directly instead of only indirectly through `CliApplication.RunAsync`'s stdout/stderr.

Every CLI test calls the public in-process entry point, `CliApplication.RunAsync(string[] args, CliHost
host, CancellationToken cancellationToken)`, directly — no test shells out to a built executable. The one
exception is `LengthUnitTokensTests`, which exercises the internal `SolidGround.Cli.Options.LengthUnitTokens`
helper's `TokenOf`/`Parse`/`DefaultToken` directly, enabled by the `InternalsVisibleTo("SolidGround.Tests")`
grant in `src/SolidGround.Cli/InternalsVisibleTo.cs`, rather than through `RunAsync`. `Program.cs`
is the only caller that builds a real `CliHost` from the actual process environment, the actual network, and
`Console.Out`/`Console.Error`; every test builds its own `CliHost` from an in-memory environment lookup (a
plain `Dictionary<string, string?>`, never `Environment.SetEnvironmentVariable`), `FakeHttpMessageHandler`
(reused from the existing OpenTopography source tests, along with its zip-archive-building helper) as the
handler factory, and `StringWriter`s for stdout/stderr that the test then inspects directly.

Because no test ever mutates the real process environment, a secrets-resolution test that wants `APPDATA` or
`HOME` to point somewhere controlled simply has its `CliHost`'s environment-lookup delegate return that
value for those two names — never reading, and never risking a collision with, the developer's or the CI
runner's actual home directory or real secrets file. Every fixture path uses the repository's existing
`AppContext.BaseDirectory`-relative convention, and every test needing a real directory on disk creates its
own with `Directory.CreateTempSubdirectory()` and deletes it afterward.

Coverage spans, at minimum: every invalid-option class (missing, unknown, duplicate, and out-of-range
options; conflicting AOI forms; and both process-only and online-only rejections) exiting `2` with no output
files written; a full offline `process` run against the committed fixture producing a bundle
`TerrainExportBundleReader` round-trips; a missing key on `fetch` and `run` exiting `3` without the HTTP
handler ever being invoked; a scripted `401` response exiting `3`; a scripted successful zip response
exercising `fetch`'s raster set and `run`'s bundle (and, with `--save-raster`, both); `verify` against both a
valid and a deliberately tampered bundle; the exact help-text substrings this note quotes above;
byte-identical output across two runs of the same inputs, including under `de-DE`; and cancellation on both
the offline path (a token already cancelled before `RunAsync` is called, proving nothing is written) and the
online path (the ambient token cancelled while a fake request is in flight, proving the same token — never a
derived one — is what Core itself observes).

One addition to the existing architecture tests independently confirms the assembly this issue builds
references no Revit assembly and no package beyond `SolidGround.Core`'s own, so the no-new-package rule
above is enforced by a test, not only by review.

## Known limitations and follow-ups

- **No record of which provenance fields were operator-asserted versus source-reported, for `collectionPeriod`/`qualityLevel`.**
  `--collection-start`/`--collection-end`/`--quality-level` can fill in fields the OpenTopography endpoint
  never reports, but the export document's schema has no field recording that the value came from the
  operator rather than the source. **Update, Issue #21 (2026-09-19):** schema version 2 resolved this for the
  *horizontal and vertical reference* fields specifically — `provenance.sourceHorizontalReferenceOrigin` and
  `.sourceVerticalReferenceOrigin` now record exactly this distinction for those two fields (see "Raster set
  persistence" and `docs/architecture/provenance-and-deterministic-exports.md`'s "Export document manifest,
  schema version 2" section) — but `collectionPeriod` and `qualityLevel` still carry no such marker. A later
  schema version, adding an explicit origin marker for those two fields, is still needed before a reader can
  tell an operator-asserted collection period or quality level apart from a source-reported one.
- **Only 40 NAD83-family UTM zone codes (10N-19N under NAD83, NAD83(HARN), NAD83(NSRS2007), and NAD83(2011))
  are supported for the GeoTIFF-GeoKeys hybrid flow.** **Update, Issue #22 (2026-09-20):** this widens the
  single NAD83-only range this bullet originally named; the scope is now four verified realizations. Zones
  10N-18N lie entirely within the conterminous United States; zone 19N also reaches Puerto Rico, so
  `OpenTopographyUsgs1mSource.ValidateGeoTiffMetadata` additionally rejects a zone 19N result whose request
  bounding box lies entirely south of `MinimumConusLatitudeForZone19N` (24.5 degrees north, south of the
  Florida Keys) rather than assume there. This support is still bounded to the conterminous United States
  because SolidGround has verified only the CONUS zones, and USGS's own Lidar Base Specification ties the
  declared NAVD88 vertical reference to CONUS specifically — Alaska, Hawaii, Puerto Rico, the Virgin Islands,
  and the territories use a per-project or local vertical datum instead (see
  `docs/architecture/opentopography-usgs1m-source.md`'s "GeoKey to WKT synthesis", "CONUS scope and its
  rationale", and "Declared vertical reference" sections). A bare AAIGrid response whose GeoTIFF metadata
  names any other EPSG family or zone — a non-CONUS NAD83 zone such as Alaska's zone 1N, a state plane
  system, or a non-UTM projection — still fails with `OpenTopographySourceMetadataException` naming the
  observed code rather than being handled. Supporting additional EPSG families or non-CONUS zones remains
  follow-up work, not part of this issue.
- **No minimum-area padding.** OpenTopography rejects a request below an empirically observed, undocumented
  per-request area minimum with HTTP 400 (exit code 2, usage) rather than a source-quality failure
  (`docs/architecture/opentopography-usgs1m-source.md`'s "Fail rather than assume" section). Neither `fetch`
  nor `run` pads a too-small AOI up to that minimum before sending the request; an operator whose AOI happens
  to fall below the threshold must enlarge it themselves. Automatic padding is follow-up work.
- **`coverageFloorFraction` is not recorded in the export document.** The document's `simplification` object
  carries only the point budget and the method name; the coverage floor that produced a given
  curvature-aware result exists only in that one run's own console output (always printed, per "Diagnostics
  and redaction"), never in the file itself.
- **Writes are not atomic.** Neither Core's own bundle exporter nor this CLI's raster-set writer uses a
  temp-file-then-rename step; both write directly to the final path. A cancellation, crash, or host shutdown
  during a write, rather than before one begins, can leave a truncated `.points.csv`, `.solidground.json`,
  `.asc`, or `.source.json` behind, and a later run has no way to distinguish a complete prior file from a
  truncated one by its mere existence.
- **The committed synthetic fixture stays small and hand-inspected.** `AaiGridWriter` makes generating a
  larger, more varied fixture straightforward; building one is future work, not part of this issue.
- **No client-side rate-limit throttling.** `fetch` and `run` each issue exactly one OpenTopography request
  per invocation and neither paces nor retries around the service's own quota. A quota response surfaces as
  an ordinary source-quality error like any other non-authorization OpenTopography failure; running many
  invocations in a short window without regard for the service's own limits remains the operator's own
  responsibility.
