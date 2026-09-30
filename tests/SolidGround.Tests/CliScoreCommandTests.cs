using SolidGround.Cli;

namespace SolidGround.Tests;

public sealed class CliScoreCommandTests
{
    [Fact]
    public async Task ScoresABundleByReconstructingItsSourceCoordinates()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory();
        try
        {
            string asc = Copy("example-site-synthetic.asc", directory.FullName);
            string prj = Copy("example-site-synthetic.prj", directory.FullName);
            string document = Copy("example-site-synthetic.solidground.json", directory.FullName);
            Copy("example-site-synthetic.points.csv", directory.FullName);

            (int code, string stdout, string stderr) = await RunAsync([
                "score", "--reference-asc", asc, "--reference-prj", prj, "--reference-vertical-datum", "NAVD88",
                "--reference-vertical-unit", "meter", "--bundle", document, "--unit", "meter"]);

            Assert.Equal(CliExitCodes.Success, code);
            Assert.Equal(string.Empty, stderr);
            Assert.Contains("maximum absolute vertical residual", stdout, StringComparison.Ordinal);
            Assert.Contains("vertical datum aligned", stdout, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory.FullName, true); }
    }

    [Fact]
    public async Task ExternalPointsRequireTheirUnitDeclarations()
    {
        DirectoryInfo directory = Directory.CreateTempSubdirectory();
        try
        {
            string asc = Copy("example-site-synthetic.asc", directory.FullName);
            string prj = Copy("example-site-synthetic.prj", directory.FullName);
            string points = Path.Combine(directory.FullName, "external.csv");
            File.WriteAllText(points, "x,y,z\n0,0,0\n");

            (int code, _, string stderr) = await RunAsync([
                "score", "--reference-asc", asc, "--reference-prj", prj, "--reference-vertical-datum", "NAVD88",
                "--reference-vertical-unit", "meter", "--points", points]);

            Assert.Equal(CliExitCodes.Usage, code);
            Assert.Contains("--external-epsg is required", stderr, StringComparison.Ordinal);
        }
        finally { Directory.Delete(directory.FullName, true); }
    }

    private static string Copy(string file, string directory)
    {
        string path = Path.Combine(directory, file);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", file), path);
        return path;
    }

    private static async Task<(int Code, string Stdout, string Stderr)> RunAsync(string[] args)
    {
        using StringWriter stdout = new();
        using StringWriter stderr = new();
        CliHost host = new(_ => null, () => throw new InvalidOperationException(), stdout, stderr);
        int code = await CliApplication.RunAsync(args, host, TestContext.Current.CancellationToken);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
