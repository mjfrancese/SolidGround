using SolidGround.Core.Http;

namespace SolidGround.Tests;

/// <summary>
/// SolidGround Issue #27 (PH3-0): the provider-agnostic key-resolution helper
/// <see cref="SolidGround.Cli.Secrets.CliOpenTopographyApiKeyProvider"/> now delegates to. Every test here
/// supplies its own injected <c>Func&lt;string, string?&gt;</c> (a <see cref="Dictionary{TKey,TValue}"/>-backed
/// fake, mirroring <see cref="CliSecretsResolutionTests"/>'s existing idiom exactly) and never touches the
/// real process environment, so none of them need to join the
/// <see cref="OpenTopographyEnvironmentCollectionDefinition"/> collection.
/// </summary>
public sealed class ApiKeyResolverTests
{
    private const string VariableName = "EXAMPLE_API_KEY";
    private const string SecretsId = "example-provider";

    [Fact]
    public void TryResolveReadsTheEnvironmentVariableFirstAndTrimsIt()
    {
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFile(appData.FullName, SecretsId, $$"""{ "{{VariableName}}": "secrets-file-value" }""");
            Dictionary<string, string?> environment = new() { [VariableName] = "  env-value  ", ["APPDATA"] = appData.FullName };

            string? resolved = ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment), new UserSecretsLocation(SecretsId, VariableName));

            Assert.Equal("env-value", resolved);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveFallsThroughToUserSecretsWhenEnvironmentIsMissingOrBlank(string? environmentValue)
    {
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFile(appData.FullName, SecretsId, $$"""{ "{{VariableName}}": "secrets-file-value" }""");
            Dictionary<string, string?> environment = new() { [VariableName] = environmentValue, ["APPDATA"] = appData.FullName };

            string? resolved = ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment), new UserSecretsLocation(SecretsId, VariableName));

            Assert.Equal("secrets-file-value", resolved);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public void TryResolveReturnsNullWhenNeitherSourceIsConfigured()
    {
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            string? resolved = ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment), new UserSecretsLocation(SecretsId, VariableName));

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public void TryResolveReturnsNullWhenNoUserSecretsLocationIsSupplied()
    {
        // Mirrors Revit's env-only contract: a real secrets file at the same id on disk must never even be
        // consulted when the caller passes no UserSecretsLocation.
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFile(appData.FullName, SecretsId, $$"""{ "{{VariableName}}": "secrets-file-value" }""");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            string? resolved = ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment));

            Assert.Null(resolved);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public void TryResolveThrowsUserSecretsResolutionExceptionNamingOnlyThePathForMalformedJson()
    {
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteSecretsFile(appData.FullName, SecretsId, "not valid json at all { [ }");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            UserSecretsResolutionException exception = Assert.Throws<UserSecretsResolutionException>(
                () => ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment), new UserSecretsLocation(SecretsId, VariableName)));

            Assert.Equal(path, exception.SecretsFilePath);
            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("not valid json at all", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public void TryResolveThrowsUserSecretsResolutionExceptionForANonStringProperty()
    {
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFile(appData.FullName, SecretsId, $$"""{ "{{VariableName}}": 5 }""");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            UserSecretsResolutionException exception = Assert.Throws<UserSecretsResolutionException>(
                () => ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment), new UserSecretsLocation(SecretsId, VariableName)));

            Assert.Contains(VariableName, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public void TryResolveLocatesTheSecretsFileUnderAppDataThenHomeWhenBothAreInjected()
    {
        // Re-proves the path algorithm end-to-end through the public TryResolve surface only, with an
        // arbitrary secretsId -- proving genuine parameterization. UserSecretsFileLocator itself is internal
        // to SolidGround.Core.Http and this issue adds no [InternalsVisibleTo] grant from Core to
        // SolidGround.Tests, so this cannot and does not call it directly. Exercises only the APPDATA and
        // HOME rungs (both supplied through the injected fake, pointing at a real temporary directory),
        // mirroring CliSecretsResolutionTests.ReadsTheKeyFromAppDataFormPath /
        // ReadsTheKeyFromHomeFormPathWhenAppDataIsAbsent exactly; it does not exercise the two real
        // OS-special-folder rungs or DOTNET_USER_SECRETS_FALLBACK_DIR, for the same reason those CLI tests
        // never do.
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        DirectoryInfo home = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFile(appData.FullName, SecretsId, $$"""{ "{{VariableName}}": "from-appdata" }""");
            Dictionary<string, string?> appDataEnvironment = new() { ["APPDATA"] = appData.FullName };

            string? fromAppData = ApiKeyResolver.TryResolve(VariableName, MakeLookup(appDataEnvironment), new UserSecretsLocation(SecretsId, VariableName));

            Assert.Equal("from-appdata", fromAppData);

            string homeSecretsPath = Path.Combine(home.FullName, ".microsoft", "usersecrets", SecretsId, "secrets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(homeSecretsPath)!);
            File.WriteAllText(homeSecretsPath, $$"""{ "{{VariableName}}": "from-home" }""");
            Dictionary<string, string?> homeEnvironment = new() { ["APPDATA"] = null, ["HOME"] = home.FullName };

            string? fromHome = ApiKeyResolver.TryResolve(VariableName, MakeLookup(homeEnvironment), new UserSecretsLocation(SecretsId, VariableName));

            Assert.Equal("from-home", fromHome);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
            Directory.Delete(home.FullName, recursive: true);
        }
    }

    [Fact]
    public void ResolveReturnsTheTrimmedValueWhenConfigured()
    {
        Dictionary<string, string?> environment = new() { [VariableName] = "  value  " };

        string resolved = ApiKeyResolver.Resolve(VariableName, MakeLookup(environment));

        Assert.Equal("value", resolved);
    }

    [Fact]
    public void ResolveThrowsMissingApiKeyExceptionNamingTheVariable()
    {
        // ApiKeyResolver.Resolve has no HTTP-related parameter, so there is nothing to wire a
        // FakeHttpMessageHandler into at this layer; the real end-to-end "no HTTP request was ever
        // attempted" proof lives in
        // CliOpenTopographyApiKeyProviderMigrationTests.FetchStillFailsClosedWithNoHttpRequestWhenNoKeyIsConfiguredAfterMigration,
        // which wires a handler into the reachable CliHost/CliApplication.RunAsync path. This test proves
        // the other half of AC2: the caught, actionable error naming the missing variable.
        Dictionary<string, string?> environment = [];

        MissingApiKeyException exception = Assert.Throws<MissingApiKeyException>(
            () => ApiKeyResolver.Resolve(VariableName, MakeLookup(environment)));

        Assert.Equal(VariableName, exception.EnvironmentVariableName);
        Assert.Contains(VariableName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NoExceptionMessageEverContainsAConfiguredKeyValue()
    {
        const string SyntheticKeyValue = "synthetic-test-key-not-real";
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            string path = WriteSecretsFile(appData.FullName, SecretsId, $"not valid json, leaks {SyntheticKeyValue} in its raw bytes");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            UserSecretsResolutionException exception = Assert.Throws<UserSecretsResolutionException>(
                () => ApiKeyResolver.TryResolve(VariableName, MakeLookup(environment), new UserSecretsLocation(SecretsId, VariableName)));

            Assert.DoesNotContain(SyntheticKeyValue, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(SyntheticKeyValue, exception.ToString(), StringComparison.Ordinal);
            Assert.Contains(path, exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static Func<string, string?> MakeLookup(Dictionary<string, string?> environment) =>
        name => environment.TryGetValue(name, out string? value) ? value : null;

    private static string WriteSecretsFile(string appDataDirectory, string secretsId, string content)
    {
        string secretsPath = Path.Combine(appDataDirectory, "Microsoft", "UserSecrets", secretsId, "secrets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
        File.WriteAllText(secretsPath, content);
        return secretsPath;
    }
}
