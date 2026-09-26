# Revit PropertyLine and shared coordinates

## Purpose and status

Issue #30 (PH3-3) creates a native `PropertyLine` alongside the `Toposolid`, in the same transaction, and adds a
default-off, Preflight-gated, opt-in write of a run's terrain origin as the model's shared coordinates. **Neither
of those two things is implemented yet.** This commit lands only Issue #30's first stage: the Revit-free geometry
cleanup that the later PropertyLine-creation stage depends on. The shared-coordinates write is independent of it:
it writes this run's own terrain local-frame origin, a value fixed upstream of and unaffected by boundary cleanup,
and never consumes the cleaned boundary or either of this stage's new tolerance parameters. This stage adds
`src/SolidGround.Core/Exports/LocalBoundaryCleaner.cs` (new) and an additive `minimumEdgeLength` parameter on
`LocalBoundaryValidator.Validate` (`src/SolidGround.Core/Exports/LocalBoundaryValidator.cs`) — plus their tests.
`SolidGround.Revit` does not call `LocalBoundaryCleaner.Clean` yet; `CreateToposolidCommand`,
`PostCreationVerification`, and every other Revit-side file are unchanged by this commit. The `PropertyLine`
creation, the shared-coordinates detection/write/verification, and the Revit-side tolerance read that wires this
stage's cleanup into `CreateToposolidCommand` all land in a later commit against this same issue, which will
extend this note in place with its own "PropertyLine creation" and "Shared-coordinates write" sections rather
than replacing what is written here.

## Geometry cleanup contract

`LocalBoundaryCleaner.Clean` repairs externally sourced `LocalBoundary` geometry — dedupe of exact and
near-duplicate vertices, drop of edges shorter than a caller-supplied minimum, and collapse of collinear
mid-edge vertices — entirely in `SolidGround.Core`, before `LocalBoundaryValidator.Validate` ever sees the
boundary. Both types are Revit-free today and stay that way: neither references any Autodesk or Revit type,
and neither depends on the other beyond the shared tolerance value described below.

### Inputs and units

```csharp
public static class LocalBoundaryCleaner
{
    public const int DefaultMaxIterations = 8;

    public static LocalBoundary Clean(
        LocalBoundary boundary,
        double vertexTolerance,
        double collinearityTolerance,
        double minimumEdgeLength,
        int maxIterations = DefaultMaxIterations);
}
```

- `boundary` — the boundary to clean. Never mutated; a new `LocalBoundary` is returned. `ArgumentNullException`
  is the only exception `Clean` ever throws; malformed-but-non-null input (too few vertices, self-intersecting,
  degenerate) is passed through unrejected — see "Reject versus repair" below.
- `vertexTolerance`, `collinearityTolerance`, `minimumEdgeLength` — all three are plain numbers in whatever
  linear unit `boundary`'s own coordinates already use. `Clean` performs no unit conversion of its own.
- `maxIterations` — defaults to `DefaultMaxIterations` (8): the most dedupe-then-collapse passes attempted per
  ring before giving up on a slow-converging input.

`LocalBoundaryValidator.Validate` gains one new, trailing, optional parameter, `double? minimumEdgeLength = null`
(its 5th positional argument), fully source/binary-compatible with every existing call site:

```csharp
public static LocalBoundaryValidationResult Validate(
    LocalBoundary boundary,
    IReadOnlyList<LocalTerrainSample> retainedSamples,
    int pointBudget,
    double? containmentToleranceMeters = null,
    double? minimumEdgeLength = null);
```

`null` (the default) skips the new check entirely — unlike `containmentToleranceMeters`'s own Core-only fallback
constant, there is no Core-only default for a value that is only meaningful once a specific downstream
consumer's own drawable minimum is known.

### Tolerance sourcing

Neither `Clean` nor `Validate`'s new check has a Core-side default tolerance; both are supplied entirely by the
caller. Core itself never reads a Revit API member. Once the later Revit-side stage of Issue #30 wires this in,
the intended source (not yet implemented) is `CreateToposolidCommand`'s own Stage 1 Preflight reading
`Autodesk.Revit.ApplicationServices.Application.ShortCurveTolerance` and `.VertexTolerance` once — Revit-internal
decimal feet, unconverted, logged — then Stage 3 converting both into the request's own `OutputUnit` before
calling `Clean`: `vertexTolerance` from `VertexTolerance`, `minimumEdgeLength` from `ShortCurveTolerance`
multiplied by a small, conservative margin (so unit-conversion or local-origin floating-point noise cannot
reintroduce an edge only technically above Revit's own raw minimum), and `collinearityTolerance` defaulted to
`vertexTolerance`'s own converted value — a distinct, separately named parameter kept independently tunable,
not because the two Revit tolerances measure the same concept. Until that stage lands, every test in this
commit supplies its own literal tolerance values directly, exactly as any other caller must.

### Order of operations

`Clean` cleans every polygon of `boundary`, one ring at a time (shell, then each hole), independently of every
other ring or polygon: it never merges vertices across rings, never changes a ring's winding, and never
reclassifies a hole as a shell. Per ring, each iteration is cyclic — the wrap-around edge (last vertex back to
first) is a real edge, since `LocalBoundaryRing.Vertices` never stores a repeated closing vertex:

1. **Dedupe pass.** Walk the ring; whenever two cyclically consecutive vertices are within
   `Math.Max(vertexTolerance, minimumEdgeLength)` of each other (Euclidean distance), drop the second. Taking
   the larger of the two tolerances means an edge `Validate`'s own new check would otherwise reject can never
   survive this pass unrepaired, even one longer than `vertexTolerance` alone but still shorter than
   `minimumEdgeLength`.
2. **Collinear-collapse pass.** For each remaining vertex `B`, with cyclic neighbors `A` and `C`, compute the
   perpendicular distance from `B` to the line through `A` and `C`; if it is at most `collinearityTolerance`,
   drop `B`.
3. **Iterate 1 + 2** up to `maxIterations` times, or until one full pass makes no further change — collapsing a
   collinear vertex can expose a newly sub-tolerance edge, and vice versa, so a single pass is not always
   enough. A ring that collapses below three vertices stops early, without erroring.
4. **Never touches true topology.** Self-intersection, a hole falling outside its shell, nested holes/shells,
   and disconnected interior remain exactly `LocalBoundaryValidator`'s job, run immediately afterward,
   unchanged.
5. **Never silently reshapes a genuine sharp corner.** A vertex whose deviation exceeds `collinearityTolerance`
   is preserved exactly, however visually spiky.

### Reject versus repair

| Concern | Who handles it | Behavior |
| --- | --- | --- |
| Exact/near-duplicate cyclically consecutive vertices, including across the wrap-around edge | `LocalBoundaryCleaner.Clean` | Repaired: the second vertex of the pair is dropped. |
| Edges shorter than the caller's `minimumEdgeLength` | `Clean` (repair, via the dedupe pass's `Math.Max` reconciliation) **and** `Validate` (reject, defense in depth) | `Clean` is expected to remove every such edge in ordinary operation; `Validate`'s own check exists because `Clean` deliberately stops early (without erroring) if a ring would drop below three vertices, and is bounded by `maxIterations` — both deliberate safety valves that mean a pathological or slow-converging input could in principle still reach `Validate` with a too-short edge. |
| Collinear/near-collinear mid-edge vertices | `Clean` | Repaired: dropped once their perpendicular deviation from their own two neighbors is within `collinearityTolerance`. |
| A ring with fewer than three distinct vertices, including one `Clean` itself reduced to that state | `Validate` | Rejected, with its existing "fewer than three distinct vertices" message, unchanged by this stage. |
| Self-intersection, a hole outside its shell, nested holes/shells, non-positive area | `Validate` | Rejected; `Clean` never attempts to repair or even detect any of these — true topology stays validation's job alone. |
| Cross-polygon (multi-polygon) self-intersection | Neither, today | Out of scope for both `Clean` and `Validate`; unchanged by this stage. |

The dividing line is simple: `LocalBoundaryCleaner` only ever repairs a ring's own vertex list — dedupe and
collinear collapse — and never throws for messy-but-parseable input. `LocalBoundaryValidator` only ever detects
and reports, never repairs, and is otherwise completely unchanged by this stage except for the one new,
opt-in check described above.

### Determinism

`Clean` is a pure function of its own arguments: it never allocates a `Random`, reads the clock, touches the
filesystem, or otherwise depends on anything outside `boundary` and the four numeric parameters. Vertex order
is preserved throughout — no `HashSet`/`Dictionary` iteration order ever decides which vertex survives or in
what order survivors appear. The dedupe pass always keeps the ring's first vertex as a fixed anchor and walks
forward from it; the collinear-collapse pass evaluates every vertex against one unchanging snapshot of its
neighbors before removing any of them, so results never depend on visiting order. Calling `Clean` twice with
identical arguments always returns vertex-for-vertex identical rings
(`LocalBoundaryCleanerTests.CleanIsDeterministicAcrossRepeatedRuns`), and the same multi-pass fixed-point loop
is exercised directly by `CleanConvergesWhenACollinearCollapseExposesANewNearDuplicatePair`.
