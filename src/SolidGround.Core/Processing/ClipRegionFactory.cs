using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// Turns an <see cref="AreaOfInterest"/> into either a WGS 84 fetch envelope or a projected clip region. One
/// shared function for `process`, `fetch`, and `run`, so the bounding-box, radius, and parcel paths can
/// never diverge between commands. See docs/architecture/cli-workflow.md's "AOI and clip derivation"
/// section for the per-form algorithm this implements and the resolved ambiguities it documents (why a
/// parcel is always WGS 84, and why re-parsing an acquisition's own well-known text for a second
/// <see cref="HorizontalReference"/> is safe). Lifted into <c>SolidGround.Core</c> for SolidGround Issue #15:
/// both methods now take the caller's own already-built <see cref="AreaOfInterest"/> directly and dispatch on
/// its concrete type, rather than a CLI-only flattened selection -- the caller already holds one of
/// <see cref="Wgs84BoundingBoxAoi"/>, <see cref="Wgs84RadiusAoi"/>, or <see cref="ParcelGeometryAoi"/>, so
/// neither method ever reconstructs one from flattened fields.
/// </summary>
public static class ClipRegionFactory
{
    /// <summary>
    /// Builds the WGS 84 bounding box `fetch`/`run` request with, together with whether/how
    /// <see cref="AoiNormalizationOptions.MinimumFetchEnvelopeSide"/> widened it (SolidGround Issue #23). Never
    /// touches a grid. All three AOI forms now route through <see cref="AoiNormalizer.Normalize"/> with default
    /// options, including <c>BoundingBox</c> — previously the one form that built its envelope directly, with no
    /// minimum-side expansion and no <see cref="FetchEnvelopeExpansion"/> to report. The clip region
    /// (<see cref="Build"/>) never reads the fetch envelope or the expansion this method produces; its own
    /// <c>Parcel</c> branch calls <see cref="AoiNormalizer.Normalize"/> too, but explicitly disables the
    /// minimum-side expansion, since that guard exists only for the fetch request this method builds.
    /// </summary>
    /// <remarks>
    /// Takes only <paramref name="aoi"/>: every concrete <see cref="AreaOfInterest"/> case already carries
    /// whatever <see cref="HorizontalReference"/> its own construction needed (a parcel AOI's own
    /// <c>HorizontalReference</c> property is set and validated by its constructor), so there is nothing left
    /// for a separate WGS 84 reference parameter to do here. A caller building an <see cref="AreaOfInterest"/>
    /// from a flattened selection (for example, the CLI's own <c>AoiSelection.ToAreaOfInterest</c>) still
    /// needs a <see cref="HorizontalReference"/> for that step, just not for this one.
    /// </remarks>
    public static (Wgs84BoundingBoxAoi Envelope, FetchEnvelopeExpansion Expansion) BuildFetchEnvelope(AreaOfInterest aoi)
    {
        ArgumentNullException.ThrowIfNull(aoi);

        NormalizedAoi normalized = aoi switch
        {
            Wgs84BoundingBoxAoi bbox => AoiNormalizer.Normalize(bbox),
            Wgs84RadiusAoi radius => AoiNormalizer.Normalize(radius),
            ParcelGeometryAoi parcel => AoiNormalizer.Normalize(parcel),
            _ => throw new ArgumentOutOfRangeException(nameof(aoi)),
        };

        return (normalized.FetchEnvelope, normalized.MinimumSideExpansion);
    }

    /// <summary>Builds the clip region in the grid's own (projected) reference. Never called with a null AOI: step 1 of the processing pipeline skips clipping entirely for that case.</summary>
    public static ClipRegion Build(AreaOfInterest aoi, IHorizontalCoordinateTransform wgs84ToGridTransform)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        ArgumentNullException.ThrowIfNull(wgs84ToGridTransform);

        HorizontalReference gridReference = wgs84ToGridTransform.Definition.TargetReference;
        switch (aoi)
        {
            case Wgs84BoundingBoxAoi bbox:
            {
                Coordinate2D sw = wgs84ToGridTransform.Forward(new Coordinate2D(bbox.WestLongitude, bbox.SouthLatitude));
                Coordinate2D se = wgs84ToGridTransform.Forward(new Coordinate2D(bbox.EastLongitude, bbox.SouthLatitude));
                Coordinate2D ne = wgs84ToGridTransform.Forward(new Coordinate2D(bbox.EastLongitude, bbox.NorthLatitude));
                Coordinate2D nw = wgs84ToGridTransform.Forward(new Coordinate2D(bbox.WestLongitude, bbox.NorthLatitude));
                string wkt = BuildPolygonWkt(sw, se, ne, nw);
                PolygonalRegion region = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wkt, gridReference);
                return ClipRegion.FromRegion(region, LinearDistance.Zero);
            }

            case Wgs84RadiusAoi radius:
            {
                Coordinate2D center = wgs84ToGridTransform.Forward(new Coordinate2D(radius.Longitude, radius.Latitude));
                return ClipRegion.Circle(center, radius.Radius, gridReference);
            }

            case ParcelGeometryAoi parcel:
            {
                // Only NormalizedAoi.Parcel and NormalizedAoi.Buffer are read below: the fetch envelope and its
                // minimum-side expansion exist solely to satisfy OpenTopography's request-area minimum and are
                // irrelevant to a clip that never calls BuildFetchEnvelope. Disabling the expansion here keeps a
                // parcel near a pole or the antimeridian from failing this offline clip over a fetch-only guard
                // it does not need (SolidGround Issue #23).
                NormalizedAoi normalized = AoiNormalizer.Normalize(parcel, new AoiNormalizationOptions { MinimumFetchEnvelopeSide = LinearDistance.Zero });
                PolygonalRegion projected = PolygonalRegionReprojection.Reproject(normalized.Parcel!, wgs84ToGridTransform, HorizontalTransformDirection.Forward);
                return ClipRegion.FromRegion(projected, normalized.Buffer);
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(aoi));
        }
    }

    private static string BuildPolygonWkt(Coordinate2D p1, Coordinate2D p2, Coordinate2D p3, Coordinate2D p4)
    {
        string a = FormatOrdinatePair(p1);
        string b = FormatOrdinatePair(p2);
        string c = FormatOrdinatePair(p3);
        string d = FormatOrdinatePair(p4);
        return $"POLYGON (({a}, {b}, {c}, {d}, {a}))";
    }

    private static string FormatOrdinatePair(Coordinate2D point) =>
        $"{point.X.ToString("R", CultureInfo.InvariantCulture)} {point.Y.ToString("R", CultureInfo.InvariantCulture)}";
}
