using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Processing;
using SolidGround.Core.Transformations;

namespace SolidGround.Core.Exports;

/// <summary>
/// Localizes a confirmed legal parcel without changing its footprint. It removes only exactly coincident
/// vertices and vertices proven to lie between collinear neighbors; every other short edge is rejected rather
/// than moved, snapped, or buffered.
/// </summary>
public static class LegalBoundaryFactory
{
    public static LocalBoundary FromPolygonalRegion(
        PolygonalRegion region,
        LocalCoordinateFrame frame,
        double minimumEdgeLength = 0d)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(frame);
        if (!double.IsFinite(minimumEdgeLength) || minimumEdgeLength < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumEdgeLength), "The minimum legal edge length must be finite and non-negative.");
        }

        if (region.HorizontalReference != frame.ProjectedHorizontalReference)
        {
            throw new ArgumentException(
                "The region's horizontal reference must equal the local coordinate frame's projected horizontal reference.",
                nameof(region));
        }

        List<LocalBoundaryPolygon> polygons = new(region.Polygons.Count);
        for (int polygonIndex = 0; polygonIndex < region.Polygons.Count; polygonIndex++)
        {
            PolygonRings source = region.Polygons[polygonIndex];
            LocalBoundaryRing shell = ToLocalRing(source.Shell, frame, minimumEdgeLength, $"polygon {polygonIndex + 1} shell");
            List<LocalBoundaryRing> holes = new(source.Holes.Count);
            for (int holeIndex = 0; holeIndex < source.Holes.Count; holeIndex++)
            {
                holes.Add(ToLocalRing(source.Holes[holeIndex], frame, minimumEdgeLength, $"polygon {polygonIndex + 1} hole {holeIndex + 1}"));
            }

            polygons.Add(new LocalBoundaryPolygon(shell, holes));
        }

        return new LocalBoundary(polygons);
    }

    private static LocalBoundaryRing ToLocalRing(
        IReadOnlyList<Coordinate2D> sourceRing,
        LocalCoordinateFrame frame,
        double minimumEdgeLength,
        string ringName)
    {
        List<Coordinate2D> sourceVertices = CleanLosslessly(sourceRing);
        List<LocalCoordinate2D> localVertices = new(sourceVertices.Count);
        foreach (Coordinate2D vertex in sourceVertices)
        {
            localVertices.Add(frame.ToLocalHorizontal(vertex));
        }

        RejectShortEdge(localVertices, minimumEdgeLength, ringName);
        return new LocalBoundaryRing(localVertices);
    }

    private static List<Coordinate2D> CleanLosslessly(IReadOnlyList<Coordinate2D> sourceRing)
    {
        int count = sourceRing.Count;
        if (count > 1 && sourceRing[0] == sourceRing[^1])
        {
            count--;
        }

        List<Coordinate2D> vertices = new(count);
        for (int index = 0; index < count; index++)
        {
            Coordinate2D coordinate = sourceRing[index];
            if (vertices.Count == 0 || vertices[^1] != coordinate)
            {
                vertices.Add(coordinate);
            }
        }

        if (vertices.Count > 1 && vertices[0] == vertices[^1])
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        bool changed;
        do
        {
            changed = false;
            if (vertices.Count < 3)
            {
                break;
            }

            for (int index = 0; index < vertices.Count; index++)
            {
                Coordinate2D previous = vertices[(index - 1 + vertices.Count) % vertices.Count];
                Coordinate2D current = vertices[index];
                Coordinate2D next = vertices[(index + 1) % vertices.Count];
                if (IsStrictlyBetweenOnSameLine(previous, current, next))
                {
                    vertices.RemoveAt(index);
                    changed = true;
                    break;
                }
            }
        }
        while (changed);

        return vertices;
    }

    private static bool IsStrictlyBetweenOnSameLine(Coordinate2D previous, Coordinate2D current, Coordinate2D next)
    {
        double firstX = current.X - previous.X;
        double firstY = current.Y - previous.Y;
        double secondX = next.X - current.X;
        double secondY = next.Y - current.Y;
        double cross = (firstX * secondY) - (firstY * secondX);
        if (cross != 0d)
        {
            return false;
        }

        return (firstX * secondX) + (firstY * secondY) > 0d;
    }

    private static void RejectShortEdge(List<LocalCoordinate2D> vertices, double minimumEdgeLength, string ringName)
    {
        if (minimumEdgeLength == 0d)
        {
            return;
        }

        for (int index = 0; index < vertices.Count; index++)
        {
            LocalCoordinate2D first = vertices[index];
            LocalCoordinate2D second = vertices[(index + 1) % vertices.Count];
            double deltaX = second.X - first.X;
            double deltaY = second.Y - first.Y;
            if (Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)) < minimumEdgeLength)
            {
                throw new ParcelExtentPlanningException(
                    $"The {ringName} contains an edge shorter than the required host tolerance. SolidGround will not move or snap a legal boundary to repair it.");
            }
        }
    }
}
