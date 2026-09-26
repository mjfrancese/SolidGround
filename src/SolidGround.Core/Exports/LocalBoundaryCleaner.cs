using SolidGround.Core.Geometry;

namespace SolidGround.Core.Exports;

/// <summary>
/// Repairs externally sourced <see cref="LocalBoundary"/> geometry -- dedupes exact and near-duplicate
/// cyclically consecutive vertices (including an implicit duplicate across the wrap-around closing edge that
/// <see cref="LocalBoundaryRing.Vertices"/> never stores explicitly), drops edges too short for the boundary's
/// eventual consumer to draw, and collapses collinear mid-edge vertices -- entirely in
/// <c>SolidGround.Core</c>, before <see cref="LocalBoundaryValidator.Validate"/> ever sees the boundary. See
/// <c>docs/architecture/revit-property-line-and-shared-coordinates.md</c>'s "Geometry cleanup contract"
/// section for the full contract this type implements (inputs and units, tolerance sourcing, order of
/// operations, and the reject-versus-repair split with <see cref="LocalBoundaryValidator"/>), including why a
/// hand-rolled, per-vertex walk was chosen over a whole-line simplifier such as Douglas-Peucker.
/// </summary>
public static class LocalBoundaryCleaner
{
    /// <summary>The default number of <see cref="Clean"/> passes attempted per ring before giving up on a slow-converging input.</summary>
    public const int DefaultMaxIterations = 8;

    /// <summary>
    /// Cleans every polygon of <paramref name="boundary"/>, one ring at a time (shell, then each hole),
    /// independently of every other ring or polygon -- this method never merges vertices across rings, never
    /// changes a ring's winding, and never reclassifies a hole as a shell. Each pass first dedupes cyclically
    /// consecutive vertices within <c>Math.Max(<paramref name="vertexTolerance"/>,
    /// <paramref name="minimumEdgeLength"/>)</c> of each other (dropping the second of the pair), then, from
    /// that same result, collapses any vertex whose perpendicular deviation from the line through its two
    /// cyclic neighbors is at most <paramref name="collinearityTolerance"/>. Passes repeat until one full pass
    /// makes no further change or <paramref name="maxIterations"/> is reached -- collapsing a collinear vertex
    /// can expose a newly sub-tolerance edge, and vice versa, so a single pass is not always enough. A ring
    /// that collapses below three vertices stops early rather than continuing to iterate: this method never
    /// throws for messy-but-parseable input, matching <see cref="LocalBoundaryFactory"/>'s and
    /// <see cref="LocalBoundaryValidator"/>'s own "never repairs by throwing" discipline --
    /// <see cref="LocalBoundaryValidator.Validate"/>, run immediately afterward and entirely unchanged by this
    /// type, is what actually rejects a still-too-small or otherwise invalid result. A vertex whose deviation
    /// exceeds <paramref name="collinearityTolerance"/> is preserved exactly, however sharp or spiky its
    /// corner, and true topology -- self-intersection, a hole falling outside its shell, or any other
    /// structural problem -- is never touched here; that stays <see cref="LocalBoundaryValidator"/>'s job.
    /// </summary>
    /// <param name="boundary">The boundary to clean. Never mutated; a new <see cref="LocalBoundary"/> is returned.</param>
    /// <param name="vertexTolerance">
    /// The Euclidean distance at or below which two cyclically consecutive vertices are treated as the same
    /// point. Expressed in whatever linear unit <paramref name="boundary"/>'s own coordinates use; no
    /// conversion is performed here.
    /// </param>
    /// <param name="collinearityTolerance">
    /// The perpendicular-distance budget at or below which a vertex is treated as lying on the line through its
    /// two cyclic neighbors, and so is dropped. Expressed in the same unit as <paramref name="vertexTolerance"/>.
    /// </param>
    /// <param name="minimumEdgeLength">
    /// The shortest edge length <paramref name="boundary"/>'s eventual consumer can actually draw. Folded into
    /// the dedupe pass's own tolerance via <c>Math.Max(vertexTolerance, minimumEdgeLength)</c> so that nothing
    /// <see cref="LocalBoundaryValidator.Validate"/>'s own equivalent check would reject can survive this
    /// method unrepaired. Expressed in the same unit as <paramref name="vertexTolerance"/>.
    /// </param>
    /// <param name="maxIterations">The most dedupe-then-collapse passes run per ring before giving up.</param>
    /// <exception cref="ArgumentNullException"><paramref name="boundary"/> is <see langword="null"/>.</exception>
    public static LocalBoundary Clean(
        LocalBoundary boundary,
        double vertexTolerance,
        double collinearityTolerance,
        double minimumEdgeLength,
        int maxIterations = DefaultMaxIterations)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        double dedupeTolerance = Math.Max(vertexTolerance, minimumEdgeLength);

        List<LocalBoundaryPolygon> polygons = new(boundary.Polygons.Count);
        foreach (LocalBoundaryPolygon polygon in boundary.Polygons)
        {
            LocalBoundaryRing shell = CleanRing(polygon.Shell, dedupeTolerance, collinearityTolerance, maxIterations);

            List<LocalBoundaryRing> holes = new(polygon.Holes.Count);
            foreach (LocalBoundaryRing hole in polygon.Holes)
            {
                holes.Add(CleanRing(hole, dedupeTolerance, collinearityTolerance, maxIterations));
            }

            polygons.Add(new LocalBoundaryPolygon(shell, holes));
        }

        return new LocalBoundary(polygons);
    }

    private static LocalBoundaryRing CleanRing(LocalBoundaryRing ring, double dedupeTolerance, double collinearityTolerance, int maxIterations)
    {
        List<LocalCoordinate2D> vertices = [.. ring.Vertices];

        for (int iteration = 0; iteration < maxIterations && vertices.Count >= 3; iteration++)
        {
            List<LocalCoordinate2D> beforePass = vertices;

            vertices = DedupeOnePass(vertices, dedupeTolerance);
            if (vertices.Count < 3)
            {
                break;
            }

            vertices = CollapseCollinearOnePass(vertices, collinearityTolerance);
            if (vertices.Count < 3 || vertices.SequenceEqual(beforePass))
            {
                break;
            }
        }

        return new LocalBoundaryRing(vertices);
    }

    /// <summary>
    /// Walks <paramref name="vertices"/> once, keeping the first vertex as a fixed anchor and dropping every
    /// later vertex within <paramref name="tolerance"/> of the last vertex still kept -- so a whole run of
    /// mutually near vertices collapses to its first member in one pass, not merely every other one. The
    /// wrap-around edge (the last kept vertex back to the anchor) is then checked the same way, since
    /// <see cref="LocalBoundaryRing.Vertices"/> never stores a repeated closing vertex but the edge it implies
    /// is real.
    /// </summary>
    private static List<LocalCoordinate2D> DedupeOnePass(List<LocalCoordinate2D> vertices, double tolerance)
    {
        List<LocalCoordinate2D> kept = new(vertices.Count) { vertices[0] };
        for (int i = 1; i < vertices.Count; i++)
        {
            if (Distance(kept[^1], vertices[i]) > tolerance)
            {
                kept.Add(vertices[i]);
            }
        }

        if (kept.Count > 1 && Distance(kept[^1], kept[0]) <= tolerance)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return kept;
    }

    /// <summary>
    /// Evaluates every vertex of <paramref name="vertices"/> against its two cyclic neighbors from that same,
    /// unmodified snapshot -- so which vertices are dropped never depends on visiting order -- then returns
    /// every vertex whose perpendicular deviation did not fall within <paramref name="tolerance"/>, in their
    /// original cyclic order.
    /// </summary>
    private static List<LocalCoordinate2D> CollapseCollinearOnePass(List<LocalCoordinate2D> vertices, double tolerance)
    {
        int count = vertices.Count;
        bool[] drop = new bool[count];
        for (int i = 0; i < count; i++)
        {
            LocalCoordinate2D previous = vertices[(i - 1 + count) % count];
            LocalCoordinate2D current = vertices[i];
            LocalCoordinate2D next = vertices[(i + 1) % count];
            drop[i] = PerpendicularDistance(current, previous, next) <= tolerance;
        }

        List<LocalCoordinate2D> kept = new(count);
        for (int i = 0; i < count; i++)
        {
            if (!drop[i])
            {
                kept.Add(vertices[i]);
            }
        }

        return kept;
    }

    /// <summary>
    /// The perpendicular distance from <paramref name="point"/> to the infinite line through
    /// <paramref name="lineStart"/> and <paramref name="lineEnd"/>, or the plain distance to
    /// <paramref name="lineStart"/> when the two coincide (a degenerate "line" that is really just a point, so
    /// no direction is defined).
    /// </summary>
    private static double PerpendicularDistance(LocalCoordinate2D point, LocalCoordinate2D lineStart, LocalCoordinate2D lineEnd)
    {
        double directionX = lineEnd.X - lineStart.X;
        double directionY = lineEnd.Y - lineStart.Y;
        double lengthSquared = (directionX * directionX) + (directionY * directionY);
        if (lengthSquared <= 0d)
        {
            return Distance(point, lineStart);
        }

        double toPointX = point.X - lineStart.X;
        double toPointY = point.Y - lineStart.Y;
        double cross = (directionX * toPointY) - (directionY * toPointX);
        return Math.Abs(cross) / Math.Sqrt(lengthSquared);
    }

    private static double Distance(LocalCoordinate2D a, LocalCoordinate2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
