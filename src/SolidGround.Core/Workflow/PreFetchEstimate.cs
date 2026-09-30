using SolidGround.Core.Processing;

namespace SolidGround.Core.Workflow;

/// <summary>A deliberately approximate, pre-acquisition fetch-envelope estimate.</summary>
public sealed record PreFetchEstimate(double EnvelopeSquareMeters, long ApproximateOneMeterSamples, int OpenTopographyRequestCount, string Label);

public static class PreFetchEstimator
{
    /// <summary>Estimates a rectangular WGS84 envelope only; it never opens a raster or calls a provider.</summary>
    public static PreFetchEstimate FromBoundingBox(BoundingBoxAoiSettings box)
    {
        ArgumentNullException.ThrowIfNull(box);
        double centerLatitude = (box.South + box.North) / 2d;
        double metersPerLatitudeDegree = 111_132d;
        double metersPerLongitudeDegree = 111_320d * Math.Cos(centerLatitude * Math.PI / 180d);
        double area = Math.Abs((box.East - box.West) * metersPerLongitudeDegree * (box.North - box.South) * metersPerLatitudeDegree);
        return Build(area);
    }

    public static PreFetchEstimate FromRadius(RadiusAoiSettings radius)
    {
        ArgumentNullException.ThrowIfNull(radius);
        return Build(Math.PI * radius.RadiusMeters * radius.RadiusMeters);
    }

    private static PreFetchEstimate Build(double area) => new(
        area, checked((long)Math.Ceiling(area)), 2,
        "Approximate rectangular fetch-envelope estimate at one metre spacing; OpenTopography USGS 1 m normally uses up to two requests (AAIGrid plus GeoTIFF metadata). It is not an entitlement, coverage, cost, or exact point-count quote.");
}
