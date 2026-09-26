using System.Text.Json;

namespace SolidGround.Core.Http;

/// <summary>Where to find a .NET user-secrets fallback for a resolved key, when a host supports one.</summary>
public sealed record UserSecretsLocation(string SecretsId, string KeyName);

/// <summary>
/// Resolves a named secret from an environment variable, then (optionally) a .NET user-secrets file --
/// exactly the order and file-location algorithm
/// <c>SolidGround.Cli.Secrets.CliOpenTopographyApiKeyProvider</c> used before SolidGround Issue #27 (PH3-0),
/// generalized by variable name / secrets id / secrets key name instead of being hardcoded to
/// <c>OPENTOPOGRAPHY_API_KEY</c> / <c>solidground-cli</c>. See
/// <c>docs/architecture/shared-http-redaction-and-key-resolution.md</c>.
/// </summary>
public static class ApiKeyResolver
{
    /// <summary>
    /// Returns the trimmed, non-empty resolved value, or null when neither source has one configured. Never
    /// throws for "not configured"; throws <see cref="UserSecretsResolutionException"/> when a user-secrets
    /// file exists but cannot be read or does not have the expected shape (mirrors the CLI's pre-Issue-#27
    /// behavior exactly: "missing" and "malformed" are different outcomes).
    /// </summary>
    /// <param name="environmentVariableName">The environment variable to check first.</param>
    /// <param name="getEnvironmentVariable">The environment-lookup delegate to use, so no caller needs to touch the real process environment in a test.</param>
    /// <param name="userSecrets">
    /// The user-secrets file to fall back to when the environment variable is absent or blank, or null to
    /// resolve from the environment only (for example, a host such as Revit that never reads user secrets).
    /// </param>
    /// <exception cref="UserSecretsResolutionException">The user-secrets file exists but could not be read, is not valid JSON, or its named property is present but not a string.</exception>
    public static string? TryResolve(
        string environmentVariableName,
        Func<string, string?> getEnvironmentVariable,
        UserSecretsLocation? userSecrets = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentVariableName);
        ArgumentNullException.ThrowIfNull(getEnvironmentVariable);

        string? fromEnvironment = getEnvironmentVariable(environmentVariableName);
        if (fromEnvironment is not null)
        {
            string trimmed = fromEnvironment.Trim();
            if (trimmed.Length > 0)
            {
                return trimmed;
            }
        }

        if (userSecrets is null)
        {
            return null;
        }

        string? path = UserSecretsFileLocator.TryGetSecretsFilePath(userSecrets.SecretsId, getEnvironmentVariable);
        if (path is null || !File.Exists(path))
        {
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new UserSecretsResolutionException($"The user-secrets file '{path}' could not be read.", path, ex);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Deliberately does not chain the caught JsonException as an inner exception: for certain
            // malformed inputs (any content System.Text.Json's reader takes as a failed attempt at the
            // "true"/"false"/"null" literal), JsonException.Message echoes back the entire offending raw
            // text it was trying to parse -- which, for a corrupted secrets file, could be the key value
            // itself. Exception.ToString() always recurses into InnerException, so keeping that reference
            // would defeat this method's own fixed, path-only message. Same "never chain a message that
            // might embed the secret" rule OpenTopographyUsgs1mSource.AcquireDetailedAsync already applies
            // to its own network-exception messages; see docs/architecture/opentopography-usgs1m-source.md's
            // "Key transport and redaction" section.
            throw new UserSecretsResolutionException($"The user-secrets file '{path}' is not valid JSON.", path);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty(userSecrets.KeyName, out JsonElement value)
                || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new UserSecretsResolutionException(
                    $"The user-secrets file '{path}' has a non-string '{userSecrets.KeyName}' property.", path);
            }

            string trimmed = value.GetString()!.Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }

    /// <summary>
    /// Like <see cref="TryResolve"/>, but raises <see cref="MissingApiKeyException"/> (naming the variable,
    /// never a value) instead of returning null. Not used by any of today's hosts -- both keep their exact
    /// current null-then-report behavior via <see cref="TryResolve"/>, to guarantee no behavior change --
    /// offered for a future caller that wants a throw-before-any-HTTP-call contract instead of hand-rolling
    /// its own null check.
    /// </summary>
    /// <exception cref="MissingApiKeyException">Neither source has a key configured.</exception>
    /// <exception cref="UserSecretsResolutionException">The user-secrets file exists but could not be read, is not valid JSON, or its named property is present but not a string.</exception>
    public static string Resolve(
        string environmentVariableName,
        Func<string, string?> getEnvironmentVariable,
        UserSecretsLocation? userSecrets = null) =>
        TryResolve(environmentVariableName, getEnvironmentVariable, userSecrets)
            ?? throw new MissingApiKeyException(environmentVariableName, userSecrets);
}

/// <summary>Raised by <see cref="ApiKeyResolver.Resolve"/> when no key is configured. Never contains a value.</summary>
public sealed class MissingApiKeyException : Exception
{
    public MissingApiKeyException(string environmentVariableName, UserSecretsLocation? userSecrets)
        : base(BuildMessage(environmentVariableName, userSecrets))
    {
        EnvironmentVariableName = environmentVariableName;
    }

    /// <summary>The environment variable that was checked. Never the key value.</summary>
    public string EnvironmentVariableName { get; }

    private static string BuildMessage(string environmentVariableName, UserSecretsLocation? userSecrets) =>
        userSecrets is null
            ? $"No API key is configured. Set the '{environmentVariableName}' environment variable."
            : $"No API key is configured. Set the '{environmentVariableName}' environment variable, or run: " +
              $"dotnet user-secrets set {userSecrets.KeyName} \"<value>\" --id {userSecrets.SecretsId}";
}

/// <summary>
/// A user-secrets file exists but could not be read or does not have the expected shape. The message names
/// only the file path, never its content or the resolved value.
/// </summary>
public sealed class UserSecretsResolutionException : Exception
{
    public UserSecretsResolutionException(string message, string secretsFilePath, Exception? innerException = null)
        : base(message, innerException)
    {
        SecretsFilePath = secretsFilePath;
    }

    /// <summary>The path of the malformed or unreadable user-secrets file. Never the file's content.</summary>
    public string SecretsFilePath { get; }
}
