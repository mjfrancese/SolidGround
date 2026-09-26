using SolidGround.Core.Http;
using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Cli.Secrets;

/// <summary>
/// Composes the environment and .NET user-secrets sources, in that order, by delegating to
/// <see cref="ApiKeyResolver.TryResolve"/> (SolidGround Issue #27, PH3-0). Never mutates or reads the real
/// process environment: every lookup goes through the injected delegate. See
/// docs/architecture/cli-workflow.md's "Secrets and key resolution" section for the full resolution order,
/// and its "Diagnostics and redaction" section for why a malformed secrets file is reported by naming its
/// path rather than any of its bytes.
/// </summary>
public sealed class CliOpenTopographyApiKeyProvider : IOpenTopographyApiKeyProvider
{
    private const string VariableName = "OPENTOPOGRAPHY_API_KEY";
    private static readonly UserSecretsLocation SecretsLocation = new("solidground-cli", VariableName);

    private readonly Func<string, string?> getEnvironmentVariable;

    public CliOpenTopographyApiKeyProvider(Func<string, string?> getEnvironmentVariable)
    {
        this.getEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
    }

    /// <exception cref="CliUsageException">
    /// The user-secrets file exists but is not valid JSON, or its <c>OPENTOPOGRAPHY_API_KEY</c> property is
    /// present but not a string.
    /// </exception>
    public OpenTopographyApiKey? GetApiKey()
    {
        string? value;
        try
        {
            value = ApiKeyResolver.TryResolve(VariableName, getEnvironmentVariable, SecretsLocation);
        }
        catch (UserSecretsResolutionException ex)
        {
            throw new CliUsageException(ex.Message, ex);
        }

        return value is null ? null : new OpenTopographyApiKey(value);
    }
}
