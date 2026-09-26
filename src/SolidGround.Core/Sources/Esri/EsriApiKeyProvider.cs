using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.Esri;

/// <summary>Supplies the Esri ArcGIS API key without prescribing where it is stored.</summary>
public interface IEsriApiKeyProvider
{
    /// <summary>Returns the current API key, or null when none is configured.</summary>
    ApiKey? GetApiKey();
}

/// <summary>
/// Reads the Esri ArcGIS API key from the <c>ARCGIS_API_KEY</c> process environment variable. Delegates to
/// <see cref="ApiKeyResolver.TryResolve"/> with no <see cref="UserSecretsLocation"/>, so resolution stops
/// after the environment-variable check, exactly mirroring
/// <c>SolidGround.Core.Sources.OpenTopography.EnvironmentOpenTopographyApiKeyProvider</c>. Wraps the resolved
/// value in the shared <see cref="ApiKey"/> directly -- no dedicated <c>EsriApiKey</c> wrapper type is
/// introduced, per <see cref="ApiKey"/>'s own doc comment describing exactly this future-provider case.
/// </summary>
public sealed class EnvironmentEsriApiKeyProvider : IEsriApiKeyProvider
{
    private const string VariableName = "ARCGIS_API_KEY";

    public ApiKey? GetApiKey()
    {
        string? value = ApiKeyResolver.TryResolve(VariableName, Environment.GetEnvironmentVariable);
        return value is null ? null : new ApiKey(value);
    }
}

/// <summary>Supplies a fixed API key (or the absence of one). Intended for tests and hosts that resolve the key through their own configuration.</summary>
public sealed class StaticEsriApiKeyProvider : IEsriApiKeyProvider
{
    private readonly ApiKey? key;

    public StaticEsriApiKeyProvider(ApiKey? key)
    {
        this.key = key;
    }

    public ApiKey? GetApiKey() => key;
}
