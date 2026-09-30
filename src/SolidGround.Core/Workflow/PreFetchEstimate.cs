using SolidGround.Core.Aois;
using SolidGround.Core.Processing;

namespace SolidGround.Core.Workflow;

/// <summary>A deliberately approximate, pre-acquisition fetch-envelope estimate.</summary>
public sealed record PreFetchEstimate(
    double EnvelopeSquareMeters,
    long ApproximateOneMeterSamples,
    int OpenTopographyRequestCount,
    string Label,
    Wgs84BoundingBoxAoi? FetchEnvelope = null,
    FetchEnvelopeExpansion? MinimumSideExpansion = null);

public static class PreFetchEstimator
{
    /// <summary>Estimates a rectangular WGS84 envelope only; it never opens a raster or calls a provider.</summary>
    public static PreFetchEstimate FromBoundingBox(BoundingBoxAoiSettings box)
    {
        ArgumentNullException.ThrowIfNull(box);
        return FromAreaOfInterest(new Wgs84BoundingBoxAoi(box.West, box.South, box.East, box.North));
    }

    public static PreFetchEstimate FromRadius(RadiusAoiSettings radius)
    {
        ArgumentNullException.ThrowIfNull(radius);
        return FromAreaOfInterest(new Wgs84RadiusAoi(
            radius.CenterLatitude,
            radius.CenterLongitude,
            SolidGround.Core.Units.LinearDistance.Meters(radius.RadiusMeters)));
    }

    /// <summary>
    /// Estimates the exact WGS 84 fetch envelope that acquisition would request, including its normalizer's
    /// 110-metre minimum-side expansion. This is deliberately pre-acquisition and has no provider side effect.
    /// </summary>
    public static PreFetchEstimate FromAreaOfInterest(AreaOfInterest aoi, SolidGround.Core.Units.LinearDistance? terrainMargin = null)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        Wgs84BoundingBoxAoi envelope = ResolveFetchEnvelope(aoi, terrainMargin ?? SolidGround.Core.Units.LinearDistance.Zero, out FetchEnvelopeExpansion? expansion);
        return Build(envelope, expansion, openTopographyRequestCount: 2,
            "Approximate rectangular fetch-envelope estimate at one metre spacing; OpenTopography USGS 1 m normally uses up to two requests (AAIGrid plus GeoTIFF metadata). It is not an entitlement, coverage, cost, or exact point-count quote.");
    }

    /// <summary>
    /// Calculates the same envelope and approximate sample count for a local-input run without representing it
    /// as an OpenTopography request or contacting any source.
    /// </summary>
    public static PreFetchEstimate FromProcessAreaOfInterest(AreaOfInterest aoi, SolidGround.Core.Units.LinearDistance? terrainMargin = null)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        Wgs84BoundingBoxAoi envelope = ResolveFetchEnvelope(aoi, terrainMargin ?? SolidGround.Core.Units.LinearDistance.Zero, out FetchEnvelopeExpansion? expansion);
        return Build(envelope, expansion, openTopographyRequestCount: 0,
            "Approximate rectangular terrain-envelope estimate at one metre spacing for local input; it reads no raster, makes no HTTP request, and is not an exact point-count quote.");
    }

    private static Wgs84BoundingBoxAoi ResolveFetchEnvelope(AreaOfInterest aoi, SolidGround.Core.Units.LinearDistance terrainMargin, out FetchEnvelopeExpansion? expansion)
    {
        if (aoi is ParcelGeometryAoi parcel)
        {
            // The planner builds the same conservative projection-aware envelope used by live acquisition.
            // A legal parcel must remain unbuffered; terrain margin is intentionally a separate value.
            expansion = null;
            return ParcelFetchEnvelopePlanner.Build(parcel, terrainMargin);
        }

        if (terrainMargin != SolidGround.Core.Units.LinearDistance.Zero)
        {
            throw new ArgumentException("A terrain margin is valid only for a legal parcel area.", nameof(terrainMargin));
        }

        (Wgs84BoundingBoxAoi envelope, FetchEnvelopeExpansion result) = ClipRegionFactory.BuildFetchEnvelope(aoi);
        expansion = result;
        return envelope;
    }

    private static PreFetchEstimate Build(Wgs84BoundingBoxAoi envelope, FetchEnvelopeExpansion? expansion, int openTopographyRequestCount, string label)
    {
        // Use the exact same conservative distance factors as AoiNormalizer's minimum-envelope operation.
        // The result is deliberately an estimate of the requested rectangle, rather than a circle or parcel
        // area, because OpenTopography receives this rectangle.
        double latitudeForLongitudeFactor = Math.Abs(envelope.SouthLatitude) >= Math.Abs(envelope.NorthLatitude)
            ? envelope.SouthLatitude
            : envelope.NorthLatitude;
        double widthMeters = (envelope.EastLongitude - envelope.WestLongitude) *
            Wgs84Ellipsoid.MetersPerDegreeLongitude(latitudeForLongitudeFactor);
        double heightMeters = (envelope.NorthLatitude - envelope.SouthLatitude) *
            Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);
        double area = widthMeters * heightMeters;
        return new PreFetchEstimate(area, checked((long)Math.Ceiling(area)), openTopographyRequestCount, label, envelope, expansion);
    }
}
