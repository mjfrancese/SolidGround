using System.Net;
using System.Net.Http.Headers;
using SolidGround.Cli;
using SolidGround.Cli.Commands;

namespace SolidGround.Tests;

/// <summary>
/// SolidGround Issue #27 (PH3-0): end-to-end proof, through <see cref="CliApplication.RunAsync"/>, that
/// migrating <see cref="SolidGround.Cli.Secrets.CliOpenTopographyApiKeyProvider"/> onto
/// <see cref="SolidGround.Core.Http.ApiKeyResolver"/> changed no observable CLI behavior. Belt-and-suspenders
/// alongside the untouched, still-passing <see cref="CliSecretsResolutionTests"/>, which this task must not
/// edit; this file is a self-contained sibling (its helpers cannot be shared across files because the
/// originals are private) rather than a modification of that one.
/// </summary>
public sealed class CliOpenTopographyApiKeyProviderMigrationTests
{
    private const string FakeKey = "fixture-fake-key-0123456789";
    private const string Bbox = "-93.6045,41.5906,-93.6031,41.5917";

    [Fact]
    public async Task FetchStillResolvesTheKeyFromTheEnvironmentBeforeUserSecretsAfterMigration()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFileUnderAppData(appData.FullName, "{ this is not valid json");
            Dictionary<string, string?> environment = new() { ["OPENTOPOGRAPHY_API_KEY"] = FakeKey, ["APPDATA"] = appData.FullName };

            (int exitCode, _, _, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task FetchStillFailsClosedWithNoHttpRequestWhenNoKeyIsConfiguredAfterMigration()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            (int exitCode, _, string stderr, FakeHttpMessageHandler handler) =
                await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Authorization, exitCode);
            Assert.Contains(FetchCommand.MissingApiKeyMessage, stderr, StringComparison.Ordinal);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedUserSecretsJsonStillExitsUsageNamingOnlyThePathAfterMigration()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            string secretsPath = WriteSecretsFileUnderAppData(appData.FullName, "not valid json at all { [ }");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            (int exitCode, _, string stderr, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains(secretsPath, stderr, StringComparison.Ordinal);
            Assert.DoesNotContain("not valid json at all", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string WriteSecretsFileUnderAppData(string appDataDirectory, string content)
    {
        string secretsPath = Path.Combine(appDataDirectory, "Microsoft", "UserSecrets", "solidground-cli", "secrets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
        File.WriteAllText(secretsPath, content);
        return secretsPath;
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr, FakeHttpMessageHandler Handler)> RunFetchAsync(
        Dictionary<string, string?> environment, string outputDirectory, CancellationToken cancellationToken)
    {
        byte[] zipBytes = OpenTopographyUsgs1mSourceTests.CreateZipArchive(
            ("example-site-synthetic.asc", File.ReadAllText(FixturePath("example-site-synthetic.asc"))),
            ("example-site-synthetic.prj", File.ReadAllText(FixturePath("example-site-synthetic.prj"))));
        FakeHttpMessageHandler handler = new((_, _) =>
        {
            HttpResponseMessage response = new(HttpStatusCode.OK) { Content = new ByteArrayContent(zipBytes) };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
            response.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment") { FileName = "usgs1m.zip" };
            return Task.FromResult(response);
        });

        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(name => environment.TryGetValue(name, out string? value) ? value : null, () => handler, stdout, stderr);

        List<string> args = ["fetch", "--bbox", Bbox, "--output", outputDirectory];

        int exitCode = await CliApplication.RunAsync([.. args], host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString(), handler);
    }
}
