using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;

namespace SolidGround.Core.Exports;

/// <summary>Every problem <see cref="LocalBoundaryValidator.Validate"/> found; empty means the boundary and its retained samples are usable.</summary>
public sealed record LocalBoundaryValidationResult(IReadOnlyList<string> Problems)
{
    public bool IsValid => Problems.Count == 0;
}

/// <summary>
/// Revit-free geometry validation for a <see cref="LocalBoundary"/> and the retained
/// <see cref="LocalTerrainSample"/>s it is meant to bound, run entirely before any Revit API call
/// (SolidGround Issue #15's Geometry Preflight stage). <see cref="Validate"/> never throws for structurally
/// malformed input -- every ring-shape, sample, or budget problem it finds becomes one line in the returned
/// result instead, so a caller (SolidGround.Revit's Preflight dialog) can show every problem found in one
/// pass rather than one exception at a time.
/// </summary>
public static class LocalBoundaryValidator
{
    /// <summary>The default containment tolerance used when <see cref="Validate"/>'s own parameter is <see langword="null"/>.</summary>
    public static readonly double DefaultContainmentToleranceMeters = 0.01;

    /// <summary>
    /// Validates <paramref name="boundary"/>'s own shape (every ring has at least three distinct vertices, no
    /// zero-length or otherwise duplicated cyclically-consecutive edge -- which also rejects a ring that still
    /// carries a redundant closing vertex equal to its first, since a stored <see cref="LocalBoundaryRing"/>
    /// never repeats one, see orchestrator decision (a) -- no self-intersection, every hole inside its shell,
    /// positive area) and <paramref name="retainedSamples"/> against it (no two retained samples share an
    /// (X, Y) position, every retained sample falls within <paramref name="containmentToleranceMeters"/> of
    /// the boundary, and the retained count does not exceed <paramref name="pointBudget"/>). Out-of-boundary
    /// samples are aggregated into one summarized problem line (a count and the worst offset), never one line
    /// per point.
    /// </summary>
    /// <param name="containmentToleranceMeters">
    /// Compared directly, with no unit conversion, against <paramref name="boundary"/>/<paramref name="retainedSamples"/>'s
    /// own coordinate values -- despite this parameter's name, it must already be expressed in whatever
    /// linear unit those coordinates use (a caller building them from a <see cref="SolidGround.Core.Transformations.LocalCoordinateFrame"/>
    /// must convert into that frame's own <c>OutputUnit</c> first, which is not always meters). Falls back to
    /// <see cref="DefaultContainmentToleranceMeters"/> -- genuinely meters, since <see langword="null"/> means
    /// "use the untransformed default" -- when <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="boundary"/> or <paramref name="retainedSamples"/> is <see langword="null"/>.</exception>
    public static LocalBoundaryValidationResult Validate(
        LocalBoundary boundary,
        IReadOnlyList<LocalTerrainSample> retainedSamples,
        int pointBudget,
        double? containmentToleranceMeters = null)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(retainedSamples);

        double tolerance = containmentToleranceMeters ?? DefaultContainmentToleranceMeters;
        List<string> problems = [];

        ValidateBoundaryShape(boundary, problems);

        if (retainedSamples.Count > pointBudget)
        {
            problems.Add(
                $"{retainedSamples.Count.ToString(CultureInfo.InvariantCulture)} retained terrain sample(s) " +
                $"exceed the point budget of {pointBudget.ToString(CultureInfo.InvariantCulture)}.");
        }

        ValidateNoDuplicateHorizontalPositions(retainedSamples, problems);
        ValidateRetainedSamplesWithinTolerance(boundary, retainedSamples, tolerance, problems);

        return new LocalBoundaryValidationResult(problems);
    }

    private static void ValidateBoundaryShape(LocalBoundary boundary, List<string> problems)
    {
        if (boundary.Polygons.Count == 0)
        {
            problems.Add("The boundary has no polygons.");
            return;
        }

        GeometryFactory factory = GeometryInterop.Services.CreateGeometryFactory();
        for (int polygonIndex = 0; polygonIndex < boundary.Polygons.Count; polygonIndex++)
        {
            LocalBoundaryPolygon polygon = boundary.Polygons[polygonIndex];
            string polygonLabel = $"boundary polygon {polygonIndex.ToString(CultureInfo.InvariantCulture)}";

            ValidateRingShape(polygon.Shell, $"{polygonLabel} shell", problems);
            for (int holeIndex = 0; holeIndex < polygon.Holes.Count; holeIndex++)
            {
                ValidateRingShape(polygon.Holes[holeIndex], $"{polygonLabel} hole {holeIndex.ToString(CultureInfo.InvariantCulture)}", problems);
            }

            Polygon? built = LocalBoundaryFactory.TryBuildNtsPolygon(factory, polygon);
            if (built is null)
            {
                // Already reported above by ValidateRingShape (too few distinct vertices, or a zero-length
                // edge that also made the ring unbuildable); nothing further to add here.
                continue;
            }

            var validity = new IsValidOp(built);
            if (!validity.IsValid)
            {
                TopologyValidationError error = validity.ValidationError;
                string near = error.Coordinate is { } coordinate
                    ? $" near ({GeometryInterop.FormatOrdinate(coordinate.X)}, {GeometryInterop.FormatOrdinate(coordinate.Y)})"
                    : string.Empty;
                problems.Add($"{polygonLabel} is not a valid simple polygon: {error.Message}{near}.");
            }
            else if (built.Area <= 0d)
            {
                problems.Add($"{polygonLabel} has non-positive area.");
            }
        }
    }

    /// <summary>
    /// Rejects fewer than three distinct vertices, and any pair of cyclically consecutive vertices (including
    /// the wrap-around edge from the last vertex back to the first, since <paramref name="ring"/> never stores
    /// that closing vertex explicitly) that are equal -- a zero-length edge wherever it occurs, including a
    /// redundant explicit closing vertex a caller constructed the ring with by mistake.
    /// </summary>
    private static void ValidateRingShape(LocalBoundaryRing ring, string ringLabel, List<string> problems)
    {
        IReadOnlyList<LocalCoordinate2D> vertices = ring.Vertices;
        int distinctCount = new HashSet<LocalCoordinate2D>(vertices).Count;
        if (distinctCount < 3)
        {
            problems.Add($"The {ringLabel} has fewer than three distinct vertices.");
            return;
        }

        int count = vertices.Count;
        for (int i = 0; i < count; i++)
        {
            LocalCoordinate2D current = vertices[i];
            LocalCoordinate2D next = vertices[(i + 1) % count];
            if (current.Equals(next))
            {
                problems.Add(
                    $"The {ringLabel} has a zero-length edge at vertex {i.ToString(CultureInfo.InvariantCulture)} " +
                    "(two cyclically consecutive vertices, including the closing edge, are equal).");
            }
        }
    }

    private static void ValidateNoDuplicateHorizontalPositions(IReadOnlyList<LocalTerrainSample> retainedSamples, List<string> problems)
    {
        HashSet<LocalCoordinate2D> seen = [];
        int duplicateCount = 0;
        foreach (LocalTerrainSample sample in retainedSamples)
        {
            LocalCoordinate2D horizontal = new(sample.Position.X, sample.Position.Y);
            if (!seen.Add(horizontal))
            {
                duplicateCount++;
            }
        }

        if (duplicateCount > 0)
        {
            problems.Add(
                $"{duplicateCount.ToString(CultureInfo.InvariantCulture)} retained terrain sample(s) share an " +
                "(X, Y) position with another retained sample; every retained point must have a distinct horizontal position.");
        }
    }

    private static void ValidateRetainedSamplesWithinTolerance(
        LocalBoundary boundary, IReadOnlyList<LocalTerrainSample> retainedSamples, double tolerance, List<string> problems)
    {
        int outsideCount = 0;
        double worstOffset = 0d;
        foreach (LocalTerrainSample sample in retainedSamples)
        {
            LocalCoordinate2D horizontal = new(sample.Position.X, sample.Position.Y);
            double distance = LocalBoundaryFactory.DistanceTo(boundary, horizontal);
            if (distance > tolerance)
            {
                outsideCount++;
                worstOffset = Math.Max(worstOffset, distance);
            }
        }

        if (outsideCount > 0)
        {
            problems.Add(
                $"{outsideCount.ToString(CultureInfo.InvariantCulture)} retained terrain sample(s) fall outside " +
                "the boundary by more than the containment tolerance; the worst offset is " +
                $"{worstOffset.ToString("R", CultureInfo.InvariantCulture)}.");
        }
    }
}
