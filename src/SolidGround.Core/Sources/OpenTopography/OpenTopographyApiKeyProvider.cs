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
/// .NET user secrets is a host/CLI concern: it needs a package reference and a configured
/// <c>UserSecretsId</c>, neither of which belongs in the Revit-free Core assembly. Reading the key from
/// user secrets is deferred to the CLI issue, which can compose its own
/// <see cref="IOpenTopographyApiKeyProvider"/> over the .NET configuration system.
/// </remarks>
public sealed class EnvironmentOpenTopographyApiKeyProvider : IOpenTopographyApiKeyProvider
{
    private const string VariableName = "OPENTOPOGRAPHY_API_KEY";

    public OpenTopographyApiKey? GetApiKey()
    {
        string? value = Environment.GetEnvironmentVariable(VariableName);
        if (value is null)
        {
            return null;
        }

        string trimmed = value.Trim();
        return trimmed.Length == 0 ? null : new OpenTopographyApiKey(trimmed);
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
