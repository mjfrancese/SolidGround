using System.Text.Json;
using System.Xml.Linq;

namespace SolidGround.Tests;

/// <summary>
/// Offline, deterministic, platform-independent checks for SolidGround Issue #31 (PH3-4)'s interactive
/// Revit-host dialog: <c>CommunityToolkit.Mvvm</c>'s package placement/version/lock-file coverage, the
/// <c>UseWPF</c>/<c>FrameworkReference</c> csproj mechanics it depends on, and the repository-wide "zero
/// <c>.xaml</c> files" guarantee the design's zero-BAML mitigation requires
/// (docs/architecture/revit-interactive-dialog.md "Package: CommunityToolkit.Mvvm 8.4.2",
/// "Purpose and boundary"). Deliberately a new, separate file rather than an addition to
/// <c>RevitHostFilesTests.cs</c>: every new repository check this issue adds lives here so that file's own
/// existing checks are touched only where an existing assertion would otherwise start failing (see
/// <c>RevitHostFilesTests.CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackagesAndOneUnconditionalCommunityToolkitMvvm</c>).
/// These tests read plain text, XML, and JSON only; they never load <c>SolidGround.Revit.dll</c>, never
/// reference <c>SolidGround.Revit</c> from this test project, and never require Revit, so they run
/// unmodified on the Linux self-hosted CI runner.
/// </summary>
public sealed class RevitInteractiveDialogTests
{
    private const string ExpectedCommunityToolkitMvvmPackageId = "CommunityToolkit.Mvvm";
    private const string ExpectedCommunityToolkitMvvmVersion = "8.4.2";

    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string RevitProjectDirectory = Path.Combine(RepositoryRoot, "src", "SolidGround.Revit");
    private static readonly string CsprojPath = Path.Combine(RevitProjectDirectory, "SolidGround.Revit.csproj");
    private static readonly string LocalLockPath = Path.Combine(RevitProjectDirectory, "packages.lock.json");
    private static readonly string CiLockPath = Path.Combine(RevitProjectDirectory, "packages.ci.lock.json");

    // ------------------------------------------------------------------------------------------------
    // Zero-.xaml repository check (AC1: "zero .xaml files, verified by a repository check")
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void RevitProjectContainsNoXamlFiles()
    {
        // The design's own zero-BAML mitigation (docs/architecture/revit-interactive-dialog.md "Purpose and
        // boundary") only holds if nothing ever adds a real .xaml/BAML file to this project -- a
        // code-behind-only SolidGroundDialog is a discipline, not something the compiler enforces on its
        // own, so this is a real, falsifiable regression backstop, not a tautology.
        string[] xamlFiles = Directory.GetFiles(RevitProjectDirectory, "*.xaml", SearchOption.AllDirectories);

        Assert.True(
            xamlFiles.Length == 0,
            $"Expected zero .xaml files under '{RevitProjectDirectory}'; found: {string.Join(", ", xamlFiles)}.");
    }

    // ------------------------------------------------------------------------------------------------
    // src/SolidGround.Revit/SolidGround.Revit.csproj: CommunityToolkit.Mvvm + UseWPF mechanics
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void CsprojPinsCommunityToolkitMvvmToTheApprovedVersionUnconditionally()
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement packageReference = Assert.Single(
            root.Descendants("PackageReference"),
            element => (string?)element.Attribute("Include") == ExpectedCommunityToolkitMvvmPackageId);

        Assert.Equal(ExpectedCommunityToolkitMvvmVersion, (string?)packageReference.Attribute("Version"));

        // AC2's "unconditional" half: unlike the two CI-only Nice3point packages, CommunityToolkit.Mvvm must
        // be a real dependency of every local, deploy, and CI build alike, so neither the element itself nor
        // its immediately enclosing ItemGroup may carry a Condition attribute.
        Assert.Null(packageReference.Attribute("Condition"));
        XElement? parent = packageReference.Parent;
        Assert.NotNull(parent);
        Assert.Null(parent.Attribute("Condition"));
    }

    [Fact]
    public void CsprojEnablesUseWpf()
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement useWpf = Assert.Single(root.Descendants("UseWPF"));

        Assert.Equal("true", useWpf.Value);
    }

    [Fact]
    public void CsprojNoLongerDeclaresAnExplicitWpfFrameworkReference()
    {
        // The concrete NETSDK1086 finding (docs/architecture/revit-interactive-dialog.md "Package:
        // CommunityToolkit.Mvvm 8.4.2"): leaving the project's old, pre-UseWPF explicit
        // <FrameworkReference Include="Microsoft.WindowsDesktop.App.WPF" /> in place alongside
        // <UseWPF>true</UseWPF> emits a warning this project's inherited TreatWarningsAsErrors=true turns
        // into a build failure, so AC1's "compiles under CI unchanged" precondition requires this element to
        // be gone entirely, not merely superseded.
        XElement root = LoadXmlRoot(CsprojPath);

        Assert.Empty(root.Descendants("FrameworkReference"));
    }

    [Fact]
    public void CommunityToolkitMvvmPackageReferenceAppearsInExactlyOneCsprojInTheRepository()
    {
        // AC2's "referenced only from SolidGround.Revit.csproj" half, as a genuine whole-repository check
        // (AGENTS.md: "The agent must keep every Revit reference out of Core, CLI, and Tests" -- this is the
        // same discipline applied to this issue's own new package, not merely an assumption about the one
        // file this test suite happens to already open elsewhere).
        string[] csprojFiles = [.. Directory.EnumerateFiles(RepositoryRoot, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsUnderBuildOutputDirectory(path, RepositoryRoot))];
        Assert.NotEmpty(csprojFiles);

        string[] matches = [.. csprojFiles.Where(path => File.ReadAllText(path).Contains(ExpectedCommunityToolkitMvvmPackageId, StringComparison.Ordinal))];

        string match = Assert.Single(matches);
        Assert.Equal(CsprojPath, match);
    }

    // ------------------------------------------------------------------------------------------------
    // Both restore lock files
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void BothRevitPackageLockFilesContainCommunityToolkitMvvmAtTheApprovedVersion()
    {
        AssertLockFileContainsExactlyOneEntryAtVersion(LocalLockPath, ExpectedCommunityToolkitMvvmPackageId, ExpectedCommunityToolkitMvvmVersion);
        AssertLockFileContainsExactlyOneEntryAtVersion(CiLockPath, ExpectedCommunityToolkitMvvmPackageId, ExpectedCommunityToolkitMvvmVersion);
    }

    private static void AssertLockFileContainsExactlyOneEntryAtVersion(string lockPath, string packageName, string expectedVersion)
    {
        (string Name, string? Resolved)[] matches = [.. ReadLockFilePackages(lockPath).Where(package => package.Name == packageName)];
        (string Name, string? Resolved) match = Assert.Single(matches);

        Assert.Equal(expectedVersion, match.Resolved);
    }

    // ------------------------------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------------------------------

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SolidGround.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"Could not locate SolidGround.slnx by walking up from '{AppContext.BaseDirectory}'.");
    }

    private static XElement LoadXmlRoot(string path)
    {
        Assert.True(File.Exists(path), $"Expected file not found: {path}");
        XDocument document = XDocument.Load(path);
        return document.Root ?? throw new InvalidOperationException($"'{path}' has no root element.");
    }

    /// <summary>Excludes git-ignored <c>bin/</c> and <c>obj/</c> build output so this only scans committed source.</summary>
    private static bool IsUnderBuildOutputDirectory(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj");
    }

    /// <summary>
    /// Parses a NuGet <c>packages*.lock.json</c> file and returns every package name (from every restore
    /// target) paired with its resolved version. The same small, self-contained reimplementation
    /// <c>RevitHostFilesTests.ReadLockFilePackages</c> already uses, duplicated here (rather than shared)
    /// since that method is <see langword="private"/> to its own file and this file is deliberately kept
    /// independent of it.
    /// </summary>
    private static List<(string Name, string? Resolved)> ReadLockFilePackages(string path)
    {
        Assert.True(File.Exists(path), $"Missing lock file: {path}");

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);

        List<(string Name, string? Resolved)> packages = [];
        foreach (JsonProperty target in document.RootElement.GetProperty("dependencies").EnumerateObject())
        {
            foreach (JsonProperty package in target.Value.EnumerateObject())
            {
                string? resolved = package.Value.TryGetProperty("resolved", out JsonElement resolvedElement)
                    ? resolvedElement.GetString()
                    : null;
                packages.Add((package.Name, resolved));
            }
        }

        return packages;
    }
}
