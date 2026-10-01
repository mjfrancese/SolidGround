using System.Text.Json;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Units;

namespace SolidGround.Core.Provenance;

/// <summary>The operator-selected meaning of the terrain's vertical placement.</summary>
public enum FloorReferenceMode
{
    KnownElevation,
    EstimatedGradeRise,
    ProvisionalGround,
}

/// <summary>A durable Revit-level identity and its actual project-plane elevation in internal international feet.</summary>
public sealed record TargetProjectLevel
{
    public TargetProjectLevel(long id, string uniqueId, string name, double projectElevationInternal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uniqueId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!double.IsFinite(projectElevationInternal)) throw new ArgumentOutOfRangeException(nameof(projectElevationInternal));
        Id = id;
        UniqueId = uniqueId;
        Name = name;
        ProjectElevationInternal = projectElevationInternal;
    }

    public long Id { get; }
    public string UniqueId { get; }
    public string Name { get; }
    public double ProjectElevationInternal { get; }
}

/// <summary>The operator's selected coordinate, retained in both display WGS 84 and authoritative projected coordinates.</summary>
public sealed record GroundPoint
{
    public GroundPoint(double wgs84Longitude, double wgs84Latitude, Coordinate2D projectedPoint)
    {
        if (!double.IsFinite(wgs84Longitude) || wgs84Longitude is < -180d or > 180d ||
            !double.IsFinite(wgs84Latitude) || wgs84Latitude is < -90d or > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(wgs84Longitude), "The ground point must be a valid WGS 84 longitude/latitude coordinate.");
        }

        Wgs84Longitude = wgs84Longitude;
        Wgs84Latitude = wgs84Latitude;
        ProjectedPoint = projectedPoint;
    }

    public double Wgs84Longitude { get; }
    public double Wgs84Latitude { get; }
    public Coordinate2D ProjectedPoint { get; }
}

/// <summary>An explicitly entered, nonnegative distance and its above/below-grade direction.</summary>
public sealed record MeasuredRise
{
    public MeasuredRise(double magnitude, LengthUnit unit, bool belowGrade)
    {
        if (!double.IsFinite(magnitude) || magnitude < 0d) throw new ArgumentOutOfRangeException(nameof(magnitude));
        _ = LengthConverter.MetersPerUnit(unit);
        Magnitude = magnitude;
        Unit = unit;
        BelowGrade = belowGrade;
    }

    public double Magnitude { get; }
    public LengthUnit Unit { get; }
    public bool BelowGrade { get; }
}

/// <summary>
/// Immutable operator evidence for a terrain floor reference. Physical floor height is deliberately separate
/// from the local frame's origin, which additionally accounts for the chosen target level's project plane.
/// </summary>
public sealed record BuildingFloorReference
{
    private const string LegalMedianPolicy = "legal-valid-cell-median-v1";
    private const string AoiMedianPolicy = "aoi-valid-cell-median-v1";

    private BuildingFloorReference(
        FloorReferenceMode mode, VerticalReference sourceReference, TargetProjectLevel targetLevel,
        double? physicalFloorElevation, GroundPoint? selectedGroundPoint, ElevationGridSample? groundSample,
        MeasuredRise? rise, string? knownSourceDescription, string? provisionalPolicy, double? provisionalGroundElevation,
        BuildingOutlineProvenance? buildingOutline)
    {
        ArgumentNullException.ThrowIfNull(sourceReference);
        ArgumentNullException.ThrowIfNull(targetLevel);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (physicalFloorElevation is double floor && !double.IsFinite(floor)) throw new ArgumentOutOfRangeException(nameof(physicalFloorElevation));
        if (provisionalGroundElevation is double provisional && !double.IsFinite(provisional)) throw new ArgumentOutOfRangeException(nameof(provisionalGroundElevation));

        Mode = mode;
        SourceReference = sourceReference;
        TargetLevel = targetLevel;
        PhysicalFloorElevation = physicalFloorElevation;
        SelectedGroundPoint = selectedGroundPoint;
        GroundSample = groundSample;
        Rise = rise;
        KnownSourceDescription = knownSourceDescription;
        ProvisionalPolicy = provisionalPolicy;
        ProvisionalGroundElevation = provisionalGroundElevation;
        BuildingOutline = buildingOutline;
    }

    public FloorReferenceMode Mode { get; }
    public VerticalReference SourceReference { get; }
    public TargetProjectLevel TargetLevel { get; }
    /// <summary>Physical H_F in <see cref="SourceReference"/>'s native vertical unit; null for a provisional preview.</summary>
    public double? PhysicalFloorElevation { get; }
    public GroundPoint? SelectedGroundPoint { get; }
    public ElevationGridSample? GroundSample { get; }
    public MeasuredRise? Rise { get; }
    public string? KnownSourceDescription { get; }
    public string? ProvisionalPolicy { get; }
    public double? ProvisionalGroundElevation { get; }
    public BuildingOutlineProvenance? BuildingOutline { get; }

    public static BuildingFloorReference KnownElevation(
        double physicalFloorElevation, VerticalReference sourceReference, TargetProjectLevel targetLevel, string knownSourceDescription,
        BuildingOutlineProvenance? buildingOutline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(knownSourceDescription);
        return new(FloorReferenceMode.KnownElevation, sourceReference, targetLevel, physicalFloorElevation,
            null, null, null, knownSourceDescription, null, null, buildingOutline);
    }

    public static BuildingFloorReference EstimatedGradeRise(
        GroundPoint selectedGroundPoint, ElevationGridSample groundSample, MeasuredRise rise, TargetProjectLevel targetLevel,
        BuildingOutlineProvenance? buildingOutline = null)
    {
        ArgumentNullException.ThrowIfNull(selectedGroundPoint);
        ArgumentNullException.ThrowIfNull(groundSample);
        ArgumentNullException.ThrowIfNull(rise);
        ArgumentNullException.ThrowIfNull(targetLevel);
        ValidateSample(groundSample);
        double signedRise = LengthConverter.Convert(rise.Magnitude, rise.Unit, groundSample.SourceReference.Unit);
        if (rise.BelowGrade) signedRise = -signedRise;
        return new(FloorReferenceMode.EstimatedGradeRise, groundSample.SourceReference, targetLevel,
            groundSample.Elevation + signedRise, selectedGroundPoint, groundSample, rise, null, null, null, buildingOutline);
    }

    public static BuildingFloorReference ProvisionalGround(
        double provisionalGroundElevation, VerticalReference sourceReference, TargetProjectLevel targetLevel, string provisionalPolicy,
        BuildingOutlineProvenance? buildingOutline = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionalPolicy);
        if (!string.Equals(provisionalPolicy, LegalMedianPolicy, StringComparison.Ordinal) &&
            !string.Equals(provisionalPolicy, AoiMedianPolicy, StringComparison.Ordinal))
        {
            throw new ArgumentException("The provisional ground policy is not recognized.", nameof(provisionalPolicy));
        }

        return new(FloorReferenceMode.ProvisionalGround, sourceReference, targetLevel, null,
            null, null, null, null, provisionalPolicy, provisionalGroundElevation, buildingOutline);
    }

    /// <summary>Returns physical H_F, or the explicitly labelled ground-median reference for provisional mode.</summary>
    public double ResolveFrameReferenceElevation() => Mode == FloorReferenceMode.ProvisionalGround
        ? ProvisionalGroundElevation!.Value
        : PhysicalFloorElevation!.Value;

    internal static string LegalValidCellMedianPolicy => LegalMedianPolicy;
    internal static string AoiValidCellMedianPolicy => AoiMedianPolicy;

    private static void ValidateSample(ElevationGridSample sample)
    {
        if (!double.IsFinite(sample.Elevation) || sample.Support is null || sample.Support.Count == 0 ||
            sample.Support.Any(cell => !double.IsFinite(cell.Elevation) || !double.IsFinite(cell.Weight) || cell.Weight <= 0d))
        {
            throw new ArgumentException("The estimated floor reference needs a finite ground sample with nonzero valid support.", nameof(sample));
        }

        double totalWeight = sample.Support.Sum(cell => cell.Weight);
        if (Math.Abs(totalWeight - 1d) > ElevationGridSampler.IndexTolerance)
        {
            throw new ArgumentException("The ground sample support weights must sum to one.", nameof(sample));
        }
    }
}

/// <summary>Strict, deterministic JSON encoding used by both portable exports and native provenance fields.</summary>
public static class BuildingFloorReferenceJson
{
    public static string Serialize(BuildingFloorReference? record)
    {
        if (record is null) return "null";
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            Write(writer, record);
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static BuildingFloorReference? Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        JsonElement root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Null) return null;
        Dictionary<string, JsonElement> values = Properties(root,
            ["mode", "sourceReference", "targetLevel", "physicalFloorElevation", "selectedGroundPoint", "groundSample", "rise", "knownSourceDescription", "provisionalPolicy", "provisionalGroundElevation", "buildingOutline"]);
        FloorReferenceMode mode = EnumValue<FloorReferenceMode>(values["mode"]);
        VerticalReference sourceReference = Vertical(values["sourceReference"]);
        TargetProjectLevel target = Target(values["targetLevel"]);
        double? physical = NullableDouble(values["physicalFloorElevation"]);
        GroundPoint? point = Point(values["selectedGroundPoint"]);
        ElevationGridSample? sample = Sample(values["groundSample"]);
        MeasuredRise? rise = Rise(values["rise"]);
        string? knownDescription = NullableString(values["knownSourceDescription"]);
        string? policy = NullableString(values["provisionalPolicy"]);
        double? provisional = NullableDouble(values["provisionalGroundElevation"]);
        BuildingOutlineProvenance? outline = BuildingOutlineProvenanceJson.Read(values["buildingOutline"]);

        return mode switch
        {
            FloorReferenceMode.KnownElevation when physical is double height && point is null && sample is null && rise is null && policy is null && provisional is null =>
                BuildingFloorReference.KnownElevation(height, sourceReference, target, knownDescription ?? throw new FormatException("Known floor reference needs a source description."), outline),
            FloorReferenceMode.EstimatedGradeRise when physical is double height && point is not null && sample is not null && rise is not null && knownDescription is null && policy is null && provisional is null =>
                ParseEstimated(point, sample, rise, target, sourceReference, height, outline),
            FloorReferenceMode.ProvisionalGround when physical is null && point is null && sample is null && rise is null && knownDescription is null && policy is not null && provisional is double elevation =>
                BuildingFloorReference.ProvisionalGround(elevation, sourceReference, target, policy, outline),
            _ => throw new FormatException("Building floor reference JSON has fields incompatible with its mode."),
        };
    }

    private static BuildingFloorReference ParseEstimated(GroundPoint point, ElevationGridSample sample, MeasuredRise rise, TargetProjectLevel target, VerticalReference sourceReference, double serializedFloor, BuildingOutlineProvenance? outline)
    {
        if (sample.SourceReference != sourceReference) throw new FormatException("Estimated floor reference JSON has inconsistent source references.");
        BuildingFloorReference parsed = BuildingFloorReference.EstimatedGradeRise(point, sample, rise, target, outline);
        if (BitConverter.DoubleToInt64Bits(parsed.PhysicalFloorElevation!.Value) != BitConverter.DoubleToInt64Bits(serializedFloor))
        {
            throw new FormatException("Estimated floor reference JSON has an inconsistent physical floor elevation.");
        }
        return parsed;
    }

    private static Dictionary<string, JsonElement> Properties(JsonElement value, IReadOnlyCollection<string> expected)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new FormatException("Building floor reference JSON object expected.");
        Dictionary<string, JsonElement> result = new(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject()) if (!result.TryAdd(property.Name, property.Value)) throw new FormatException("Building floor reference JSON has a duplicate property.");
        if (result.Count != expected.Count || expected.Any(name => !result.ContainsKey(name)) || result.Keys.Any(name => !expected.Contains(name, StringComparer.Ordinal))) throw new FormatException("Building floor reference JSON has an unexpected property set.");
        return result;
    }
    private static string String(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw new FormatException("Building floor reference JSON string expected.");
    private static string? NullableString(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : String(value);
    private static double Number(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number) && double.IsFinite(number) ? number : throw new FormatException("Building floor reference JSON finite number expected.");
    private static double? NullableDouble(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : Number(value);
    private static int Integer(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number) ? number : throw new FormatException("Building floor reference JSON integer expected.");
    private static long Long(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long number) ? number : throw new FormatException("Building floor reference JSON integer expected.");
    private static T EnumValue<T>(JsonElement value) where T : struct, Enum { string text = String(value); return Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) ? Enum.Parse<T>(text, false) : throw new FormatException("Building floor reference JSON enum value is not recognized."); }
    private static Coordinate2D Coordinate(JsonElement value) { Dictionary<string, JsonElement> fields = Properties(value, ["x", "y"]); return new Coordinate2D(Number(fields["x"]), Number(fields["y"])); }
    private static VerticalReference Vertical(JsonElement value) { Dictionary<string, JsonElement> fields = Properties(value, ["datum", "unit", "geoidModel"]); return new VerticalReference(String(fields["datum"]), EnumValue<LengthUnit>(fields["unit"]), NullableString(fields["geoidModel"])); }
    private static TargetProjectLevel Target(JsonElement value) { Dictionary<string, JsonElement> fields = Properties(value, ["id", "uniqueId", "name", "projectElevationInternal"]); return new TargetProjectLevel(Long(fields["id"]), String(fields["uniqueId"]), String(fields["name"]), Number(fields["projectElevationInternal"])); }
    private static GroundPoint? Point(JsonElement value) { if (value.ValueKind == JsonValueKind.Null) return null; Dictionary<string, JsonElement> fields = Properties(value, ["wgs84Longitude", "wgs84Latitude", "projectedPoint"]); return new GroundPoint(Number(fields["wgs84Longitude"]), Number(fields["wgs84Latitude"]), Coordinate(fields["projectedPoint"])); }
    private static MeasuredRise? Rise(JsonElement value) { if (value.ValueKind == JsonValueKind.Null) return null; Dictionary<string, JsonElement> fields = Properties(value, ["magnitude", "unit", "belowGrade"]); if (fields["belowGrade"].ValueKind is not JsonValueKind.True and not JsonValueKind.False) throw new FormatException("Building floor reference JSON Boolean expected."); return new MeasuredRise(Number(fields["magnitude"]), EnumValue<LengthUnit>(fields["unit"]), fields["belowGrade"].GetBoolean()); }
    private static ElevationGridSample? Sample(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        Dictionary<string, JsonElement> fields = Properties(value, ["projectedPoint", "elevation", "sourceReference", "method", "support"]);
        if (!string.Equals(String(fields["method"]), "strict-cell-center-bilinear-v1", StringComparison.Ordinal)) throw new FormatException("Building floor reference JSON sampling method is not recognized.");
        if (fields["support"].ValueKind != JsonValueKind.Array) throw new FormatException("Building floor reference JSON support array expected.");
        List<ElevationGridSupportCell> support = [];
        foreach (JsonElement item in fields["support"].EnumerateArray()) { Dictionary<string, JsonElement> cell = Properties(item, ["row", "column", "center", "elevation", "weight"]); support.Add(new ElevationGridSupportCell(Integer(cell["row"]), Integer(cell["column"]), Coordinate(cell["center"]), Number(cell["elevation"]), Number(cell["weight"]))); }
        return new ElevationGridSample(Coordinate(fields["projectedPoint"]), Number(fields["elevation"]), Vertical(fields["sourceReference"]), support.AsReadOnly());
    }
    internal static BuildingOutlineProvenance? ReadOutline(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null) return null;
        Dictionary<string, JsonElement> fields = Properties(value, ["provider", "release", "licenseIdentifier", "licenseUrl", "licenseText", "attribution", "manifestSha256", "tileKeys", "retrievedDate"]);
        if (fields["tileKeys"].ValueKind != JsonValueKind.Array) throw new FormatException("Building outline tile keys must be an array.");
        IReadOnlyList<string> keys = fields["tileKeys"].EnumerateArray().Select(String).ToArray();
        if (!DateOnly.TryParseExact(String(fields["retrievedDate"]), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateOnly date)) throw new FormatException("Building outline retrieved date is invalid.");
        if (!Uri.TryCreate(String(fields["licenseUrl"]), UriKind.Absolute, out Uri? licenseUrl)) throw new FormatException("Building outline license URL is invalid.");
        return new BuildingOutlineProvenance(String(fields["provider"]), String(fields["release"]), String(fields["licenseIdentifier"]), licenseUrl, String(fields["licenseText"]), String(fields["attribution"]), String(fields["manifestSha256"]), keys, date);
    }

    internal static void Write(Utf8JsonWriter writer, BuildingFloorReference? record)
    {
        if (record is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject();
        writer.WriteString("mode", record.Mode.ToString());
        writer.WritePropertyName("sourceReference"); WriteVerticalReference(writer, record.SourceReference);
        writer.WritePropertyName("targetLevel");
        writer.WriteStartObject(); writer.WriteNumber("id", record.TargetLevel.Id); writer.WriteString("uniqueId", record.TargetLevel.UniqueId); writer.WriteString("name", record.TargetLevel.Name); writer.WriteNumber("projectElevationInternal", record.TargetLevel.ProjectElevationInternal); writer.WriteEndObject();
        WriteNullableNumber(writer, "physicalFloorElevation", record.PhysicalFloorElevation);
        writer.WritePropertyName("selectedGroundPoint"); WriteGroundPoint(writer, record.SelectedGroundPoint);
        writer.WritePropertyName("groundSample"); WriteGroundSample(writer, record.GroundSample);
        writer.WritePropertyName("rise"); WriteRise(writer, record.Rise);
        WriteNullableString(writer, "knownSourceDescription", record.KnownSourceDescription);
        WriteNullableString(writer, "provisionalPolicy", record.ProvisionalPolicy);
        WriteNullableNumber(writer, "provisionalGroundElevation", record.ProvisionalGroundElevation);
        writer.WritePropertyName("buildingOutline"); BuildingOutlineProvenanceJson.Write(writer, record.BuildingOutline);
        writer.WriteEndObject();
    }

    private static void WriteVerticalReference(Utf8JsonWriter writer, VerticalReference reference)
    {
        writer.WriteStartObject(); writer.WriteString("datum", reference.Datum); writer.WriteString("unit", reference.Unit.ToString()); writer.WriteString("geoidModel", reference.GeoidModel); writer.WriteEndObject();
    }
    private static void WriteGroundPoint(Utf8JsonWriter writer, GroundPoint? point)
    {
        if (point is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject(); writer.WriteNumber("wgs84Longitude", point.Wgs84Longitude); writer.WriteNumber("wgs84Latitude", point.Wgs84Latitude); writer.WritePropertyName("projectedPoint"); WriteCoordinate(writer, point.ProjectedPoint); writer.WriteEndObject();
    }
    private static void WriteGroundSample(Utf8JsonWriter writer, ElevationGridSample? sample)
    {
        if (sample is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject(); writer.WritePropertyName("projectedPoint"); WriteCoordinate(writer, sample.ProjectedPoint); writer.WriteNumber("elevation", sample.Elevation); writer.WritePropertyName("sourceReference"); WriteVerticalReference(writer, sample.SourceReference); writer.WriteString("method", "strict-cell-center-bilinear-v1"); writer.WritePropertyName("support"); writer.WriteStartArray();
        foreach (ElevationGridSupportCell cell in sample.Support) { writer.WriteStartObject(); writer.WriteNumber("row", cell.Row); writer.WriteNumber("column", cell.Column); writer.WritePropertyName("center"); WriteCoordinate(writer, cell.Center); writer.WriteNumber("elevation", cell.Elevation); writer.WriteNumber("weight", cell.Weight); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject();
    }
    private static void WriteRise(Utf8JsonWriter writer, MeasuredRise? rise)
    {
        if (rise is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject(); writer.WriteNumber("magnitude", rise.Magnitude); writer.WriteString("unit", rise.Unit.ToString()); writer.WriteBoolean("belowGrade", rise.BelowGrade); writer.WriteEndObject();
    }
    internal static void WriteOutline(Utf8JsonWriter writer, BuildingOutlineProvenance? outline)
    {
        if (outline is null) { writer.WriteNullValue(); return; }
        writer.WriteStartObject(); writer.WriteString("provider", outline.Provider); writer.WriteString("release", outline.Release); writer.WriteString("licenseIdentifier", outline.LicenseIdentifier); writer.WriteString("licenseUrl", outline.LicenseUrl.AbsoluteUri); writer.WriteString("licenseText", outline.LicenseText); writer.WriteString("attribution", outline.Attribution); writer.WriteString("manifestSha256", outline.ManifestSha256); writer.WritePropertyName("tileKeys"); writer.WriteStartArray(); foreach (string key in outline.TileKeys) writer.WriteStringValue(key); writer.WriteEndArray(); writer.WriteString("retrievedDate", outline.DateOnlyRetrievedDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)); writer.WriteEndObject();
    }
    private static void WriteCoordinate(Utf8JsonWriter writer, Coordinate2D coordinate) { writer.WriteStartObject(); writer.WriteNumber("x", coordinate.X); writer.WriteNumber("y", coordinate.Y); writer.WriteEndObject(); }
    private static void WriteNullableNumber(Utf8JsonWriter writer, string name, double? value) { if (value is double number) writer.WriteNumber(name, number); else writer.WriteNull(name); }
    private static void WriteNullableString(Utf8JsonWriter writer, string name, string? value) { if (value is null) writer.WriteNull(name); else writer.WriteString(name, value); }
}

/// <summary>Strict, canonical JSON for standalone building-outline acquisition attribution.</summary>
public static class BuildingOutlineProvenanceJson
{
    public static string Serialize(BuildingOutlineProvenance? record)
    {
        if (record is null) return "null";
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream))
        {
            Write(writer, record);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    public static BuildingOutlineProvenance? Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
        return Read(document.RootElement);
    }

    internal static BuildingOutlineProvenance? Read(JsonElement value) => BuildingFloorReferenceJson.ReadOutline(value);

    internal static void Write(Utf8JsonWriter writer, BuildingOutlineProvenance? record) => BuildingFloorReferenceJson.WriteOutline(writer, record);

    /// <summary>Compares the complete persisted outline context, including retrieval date and every license field.</summary>
    public static bool HasSameValue(BuildingOutlineProvenance? left, BuildingOutlineProvenance? right) =>
        string.Equals(Serialize(left), Serialize(right), StringComparison.Ordinal);
}
