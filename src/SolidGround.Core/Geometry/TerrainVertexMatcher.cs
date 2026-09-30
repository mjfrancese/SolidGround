namespace SolidGround.Core.Geometry;

/// <summary>One unmatched expected terrain vertex and its closest observed vertex delta.</summary>
public sealed record TerrainVertexMismatch(int ExpectedIndex, Coordinate3D Expected, Coordinate3D? NearestActual, Coordinate3D NearestDelta);

/// <summary>Reports that more than one expected vertex could only use the same observed vertex.</summary>
public sealed record TerrainVertexCandidateCollision(int ActualIndex, IReadOnlyList<int> ExpectedIndices);

/// <summary>The result of a tolerance-bounded, injective expected-to-actual vertex match.</summary>
public sealed record TerrainVertexMatchResult(
    IReadOnlyList<TerrainVertexMismatch> UnmatchedExpected,
    IReadOnlyList<TerrainVertexCandidateCollision> CandidateCollisions)
{
    public bool Passed => UnmatchedExpected.Count == 0;
}

/// <summary>
/// Matches expected terrain vertices to observed vertices without relying on Revit's returned order. Each
/// observed vertex may satisfy at most one expected vertex; additional observed vertices are permitted for
/// profile-generated geometry.
/// </summary>
public static class TerrainVertexMatcher
{
    public static TerrainVertexMatchResult Match(
        IReadOnlyList<Coordinate3D> expected,
        IReadOnlyList<Coordinate3D> actual,
        double tolerance)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (!double.IsFinite(tolerance) || tolerance < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be finite and non-negative.");
        }

        List<int>[] candidates = new List<int>[expected.Count];
        List<int>[] reverseCandidates = new List<int>[actual.Count];
        for (int actualIndex = 0; actualIndex < actual.Count; actualIndex++)
        {
            reverseCandidates[actualIndex] = [];
        }

        for (int expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            candidates[expectedIndex] = [];
            for (int actualIndex = 0; actualIndex < actual.Count; actualIndex++)
            {
                if (WithinTolerance(expected[expectedIndex], actual[actualIndex], tolerance))
                {
                    candidates[expectedIndex].Add(actualIndex);
                    reverseCandidates[actualIndex].Add(expectedIndex);
                }
            }

            candidates[expectedIndex].Sort((left, right) => SquaredDistance(expected[expectedIndex], actual[left]).CompareTo(SquaredDistance(expected[expectedIndex], actual[right])));
        }

        int[] actualToExpected = Enumerable.Repeat(-1, actual.Count).ToArray();
        int[] expectedToActual = Enumerable.Repeat(-1, expected.Count).ToArray();
        for (int expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            bool[] visitedActual = new bool[actual.Count];
            TryAssign(expectedIndex, candidates, actualToExpected, expectedToActual, visitedActual);
        }

        List<TerrainVertexMismatch> unmatched = [];
        for (int expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            if (expectedToActual[expectedIndex] >= 0)
            {
                continue;
            }

            Coordinate3D? nearest = null;
            Coordinate3D delta = new(0d, 0d, 0d);
            if (actual.Count > 0)
            {
                int nearestIndex = Enumerable.Range(0, actual.Count).MinBy(actualIndex => SquaredDistance(expected[expectedIndex], actual[actualIndex]));
                nearest = actual[nearestIndex];
                delta = Delta(expected[expectedIndex], nearest.Value);
            }

            unmatched.Add(new TerrainVertexMismatch(expectedIndex, expected[expectedIndex], nearest, delta));
        }

        List<TerrainVertexCandidateCollision> collisions = [];
        for (int actualIndex = 0; actualIndex < reverseCandidates.Length; actualIndex++)
        {
            if (reverseCandidates[actualIndex].Count > 1)
            {
                collisions.Add(new TerrainVertexCandidateCollision(actualIndex, reverseCandidates[actualIndex].AsReadOnly()));
            }
        }

        return new TerrainVertexMatchResult(unmatched.AsReadOnly(), collisions.AsReadOnly());
    }

    private static bool TryAssign(int expectedIndex, List<int>[] candidates, int[] actualToExpected, int[] expectedToActual, bool[] visitedActual)
    {
        foreach (int actualIndex in candidates[expectedIndex])
        {
            if (visitedActual[actualIndex])
            {
                continue;
            }

            visitedActual[actualIndex] = true;
            int incumbent = actualToExpected[actualIndex];
            if (incumbent < 0 || TryAssign(incumbent, candidates, actualToExpected, expectedToActual, visitedActual))
            {
                actualToExpected[actualIndex] = expectedIndex;
                expectedToActual[expectedIndex] = actualIndex;
                return true;
            }
        }

        return false;
    }

    private static bool WithinTolerance(Coordinate3D expected, Coordinate3D actual, double tolerance) =>
        Math.Abs(expected.X - actual.X) <= tolerance
        && Math.Abs(expected.Y - actual.Y) <= tolerance
        && Math.Abs(expected.Elevation - actual.Elevation) <= tolerance;

    private static Coordinate3D Delta(Coordinate3D expected, Coordinate3D actual) =>
        new(actual.X - expected.X, actual.Y - expected.Y, actual.Elevation - expected.Elevation);

    private static double SquaredDistance(Coordinate3D first, Coordinate3D second)
    {
        double x = first.X - second.X;
        double y = first.Y - second.Y;
        double z = first.Elevation - second.Elevation;
        return (x * x) + (y * y) + (z * z);
    }
}
