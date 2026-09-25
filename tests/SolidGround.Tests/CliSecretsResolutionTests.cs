using System.Net;
using System.Net.Http.Headers;
using SolidGround.Cli;

namespace SolidGround.Tests;

/// <summary>
/// The two-source OpenTopography API key resolution order (environment variable, then the .NET user-secrets
/// file) and the exact path algorithm that locates that file. See docs/architecture/cli-workflow.md's
/// "Secrets and key resolution" section. Every test here constructs a <see cref="CliHost"/> whose
/// <see cref="CliHost.GetEnvironmentVariable"/> is a test-owned dictionary lookup; none of them ever call
/// <see cref="Environment.SetEnvironmentVariable"/> or read the developer's real <c>%APPDATA%</c> or
/// <c>~/.microsoft/usersecrets</c>.
/// </summary>
public sealed class CliSecretsResolutionTests
{
    private const string FakeKey = "fixture-fake-key-0123456789";
    private const string Bbox = "-93.6045,41.5906,-93.6031,41.5917";

    [Fact]
    public async Task EnvironmentVariableWinsOverAnyUserSecretsFile()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFileUnderAppData(appData.FullName, "{ this is not valid json");
            Dictionary<string, string?> environment = new() { ["OPENTOPOGRAPHY_API_KEY"] = FakeKey, ["APPDATA"] = appData.FullName };

            (int exitCode, _, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyOrWhitespaceEnvironmentVariableFallsThroughToUserSecrets()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFileUnderAppData(appData.FullName, $$"""{ "OPENTOPOGRAPHY_API_KEY": "{{FakeKey}}" }""");
            Dictionary<string, string?> environment = new() { ["OPENTOPOGRAPHY_API_KEY"] = "   ", ["APPDATA"] = appData.FullName };

            (int exitCode, _, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsTheKeyFromAppDataFormPath()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFileUnderAppData(appData.FullName, $$"""{ "OPENTOPOGRAPHY_API_KEY": "{{FakeKey}}" }""");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            (int exitCode, _, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task ReadsTheKeyFromHomeFormPathWhenAppDataIsAbsent()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo home = Directory.CreateTempSubdirectory();
        try
        {
            string secretsPath = Path.Combine(home.FullName, ".microsoft", "usersecrets", "solidground-cli", "secrets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(secretsPath)!);
            File.WriteAllText(secretsPath, $$"""{ "OPENTOPOGRAPHY_API_KEY": "{{FakeKey}}" }""");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = null, ["HOME"] = home.FullName };

            (int exitCode, _, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(home.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task MissingSecretsFileIsTreatedAsAbsentNotAnError()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            (int exitCode, _, string stderr) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Authorization, exitCode);
            Assert.Contains("OPENTOPOGRAPHY_API_KEY", stderr, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedSecretsJsonExitsTwoNamingThePath()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            string secretsPath = WriteSecretsFileUnderAppData(appData.FullName, "not valid json at all { [ }");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            (int exitCode, _, string stderr) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

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

    [Fact]
    public async Task NonStringApiKeyPropertyExitsTwo()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo appData = Directory.CreateTempSubdirectory();
        try
        {
            WriteSecretsFileUnderAppData(appData.FullName, """{ "OPENTOPOGRAPHY_API_KEY": 5 }""");
            Dictionary<string, string?> environment = new() { ["APPDATA"] = appData.FullName };

            (int exitCode, _, _) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Usage, exitCode);
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
            Directory.Delete(appData.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task KeyNeverAppearsInStdoutStderrOrWrittenFiles()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            Dictionary<string, string?> environment = new() { ["OPENTOPOGRAPHY_API_KEY"] = FakeKey };

            (int exitCode, string stdout, string stderr) = await RunFetchAsync(environment, tempDirectory.FullName, TestContext.Current.CancellationToken, verbose: true);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.DoesNotContain(FakeKey, stdout, StringComparison.Ordinal);
            Assert.DoesNotContain(FakeKey, stderr, StringComparison.Ordinal);

            foreach (string path in Directory.GetFiles(tempDirectory.FullName))
            {
                Assert.DoesNotContain(FakeKey, File.ReadAllText(path), StringComparison.Ordinal);
            }
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
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

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunFetchAsync(
        Dictionary<string, string?> environment, string outputDirectory, CancellationToken cancellationToken, bool verbose = false)
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
        if (verbose)
        {
            args.Add("--verbose");
        }

        int exitCode = await CliApplication.RunAsync([.. args], host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
