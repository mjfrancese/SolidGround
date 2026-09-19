using System.Globalization;
using SolidGround.Cli.Options;
using SolidGround.Core.Aois;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Processing;

/// <summary>The single area-of-interest form an operator gave, if any. See docs/architecture/cli-workflow.md's "AOI and clip derivation" section.</summary>
internal enum AoiKind
{
    BoundingBox,
    Radius,
    Parcel,
}

/// <summary>
/// The CLI-only value describing whichever single AOI form the operator gave; <see langword="null"/> on
/// <see cref="TerrainProcessingPipeline.RunAsync"/>'s own signature means "no AOI". See
/// docs/architecture/cli-workflow.md's "AOI and clip derivation" section for how <see cref="ClipRegionFactory"/>
/// consumes this value, and its "Options and defaults" section for the option table this type is bound from.
/// Every distance-bearing member is <see cref="LinearDistance"/>, matching the Core signatures these values
/// ultimately reach (<c>Wgs84RadiusAoi</c>, <c>ParcelGeometryAoi</c>, <c>ClipRegion.Circle</c>).
/// </summary>
internal sealed record AoiSelection
{
    internal required AoiKind Kind { get; init; }

    // AoiKind.BoundingBox
    internal double West { get; init; }
    internal double South { get; init; }
    internal double East { get; init; }
    internal double North { get; init; }

    // AoiKind.Radius
    internal double CenterLatitude { get; init; }
    internal double CenterLongitude { get; init; }
    internal LinearDistance Radius { get; init; }

    // AoiKind.Parcel
    internal ParcelGeometryFormat ParcelFormat { get; init; }
    internal string? ParcelText { get; init; }

    // AoiKind.Parcel only; LinearDistance.Zero when --buffer was not given.
    internal LinearDistance Buffer { get; init; }

    /// <summary>
    /// Binds whichever single AOI form <paramref name="invocation"/> gave into an <see cref="AoiSelection"/>,
    /// enforcing the mutual-exclusion and pairing rules docs/architecture/cli-workflow.md's "Options and
    /// defaults" section documents verbatim. Centralized here (rather than duplicated per command) so
    /// `process`, `fetch`, and `run` can never validate or report the same mistake differently -- the same
    /// reasoning that section gives for <see cref="ClipRegionFactory"/> being one shared function rather than
    /// three.
    /// </summary>
    /// <param name="required">True for `fetch`/`run`, where exactly one AOI form is mandatory; false for `process`, where none means "whole grid".</param>
    /// <exception cref="CliUsageException">More than one AOI form was given, a paired option is missing its partner, or a required AOI is entirely absent.</exception>
    internal static AoiSelection? Bind(ParsedInvocation invocation, string verb, bool required)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(verb);

        bool hasBbox = invocation.HasOption("bbox");
        bool hasCenter = invocation.HasOption("center");
        bool hasRadius = invocation.HasOption("radius");
        bool hasParcel = invocation.HasOption("parcel");
        bool hasParcelFormat = invocation.HasOption("parcel-format");
        bool hasBuffer = invocation.HasOption("buffer");

        if (hasCenter != hasRadius)
        {
            throw new CliUsageException("--center and --radius must be given together.");
        }

        bool hasRadiusForm = hasCenter && hasRadius;
        int formCount = (hasBbox ? 1 : 0) + (hasRadiusForm ? 1 : 0) + (hasParcel ? 1 : 0);
        if (formCount > 1)
        {
            throw new CliUsageException("--bbox, --center/--radius, and --parcel are mutually exclusive; give at most one AOI form.");
        }

        if (hasBuffer && !hasParcel)
        {
            throw new CliUsageException("--buffer only applies to --parcel.");
        }

        if (hasParcelFormat && !hasParcel)
        {
            throw new CliUsageException("--parcel-format only applies to --parcel.");
        }

        if (formCount == 0)
        {
            if (required)
            {
                throw new CliUsageException($"{verb} requires exactly one of --bbox, --center/--radius, or --parcel.");
            }

            return null;
        }

        if (hasBbox)
        {
            return BindBoundingBox(invocation.GetValue("bbox")!);
        }

        if (hasRadiusForm)
        {
            return BindRadius(invocation.GetValue("center")!, invocation.GetValue("radius")!);
        }

        return BindParcel(invocation.GetValue("parcel")!, invocation.GetValue("parcel-format"), invocation.GetValue("buffer"));
    }

    private static AoiSelection BindBoundingBox(string text)
    {
        string[] parts = text.Split(',');
        if (parts.Length != 4
            || !TryParseFiniteDouble(parts[0], out double west)
            || !TryParseFiniteDouble(parts[1], out double south)
            || !TryParseFiniteDouble(parts[2], out double east)
            || !TryParseFiniteDouble(parts[3], out double north))
        {
            throw new CliUsageException("--bbox must be exactly four comma-separated finite numbers: west,south,east,north.");
        }

        try
        {
            _ = new Wgs84BoundingBoxAoi(west, south, east, north);
        }
        catch (ArgumentException ex)
        {
            // Never the caught exception's own Message: Core's ArgumentOutOfRangeException embeds the
            // offending coordinate verbatim, which would echo a supplied value into a usage error. See
            // docs/architecture/cli-workflow.md's "Diagnostics and redaction" section.
            throw new CliUsageException(
                "--bbox must have west < east, south < north, and all four values within valid WGS 84 ranges.", ex);
        }

        return new AoiSelection { Kind = AoiKind.BoundingBox, West = west, South = south, East = east, North = north };
    }

    private static AoiSelection BindRadius(string centerText, string radiusText)
    {
        string[] centerParts = centerText.Split(',');
        if (centerParts.Length != 2
            || !TryParseFiniteDouble(centerParts[0], out double latitude)
            || !TryParseFiniteDouble(centerParts[1], out double longitude))
        {
            throw new CliUsageException("--center must be exactly two comma-separated finite numbers: lat,lon.");
        }

        if (!TryParseFiniteDouble(radiusText, out double radiusMeters))
        {
            throw new CliUsageException("--radius must be a finite number.");
        }

        // Stricter than LinearDistance's own >= 0 guard: a zero-radius circle AOI is not a meaningful area of
        // interest, and process's radius clip path (ClipRegion.Circle) never routes through Wgs84RadiusAoi's
        // own > 0 constructor guard, so this option-level check is what actually enforces it there.
        if (radiusMeters <= 0d)
        {
            throw new CliUsageException("--radius must be greater than zero.");
        }

        try
        {
            _ = new Wgs84RadiusAoi(latitude, longitude, LinearDistance.Meters(radiusMeters));
        }
        catch (ArgumentException ex)
        {
            // Never ex.Message -- see the identical reasoning in BindBoundingBox above.
            throw new CliUsageException("--center/--radius must be a valid WGS 84 latitude and longitude with a positive radius.", ex);
        }

        return new AoiSelection
        {
            Kind = AoiKind.Radius,
            CenterLatitude = latitude,
            CenterLongitude = longitude,
            Radius = LinearDistance.Meters(radiusMeters),
        };
    }

    private static AoiSelection BindParcel(string parcelPath, string? parcelFormatText, string? bufferText)
    {
        ParcelGeometryFormat format = ResolveParcelFormat(parcelPath, parcelFormatText);
        string parcelText = ReadOperandFile("--parcel", parcelPath);
        LinearDistance buffer = BindBuffer(bufferText);
        return new AoiSelection { Kind = AoiKind.Parcel, ParcelFormat = format, ParcelText = parcelText, Buffer = buffer };
    }

    private static LinearDistance BindBuffer(string? bufferText)
    {
        if (bufferText is null)
        {
            return LinearDistance.Zero;
        }

        if (!TryParseFiniteDouble(bufferText, out double bufferMeters) || bufferMeters < 0d)
        {
            throw new CliUsageException("--buffer must be a finite number greater than or equal to zero.");
        }

        return LinearDistance.Meters(bufferMeters);
    }

    private static ParcelGeometryFormat ResolveParcelFormat(string parcelPath, string? parcelFormatText)
    {
        if (parcelFormatText is not null)
        {
            return parcelFormatText switch
            {
                "geojson" => ParcelGeometryFormat.GeoJson,
                "wkt" => ParcelGeometryFormat.Wkt,
                _ => throw new CliUsageException("--parcel-format must be one of: geojson, wkt."),
            };
        }

        string extension = Path.GetExtension(parcelPath);
        return extension.ToLowerInvariant() switch
        {
            ".geojson" or ".json" => ParcelGeometryFormat.GeoJson,
            ".wkt" => ParcelGeometryFormat.Wkt,
            _ => throw new CliUsageException(
                $"--parcel '{parcelPath}' has an extension that --parcel-format cannot be inferred from; give --parcel-format explicitly."),
        };
    }

    private static bool TryParseFiniteDouble(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    /// <summary>
    /// Reads an operator-supplied path for `process`/`fetch`/`run` (never `verify`, whose own file-reading
    /// convention is reversed -- see docs/architecture/cli-workflow.md's "Exit codes and error classes"
    /// section), turning an I/O failure into a usage error that names the option and the path.
    /// </summary>
    private static string ReadOperandFile(string optionName, string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliUsageException($"{optionName} '{path}' could not be read.", ex);
        }
    }
}
