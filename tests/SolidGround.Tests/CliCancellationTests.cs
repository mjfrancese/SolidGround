using SolidGround.Cli;

namespace SolidGround.Tests;

/// <summary>
/// Exit 130 (cancelled), for all four commands: a token already cancelled before the call, on the offline
/// `process` and `verify` paths, and a token cancelled during an online `fetch`/`run` request. See
/// docs/architecture/cli-workflow.md's "Exit codes and error classes" section for the universal,
/// first-match <see cref="OperationCanceledException"/> -> 130 mapping every command shares, including
/// `verify`'s own closed <c>{0, 2, 5, 130}</c> exit-code set.
/// </summary>
public sealed class CliCancellationTests
{
    private const string ExpectedCancelledLine = "error (cancelled): the operation was cancelled.";

    [Fact]
    public async Task AnAlreadyCancelledTokenExitsAsCancelledOnTheOfflineProcessPathBeforeAnyFileIsWritten()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            using StringWriter stdout = new();
            using StringWriter stderr = new();
            CliHost host = new(
                _ => null,
                () => throw new InvalidOperationException("process must never perform an HTTP call."),
                stdout,
                stderr);

            using CancellationTokenSource cts = new();
            cts.Cancel();

            int exitCode = await CliApplication.RunAsync(
                [
                    "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"),
                    "--output", tempDirectory.FullName,
                ],
                host,
                cts.Token);

            Assert.Equal(CliExitCodes.Cancelled, exitCode);
            Assert.Equal(ExpectedCancelledLine, stderr.ToString().TrimEnd('\r', '\n'));
            Assert.Empty(Directory.GetFileSystemEntries(tempDirectory.FullName));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task CancellingTheAmbientTokenDuringAFetchRequestExitsAsCancelledAndWritesNoRasterSetFiles()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            using CancellationTokenSource cts = new();
            FakeHttpMessageHandler handler = new((_, cancellationToken) =>
            {
                cts.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException("unreachable: ThrowIfCancellationRequested must throw first.");
            });

            using StringWriter stdout = new();
            using StringWriter stderr = new();
            CliHost host = new(name => name == "OPENTOPOGRAPHY_API_KEY" ? "fixture-fake-key-0123456789" : null, () => handler, stdout, stderr);

            int exitCode = await CliApplication.RunAsync(
                ["fetch", "--bbox", "[withheld],[withheld],[withheld],[withheld]", "--output", tempDirectory.FullName],
                host,
                cts.Token);

            Assert.Equal(CliExitCodes.Cancelled, exitCode);
            Assert.Equal(ExpectedCancelledLine, stderr.ToString().TrimEnd('\r', '\n'));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.asc")));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.prj")));
            Assert.False(File.Exists(Path.Combine(tempDirectory.FullName, "terrain.source.json")));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenExitsAsCancelledOnTheOnlineRunPathBeforeAnyFileIsWritten()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            FakeHttpMessageHandler handler = new((_, _) =>
                throw new InvalidOperationException("run must never complete an HTTP call against an already-cancelled token."));

            using StringWriter stdout = new();
            using StringWriter stderr = new();
            CliHost host = new(name => name == "OPENTOPOGRAPHY_API_KEY" ? "fixture-fake-key-0123456789" : null, () => handler, stdout, stderr);

            using CancellationTokenSource cts = new();
            cts.Cancel();

            int exitCode = await CliApplication.RunAsync(
                ["run", "--bbox", "[withheld],[withheld],[withheld],[withheld]", "--output", tempDirectory.FullName],
                host,
                cts.Token);

            Assert.Equal(CliExitCodes.Cancelled, exitCode);
            Assert.Equal(ExpectedCancelledLine, stderr.ToString().TrimEnd('\r', '\n'));
            Assert.Empty(Directory.GetFileSystemEntries(tempDirectory.FullName));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenExitsAsCancelledOnTheOfflineVerifyPath()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            string documentPath = Path.Combine(tempDirectory.FullName, "example-site-synthetic.solidground.json");
            string pointsPath = Path.Combine(tempDirectory.FullName, "example-site-synthetic.points.csv");
            File.Copy(FixturePath("example-site-synthetic.solidground.json"), documentPath);
            File.Copy(FixturePath("example-site-synthetic.points.csv"), pointsPath);

            using StringWriter stdout = new();
            using StringWriter stderr = new();
            CliHost host = new(
                _ => null,
                () => throw new InvalidOperationException("verify must never perform an HTTP call."),
                stdout,
                stderr);

            using CancellationTokenSource cts = new();
            cts.Cancel();

            int exitCode = await CliApplication.RunAsync(["verify", "--document", documentPath], host, cts.Token);

            Assert.Equal(CliExitCodes.Cancelled, exitCode);
            Assert.Equal(ExpectedCancelledLine, stderr.ToString().TrimEnd('\r', '\n'));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);
}
