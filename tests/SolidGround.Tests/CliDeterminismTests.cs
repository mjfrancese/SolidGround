using System.Globalization;
using SolidGround.Cli;
using SolidGround.Core.Exports;

namespace SolidGround.Tests;

/// <summary>
/// Same-machine byte-identical determinism for `process` output and for generated help text, including
/// under a non-invariant current culture. See docs/architecture/cli-workflow.md's "Determinism" section.
/// </summary>
public sealed class CliDeterminismTests
{
    [Fact]
    public async Task ProcessingTheSameInputsTwiceProducesByteIdenticalBundles()
    {
        DirectoryInfo firstDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo secondDirectory = Directory.CreateTempSubdirectory();
        try
        {
            (int firstExitCode, _, _) = await RunAsync(BuildArgs(firstDirectory.FullName), TestContext.Current.CancellationToken);
            (int secondExitCode, _, _) = await RunAsync(BuildArgs(secondDirectory.FullName), TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, firstExitCode);
            Assert.Equal(CliExitCodes.Success, secondExitCode);
            AssertBundlesAreByteIdentical(firstDirectory.FullName, secondDirectory.FullName);
        }
        finally
        {
            Directory.Delete(firstDirectory.FullName, recursive: true);
            Directory.Delete(secondDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task DeterminismHoldsUnderTheDeDeCultureToo()
    {
        DirectoryInfo invariantDirectory = Directory.CreateTempSubdirectory();
        DirectoryInfo deDeDirectory = Directory.CreateTempSubdirectory();
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            (int invariantExitCode, _, _) = await RunAsync(BuildArgs(invariantDirectory.FullName), TestContext.Current.CancellationToken);

            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            (int deDeExitCode, _, _) = await RunAsync(BuildArgs(deDeDirectory.FullName), TestContext.Current.CancellationToken);

            Assert.Equal(CliExitCodes.Success, invariantExitCode);
            Assert.Equal(CliExitCodes.Success, deDeExitCode);
            AssertBundlesAreByteIdentical(invariantDirectory.FullName, deDeDirectory.FullName);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            Directory.Delete(invariantDirectory.FullName, recursive: true);
            Directory.Delete(deDeDirectory.FullName, recursive: true);
        }
    }

    [Fact]
    public async Task HelpTextIsByteIdenticalAcrossTwoCalls()
    {
        (int firstExitCode, string firstStdout, _) = await RunAsync(["help", "process"], TestContext.Current.CancellationToken);
        (int secondExitCode, string secondStdout, _) = await RunAsync(["help", "process"], TestContext.Current.CancellationToken);

        Assert.Equal(CliExitCodes.Success, firstExitCode);
        Assert.Equal(CliExitCodes.Success, secondExitCode);
        Assert.Equal(firstStdout, secondStdout);
    }

    // ---- shared helpers --------------------------------------------------------------------------------

    private static string FixturePath(string fileName) => Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName);

    private static string[] BuildArgs(string outputDirectory) =>
        ["process", "--asc", FixturePath("example-site-synthetic.asc"), "--prj", FixturePath("example-site-synthetic.prj"), "--output", outputDirectory];

    private static void AssertBundlesAreByteIdentical(string firstDirectory, string secondDirectory)
    {
        string documentSuffix = TerrainExportBundleRenderer.DocumentFileSuffix;
        string pointsSuffix = TerrainExportBundleRenderer.PointsFileSuffix;
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(firstDirectory, "terrain" + documentSuffix)),
            File.ReadAllBytes(Path.Combine(secondDirectory, "terrain" + documentSuffix)));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(firstDirectory, "terrain" + pointsSuffix)),
            File.ReadAllBytes(Path.Combine(secondDirectory, "terrain" + pointsSuffix)));
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(
            _ => null,
            () => throw new InvalidOperationException("process/help must never perform an HTTP call."),
            stdout,
            stderr);

        int exitCode = await CliApplication.RunAsync(args, host, cancellationToken).ConfigureAwait(false);
        return (exitCode, stdout.ToString(), stderr.ToString());
    }
}
