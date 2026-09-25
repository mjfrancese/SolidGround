using SolidGround.Cli;
using SolidGround.Core.Exports;

namespace SolidGround.Tests;

/// <summary>
/// Every invalid-option class the CLI's fixed option table and per-command binders reject, plus the
/// verb-resolution edge cases (unknown verb, no arguments, help for an unknown verb). See
/// docs/architecture/cli-workflow.md's "Options and defaults" and "Exit codes and error classes" sections.
/// Every case here calls <see cref="CliApplication.RunAsync"/> in-process with an offline host whose HTTP
/// factory throws if it is ever invoked, so no case in this file can perform a network call.
/// </summary>
public sealed class CliOptionParsingTests
{
    // ---- process: required options -------------------------------------------------------------------

    [Fact]
    public async Task MissingAscForProcessExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(dir => ["process", "--output", dir], "--asc", TestContext.Current.CancellationToken);

    [Fact]
    public async Task MissingOutputForProcessExitsWithUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(["process", "--asc", "dummy.asc"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.Contains("--output", stderr, StringComparison.Ordinal);
    }

    // ---- option-table mechanics -----------------------------------------------------------------------

    [Fact]
    public async Task UnknownOptionExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--frobnicate", "x"], "--frobnicate", TestContext.Current.CancellationToken);

    [Fact]
    public async Task DuplicateOptionExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--budget", "1", "--budget", "2"], "--budget", TestContext.Current.CancellationToken);

    // ---- numeric and enum range validation --------------------------------------------------------------

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public async Task OutOfRangeBudgetExitsWithUsageError(string budget) =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--budget", budget], "--budget", TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    public async Task OutOfRangeTimeoutExitsWithUsageError(string timeout) =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["fetch", "--bbox", "-93.6045,41.5906,-93.6031,41.5917", "--output", dir, "--timeout", timeout],
            "--timeout",
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task OutOfRangeCoverageFloorExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--coverage-floor", "1.5"], "--coverage-floor", TestContext.Current.CancellationToken);

    [Fact]
    public async Task UndefinedUnitValueExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--unit", "feet"], "--unit", TestContext.Current.CancellationToken);

    [Fact]
    public async Task UndefinedMethodValueProvesTinErrorIsUnreachableFromTheCli() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--method", "tin-error"], "--method", TestContext.Current.CancellationToken);

    [Fact]
    public async Task MalformedOriginExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--origin", "banana"], "--origin", TestContext.Current.CancellationToken);

    // ---- AOI mutual exclusion and pairing ---------------------------------------------------------------

    [Fact]
    public async Task BboxWithOnlyThreeNumbersExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--bbox", "1,2,3"], "--bbox", TestContext.Current.CancellationToken);

    [Fact]
    public async Task BboxWithWestNotLessThanEastExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--bbox", "-90.0,38.0,-91.0,39.0"], "--bbox", TestContext.Current.CancellationToken);

    [Fact]
    public async Task CenterWithoutRadiusExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--center", "41.59,-93.60"], "--center", TestContext.Current.CancellationToken);

    [Fact]
    public async Task RadiusWithoutCenterExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--radius", "50"], "--radius", TestContext.Current.CancellationToken);

    [Fact]
    public async Task BboxTogetherWithParcelExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--bbox", "-93.7,41.5,-93.6,41.6", "--parcel", "lot.geojson"],
            "--bbox",
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task BufferTogetherWithBboxExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--bbox", "-93.7,41.5,-93.6,41.6", "--buffer", "5"],
            "--buffer",
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task ParcelWithUnrecognizedExtensionAndNoFormatExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--parcel", "foo.bar"], "--parcel-format", TestContext.Current.CancellationToken);

    [Fact]
    public async Task FetchWithZeroAoiFormsExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(dir => ["fetch", "--output", dir], "--bbox", TestContext.Current.CancellationToken);

    [Fact]
    public async Task RunWithZeroAoiFormsExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(dir => ["run", "--output", dir], "--bbox", TestContext.Current.CancellationToken);

    // ---- base-name rule ----------------------------------------------------------------------------------

    [Fact]
    public async Task NameContainingWhitespaceExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--name", "has space"], "--name", TestContext.Current.CancellationToken);

    [Fact]
    public async Task NameStartingWithADotExitsWithUsageError() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", "dummy.asc", "--output", dir, "--name", ".starts-with-dot"], "--name", TestContext.Current.CancellationToken);

    // ---- run/fetch reject process-only or vice versa -----------------------------------------------------

    [Fact]
    public async Task RunRejectsVerticalDatumAsAProcessOnlyOption() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["run", "--bbox", "-93.7,41.5,-93.6,41.6", "--output", dir, "--vertical-datum", "NAVD88"],
            "--vertical-datum",
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task RunRejectsSourceNameAsAProcessOnlyOption() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["run", "--bbox", "-93.7,41.5,-93.6,41.6", "--output", dir, "--source-name", "Foo"],
            "--source-name",
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task FetchRejectsUnitAsAProcessOnlyOption() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["fetch", "--bbox", "-93.7,41.5,-93.6,41.6", "--output", dir, "--unit", "meter"], "--unit", TestContext.Current.CancellationToken);

    // ---- collection period ---------------------------------------------------------------------------------

    [Fact]
    public async Task CollectionStartWithoutCollectionEndExitsWithUsageError() =>
        // These two collection-period options are validated late (after the .asc/.prj pair is read and
        // parsed, docs/architecture/cli-workflow.md's "Commands" section), so this case needs a genuinely
        // valid --asc/--prj pair to reach that validation, unlike every other case in this file.
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", dir, "--collection-start", "2024-05-01"],
            "--collection-start",
            TestContext.Current.CancellationToken);

    [Fact]
    public async Task CollectionStartNotInFixedFormatProvesInvariantCultureExactParsingIsUsed() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => [
                "process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", dir,
                "--collection-start", "05/01/2024", "--collection-end", "2024-05-02",
            ],
            "--collection-start",
            TestContext.Current.CancellationToken);

    // ---- verify, global options, and verb resolution -------------------------------------------------------

    [Fact]
    public async Task DuplicateDocumentOptionOnVerifyExitsWithUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(
            ["verify", "--document", "terrain.solidground.json", "--document", "other.solidground.json"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.Contains("--document", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionWithATrailingArgumentExitsWithUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(["--version", "extra"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.Contains("--version", stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownVerbIsAUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(["bogus"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("bogus", stderr, StringComparison.Ordinal);
        Assert.Contains(ExpectedUnknownCommandLine, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoArgumentsAtAllIsAUsageErrorThatAlsoPrintsGlobalHelp()
    {
        (int exitCode, string stdout, string stderr) = await InvokeAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.Contains("no command was given", stderr, StringComparison.Ordinal);
        Assert.Contains("process", stdout, StringComparison.Ordinal);
        Assert.Contains("fetch", stdout, StringComparison.Ordinal);
        Assert.Contains("run", stdout, StringComparison.Ordinal);
        Assert.Contains("verify", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpForAnUnknownVerbIsAUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(["help", "bogus"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("bogus", stderr, StringComparison.Ordinal);
        Assert.Contains(ExpectedUnknownCommandLine, stderr, StringComparison.Ordinal);
    }

    // ---- unknown verb: all four reachable forms report the identical usage line (exit 2) -----------------

    [Fact]
    public async Task HelpFlagBeforeAnUnknownVerbIsAUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(["--help", "bogus"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("bogus", stderr, StringComparison.Ordinal);
        Assert.Contains(ExpectedUnknownCommandLine, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HelpFlagAfterAnUnknownVerbIsAUsageError()
    {
        (int exitCode, _, string stderr) = await InvokeAsync(["bogus", "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Usage, exitCode);
        Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
        Assert.DoesNotContain("bogus", stderr, StringComparison.Ordinal);
        Assert.Contains(ExpectedUnknownCommandLine, stderr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareHelpFlagWithNoVerbRendersGlobalHelpAndExitsZero()
    {
        (int exitCode, string stdout, string stderr) = await InvokeAsync(["--help"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("process", stdout, StringComparison.Ordinal);
        Assert.Contains("fetch", stdout, StringComparison.Ordinal);
        Assert.Contains("run", stdout, StringComparison.Ordinal);
        Assert.Contains("verify", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareShortHelpFlagWithNoVerbRendersGlobalHelpAndExitsZero()
    {
        (int exitCode, string stdout, string stderr) = await InvokeAsync(["-h"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("process", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BareHelpCommandWithNoVerbRendersGlobalHelpAndExitsZero()
    {
        (int exitCode, string stdout, string stderr) = await InvokeAsync(["help"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("process", stdout, StringComparison.Ordinal);
    }

    // ---- --name=value syntax (docs/architecture/cli-workflow.md's "Options and defaults" section states
    // both --name value and --name=value are accepted) -----------------------------------------------------

    [Fact]
    public async Task NameEqualsValueSyntaxIsAcceptedAndWritesABundle()
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, string stderr) = await InvokeAsync(
                [
                    "process", $"--asc={FixturePath("example-site-synthetic.asc")}", $"--output={tempDirectory.FullName}",
                ],
                TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, exitCode);
            Assert.Equal(string.Empty, stderr);
            Assert.True(File.Exists(Path.Combine(tempDirectory.FullName, "terrain" + TerrainExportBundleRenderer.DocumentFileSuffix)));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task NameEqualsValueSyntaxStillRejectsAnOutOfRangeValueNamingTheOption() =>
        await AssertUsageErrorAndNoOutputFilesAsync(
            dir => ["process", $"--asc={FixturePath("example-site-synthetic.asc")}", $"--output={dir}", "--budget=0"],
            "--budget",
            TestContext.Current.CancellationToken);

    // ---- shared helpers --------------------------------------------------------------------------------

    private const string ExpectedUnknownCommandLine = "unknown command; run 'help' to list the commands.";

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> InvokeAsync(string[] args, CancellationToken cancellationToken)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(
            _ => null,
            () => throw new InvalidOperationException("This test must never perform an HTTP call."),
            stdout,
            stderr);

        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }

    /// <summary>
    /// Runs <paramref name="buildArgs"/> against a freshly created, empty temp directory (substituted for
    /// every case's own <c>--output</c> value), asserts the exit code is 2 with a stderr line naming
    /// <paramref name="expectedFragment"/>, and asserts that directory is still empty afterward -- proving
    /// the CLI's own validation runs, and rejects the invocation, before any file is written.
    /// </summary>
    private static async Task AssertUsageErrorAndNoOutputFilesAsync(Func<string, string[]> buildArgs, string expectedFragment, CancellationToken cancellationToken)
    {
        DirectoryInfo tempDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int exitCode, _, string stderr) = await InvokeAsync(buildArgs(tempDirectory.FullName), cancellationToken).ConfigureAwait(false);

            Assert.Equal(CliExitCodes.Usage, exitCode);
            Assert.Contains("error (usage):", stderr, StringComparison.Ordinal);
            Assert.Contains(expectedFragment, stderr, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFileSystemEntries(tempDirectory.FullName));
        }
        finally
        {
            Directory.Delete(tempDirectory.FullName, recursive: true);
        }
    }
}
