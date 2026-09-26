using SolidGround.Core.Http;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>Supplies the OpenTopography API key without prescribing where it is stored.</summary>
public interface IOpenTopographyApiKeyProvider
{
    /// <summary>Returns the current API key, or null when none is configured.</summary>
    OpenTopographyApiKey? GetApiKey();
}

/// <summary>
/// Reads the OpenTopography API key from the <c>OPENTOPOGRAPHY_API_KEY</c> process environment variable.
/// The value is trimmed; a null, empty, or all-whitespace value is treated as "no key configured".
/// </summary>
/// <remarks>
/// Delegates to <see cref="ApiKeyResolver.TryResolve"/> with no <see cref="UserSecretsLocation"/>, so
/// resolution stops after the environment-variable check -- this type still never reads a user-secrets
/// file. A host that also wants a user-secrets fallback (for example, the CLI) composes its own
/// <see cref="IOpenTopographyApiKeyProvider"/> by calling <see cref="ApiKeyResolver.TryResolve"/> directly
/// with a <see cref="UserSecretsLocation"/> supplied, the way <c>SolidGround.Cli.Secrets.CliOpenTopographyApiKeyProvider</c>
/// does. User-secrets file-location and parsing now live in <see cref="ApiKeyResolver"/>/<c>UserSecretsFileLocator</c>
/// themselves (SolidGround Issue #27, PH3-0), not "deferred to the CLI" as an earlier version of this comment
/// stated -- that earlier claim assumed user-secrets support would need a package reference, which was never
/// true: the CLI's own pre-Issue-#27 implementation used hand-rolled <c>System.Text.Json</c> parsing with
/// zero package reference, so the logic was portable into the Revit-free Core assembly without adding one.
/// </remarks>
public sealed class EnvironmentOpenTopographyApiKeyProvider : IOpenTopographyApiKeyProvider
{
    private const string VariableName = "OPENTOPOGRAPHY_API_KEY";

    public OpenTopographyApiKey? GetApiKey()
    {
        string? value = ApiKeyResolver.TryResolve(VariableName, Environment.GetEnvironmentVariable);
        return value is null ? null : new OpenTopographyApiKey(value);
    }
}

/// <summary>Supplies a fixed API key (or the absence of one). Intended for tests and hosts that resolve the key through their own configuration.</summary>
public sealed class StaticOpenTopographyApiKeyProvider : IOpenTopographyApiKeyProvider
{
    private readonly OpenTopographyApiKey? key;

    public StaticOpenTopographyApiKeyProvider(OpenTopographyApiKey? key)
    {
        this.key = key;
    }

    public OpenTopographyApiKey? GetApiKey() => key;
}
