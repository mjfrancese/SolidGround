using SolidGround.Core.Http;
using SolidGround.Core.Sources.Esri;

namespace SolidGround.Cli.Secrets;

/// <summary>
/// Resolves the Esri ArcGIS API key through the injected <see cref="CliHost.GetEnvironmentVariable"/> delegate,
/// mirroring <see cref="CliOpenTopographyApiKeyProvider"/>'s exact shape. Core's own
/// <see cref="EnvironmentEsriApiKeyProvider"/> is not reused here because it calls the real
/// <see cref="Environment.GetEnvironmentVariable(string)"/> directly, which would force any CLI test
/// exercising this provider to mutate the real process environment -- forbidden by this repository's own
/// secrets-hygiene rule. See docs/architecture/cli-workflow.md's "### geocode" subsection.
/// </summary>
public sealed class CliEsriApiKeyProvider : IEsriApiKeyProvider
{
    private const string VariableName = "ARCGIS_API_KEY";

    private readonly Func<string, string?> getEnvironmentVariable;

    public CliEsriApiKeyProvider(Func<string, string?> getEnvironmentVariable)
    {
        this.getEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
    }

    // No UserSecretsLocation: Core's own EnvironmentEsriApiKeyProvider precedent is environment-only too.
    public ApiKey? GetApiKey()
    {
        string? value = ApiKeyResolver.TryResolve(VariableName, getEnvironmentVariable);
        return value is null ? null : new ApiKey(value);
    }
}
