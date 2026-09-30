using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Exports;
using SolidGround.Core.Provenance;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>Pure composition for the Revit parcel path: legal geometry stays unbuffered while terrain gets its own margin.</summary>
public static class TerrainRunComposition
{
    public static ParcelExtentGeometry? BuildParcelExtent(
        AreaOfInterest aoi,
        IHorizontalCoordinateTransform wgs84ToGridTransform,
        LinearDistance terrainMargin,
        LinearDistance minimumLegalEdgeLength)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        ArgumentNullException.ThrowIfNull(wgs84ToGridTransform);
        if (aoi is not ParcelGeometryAoi parcelAoi)
        {
            return null;
        }

        if (parcelAoi.Buffer != LinearDistance.Zero)
        {
            throw new ParcelExtentPlanningException(
                "A legal parcel buffer is not supported in the Revit workflow. Set areaOfInterest.parcel.bufferMeters to zero and use terrainExtensionMeters for terrain-only context.");
        }

        PolygonalRegion legal = ParcelGeometryParser.Parse(parcelAoi);
        if (legal.HorizontalReference != wgs84ToGridTransform.Definition.TargetReference)
        {
            if (legal.HorizontalReference != wgs84ToGridTransform.Definition.SourceReference)
            {
                throw new ParcelExtentPlanningException("The legal parcel reference does not match the resolved terrain transform.");
            }

            legal = PolygonalRegionReprojection.Reproject(legal, wgs84ToGridTransform, HorizontalTransformDirection.Forward);
        }

        return new ParcelExtentGeometry(legal, terrainMargin, minimumLegalEdgeLength);
    }

    /// <summary>Builds the duplicate-guard identity after the final pipeline output is available.</summary>
    public static TerrainIdentity BuildIdentity(
        TerrainExportPayload payload,
        AreaOfInterest aoi,
        TerrainExtentPlan? extentPlan,
        double terrainExtensionMeters)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(aoi);
        if (!double.IsFinite(terrainExtensionMeters) || terrainExtensionMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(terrainExtensionMeters));
        }

        (TerrainIdentityKind kind, string material) = aoi switch
        {
            Wgs84BoundingBoxAoi box => (TerrainIdentityKind.BoundingBox, string.Create(CultureInfo.InvariantCulture, $"{box.WestLongitude:R},{box.SouthLatitude:R},{box.EastLongitude:R},{box.NorthLatitude:R}")),
            Wgs84RadiusAoi radius => (TerrainIdentityKind.Radius, string.Create(CultureInfo.InvariantCulture, $"{radius.Longitude:R},{radius.Latitude:R},{radius.Radius.ToMeters():R}")),
            ParcelGeometryAoi => (TerrainIdentityKind.Polygon, CanonicalRegion(extentPlan?.LegalParcelRegion ?? throw new ParcelExtentPlanningException("The parcel identity needs a resolved legal extent plan."))),
            _ => throw new ArgumentException($"Unsupported area-of-interest type '{aoi.GetType().Name}'.", nameof(aoi)),
        };

        string legalTopology = extentPlan is null ? material : CanonicalRegion(extentPlan.LegalParcelRegion);
        return TerrainIdentity.Create(payload, kind, material, legalTopology, terrainExtensionMeters);
    }

    private static string CanonicalRegion(PolygonalRegion region)
    {
        IEnumerable<string> polygons = region.Polygons.Select(polygon =>
            CanonicalRing(polygon.Shell) + "|" + string.Join("|", polygon.Holes.Select(CanonicalRing).OrderBy(value => value, StringComparer.Ordinal)));
        return string.Join("||", polygons.OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string CanonicalRing(IReadOnlyList<Geometry.Coordinate2D> ring)
    {
        int count = ring.Count > 1 && ring[0] == ring[^1] ? ring.Count - 1 : ring.Count;
        string[] points = Enumerable.Range(0, count).Select(index => string.Create(CultureInfo.InvariantCulture, $"{ring[index].X:R},{ring[index].Y:R}")).ToArray();
        string forward = MinimumRotation(points);
        Array.Reverse(points);
        string reverse = MinimumRotation(points);
        return string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse;
    }

    private static string MinimumRotation(string[] points) => Enumerable.Range(0, points.Length)
        .Select(start => string.Join(";", Enumerable.Range(0, points.Length).Select(offset => points[(start + offset) % points.Length])))
        .Min(StringComparer.Ordinal)!;
}
