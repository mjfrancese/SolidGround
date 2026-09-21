using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolidGround.Core.Exports;
using SolidGround.Core.Metadata;
using SolidGround.Core.Simplification;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>Which of `fetch` (live OpenTopography) or `process` (local `.asc`/`.prj`, optional source sidecar) a run performs.</summary>
public enum TerrainAcquisitionMode
{
    Fetch,
    Process,
}

/// <summary>Which of the three area-of-interest forms an <see cref="AoiSettings"/> value carries.</summary>
public enum AreaOfInterestKind
{
    BoundingBox,
    Radius,
    Parcel,
}

/// <summary><c>areaOfInterest.boundingBox</c>: constructs a real <c>Wgs84BoundingBoxAoi</c>; its own range/ordering guards apply.</summary>
public sealed record BoundingBoxAoiSettings
{
    public required double West { get; init; }
    public required double South { get; init; }
    public required double East { get; init; }
    public required double North { get; init; }
}

/// <summary><c>areaOfInterest.radius</c>: constructs a real <c>Wgs84RadiusAoi</c>; <see cref="RadiusMeters"/> must be positive.</summary>
public sealed record RadiusAoiSettings
{
    public required double CenterLatitude { get; init; }
    public required double CenterLongitude { get; init; }
    public required double RadiusMeters { get; init; }
}

/// <summary>
/// <c>areaOfInterest.parcel</c>: constructs a real <c>ParcelGeometryAoi</c> once <see cref="Path"/>'s contents
/// are read (a Preflight, file-system concern -- never read by <see cref="TerrainRequestSettings.Validate"/>
/// itself). <see cref="Format"/> is the literal documented token (<c>"geojson"</c> or <c>"wkt"</c>), not a C#
/// enum bound through <see cref="TerrainRequestSettings.JsonOptions"/>'s shared <see cref="JsonStringEnumConverter"/>:
/// that converter's <see cref="JsonNamingPolicy.CamelCase"/> would decode a <c>GeoJson</c> enum member as
/// <c>"geoJson"</c>, not the documented all-lowercase <c>"geojson"</c>. <see cref="AoiSettingsFactory"/> maps
/// this token by hand instead, mirroring the CLI's own <c>AoiSelection.ResolveParcelFormat</c>.
/// </summary>
public sealed record ParcelAoiSettings
{
    public required string Path { get; init; }
    public string? Format { get; init; }
    public double BufferMeters { get; init; }
}

/// <summary><c>areaOfInterest</c>: exactly the sub-object matching <see cref="Kind"/> may be non-null.</summary>
public sealed record AoiSettings
{
    public required AreaOfInterestKind Kind { get; init; }
    public BoundingBoxAoiSettings? BoundingBox { get; init; }
    public RadiusAoiSettings? Radius { get; init; }
    public ParcelAoiSettings? Parcel { get; init; }
}

/// <summary>
/// <c>process</c>: required when <see cref="TerrainRequestSettings.Mode"/> is
/// <see cref="TerrainAcquisitionMode.Process"/>; ignored (may be present or absent either way) when it is
/// <see cref="TerrainAcquisitionMode.Fetch"/>. Every path here is checked for existence only at Preflight,
/// never by <see cref="TerrainRequestSettings.Validate"/>.
/// </summary>
public sealed record ProcessInputSettings
{
    public required string Asc { get; init; }
    public string? Prj { get; init; }
    public string? SourceJson { get; init; }
    public string? SourceName { get; init; }
    public string? Dataset { get; init; }
    public string? VerticalDatum { get; init; }
    public LengthUnit? VerticalUnit { get; init; }
    public string? Geoid { get; init; }
    public string? CollectionStart { get; init; }
    public string? CollectionEnd { get; init; }
    public string? QualityLevel { get; init; }
}

/// <summary><c>simplification</c>: <see cref="Method"/> may not be <see cref="SimplificationMethod.TinError"/> -- not implemented by <c>GridTerrainSimplifier</c>, and deliberately not a documented template choice.</summary>
public sealed record SimplificationSettings
{
    public required SimplificationMethod Method { get; init; }
    public required int PointBudget { get; init; }
    public required double CoverageFloorFraction { get; init; }
}

/// <summary><c>output</c>: the export bundle's destination directory and base name.</summary>
public sealed record OutputSettings
{
    public required string Directory { get; init; }
    public required string BaseName { get; init; }
}

/// <summary>
/// The complete host-neutral settings contract <c>SolidGround.Revit</c>'s own <c>RevitSettings</c> wraps
/// (adding only the Revit-only <c>level</c>/<c>toposolidType</c> name overrides). See SolidGround Issue #15's
/// design record §3/§4 for the exact JSON schema and §4.2 for <see cref="JsonOptions"/>'s strict-decode
/// contract.
/// </summary>
public sealed record TerrainRequestSettings
{
    public required TerrainAcquisitionMode Mode { get; init; }
    public required AoiSettings AreaOfInterest { get; init; }
    public ProcessInputSettings? Process { get; init; }
    public required LocalOriginRequest LocalOrigin { get; init; }
    public required LengthUnit OutputUnit { get; init; }
    public required SimplificationSettings Simplification { get; init; }
    public required OutputSettings Output { get; init; }
    public int NetworkTimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// The one <see cref="JsonSerializerOptions"/> instance every decode of this record (and
    /// <c>SolidGround.Revit</c>'s own <c>RevitSettings</c> wrapper) uses: camelCase property names, unknown
    /// members rejected at every nesting level, <c>//</c> comments and trailing commas tolerated (so the
    /// shipped template can carry both), and every enum-typed field (<see cref="Mode"/>, <see cref="OutputUnit"/>,
    /// <see cref="LocalOriginRequest.Kind"/>, <see cref="SimplificationSettings.Method"/>,
    /// <see cref="ProcessInputSettings.VerticalUnit"/>) bound directly from its documented camelCase token.
    /// Core-hosted (not inline inside <c>SolidGround.Revit</c>'s settings I/O) so a Core-only test can decode
    /// the shipped template's exact bytes without Revit.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// A placeholder WGS 84 geographic reference used only to exercise <c>ParcelGeometryAoi</c>'s constructor
    /// during <see cref="Validate"/>, before any real reference is available (Preflight builds the real one
    /// later, only once this method has already accepted the settings). Never used for an actual coordinate
    /// computation.
    /// </summary>
    private static readonly HorizontalReference PlaceholderWgs84Reference = new(
        "WGS 84", "World Geodetic System 1984", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    /// <summary>A placeholder, non-blank stand-in for a parcel's actual geometry text, which <see cref="Validate"/> never itself reads or parses (a file-system/Preflight concern).</summary>
    private const string PlaceholderParcelGeometryText = "SOLIDGROUND_VALIDATE_PLACEHOLDER";

    /// <summary>
    /// Reports every problem this settings value has -- never short-circuiting on the first one found, and
    /// never itself reading a file (existence, sidecar, and parcel-geometry checks are all Preflight
    /// concerns). Reuses the real <c>Wgs84BoundingBoxAoi</c>/<c>Wgs84RadiusAoi</c>/<c>ParcelGeometryAoi</c>
    /// (via <see cref="AoiSettingsFactory.Build"/>) and <see cref="TerrainExportBaseName.Validate"/>
    /// constructors rather than re-deriving their rules.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        List<string> problems = [];

        if (!Enum.IsDefined(Mode))
        {
            problems.Add("mode has an unrecognized value.");
        }

        if (AreaOfInterest is null)
        {
            // `required` on a reference-typed property only guarantees the JSON key was present, not
            // non-null (System.Text.Json accepts an explicit `null` for a `required` reference-typed
            // property because JsonOptions does not set RespectNullableAnnotations); guard every such
            // section explicitly, mirroring ValidateProcess's own Process-is-null pattern, so an
            // explicit-null section is reported here rather than throwing NullReferenceException out of
            // this documented never-throws method (SolidGround Issue #15 Stage 2 review fix).
            problems.Add("areaOfInterest is required.");
        }
        else
        {
            ValidateAreaOfInterest(problems);
        }

        if (Mode == TerrainAcquisitionMode.Process)
        {
            ValidateProcess(problems);
        }

        if (!Enum.IsDefined(OutputUnit))
        {
            problems.Add("outputUnit has an unrecognized value.");
        }

        if (LocalOrigin is null)
        {
            problems.Add("localOrigin is required.");
        }
        else if (!double.IsFinite(LocalOrigin.X) || !double.IsFinite(LocalOrigin.Y) || !double.IsFinite(LocalOrigin.Z))
        {
            problems.Add("localOrigin.x, localOrigin.y, and localOrigin.z must all be finite.");
        }

        if (Simplification is null)
        {
            problems.Add("simplification is required.");
        }
        else
        {
            ValidateSimplification(problems);
        }

        if (Output is null)
        {
            problems.Add("output is required.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(Output.Directory))
            {
                problems.Add("output.directory is required.");
            }

            try
            {
                TerrainExportBaseName.Validate(Output.BaseName, nameof(Output.BaseName));
            }
            catch (ArgumentException)
            {
                problems.Add("output.baseName is not a valid export base name.");
            }
        }

        if (NetworkTimeoutSeconds <= 0)
        {
            problems.Add("networkTimeoutSeconds must be positive.");
        }

        return problems;
    }

    private void ValidateAreaOfInterest(List<string> problems)
    {
        if (!Enum.IsDefined(AreaOfInterest.Kind))
        {
            problems.Add("areaOfInterest.kind has an unrecognized value.");
            return;
        }

        bool boundingBoxGiven = AreaOfInterest.BoundingBox is not null;
        bool radiusGiven = AreaOfInterest.Radius is not null;
        bool parcelGiven = AreaOfInterest.Parcel is not null;
        bool matchingSubObjectGiven;

        switch (AreaOfInterest.Kind)
        {
            case AreaOfInterestKind.BoundingBox:
                matchingSubObjectGiven = boundingBoxGiven;
                if (!boundingBoxGiven)
                {
                    problems.Add("areaOfInterest.boundingBox is required when areaOfInterest.kind is 'boundingBox'.");
                }

                if (radiusGiven)
                {
                    problems.Add("areaOfInterest.radius must be null when areaOfInterest.kind is 'boundingBox'.");
                }

                if (parcelGiven)
                {
                    problems.Add("areaOfInterest.parcel must be null when areaOfInterest.kind is 'boundingBox'.");
                }

                break;

            case AreaOfInterestKind.Radius:
                matchingSubObjectGiven = radiusGiven;
                if (boundingBoxGiven)
                {
                    problems.Add("areaOfInterest.boundingBox must be null when areaOfInterest.kind is 'radius'.");
                }

                if (!radiusGiven)
                {
                    problems.Add("areaOfInterest.radius is required when areaOfInterest.kind is 'radius'.");
                }

                if (parcelGiven)
                {
                    problems.Add("areaOfInterest.parcel must be null when areaOfInterest.kind is 'radius'.");
                }

                break;

            case AreaOfInterestKind.Parcel:
                matchingSubObjectGiven = parcelGiven;
                if (boundingBoxGiven)
                {
                    problems.Add("areaOfInterest.boundingBox must be null when areaOfInterest.kind is 'parcel'.");
                }

                if (radiusGiven)
                {
                    problems.Add("areaOfInterest.radius must be null when areaOfInterest.kind is 'parcel'.");
                }

                if (!parcelGiven)
                {
                    problems.Add("areaOfInterest.parcel is required when areaOfInterest.kind is 'parcel'.");
                }

                break;

            default:
                matchingSubObjectGiven = false;
                break;
        }

        if (!matchingSubObjectGiven)
        {
            return;
        }

        try
        {
            _ = AoiSettingsFactory.Build(AreaOfInterest, PlaceholderWgs84Reference, PlaceholderParcelGeometryText);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            problems.Add($"areaOfInterest.{SubObjectName(AreaOfInterest.Kind)} has an invalid value.");
        }
    }

    private static string SubObjectName(AreaOfInterestKind kind) => kind switch
    {
        AreaOfInterestKind.BoundingBox => "boundingBox",
        AreaOfInterestKind.Radius => "radius",
        AreaOfInterestKind.Parcel => "parcel",
        _ => "kind",
    };

    private void ValidateProcess(List<string> problems)
    {
        if (Process is null)
        {
            problems.Add("process is required when mode is 'process'.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Process.Asc))
        {
            problems.Add("process.asc is required when mode is 'process'.");
        }

        ValidateNonBlankIfGiven(Process.SourceName, "process.sourceName", problems);
        ValidateNonBlankIfGiven(Process.Dataset, "process.dataset", problems);
        ValidateNonBlankIfGiven(Process.VerticalDatum, "process.verticalDatum", problems);
        ValidateNonBlankIfGiven(Process.Geoid, "process.geoid", problems);
        ValidateNonBlankIfGiven(Process.QualityLevel, "process.qualityLevel", problems);

        if (Process.VerticalUnit is { } verticalUnit && !Enum.IsDefined(verticalUnit))
        {
            problems.Add("process.verticalUnit has an unrecognized value.");
        }

        string? collectionStartText = Process.CollectionStart;
        string? collectionEndText = Process.CollectionEnd;
        bool startGiven = collectionStartText is not null;
        bool endGiven = collectionEndText is not null;
        if (startGiven != endGiven)
        {
            problems.Add("process.collectionStart and process.collectionEnd must both be given or both be omitted.");
        }
        else if (startGiven && endGiven)
        {
            bool startValid = DateOnly.TryParseExact(collectionStartText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start);
            bool endValid = DateOnly.TryParseExact(collectionEndText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly end);
            if (!startValid)
            {
                problems.Add("process.collectionStart must be a yyyy-MM-dd date.");
            }

            if (!endValid)
            {
                problems.Add("process.collectionEnd must be a yyyy-MM-dd date.");
            }

            if (startValid && endValid && start > end)
            {
                problems.Add("process.collectionStart must not be after process.collectionEnd.");
            }
        }
    }

    private static void ValidateNonBlankIfGiven(string? value, string fieldName, List<string> problems)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{fieldName} must not be blank when given.");
        }
    }

    private void ValidateSimplification(List<string> problems)
    {
        if (!Enum.IsDefined(Simplification.Method))
        {
            problems.Add("simplification.method has an unrecognized value.");
        }
        else if (Simplification.Method == SimplificationMethod.TinError)
        {
            problems.Add("simplification.method must be 'curvatureAware' or 'uniformSampler'; 'tinError' is not implemented.");
        }

        if (Simplification.PointBudget is < 1 or > 50_000)
        {
            problems.Add("simplification.pointBudget must be between 1 and 50000 inclusive.");
        }

        if (!double.IsFinite(Simplification.CoverageFloorFraction) || Simplification.CoverageFloorFraction is < 0d or > 1d)
        {
            problems.Add("simplification.coverageFloorFraction must be between 0 and 1 inclusive.");
        }
    }
}
