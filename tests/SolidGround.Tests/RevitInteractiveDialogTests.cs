using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    private static readonly string DialogDirectory = Path.Combine(RevitProjectDirectory, "Dialog");

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
    // Stage C: theming/accessibility source-scan checks (docs/architecture/revit-interactive-dialog.md
    // "Theming and accessibility"). Deliberately plain-text scans, not a rendered-UI check: this test project
    // never references SolidGround.Revit or loads its assembly (see this file's own header), so no test here
    // can construct a real SolidGroundDialog -- these checks read the same committed Dialog/*.cs source a
    // human reviewer would.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolidGroundDialogSourceBranchesOnHighContrastAndOnTheRevitUiTheme()
    {
        string concatenatedSource = ReadAllDialogSourceConcatenated();

        Assert.Contains("SystemParameters.HighContrast", concatenatedSource, StringComparison.Ordinal);
        Assert.Contains("SystemColors.", concatenatedSource, StringComparison.Ordinal);
        Assert.Contains("UIThemeManager.CurrentTheme", concatenatedSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogSourceSetsAutomationPropertiesNameOnEveryDeclaredInteractiveControl()
    {
        // A zero-slack, self-consistent match (design review finding, major -- not a minimum-count
        // tolerance): the dialog source declares a small, hand-maintained constant
        // (SolidGroundDialogAutomationInventory.ExpectedControlCount), bumped by hand exactly when a control
        // is added to or removed from the dialog, alongside the matching AutomationProperties.SetName( call.
        // This reads both numbers directly from source and asserts they agree, so dropping (or adding) a
        // single control's SetName call without also updating the constant -- or vice versa -- fails this
        // test; coordinated, deliberate changes to both together are how the constant is meant to evolve.
        string concatenatedSource = ReadAllDialogSourceConcatenated();

        int actualSetNameCallCount = Regex.Count(concatenatedSource, Regex.Escape("AutomationProperties.SetName("));

        Match constantMatch = Regex.Match(concatenatedSource, @"ExpectedControlCount\s*=\s*(\d+)\s*;");
        Assert.True(constantMatch.Success, "Expected to find 'SolidGroundDialogAutomationInventory.ExpectedControlCount's declaration.");
        int declaredExpectedControlCount = int.Parse(constantMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        Assert.True(actualSetNameCallCount > 0, "Expected at least one AutomationProperties.SetName( call site.");
        Assert.Equal(declaredExpectedControlCount, actualSetNameCallCount);
    }

    [Theory]
    [InlineData("private void Geocode()", "AddressGeocoderException")]
    [InlineData("private void FindParcel()", "ParcelBoundarySourceException")]
    public void SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies(
        string methodSignature, string expectedSourceExceptionCatch)
    {
        string concatenatedSource = ReadAllDialogSourceConcatenated();
        string methodBody = ExtractMethodBody(concatenatedSource, methodSignature);

        Assert.Contains($"catch ({expectedSourceExceptionCatch}", methodBody, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("throw;", methodBody, StringComparison.Ordinal);
        Assert.DoesNotContain("throw ex;", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelClearsStaleParcelStateWhenTheSelectedGeocodeCandidateChanges()
    {
        // Review finding, major (docs/architecture/revit-interactive-dialog.md "MVVM shape (and why no
        // messenger)"): re-selecting a different geocode candidate (or re-geocoding after navigating back) left
        // a previously found ParcelCandidates/SelectedParcelCandidate in place, so Create could silently pair a
        // parcel from one location with a newly confirmed address/point from another. This
        // CommunityToolkit.Mvvm-invoked partial hook fires on every write to SelectedGeocodeCandidate --
        // including both assignments inside Geocode() and the GeocodeCandidates list's own two-way binding --
        // so this asserts it actually clears every piece of stale parcel state, not merely that it exists.
        string concatenatedSource = ReadAllDialogSourceConcatenated();
        string methodBody = ExtractMethodBody(
            concatenatedSource,
            "partial void OnSelectedGeocodeCandidateChanged(AddressGeocodeCandidate? value)");

        Assert.Contains("ParcelCandidates = []", methodBody, StringComparison.Ordinal);
        Assert.Contains("SelectedParcelCandidate = null", methodBody, StringComparison.Ordinal);
        Assert.Contains("_parcelLookupAttempted = false", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelRequiresAddressTextToStillMatchTheConfirmedCandidateBeforeAllowingNext()
    {
        // Review finding, blocker (docs/architecture/revit-interactive-dialog.md "MVVM shape (and why no
        // messenger)"): editing AddressText after a candidate was already confirmed -- without a fresh
        // successful Geocode -- left the stale SelectedGeocodeCandidate/SelectedParcelCandidate pairing
        // reachable all the way to Create, silently building the AOI and AddressParcelProvenance for a
        // different, earlier-confirmed location than what the address box currently displays. CanGoNext's
        // AddressEntry case must also require the live AddressText to still match the text that produced the
        // currently confirmed candidate, and both of Geocode's success branches (a real geocode, and a direct
        // "latitude, longitude" entry) must keep that comparison value current.
        string concatenatedSource = ReadAllDialogSourceConcatenated();

        string canGoNextBody = ExtractMethodBody(concatenatedSource, "private bool CanGoNext() => CurrentStep switch");
        Assert.Contains("_confirmedAddressText", canGoNextBody, StringComparison.Ordinal);

        string geocodeBody = ExtractMethodBody(concatenatedSource, "private void Geocode()");
        int confirmedAddressTextAssignmentCount = Regex.Count(geocodeBody, Regex.Escape("_confirmedAddressText = addressText;"));
        Assert.Equal(2, confirmedAddressTextAssignmentCount);
    }

    // ------------------------------------------------------------------------------------------------
    // Stage C review fixes: SolidGroundDialog.cs's own control theming (docs/architecture/revit-interactive-dialog.md
    // "Theming and accessibility"). Plain-text scans, for the same cross-platform reason as the checks above.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolidGroundDialogSourceNeverHardcodesABrushInsteadOfDrawingFromThePalette()
    {
        // Review finding, major: the top-level error banner and the buffer panel's inline validation message
        // used to hardcode Brushes.Firebrick, so neither ever varied with the resolved DialogPalette (Light,
        // Dark, or High Contrast). DialogTheme.cs is the one place literal Brushes.* values may legitimately
        // appear (it is what defines each palette); SolidGroundDialog.cs itself must always draw from the
        // palette its constructor already resolved.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        Assert.DoesNotContain("Brushes.", dialogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogSourceSetsControlBackgroundOnEveryButtonTextBoxComboBoxAndListBox()
    {
        // Review finding, blocker: WPF's Control.Background is not an inherited dependency property (unlike
        // Foreground), so the Window's own themed Background never reaches a descendant TextBox/ComboBox/
        // ListBox on its own -- each needed its own explicit Background alongside its existing Foreground.
        // Review finding, major (follow-up): the same gap applied identically to every Button -- Button is
        // also a Control whose Background is not inherited -- but the six buttons (findButton,
        // findParcelButton, cancelButton, backButton, nextButton, createButton) were left out of the original
        // fix, so under the Dark palette each button's white ControlText Foreground rendered over WPF's
        // unthemed default (light) button face. Thirteen such controls exist today: addressTextBox, the
        // geocode ListBox, findButton, findParcelButton, the parcel ListBox, bufferTextBox,
        // pointBudgetTextBox, levelComboBox, toposolidTypeComboBox, and the navigation bar's
        // cancel/back/next/create buttons.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        int controlBackgroundAssignmentCount = Regex.Count(dialogSource, Regex.Escape("Background = palette.ControlBackground"));

        Assert.Equal(13, controlBackgroundAssignmentCount);
    }

    [Fact]
    public void SolidGroundDialogSourceThemesEveryComboBoxDropdownPopupBackground()
    {
        // Review finding, major: setting Background on a ComboBox themes only its closed selection box.
        // WPF's stock (non-Fluent) ComboBox control template paints the separately templated dropdown popup's
        // own Border from the SystemColors.WindowBrushKey dynamic resource, never from a TemplateBinding to
        // the control's own Background -- so, unthemed, the popup kept the OS's plain light surface while its
        // item text still inherited the ComboBox's own (correctly themed) Foreground, an unreadable
        // white-on-white combination under the Dark palette. Both levelComboBox and toposolidTypeComboBox
        // must locally override that resource key.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        int popupBackgroundOverrideCount = Regex.Count(
            dialogSource,
            Regex.Escape("Resources[SystemColors.WindowBrushKey] = palette.ControlBackground"));

        Assert.Equal(2, popupBackgroundOverrideCount);
    }

    [Fact]
    public void SolidGroundDialogSourceSetsControlTextForegroundOnEveryButtonComboBoxTextBoxAndListBox()
    {
        // Review finding, minor: findButton, findParcelButton, levelComboBox, and toposolidTypeComboBox never
        // set an explicit Foreground, unlike every sibling control of the same kind, so they fell back to
        // palette.WindowText by inheritance instead of the palette.ControlText every other themed
        // TextBox/ListBox/Button uses. Thirteen controls set it today: addressTextBox, findButton, the geocode
        // ListBox, findParcelButton, the parcel ListBox, bufferTextBox, pointBudgetTextBox, levelComboBox,
        // toposolidTypeComboBox, and the navigation bar's cancel/back/next/create buttons.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        int controlTextAssignmentCount = Regex.Count(dialogSource, Regex.Escape("Foreground = palette.ControlText"));

        Assert.Equal(13, controlTextAssignmentCount);
    }

    // ------------------------------------------------------------------------------------------------
    // Stage C review fix: provenance-preview wording accuracy (docs/architecture/revit-interactive-dialog.md
    // "Content model and sections" step 9).
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolidGroundDialogSourceShowsDistinctProvenancePreviewWordingForAGeocodedAddressVersusADirectPointEntry()
    {
        // Review finding, major: the panel used to show one unconditional "the confirmed address will be
        // included in this run's exported provenance record" sentence whenever AoiSource was FindParcel, even
        // though a direct "latitude, longitude" entry never geocodes anything, so
        // AddressParcelProvenanceFactory.Create attaches no address data in that sub-case (addressWasGeocoded
        // is false). The panel must show two mutually exclusive, accurate sentences instead -- one bound to
        // ShowFindParcelGeocodedIntro, one to ShowFindParcelDirectPointIntro -- and the old blanket claim must
        // be gone.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        Assert.Contains("internal bool ShowFindParcelGeocodedIntro", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("internal bool ShowFindParcelDirectPointIntro", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ShowFindParcelGeocodedIntro", dialogSource, StringComparison.Ordinal);
        Assert.Contains("ShowFindParcelDirectPointIntro", dialogSource, StringComparison.Ordinal);
        Assert.Contains("No address will be attached to this run's exported provenance record.", dialogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("came from an interactive address/parcel lookup", dialogSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // Stage C review fixes: Highlight contrast and numeric-binding culture
    // (docs/architecture/revit-interactive-dialog.md "Theming and accessibility").
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("LightPalette")]
    [InlineData("DarkPalette")]
    public void DialogPaletteHighlightMeetsWcagAaTextContrastAgainstWindow(string paletteFieldName)
    {
        // Review finding, major: Highlight is used as Foreground for real, non-large warning/status text (the
        // busy banner and both point-budget warning texts in SolidGroundDialog.cs), always directly over this
        // palette's own Window background (Control.Background is not inherited, but none of those three
        // TextBlocks sets its own Background). LightPalette's original #0078D4 measured only roughly 4.08:1
        // against #F3F3F3 -- short of the WCAG AA 4.5:1 normal-text threshold DialogTheme.cs already holds
        // Error to -- and was never checked. This independently recomputes the standard sRGB-to-linear WCAG
        // relative-luminance contrast formula from the literal hex values committed in DialogTheme.cs, rather
        // than trusting a code comment, so a future palette edit can't silently regress below the bar again.
        string themeSource = ReadDialogFile("DialogTheme.cs");
        string paletteBlock = ExtractPaletteBlock(themeSource, paletteFieldName);

        (byte R, byte G, byte B) window = ExtractRgbFromSolidColorBrush(paletteBlock, "Window");
        (byte R, byte G, byte B) highlight = ExtractRgbFromSolidColorBrush(paletteBlock, "Highlight");

        double contrastRatio = ContrastRatio(window, highlight);

        Assert.True(
            contrastRatio >= 4.5,
            $"Expected {paletteFieldName}'s Highlight-vs-Window WCAG AA contrast ratio to be at least 4.5:1; was {contrastRatio:F2}:1.");
    }

    [Fact]
    public void SolidGroundDialogSourceSetsWindowLanguageFromCurrentCultureOnce()
    {
        // Review finding, minor: bufferTextBox/pointBudgetTextBox two-way bind TextBox.Text directly to the
        // double/int-typed BufferMeters/PointBudget properties with no Converter/ConverterCulture, so WPF's
        // own implicit numeric conversion resolves its CultureInfo from this Window's own Language property --
        // hardcoded to en-US by default, regardless of the OS's configured culture -- not from
        // CultureInfo.CurrentCulture. Language is an inherited dependency property, so setting it once on the
        // root Window, alongside the existing "read once, at construction" theme/high-contrast reads, is
        // sufficient for every descendant control.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        Assert.Contains(
            "Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);",
            dialogSource,
            StringComparison.Ordinal);
    }

    private static string ReadAllDialogSourceConcatenated()
    {
        Assert.True(Directory.Exists(DialogDirectory), $"Missing directory: {DialogDirectory}");
        string[] files = [.. Directory.EnumerateFiles(DialogDirectory, "*.cs", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)];
        Assert.NotEmpty(files);

        return string.Join('\n', files.Select(File.ReadAllText));
    }

    /// <summary>Reads exactly one named file under <see cref="DialogDirectory"/>, for checks scoped to a single file rather than the whole concatenated Dialog/ source.</summary>
    private static string ReadDialogFile(string fileName)
    {
        string path = Path.Combine(DialogDirectory, fileName);
        Assert.True(File.Exists(path), $"Missing file: {path}");

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Returns the exact <c>{ ... }</c> extent of the first method whose signature is
    /// <paramref name="methodSignature"/>, found by counting balanced braces from that signature's own opening
    /// brace -- not a fixed-size character window -- so this cannot bleed into a neighboring method's body
    /// either by stopping too early or reading too far.
    /// </summary>
    private static string ExtractMethodBody(string source, string methodSignature)
    {
        int signatureIndex = source.IndexOf(methodSignature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Expected to find '{methodSignature}'.");

        int openBraceIndex = source.IndexOf('{', signatureIndex);
        Assert.True(openBraceIndex >= 0, $"Expected an opening brace after '{methodSignature}'.");

        int depth = 0;
        for (int i = openBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return source[openBraceIndex..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced braces while extracting the body of '{methodSignature}'.");
    }

    /// <summary>
    /// Returns the exact <c>(...)</c> extent of <c>"{paletteFieldName} = new(...)"</c>'s argument list, found
    /// by counting balanced parentheses from its own opening paren -- the same technique
    /// <see cref="ExtractMethodBody"/> uses for braces -- so a <c>DialogPalette</c> record's own property
    /// values can be read without bleeding into a neighboring palette field's declaration.
    /// </summary>
    private static string ExtractPaletteBlock(string source, string paletteFieldName)
    {
        string signature = paletteFieldName + " = new(";
        int signatureIndex = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Expected to find '{signature}'.");

        int openParenIndex = signatureIndex + signature.Length - 1;
        int depth = 0;
        for (int i = openParenIndex; i < source.Length; i++)
        {
            if (source[i] == '(')
            {
                depth++;
            }
            else if (source[i] == ')')
            {
                depth--;
                if (depth == 0)
                {
                    return source[openParenIndex..(i + 1)];
                }
            }
        }

        throw new InvalidOperationException($"Unbalanced parens while extracting '{paletteFieldName}'.");
    }

    /// <summary>Reads the literal <c>0xRR, 0xGG, 0xBB</c> triple out of <c>"{propertyName}: new SolidColorBrush(Color.FromRgb(0xRR, 0xGG, 0xBB))"</c> within <paramref name="paletteBlock"/>.</summary>
    private static (byte R, byte G, byte B) ExtractRgbFromSolidColorBrush(string paletteBlock, string propertyName)
    {
        Match match = Regex.Match(
            paletteBlock,
            $@"{Regex.Escape(propertyName)}:\s*new SolidColorBrush\(Color\.FromRgb\(0x([0-9A-Fa-f]{{2}}),\s*0x([0-9A-Fa-f]{{2}}),\s*0x([0-9A-Fa-f]{{2}})\)\)");
        Assert.True(match.Success, $"Expected to find '{propertyName}: new SolidColorBrush(Color.FromRgb(0xRR, 0xGG, 0xBB))'.");

        byte r = Convert.ToByte(match.Groups[1].Value, 16);
        byte g = Convert.ToByte(match.Groups[2].Value, 16);
        byte b = Convert.ToByte(match.Groups[3].Value, 16);
        return (r, g, b);
    }

    /// <summary>The standard WCAG 2.x relative-luminance-based contrast ratio (always &gt;= 1.0) between two sRGB colors, order-independent.</summary>
    private static double ContrastRatio((byte R, byte G, byte B) first, (byte R, byte G, byte B) second)
    {
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        double lighter = Math.Max(firstLuminance, secondLuminance);
        double darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>The standard WCAG 2.x relative luminance of an sRGB color (sRGB -&gt; linear -&gt; the ITU-R BT.709 luma weights).</summary>
    private static double RelativeLuminance((byte R, byte G, byte B) color)
    {
        static double Linearize(byte channel)
        {
            double normalized = channel / 255.0;
            return normalized <= 0.03928 ? normalized / 12.92 : Math.Pow((normalized + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Linearize(color.R)) + (0.7152 * Linearize(color.G)) + (0.0722 * Linearize(color.B));
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
