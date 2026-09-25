using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SolidGround.Tests;

/// <summary>
/// Offline, deterministic, platform-independent checks against the committed
/// <c>src/SolidGround.Revit</c> host files (Issue #14): the manifest, the csproj's local-vs-CI reference
/// mechanics, both restore lock files, the solution file, the two ribbon icons, and the CI
/// workflow's trigger surface. These tests read plain text, XML, JSON, and raw PNG header bytes only;
/// they never load <c>SolidGround.Revit.dll</c>, never reference <c>SolidGround.Revit</c> from this test
/// project, and never require Revit, so they run unmodified on the Linux self-hosted CI runner.
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

    [Fact]
    public void CsprojDeclaresExactlyTwoEmbeddedResourceItemsForTheRibbonIcons()
    {
        // SolidGround Issue #19's "resource convention" acceptance criterion: SolidGroundApplication.cs
        // loads both ribbon icons by LogicalName through GetManifestResourceStream, so the csproj must
        // embed exactly the two icon files -- no stray third resource, no accidental duplicate.
        XElement root = LoadXmlRoot(CsprojPath);
        XElement[] embeddedResources = [.. root.Descendants("EmbeddedResource")];

        Assert.Equal(2, embeddedResources.Length);
    }

    [Theory]
    [InlineData("SolidGround.16.png", "SmallIconResourceName")]
    [InlineData("SolidGround.32.png", "LargeIconResourceName")]
    public void CsprojEmbedsEachRibbonIconWithARelativeIncludeAndTheMatchingApplicationLogicalNameConstant(
        string fileName, string applicationConstantName)
    {
        XElement root = LoadXmlRoot(CsprojPath);
        XElement resource = Assert.Single(
            root.Descendants("EmbeddedResource"),
            element => ((string?)element.Attribute("Include"))?.EndsWith(fileName, StringComparison.Ordinal) == true);

        string include = (string?)resource.Attribute("Include") ?? string.Empty;

        // Path.IsPathRooted disagrees with itself across platforms (a Windows drive-letter or
        // backslash-rooted path is not "rooted" by Unix's own rules), and this suite also runs on the
        // Linux self-hosted CI runner, so a plain string check is used instead of that BCL method.
        Assert.False(include.StartsWith('/') || include.StartsWith('\\'), $"EmbeddedResource Include '{include}' must be relative, not rooted.");
        Assert.False(Regex.IsMatch(include, "^[A-Za-z]:"), $"EmbeddedResource Include '{include}' must not contain a drive letter.");
        Assert.Equal($@"Resources\{fileName}", include);

        string expectedLogicalName = ReadApplicationStringConstant(applicationConstantName);
        Assert.Equal(expectedLogicalName, (string?)resource.Attribute("LogicalName"));
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
    // (f) Ribbon icons (SolidGround Issue #19 strengthens these beyond the Issue #14 placeholder-era
    // dimension-only check: IHDR's full pixel format, the chunk sequence, and real corner transparency)
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("SolidGround.16.png", 16, 16)]
    [InlineData("SolidGround.32.png", 32, 32)]
    public void RibbonIconIhdrDeclaresEightBitNonInterlacedRgbaAtTheExpectedPixelDimensions(
        string fileName, int expectedWidth, int expectedHeight)
    {
        string path = Path.Combine(RevitProjectDirectory, "Resources", fileName);
        Assert.True(File.Exists(path), $"Missing icon file: {path}");

        (int width, int height, int bitDepth, int colorType, int interlaceMethod) = ReadPngIhdr(path);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
        Assert.Equal(8, bitDepth);
        Assert.Equal(6, colorType); // PNG colour type 6: truecolour with alpha (RGBA).
        Assert.Equal(0, interlaceMethod);
    }

    [Theory]
    [InlineData("SolidGround.16.png")]
    [InlineData("SolidGround.32.png")]
    public void RibbonIconContainsOnlyCriticalPngChunks(string fileName)
    {
        // WPF's BitmapFrame reads a pHYs chunk's pixels-per-metre value as the image's DPI, and 96 DPI
        // (what WPF assumes in that chunk's absence) is not an exact integer number of pixels per metre
        // (96 / 0.0254 = 3779.527...), so any pHYs chunk on a 96 DPI-authored icon is necessarily a
        // rounded approximation -- WPF would then size this 16x16/32x32 icon at roughly 16.002/32.004
        // device-independent pixels instead of exactly 16/32. Omitting pHYs entirely leaves WPF's
        // documented 96 DPI default in force, so the icon renders pixel-perfect on the ribbon.
        // gAMA/cHRM/sRGB/iCCP invite a colour-managed decoder to rescale the glyph's authored colour
        // values, which is equally unwanted for a fixed-palette ribbon icon. Restricting the chunk
        // sequence to the three critical chunks (IHDR, IDAT, IEND) rules out all of the above by
        // construction, rather than only the specific chunk types named here.
        string path = Path.Combine(RevitProjectDirectory, "Resources", fileName);
        Assert.True(File.Exists(path), $"Missing icon file: {path}");

        List<string> chunkTypes = ReadPngChunkTypes(path);

        HashSet<string> allowedChunkTypes = ["IHDR", "IDAT", "IEND"];
        string[] disallowedChunkTypes = [.. chunkTypes.Where(type => !allowedChunkTypes.Contains(type)).Distinct()];
        Assert.True(
            disallowedChunkTypes.Length == 0,
            $"'{path}' contains non-critical chunk(s): {string.Join(", ", disallowedChunkTypes)}.");

        Assert.Equal("IHDR", chunkTypes[0]);
        Assert.Equal("IEND", chunkTypes[^1]);
        Assert.Contains("IDAT", chunkTypes);
    }

    [Theory]
    [InlineData("SolidGround.16.png")]
    [InlineData("SolidGround.32.png")]
    public void RibbonIconHasATransparentBackgroundBehindAnOpaqueGlyph(string fileName)
    {
        string path = Path.Combine(RevitProjectDirectory, "Resources", fileName);
        Assert.True(File.Exists(path), $"Missing icon file: {path}");

        (int width, int height, byte[] pixels) = DecodePngRgbaPixels(path);

        Assert.Equal(0, AlphaAt(pixels, width, 0, 0));
        Assert.Equal(0, AlphaAt(pixels, width, width - 1, 0));
        Assert.Equal(0, AlphaAt(pixels, width, 0, height - 1));
        Assert.Equal(0, AlphaAt(pixels, width, width - 1, height - 1));

        int totalPixelCount = width * height;
        int opaquePixelCount = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] == 255) { opaquePixelCount++; }
        }

        Assert.True(
            opaquePixelCount >= totalPixelCount / 4,
            $"Expected at least a quarter of '{path}' pixels to be fully opaque (alpha 255); found {opaquePixelCount} of {totalPixelCount}.");
    }

    [Fact]
    public void RevitResourcesDirectoryContainsOnlyTheApprovedRibbonIconFiles()
    {
        // SolidGround Issue #19 acceptance criterion 6 ("only final reviewed assets and useful
        // source/provenance files enter the repository"): nothing in this suite previously failed if a
        // future commit dropped a stray design-exploration file -- an extra candidate PNG, an
        // iteration render, a designer's own scratch note -- into this folder alongside the two
        // shipped icons and their README. This is an explicit allow-list, not a blocklist naming
        // specific unwanted files, so any unexpected addition of any kind is caught, not just the
        // ones anticipated today.
        //
        // AC5 ("generation notes identify the tool, prompt intent, seed when applicable, and human
        // cleanup without including credentials") has no automated coverage, unlike AC6 above, and is
        // not expected to gain any: the final-asset-landing commit settled generation notes into
        // docs/architecture/revit-ribbon-icons.md and this same Resources/README.md -- not a separate
        // generation-notes file under Resources/ -- so the allow-list below needs no new entry for it.
        // A content check would only ever assert prose stayed prose, so AC5 is met by those two
        // documents themselves (reviewed for the absence of credentials and PixelLab identifiers as
        // part of writing them), not by an automated test.
        string resourcesDirectory = Path.Combine(RevitProjectDirectory, "Resources");
        Assert.True(Directory.Exists(resourcesDirectory), $"Missing directory: {resourcesDirectory}");

        HashSet<string> approvedFileNames = new(StringComparer.Ordinal) { "README.md", "SolidGround.16.png", "SolidGround.32.png" };
        string[] actualFileNames = [.. Directory.EnumerateFiles(resourcesDirectory).Select(path => Path.GetFileName(path))];

        string[] unexpectedFileNames = [.. actualFileNames.Where(name => !approvedFileNames.Contains(name))];
        Assert.True(
            unexpectedFileNames.Length == 0,
            $"Unexpected file(s) in '{resourcesDirectory}': {string.Join(", ", unexpectedFileNames)}.");

        Assert.Empty(Directory.EnumerateDirectories(resourcesDirectory));
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
    //
    // NoRevitSourceFileReferencesExtensibleStorageTypesYet (Issue #15's design record §1.2/§10.3 backstop for
    // "#15 ships zero Extensible Storage code") stood here until SolidGround Issue #16 Stage 2 attached real
    // Extensible Storage code to SolidGround.Revit; its own doc comment called it "deliberately removed when
    // #16 lands," so it is removed rather than updated.

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
    // (i) SolidGround Issue #15's 2026-09-21 threshold-evidence addendum
    // ------------------------------------------------------------------------------------------------
    //
    // Neither check below can be exercised through a real Revit process from this offline, Revit-free test
    // project (docs/architecture/revit-toposolid-creation.md's "Step 7": Revit itself silently caps the
    // combined `Toposolid.Create` overload's retained vertex count near `Revit.ini`'s own configured
    // `NativeToposolidMaxPointThreshold`, raising no exception). These are the same kind of falsifiable,
    // plain-text regression backstop as the disclaimer checks above: `RevitIniToposolidThresholdsTests`
    // covers the Revit-free parser itself; these two only guard that the Revit-host call sites built on top
    // of it -- the Preflight guard and the post-create verification message -- have not silently regressed.

    [Fact]
    public void CreateToposolidCommandReadsRevitIniAndGuardsThePointBudgetAtPreflight()
    {
        string path = Path.Combine(RevitProjectDirectory, "Commands", "CreateToposolidCommand.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        Assert.Contains("CurrentUsersDataFolderPath", content, StringComparison.Ordinal);
        Assert.Contains("RevitIniToposolidThresholds.Parse", content, StringComparison.Ordinal);
        Assert.Contains("NativeToposolidMaxPointThreshold", content, StringComparison.Ordinal);
        Assert.Contains("exceeds this machine's NativeToposolidMaxPointThreshold", content, StringComparison.Ordinal);
    }

    [Fact]
    public void PostCreationVerificationNamesTheRevitIniThresholdAsTheLikelyCauseOfAVertexShortfall()
    {
        string path = Path.Combine(RevitProjectDirectory, "Transactions", "PostCreationVerification.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        Assert.Contains("NativeToposolidMaxPointThreshold", content, StringComparison.Ordinal);
        Assert.Contains("the likely cause is Revit's own Revit.ini NativeToposolidMaxPointThreshold setting", content, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // (j) SolidGround Issue #15's 2026-09-21 redacted-request-URI logging addendum
    // ------------------------------------------------------------------------------------------------
    //
    // Same kind of falsifiable, plain-text regression backstop as (i) above. A live HTTP 401
    // diagnosis session (docs/architecture/revit-toposolid-creation.md's Manual evidence plan, Step 8b) found the
    // add-in's fetch-mode log never recorded which request an acquisition failure belonged to -- and neither
    // did the CLI's own --verbose FetchCommand output, whose "request '<uri>'." line only prints after a
    // successful acquisition (FetchCommand.PrintAcquisitionEvidence; CliApplication's top-level catch clauses
    // print only ex.Message on failure). That gap on both sides made the HTTP 401 harder to diagnose from
    // either tool's own log alone. This guards that both the success and failure fetch-mode log lines are
    // still present, and that each reads its URI from an already-redacted source
    // (OpenTopographyResponseEvidence/OpenTopographyException, never a raw request object), never an
    // unredacted query string.

    [Fact]
    public void FetchModeLogsTheRedactedAcquisitionRequestUriOnSuccessAndFailure()
    {
        string path = Path.Combine(RevitProjectDirectory, "Commands", "CreateToposolidCommand.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        Assert.Contains("catch (OpenTopographyException ex)", content, StringComparison.Ordinal);
        Assert.Contains(
            """AddInLog.Info($"Fetch mode acquisition request (failed): '{ex.RedactedRequestUri}'.");""",
            content, StringComparison.Ordinal);
        Assert.Contains(
            """AddInLog.Info($"Fetch mode acquisition request (succeeded): '{acquisition.Evidence.RedactedRequestUri}'.");""",
            content, StringComparison.Ordinal);
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
    /// Reads a <c>private const string</c> value directly out of SolidGroundApplication.cs's own source
    /// text (for example <c>SmallIconResourceName</c>), so the csproj LogicalName assertion above fails
    /// loudly the moment that constant and the csproj attribute it must match drift apart, rather than
    /// hardcoding a second copy of the expected value in this test file.
    /// </summary>
    private static string ReadApplicationStringConstant(string constantName)
    {
        string path = Path.Combine(RevitProjectDirectory, "SolidGroundApplication.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        // Word-boundary anchored so a legitimate longer identifier can never be matched as a substring
        // (the same defensive style NoRevitProjectOrScriptFileHardcodesAnAllUserAddInPath above already
        // uses for the same class of risk, e.g. \ballusersaddins\b).
        Match match = Regex.Match(content, $@"\b{Regex.Escape(constantName)}\b\s*=\s*""([^""]+)""");
        Assert.True(match.Success, $"Could not find a string constant named '{constantName}' in '{path}'.");
        return match.Groups[1].Value;
    }

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

    /// <summary>Reads the fixed-layout IHDR chunk that the PNG spec guarantees is the very first chunk.</summary>
    private static (int Width, int Height, int BitDepth, int ColorType, int InterlaceMethod) ReadPngIhdr(string path)
    {
        byte[] expectedSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        using FileStream stream = File.OpenRead(path);
        byte[] header = new byte[29];
        stream.ReadExactly(header);

        Assert.True(header.AsSpan(0, 8).SequenceEqual(expectedSignature), $"'{path}' does not start with the PNG signature.");
        Assert.Equal("IHDR", Encoding.ASCII.GetString(header, 12, 4));

        int width = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        int bitDepth = header[24];
        int colorType = header[25];
        int interlaceMethod = header[28];
        return (width, height, bitDepth, colorType, interlaceMethod);
    }

    /// <summary>
    /// Walks a PNG's chunk stream after the 8-byte signature, returning each chunk's type and data in
    /// file order. Does not validate CRCs: these are committed, offline fixture files, not
    /// attacker-controlled input, so the signature and IHDR-name checks already in use elsewhere in this
    /// file are enough to catch a truncated or non-PNG file.
    /// </summary>
    private static List<(string Type, byte[] Data)> ReadPngChunks(string path)
    {
        byte[] expectedSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(expectedSignature), $"'{path}' does not start with the PNG signature.");

        List<(string Type, byte[] Data)> chunks = [];
        int position = 8;
        while (position < bytes.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(position, 4));
            string type = Encoding.ASCII.GetString(bytes, position + 4, 4);
            byte[] data = bytes[(position + 8)..(position + 8 + length)];
            chunks.Add((type, data));
            position += 8 + length + 4; // length field + type + data + CRC
        }

        return chunks;
    }

    private static List<string> ReadPngChunkTypes(string path) => [.. ReadPngChunks(path).Select(chunk => chunk.Type)];

    /// <summary>
    /// Decodes an 8-bit RGBA (PNG colour type 6) image's pixels for a direct transparency assertion,
    /// without taking a package dependency on top of the standard library: concatenates every IDAT
    /// chunk's compressed bytes, inflates them with <see cref="ZLibStream"/>, then reverses each of the
    /// PNG spec's five per-scanline filter types (section 9: None, Sub, Up, Average, Paeth) one row at a
    /// time. Every ribbon icon this repository ships is 8-bit RGBA (asserted below), so this
    /// intentionally does not handle any other bit depth or colour type.
    /// </summary>
    private static (int Width, int Height, byte[] Pixels) DecodePngRgbaPixels(string path)
    {
        List<(string Type, byte[] Data)> chunks = ReadPngChunks(path);

        (string ihdrType, byte[] ihdr) = chunks[0];
        Assert.Equal("IHDR", ihdrType);

        int width = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(0, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(ihdr.AsSpan(4, 4));
        int bitDepth = ihdr[8];
        int colorType = ihdr[9];
        Assert.Equal(8, bitDepth);
        Assert.Equal(6, colorType);

        using MemoryStream compressed = new();
        foreach ((string type, byte[] data) in chunks)
        {
            if (type == "IDAT") { compressed.Write(data); }
        }

        compressed.Position = 0;
        using ZLibStream inflater = new(compressed, CompressionMode.Decompress);
        using MemoryStream rawStream = new();
        inflater.CopyTo(rawStream);
        byte[] raw = rawStream.ToArray();

        const int BytesPerPixel = 4;
        int stride = width * BytesPerPixel;
        byte[] pixels = new byte[height * stride];
        byte[] previousLine = new byte[stride];
        int rawPosition = 0;

        for (int y = 0; y < height; y++)
        {
            byte filterType = raw[rawPosition];
            rawPosition++;
            byte[] currentLine = new byte[stride];

            for (int x = 0; x < stride; x++)
            {
                byte filtered = raw[rawPosition + x];
                byte left = x >= BytesPerPixel ? currentLine[x - BytesPerPixel] : (byte)0;
                byte above = previousLine[x];
                byte aboveLeft = x >= BytesPerPixel ? previousLine[x - BytesPerPixel] : (byte)0;

                currentLine[x] = filterType switch
                {
                    0 => filtered,
                    1 => unchecked((byte)(filtered + left)),
                    2 => unchecked((byte)(filtered + above)),
                    3 => unchecked((byte)(filtered + ((left + above) / 2))),
                    4 => unchecked((byte)(filtered + PaethPredictor(left, above, aboveLeft))),
                    _ => throw new NotSupportedException($"Unsupported PNG filter type {filterType} in '{path}'."),
                };
            }

            Array.Copy(currentLine, 0, pixels, y * stride, stride);
            rawPosition += stride;
            previousLine = currentLine;
        }

        return (width, height, pixels);
    }

    /// <summary>PNG spec section 9.2's Paeth predictor, used to reverse filter type 4 above.</summary>
    private static byte PaethPredictor(byte left, byte above, byte aboveLeft)
    {
        int initial = left + above - aboveLeft;
        int distanceToLeft = Math.Abs(initial - left);
        int distanceToAbove = Math.Abs(initial - above);
        int distanceToAboveLeft = Math.Abs(initial - aboveLeft);

        if (distanceToLeft <= distanceToAbove && distanceToLeft <= distanceToAboveLeft) { return left; }
        return distanceToAbove <= distanceToAboveLeft ? above : aboveLeft;
    }

    private static int AlphaAt(byte[] rgbaPixels, int width, int x, int y) => rgbaPixels[(((y * width) + x) * 4) + 3];
}
