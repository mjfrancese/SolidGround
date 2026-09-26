using System.Globalization;
using NetTopologySuite.Geometries;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>
/// Turns one Esri JSON feature's flat <c>rings</c> array (a list of rings, each a closed sequence of X/Y
/// points) into a validated <see cref="PolygonalRegion"/>. Public (not internal): <c>SolidGround.Core</c>
/// grants no <c>InternalsVisibleTo</c> to <c>SolidGround.Tests</c>, and this type is exercised directly by its
/// own dedicated test file. Builds its own NTS <see cref="Polygon"/>/<see cref="MultiPolygon"/> internally and
/// calls <see cref="PolygonalRegion.FromGeometry"/> before returning, so its public signature never exposes an
/// NTS type.
/// </summary>
public static class EsriJsonPolygonReader
{
    /// <summary>
    /// Esri's documented convention: exterior rings are wound clockwise, holes counterclockwise; a multipart
    /// polygon is just more rings in the same flat array, disambiguated by winding, not explicit nesting. Each
    /// counterclockwise ring is assigned to the most recently opened clockwise shell that boundary-inclusively
    /// covers its first vertex (tried in reverse order); a service that violates this convention badly enough
    /// to produce nonsense topology is still caught by <see cref="PolygonalRegion.FromGeometry"/>'s own
    /// <c>IsValidOp</c> check.
    /// </summary>
    /// <exception cref="ParcelGeometryException">A ring is unclosed, too short, or the combined geometry fails topology validation.</exception>
    /// <exception cref="CountyParcelRegistryUnexpectedResponseException">A counterclockwise ring has no enclosing clockwise shell.</exception>
    public static PolygonalRegion Read(IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings, HorizontalReference reference, int featureIndex)
    {
        ArgumentNullException.ThrowIfNull(rings);
        ArgumentNullException.ThrowIfNull(reference);

        if (rings.Count == 0)
        {
            throw new ParcelGeometryException($"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)} has no rings.");
        }

        GeometryFactory factory = GeometryInterop.Services.CreateGeometryFactory();
        List<(LinearRing Shell, List<LinearRing> Holes)> shells = [];

        for (int ringIndex = 0; ringIndex < rings.Count; ringIndex++)
        {
            IReadOnlyList<(double X, double Y)> ringPoints = rings[ringIndex];
            LinearRing ring = BuildLinearRing(ringPoints, factory, featureIndex, ringIndex);

            if (SignedArea(ringPoints) < 0d)
            {
                shells.Add((ring, []));
                continue;
            }

            if (!TryAssignHoleToAnEnclosingShell(ring, shells, factory))
            {
                throw new CountyParcelRegistryUnexpectedResponseException(
                    $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)}'s ring {ringIndex.ToString(CultureInfo.InvariantCulture)} is counterclockwise (a hole) but has no enclosing clockwise shell.",
                    redactedRequestUri: null,
                    errorCode: null,
                    transportStatusCode: null,
                    serverMessage: null);
            }
        }

        List<Polygon> polygons = new(shells.Count);
        foreach ((LinearRing shell, List<LinearRing> holes) in shells)
        {
            polygons.Add(factory.CreatePolygon(shell, [.. holes]));
        }

        NtsGeometry combined = polygons.Count == 1 ? polygons[0] : factory.CreateMultiPolygon([.. polygons]);
        return PolygonalRegion.FromGeometry(combined, reference);
    }

    private static bool TryAssignHoleToAnEnclosingShell(LinearRing hole, List<(LinearRing Shell, List<LinearRing> Holes)> shells, GeometryFactory factory)
    {
        Point holeStart = factory.CreatePoint(hole.Coordinates[0]);
        for (int shellIndex = shells.Count - 1; shellIndex >= 0; shellIndex--)
        {
            Polygon shellPolygon = factory.CreatePolygon(shells[shellIndex].Shell);
            if (shellPolygon.Covers(holeStart))
            {
                shells[shellIndex].Holes.Add(hole);
                return true;
            }
        }

        return false;
    }

    /// <summary>The standard shoelace formula. Negative means clockwise (a shell); positive means counterclockwise (a hole).</summary>
    private static double SignedArea(IReadOnlyList<(double X, double Y)> points)
    {
        double sum = 0d;
        for (int i = 0; i < points.Count; i++)
        {
            (double x0, double y0) = points[i];
            (double x1, double y1) = points[(i + 1) % points.Count];
            sum += (x0 * y1) - (x1 * y0);
        }

        return 0.5d * sum;
    }

    private static LinearRing BuildLinearRing(IReadOnlyList<(double X, double Y)> points, GeometryFactory factory, int featureIndex, int ringIndex)
    {
        if (points.Count < 4)
        {
            throw new ParcelGeometryException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)}'s ring {ringIndex.ToString(CultureInfo.InvariantCulture)} has " +
                $"{points.Count.ToString(CultureInfo.InvariantCulture)} position(s), but a ring requires at least 4.");
        }

        (double firstX, double firstY) = points[0];
        (double lastX, double lastY) = points[^1];
        if (firstX != lastX || firstY != lastY)
        {
            throw new ParcelGeometryException(
                $"Feature {featureIndex.ToString(CultureInfo.InvariantCulture)}'s ring {ringIndex.ToString(CultureInfo.InvariantCulture)} is not closed: " +
                "its first position must exactly equal its last position.");
        }

        Coordinate[] coordinates = new Coordinate[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            coordinates[i] = new Coordinate(points[i].X, points[i].Y);
        }

        return factory.CreateLinearRing(coordinates);
    }
}
