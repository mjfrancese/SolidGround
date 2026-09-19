# Terrain-aware decimation

Issue #7 implements `SolidGround.Core.Simplification.GridTerrainSimplifier`,
`SolidGround.Core.Simplification.SimplificationDiagnostics`, and
`SolidGround.Core.Simplification.TerrainSimplificationException`, and extends
`SolidGround.Core.Simplification.SimplificationResult` with an optional `Diagnostics` property. It adds no
package reference; every pass is plain, single-threaded BCL arithmetic and `System.Linq` ordering over an
already-parsed `SolidGround.Core.Terrain.ElevationGrid`. AGENTS.md binds this issue: it asks for "a
terrain-aware simplifier, such as curvature-aware selection or TIN error simplification" (an either/or), so
this issue implements exactly one terrain-aware method (`SimplificationMethod.CurvatureAware`) plus one
explicitly-labeled, non-default comparison baseline (`SimplificationMethod.UniformSampler`) — the smallest
design that satisfies that either/or and the acceptance criteria below — and rejects the TIN candidate at run
time rather than building a Garland–Heckbert-style greedy-insertion triangulation over NetTopologySuite.
`SimplificationMethod`, `SimplificationRequest`, and `ITerrainSimplifier` (Issue #2) are unchanged.

## Structural classification: one rule for grid edges, NODATA holes, and parcel boundaries

For a valid (non-null) candidate cell at `(row, column)`, `GridTerrainSimplifier` examines all 8 Moore-neighborhood
offsets. A neighbor is **missing** when it is outside `[0, RowCount) x [0, ColumnCount)` or `ElevationGrid.GetElevation`
returns `null` there; `ElevationGrid.GetElevation` performs no bounds check of its own, so every neighbor access is
explicitly bounds-checked first (`GridTerrainSimplifier.SafeElevation`). A cell with one or more missing neighbors is
**structural**; otherwise it is **interior**. This single rule derives three cases the algorithm never needs to tell
apart:

- **Grid edges**: any cell on the array perimeter always has an out-of-bounds neighbor.
- **NODATA hole rims**: an interior cell 8-adjacent to a `null` cell.
- **Parcel-boundary rims**: `src/SolidGround.Core/Clipping/GridClipper.cs` represents both `GridCellStatus.SourceNoData`
  and `GridCellStatus.RegionExcluded` as `null` in its output `ElevationGrid` — a genuine source gap and a
  post-clip exclusion are indistinguishable once they reach an `ElevationGrid`, and the simplifier does not need
  to distinguish them: both mean "the known surface stops here."

8-connectivity (not 4) is deliberate: a cell that only diagonally touches a hole or boundary would otherwise be
misclassified as interior, leaving a corner-bridging gap unprotected. `GridTerrainSimplifierTests.DiagonallyAdjacentCellsNextToAHoleAreAlsoClassifiedStructural`
proves this directly by comparing the actual `StructuralCandidateCount` against an independently reimplemented
8-neighbor count for a single-cell hole.

## Curvature and elevation-residual scoring

For candidate `(row, column, elevation)`, with `west`/`east`/`north`/`south` the (possibly missing) axis neighbors:

```text
Cxx = (west and east both present) ? (west + east - 2*elevation) / (CellSizeX * CellSizeX) : 0.0
Cyy = (north and south both present) ? (north + south - 2*elevation) / (CellSizeY * CellSizeY) : 0.0
CurvatureMagnitude = |Cxx| + |Cyy|                         // elevation unit per squared horizontal grid unit
ElevationResidual  = |elevation - average(present axis neighbors)|   // elevation unit, or 0 if none are present
```

Both axis terms are cell-size normalized (`CellSizeX`/`CellSizeY` are independent on `ElevationGrid`) and each
defaults to exactly `0.0` when its own neighbor pair is unavailable, rather than dividing by a shared "number of
available pairs" that can be zero at a corner. `CurvatureMagnitude` sums `|Cxx|` and `|Cyy|` rather than computing
`|Cxx + Cyy|` so that a saddle point — a mountain pass, where the two axis curvatures have opposite sign but
comparable magnitude — is not scored as flat just because its axis terms happen to cancel.
`GridTerrainSimplifierTests.SaddleShapedTerrainIsNotCancelledByOppositeAxisCurvatureSigns` proves this on an exact
quadratic saddle (`elevation = (row-5)^2 - (column-5)^2`) where every true-interior cell has `Cxx = -2` and
`Cyy = +2`: the retained/removed accounting shows a removed-candidate curvature of exactly `4.0`, not the `~0.0`
a naive `|Cxx + Cyy|` implementation would produce. Both metrics are computed once per candidate in Pass 0, for
every candidate (structural and interior alike), because structural truncation (below) uses `CurvatureMagnitude`
as a tie-break and honest removed-point diagnostics need both statistics regardless of which tier a cell falls in.

## Three-pass budget allocation

Pass 1 (structural, mandatory) retains every structural candidate unless `StructuralCandidateCount` exceeds
`SimplificationRequest.PointBudget`, in which case it sorts by `(MissingNeighborCount desc, CurvatureMagnitude
desc, Row asc, Column asc)` and truncates to the budget (see "Budget enforcement" below). Pass 2 (curvature-ranked
interior fill) splits the remaining budget into a `curvatureAllotment` and a `coverageReserve` (`coverageReserve =
floor(remainingBudget * coverageFloorFraction)`, default `GridTerrainSimplifier.DefaultCoverageFloorFraction =
0.2`), then takes the top-`curvatureAllotment` non-structural candidates by `(CurvatureMagnitude desc, Row asc,
Column asc)`. Pass 3 (coverage floor, with rollover) adds back any of Pass 2's unused curvature allotment
(`leftover = curvatureAllotment - curvatureTaken`) to `coverageReserve`, forming `coverageBudgetFinal`, then groups
the still-unselected candidates into `blockSize x blockSize` spatial blocks, groups and sorts those blocks by
`(BlockRow asc, BlockColumn asc)`, then visits them in **waves** rather than in that flat order: the earliest
not-yet-visited block of every BlockRow (BlockRow ascending) is visited before any BlockRow's second block, and
so on, picking the highest-curvature member of each visited block (tie: `Row asc, Column asc`), until
`coverageBudgetFinal` is reached or blocks run out. This guarantees every BlockRow that still holds an
unselected candidate gets a pick before any BlockRow gets a second one, so the block-selected share of the
budget spreads across every band of the grid rather than clustering in one place, even when
`coverageBudgetFinal` is smaller than the block grid's total block count.

The wave-based visitation order fixes a SolidGround Issue #7 review finding: visiting blocks in a flat
`(BlockRow asc, BlockColumn asc)` order and stopping the instant `coverageBudgetFinal` picks were made meant a
`coverageBudgetFinal` smaller than the number of blocks spanned by the first BlockRow(s) never reached any
later BlockRow at all — a 30x30 flat plane with `coverageFloorFraction: 1.0` and a budget of exactly
`perimeterCount + 2` forces `coverageBudgetFinal` to `2` against a natural 2x2 block grid, and the flat
visitation order put both picks in the same (topmost) BlockRow every time.
`GridTerrainSimplifierTests.CoverageFloorReachesEveryBlockRowEvenWhenTheBudgetIsSmallerThanTheNaturalBlockCount`
reproduces exactly that scenario and asserts the two coverage-floor picks land in different BlockRows.
`GridTerrainSimplifierTests.CoverageFloorAloneSpreadsRetainedInteriorSamplesAcrossEveryQuadrantWhenCurvatureRankingContributesNothing`
still separately proves the block-selection mechanism itself reaches every quadrant when `coverageFloorFraction:
1.0` makes `curvatureAllotment` exactly `0` and `coverageBudgetFinal` comfortably exceeds the natural block
count (so every block is visited regardless of visitation order, wave-based or otherwise).
`GridTerrainSimplifierTests.AFlatPlaneRetainsOnlyPerimeterStructuralPointsPlusACoverageFloorSpreadAcrossTheInterior`
exercises the *default* `coverageFloorFraction` (`0.2`) on the same flat plane but only checks a coarse
quadrant count, which Pass 2 alone can already satisfy there: when every interior candidate ties at
`CurvatureMagnitude = 0.0`, Pass 2's `(Row asc, Column asc)` tie-break degenerates to a contiguous row-major
run, and under the default fraction that run is wide enough to itself span more than one quadrant before Pass 3
contributes a single point. A caller who needs guaranteed even coverage of a genuinely flat or heavily tied
region — rather than whatever spread Pass 2's tie-break happens to produce — should raise `coverageFloorFraction`
toward the isolated-floor case above; this is a known characteristic of the exact, designed Pass 2 ordering
(`CurvatureMagnitude` desc, `Row` asc, `Column` asc, with no spatial dispersion of its own). It is unrelated to
Pass 3's own block-visitation order, which — since the wave-based fix above — spreads across every BlockRow
regardless of how small `coverageBudgetFinal` is, down to the pigeonhole limit of one pick per BlockRow.

`blockSize = Math.Max(1, (int)Math.Ceiling(Math.Sqrt((double)totalCells / coverageBudgetFinal)))` deliberately
casts `totalCells` (already `long`, to avoid `RowCount * ColumnCount` overflowing `int` on a large grid) to
`double` before dividing by `coverageBudgetFinal`: an integer division here would truncate toward zero and often
collapse to a smaller block count than intended, changing the coverage grid's granularity. Because a block only
ever contributes at most one retained point regardless of how many candidates it holds, `coverageBudgetFinal` is
an upper bound on `CoverageFloorPointCount`, not a guarantee — when the still-unselected candidates cluster into
fewer distinct blocks than `coverageBudgetFinal`, some of that reserve genuinely goes unused. This is why
`GridTerrainSimplifierTests.DiagnosticsInteriorCandidatesExhaustedReflectsWhetherBudgetOrCandidateSupplyWasTheLimit`'s
"budget comfortably exceeds supply" case sizes its budget so Pass 2 alone already exhausts every interior
candidate, rather than relying on Pass 3 to finish the job.

### Budget invariant (proved, not merely asserted)

`coverageBudgetFinal = remainingBudget - curvatureSelected.Count` (since `coverageReserve + curvatureAllotment =
remainingBudget` by construction, and `leftover = curvatureAllotment - curvatureTaken`). The Pass 3 loop guard
(`coverageSelected.Count >= coverageBudgetFinal`) keeps `coverageSelected.Count <= coverageBudgetFinal`, so
`curvatureSelected.Count + coverageSelected.Count <= remainingBudget = PointBudget - StructuralPointCount` whenever
Pass 1 was not truncated, or both terms are exactly `0` when it was (`remainingBudget = 0`). Either way,
`StructuralPointCount + CurvatureSelectedPointCount + CoverageFloorPointCount <= PointBudget`, always.
`SimplifyUniform`'s own `retained.Count = min(PointBudget, CandidatePointCount) <= PointBudget` always.
`SimplificationResult`'s existing, unmodified constructor check (`retainedSamples.Length > request.PointBudget`
throws) remains an untouched second line of defense behind both derivations.

## Total ordering and determinism

Every sort used anywhere in `GridTerrainSimplifier` terminates in `(Row, Column)`, the only pair unique to a single
candidate in a rectangular grid, so no two distinct candidates ever compare equal and the result cannot depend on
whether `List<T>.Sort` (unstable) or LINQ `OrderBy`/`ThenBy` (stable) was used. The final retained set is always
re-sorted by `(Row asc, Column asc)` as an explicit last step, independent of which pass or method produced each
point, and the aggregate removed-candidate statistics (`Mean...` fields) are summed over the candidate list in
that same row-major order the Pass 0 scan already builds it in — pinned explicitly so summation order is never
ambiguous. `GridTerrainSimplifier` uses no `Parallel.For`, `PLINQ`, `Task.Run`, `System.Random`, `Guid`, or
wall-clock input anywhere; all arithmetic is plain single-threaded `double` operations over an immutable
`ElevationGrid`, so IEEE-754 guarantees byte-identical results across repeated calls with the same input and
options. `GridTerrainSimplifierTests.SimplifyAsyncIsDeterministicAcrossRepeatedRunsOnTheSameGridInstance` and
`...OnAFreshlyConstructedValueEqualGrid` both assert this: the second test rebuilds an equal-by-value but
reference-distinct `ElevationGrid` to rule out any accidental reliance on instance identity or caching.

## Budget enforcement, including an infeasible structural set

An infeasible structural set (`StructuralCandidateCount > PointBudget`) is a **deterministic priority truncation
with a visible diagnostic**, not an exception: `SimplificationDiagnostics.StructuralPointsOmittedForBudget` and
`StructuralBudgetShortfall` make the truncation observable rather than silent, and a positive-budget request never
needs to expect a throw just because the parcel boundary or a hole rim is unusually large. This is a deliberate
contrast with `GridClipper.Clip`, which does throw `GridClipException` when its effective region covers zero
cells — that condition has no reasonable partial result to return, while an over-budget structural set does (the
most-isolated cells first). `GridTerrainSimplifierTests.ATightBudgetBelowTheStructuralCandidateCountTruncatesDeterministicallyAndReportsAShortfall`
and `...ATightBudgetExactlyEqualToTheStructuralCandidateCountFitsWithNoShortfall` cover both sides of that boundary
against an independently computed top-K expectation. An all-NODATA grid (`CandidatePointCount == 0`) is likewise
not an error: it returns an empty, valid `SimplificationResult`, matching the precedent that `TerrainProvenance`
already accepts `originalPointCount == 0` with a `null` `ElevationRange`.

`SimplificationDiagnostics`'s own constructor mirrors `ElevationRange`'s finite/ordered validation for
`MinElevation`/`MaxElevation`, but deliberately does **not** enforce a mean-cannot-exceed-max cross-check for
`MaxRemovedCurvatureMagnitude`/`MeanRemovedCurvatureMagnitude` or `MaxRemovedElevationResidual`/`MeanRemovedElevationResidual`.
Unlike `MinElevation`/`MaxElevation` (each a single stored value), the four removed-candidate statistics are
independently accumulated floating-point sums; IEEE-754 summation of equal inputs can round a computed mean a
single ulp above a max drawn from the very same values (`0.1 + 0.1 + 0.1`, divided by `3`, rounds to slightly more
than `0.1`). Rejecting that would make an honestly computed, entirely correct diagnostics value throw. Only
finiteness and non-negativity are validated for those four fields;
`GridTerrainSimplifierTests.SimplificationDiagnosticsConstructorAllowsAMeanStatisticSlightlyAboveItsMaximumFromFloatingPointSummation`
constructs exactly that rounding case and asserts it succeeds.

### Error taxonomy

| Exception | Trigger |
| --- | --- |
| `ArgumentNullException` | `terrain` or `request` is `null`. |
| `ArgumentException` (paramName `terrain`) | `terrain` is an `ElevationData` other than `ElevationGrid` (for example `TerrainSampleSet`); names the actual runtime type. |
| `TerrainSimplificationException` | `request.Method == SimplificationMethod.TinError`; names both implemented alternatives. |
| `OperationCanceledException` | `cancellationToken` is already cancelled at entry, or becomes cancelled between the structural and interior-fill passes. |
| `ArgumentOutOfRangeException` | `GridTerrainSimplifier(coverageFloorFraction)` outside `[0, 1]` or non-finite; a `SimplificationDiagnostics` count is negative; an elevation bound or removed-statistic is non-finite or negative; `elevationUnit` is undefined. |
| `ArgumentException` | `SimplificationDiagnostics.structuralPointCount > structuralCandidateCount`; exactly one of `minElevation`/`maxElevation` is `null`, or their nullness disagrees with `candidatePointCount == 0`; `minElevation > maxElevation`; a `SimplificationResult`'s `diagnostics.CandidatePointCount` disagrees with `originalPointCount`, or its four selection counts do not sum to the retained sample count. |
| *(none — a valid, empty result)* | An all-NODATA grid, or a structural set larger than the budget. |

## Why TinError is not implemented

`SimplificationMethod.TinError` stays a legal, defined enum value — `SimplificationRequest`'s existing,
byte-for-byte-unchanged `Enum.IsDefined` validation keeps accepting it — but `GridTerrainSimplifier` rejects it at
run time with a `TerrainSimplificationException` naming both implemented alternatives. AGENTS.md's "curvature-aware
selection **or** TIN error simplification" phrasing is explicitly an either/or, and building a
Garland–Heckbert-style greedy-insertion TIN over NetTopologySuite's
`Polygonizer`/`ConstrainedDelaunayTriangulator`/`STRtree` in addition to a working pure-BCL method would be more
than that either/or or the acceptance criteria require. The enum member is left intact as
reserved-but-unimplemented so a future issue can add a second `ITerrainSimplifier` (or extend this one) without a
breaking contract change.

## The comparison sampler: UniformSampler

`SimplificationMethod.UniformSampler` is reachable only via an explicit `new SimplificationRequest(method:
SimplificationMethod.UniformSampler)` — `SimplificationRequest`'s own default stays `CurvatureAware`, unchanged,
proved directly by `GridTerrainSimplifierTests.UniformSamplerNeverPopulatesStructuralOrCurvatureSelectedDiagnosticsAndIsNotTheDefault`.
It performs no structural or curvature classification: it builds the same row-major candidate list Pass 0 always
builds, then, when the candidate count `N` exceeds the budget, selects index `floor(i * N / budget)` for each
`i` in `[0, budget)`. This always selects exactly `min(PointBudget, N)` **distinct** indices spanning the full grid
evenly with no undershoot (`floor((i+1)N/budget) - floor(iN/budget) >= 1` whenever `budget <= N`), fixing an
under-fill bug a stride-based (`ceil`/`sqrt`-based) formula can exhibit. The actual index arithmetic,
`(int)(((long)i * candidateCount) / pointBudget)`, deliberately widens `i * candidateCount` to `long` before
dividing: both operands are plain `int`s, and their product can exceed `int.MaxValue` well before either grid
dimension becomes unreasonable (for example a 100,000-candidate grid sampled at a 50,000 budget already
multiplies past 2^31 at `i = 49,999`: `49,999 * 100,000 = 4,999,900,000`). `UniformSampler` still never
fabricates an elevation for a `null` cell — it only ever indexes into the already-NODATA-filtered candidate
list — but it has no notion of a grid edge, a hole rim, or a parcel boundary, so its diagnostics report
`StructuralCandidateCount = StructuralPointCount =
CurvatureSelectedPointCount = CoverageFloorPointCount = 0` and `UniformlySampledPointCount = RetainedPointCount`,
making it immediately obvious from `SimplificationDiagnostics` alone that this method ran and that it protected no
boundary.
`GridTerrainSimplifierTests.TheDefaultCurvatureAwareMethodRetainsMoreOfARidgeLineThanUniformSamplerAtTheSameTightBudget`
and `...CurvatureAwareAchievesALowerMeanRemovedElevationResidualThanUniformSamplerAtTheSameBudget` give this
contrast quantitative evidence: at the same tight budget on the same ridge grid, `CurvatureAware` retains more
near-ridge samples and leaves a strictly lower mean removed elevation residual than `UniformSampler`.

## Accuracy caveat

AGENTS.md frames SolidGround as a site-form tool, not a survey instrument, and this issue changes nothing about
that. `CurvatureMagnitude` and `ElevationResidual` (and the diagnostics built from them) are generalization-quality
proxies for how much local relief a budget-constrained selection left out — they are not a certified geometric
error bound, an RMSE figure, or a substitute for the QL2 bare-earth lidar accuracy limits AGENTS.md already
documents (roughly 10 cm vertical RMSE in favorable conditions, worse under mature canopy such as the reference parcel fixture). A low `MeanRemovedElevationResidual` means the retained points reproduce the *simplified* surface's
local shape well; it says nothing about how well that surface reproduces the true ground beneath the canopy.

## Boundary: what #8 and #9 still own

| Concern | Owner | Notes |
| --- | --- | --- |
| Mapping `SimplificationResult`/`SimplificationDiagnostics` into `TerrainProvenance`'s `originalPointCount`/`retainedPointCount`/`elevationRange`, and into `TerrainExportPayload` | Issue #8 | This issue adds `GridTerrainSimplifier` and its diagnostics only; `src/SolidGround.Core/Provenance/TerrainProvenance.cs` and `src/SolidGround.Core/Exports/TerrainExportContracts.cs` are untouched. |
| CLI exposure of `SimplificationRequest.PointBudget`/`Method` and `GridTerrainSimplifier`'s `coverageFloorFraction`, plus printing diagnostics | Issue #9 | `src/SolidGround.Cli/Program.cs` is untouched by this issue. |
| Extending simplification to `TerrainSampleSet` (a classified point-cloud source) | A future point-cloud-source issue | `GridTerrainSimplifier` accepts only `ElevationGrid`, by design: structural and curvature classification need raster adjacency `TerrainSampleSet` does not have. |
| True TIN-error simplification | Not scheduled | See "Why TinError is not implemented" above; the enum member remains reserved. |
