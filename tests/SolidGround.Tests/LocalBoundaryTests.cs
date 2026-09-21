using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="LocalBoundaryValidator.Validate"/>'s ring/polygon-shape checks: vertex distinctness,
/// zero-length (including redundant-closing-vertex) edges, self-intersection, non-positive area, and holes.
/// Every test here uses an empty retained-sample list and a generous budget, so only the boundary's own shape
/// can produce a problem -- <see cref="LocalBoundaryValidatorTests"/> covers the retained-sample/budget checks
/// against an already-valid boundary. See SolidGround Issue #15's design record, orchestrator decision (a):
/// a <see cref="LocalBoundaryRing"/> never stores a repeated closing vertex, so "closed" here means the
/// implicit wrap-around edge from the last vertex back to the first is well-formed, not that one was stored.
/// </summary>
public sealed class LocalBoundaryTests
{
    [Fact]
    public void RejectsRingWithFewerThanThreeDistinctVertices()
    {
        // Three stored vertices, but only two distinct positions: (0, 0) repeats.
        LocalBoundaryRing shell = Ring((0, 0), (0, 0), (1, 1));

        LocalBoundaryValidationResult result = ValidateShellOnly(shell);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("fewer than three distinct vertices", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsUnclosedRing()
    {
        // A LocalBoundaryRing never stores a closing vertex (orchestrator decision (a)); one constructed with
        // a redundant explicit closing vertex equal to its first is itself invalid input. The wrap-around
        // check (comparing the last stored vertex back to the first) catches this as a zero-length edge.
        LocalBoundaryRing shell = Ring((0, 0), (4, 0), (4, 4), (0, 0));

        LocalBoundaryValidationResult result = ValidateShellOnly(shell);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("zero-length edge", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsZeroLengthConsecutiveEdge()
    {
        // An interior duplicate: vertices 1 and 2 are both (4, 0).
        LocalBoundaryRing shell = Ring((0, 0), (4, 0), (4, 0), (4, 4));

        LocalBoundaryValidationResult result = ValidateShellOnly(shell);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("zero-length edge", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsSelfIntersectingRing()
    {
        // A "bowtie" quadrilateral: edge (0,0)-(4,4) crosses edge (4,0)-(0,4) at (2, 2).
        LocalBoundaryRing shell = Ring((0, 0), (4, 4), (4, 0), (0, 4));

        LocalBoundaryValidationResult result = ValidateShellOnly(shell);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void RejectsNonPositiveArea()
    {
        // Three distinct but collinear points: the "triangle" has zero area. A degenerate ring like this is
        // also non-simple (its closing edge fully retraces the first two), so NetTopologySuite's own
        // topology-validity check is what actually reports it -- the boundary is rejected either way.
        LocalBoundaryRing shell = Ring((0, 0), (2, 0), (4, 0));

        LocalBoundaryValidationResult result = ValidateShellOnly(shell);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void AcceptsValidShellWithOneHole()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0, 10));
        LocalBoundaryRing hole = Ring((2, 2), (2, 3), (3, 3), (3, 2));
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [hole])]);

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, [], pointBudget: 1000);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void AcceptsMultiplePolygons()
    {
        LocalBoundaryRing first = Ring((0, 0), (10, 0), (10, 10), (0, 10));
        LocalBoundaryRing second = Ring((100, 100), (110, 100), (110, 110), (100, 110));
        LocalBoundary boundary = new(
        [
            new LocalBoundaryPolygon(first, []),
            new LocalBoundaryPolygon(second, []),
        ]);

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, [], pointBudget: 1000);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    private static LocalBoundaryValidationResult ValidateShellOnly(LocalBoundaryRing shell)
    {
        LocalBoundary boundary = new([new LocalBoundaryPolygon(shell, [])]);
        return LocalBoundaryValidator.Validate(boundary, [], pointBudget: 1000);
    }

    private static LocalBoundaryRing Ring(params (double X, double Y)[] vertices) =>
        new([.. vertices.Select(v => new LocalCoordinate2D(v.X, v.Y))]);
}
