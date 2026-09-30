namespace SolidGround.Core.Configuration;

/// <summary>
/// Resolves paths stored in a settings document. A relative value is always interpreted from the document
/// which supplied it, rather than from Revit's working directory. This keeps legacy import, Settings Browse,
/// and process-mode input resolution consistent.
/// </summary>
public static class SettingsPathResolver
{
    /// <exception cref="ArgumentException">A value is blank or the settings path has no directory.</exception>
    public static string Resolve(string settingsDocumentPath, string configuredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsDocumentPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        if (Path.IsPathFullyQualified(configuredPath))
        {
            return configuredPath;
        }

        string? directory = Path.GetDirectoryName(settingsDocumentPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The settings document path must include a directory.", nameof(settingsDocumentPath));
        }

        return Path.GetFullPath(Path.Combine(directory, configuredPath));
    }
}
