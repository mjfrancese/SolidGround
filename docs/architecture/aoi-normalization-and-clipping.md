# AOI normalization and parcel clipping

Issue #5 implements `SolidGround.Core.Aois.AoiNormalizer` (turning any `AreaOfInterest` into a WGS 84 fetch
envelope) and the new `SolidGround.Core.Clipping` namespace (turning a parsed parcel boundary into a clipped
`ElevationGrid`). It also lands the first geospatial package, `SolidGround.Core.Aois.ParcelGeometryParser`
(WKT and GeoJSON), and `PolygonalRegion`, the validated polygon/multipolygon type both subsystems share.

## Dependency decision, verified 2026-09-18

`src/SolidGround.Core/SolidGround.Core.csproj` adds exactly one `PackageReference`:
[NetTopologySuite 2.6.0](https://www.nuget.org/packages/NetTopologySuite/2.6.0) (BSD-3-Clause). The
netstandard2.1 asset .NET 10 restores has zero further dependencies, no `runtimes/` folder, and no
`DllImport` anywhere in the package — confirmed by inspecting the restored package under
`~/.nuget/packages/nettopologysuite/2.6.0` and the test output directory, and by
`ArchitectureTests.EveryDeployedAssemblyIsManagedAndNoNativeRuntimesDirectoryExists`, which loads every
deployed `.dll` with `AssemblyName.GetAssemblyName` (a native image throws `BadImageFormatException` instead
of returning a name) and asserts no `runtimes/` directory exists under the test output. AGENTS.md names
robust polygon validity, buffering, holes, multipolygons, and clipping as the reason NetTopologySuite is an
acceptable dependency; `IsValidOp`, `BufferOp` (via `Geometry.Buffer`), `PreparedGeometryFactory`, and the OGC
`WKTReader` are exactly those operations.

`NetTopologySuite.IO.GeoJSON` (and `NetTopologySuite.IO.GeoJSON4STJ`) were **not** added. An earlier draft of
this decision claimed a NuGet audit restore failure for the 4STJ package; that claim was wrong. A throwaway
`net10.0` restore showed the .NET 10 SDK prunes 4STJ's `System.Text.Json` 6.0.3 transitive dependency, so no
`NU1903` advisory fires. The real reasons neither package was added: both `NetTopologySuite.IO.*` packages
are netstandard2.0-only; the mainline `NetTopologySuite.IO.GeoJSON` package pulls in `Newtonsoft.Json`, which
AGENTS.md's Revit-process constraint (no native or unnecessarily heavy dependency graph in an isolated add-in
context) argues against pulling in only for parcel parsing; the 4STJ variant additionally pulls in
`NetTopologySuite.Features`, which SolidGround does not otherwise need; and SolidGround needs its own
validation layer regardless of which reader it started from — indexed feature/polygon/ring/position error
messages, the `crs` acceptance rule, and format-fixed axis-order rejection are SolidGround-specific behavior
neither package provides. A bounded GeoJSON polygon reader over the inbox `System.Text.Json.JsonDocument`
(`ParcelGeometryParser`'s private `ParseGeoJson*` methods) is smaller, has no new transitive dependency at
all, and follows AGENTS.md's preference for the standard library for small, well-bounded functions. This
reverses the `NetTopologySuite.IO.GeoJSON 4.0.0` candidate `docs/architecture/phase-1-contracts.md` recorded
in Issue #2; see the update to that file below.

Lock files: `dotnet restore SolidGround.slnx` (no `--locked-mode`) regenerated `packages.lock.json` for all
three projects — Core gains a direct `NetTopologySuite` entry, Cli and Tests each gain a `Transitive`
`NetTopologySuite` entry through their project reference to Core — and `dotnet restore SolidGround.slnx
--locked-mode` then verified the regenerated locks are self-consistent.

## Package-neutrality: one deliberate, scoped exception

`docs/architecture/phase-1-contracts.md` states Core's public contracts use only SolidGround types and BCL
collections. `PolygonalRegion.Geometry` is a deliberate, narrow exception to that rule for the AOI/clipping
subsystem only: it exposes `NetTopologySuite.Geometries.Geometry` directly, documented on the property itself
as "the deliberate seam a horizontal coordinate transform (Issue #6) or a clip operation reads and rebuilds
from." Every other member of `PolygonalRegion` — `Polygons` (`PolygonRings`/`Coordinate2D`), `Envelope`
(`PlanarEnvelope`), `Area`, `PolygonCount`, `HoleCount` — is package-neutral, and every other new public type
in this issue (`NormalizedAoi`, `AoiNormalizationOptions`, `ClipRegion`, `GridClipOptions`, `GridClipResult`)
is package-neutral too. `ClipRegion` and `GridClipper` accept and produce `PolygonalRegion`, never a bare NTS
type, so a caller that never inspects `PolygonalRegion.Geometry` directly never needs an `NetTopologySuite`
`using` at all. `IElevationSource`, `ElevationData`, and every other Phase 1 contract from Issue #2 remain
fully package-neutral; this exception is scoped to the one type whose entire job is to validate and hold a
geometry.

## Ellipsoid formulas and reference values

`Wgs84Ellipsoid` (`SolidGround.Core.Aois`) computes meters-per-degree factors from the WGS 84 defining
constants — semi-major axis `a = 6378137` m, inverse flattening `1/f = 298.257223563` — using the standard
meridional and prime-vertical radius-of-curvature formulas:

```text
e^2 = f(2-f)
N(phi) = a / sqrt(1 - e^2 sin^2(phi))        // prime-vertical radius of curvature
M(phi) = a(1-e^2) / (1 - e^2 sin^2(phi))^1.5 // meridional radius of curvature
metersPerDegreeLatitude(phi)  = (pi/180) * M(phi)
metersPerDegreeLongitude(phi) = (pi/180) * N(phi) * cos(phi)
```

At the reference parcel scenario's latitude, `[withheld]`°N, these evaluate to `metersPerDegreeLatitude ≈
[withheld]` m and `metersPerDegreeLongitude ≈ [withheld]` m (independently recomputed for this issue; the
often-quoted spherical approximations `110996`/`86960` are not used anywhere in this codebase).
`Wgs84EllipsoidTests` reimplements the same formula independently (a second, separate implementation in the
test file, not a call into `Wgs84Ellipsoid`) and asserts both factors are within `1e-9` relative of that
reimplementation and within `0.01%` of the two reference values above. `MetersPerDegreeLongitude` is
effectively zero at either pole — a degree of longitude spans no distance at a pole, which is mathematically
correct — but not a special case in the implementation and not bit-exact `0.0`: `cos(±90°)` evaluates to a
residual on the order of `1e-12` in IEEE-754 double precision (confirmed against the actual runtime), since
`Math.PI` is itself only an approximation of pi and `90 * (Math.PI / 180)` does not land on exactly `pi/2`.

## Envelope padding rule and its conservatism

`AoiNormalizer` pads a geographic envelope by a distance in meters using this rule: `dLon = pad /
MetersPerDegreeLongitude(latitude with the largest |lat| in the envelope)`, and `dLat = pad /
MetersPerDegreeLatitude(0)` — latitude 0 (the equator), unconditionally, regardless of where the envelope
itself sits. Both choices are conservative (over-cover, never under-cover), but for different reasons. A
degree of longitude spans fewer meters at higher |latitude|, so evaluating the longitude factor at the
envelope's most-poleward latitude yields the *largest* `dLon` the envelope could need anywhere within it —
the *most*-poleward latitude in a `[south, north]` range is always at `south` or `north`, since `|latitude|`
is convex and a convex function's maximum over a closed interval is always at an endpoint, so this factor is
still evaluated per envelope. A degree of latitude spans the fewest meters at the equator and is monotonically
larger at every other latitude (`Wgs84Ellipsoid.MetersPerDegreeLatitude` is monotonically increasing in
absolute latitude), so latitude 0 is that factor's global minimum over the *entire* `[-90, 90]` domain, not
merely over `[south, north]` — every latitude the padded edge could possibly cross while moving from `south`
to `paddedSouth` (or `north` to `paddedNorth`) has a meters-per-degree factor at least as large as the one
used to size `dLat`, so the true geodesic distance covered is always at least `pad`, unconditionally.

An earlier version of this method evaluated the latitude factor at the envelope's own least-poleward latitude
instead (the interior point 0 only when the envelope straddled or touched the equator, `south <= 0 <= north`;
otherwise whichever of `south`/`north` had the smaller absolute value) — conservative for a *small* pad, but
only a local approximation: as the pad grows, the padded edge moves measurably closer to (or past) the
equator, where the true meters-per-degree keeps shrinking below the value assumed at the envelope's own
latitude, so the fixed degree delta computed from that assumption covers slightly less than `pad` meters of
actual distance. Confirmed against the actual runtime at a representative ExampleSite-latitude envelope (`south
= [withheld]°`): a 50 km pad under-covered by about 1.9 m, and a 100 km pad by about 7.7 m — negligible at the
buffer and margin sizes (single meters) this application actually uses, but a real gap in the "always
over-covers" guarantee this design previously claimed unconditionally. Using the global minimum at latitude 0
in every case closes that gap exactly, at the cost of using a very slightly larger `dLat` than strictly
necessary for envelopes that never approach the equator — again irrelevant at this application's realistic
buffer and margin sizes (see the accuracy caveat below).

If the padded envelope would leave `[-180, 180]` longitude or `[-90, 90]` latitude, `AoiNormalizer` throws
`AoiNormalizationException` rather than silently clamping or wrapping across the antimeridian or a pole —
SolidGround does not support either.

Each `AreaOfInterest` form maps to one `FetchEnvelopeBasis`:

| Source | Basis | Padding |
| --- | --- | --- |
| `Wgs84BoundingBoxAoi` | `BoundingBox` | The box padded by `EnvelopeMargin` alone (`Margin` zero gives a value-equal box). |
| `Wgs84RadiusAoi` | `RadiusOnWgs84Ellipsoid` | `radius + margin`, both factors evaluated at the center latitude (not the envelope-extreme rule above, since a point has only one latitude). The resulting box under-covers a true geodesic circle by a residual below one centimeter for radii up to 5 km at mid latitudes; `EnvelopeMargin` absorbs larger cases or higher latitudes. |
| `ParcelGeometryAoi` (geographic reference) | `GeographicParcelEnvelope` | The parsed parcel's own envelope, padded by `buffer + margin` with the envelope-extreme rule. |
| `ParcelGeometryAoi` (projected reference) | `TransformedParcelEnvelope` | Every vertex of every ring (shell and holes, every polygon) transformed to WGS 84 via the caller-supplied `IHorizontalCoordinateTransform`, then the envelope of the transformed vertices padded by `buffer + margin`. |

For a geographic parcel, this fetch-envelope padding is **not** the geometric buffer: it exists only so the
fetched raster covers the buffered parcel before clipping runs, and the phase-1-contracts invariant "a buffer
is never applied to angular coordinates" still holds for the clip geometry itself — `GridClipper` is the only
place a geometric buffer is ever applied, and only after a parcel is in a projected reference (see below). A
parcel's declared datum is never altered by this padding; a non-WGS84 geographic datum such as NAD83 differs
from WGS 84 by roughly 1-2 m at CONUS latitudes, a residual `EnvelopeMargin` can absorb without SolidGround
needing a datum transform just to size a fetch request.

## Buffer semantics: fetch padding vs. a geometric buffer

Two different "buffer" concepts exist in this issue, deliberately kept apart:

- `AoiNormalizationOptions.EnvelopeMargin` and `ParcelGeometryAoi.Buffer`, when normalized, only ever pad
  `NormalizedAoi.FetchEnvelope` — a WGS 84 bounding box used to size a *request*. Neither ever touches a clip
  geometry.
- `ClipRegion.Buffer` is a real geometric buffer, applied by `GridClipper` to `ClipRegion.Region`'s geometry,
  and only once that geometry is in a projected reference with a linear unit (`GridClipper` rejects a
  positive buffer on a geographic reference with `GridClipException`, since a buffer in degrees is not a
  distance). `NormalizedAoi.Buffer` (which is `ParcelGeometryAoi.Buffer` for a parcel AOI) is the natural
  value to carry into a later `ClipRegion.Buffer` once the parcel is projected — Issue #5 does not perform
  that hookup itself, since projecting the parcel is Issue #6's `IHorizontalCoordinateTransform`.

When `ClipRegion.Buffer.Value` is exactly zero, `GridClipper` never calls `Geometry.Buffer` at all. NTS
documents `Geometry.Buffer(0)` as a topology "validify" operation — it can change vertex ordering or count
even for an already-valid input — not a no-op; a throwaway probe against the actual NetTopologySuite 2.6.0
runtime confirmed a simple valid square is not `EqualsExact` to its own `Buffer(0)` result. Calling it
unconditionally would make the *unbuffered* clip path silently rebuild geometry it never needed to touch, so
`GridClipper` special-cases zero and reuses `ClipRegion.Region` unchanged. A positive buffer is applied with
`Geometry.Buffer(distance, new BufferParameters(8, EndCapStyle.Round, JoinStyle.Round,
BufferParameters.DefaultMitreLimit))` — 8 line segments per quadrant of a rounded corner or circular cap, the
same approximation `ClipRegion.Circle` uses to build a circular region from `Point.Buffer`. The buffered
result is re-validated through `PolygonalRegion.FromGeometry` before `GridClipper` uses it.

`ClipRegion.FromRegion` and `ClipRegion.Circle` are this issue's seams for Issue #6: `FromRegion` wraps an
already-projected `PolygonalRegion` (for example, one `IHorizontalCoordinateTransform` produced) with a
buffer distance; `Circle` builds a circular region directly in a projected reference without going through
WKT/GeoJSON at all.

## Cell inclusion rules

`GridClipper` prepares the effective (possibly buffered) region's geometry once per call with
`PreparedGeometryFactory.Prepare`, then classifies every source cell:

- `GridCellInclusion.CellCenterCovered` (the default): a cell is included iff `prepared.Covers(pointAtCellCenter)`.
  `Covers` is boundary-inclusive — a cell center that lands exactly on the region's edge counts as included,
  confirmed against the actual NTS runtime (a point on a test square's boundary reported `Covers` and
  `Intersects` both `true`).
- `GridCellInclusion.CellFootprintIntersects`: a cell is included iff `prepared.Intersects(footprint)`, where
  `footprint` is the cell's full axis-aligned rectangle (`center ± half cell size` in each direction, built
  with `GeometryFactory.ToGeometry(Envelope)`). This is always a superset of `CellCenterCovered`'s inclusion
  for the same region (a cell's center point is part of its own footprint, so `Covers(center)` implies
  `Intersects(footprint)`), and is a *strict* superset whenever the region's boundary is not aligned with
  every cell's center or edge — for example an obliquely angled edge (any slope other than `0`, infinite, or
  `±1` on a unit grid) that clips a cell's footprint without reaching its center.

A horizontal or vertical edge, or one at exactly `±45°`, built entirely from grid-aligned half-integer
coordinates (as `ElevationGrid.GetCellCenter` produces for a `LowerLeftCorner`-anchored, unit-cell grid) is a
degenerate case worth naming: such an edge always re-crosses another cell's own center at the very next grid
step, so it can never clip a same-footprint-only cell without that neighbor's center already being on the
boundary too. `GridClipperTests` exercises this directly: a rectangle whose edges pass through a ring of cell
centers shows identical inclusion under both rules (the ring cells straddle the boundary and are included
either way), while a triangle with one oblique-sloped edge (built the same way, from
`ElevationGrid.GetCellCenter`) demonstrably grows the included set under `CellFootprintIntersects`.

## Crop offset rule and its tolerance

When `GridClipOptions.CropToRegionEnvelope` is `true` (the default), `GridClipper` finds the minimal
source-array row range `[rowStart, rowEnd]` and column range `[colStart, colEnd]` containing every included
cell, and slices both the elevation and status arrays to that window. The new grid's column anchor is always
`SouthwestAnchor.X + colStart * CellSizeX` — columns never reverse direction. The row anchor depends on
`GridRowOrder`, because `ElevationGrid.GetCellCenter` measures a `NorthToSouth` grid's row position from the
*bottom* using `RowCount - 1 - row`:

- `SouthToNorth`: `SouthwestAnchor.Y + rowStart * CellSizeY` — row indices already count up from the south,
  so the new anchor simply advances past the trimmed southern rows.
- `NorthToSouth`: `SouthwestAnchor.Y + (RowCount - 1 - rowEnd) * CellSizeY` — the new grid's own southernmost
  row is source row `rowEnd` (the largest row index kept, since row 0 is the *north* edge), so the anchor
  must be derived from that row's south-based position, not `rowStart`.

Both formulas were derived so that, algebraically, every retained cell's `GetCellCenter` result is unchanged
by cropping. In floating point they are not always bit-identical to reading the same cell from the
uncropped source grid — the anchor shift adds one extra addition/multiplication rounding step before
`GetCellCenter`'s own arithmetic runs again — so `GridClipperTests` asserts equality within `1e-6`, not exact
equality, and does so for both row orders and for an intentionally asymmetric window (a different trim on
every one of the four sides) so the general formula is exercised, not just a symmetric special case.

## Error taxonomy

| Exception | Base type | Thrown when |
| --- | --- | --- |
| `ParcelGeometryException` | `FormatException` | `ParcelGeometryParser` cannot parse WKT/GeoJSON text, or `PolygonalRegion.FromGeometry` rejects non-polygonal, empty, non-finite, out-of-geographic-range, or topologically invalid geometry. Messages carry a feature/polygon/ring/position index and a coordinate where available, never the complete input text. |
| `AoiNormalizationException` | `InvalidOperationException` | Padding an envelope would cross the antimeridian or a pole; or a projected-reference parcel was normalized with no `IHorizontalCoordinateTransform` supplied. |
| `GridClipException` | `InvalidOperationException` | A clip region's horizontal reference does not equal the grid's; a positive buffer was requested in a reference that is not projected with a linear unit; or the effective region covers no cell of the grid. |

`ParcelGeometryException` and `AoiNormalizationException` propagate through `AoiNormalizer.Normalize`
unchanged — normalizing a parcel AOI parses its geometry first, so a malformed parcel surfaces as
`ParcelGeometryException`, not a normalization-specific exception.

## Seams for later issues

- **Issue #6** (horizontal coordinate transforms): `PolygonalRegion.FromGeometry(Geometry, HorizontalReference)`
  accepts an already-reprojected geometry directly; `ClipRegion.FromRegion`/`ClipRegion.Circle` accept an
  already-projected `PolygonalRegion`; `AoiNormalizer.Normalize`'s `IHorizontalCoordinateTransform?
  parcelToWgs84` parameter is the only place this issue calls the interface, and only to size a fetch
  envelope — Issue #5 does not implement `IHorizontalCoordinateTransform` and does not reference ProjNet.
- **Issue #7** (simplification): `GridClipResult.GetCellStatuses()` (or `GetCellStatus(row, column)` for one
  cell) distinguishes `SourceNoData` (inside the region, but the source grid had no value) from
  `RegionExcluded` (outside the region), so a later simplifier can tell "nothing was ever observed here"
  apart from "this is outside the parcel" without re-deriving it from a `null` elevation alone.
- **Issue #8** (provenance): `GridClipResult` carries every count a `TerrainProvenance` clip-stage extension
  will need — `SourceCellCount`, `IncludedCellCount`, `RetainedElevationCount`, `NoDataCellsInsideRegion`,
  `ExcludedCellCount` — plus `EffectiveRegion` and `Inclusion`, and `NormalizedAoi` carries `Basis` and
  `EnvelopeMargin`.
- **Issue #9** (CLI wiring): every AOI form, buffer, and margin is an explicit, validated value
  (`LinearDistance`, never a bare `double`); `src/SolidGround.Cli/Program.cs` is untouched by this issue.

## `Wgs84RadiusAoi` now carries `LinearDistance`

`Wgs84RadiusAoi.Radius` replaced `double RadiusMeters` so every distance in the AOI contracts —
`Wgs84RadiusAoi.Radius`, `ParcelGeometryAoi.Buffer`, `ClipRegion.Buffer`, `AoiNormalizationOptions.EnvelopeMargin`
— is the same explicit, unit-carrying `LinearDistance` type, consistent with `LengthConverter` being the one
tested conversion component AGENTS.md requires. `OpenTopographyUsgs1mSource` still accepts only
`Wgs84BoundingBoxAoi` and is unaffected; its existing rejection test for a non-bounding-box AOI now
constructs its radius AOI with `LinearDistance.Meters(...)`.

## Fixtures

`tests/SolidGround.Tests/Fixtures/example-site-synthetic-parcel.geojson` and `.wkt` are the same synthetic
~[withheld] m² ([withheld] sq ft) rectangle — the [withheld] footprint — in two independent reference forms: WGS 84
degrees (built by converting a fixed meter half-width/half-height with `Wgs84Ellipsoid`'s factors at the
ExampleSite latitude) and NAD83 / UTM zone 15N meters (`EPSG:26915`, the reference `example-site-synthetic.prj`
already describes). The WKT fixture's easting is near `[withheld]`, not the `545000` figure an earlier draft of
this design suggested. That earlier `545000` draft was simply a plain error, not a deliberately different "plausible" example; the reasoning is withheld here to avoid disclosing the real site's location. Both fixtures are synthetic and illustrative, not a real survey, and (per
`FixtureSecurityTests`) contain no request URL, authorization header, or API credential.

## Accuracy caveat

Every number in this issue — the ellipsoid factors, the fetch-envelope padding, the 1 m grid cells, the
synthetic fixtures — supports SolidGround's stated purpose as a site-form tool, not a survey instrument. A
centimeter-scale radius under-coverage, a millimeter-scale crop-offset rounding difference, or an 8-segment
polygon approximation of a circle are all irrelevant at the 1 m native resolution of the USGS 1 m product
this repository targets, and none of them changes AGENTS.md's existing caveat that this surface is not
suitable for foundation-perimeter grading or construction layout.
