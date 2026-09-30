using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// A display-only legal and terrain preview in a local metre plane. It is not a source-grid projection,
/// a terrain-processing input, or a statement of actual terrain-boundary accuracy. The local plane uses WGS 84
/// ellipsoid metres-per-degree at the legal envelope centre only to make an immediately visible outline; the
/// actual terrain boundary remains the raster-CRS result derived after download.
/// </summary>
public sealed record ParcelTerrainPreview
{
    private static readonly HorizontalReference LocalMetricDisplayReference = new(
        "SolidGround:parcel-terrain-preview-local-metric",
        "WGS 84 local display approximation",
        HorizontalReferenceKind.Projected,
        HorizontalUnit.Linear(LengthUnit.Meter),
        HorizontalAxisOrder.EastingNorthing);

    private readonly double legalCenterLongitude;
    private readonly double legalCenterLatitude;
    private readonly double metersPerLongitudeDegree;
    private readonly double metersPerLatitudeDegree;

    private ParcelTerrainPreview(
        PolygonalRegion legalRegion,
        PolygonalRegion terrainRegion,
        double legalCenterLongitude,
        double legalCenterLatitude,
        double metersPerLongitudeDegree,
        double metersPerLatitudeDegree)
    {
        LegalRegion = legalRegion;
        TerrainRegion = terrainRegion;
        LegalPolygonCount = legalRegion.PolygonCount;
        LegalHoleCount = legalRegion.HoleCount;
        TerrainPolygonCount = terrainRegion.PolygonCount;
        TerrainHoleCount = terrainRegion.HoleCount;
        TopologyChangeCount = Math.Abs(TerrainPolygonCount - LegalPolygonCount) + Math.Abs(TerrainHoleCount - LegalHoleCount);
        this.legalCenterLongitude = legalCenterLongitude;
        this.legalCenterLatitude = legalCenterLatitude;
        this.metersPerLongitudeDegree = metersPerLongitudeDegree;
        this.metersPerLatitudeDegree = metersPerLatitudeDegree;
    }

    /// <summary>The unbuffered legal region in the local metre display plane.</summary>
    public PolygonalRegion LegalRegion { get; }

    /// <summary>The terrain-only buffered region in the same local metre display plane.</summary>
    public PolygonalRegion TerrainRegion { get; }

    /// <summary>The number of legal polygons visible in <see cref="LegalRegion"/>.</summary>
    public int LegalPolygonCount { get; }

    /// <summary>The number of legal holes visible in <see cref="LegalRegion"/>.</summary>
    public int LegalHoleCount { get; }

    /// <summary>The number of terrain-preview polygons visible in <see cref="TerrainRegion"/>.</summary>
    public int TerrainPolygonCount { get; }

    /// <summary>The number of terrain-preview holes visible in <see cref="TerrainRegion"/>.</summary>
    public int TerrainHoleCount { get; }

    /// <summary>The absolute polygon and hole count changes caused by the display-only terrain margin.</summary>
    public int TopologyChangeCount { get; }

    /// <summary>
    /// Maps a WGS 84 longitude-latitude coordinate into this preview's local metre plane, using the fixed
    /// legal-envelope-centre scale. It is suitable only for drawing this preview, never source acquisition,
    /// raster clipping, world-coordinate reconstruction, or a geodetic measurement.
    /// </summary>
    public Coordinate2D ToDisplay(Coordinate2D longitudeLatitude)
    {
        Wgs84BoundingBoxAoi.ValidateLongitude(longitudeLatitude.X, nameof(longitudeLatitude));
        Wgs84BoundingBoxAoi.ValidateLatitude(longitudeLatitude.Y, nameof(longitudeLatitude));
        return new Coordinate2D(
            (longitudeLatitude.X - legalCenterLongitude) * metersPerLongitudeDegree,
            (longitudeLatitude.Y - legalCenterLatitude) * metersPerLatitudeDegree);
    }

    /// <summary>
    /// Builds display-only local metre geometry from a WGS 84 (EPSG:4326, longitude-latitude) legal region
    /// and a terrain-only margin. The buffer uses <see cref="GridClipper.ResolveEffectiveRegion"/> in the
    /// local plane; it does not guess a source CRS or feed terrain processing.
    /// </summary>
    public static ParcelTerrainPreview Build(PolygonalRegion legalWgs84, LinearDistance terrainMargin)
    {
        ArgumentNullException.ThrowIfNull(legalWgs84);
        ValidateWgs84LegalRegion(legalWgs84);

        PlanarEnvelope envelope = legalWgs84.Envelope;
        double centerLongitude = (envelope.MinX + envelope.MaxX) / 2d;
        double centerLatitude = (envelope.MinY + envelope.MaxY) / 2d;
        double longitudeScale = Wgs84Ellipsoid.MetersPerDegreeLongitude(centerLatitude);
        double latitudeScale = Wgs84Ellipsoid.MetersPerDegreeLatitude(centerLatitude);
        if (!double.IsFinite(longitudeScale) || longitudeScale <= 0d || !double.IsFinite(latitudeScale) || latitudeScale <= 0d)
        {
            throw new ArgumentException(
                "A parcel terrain preview requires a legal envelope centre with finite local WGS 84 metre scales.",
                nameof(legalWgs84));
        }

        LocalMetricDisplayTransform transform = new(
            legalWgs84.HorizontalReference,
            centerLongitude,
            centerLatitude,
            longitudeScale,
            latitudeScale);
        PolygonalRegion legalDisplay = PolygonalRegionReprojection.Reproject(
            legalWgs84,
            transform,
            HorizontalTransformDirection.Forward);
        PolygonalRegion terrainDisplay = GridClipper.ResolveEffectiveRegion(
            ClipRegion.FromRegion(legalDisplay, terrainMargin));

        return new ParcelTerrainPreview(
            legalDisplay,
            terrainDisplay,
            centerLongitude,
            centerLatitude,
            longitudeScale,
            latitudeScale);
    }

    private static void ValidateWgs84LegalRegion(PolygonalRegion legalWgs84)
    {
        HorizontalReference reference = legalWgs84.HorizontalReference;
        if (reference.Kind != HorizontalReferenceKind.Geographic
            || reference.AxisOrder != HorizontalAxisOrder.LongitudeLatitude
            || !string.Equals(reference.CoordinateReferenceSystem, "EPSG:4326", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A parcel terrain preview requires a WGS 84 (EPSG:4326) longitude-latitude legal region.",
                nameof(legalWgs84));
        }
    }

    private sealed class LocalMetricDisplayTransform : IHorizontalCoordinateTransform
    {
        private readonly double centerLongitude;
        private readonly double centerLatitude;
        private readonly double longitudeScale;
        private readonly double latitudeScale;

        public LocalMetricDisplayTransform(
            HorizontalReference sourceReference,
            double centerLongitude,
            double centerLatitude,
            double longitudeScale,
            double latitudeScale)
        {
            this.centerLongitude = centerLongitude;
            this.centerLatitude = centerLatitude;
            this.longitudeScale = longitudeScale;
            this.latitudeScale = latitudeScale;
            CoordinateOperationDefinition operation = new(
                "SolidGround display approximation",
                "WGS 84 ellipsoid metres-per-degree at the legal envelope centre");
            Definition = new HorizontalTransformationDefinition(
                sourceReference,
                LocalMetricDisplayReference,
                operation,
                operation,
                "SolidGround display approximation",
                "1");
        }

        public HorizontalTransformationDefinition Definition { get; }

        public Coordinate2D Forward(Coordinate2D source) => new(
            (source.X - centerLongitude) * longitudeScale,
            (source.Y - centerLatitude) * latitudeScale);

        public Coordinate2D Inverse(Coordinate2D target) => new(
            (target.X / longitudeScale) + centerLongitude,
            (target.Y / latitudeScale) + centerLatitude);
    }
}
