using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Clipping;

/// <summary>
/// A polygonal clip boundary and the geometric buffer <see cref="GridClipper"/> applies to it, in the
/// boundary's own horizontal reference. The buffer here is a real geometric buffer, applied only once the
/// region is in a projected reference — never the fetch-envelope margin <c>AoiNormalizer</c> applies to
/// angular coordinates.
/// </summary>
public sealed class ClipRegion
{
    private ClipRegion(PolygonalRegion region, LinearDistance buffer)
    {
        Region = region;
        Buffer = buffer;
    }

    /// <summary>The clip boundary.</summary>
    public PolygonalRegion Region { get; }

    /// <summary>The geometric buffer <see cref="GridClipper"/> applies to <see cref="Region"/> before clipping.</summary>
    public LinearDistance Buffer { get; }

    /// <summary>Wraps an already-validated <paramref name="region"/> with a geometric <paramref name="buffer"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> is <see langword="null"/>.</exception>
    public static ClipRegion FromRegion(PolygonalRegion region, LinearDistance buffer)
    {
        ArgumentNullException.ThrowIfNull(region);
        return new ClipRegion(region, buffer);
    }

    /// <summary>
    /// Builds a circular clip region of <paramref name="radius"/> centered at <paramref name="center"/>, in
    /// <paramref name="projectedReference"/>. The circle is approximated with 8 line segments per quadrant
    /// (matching the approximation <see cref="GridClipper"/> uses for a geometric buffer), so its area is
    /// slightly less than an ideal circle's.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="projectedReference"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="projectedReference"/> is not projected with a linear unit.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is not positive.</exception>
    /// <exception cref="ParcelGeometryException">The resulting circle fails <see cref="PolygonalRegion.FromGeometry"/> validation.</exception>
    public static ClipRegion Circle(Coordinate2D center, LinearDistance radius, HorizontalReference projectedReference)
    {
        ArgumentNullException.ThrowIfNull(projectedReference);
        if (projectedReference.Kind != HorizontalReferenceKind.Projected)
        {
            throw new ArgumentException("A circular clip region requires a projected horizontal reference.", nameof(projectedReference));
        }

        LengthUnit linearUnit = projectedReference.Unit.LinearUnit
            ?? throw new ArgumentException("A circular clip region requires a projected horizontal reference with a linear unit.", nameof(projectedReference));

        if (radius.Value <= 0d)
        {
            // LinearDistance itself only rejects a negative value, so zero must be rejected here. Without
            // this check, a zero radius reaches Point.Buffer(0) — the exact "validify" operation GridClipper's
            // own BuildEffectiveRegion doc comment special-cases and avoids, since NTS documents it as a
            // topology validity fix, not a no-op. It happens to leave an empty polygon here, which
            // PolygonalRegion.FromGeometry then rejects with a generic "geometry is empty" message that does
            // not name the actual problem, unlike Wgs84RadiusAoi's constructor for the same mistake.
            throw new ArgumentOutOfRangeException(nameof(radius), radius.Value, "Radius must be positive.");
        }

        GeometryFactory factory = GeometryInterop.Services.CreateGeometryFactory();
        Point centerPoint = factory.CreatePoint(new Coordinate(center.X, center.Y));
        double radiusInReferenceUnit = radius.In(linearUnit);

        NtsGeometry circle = centerPoint.Buffer(
            radiusInReferenceUnit,
            new BufferParameters(8, EndCapStyle.Round, JoinStyle.Round, BufferParameters.DefaultMitreLimit));

        PolygonalRegion region = PolygonalRegion.FromGeometry(circle, projectedReference);
        return new ClipRegion(region, LinearDistance.Zero);
    }
}
