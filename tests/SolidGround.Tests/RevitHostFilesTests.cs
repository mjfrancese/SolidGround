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
    public void CsprojDeclaresNoPackageReferenceBeyondTheTwoCiOnlyNice3PointPackagesAndOneUnconditionalCommunityToolkitMvvm()
    {
        // SolidGround Issue #31 (PH3-4) added the interactive dialog's one real, shipped runtime dependency,
        // CommunityToolkit.Mvvm, alongside the two pre-existing CI-only Nice3point compile stand-ins -- see
        // RevitInteractiveDialogTests.cs for the CommunityToolkit.Mvvm-specific placement/version/lock-file
        // checks (docs/architecture/revit-interactive-dialog.md "Package: CommunityToolkit.Mvvm 8.4.2").
        // Renamed from this fact's original, narrower name (which asserted exactly the two Nice3point
        // entries and nothing else) to keep this file's own convention of exact, literal test names.
        XElement root = LoadXmlRoot(CsprojPath);
        string[] includes = [.. root.Descendants("PackageReference").Select(element => (string?)element.Attribute("Include") ?? string.Empty)];

        Assert.Equal(3, includes.Length);
        Assert.Contains(ExpectedNice3PointRevitApi, includes);
        Assert.Contains(ExpectedNice3PointRevitApiUi, includes);
        Assert.Contains("CommunityToolkit.Mvvm", includes);
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
        // SolidGround Issue #31, PH3-4, Stage D: the problem-line prose itself moved into Core
        // (RevitIniToposolidThresholds.DescribeExceedance, landed Stage A; its own test asserts the exact text)
        // so SolidGroundDialog's inline point-budget warning can share it verbatim -- it no longer appears in
        // this file's own source text at all. These two call sites are what replace it here.
        Assert.Contains("RevitIniToposolidThresholds.ExceedsNativeThreshold", content, StringComparison.Ordinal);
        Assert.Contains("RevitIniToposolidThresholds.DescribeExceedance", content, StringComparison.Ordinal);
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
    // (k) SolidGround Issue #30 (PH3-3)
    // ------------------------------------------------------------------------------------------------
    //
    // The same kind of falsifiable, plain-text regression backstop as (h)/(i)/(j) above, for facts only a
    // live Revit process could otherwise exercise end-to-end (docs/architecture/revit-property-line-and-shared-coordinates.md's
    // "Geometry cleanup contract" > "Correction to an earlier prep note").

    [Fact]
    public void CreateToposolidCommandCleansTheBoundaryBeforeValidatingIt()
    {
        string content = ReadCreateToposolidCommandSource();

        // The trailing "(" pins each substring to its one real call site, not a prose mention of the same
        // method name in a doc comment elsewhere in this file.
        int cleanIndex = RequireIndex(content, "LocalBoundaryCleaner.Clean(");
        int validateIndex = RequireIndex(content, "LocalBoundaryValidator.Validate(");

        Assert.True(cleanIndex < validateIndex, "Expected LocalBoundaryCleaner.Clean( to appear before LocalBoundaryValidator.Validate(.");
    }

    [Fact]
    public void CreateToposolidCommandCreatesThePropertyLineInsideTheSameTransactionAsTheToposolid()
    {
        string content = ReadCreateToposolidCommandSource();

        // "transaction.Start()" has exactly one occurrence in the file, inside RunTransaction (Stage 5) --
        // review fix: without this lower bound, a regression that hoisted either creation call back into Stage 4
        // (geometry construction, pre-transaction; textually before RunTransaction in this same file) would
        // still satisfy the two "< commitIndex" checks below, since the one real transaction.Commit(); always
        // sits later in the file regardless of where in Stage 4 the call moved to. Both bounds together pin
        // AC1's "in one transaction" guarantee, not just "somewhere before commit".
        int startIndex = RequireIndex(content, "transaction.Start()");
        int toposolidCreateIndex = RequireIndex(content, "ToposolidCreationService.Create(");
        int propertyLineCreateIndex = RequireIndex(content, "PropertyLineCreationService.Create(");
        // The trailing ";" excludes this file's own doc comment, which mentions "transaction.Commit()" (no
        // trailing semicolon there) well before the real call.
        int commitIndex = RequireIndex(content, "transaction.Commit();");

        Assert.True(toposolidCreateIndex > startIndex, "Expected ToposolidCreationService.Create( to appear after transaction.Start().");
        Assert.True(propertyLineCreateIndex > startIndex, "Expected PropertyLineCreationService.Create( to appear after transaction.Start().");
        Assert.True(toposolidCreateIndex < commitIndex, "Expected ToposolidCreationService.Create( to appear before the real transaction.Commit();.");
        Assert.True(propertyLineCreateIndex < commitIndex, "Expected PropertyLineCreationService.Create( to appear before the real transaction.Commit();.");
    }

    [Fact]
    public void CreateToposolidCommandGatesPropertyLineCreationToParcelAreasOfInterest()
    {
        // The naive version of this check -- asserting only that the literal AreaOfInterestKind.Parcel appears
        // somewhere between the Stage 4 and Stage 5 call sites -- is vacuous: that exact substring already
        // occurs twice in this file, entirely inside RunDocumentPreflight's own pre-existing, unrelated
        // parcel-geometry-file-reading logic, which sits, as plain text, in that same wide range regardless of
        // anything this issue adds. This instead asserts the actual gate identifier (isParcelAoi, a name this
        // issue introduces new -- no pre-existing occurrence exists to confuse the check) appears within a
        // small, fixed character window immediately preceding each of the two real call sites. A never-wired
        // implementation (PropertyLine created unconditionally, isParcelAoi never referenced near either call
        // site) fails this test.
        string source = ReadCreateToposolidCommandSource();

        AssertGatedByIsParcelAoi(source, "BoundaryIsValidPropertyLine(");
        AssertGatedByIsParcelAoi(source, "PropertyLineCreationService.Create(");
    }

    private static void AssertGatedByIsParcelAoi(string source, string callSiteText)
    {
        int callIndex = RequireIndex(source, callSiteText);

        const int MaxPrecedingDistance = 800; // includes the legal-boundary losslessness guard in the parcel gate
                                               // expression, not the whole file -- unlike the vacuous check above.
        string window = source[Math.Max(0, callIndex - MaxPrecedingDistance)..callIndex];
        Assert.Contains("isParcelAoi", window, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCoordinatesWriteDefaultsToOffAndPreflightRefusesWhenAlreadyCoordinated()
    {
        string commandContent = ReadCreateToposolidCommandSource();

        // Strengthened beyond plain substring presence (review fix): also asserts the detector call and its
        // refusal message sit inside RunDocumentPreflight, before RunTransaction, so a future edit that moved
        // this identical check (same detector call, same message text) into the transaction stage would fail
        // this test -- matching AC3's "Preflight refuses ... before any transaction" requirement.
        int detectorIndex = RequireIndex(commandContent, "SharedCoordinatesDetector.LooksAlreadyCoordinated");
        int refusalMessageIndex = RequireIndex(commandContent, "SolidGround will not overwrite existing shared coordinates");
        int runTransactionIndex = RequireIndex(commandContent, "private static Result RunTransaction(");

        Assert.True(detectorIndex < runTransactionIndex, "Expected the shared-coordinates detector check to run inside RunDocumentPreflight, before RunTransaction.");
        Assert.True(refusalMessageIndex < runTransactionIndex, "Expected the shared-coordinates refusal message to be added inside RunDocumentPreflight, before RunTransaction.");

        // 2026-09-27 live-evidence fix: a live Revit 2027 session (manual evidence Step 14.5) found Revit's own
        // default template ships a brand-new document with its survey point already reporting the "clipped"
        // startup state, so the refusal message must no longer claim that state as evidence of prior
        // coordination, and must instead name the actual signals the corrected proxy now uses.
        Assert.DoesNotContain("is clipped", commandContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("a non-zero shared project position or angle", commandContent, StringComparison.Ordinal);
        Assert.Contains("a moved survey point", commandContent, StringComparison.Ordinal);

        // Review fix: the two checks above only pin the detector call's *position* relative to RunTransaction,
        // not whether it is actually conditioned on the opt-in setting at all. A guard that always ran the
        // detector (or that inverted/widened WriteIfAbsent so the detector runs regardless of the setting) would
        // leave the detector call and refusal message exactly where they are today, so both checks above would
        // still pass -- while RunTransaction's own write gate never re-checks "already coordinated" (it only
        // re-checks WriteIfAbsent; see CreateToposolidCommandGatesTheSharedCoordinatesWriteToTheOptInSetting),
        // so an opted-in run against an already-coordinated document would then silently overwrite real
        // shared coordinates with no refusal anywhere. This anchors the exact, non-negated
        // "if (settings.SharedCoordinates.WriteIfAbsent)" gate within a small, fixed window immediately
        // preceding the detector call, so deleting, inverting, or widening that gate now fails this test too.
        const int MaxPrecedingDistance = 200; // the real gate sits well under 200 characters before this call
                                               // site; the unrelated Stage-5 occurrence of the same settings
                                               // property sits tens of thousands of characters away.
        string detectorWindow = commandContent[Math.Max(0, detectorIndex - MaxPrecedingDistance)..detectorIndex];
        Assert.Contains("if (settings.SharedCoordinates.WriteIfAbsent)", detectorWindow, StringComparison.Ordinal);

        string settingsIoPath = Path.Combine(RevitProjectDirectory, "Settings", "RevitSettingsIo.cs");
        Assert.True(File.Exists(settingsIoPath), $"Missing file: {settingsIoPath}");
        string settingsIoContent = File.ReadAllText(settingsIoPath);

        Assert.Contains(
            "\"sharedCoordinates\": { \"writeIfAbsent\": false }", settingsIoContent, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandGatesTheSharedCoordinatesWriteToTheOptInSetting()
    {
        // Mirrors AssertGatedByIsParcelAoi's technique above (review fix): the sibling isParcelAoi/PropertyLine
        // gate and the unit-conversion call site immediately below both already have this same defense-in-depth
        // backstop; the WriteIfAbsent gate guarding SharedCoordinatesWriter.Write itself -- the one call that
        // would perform an unwanted ProjectLocation/BasePoint mutation (AC3) if this guard were ever removed,
        // inverted, or widened -- did not.
        string source = ReadCreateToposolidCommandSource();

        int writeIndex = RequireIndex(source, "SharedCoordinatesWriter.Write(");
        const int MaxPrecedingDistance = 800; // the real gate sits well under 800 characters before this call
                                               // site; the unrelated Preflight occurrence of the same settings
                                               // property sits tens of thousands of characters away, well
                                               // outside this window.
        string window = source[Math.Max(0, writeIndex - MaxPrecedingDistance)..writeIndex];
        Assert.Contains("SharedCoordinates.WriteIfAbsent", window, StringComparison.Ordinal);

        // Review fix: a bare property-name substring check above cannot tell a correct, non-negated gate from
        // one that was inverted (`if (!context.Settings.SharedCoordinates.WriteIfAbsent && ...)`, writing shared
        // coordinates exactly when the opt-in is OFF -- the literal opposite of AC3) or widened (the `&&`
        // immediately after WriteIfAbsent loosened to `||`, so the write can fire even when the opt-in is OFF).
        // Both mutations leave the bare substring above untouched, since neither the "!" nor a changed
        // connective removes it. This anchors the exact, non-negated "if (...WriteIfAbsent && " prefix as it
        // reads today: an inversion, a widened connective right after WriteIfAbsent, or outright deletion all
        // break this literal, while the correct implementation keeps it intact.
        Assert.Contains(
            "if (context.Settings.SharedCoordinates.WriteIfAbsent && ", window, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandCatchesSharedCoordinatesWriteExceptionAlongsideItsSiblings()
    {
        // Review fix: without "or SharedCoordinatesWriteException" in this filter, a real SetProjectPosition
        // rejection falls through to the generic Stage-5 catch-all below and misattributes the failure to "the
        // toposolid" -- the exact round-1-confirmed bug this branch fixes (see the comment immediately above
        // this catch clause in CreateToposolidCommand.cs).
        string source = ReadCreateToposolidCommandSource();

        int filterIndex = RequireIndex(source, "ex is ToposolidCreationException or PropertyLineCreationException");
        const int MaxFollowingDistance = 120; // tight window: only the rest of this same `when (...)` clause.
        string window = source[filterIndex..Math.Min(source.Length, filterIndex + MaxFollowingDistance)];
        Assert.Contains("or SharedCoordinatesWriteException)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandLogsTheFullExceptionInItsSharedCatchBlock()
    {
        // Re-check fix: SharedCoordinatesWriter.Write and .VerifyWritten each throw SharedCoordinatesWriteException
        // with their own distinct message text ("...write:" vs "...write during verification:"), but the dialog
        // body this same catch block shows deliberately carries only the inner exception's message (its own next
        // comment's "non-redundant detail" rationale) -- so without a call that logs ex itself, that distinguishing
        // wording would reach neither the user nor the trace log, and a log reader could never tell the two Revit
        // rejections apart. This anchors the AddInLog.Error(..., ex) call added between the catch header and the
        // ShowTransactionOutcome return so a future edit cannot silently drop it again.
        string source = ReadCreateToposolidCommandSource();

        int filterIndex = RequireIndex(
            source, "ex is ToposolidCreationException or PropertyLineCreationException or SharedCoordinatesWriteException");
        int returnIndex = RequireIndex(source, "return ShowTransactionOutcome(status, headline,");
        Assert.True(returnIndex > filterIndex, "Expected the ShowTransactionOutcome return after the catch filter.");

        string window = source[filterIndex..returnIndex];
        Assert.Contains("AddInLog.Error(", window, StringComparison.Ordinal);
        Assert.Contains(", ex);", window, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCoordinatesWriteUsesTheOriginsOwnNativeUnitNotOutputUnit()
    {
        // Backstops docs/architecture/revit-property-line-and-shared-coordinates.md's "Unit convention for
        // the shared-coordinates value" section's own named "single most likely implementer mistake" -- using
        // context.Settings.Request.OutputUnit/the Stage 4 revitUnit local instead of
        // SharedCoordinateOrigin.Resolve(...)'s HorizontalUnit/VerticalUnit at the shared-coordinates write's
        // own call site, silently corrupting the anchor with no exception anywhere. The real conversion runs
        // through RevitUnitConversion.ToInternal, which lives in SolidGround.Revit and opens with
        // "using Autodesk.Revit.DB;" -- SolidGround.Tests can never reference it directly
        // (TestAssemblyReferencesNeitherTheRevitApiNorTheRevitHostAssembly forbids a RevitAPI/SolidGround.Revit
        // reference), so only this plain-text scan, not a Core-level unit test, can catch a regression here.
        string source = ReadCreateToposolidCommandSource();

        int resolveIndex = RequireIndex(source, "SharedCoordinateOrigin.Resolve(");
        int writeIndex = RequireIndex(source, "SharedCoordinatesWriter.Write(");
        Assert.True(resolveIndex < writeIndex, "Expected SharedCoordinateOrigin.Resolve( to appear before SharedCoordinatesWriter.Write(.");

        string between = source[resolveIndex..writeIndex];
        Assert.DoesNotContain("context.Settings.Request.OutputUnit", between, StringComparison.Ordinal);
        Assert.DoesNotContain("revitUnit", between, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCoordinatesDetectorNeverReadsTheSurveyPointsClippedProperty()
    {
        // 2026-09-27 live-evidence fix: a live Revit 2027 session (manual evidence Step 14.5) found that a
        // brand-new document opened from Revit's own default template (Default_I_ENU.rte) reports its survey
        // point already Clipped == true at the internal origin, so a detector that reads that property at all
        // -- regardless of how the result is combined with other signals -- can misdetect an uncoordinated
        // model as already coordinated. This backstops that SharedCoordinatesDetector never reads it again,
        // from a test assembly that cannot reference the Revit API to check this any other way. Deliberately
        // scoped to the detector class body only (up to the next type declaration), not the whole file, so this
        // guard cannot be satisfied by coincidentally removing an unrelated occurrence elsewhere.
        string source = ReadSharedCoordinatesGateSource();

        int detectorStart = RequireIndex(source, "internal static class SharedCoordinatesDetector");
        int detectorEnd = RequireIndex(source, "internal sealed class SharedCoordinatesWriteException");
        Assert.True(detectorEnd > detectorStart, "Expected SharedCoordinatesWriteException to be declared after SharedCoordinatesDetector.");

        string detectorBody = source[detectorStart..detectorEnd];
        Assert.DoesNotContain(".Clipped", detectorBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCoordinatesWriterNeverSetsTheSurveyPointsClippedProperty()
    {
        // Companion guard to the detector check above (2026-09-27 live-evidence fix): the writer must leave
        // whatever "clipped" state the user's own document already had exactly as it was --
        // docs/architecture/revit-property-line-and-shared-coordinates.md's "Why Write no longer sets Clipped"
        // section -- detecting its own write afterward purely through the resulting non-zero project position
        // instead. Deliberately scoped to the writer class body (to end of file, its last type), not the whole
        // file, matching the detector guard's own technique above.
        string source = ReadSharedCoordinatesGateSource();

        int writerStart = RequireIndex(source, "internal static class SharedCoordinatesWriter");
        string writerBody = source[writerStart..];
        Assert.DoesNotContain(".Clipped", writerBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedCoordinatesStatementAccuratelyDescribesTheSurveyPointMove()
    {
        // 2026-09-27 live-evidence fix (manual evidence Step 14.5 re-run): the opted-in write's own
        // dialog/placement-record sentence previously claimed "the project base point, survey point, and
        // site location were otherwise left unchanged" -- but the same live session's own log showed
        // ProjectLocation.SetProjectPosition moves the survey point's own internal Position from (0, 0, 0)
        // to the negative of the newly written east-west/north-south as an intrinsic side effect of that
        // one Revit API call (docs/architecture/revit-property-line-and-shared-coordinates.md's "Why Write
        // no longer sets Clipped" section). The old sentence was false, not merely imprecise. This
        // backstops the corrected sentence and guards against the old, inaccurate claim ever coming back.
        string source = ReadCreateToposolidCommandSource();

        Assert.DoesNotContain(
            "the project base point, survey point, and site location were otherwise left unchanged",
            source, StringComparison.Ordinal);
        Assert.Contains(
            "Revit moved the survey point to the new shared origin as part of that write",
            source, StringComparison.Ordinal);
        Assert.Contains(
            "SolidGround made no other change to the project base point or site location.",
            source, StringComparison.Ordinal);

        // The off (opt-in never fired) path's own sentence is a separate, unrelated branch untouched by
        // this fix -- only the opted-in, shared-coordinates-written branch's wording was ever wrong.
        Assert.Contains(
            "SolidGround made no change to ActiveProjectLocation, the project base point, the survey point, or site location during this run.",
            source, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // (l) SolidGround Issue #31 (PH3-4), Stage D: wiring the interactive dialog into
    // CreateToposolidCommand.Execute. Same falsifiable, plain-text regression-backstop discipline as every
    // check above: this file never references SolidGround.Revit or loads its assembly.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void CreateToposolidCommandShowsTheInteractiveDialogBeforePreflightAndBeforeAnyTransaction()
    {
        // AC3/docs/architecture/revit-interactive-dialog.md's "Result-code mapping": Stage 0.5's ShowModal call
        // site must run before Stage 1's RunDocumentPreflight call site, which must in turn run before Stage
        // 5's Transaction.Start(). RequireIndex finds the first occurrence of each literal; ShowModal( and
        // RunDocumentPreflight( each have exactly one real call site in this file (their own declarations sit
        // later in the file, so the call sites are what RequireIndex finds first).
        string source = ReadCreateToposolidCommandSource();

        int showModalIndex = RequireIndex(source, "SolidGroundDialogHost.ShowModal(");
        int preflightCallIndex = RequireIndex(source, "RunDocumentPreflight(");
        int transactionStartIndex = RequireIndex(source, "transaction.Start()");

        Assert.True(showModalIndex < preflightCallIndex, "Expected SolidGroundDialogHost.ShowModal( to appear before RunDocumentPreflight(.");
        Assert.True(preflightCallIndex < transactionStartIndex, "Expected RunDocumentPreflight( to appear before transaction.Start().");
    }

    [Fact]
    public void CreateToposolidCommandReturnsCancelledWithNoTaskDialogWhenTheDialogIsCancelled()
    {
        string source = ReadCreateToposolidCommandSource();

        int dialogNullCheckIndex = RequireIndex(source, "if (dialogResult is null)");
        const int MaxFollowingDistance = 220; // tight window: only this if block's own body (comment + return).
        string window = source[dialogNullCheckIndex..Math.Min(source.Length, dialogNullCheckIndex + MaxFollowingDistance)];

        Assert.Contains("return Result.Cancelled;", window, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskDialog.Show(", window, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandDerivesTheAoiFromSettingsOnlyWhenTheDialogChoseTheSettingsFile()
    {
        // Owner decision 1's refinement (docs/architecture/revit-interactive-dialog.md's "AOI and provenance"):
        // both AOI paths remain available. The FindParcel branch must use dialogResult.Aoi directly; the
        // settings-derived path (AoiSettingsFactory.Build, unchanged from before this issue) must still exist,
        // gated behind the opposite branch of the identical DialogAoiSource.FindParcel check.
        string source = ReadCreateToposolidCommandSource();

        int gateIndex = RequireIndex(source, "if (dialogResult.AoiSource == DialogAoiSource.FindParcel)");
        const int MaxFollowingDistance = 200;
        string findParcelBranch = source[gateIndex..Math.Min(source.Length, gateIndex + MaxFollowingDistance)];
        Assert.Contains("aoi = dialogResult.Aoi;", findParcelBranch, StringComparison.Ordinal);

        Assert.Contains("AoiSettingsFactory.Build(", source, StringComparison.Ordinal);
        Assert.Contains("File.ReadAllText(parcelPath)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandThreadsTheDialogsAddressParcelProvenanceIntoTheAcquisitionPipeline()
    {
        // SolidGround Issue #31, PH3-4: dialogResult.AddressParcel (null on the UseSettingsFile path, by
        // SolidGroundDialogResult's own contract; populated on the FindParcel path) reaches
        // TerrainProcessingPipeline.RunAsync's own new optional trailing parameter (Stage A) through
        // RunPipelineAsync/RunFetchPipelineAsync/RunProcessPipelineAsync, threaded as an ordinary parameter
        // the whole way -- never re-derived at either of the two TerrainProcessingPipeline.RunAsync call sites.
        string source = ReadCreateToposolidCommandSource();

        int pipelineCallIndex = RequireIndex(source, "RunPipelineAsync(context.Settings.Request, context.Wgs84Reference, context.Aoi,");
        const int MaxFollowingDistance = 150; // needle itself is ~79 characters; leaves a real margin after it.
        string window = source[pipelineCallIndex..Math.Min(source.Length, pipelineCallIndex + MaxFollowingDistance)];
        Assert.Contains("dialogResult.AddressParcel,", window, StringComparison.Ordinal);

        int addressParcelParameterCount = Regex.Count(source, Regex.Escape("AddressParcelProvenance? addressParcel"));
        Assert.Equal(3, addressParcelParameterCount); // RunPipelineAsync, RunFetchPipelineAsync, RunProcessPipelineAsync.

        int runAsyncAddressParcelArgumentCount = Regex.Count(source, Regex.Escape("cancellationToken, addressParcel, parcelExtent)"));
        Assert.Equal(2, runAsyncAddressParcelArgumentCount); // TerrainProcessingPipeline.RunAsync's two call sites.
    }

    [Fact]
    public void CreateToposolidCommandBuildsEffectiveSettingsFromTheDialogsOutputUnitPointBudgetAndSharedCoordinatesChoice()
    {
        // Review finding (coverage gap), major, fixed: docs/architecture/revit-interactive-dialog.md's
        // "Settings interaction: prefill, not override" -- the operator's in-dialog OutputUnit/PointBudget/
        // WriteSharedCoordinatesIfAbsent choices, not the raw settings-file values, are what the rest of the
        // run (effectiveSettings) actually uses. This is the one place those choices take effect; unlike every
        // other Stage D wiring fact in this region, it previously had no dedicated test.
        string source = ReadCreateToposolidCommandSource();

        int mergeIndex = RequireIndex(source, "RevitSettings effectiveSettings = dialogResult.EffectiveSettings ?? loaded.Settings with");
        const int MaxFollowingDistance = 700; // also covers the fallback with-expression after EffectiveSettings.
        string window = source[mergeIndex..Math.Min(source.Length, mergeIndex + MaxFollowingDistance)];

        Assert.Contains("OutputUnit = dialogResult.OutputUnit,", window, StringComparison.Ordinal);
        Assert.Contains("PointBudget = dialogResult.PointBudget", window, StringComparison.Ordinal);
        Assert.Contains(
            "SharedCoordinates = new RevitSharedCoordinatesSettings(dialogResult.WriteSharedCoordinatesIfAbsent)",
            window, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandRevalidatesTheEffectiveSettingsAfterTheDialogMergeAndBeforePreflight()
    {
        // Review finding, major, fixed: at the time this test was written, the dialog's own Point Budget step
        // enforced only `PointBudget > 0` (SolidGroundDialogViewModel.CanGoNext), so a dialog-supplied override
        // could otherwise bypass the 1-50000 bound TerrainRequestSettings.Validate() already enforces for every
        // settings-file-sourced value. A later re-check finding closed that gap at its source
        // (CanGoNext's PointBudget case now also requires SolidGroundDialogViewModel.PointBudgetRangeErrorText
        // to be null, the identical bound), but this command-layer re-validation call is kept as-is,
        // deliberately: it is defense-in-depth against any future dialog change (or any other future caller of
        // ExecuteCore) that supplies an effectiveSettings value the dialog itself never validated. This asserts
        // the re-validation call exists, runs after the merge (so it sees the dialog's own PointBudget/OutputUnit)
        // but before Stage 1 Preflight begins, and maps any problem to the same shared ShowProblemList/Cancelled
        // path every other Preflight-shaped rejection already uses.
        string source = ReadCreateToposolidCommandSource();

        int mergeIndex = RequireIndex(source, "RevitSettings effectiveSettings = dialogResult.EffectiveSettings ?? loaded.Settings with");
        int validateIndex = RequireIndex(source, "effectiveSettings.Request.Validate()");
        int preflightCallIndex = RequireIndex(source, "RunDocumentPreflight(");

        Assert.True(mergeIndex < validateIndex, "Expected the effectiveSettings merge to appear before its own re-validation.");
        Assert.True(validateIndex < preflightCallIndex, "Expected effectiveSettings.Request.Validate() to run before RunDocumentPreflight(.");

        const int MaxFollowingDistance = 340; // covers the guard's own Count check, ShowProblemList call, and return (measured: 311 chars).
        string window = source[validateIndex..Math.Min(source.Length, validateIndex + MaxFollowingDistance)];
        Assert.Contains("if (effectiveSettingsProblems.Count > 0)", window, StringComparison.Ordinal);
        Assert.Contains("ShowProblemList(", window, StringComparison.Ordinal);
        Assert.Contains("return Result.Cancelled;", window, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateToposolidCommandNeverWritesBackToTheSettingsFile()
    {
        // docs/architecture/revit-interactive-dialog.md's "Settings interaction: prefill, not override": the
        // operator's dialog choices override the settings file's values for the run about to happen only --
        // CreateToposolidCommand.cs must never call any RevitSettingsIo member beyond the two it already used
        // before this issue (EnsureTemplateExists only ever creates an absent file; TryLoad never writes).
        string source = ReadCreateToposolidCommandSource();

        Assert.Contains("RevitSettingsIo.LoadForUi", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RevitSettingsIo.Save", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RevitSettingsIoDeclaresExactlyOneFileWriteCallSiteTheTemplateCreationItself()
    {
        // Companion guard, at the settings-I/O file itself rather than only its one caller above: a genuine
        // regression that added a second write call (a real "save the dialog's choices back" feature) would
        // satisfy the check above just as well if the new write went through a differently named method this
        // file itself defines and calls internally -- this instead counts every real file-write call site in
        // RevitSettingsIo.cs directly, which must still be exactly the one EnsureTemplateExists already had
        // before this issue (its own atomic create-only write, guarded by "only when the file is absent").
        string path = Path.Combine(RevitProjectDirectory, "Settings", "RevitSettingsIo.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string content = File.ReadAllText(path);

        int writeCallCount = Regex.Count(content, Regex.Escape("File.WriteAllText("));
        Assert.Equal(1, writeCallCount);
    }

    private static string ReadSharedCoordinatesGateSource()
    {
        string path = Path.Combine(RevitProjectDirectory, "Transactions", "SharedCoordinatesGate.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        return File.ReadAllText(path);
    }

    private static string ReadCreateToposolidCommandSource()
    {
        string path = Path.Combine(RevitProjectDirectory, "Commands", "CreateToposolidCommand.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        return File.ReadAllText(path);
    }

    private static int RequireIndex(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected to find '{needle}'.");
        return index;
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
