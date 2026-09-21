using Autodesk.Revit.DB;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;

namespace SolidGround.Revit.Geometry;

/// <summary>
/// Builds the Revit geometry <c>Toposolid.Create</c> consumes from Core's Revit-free
/// <see cref="LocalBoundary"/> and retained <see cref="LocalTerrainSample"/>s. See SolidGround Issue #15's
/// design record §7.2 (the planarity fix): every boundary-ring vertex shares one constant, caller-supplied
/// Z, so a non-planar boundary is impossible by construction, not merely checked for -- the terrain shape
/// comes entirely from <see cref="BuildPoints"/>'s output. Every Revit API member used here is verified
/// against <c>apidump/out/Autodesk.Revit.DB.{CurveLoop,Line,XYZ,BoundingBoxXYZ}.txt</c>.
/// </summary>
internal static class BoundaryGeometryBuilder
{
    /// <summary>
    /// Converts every retained terrain sample into a Revit-internal <see cref="XYZ"/>, one component at a
    /// time via <see cref="UnitUtils.ConvertToInternalUnits(double, ForgeTypeId)"/> (design record §8: "per
    /// component", never a bare double assumed to already be Revit-internal).
    /// </summary>
    internal static IList<XYZ> BuildPoints(IReadOnlyList<LocalTerrainSample> samples, ForgeTypeId unit)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(unit);

        List<XYZ> points = new(samples.Count);
        foreach (LocalTerrainSample sample in samples)
        {
            points.Add(new XYZ(
                UnitUtils.ConvertToInternalUnits(sample.Position.X, unit),
                UnitUtils.ConvertToInternalUnits(sample.Position.Y, unit),
                UnitUtils.ConvertToInternalUnits(sample.Position.Elevation, unit)));
        }

        return points;
    }

    /// <summary>
    /// Builds one <see cref="CurveLoop"/> per shell/hole ring of every polygon in <paramref name="boundary"/>,
    /// every vertex sharing <paramref name="constantZInternal"/> (already Revit-internal units -- the caller,
    /// not this method, derives it from the retained samples' own local elevation range; see design record
    /// §6.4 step 3/§7.2). Closes each ring from its last stored vertex back to its first: a
    /// <see cref="LocalBoundaryRing"/> never repeats its first vertex as a trailing closing vertex
    /// (orchestrator decision (a)), so every ring here needs exactly <c>Vertices.Count</c> bound
    /// <see cref="Line"/> segments, the last one closing the loop.
    /// </summary>
    internal static IList<CurveLoop> BuildProfiles(LocalBoundary boundary, double constantZInternal, ForgeTypeId unit)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(unit);
        if (!double.IsFinite(constantZInternal))
        {
            throw new ArgumentOutOfRangeException(nameof(constantZInternal), constantZInternal, "The boundary plane elevation must be finite.");
        }

        List<CurveLoop> profiles = new(boundary.Polygons.Count);
        foreach (LocalBoundaryPolygon polygon in boundary.Polygons)
        {
            profiles.Add(BuildLoop(polygon.Shell, constantZInternal, unit));
            foreach (LocalBoundaryRing hole in polygon.Holes)
            {
                profiles.Add(BuildLoop(hole, constantZInternal, unit));
            }
        }

        return profiles;
    }

    /// <summary>The axis-aligned bounding box of <paramref name="points"/> alone -- never the boundary profiles (design record §6.4 step 5).</summary>
    /// <exception cref="ArgumentException"><paramref name="points"/> is empty.</exception>
    internal static BoundingBoxXYZ ComputeExpectedBoundingBox(IList<XYZ> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (points.Count == 0)
        {
            throw new ArgumentException("At least one point is required to compute a bounding box.", nameof(points));
        }

        XYZ first = points[0];
        double minX = first.X, minY = first.Y, minZ = first.Z;
        double maxX = minX, maxY = minY, maxZ = minZ;
        foreach (XYZ point in points)
        {
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            minZ = Math.Min(minZ, point.Z);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
            maxZ = Math.Max(maxZ, point.Z);
        }

        return new BoundingBoxXYZ
        {
            Min = new XYZ(minX, minY, minZ),
            Max = new XYZ(maxX, maxY, maxZ),
        };
    }

    private static CurveLoop BuildLoop(LocalBoundaryRing ring, double constantZInternal, ForgeTypeId unit)
    {
        IReadOnlyList<LocalCoordinate2D> vertices = ring.Vertices;
        if (vertices.Count < 3)
        {
            // Defensive: LocalBoundaryValidator already rejects this before Stage 4 is ever reached.
            throw new ArgumentException("A boundary ring needs at least three distinct vertices.", nameof(ring));
        }

        XYZ[] points = new XYZ[vertices.Count];
        for (int i = 0; i < vertices.Count; i++)
        {
            points[i] = new XYZ(
                UnitUtils.ConvertToInternalUnits(vertices[i].X, unit),
                UnitUtils.ConvertToInternalUnits(vertices[i].Y, unit),
                constantZInternal);
        }

        CurveLoop loop = new();
        for (int i = 0; i < points.Length; i++)
        {
            XYZ start = points[i];
            XYZ end = points[(i + 1) % points.Length];
            loop.Append(Line.CreateBound(start, end));
        }

        return loop;
    }
}
