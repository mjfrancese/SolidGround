using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// Builds the real <c>AreaOfInterest</c> an <see cref="AoiSettings"/> value describes, reusing
/// <c>Wgs84BoundingBoxAoi</c>/<c>Wgs84RadiusAoi</c>/<c>ParcelGeometryAoi</c>'s own constructors (and their
/// validation) rather than re-deriving the rules -- exactly the constructions
/// <c>SolidGround.Cli.Processing.AoiSelection.ToAreaOfInterest</c> already performs for the CLI's own flag
/// surface. Factored out of what would otherwise be inline construction inside <c>SolidGround.Revit</c>'s
/// Preflight stage so it is directly, Revit-free testable; also reused by
/// <see cref="TerrainRequestSettings.Validate"/> itself (with placeholder inputs) to validate an AOI's shape
/// before any file is read.
/// </summary>
public static class AoiSettingsFactory
{
    /// <summary>
    /// Builds the <see cref="AreaOfInterest"/> matching <paramref name="settings"/>'s own
    /// <see cref="AoiSettings.Kind"/>, reading only that one form's sub-object -- the other two, even if
    /// non-null, are ignored. <paramref name="wgs84Reference"/> and <paramref name="parcelGeometryText"/> are
    /// used only for <see cref="AreaOfInterestKind.Parcel"/>; <paramref name="parcelGeometryText"/> is the
    /// parcel file's already-read contents (never read by this method itself).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="settings"/> or <paramref name="wgs84Reference"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">
    /// The sub-object matching <see cref="AoiSettings.Kind"/> is <see langword="null"/>;
    /// <paramref name="parcelGeometryText"/> is <see langword="null"/> for a <see cref="AreaOfInterestKind.Parcel"/>
    /// kind; or a parcel's own <see cref="ParcelAoiSettings.Format"/> is neither <see langword="null"/>,
    /// <c>"geojson"</c>, nor <c>"wkt"</c>, and could not be inferred from <see cref="ParcelAoiSettings.Path"/>'s
    /// extension either.
    /// </exception>
    /// <exception cref="ArgumentException">A sub-object's own numeric fields fail its real AOI type's constructor guards.</exception>
    public static AreaOfInterest Build(AoiSettings settings, HorizontalReference wgs84Reference, string? parcelGeometryText)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(wgs84Reference);

        return settings.Kind switch
        {
            AreaOfInterestKind.BoundingBox => BuildBoundingBox(settings.BoundingBox),
            AreaOfInterestKind.Radius => BuildRadius(settings.Radius),
            AreaOfInterestKind.Parcel => BuildParcel(settings.Parcel, wgs84Reference, parcelGeometryText),
            _ => throw new FormatException($"areaOfInterest.kind '{settings.Kind}' is not recognized."),
        };
    }

    private static Wgs84BoundingBoxAoi BuildBoundingBox(BoundingBoxAoiSettings? settings)
    {
        if (settings is null)
        {
            throw new FormatException("areaOfInterest.boundingBox is required when areaOfInterest.kind is 'boundingBox'.");
        }

        return new Wgs84BoundingBoxAoi(settings.West, settings.South, settings.East, settings.North);
    }

    private static Wgs84RadiusAoi BuildRadius(RadiusAoiSettings? settings)
    {
        if (settings is null)
        {
            throw new FormatException("areaOfInterest.radius is required when areaOfInterest.kind is 'radius'.");
        }

        return new Wgs84RadiusAoi(settings.CenterLatitude, settings.CenterLongitude, LinearDistance.Meters(settings.RadiusMeters));
    }

    private static ParcelGeometryAoi BuildParcel(ParcelAoiSettings? settings, HorizontalReference wgs84Reference, string? parcelGeometryText)
    {
        if (settings is null)
        {
            throw new FormatException("areaOfInterest.parcel is required when areaOfInterest.kind is 'parcel'.");
        }

        if (string.IsNullOrWhiteSpace(settings.Path))
        {
            // Guards both an explicit-null Path (the same required-reference-type-property gap
            // ValidateAreaOfInterest's null guards close elsewhere) and a blank Path given alongside an
            // explicit Format -- ResolveParcelFormat's extension-inference branch below is not the only
            // path that reads Path, and a blank value is never a valid parcel source either way
            // (SolidGround Issue #15 Stage 2 review fix).
            throw new FormatException("areaOfInterest.parcel.path is required.");
        }

        if (parcelGeometryText is null)
        {
            throw new FormatException("areaOfInterest.parcel's geometry text is required (its file's contents, read by the caller).");
        }

        ParcelGeometryFormat format = ResolveParcelFormat(settings);
        LinearDistance buffer = LinearDistance.Meters(settings.BufferMeters);
        return new ParcelGeometryAoi(format, parcelGeometryText, wgs84Reference, buffer);
    }

    /// <summary>
    /// Mirrors the CLI's own <c>AoiSelection.ResolveParcelFormat</c>: an explicit token wins; otherwise the
    /// format is inferred from <see cref="ParcelAoiSettings.Path"/>'s extension.
    /// </summary>
    private static ParcelGeometryFormat ResolveParcelFormat(ParcelAoiSettings settings)
    {
        if (settings.Format is not null)
        {
            return settings.Format switch
            {
                "geojson" => ParcelGeometryFormat.GeoJson,
                "wkt" => ParcelGeometryFormat.Wkt,
                _ => throw new FormatException("areaOfInterest.parcel.format must be one of: geojson, wkt."),
            };
        }

        string extension = Path.GetExtension(settings.Path);
        return extension.ToLowerInvariant() switch
        {
            ".geojson" or ".json" => ParcelGeometryFormat.GeoJson,
            ".wkt" => ParcelGeometryFormat.Wkt,
            _ => throw new FormatException(
                $"areaOfInterest.parcel.path '{settings.Path}' has an extension areaOfInterest.parcel.format cannot be inferred from; give areaOfInterest.parcel.format explicitly."),
        };
    }
}
