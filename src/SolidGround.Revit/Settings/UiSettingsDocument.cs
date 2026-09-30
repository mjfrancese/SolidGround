using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SolidGround.Core.Configuration;
using SolidGround.Core.Processing;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Revit.Settings;

/// <summary>
/// The versioned per-user settings contract. It intentionally contains no credentials and no remembered
/// shared-coordinate opt-in; both values have narrower lifetimes than operator preferences.
/// </summary>
internal sealed record UiSettingsDocument(int SchemaVersion, RevitSettings Settings)
{
    internal const int CurrentSchemaVersion = 1;
}

/// <summary>An immutable decoded draft and the exact target bytes it was based on.</summary>
internal sealed record UiSettingsDraft(RevitSettings Settings, SettingsFileVersion ExpectedVersion, string Path);

/// <summary>Strict serializer and digest-bound store for <see cref="UiSettingsDocument"/>.</summary>
internal static class UiSettingsStore
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions StrictRequestOptions = new(TerrainRequestSettings.JsonOptions)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    internal static bool TryLoad(string path, out UiSettingsDraft? draft, out string? error)
    {
        draft = null;
        error = null;
        SettingsFileSnapshot snapshot;
        try
        {
            snapshot = AtomicSettingsFile.Read(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"Settings at '{path}' could not be read: {ex.Message}";
            return false;
        }

        if (snapshot.Bytes is null)
        {
            draft = new UiSettingsDraft(CreateDefault(), snapshot.Version, path);
            return true;
        }

        try
        {
            draft = new UiSettingsDraft(Decode(snapshot.Bytes, path), snapshot.Version, path);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or ArgumentException)
        {
            // JsonNode's object materializer raises ArgumentException for duplicate property names
            // before Decode can inspect the object. Treat it like every other malformed document:
            // report repair guidance and, crucially, leave the original bytes untouched.
            error = $"Settings at '{path}' need repair: {ex.Message}";
            return false;
        }
    }

    internal static UiSettingsDraft Save(UiSettingsDraft draft, RevitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
        byte[] bytes = Utf8NoBom.GetBytes(Encode(settings));
        SettingsFileVersion version = AtomicSettingsFile.Save(draft.Path, draft.ExpectedVersion, bytes);
        return new UiSettingsDraft(settings, version, draft.Path);
    }

    /// <summary>Validates a proposed document before a companion staged file is published.</summary>
    internal static void ValidateForSave(RevitSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Validate(settings);
    }

    internal static RevitSettings CreateDefault()
    {
        string exports = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SolidGround", "Exports");
        TerrainRequestSettings request = new()
        {
            Mode = TerrainAcquisitionMode.Fetch,
            AreaOfInterest = new AoiSettings
            {
                Kind = AreaOfInterestKind.BoundingBox,
                BoundingBox = new BoundingBoxAoiSettings { West = -1d, South = -1d, East = 1d, North = 1d },
            },
            LocalOrigin = new LocalOriginRequest(LocalOriginKind.Southwest, 0d, 0d, 0d),
            OutputUnit = LengthUnit.UsSurveyFoot,
            Simplification = new SimplificationSettings { Method = SimplificationMethod.CurvatureAware, PointBudget = 15000, CoverageFloorFraction = 0.2d },
            Output = new OutputSettings { Directory = exports, BaseName = "terrain" },
            NetworkTimeoutSeconds = 300,
        };
        return new RevitSettings(
            request,
            new RevitTargetSettings(null, null),
            new RevitSharedCoordinatesSettings(false),
            new RevitAddressAndParcelSettings(AddressGeocoderProvider.Census, null, null, null, null, null, null));
    }

    private static string Encode(RevitSettings settings)
    {
        JsonObject root = new()
        {
            ["schemaVersion"] = UiSettingsDocument.CurrentSchemaVersion,
            ["request"] = JsonSerializer.SerializeToNode(settings.Request, StrictRequestOptions),
            ["levelName"] = settings.Target.LevelName,
            ["toposolidTypeName"] = settings.Target.ToposolidTypeName,
            ["terrainExtensionMeters"] = settings.TerrainExtensionMeters,
            ["distanceDisplayFormat"] = DistanceDisplayFormatTokens.ToSettingsToken(settings.DistanceDisplayFormat),
            ["addressAndParcel"] = new JsonObject
            {
                ["geocoderProvider"] = ProviderToken(settings.AddressAndParcel.GeocoderProvider),
                ["countyRegistryPath"] = settings.AddressAndParcel.CountyRegistryPath,
                ["countyGeoidOverride"] = settings.AddressAndParcel.CountyGeoidOverride,
                ["localParcelFilePath"] = settings.AddressAndParcel.LocalParcelFilePath,
                ["localParcelFileSourceLabel"] = settings.AddressAndParcel.LocalParcelFileSourceLabel,
                ["localParcelFileLicenseDisclaimerText"] = settings.AddressAndParcel.LocalParcelFileLicenseDisclaimerText,
                ["nearbySearchRadiusMeters"] = settings.AddressAndParcel.NearbySearchRadiusMeters,
                ["countyServiceAuthorizedUseAcknowledged"] = settings.AddressAndParcel.CountyServiceAuthorizedUseAcknowledged,
            },
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine;
    }

    private static RevitSettings Decode(byte[] bytes, string path)
    {
        JsonNode? node = JsonNode.Parse(bytes, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow, AllowTrailingCommas = false });
        JsonObject root = node as JsonObject ?? throw new FormatException("The root must be a JSON object.");
        string[] required = ["schemaVersion", "request", "levelName", "toposolidTypeName", "terrainExtensionMeters", "distanceDisplayFormat", "addressAndParcel"];
        foreach (KeyValuePair<string, JsonNode?> property in root)
        {
            if (!required.Contains(property.Key, StringComparer.Ordinal))
            {
                throw new FormatException($"Unknown field '{property.Key}'.");
            }
        }
        foreach (string field in required)
        {
            if (!root.ContainsKey(field)) throw new FormatException($"Missing required field '{field}'.");
        }
        int schemaVersion = root["schemaVersion"]?.GetValue<int>() ?? throw new FormatException("schemaVersion must be an integer.");
        if (schemaVersion != UiSettingsDocument.CurrentSchemaVersion) throw new FormatException($"schemaVersion '{schemaVersion}' is unsupported; expected {UiSettingsDocument.CurrentSchemaVersion}.");
        TerrainRequestSettings request = root["request"]?.Deserialize<TerrainRequestSettings>(StrictRequestOptions) ?? throw new FormatException("request is required.");
        string? level = root["levelName"]?.GetValue<string?>();
        string? type = root["toposolidTypeName"]?.GetValue<string?>();
        double extension = root["terrainExtensionMeters"]?.GetValue<double>() ?? throw new FormatException("terrainExtensionMeters must be a number.");
        string format = root["distanceDisplayFormat"]?.GetValue<string>() ?? throw new FormatException("distanceDisplayFormat must be a string.");
        JsonObject address = root["addressAndParcel"] as JsonObject ?? throw new FormatException("addressAndParcel must be an object.");
        RevitAddressAndParcelSettings addressSettings = DecodeAddress(address);
        RevitSettings settings = new(request, new RevitTargetSettings(level, type), new RevitSharedCoordinatesSettings(false), addressSettings, extension, DistanceDisplayFormatTokens.ParseSettingsToken(format));
        Validate(settings);
        return settings;
    }

    private static RevitAddressAndParcelSettings DecodeAddress(JsonObject address)
    {
        string[] fields = ["geocoderProvider", "countyRegistryPath", "countyGeoidOverride", "localParcelFilePath", "localParcelFileSourceLabel", "localParcelFileLicenseDisclaimerText", "nearbySearchRadiusMeters", "countyServiceAuthorizedUseAcknowledged"];
        foreach (KeyValuePair<string, JsonNode?> property in address)
        {
            if (!fields.Contains(property.Key, StringComparer.Ordinal)) throw new FormatException($"Unknown addressAndParcel field '{property.Key}'.");
        }
        foreach (string field in fields) if (!address.ContainsKey(field)) throw new FormatException($"Missing required addressAndParcel field '{field}'.");
        string provider = address["geocoderProvider"]?.GetValue<string>() ?? throw new FormatException("addressAndParcel.geocoderProvider must be a string.");
        double? radius = address["nearbySearchRadiusMeters"]?.GetValue<double?>();
        bool acknowledged = address["countyServiceAuthorizedUseAcknowledged"]?.GetValue<bool>() ?? throw new FormatException("addressAndParcel.countyServiceAuthorizedUseAcknowledged must be a boolean.");
        return new RevitAddressAndParcelSettings(ParseProvider(provider), address["countyRegistryPath"]?.GetValue<string?>(), address["countyGeoidOverride"]?.GetValue<string?>(), address["localParcelFilePath"]?.GetValue<string?>(), address["localParcelFileSourceLabel"]?.GetValue<string?>(), address["localParcelFileLicenseDisclaimerText"]?.GetValue<string?>(), radius, acknowledged);
    }

    private static void Validate(RevitSettings settings)
    {
        if (!double.IsFinite(settings.TerrainExtensionMeters) || settings.TerrainExtensionMeters < 0d) throw new FormatException("terrainExtensionMeters must be finite and nonnegative.");
        if (settings.AddressAndParcel.NearbySearchRadiusMeters is { } radius && (!double.IsFinite(radius) || radius <= 0d)) throw new FormatException("addressAndParcel.nearbySearchRadiusMeters must be finite and positive.");
        IReadOnlyList<string> problems = settings.Request.Validate();
        if (problems.Count > 0) throw new FormatException(string.Join(Environment.NewLine, problems));
    }

    private static string ProviderToken(AddressGeocoderProvider provider) => provider switch
    {
        AddressGeocoderProvider.Census => "census", AddressGeocoderProvider.Geocodio => "geocodio", AddressGeocoderProvider.Esri => "esri", _ => throw new ArgumentOutOfRangeException(nameof(provider)),
    };
    private static AddressGeocoderProvider ParseProvider(string provider) => provider switch
    {
        "census" => AddressGeocoderProvider.Census, "geocodio" => AddressGeocoderProvider.Geocodio, "esri" => AddressGeocoderProvider.Esri, _ => throw new FormatException($"addressAndParcel.geocoderProvider '{provider}' is not recognized."),
    };
}
