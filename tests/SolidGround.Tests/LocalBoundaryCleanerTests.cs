using System.Reflection;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="LocalBoundaryCleaner.Clean"/>: dedupe of exact/near-duplicate cyclically consecutive
/// vertices (including the implicit wrap-around closing edge), collapse of collinear and near-collinear
/// mid-edge vertices, the multi-pass fixed-point loop, per-ring/per-polygon independence, and its integration
/// with the unchanged <see cref="LocalBoundaryValidator.Validate"/>. See
/// <c>docs/architecture/revit-property-line-and-shared-coordinates.md</c>'s "Geometry cleanup contract"
/// section for the full contract every test here exercises. Every tolerance below is a dimensionless test
/// value (as if already expressed in whatever unit the coordinates use), matching
/// <see cref="LocalBoundaryValidatorTests"/>'s own established convention.
/// </summary>
public sealed class LocalBoundaryCleanerTests
{
    private const double VertexTolerance = 0.01;
    private const double CollinearityTolerance = 0.01;

    [Fact]
    public void CleanCollapsesAnExactDuplicateInteriorVertex()
    {
        // …A,B,B,C… -> …A,B,C… : (10,0) is repeated verbatim before the ring continues to (10,10).
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanCollapsesANearDuplicateInteriorVertexWithinTolerance()
    {
        // …A,B,B+ε,C… with ε (0.001) strictly less than VertexTolerance (0.01).
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10.001, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanCollapsesASubToleranceMicroSegment()
    {
        // A short (~0.0036), non-obviously-duplicate-looking segment between two interior points forming a
        // small bump off the bottom edge: (5,3) and (5.003,3.002) are two genuinely distinct-looking
        // coordinates, not an obvious repeated point, but their edge is still well under VertexTolerance.
        // The bump's apex (5,3) itself survives: its deviation from the (0,0)-(10,0) line is 3, far beyond
        // CollinearityTolerance, so this is dedupe alone at work, not the collinear pass.
        LocalBoundaryRing shell = Ring((0, 0), (5, 3), (5.003, 3.002), (10, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [
                new LocalCoordinate2D(0, 0), new LocalCoordinate2D(5, 3), new LocalCoordinate2D(10, 0),
                new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10),
            ],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanRemovesCollinearMidEdgeVerticesOnAStraightLotLine()
    {
        // (3,0) and (6,0) sit exactly on the straight line from (0,0) to (10,0); corners and area must survive
        // unchanged once both mid-edge vertices are gone.
        LocalBoundaryRing shell = Ring((0, 0), (3, 0), (6, 0), (10, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanRemovesNearCollinearVerticesWithinThePerpendicularTolerance()
    {
        // (3,0) and (7,0.005) are each within CollinearityTolerance of the line through their own immediate
        // neighbors -- not exactly on it, unlike the previous test -- and both still collapse.
        LocalBoundaryRing shell = Ring((0, 0), (3, 0), (7, 0.005), (10, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanPreservesAGenuineSlightBendThatExceedsTheCollinearityTolerance()
    {
        // Negative case: (5,0.5) deviates by 0.5 from the (0,0)-(10,0) line, far beyond CollinearityTolerance
        // (0.01) -- a real, if slight, bend that must never be simplified away.
        LocalBoundaryRing shell = Ring((0, 0), (5, 0.5), (10, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [
                new LocalCoordinate2D(0, 0), new LocalCoordinate2D(5, 0.5), new LocalCoordinate2D(10, 0),
                new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10),
            ],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanCollapsesANearDuplicateAcrossTheWrapAroundEdge()
    {
        // The fourth stored vertex (0.003,0.004) is a near-duplicate of the first (0,0) -- the equivalent, for
        // a ring that never stores an explicit closing vertex, of a duplicated OGC-style closing coordinate.
        // The implicit wrap-around edge (last vertex back to first) must collapse it exactly like any other
        // cyclically consecutive pair, leaving a clean triangle.
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0.003, 0.004));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10)],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanHandlesShellAndHoleIndependently()
    {
        // The shell carries a collinear mid-edge vertex; the hole carries its own, unrelated near-duplicate
        // vertex. Each ring is cleaned on its own terms, and the hole still falls entirely inside the cleaned
        // shell afterward.
        LocalBoundaryRing shell = Ring((0, 0), (5, 0), (10, 0), (10, 10), (0, 10));
        LocalBoundaryRing hole = Ring((3, 3), (3.001, 3.001), (6, 3), (6, 6), (3, 6));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [hole])]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        LocalBoundaryPolygon polygon = Assert.Single(cleaned.Polygons);
        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            polygon.Shell.Vertices);
        LocalBoundaryRing cleanedHole = Assert.Single(polygon.Holes);
        Assert.Equal(
            [new LocalCoordinate2D(3, 3), new LocalCoordinate2D(6, 3), new LocalCoordinate2D(6, 6), new LocalCoordinate2D(3, 6)],
            cleanedHole.Vertices);

        // Hole-inside-shell containment still holds: three interior samples, clear of the hole, validate clean.
        LocalTerrainSample[] samples = [Sample(1, 1), Sample(8, 8), Sample(1, 8)];
        Assert.True(LocalBoundaryValidator.Validate(cleaned, samples, pointBudget: 1000).IsValid);
    }

    [Fact]
    public void CleanOnlyChangesTheMessyPolygonInAMultipolygon()
    {
        // Two disjoint shells: the first is already clean and must survive vertex-for-vertex unchanged; only
        // the second carries a collinear mid-edge vertex.
        LocalBoundaryRing cleanShell = Ring((0, 0), (4, 0), (4, 4), (0, 4));
        LocalBoundaryRing messyShell = Ring((100, 100), (103, 100), (106, 100), (106, 106), (100, 106));
        LocalBoundary boundary = new(
        [
            new LocalBoundaryPolygon(cleanShell, []),
            new LocalBoundaryPolygon(messyShell, []),
        ]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        Assert.Equal(2, cleaned.Polygons.Count);
        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(4, 0), new LocalCoordinate2D(4, 4), new LocalCoordinate2D(0, 4)],
            cleaned.Polygons[0].Shell.Vertices);
        Assert.Equal(
            [
                new LocalCoordinate2D(100, 100), new LocalCoordinate2D(106, 100),
                new LocalCoordinate2D(106, 106), new LocalCoordinate2D(100, 106),
            ],
            cleaned.Polygons[1].Shell.Vertices);
    }

    [Fact]
    public void CleanedRingBelowThreeVerticesStillFailsValidation()
    {
        // Two near-duplicate pairs collapse the ring straight down to two vertices -- a degenerate sliver.
        // Clean never throws for this; LocalBoundaryValidator.Validate (entirely unchanged) still rejects it
        // downstream, with its existing message.
        LocalBoundaryRing shell = Ring((0, 0), (0.001, 0), (10, 0), (10.001, 0));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        LocalBoundaryRing cleanedShell = Assert.Single(cleaned.Polygons).Shell;
        Assert.Equal(2, cleanedShell.Vertices.Count);

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(cleaned, [], pointBudget: 1000);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("fewer than three distinct vertices", StringComparison.Ordinal));
    }

    [Fact]
    public void CleanCollinearCollapseAloneCanReduceARingBelowThreeVertices()
    {
        // Three genuinely distinct points, all exactly on one line, with no near-duplicate pair for
        // DedupeOnePass to act on first -- every pairwise/wraparound distance (5, 5, 10) is far beyond
        // VertexTolerance, so DedupeOnePass leaves all three vertices untouched and CollapseCollinearOnePass
        // alone is responsible for the reduction. This isolates a distinct code path from
        // CleanedRingBelowThreeVerticesStillFailsValidation above, whose below-three result instead comes from
        // the dedupe pass (CleanRing's first below-three check) before the collinear-collapse pass ever runs.
        // Here, every vertex's own two cyclic neighbors are also on the same line, so every vertex's
        // perpendicular deviation is exactly zero and all three collapse in the same pass -- all the way to
        // zero vertices, not merely fewer than three. Clean never throws for this; LocalBoundaryValidator
        // .Validate (unchanged) still rejects the empty result with its existing message.
        LocalBoundaryRing shell = Ring((0, 0), (5, 0), (10, 0));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        LocalBoundaryRing cleanedShell = Assert.Single(cleaned.Polygons).Shell;
        Assert.Empty(cleanedShell.Vertices);

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(cleaned, [], pointBudget: 1000);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("fewer than three distinct vertices", StringComparison.Ordinal));
    }

    [Fact]
    public void CleanNeverMasksATrueSelfIntersection()
    {
        // A "bowtie" quadrilateral: none of its vertices are near-duplicate or collinear with their own
        // neighbors, so Clean leaves it byte-for-byte unchanged. Validate's existing self-intersection
        // rejection must still fire exactly as it does for the untouched shape (LocalBoundaryTests.RejectsSelfIntersectingRing).
        LocalBoundaryRing shell = Ring((0, 0), (4, 4), (4, 0), (0, 4));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        Assert.Equal(shell.Vertices, Assert.Single(cleaned.Polygons).Shell.Vertices);
        Assert.False(LocalBoundaryValidator.Validate(cleaned, [], pointBudget: 1000).IsValid);
    }

    [Fact]
    public void CleanIsIdempotentOnAlreadyCleanInput()
    {
        // Zero behavior change for the already-shipped Issue #15 flow: a plain, already-clean rectangle with
        // no near-duplicate or collinear vertices must survive Clean byte-for-byte.
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(shell.Vertices, cleaned.Vertices);
    }

    [Fact]
    public void CleanedBoundaryPassesValidation()
    {
        // The intentionally messy, synthetic fixture this stage's acceptance criterion (AC2) requires:
        // an exact duplicate, two collinear/near-collinear mid-edge vertices, a near-duplicate corner, and a
        // near-duplicate across the wrap-around edge, all combined into one ring that must still converge to
        // the same clean rectangle every other test in this file builds directly, and must then pass
        // LocalBoundaryValidator.Validate (unchanged). This test assembly deliberately does not call
        // SolidGround.Revit's BoundaryGeometryBuilder.BuildProfiles, which
        // ArchitectureTests.TestAssemblyReferencesNeitherTheRevitApiNorTheRevitHostAssembly forbids from a
        // Revit-API-free test assembly.
        LocalBoundaryRing messyShell = Ring(
            (0, 0),             // corner 1
            (0, 0),             // exact duplicate of corner 1
            (3, 0),             // exactly collinear mid-edge vertex
            (6.001, 0.0005),    // near-collinear mid-edge vertex
            (10, 0),            // corner 2
            (10, 0.002),        // near-duplicate of corner 2
            (10, 10),           // corner 3
            (0, 10),            // corner 4
            (0.001, 0.0015));   // near-duplicate of corner 1, across the wrap-around edge
        LocalBoundary boundary = new([new LocalBoundaryPolygon(messyShell, [])]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        LocalBoundaryRing cleanedShell = Assert.Single(cleaned.Polygons).Shell;
        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            cleanedShell.Vertices);

        LocalTerrainSample[] samples = [Sample(2, 2), Sample(8, 8), Sample(2, 8)];
        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(cleaned, samples, pointBudget: 1000);
        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void CleanConvergesWhenACollinearCollapseExposesANewNearDuplicatePair()
    {
        // Engineered so pass 1's collinear-collapse removes (20,0) -- collinear with its own neighbors
        // (10,0) and (10.005,0) -- which makes those two vertices cyclically adjacent for the first time.
        // Only pass 2's dedupe then collapses that newly exposed near-duplicate pair (they were never
        // adjacent in the original ring, so pass 1's own dedupe sub-pass could not have touched them). None of
        // this file's other cases exercises more than one effective outer iteration, leaving the
        // maxIterations fixed-point loop itself unproven without this case.
        LocalBoundaryRing shell = Ring((-5, 5), (10, 0), (20, 0), (10.005, 0), (20, 10), (-5, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [
                new LocalCoordinate2D(-5, 5), new LocalCoordinate2D(10, 0),
                new LocalCoordinate2D(20, 10), new LocalCoordinate2D(-5, 10),
            ],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanCollapsesAnEdgeStrictlyBetweenVertexToleranceAndMinimumEdgeLength()
    {
        // The edge from (10,0) to (10.03,0) has length 0.03: strictly greater than VertexTolerance (0.01) but
        // strictly less than a larger minimumEdgeLength (0.05). Clean's own dedupe pass must still collapse
        // it, via Math.Max(vertexTolerance, minimumEdgeLength), rather than leaving it for
        // LocalBoundaryValidator.Validate's own equivalent check to reject
        // (see ValidateRejectsAnEdgeShorterThanTheSuppliedMinimumEdgeLength in LocalBoundaryValidatorTests).
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10.03, 0), (10, 10), (0, 10));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);

        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0.05);

        Assert.Equal(
            [new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10)],
            Assert.Single(cleaned.Polygons).Shell.Vertices);
    }

    [Fact]
    public void CleanPreservesASharpSpikeProtrusion()
    {
        // An extreme, needle-like spike: (5,50)'s cyclic neighbors in this ring are (10,0) and (10,10) -- the
        // vertical line x=10 -- and it deviates by 5 from that line, far beyond CollinearityTolerance (0.01),
        // however visually spiky the resulting corner is. Never silently reshaped.
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (5, 50), (10, 10), (0, 10));

        LocalBoundaryRing cleaned = CleanShell(shell);

        Assert.Equal(
            [
                new LocalCoordinate2D(0, 0), new LocalCoordinate2D(10, 0), new LocalCoordinate2D(5, 50),
                new LocalCoordinate2D(10, 10), new LocalCoordinate2D(0, 10),
            ],
            cleaned.Vertices);
    }

    [Fact]
    public void CleanIsDeterministicAcrossRepeatedRuns()
    {
        // The same messy input, cleaned twice independently, must produce byte-identical results both times --
        // no hidden dependency on hashing/iteration order or any other source of nondeterminism.
        LocalBoundaryRing shell = Ring(
            (0, 0), (0, 0), (3, 0), (6.001, 0.0005), (10, 0), (10, 0.002), (10, 10), (0, 10), (0.001, 0.0015));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);

        LocalBoundary first = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);
        LocalBoundary second = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);

        Assert.Equal(Assert.Single(first.Polygons).Shell.Vertices, Assert.Single(second.Polygons).Shell.Vertices);
    }

    [Fact]
    public void CleanThrowsForANullBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => LocalBoundaryCleaner.Clean(null!, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d));
    }

    [Fact]
    public void CoreExposesAPublicLocalBoundaryCleaner()
    {
        // SolidGround Issue #30 (PH3-3), stage 1: geometry cleanup runs entirely in Core, before any Revit API
        // call, exactly like LocalBoundaryValidator's own Geometry Preflight role (ArchitectureTests'
        // CoreExposesAPublicLocalBoundaryValidator mirrors this same pattern). Placed here rather than in
        // ArchitectureTests.cs per this stage's own scope: existing architecture-guard test files are
        // additive-only.
        Type? cleaner = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Exports.LocalBoundaryCleaner");

        Assert.NotNull(cleaner);
        Assert.True(cleaner.IsPublic);
        Assert.NotNull(cleaner.GetMethod("Clean", BindingFlags.Public | BindingFlags.Static));
    }

    private static LocalBoundaryRing CleanShell(LocalBoundaryRing shell)
    {
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);
        LocalBoundary cleaned = LocalBoundaryCleaner.Clean(boundary, VertexTolerance, CollinearityTolerance, minimumEdgeLength: 0d);
        return Assert.Single(cleaned.Polygons).Shell;
    }

    private static LocalTerrainSample Sample(double x, double y) => new(new LocalCoordinate(x, y, 0));

    private static LocalBoundaryRing Ring(params (double X, double Y)[] vertices) =>
        new([.. vertices.Select(v => new LocalCoordinate2D(v.X, v.Y))]);
}
