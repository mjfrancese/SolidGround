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
        // Re-check finding, minor, fixed: ResultSetTruncated is stale parcel state exactly like UsedNearbyTier
        // (its sibling reset one line above in the real source) -- a truncation caveat left over from an
        // earlier confirmed point must not linger for a search that has not yet run against the new one.
        Assert.Contains("ResultSetTruncated = false", methodBody, StringComparison.Ordinal);
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
        //
        // Corrected (not weakened) for the Stage E live-session finding, Stage F fix (docs/architecture/
        // revit-interactive-dialog.md "Stage E live-session findings and Stage F fixes", defect A): both
        // Geocode's success branches, and CanGoNext's own read here, now go through the *property*
        // (ConfirmedAddressText), not the bare field directly -- CommunityToolkit.Mvvm's own analyzer
        // (MVVMTK0034) disallows referencing an [ObservableProperty]-annotated field directly once it is one,
        // and the property's own NotifyCanExecuteChangedFor(nameof(NextCommand)) is what re-evaluates Next
        // regardless of statement order relative to SelectedGeocodeCandidate's assignment -- see the dedicated
        // structural check immediately below this test for the attribute-level guarantee that makes that true.
        string concatenatedSource = ReadAllDialogSourceConcatenated();

        string canGoNextBody = ExtractMethodBody(concatenatedSource, "private bool CanGoNext() => CurrentStep switch");
        Assert.Contains("ConfirmedAddressText", canGoNextBody, StringComparison.Ordinal);

        string geocodeBody = ExtractMethodBody(concatenatedSource, "private void Geocode()");
        int confirmedAddressTextAssignmentCount = Regex.Count(geocodeBody, Regex.Escape("ConfirmedAddressText = addressText;"));
        Assert.Equal(2, confirmedAddressTextAssignmentCount);
    }

    [Fact]
    public void SolidGroundDialogViewModelConfirmedAddressTextIsObservableAndReEvaluatesNextCommandItself()
    {
        // Defect A (SolidGround Issue #31 live-session evidence, docs/architecture/revit-interactive-dialog.md
        // "Stage E live-session findings and Stage F fixes"): Next stayed disabled after a successful Find, for
        // both an address and a "latitude, longitude" pair. Root cause: _confirmedAddressText was a bare,
        // non-notifying field; both of Geocode's success branches assigned SelectedGeocodeCandidate (which
        // re-evaluates NextCommand via its own NotifyCanExecuteChangedFor) *before* assigning
        // _confirmedAddressText, so CanGoNext's AddressEntry case (`AddressText.Trim() == _confirmedAddressText`)
        // was re-checked one statement too early, against the *previous* confirmed value, and nothing
        // re-evaluated NextCommand a second time afterward. The fix must be structural, not a mere reordering of
        // the two statements (a future third dependency added carelessly in the wrong order would silently
        // reintroduce the same bug): _confirmedAddressText must itself be an [ObservableProperty] carrying its
        // own [NotifyCanExecuteChangedFor(nameof(NextCommand))], so whichever success branch assigns it (in
        // whatever order relative to SelectedGeocodeCandidate) re-evaluates NextCommand itself, at the point
        // both pieces of state are already final.
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        int fieldIndex = RequireIndex(viewModelSource, "private string? _confirmedAddressText;");
        const int MaxPrecedingDistance = 400; // comfortably covers both attribute lines immediately above it.
        string precedingWindow = viewModelSource[Math.Max(0, fieldIndex - MaxPrecedingDistance)..fieldIndex];

        Assert.Contains("[ObservableProperty]", precedingWindow, StringComparison.Ordinal);
        Assert.Contains("[NotifyCanExecuteChangedFor(nameof(NextCommand))]", precedingWindow, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelCanGoNextEnforcesTheFullInclusivePointBudgetRangeNotJustPositive()
    {
        // Re-check finding, minor, fixed (SolidGround Issue #31, PH3-4, Stage D): this step's own CanGoNext
        // case previously enforced only `PointBudget > 0`, so a value above
        // SimplificationSettings.MaxPointBudget was caught only after the whole ten-step wizard completed and
        // Create ran CreateToposolidCommand's post-merge effectiveSettings.Request.Validate() call, discarding
        // the entire interactive session with no retained state for a mistake this step could have caught
        // immediately instead. CanGoNext's PointBudget case must now depend on PointBudgetRangeErrorText, which
        // subsumes the old `> 0` check (SimplificationSettings.MinPointBudget is 1).
        string concatenatedSource = ReadAllDialogSourceConcatenated();

        string canGoNextBody = ExtractMethodBody(concatenatedSource, "private bool CanGoNext() => CurrentStep switch");
        Assert.Contains(
            "SolidGroundDialogStep.PointBudget => PointBudgetRangeErrorText is null,", canGoNextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("PointBudget > 0", canGoNextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelPointBudgetRangeErrorTextReusesSimplificationSettingsBoundsNotADuplicatedLiteral()
    {
        // Re-check finding, minor, fixed: reuses SimplificationSettings' own MinPointBudget/MaxPointBudget
        // constants -- the identical bound TerrainRequestSettings.Validate() already enforces for every
        // settings-file-sourced value -- rather than a second, dialog-local literal that could silently drift
        // out of sync with it.
        //
        // "public", not "internal" (corrected, not weakened, for the Stage E live-session finding, Stage F fix:
        // docs/architecture/revit-interactive-dialog.md "Stage E live-session findings and Stage F fixes",
        // defect D -- see SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal
        // below for the general check this property's own fix is one instance of).
        string concatenatedSource = ReadAllDialogSourceConcatenated();

        int propertyIndex = RequireIndex(concatenatedSource, "public string? PointBudgetRangeErrorText =>");
        const int MaxFollowingDistance = 200; // covers the range condition itself (measured: 154 chars).
        string window = concatenatedSource[propertyIndex..Math.Min(concatenatedSource.Length, propertyIndex + MaxFollowingDistance)];

        Assert.Contains("SimplificationSettings.MinPointBudget", window, StringComparison.Ordinal);
        Assert.Contains("SimplificationSettings.MaxPointBudget", window, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogSourceBindsAnAlwaysOnPointBudgetRangeErrorAlongsideTheRevitIniWarning()
    {
        // Re-check finding: the view-model's always-on range check needs a bound control the operator can
        // actually see -- mirroring the buffer panel's own BufferErrorText/TextPresenceToVisibility idiom,
        // not the Revit.ini-native warning's tolerant-by-design ShowPointBudgetWarning/BooleanToVisibility one.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string panelBody = ExtractMethodBody(dialogSource, "private static StackPanel BuildPointBudgetPanel(DialogPalette palette)");

        Assert.Contains(
            "new Binding(nameof(SolidGroundDialogViewModel.PointBudgetRangeErrorText))", panelBody, StringComparison.Ordinal);
        Assert.Contains("Converter = TextPresenceToVisibility", panelBody, StringComparison.Ordinal);
        Assert.Contains("rangeErrorText", panelBody, StringComparison.Ordinal);
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
        //
        // "public", not "internal" (corrected, not weakened, for the Stage E live-session finding, Stage F fix:
        // docs/architecture/revit-interactive-dialog.md "Stage E live-session findings and Stage F fixes",
        // defect B -- both sentences showed at once, regardless of path, because a WPF Binding cannot resolve
        // an internal property; see SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal
        // below for the general check these two properties' own fix is an instance of).
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        Assert.Contains("public bool ShowFindParcelGeocodedIntro", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public bool ShowFindParcelDirectPointIntro", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ShowFindParcelGeocodedIntro", dialogSource, StringComparison.Ordinal);
        Assert.Contains("ShowFindParcelDirectPointIntro", dialogSource, StringComparison.Ordinal);
        Assert.Contains("No address will be attached to this run's exported provenance record.", dialogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("came from an interactive address/parcel lookup", dialogSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // SolidGround Issue #35 (PH3-8): OpenTopography attribution shown once per run
    // (docs/architecture/source-licensing-and-attribution.md's "Dialog: attribution shown once per run"
    // section). OpenTopography's own attribution was never shown anywhere in this dialog before this change,
    // on either AoiSource path, because nothing threaded TerrainRequestSettings.Mode (or any elevation-source
    // fact) into it at all. ShowFetchModeSourceAttribution/ShowProcessModeSourceNote are the exact, mutually
    // exclusive, Mode-gated conditions for the two new sentences.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolidGroundDialogSourceShowsTheOpenTopographyAttributionInFetchModeAndAConditionalSourceNoteInProcessMode()
    {
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        // "public", not "internal" -- covered generically going forward by
        // SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal below, but pinned
        // explicitly here too, matching this file's own precedent for every other provenance-preview property.
        Assert.Contains("public bool ShowFetchModeSourceAttribution", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public bool ShowProcessModeSourceNote", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ShowFetchModeSourceAttribution", dialogSource, StringComparison.Ordinal);
        Assert.Contains("ShowProcessModeSourceNote", dialogSource, StringComparison.Ordinal);

        // Review finding, major: pin the exact Visibility bindings (control, property, and converter), not
        // just bare-substring presence anywhere in the file -- a swapped, missing, or wrong-converter binding
        // (for example TextPresenceToVisibility, which always collapses a bool to Collapsed) would otherwise
        // still pass every check above, silently. Same idiom as
        // SolidGroundDialogSourceBindsAnAlwaysOnPointBudgetRangeErrorAlongsideTheRevitIniWarning, above.
        string panelBody = ExtractMethodBody(dialogSource, "private static StackPanel BuildProvenancePreviewPanel(DialogPalette palette)");
        Assert.Contains(
            "fetchModeSourceAttribution.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowFetchModeSourceAttribution)) { Converter = BooleanToVisibility });",
            panelBody, StringComparison.Ordinal);
        Assert.Contains(
            "processModeSourceNote.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowProcessModeSourceNote)) { Converter = BooleanToVisibility });",
            panelBody, StringComparison.Ordinal);

        // A stable substring of the OpenTopography constant, resolved through the same using this file relies
        // on, plus the using itself -- SolidGroundDialog.cs shows OpenTopographyUsgs1mSource.AttributionNotice
        // directly (a compile-time constant), never a second, duplicated copy of the notice text.
        Assert.Contains("OpenTopographyUsgs1mSource.AttributionNotice", dialogSource, StringComparison.Ordinal);
        Assert.Contains("using SolidGround.Core.Sources.OpenTopography;", dialogSource, StringComparison.Ordinal);

        // Deliberately conditional wording, not a promised attribution string: a process-mode run's actual
        // source sidecar is not read at dialog-construction time.
        Assert.Contains("This run processes an already-downloaded elevation file.", dialogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogHostThreadsTheConfiguredModeIntoSolidGroundDialogInputs()
    {
        // The provenance-preview panel decides which of the two new sentences to show from
        // TerrainRequestSettings.Mode, read from the settings file before the dialog opens (independent of
        // AoiSource) -- ShowModal must thread it into SolidGroundDialogInputs so the view-model can see it at
        // all.
        string hostSource = ReadDialogFile("SolidGroundDialogHost.cs");
        string inputsSource = ReadDialogFile("SolidGroundDialogInputs.cs");

        Assert.Contains("settings.Request.Mode", hostSource, StringComparison.Ordinal);
        Assert.Contains("TerrainAcquisitionMode Mode", inputsSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // Stage E live-session findings, Stage F fixes (docs/architecture/revit-interactive-dialog.md "Stage E
    // live-session findings and Stage F fixes"): a live Revit 2027 session exercising the Stage D wiring found
    // defects B (step 9 provenance preview showed every conditional sentence at once), C (step 3's
    // zero-candidates message showed even when a candidate was listed), and D (step 5/10's point-budget warning
    // and range-error text never appeared, despite the range rule itself correctly disabling Next). All three
    // share one root cause: SolidGroundDialogViewModel is itself internal, and each of these was a hand-written
    // computed property *also* declared internal, referenced from SolidGroundDialog.cs only through a
    // string-Path `new Binding(nameof(...))`. WPF resolves that kind of binding source against a plain CLR
    // object via reflection, which only ever discovers public members -- an internal property bound this way
    // fails silently (a "BindingExpression path error: '...' property not found" trace line the operator never
    // sees) and the bound dependency property is left at its own default: Visibility.Visible for a Visibility
    // binding (defects B and C: unconditionally shown), or an empty string for a Text binding (defect D:
    // "never appears" -- indistinguishable from no warning applying at all). This was confirmed empirically with
    // a standalone, non-committed WPF reproduction outside this repository (this test project cannot construct
    // a real SolidGroundDialogViewModel/WPF Binding itself -- see this file's own header): an otherwise-identical
    // pair of internal/public bool properties, and internal/public string? properties, bound the same way,
    // showed exactly this split -- the public ones tracked their real value; the internal ones stuck at
    // Visible/empty regardless of it. The buffer panel's own step 4 error text (BufferErrorText) was checked and
    // confirmed unaffected, since it is an [ObservableProperty]-backed, generator-emitted public property, never
    // a hand-written internal one.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolidGroundDialogViewModelShowNoParcelCandidatesMessageIsPublicNotInternal()
    {
        // Defect C: the step 3 "no parcel boundary was found" message showed even when a parcel candidate was
        // listed (and alongside an inline registry error) -- ShowNoParcelCandidatesMessage's own Visibility
        // binding could never resolve while this property was internal, so it stuck at Visibility.Visible
        // regardless of _parcelLookupAttempted/ParcelCandidates.Count's real, correctly-computed values.
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        Assert.Contains("public bool ShowNoParcelCandidatesMessage", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal bool ShowNoParcelCandidatesMessage", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelPointBudgetWarningPropertiesArePublicNotInternal()
    {
        // Defect D: the step 5 (and step 10 preflight-summary) point-budget warning never appeared, even well
        // above the machine's own configured NativeToposolidMaxPointThreshold. ShowPointBudgetWarning gates the
        // warning TextBlock's Visibility; PointBudgetWarningText supplies its Text -- both bindings could never
        // resolve while these were internal, so Text stuck at an empty string (indistinguishable from "no
        // warning applies") regardless of Visibility. PointBudgetRangeErrorText (the sibling always-on range
        // error, also part of defect D) is checked by
        // SolidGroundDialogViewModelPointBudgetRangeErrorTextReusesSimplificationSettingsBoundsNotADuplicatedLiteral
        // above, which this same live-session finding also corrected.
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        Assert.Contains("public bool ShowPointBudgetWarning", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal bool ShowPointBudgetWarning", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("public string? PointBudgetWarningText", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal string? PointBudgetWarningText", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal()
    {
        // The general, forward-looking form of the three checks immediately above: not a fixed list of names,
        // so a *future* hand-written computed property that gets bound the same way, without also being made
        // public, fails this test too. Reads every `nameof(SolidGroundDialogViewModel.X)` argument to a
        // `new Binding(...)` call in SolidGroundDialog.cs -- both the plain literal form
        // (`new Binding(nameof(SolidGroundDialogViewModel.X))`) and the composite/interpolated-path form used
        // for every property-path binding in this file
        // (`new Binding($"{nameof(SolidGroundDialogViewModel.X)}.{nameof(Other.Y)}")`), since WPF's reflection
        // resolves the same first path segment, and so the same public-only visibility rule, either way -- then
        // reads every hand-written `internal`/`public <Type> X => ...`-shaped property declaration in
        // SolidGroundDialogViewModel.cs (an X backed instead by an [ObservableProperty] field or a
        // [RelayCommand] method never appears in that second scan at all -- CommunityToolkit.Mvvm's source
        // generator only ever emits public members for those, so this only ever inspects the hand-written
        // computed properties actually at risk), and asserts none of the bound names is declared internal.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        HashSet<string> boundNames = [.. Regex.Matches(dialogSource, @"new Binding\([^)\n]*nameof\(SolidGroundDialogViewModel\.(\w+)\)")
            .Select(match => match.Groups[1].Value)];
        Assert.NotEmpty(boundNames);

        Dictionary<string, string> declaredAccessibilityByName = [];
        foreach (Match match in Regex.Matches(viewModelSource, @"(?m)^[ \t]*(internal|public)\s+\S+\s+(\w+)\s*=>"))
        {
            declaredAccessibilityByName[match.Groups[2].Value] = match.Groups[1].Value;
        }

        // Guards the guard: if this ever drops to zero, the regex above stopped matching this file's own
        // property-declaration shape (for example a reformat to block-bodied `{ get => ...; }` properties)
        // silently rather than the check just having nothing to flag.
        Assert.NotEmpty(declaredAccessibilityByName);

        List<string> violations = [.. boundNames
            .Where(name => declaredAccessibilityByName.TryGetValue(name, out string? accessibility) && accessibility == "internal")
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.True(
            violations.Count == 0,
            "Expected every SolidGroundDialogViewModel property referenced by a WPF Binding in SolidGroundDialog.cs " +
            "to be declared 'public', not 'internal' (WPF's Binding resolves a plain CLR object's Path by " +
            $"reflection, which only ever discovers public members): {string.Join(", ", violations)}.");
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

    // ------------------------------------------------------------------------------------------------
    // Stage D review fix (major, fixed): BuildParcelSource must never let a bad countyRegistryPath escape
    // ShowModal uncaught -- docs/architecture/revit-interactive-dialog.md "Settings interaction: prefill, not
    // override" and "Result-code mapping".
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void BuildParcelSourceCatchesARegistryLoadFailureAndNeverLetsItEscapeShowModal()
    {
        // Before this fix, CountyParcelRegistry.Load ran with no surrounding try/catch, so a missing or
        // malformed countyRegistryPath threw CountyParcelRegistryFormatException straight out of ShowModal --
        // reaching only CreateToposolidCommand.Execute's generic top-level catch, and blocking the whole
        // dialog (including the unrelated "Use the area in the settings file" AOI path) -- instead of the
        // documented "reported the first time a parcel lookup is attempted, never here at construction time"
        // contract this same method's own doc comment already promised.
        string dialogHostSource = ReadDialogFile("SolidGroundDialogHost.cs");
        string methodBody = ExtractMethodBody(
            dialogHostSource,
            "private static IParcelBoundarySource? BuildParcelSource(RevitAddressAndParcelSettings settings, HttpClient httpClient)");

        int loadIndex = RequireIndex(methodBody, "CountyParcelRegistry.Load(settings.CountyRegistryPath)");
        int catchIndex = RequireIndex(methodBody, "catch (CountyParcelRegistryFormatException");
        Assert.True(catchIndex > loadIndex, "Expected a catch (CountyParcelRegistryFormatException ...) after the Load( call site.");

        // The caught failure must still reach the operator, not be silently swallowed: it becomes a deferred
        // FailedParcelSource whose own FindAsync raises AutoGeoidCountyParcelSourceException -- caught inline
        // by SolidGroundDialogViewModel.FindParcel's existing, unchanged "catch (ParcelBoundarySourceException
        // ex)" clause, exactly like any other parcel-lookup failure.
        Assert.Contains("return new FailedParcelSource(ex.Message);", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedParcelSourceThrowsAutoGeoidCountyParcelSourceExceptionFromFindAsync()
    {
        string dialogHostSource = ReadDialogFile("SolidGroundDialogHost.cs");
        string classBody = ExtractMethodBody(
            dialogHostSource,
            "private sealed class FailedParcelSource(string message) : IParcelBoundarySource");

        Assert.Contains("throw new AutoGeoidCountyParcelSourceException(message);", classBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogClassRemarksDescribeStageDWiringNotStageCsUnreachableState()
    {
        // Stage D wires this type in (SolidGroundDialogHost.ShowModal constructs and shows it from
        // CreateToposolidCommand.ExecuteCore's Stage 0.5), so the class-level doc comment inherited from
        // Stage C's own commit -- written when the type genuinely was "not constructed anywhere ... remains
        // unreachable from a running add-in" -- must no longer claim that (review finding, minor, fixed).
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        Assert.DoesNotContain("remains unreachable from a running add-in", dialogSource, StringComparison.Ordinal);
        Assert.DoesNotContain("is a later stage's own scope", dialogSource, StringComparison.Ordinal);
        Assert.Contains("SolidGroundDialogHost.ShowModal", dialogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogResultViewModelAndInputsDocCommentsDescribeStageDWiringNotStageCUnwiredState()
    {
        // The same class of staleness the test immediately above guards against in SolidGroundDialog.cs was
        // also present, byte-for-byte, in three sibling doc comments that this fix's own review missed the
        // first time: SolidGroundDialogResult.cs said ShowModal was "not implemented in this stage, which
        // builds and populates this type but wires it into no command"; SolidGroundDialogViewModel.cs said it
        // was "never constructed except by a later stage's own SolidGroundDialogHost.ShowModal (not
        // implemented yet) ... not reachable from CreateToposolidCommand"; SolidGroundDialogInputs.cs said it
        // was gathered by "a later stage's SolidGroundDialogHost.ShowModal ... Stage C". All three are now
        // false: SolidGroundDialogHost.ShowModal is the real, landed Stage D call site inside
        // CreateToposolidCommand.ExecuteCore's Stage 0.5 (review finding, minor, fixed).
        string resultSource = ReadDialogFile("SolidGroundDialogResult.cs");
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");
        string inputsSource = ReadDialogFile("SolidGroundDialogInputs.cs");

        foreach (string source in new[] { resultSource, viewModelSource, inputsSource })
        {
            Assert.DoesNotContain("not implemented in this stage", source, StringComparison.Ordinal);
            Assert.DoesNotContain("not implemented yet", source, StringComparison.Ordinal);
            Assert.DoesNotContain("a later stage's own", source, StringComparison.Ordinal);
            Assert.DoesNotContain("wires it into no command", source, StringComparison.Ordinal);
            Assert.Contains("SolidGroundDialogHost.ShowModal", source, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("not reachable from", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Stage C:", inputsSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // Stage D: IsCancel/IsDefault (SolidGround Issue #31, PH3-4; see docs/architecture/revit-interactive-dialog.md "Purpose and boundary").
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void SolidGroundDialogSourceSetsIsCancelOnTheCancelButton()
    {
        // Esc invokes cancelButton's own Click -- which, via ButtonBase's own ICommand-execution handling,
        // runs the bound CancelCommand exactly as a mouse click would -- from any control in the dialog.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        Assert.Contains("cancelButton.IsCancel = true;", dialogSource, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateDefaultButtonSetsIsDefaultOnExactlyOnePerStepPrimaryActionAtATime()
    {
        // Never a fixed, always-true IsDefault on more than one of Find/Find parcel/Next/Create at once: the
        // AddressEntry/ParcelCandidates steps each have a real window where their own panel-local search
        // button and the persistent Next button are simultaneously visible and enabled (after a successful
        // search, before Next is pressed), so a fixed assignment would leave Enter's behavior genuinely
        // ambiguous. UpdateDefaultButton instead clears all four, then sets exactly one, every time
        // CurrentStep or NextCommand's own CanExecute result might have changed.
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string methodBody = ExtractMethodBody(dialogSource, "private void UpdateDefaultButton()");

        Assert.Contains("_findButton.IsDefault = false;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_findParcelButton.IsDefault = false;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_nextButton.IsDefault = false;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_createButton.IsDefault = false;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_findButton.IsDefault = true;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_findParcelButton.IsDefault = true;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_nextButton.IsDefault = true;", methodBody, StringComparison.Ordinal);
        Assert.Contains("_createButton.IsDefault = true;", methodBody, StringComparison.Ordinal);
        // Reuses NextCommand's own CanExecute rather than re-deriving CanGoNext's completion rule a second
        // time, so the two can never drift apart on a future step-completion-rule change.
        Assert.Contains("_viewModel.NextCommand.CanExecute(null)", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogRecomputesTheDefaultButtonWheneverCurrentStepOrNextCommandCanExecuteChanges()
    {
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");

        Assert.Contains("viewModel.PropertyChanged += OnViewModelPropertyChanged;", dialogSource, StringComparison.Ordinal);
        Assert.Contains("viewModel.NextCommand.CanExecuteChanged += OnNextCommandCanExecuteChanged;", dialogSource, StringComparison.Ordinal);
        // Unsubscribed on Close, mirroring CloseRequested's own existing discipline immediately above.
        Assert.Contains("viewModel.PropertyChanged -= OnViewModelPropertyChanged;", dialogSource, StringComparison.Ordinal);
        Assert.Contains("viewModel.NextCommand.CanExecuteChanged -= OnNextCommandCanExecuteChanged;", dialogSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // Nearby-parcel fallback tier (SolidGround Issue #31 follow-up: a geocoded point commonly lands a few
    // meters outside its true parcel). See docs/architecture/parcel-boundary-sources.md's "Nearby-parcel
    // fallback tier" and this note's own "Nearby-parcel fallback tier" section.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void FindParcelCallsTheNearbyParcelBoundaryFinderRatherThanTheParcelSourceDirectly()
    {
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");
        string methodBody = ExtractMethodBody(viewModelSource, "private void FindParcel()");

        Assert.Contains("NearbyParcelBoundaryFinder.FindAsync(", methodBody, StringComparison.Ordinal);
        // The direct, single-tier call this replaced -- must be gone, not merely joined by the new one.
        Assert.DoesNotContain("parcelSource.FindAsync(", methodBody, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogViewModelExposesAPublicNearbyTierNoticeTextReferencedFromTheDialog()
    {
        // NearbyTierNoticeText is a hand-written computed property bound by a WPF string-Path Binding, so it
        // must be public, not internal (see SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal,
        // which already covers this generically -- this test additionally pins the property's own existence
        // and its use of the resolved, possibly-configured radius rather than a re-hardcoded "30").
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        Assert.Contains("public string? NearbyTierNoticeText", viewModelSource, StringComparison.Ordinal);
        Assert.DoesNotContain("internal string? NearbyTierNoticeText", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("NearbySearchRadiusMeters", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("new Binding(nameof(SolidGroundDialogViewModel.NearbyTierNoticeText))", dialogSource, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------------------------------------
    // Re-check finding, minor, fixed: ParcelProximityAcquisition.ResultSetTruncated (the nearby tier's own
    // exceededTransferLimit signal) used to be discarded by NearbyParcelBoundaryFinder, so the dialog could
    // never learn -- or tell the operator -- that the shown nearby-parcel candidate list might be an
    // incomplete subset of what the county service actually has within the search radius. See
    // docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier" and this note's own
    // "Nearby-parcel fallback tier (follow-up)" section.
    // ------------------------------------------------------------------------------------------------

    [Fact]
    public void FindParcelSetsResultSetTruncatedFromTheAcquisitionBeforeAssigningParcelCandidates()
    {
        // Mirrors UsedNearbyTier's own existing ordering requirement immediately above it in the real source:
        // ParcelCandidates's setter is what actually re-evaluates NearbyTierNoticeText (via its own
        // NotifyPropertyChangedFor), and that property's getter reads ResultSetTruncated too, so
        // ResultSetTruncated must already hold this lookup's own final value by the time ParcelCandidates is
        // assigned.
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");
        string methodBody = ExtractMethodBody(viewModelSource, "private void FindParcel()");

        int truncatedIndex = RequireIndex(methodBody, "ResultSetTruncated = acquisition.ResultSetTruncated;");
        int candidatesIndex = RequireIndex(methodBody, "ParcelCandidates = new ObservableCollection<ParcelProximityCandidate>(acquisition.Candidates);");
        Assert.True(truncatedIndex < candidatesIndex, "Expected ResultSetTruncated to be set before ParcelCandidates, mirroring UsedNearbyTier's own ordering.");
    }

    [Fact]
    public void NearbyTierNoticeTextAppendsACaveatWhenTheResultSetWasTruncated()
    {
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");
        string propertyText = ExtractExpressionBodyPropertyText(viewModelSource, "public string? NearbyTierNoticeText =>");

        Assert.Contains("ResultSetTruncated", propertyText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParcelProximityAcquisitionResultSetTruncatedIsForwardedFromEitherTier()
    {
        // NearbyParcelBoundaryFinderTests.cs proves the runtime behavior against a fake IParcelBoundarySource;
        // this pins the Core source text directly, since this project never references SolidGround.Core's own
        // test assembly and this dialog-facing test file otherwise only reads Revit-host source.
        string path = Path.Combine(RepositoryRoot, "src", "SolidGround.Core", "Sources", "NearbyParcelBoundaryFinder.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string source = File.ReadAllText(path);

        Assert.Contains("resultSetTruncated: exact.ResultSetTruncated", source, StringComparison.Ordinal);
        Assert.Contains("resultSetTruncated: nearby.ResultSetTruncated", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ParcelCandidateTemplateShowsEachCandidatesDistance()
    {
        string dialogSource = ReadDialogFile("SolidGroundDialog.cs");
        string templateBody = ExtractMethodBody(dialogSource, "private static DataTemplate BuildParcelCandidateTemplate()");

        Assert.Contains("nameof(ParcelProximityCandidate.DistanceMeters)", templateBody, StringComparison.Ordinal);
    }

    [Fact]
    public void ParcelCandidatesAndSelectedParcelCandidateAreTypedAsParcelProximityCandidate()
    {
        // The operator must still explicitly select a parcel -- SelectedParcelCandidate is never assigned a
        // default after a lookup (docs/architecture/revit-interactive-dialog.md "Content model and sections"
        // step 3) -- this only pins the type change ParcelCandidates/SelectedParcelCandidate needed to carry
        // each candidate's own distance through to the bound list.
        string viewModelSource = ReadDialogFile("SolidGroundDialogViewModel.cs");

        Assert.Contains("ObservableCollection<ParcelProximityCandidate> _parcelCandidates", viewModelSource, StringComparison.Ordinal);
        Assert.Contains("ParcelProximityCandidate? _selectedParcelCandidate", viewModelSource, StringComparison.Ordinal);
    }

    [Fact]
    public void RevitAddressAndParcelSettingsDeclaresTheNearbySearchRadiusMetersField()
    {
        string path = Path.Combine(RevitProjectDirectory, "Settings", "RevitAddressAndParcelSettings.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string source = File.ReadAllText(path);

        Assert.Contains("double? NearbySearchRadiusMeters", source, StringComparison.Ordinal);
    }

    [Fact]
    public void RevitSettingsIoDecodesNearbySearchRadiusMetersAndShipsItInTheTemplate()
    {
        string path = Path.Combine(RevitProjectDirectory, "Settings", "RevitSettingsIo.cs");
        Assert.True(File.Exists(path), $"Missing file: {path}");
        string source = File.ReadAllText(path);

        Assert.Contains("\"nearbySearchRadiusMeters\"", source, StringComparison.Ordinal);
        string parseBody = ExtractMethodBody(source, "private static RevitAddressAndParcelSettings ParseAddressAndParcel(JsonNode? node)");
        Assert.Contains("nearbySearchRadiusMeters", parseBody, StringComparison.Ordinal);
        // The template's own documented default: absent/null means "let Core pick its own default", never a
        // second, duplicated "30" literal on the Revit side.
        Assert.Contains("\"nearbySearchRadiusMeters\": null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SolidGroundDialogHostResolvesTheConfiguredRadiusOrFallsBackToTheCoreDefault()
    {
        string hostSource = ReadDialogFile("SolidGroundDialogHost.cs");

        Assert.Contains("NearbySearchRadiusMeters", hostSource, StringComparison.Ordinal);
        Assert.Contains("NearbyParcelBoundaryFinder.DefaultRadiusMeters", hostSource, StringComparison.Ordinal);
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
    /// The same small, self-contained substring-index helper <c>RevitHostFilesTests.RequireIndex</c> already
    /// uses, duplicated here (rather than shared) for the identical reason <see cref="ReadLockFilePackages"/>'s
    /// own doc comment gives: that method is <see langword="private"/> to its own file and this file is
    /// deliberately kept independent of it.
    /// </summary>
    private static int RequireIndex(string source, string needle)
    {
        int index = source.IndexOf(needle, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Expected to find '{needle}'.");
        return index;
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
    /// Returns the exact extent of an expression-bodied member (<c>"... =&gt; ...;"</c>, no braces) whose
    /// signature is <paramref name="propertySignature"/> -- for example <c>"public string? Foo =&gt;"</c> --
    /// found by counting balanced parentheses from the signature's own end and stopping at the first
    /// paren-depth-0 <c>;</c> outside of a string literal, the same balanced-counting technique
    /// <see cref="ExtractMethodBody"/> uses for braces (plus one addition <see cref="ExtractMethodBody"/> does
    /// not need: a bare <c>"</c> toggles a simple in-string flag, so a <c>;</c>/<c>(</c>/<c>)</c> character
    /// that is really just part of an interpolated string's own literal text -- for example
    /// <c>"nearest first; confirm the right one."</c> -- is never mistaken for real code). Used for
    /// <c>NearbyTierNoticeText</c>, which (unlike <c>FindParcel</c>) has no <c>{ }</c> block for
    /// <see cref="ExtractMethodBody"/> to find.
    /// </summary>
    private static string ExtractExpressionBodyPropertyText(string source, string propertySignature)
    {
        int signatureIndex = source.IndexOf(propertySignature, StringComparison.Ordinal);
        Assert.True(signatureIndex >= 0, $"Expected to find '{propertySignature}'.");

        int depth = 0;
        bool inString = false;
        for (int i = signatureIndex; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '"')
            {
                inString = !inString;
            }
            else if (!inString && c == '(')
            {
                depth++;
            }
            else if (!inString && c == ')')
            {
                depth--;
            }
            else if (!inString && c == ';' && depth == 0)
            {
                return source[signatureIndex..(i + 1)];
            }
        }

        throw new InvalidOperationException($"No terminating ';' found while extracting the expression body of '{propertySignature}'.");
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
