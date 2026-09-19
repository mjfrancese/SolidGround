using NetTopologySuite.Geometries;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Aois;

/// <summary>Which side of an <see cref="IHorizontalCoordinateTransform"/> a <see cref="PolygonalRegionReprojection"/> applies.</summary>
public enum HorizontalTransformDirection
{
    /// <summary>Maps every coordinate from <c>transform.Definition.SourceReference</c> to <c>TargetReference</c>.</summary>
    Forward,

    /// <summary>Maps every coordinate from <c>transform.Definition.TargetReference</c> back to <c>SourceReference</c>.</summary>
    Inverse,
}

/// <summary>
/// Reprojects a <see cref="PolygonalRegion"/> through an <see cref="IHorizontalCoordinateTransform"/>. This is
/// the seam <see cref="Clipping.GridClipper"/>'s own exception message names ("Transform the region into the
/// grid's reference first (SolidGround Issue #6)") and <see cref="PolygonalRegion.Geometry"/>'s own doc comment
/// anticipates. It does not decide WHEN reprojection is needed, and it does not call
/// <see cref="Clipping.GridClipper"/> -- assembling the real acquire-reproject-clip pipeline is Issue #9's job.
/// </summary>
public static class PolygonalRegionReprojection
{
    /// <exception cref="ArgumentNullException"><paramref name="region"/> or <paramref name="transform"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="direction"/> is not a defined <see cref="HorizontalTransformDirection"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="region"/>'s <see cref="PolygonalRegion.HorizontalReference"/> does not exactly equal the
    /// reference <paramref name="transform"/> expects on the side named by <paramref name="direction"/>
    /// (<c>SourceReference</c> for <see cref="HorizontalTransformDirection.Forward"/>, <c>TargetReference</c>
    /// for <see cref="HorizontalTransformDirection.Inverse"/>). Comparison is <see cref="HorizontalReference"/>'s
    /// own value equality; a region and transform built from independently-authored WKT describing "the same"
    /// CRS with a differently-spelled datum will NOT compare equal. Callers should derive a region's reference
    /// from the same <see cref="WellKnownTextReferenceParser.Parse"/> call used to build the transform.
    /// </exception>
    /// <exception cref="HorizontalCoordinateTransformException">The transform itself fails for some coordinate; propagated unchanged from <paramref name="transform"/>.</exception>
    /// <exception cref="ParcelGeometryException">The reprojected geometry fails <see cref="PolygonalRegion.FromGeometry"/>'s validation (non-finite/out-of-range coordinate, invalid topology, or empty result).</exception>
    public static PolygonalRegion Reproject(PolygonalRegion region, IHorizontalCoordinateTransform transform, HorizontalTransformDirection direction)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(transform);
        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported horizontal transform direction.");
        }

        HorizontalReference expectedFrom = direction == HorizontalTransformDirection.Forward
            ? transform.Definition.SourceReference
            : transform.Definition.TargetReference;
        HorizontalReference producedTo = direction == HorizontalTransformDirection.Forward
            ? transform.Definition.TargetReference
            : transform.Definition.SourceReference;

        if (region.HorizontalReference != expectedFrom)
        {
            string expectedRole = direction == HorizontalTransformDirection.Forward ? "source" : "target";
            throw new ArgumentException(
                $"The region's horizontal reference '{region.HorizontalReference.CoordinateReferenceSystem}' does not match " +
                $"the transform's expected {expectedRole} reference '{expectedFrom.CoordinateReferenceSystem}'. Reproject a " +
                "region only with a transform whose corresponding reference exactly equals the region's own reference.",
                nameof(region));
        }

        NtsGeometry copy = region.Geometry.Copy();
        copy.Apply(new HorizontalTransformCoordinateFilter(transform, direction));
        copy.GeometryChanged();

        return PolygonalRegion.FromGeometry(copy, producedTo);
    }

    private sealed class HorizontalTransformCoordinateFilter(IHorizontalCoordinateTransform transform, HorizontalTransformDirection direction) : ICoordinateFilter
    {
        public void Filter(Coordinate coord)
        {
            Coordinate2D input = new(coord.X, coord.Y);
            Coordinate2D output = direction == HorizontalTransformDirection.Forward ? transform.Forward(input) : transform.Inverse(input);
            coord.X = output.X;
            coord.Y = output.Y;
        }
    }
}
