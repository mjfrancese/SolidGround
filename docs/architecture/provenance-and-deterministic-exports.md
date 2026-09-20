# Provenance and deterministic exports

Issue #8 implements `SolidGround.Core.Provenance.ElevationStatistics`, `SolidGround.Core.Provenance.TerrainExportPayloadAssembler`, `SolidGround.Core.Provenance.TerrainProvenanceException`, and a `TerrainProvenance.CurrentSchemaVersion` constant; and `SolidGround.Core.Exports.TerrainExportBundle`, `TerrainExportBundleRenderer`, `TerrainExportBundleReader`, `FileSystemTerrainExporter`, and `TerrainExportException`. It adds no package reference: the writer and reader use only `System.Text.Json`'s `Utf8JsonWriter`/`JsonDocument` from the BCL, and every numeric, date, and enum formatting rule is plain, culture-invariant arithmetic in the style `AaiGridParser` already established for reading. `TerrainProvenance`, `ElevationRange`, `TerrainExportPayload`, `ITerrainExporter`, `TerrainExportReceipt`, and `LocalTerrainSample` (Issue #2) are otherwise unchanged: their constructors, invariants, and property sets are exactly what Issue #2 shipped.

## Purpose and boundary

This design covers only `SolidGround.Core.Provenance` and `SolidGround.Core.Exports`: turning an already-clipped `ElevationGrid` and an already-computed `SimplificationResult` into a self-describing `TerrainExportPayload`, and turning that payload into a deterministic on-disk bundle, or back. Everything upstream of the candidate grid — acquisition, AAIGrid parsing, AOI normalization, clipping, coordinate/unit/local-origin transformation, and terrain-aware decimation — is unchanged and covered by the earlier design notes this one builds on.

Two things stay explicitly outside Core, both already fenced off before this issue started:

- CLI wiring (destination selection, argument parsing, printing a receipt or a provenance summary) is Issue #9's; `src/SolidGround.Cli/Program.cs` is untouched here.
- Attaching the resulting provenance to a Revit element via Extensible Storage is Phase 2's; this issue only makes sure the manifest is nested, not pre-flattened, so that later mapping has a structure to walk.

See "Boundary: what #9 and Phase 2 still own" for the fuller list.

## Decisions

**A bundle is always two files rendered together, never one alone.** `TerrainExportBundleRenderer.Render` returns one `TerrainExportBundle` holding both the export document's bytes and the points file's bytes, and `TerrainExportBundleReader.Read` takes both spans back in. Revit's toposolid points-file import fallback needs a bare, header-free `x,y,z` CSV — embedding the machine-readable record inline would break that fallback — so the JSON document instead carries the full provenance record and binds the CSV to itself by recording its exact sample count (`points.count`) and SHA-256 (`points.sha256`).

**The only serializer is the BCL's `System.Text.Json`, hand-driven, not reflected.** `RenderDocument` builds the document with a bare `Utf8JsonWriter`; `TerrainExportBundleReader` reads it back with `JsonDocument`. Nothing here uses a reflection-based `JsonSerializer` over the domain records: several of them (for example `TerrainProvenance.SourceHorizontalReference`) expose derived, non-stored properties that a generic serializer would either wrongly include or need extra attribution to exclude, and one (`HorizontalUnit`) has no public constructor at all, so a generic deserializer could not rebuild it. Writing and reading by hand also means the exact property set and order are decided once, in one place, and cannot silently drift the way reflection-driven output can when a property is later added to a record.

**The document shape mirrors the C# object graph; nothing is pre-flattened.** `provenance.localFrame.origin.x` is nested exactly as deep as `TerrainProvenance.LocalFrame.Origin.X` is nested in code. A Phase 2 Extensible Storage mapping (out of this issue's scope) can walk the same structure the JSON already has instead of having to un-flatten a table first.

**The reader is strict; `TerrainProvenance` itself stays permissive.** `TerrainProvenance`'s own constructor (Issue #2, unchanged by this issue) still allows `originalPointCount == 0` with `elevationRange: null` — that permissiveness is deliberate and matches `GridTerrainSimplifier`'s own precedent of returning a valid, empty `SimplificationResult` for an all-NODATA grid. The rejection of an empty valid-data set for an actual *export* happens one layer up, in `TerrainExportPayloadAssembler.Assemble` and, redundantly, in `TerrainExportBundleRenderer.Render` — see "NODATA, empty candidate sets, and statistics". `TerrainExportBundleReader`, in turn, rejects any deviation from the schema version 1 manifest by name and JSON path rather than guessing at an unrecognized shape — see "Versioning and compatibility policy".

**Sample order is the simplifier's order; nothing here re-sorts.** `TerrainExportPayloadAssembler.Assemble` maps `simplification.RetainedSamples` — already sorted `(Row asc, Column asc)`, i.e. north-to-south, west-to-east, as `GridTerrainSimplifier`'s explicit last step — straight into `LocalTerrainSample`s with a single `Select`. Nothing in this issue sorts, groups, or deduplicates a second time; the points file and the reconstructed payload both preserve exactly that order.

**A base name is validated once, by one shared rule.** `TerrainExportBaseName.Validate` (an internal helper co-located in `TerrainExportBundleRenderer.cs`) is called by both `TerrainExportBundleRenderer.Render` and the `FileSystemTerrainExporter` constructor, so the two can never disagree about a valid name: non-empty, no whitespace, only `[A-Za-z0-9._-]`, must not start with `.` or `-`, and must not already end with `.solidground.json` or `.points.csv`.

## Provenance record and AGENTS.md field coverage

AGENTS.md's "Provenance decision" requires preserving, at minimum, source dataset, collection date, quality level, horizontal datum, vertical datum, original and retained point counts, elevation minimum and maximum, output unit and foot definition, and a complete, reversible local-origin offset, plus an explicit schema version. Every field is present in the schema version 1 manifest (see the next section for the full document):

| AGENTS.md field | JSON path | C# source |
| --- | --- | --- |
| Source dataset | `provenance.source.sourceName`, `provenance.source.datasetIdentifier` | `ElevationSourceMetadata.SourceName` / `.DatasetIdentifier` |
| Collection date | `provenance.source.collectionPeriod` (`{start, end}` or `null`) | `ElevationSourceMetadata.CollectionPeriod` |
| Quality level | `provenance.source.qualityLevel` (or `null`) | `ElevationSourceMetadata.QualityLevel` |
| Horizontal datum | `provenance.horizontalTransformation.sourceReference.datum` (geographic) and `provenance.localFrame.projectedHorizontalReference.datum` (projected) | `HorizontalReference.Datum`, reachable via `TerrainProvenance.SourceHorizontalReference` and `.LocalFrame.ProjectedHorizontalReference`; `TerrainProvenance`'s constructor requires `HorizontalTransformation.TargetReference == LocalFrame.ProjectedHorizontalReference`, so the two projected-side copies can never disagree |
| Vertical datum | `provenance.sourceVerticalReference.datum` (== `provenance.localFrame.verticalReference.datum`) | `VerticalReference.Datum`; `TerrainProvenance`'s constructor requires `SourceVerticalReference == LocalFrame.VerticalReference` because no vertical datum transformation is performed |
| Original point count | `provenance.originalPointCount` | `TerrainProvenance.OriginalPointCount`, computed by `ElevationStatistics.CountValidCells(candidateGrid)` |
| Retained point count | `provenance.retainedPointCount` | `TerrainProvenance.RetainedPointCount`, equal to `samples.Length` and to `simplification.RetainedSamples.Count` |
| Elevation minimum/maximum | `provenance.elevationRange.minimum` / `.maximum` | `ElevationRange.Minimum` / `.Maximum`, computed by `ElevationStatistics.ComputeRange(candidateGrid)` |
| Output unit and foot definition | `provenance.localFrame.outputUnit` (also `points.unit`); the numeric definition itself in `unitDefinitions[].definition` / `.metersPerUnit` | `LocalCoordinateFrame.OutputUnit`; `TerrainExportBundleRenderer.UnitDefinitionText` / `LengthConverter.MetersPerUnit` |
| Complete, reversible local-origin offset | `provenance.localFrame.origin` (`{x, y, elevation}`) plus `provenance.horizontalTransformation.{forwardOperation, inverseOperation, engineName, engineVersion}` | `LocalCoordinateFrame.Origin` (in source units) and `HorizontalTransformationDefinition`; see "Reconstructing source coordinates" |
| Explicit schema version | `schemaVersion` (top-level) | `TerrainProvenance.SchemaVersion`, always `TerrainProvenance.CurrentSchemaVersion` (`1`) in a document this build writes |

`collectionPeriod` and `qualityLevel` are `null` for every export produced from the only implemented source today: `OpenTopographyUsgs1mSource` always builds `new ElevationSourceMetadata("OpenTopography", "USGS1m")` with both optional fields omitted, because the `usgsdem` endpoint reports neither in its response, and AGENTS.md's "fail rather than assume" rule forbids fabricating catalog values here. `ElevationSourceMetadata` (Issue #2) already models both fields as nullable for exactly this reason. An operator-asserted value for either field — with the assertion's own origin recorded, so a fabricated value is never indistinguishable from a source-reported one — is Issue #9 or later follow-up work, not this issue's.

## Export document manifest, schema version 1

`TerrainExportBundleRenderer.Render` writes, and `TerrainExportBundleReader` strictly reads, exactly this shape. The property order below is the order the writer actually emits (`RenderDocument`, `WriteProvenance`, and so on), which is each source record's own declared property order, skipping derived properties such as `TerrainProvenance.SourceHorizontalReference` (an `=>`-forwarding property, not a stored one). Every property is always written; a value that is logically absent is written as JSON `null`, never omitted.

```text
schema                        string   constant "solidground.terrain-export" (TerrainExportBundleRenderer.DocumentSchema)
schemaVersion                  int     TerrainProvenance.SchemaVersion (== TerrainProvenance.CurrentSchemaVersion, 1)
provenance                     object  TerrainProvenance
  source                        object  ElevationSourceMetadata
    sourceName                   string
    datasetIdentifier             string
    collectionPeriod              object | null   { start, end } as "yyyy-MM-dd" strings, or JSON null
    qualityLevel                   string | null
  horizontalTransformation       object  HorizontalTransformationDefinition
    sourceReference                object  HorizontalReference (shape below)
    targetReference                 object  HorizontalReference; equals localFrame.projectedHorizontalReference
    forwardOperation                 object  { format, definition } (CoordinateOperationDefinition)
    inverseOperation                  object  { format, definition }
    engineName                         string
    engineVersion                       string
  sourceVerticalReference            object  VerticalReference (shape below); equals localFrame.verticalReference
  localFrame                         object  LocalCoordinateFrame
    origin                            object  { x, y, elevation } -- see the source-unit note below
    projectedHorizontalReference      object  HorizontalReference (Kind is always "Projected")
    verticalReference                  object  VerticalReference
    outputUnit                         string  LengthUnit member name
  simplification                     object  SimplificationRequest
    pointBudget                        int
    method                              string  SimplificationMethod member name
  originalPointCount                   int
  retainedPointCount                    int
  elevationRange                       object  ElevationRange -- never null in schema version 1
    minimum                             number
    maximum                              number
    unit                                 string  LengthUnit member name
unitDefinitions                  array   one entry per DISTINCT LengthUnit used anywhere above, ascending by the
                                          enum's underlying int (Meter=0, UsSurveyFoot=1, InternationalFoot=2)
  [n].unit                        string  LengthUnit member name
  [n].metersPerUnit                number  LengthConverter.MetersPerUnit(unit)
  [n].definition                    string  "1 m" | "1200/3937 m" | "0.3048 m"
points                            object
  file                              string  the points file's own name only, no directory (bundles stay relocatable)
  format                             string  constant "csv"
  columns                            array   ["x", "y", "elevation"]
  unit                               string  localFrame.outputUnit's member name
  count                              int    == retainedPointCount
  sha256                             string  lowercase hex SHA-256 of the exact points file bytes
```

`HorizontalReference` objects (`sourceReference`, `targetReference`, `projectedHorizontalReference`) all share one shape, written by `WriteHorizontalReference`:

```text
coordinateReferenceSystem    string
datum                         string
kind                            string  HorizontalReferenceKind member name ("Geographic" | "Projected")
unit                             object  HorizontalUnit (shape below)
axisOrder                        string  HorizontalAxisOrder member name
```

`HorizontalUnit` has no public constructor of its own (only the `HorizontalUnit.DecimalDegrees` singleton and the `HorizontalUnit.Linear(LengthUnit)` factory), so it is written, by `WriteHorizontalUnit`, as its own two-property object rather than a bare string:

```text
referenceKind    string          HorizontalReferenceKind member name
linearUnit        string | null   LengthUnit member name when Projected, null when Geographic
```

`VerticalReference` objects (`sourceVerticalReference`, `localFrame.verticalReference`), written by `WriteVerticalReference`:

```text
datum        string
unit          string          LengthUnit member name
geoidModel     string | null
```

**The `localFrame.origin` object is in source units, not `outputUnit`.** `LocalCoordinateFrame.Origin` is a bare `Coordinate3D`: its `X`/`Y` are in whatever linear unit `projectedHorizontalReference.Unit.LinearUnit` names, and its `Elevation` is in `verticalReference.Unit` — never in `localFrame.outputUnit`. `WriteLocalFrame` writes `origin.x`/`.y`/`.elevation` straight from `LocalCoordinateFrame.Origin`'s own components, with no unit conversion. Only the retained *samples* (in `points.csv`, and `points.unit`) are in `outputUnit`. See "Reconstructing source coordinates" for what this means for a reader.

Numbers, dates, and enums follow fixed rules everywhere in the document: every `double` via `Utf8JsonWriter.WriteNumber(string, double)` (round-trippable and culture-invariant by construction — `Utf8JsonWriter` never consults `CultureInfo.CurrentCulture`); every `int` via `WriteNumber(string, int)`; every `DateOnly` via `.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)` (`WriteSource`); every enum via its bare `.ToString()`, i.e. the C# member name such as `"UsSurveyFoot"` or `"CurvatureAware"`, never a numeric value or a custom display string.

## Points file format, version 1

`TerrainExportBundleRenderer.RenderPoints` writes one line per retained sample, in `payload.Samples`' order — the simplifier's own row-major order, see "Decisions". There is no header row, no comments, no byte-order mark, and no blank lines: Revit's points-file toposolid import fallback expects bare comma-separated `x,y,z` lines, and a header line would break it.

Each line is `x,y,elevation` — `LocalTerrainSample.Position`'s three `LocalCoordinate` components, all three already expressed in `localFrame.OutputUnit` (a `LocalCoordinate` carries no unit tag of its own; its unit is whatever `LocalCoordinateFrame.ToLocal` last produced it in). Each number is rendered with `value.ToString("R", CultureInfo.InvariantCulture)`: the shortest round-trippable form, chosen so `TerrainExportBundleReader`'s `double.TryParse(field, NumberStyles.Float, CultureInfo.InvariantCulture, ...)` returns the identical bit pattern back — the same culture-invariant parsing precedent `AaiGridParser` already established for reading. Negative zero renders as `-0`; `RenderPoints` does not special-case it, so it is accepted and deterministic like any other value.

Every line, including the last, ends with a bare `\n` (`RenderPoints` appends `'\n'` per sample; it never uses `Environment.NewLine`). A payload with zero retained samples renders zero bytes, since `RenderPoints`'s loop simply does not run. The file is UTF-8 without a byte-order mark (`Encoding.UTF8.GetBytes` never writes an encoding preamble), which is safe because every character `"R"`-formatted invariant-culture output can contain (digits, `-`, `.`, `E`) is pure ASCII.

`TerrainExportBundleReader.Read` validates the points bytes against the document it was parsed with: the SHA-256 of the exact bytes must equal `points.sha256`; an empty span is accepted only when `points.count` is also `0`; otherwise the text must end with `\n`, split into exactly `points.count` lines, and every line must split into exactly three comma-separated fields, each parsing with `NumberStyles.Float` and `CultureInfo.InvariantCulture` to a finite `double`. Any deviation — a hash mismatch, a wrong line count, a malformed line — throws `TerrainExportException`; reconstructing each `LocalCoordinate`/`LocalTerrainSample` re-runs those types' own finite-value invariants (Issue #2), and any resulting `ArgumentException` is likewise wrapped as `TerrainExportException`.

## Determinism rules

`RenderDocument` constructs its `Utf8JsonWriter` with `new JsonWriterOptions { Indented = true, IndentCharacter = ' ', IndentSize = 2, NewLine = "\n" }`. `NewLine` is set explicitly and deliberately: `JsonWriterOptions.NewLine`'s default is `Environment.NewLine`, which is `"\r\n"` on the Windows development workstation and `"\n"` on the Linux `self-hosted` CI runner — leaving it at the default would make every indented line ending depend on which machine rendered the document. `Utf8JsonWriter` also never consults `CultureInfo.CurrentCulture` for any number it writes, so running under a non-invariant current culture cannot change a single byte either.

Property order is fixed by the manifest above and is never produced by enumerating a `Dictionary`, a `HashSet`, or reflection metadata. The one place a `HashSet<LengthUnit>` appears at all, `TerrainExportBundleRenderer.CollectDistinctUnitsInEnumOrder`, immediately re-sorts it with `.OrderBy(unit => (int)unit)` before anything is written, so the hash set's own unspecified iteration order never reaches the output bytes.

After the `Utf8JsonWriter` is flushed and disposed, `RenderDocument` appends exactly one `'\n'` byte directly to the underlying stream, so the document always ends with a single trailing newline; the document is UTF-8 with no byte-order mark. Combined with the points file rules above, no byte either rendered file ever contains is `0x0D`, and neither file carries a byte-order mark.

These guarantees are proven for `TerrainExportBundleRenderer.Render` itself: the same `TerrainExportPayload` value, rendered on any platform or under any thread culture, produces identical `DocumentBytes` and `PointsBytes`, because every operation `Render` performs — `Utf8JsonWriter` output, `"R"`-format double formatting, SHA-256 hashing — depends only on IEEE-754 arithmetic, managed number formatting, and a fixed hash algorithm, none of which consult locale or platform state. They are not, by themselves, a guarantee that two different machines building the *inputs* to a payload from scratch will reach the same payload: the horizontal transformation upstream of this issue (`ProjNetHorizontalCoordinateTransformFactory`, Issue #6) and parcel-region buffering (NetTopologySuite, Issue #5) both rely on transcendental functions whose last bit can differ between the Windows workstation's UCRT and the Linux `self-hosted` runner's glibc. A real end-to-end parcel workflow is therefore deterministic on one machine — running it twice on the same machine reproduces the same bytes — and is expected, but not proven, to reproduce identical bytes across machines. A golden-fixture comparison that spans that upstream reprojection/buffering is consequently a same-machine or committed-fixture comparison, not a cross-machine proof; only `Render` over an already-built, identical payload carries the stronger, platform-independent guarantee. Golden fixtures committed for this pipeline need an `eol=lf` `.gitattributes` rule for the fixture extensions involved (`*.json`, `*.csv`) so that a Windows checkout does not silently turn their committed `\n` bytes into `\r\n`, which would fail a byte-for-byte comparison for a reason that has nothing to do with the renderer itself.

**Update, Issue #10 (2026-09-19):** the rule this section states must be read broadly: every piece of text embedded verbatim in an export — a committed fixture sidecar such as `example-site-synthetic.prj`'s WKT, and Core's own definition constants such as `ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText` — must be byte-identical on every platform and every checkout setting, not merely the golden files themselves. A fresh clone on Windows with `core.autocrlf=true` at commit `783e0dd` failed the golden test above for exactly this reason on two fronts at once: `example-site-synthetic.prj` checked out with CRLF (the `.gitattributes` pin then covered only `*.json` and `*.csv`, not `*.prj`), and `Wgs84WellKnownText`, then a `public const string` multi-line raw string literal, inherited the CRLF line endings of its own already-checked-out source file. Both are now fixed at the source rather than papered over at comparison time: `.gitattributes` now pins every file under `tests/SolidGround.Tests/Fixtures/**` to `eol=lf`, and `Wgs84WellKnownText` is now a `public static readonly string` piped through `.ReplaceLineEndings("\n")` so its runtime value cannot depend on how its source file was checked out. Goldens must be platform-neutral by construction — by pinning every input's checkout line endings and by normalizing every embedded constant at the point of definition — rather than by hoping a particular workstation's or runner's checkout happens to agree with the committed bytes. See "The hardcoded WGS 84 constant and the NAD83↔WGS84 zero-datum-shift" in `docs/architecture/coordinate-transformation-and-units.md` for the constant-side fix and its two guard tests.

## Versioning and compatibility policy

`TerrainProvenance.CurrentSchemaVersion` (`= 1`) names the one manifest this build of SolidGround writes and the only one its reader accepts. Any change to the document shape — a property added, removed, renamed, or retyped; a nullability change; a property reordered; or a points-file column change — must increment this constant and add a new, separately documented manifest section here; the version 1 manifest above stays documented rather than being edited in place, matching the "Update, Issue #N" convention the other design notes in this repository use for later corrections.

The reader enforces this at the two points where a document declares what it is: `TerrainExportBundleReader.ParseDocument` rejects any `schema` that is not exactly `TerrainExportBundleRenderer.DocumentSchema` and any `schemaVersion` that is not exactly `TerrainProvenance.CurrentSchemaVersion`, in both cases with a `TerrainExportException` naming the actual and expected value. It never falls back to a best-effort parse of an unrecognized shape. Every other manifest property is equally strict: `ReadObjectProperties` rejects a missing property, an unrecognized property, and a duplicate property name in the same pass, and reports the offending JSON path in the exception message. Enum-valued properties (`kind`, `unit`, `method`, and so on) must match a defined member name exactly and case-sensitively: `RequireEnum` checks the JSON string against `Enum.GetNames<TEnum>()` with an ordinal, exact comparison before parsing, deliberately stricter than `Enum.TryParse` alone, which would also accept a defined member's numeric value as a string, a whitespace-padded name, or (since none of this manifest's enums carry `[Flags]`) a comma-joined name list; `unitDefinitions` entries must match the computed distinct-unit list bit-for-bit in `metersPerUnit` and exactly in `definition` text. None of this is negotiable for a version 1 document — a variation a future writer might consider harmless is rejected exactly like a genuinely corrupt document.

`TerrainExportBundleRenderer.Render` enforces the same version constraint from the write side: it throws `TerrainExportException` if `payload.Provenance.SchemaVersion != TerrainProvenance.CurrentSchemaVersion`, because this writer implements exactly one manifest and never guesses at how to render an older or newer one.

Phase 2's Extensible Storage mapping (out of this issue's scope) is expected to read from this same nested manifest — see "Purpose and boundary" — which is why the shape is not pre-flattened even though a flat table might otherwise be a more natural fit for a schema-backed `Entity`.

## NODATA, empty candidate sets, and statistics

A *valid* cell, in this issue's vocabulary, is an `ElevationGrid` cell whose elevation is not `null`. NODATA and clip-excluded cells are indistinguishable once they reach an `ElevationGrid` — both are `null` — a distinction `GridClipper` preserves separately, in `GridCellStatus`, only before that point (see the AOI normalization and clipping design note).

`ElevationStatistics.CountValidCells` counts a grid's valid cells and returns `0` for an all-NODATA grid rather than throwing. `ElevationStatistics.ComputeRange` computes the minimum and maximum over valid cells only, tagged with `grid.VerticalReference.Unit`, and throws `TerrainProvenanceException` ("The grid has no valid elevation: every cell is NODATA, and NODATA cells are excluded from statistics.") when `CountValidCells` is `0` — there is no minimum or maximum to report.

`TerrainProvenance`'s own constructor (Issue #2, unchanged here) stays permissive about this case: `originalPointCount == 0` with `elevationRange: null` is legal there, mirroring `SimplificationResult`'s own precedent of returning a valid, empty result for an all-NODATA grid rather than throwing. The rejection AGENTS.md implies for an actual *export* — nothing to export is treated as an error, not a silent empty file — happens one layer up, at two independent points:

- `TerrainExportPayloadAssembler.Assemble` calls `ElevationStatistics.CountValidCells(candidateGrid)` first; if it is `0`, `Assemble` throws `TerrainProvenanceException` ("The candidate set has no valid elevation: every cell handed to the simplifier is NODATA, and NODATA cells are excluded from export.") before it builds any `TerrainProvenance` or `ElevationRange` at all.
- `TerrainExportBundleRenderer.Render` independently throws `TerrainExportException` ("Nothing to export: the provenance record's original point count is zero.") if `provenance.OriginalPointCount == 0` ever reaches it — a second line of defense in case a `TerrainProvenance` reached the renderer some other way than through `Assemble`.

`TerrainExportBundleReader.ParseElevationRange` mirrors this on the read side: it rejects a null `elevationRange` outright, since a strict schema version 1 document never legitimately carries one — the writer already rejects an original point count of zero, and `TerrainProvenance`'s own constructor requires a non-null `elevationRange` whenever the original point count is positive.

`Assemble` also cross-checks a non-null `simplification.Diagnostics` against what `ElevationStatistics` independently computed from the same `candidateGrid`: `Diagnostics.CandidatePointCount` must equal `originalPointCount`, and `Diagnostics.MinElevation`/`MaxElevation`/`ElevationUnit` must equal `elevationRange.Minimum`/`.Maximum`/`.Unit` by exact `double` equality (both sides are computed from the same cells, so an exact comparison is the correct check, not a tolerance). Any mismatch throws `TerrainProvenanceException` naming the specific field that disagreed. Separately, `Assemble` requires `elevationRange.Unit == sourceVerticalReference.Unit`, throwing `TerrainProvenanceException` naming both units when a caller passes a grid whose vertical unit does not match the vertical reference it also passed.

`Assemble` is also the only point in the pipeline where `candidateGrid` and the caller-declared `localFrame`/`sourceVerticalReference` are simultaneously available — `TerrainProvenance`'s own constructor cross-checks `sourceVerticalReference`/`horizontalTransformation` against `localFrame` (see "Provenance record and AGENTS.md field coverage") but never sees `candidateGrid` at all. `Assemble` therefore also requires `candidateGrid.HorizontalReference == localFrame.ProjectedHorizontalReference` and `candidateGrid.VerticalReference == sourceVerticalReference` (the full record, not just `Unit`), throwing `TerrainProvenanceException` naming both sides of whichever comparison failed. The vertical check catches a grid and a declared vertical reference that agree on `Unit` but disagree on `Datum` or `GeoidModel` — a mismatch the `Unit`-only check above cannot see, and one that would otherwise let a wrong vertical datum reach the exported provenance silently, since `TerrainSample`/`Coordinate3D` carry no CRS tag of their own.

## Reconstructing source coordinates

Every retained sample is exported as a `LocalCoordinate` produced by `LocalCoordinateFrame.ToLocal`, and `provenance.localFrame` records exactly what `ToLocal` needs to be reversed. `ToLocal` and its inverse `ToSource` (both Issue #6, unchanged here) are:

```text
ToLocal(source):
    x         = Convert(source.X - Origin.X,               HorizontalUnit,          OutputUnit)
    y         = Convert(source.Y - Origin.Y,                HorizontalUnit,          OutputUnit)
    elevation = Convert(source.Elevation - Origin.Elevation, VerticalReference.Unit, OutputUnit)

ToSource(local):
    X         = Origin.X + Convert(local.X,                 OutputUnit, HorizontalUnit)
    Y         = Origin.Y + Convert(local.Y,                 OutputUnit, HorizontalUnit)
    Elevation = Origin.Elevation + Convert(local.Elevation, OutputUnit, VerticalReference.Unit)
```

where `HorizontalUnit` is the private computed property `ProjectedHorizontalReference.Unit.LinearUnit`, and `Convert(value, from, to) = value * LengthConverter.MetersPerUnit(from) / LengthConverter.MetersPerUnit(to)`.

The detail that matters for reconstruction: `Origin` is a plain `Coordinate3D`, stored entirely in *source* units — `Origin.X`/`Origin.Y` in `ProjectedHorizontalReference.Unit.LinearUnit`, `Origin.Elevation` in `VerticalReference.Unit` — never in `OutputUnit`. `ToLocal` subtracts `Origin` from the source coordinate *before* converting anything, so only the delta crosses the unit boundary; `ToSource` is the exact mirror, converting the delta back to source units first and adding source-unit `Origin` last. A reader must therefore follow the same order, not "convert the whole local coordinate, then add the origin":

1. Take the sample's `x`/`y`/`elevation` (from `points.csv`, or `LocalTerrainSample.Position`); its unit is `provenance.localFrame.outputUnit`, which also equals `points.unit`.
2. Convert `x`/`y` from `outputUnit` to `provenance.localFrame.projectedHorizontalReference.unit.linearUnit`, and `elevation` from `outputUnit` to `provenance.localFrame.verticalReference.unit`, using `LengthConverter.Convert` (or, equivalently, the `metersPerUnit` values recorded in `unitDefinitions`).
3. Add `provenance.localFrame.origin.x`/`.y`/`.elevation` — already in those same source units, per the note above — to get the projected horizontal coordinate and the source-vertical elevation. Steps 2–3 together are exactly `LocalCoordinateFrame.ToSource`; a caller holding a reconstructed `LocalCoordinateFrame` (which `TerrainExportBundleReader.Read`/`.ReadProvenance` hand back inside `TerrainProvenance.LocalFrame`) can simply call it rather than reimplementing these steps.
4. To recover the original geographic coordinate the horizontal transformation started from, apply `provenance.horizontalTransformation.inverseOperation` using the named `engineName`/`engineVersion` (`"ProjNET"`/`"2.1.0"` for every production export today). The manifest stores this operation as opaque, engine-specific definition text (`format` plus `definition` — production uses the original WKT text ProjNET was given, not a re-serialized, engine-neutral formula, because ProjNET's own `MathTransform.WKT` cannot re-serialize the concatenated transform this codebase builds). Reproducing the exact numeric result therefore depends on running the same named engine version, not merely reading the operation text.
5. There is no vertical datum step to reverse. No vertical datum transformation is performed anywhere in SolidGround, and `TerrainProvenance`'s constructor already enforces `SourceVerticalReference == LocalFrame.VerticalReference` for exactly that reason (Issue #2): the vertical component of a reconstructed coordinate is only the unit conversion and origin addition in steps 2–3 above.

`TerrainExportBundleReader` itself stops at reconstructing the `TerrainProvenance`/`TerrainExportPayload` records — it does not call `ToSource` on a caller's behalf. Calling `LocalCoordinateFrame.ToSource` (step 3) and applying `inverseOperation` (step 4) are the caller's own next steps once it holds the reconstructed provenance.

## Boundary: what #9 and Phase 2 still own

| Concern | Owner | Notes |
| --- | --- | --- |
| CLI destination selection, output directory/base-name argument parsing, and printing a receipt or provenance summary | Issue #9 | `FileSystemTerrainExporter` and `TerrainExportBundleRenderer` are ready to call as-is; `src/SolidGround.Cli/Program.cs` is untouched by this issue. |
| Operator-asserted `collectionPeriod`/`qualityLevel` for a source that does not report them, with the assertion's own origin recorded | Issue #9 / follow-up | `ElevationSourceMetadata` already models both fields as optional (Issue #2); this issue never fabricates a value the OpenTopography `usgsdem` endpoint does not return. |
| Choosing which point becomes the local origin | Issue #9 | Unchanged from the coordinate transformation and units design note: `LocalOriginSnapping` only snaps a caller-supplied candidate; it never chooses one. |
| Mapping the export document manifest onto a Revit Extensible Storage schema (stable GUID, per-field storage) | Phase 2 | AGENTS.md's Extensible Storage decision; the manifest here is deliberately nested, not pre-flattened, so that mapping can walk the same structure. |
| Attaching provenance to a created toposolid element, and any Revit-side read-back | Phase 2 | `TerrainExportBundleReader` reconstructs a `TerrainProvenance`/`TerrainExportPayload` from bytes; a Revit-side adapter that calls it is a separate, later concern. |
| An export destination other than the local file system | Not scheduled | `ITerrainExporter` (Issue #2) stays destination-neutral; `FileSystemTerrainExporter` is the only implementation this issue adds. |
| A schema version 2 manifest | Not scheduled | See "Versioning and compatibility policy": the version 1 manifest stays documented once a version 2 is ever added. |

## Known limitations

- `GridTerrainSimplifier`'s `coverageFloorFraction` (constructor-level, Issue #7, default `0.2`) is not recorded anywhere in this manifest: `provenance.simplification` serializes `SimplificationRequest`, which carries only `pointBudget` and `method`. A document alone cannot say which coverage floor produced a given `CurvatureAware` result.
- `provenance.horizontalTransformation.engineVersion` (`"ProjNET"`/`"2.1.0"` today, hardcoded literals in production, not the reflected assembly version) changes whenever the pinned ProjNET package version changes. An older export's `engineVersion` documents which engine build actually produced its `forwardOperation`/`inverseOperation` text; it is not a guarantee that a later build of SolidGround reproduces the identical numeric transform from that text.
- Cross-machine determinism of the reprojection and buffering stages that build a real payload's inputs is expected, not proven — see "Determinism rules". Only `TerrainExportBundleRenderer.Render` over an already-built, identical payload carries a platform-independent, proven byte-for-byte guarantee.
- `collectionPeriod` and `qualityLevel` are `null` in every export produced by the only implemented source today, because the OpenTopography `usgsdem` endpoint reports neither — see "Provenance record and AGENTS.md field coverage".
- SolidGround remains a site-form tool, not a survey instrument (AGENTS.md, "Accuracy and product claims"). Nothing in this export format certifies a geometric error bound or a vertical accuracy figure: `elevationRange` and the point counts describe the exported surface itself, not how well that surface reproduces the true ground beneath canopy.
