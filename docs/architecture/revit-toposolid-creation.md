# Revit toposolid creation

Issue #15 rewrites `CreateToposolidCommand` on 2026-09-21 to turn a settings-driven OpenTopography fetch or
local `.asc`/`.prj` `process` run into a native Revit `Toposolid`, created inside one transaction that is
provably unchanged on every rejected path, plus a pre-transaction export bundle and a post-commit Revit-side
placement record. It also lifts `SolidGround.Cli`'s terrain processing pipeline — `TerrainProcessingPipeline`,
`ClipRegionFactory`, `LocalOriginFactory`/`LocalOriginRequest` (renamed from `LocalOriginSelection`),
`VerticalReferenceResolution`, `RasterSourceSidecar`/`RasterSourceSidecarIo`, and `LengthUnitTokens` — into
`SolidGround.Core`, `public`, so `SolidGround.Revit` can reuse it without a back-reference to
`SolidGround.Cli`: AGENTS.md's Revit add-in conventions fix `SolidGround.Revit.csproj`'s only
`ProjectReference` at `SolidGround.Core`, and every one of these types is now something both hosts need. This
note follows `docs/architecture/revit-add-in-conventions.md`'s owner-approved decisions and
`docs/architecture/revit-2027-verification-and-host-design.md`'s locked thin-host design, and it continues
directly from Issue #14's scaffold (`docs/architecture/revit-add-in-host-scaffold.md`): `CreateToposolidCommand`
is the same class that shipped as a read-only Preflight-and-report command in Issue #14, now rewritten to
actually create the toposolid.

Issue #15 ships **zero** Extensible Storage code (Issue #16's territory), no code-signing or packaging change
(Issue #17's), and no ribbon-icon redesign (Issue #19's) beyond the button tooltip/long-description text
update Issue #14's own scaffold note already anticipated.

## Basis

Three independently synthesized implementation designs were produced from this repository,
`docs/architecture/revit-2027-verification-and-host-design.md`'s 17 verification items, and fresh
`MetadataLoadContext` reflection dumps of the installed Revit 2027 (`27.0.10.13`) assemblies; three
independent reviewers scored them, and the majority-winning design was corrected through two further review
passes that cross-checked it against the live `src/`/`tests/` source and against a dedicated re-verification
pass over the installed SDK and Autodesk's current `27.2.0.0`-documented `Revit-API-MainReference` pages (the
same version gap — installed `27.0.10.13` vs. documented `27.2.0.0` — every other Phase 2 note in this
repository already carries; see "Revit API members used" below). This note is the committed record of that
process and of the code it produced; the working design and verification artifacts themselves were not
committed, matching how `docs/architecture/revit-2027-verification-and-host-design.md` already records Issue
#13's own research pass without committing its raw inputs.

`docs/architecture/revit-2027-verification-and-host-design.md`'s own "Follow-ups" section had already scoped
what Issue #15 would inherit: the `Toposolid.Create` signatures and the Option A/Option B choice (item 15),
the Level/ToposolidType selection defaults and the coordinate/unit mapping (item 16), the point-budget rule
and the `IFailuresPreprocessor` defensive design (item 9), the locked synchronous
`Task.Run(...).GetAwaiter().GetResult()` bridge with `ExternalEvent` explicitly deferred (item 6), and item
2's `Result.Cancelled`/`Result.Failed` Undo-stack side effect. This note settles items 15, 16, and 9 by
construction — the shipped code makes one fixed choice for each, recorded under "Design decisions" below —
but does not settle item 2, keeps item 6 exactly as locked, and leaves the numeric point-threshold
relationship item 9 raises open pending manual test step 7's own evidence. See "Manual evidence plan" below
for all three.

## What Issue #15 built

Landed across three commits: `ef5aa53` (the Core lift), `b04754b` (Core boundary/selection/settings
additions), `5e95521` (the Revit host adapter and command rewrite). No new `PackageReference`, lock-file
entry, or `SolidGround.slnx` change appears in any of the three — `System.Text.Json` and every geometry type
used already ship in `SolidGround.Core`'s and `net10.0-windows`'s existing dependency graphs.

### `SolidGround.Core` — moved from `SolidGround.Cli`, made `public`

| File | Was | Notes |
| --- | --- | --- |
| `Processing/TerrainProcessingPipeline.cs` | `SolidGround.Cli/Processing/TerrainProcessingPipeline.cs`, `internal` | Clips (optionally), snaps a local origin, simplifies, and assembles the export payload — the exact steps `process`/`run` already shared. `AoiSelection`/`LocalOriginSelection` parameters became `AreaOfInterest`/`LocalOriginRequest`. |
| `Processing/ClipRegionFactory.cs` | same, `internal` | `BuildFetchEnvelope`/`Build` switch on the three concrete `AreaOfInterest` subtypes (`Wgs84BoundingBoxAoi`/`Wgs84RadiusAoi`/`ParcelGeometryAoi`) directly instead of reconstructing one from flattened fields. |
| `Processing/LocalOriginRequest.cs` | `Processing/LocalOriginSelection.cs`, `internal sealed record LocalOriginSelection` | Renamed. `Kind` carries `[property: JsonRequired]` so a settings.json `localOrigin` missing `kind` fails decode instead of silently defaulting to `Southwest` — a review fix beyond the original design record. |
| `Processing/LocalOriginFactory.cs` | same, `internal` | Body unchanged. |
| `Processing/VerticalReferenceResolution.cs` | same, `internal` | Keeps its `RasterSourceSidecar?` parameter (no narrower replacement record). `Resolve` now throws `FormatException` instead of the CLI-only `CliUsageException`; message text unchanged. |
| `Sources/RasterSourceSidecar.cs` | `Rasters/RasterSourceSidecar.cs`, `internal` | Five records (`RasterSourceSidecar`, `RasterSourceVertical`, `RasterSourceAcquisition`, `RasterSourceMetadataRequest`, `RasterSourceFetchEnvelope`), all `public sealed record` now; fields unchanged. |
| `Sources/RasterSourceSidecarIo.cs` | `Rasters/RasterSourceSidecarIo.cs`, `internal` | `Read`'s roughly sixteen validation-failure throw sites changed from `CliUsageException` to `FormatException`; message text unchanged. |
| `Units/LengthUnitTokens.cs` | `Options/LengthUnitTokens.cs`, `internal` | `Parse` throws `FormatException`; the CLI's one call site wraps it back into `CliUsageException` so CLI-facing text is unchanged. |

`SolidGround.Cli`'s own commands (`RunCommand.cs`, `ProcessCommand.cs`), `Processing/AoiSelection.cs` (gained
`internal AreaOfInterest ToAreaOfInterest(HorizontalReference)`), and `Rasters/RasterSetIo.cs` were edited to
consume the moved types. No CLI option, exit code, stdout/stderr line, or golden fixture changed — the full
existing CLI/golden test suite is the regression proof and it passed unmodified (see "Verification" below).
No `.csproj`, lock file, or `SolidGround.slnx` entry changed anywhere across all three commits, confirmed
directly against the commit range.

### `SolidGround.Core` — new

| File | Responsibility |
| --- | --- |
| `Exports/LocalBoundary.cs` | `LocalBoundaryRing`/`LocalBoundaryPolygon`/`LocalBoundary` — a 2D, Revit-free plan-view boundary. `Z` never appears: see "The boundary-Z decision" below. |
| `Exports/LocalBoundaryFactory.cs` | `FromPolygonalRegion` (a clipped AOI), `FromGridEnvelope` (the whole-grid fallback for `process` mode with no AOI), `ContainsWithTolerance`. |
| `Exports/LocalBoundaryValidator.cs` | Every boundary/retained-sample problem in one pass, never short-circuiting: ring shape, self-intersection, holes, positive area, duplicate/out-of-boundary retained points, budget. |
| `Processing/NamedElevationSelection.cs` | `NamedElevationCandidate`/`NamedElevationSelector.SelectLowestElevation` — the `Level` selection rule. |
| `Processing/NamedSelection.cs` | `NamedCandidate`/`NamedSelector.SelectFirstByOrdinalName` — the `ToposolidType` selection rule. |
| `Processing/AoiSettingsFactory.cs` | Builds a real `AreaOfInterest` from `AoiSettings`, reusing `Wgs84BoundingBoxAoi`/`Wgs84RadiusAoi`/`ParcelGeometryAoi`'s own constructors rather than re-deriving their rules. |
| `Processing/TerrainRequestSettings.cs` | The host-neutral settings contract (see "Settings file reference" below), its never-throws `Validate()`, and the one shared `JsonOptions` every decode of this record uses. |
| `Geometry/Coordinates.cs` (edited) | Adds `LocalCoordinate2D`, the same finite-value guard as `Coordinate2D`/`Coordinate3D`/`LocalCoordinate`. |
| `Transformations/LocalCoordinateFrame.cs` (edited) | Adds `ToLocalHorizontal(Coordinate2D)`, reusing the existing private `HorizontalUnit` computation. |
| `Exports/TerrainExportBundleRenderer.cs` (edited) | `TerrainExportBaseName`: `internal` → `public`, body unchanged, so settings validation can reuse the exporter's own base-name rule. |

### `SolidGround.Revit` — new/rewritten

| File | Responsibility |
| --- | --- |
| `Settings/RevitSettingsLocator.cs` | Resolves `%ProgramData%\SolidGround\Revit\settings.json` via `Environment.SpecialFolder.CommonApplicationData`; never hardcoded. |
| `Settings/RevitSettingsIo.cs` | `EnsureTemplateExists` (mutex-guarded, atomic, create-if-absent) and `TryLoad` (strict decode + `Validate()`, every problem folded into one error string). |
| `Settings/RevitSettings.cs`, `RevitTargetSettings.cs` | The Revit-only settings wrapper (`Request` + `Target`) and its `level.name`/`toposolidType.name` overrides. |
| `Geometry/RevitUnitConversion.cs` | `LengthUnit` → `ForgeTypeId`, plus the one place a scalar in that unit becomes Revit-internal. |
| `Geometry/BoundaryGeometryBuilder.cs` | `LocalBoundary` + one constant `Z` → `IList<CurveLoop>`; retained samples → `IList<XYZ>`; the expected bounding box. |
| `Elements/LevelAndTypeResolver.cs` | Thin adapter over the two Core selectors; never `Level.Create`, never creates or duplicates a `ToposolidType`. |
| `Transactions/ToposolidCreationService.cs` | `ToposolidCreationStrategy`, `Create` (dispatches to the combined or profiles-then-`SlabShapeEditor` overload), `ToposolidCreationException`. |
| `Transactions/ToposolidCreationFailurePreprocessor.cs`, `ToposolidCreationFailureLog` | `IFailuresPreprocessor`: logs every `FailureMessageAccessor`, requests rollback on `Error`/`DocumentCorruption`, deletes warnings otherwise. |
| `Transactions/PostCreationVerification.cs` | The pre-transaction planarity guard and the post-create bounding-box/slab-shape-vertex-count check. |
| `Transactions/OrphanCheck.cs` | `OrphanSnapshot` (BasePoint/SurveyPoint position and shared position, `SiteLocation` place name, `ActiveProjectLocation` name), `Capture`, `Unchanged`. |
| `Provenance/PlacementRecord.cs`, `PlacementRecordWriter.cs` | The placement-record shape (see "Placement record schema" below) and its hand-written `Utf8JsonWriter` renderer. |
| `Commands/CreateToposolidCommand.cs` (rewritten) | The full six-stage flow — see "Command flow" below. |
| `SolidGroundApplication.cs` (edited) | `ButtonToolTip`/`ButtonLongDescription` updated to describe real creation; the site-form/not-a-survey-instrument disclaimer retained. |

### Tests

New, fully offline: `LocalBoundaryTests`, `LocalBoundaryFactoryTests`, `LocalBoundaryValidatorTests`,
`LocalCoordinateFrameHorizontalTests`, `NamedElevationSelectorTests`, `NamedSelectorTests`,
`AoiSettingsFactoryTests`, `TerrainRequestSettingsTests`, `TerrainProcessingPipelineTests`,
`ClipRegionFactoryTests`, `LocalOriginFactoryTests`, `VerticalReferenceResolutionTests`, and
`RasterSourceSidecarIoTests` (new against the now-public Core type — no CLI-scoped predecessor test existed
to move; the type was `internal` and previously exercised only indirectly through `CliApplication.RunAsync`).
`LengthUnitTokensTests` moved (`using SolidGround.Cli.Options;` → `using SolidGround.Core.Units;`); its
round-trip and default-token assertions carry over unchanged, and its unknown-token rejection assertion now
checks `FormatException` instead of `CliUsageException`. `ArchitectureTests` gained
`CoreExposesAPublicTerrainProcessingPipeline`, `CoreExposesAPublicLocalBoundaryValidator`,
`CoreExposesAPublicRasterSourceSidecarIo`, and `NetTopologySuiteTypesNeverAppearInAnyNewPublicCoreSignature`
(scoped to the new `Exports`/`Processing`/`Sources`/`Units` additions, excepting the already-reviewed
`PolygonalRegion.Geometry`). `RevitHostFilesTests` gained `NoRevitSourceFileReferencesExtensibleStorageTypesYet`
(a plain-text scan of every `src/SolidGround.Revit/**/*.cs` for `ExtensibleStorage`/`SchemaBuilder`/
`GetEntity`/`SetEntity`, a falsifiable backstop for "#15 ships zero Extensible Storage code," deliberately
removed when Issue #16 lands), `ButtonLongDescriptionKeepsTheSiteFormNotASurveyInstrumentDisclaimer`, and
`CreateToposolidCommandSuccessDialogKeepsTheSiteFormNotASurveyInstrumentDisclaimer`. See "Verification" below
for the current pass count.

## Design decisions with reasons

### The Core lift keeps `SolidGround.Revit` to one `ProjectReference`

`docs/architecture/revit-add-in-conventions.md`'s Layout rule fixes `SolidGround.Revit.csproj`'s only
`ProjectReference` at `SolidGround.Core`, enforced by `RevitHostFilesTests.CsprojHasExactlyOneProjectReferenceToCore`.
`TerrainProcessingPipeline` and its five collaborators were `internal` to `SolidGround.Cli` before this issue,
so a Revit command that needed them had exactly two options: duplicate the pipeline, or move it. Duplication
was rejected outright — it is exactly the kind of drift AGENTS.md's centralization rules (length conversion,
provenance, etc.) exist to prevent. The lift is behavior-preserving by construction: every moved type's body
is unchanged (only its accessibility and, for `RasterSourceSidecarIo`/`LengthUnitTokens`, its thrown exception
type changed), and the full pre-existing CLI/golden test suite — the highest-risk regression surface for a
lift like this — passed unmodified after it (see "Verification"). `RasterSourceSidecar`/`RasterSourceSidecarIo`
moved in full, not as a narrower replacement record: `SolidGround.Revit`'s own `process` mode needed the exact
same optional source-sidecar support the CLI's `process` command already has (see "Process mode mirrors the
CLI's sidecar auto-detection" below), so the full type was the only option that did not reduce scope.

### Process mode mirrors the CLI's sidecar auto-detection

`RunProcessPipelineAsync` in `CreateToposolidCommand.cs` resolves `process.sourceJson` exactly the way
`ProcessCommand.RunAsync` resolves `--source-json`: an explicit path is read and must exist; a blank
(`null`) configured path falls back to `Path.ChangeExtension(ascPath, ".source.json")`, used only if that
file happens to exist. The `".source.json"` literal is duplicated in `CreateToposolidCommand.cs` as
`DefaultSourceJsonExtension` rather than referenced from `SolidGround.Cli.Rasters.RasterSetIo.SourceFileExtension`
(the CLI's own single source of truth for that string) because `SolidGround.Revit` cannot reference
`SolidGround.Cli` at all — the one-`ProjectReference` rule above applies here too. Both constants are the
same four characters plus the file's own suffix; nothing enforces that they cannot drift apart beyond this
note and each file's own comment pointing at the other.

### The ring closing-vertex convention

`LocalBoundaryRing.Vertices` never repeats its first vertex as a trailing closing vertex, unlike
`SolidGround.Core.Aois.PolygonRings` (whose `Shell`/`Holes` always carry the OGC/NetTopologySuite-style
duplicated closing coordinate). `LocalBoundaryFactory.FromPolygonalRegion` strips that duplicate when
building a ring from a `PolygonalRegion`; `LocalBoundaryValidator` rejects a ring that still carries one — or
any other pair of cyclically consecutive duplicate vertices, including the wrap-around edge from the last
vertex back to the first — as a zero-length-edge problem. `BoundaryGeometryBuilder.BuildProfiles` (Revit
side) is the one place that closes the loop, appending `Line.CreateBound(points[i], points[(i + 1) %
points.Length])` for every vertex including the wrap-around segment. This keeps the closing-vertex
responsibility in exactly one place on each side of the Core/Revit boundary, rather than letting a caller
guess whether a given `LocalBoundaryRing` is already closed.

### Settings file design

`RevitSettingsLocator.Resolve()` builds `%ProgramData%\SolidGround\Revit\settings.json` from
`Environment.SpecialFolder.CommonApplicationData`, matching `AddInLog`'s own resolution pattern and
AGENTS.md's "settings and logs are machine-wide" rule. `RevitSettingsIo.EnsureTemplateExists` writes the
template (see "Settings file reference" below) only when the file is absent, guarded by a named,
cross-process `Mutex`:

- The mutex name is `Global\SolidGround.Revit.Settings.<SHA-256 of the settings path, hex>` — a raw path is
  not itself a valid mutex name (backslashes, length limits), so `RevitSettingsIo.BuildMutexName` hashes it
  instead of using it verbatim.
- `WaitOne` uses a fixed 5-second timeout (`RevitSettingsIo.MutexTimeout`, orchestrator decision (c)). A
  timeout is reported through the same `writeError` channel as a real I/O failure, but with its own message
  naming the lock itself ("SolidGround could not acquire its settings lock within 5 seconds...") — deliberately
  never conflated with the write-access-denied message below, since the remedy is different (wait and retry,
  versus grant permissions).
- `AbandonedMutexException` is caught and treated as a normal acquisition (orchestrator decision (c)): .NET's
  own semantics already transfer ownership to the catching thread, so an abandoned mutex from a crashed prior
  process is not a failure condition here.
- Absence is re-checked inside the mutex before writing, so a concurrent winner is success
  (`justCreated = false`), not an error.
- The write itself is a sibling temp file (`<path>.<new GUID>.tmp`) plus `File.Move`, never a direct write to
  the final path, and the method never rewrites an existing file — even an invalid one. A decode or
  validation failure is always reported by `TryLoad` instead, naming the exact path and cause.
- `UnauthorizedAccessException` during the write gets its own distinct message, naming this very file by
  path: *"...or create the file by hand using the template in docs/architecture/revit-toposolid-creation.md."*
  — never conflated with "the file simply does not exist yet."

AGENTS.md's general settings convention calls for "a mutex plus digest-conflict check plus atomic write."
The digest-conflict check has no live trigger in this milestone: `EnsureTemplateExists` only ever performs a
create-if-absent write, never a read-modify-write cycle on an existing file — the only case a digest conflict
could arise from. This is stated here explicitly, as a documented gap rather than dead code: a future
settings-editing feature (a WPF picker, out of this milestone's scope) that performs a real read-modify-write
would need to add it.

`RevitSettingsIo.TryLoad` decodes the one flat settings document by splitting it into the
`TerrainRequestSettings`-shaped portion and the two Revit-only `level.name`/`toposolidType.name` overrides
(`JsonNode`-based splitting, so the split itself never depends on `TerrainRequestSettings`'s own shape),
decodes the first with `TerrainRequestSettings.JsonOptions` — the one shared `JsonSerializerOptions` every
decode of this record uses, camelCase property names, unknown members rejected at every nesting level,
`//` comments and trailing commas tolerated, and a `JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`
binding every enum-typed field (`mode`, `outputUnit`, `localOrigin.kind`, `simplification.method`,
`process.verticalUnit`) directly from its documented token — then runs `TerrainRequestSettings.Validate()`,
folding every decode or validation problem into one multi-line error. `JsonOptions` is Core-hosted (not
inline inside `RevitSettingsIo`) specifically so a Core-only test
(`TerrainRequestSettingsTests.JsonOptionsDecodesTheShippedTemplateTextVerbatim`) can decode the shipped
template's exact bytes without Revit, per AGENTS.md's "must not require Revit to run Core tests" rule. A
`JsonException` from an unrecognized enum token is caught inside `TryLoad` and folded into the same generic
error text as any other malformed-JSON failure — it never reaches `Validate()`. That folded text does,
however, splice in the `JsonException`'s own `.NET`-generated `Message` (which can name internal CLR/JSON-path
detail) verbatim, the same as every other decode/validation problem, so it does reach the user: inline, within
`ProblemReportDialog`'s capped `TaskDialog` lines, and in full in the report `ProblemReportDialog` always
writes to the log folder. `Validate()` itself reuses the real `Wgs84BoundingBoxAoi`/`Wgs84RadiusAoi`/`ParcelGeometryAoi`
constructors (via `AoiSettingsFactory.Build`, with a fixed placeholder WGS 84 reference and a fixed
non-blank placeholder parcel-geometry string) and `TerrainExportBaseName.Validate` rather than re-deriving
any of their rules, and it never itself opens a file — every path-existence check is a Preflight, not a
settings-validation, concern.

### The boundary-Z decision and its open empirical question

The locked Preflight checklist requires the toposolid boundary to be closed, planar, and parallel to the
horizontal plane. A boundary loop whose vertices each took the elevation of their nearest retained terrain
sample would be neither planar nor horizontal for any non-flat site, and would fail exactly the check it is
supposed to satisfy — an opaque `Toposolid.Create`/`CurveLoop` rejection deep inside the transaction instead
of a clear Preflight message. A boundary pinned to a flat, hardcoded source-datum `Z = 0` risks a "sea-level
skirt" tens or hundreds of feet away from the real terrain — a modeling defect, even though it is technically
planar. An early version of this design sourced the one constant `Z` from the resolved `Level`'s own
`Elevation` instead, which does not solve the problem: it reintroduces the identical failure one level
removed, because `LocalOriginFactory`'s `Southwest`/`Centroid` origin computation hardcodes the origin's own
elevation to `0d`, so under the shipped default local origin every retained sample's local `Z` is its full,
un-rebased absolute source elevation (roughly 600 ft for the ExampleSite fixture), while `Level.Elevation` on a
fresh or default project sits at or near `0` — the same hundreds-of-feet gap, with `Level.Elevation`
substituted for a literal source-datum zero.

The shipped fix treats the boundary `profiles` passed to `Toposolid.Create` as a **plan-view sketch of the
element's extent**, never the terrain shape — the terrain shape comes entirely from the `points` array. Every
vertex of every ring in every polygon shares **one constant `Z`**, computed in `CreateToposolidCommand.ExecuteCore`
as `points.Min(point => point.Z)` — the minimum of the retained terrain samples' own local elevation, already
in Revit-internal units, computed *after* `points` is built. This is guaranteed close to the terrain
regardless of the Level's or the local origin's absolute magnitude, because it is derived from that exact
terrain; `Level.Elevation` is no longer read for boundary geometry at all, only for the resolved `Level`'s
`ElementId`. `BoundaryGeometryBuilder.BuildProfiles` takes this one scalar as a parameter, never a per-vertex
source, so a non-planar boundary is impossible by construction; `PostCreationVerification.AllProfilesArePlanar`
additionally calls `CurveLoop.HasPlane()` on every built loop as a cheap, purely defensive assertion before
`Toposolid.Create` is ever invoked — this can only fail on a construction bug, never on terrain data, and
exists to fail loudly at Preflight rather than silently inside the transaction if one ever creeps in.
`SolidGround.Core.Exports.LocalBoundary` stays two-dimensional (`LocalCoordinate2D`, no `Z` at all) precisely
because of this fix: `Z` is a Revit-side, single-scalar concern applied once, uniformly, never a Core boundary
property.

**What this does not resolve.** Whether the flat boundary-ring `Z` persists as visible geometry at or near the
parcel edge in the final `Toposolid` — a shallow, flat rim where the boundary and the nearest interior points
differ in `Z` — or whether Revit's shape-editing behavior fully overrides it with interpolated terrain
everywhere the points reach, is not established by any documentation source reached during design or
verification. Because the fix above keeps the boundary's `Z` within the terrain's own local range regardless
of which is true, both outcomes are benign — at worst a shallow rim at the terrain's own low point, never a
rim hundreds of feet away — but this design does not assume an answer either way. Manual test step 8a item 5
below exists to observe and record which actually occurs; see "Manual evidence plan."

### Transaction and failure handling

`Result` is derived **only** from the real `TransactionStatus` Revit returns, never from "did an exception
happen" — see "Transaction status and the Result-code policy" below for the full table. `RunTransaction` in
`CreateToposolidCommand.cs` wraps everything from `ToposolidCreationService.Create` through
`transaction.Commit()` in one `try`, with two `catch` clauses: `catch (ToposolidCreationException ex)` for a
Revit-rejected boundary or points, and a general `catch (Exception ex) when (ex is not OutOfMemoryException
and not StackOverflowException)` for everything else that can go wrong in that span — `document.Regenerate()`
throwing `RegenerationFailedException`, the Issue #16 extension-point hook throwing, or `Commit()` itself
throwing. Both catches compute `TransactionStatus status = transaction.HasEnded() ? transaction.GetStatus() :
transaction.RollBack();` — never calling `RollBack()` a second time on a transaction that has already ended —
then derive `Result` from that status alone. `transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions()
.SetFailuresPreprocessor(...).SetClearAfterRollback(true))` is called before `transaction.Start()`; Autodesk's
`SetFailureHandlingOptions` documentation states options "can be set at any time before the transaction is
either committed or rolled back," which permits this ordering, though the one published Autodesk code example
calls it *after* `Start()` — a documented-compatible, not documented-recommended, choice (see "Revit API
members used" below).

`ToposolidCreationFailurePreprocessor` (an `IFailuresPreprocessor`) logs every `FailureMessageAccessor` it
observes (severity, description text, and a best-effort `FailureDefinitionId` string) into both `AddInLog`
and a caller-supplied `ToposolidCreationFailureLog`. It requests `FailureProcessingResult.ProceedWithRollBack`
on any `FailureSeverity.Error`/`.DocumentCorruption` message; otherwise it deletes every `Warning`-severity
message and returns `Continue`. `RunTransaction` checks `failureLog.HasBlockingFailure` itself, immediately
after `PostCreationVerification.Verify` — a blocking failure message and a failed geometry verification are
treated as two independent rejection reasons, with verification's own message taking the dialog when both
occur.

`document.Regenerate()` runs exactly once, at command level, immediately after `ToposolidCreationService.Create`
returns — `Create` itself never calls `Regenerate()` for either strategy. Autodesk's `Document.Regenerate`
documentation notes that `Commit()` itself auto-regenerates, so this separate, earlier call exists specifically
to make the freshly created geometry visible to `PostCreationVerification.Verify` *before* `Commit()` runs, not
as a redundant step.

### Transaction status and the Result-code policy

| Observed status | Meaning | `Result` |
| --- | --- | --- |
| Never reached `Start()`, or `Start()` returned anything but `Started` | Nothing began | `Cancelled` |
| Explicit `RollBack()` returns `RolledBack` | Document provably reverted | `Cancelled` |
| `Commit()` returns `Committed` | Success | `Succeeded` |
| `RollBack()`/`Commit()` returns anything else (`Error`, `Pending`, still `Started`) | Indeterminate — may be modified | `Failed` |
| Any exception before `Start()` (Preflight, settings, acquisition, geometry Preflight) | No `Transaction` object ever existed | `Cancelled` |
| Any exception after `Start()` not covered above — `Create` throwing `ToposolidCreationException`, `Regenerate()` throwing `RegenerationFailedException`, the Issue #16 hook throwing, or `Commit()` itself throwing | Reached through a `catch`, not a checked return; `transaction.HasEnded()` gates whether `RollBack()` is attempted at all, never a second time on an already-ended transaction | `Cancelled` if the resulting status is `RolledBack`, else `Failed` |

This table is provisional pending manual test step 5 below, which specifically probes whether a genuinely
`RolledBack` transaction ever corrupts a prior, unrelated Undo entry the way a zero-transaction `Result.Failed`
reportedly can on an earlier Revit version, and separately probes the last row (a post-`Start()` exception
that never touches `Create`/`Verify`). Autodesk's `Transaction` class-page documentation states the
`Dispose()`-triggered automatic-rollback behavior explicitly and simultaneously advises against relying on it
("always call either Commit or RollBack explicitly") — this design already does not rely on it: every code
path above calls `RollBack()` explicitly and reads the returned status, and the `using` statement around
`Transaction` is a safety net only.

### Level and ToposolidType selection

`LevelAndTypeResolver` never calls `Level.Create`, never creates or duplicates a `ToposolidType`. It projects
every existing `Level`/`ToposolidType` (via `FilteredElementCollector(document).OfClass(typeof(Level))`/
`.OfClass(typeof(ToposolidType))`) into a Revit-free `NamedElevationCandidate`/`NamedCandidate`, hands the list
to `NamedElevationSelector.SelectLowestElevation`/`NamedSelector.SelectFirstByOrdinalName`, and maps the
winning candidate's `long` id back to the real element through a dictionary keyed by `ElementId.Value`.
Selection: a configured name (`level.name`/`toposolidType.name` in settings.json) wins on an exact ordinal
match; otherwise the Level with the lowest `Elevation` wins (ties broken by ordinal `Name`), and the first
ToposolidType by ordinal `Name` wins. No candidate of the requested kind anywhere in the document is a
Preflight rejection, not a creation. Because boundary geometry no longer reads `Level.Elevation` at all (see
"The boundary-Z decision" above), this rule's bias toward the lowest-elevation Level has no geometric
consequence — it only affects which Level the created element is organizationally associated with.

### Unit conversion

`RevitUnitConversion.ToForgeTypeId` is the one place a `SolidGround.Core.Units.LengthUnit` maps to a Revit
`ForgeTypeId`: `UsSurveyFoot` → `UnitTypeId.UsSurveyFeet` (exactly `1200/3937` m), `InternationalFoot` →
`UnitTypeId.Feet` (exactly `0.3048` m), `Meter` → `UnitTypeId.Meters`. Every coordinate crossing from Core into
the Revit API goes through `UnitUtils.ConvertToInternalUnits(value, forgeTypeId)` **per component** in
`BoundaryGeometryBuilder` — never a bare `double` assumed to already be Revit-internal feet. The chosen
`ForgeTypeId.TypeId` string is logged once per run and recorded in the placement record's `unitConversion`
object (see "Placement record schema" below). Whether `UnitTypeId.Feet` (international foot) or
`UnitTypeId.UsSurveyFeet` is numerically Revit's own internal foot is not established by any documentation
source reached during design or verification — Autodesk's own reference pages for both members describe only
their real-world definition, never an internal-unit correspondence. The code never assumes an answer either
way; manual test step 8a item 3 below is the only thing that settles it. `PlacementUnitConversionRecord.RoundTripDelta`
(`ConvertFromInternalUnits(ConvertToInternalUnits(1.0, id), id) - 1.0`) is a separate, automatic, every-run
self-consistency check, not a substitute for that one-time probe.

### No probe or test-only code ships in the add-in

Every API-behavior probe this design needed before implementation, and every probe the manual evidence plan
below still needs, runs from a throwaway add-in kept entirely outside this repository. Nothing under
`src/SolidGround.Revit` exists only to support that probing, and no `#if DEBUG`-style manual-test scaffolding
ships in the command. The dev loop stays build, deploy, restart Revit, verify by hash, matching Issue #14's
own "Debugging" convention.

## The Issue #16 extension point

```csharp
internal delegate void ToposolidCreatedHook(
    Document document, Toposolid toposolid, TerrainExportPayload payload, PlacementRecordDraft placementDraft);
```

Declared in `CreateToposolidCommand.cs` and invoked unconditionally at one fixed call site inside
`RunTransaction`, after post-create geometry verification passes and before `transaction.Commit()`, inside the
same transaction:

```csharp
ToposolidCreatedHook? postCreationHook = null; // Issue #16 supplies a non-null value here.
postCreationHook?.Invoke(document, toposolid, outcome.Payload, draft);
```

**Reasoning.** Revit itself instantiates `CreateToposolidCommand` by reflection from the `.addin` manifest's
`FullClassName` — `SolidGroundApplication.cs` registers the `PushButtonData` by assembly/class name only, and
`SolidGround.Revit.csproj`'s sole `ProjectReference` is `SolidGround.Core` — so no SolidGround code ever calls
`new CreateToposolidCommand(...)`, leaving no constructor or initializer site for a runtime-injected callback.
Since Issue #16 will be a later commit to this same file in this same assembly, not a separately deployed
plugin, a fixed call site with a nullable delegate defaulted to `null` already gives it everything a
dependency-injected extension point would: #16 changes exactly the one assignment line to reference its real
Extensible-Storage-attaching method. A named stub method was considered and is equally valid; a bare nullable
delegate was chosen because it needs no separate declaration and reads directly at its one call site.

`placementDraft` (`PlacementRecordDraft`) is every placement-record value computed **before** `Commit()`: the
element already exists mid-transaction, so `BasePoint`/`SurveyPoint`/etc. are readable regardless of commit
state. Only the **serialized, on-disk** placement-record write is deferred until a confirmed `Committed`
status (`ReportSuccess`, called only after `Commit()` returns `Committed`) — so a rolled-back run never leaves
an orphaned placement-record file on disk.

The hook **may throw**. Because the call site sits inside `RunTransaction`'s widened `try` (see "Transaction
and failure handling" above), any exception it raises is caught by the same general `catch (Exception ex)
when (...)` that rolls back (or reads the already-ended status) and derives `Result` from `TransactionStatus`
exactly like every other post-`Start()` failure — Issue #16 does not need its own exception-handling path at
this call site.

## Settings file reference

Path: `%ProgramData%\SolidGround\Revit\settings.json`, resolved via
`Environment.SpecialFolder.CommonApplicationData` (`RevitSettingsLocator.Resolve()`), never hardcoded. The
add-in itself still installs per-user; only settings and logs are machine-wide, per AGENTS.md's Revit add-in
conventions.

### Field table

Every token-valued field whose backing C# type is an enum — `mode`, `localOrigin.kind`, `outputUnit`,
`process.verticalUnit`, `simplification.method` — binds directly to that enum via the shared
`JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. An unrecognized token fails at JSON-decode time, before
`Validate()` ever runs, and is reported through the same generic "SolidGround Preflight found a problem" path
as any other malformed field. For a `required` field, the "Default" column below names the value the shipped
template happens to use, not a fallback used when the JSON key is entirely absent — an absent required key is
always a decode failure (`System.Text.Json`'s own `required`-member support), never a silent default.

| JSON path | Type | Required | Default | Validation |
| --- | --- | --- | --- | --- |
| `mode` | `"fetch"` \| `"process"` | yes | — | one of the two tokens |
| `areaOfInterest.kind` | `"boundingBox"` \| `"radius"` \| `"parcel"` | yes | — | one of the three; exactly the matching sub-object below may be non-null |
| `areaOfInterest.boundingBox.{west,south,east,north}` | number | iff `kind==boundingBox` | — | constructs a real `Wgs84BoundingBoxAoi`; its own range/ordering guards apply |
| `areaOfInterest.radius.{centerLatitude,centerLongitude,radiusMeters}` | number | iff `kind==radius` | — | constructs a real `Wgs84RadiusAoi`; `radiusMeters > 0` |
| `areaOfInterest.parcel.{path,format,bufferMeters}` | string, `"geojson"`\|`"wkt"`\|null, number | iff `kind==parcel` | `bufferMeters=0` | `path` non-blank (existence checked at Preflight); `format` inferred from `path`'s extension when `null`; `bufferMeters >= 0` |
| `process.asc` | string (path) | iff `mode==process` | — | non-blank (existence checked at Preflight) |
| `process.prj` | string? (path) | no | `{asc-without-ext}.prj` | existence checked at Preflight |
| `process.sourceJson` | string? (path) | no | none (no sidecar); if blank, the default sidecar path next to `.asc` is used only if it exists | if given explicitly, existence checked at Preflight; read via `RasterSourceSidecarIo.Read` |
| `process.sourceName`, `dataset`, `verticalDatum`, `geoid`, `qualityLevel` | string? | no | fall back to sidecar → `.prj`'s `VERT_CS` → `"local-file"` | non-blank if given |
| `process.verticalUnit` | `"usSurveyFoot"` \| `"internationalFoot"` \| `"meter"` \| `null` | no | `null` (falls back to sidecar → `.prj`'s `VERT_CS`) | bound directly as `LengthUnit?`; flows straight into `VerticalReferenceResolution.Resolve` with no separate parse step |
| `process.collectionStart`, `collectionEnd` | string? (`yyyy-MM-dd`) | no, both-or-neither | — | valid dates, start ≤ end |
| `localOrigin.kind` | `"southwest"` \| `"centroid"` \| `"explicit"` | yes | — | `[JsonRequired]`: an absent key fails decode rather than silently defaulting to `southwest` |
| `localOrigin.{x,y,z}` | number | iff `kind==explicit` | `0` | finite |
| `outputUnit` | `"usSurveyFoot"` \| `"internationalFoot"` \| `"meter"` | yes | `"usSurveyFoot"` | bound directly as `LengthUnit` |
| `simplification.method` | `"curvatureAware"` \| `"uniformSampler"` | yes | `"curvatureAware"` | never `"tinError"` — `GridTerrainSimplifier` does not implement it, and that enum member is deliberately not exposed as a documented choice |
| `simplification.pointBudget` | integer | yes | `15000` | 1..50000 inclusive |
| `simplification.coverageFloorFraction` | number | yes | `0.2` | `[0,1]` |
| `level.name` | string? | no | `null` (lowest-elevation default rule) | non-blank must exact-match (ordinal) an existing `Level.Name` (Preflight) |
| `toposolidType.name` | string? | no | `null` (first-by-ordinal-name default rule) | same shape as `level.name` |
| `output.directory` | string (path) | yes | — | non-blank; created if absent |
| `output.baseName` | string | yes | `"terrain"` | `TerrainExportBaseName.Validate` |
| `networkTimeoutSeconds` | integer | no | `300` | positive |

### Template

Written verbatim, byte for byte, from `RevitSettingsIo.TemplateJson` — the only time `EnsureTemplateExists`
ever writes this file is when it is entirely absent:

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

A `fetch`-mode settings file (not the shipped template) sets `"mode": "fetch"`, `"process": null`, and
populates `areaOfInterest.boundingBox` or `.radius` instead of `.parcel` — `process` is never read once
`mode` is `"fetch"`, but a present-and-non-null `process` object is not itself rejected.

### The live ExampleSite dependency on `OPENTOPOGRAPHY_API_KEY`

`fetch` mode resolves the key through `SolidGround.Core.Sources.OpenTopography.EnvironmentOpenTopographyApiKeyProvider`
— strictly `Environment.GetEnvironmentVariable("OPENTOPOGRAPHY_API_KEY")`, trimmed, empty-after-trim treated
as absent. This is a different, narrower resolution chain than the CLI's own: `SolidGround.Cli`'s
`CliOpenTopographyApiKeyProvider` additionally falls back to the `.NET` user-secrets file when the environment
variable is unset; `SolidGround.Revit` has no such fallback and never will, since user secrets are a
development-time `dotnet` tooling concept with no natural analog inside a Revit session. In practice this
means the variable must be present in **the `Revit.exe` process's own environment** — not merely a terminal
session that happens to be open at the same time, and not the account's persisted user/machine environment
variable unless Revit was launched (or the machine last signed in) after it was set. Revit does not inherit an
already-running shell's session variables retroactively.

Document Preflight (Stage 1) checks only that a key is present — `GetApiKey() is not null` — and only when
`mode` is `"fetch"`; it never reads the key's value into any string this command touches, and no code path
anywhere in `SolidGround.Revit` echoes, logs, or persists it. The prepared manual-evidence harness (outside
this repository) launches Revit with the variable scoped to the launched child process only
(`Start-Revit2027.ps1 -EnvironmentVariable @{ OPENTOPOGRAPHY_API_KEY = ... }`), with a documented `setx`
fallback for when that is not practical — see "Manual evidence plan," step 8b, below. No `.env` file is ever
read by `SolidGround.Revit` or by the harness.

## Command flow

`CreateToposolidCommand.Execute(ExternalCommandData, ref string message, ElementSet)` —
`[Transaction(TransactionMode.Manual)]` plus `[Regeneration(RegenerationOption.Manual)]`, unchanged from
Issue #14. `message` is deliberately left at its caller-provided empty value on every return path: Revit only
shows its own automatic result dialog when `message` is non-empty, so leaving it empty keeps every outcome to
exactly the one `TaskDialog` the command constructs itself. The outer
`catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)` in `Execute`
always returns `Result.Cancelled`: every post-`Start()` exception is already handled inside
`RunTransaction`'s own try/catch (see "Transaction and failure handling" above), so nothing that reaches this
outer catch can have opened a `Transaction`.

### Stage 1 — Document Preflight (read-only, no network, no transaction)

`RunDocumentPreflight` accumulates every problem it finds into one list rather than stopping at the first,
with three exceptions that return early (a structural problem makes every later check meaningless): no
usable active document, the settings file having just been written, and a settings write or load failure.
The no-document return is deferred until after settings load — steps 2 and 3 below still run with no
document, so a missing document never masks a settings problem — which means steps 4 through 7 below only
run once step 1 found a usable, non-family document. In order:

1. Active document present, not a family document.
2. `RevitSettingsIo.EnsureTemplateExists` — three outcomes: template just written (one problem, "edit and
   rerun," **early return**), write failed (a distinct settings-lock or write-access problem, **early
   return**), or the file was already present.
3. `RevitSettingsIo.TryLoad` — a decode or validation failure folds every problem into the list (**early
   return**).
4. `mode == Fetch` → `EnvironmentOpenTopographyApiKeyProvider().GetApiKey()` resolvable.
5. `AoiSettingsFactory.Build(settings.Request.AreaOfInterest, wgs84Reference, parcelText)` — parcel text is
   read via `File.ReadAllText` only when `kind == parcel`; a read or construction failure is one more
   accumulated problem, not a return.
6. `process` mode only: `process.asc`/`.prj`/`.sourceJson` existence, each checked individually (added in
   review beyond the original design record, so a missing process-mode file surfaces here, identically
   worded, instead of later from Stage 2's acquisition-failure path).
7. `LevelAndTypeResolver.ResolveLevel`/`ResolveToposolidType` against the configured names.
8. If every check above accumulated zero problems: `OrphanCheck.Capture(document)` — the orphan-check
   baseline.

Steps 4 through 7 can all contribute problems to one combined dialog; only steps 2 and 3 short-circuit with a
single-cause dialog, since nothing past a settings failure can be meaningfully checked. Any problem →
`ShowProblemList("SolidGround Preflight found a problem.", ...)` (the existing `ProblemReportDialog.BuildRejectionBody`,
capped at 8 inline lines, full list to the log folder) → `Result.Cancelled`. Nothing past this stage runs.

### Stage 2 — Acquisition (network/file I/O; the only stage using the synchronous bridge)

Before running, `mode == Fetch` logs (and the eventual failure dialog, if any, implicitly reflects) that Revit
will be unresponsive for up to `networkTimeoutSeconds` while the request runs:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(request.NetworkTimeoutSeconds));
acquisition = Task.Run(() => RunPipelineAsync(request, wgs84Reference, aoi, cts.Token), cts.Token)
    .GetAwaiter().GetResult();
```

- **Fetch**: `ClipRegionFactory.BuildFetchEnvelope` → `OpenTopographyUsgs1mSource.AcquireDetailedAsync` →
  cast `.Acquisition.Data` to `ElevationGrid`; `ProjNetHorizontalCoordinateTransformFactory.Create` from the
  fixed WGS 84 WKT and the acquisition's own returned WKT; a defensive check that the built transform's
  target reference equals the grid's own horizontal reference throws `InvalidOperationException` otherwise.
- **Process**: reads `.asc`/`.prj`/the optional sidecar (see "Process mode mirrors the CLI's sidecar
  auto-detection" above); `WellKnownTextReferenceParser.Parse`; `VerticalReferenceResolution.Resolve`
  (wrapped in its own `try`/`catch (FormatException)` that translates the CLI-flavored message into one
  naming `process.verticalDatum`/`process.verticalUnit`/`process.sourceJson` instead of
  `--vertical-datum`/`--vertical-unit`/`--source-json`); `AaiGridParser.Parse`.
- Either mode: `TerrainProcessingPipeline.RunAsync(...)` → `TerrainProcessingOutcome`.
- Every exception this stage can throw — `OpenTopographyException`, `FormatException` (including the
  translated vertical-reference message), `IOException`, `OperationCanceledException` (the timeout),
  `InvalidOperationException` (the grid/transform mismatch check above, plus `GridClipException` and
  `TerrainProvenanceException`, both of which derive from `InvalidOperationException`) — is caught by one
  `catch (Exception ex) when (IsAcquisitionFailure(ex))` and shown as a single-cause dialog; see "Error
  catalogue" below for the exact headline/detail split. **Stop.**

### Stage 3 — Geometry Preflight (Core-only; still no Revit API call)

1. `LocalBoundary boundary = LocalBoundaryFactory.FromPolygonalRegion(outcome.ClipResult.EffectiveRegion,
   localFrame)` when a clip actually ran, else `LocalBoundaryFactory.FromGridEnvelope(grid, localFrame)` — the
   whole-grid fallback, which applies only when `process` mode configured no AOI at all (`fetch`/`run` always
   have one, since an AOI is how the fetch envelope itself is built).
2. `LocalBoundaryValidator.Validate(boundary, payload.Samples, pointBudget, containmentTolerance: null)` — any
   problem → one capped dialog ("SolidGround could not build a valid boundary."), `Result.Cancelled`. **Stop.**
3. `new FileSystemTerrainExporter(output.Directory, output.BaseName).ExportAsync(payload, CancellationToken.None)`
   — written **before the transaction**, so a failure here leaves nothing Revit-side touched. A defensive
   `ArgumentException` from the constructor (Stage 1's settings validation should already have prevented this)
   gets its own headline, distinct from the boundary-validation text, per a review fix landed with the
   command rewrite. A `TerrainExportException` from the export itself gets a directory-naming headline.

### Stage 4 — Geometry construction (pre-transaction; `Document` untouched)

1. `ForgeTypeId revitUnit = RevitUnitConversion.ToForgeTypeId(request.OutputUnit)`.
2. `IList<XYZ> points = BoundaryGeometryBuilder.BuildPoints(payload.Samples, revitUnit)`.
3. `double constantZInternal = points.Min(p => p.Z)` — see "The boundary-Z decision" above. `payload.Samples`
   is guaranteed non-empty by this point (Stage 2's `TerrainProvenanceException` and Stage 3's
   `LocalBoundaryValidator` both already reject empty/insufficient terrain earlier), so this call is always
   safe.
4. `IList<CurveLoop> profiles = BoundaryGeometryBuilder.BuildProfiles(boundary, constantZInternal, revitUnit)`.
5. `BoundingBoxXYZ expected = BoundaryGeometryBuilder.ComputeExpectedBoundingBox(points)` — computed purely
   from `points`, never from `profiles`.
6. `PostCreationVerification.AllProfilesArePlanar(profiles, out problem)` — a coding-error guard, not a
   data-driven one (see "The boundary-Z decision"); a failure here shows the same "could not build a valid
   boundary" headline as Stage 3's own boundary-validation dialog, `Result.Cancelled`. **Stop.**

### Stage 5 — Transaction (the only stage that mutates `Document`)

See "Transaction and failure handling" and "Transaction status and the Result-code policy" above for the
full mechanism. Summary: `Transaction.Start()` not `Started` → immediate `Result.Cancelled`, nothing began.
Otherwise: `ToposolidCreationService.Create` → `document.Regenerate()` → `PostCreationVerification.Verify`
(and the failure-preprocessor's own blocking-failure flag) → the Issue #16 hook → `transaction.Commit()`,
all inside one `try` whose two `catch` clauses derive `Result` from the observed `TransactionStatus`. A
`Commit()` that returns anything but `Committed` is `Result.Failed` with no further `RollBack()` attempt (the
transaction has already ended, one way or another).

### Stage 6 — Success (only after a confirmed `Committed` status)

`ReportSuccess` never lets an exception past its own catastrophic-exclusion catch filters: the modeling action
is already durable by this point, so nothing here may flip `Result` away from `Succeeded`.

1. `PlacementRecordDraft.ToRecord(toposolid.Id.Value, DateTime.UtcNow)` → `PlacementRecordWriter.Write(...)`,
   written next to the export bundle. A write failure is logged only; the success dialog's own "Placement
   record:" line reads "could not be written (see log)" instead of a path.
2. `OrphanCheck.Unchanged(before, OrphanCheck.Capture(document), out problem)` — logged (info if unchanged,
   warning if not), never itself a rollback trigger, since the change is already durably committed.
3. `AddInLog.Info(...)` — build identity, Level/Type chosen, retained/original point counts, `toposolid.Id`.
4. One `TaskDialog`: element id, Level/Type names, point count (retained of original, with budget), export
   bundle path, placement-record path (or the "could not be written" line), log directory, and the retained
   site-form/not-a-survey-instrument disclaimer.

## Placement record schema

Written to `<output.directory>\<output.baseName>.revit-placement.json` by `PlacementRecordWriter.Write`, only
after a confirmed `Committed` status. Indented UTF-8, no BOM, `\n` newlines, camelCase property names, fixed
property order — mirroring `TerrainExportBundleRenderer`'s own conventions, never a reflection-based
serializer.

```jsonc
{
  "schema": "solidground.revit-placement",
  "schemaVersion": 1,
  "createdUtc": "2026-09-21T04:00:00Z",
  "exportDocument": "terrain.solidground.json",
  "exportPoints": "terrain.points.csv",
  "toposolid": {
    "elementId": 123456,
    "levelName": "Level 1", "levelId": 111111,
    "toposolidTypeName": "Site - Existing", "toposolidTypeId": 222222,
    "creationStrategy": "combinedOverload"
  },
  "unitConversion": {
    "outputUnit": "usSurveyFoot",
    "forgeTypeId": "autodesk.unit.unit:usSurveyFeet-1.0.1",
    "metersPerOutputUnit": 0.3048006096012192,
    "roundTripDelta": 0.0
  },
  "localOrigin": {
    "sourceX": [withheld], "sourceY": [withheld], "sourceElevation": 183.10,
    "horizontalReference": "PROJCS[...]",
    "verticalReference": { "datum": "NAVD88", "unit": "meter", "geoidModel": null }
  },
  "boundaryPlaneElevation": {
    "constantZInternal": 0.33,
    "source": "minimumRetainedSampleElevation",
    "levelElevationInternal": 0.0,
    "note": "Every boundary CurveLoop vertex shares this one internal-unit Z, the minimum of the retained terrain samples' own local elevation (not the resolved Level's Elevation, recorded here only for reference); terrain shape comes entirely from the points array."
  },
  "revitCoordinates": {
    "internalOriginIsZero": true,
    "basePointPosition": {"x":0,"y":0,"z":0}, "basePointSharedPosition": {"x":0,"y":0,"z":0},
    "surveyPointPosition": {"x":0,"y":0,"z":0}, "surveyPointSharedPosition": {"x":0,"y":0,"z":0},
    "activeProjectLocationName": "Internal"
  },
  "sharedCoordinatesStatement": "SolidGround made no change to ActiveProjectLocation, the project base point, the survey point, or site location during this run.",
  "pointCounts": { "original": 48213, "retained": 14998, "budget": 15000 }
}
```

`toposolid.creationStrategy` is `"combinedOverload"` or `"profilesThenSlabShapeEditor"`, matching
`ToposolidCreationService.DefaultStrategy`'s value at the time of the run (see "Manual evidence plan," step
8a, for how this default may change). `unitConversion.roundTripDelta` is the automatic self-consistency check
described under "Unit conversion" above, always expected near zero.

**Reversibility.** `source = localOrigin + (revitInternalPoint converted back to the output unit via
unitConversion, then to source units via the same length-conversion component every other export uses)` —
exactly `LocalCoordinateFrame.ToSource`, fed by `UnitUtils.ConvertFromInternalUnits`'s inverse.
`ActiveProjectLocation`/`BasePoint`/`SurveyPoint`/`SiteLocation` are only ever **read**, never written,
matching the locked design's "leave shared coordinates untouched" rule; `sharedCoordinatesStatement` states
this explicitly rather than leaving it implicit.

**Repeated-run alignment.** `localOrigin.kind: "southwest"`/`"centroid"` reproduces the same origin between
two runs only if the grid's own corner envelope is identical both times — exactly true for `process` mode
against the same `.asc`, only approximately true for `fetch` mode (OpenTopography's returned extent can vary
slightly after minimum-envelope padding). For a placement meant to be reproduced identically later,
`localOrigin.kind: "explicit"` with a prior run's own recorded `localOrigin.sourceX`/`sourceY`/`sourceElevation`
pasted back into settings.json is the only guaranteed-stable choice.

## Error catalogue

Doc Preflight (Stage 1) funnels every one of its own problems into **one shared dialog**,
`"SolidGround Preflight found a problem."`, with the distinguishing text appearing as one capped inline
problem line (never as a separate dialog headline) — rows 1 through 9 below all share that one `MainInstruction`.
Acquisition (Stage 2) funnels every one of its own exception types into a **shared headline per exception
category** (rows 10 through 12), with the distinguishing text as the dialog's body detail. Every later stage
shows its own distinct headline.

| # | Scenario | Stage | `Result` | What is shown |
| --- | --- | --- | --- | --- |
| 1 | No active / family document | Doc Preflight | Cancelled | shared dialog; problem line names which |
| 2 | Settings template just written | Doc Preflight | Cancelled | shared dialog (shown alone: early return); "A starting template was written to '\<path\>'. Edit it and run this command again." |
| 3 | Settings lock could not be acquired within 5 seconds | Doc Preflight | Cancelled | shared dialog (shown alone: early return); names the `Global\` mutex, asks to retry |
| 4 | Settings directory not writable (`UnauthorizedAccessException`) | Doc Preflight | Cancelled | shared dialog (shown alone: early return); names this file by path as the manual-template fallback |
| 5 | Settings JSON fails strict decode / semantic validation | Doc Preflight | Cancelled | shared dialog (shown alone: early return); every `Validate()` problem, one per line |
| 6 | `OPENTOPOGRAPHY_API_KEY` missing (fetch mode) | Doc Preflight | Cancelled | shared dialog; problem line |
| 7 | AOI construction fails (bad bbox/radius/parcel geometry, or an unreadable parcel file) | Doc Preflight | Cancelled | shared dialog; problem line names the constructor's own message |
| 8 | `process.asc`/`.prj`/`.sourceJson` missing | Doc Preflight | Cancelled | shared dialog; one problem line per missing file |
| 9 | Level / ToposolidType unresolved | Doc Preflight | Cancelled | shared dialog; one problem line per unresolved kind |
| 10 | Network/OpenTopography failure, grid/transform reference mismatch, `GridClipException`, `TerrainProvenanceException`, or a translated vertical-reference `FormatException` | Acquisition | Cancelled | "SolidGround could not acquire terrain data." + `ex.Message` (already the translated sentence for the vertical-reference case) |
| 11 | Fetch-mode timeout (`OperationCanceledException`) | Acquisition | Cancelled | "The request did not complete within \<N\> seconds." + a suggestion to raise `networkTimeoutSeconds` |
| 12 | Local file unreadable (content read, not existence — an `IOException`) | Acquisition | Cancelled | "Could not read a configured file." + `ex.Message` |
| 13 | `LocalBoundaryValidator` problems | Geometry Preflight | Cancelled | "SolidGround could not build a valid boundary." + capped list |
| 14 | Output directory/base name invalid at export-construction time (defensive) | Geometry Preflight | Cancelled | "SolidGround could not use the configured output settings." + message |
| 15 | Export-bundle write failure | Geometry Preflight | Cancelled | "The terrain export could not be written to '\<directory\>'." + `ex.Message` |
| 16 | Defensive planarity check fails (construction bug only, never terrain data) | Geometry construction | Cancelled | same "could not build a valid boundary" headline as row 13 |
| 17 | `Transaction.Start()` not `Started` | Transaction | Cancelled | "SolidGround could not start a Revit transaction." + observed status |
| 18 | Post-create bounding-box or slab-shape-vertex-count mismatch | Transaction | Cancelled if `RolledBack`, else Failed | "The created toposolid's geometry did not match the source data; the change was undone." (or the generic Failed text) |
| 19 | `IFailuresPreprocessor` observed a blocking (`Error`/`DocumentCorruption`) failure | Transaction | Cancelled if `RolledBack`, else Failed | "Revit reported a problem while creating the toposolid." + joined failure messages |
| 20 | `Toposolid.Create`/`AddPoints` throws (`ToposolidCreationException`) | Transaction | Cancelled if `RolledBack`, else Failed | "Revit rejected the generated toposolid boundary or points." + inner exception message |
| 21 | `Regenerate()`, the Issue #16 hook, or `Commit()` itself throwing | Transaction | Cancelled if `RolledBack`, else Failed | "SolidGround hit a problem while finishing the toposolid." + `ex.Message` |
| 22 | `Commit()` returns anything but `Committed` | Transaction | Failed | "SolidGround could not confirm whether the toposolid was created. Check the document and Undo if needed." (no further `RollBack()` attempted) |
| 23 | Placement-record write fails post-commit | Post-commit | **Succeeded** | logged only; success dialog's own line says "could not be written (see log)" |
| 24 | Orphan check detects an unexpected shared-coordinate change post-commit | Post-commit | **Succeeded** | logged only (info if unchanged, warning if not); never shown in any dialog |
| 25 | Unhandled exception anywhere before Stage 5 opens a transaction, or any other bug the code above cannot name | any | Cancelled | `Execute`'s own outer catch: "SolidGround hit an unexpected problem and stopped. Nothing in the model changed." + `ex.Message` |
| 26 | Success | Post-commit | Succeeded | element id, Level/Type, point counts, bundle/placement-record/log paths, disclaimer |

## Manual evidence plan

All three steps below run first in **`process` mode** against the committed
`tests/SolidGround.Tests/Fixtures/example-site-synthetic.*` fixtures — offline, deterministic, no key needed. The
live ExampleSite `fetch`-mode run is attempted once `OPENTOPOGRAPHY_API_KEY` is present in the Revit process
environment (see "The live ExampleSite dependency" above); it is reported as the remaining acceptance item if
still absent when this plan is executed, rather than skipped silently. A prepared, read-only-by-default
harness already exists outside this repository for exactly this plan (scripts to launch/close Revit 2027,
find and capture dialogs, click both WPF ribbon controls and native `TaskDialog` buttons, read
`Revit.ini`/the add-in's `%ProgramData%` log, and redact-check the launcher's own environment-variable
handling); this note records the plan and every step's expected observation, not the harness's own
implementation.

Every "**Evidence.**" paragraph below is a placeholder. This documentation stage did not launch Revit and did
not run any of these steps; each is left for the manual session that follows, per this milestone's own
implementation sequencing (manual verification, then docs, with the evidence-dependent sections of this note
filled in afterward).

### Step 5 — `Result.Cancelled`/`Result.Failed` Undo-stack behavior (settles verification item 2)

Two throwaway stand-in commands sharing `CreateToposolidCommand`'s exact `[Transaction(TransactionMode.Manual)]`/
`[Regeneration(RegenerationOption.Manual)]` attribute pair, kept entirely outside this repository.

1. **Baseline.** Perform one ordinary undoable edit (move an element / change a parameter); confirm
   "Undo \<action\>" is available.
2. **Zero-transaction `Cancelled`.** Point settings at a nonexistent `process.asc` (a guaranteed Preflight
   rejection); run; confirm `Result.Cancelled`; press Ctrl+Z once; confirm the baseline edit reverts
   normally.
3. **Rolled-back `Cancelled`.** Force Preflight to pass but the post-create bounding-box check to fail (a
   scratch settings/fixture copy with an artificially wrong expected-extent tolerance) so the command opens a
   transaction, calls `RollBack()`, and observes `RolledBack`; repeat the baseline-edit-then-Ctrl+Z check and
   confirm it behaves identically to scenario 2.
4. **Zero-transaction `Failed`.** A throwaway command that never opens a transaction but hardcodes
   `return Result.Failed;`; repeat the baseline-edit check; observe whether the baseline edit itself is
   affected.
5. **Commit-then-`Failed`.** A throwaway command that opens and commits one trivial, reversible transaction
   and then still hardcodes `Result.Failed`; observe the same.
6. **Post-`Start()`-exception `Cancelled`, not from `Create` or verification.** A throwaway command that opens
   a transaction, performs one trivial, reversible edit (so the transaction has genuinely mutated something),
   then deliberately throws an unrelated exception (for example `throw new InvalidOperationException("probe")`)
   before calling `Commit()`. Confirm the widened `catch (Exception ex)` in `RunTransaction` calls
   `RollBack()` (since `HasEnded()` is false at that point), observes `RolledBack`, and returns
   `Result.Cancelled`; repeat the baseline-edit-then-Ctrl+Z check and confirm it behaves identically to
   scenario 3. Also record whether `transaction.HasEnded()` ever reports `true` immediately after this
   specific throw (it should not, since nothing here calls `Commit()`/`RollBack()` before the throw).

Record every Undo tooltip verbatim. Apply the finding to "Transaction status and the Result-code policy"
above (it is written as a single, easily-flipped table row for exactly this reason).

**Evidence.** Not yet collected.

### Step 7 — `Revit.ini` threshold values and the point-count boundary (settles verification item 9)

1. Read `%APPDATA%\Autodesk\Revit\Autodesk Revit 2027\Revit.ini`'s `[Misc]` section for the real
   `NativeToposolidMaxPointThreshold` (expected default `20000`, but must be read, not assumed). A read-only
   check of this file, taken before Issue #15's implementation began, already recorded
   `NativeToposolidMaxPointThreshold=20000` and `LinkToposolidMaxPointThreshold=20000` on the development
   machine — a preparatory fact, not a substitute for re-reading it as part of this step, and not itself
   evidence of what `Toposolid.Create` does at or past that count.
2. Generate three synthetic, non-committed `.asc` fixtures whose valid (non-`NODATA`) cell count is exactly
   `threshold-1`, `threshold`, `threshold+1` (a rectangular grid with headroom, excess cells concentrated on
   one edge as `NODATA_value` to keep the retained region one connected footprint; elevation on a smooth
   deterministic ramp/sine of row/column, never a flat plane; reuse `example-site-synthetic.prj` verbatim for all
   three).
3. For each grid: `process` mode, `areaOfInterest.kind: "boundingBox"` sized to fully cover it,
   `simplification.pointBudget` set well above the grid's own valid-cell count (within the 50,000 ceiling) so
   `GridTerrainSimplifier`'s retain-all branch passes every valid cell through unsimplified — giving an exact,
   deterministic retained count equal to the grid's own valid-cell count at each of the three targets.
4. Run once per grid. Observe: does `Toposolid.Create`/`AddPoints` throw, silently truncate (compare
   `PostCreationVerification`'s read-back slab-shape-vertex count), or succeed unaffected at each of the three
   counts; does `ToposolidCreationFailurePreprocessor`'s captured list contain anything toposolid-related in
   the over-threshold run — specifically watching for `BuiltInFailures.SlabShapeFailures.*` or
   `BuiltInFailures.SiteFailures.ToposolidSlopeExceedsThreshold` (a slope failure, unrelated to point count,
   not to be conflated with the threshold question).
5. Record the read `Revit.ini` value and all three outcomes verbatim below; correct AGENTS.md's point-budget
   guidance only if evidence contradicts it, per AGENTS.md's own governance rule.

**Evidence.** Not yet collected.

### Step 8 — ExampleSite end to end, Option A/B probe, unit round trip (settles verification items 15, 16), plus the orphan check

Split to conserve OpenTopography's finite daily quota.

**8a — synthetic mechanics (process mode, no network, repeatable).**

1. Build one small synthetic `.asc` where a duplicate-XY pair and an out-of-boundary point can be placed
   precisely by construction.
2. Run with `ToposolidCreationStrategy.CombinedOverload` (the shipped default), then again with
   `ProfilesThenSlabShapeEditor` (a temporary local flip of `ToposolidCreationService.DefaultStrategy`),
   against the fixture and its duplicate-XY/out-of-boundary variants, using a scratch bypass of Core's own
   `LocalBoundaryValidator` (which would otherwise reject such input before Revit is ever reached — the point
   of this probe is specifically what Revit itself does when SolidGround's own defense-in-depth is bypassed).
   Observe which path throws, clamps, or silently accepts each invalid case. This result decides whether
   `CombinedOverload` stays the shipped default, per the locked design's own "flip only if Option A silently
   accepts invalid points, with the reason recorded" rule.
3. Log `UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Feet)` and
   `UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.UsSurveyFeet)`; whichever equals exactly `1.0` is Revit's
   true internal foot (see "Unit conversion" above).
4. Observe whether `toposolid.get_BoundingBox(null)` returns non-null immediately after
   `document.Regenerate()`, and whether `GetSlabShapeEditor().IsEnabled` is true after the combined `Create`
   call — `PostCreationVerification` already degrades gracefully either way (a null bounding box is logged as
   a warning, not a failure; a disabled slab-shape editor skips the per-vertex check).
5. **Boundary-Z persistence** (see "The boundary-Z decision and its open empirical question" above). Using
   the same fixture, compare the created `Toposolid`'s actual geometry at or near the parcel edge against its
   interior, terrain-driven shape (for example via `SlabShapeVertices` near the boundary, or a visual/dimension
   check). Record whether the flat, single-scalar boundary `Z` persists as a visible rim at the edge, or is
   fully overridden by point-driven interpolation everywhere the points reach.
6. **Orphan check.** Compare `OrphanCheck.Capture(document)` before and after each run in this section;
   confirm it matches the automatic in-command log line (`OrphanCheck.Unchanged`'s own info/warning message).
   This cross-checks the automatic check itself, not only terrain-creation correctness.

**Evidence.** Not yet collected.

**8b — live acceptance (fetch mode, exactly one run).**

1. Precondition: `OPENTOPOGRAPHY_API_KEY` visible to the **Revit.exe process itself** — see "The live
   ExampleSite dependency on `OPENTOPOGRAPHY_API_KEY`" above.
2. `settings.json`: `"mode": "fetch"`, an AOI centered on `[withheld], [withheld]` (the reference parcel), sized like
   the existing synthetic parcel (approximately [withheld] sq ft), the creation strategy left at whatever 8a
   settled.
3. Run once. Confirm the full pipeline end to end: acquisition, clip, simplify, export bundle, boundary
   build, transaction, creation, post-create verification, commit, placement record, success dialog; and that
   the created geometry matches the source raster within `ProjNetHorizontalCoordinateTransformFactory`'s
   existing round-trip tolerance.
4. Record the full run (settings used, dialog text, log excerpt, placement record — the key already redacted
   by construction) below. If the key is still absent when this step is reached, report that explicitly
   rather than skip silently; it is the one acceptance item this milestone cannot close offline.

**Evidence.** Not yet collected.

## Revit API members used

Every entry below was confirmed by `MetadataLoadContext` reflection against the installed Revit 2027
(`27.0.10.13`) `RevitAPI.dll`/`RevitAPIUI.dll` and, for the rows marked with an Autodesk URL, independently
against Autodesk's current `Revit-API-MainReference` pages (all of which report themselves as
**Assembly: RevitAPI (in RevitAPI.dll) Version: 27.2.0.0**, the same version-gap caveat every other Phase 2
note in this repository already carries against the installed `27.0.10.13`). "Verified in installed dump"
means member existence and static signature only — never runtime behavior, and never confirmed by
compilation; every multi-statement code excerpt elsewhere in this note is reflection-verified for member
existence, not compiler-verified independently of the real build (which itself does compile — see
"Verification" below).

| Revit API member | Installed-dump status | Autodesk 2027 documentation |
| --- | --- | --- |
| `Toposolid.Create(Document, IList<CurveLoop>, IList<XYZ>, ElementId, ElementId)` (combined) | Verified, signature matches | [Toposolid.Create Method (combined)](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/0d4cd6ef-eadd-ace6-2999-2270c1317fb1.htm) — documents boundary-validity `ArgumentException` conditions (closed/simple/individually-closed/planar/horizontal profiles, no helical curve), level/type/sketch failures; does **not** state whether `points` must lie inside `profiles` |
| `Toposolid.Create(Document, IList<CurveLoop>, ElementId, ElementId)` (profiles-only) | Verified, signature matches | [Toposolid.Create Method (profiles-only)](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/35fc44fe-c86f-6963-c2b2-e6290288c748.htm) — identical exception table to the combined overload |
| `Toposolid.Create(Document, IList<XYZ>, ElementId, ElementId)` (points-only, not used by this design) | Verified, signature matches | [Toposolid.Create Method (points-only)](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/fb528545-b035-bfe2-71da-88baa9a979ff.htm) — `ArgumentException` if fewer than 3 points, otherwise the same level/type/sketch conditions |
| `Toposolid.CreateFromTopographySurface(...)` (not used) | Verified | [Toposolid.CreateFromTopographySurface Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/662a7ccb-63b9-a106-1b3e-659af6a35c70.htm) |
| `Toposolid.GetSlabShapeEditor()` | Verified | [Toposolid.GetSlabShapeEditor Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/4b7c441c-9f4f-4756-14bc-5fc387043c3e.htm) |
| `Element.get_BoundingBox(View)` (`Element.BoundingBox` is **not** a parameterless property) | Verified, re-confirmed by a targeted `GetIndexParameters()` probe | [Element.get_BoundingBox Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/def2f9f2-b23a-bcea-43a3-e6de41b014c8.htm) — Autodesk's own C# syntax block renders it as an indexer, `this[View A_0]`; "if the model box is not known, this will return null" |
| `BoundingBoxXYZ` ctor, `Min`/`Max`/`Bounds`/`Transform`/`Enabled`/`MinEnabled`/`MaxEnabled`/`BoundEnabled`/`IsSet` | Verified | [BoundingBoxXYZ Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/3c452286-57b1-40e2-2795-c90bff1fcec2.htm) |
| `SlabShapeEditor.Enable(): void` | Verified — confirmed **not fluent** | [SlabShapeEditor.Enable Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/792bbdb8-4629-7383-fbab-341df4b02341.htm) — `void`, no documented exceptions or remarks |
| `SlabShapeEditor.AddPoints(IList<XYZ>): IList<SlabShapeVertex>` | Verified | [SlabShapeEditor.AddPoints Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/52f630ac-2e57-4b33-7776-d499d469630d.htm) — **Remarks: "points should be distinct on the x-y plane, and they should be inside the element boundary. SlabShapeEditor must be enabled before calling this method. Regenerate the document after element creation if this method is called in the same transaction."** |
| `SlabShapeEditor.AddPoint(XYZ)`, `.ResetSlabShape()` (not used) | Verified | [SlabShapeEditor Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/06308ccc-46e7-6ff8-582c-6891af8b75e9.htm) — not individually opened |
| `SlabShapeEditor.SlabShapeVertices` | Verified | [SlabShapeEditor.SlabShapeVertices Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/01fbf5d9-6fa7-6483-6a1c-5cf439f27dc7.htm) |
| `SlabShapeEditor.IsEnabled` | Verified | [SlabShapeEditor.IsEnabled Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/9aaaf1ca-5f52-c5be-9d5b-2230ad5131cc.htm) — "if true, the creases and vertices are accessible and modifiable" |
| `SlabShapeVertex.Position` (get-only) | Verified | [SlabShapeVertex.Position Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/194184c3-4274-cd07-5353-5b65500024db.htm) |
| `SlabShapeVertex.VertexType` (not used) | Verified | [SlabShapeVertex Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/8c022b91-723f-045d-3024-8cb037a41acc.htm) — not individually opened |
| `UnitTypeId.Feet` | Verified | [UnitTypeId.Feet Field](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/b7f9017c-b8a0-5004-4496-4c07a00c3659.htm) — description only "Feet," no numeric-definition or internal-unit remark |
| `UnitTypeId.UsSurveyFeet` | Verified | [UnitTypeId.UsSurveyFeet Field](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/8b8dbb87-a077-0de9-c9c2-1e77e8f146ab.htm) — same absence of a numeric/internal-unit remark |
| `UnitTypeId.Meters` | Verified | (class page only, not individually opened) |
| `ForgeTypeId` ctors, `TypeId`, `Empty()`, `NameEquals`, `StrictlyEquals` | Verified | [ForgeTypeId Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/d9fcf276-9566-de83-2b0b-d89b65ccc8af.htm) |
| `UnitUtils.ConvertToInternalUnits(double, ForgeTypeId)` | Verified | [UnitUtils.ConvertToInternalUnits Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/b5e8d065-d274-62f8-7b5d-89722f7c44f3.htm) — `ArgumentException` if the value is not finite or the unit id is not a real unit identifier |
| `UnitUtils.ConvertFromInternalUnits(double, ForgeTypeId)` | Verified | [UnitUtils.ConvertFromInternalUnits Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/60c6aac3-8306-c56e-b62f-b7011b9ad7b6.htm) — same two exceptions |
| `UnitUtils.Convert(double, ForgeTypeId, ForgeTypeId)` (not used directly) | Verified | [UnitUtils Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/128dd879-fea8-5d7b-1eb2-d64f87753990.htm) |
| `TransactionStatus` (`Uninitialized=0, Started=1, RolledBack=2, Committed=3, Pending=4, Error=5, Proceed=6`) | Verified | [TransactionStatus Enumeration](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/29b9a7a8-6754-8310-e063-622b569bb6d5.htm) |
| `Transaction` ctors, `Start()`, `Commit()`, `RollBack()`, `GetFailureHandlingOptions()` | Verified | [Transaction Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/308ebf8d-d96d-4643-cd1d-34fffcea53fd.htm) — Remarks confirm the `Dispose()` auto-rollback behavior and explicitly advise against relying on it |
| `Transaction.GetStatus()` | Verified | [Transaction.GetStatus Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/fdf98941-eee4-d8af-e3f7-5b6c7ccc3c74.htm) — "if the status was set to Pending... it will be changed later... asynchronously" |
| `Transaction.HasStarted()` | Verified | [Transaction.HasStarted Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/425a8103-a11b-4c45-f002-0e7bc602d074.htm) — "may return True even after Commit/RollBack... if the method returned TransactionStatus.Pending" |
| `Transaction.HasEnded()` | Verified | [Transaction.HasEnded Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/0287f338-0d0c-aff2-c75b-0aefe452969d.htm) — documented equivalence: `HasEnded()` ⇔ `GetStatus() ∈ {Committed, RolledBack}` |
| `Transaction.SetFailureHandlingOptions(FailureHandlingOptions): void` | Verified | [Transaction.SetFailureHandlingOptions Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/1e913cca-f75b-8dfb-b172-5a04f3732b85.htm) — "can be set at any time before... committed or rolled back"; the one published example calls it *after* `Start()` |
| `FailureHandlingOptions` — no public constructor; `SetFailuresPreprocessor`/`SetClearAfterRollback` fluent | Verified — class page has no Constructors section | [FailureHandlingOptions.SetFailuresPreprocessor](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/0647c18e-c1ad-60b8-d993-cb464b7b676e.htm), [.SetClearAfterRollback](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/bebe6efd-b05f-7a0b-4cc3-609ec35be42c.htm), [class page](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/c03bb2e5-f679-bf24-4e87-08b3c3a08385.htm) |
| `IFailuresPreprocessor.PreprocessFailures(FailuresAccessor): FailureProcessingResult` | Verified | [IFailuresPreprocessor Interface](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/053c6262-d958-b1b6-44b7-35d0d83b5a43.htm), [.PreprocessFailures Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/56e273aa-7d84-4a95-f06c-8a12e34e8be0.htm) — confirms `Continue`/`ProceedWithRollBack` are the sanctioned pair for this interface |
| `FailureProcessingResult` (`Continue=0, ProceedWithCommit=1, ProceedWithRollBack=2, WaitForUserInput=3`) | Verified | [FailureProcessingResult Enumeration](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/f147e6e6-4b2e-d61c-df9b-8b8e5ebe3fcb.htm) — the silent `Continue`/`WaitForUserInput` reinterpretation rules documented here apply to the separate, more privileged `FailuresProcessor` extension point, not to `IFailuresPreprocessor` |
| `FailuresAccessor.GetFailureMessages()`, `.DeleteWarning`, `.DeleteAllWarnings` | Verified | [FailuresAccessor Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/dea68b06-a061-fc05-d814-db741f2e7f14.htm) — instance is deactivated once `PreprocessFailures` returns; never cached |
| `FailureMessageAccessor.GetSeverity()`, `.GetDescriptionText()`, `.GetFailureDefinitionId()` | Verified | [FailureMessageAccessor Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/753477d8-b720-97a0-26f5-439d49de418c.htm) |
| `FailureSeverity` (`None=0, Warning=1, Error=2, DocumentCorruption=3`) | Verified | [FailureSeverity Enumeration](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/d0cdffe3-22c5-b764-8090-5104f044b000.htm) |
| `BuiltInFailures.SlabShapeFailures.*` (six members used: `SlabShapeWarnVerticesCoincident`, `.SlabShapeWarnVerticesDeleted`, `.SlabShapeFailedNotHorz`, `.SlabShapeFailedTooThin`, `.SlabShapeEditFailed`, `.SlabShapeEditFailedError`) | Verified | [BuiltInFailures.SlabShapeFailures Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/5aabd0a2-ad24-2456-c163-68bd06914073.htm) |
| `BuiltInFailures.SiteFailures.ToposolidSlopeExceedsThreshold` (unrelated to point count) | Verified | [BuiltInFailures.SiteFailures Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/0109bb6f-5cae-c271-989d-54be8b081669.htm) — message text is about thickness-display accuracy under extreme slope, not a hard block |
| `Level.Elevation` (get) | Verified | [Level.Elevation Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/b5d48a18-4aa9-7457-7a6a-6d4966eaf77f.htm) — "value is given in decimal feet" |
| `Level.ProjectElevation`, `Level.Create(Document, double)` (exists; never called) | Verified | [Level.ProjectElevation Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/895ef506-bfea-cc4e-31f8-aad2af6672e4.htm), [Level.Create Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/d661b7cd-dec8-6ae6-a753-b14ac2568772.htm) |
| `ToposolidType.GetContourSetting()`, `.SetContourSettting()` (triple-t, confirmed real spelling) | Verified | [.GetContourSetting](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/916e164a-0d63-6d1d-790b-08303219d9b9.htm), [.SetContourSettting](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/07e0d502-9a75-7a41-468e-a3de3a241259.htm) — the page title itself reads "SetContourSettting Method" |
| `FilteredElementCollector(Document).OfClass(Type)`, `.ToElements()` (no generic `OfClass<T>()`) | Verified | [.OfClass](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/b0a5f22c-6951-c3af-cd29-1f28f574035d.htm), [.ToElements](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/732b4a0d-62d8-b86d-120b-8ea3d9713b34.htm) — the doc's own worked example uses `typeof(Level)` verbatim |
| `BasePoint.GetProjectBasePoint(Document)`, `.GetSurveyPoint(Document)` (static), `.Position`, `.SharedPosition` | Verified | [BasePoint Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/154074ae-d653-aaff-b84b-6336a1cbafaa.htm) — `SharedPosition` is itself active-`ProjectLocation`-relative, reinforcing why `OrphanCheck` also separately captures `ActiveProjectLocation.Name` |
| `BasePoint.IsShared`, `.Clipped` (read only; always `false` for the project base point) | Verified | [BasePoint Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/154074ae-d653-aaff-b84b-6336a1cbafaa.htm) |
| `ProjectLocation : Instance`; `.GetSiteLocation()`, `.GetProjectPosition(XYZ)` | Verified — `GetTotalTransform`/`GetTransform` confirmed **inherited from `Instance`**, not declared on `ProjectLocation` | [ProjectLocation Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/1249d5fa-74f3-cf64-0a63-7ab370b67a5c.htm), [.GetSiteLocation](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/b15628ab-b246-233d-6587-9205d2ad04a3.htm) |
| `ProjectLocation.Name` (inherited from `Element.Name`, not declared on `ProjectLocation` itself) | Verified only via a targeted re-dump adding `ProjectLocation` to the reflection tool's inherited-member allow-list | [Element.Name Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/e372092e-ff47-71c2-1272-50ab08e5a41d.htm) — the page `ProjectLocation`'s own class page links to, annotated "(Inherited from Element)" |
| `SiteLocation.PlaceName` (used by `OrphanCheck`) | Verified | [SiteLocation.PlaceName Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/9a34156b-1fee-402a-01d2-8489132245c2.htm) — "The place name of the site." |
| `SiteLocation.Latitude`/`.Longitude` (**not used anywhere in this design**) | Verified, present | [.Latitude](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/0e39fbc4-e7c6-0c26-b7b3-70b4028fa927.htm), [.Longitude](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/f8183a87-88ed-234b-c91c-cb7775a87a7f.htm) — both in **radians**, not degrees; flagged for any future code path that does read them |
| `InternalOrigin.Get(Document)` (static); `.Position` | Verified, present | [.Get](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/ed465108-9467-3e78-8da4-8cdaa56e16cf.htm), [.Position](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/a31517d1-9456-1c63-c30c-98b2bf423d58.htm) — "always `(0,0,0)`" is near-certainly true by convention but is **not textually stated** on either the class or property reference page |
| `Document.ActiveProjectLocation` (read only) | Verified | [Document.ActiveProjectLocation Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/cd6733bb-4510-bb58-5ca5-21ededb30cdf.htm) |
| `Document.IsFamilyDocument` | Verified | [Document.IsFamilyDocument Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/076721f1-772c-f8ec-2097-c3e674b37537.htm) |
| `Document.Regenerate(): void` | Verified | [Document.Regenerate Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/22468e2c-9772-8478-0816-c9759aa43428.htm) — "when a transaction is committed there is an automatic call to regenerate the document"; `RegenerationFailedException` "must be aborted" |
| `Document.Delete(ElementId)` (not called by this design) | Verified | [Document.Delete Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/a0461dd1-71d9-4581-1604-2ef8c211dd60.htm) |
| `CurveLoop.Create(IList<Curve>)` (not used directly; `Append` is) | Verified | [CurveLoop.Create Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/5422ec92-2b9e-6b33-80ac-417b8336ae18.htm) — throws if input curves are "not contiguous" |
| `CurveLoop.Append(Curve): void` | Verified | [CurveLoop.Append Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/9ecde812-a299-b823-35fc-4428e9298602.htm) — same contiguity caveat |
| `CurveLoop.HasPlane(): bool` | Verified | [CurveLoop.HasPlane Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/69c92503-2025-ddab-ba91-3085aa2e8117.htm) — "true if the curve loop is planar" |
| `CurveLoop.IsOpen()` (not called by this design) | Verified | [CurveLoop.IsOpen Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/ac68d75b-1fda-28f2-c5b2-01c24ff1b8b8.htm) — the flag can be marked open/closed independent of actual geometry in some Revit-internal routines |
| `Line.CreateBound(XYZ, XYZ)` | Verified | [Line.CreateBound Method](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/7885bdf9-3007-ea60-af6b-a96ac7672c18.htm) — `ArgumentsInconsistentException` "if curve length is too small for... `Application.ShortCurveTolerance`" |
| `XYZ(double, double, double)` ctor; `X`/`Y`/`Z` (get-only) | Verified | [XYZ Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/c2fd995c-95c0-58fb-f5de-f3246cbc5600.htm) |
| `ElementId.Value: long` (modern; not a pre-2024 `int IntegerValue`) | Verified | [ElementId.Value Property](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/6f216e39-b66d-5df5-c60c-b9aaccb1e28a.htm) |
| `Autodesk.Revit.Exceptions.ArgumentException`, `.InvalidOperationException` | Verified | (caught by `ToposolidCreationService.Create`; not separately opened this pass) |
| `ExtensibleStorage.Entity`/`Schema` (sizing only — **zero code in Issue #15 references these types**) | Verified, present | [Entity Class](https://help.autodesk.com/cloudhelp/2027/ENU/Revit-API-MainReference/files/html/cf17f0e8-33bd-ef95-bf4b-e6298406f29b.htm) — referenced only to size the Issue #16 extension-point signature above |
| No managed member named or containing `Threshold` relating to point count exists in any of the three managed assemblies; `SiteDB.dll` (the only binary naming `NativeToposolidMaxPointThreshold`/`LinkToposolidMaxPointThreshold`) has no CLR header | Verified — global reflection member-name search, `assemblies.txt` confirms `SiteDB.dll` `HasCorHeader(Managed): False` | (absence claim; no documentation page to cite) |

### Not established by reflection or documentation (each has a named owner below)

1. Whether `NativeToposolidMaxPointThreshold`/`LinkToposolidMaxPointThreshold` gates the
   `Toposolid.Create(profiles, points, ...)` code path at all — every documentation source scopes both
   settings to interactive DWG/text-file import and linked-topography reload, never to this general-purpose
   overload. Owned by manual test step 7 above.
2. `Result.Cancelled` vs. `Result.Failed` Undo-stack side effects, specifically after a genuine `RolledBack`
   status (as opposed to zero transactions ever opened). Owned by manual test step 5 above; the Result-code
   policy table is explicitly provisional pending this evidence.
3. Whether `UnitTypeId.Feet` or `UnitTypeId.UsSurveyFeet` is numerically Revit's own internal foot — neither
   member's reference page states a numeric definition or an internal-unit correspondence. Owned by manual
   test step 8a item 3; the shipped code never assumes an answer either way (see "Unit conversion" above).
4. Whether the combined `Toposolid.Create` overload (the shipped default) silently accepts invalid, duplicate,
   or out-of-boundary points, throws, or clamps — its own Exceptions table documents boundary-validity
   conditions but not point-distinctness or containment, unlike the sibling `AddPoints` method's documented
   contract. Owned by manual test step 8a item 2; decides whether the default strategy flips.
5. Whether `GetSlabShapeEditor().IsEnabled` is `true` after the combined `Create` call. Owned by manual test
   step 8a item 4; `PostCreationVerification` degrades to bounding-box-only if false.
6. Whether `Element.get_BoundingBox(null)` reliably returns a populated, non-null result immediately after
   `document.Regenerate()` on a freshly created `Toposolid` — general Revit convention, not independently
   confirmed. `PostCreationVerification` treats a null/disabled result as a logged warning, never an automatic
   rollback trigger, for exactly this reason.
7. Whether `Transaction.GetFailureHandlingOptions()`/`SetFailureHandlingOptions()` must be called before vs.
   after `Start()` — Autodesk's own documentation states options "can be set at any time before... committed
   or rolled back" (textually permitting the before-`Start()` ordering this code uses), but its one published
   example calls it after `Start()`. Watched during manual test step 8.
8. The live OpenTopography fetch through Revit's own process environment — no key was present in any session
   that produced this note or the code it documents. Owned by manual test step 8b above.
9. Whether the flat, single-scalar boundary-ring `Z` persists as visible geometry at or near the parcel edge,
   or is fully overridden by point-driven interpolation — see "The boundary-Z decision" above. Owned by
   manual test step 8a item 5. Both outcomes are benign under the shipped fix; this item records which
   actually occurs, not which is safe.
10. Whether `Transaction.HasEnded()` reliably reflects the transaction's true end-state immediately after an
    exception thrown mid-`Commit()`/mid-`Regenerate()`, well enough to safely gate whether a second
    `RollBack()` call is attempted. The code never calls `RollBack()` a second time on a transaction
    `HasEnded()` already reports as ended, treating that reported status as authoritative either way. Owned
    by manual test step 5 scenario 6, so far as that scenario can reach this specific path.
11. `Line.CreateBound`'s documented `ArgumentsInconsistentException` for an edge shorter than
    `Application.ShortCurveTolerance` is a real gap in `LocalBoundaryValidator`'s own "zero-length edges only"
    check: a boundary-ring edge that is short but not exactly zero-length could pass Core-side validation and
    still throw from `BoundaryGeometryBuilder.BuildProfiles`'s `Line.CreateBound` call on the Revit side. Not
    fixed in this milestone; a candidate follow-up (see "Known limitations" below).

## Verification

`dotnet restore SolidGround.slnx --locked-mode`, `dotnet build SolidGround.slnx --configuration Release
--no-restore`, and `dotnet test --project tests/SolidGround.Tests/SolidGround.Tests.csproj --configuration
Release --no-build` all pass against the code this note describes: 0 warnings, 0 errors, and the full offline
suite green (870 tests: 869 passed, 1 skipped — the live OpenTopography test, which requires
`SOLIDGROUND_OPENTOPOGRAPHY_LIVE=1` and a real key and is kept skipped by design). The CI-shaped compile gate
— `dotnet restore src/SolidGround.Revit/SolidGround.Revit.csproj --locked-mode -p:UseRevitReferenceAssemblies=true`
then `dotnet build ... -p:UseRevitReferenceAssemblies=true` — also passes with 0 warnings, confirming
`SolidGround.Revit` still compiles against the CI-only, exact-pinned `Nice3point.Revit.Api.RevitAPI`/`RevitAPIUI`
packages with no change to the gate mechanism itself. None of this is a substitute for the manual evidence plan
above: every check here is offline and Revit-free by construction (`SolidGround.Tests` never launches Revit),
which is exactly why the manual plan exists as a separate, later step.

## Known limitations and follow-ups

- **`%ProgramData%` write access and concurrency.** The settings design handles the single-writer,
  create-if-absent case. Two concurrent Revit processes (or two concurrent command runs) writing to the same
  log file, export directory, or placement-record path under the same configured `output.baseName`/
  `output.directory` can interleave or overwrite each other; this milestone adds no per-run uniqueness (for
  example, timestamped file names) or cross-process locking beyond the settings-file mutex, which only ever
  guards the one-time template write. Mitigation for now is user-level: configure distinct
  `output.baseName`/`output.directory` per concurrent session.
- **UI freeze during `fetch` mode.** The locked synchronous bridge (`Task.Run(...).GetAwaiter().GetResult()`
  on Revit's own UI thread) leaves Revit fully unresponsive for the fetch duration, bounded only by
  `networkTimeoutSeconds`. The command logs (and its eventual dialog implicitly reflects) this before the
  call runs; it does not remove the block, since the synchronous bridge itself was locked by
  `docs/architecture/revit-2027-verification-and-host-design.md`'s own thin-host design, outside this
  milestone's scope to revisit.
- **The digest-conflict-check clause is a no-op.** See "Settings file design" above; a future settings-editing
  feature that performs a real read-modify-write would need to implement it for real.
- **The Issue #16 extension point is a bare nullable delegate, not a named stub method.** Functionally
  equivalent to the alternative considered; chosen for being one line shorter with no separate declaration
  needed.
- **`Line.CreateBound`'s short-curve tolerance is not independently validated.** `LocalBoundaryValidator`
  checks for exactly-zero-length edges only; an edge shorter than `Application.ShortCurveTolerance` but not
  exactly zero could still reach `BoundaryGeometryBuilder.BuildProfiles` and throw
  `ArgumentsInconsistentException` there instead of failing at Preflight with a clear message. Not fixed in
  this milestone; a candidate follow-up once real-world fixtures are available to size the check against.
- **`FailureDefinitionId`'s own logged value is a `ToString()`, not a stable identifier.**
  `ToposolidCreationFailurePreprocessor` logs `message.GetFailureDefinitionId()?.ToString()` because
  `FailureDefinitionId`'s underlying `Guid`-bearing shape was not independently confirmed against the
  installed dump; this is sufficient for a human-readable log line but not for programmatic matching against
  a specific `BuiltInFailures` member by id.

## What this note does not do

This note records what Issue #15 already built; it does not itself change any decision recorded in
`docs/architecture/revit-add-in-conventions.md` or `docs/architecture/revit-2027-verification-and-host-design.md`,
and it does not perform the manual evidence plan above — every "Evidence" entry is a placeholder for a later,
real Revit 2027 session, not a claim this note makes itself. It does not begin Issue #16 (Extensible Storage
provenance; the extension point above is the only thing Issue #15 leaves for it to change), Issue #17
(signing, packaging, or a clean-install validation), or Issue #19 (the real ribbon icon design) — those stay
explicit, separately approved implementation tasks per AGENTS.md's "Mission and current boundary" section.
