using SolidGround.Core.Aois;

namespace SolidGround.Core.Sources.BuildingOutlines;

/// <summary>Retrieves optional, display-only building outlines for a WGS 84 viewport.</summary>
public interface IBuildingOutlineSource
{
    Task<BuildingOutlineAcquisition> GetAsync(Wgs84BoundingBoxAoi bounds, CancellationToken token = default);
}

/// <summary>
/// The result of an optional outline lookup. A non-null <see cref="UnavailabilityReason"/> is a safe,
/// operator-facing explanation and always accompanies an empty outline list. An empty list with no reason
/// means the bounded source completed successfully and found no visible polygons.
/// </summary>
public sealed record BuildingOutlineAcquisition
{
    public BuildingOutlineAcquisition(
        IReadOnlyList<PolygonalRegion> outlines,
        BuildingOutlineProvenance provenance,
        string? unavailabilityReason = null)
    {
        ArgumentNullException.ThrowIfNull(outlines);
        ArgumentNullException.ThrowIfNull(provenance);
        if (outlines.Any(outline => outline is null))
        {
            throw new ArgumentException("Building outlines cannot contain null geometry.", nameof(outlines));
        }

        if (unavailabilityReason is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(unavailabilityReason);
            if (outlines.Count != 0)
            {
                throw new ArgumentException("An unavailable building-outline result cannot contain partial geometry.", nameof(outlines));
            }
        }

        Outlines = Array.AsReadOnly(outlines.ToArray());
        Provenance = provenance;
        UnavailabilityReason = unavailabilityReason;
    }

    public IReadOnlyList<PolygonalRegion> Outlines { get; }
    public BuildingOutlineProvenance Provenance { get; }
    public string? UnavailabilityReason { get; }
}

/// <summary>
/// Immutable source metadata for a building-outline lookup. It intentionally holds release-level metadata and
/// coarse L9 tile identities only: it never contains a location query, source tile URL, address, parcel id,
/// feature property, height, or door inference.
/// </summary>
public sealed record BuildingOutlineProvenance
{
    public BuildingOutlineProvenance(
        string provider,
        string release,
        string licenseIdentifier,
        Uri licenseUrl,
        string licenseText,
        string attribution,
        string manifestSha256,
        IReadOnlyList<string> tileKeys,
        DateOnly? dateOnlyRetrievedDate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(release);
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseIdentifier);
        ArgumentNullException.ThrowIfNull(licenseUrl);
        if (!licenseUrl.IsAbsoluteUri || licenseUrl.Scheme != Uri.UriSchemeHttps || licenseUrl.UserInfo.Length != 0 || licenseUrl.Query.Length != 0 || licenseUrl.Fragment.Length != 0)
        {
            throw new ArgumentException("The license URL must be an absolute HTTPS URL without credentials, query, or fragment.", nameof(licenseUrl));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(licenseText);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribution);
        if (manifestSha256.Length != 64 || !manifestSha256.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("The manifest digest must be a 64-character SHA-256 hex value.", nameof(manifestSha256));
        }

        ArgumentNullException.ThrowIfNull(tileKeys);
        foreach (string key in tileKeys)
        {
            if (!Level9QuadKey.IsValid(key))
            {
                throw new ArgumentException("Every building-outline tile key must be a level-9 quadkey.", nameof(tileKeys));
            }
        }

        Provider = provider;
        Release = release;
        LicenseIdentifier = licenseIdentifier;
        LicenseUrl = licenseUrl;
        LicenseText = licenseText;
        Attribution = attribution;
        ManifestSha256 = manifestSha256.ToUpperInvariant();
        TileKeys = Array.AsReadOnly(tileKeys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal).ToArray());
        DateOnlyRetrievedDate = dateOnlyRetrievedDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
    }

    public string Provider { get; }
    public string Release { get; }
    public string LicenseIdentifier { get; }
    public Uri LicenseUrl { get; }
    public string LicenseText { get; }
    public string Attribution { get; }
    public string ManifestSha256 { get; }
    public IReadOnlyList<string> TileKeys { get; }
    public DateOnly DateOnlyRetrievedDate { get; }
}

/// <summary>Validated Bing/quadkey notation for precisely one Web Mercator level-9 tile.</summary>
public readonly record struct Level9QuadKey
{
    public const int Level = 9;

    public Level9QuadKey(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException("A level-9 quadkey has exactly nine digits, each between 0 and 3.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static bool IsValid(string? value) => value is { Length: Level } && value.All(character => character is >= '0' and <= '3');

    public override string ToString() => Value;
}
