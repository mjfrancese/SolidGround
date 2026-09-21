using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="LocalBoundaryValidator.Validate"/>'s retained-sample checks: duplicate (X, Y)
/// positions, containment tolerance (aggregated into one summarized problem line), and the point budget.
/// Every test here uses an already-well-formed boundary, so only the retained-sample checks can produce a
/// problem -- <see cref="LocalBoundaryTests"/> covers the boundary's own ring-shape checks.
/// </summary>
public sealed class LocalBoundaryValidatorTests
{
    [Fact]
    public void ValidateReportsNoProblemsForAWellFormedTriangleAndInteriorPoints()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(2, 2, 10), Sample(3, 1, 12)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void ValidateReportsAProblemForDuplicateXyPoints()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(2, 2, 10), Sample(2, 2, 15)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("(X, Y) position", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateReportsAProblemForAPointOutsideTheBoundary()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(2, 2, 10), Sample(100, 100, 10)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("outside the boundary", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAggregatesMultipleOutsidePointsIntoOneProblemLineWithACountAndWorstOffset()
    {
        LocalBoundary boundary = SquareBoundary();
        // Directly west of the square's west edge (x = 0), at y = 5: the nearest boundary point for each is
        // (0, 5), so each point's offset is exactly the absolute value of its own x.
        LocalTerrainSample[] samples = [Sample(-1, 5, 0), Sample(-2, 5, 0), Sample(-5, 5, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 100);

        Assert.False(result.IsValid);
        string[] outsideProblems = [.. result.Problems.Where(p => p.Contains("outside the boundary", StringComparison.Ordinal))];
        string problem = Assert.Single(outsideProblems);
        Assert.Contains("3", problem, StringComparison.Ordinal);
        Assert.Contains("5", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateReportsAProblemWhenRetainedCountExceedsBudget()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(1, 1, 0), Sample(2, 1, 0), Sample(1, 2, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 2);

        Assert.False(result.IsValid);
        Assert.Contains(result.Problems, p => p.Contains("point budget", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidateAcceptsRetainedCountExactlyAtBudget()
    {
        LocalBoundary boundary = TriangleBoundary();
        LocalTerrainSample[] samples = [Sample(1, 1, 0), Sample(2, 1, 0), Sample(1, 2, 0)];

        LocalBoundaryValidationResult result = LocalBoundaryValidator.Validate(boundary, samples, pointBudget: 3);

        Assert.True(result.IsValid);
        Assert.Empty(result.Problems);
    }

    private static LocalTerrainSample Sample(double x, double y, double elevation) => new(new LocalCoordinate(x, y, elevation));

    private static LocalBoundary TriangleBoundary()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundary SquareBoundary()
    {
        LocalBoundaryRing shell = Ring((0, 0), (10, 0), (10, 10), (0, 10));
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    private static LocalBoundaryRing Ring(params (double X, double Y)[] vertices) =>
        new([.. vertices.Select(v => new LocalCoordinate2D(v.X, v.Y))]);
}
