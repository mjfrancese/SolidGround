using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// Builds a bounded, numerically conservative WGS 84 request envelope for a legal parcel before source-grid
/// CRS metadata is available. It derives the union from the same terrain-only projected buffers for every
/// verified NAD83 UTM candidate; it does not issue an acquisition request or select a source CRS. It is not
/// a formal unbounded coverage proof; post-grid complete-coverage validation remains authoritative.
/// </summary>
public static class ParcelFetchEnvelopePlanner
{
    /// <summary>The greatest projected segment length sampled before transforming a buffered candidate back to WGS 84.</summary>
    public static readonly LinearDistance MaximumProjectedSampleSpacing = LinearDistance.Meters(5d);

    /// <summary>
    /// Explicit numerical allowance applied around the union of inverse-transformed samples. Its adequacy is
    /// independently exercised at one-metre spacing for the supported candidates; it is a fetch coverage
    /// allowance, not a legal-boundary or terrain-buffer edit, and does not establish a formal global guarantee.
    /// </summary>
    public static readonly LinearDistance NumericalPadding = LinearDistance.Meters(10d);

    /// <summary>Caps per-candidate inverse samples so a pathological local parcel cannot cause unbounded preflight work.</summary>
    public const int MaximumSamplesPerCandidate = 250_000;

    private static readonly VerticalReference PlanningPlaceholderVerticalReference = new("Planning placeholder", LengthUnit.Meter);

    /// <summary>
    /// Builds the validated numerical request envelope for an unbuffered canonical WGS 84 legal parcel and
    /// its separate projected terrain margin. The result remains subject to post-grid complete-coverage
    /// validation once the source CRS and grid are known.
    /// </summary>
    public static Wgs84BoundingBoxAoi Build(ParcelGeometryAoi legalParcel, LinearDistance terrainMargin)
    {
        ArgumentNullException.ThrowIfNull(legalParcel);
        if (legalParcel.Buffer.Value != 0d)
        {
            throw new ParcelFetchEnvelopePlanningException(
                "A pre-acquisition parcel fetch envelope requires an unbuffered legal parcel; supply the terrain margin separately.");
        }

        if (legalParcel.HorizontalReference != ParcelBoundaryWgs84.Reference)
        {
            throw new ParcelFetchEnvelopePlanningException(
                "A pre-acquisition parcel fetch envelope currently requires the canonical WGS 84 parcel reference. " +
                "Projected or other geographic parcel references require an explicitly verified transform and are not approximated here.");
        }

        PolygonalRegion legalRegion = ParcelGeometryParser.Parse(legalParcel);
        GeographicBounds bounds = new();
        foreach (NorthAmericanUtmDefinition candidate in NorthAmericanUtmWellKnownText.Definitions)
        {
            string targetWkt = NorthAmericanUtmWellKnownText.Create(candidate.EpsgCode, PlanningPlaceholderVerticalReference);
            IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
                ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText,
                targetWkt);
            PolygonalRegion projectedLegalRegion = PolygonalRegionReprojection.Reproject(
                legalRegion,
                transform,
                HorizontalTransformDirection.Forward);
            PolygonalRegion terrainRegion = GridClipper.ResolveEffectiveRegion(
                ClipRegion.FromRegion(projectedLegalRegion, terrainMargin));

            int sampleCount = 0;
            VisitDensifiedCoverageBoundary(terrainRegion, projected =>
            {
                Coordinate2D geographic = transform.Inverse(projected);
                bounds.Include(geographic);
                sampleCount++;
                if (sampleCount > MaximumSamplesPerCandidate)
                {
                    throw new ParcelFetchEnvelopePlanningException(
                        $"The legal parcel requires more than {MaximumSamplesPerCandidate} inverse samples for one UTM candidate at " +
                        $"the {MaximumProjectedSampleSpacing.ToMeters():G} m maximum spacing; reduce its complexity before acquisition.");
                }
            });
        }

        if (!bounds.HasValue)
        {
            throw new ParcelFetchEnvelopePlanningException("The legal parcel produced no finite inverse-transform samples for a fetch envelope.");
        }

        (double west, double south, double east, double north) = Pad(bounds, NumericalPadding.ToMeters());
        Wgs84BoundingBoxAoi rawEnvelope;
        try
        {
            rawEnvelope = new Wgs84BoundingBoxAoi(west, south, east, north);
        }
        catch (ArgumentException ex)
        {
            throw new ParcelFetchEnvelopePlanningException(
                "The conservative pre-acquisition terrain envelope reaches outside the supported WGS 84 longitude/latitude range.", ex);
        }

        // Reuse the established 110 m minimum fetch-side policy after all projected-buffer samples and the
        // explicit numerical allowance are included. This adds no geographic approximation of terrainMargin.
        return AoiNormalizer.Normalize(rawEnvelope).FetchEnvelope;
    }

    private static void VisitDensifiedCoverageBoundary(PolygonalRegion region, Action<Coordinate2D> visitor)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(visitor);
        foreach (PolygonRings polygon in region.Polygons)
        {
            VisitDensifiedRing(polygon.Shell, visitor);
            foreach (IReadOnlyList<Coordinate2D> hole in polygon.Holes)
            {
                VisitDensifiedRing(hole, visitor);
            }
        }

        PlanarEnvelope envelope = region.Envelope;
        VisitDensifiedRing(
        [
            new Coordinate2D(envelope.MinX, envelope.MinY), new Coordinate2D(envelope.MaxX, envelope.MinY),
            new Coordinate2D(envelope.MaxX, envelope.MaxY), new Coordinate2D(envelope.MinX, envelope.MaxY),
            new Coordinate2D(envelope.MinX, envelope.MinY),
        ],
        visitor);
    }

    private static void VisitDensifiedRing(IReadOnlyList<Coordinate2D> ring, Action<Coordinate2D> visitor)
    {
        if (ring.Count < 2)
        {
            return;
        }

        for (int index = 1; index < ring.Count; index++)
        {
            VisitDensifiedSegment(ring[index - 1], ring[index], visitor);
        }
    }

    private static void VisitDensifiedSegment(Coordinate2D start, Coordinate2D end, Action<Coordinate2D> visitor)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        double intervalCount = Math.Ceiling(length / MaximumProjectedSampleSpacing.ToMeters());
        if (!double.IsFinite(intervalCount) || intervalCount > MaximumSamplesPerCandidate)
        {
            throw new ParcelFetchEnvelopePlanningException(
                $"One projected terrain-boundary segment requires more than {MaximumSamplesPerCandidate} samples at " +
                $"the {MaximumProjectedSampleSpacing.ToMeters():G} m maximum spacing; reduce its complexity before acquisition.");
        }

        int intervals = (int)intervalCount;
        intervals = Math.Max(intervals, 1);
        for (int interval = 0; interval <= intervals; interval++)
        {
            double fraction = (double)interval / intervals;
            visitor(new Coordinate2D(start.X + (fraction * deltaX), start.Y + (fraction * deltaY)));
        }
    }

    private static (double West, double South, double East, double North) Pad(GeographicBounds bounds, double paddingMeters)
    {
        double extremeLatitude = Math.Abs(bounds.South) >= Math.Abs(bounds.North) ? bounds.South : bounds.North;
        double metersPerLongitudeDegree = Wgs84Ellipsoid.MetersPerDegreeLongitude(extremeLatitude);
        if (metersPerLongitudeDegree <= 0d || !double.IsFinite(metersPerLongitudeDegree))
        {
            throw new ParcelFetchEnvelopePlanningException("The pre-acquisition terrain envelope is too close to a pole for a finite longitude allowance.");
        }

        double longitudePadding = paddingMeters / metersPerLongitudeDegree;
        double latitudePadding = paddingMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);
        return (
            bounds.West - longitudePadding,
            bounds.South - latitudePadding,
            bounds.East + longitudePadding,
            bounds.North + latitudePadding);
    }

    private sealed class GeographicBounds
    {
        public double West { get; private set; } = double.PositiveInfinity;
        public double South { get; private set; } = double.PositiveInfinity;
        public double East { get; private set; } = double.NegativeInfinity;
        public double North { get; private set; } = double.NegativeInfinity;
        public bool HasValue => double.IsFinite(West) && double.IsFinite(South) && double.IsFinite(East) && double.IsFinite(North);

        public void Include(Coordinate2D coordinate)
        {
            if (!double.IsFinite(coordinate.X) || !double.IsFinite(coordinate.Y))
            {
                throw new ParcelFetchEnvelopePlanningException("A UTM candidate produced a non-finite inverse-transform coordinate.");
            }

            West = Math.Min(West, coordinate.X);
            South = Math.Min(South, coordinate.Y);
            East = Math.Max(East, coordinate.X);
            North = Math.Max(North, coordinate.Y);
        }
    }
}

/// <summary>A pre-acquisition legal-parcel fetch envelope could not be conservatively planned.</summary>
public sealed class ParcelFetchEnvelopePlanningException : InvalidOperationException
{
    public ParcelFetchEnvelopePlanningException(string message)
        : base(message)
    {
    }

    public ParcelFetchEnvelopePlanningException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
