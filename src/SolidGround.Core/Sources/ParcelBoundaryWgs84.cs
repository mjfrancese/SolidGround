using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;

namespace SolidGround.Core.Sources;

/// <summary>
/// Shared WGS 84 reference and area computation both parcel-boundary sources use, so their candidates are
/// directly comparable and internally self-consistent.
/// </summary>
public static class ParcelBoundaryWgs84
{
    /// <summary>
    /// The one <see cref="HorizontalReference"/> both parcel sources build every <see cref="PolygonalRegion"/>
    /// in. Built from the same canonical WKT <see cref="ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText"/>
    /// already uses, via <see cref="WellKnownTextReferenceParser"/> -- not a third hand-written literal.
    /// </summary>
    internal static readonly HorizontalReference Reference =
        WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;

    /// <summary>
    /// Computes a WGS 84 <paramref name="boundary"/>'s area in square meters, using <see cref="PolygonalRegion.Area"/>
    /// (the boundary's own true polygon area in square degrees) scaled by the meters-per-degree factors at the
    /// boundary's center latitude -- never the bounding-box width-times-height approximation, which
    /// systematically overstates area for any non-rectangular parcel. This is a per-degree scale factor
    /// evaluated at one center latitude, not a full ProjNET reprojection round trip; that remains unnecessary
    /// for this issue's own tolerance-based area assertions.
    /// </summary>
    public static double ComputeAreaSquareMeters(PolygonalRegion boundary)
    {
        ArgumentNullException.ThrowIfNull(boundary);

        double centerLatitude = (boundary.Envelope.MinY + boundary.Envelope.MaxY) / 2d;
        double metersPerDegreeLongitude = Wgs84Ellipsoid.MetersPerDegreeLongitude(centerLatitude);
        double metersPerDegreeLatitude = Wgs84Ellipsoid.MetersPerDegreeLatitude(centerLatitude);
        return boundary.Area * metersPerDegreeLongitude * metersPerDegreeLatitude;
    }
}
