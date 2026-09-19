using System.Text.Json;
using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Cli.Secrets;

/// <summary>
/// Composes the environment and .NET user-secrets sources, in that order. Never mutates or reads the real
/// process environment: every lookup goes through the injected delegate. See docs/architecture/cli-workflow.md's
/// "Secrets and key resolution" section for the full resolution order, and its "Diagnostics and redaction"
/// section for why a malformed secrets file is reported by naming its path rather than any of its bytes.
/// </summary>
public sealed class CliOpenTopographyApiKeyProvider : IOpenTopographyApiKeyProvider
{
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
        string? fromEnvironment = getEnvironmentVariable("OPENTOPOGRAPHY_API_KEY");
        if (fromEnvironment is not null)
        {
            string trimmed = fromEnvironment.Trim();
            if (trimmed.Length > 0)
            {
                return new OpenTopographyApiKey(trimmed);
            }
        }

        string? path = UserSecretsFileLocator.TryGetSecretsFilePath(getEnvironmentVariable);
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
            throw new CliUsageException($"The user-secrets file '{path}' could not be read.", ex);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new CliUsageException($"The user-secrets file '{path}' is not valid JSON.", ex);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("OPENTOPOGRAPHY_API_KEY", out JsonElement value)
                || value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            if (value.ValueKind != JsonValueKind.String)
            {
                throw new CliUsageException($"The user-secrets file '{path}' has a non-string 'OPENTOPOGRAPHY_API_KEY' property.");
            }

            // Trimmed exactly like the environment-variable branch above, so a value with incidental
            // surrounding whitespace is handled identically regardless of which of the two sources supplied
            // it. See docs/architecture/cli-workflow.md's "Secrets and key resolution" section.
            string trimmed = value.GetString()!.Trim();
            return trimmed.Length == 0 ? null : new OpenTopographyApiKey(trimmed);
        }
    }
}
