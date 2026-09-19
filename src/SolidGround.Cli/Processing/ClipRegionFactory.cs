using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Processing;

/// <summary>
/// Turns an <see cref="AoiSelection"/> into either a WGS 84 fetch envelope or a projected clip region. One
/// shared function for `process`, `fetch`, and `run`, so the bounding-box, radius, and parcel paths can
/// never diverge between commands. See docs/architecture/cli-workflow.md's "AOI and clip derivation"
/// section for the per-form algorithm this implements and the resolved ambiguities it documents (why a
/// parcel is always WGS 84, and why re-parsing an acquisition's own well-known text for a second
/// <see cref="HorizontalReference"/> is safe).
/// </summary>
internal static class ClipRegionFactory
{
    /// <summary>Builds the WGS 84 bounding box `fetch`/`run` request with. Never touches a grid.</summary>
    internal static Wgs84BoundingBoxAoi BuildFetchEnvelope(AoiSelection aoi, HorizontalReference wgs84Reference)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        ArgumentNullException.ThrowIfNull(wgs84Reference);

        return aoi.Kind switch
        {
            AoiKind.BoundingBox => new Wgs84BoundingBoxAoi(aoi.West, aoi.South, aoi.East, aoi.North),
            AoiKind.Radius => AoiNormalizer.Normalize(new Wgs84RadiusAoi(aoi.CenterLatitude, aoi.CenterLongitude, aoi.Radius)).FetchEnvelope,
            AoiKind.Parcel => AoiNormalizer.Normalize(new ParcelGeometryAoi(aoi.ParcelFormat, aoi.ParcelText!, wgs84Reference, aoi.Buffer)).FetchEnvelope,
            _ => throw new ArgumentOutOfRangeException(nameof(aoi)),
        };
    }

    /// <summary>Builds the clip region in the grid's own (projected) reference. Never called with a null AOI: step 1 of the processing pipeline skips clipping entirely for that case.</summary>
    internal static ClipRegion Build(AoiSelection aoi, IHorizontalCoordinateTransform wgs84ToGridTransform)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        ArgumentNullException.ThrowIfNull(wgs84ToGridTransform);

        HorizontalReference gridReference = wgs84ToGridTransform.Definition.TargetReference;
        switch (aoi.Kind)
        {
            case AoiKind.BoundingBox:
            {
                Coordinate2D sw = wgs84ToGridTransform.Forward(new Coordinate2D(aoi.West, aoi.South));
                Coordinate2D se = wgs84ToGridTransform.Forward(new Coordinate2D(aoi.East, aoi.South));
                Coordinate2D ne = wgs84ToGridTransform.Forward(new Coordinate2D(aoi.East, aoi.North));
                Coordinate2D nw = wgs84ToGridTransform.Forward(new Coordinate2D(aoi.West, aoi.North));
                string wkt = BuildPolygonWkt(sw, se, ne, nw);
                PolygonalRegion region = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wkt, gridReference);
                return ClipRegion.FromRegion(region, LinearDistance.Zero);
            }

            case AoiKind.Radius:
            {
                Coordinate2D center = wgs84ToGridTransform.Forward(new Coordinate2D(aoi.CenterLongitude, aoi.CenterLatitude));
                return ClipRegion.Circle(center, aoi.Radius, gridReference);
            }

            case AoiKind.Parcel:
            {
                HorizontalReference wgs84 = wgs84ToGridTransform.Definition.SourceReference;
                ParcelGeometryAoi parcelAoi = new(aoi.ParcelFormat, aoi.ParcelText!, wgs84, aoi.Buffer);
                NormalizedAoi normalized = AoiNormalizer.Normalize(parcelAoi);
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
