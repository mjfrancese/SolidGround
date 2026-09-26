namespace SolidGround.Core.Http;

/// <summary>
/// Locates a .NET user-secrets file the same way the <c>dotnet user-secrets</c> SDK tool does. Moved
/// verbatim from <c>SolidGround.Cli.Secrets.UserSecretsFileLocator</c> (SolidGround Issue #27, PH3-0); only
/// <c>secretsId</c> became a parameter instead of a hardcoded <c>"solidground-cli"</c> constant. Internal:
/// <see cref="ApiKeyResolver"/> is the public surface.
/// </summary>
internal static class UserSecretsFileLocator
{
    /// <summary>
    /// Mirrors the .NET SDK's own path resolution exactly: <c>APPDATA</c> wins unconditionally when set and
    /// non-empty (Windows form); otherwise <c>HOME</c>, then the OS's own <c>ApplicationData</c> special
    /// folder, then its <c>UserProfile</c> special folder, then <c>DOTNET_USER_SECRETS_FALLBACK_DIR</c> (Unix
    /// form under each root). Only <c>APPDATA</c> and <c>HOME</c> are read through
    /// <paramref name="getEnvironmentVariable"/> -- deliberately, so a test's injected environment alone
    /// controls the two branches an offline test needs to exercise; the deeper fallback rungs are never
    /// exercised by an offline test (no offline test runs without <c>APPDATA</c> or <c>HOME</c> set to a temp
    /// directory by that same test).
    /// </summary>
    internal static string? TryGetSecretsFilePath(string secretsId, Func<string, string?> getEnvironmentVariable)
    {
        string? appData = getEnvironmentVariable("APPDATA");
        if (!string.IsNullOrEmpty(appData))
        {
            return Path.Combine(appData, "Microsoft", "UserSecrets", secretsId, "secrets.json");
        }

        string? home = getEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(home))
        {
            return Path.Combine(home, ".microsoft", "usersecrets", secretsId, "secrets.json");
        }

        string appDataFolder = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(appDataFolder))
        {
            return Path.Combine(appDataFolder, ".microsoft", "usersecrets", secretsId, "secrets.json");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        if (!string.IsNullOrEmpty(userProfile))
        {
            return Path.Combine(userProfile, ".microsoft", "usersecrets", secretsId, "secrets.json");
        }

        string? fallback = getEnvironmentVariable("DOTNET_USER_SECRETS_FALLBACK_DIR");
        return string.IsNullOrEmpty(fallback) ? null : Path.Combine(fallback, ".microsoft", "usersecrets", secretsId, "secrets.json");
    }
}
