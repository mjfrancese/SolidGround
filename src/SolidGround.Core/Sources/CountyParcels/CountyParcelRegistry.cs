using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SolidGround.Core.Sources.CountyParcels;

/// <summary>The county-supplied field name for each surfaced <see cref="ParcelBoundaryCandidate"/> attribute. Every non-null value must name a field on the county's own ArcGIS layer, never an owner/mailing field.</summary>
public sealed record CountyParcelFieldMap
{
    public required string ParcelId { get; init; }

    /// <summary>Also the field the address-substring query runs against: one mapping, not two, so the searched field and the displayed field can never drift apart.</summary>
    public required string SitusAddress { get; init; }

    public string? Subdivision { get; init; }
    public string? Lot { get; init; }
    public string? Block { get; init; }
    public string? Plat { get; init; }
    public string? Book { get; init; }
    public string? Page { get; init; }
    public string? LegalDescription { get; init; }

    /// <summary>Assumed to already be in acres; a county reporting the equivalent field in square feet is a known, documented limitation.</summary>
    public string? ReportedAcres { get; init; }

    public string? Zoning { get; init; }
    public string? StableParcelId { get; init; }
}

/// <summary>One county's ArcGIS parcel service registration.</summary>
public sealed record CountyParcelRegistryEntry
{
    /// <summary>The 5-digit Census county GEOID.</summary>
    public required string Geoid { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>The ArcGIS REST MapServer/FeatureServer base URL, with no trailing slash or layer segment. A plain string (validated by hand in <see cref="CountyParcelRegistry.Load"/>), consistent with <c>ParcelAoiSettings.Path</c>'s own plain-string style.</summary>
    public required string ServiceBaseUrl { get; init; }

    public required int LayerIndex { get; init; }
    public required CountyParcelFieldMap FieldMap { get; init; }

    /// <summary>The county service's own license/disclaimer/no-warranty text, surfaced verbatim on every candidate this entry produces.</summary>
    public required string LicenseDisclaimerText { get; init; }
}

/// <summary>The decoded shape of a county parcel registry JSON file.</summary>
public sealed record CountyParcelRegistryDocument
{
    public required int SchemaVersion { get; init; }
    public required IReadOnlyList<CountyParcelRegistryEntry> Counties { get; init; }
}

/// <summary>A county parcel registry file could not be read, was not valid JSON for the documented format, or failed one of its documented content rules.</summary>
public sealed class CountyParcelRegistryFormatException : FormatException
{
    public CountyParcelRegistryFormatException()
    {
    }

    public CountyParcelRegistryFormatException(string message)
        : base(message)
    {
    }

    public CountyParcelRegistryFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Loads a machine-local JSON registry of county ArcGIS parcel services, keyed by 5-digit Census county GEOID.
/// Ships with zero built-in entries: every real county a host adds lives only in the machine-local file at a
/// path the host supplies, never committed to this repository. See docs/architecture/parcel-boundary-sources.md
/// for the full JSON schema and "how a user adds their own county."
/// </summary>
public sealed class CountyParcelRegistry
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    // \A/\z (not ^/$): .NET's `$` also matches immediately before a single trailing '\n' even without
    // RegexOptions.Multiline, so "12345\n" would otherwise pass this pattern, get stored as a corrupted
    // dictionary key, and later fail an ordinary GEOID lookup with a misleading "unregistered" error instead
    // of being rejected here as documented ("a geoid is not exactly 5 ASCII digits").
    private static readonly Regex GeoidPattern = new(@"\A\d{5}\z");

    private CountyParcelRegistry(string sourcePath, IReadOnlyDictionary<string, CountyParcelRegistryEntry> entriesByGeoid)
    {
        SourcePath = sourcePath;
        EntriesByGeoid = entriesByGeoid;
    }

    /// <summary>The path this registry was loaded from, for an actionable error message.</summary>
    public string SourcePath { get; }

    public IReadOnlyDictionary<string, CountyParcelRegistryEntry> EntriesByGeoid { get; }

    /// <exception cref="CountyParcelRegistryFormatException">
    /// The file could not be read; is not valid JSON per the strict decode options (unknown property at any
    /// level, wrong shape); <c>schemaVersion</c> is not <see cref="CurrentSchemaVersion"/>; <c>counties</c> is
    /// empty; a <c>geoid</c> is not exactly 5 ASCII digits; a <c>geoid</c> is duplicated; a
    /// <c>serviceBaseUrl</c> does not parse as an absolute URI, its scheme is not exactly <c>https</c>, it ends
    /// with a trailing slash, or its final path segment already looks like a <c>layerIndex</c>; a
    /// <c>layerIndex</c> is negative; a <c>fieldMap.parcelId</c>/<c>situsAddress</c> is blank; any non-null
    /// <c>fieldMap</c> value is owner-like per <see cref="OwnerFieldNameGuard"/>; or <c>displayName</c>/
    /// <c>licenseDisclaimerText</c> is blank. Every problem is collected -- this method never stops at the
    /// first one found -- and joined into one message.
    /// </exception>
    public static CountyParcelRegistry Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CountyParcelRegistryFormatException($"The county parcel registry file '{path}' could not be read: {ex.Message}", ex);
        }

        CountyParcelRegistryDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<CountyParcelRegistryDocument>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new CountyParcelRegistryFormatException(
                $"The county parcel registry file '{path}' is not valid JSON for the documented registry format: {ex.Message}", ex);
        }

        if (document is null)
        {
            throw new CountyParcelRegistryFormatException($"The county parcel registry file '{path}' decoded to a null document.");
        }

        List<string> problems = [];
        if (document.SchemaVersion != CurrentSchemaVersion)
        {
            problems.Add($"schemaVersion must be {CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture)}, but was {document.SchemaVersion.ToString(CultureInfo.InvariantCulture)}.");
        }

        if (document.Counties.Count == 0)
        {
            problems.Add("counties must contain at least one entry.");
        }

        Dictionary<string, CountyParcelRegistryEntry> entriesByGeoid = new(StringComparer.Ordinal);
        for (int i = 0; i < document.Counties.Count; i++)
        {
            CountyParcelRegistryEntry entry = document.Counties[i];
            ValidateEntry(entry, i, problems);
            if (GeoidPattern.IsMatch(entry.Geoid) && !entriesByGeoid.TryAdd(entry.Geoid, entry))
            {
                problems.Add($"counties[{i.ToString(CultureInfo.InvariantCulture)}]: geoid '{entry.Geoid}' is a duplicate of an earlier entry.");
            }
        }

        if (problems.Count > 0)
        {
            throw new CountyParcelRegistryFormatException(
                $"The county parcel registry file '{path}' has {problems.Count.ToString(CultureInfo.InvariantCulture)} problem(s):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        }

        return new CountyParcelRegistry(path, entriesByGeoid);
    }

    private static void ValidateEntry(CountyParcelRegistryEntry entry, int index, List<string> problems)
    {
        string prefix = $"counties[{index.ToString(CultureInfo.InvariantCulture)}]";

        if (!GeoidPattern.IsMatch(entry.Geoid))
        {
            problems.Add($"{prefix}: geoid '{entry.Geoid}' must be exactly 5 ASCII digits.");
        }

        if (string.IsNullOrWhiteSpace(entry.DisplayName))
        {
            problems.Add($"{prefix}: displayName is required.");
        }

        bool isAbsoluteHttps = Uri.TryCreate(entry.ServiceBaseUrl, UriKind.Absolute, out Uri? serviceUri)
            && string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        if (!isAbsoluteHttps)
        {
            problems.Add($"{prefix}: serviceBaseUrl must be an absolute https URL.");
        }
        else if (serviceUri!.AbsolutePath.EndsWith('/'))
        {
            problems.Add($"{prefix}: serviceBaseUrl must not end with a trailing slash.");
        }
        else if (HasTrailingLayerIndexSegment(serviceUri))
        {
            problems.Add($"{prefix}: serviceBaseUrl must not already include a layer segment; layerIndex is appended separately.");
        }

        if (entry.LayerIndex < 0)
        {
            problems.Add($"{prefix}: layerIndex must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(entry.LicenseDisclaimerText))
        {
            problems.Add($"{prefix}: licenseDisclaimerText is required.");
        }

        ValidateFieldMap(entry.FieldMap, prefix, problems);
    }

    /// <summary>True when <paramref name="serviceUri"/>'s last non-empty path segment already parses as a non-negative integer -- the shape of an already-appended <c>layerIndex</c> that <see cref="CountyParcelRegistrySource.BuildLayerQueryPathAndFixedParameters"/> would otherwise append a second time.</summary>
    private static bool HasTrailingLayerIndexSegment(Uri serviceUri)
    {
        string[] segments = serviceUri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length > 0 && int.TryParse(segments[^1], NumberStyles.None, CultureInfo.InvariantCulture, out _);
    }

    private static void ValidateFieldMap(CountyParcelFieldMap fieldMap, string prefix, List<string> problems)
    {
        if (string.IsNullOrWhiteSpace(fieldMap.ParcelId))
        {
            problems.Add($"{prefix}.fieldMap: parcelId is required.");
        }

        if (string.IsNullOrWhiteSpace(fieldMap.SitusAddress))
        {
            problems.Add($"{prefix}.fieldMap: situsAddress is required.");
        }

        CheckOwnerLike(fieldMap.ParcelId, "parcelId", prefix, problems);
        CheckOwnerLike(fieldMap.SitusAddress, "situsAddress", prefix, problems);
        CheckOwnerLike(fieldMap.Subdivision, "subdivision", prefix, problems);
        CheckOwnerLike(fieldMap.Lot, "lot", prefix, problems);
        CheckOwnerLike(fieldMap.Block, "block", prefix, problems);
        CheckOwnerLike(fieldMap.Plat, "plat", prefix, problems);
        CheckOwnerLike(fieldMap.Book, "book", prefix, problems);
        CheckOwnerLike(fieldMap.Page, "page", prefix, problems);
        CheckOwnerLike(fieldMap.LegalDescription, "legalDescription", prefix, problems);
        CheckOwnerLike(fieldMap.ReportedAcres, "reportedAcres", prefix, problems);
        CheckOwnerLike(fieldMap.Zoning, "zoning", prefix, problems);
        CheckOwnerLike(fieldMap.StableParcelId, "stableParcelId", prefix, problems);
    }

    private static void CheckOwnerLike(string? value, string propertyName, string prefix, List<string> problems)
    {
        if (!string.IsNullOrWhiteSpace(value) && OwnerFieldNameGuard.IsOwnerLike(value))
        {
            problems.Add($"{prefix}.fieldMap.{propertyName}: '{value}' looks like an owner/mailing field name, which SolidGround never maps.");
        }
    }
}
