using System.Globalization;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Aois;

/// <summary>Identifies how <see cref="NormalizedAoi.FetchEnvelope"/> was derived from its source area of interest.</summary>
public enum FetchEnvelopeBasis
{
    /// <summary>The source was already a WGS 84 bounding box, optionally padded by a margin.</summary>
    BoundingBox,

    /// <summary>The source was a WGS 84 center and radius; the envelope was built on the WGS 84 ellipsoid.</summary>
    RadiusOnWgs84Ellipsoid,

    /// <summary>The source was a parcel already in a geographic reference; its own envelope was padded.</summary>
    GeographicParcelEnvelope,

    /// <summary>The source was a parcel in a projected reference; its vertices were transformed to WGS 84 first.</summary>
    TransformedParcelEnvelope,
}

/// <summary>An area of interest could not be normalized into a WGS 84 fetch envelope.</summary>
public sealed class AoiNormalizationException : InvalidOperationException
{
    public AoiNormalizationException()
    {
    }

    public AoiNormalizationException(string message)
        : base(message)
    {
    }

    public AoiNormalizationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Options controlling <see cref="AoiNormalizer.Normalize"/>. Every member has a safe default.</summary>
public sealed record AoiNormalizationOptions
{
    /// <summary>
    /// Extra padding applied only to the fetch envelope, on top of any parcel buffer. It is never applied to
    /// clip geometry: a geometric buffer is applied only by <c>GridClipper</c>, after a parcel is in a
    /// projected reference.
    /// </summary>
    public LinearDistance EnvelopeMargin { get; init; } = LinearDistance.Zero;

    /// <summary>
    /// The smallest width and height <see cref="NormalizedAoi.FetchEnvelope"/> may have, applied after
    /// <see cref="EnvelopeMargin"/> and any parcel buffer. Every basis is affected: a fetch envelope narrower
    /// than this on either axis is expanded symmetrically about its own centre until that axis reaches
    /// exactly this distance, never touching clip geometry. The default, 110 m, guards against
    /// OpenTopography's own undocumented minimum request area (see
    /// docs/architecture/aoi-normalization-and-clipping.md's "Minimum fetch envelope, verified 2026-09-20"
    /// section for the probe evidence and the margin this default carries over the observed threshold).
    /// <see cref="LinearDistance.Zero"/> disables this expansion entirely; <see cref="LinearDistance"/>
    /// itself rejects a negative value.
    /// </summary>
    public LinearDistance MinimumFetchEnvelopeSide { get; init; } = LinearDistance.Meters(110d);
}

/// <summary>
/// Records whether <see cref="AoiNormalizer.Normalize"/> widened <see cref="NormalizedAoi.FetchEnvelope"/> to
/// meet <see cref="AoiNormalizationOptions.MinimumFetchEnvelopeSide"/>, and the width/height before and after.
/// When <see cref="Applied"/> is <see langword="false"/>, <see cref="WidthAfter"/> equals <see cref="WidthBefore"/>
/// and <see cref="HeightAfter"/> equals <see cref="HeightBefore"/>.
/// </summary>
public sealed record FetchEnvelopeExpansion(
    bool Applied,
    LinearDistance MinimumSide,
    LinearDistance WidthBefore,
    LinearDistance HeightBefore,
    LinearDistance WidthAfter,
    LinearDistance HeightAfter);

/// <summary>
/// The result of normalizing one <see cref="AreaOfInterest"/> into a WGS 84 fetch envelope suitable for
/// requesting source data, together with the AOI's own clip geometry where one exists.
/// </summary>
public sealed record NormalizedAoi
{
    public NormalizedAoi(
        AreaOfInterest source,
        Wgs84BoundingBoxAoi fetchEnvelope,
        PolygonalRegion? parcel,
        LinearDistance buffer,
        FetchEnvelopeBasis basis,
        LinearDistance envelopeMargin,
        FetchEnvelopeExpansion minimumSideExpansion)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(fetchEnvelope);
        ArgumentNullException.ThrowIfNull(minimumSideExpansion);
        if (!Enum.IsDefined(basis))
        {
            throw new ArgumentOutOfRangeException(nameof(basis), basis, "Unsupported fetch envelope basis.");
        }

        bool expectsParcel = basis is FetchEnvelopeBasis.GeographicParcelEnvelope or FetchEnvelopeBasis.TransformedParcelEnvelope;
        if (expectsParcel != (parcel is not null))
        {
            throw new ArgumentException(
                $"A parsed parcel is required exactly when basis is {nameof(FetchEnvelopeBasis.GeographicParcelEnvelope)} " +
                $"or {nameof(FetchEnvelopeBasis.TransformedParcelEnvelope)}.",
                nameof(parcel));
        }

        Source = source;
        FetchEnvelope = fetchEnvelope;
        Parcel = parcel;
        Buffer = buffer;
        Basis = basis;
        EnvelopeMargin = envelopeMargin;
        MinimumSideExpansion = minimumSideExpansion;
    }

    /// <summary>The area of interest this result was normalized from.</summary>
    public AreaOfInterest Source { get; }

    /// <summary>The WGS 84 bounding box an elevation source should be asked to cover.</summary>
    public Wgs84BoundingBoxAoi FetchEnvelope { get; }

    /// <summary>The parsed parcel boundary in its own declared reference; <see langword="null"/> for a bounding box or radius AOI.</summary>
    public PolygonalRegion? Parcel { get; }

    /// <summary>
    /// The geometric buffer to apply during clipping (zero for a bounding box or radius AOI). This is a
    /// conceptually distinct distance from <see cref="EnvelopeMargin"/> — a geometric clip buffer versus extra
    /// fetch-envelope padding, never interchanged in behavior — but the two are ordinary <see cref="LinearDistance"/>
    /// values and may coincidentally share a numeric value (for example, both are <see cref="LinearDistance.Zero"/>
    /// for a bounding box AOI normalized with default options).
    /// </summary>
    public LinearDistance Buffer { get; }

    /// <summary>How <see cref="FetchEnvelope"/> was derived.</summary>
    public FetchEnvelopeBasis Basis { get; }

    /// <summary>The extra fetch-envelope margin that was applied, from <see cref="AoiNormalizationOptions.EnvelopeMargin"/>.</summary>
    public LinearDistance EnvelopeMargin { get; }

    /// <summary>
    /// Whether, and by how much, <see cref="FetchEnvelope"/> was widened to meet
    /// <see cref="AoiNormalizationOptions.MinimumFetchEnvelopeSide"/>, applied after <see cref="EnvelopeMargin"/>
    /// and any parcel buffer.
    /// </summary>
    public FetchEnvelopeExpansion MinimumSideExpansion { get; }
}

/// <summary>
/// WGS 84 ellipsoid meters-per-degree factors, used to pad geographic envelopes by a distance measured in
/// meters. Uses the WGS 84 defining constants (semi-major axis 6378137 m, inverse flattening
/// 298.257223563) with the standard meridional and prime-vertical radius-of-curvature formulas.
/// </summary>
public static class Wgs84Ellipsoid
{
    private const double SemiMajorAxisMeters = 6378137d;
    private const double InverseFlattening = 298.257223563d;
    private const double Flattening = 1d / InverseFlattening;
    private const double EccentricitySquared = Flattening * (2d - Flattening);
    private const double DegreesToRadians = Math.PI / 180d;

    /// <summary>
    /// The number of meters spanned by one degree of latitude at <paramref name="latitudeDegrees"/>, along
    /// the meridian (north-south). Uses the meridional radius of curvature
    /// <c>M = a(1-e^2) / (1-e^2 sin^2(phi))^1.5</c>.
    /// </summary>
    public static double MetersPerDegreeLatitude(double latitudeDegrees)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitudeDegrees, nameof(latitudeDegrees));
        double sinPhiSquared = SinSquared(latitudeDegrees);
        double denominator = 1d - (EccentricitySquared * sinPhiSquared);
        double meridionalRadius = SemiMajorAxisMeters * (1d - EccentricitySquared) / Math.Pow(denominator, 1.5d);
        return DegreesToRadians * meridionalRadius;
    }

    /// <summary>
    /// The number of meters spanned by one degree of longitude at <paramref name="latitudeDegrees"/>, along
    /// the parallel (east-west). Uses the prime-vertical radius of curvature <c>N = a / sqrt(1-e^2 sin^2(phi))</c>
    /// scaled by <c>cos(phi)</c>. This is effectively zero at the poles (within floating-point precision,
    /// ~1e-12), since a degree of longitude spans no distance there, but <c>cos(±90°)</c> is not bit-exact
    /// <c>0.0</c> in IEEE-754 double precision — <c>Math.PI</c> is itself only an approximation of pi.
    /// </summary>
    public static double MetersPerDegreeLongitude(double latitudeDegrees)
    {
        Wgs84BoundingBoxAoi.ValidateLatitude(latitudeDegrees, nameof(latitudeDegrees));
        double phi = latitudeDegrees * DegreesToRadians;
        double primeVerticalRadius = SemiMajorAxisMeters / Math.Sqrt(1d - (EccentricitySquared * SinSquared(latitudeDegrees)));
        return DegreesToRadians * primeVerticalRadius * Math.Cos(phi);
    }

    private static double SinSquared(double latitudeDegrees)
    {
        double sinPhi = Math.Sin(latitudeDegrees * DegreesToRadians);
        return sinPhi * sinPhi;
    }
}

/// <summary>
/// Normalizes every <see cref="AreaOfInterest"/> form into a WGS 84 fetch envelope an elevation source can be
/// asked to cover, keeping the AOI's own clip geometry (where one exists) in its own reference. See
/// docs/architecture/aoi-normalization-and-clipping.md for the padding rule and its conservatism.
/// </summary>
public static class AoiNormalizer
{
    /// <summary>
    /// Normalizes <paramref name="aoi"/>. <paramref name="parcelToWgs84"/> is required only when
    /// <paramref name="aoi"/> is a <see cref="ParcelGeometryAoi"/> in a projected reference (SolidGround
    /// Issue #6 supplies an implementation); every other AOI form ignores it.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="aoi"/> is <see langword="null"/>.</exception>
    /// <exception cref="ParcelGeometryException">A parcel AOI's geometry text is not valid.</exception>
    /// <exception cref="AoiNormalizationException">
    /// The padded envelope would cross the antimeridian or a pole, or a projected parcel was supplied with no
    /// <paramref name="parcelToWgs84"/>.
    /// </exception>
    public static NormalizedAoi Normalize(
        AreaOfInterest aoi,
        AoiNormalizationOptions? options = null,
        IHorizontalCoordinateTransform? parcelToWgs84 = null)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        AoiNormalizationOptions effectiveOptions = options ?? new AoiNormalizationOptions();
        LinearDistance margin = effectiveOptions.EnvelopeMargin;
        LinearDistance minimumSide = effectiveOptions.MinimumFetchEnvelopeSide;

        return aoi switch
        {
            Wgs84BoundingBoxAoi bbox => NormalizeBoundingBox(bbox, margin, minimumSide),
            Wgs84RadiusAoi radius => NormalizeRadius(radius, margin, minimumSide),
            ParcelGeometryAoi parcel => NormalizeParcel(parcel, margin, minimumSide, parcelToWgs84),
            _ => throw new ArgumentException($"Unsupported area-of-interest type '{aoi.GetType().Name}'.", nameof(aoi)),
        };
    }

    private static NormalizedAoi NormalizeBoundingBox(Wgs84BoundingBoxAoi bbox, LinearDistance margin, LinearDistance minimumSide)
    {
        (double west, double south, double east, double north) = PadEnvelope(
            bbox.WestLongitude, bbox.SouthLatitude, bbox.EastLongitude, bbox.NorthLatitude,
            margin.ToMeters());
        (west, south, east, north, FetchEnvelopeExpansion expansion) = ExpandToMinimumSide(west, south, east, north, minimumSide);

        Wgs84BoundingBoxAoi fetchEnvelope = new(west, south, east, north);
        return new NormalizedAoi(bbox, fetchEnvelope, parcel: null, LinearDistance.Zero, FetchEnvelopeBasis.BoundingBox, margin, expansion);
    }

    /// <summary>
    /// Pads a WGS 84 circle of <c>radius + margin</c> meters around the AOI's center. Both meters-per-degree
    /// factors are evaluated at the center latitude, so the resulting box under-covers a true geodesic circle
    /// by a residual below one centimeter for radii up to 5 km at mid latitudes; <see cref="AoiNormalizationOptions.EnvelopeMargin"/>
    /// absorbs larger cases.
    /// </summary>
    private static NormalizedAoi NormalizeRadius(Wgs84RadiusAoi radiusAoi, LinearDistance margin, LinearDistance minimumSide)
    {
        double padMeters = radiusAoi.Radius.ToMeters() + margin.ToMeters();
        (double west, double south, double east, double north) = PadAroundLatitudes(
            radiusAoi.Longitude, radiusAoi.Latitude, radiusAoi.Longitude, radiusAoi.Latitude,
            padMeters, radiusAoi.Latitude, radiusAoi.Latitude);
        (west, south, east, north, FetchEnvelopeExpansion expansion) = ExpandToMinimumSide(west, south, east, north, minimumSide);

        Wgs84BoundingBoxAoi fetchEnvelope = new(west, south, east, north);
        return new NormalizedAoi(radiusAoi, fetchEnvelope, parcel: null, LinearDistance.Zero, FetchEnvelopeBasis.RadiusOnWgs84Ellipsoid, margin, expansion);
    }

    private static NormalizedAoi NormalizeParcel(
        ParcelGeometryAoi parcelAoi, LinearDistance margin, LinearDistance minimumSide, IHorizontalCoordinateTransform? parcelToWgs84)
    {
        PolygonalRegion parcel = ParcelGeometryParser.Parse(parcelAoi);
        double padMeters = parcelAoi.Buffer.ToMeters() + margin.ToMeters();

        return parcelAoi.HorizontalReference.Kind switch
        {
            HorizontalReferenceKind.Geographic => NormalizeGeographicParcel(parcelAoi, parcel, padMeters, margin, minimumSide),
            HorizontalReferenceKind.Projected => NormalizeProjectedParcel(parcelAoi, parcel, padMeters, margin, minimumSide, parcelToWgs84),
            _ => throw new ArgumentOutOfRangeException(nameof(parcelAoi), parcelAoi.HorizontalReference.Kind, "Unsupported horizontal reference kind."),
        };
    }

    /// <summary>
    /// A parcel already in a geographic reference pads its own envelope directly. This padding is a
    /// fetch-coverage guarantee, not a geometric buffer — the geometric buffer is applied only by
    /// <c>GridClipper</c> in a projected reference, so a buffer is still never applied to angular coordinates.
    /// A non-WGS84 geographic datum such as NAD83 differs from WGS 84 by roughly 1-2 m; that residual is
    /// covered by <see cref="AoiNormalizationOptions.EnvelopeMargin"/>, and the parcel itself keeps its
    /// declared datum unchanged.
    /// </summary>
    private static NormalizedAoi NormalizeGeographicParcel(
        ParcelGeometryAoi parcelAoi, PolygonalRegion parcel, double padMeters, LinearDistance margin, LinearDistance minimumSide)
    {
        PlanarEnvelope envelope = parcel.Envelope;
        (double west, double south, double east, double north) = PadEnvelope(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY, padMeters);
        (west, south, east, north, FetchEnvelopeExpansion expansion) = ExpandToMinimumSide(west, south, east, north, minimumSide);

        Wgs84BoundingBoxAoi fetchEnvelope = new(west, south, east, north);
        return new NormalizedAoi(parcelAoi, fetchEnvelope, parcel, parcelAoi.Buffer, FetchEnvelopeBasis.GeographicParcelEnvelope, margin, expansion);
    }

    /// <summary>
    /// A parcel in a projected reference has every vertex of every ring (shell and holes, across every
    /// polygon) transformed to WGS 84 with <paramref name="parcelToWgs84"/>; the fetch envelope is the padded
    /// envelope of those transformed vertices. <see cref="NormalizedAoi.Parcel"/> keeps the untransformed,
    /// originally-parsed geometry in its own projected reference — the transform is used only to size the
    /// fetch envelope.
    /// </summary>
    private static NormalizedAoi NormalizeProjectedParcel(
        ParcelGeometryAoi parcelAoi, PolygonalRegion parcel, double padMeters, LinearDistance margin, LinearDistance minimumSide,
        IHorizontalCoordinateTransform? parcelToWgs84)
    {
        if (parcelToWgs84 is null)
        {
            throw new AoiNormalizationException(
                "Normalizing a parcel area of interest in a projected reference requires a horizontal coordinate " +
                "transform to WGS 84 (SolidGround Issue #6 supplies one); none was provided.");
        }

        List<Coordinate2D> transformedVertices = [];
        foreach (PolygonRings rings in parcel.Polygons)
        {
            transformedVertices.AddRange(rings.Shell.Select(parcelToWgs84.Forward));
            foreach (IReadOnlyList<Coordinate2D> hole in rings.Holes)
            {
                transformedVertices.AddRange(hole.Select(parcelToWgs84.Forward));
            }
        }

        double minLongitude = transformedVertices.Min(vertex => vertex.X);
        double maxLongitude = transformedVertices.Max(vertex => vertex.X);
        double minLatitude = transformedVertices.Min(vertex => vertex.Y);
        double maxLatitude = transformedVertices.Max(vertex => vertex.Y);

        (double west, double south, double east, double north) = PadEnvelope(minLongitude, minLatitude, maxLongitude, maxLatitude, padMeters);
        (west, south, east, north, FetchEnvelopeExpansion expansion) = ExpandToMinimumSide(west, south, east, north, minimumSide);

        Wgs84BoundingBoxAoi fetchEnvelope = new(west, south, east, north);
        return new NormalizedAoi(parcelAoi, fetchEnvelope, parcel, parcelAoi.Buffer, FetchEnvelopeBasis.TransformedParcelEnvelope, margin, expansion);
    }

    /// <summary>
    /// Pads a geographic envelope by <paramref name="padMeters"/>, conservatively: the longitude delta uses
    /// the envelope latitude with the largest absolute value (where a degree of longitude spans the fewest
    /// meters), and the latitude delta unconditionally uses latitude 0 (the equator), so the padded envelope
    /// always over-covers a true geodesic pad, never under-covers it. <see
    /// cref="Wgs84Ellipsoid.MetersPerDegreeLatitude"/> is monotonically increasing in absolute latitude, so
    /// latitude 0 is its global minimum over the entire <c>[-90, 90]</c> domain — not merely over <c>[south,
    /// north]</c> — which is what makes the latitude delta conservative unconditionally: every latitude the
    /// padded edge could possibly cross on its way from <c>south</c> to <c>paddedSouth</c> (or <c>north</c>
    /// to <c>paddedNorth</c>) has a meters-per-degree factor at least as large as the one used to size the
    /// delta, so the true geodesic distance covered is always at least <paramref name="padMeters"/>. Using
    /// each endpoint's own factor instead (as an earlier version of this method did whenever the envelope did
    /// not straddle the equator) is only a *local* approximation — accurate for a small pad, but an
    /// increasingly optimistic one as the pad grows, because the assumed rate does not fall as the padded
    /// edge approaches the equator; it silently under-covers by single-digit meters once the pad reaches
    /// tens of kilometers (confirmed against the actual runtime: about 1.9 m short at a 50 km pad, 7.7 m
    /// short at a 100 km pad; negligible — sub-millimeter or smaller — at the single-meter buffer and margin
    /// sizes this application actually uses). The longitude factor has no equivalent unconditional
    /// simplification: it wants the *largest* absolute latitude in <c>[south, north]</c>, which a convex function like
    /// <c>|latitude|</c> always attains at an endpoint of that specific closed interval, never at a global
    /// constant, so it is still evaluated per envelope.
    /// </summary>
    private static (double West, double South, double East, double North) PadEnvelope(
        double west, double south, double east, double north, double padMeters)
    {
        double latitudeForLongitudeFactor = Math.Abs(south) >= Math.Abs(north) ? south : north;
        return PadAroundLatitudes(west, south, east, north, padMeters, latitudeForLongitudeFactor, latitudeForLatitudeFactor: 0d);
    }

    private static (double West, double South, double East, double North) PadAroundLatitudes(
        double west, double south, double east, double north,
        double padMeters, double latitudeForLongitudeFactor, double latitudeForLatitudeFactor)
    {
        double paddedWest = west;
        double paddedEast = east;
        double paddedSouth = south;
        double paddedNorth = north;

        if (padMeters != 0d)
        {
            double longitudeDelta = padMeters / Wgs84Ellipsoid.MetersPerDegreeLongitude(latitudeForLongitudeFactor);
            double latitudeDelta = padMeters / Wgs84Ellipsoid.MetersPerDegreeLatitude(latitudeForLatitudeFactor);
            paddedWest -= longitudeDelta;
            paddedEast += longitudeDelta;
            paddedSouth -= latitudeDelta;
            paddedNorth += latitudeDelta;
        }

        ThrowIfOutOfRange(
            paddedWest, paddedSouth, paddedEast, paddedNorth,
            $"Padding the fetch envelope by {padMeters.ToString("R", CultureInfo.InvariantCulture)} m");

        return (paddedWest, paddedSouth, paddedEast, paddedNorth);
    }

    /// <summary>
    /// Widens a WGS 84 envelope, symmetrically about its own centre, on whichever of its two axes falls short
    /// of <paramref name="minimumSide"/>: the longitude (east-west) axis is measured, and if necessary
    /// expanded, using the same conservative longitude factor <see cref="PadEnvelope"/> uses (evaluated at
    /// whichever of <paramref name="south"/>/<paramref name="north"/> has the larger absolute value); the
    /// latitude (north-south) axis always uses the equator's factor, exactly like <see cref="PadEnvelope"/>,
    /// for the same unconditional-conservatism reason documented there. Applied after any existing padding
    /// (a margin or a parcel buffer), so it only ever grows an envelope, never shrinks one a caller already
    /// sized larger than the minimum. <see cref="AoiNormalizationOptions.MinimumFetchEnvelopeSide"/> zero
    /// disables this entirely (both measured sides are reported, but the returned bounds are unchanged).
    /// Reuses <see cref="ThrowIfOutOfRange"/> — the identical antimeridian/pole guard <see cref="PadAroundLatitudes"/>
    /// applies — so an envelope that cannot be widened to the minimum without crossing the antimeridian or a
    /// pole fails exactly as an over-large margin or buffer already would.
    /// </summary>
    private static (double West, double South, double East, double North, FetchEnvelopeExpansion Expansion) ExpandToMinimumSide(
        double west, double south, double east, double north, LinearDistance minimumSide)
    {
        double minimumSideMeters = minimumSide.ToMeters();

        double latitudeForLongitudeFactor = Math.Abs(south) >= Math.Abs(north) ? south : north;
        double metersPerDegreeLongitude = Wgs84Ellipsoid.MetersPerDegreeLongitude(latitudeForLongitudeFactor);
        double metersPerDegreeLatitude = Wgs84Ellipsoid.MetersPerDegreeLatitude(0d);

        double widthBeforeMeters = (east - west) * metersPerDegreeLongitude;
        double heightBeforeMeters = (north - south) * metersPerDegreeLatitude;
        LinearDistance widthBefore = LinearDistance.Meters(widthBeforeMeters);
        LinearDistance heightBefore = LinearDistance.Meters(heightBeforeMeters);

        double expandedWest = west;
        double expandedEast = east;
        double expandedSouth = south;
        double expandedNorth = north;
        bool applied = false;

        if (minimumSideMeters > 0d && widthBeforeMeters < minimumSideMeters)
        {
            double halfWidthDegrees = minimumSideMeters / metersPerDegreeLongitude / 2d;
            double centerLongitude = (west + east) / 2d;
            expandedWest = centerLongitude - halfWidthDegrees;
            expandedEast = centerLongitude + halfWidthDegrees;
            applied = true;
        }

        if (minimumSideMeters > 0d && heightBeforeMeters < minimumSideMeters)
        {
            double halfHeightDegrees = minimumSideMeters / metersPerDegreeLatitude / 2d;
            double centerLatitude = (south + north) / 2d;
            expandedSouth = centerLatitude - halfHeightDegrees;
            expandedNorth = centerLatitude + halfHeightDegrees;
            applied = true;
        }

        if (!applied)
        {
            return (west, south, east, north, new FetchEnvelopeExpansion(false, minimumSide, widthBefore, heightBefore, widthBefore, heightBefore));
        }

        ThrowIfOutOfRange(
            expandedWest, expandedSouth, expandedEast, expandedNorth,
            $"Expanding the fetch envelope to a minimum side of {minimumSideMeters.ToString("R", CultureInfo.InvariantCulture)} m");

        double widthAfterMeters = (expandedEast - expandedWest) * metersPerDegreeLongitude;
        double heightAfterMeters = (expandedNorth - expandedSouth) * metersPerDegreeLatitude;
        FetchEnvelopeExpansion expansion = new(
            true, minimumSide, widthBefore, heightBefore, LinearDistance.Meters(widthAfterMeters), LinearDistance.Meters(heightAfterMeters));

        return (expandedWest, expandedSouth, expandedEast, expandedNorth, expansion);
    }

    /// <summary>
    /// The antimeridian/pole guard <see cref="PadAroundLatitudes"/> and <see cref="ExpandToMinimumSide"/> both
    /// apply to a candidate envelope before returning it: SolidGround does not support an area of interest
    /// whose fetch envelope would cross the antimeridian or a pole, regardless of which operation pushed it
    /// there.
    /// </summary>
    private static void ThrowIfOutOfRange(double west, double south, double east, double north, string action)
    {
        if (west < -180d || east > 180d || south < -90d || north > 90d)
        {
            throw new AoiNormalizationException(
                $"{action} would move its bounds to " +
                $"longitude [{GeometryInterop.FormatOrdinate(west)}, {GeometryInterop.FormatOrdinate(east)}] and latitude " +
                $"[{GeometryInterop.FormatOrdinate(south)}, {GeometryInterop.FormatOrdinate(north)}], " +
                "outside the supported range of longitude [-180, 180] and latitude [-90, 90]. SolidGround does not support an " +
                "area of interest this close to the antimeridian or a pole.");
        }
    }
}
