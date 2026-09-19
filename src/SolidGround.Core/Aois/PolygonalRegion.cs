using System.Globalization;
using NetTopologySuite;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Aois;

/// <summary>A package-neutral axis-aligned bounding envelope, in the ordinate units of its owning reference.</summary>
public sealed record PlanarEnvelope
{
    public PlanarEnvelope(double minX, double minY, double maxX, double maxY)
    {
        if (!double.IsFinite(minX) || !double.IsFinite(minY) || !double.IsFinite(maxX) || !double.IsFinite(maxY))
        {
            throw new ArgumentOutOfRangeException(nameof(minX), "Envelope ordinates must be finite.");
        }

        if (minX > maxX)
        {
            throw new ArgumentException("MinX must be less than or equal to MaxX.", nameof(minX));
        }

        if (minY > maxY)
        {
            throw new ArgumentException("MinY must be less than or equal to MaxY.", nameof(minY));
        }

        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
    }

    public double MinX { get; }
    public double MinY { get; }
    public double MaxX { get; }
    public double MaxY { get; }
}

/// <summary>
/// A package-neutral projection of one polygon's rings: its exterior shell and zero or more interior holes,
/// each a closed sequence of coordinates in the owning <see cref="PolygonalRegion"/>'s horizontal reference.
/// </summary>
public sealed record PolygonRings
{
    public PolygonRings(IReadOnlyList<Coordinate2D> shell, IReadOnlyList<IReadOnlyList<Coordinate2D>> holes)
    {
        ArgumentNullException.ThrowIfNull(shell);
        ArgumentNullException.ThrowIfNull(holes);

        Shell = shell;
        Holes = holes;
    }

    public IReadOnlyList<Coordinate2D> Shell { get; }
    public IReadOnlyList<IReadOnlyList<Coordinate2D>> Holes { get; }
}

/// <summary>
/// The parcel geometry supplied as WKT or GeoJSON could not be interpreted as valid polygonal geometry.
/// Messages include a feature, polygon, or ring index and a coordinate where available, and never the
/// complete original input text.
/// </summary>
public sealed class ParcelGeometryException : FormatException
{
    public ParcelGeometryException()
    {
    }

    public ParcelGeometryException(string message)
        : base(message)
    {
    }

    public ParcelGeometryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// A validated polygon or multipolygon parcel boundary in a declared horizontal reference. Wraps a
/// NetTopologySuite geometry as its one deliberate, documented seam into a geospatial package; every other
/// member is package-neutral.
/// </summary>
public sealed class PolygonalRegion
{
    private PolygonalRegion(NtsGeometry geometry, HorizontalReference horizontalReference, IReadOnlyList<PolygonRings> polygons)
    {
        Geometry = geometry;
        HorizontalReference = horizontalReference;
        Polygons = polygons;
        var internalEnvelope = geometry.EnvelopeInternal;
        Envelope = new PlanarEnvelope(internalEnvelope.MinX, internalEnvelope.MinY, internalEnvelope.MaxX, internalEnvelope.MaxY);
    }

    /// <summary>
    /// The validated geometry (a <see cref="Polygon"/> or <see cref="MultiPolygon"/>) backing this region,
    /// in X=east/longitude, Y=north/latitude order. This is the deliberate seam a horizontal coordinate
    /// transform (Issue #6) or a clip operation reads and rebuilds from.
    /// </summary>
    public NetTopologySuite.Geometries.Geometry Geometry { get; }

    /// <summary>The horizontal reference <see cref="Geometry"/>'s coordinates are expressed in.</summary>
    public HorizontalReference HorizontalReference { get; }

    /// <summary>The same polygons as <see cref="Geometry"/>, projected into package-neutral rings.</summary>
    public IReadOnlyList<PolygonRings> Polygons { get; }

    /// <summary>
    /// The axis-aligned bounding envelope of <see cref="Geometry"/>, taken from its internal envelope
    /// (<c>Geometry.EnvelopeInternal</c>), not <c>Geometry.Envelope</c>, which returns a geometry rather than
    /// an envelope value.
    /// </summary>
    public PlanarEnvelope Envelope { get; }

    /// <summary>The area of <see cref="Geometry"/>, in the squared units of <see cref="HorizontalReference"/>.</summary>
    public double Area => Geometry.Area;

    /// <summary>The number of polygons in <see cref="Geometry"/> (1 for a <see cref="Polygon"/>).</summary>
    public int PolygonCount => Polygons.Count;

    /// <summary>The total number of interior holes across every polygon in <see cref="Geometry"/>.</summary>
    public int HoleCount => Polygons.Sum(polygon => polygon.Holes.Count);

    /// <summary>
    /// Normalizes <paramref name="geometry"/> into a validated <see cref="PolygonalRegion"/>. This is
    /// Issue #6's seam for an already-reprojected geometry: it accepts a <see cref="Polygon"/>,
    /// <see cref="MultiPolygon"/>, or <see cref="GeometryCollection"/> containing only those (recursively
    /// flattened; any other geometry type, or an empty geometry at any depth, is rejected), requires every
    /// resulting coordinate to be finite, requires geographic-range coordinates when
    /// <paramref name="reference"/>'s kind is geographic, and requires the combined result to pass
    /// <see cref="IsValidOp"/>. It never repairs invalid input.
    /// </summary>
    /// <exception cref="ParcelGeometryException">
    /// <paramref name="geometry"/> contains non-polygonal or empty geometry, a non-finite or out-of-range
    /// coordinate, or fails topology validation.
    /// </exception>
    public static PolygonalRegion FromGeometry(NtsGeometry geometry, HorizontalReference reference)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(reference);

        List<Polygon> flattened = [];
        Flatten(geometry, flattened, "geometry");
        if (flattened.Count == 0)
        {
            throw new ParcelGeometryException("The parcel geometry contains no polygonal rings.");
        }

        GeometryFactory factory = geometry.Factory;
        NtsGeometry combined = flattened.Count == 1
            ? flattened[0]
            : factory.CreateMultiPolygon([.. flattened]);

        bool checkGeographicRange = reference.Kind == HorizontalReferenceKind.Geographic;
        foreach (Coordinate coordinate in combined.Coordinates)
        {
            if (!double.IsFinite(coordinate.X) || !double.IsFinite(coordinate.Y))
            {
                throw new ParcelGeometryException("The parcel geometry contains a non-finite coordinate.");
            }

            if (checkGeographicRange && (coordinate.X is < -180d or > 180d || coordinate.Y is < -90d or > 90d))
            {
                throw new ParcelGeometryException(
                    $"The parcel geometry contains a coordinate ({GeometryInterop.FormatOrdinate(coordinate.X)}, {GeometryInterop.FormatOrdinate(coordinate.Y)}) " +
                    "outside the geographic range of longitude [-180, 180] and latitude [-90, 90].");
            }
        }

        var validity = new IsValidOp(combined);
        if (!validity.IsValid)
        {
            TopologyValidationError error = validity.ValidationError;
            string near = error.Coordinate is Coordinate invalidCoordinate
                ? $" near ({GeometryInterop.FormatOrdinate(invalidCoordinate.X)}, {GeometryInterop.FormatOrdinate(invalidCoordinate.Y)})"
                : string.Empty;
            throw new ParcelGeometryException($"The parcel geometry is not a valid simple polygon: {error.Message}{near}.");
        }

        List<PolygonRings> polygonRings = [.. flattened.Select(ToPolygonRings)];
        return new PolygonalRegion(combined, reference, polygonRings);
    }

    /// <summary>
    /// Recursively descends <paramref name="geometry"/>, adding every <see cref="Polygon"/> found (including
    /// each member of a <see cref="MultiPolygon"/> and every polygonal leaf of a nested
    /// <see cref="GeometryCollection"/>) to <paramref name="output"/>. Rejects any other geometry type and
    /// any empty geometry, at any depth, identifying it by <paramref name="path"/>.
    /// </summary>
    private static void Flatten(NtsGeometry geometry, List<Polygon> output, string path)
    {
        if (geometry.IsEmpty)
        {
            throw new ParcelGeometryException($"The parcel geometry at {path} is empty; SolidGround requires non-empty polygonal geometry.");
        }

        switch (geometry)
        {
            case Polygon polygon:
                output.Add(polygon);
                break;

            case MultiPolygon multiPolygon:
                for (int i = 0; i < multiPolygon.NumGeometries; i++)
                {
                    Flatten(multiPolygon.GetGeometryN(i), output, $"{path}, polygon {i.ToString(CultureInfo.InvariantCulture)}");
                }

                break;

            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    Flatten(collection.GetGeometryN(i), output, $"{path}, member {i.ToString(CultureInfo.InvariantCulture)}");
                }

                break;

            default:
                throw new ParcelGeometryException(
                    $"The parcel geometry at {path} has unsupported type '{geometry.GeometryType}'; only Polygon, " +
                    "MultiPolygon, and GeometryCollection (of polygonal members) are supported.");
        }
    }

    private static PolygonRings ToPolygonRings(Polygon polygon)
    {
        Coordinate2D[] shell = [.. polygon.Shell.Coordinates.Select(ToCoordinate2D)];
        List<IReadOnlyList<Coordinate2D>> holes = [];
        foreach (LinearRing hole in polygon.Holes)
        {
            holes.Add([.. hole.Coordinates.Select(ToCoordinate2D)]);
        }

        return new PolygonRings(shell, holes);
    }

    private static Coordinate2D ToCoordinate2D(Coordinate coordinate) => new(coordinate.X, coordinate.Y);
}

/// <summary>
/// Shared, assembly-internal NetTopologySuite interop helpers, so <see cref="PolygonalRegion"/>,
/// <see cref="ParcelGeometryParser"/>, <see cref="AoiNormalizer"/>, and
/// <see cref="SolidGround.Core.Clipping.ClipRegion"/> do not each redeclare the same
/// <see cref="NtsGeometryServices"/> configuration or coordinate-formatting convention.
/// </summary>
internal static class GeometryInterop
{
    /// <summary>
    /// The single <see cref="NtsGeometryServices"/> configuration (floating precision, SRID 0) used everywhere
    /// this issue constructs NetTopologySuite geometry directly, from WKT, from GeoJSON coordinates, or for a
    /// circular <see cref="SolidGround.Core.Clipping.ClipRegion"/>.
    /// </summary>
    internal static readonly NtsGeometryServices Services = new(new PrecisionModel(PrecisionModels.Floating), 0);

    /// <summary>Formats one coordinate ordinate for an error message: round-trippable, invariant-culture.</summary>
    internal static string FormatOrdinate(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
