using System.Reflection;
using SolidGround.Cli;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// The CLI's deterministic, table-generated help text and <c>--version</c> output. See
/// docs/architecture/cli-workflow.md's "Commands", "Options and defaults", "Units", and "Local origin
/// selection and its consequences" sections for the exact contractual sentences asserted here.
/// </summary>
public sealed class CliHelpTextTests
{
    [Fact]
    public async Task GlobalHelpListsAllFourCommands()
    {
        (int exitCode, string stdout, _) = await RunAsync(["help"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("process", stdout, StringComparison.Ordinal);
        Assert.Contains("fetch", stdout, StringComparison.Ordinal);
        Assert.Contains("run", stdout, StringComparison.Ordinal);
        Assert.Contains("verify", stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("run")]
    public async Task UnitHelpTextNamesBothExactFootDefinitions(string verb)
    {
        (int exitCode, string stdout, _) = await RunAsync(["help", verb], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("1200/3937", stdout, StringComparison.Ordinal);
        Assert.Contains("0.3048", stdout, StringComparison.Ordinal);
        Assert.Contains(LengthUnitTokens.DefaultToken + " (the default)", stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("process")]
    [InlineData("run")]
    public async Task OriginHelpTextNamesEveryConsequence(string verb)
    {
        (int exitCode, string stdout, _) = await RunAsync(["help", verb], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Contains("subtracted from every coordinate before unit conversion", stdout, StringComparison.Ordinal);
        Assert.Contains("records the origin, CRS, datum, and unit", stdout, StringComparison.Ordinal);
        Assert.Contains("southwest and centroid differ per parcel", stdout, StringComparison.Ordinal);
        Assert.Contains("shifts every elevation", stdout, StringComparison.Ordinal);
        Assert.Contains("whole number of source units", stdout, StringComparison.Ordinal);
    }

    [Fact]
    public async Task VersionPrintsASingleLineAndExitsZero()
    {
        (int exitCode, string stdout, string stderr) = await RunAsync(["--version"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);

        string trimmed = stdout.TrimEnd('\r', '\n');
        Assert.DoesNotContain('\n', trimmed);
        Assert.Equal(ExpectedVersionText(), trimmed);
    }

    [Fact]
    public async Task HelpFlagShortCircuitsEvenWithOtherInvalidOptions()
    {
        (int exitCode, string stdout, string stderr) = await RunAsync(
            ["process", "--asc", "missing.asc", "--bogus", "--help"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, exitCode);
        Assert.Equal(string.Empty, stderr);
        Assert.Contains("process", stdout, StringComparison.Ordinal);
        Assert.Contains("Options:", stdout, StringComparison.Ordinal);
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static string ExpectedVersionText()
    {
        Assembly assembly = typeof(CliApplication).Assembly;
        string? informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        string version = informationalVersion ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
        return $"SolidGround CLI {version}";
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(
            _ => null,
            () => throw new InvalidOperationException("help/version must never perform an HTTP call."),
            stdout,
            stderr);

        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
