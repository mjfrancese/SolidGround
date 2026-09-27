# Revit PropertyLine and shared coordinates

## Purpose and status

Issue #30 (PH3-3) implements owner decisions 5-6 (`docs/architecture/phase-3-interactive-add-in-research.md`
lines 147-148): a native `PropertyLine` created alongside the `Toposolid`, in the same transaction, from the
same underlying boundary geometry; a default-off, Preflight-gated, opt-in write of this run's terrain origin as
the model's shared coordinates, refused when the model already appears to have shared coordinates set; and the
defensive Core-side geometry cleanup (dedupe/collinear-collapse) this issue's scope requires before any of this
reaches Revit's `CurveLoop` APIs. Cross-references `docs/architecture/revit-toposolid-creation.md` for the
shared six-stage command flow, transaction/rollback policy, and dialog helpers this note only amends.

Landed in four stages. **Stage 1** (commit `c81abb1`) added the Revit-free `LocalBoundaryCleaner` and
`LocalBoundaryValidator`'s optional `minimumEdgeLength` parameter in `SolidGround.Core` —
`src/SolidGround.Core/Exports/LocalBoundaryCleaner.cs` (new) and the additive parameter on
`LocalBoundaryValidator.Validate` (`src/SolidGround.Core/Exports/LocalBoundaryValidator.cs`) — plus their
tests; not yet wired into `SolidGround.Revit`, which was unchanged by that commit. **Stage 2** (commit
`842cadc`) wires that cleanup into `CreateToposolidCommand`'s Stage 3, adds `PropertyLine` creation gated to
parcel areas of interest, the shared-coordinates detection/write/verification gate, the Revit-side tolerance
read at Preflight, and the placement-record schema v3 fields. **Stage 3** (this update, 2026-09-27) is a
live-evidence fix: a live Revit 2027 session (manual evidence Step 14.5) found Stage 2's own shared-coordinates
detection proxy misread a brand-new document opened from Revit's own default template as already coordinated,
because that template ships its survey point already clipped. The decision moved into a pure, Revit-free,
unit-tested Core function that never reads a "clipped" flag of any kind, and the writer no longer sets one
either — see "Shared-coordinates detection" and "Why `Write` no longer sets `Clipped`" below, and "Known
limitations" for the full, plain-English record of the live finding. **Stage 4** (this closeout) completes Step
14's live Revit 2027 evidence session and corrects a second finding from that same session: the opted-in
write's own success-dialog/placement-record sentence claimed the project base point, survey point, and site
location were "otherwise left unchanged," but the session's own log showed `ProjectLocation.SetProjectPosition`
moves the survey point's own internal `Position` as an intrinsic side effect of that one call, so the claim was
false, not merely imprecise. The sentence now states the move plainly instead of denying it — see "Post-commit
reporting" below, "Known limitations" for the full record, and "Manual evidence (Revit 2027, 2026-09-26/27)"
below for Step 14's complete results.

Every Revit API member below was verified against the installed Revit 2027 `RevitAPI.dll` (FileVersion
`27.0.10.13`) via `MetadataLoadContext`/`System.Reflection.Metadata` reflection, cross-checked against
Autodesk's shipped `RevitAPI.xml` doc comments, matching the method `docs/architecture/revit-2027-verification-and-host-design.md`
established for Issue #13. The local build compiling `SolidGround.Revit` against the installed Revit 2027 SDK
is itself confirming evidence for every signature this note cites: a member that did not exist, or did not
match the signature used here, would fail that build.

## Owner decisions (2026-09-26)

The owner's own instruction on Issue #30: "Regarding #30, go with all recommended."

1. **The shared-coordinates detection proxy** (survey point at the internal origin and unclipped, OR'd with
   `ProjectLocations.Size > 1`) is accepted as designed, on the explicit condition that a live Revit 2027
   confirmation against a real document that already has shared coordinates set still runs before this ships
   (see "Manual evidence plan" below, Step 14.6). **Corrected 2026-09-27 (live-evidence fix):** that live
   confirmation found the `unclipped` term misread Revit's own default template as already coordinated; the
   accepted proxy's formula was replaced and no longer reads the survey point's clipped state in any form — see
   "Shared-coordinates detection" below. **Condition met, 2026-09-26/27:** Step 14.6's re-run against the
   corrected proxy (both an already-coordinated scratch document and the Step 14.5 document's own prior write)
   confirmed the opt-in correctly refuses in both cases; see "Manual evidence (Revit 2027, 2026-09-26/27)"
   below.
2. **The value written, when opted in, is the terrain's own local-frame source origin at zero rotation** — the
   same coordinate already in the placement record's `localOrigin.sourceX`/`sourceY`/`sourceElevation`. Zero
   rotation means SolidGround treats the terrain's projected grid north as Revit's true north: no
   grid-convergence correction is computed or applied (see "Zero rotation, stated plainly" below).
3. **`PropertyLine` creation is restricted to parcel areas of interest only, not unconditional.** A
   bounding-box or radius AOI creates a `Toposolid` and nothing else, with no error and no dialog-headline
   difference; the success report states plainly that no property line was created for that run. Owner
   decision 5 (`phase-3-interactive-add-in-research.md` lines 147-148) is read as applying to the Phase 3
   address-to-parcel workflow, not as separately mandating unconditional `PropertyLine` creation for every AOI
   kind this issue supports.
4. **The `PropertyLine`'s element id/area and the shared-coordinates write outcome are recorded in the
   placement record, schema bump 2 → 3.** These are placement facts, consistent with the owner's separate
   2026-09-26 Issue #33 decision that the placement record carries geometry/unit/placement fields but not the
   address/parcel provenance record.

## Revit 2027 API surface used (new members, beyond `revit-toposolid-creation.md`'s existing table)

| Member | Basis |
| --- | --- |
| `PropertyLine.Create(Document, IList<CurveLoop>)` | installed-SDK reflection (`<since>2027</since>`) + `RevitAPI.xml` |
| `PropertyLine.IsValidBoundary(IList<CurveLoop>)` — static, no `Document`/transaction | installed-SDK + `RevitAPI.xml`: "not necessary to close... should not intersect with each other; each loop planar and parallel to the horizontal (XY) plane" |
| `PropertyLine.IsClosedLoop(): bool`, `PropertyLine.Area { get; }` (`>0` iff closed loop, else `-1`) | installed-SDK + `RevitAPI.xml` |
| `ProjectLocation.SetProjectPosition(XYZ, ProjectPosition): void` (instance, via `document.ActiveProjectLocation`) | installed-SDK; `RevitAPI.xml`: "similar to the Revit command 'Specify Coordinates at Point'" |
| `ProjectLocation.GetProjectPosition(XYZ): ProjectPosition` | installed-SDK + `RevitAPI.xml` |
| `ProjectPosition(double ew, double ns, double elevation, double angle)` ctor; `EastWest`/`NorthSouth`/`Elevation`/`Angle` get/set | installed-SDK; `RevitAPI.xml`: all in decimal feet/radians |
| `BasePoint.GetSurveyPoint(Document)` (static), `.Position` (get-only) | installed-SDK + `RevitAPI.xml` — `.Clipped` (get/set) was used through 2026-09-26; the 2026-09-27 live-evidence fix below removed every read and write of it from this design, so it is no longer part of this note's own API surface |
| `Document.ProjectLocations { get; }: ProjectLocationSet` (`.Size`, `.IsEmpty`) | installed-SDK + `RevitAPI.xml` |
| `Autodesk.Revit.ApplicationServices.Application.ShortCurveTolerance { get; }: double` | installed-SDK; `RevitAPI.xml`: "the enforced minimum length for any curve created by Revit," `<since>2014</since>` |
| `Autodesk.Revit.ApplicationServices.Application.VertexTolerance { get; }: double` | installed-SDK; `RevitAPI.xml`: "two points within this distance are considered coincident... do not use this value to set the distance between two points," `<since>2012</since>` |

`PropertyLine.Create`'s own `RevitAPI.xml` doc comment documents `ArgumentException`, `ArgumentNullException`,
`InvalidOperationException`, `ModificationForbiddenException`, and `ModificationOutsideTransactionException` —
a strictly broader list than `Toposolid.Create`'s own doc page, which `ToposolidCreationService`'s existing
two-exception catch filter mirrors. `PropertyLineCreationService.Create`'s own catch filter accordingly widens
to three exception types (`ArgumentException`, `InvalidOperationException`, `ModificationForbiddenException`),
deliberately excluding `ModificationOutsideTransactionException`: that one means this service was called with
no open transaction, a SolidGround programming bug, not a user-addressable condition — the generic Stage-5
catch-all's own message is the honest one for that case, not a boundary-specific message that would misdirect
the user toward fixing their input geometry.

`ProjectLocation.SetProjectPosition`'s own `RevitAPI.xml` doc comment documents `ArgumentNullException` and
`InvalidOperationException` ("Unable to use the project position's transform to calculate the point.").
**Review fix:** `SharedCoordinatesWriter.Write` wraps this call in a `try`/`catch` mirroring
`PropertyLineCreationService.Create`'s own pattern, translating either exception into a new
`SharedCoordinatesWriteException` (error catalogue row 20b) instead of letting it
fall through to the generic Stage-5 catch-all, which would otherwise misattribute the failure to the toposolid
even though, by that point, the `Toposolid` (and any `PropertyLine`) had already been validly created and only
the shared-coordinates write itself failed. **Corrected 2026-09-27 (live-evidence fix):** through 2026-09-26,
this same `try`/`catch` also wrapped a second call, `BasePoint.Clipped`'s setter — its own doc comment documented
that identical `InvalidOperationException` for its setter, but only "for a non-shared BasePoint," structurally
unreachable here since `BasePoint.GetSurveyPoint` always returns the shared survey point, never the (non-shared)
project base point. `Write` no longer sets `Clipped` at all (see "Why `Write` no longer sets `Clipped`" below),
so this `try`/`catch` now wraps only the one `SetProjectPosition` call described above.

**Not established by any documentation source; owner-visible risk, accepted (owner decision 1).** No Revit API
member directly answers "has this document's shared coordinates already been set." `BasePoint.IsShared` is
confirmed, by its own doc comment and by this repository's own pre-existing `OrphanCheck.cs` observation, to be
a **fixed type discriminant** (always `true` for the survey point, always `false` for the project base point)
— a trap, not a usable runtime flag. `Document.AcquireCoordinates(ElementId)`/`.PublishCoordinates(LinkElementId)`
are one-shot mutating actions tied to a linked model, not queries. The recommended proxy below is the
strongest evidence-backed candidate found, not a certified fact.

## Geometry cleanup contract (Core, Revit-free)

`src/SolidGround.Core/Exports/LocalBoundaryCleaner.cs` (Stage 1):

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

Runs in `CreateToposolidCommand.ExecuteCore`'s Stage 3, **after** `LocalBoundaryFactory.FromPolygonalRegion`/
`FromGridEnvelope` and **before** `LocalBoundaryValidator.Validate` — unconditionally, on every run regardless
of AOI kind or the shared-coordinates opt-in. Per polygon, per ring (shell, then each hole), independently:
never merges across rings/polygons, never changes winding, never reclassifies a hole as a shell. A hand-rolled,
two-pass, per-vertex walk over `LocalCoordinate2D` (dedupe, then collinear-collapse, iterated to a fixed point
or `maxIterations`), not `DouglasPeuckerSimplifier`: this contract needs two independently-sourced, distinct
Revit tolerances used for two distinct purposes (a coincidence test vs. a per-vertex collinearity test), and
needs a hard guarantee that a genuine sharp corner is never touched regardless of the rest of the ring's shape.
Never touches true topology (self-intersection, a hole outside its shell, etc.) — that stays
`LocalBoundaryValidator`'s job, run immediately afterward, unchanged.

**Dedupe pass** drops the second of any cyclically-consecutive pair within
`Math.Max(vertexTolerance, minimumEdgeLength)` of each other — the `Math.Max` closes the gap band between a
true-coincidence tolerance and the separate, often-larger minimum edge length Revit can actually draw, so
nothing `LocalBoundaryValidator`'s own equivalent check would reject can survive `Clean` unrepaired.
**Collinear-collapse pass** drops any vertex whose perpendicular deviation from the line through its two cyclic
neighbors is at most `collinearityTolerance`. Both passes iterate together (collapsing a collinear vertex can
expose a newly sub-tolerance edge and vice versa) until a full pass makes no change or `maxIterations` is
reached; a ring that drops below three vertices stops early without erroring — `LocalBoundaryValidator.Validate`
rejects that downstream with its existing message, matching `LocalBoundaryFactory`/`LocalBoundaryValidator`'s
own "never repairs by throwing" discipline. A vertex whose deviation exceeds `collinearityTolerance` is
preserved exactly, however sharp or spiky its corner.

**Reject versus repair.** The dividing line between `Clean` and `Validate` is simple: `LocalBoundaryCleaner`
only ever repairs a ring's own vertex list — dedupe and collinear collapse — and never throws for
messy-but-parseable input; `LocalBoundaryValidator` only ever detects and reports, never repairs.

| Concern | Who handles it | Behavior |
| --- | --- | --- |
| Exact/near-duplicate cyclically consecutive vertices, including across the wrap-around edge | `Clean` | Repaired: the second vertex of the pair is dropped. |
| Edges shorter than the caller's `minimumEdgeLength` | `Clean` (repair, via the dedupe pass's `Math.Max` reconciliation) **and** `Validate` (reject, defense in depth) | `Clean` is expected to remove every such edge in ordinary operation; `Validate`'s own check exists because `Clean` deliberately stops early (without erroring) if a ring would drop below three vertices, and is bounded by `maxIterations` — both deliberate safety valves that mean a pathological or slow-converging input could in principle still reach `Validate` with a too-short edge. |
| Collinear/near-collinear mid-edge vertices | `Clean` | Repaired: dropped once their perpendicular deviation from their own two neighbors is within `collinearityTolerance`. |
| A ring with fewer than three distinct vertices, including one `Clean` itself reduced to that state | `Validate` | Rejected, with its existing "fewer than three distinct vertices" message, unchanged by this stage. |
| Self-intersection, a hole outside its shell, nested holes/shells, non-positive area | `Validate` | Rejected; `Clean` never attempts to repair or even detect any of these — true topology stays validation's job alone. |
| Cross-polygon (multi-polygon) self-intersection | Neither | Out of scope for both `Clean` and `Validate` — see "Cross-polygon self-intersection" under "PropertyLine creation (Revit)" below for why `PropertyLine.IsValidBoundary` covers this instead. |

**Determinism.** `Clean` is a pure function of its own arguments: it never allocates a `Random`, reads the
clock, touches the filesystem, or otherwise depends on anything outside `boundary` and its four numeric
parameters. Vertex order is preserved throughout — no `HashSet`/`Dictionary` iteration order ever decides which
vertex survives or in what order survivors appear. The dedupe pass always keeps the ring's first vertex as a
fixed anchor and walks forward from it; the collinear-collapse pass evaluates every vertex against one
unchanging snapshot of its neighbors before removing any of them, so results never depend on visiting order.
Calling `Clean` twice with identical arguments always returns vertex-for-vertex identical rings
(`LocalBoundaryCleanerTests.CleanIsDeterministicAcrossRepeatedRuns`), and the same multi-pass fixed-point loop
is exercised directly by `CleanConvergesWhenACollinearCollapseExposesANewNearDuplicatePair`.

**Tolerance sourcing (Revit side, `CreateToposolidCommand`).** `commandData.Application.Application
.ShortCurveTolerance`/`.VertexTolerance` are read once at Stage 1 (Preflight), logged, and stored on
`DocumentContext` as `ShortCurveToleranceInternal`/`VertexToleranceInternal` (Revit-internal decimal feet,
unconverted — `SolidGround.Core` never references either `Application` member directly). At Stage 3, both
convert into the request's own `OutputUnit` via `UnitUtils.ConvertFromInternalUnits` (the same `ForgeTypeId
revitUnit` Stage 4 used to compute, moved to the top of Stage 3 since it depends only on
`context.Settings.Request.OutputUnit`, not on any acquisition result):

```csharp
double vertexTolerance = UnitUtils.ConvertFromInternalUnits(context.VertexToleranceInternal, revitUnit);
double minimumEdgeLength =
    UnitUtils.ConvertFromInternalUnits(context.ShortCurveToleranceInternal, revitUnit) * ShortCurveToleranceMargin; // 2.0
double collinearityTolerance = vertexTolerance; // reuses VertexTolerance's own coincidence-scale distance,
                                                 // a distinct named constant so it can be tuned independently later.
LocalBoundary boundary = LocalBoundaryCleaner.Clean(rawBoundary, vertexTolerance, collinearityTolerance, minimumEdgeLength);
```

`shortCurveToleranceMargin` (2.0) is a conservative multiplier so unit-conversion/local-origin floating-point
noise cannot reintroduce an edge only technically above Revit's own raw minimum.

**`LocalBoundaryValidator.Validate`** (Stage 1) gained one new, trailing, optional parameter, fully
source/binary-compatible with every pre-existing call site:

```csharp
public static LocalBoundaryValidationResult Validate(
    LocalBoundary boundary,
    IReadOnlyList<LocalTerrainSample> retainedSamples,
    int pointBudget,
    double? containmentToleranceMeters = null,
    double? minimumEdgeLength = null);   // null (the default) skips this check.
```

`ValidateRingShape` gains one more check (only when `minimumEdgeLength is not null`): for every cyclically
consecutive edge, if its length is `> 0` but `< minimumEdgeLength`, add one aggregated problem line per ring
(count + shortest length found) — a distinct message from the existing zero-length wording. `CreateToposolidCommand`'s
Stage 3 call site passes `minimumEdgeLength` as `Validate`'s 5th positional argument. This check still exists
once `Clean` also uses `minimumEdgeLength` as defense in depth, matching `LocalBoundaryValidator`'s established
"never repairs, only detects" role: `Clean` is expected to remove every edge this check would otherwise reject
in ordinary operation, but `Clean` also deliberately stops early without erroring if a ring would drop below 3
vertices, and is bounded by `maxIterations` — both deliberate safety valves that mean a pathological or
slow-converging input could in principle still reach `Validate` with a too-short edge. Rather than let that
reach `BoundaryGeometryBuilder.BuildProfiles`'s `Line.CreateBound` call and throw
`Autodesk.Revit.Exceptions.ArgumentsInconsistentException` there uncaught — exactly the "Known limitations"
follow-up `revit-toposolid-creation.md` previously recorded as unfixed — this check still catches it, at
Stage 3, with a clear, actionable Preflight-style message, before Stage 4 ever builds a `CurveLoop`.

**Correction to an earlier prep note.** A candidate test that would call
`SolidGround.Revit.Geometry.BoundaryGeometryBuilder.BuildProfiles` from the Core-only test assembly cannot be
written: `ArchitectureTests.TestAssemblyReferencesNeitherTheRevitApiNorTheRevitHostAssembly` asserts the test
assembly never references `RevitAPI` or `SolidGround.Revit`. The Core-level tests stop at "the cleaned
boundary passes `LocalBoundaryValidator.Validate`"; offline, plain-text tests in `RevitHostFilesTests.cs`
(matching the existing `RevitIni`-guard precedent) instead assert the *source ordering and gating* of the
Revit-side call sites (see "Tests" below).

## PropertyLine creation (Revit)

### AOI-kind gate: how the command knows, and what a non-parcel run does

`PropertyLine` creation is gated to parcel areas of interest (owner decision 3): a bounding-box or radius AOI
creates a `Toposolid` and nothing else, exactly as every run did before this issue — no `PropertyLine`, and, just
as importantly, no error, no Preflight problem, and no dialog-headline difference for that run. This reuses a
discriminant the codebase already has: `TerrainRequestSettings.AreaOfInterest.Kind` is an `AreaOfInterestKind`
(`BoundingBox`, `Radius`, `Parcel`), the same discriminant `RunDocumentPreflight` already compares against
`AreaOfInterestKind.Parcel` at its own parcel-geometry-file-reading step. No new field on `DocumentContext`, no
new settings key: the gate expression, `bool isParcelAoi = context.Settings.Request.AreaOfInterest.Kind ==
AreaOfInterestKind.Parcel;`, is evaluated inline at Stage 4 (`ExecuteCore`) and again at Stage 5
(`RunTransaction`), deliberately not cached as a shared field, mirroring the existing Stage 1 precedent's own
inline-comparison style.

Three call sites, all conditioned on `isParcelAoi`, all "skip silently" when it is `false`:

1. **Stage 4 (Geometry construction, pre-transaction).** `PostCreationVerification.BoundaryIsValidPropertyLine`
   is called only `if (isParcelAoi)`, against `propertyLineProfiles` — a second, independently-built
   `IList<CurveLoop>`, never the `profiles` list `ToposolidCreationService.Create` already consumes (see "Why
   PropertyLine creation builds its own independent CurveLoop list" below). For a bounding-box/radius run this
   block is never reached at all, so `propertyLineProfiles` is never constructed for that run.
2. **Stage 5 (Transaction), creation.** `PropertyLineCreationService.Create(document, propertyLineProfiles)` is
   called only `if (isParcelAoi)`; a non-parcel run's `PropertyLine? propertyLine` local is simply `null` — no
   Revit API call attempted, so `PropertyLineCreationException` can structurally never be thrown for a
   non-parcel run.
3. **Stage 5 (Transaction), post-create verification.** `PostCreationVerification.VerifyPropertyLine` is called
   only when `propertyLine is not null`; for a non-parcel run, `propertyLineVerification` is instead a fixed
   passing sentinel, `new VerificationResult(true, "No property line was created for this bounding-box/radius
   area of interest.")` — mirroring the identical off-path idiom used for `sharedCoordinatesVerification` when
   the opt-in is off.

### Why PropertyLine creation builds its own independent CurveLoop list, not `profiles` itself

No source this note's own evidence base contains says anything about whether `Toposolid.Create`/`PropertyLine
.Create` copy their input `CurveLoop` data into an independent representation or instead retain, and
potentially later invalidate or mutate, the caller's own objects. This is not hypothetical: `CurveLoop` is
confirmed, by reflection, to implement `IDisposable` — a disposable, kernel-backed geometry handle, not simple
value data — so aliasing the same instances across two independent element-creation calls is a genuine,
unverified assumption. Rather than guess, `PropertyLineCreationService.Create` is fed its own,
separately-constructed `IList<CurveLoop>`, built by a second `BoundaryGeometryBuilder.BuildProfiles` call over
the identical `boundary`/`constantZInternal`/`revitUnit` inputs — cheap and deterministic (pure in-memory
geometry construction, no I/O, no Revit API call), and skipped entirely for a non-parcel run.

`PostCreationVerification.cs` gained two peer static methods, alongside its existing pre-transaction
`AllProfilesArePlanar` and post-create `Verify`:

```csharp
// Stage 4, pre-transaction -- mirrors AllProfilesArePlanar's placement and Result.Cancelled-on-failure handling.
internal static bool BoundaryIsValidPropertyLine(IList<CurveLoop> profiles, out string? problem);

// Stage 5, post-create, inside the transaction -- a cheap sanity check using PropertyLine's own
// IsClosedLoop()/Area instead of a SlabShapeEditor/vertex-count surface, which PropertyLine has none of.
internal static VerificationResult VerifyPropertyLine(PropertyLine propertyLine);
```

`BoundaryIsValidPropertyLine` needs no separate cross-loop-planarity check: `BoundaryGeometryBuilder.BuildProfiles`
already gives every ring of every polygon the identical `constantZInternal` (the boundary-Z decision, the same
invariant `AllProfilesArePlanar` already relies on for the `Toposolid`), so every loop is already coplanar and
horizontal by construction. Cross-polygon self-intersection (a `LocalBoundary` with more than one
`LocalBoundaryPolygon`) is not checked by `LocalBoundaryValidator` today; `PropertyLine.IsValidBoundary`'s own
doc comment ("should not intersect with each other") is written over the whole `IList<CurveLoop>`, so this
design relies on Revit's own verified check for that condition rather than teaching `LocalBoundaryValidator` a
new NTS-based polygon-vs-polygon overlap check. Now that `PropertyLine` creation is parcel-AOI-only, this
check's only reachable trigger is a multi-polygon parcel; no known practical case in this repository's current
parcel fixtures triggers this, flagged as a residual limitation, not fixed.

### Transaction flow (`CreateToposolidCommand.RunTransaction`, Stage 5)

```csharp
toposolid = ToposolidCreationService.Create(document, profiles, points, context.ToposolidType.Id, context.Level.Id, ToposolidCreationService.DefaultStrategy);
PropertyLine? propertyLine = isParcelAoi
    ? PropertyLineCreationService.Create(document, propertyLineProfiles!)
    : null;

document.Regenerate();

VerificationResult verification = PostCreationVerification.Verify(toposolid, expected, points, ToposolidCreationService.DefaultStrategy, toleranceInternal, context.NativeToposolidMaxPointThreshold);
VerificationResult propertyLineVerification = propertyLine is not null
    ? PostCreationVerification.VerifyPropertyLine(propertyLine)
    : new VerificationResult(true, "No property line was created for this bounding-box/radius area of interest.");

// (shared-coordinates write -- see "Shared-coordinates detection, write, and verification" below)

if (!verification.Passed || !propertyLineVerification.Passed || !sharedCoordinatesVerification.Passed || failureLog.HasBlockingFailure)
{
    // An explicit, ordered 4-way choice: toposolid verification, then property-line verification, then
    // shared-coordinates verification, then the blocking-Revit-failure fallback, in that fixed order.
    (string headline, string detail) =
        !verification.Passed ? ("The created toposolid's geometry did not match the source data; the change was undone.", verification.Detail)
        : !propertyLineVerification.Passed ? ("The created property line did not verify; the change was undone.", propertyLineVerification.Detail)
        : !sharedCoordinatesVerification.Passed ? ("The shared-coordinates write did not verify; the change was undone.", sharedCoordinatesVerification.Detail)
        : ("Revit reported a problem while finishing this run.", string.Join(" | ", failureLog.Messages));
    TransactionStatus rolledBack = transaction.RollBack();
    return ShowTransactionOutcome(rolledBack, headline, detail);
    // No partial element either way: the whole transaction (Toposolid + PropertyLine, when attempted, + any
    // shared-coordinates write) rolls back together.
}
```

**Review fix:** the blocking-Revit-failure fallback headline above was broadened from the pre-Issue-#30 wording
("...while creating the toposolid.") since `failureLog` can now also be populated by `PropertyLine` creation or
the shared-coordinates write's own `document.Regenerate()`, not only the `Toposolid`'s.

The existing `catch (ToposolidCreationException ex)` clause widens to
`catch (Exception ex) when (ex is ToposolidCreationException or PropertyLineCreationException or
SharedCoordinatesWriteException)`, branching the dialog headline on the exception's runtime type. For a
non-parcel run, `PropertyLineCreationService.Create` is never called, so `PropertyLineCreationException` can
structurally never be thrown on that path; the `SharedCoordinatesWriteException` branch is likewise unreachable
whenever `sharedCoordinates.writeIfAbsent` is off (the shipped default).

## Shared-coordinates detection, write, and verification (Revit)

**Updated 2026-09-27 (live-evidence fix).** The design below through 2026-09-26 read the survey point's own
"clipped" flag as part of the detection proxy, and the writer set that same flag as its own after-the-fact
marker. A live Revit 2027 session (manual evidence Step 14.5) found that a brand-new document opened from
Revit 2027's own default template (`Default_I_ENU.rte`) already reports that flag set at the internal origin,
so a proxy that read it refused the opt-in write on a genuinely uncoordinated model. See "Shared-coordinates
detection" and "Why `Write` no longer sets `Clipped`" below for the corrected design, and "Known limitations"
for the plain-English record of the live finding itself.

`src/SolidGround.Revit/Transactions/SharedCoordinatesGate.cs` holds two static classes. The actual "already
coordinated" decision lives in the Revit-free, unit-tested `SolidGround.Core.Transformations
.SharedCoordinateDetection.LooksAlreadyCoordinated` (plain doubles/ints in, `bool` out, no Revit type anywhere
in its signature); `SharedCoordinatesDetector` only reads the raw Revit values that function needs and hands
them across, then logs every value read and the result:

```csharp
internal static class SharedCoordinatesDetector
{
    private const double AngleToleranceRadians = 1e-9;

    internal static bool LooksAlreadyCoordinated(Document document, double lengthToleranceInternal)
    {
        ProjectPosition projectPosition = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        XYZ surveyPointPosition = BasePoint.GetSurveyPoint(document).Position;
        int projectLocationCount = document.ProjectLocations.Size;

        bool result = SharedCoordinateDetection.LooksAlreadyCoordinated(
            projectPosition.EastWest, projectPosition.NorthSouth, projectPosition.Elevation, projectPosition.Angle,
            surveyPointPosition.X, surveyPointPosition.Y, surveyPointPosition.Z,
            projectLocationCount, lengthToleranceInternal, AngleToleranceRadians);

        AddInLog.Info(/* every raw value read above, plus result */);
        return result;
    }
}

internal static class SharedCoordinatesWriter
{
    internal static ProjectPosition Write(Document document, double eastWestInternal, double northSouthInternal, double elevationInternal)
    {
        ProjectPosition position = new(eastWestInternal, northSouthInternal, elevationInternal, angle: 0d);
        try
        {
            document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentNullException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            throw new SharedCoordinatesWriteException($"Revit rejected the shared-coordinates write: {ex.Message}", ex);
        }

        return position;
    }

    internal static bool VerifyWritten(Document document, ProjectPosition requested, double toleranceInternal, out string? problem);
}
```

`lengthToleranceInternal` is `Application.VertexTolerance`, already read once at Preflight by
`LogAndReadGeometryTolerances` (see "Geometry cleanup contract" > "Tolerance sourcing" above) and passed in here
rather than read a second time; `AngleToleranceRadians` is a small, fixed constant (`1e-9` radians), the same
value and rationale `SharedCoordinatesWriter.VerifyWritten`'s own tolerance already uses (see "Verifying and
recording the zero-rotation value" below) -- `ProjectPosition.Angle` is either exactly its startup value (0) or
a real value a user or a prior SolidGround write actually set, so it does not need a caller-supplied,
machine-specific tolerance the way the length axes do.

### Shared-coordinates detection

No direct Revit API member answers "has this document's shared coordinates already been set" (see the API
table above). **Corrected 2026-09-27 (live-evidence fix; supersedes owner decision 1's original proxy):** a
live Revit 2027 session (manual evidence Step 14.5, Revit 2027 build `27.0.10.13`) opened a brand-new document
from Revit's own default template (`Default_I_ENU.rte`) and read `ActiveProjectLocation.GetProjectPosition
(XYZ.Zero)` → `EastWest=0, NorthSouth=0, Elevation=0, Angle=0`, the survey point's own `Position=(0,0,0)`,
`ProjectLocations.Size=1`, and the survey point's own `Clipped=True`. The original proxy's `!surveyPoint.Clipped`
term made this brand-new, never-touched document read as "already coordinated" — refusing the opt-in write on
exactly the uncoordinated case it exists to allow. **The default template ships the survey point clipped, so a
clipped survey point is not evidence of shared coordinates.**

The corrected proxy drops that term entirely and never reads it again, in any form. The actual decision is a
pure, Revit-free function, `SolidGround.Core.Transformations.SharedCoordinateDetection.LooksAlreadyCoordinated`
(plain doubles/ints in, `bool` out — see "Shared-coordinates detection, write, and verification (Revit)" above
for the exact signature): **already coordinated** (`true`) when any of the following holds, otherwise **never
coordinated** (`false`):

- `document.ProjectLocations.Size > 1` (unchanged from the original proxy — a second, coarser, independent
  signal biased toward refusing rather than under-refusing).
- Any of the active `ProjectPosition`'s `EastWest`/`NorthSouth`/`Elevation` has an absolute value greater than
  the caller's length tolerance (`Application.VertexTolerance`).
- The active `ProjectPosition`'s `Angle` has an absolute value greater than a small, fixed angle tolerance
  (`1e-9` radians).
- The survey point's own `Position` is farther than the length tolerance from the internal origin `(0,0,0)`
  (the one signal the original proxy already had, now compared as a true 3D distance rather than combined with
  `Clipped`) — directly supported by `Document.ResetSharedCoordinates()`'s own doc comment ("survey point will
  be reset back to startup location, where it coincides with the Internal Origin"), combined with
  `InternalOrigin.Position` always being `(0,0,0)` (confirmed for Issue #13).

Testing the active `ProjectPosition` (not only the survey point) is what makes this proxy strictly stronger
than the one it replaces, not merely different: a document's shared coordinates can be set (via
`SetProjectPosition`, "Specify Coordinates at Point," or Acquire Coordinates) without ever moving the survey
point's own internal-coordinate `Position` away from `(0,0,0)` at all — confirmed by `BasePoint.SharedPosition`'s
own doc comment, and the exact mechanism "Why `Write` no longer sets `Clipped`" below relies on. A proxy that
looked at the survey point alone would miss that case entirely; this one does not.

**Corrected 2026-09-26/27 (live-evidence fix, Stage 4):** manual evidence Step 14.5's re-run (see "Manual
evidence (Revit 2027, 2026-09-26/27)" below) found that `SetProjectPosition` — the exact call
`SharedCoordinatesWriter.Write` issues — does move the survey point's own internal `Position`, from `(0,0,0)`
to the negative of the newly written coordinates, as an intrinsic side effect of that one call.
`BasePoint.SharedPosition`'s doc comment (a member this note's own API-surface table above does not list,
unlike `.Position`) does not, therefore, support reading `SetProjectPosition` as a mechanism that sets shared
coordinates without ever moving the survey point's `Position`; that specific example is withdrawn. The proxy
remains strictly stronger for a reason independent of it: testing the active `ProjectPosition` directly, rather
than inferring coordination from the survey point's `Position` alone, means detection does not depend on
whether a given mechanism happens to move `Position` as a side effect the way `SetProjectPosition` now
demonstrably does — Revit's own "Specify Coordinates at Point" and Acquire Coordinates commands have not been
separately verified against this document's own live evidence, and this proxy relies on neither behaving one
way or the other.

**Caveats the owner accepted knowingly (owner decision 1), reduced but not eliminated by this fix:** a user can
manually run "Specify Coordinates at Point" (moving the project position, the survey point, or both) without
ever truly acquiring or publishing coordinates from a real survey, so this remains a heuristic, not a certified
"ran Acquire/Publish Coordinates" bit. The theoretical case of a document that genuinely acquired coordinates
which happen to coincide with the internal origin at zero rotation would still produce a false negative — now
requiring every one of the four signals above to simultaneously read as "uncoordinated," rather than depending
on one single flag the way the original proxy did. Step 14.6 of the manual evidence plan below exists to probe
this against a real document.

### Why `Write` no longer sets `Clipped`

**Corrected 2026-09-27 (live-evidence fix).** Through 2026-09-26, `Write` set the survey point's own `Clipped`
property to `true` immediately after `SetProjectPosition`, reasoning that without it, a second run against
SolidGround's own prior write would be self-defeating: `SetProjectPosition` changes the `ProjectLocation`
transform, not the survey point's own internal-coordinate `Position` (confirmed by `BasePoint.SharedPosition`'s
own doc comment), so the survey point's `Position` would most likely still read `(0,0,0)` afterward, and the
pre-2026-09-27 proxy depended on `Clipped` alone to notice the change.

**Further corrected 2026-09-26/27 (live-evidence fix, Stage 4):** the premise above — that `SetProjectPosition`
leaves the survey point's own internal-coordinate `Position` unchanged — is itself now known false. Manual
evidence Step 14.5's re-run (see "Manual evidence (Revit 2027, 2026-09-26/27)" below) found `Position` moves
from `(0, 0, 0)` to the negative of the newly written east-west/north-south as an intrinsic side effect of that
same call. This does not revive the pre-2026-09-27 reasoning for setting `Clipped`, and the conclusion below is
unchanged: the corrected proxy still does not depend on `Clipped`, because it reads the genuinely non-zero
`ProjectPosition` `Write` leaves behind directly, regardless of whether `Position` also moves alongside it.

The corrected proxy above no longer needs any such marker: `Write`'s own `SetProjectPosition` call leaves a
genuinely non-zero, real `ProjectPosition` behind — that is the entire point of the write — so a subsequent
`LooksAlreadyCoordinated` call reads that same non-zero `EastWest`/`NorthSouth`/`Elevation` (or a non-zero
`Angle`, on some future design that computes one) directly and correctly reports "already coordinated," with no
dependency on `Clipped` in either direction. `Write` therefore no longer touches `Clipped` at all, leaving
whatever clipped state the user's own document already had exactly as it was: SolidGround has no legitimate
reason to change a property it does not itself rely on, and a user may have left the survey point unclipped for
reasons entirely unrelated to shared coordinates. Manual evidence Step 14.6's second check (running the opt-in
again against Step 14.5's own document) still applies unchanged: that second run must still refuse, but now
because of the non-zero project position `Write` left behind, not because of `Clipped`.

### Settings: the shared-coordinates opt-in

Expressed as a settings flag until Issue #31's dialog hosts a real checkbox, strict-decoded, default `false`,
following `RevitTargetSettings`'s own existing `level`/`toposolidType` JsonNode-split precedent — as a new
sibling record, not folded into `RevitTargetSettings` (whose own doc comment scopes it to Level/ToposolidType
name overrides, a different concern).

`src/SolidGround.Revit/Settings/RevitSharedCoordinatesSettings.cs`:

```csharp
internal sealed record RevitSharedCoordinatesSettings(bool WriteIfAbsent);
```

`RevitSettings.cs` gains a third property: `internal sealed record RevitSettings(TerrainRequestSettings
Request, RevitTargetSettings Target, RevitSharedCoordinatesSettings SharedCoordinates);`. `RevitSettingsIo
.TryLoad` reads and removes `"sharedCoordinates"` the same way it already reads/removes `"level"`/
`"toposolidType"`. `RevitSettingsIo.TemplateJson` gains one new commented block, placed after `toposolidType`,
before `output` — this exact text is kept byte for byte in sync across `RevitSettingsIo.cs`,
`revit-toposolid-creation.md`'s "Template" section, and `TerrainRequestSettingsTests.cs`'s `ShippedTemplateText`:

```jsonc
  // "writeIfAbsent": true lets SolidGround write this run's terrain origin as this model's shared
  // coordinates (ActiveProjectLocation), but ONLY when the model has none yet -- Preflight refuses when it
  // looks like the model already has shared coordinates set. Default false: unchanged from Issue #15.
  "sharedCoordinates": { "writeIfAbsent": false },
```

### Preflight refusal (Stage 1, `RunDocumentPreflight`)

A new step, placed after the Level/ToposolidType/`Revit.ini` checks and before `OrphanCheck.Capture` (error
catalogue row 9b, alongside row 9a's own "Preflight-only, pre-transaction, machine/document-state-dependent
refusal" precedent) — accumulates into `problems` like every other check in that stage, no early return:

```csharp
if (settings.SharedCoordinates.WriteIfAbsent && SharedCoordinatesDetector.LooksAlreadyCoordinated(document, vertexToleranceInternal))
{
    problems.Add(
        "sharedCoordinates.writeIfAbsent is enabled, but this model already appears to have shared " +
        "coordinates set (a non-zero shared project position or angle, a moved survey point, or more than " +
        "one project location). SolidGround will not overwrite existing shared coordinates. Set " +
        "sharedCoordinates.writeIfAbsent to false to run without writing shared coordinates.");
}
```

**2026-09-27 live-evidence fix:** the message no longer says a clipped survey point is evidence of prior
coordination (the default template ships one clipped) — see "Shared-coordinates detection" above for the live
finding this corrects. `vertexToleranceInternal` is the same value `LogAndReadGeometryTolerances` already read
just above this check, reused rather than read a second time.

`RunDocumentPreflight` also reads and logs the two new tolerances here, stored on the widened `DocumentContext`
record (new fields `ShortCurveToleranceInternal`, `VertexToleranceInternal`, alongside the existing
`NativeToposolidMaxPointThreshold`).

### The write itself and its source value

The value written is this run's own terrain local-frame origin (owner decision 2) — the same projected source
coordinate already in the placement record's `localOrigin.sourceX/sourceY/sourceElevation` — at angle = 0.
`document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position)`: passing `XYZ.Zero` as the point means
"Revit's internal origin becomes (EW, NS, Elevation) in shared coordinates," exactly SolidGround's own local
frame's origin convention (`LocalCoordinateFrame.ToLocal`/`ToLocalHorizontal` subtract `Origin`, so local
`(0,0,0)` already *is* the terrain's source origin).

**Zero rotation, stated plainly.** `angle = 0` means SolidGround computes no grid-convergence correction
anywhere today, so this design treats the terrain's projected grid north — the source projected CRS's own
north, e.g. UTM zone 15N grid north for the example site — as Revit's `Angle`-from-True-North reference. Grid
north and true north coincide only exactly on a projection's own central meridian and diverge (by the real-world
grid convergence angle) everywhere else; SolidGround neither computes nor corrects for that divergence, so a
model positioned this way is anchored correctly in horizontal/vertical position but its true-north rotation
carries whatever small error the site's own distance from the UTM zone's central meridian implies.

**Elevation is written, not left at Revit's default.** `ProjectPosition`'s 4-argument constructor is atomic
across `EastWest`/`NorthSouth`/`Elevation`/`Angle` — there is no partial-axis overload. AGENTS.md's "Data and
numeric contracts" already requires SolidGround to carry the original offset, CRS, datum, and unit through
provenance and to reject any workflow that cannot reconstruct source coordinates from local coordinates plus
metadata — a rule this design reads as applying to every axis, not only the horizontal two. Leaving `Elevation`
at an arbitrary default while writing real `EastWest`/`NorthSouth` values would produce a shared-coordinates
anchor reversible on two axes and silently wrong on the third.

**Review fix: only attempted once the toposolid and property line have already verified, with no blocking
Revit failure pending.** `RunTransaction` gates this whole block on `verification.Passed &&
propertyLineVerification.Passed && !failureLog.HasBlockingFailure`, in addition to
`sharedCoordinates.writeIfAbsent`. Without this guard, a `SharedCoordinatesWriteException` thrown from inside
this block would be caught by the widened Stage-5 catch clause (see "Transaction flow" above), which selects its
headline purely from the caught exception's runtime type and never inspects `verification`/
`propertyLineVerification` — masking an already-known, higher-priority toposolid or property-line verification
failure behind the lower-priority shared-coordinates message, contrary to this note's own fixed
toposolid/property-line/shared-coordinates/blocking-failure order. Skipping the write once a higher-priority
check has already failed leaves `sharedCoordinatesVerification` at its passing sentinel, so the ordered check in
"Transaction flow" still reports the true, first cause.

**Why a second `document.Regenerate()` call is required before verification.** Whether Revit's internal
`ProjectLocation` transform recalculation `SetProjectPosition`'s own remarks describe is applied lazily
(needing `Regenerate()`) or immediately is not stated by any documentation source found. Rather than depend on
an unconfirmed same-transaction read-after-write assumption, `Write` is followed by `document.Regenerate()`
before `VerifyWritten` reads it back — mirroring the established Create-then-Regenerate-then-Verify pattern
already used for the `Toposolid` and `PropertyLine`. Manual evidence Step 14.5 records which explanation (needed
vs. already-reflected) was actually true, though the code no longer depends on the answer either way.

### Verifying and recording the zero-rotation value

`SharedCoordinatesWriter.VerifyWritten` compares `EastWest`/`NorthSouth`/`Elevation` against `toleranceInternal`
and `Angle` against a tight, exact-equality-scale tolerance (`1e-9` radians): `requested.Angle` is always
exactly the `0d` literal `Write` passes, never a computed value, so this catches any unexpected Revit-side
rotation side effect without being sensitive to the floating-point noise the other three axes already absorb.
`PlacementSharedCoordinatesWriteRecord` carries `AngleInternal`, recorded Revit-internal (radians) rather than
converted like its `EastWest`/`NorthSouth`/`Elevation` siblings (which stay in `Origin`'s own native unit,
named by the paired `HorizontalUnit`/`VerticalUnit` fields, not always meters — see "Unit convention for the
shared-coordinates value" below) — an angle has no length unit to convert into. Manual evidence Steps 14.5/14.6 explicitly instruct the tester to read back and
record `ProjectLocation.GetProjectPosition(XYZ.Zero).Angle`, confirming it round-trips as exactly `0`.

### Unit convention for the shared-coordinates value

Three different units are in play for this one write, and conflating any two of them is the exact failure mode
this section rules out:

1. **`LocalCoordinateFrame.Origin`'s own unit — always the source projected/vertical reference's own native
   unit, never `OutputUnit`, and NOT always meters.** `Origin` is `LocalCoordinateFrame`'s own constructor
   argument, stored untouched; only `ToLocal`/`ToLocalHorizontal` apply `OutputUnit`, and neither is ever called
   on `Origin` itself. For `fetch` mode's `NorthAmericanUtmWellKnownText` UTM zones paired with the NAVD88
   vertical reference, that native unit is meters for both axes — but `process` mode parses its horizontal
   reference from a user-supplied `.prj` (which can declare a non-metric projected unit, for example a State
   Plane zone in US survey feet or international feet — `WellKnownTextReferenceParser`'s own
   `RecognizesTheUsSurveyFootConversionFactor`/`RecognizesTheInternationalFootConversionFactor` tests) and its
   vertical unit from `process.verticalUnit`, either of which can be a foot unit. The code below never hardcodes
   meters — it reads the unit from the reference itself, exactly because this case is real.
2. **The pipeline's own `OutputUnit` (default `LengthUnit.UsSurveyFoot`) — a completely different value, feeding
   a completely different conversion.** `OutputUnit` governs only the boundary/point coordinates
   `BoundaryGeometryBuilder.BuildPoints`/`BuildProfiles` build from `LocalCoordinateFrame.ToLocal`/
   `ToLocalHorizontal`'s already-shifted results. It has no bearing on `Origin`, and must never be reached for
   this write.
3. **`ProjectPosition.EastWest`/`NorthSouth`/`Elevation` — Revit's own internal length representation,
   documented "measured in decimal feet," never independently assumed to equal a specific real-world foot.**
   This design never converts a `double` into Revit-internal units by any means other than Revit's own
   `UnitUtils.ConvertToInternalUnits(value, forgeTypeId)`, always tagging `value` with the `ForgeTypeId`
   matching *that value's own true unit*.

**The exact conversion.** `origin.X`/`.Y` convert via `RevitUnitConversion.ToInternal(value, horizontalUnit)`;
`origin.Elevation` converts via `RevitUnitConversion.ToInternal(value, verticalUnit)` — where
`horizontalUnit`/`verticalUnit` are `Origin`'s own native units, obtained from `SharedCoordinateOrigin.Resolve`,
**never** `context.Settings.Request.OutputUnit`/the Stage 4 `revitUnit` local — the single most likely
implementer mistake here, since `revitUnit`/`OutputUnit` are already the unit values sitting in scope for
everything else `RunTransaction` does. Getting this wrong would silently misinterpret a projected-CRS meters
value as if it were already in US survey feet (the shipped default `OutputUnit`) before handing it to
`UnitUtils.ConvertToInternalUnits`, corrupting the shared-coordinates write by a factor of `1200/3937 ≈
0.3048006` with no exception anywhere.

`src/SolidGround.Core/Transformations/SharedCoordinateOrigin.cs` (new, Revit-free) resolves this once so no
Revit-side call site has to re-derive it:

```csharp
public static class SharedCoordinateOrigin
{
    public readonly record struct Resolved(Coordinate3D Origin, LengthUnit HorizontalUnit, LengthUnit VerticalUnit);

    public static Resolved Resolve(LocalCoordinateFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        LengthUnit horizontalUnit = frame.ProjectedHorizontalReference.Unit.LinearUnit!.Value;
        return new Resolved(frame.Origin, horizontalUnit, frame.VerticalReference.Unit);
    }
}
```

The Revit-side call site (`CreateToposolidCommand.RunTransaction`):

```csharp
SharedCoordinateOrigin.Resolved resolvedOrigin = SharedCoordinateOrigin.Resolve(outcome.Payload.Provenance.LocalFrame);
double eastWestInternal = RevitUnitConversion.ToInternal(resolvedOrigin.Origin.X, resolvedOrigin.HorizontalUnit);
double northSouthInternal = RevitUnitConversion.ToInternal(resolvedOrigin.Origin.Y, resolvedOrigin.HorizontalUnit);
double elevationInternal = RevitUnitConversion.ToInternal(resolvedOrigin.Origin.Elevation, resolvedOrigin.VerticalUnit);
```

See "Tests" below for `SharedCoordinateOriginTests`, which pins this helper's own contract against exact
numbers, including the US survey foot case specifically (the shipped default `OutputUnit`, and therefore the
value most likely to be reached for by mistake).

### Post-commit reporting (`ReportSuccess`, Stage 6)

`ReportSuccess` branches on `draft.SharedCoordinatesWrite.Attempted`: when `false` (the default, common path),
`OrphanCheck.Unchanged` runs exactly as before Issue #30 — completely unchanged behavior for the off-by-default
path. When `true`, it skips `Unchanged`'s comparison (which would otherwise misreport the intended write as an
"unexpected change") and logs the new position directly instead, reading it back fresh via
`context.Document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero)`. The placement record's
`sharedCoordinatesStatement` field is likewise two-valued now: the unchanged sentence when the opt-in never
fires, or a new sentence naming that SolidGround wrote shared coordinates this run, when it does — and the
success dialog's own body text is now built from that same `draft.SharedCoordinatesStatement` field instead of
carrying a second, independent, hardcoded copy of the disclaimer sentence, so the two can never drift apart.
**Corrected 2026-09-26/27 (live-evidence fix, Stage 4).** Through this closeout, the written-branch sentence
additionally claimed the project base point, survey point, and site location were "otherwise left unchanged."
Manual evidence Step 14.5's re-run log showed that is false: `ProjectLocation.SetProjectPosition` moves the
survey point's own internal `Position` from `(0, 0, 0)` to the negative of the newly written east-west/
north-south as an intrinsic side effect of that one call (see "Why `Write` no longer sets `Clipped`" above for
the related, already-corrected `Clipped` behavior — this is the survey point's `Position`, a different member).
The sentence now reads: "SolidGround wrote this run's terrain origin as this model's shared coordinates
(sharedCoordinates.writeIfAbsent). Revit moved the survey point to the new shared origin as part of that write;
SolidGround made no other change to the project base point or site location." The off-path (opt-in never fired)
sentence is untouched.

The success dialog also states the `PropertyLine` outcome, computed at render time from `draft.PropertyLine`
(the same structural field `BuildPlacementDraft` already threads through), placed as its own line immediately
after "Points retained: ..." and before the export-bundle path line:

```csharp
string propertyLineStatement = draft.PropertyLine.Created
    ? $"Property line: element id {draft.PropertyLine.ElementId!.Value}."
    : "Property line: not created (this area of interest is not a parcel boundary).";
```

Unlike `sharedCoordinatesStatement` (a free-text sentence that is itself part of the persisted schema,
inherited from Issue #15), the placement record's JSON never stores this sentence — only `propertyLine`'s own
structural fields (`created`/`elementId`/`areaInternal`) are persisted; a script reading the JSON checks
`propertyLine.created`, never dialog prose.

## Settings file reference — field table addition

| JSON path | Type | Required | Default | Validation |
| --- | --- | --- | --- | --- |
| `sharedCoordinates.writeIfAbsent` | boolean | no | `false` | none beyond JSON boolean decode; Preflight (row 9b) refuses the *run*, not the settings file, when `true` and the document already looks coordinated |

## Error catalogue extension

| # | Scenario | Stage | `Result` | What is shown |
| --- | --- | --- | --- | --- |
| 9b | `sharedCoordinates.writeIfAbsent` is `true` and `SharedCoordinatesDetector.LooksAlreadyCoordinated` is `true` | Doc Preflight | Cancelled | shared dialog; the exact message quoted above |
| 16b | `PropertyLine.IsValidBoundary(propertyLineProfiles)` returns `false` (**parcel AOI only**; structurally unreachable for a bounding-box/radius run) | Geometry construction | Cancelled | same "could not build a valid boundary" headline as row 13/16 |
| 18b | `PostCreationVerification.VerifyPropertyLine` fails, not closed / non-positive area (**parcel AOI only**, same reason) | Transaction | Cancelled if `RolledBack`, else Failed | "The created property line did not verify; the change was undone." + detail |
| 18c | `SharedCoordinatesWriter.VerifyWritten` fails | Transaction | Cancelled if `RolledBack`, else Failed | "The shared-coordinates write did not verify; the change was undone." + requested-vs-read-back detail |
| 20a | `PropertyLine.Create` throws (`PropertyLineCreationException`) (**parcel AOI only**, same reason) | Transaction | Cancelled if `RolledBack`, else Failed | "Revit rejected the generated property line boundary." + inner exception message |
| 20b | `SharedCoordinatesWriter.Write` throws (`SharedCoordinatesWriteException`) (opt-in only; review fix) | Transaction | Cancelled if `RolledBack`, else Failed | "Revit rejected the shared-coordinates write." + inner exception message |

No new `Result`-derivation branch: every new row reuses the existing `transaction.HasEnded() ?
transaction.GetStatus() : transaction.RollBack()` → `Cancelled` if `RolledBack` else `Failed` policy, exactly
like Issue #16's own rows 21a-21c. Rows 16b, 18b, and 20a apply only to a parcel AOI run: a bounding-box/radius
run never calls `PostCreationVerification.BoundaryIsValidPropertyLine`, `PropertyLineCreationService.Create`,
or `PostCreationVerification.VerifyPropertyLine` at all, so none of these three rows can fire for it. Row 20b
applies only when `sharedCoordinates.writeIfAbsent` is `true`: the opt-in off path never calls
`SharedCoordinatesWriter.Write` at all, so `SharedCoordinatesWriteException` can structurally never be thrown
for it. No new row exists for "a bounding-box/radius AOI creates no PropertyLine": that is not an error at all
(row 26, the success row, covers it).

## Placement record schema

Schema-version bump **2 → 3** (a counter independent of `TerrainProvenance.CurrentSchemaVersion` and of the
Extensible Storage schema's own version — three separate counters that share a field name). Two new top-level
objects, appended after `extensibleStorage`:

```jsonc
"propertyLine": { "created": true, "elementId": 123457, "areaInternal": 1652.34 },
// non-parcel AOI: "propertyLine": { "created": false, "elementId": null, "areaInternal": null }
"sharedCoordinatesWrite": {
  "attempted": false
  // when true: "eastWest": 449674.0, "northSouth": 4604563.0, "elevation": 183.10, "angleInternal": 0.0,
  //            "horizontalUnit": "meter", "verticalUnit": "meter", "verified": true
}
```

New C# types (`SolidGround.Core.Provenance`, alongside `PlacementRecord.cs`'s existing sibling records):

```csharp
public sealed record PlacementPropertyLineRecord(bool Created, long? ElementId, double? AreaInternal);

public sealed record PlacementSharedCoordinatesWriteRecord(
    bool Attempted, double? EastWest, double? NorthSouth, double? Elevation,
    double? AngleInternal, string? HorizontalUnit, string? VerticalUnit, bool? Verified);
```

`PlacementRecordDraft`/`PlacementRecord` each gain `PlacementPropertyLineRecord PropertyLine` and
`PlacementSharedCoordinatesWriteRecord SharedCoordinatesWrite` — neither is ever itself null; `Created`/
`Attempted` each carry their own off/on distinction, matching one identical shape for both fields, and matching
this repository's own established rendering idiom (a nested object that is always written, with presence/absence
signaled by a field inside it — never an entire top-level nested object silently omitted).
`PlacementPropertyLineRecord.AreaInternal` keeps this schema's existing `*Internal` suffix (matching
`boundaryPlaneElevation.constantZInternal`) because `PropertyLine.Area`'s own doc comment states only its
closed-loop-detection meaning, not an explicit unit sentence.

`sharedCoordinatesWrite.eastWest`/`northSouth`/`elevation` are recorded in `Origin`'s own native unit — named by
the paired `horizontalUnit`/`verticalUnit` fields, not always meters (see "Unit convention for the
shared-coordinates value" above) — not the raw Revit-internal `double` actually passed to `ProjectPosition`'s
constructor: the identical values already recorded in this same document's `localOrigin.sourceX`/`sourceY`/
`sourceElevation`, chosen so a human or script reader can compare the two side by side without first learning
Revit's internal-foot convention. `sharedCoordinatesWrite.angleInternal` is the one exception, recorded
Revit-internal (radians), never converted.
`sharedCoordinatesWrite.verified`, whenever `attempted` is `true`, is always `true` (never `false`) in any
placement record that reaches disk: a `false` `sharedCoordinatesVerification` rolls back the whole transaction,
so no placement record is ever written for that run at all. Whenever `attempted` is `false` — the shipped
default — `verified` is `null`, like every other detail field in this object.

## Non-goals

- No WPF dialog or real checkbox UI — that is #31's own scope.
- No true-north/grid-convergence angle computation — `ProjectPosition.Angle` is always written as `0` (see
  "Zero rotation, stated plainly" above).
- No Extensible Storage provenance extension to the `PropertyLine` element — Issue #16's schema stays
  Toposolid-only; the placement record is the lighter-weight mechanism this issue uses instead.
- No new Core-side polygon-vs-polygon (cross-loop) intersection check — relies on `PropertyLine
  .IsValidBoundary`'s own documented, Revit-verified cross-loop check instead of duplicating it in Core.
- No ability to *move* or *reset* existing shared coordinates — the opt-in only ever writes when Preflight's
  proxy says none exist yet; it never offers to overwrite.
- No `PropertyLine` for a bounding-box or radius AOI (owner decision 3): not a deferred feature; the owner
  considered and rejected labeling an arbitrary bounding-box rectangle or radius-circle approximation a
  "property line" at all.
- Does not touch Issue #33's `TerrainProvenance`/`AddressParcelProvenance` work, the Extensible Storage schema,
  ribbon/icons, or packaging/signing — all unrelated and unchanged.

## Tests

**`tests/SolidGround.Tests/LocalBoundaryCleanerTests.cs`** (Stage 1, Core-only): dedupe of exact/near-duplicate
cyclically-consecutive vertices including the implicit wrap-around closing edge, collapse of collinear/near-
collinear mid-edge vertices, a genuine sharp corner/spike preserved, shell-and-hole independence, multipolygon
independence, multi-pass convergence, the `Math.Max(vertexTolerance, minimumEdgeLength)` gap-band case,
determinism, integration with the unchanged `LocalBoundaryValidator.Validate` (a degenerate result still
rejected, a true self-intersection still rejected, a combined messy fixture still passes once cleaned), and
`ArgumentNullException` for a null boundary. `CoreExposesAPublicLocalBoundaryCleaner` (an architecture-style
reflection guard, placed in this file rather than `ArchitectureTests.cs` per that file's own additive-only
scope).

**`tests/SolidGround.Tests/LocalBoundaryValidatorTests.cs`** (Stage 1, additive): `ValidateRejectsAnEdgeShorterThanTheSuppliedMinimumEdgeLength`,
`ValidateAcceptsWhenMinimumEdgeLengthIsNull`, an aggregated-count case, and a case combined with the existing
zero-length check.

**`tests/SolidGround.Tests/SharedCoordinateOriginTests.cs`** (new, Core-only, Revit-free): `Resolve` returns
`Origin` and its own native units unchanged; `Resolve` ignores `OutputUnit` even when `OutputUnit` is the
shipped default (US survey foot) — the specific unit-mixup failure mode "Unit convention for the
shared-coordinates value" names; `Resolve` throws `ArgumentNullException` for a null frame.

**`tests/SolidGround.Tests/SharedCoordinateDetectionTests.cs`** (new, Core-only, Revit-free, 2026-09-27
live-evidence fix): pins `SharedCoordinateDetection.LooksAlreadyCoordinated`'s own contract, including the exact
default-template case the live finding above uncovered (all zero, `projectLocationCount=1`: `false`); each of
the four signals (`ProjectLocations.Size > 1`, each project-position axis, `Angle`, the survey-point distance)
returning `true` alone, including a negative-value case proving the comparison uses absolute value; the
survey-point check's own true-3D-distance behavior (a combined distance exceeding tolerance even though no
single axis does); just-inside/just-outside tolerance boundary pairs for the length and angle comparisons; a
written UTM-scale position (matching `SharedCoordinateOriginTests`' own example-site origin) returning `true`;
and `ArgumentOutOfRangeException` for every non-finite double argument (`NaN`, positive and negative infinity),
a negative length or angle tolerance, and a `projectLocationCount` below `1`.

**`tests/SolidGround.Tests/TerrainRequestSettingsTests.cs`** (additive): `ShippedTemplateText` gains the
identical new `sharedCoordinates` block; `JsonOptionsDecodesTheShippedTemplateTextVerbatim` adds
`requestShapedPortion.Remove("sharedCoordinates")` alongside its existing `level`/`toposolidType` removals.

**`tests/SolidGround.Tests/PlacementRecordRendererTests.cs`** (additive): `SchemaVersionIsNowThree` (replacing
`SchemaVersionIsNowTwo`); the document-property-order test gains `propertyLine`/`sharedCoordinatesWrite`; new
tests pin both records' rendering in the not-created/not-attempted default state (every detail field explicit
JSON `null`) and in the created/attempted state (every field populated, including the zero-rotation
`angleInternal`).

**`tests/SolidGround.Tests/RevitHostFilesTests.cs`** (additive, plain-text scans of `CreateToposolidCommand.cs`/
`RevitSettingsIo.cs`/`SharedCoordinatesGate.cs`, matching the file's established `RevitIni`-guard precedent —
the only way to backstop a fact only a live Revit process could otherwise exercise, from a test assembly that
cannot reference the Revit
API at all):

- `CreateToposolidCommandCleansTheBoundaryBeforeValidatingIt` — `LocalBoundaryCleaner.Clean(` appears before
  `LocalBoundaryValidator.Validate(` (the trailing `(` pins each to its one real call site, not a prose mention
  of the same method name in a doc comment elsewhere in the file).
- `CreateToposolidCommandCreatesThePropertyLineInsideTheSameTransactionAsTheToposolid` — both
  `ToposolidCreationService.Create(` and `PropertyLineCreationService.Create(` appear before the real
  `transaction.Commit();` (the trailing `;` excludes this file's own doc comment, which mentions
  `transaction.Commit()` with no trailing semicolon, well before the real call). **Review fix:** both calls are
  also asserted to appear *after* the file's one `transaction.Start()` occurrence (inside `RunTransaction`,
  Stage 5) — not only before `transaction.Commit();` — so a regression that hoisted either creation call back
  into Stage 4 (geometry construction, pre-transaction) would still satisfy the two "before commit" checks
  alone, since the real `transaction.Commit();` always sits later in the file regardless of where in Stage 4
  the call moved to; both bounds together pin AC1's "in one transaction" guarantee, not just "somewhere before
  commit".
- `CreateToposolidCommandGatesPropertyLineCreationToParcelAreasOfInterest` — deliberately does **not** merely
  check that `AreaOfInterestKind.Parcel` appears somewhere between the Stage 4 and Stage 5 call sites: that
  literal substring already occurs, unrelated to this issue, inside `RunDocumentPreflight`'s own pre-existing
  parcel-geometry-file-reading logic, which sits in that same wide range regardless of anything this issue
  adds — a `PropertyLine` created unconditionally would still pass a check written that way. Instead asserts
  that the actual gate identifier, `isParcelAoi` (a name this issue introduces new, with no pre-existing
  occurrence to confuse the check), appears within a small, fixed character window immediately preceding each
  of the two real call sites.
- `SharedCoordinatesWriteDefaultsToOffAndPreflightRefusesWhenAlreadyCoordinated` — the detector call and the
  refusal message text appear in `CreateToposolidCommand.cs`, both before the real `RunTransaction` method
  (review fix: not merely present anywhere in the file, so a future edit that relocated this identical check
  into the transaction stage would fail this test); the shipped template text in `RevitSettingsIo.cs` contains
  `"sharedCoordinates": { "writeIfAbsent": false }` verbatim. **Second review fix:** the two checks above only
  pin the detector call's *position* relative to `RunTransaction`, not whether it is actually conditioned on
  the opt-in setting at all — a regression that always ran the detector (or inverted/widened `WriteIfAbsent`
  so the detector ran regardless of the setting) would leave both untouched, while `RunTransaction`'s own write
  gate never re-checks "already coordinated" (only `WriteIfAbsent` again). A further assertion now anchors the
  exact, non-negated `if (settings.SharedCoordinates.WriteIfAbsent)` gate within a small, fixed window
  immediately preceding the detector call. **2026-09-27 live-evidence fix:** two further assertions confirm the
  refusal message no longer contains "is clipped" (case-insensitive) and does contain the corrected wording's
  own "a non-zero shared project position or angle" and "a moved survey point" phrases.
- `CreateToposolidCommandGatesTheSharedCoordinatesWriteToTheOptInSetting` (review fix) — mirrors
  `AssertGatedByIsParcelAoi`'s own technique: asserts `SharedCoordinates.WriteIfAbsent` appears within a small,
  fixed character window immediately preceding the real `SharedCoordinatesWriter.Write(` call site. **Second
  review fix:** that bare property-name substring cannot by itself tell a correct, non-negated gate from one
  that was inverted (`if (!context.Settings.SharedCoordinates.WriteIfAbsent && ...)`) or widened (the `&&`
  immediately after `WriteIfAbsent` loosened to `||`) — neither mutation removes the substring. A further
  assertion now anchors the exact, non-negated `if (context.Settings.SharedCoordinates.WriteIfAbsent && `
  prefix, so a future edit that deleted, inverted, or widened that gate (AC3's own "off, no write" guarantee)
  now fails this test instead of only being caught by the deferred manual Revit evidence session.
- `CreateToposolidCommandCatchesSharedCoordinatesWriteExceptionAlongsideItsSiblings` (review fix) — asserts
  `"or SharedCoordinatesWriteException)"` appears within a small, fixed character window (120 characters)
  immediately after the catch filter's `ex is ToposolidCreationException or PropertyLineCreationException`
  clause, so a regression that dropped this third exception type back out of the filter — reintroducing the
  bug where a real `SharedCoordinatesWriter.Write` rejection fell through to the generic Stage-5 catch-all and
  misattributed the failure to "the toposolid" (error catalogue row 20b) — is caught automatically instead of
  only by the deferred manual Revit evidence session.
- `SharedCoordinatesWriteUsesTheOriginsOwnNativeUnitNotOutputUnit` — backstops "Unit convention for the
  shared-coordinates value"'s own named "single most likely implementer mistake": asserts
  `SharedCoordinateOrigin.Resolve(` appears before `SharedCoordinatesWriter.Write(`, and that neither
  `context.Settings.Request.OutputUnit` nor the bare identifier `revitUnit` appears in the source text between
  them. The real conversion runs through `RevitUnitConversion.ToInternal`, which lives in `SolidGround.Revit`
  and opens with `using Autodesk.Revit.DB;` — `SolidGround.Tests` can never reference it directly
  (`TestAssemblyReferencesNeitherTheRevitApiNorTheRevitHostAssembly` forbids a `RevitAPI`/`SolidGround.Revit`
  reference), so only this plain-text scan, not a Core-level unit test, can catch a regression at this exact
  call site.
- `SharedCoordinatesDetectorNeverReadsTheSurveyPointsClippedProperty` (2026-09-27 live-evidence fix) — reads
  `SharedCoordinatesGate.cs`, slices out just the `SharedCoordinatesDetector` class body (from its own class
  declaration up to the next type, `SharedCoordinatesWriteException`), and asserts `.Clipped` does not appear
  anywhere in that slice — the only way to backstop, from a test assembly that cannot reference the Revit API,
  that a future edit does not reintroduce the exact defect this fix removes.
- `SharedCoordinatesWriterNeverSetsTheSurveyPointsClippedProperty` (2026-09-27 live-evidence fix) — the
  companion guard: slices out the `SharedCoordinatesWriter` class body (from its own class declaration to end of
  file) and asserts `.Clipped` does not appear anywhere in that slice either.
- `SharedCoordinatesStatementAccuratelyDescribesTheSurveyPointMove` (2026-09-26/27 live-evidence fix, Stage 4)
  — asserts the old, inaccurate "the project base point, survey point, and site location were otherwise left
  unchanged" substring is absent from `CreateToposolidCommand.cs`, that the corrected sentence's "Revit moved
  the survey point to the new shared origin as part of that write" and "SolidGround made no other change to
  the project base point or site location." both appear, and that the off-path (opt-in never fired) sentence
  is unchanged — the same falsifiable, plain-text technique as the two guards immediately above, for the same
  reason (a test assembly that cannot reference the Revit API).

Each of these three checks above (`CreateToposolidCommandGatesTheSharedCoordinatesWriteToTheOptInSetting`,
`CreateToposolidCommandCatchesSharedCoordinatesWriteExceptionAlongsideItsSiblings`, and
`SharedCoordinatesWriteUsesTheOriginsOwnNativeUnitNotOutputUnit`) was verified, by deliberately mutating the
shipped implementation and re-running the specific test, to actually fail against the broken version and pass
again once reverted (not merely written to pass
against the intended implementation without ever having been red). The two "Second review fix" gate-anchor
assertions added above (in `SharedCoordinatesWriteDefaultsToOffAndPreflightRefusesWhenAlreadyCoordinated` and
`CreateToposolidCommandGatesTheSharedCoordinatesWriteToTheOptInSetting`) were verified the same way against
both an inverted gate and, for the latter, a widened (`&&` to `||`) gate: each mutation was confirmed to fail
only the new assertion, with the pre-existing assertions in the same test still passing exactly as the review
finding describes, and reverting was confirmed to restore a fully passing suite.

The two 2026-09-27 `.Clipped`-absence guards above were verified against this repository's own pre-fix commit
`842cadc` rather than by a working-tree mutate-and-revert cycle: `git show 842cadc:src/SolidGround.Revit/Transactions/SharedCoordinatesGate.cs`
contains `!surveyPoint.Clipped` inside the pre-fix `SharedCoordinatesDetector` body and `.Clipped = true` inside
the pre-fix `SharedCoordinatesWriter` body, confirming both guards would have failed against that commit's own
code and only pass once each class stopped referencing the property. The 2026-09-27 message-wording assertions
added to `SharedCoordinatesWriteDefaultsToOffAndPreflightRefusesWhenAlreadyCoordinated` above were verified live,
in the ordinary red-then-green sense: added while `CreateToposolidCommand.cs` still carried the pre-fix message,
confirmed failing (`Assert.DoesNotContain` found `"is clipped"`), then confirmed passing once the message was
corrected. `SharedCoordinatesStatementAccuratelyDescribesTheSurveyPointMove` was verified the same way during
this closeout: added while the sentence still read "otherwise left unchanged" (confirmed failing — the
`Assert.DoesNotContain` assertion found the old substring), then confirmed passing once the sentence was
corrected.

## Manual evidence plan — Step 14 (continues Steps 1-8b, 9-13; settles AC4)

**Status: complete (2026-09-26/27).** See "Manual evidence (Revit 2027, 2026-09-26/27)" below for the executed
session's full results, including the 14.5 finding, its fix, and the 14.5/14.6 re-run.

Run first in `process` mode against the committed `example-site-synthetic.*` fixtures (opt-in off), then
repeated with `sharedCoordinates.writeIfAbsent: true` against (a) a brand-new document and (b) a document
already run once, by hand, through Revit's own "Specify Coordinates at a Point" or Acquire Coordinates.

14.1. **Baseline creation (opt-in off), both AOI branches.**
   - **1a — parcel AOI**, against the committed `example-site-synthetic-parcel.geojson`/`.wkt` fixture. Confirm
     a `Toposolid` AND a `PropertyLine` both exist afterward, and `OrphanCheck`'s post-commit log line still
     reads "unchanged," exactly as today. Confirm the success dialog's new line reads the element-id form and
     the placement record's `propertyLine.created` is `true` with a matching `elementId`/`areaInternal`. Also
     confirm the `PropertyLine`'s own boundary is geometrically congruent with the `Toposolid`'s (matching
     footprint area/extent, e.g. via `PropertyLine.GetBoundary()`/`.Area` compared against the `Toposolid`'s own
     boundary) — the first live confirmation that building two independent `IList<CurveLoop>` instances over
     the identical inputs actually produces two consistent, correctly shaped elements. Also record the two
     `AddInLog` lines this run now produces for the Revit-native tolerances: Stage 1 (Document Preflight) logs
     `Application.ShortCurveTolerance`/`Application.VertexTolerance` against the reference Revit 2027
     installation (raw internal-feet values), and Stage 3 (Geometry Preflight) separately logs the
     `vertexTolerance`/`collinearityTolerance`/`minimumEdgeLength` this run actually used, converted into
     `OutputUnit`.
   - **1b — bounding-box AOI**, against a small `areaOfInterest.boundingBox` built from the example site's own
     public coordinates (41.591194, -93.603806). Confirm a `Toposolid` is created and **no `PropertyLine`**
     element exists afterward, that Preflight and the transaction show no problem or error of any kind, that
     the success dialog's new line reads the "not created" sentence verbatim, and that the placement record's
     `propertyLine` is `{"created": false}` with no `elementId`/`areaInternal` present. Repeat once more with a
     `areaOfInterest.radius` AOI built from the same coordinates, confirming the identical "no PropertyLine"
     outcome.
14.2. **Save/reopen (1a's document).** Confirm the `PropertyLine` survives, with its boundary
   (`GetBoundary()`/`Area`) unchanged.
14.3. **Undo (1a's document).** One Ctrl+Z removes both the `Toposolid` and the `PropertyLine` together as a
   single Undo entry; Redo brings both back together.
14.4. **Rejected boundary rolls back both (parcel AOI only).** Force `PropertyLine.IsValidBoundary`/
   `VerifyPropertyLine` to fail (an engineered self-intersecting multi-polygon parcel fixture); confirm neither
   element exists afterward.
14.5. **Opt-in write against a never-coordinated document.** A 2026-09-27 run of this step against a brand-new
   document opened from Revit 2027's own default template (`Default_I_ENU.rte`, Revit build `27.0.10.13`) found
   the defect this note's 2026-09-27 live-evidence fix corrects: `ActiveProjectLocation.GetProjectPosition
   (XYZ.Zero)` read `EastWest=0, NorthSouth=0, Elevation=0, Angle=0`, the survey point's own `Position=(0,0,0)`,
   `ProjectLocations.Size=1`, and the survey point's own `Clipped=True`. The pre-fix proxy's `!surveyPoint
   .Clipped` term made `LooksAlreadyCoordinated` return `true` for this brand-new, never-touched document, and
   Preflight refused the opt-in on exactly the uncoordinated case it exists to allow — the default template
   ships the survey point clipped, so a clipped survey point is not evidence of shared coordinates. This step
   must be re-run against the corrected proxy: confirm `LooksAlreadyCoordinated` now logs `false` for this exact
   document at Preflight, the run proceeds, and `ActiveProjectLocation.GetProjectPosition(XYZ.Zero)` afterward
   matches the terrain's own recorded local origin (converted). Also read back and record `ProjectPosition
   .Angle`: confirm it round-trips as exactly `0`; record it in the placement record's
   `sharedCoordinatesWrite.angleInternal` field alongside the dialog/log evidence this step already captures.
   Also log whether `SharedCoordinatesWriter.VerifyWritten`'s read-back matched on the first attempt (i.e.,
   whether the `document.Regenerate()` call between `Write` and `VerifyWritten` was actually load-bearing) — the
   code no longer depends on this answer either way, but the session should record it.
14.6. **Opt-in refusal against an already-coordinated document.** After a real, human-performed "Specify
   Coordinates at a Point" (or Acquire Coordinates from a throwaway link) on a scratch document, run with the
   opt-in on; confirm Preflight refuses (exact dialog text — the corrected wording, naming a non-zero shared
   project position or angle, a moved survey point, or more than one project location, never a clipped survey
   point), `Result.Cancelled`, and the document's shared coordinates are provably unchanged afterward —
   including `ProjectPosition.Angle`. This step empirically confirms or falsifies the corrected detection proxy
   — record the exact before/after values regardless of outcome. Also run the opt-in a second time against the
   Step 14.5 document (SolidGround's own prior write, not a manual UI action) to confirm the second run
   correctly refuses because of the non-zero `ProjectPosition` `SharedCoordinatesWriter.Write` left behind, not
   because of the survey point's own clipped state, which `Write` no longer touches.
14.7. **Private real-property confirmation.** One full run (opt-in off) against a real property the operator
   has legitimate access to, performed entirely privately; only "testing was done," never a location,
   coordinate, or screenshot, may be recorded in this repository.

Record every dialog verbatim, matching the dialog-verbatim discipline `revit-toposolid-creation.md`'s own
Steps 5/7/8 establish.

## Manual evidence (Revit 2027, 2026-09-26/27)

Executed against the installed Revit 2027 application (build `27.0.10.13`), the same reference installation
this note's API-surface table above was verified against. Two builds were tested: the pre-fix Stage 2 build
(commit `842cadc`, deployed build id `20260926-203758-fbd1280f`, `SolidGround.Revit.dll` SHA-256
`FBD1280FC4E938E0B3C885716BD9065DC989219119B3D41E20EE97025CD55DF1`, `SolidGround.Core.dll` SHA-256
`6E754B81317745A4043956D3680BC23294614F38A019E23F897E88DEB56C860B`, signed Valid/timestamped, `deploy -Verify`
OK on 6 files) and the fixed Stage 3 build (commit `2ffc101`, CI run `36291195288` green, deployed build id
`20260926-222218-d44e7bcc`, `SolidGround.Revit.dll` SHA-256
`D44E7BCCEAFF72A002B2C126E2A7C35FAD6237D488927B36BE1ECD7FABAC9CEB`, `SolidGround.Core.dll` SHA-256
`D091922335D24E8D0B0A65DF62DD394E0238206775E822F7E29F96A9EC08A729`, signed Valid, `deploy -Verify` OK). The
default template `Default_I_ENU.rte` (SHA-256 `1e7520d6...d000c4cd`) was confirmed unchanged after every close
during the session.

**Method.** Automation acted on Revit's own window and control handles directly (a handle-only method), never
simulated keystrokes into a menu. A throwaway probe add-in (`SolidGroundStep14Probe`, build `d27b5870`, kept
entirely outside this repository) exposed its own dedicated ribbon tab ("SG Step14") for helper commands —
state dumps, Undo/Redo triggers, and a scripted "Specify Coordinates at a Point" stand-in — because Revit's
built-in External Tools pulldown is not UI-Automation reachable. No computer-use window or other Revit version
was active on the desktop during the session. An Autodesk licensing notice had to be dismissed at each Revit
launch during the session; it is unrelated to SolidGround or to signing.

**Step order.** 14.3 (Undo/Redo) was run before 14.2 (Save/Reopen), reversing the plan's own listed order,
because closing the document clears the Undo stack — running Save/Reopen first would have made 14.3
unobservable. The probe's own "Reopen Document" helper could not substitute for a real close/reopen at 14.2
either: Revit's API reports "The active document may not be closed from the API." Reopening was instead done
by closing Revit gracefully and launching a fresh process against the saved `.rvt` file.

**14.1 (baseline creation, opt-in off) — PASS, both branches.** **1a (parcel AOI):** both a `Toposolid`
(element id `317345`) and a `PropertyLine` (element id `317352`) were created; the success dialog showed the
element-id form of the property-line statement, and the placement record's `propertyLine.created` was `true`
with a matching `elementId`/`areaInternal` (area `17222.252111310587` sq ft internal, consistent with the
synthetic parcel's ~1,600 m², `IsClosedLoop=True`).
Both Revit-native-tolerance log lines appeared: Stage 1 (Document Preflight) logged
`Application.ShortCurveTolerance=0.0025602645572916664` and `Application.VertexTolerance=0.0005233832795` (raw
internal feet); Stage 3 (Geometry Preflight) separately logged the run's own converted-to-`usSurveyFoot`
cleanup tolerances (`vertex=0.000523382232733441`, `minimumEdgeLength=0.005120518873525104`). **1b
(bounding-box and radius AOI):** a `Toposolid` was created for each (element ids `317371` and `317379`) with
no `PropertyLine`, no Preflight/transaction problem of any kind, the success dialog's "not created" sentence
verbatim, and `propertyLine: {"created": false}` with no `elementId`/`areaInternal`; a state dump after all
three 14.1 runs read `Toposolids=3, PropertyLines=1`.

**14.3 (Undo/Redo, run before 14.2 — see "Step order" above) — PASS.** Two Undo operations removed the two 1b
`Toposolid`s one at a time (down to `Toposolids=1, PropertyLines=1` — 1a's own pair); a third Undo then removed
the 1a `Toposolid` and `PropertyLine` together as a single Undo entry (down to `0`/`0` overall), confirming
they share one Undo entry; Redo restored the 1a pair together, reproducing the same `PropertyLine` element id
(`317352`) and area. A read-only probe state dump does not itself clear the Redo stack.

**14.2 (Save/Reopen) — PASS.** `SaveAs` (by API) left `IsModified=False`; after a graceful close and a fresh
Revit process opened against the saved file (see "Step order" above for why this substituted for an in-process
reopen), the `PropertyLine` (`317352`) and its area were unchanged and exactly one `Toposolid` remained.

**14.4 (rejected boundary, parcel AOI only) — PASS, as predicted.** The engineered self-intersecting
multi-polygon parcel fixture was rejected by Core's own parcel-validity check before any transaction opened
("SolidGround could not acquire terrain data."); element counts were unchanged (`1`/`1`). A direct probe call
confirmed `PropertyLine.IsValidBoundary` returns `False` for the overlapping-squares fixture and `True` for a
disjoint control fixture.

**14.5 (opt-in write against a never-coordinated document) — FAIL on the pre-fix build, PASS after the fix.**
Against the pre-fix build (`842cadc`), a brand-new document opened from `Default_I_ENU.rte` read
`ActiveProjectLocation.GetProjectPosition(XYZ.Zero)` as all zero, survey point `Position=(0, 0, 0)`,
`ProjectLocations.Size=1`, and survey point `Clipped=True`; the pre-fix proxy's `!surveyPoint.Clipped` term
made `LooksAlreadyCoordinated` return `True` for this genuinely uncoordinated document, and Preflight refused
the opt-in on exactly the case it exists to allow (the finding "Purpose and status" and "Known limitations"
above already record). Fixed in commit `2ffc101`. Re-run against the fixed build on an equivalent fresh
document: PASS. Preflight logged the identical before-state (`Clipped` no longer read) with
`LooksAlreadyCoordinated=False`; the run proceeded, and the write logged "SolidGround wrote shared coordinates
this run: EastWest=1475308.3989501311, NorthSouth=15106833.98950131, Elevation=0 (decimal feet)" — the example
site's own local origin converted to decimal feet (`449674` m / `0.3048`, `4604563` m / `0.3048` exactly).
`ProjectPosition.Angle` round-tripped as exactly `0`. The placement record recorded `sharedCoordinatesWrite`
`{"attempted":true,"eastWest":449674,"northSouth":4604563,"elevation":0,"angleInternal":0,"horizontalUnit":
"meter","verticalUnit":"meter","verified":true}` — the example-site written values, verified.
`SharedCoordinatesWriter.VerifyWritten`'s read-back matched on the first attempt.

The same re-run surfaced the second finding this closeout fixes: the survey point's own internal `Position`
read `(0, 0, 0)` before the write and `(-1475308.3989501311, -15106833.98950131, 0)` afterward — still
`Clipped=True` throughout — confirming `ProjectLocation.SetProjectPosition` moves the survey point as an
intrinsic side effect. The success dialog shown during this run accordingly read the pre-fix sentence verbatim:
"SolidGround wrote this run's terrain origin as this model's shared coordinates (sharedCoordinates.writeIfAbsent);
the project base point, survey point, and site location were otherwise left unchanged." — false, given the
survey-point move just observed. "Post-commit reporting" above records the corrected sentence this closeout
ships instead.

**14.6 (opt-in refusal against an already-coordinated document) — PASS, both sub-tests.** **Sub-test A (fresh
document, coordinated by an API test helper):** the probe's "Set Coordinated State" helper wrote
`ProjectPosition(100, 200, 5, 0)` and moved the survey point to `(-100, -200, -5)` (`ProjectLocations.Size` `1`
→ `2`); the opted-in run then correctly refused at Preflight (`LooksAlreadyCoordinated=True`), created nothing,
and left `ProjectPosition`, the survey point, and `ProjectLocations.Size=2` unchanged. **Sub-test B (the Step
14.5 document, SolidGround's own prior write, not a manual UI action):** the opted-in run again correctly
refused ("SolidGround Preflight found a problem." … "a non-zero shared project position or angle, a moved
survey point, or more than one project location"), with `LooksAlreadyCoordinated=True` logged from the
non-zero position left by 14.5's own write, and the document's shared coordinates unchanged (same
`EastWest`/`NorthSouth`, `Angle=0`, still one `Toposolid`). Together these empirically confirm the corrected
detection proxy for both a manually-coordinated document and SolidGround's own prior write.

**14.7 (private real-property confirmation).** A private end-to-end run against a real property (live
OpenTopography fetch, parcel boundary from a machine-local county registry, opt-in off) passed on both the
pre-fix and fixed builds. No location, county, parcel id, area, coordinates, file path, or screenshot is
recorded here or elsewhere in this repository.

**Session hygiene.** Every close answered "Save changes to `Default_I_ENU.rte`?" with No; the template hash
was unchanged throughout (see above). The probe add-in was uninstalled afterward (its manifest and versioned
folder removed). The machine's settings file (temporarily altered for the session) was restored to its
original content. A final `deploy -Verify` passed. The session's own log was checked for secret leakage
afterward: the API key value itself appeared zero times; its two logged markers were confirmed redacted.

### Decisions recorded from evidence (2026-09-26/27)

- **AC4 (the shared-coordinates detection proxy) is settled.** Step 14.5's re-run and Step 14.6's two
  sub-tests together confirm the corrected, `Clipped`-free proxy: it no longer misdetects a brand-new
  default-template document as already coordinated (14.5), and it still correctly refuses on both a
  manually-coordinated document and SolidGround's own prior write (14.6). Owner decision 1's condition above is
  met.
- **The `sharedCoordinatesStatement` wording defect Step 14.5 surfaced is fixed in this closeout**, not left as
  a known limitation: the opted-in sentence no longer denies the survey-point move Step 14.5's own session
  recorded; see "Post-commit reporting" above for the corrected sentence and
  `SharedCoordinatesStatementAccuratelyDescribesTheSurveyPointMove` (in "Tests" above) for its regression guard.
- **Step 14 is complete; no further manual evidence is pending for Issue #30 (PH3-3).**

## Known limitations

- **Live finding (2026-09-27, Revit 2027 build `27.0.10.13`, manual evidence Step 14.5): the original detection
  proxy misread Revit's own default template as already coordinated.** A brand-new document opened from
  `Default_I_ENU.rte` reported `ActiveProjectLocation.GetProjectPosition(XYZ.Zero)` as `EastWest=0,
  NorthSouth=0, Elevation=0, Angle=0`, the survey point's own `Position=(0,0,0)`, `ProjectLocations.Size=1`, and
  the survey point's own `Clipped=True`. The proxy's `!surveyPoint.Clipped` term made `LooksAlreadyCoordinated`
  return `true` for this genuinely never-touched document, so Preflight refused the opt-in on exactly the case
  it exists to allow. **The default template ships the survey point clipped, so a clipped survey point is not
  evidence of shared coordinates.** Fixed the same day: the decision moved into the Revit-free
  `SolidGround.Core.Transformations.SharedCoordinateDetection.LooksAlreadyCoordinated`, which never takes a
  "clipped" flag of any kind, and `SharedCoordinatesWriter.Write` no longer sets one either — see
  "Shared-coordinates detection" and "Why `Write` no longer sets `Clipped`" above for the corrected design.
- **Live finding (2026-09-26/27, Revit 2027 build `27.0.10.13`, manual evidence Step 14.5 re-run): the opted-in
  write's own success-dialog/placement-record sentence inaccurately claimed the survey point was left
  unchanged.** The same re-run that confirmed the fix above also showed the survey point's own internal
  `Position` moves from `(0, 0, 0)` to the negative of the newly written east-west/north-south as an intrinsic
  side effect of `ProjectLocation.SetProjectPosition` — yet the sentence shown that run still read "...the
  project base point, survey point, and site location were otherwise left unchanged," which is false. Fixed in
  this closeout: the sentence now states the move plainly instead of denying it — see "Post-commit reporting"
  above for the corrected wording and "Manual evidence (Revit 2027, 2026-09-26/27)" above for the finding.
- **The shared-coordinates detection proxy remains a heuristic, not a certified fact** (owner decision 1,
  narrowed by the 2026-09-27 fix above): a user who manually runs "Specify Coordinates at Point" (moving the
  project position, the survey point, or both) without ever truly acquiring/publishing coordinates from a real
  survey is indistinguishable from a genuinely coordinated document; the theoretical acquire-at-exactly-the-
  origin-and-zero-rotation case would still produce a false negative, though it now requires every one of the
  corrected proxy's four signals to simultaneously read as "uncoordinated," rather than depending on one single
  flag the way the pre-2026-09-27 proxy did. Accepted subject to Step 14.6's live confirmation, which passed
  2026-09-26/27 against both a manually-coordinated document and SolidGround's own prior write — see "Manual
  evidence (Revit 2027, 2026-09-26/27)" above.
- **`ProjectPosition.Angle` is always `0`; grid-convergence correction is not computed.** See "Zero rotation,
  stated plainly" above; a model's true-north rotation carries whatever error the site's distance from its UTM
  zone's central meridian implies.
- **Cross-polygon self-intersection for a multi-polygon parcel** relies entirely on `PropertyLine
  .IsValidBoundary`'s own documented, Revit-verified check; no known practical case in this repository's
  current fixtures triggers it, but it is not independently re-verified in Core.
- **This closes the pre-existing "`Line.CreateBound`'s short-curve tolerance is not independently validated"**
  follow-up `docs/architecture/revit-toposolid-creation.md`'s "Known limitations" section previously recorded:
  `LocalBoundaryCleaner.Clean`'s `minimumEdgeLength` reconciliation (`Math.Max(vertexTolerance,
  minimumEdgeLength)`) now dedupes at least as aggressively as `LocalBoundaryValidator.Validate`'s own
  equivalent check rejects, so an edge shorter than `Application.ShortCurveTolerance` but not exactly zero is
  now caught, and repaired when possible, before `BoundaryGeometryBuilder.BuildProfiles` is ever reached.

## What this note does not do

Does not begin PH3-4 (#31, the WPF dialog) or Issue #33 (`TerrainProvenance` schema v3, address/parcel
provenance) — both stay separate, already-scoped issues. Off (`sharedCoordinates.writeIfAbsent: false`, the
shipped default), behavior is unchanged from Issue #15/#16/#19/#17 except that `LocalBoundaryCleaner.Clean` now
runs unconditionally ahead of `LocalBoundaryValidator.Validate` (repairing, never rejecting, messy-but-valid
input) and a `PropertyLine` now also appears for a parcel AOI.
