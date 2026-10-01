using SolidGround.Core.Geometry;

namespace SolidGround.Tests;

public sealed class TerrainVertexMatcherTests
{
    [Fact]
    public void RejectsAnAggregatePassingWrongVertexAndReportsTheExpectedIndex()
    {
        IReadOnlyList<Coordinate3D> expected =
        [
            new(0d, 0d, 0d),
            new(10d, 0d, 0d),
            new(0d, 10d, 0d),
        ];
        // These vertices have the same count and bounding box as expected, but the last vertex is at
        // the wrong elevation. A bounding-box-only verification would accept it.
        IReadOnlyList<Coordinate3D> actual =
        [
            new(0d, 0d, 0d),
            new(10d, 0d, 0d),
            new(0d, 10d, 7d),
        ];

        TerrainVertexMatchResult result = TerrainVertexMatcher.Match(expected, actual, 0.01d);

        Assert.False(result.Passed);
        TerrainVertexMismatch mismatch = Assert.Single(result.UnmatchedExpected);
        Assert.Equal(2, mismatch.ExpectedIndex);
        Assert.Equal(7d, mismatch.NearestDelta.Elevation, 9);
    }

    [Fact]
    public void UsesInjectiveMatchingWhenTwoExpectedVerticesCompeteForOneActualVertex()
    {
        IReadOnlyList<Coordinate3D> expected = [new(0d, 0d, 0d), new(0.01d, 0d, 0d)];
        IReadOnlyList<Coordinate3D> actual = [new(0d, 0d, 0d), new(4d, 4d, 4d)];

        TerrainVertexMatchResult result = TerrainVertexMatcher.Match(expected, actual, 0.02d);

        Assert.False(result.Passed);
        Assert.Single(result.UnmatchedExpected);
        Assert.Single(result.CandidateCollisions);
    }

    [Fact]
    public void UsesLocalBucketsForASeparatedFifteenThousandVertexTerrain()
    {
        Coordinate3D[] points = Enumerable.Range(0, 15_000).Select(index => new Coordinate3D(index * 2d, 0d, index % 17)).ToArray();

        TerrainVertexMatchResult result = TerrainVertexMatcher.Match(points, points.Reverse().ToArray(), 0.01d);

        Assert.True(result.Passed);
        Assert.True(result.CandidateComparisons < 100_000, $"Expected bounded candidate work, got {result.CandidateComparisons} comparisons.");
    }
}
