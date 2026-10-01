namespace SolidGround.Core.Geometry;

/// <summary>One unmatched expected terrain vertex and its closest observed vertex delta.</summary>
public sealed record TerrainVertexMismatch(int ExpectedIndex, Coordinate3D Expected, Coordinate3D? NearestActual, Coordinate3D NearestDelta);

/// <summary>Reports that more than one expected vertex could only use the same observed vertex.</summary>
public sealed record TerrainVertexCandidateCollision(int ActualIndex, IReadOnlyList<int> ExpectedIndices);

/// <summary>The result of a tolerance-bounded, injective expected-to-actual vertex match.</summary>
public sealed record TerrainVertexMatchResult(
    IReadOnlyList<TerrainVertexMismatch> UnmatchedExpected,
    IReadOnlyList<TerrainVertexCandidateCollision> CandidateCollisions,
    int CandidateComparisons)
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

        Dictionary<Bucket, List<int>> spatialIndex = BuildSpatialIndex(actual, tolerance);
        List<int>[] candidates = new List<int>[expected.Count];
        Dictionary<int, List<int>> reverseCandidates = [];
        int comparisons = 0;

        for (int expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            candidates[expectedIndex] = [];
            foreach (int actualIndex in CandidateIndices(expected[expectedIndex], spatialIndex, tolerance))
            {
                comparisons++;
                if (WithinTolerance(expected[expectedIndex], actual[actualIndex], tolerance))
                {
                    candidates[expectedIndex].Add(actualIndex);
                    if (!reverseCandidates.TryGetValue(actualIndex, out List<int>? reverse))
                    {
                        reverse = [];
                        reverseCandidates.Add(actualIndex, reverse);
                    }
                    reverse.Add(expectedIndex);
                }
            }

            candidates[expectedIndex].Sort((left, right) => SquaredDistance(expected[expectedIndex], actual[left]).CompareTo(SquaredDistance(expected[expectedIndex], actual[right])));
        }

        int[] actualToExpected = Enumerable.Repeat(-1, actual.Count).ToArray();
        int[] expectedToActual = Enumerable.Repeat(-1, expected.Count).ToArray();
        for (int expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            HashSet<int> visitedActual = [];
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
        foreach ((int actualIndex, List<int> contenders) in reverseCandidates.OrderBy(pair => pair.Key))
        {
            if (contenders.Count > 1)
            {
                collisions.Add(new TerrainVertexCandidateCollision(actualIndex, contenders.AsReadOnly()));
            }
        }

        return new TerrainVertexMatchResult(unmatched.AsReadOnly(), collisions.AsReadOnly(), comparisons);
    }

    private static bool TryAssign(int expectedIndex, List<int>[] candidates, int[] actualToExpected, int[] expectedToActual, HashSet<int> visitedActual)
    {
        foreach (int actualIndex in candidates[expectedIndex])
        {
            if (visitedActual.Contains(actualIndex))
            {
                continue;
            }

            visitedActual.Add(actualIndex);
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

    private static Dictionary<Bucket, List<int>> BuildSpatialIndex(IReadOnlyList<Coordinate3D> actual, double tolerance)
    {
        Dictionary<Bucket, List<int>> index = [];
        for (int indexValue = 0; indexValue < actual.Count; indexValue++)
        {
            Bucket bucket = Bucket.For(actual[indexValue], tolerance);
            if (!index.TryGetValue(bucket, out List<int>? values))
            {
                values = [];
                index.Add(bucket, values);
            }
            values.Add(indexValue);
        }
        return index;
    }

    private static List<int> CandidateIndices(Coordinate3D expected, Dictionary<Bucket, List<int>> index, double tolerance)
    {
        if (tolerance == 0d)
        {
            return index.TryGetValue(Bucket.For(expected, tolerance), out List<int>? exact) ? exact : [];
        }

        Bucket center = Bucket.For(expected, tolerance);
        List<int> result = [];
        for (long x = center.X - 1; x <= center.X + 1; x++)
        for (long y = center.Y - 1; y <= center.Y + 1; y++)
        for (long z = center.Z - 1; z <= center.Z + 1; z++)
        {
            if (index.TryGetValue(new Bucket(x, y, z), out List<int>? values)) result.AddRange(values);
        }
        return result;
    }

    private readonly record struct Bucket(long X, long Y, long Z)
    {
        internal static Bucket For(Coordinate3D point, double tolerance) => tolerance == 0d
            ? new(BitConverter.DoubleToInt64Bits(point.X), BitConverter.DoubleToInt64Bits(point.Y), BitConverter.DoubleToInt64Bits(point.Elevation))
            : new(ToCell(point.X, tolerance), ToCell(point.Y, tolerance), ToCell(point.Elevation, tolerance));

        private static long ToCell(double value, double size) => checked((long)Math.Floor(value / size));
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
