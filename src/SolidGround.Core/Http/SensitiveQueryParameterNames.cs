namespace SolidGround.Core.Http;

/// <summary>
/// Named presets of sensitive query-parameter names for <see cref="SensitiveQueryRedactor"/>. Matching is
/// always case-insensitive (see <see cref="SensitiveQueryRedactor"/>), so a single entry such as "API_Key"
/// already covers a lowercase "api_key" spelling too. Added for SolidGround Issue #27 (PH3-0); see
/// <c>docs/architecture/shared-http-redaction-and-key-resolution.md</c>.
/// </summary>
public static class SensitiveQueryParameterNames
{
    /// <summary>OpenTopography's only sensitive parameter (the <c>usgsdem</c> endpoint's "API_Key" query parameter).</summary>
    public static IReadOnlyCollection<string> OpenTopography { get; } = ["API_Key"];

    /// <summary>Esri ArcGIS REST API's token parameter (for example, geocoding with <c>forStorage=true</c>).</summary>
    public static IReadOnlyCollection<string> Esri { get; } = ["token"];

    /// <summary>
    /// Every name known to this codebase's HTTP sources so far. Intended for a provider-agnostic log sink
    /// with no single provider in view. Must never be substituted into a specific provider's own facade (see
    /// <c>SolidGround.Core.Sources.OpenTopography.OpenTopographyRedaction</c>) -- broadening a provider's own
    /// redacted set could change that provider's own historical output.
    /// </summary>
    public static IReadOnlyCollection<string> KnownFamilies { get; } = ["API_Key", "token"];
}
