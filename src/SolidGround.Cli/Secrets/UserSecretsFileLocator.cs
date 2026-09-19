namespace SolidGround.Cli.Secrets;

/// <summary>
/// Locates the .NET user-secrets file the same way the <c>dotnet user-secrets</c> SDK tool does, so the CLI
/// reads exactly the file that tool writes. See docs/architecture/cli-workflow.md's "Secrets and key
/// resolution" section for the full resolution order (environment variable first, then this file) and for
/// why only <c>APPDATA</c>/<c>HOME</c> are read through an injected delegate while the deeper fallback rungs
/// use the real OS special-folder APIs directly.
/// </summary>
internal static class UserSecretsFileLocator
{
    private const string SecretsId = "solidground-cli";

    /// <summary>
    /// Mirrors the .NET SDK's own path resolution exactly: <c>APPDATA</c> wins unconditionally when set and
    /// non-empty (Windows form); otherwise <c>HOME</c>, then the OS's own <c>ApplicationData</c> special
    /// folder, then its <c>UserProfile</c> special folder, then <c>DOTNET_USER_SECRETS_FALLBACK_DIR</c> (Unix
    /// form under each root). Only <c>APPDATA</c> and <c>HOME</c> are read through
    /// <paramref name="getEnvironmentVariable"/> -- deliberately, so a test's injected environment alone
    /// controls the two branches docs/architecture/cli-workflow.md's "Testing strategy" section requires
    /// tests to exercise; the deeper fallback rungs are never exercised by an offline test (no offline test
    /// runs without <c>APPDATA</c> or <c>HOME</c> set to a temp directory by that same test).
    /// </summary>
    internal static string? TryGetSecretsFilePath(Func<string, string?> getEnvironmentVariable)
    {
        string? appData = getEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appData))
        {
            return Path.Combine(appData, "Microsoft", "UserSecrets", SecretsId, "secrets.json");
        }

        string? home = getEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            return Path.Combine(home, ".microsoft", "usersecrets", SecretsId, "secrets.json");
        }

        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(appDataFolder))
        {
            return Path.Combine(appDataFolder, ".microsoft", "usersecrets", SecretsId, "secrets.json");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(userProfile))
        {
            return Path.Combine(userProfile, ".microsoft", "usersecrets", SecretsId, "secrets.json");
        }

        string? fallback = getEnvironmentVariable("DOTNET_USER_SECRETS_FALLBACK_DIR");
        return string.IsNullOrEmpty(fallback) ? null : Path.Combine(fallback, ".microsoft", "usersecrets", SecretsId, "secrets.json");
    }
}
