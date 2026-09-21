using NetTopologySuite.Geometries;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;

namespace SolidGround.Core.Exports;

/// <summary>
/// Builds a <see cref="LocalBoundary"/> from an already-validated, already-projected region or an
/// unclipped grid's own corner envelope, and implements tolerance-aware containment against it. Internally
/// backed by NetTopologySuite (reusing <see cref="GeometryInterop"/>, the same seam
/// <see cref="PolygonalRegion"/>/<see cref="Clipping.ClipRegion"/> already use) for robust polygon validity,
/// area, and point-distance queries -- but, per SolidGround Issue #15's own architecture-test guard
/// (<c>ArchitectureTests.NetTopologySuiteTypesNeverAppearInAnyNewPublicCoreSignature</c>), no NetTopologySuite
/// type ever appears on a public member here; every such helper below is <see langword="internal"/>, shared
/// with <see cref="LocalBoundaryValidator"/> in the same assembly.
/// </summary>
public static class LocalBoundaryFactory
{
    /// <summary>
    /// Projects <paramref name="region"/>'s polygons into <paramref name="frame"/>'s horizontal axes. Every
    /// shell/hole ring <paramref name="region"/> carries (<see cref="PolygonRings.Shell"/> and
    /// <see cref="PolygonRings.Holes"/>) always repeats its first coordinate as a trailing closing coordinate
    /// (the OGC/NetTopologySuite convention); this method strips that duplicate so the resulting
    /// <see cref="LocalBoundaryRing.Vertices"/> never does (SolidGround Issue #15, orchestrator decision (a)).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="region"/>'s horizontal reference does not match <paramref name="frame"/>'s.</exception>
    public static LocalBoundary FromPolygonalRegion(PolygonalRegion region, LocalCoordinateFrame frame)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(frame);

        if (region.HorizontalReference != frame.ProjectedHorizontalReference)
        {
            throw new ArgumentException(
                "The region's horizontal reference must equal the local coordinate frame's projected horizontal reference.",
                nameof(region));
        }

        List<LocalBoundaryPolygon> polygons = new(region.Polygons.Count);
        foreach (PolygonRings source in region.Polygons)
        {
            LocalBoundaryRing shell = ToLocalRing(source.Shell, frame);
            List<LocalBoundaryRing> holes = new(source.Holes.Count);
            foreach (IReadOnlyList<Coordinate2D> hole in source.Holes)
            {
                holes.Add(ToLocalRing(hole, frame));
            }

            polygons.Add(new LocalBoundaryPolygon(shell, holes));
        }

        return new LocalBoundary(polygons);
    }

    /// <summary>
    /// Builds the whole-grid rectangle boundary used as `process` mode's fallback when no AOI is configured:
    /// one polygon, no holes, one ring of the grid's four corner-envelope vertices
    /// (<see cref="ElevationGrid.GetCornerEnvelope"/>), southwest/southeast/northeast/northwest, with no
    /// repeated closing vertex.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="grid"/> or <paramref name="frame"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="grid"/>'s horizontal reference does not match <paramref name="frame"/>'s.</exception>
    public static LocalBoundary FromGridEnvelope(ElevationGrid grid, LocalCoordinateFrame frame)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(frame);

        if (grid.HorizontalReference != frame.ProjectedHorizontalReference)
        {
            throw new ArgumentException(
                "The grid's horizontal reference must equal the local coordinate frame's projected horizontal reference.",
                nameof(grid));
        }

        PlanarEnvelope envelope = grid.GetCornerEnvelope();
        LocalCoordinate2D southwest = frame.ToLocalHorizontal(new Coordinate2D(envelope.MinX, envelope.MinY));
        LocalCoordinate2D southeast = frame.ToLocalHorizontal(new Coordinate2D(envelope.MaxX, envelope.MinY));
        LocalCoordinate2D northeast = frame.ToLocalHorizontal(new Coordinate2D(envelope.MaxX, envelope.MaxY));
        LocalCoordinate2D northwest = frame.ToLocalHorizontal(new Coordinate2D(envelope.MinX, envelope.MaxY));

        LocalBoundaryRing shell = new([southwest, southeast, northeast, northwest]);
        return new LocalBoundary([new LocalBoundaryPolygon(shell, [])]);
    }

    /// <summary>
    /// True when <paramref name="point"/> is within <paramref name="tolerance"/> of <paramref name="boundary"/>
    /// -- inside any polygon's shell (minus its holes), on its edge, or merely close to it. A point strictly
    /// inside a hole is excluded unless it is within <paramref name="tolerance"/> of the hole's own edge, the
    /// same boundary-adjacent tolerance a point just inside or outside the outer shell gets. A polygon whose
    /// shell cannot be built into a valid ring (fewer than three distinct vertices, or a degenerate/invalid
    /// shape) contributes no coverage rather than throwing -- <see cref="LocalBoundaryValidator"/> is
    /// responsible for reporting that as its own problem.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="boundary"/> is <see langword="null"/>.</exception>
    public static bool ContainsWithTolerance(this LocalBoundary boundary, LocalCoordinate2D point, double tolerance) =>
        DistanceTo(boundary, point) <= tolerance;

    /// <summary>
    /// The minimum distance from <paramref name="point"/> to any polygon of <paramref name="boundary"/> that
    /// could be built into a valid geometry (zero when the point is inside or on one), or
    /// <see cref="double.PositiveInfinity"/> when <paramref name="boundary"/> has no polygon that could be
    /// built at all. <see langword="internal"/>: <see cref="ContainsWithTolerance"/> is the public tolerance
    /// query; <see cref="LocalBoundaryValidator"/> uses this directly to report a retained point's worst
    /// offset, not only whether it passed.
    /// </summary>
    internal static double DistanceTo(LocalBoundary boundary, LocalCoordinate2D point)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        GeometryFactory factory = GeometryInterop.Services.CreateGeometryFactory();
        Point queryPoint = factory.CreatePoint(new Coordinate(point.X, point.Y));

        double nearest = double.PositiveInfinity;
        foreach (LocalBoundaryPolygon polygon in boundary.Polygons)
        {
            Polygon? built = TryBuildNtsPolygon(factory, polygon);
            if (built is not null)
            {
                nearest = Math.Min(nearest, built.Distance(queryPoint));
            }
        }

        return nearest;
    }

    /// <summary>
    /// Builds an NTS <see cref="Polygon"/> (shell plus every hole that could itself be built) from
    /// <paramref name="polygon"/>, or <see langword="null"/> when the shell cannot be built into a valid ring.
    /// A hole that cannot be built is silently omitted (not treated as excluded terrain) rather than aborting
    /// the whole polygon: <see cref="LocalBoundaryValidator"/> reports that hole's own ring-shape problem
    /// separately, and dropping it here is the safer direction for a containment query (fewer spurious
    /// "outside the boundary" rejections from a boundary that is already reported invalid for other reasons).
    /// <see langword="internal"/>: shared with <see cref="LocalBoundaryValidator"/>'s self-intersection and
    /// area checks so the ring/polygon construction is defined exactly once.
    /// </summary>
    internal static Polygon? TryBuildNtsPolygon(GeometryFactory factory, LocalBoundaryPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(polygon);

        LinearRing? shell = TryBuildNtsRing(factory, polygon.Shell);
        if (shell is null)
        {
            return null;
        }

        List<LinearRing> holes = [];
        foreach (LocalBoundaryRing hole in polygon.Holes)
        {
            LinearRing? holeRing = TryBuildNtsRing(factory, hole);
            if (holeRing is not null)
            {
                holes.Add(holeRing);
            }
        }

        try
        {
            return factory.CreatePolygon(shell, [.. holes]);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds an NTS <see cref="LinearRing"/> from <paramref name="ring"/> by re-closing it (appending its
    /// first vertex as NTS's own required trailing closing coordinate -- the reverse of
    /// <see cref="FromPolygonalRegion"/>'s stripping), or <see langword="null"/> when <paramref name="ring"/>
    /// has fewer than three vertices or is otherwise not a valid simple ring. Never throws.
    /// </summary>
    internal static LinearRing? TryBuildNtsRing(GeometryFactory factory, LocalBoundaryRing ring)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(ring);

        IReadOnlyList<LocalCoordinate2D> vertices = ring.Vertices;
        if (vertices.Count < 3)
        {
            return null;
        }

        Coordinate[] coordinates = new Coordinate[vertices.Count + 1];
        for (int i = 0; i < vertices.Count; i++)
        {
            coordinates[i] = new Coordinate(vertices[i].X, vertices[i].Y);
        }

        coordinates[^1] = coordinates[0];

        try
        {
            return factory.CreateLinearRing(coordinates);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static LocalBoundaryRing ToLocalRing(IReadOnlyList<Coordinate2D> sourceRing, LocalCoordinateFrame frame)
    {
        int count = sourceRing.Count;
        int openCount = count > 0 && sourceRing[0] == sourceRing[count - 1] ? count - 1 : count;

        List<LocalCoordinate2D> vertices = new(openCount);
        for (int i = 0; i < openCount; i++)
        {
            vertices.Add(frame.ToLocalHorizontal(sourceRing[i]));
        }

        return new LocalBoundaryRing(vertices);
    }
}
