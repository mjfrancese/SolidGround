using SolidGround.Core.Sources.Esri;
using SolidGround.Core.Sources.Geocodio;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Http;

namespace SolidGround.Revit.Settings;

/// <summary>
/// Revit-process lifetime credential overrides. Values are deliberately private, have no formatter, and are
/// cleared from the only retained references during add-in shutdown. Callers must never log an exception that
/// embeds an entered value.
/// </summary>
internal static class SessionApiKeyOverrides
{
    private static string? openTopography;
    private static string? geocodio;
    private static string? esri;
    private static long revision;

    internal static bool HasOpenTopography => openTopography is not null;
    /// <summary>Monotonic, non-secret identity used to invalidate lookup snapshots after a session-key edit.</summary>
    internal static long Revision => Interlocked.Read(ref revision);
    internal static void UseOpenTopography(string value) { openTopography = RequireValue(value); BumpRevision(); }
    internal static void UseGeocodio(string value) { geocodio = RequireValue(value); BumpRevision(); }
    internal static void UseEsri(string value) { esri = RequireValue(value); BumpRevision(); }
    internal static void ClearOpenTopography() { openTopography = null; BumpRevision(); }
    internal static void ClearGeocodio() { geocodio = null; BumpRevision(); }
    internal static void ClearEsri() { esri = null; BumpRevision(); }
    internal static void ClearAll() { openTopography = null; geocodio = null; esri = null; BumpRevision(); }

    internal static IOpenTopographyApiKeyProvider OpenTopographyProvider() => new SessionOpenTopographyProvider();
    internal static IGeocodioApiKeyProvider GeocodioProvider() => new SessionGeocodioProvider();
    internal static IEsriApiKeyProvider EsriProvider() => new SessionEsriProvider();

    private static string RequireValue(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("A session key must not be blank.", nameof(value)) : value;
    private static void BumpRevision() => Interlocked.Increment(ref revision);
    private sealed class SessionOpenTopographyProvider : IOpenTopographyApiKeyProvider { public OpenTopographyApiKey? GetApiKey() { string? value = openTopography; return value is null ? new EnvironmentOpenTopographyApiKeyProvider().GetApiKey() : new OpenTopographyApiKey(value); } }
    private sealed class SessionGeocodioProvider : IGeocodioApiKeyProvider { public ApiKey? GetApiKey() { string? value = geocodio; return value is null ? new EnvironmentGeocodioApiKeyProvider().GetApiKey() : new ApiKey(value); } }
    private sealed class SessionEsriProvider : IEsriApiKeyProvider { public ApiKey? GetApiKey() { string? value = esri; return value is null ? new EnvironmentEsriApiKeyProvider().GetApiKey() : new ApiKey(value); } }
}
