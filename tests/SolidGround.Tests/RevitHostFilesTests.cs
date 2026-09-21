using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SolidGround.Tests;

/// <summary>
/// Offline, deterministic, platform-independent checks against the committed
/// <c>src/SolidGround.Revit</c> host files (Issue #14): the manifest, the csproj's local-vs-CI reference
/// mechanics, both restore lock files, the solution file, the two placeholder ribbon icons, and the CI
/// workflow's trigger surface. These tests read plain text, XML, JSON, and raw PNG header bytes only;
/// they never load <c>SolidGround.Revit.dll</c>, never reference <c>SolidGround.Revit</c> from this test
/// project, and never require Revit, so they run unmodified on the Linux solidground-pve2 CI runner.
/// </summary>
public sealed class RevitHostFilesTests
{
    private const string ExpectedNice3PointRevitApi = "Nice3point.Revit.Api.RevitAPI";
    private const string ExpectedNice3PointRevitApiUi = "Nice3point.Revit.Api.RevitAPIUI";
    private const string ExpectedNice3PointVersion = "2027.0.10";

    // Freshly minted for Issue #14 and pinned permanently in src/SolidGround.Revit/SolidGround.addin
    // (see that file's own header comment and docs/architecture/revit-add-in-host-scaffold.md). Revit's
    // per-machine unsigned-add-in trust decision and the isolated-context resolution are keyed to this
    // exact value, so an accidental regeneration must fail this suite, not just well-formedness checks.
    private const string ExpectedAddInId = "bb4d7576-b0fb-432b-a2c5-15c7ea59147d";

    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string RevitProjectDirectory = Path.Combine(RepositoryRoot, "src", "SolidGround.Revit");
    private static readonly string ScriptsDirectory = Path.Combine(RepositoryRoot, "scripts");
    private static readonly string ManifestPath = Path.Combine(RevitProjectDirectory, "SolidGround.addin");
    private static readonly string CsprojPath = Path.Combine(RevitProjectDirectory, "SolidGround.Revit.csproj");
    private static readonly string LocalLockPath = Path.Combine(RevitProjectDirectory, "packages.lock.json");
    private static readonly string CiLockPath = Path.Combine(RevitProjectDirectory, "packages.ci.lock.json");
    private static readonly string SolutionPath = Path.Combine(RepositoryRoot, "SolidGround.slnx");
    private static readonly string CiWorkflowPath = Path.Combine(RepositoryRoot, ".github", "workflows", "ci.yml");

    // ------------------------------------------------------------------------------------------------
    // (a) src/SolidGround.Revit/SolidGround.addin
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void ManifestDeclaresExactlyOneApplicationAddInAndNoCommandEntry()
    {
        XElement root = LoadXmlRoot(ManifestPath);

        XElement[] addIns = [.. root.Elements("AddIn")];
        XElement addIn = Assert.Single(addIns);
        Assert.Equal("Application", (string?)addIn.Attribute("Type"));
    }

    [Fact]
    public void ManifestAddInHasTheExactExpectedIdentityValues()
    {
        XElement addIn = LoadManifestApplicationAddIn();

        Assert.Equal("SolidGround", (string?)addIn.Element("Name"));
        Assert.Equal("SolidGround.Revit.dll", (string?)addIn.Element("Assembly"));
        Assert.Equal("SolidGround.Revit.SolidGroundApplication", (string?)addIn.Element("FullClassName"));
        Assert.Equal("SolidGround", (string?)addIn.Element("VendorId"));
        Assert.Equal("SolidGround", (string?)addIn.Element("VendorDescription"));
    }

    [Fact]
    public void ManifestAddInIdParsesAsANonEmptyGuid()
    {
        XElement addIn = LoadManifestApplicationAddIn();
        string? addInId = (string?)addIn.Element("AddInId");

        Assert.False(string.IsNullOrWhiteSpace(addInId));
        Assert.True(Guid.TryParse(addInId, out Guid parsed), $"AddInId '{addInId}' does not parse as a GUID.");
        Assert.NotEqual(Guid.Empty, parsed);
        Assert.Equal(ExpectedAddInId, addInId, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ManifestSettingsConfigureAnExplicitIsolatedAddInContext()
    {
        XElement root = LoadXmlRoot(ManifestPath);
        XElement? manifestSettings = root.Element("ManifestSettings");
        Assert.NotNull(manifestSettings);

        Assert.Equal("False", (string?)manifestSettings.Element("UseRevitContext"));
        Assert.Equal("SolidGround", (string?)manifestSettings.Element("ContextName"));
        Assert.Equal("False", (string?)manifestSettings.Element("UseAllContextsForDependencyResolution"));
    }

    [Fact]
    public void ManifestDeclaresNoPublicAssembliesOrDependenciesElements()
    {
        XElement root = LoadXmlRoot(ManifestPath);

        Assert.Empty(root.Descendants("PublicAssemblies"));
        Assert.Empty(root.Descendants("Dependencies"));
    }

    // ------------------------------------------------------------------------------------------------
    // (b) src/SolidGround.Revit/SolidGround.Revit.csproj
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void CsprojTargetsNet10Windows()
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement targetFramework = Assert.Single(root.Descendants("TargetFramework"));

        Assert.Equal("net10.0-windows", targetFramework.Value);
    }

    [Fact]
    public void CsprojHasExactlyOneProjectReferenceToCore()
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement projectReference = Assert.Single(root.Descendants("ProjectReference"));

        string? include = (string?)projectReference.Attribute("Include");
        Assert.NotNull(include);
        Assert.EndsWith("SolidGround.Core.csproj", include, StringComparison.Ordinal);
    }

    [Fact]
    public void CsprojCopiesLockFileAssembliesLocally()
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement setting = Assert.Single(root.Descendants("CopyLocalLockFileAssemblies"));

        Assert.Equal("true", setting.Value);
    }

    [Theory]
    [InlineData("RevitAPI")]
    [InlineData("RevitAPIUI")]
    public void CsprojReferencesTheInstalledRevitSdkOnlyWhenNotUsingCiReferenceAssemblies(string referenceName)
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement reference = Assert.Single(
            root.Descendants("Reference"), element => (string?)element.Attribute("Include") == referenceName);

        XElement? privateElement = reference.Element("Private");
        Assert.NotNull(privateElement);
        Assert.Equal("false", privateElement.Value);

        XElement? hintPath = reference.Element("HintPath");
        Assert.NotNull(hintPath);
        Assert.Contains("$(RevitInstallDir)", hintPath.Value, StringComparison.Ordinal);

        string condition = NormalizeCondition(RequireCondition(reference));
        Assert.Contains("$(userevitreferenceassemblies)!=true", condition, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ExpectedNice3PointRevitApi)]
    [InlineData(ExpectedNice3PointRevitApiUi)]
    public void CsprojPinsTheCiOnlyNice3PointPackagesToTheApprovedVersion(string packageName)
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement packageReference = Assert.Single(
            root.Descendants("PackageReference"), element => (string?)element.Attribute("Include") == packageName);

        Assert.Equal(ExpectedNice3PointVersion, (string?)packageReference.Attribute("Version"));

        string condition = NormalizeCondition(RequireCondition(packageReference));
        Assert.Contains("$(userevitreferenceassemblies)==true", condition, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackages()
    {
        XElement root = LoadXmlRoot(CsprojPath);
        string[] includes = [.. root.Descendants("PackageReference").Select(element => (string?)element.Attribute("Include") ?? string.Empty)];

        Assert.Equal(2, includes.Length);
        Assert.Contains(ExpectedNice3PointRevitApi, includes);
        Assert.Contains(ExpectedNice3PointRevitApiUi, includes);
    }

    [Fact]
    public void CsprojEnablesWindowsTargetingAndTheCiLockFileOnlyUnderTheCiCondition()
    {
        XElement root = LoadXmlRoot(CsprojPath);

        XElement enableWindowsTargeting = Assert.Single(root.Descendants("EnableWindowsTargeting"));
        Assert.Equal("true", enableWindowsTargeting.Value);
        Assert.Contains(
            "$(userevitreferenceassemblies)==true",
            NormalizeCondition(RequireCondition(enableWindowsTargeting)),
            StringComparison.OrdinalIgnoreCase);

        XElement lockFilePath = Assert.Single(root.Descendants("NuGetLockFilePath"));
        Assert.Equal("packages.ci.lock.json", lockFilePath.Value);
        Assert.Contains(
            "$(userevitreferenceassemblies)==true",
            NormalizeCondition(RequireCondition(lockFilePath)),
            StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------------------------------------
    // (c) Both restore lock files
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void BothRevitPackageLockFilesExist()
    {
        Assert.True(File.Exists(LocalLockPath), $"Missing lock file: {LocalLockPath}");
        Assert.True(File.Exists(CiLockPath), $"Missing lock file: {CiLockPath}");
    }

    [Fact]
    public void LocalLockFileContainsNoNice3PointEntry()
    {
        foreach ((string name, _) in ReadLockFilePackages(LocalLockPath))
        {
            Assert.DoesNotContain("nice3point", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData(ExpectedNice3PointRevitApi)]
    [InlineData(ExpectedNice3PointRevitApiUi)]
    public void CiLockFileContainsBothNice3PointPackagesAtTheApprovedVersion(string packageName)
    {
        (string Name, string? Resolved)[] matches = [.. ReadLockFilePackages(CiLockPath).Where(package => package.Name == packageName)];
        (string Name, string? Resolved) match = Assert.Single(matches);

        Assert.Equal(ExpectedNice3PointVersion, match.Resolved);
    }

    // ------------------------------------------------------------------------------------------------
    // (d) No hardcoded all-user add-in path anywhere under src/SolidGround.Revit or scripts/
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void NoRevitProjectOrScriptFileHardcodesAnAllUserAddInPath()
    {
        // AGENTS.md "Revit 2027 rules": the agent must never hardcode an all-user add-in directory; the
        // all-user path must always be read at runtime from ControlledApplication.AllUsersAddinsLocation
        // or Application.AllUsersAddinsLocation. The word-boundary on the third pattern is required so a
        // legitimate AllUsersAddinsLocation member access (SolidGroundApplication.cs, the csproj's own
        // doc comments) never trips this check; only a bare "AllUsersAddins" literal path segment should.
        (string Label, Regex Pattern)[] forbiddenPatterns =
        [
            ("ProgramData\\Autodesk", new Regex(@"programdata\\{1,2}autodesk", RegexOptions.IgnoreCase)),
            ("Program Files\\Autodesk\\Revit\\AddIns", new Regex(@"program\ files\\{1,2}autodesk\\{1,2}revit\\{1,2}addins", RegexOptions.IgnoreCase)),
            ("AllUsersAddins", new Regex(@"\ballusersaddins\b", RegexOptions.IgnoreCase)),
        ];

        List<string> offenders = [];
        foreach (string file in EnumerateFilesToScanForHardcodedPaths())
        {
            string content = File.ReadAllText(file);
            foreach ((string label, Regex pattern) in forbiddenPatterns)
            {
                if (pattern.IsMatch(content))
                {
                    offenders.Add($"{file}: matched '{label}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Hardcoded all-user add-in path segment(s) found:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    // ------------------------------------------------------------------------------------------------
    // (e) SolidGround.slnx
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolutionListsTheRevitProject()
    {
        XElement root = LoadXmlRoot(SolutionPath);
        bool listed = root.Descendants("Project")
            .Any(element => string.Equals(
                (string?)element.Attribute("Path"),
                "src/SolidGround.Revit/SolidGround.Revit.csproj",
                StringComparison.Ordinal));

        Assert.True(listed, $"Expected '{SolutionPath}' to list a <Project Path=\"src/SolidGround.Revit/SolidGround.Revit.csproj\" /> entry.");
    }

    // ------------------------------------------------------------------------------------------------
    // (f) Placeholder ribbon icons
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SolidGround.16.png", 16, 16)]
    [InlineData("SolidGround.32.png", 32, 32)]
    public void RibbonIconHasTheExpectedPixelDimensions(string fileName, int expectedWidth, int expectedHeight)
    {
        string path = Path.Combine(RevitProjectDirectory, "Resources", fileName);
        Assert.True(File.Exists(path), $"Missing icon file: {path}");

        (int width, int height) = ReadPngDimensions(path);
        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    // ------------------------------------------------------------------------------------------------
    // (g) .github/workflows/ci.yml
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void CiWorkflowHasNoDisallowedTriggersAndReferencesTheApprovedRunner()
    {
        Assert.True(File.Exists(CiWorkflowPath), $"Missing CI workflow: {CiWorkflowPath}");
        string text = File.ReadAllText(CiWorkflowPath);

        // Scoped to the "on:" trigger block itself, not the whole file: a step-level comment elsewhere is
        // free to cite "Issue #14" (as this very workflow's compile-gate step does) without that citation
        // being mistaken for a forbidden "issue" trigger. AGENTS.md's "Build and CI" condition 2 and
        // never-list forbid this self-hosted-runner-on-a-public-repo lane from ever gaining a
        // pull-request, fork, issue, comment, release, external-dispatch (for example workflow_dispatch
        // or repository_dispatch), or reusable-workflow (workflow_call) entry path. An allow-list of the
        // one permitted top-level trigger key is used here -- rather than a blocklist of forbidden event
        // names -- so a newly invented or renamed GitHub event is rejected by default instead of needing
        // to be individually enumerated as this test is maintained (a blocklist previously missed both
        // "workflow_call" and "repository_dispatch").
        string triggerBlock = ExtractTriggerBlock(text);
        string[] triggerEventNames = ExtractTopLevelTriggerEventNames(triggerBlock);
        Assert.Equal(["push"], triggerEventNames);

        Assert.Contains("solidground-pve2", text, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // (h) SolidGround Issue #15
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void NoRevitSourceFileReferencesExtensibleStorageTypesYet()
    {
        // SolidGround Issue #15's design record §1.2/§10.3: #15 ships zero Extensible Storage code (Issue
        // #16 owns that). A CI-checked, falsifiable backstop -- deliberately removed when #16 lands -- for
        // "no Autodesk.Revit.DB.ExtensibleStorage.* type is referenced anywhere in SolidGround.Revit".
        string[] forbiddenTokens = ["ExtensibleStorage", "SchemaBuilder", "GetEntity", "SetEntity"];

        List<string> offenders = [];
        foreach (string file in Directory.EnumerateFiles(RevitProjectDirectory, "*.cs", SearchOption.AllDirectories))
        {
            if (IsUnderBuildOutputDirectory(file, RevitProjectDirectory))
            {
                continue;
            }

            string content = File.ReadAllText(file);
            foreach (string token in forbiddenTokens)
            {
                if (content.Contains(token, StringComparison.Ordinal))
                {
                    offenders.Add($"{file}: matched '{token}'");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Extensible Storage token(s) found (Issue #16 territory, not #15):" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void ButtonLongDescriptionKeepsTheSiteFormNotASurveyInstrumentDisclaimer()
    {
        // SolidGround Issue #15's design record §0.4 item 9: the tooltip rewrite must retain, in updated
        // form, the existing disclaimer AGENTS.md's "Accuracy and product claims" section requires -- a
        // stale-clause fix must not silently drop the disclaimer clause it sits next to.
        string path = Path.Combine(RevitProjectDirectory, "SolidGroundApplication.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        Assert.Contains("ButtonLongDescription", content, StringComparison.Ordinal);
        Assert.Contains("site-form tool, not", content, StringComparison.Ordinal);
        Assert.Contains("a survey instrument", content, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandSuccessDialogKeepsTheSiteFormNotASurveyInstrumentDisclaimer()
    {
        // Same requirement (§0.4 item 9), applied to the rewritten success-dialog body (§6.6 step 4), which
        // carries the identical disclaimer today and is not otherwise required to keep any fixed wording.
        string path = Path.Combine(RevitProjectDirectory, "Commands", "CreateToposolidCommand.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        Assert.Contains("site-form tool, not", content, StringComparison.Ordinal);
        Assert.Contains("a survey instrument", content, StringComparison.Ordinal);
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

    /// <summary>
    /// Returns just the YAML <c>on:</c> trigger block from a workflow file: the <c>on:</c> line itself
    /// plus every following line indented under it, stopping at the next line that starts at column 0.
    /// </summary>
    private static string ExtractTriggerBlock(string workflowText)
    {
        string[] lines = workflowText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        int start = Array.FindIndex(lines, line => line.StartsWith("on:", StringComparison.Ordinal));
        Assert.True(start >= 0, "Expected an 'on:' trigger block in the CI workflow.");

        List<string> block = [lines[start]];
        for (int i = start + 1; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length > 0 && !char.IsWhiteSpace(line[0]))
            {
                break;
            }

            block.Add(line);
        }

        return string.Join('\n', block);
    }

    /// <summary>
    /// Extracts the top-level trigger event names from a YAML <c>on:</c> block returned by
    /// <see cref="ExtractTriggerBlock"/>. Handles the forms the GitHub Actions schema allows: an inline
    /// scalar (<c>on: push</c>), an inline flow sequence (<c>on: [push, pull_request]</c>), and the block
    /// form this repository's workflow actually uses (<c>on:\n  push:\n    branches: [main]</c>),
    /// including a block-sequence variant (<c>on:\n  - push</c>). Only the block's own top-level entries
    /// come back -- a nested key such as a "push:" mapping's own "branches:" child is excluded by
    /// indentation depth, not by name, so this generalizes to any event name GitHub adds in the future
    /// without needing this helper itself to name it.
    /// </summary>
    private static string[] ExtractTopLevelTriggerEventNames(string triggerBlock)
    {
        const string Header = "on:";
        string[] lines = triggerBlock.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        Assert.True(lines.Length > 0 && lines[0].StartsWith(Header, StringComparison.Ordinal),
            $"Expected the trigger block to start with '{Header}'.");

        string inlineValue = lines[0][Header.Length..].Trim();
        if (inlineValue.Length > 0)
        {
            // Flow scalar ("on: push") or flow sequence ("on: [push, pull_request]").
            string trimmed = inlineValue.Trim('[', ']');
            return [.. trimmed
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal)];
        }

        List<(int Indent, string Content)> bodyLines = [];
        foreach (string rawLine in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(rawLine)) { continue; }
            int indent = rawLine.Length - rawLine.TrimStart(' ').Length;
            bodyLines.Add((indent, rawLine.Trim()));
        }

        if (bodyLines.Count == 0)
        {
            return [];
        }

        int topLevelIndent = bodyLines.Min(line => line.Indent);
        List<string> keys = [];
        foreach ((int indent, string content) in bodyLines)
        {
            if (indent != topLevelIndent) { continue; }

            string key = content.StartsWith("- ", StringComparison.Ordinal)
                ? content[2..].Trim()
                : content.Split(':', 2)[0].Trim();

            if (key.Length > 0) { keys.Add(key); }
        }

        return [.. keys.Distinct(StringComparer.Ordinal).OrderBy(key => key, StringComparer.Ordinal)];
    }

    private static XElement LoadManifestApplicationAddIn() =>
        LoadXmlRoot(ManifestPath).Elements("AddIn").Single(element => (string?)element.Attribute("Type") == "Application");

    /// <summary>
    /// Returns the effective MSBuild <c>Condition</c> covering <paramref name="element"/>: its own
    /// <c>Condition</c> attribute if present, otherwise the nearest ancestor's (for example, an
    /// enclosing conditioned <c>ItemGroup</c> or <c>PropertyGroup</c>).
    /// </summary>
    private static string RequireCondition(XElement element)
    {
        string[] conditions = [.. new[] { element }
            .Concat(element.Ancestors())
            .Select(candidate => candidate.Attribute("Condition")?.Value)
            .Where(value => value is not null)!];

        Assert.True(
            conditions.Length > 0,
            $"Expected a Condition attribute on or above <{element.Name.LocalName} Include=\"{(string?)element.Attribute("Include")}\">, but found none.");

        return string.Join(" && ", conditions);
    }

    /// <summary>Strips spaces and quote characters so condition text compares reliably regardless of exact quoting/spacing style.</summary>
    private static string NormalizeCondition(string condition) =>
        condition
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("\"", string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Parses a NuGet <c>packages*.lock.json</c> file and returns every package name (from every
    /// restore target) paired with its resolved version. Extracts plain strings inside the method's own
    /// <c>using</c> scope rather than yielding <see cref="JsonElement"/>/<see cref="JsonProperty"/>
    /// values, which become invalid once their backing <see cref="JsonDocument"/> is disposed.
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

    private static IEnumerable<string> EnumerateFilesToScanForHardcodedPaths()
    {
        List<string> files = [];

        files.AddRange(Directory.EnumerateFiles(RevitProjectDirectory, "*.csproj", SearchOption.TopDirectoryOnly));
        files.AddRange(Directory.EnumerateFiles(RevitProjectDirectory, "*.addin", SearchOption.TopDirectoryOnly));
        files.AddRange(
            Directory.EnumerateFiles(RevitProjectDirectory, "*.cs", SearchOption.AllDirectories)
                .Where(path => !IsUnderBuildOutputDirectory(path, RevitProjectDirectory)));

        if (Directory.Exists(ScriptsDirectory))
        {
            files.AddRange(
                Directory.EnumerateFiles(ScriptsDirectory, "*", SearchOption.AllDirectories)
                    .Where(path => !IsUnderBuildOutputDirectory(path, ScriptsDirectory)));
        }

        return files.Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal);
    }

    /// <summary>Excludes git-ignored <c>bin/</c> and <c>obj/</c> build output so this only scans committed source.</summary>
    private static bool IsUnderBuildOutputDirectory(string path, string root)
    {
        string relative = Path.GetRelativePath(root, path);
        string[] segments = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment => segment is "bin" or "obj");
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        byte[] expectedSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        using FileStream stream = File.OpenRead(path);
        byte[] header = new byte[24];
        stream.ReadExactly(header);

        Assert.True(header.AsSpan(0, 8).SequenceEqual(expectedSignature), $"'{path}' does not start with the PNG signature.");
        Assert.Equal("IHDR", Encoding.ASCII.GetString(header, 12, 4));

        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        return (width, height);
    }
}
