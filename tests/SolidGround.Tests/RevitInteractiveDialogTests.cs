using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SolidGround.Tests;

/// <summary>Cross-platform contract checks for code-built WPF source; rendered-state coverage lives in the local Windows lane.</summary>
public sealed class RevitInteractiveDialogTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Dialog = Path.Combine(Root, "src", "SolidGround.Revit", "Dialog");
    private static readonly string RevitProject = Path.Combine(Root, "src", "SolidGround.Revit");
    private const string MvvmPackage = "CommunityToolkit.Mvvm";
    private const string MvvmVersion = "8.4.2";

    [Fact]
    public void RevitDialogRemainsCodeOnlyAndUsesTheApprovedMvvmPackageShape()
    {
        Assert.Empty(Directory.EnumerateFiles(RevitProject, "*.xaml", SearchOption.AllDirectories));
        XDocument project = XDocument.Load(Path.Combine(RevitProject, "SolidGround.Revit.csproj"));
        XElement package = Assert.Single(project.Descendants("PackageReference"), element => (string?)element.Attribute("Include") == MvvmPackage);
        Assert.Equal(MvvmVersion, (string?)package.Attribute("Version"));
        Assert.Null(package.Attribute("Condition"));
        Assert.Null(package.Parent?.Attribute("Condition"));
        Assert.Equal("true", Assert.Single(project.Descendants("UseWPF")).Value);
        Assert.Empty(project.Descendants("FrameworkReference"));
    }

    [Fact]
    public void MvvmIsRevitOnlyAndPinnedInBothRestoreGraphs()
    {
        string[] projects = [.. Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains("\\bin\\", StringComparison.OrdinalIgnoreCase) && !path.Contains("\\obj\\", StringComparison.OrdinalIgnoreCase))];
        Assert.Equal(Path.Combine(RevitProject, "SolidGround.Revit.csproj"), Assert.Single(projects, path => File.ReadAllText(path).Contains(MvvmPackage, StringComparison.Ordinal)));
        foreach (string lockFile in new[] { "packages.lock.json", "packages.ci.lock.json" })
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RevitProject, lockFile)));
            string source = document.RootElement.GetRawText();
            Assert.Equal(1, Regex.Count(source, "\\\"" + Regex.Escape(MvvmPackage) + "\\\""));
            Assert.Contains("\"resolved\": \"" + MvvmVersion + "\"", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ThemeAndControlStylesKeepHighContrastAndSemanticPaletteCoverage()
    {
        string theme = Read("DialogTheme.cs");
        string styles = Read("DialogControlStyles.cs");
        Assert.Contains("SystemParameters.HighContrast", Read("SolidGroundDialogHost.cs"), StringComparison.Ordinal);
        Assert.Contains("SystemColors.", theme, StringComparison.Ordinal);
        Assert.Contains("UIThemeManager.CurrentTheme", Read("SolidGroundDialogHost.cs"), StringComparison.Ordinal);
        Assert.Contains("PrimaryButtonStyleKey", styles, StringComparison.Ordinal);
        Assert.Contains("ComboBoxTemplate", styles, StringComparison.Ordinal);
        Assert.Contains("ToolTipStyle", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogBindingsTargetPublicViewModelPropertiesAndUseNoInlineBrushes()
    {
        string dialog = Read("SolidGroundDialog.cs");
        string viewModel = Read("SolidGroundDialogViewModel.cs");
        string[] hostSuppliedSummaryBindings = ["RadiusLabel", "LocationAttribution", "NativePointBudgetWarning"];
        foreach (string binding in Regex.Matches(dialog, @"nameof\(SolidGroundDialogViewModel\.([A-Za-z0-9_]+)\)").Select(match => match.Groups[1].Value).Where(name => !name.EndsWith("Command", StringComparison.Ordinal) && !hostSuppliedSummaryBindings.Contains(name, StringComparer.Ordinal)).Distinct(StringComparer.Ordinal))
        {
            bool explicitPublicProperty = Regex.IsMatch(viewModel, @"public\s+(?:[A-Za-z0-9_?.<>]+\s+)+" + Regex.Escape(binding) + @"\b");
            string backingName = "_" + char.ToLowerInvariant(binding[0]) + binding[1..];
            bool generatedPublicProperty = Regex.IsMatch(viewModel, @"\[ObservableProperty\][\s\S]{0,600}" + Regex.Escape(backingName) + @"\b");
            Assert.True(explicitPublicProperty || generatedPublicProperty, $"Binding '{binding}' must target a public property or an [ObservableProperty] backing field.");
        }
        Assert.DoesNotContain("Brushes.", dialog, StringComparison.Ordinal);
        Assert.DoesNotContain("Color.From", dialog, StringComparison.Ordinal);
    }

    [Fact]
    public void LookupPathsAreAsyncBoundedAndInvalidateStaleConfirmedState()
    {
        string source = Read("SolidGroundDialogViewModel.cs");
        Assert.Contains("timeout.CancelAfter", source, StringComparison.Ordinal);
        Assert.Contains("catch (AddressGeocoderException", source, StringComparison.Ordinal);
        Assert.Contains("catch (ParcelBoundarySourceException", source, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult", source, StringComparison.Ordinal);
        Assert.Contains("_flow.ChangeInput()", source, StringComparison.Ordinal);
        Assert.Contains("SelectedGeocodeCandidate = null", source, StringComparison.Ordinal);
        Assert.Contains("SelectedParcelCandidate = null", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogRetainsSourceAttributionNearbySafetyAndTypedParcelSelection()
    {
        string source = Read("SolidGroundDialogViewModel.cs");
        string host = Read("SolidGroundDialogHost.cs");
        Assert.Contains("NearbyParcelBoundaryFinder.FindAsync", source, StringComparison.Ordinal);
        Assert.Contains("ParcelProximityCandidate? _selectedParcelCandidate", source, StringComparison.Ordinal);
        Assert.Contains("Nearby parcels are never selected automatically", Read("SolidGroundDialog.cs"), StringComparison.Ordinal);
        Assert.Contains("CountyServiceAuthorizedUseAcknowledged", host, StringComparison.Ordinal);
        Assert.Contains("TerrainAcquisitionMode Mode", Read("SolidGroundDialogInputs.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void GuidedDialogHasOnlyLocationParcelAndReviewStages()
    {
        string step = Read("SolidGroundDialogStep.cs");
        Assert.Contains("Location,", step, StringComparison.Ordinal);
        Assert.Contains("Parcel,", step, StringComparison.Ordinal);
        Assert.Contains("Review,", step, StringComparison.Ordinal);
        Assert.DoesNotContain("PreflightSummary", step, StringComparison.Ordinal);
        Assert.DoesNotContain("PointBudget", step, StringComparison.Ordinal);
    }

    [Fact]
    public void LookupsAreAsyncAndUseCoreRevisionTickets()
    {
        string source = Read("SolidGroundDialogViewModel.cs");
        Assert.Contains("async Task FindAsync(CancellationToken", source, StringComparison.Ordinal);
        Assert.Contains("async Task FindParcelsAsync(CancellationToken", source, StringComparison.Ordinal);
        Assert.Contains("LocationParcelLookupTicket", source, StringComparison.Ordinal);
        Assert.Contains("TryApplyLocations(ticket", source, StringComparison.Ordinal);
        Assert.Contains("TryApplyParcels(ticket", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetAwaiter().GetResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogRequiresExplicitParcelConfirmationAndNeverBuffersLegalGeometry()
    {
        string source = Read("SolidGroundDialogViewModel.cs");
        Assert.Contains("TryUseParcel", source, StringComparison.Ordinal);
        Assert.Contains("ParcelBoundaryAoiFactory.FromCandidate(parcel.Candidate, LinearDistance.Meters(0))", source, StringComparison.Ordinal);
        Assert.Contains("WriteSharedCoordinatesIfAbsent = false", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DialogResultCarriesOptionalEffectiveSettingsForTheCommand()
    {
        string inputs = Read("SolidGroundDialogInputs.cs");
        string result = Read("SolidGroundDialogResult.cs");
        Assert.Contains("Func<RevitSettings, RevitSettings?>? EditSettings", inputs, StringComparison.Ordinal);
        Assert.Contains("RevitSettings? EffectiveSettings = null", result, StringComparison.Ordinal);
        Assert.Contains("ExplicitArea", Read("SolidGroundDialogStep.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void DialogUsesManagedVectorPreviewAndNoEmbeddedBrowser()
    {
        string all = string.Join('\n', Directory.EnumerateFiles(Dialog, "*.cs").Select(File.ReadAllText));
        Assert.Contains("class ParcelBoundaryPreview", all, StringComparison.Ordinal);
        Assert.Contains("Canvas", all, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView", all, StringComparison.Ordinal);
        Assert.Contains("selected.Candidate.Boundary", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
        Assert.Contains("StrokeDashArray", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
        Assert.Contains("SelectedGeocodeCandidate", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
        Assert.Contains("DialogPalette palette", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
        Assert.Contains("ParcelTerrainPreview.Build", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
        Assert.Contains("approximate terrain extension", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
        Assert.Contains("N ↑", Read("ParcelBoundaryPreview.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void WindowUsesVisibleLabelsDetailedParcelRowsAndOperatorInitiatedStatelessLinks()
    {
        string source = Read("SolidGroundDialog.cs");
        Assert.Contains("Field(\"Street address\"", source, StringComparison.Ordinal);
        Assert.Contains("Field(\"Latitude\"", source, StringComparison.Ordinal);
        Assert.Contains("Field(\"West longitude\"", source, StringComparison.Ordinal);
        Assert.Contains("Browse GeoJSON or WKT", source, StringComparison.Ordinal);
        Assert.Contains("Candidate.ComputedAreaSquareMeters", source, StringComparison.Ordinal);
        Assert.Contains("Candidate.SourceIdentity", source, StringComparison.Ordinal);
        Assert.Contains("SelectedParcelSourceTerms", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SolidGroundDialogViewModel.LocationAttribution)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SolidGroundDialogViewModel.RadiusLabel)", source, StringComparison.Ordinal);
        Assert.Contains("nameof(SolidGroundDialogViewModel.NativePointBudgetWarning)", source, StringComparison.Ordinal);
        Assert.Contains("new GridLength(55d, GridUnitType.Star)", source, StringComparison.Ordinal);
        Assert.Contains("openstreetmap.org/?mlat=", source, StringComparison.Ordinal);
        Assert.Contains("portal.opentopography.org", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView", source, StringComparison.Ordinal);
        Assert.Contains("UseShellExecute = true", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowKeepsFooterFixedAndHasOneVisibleDefaultAndOneCancelButton()
    {
        string source = Read("SolidGroundDialog.cs");
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", source, StringComparison.Ordinal);
        Assert.Contains("Add(root, BuildFooter(palette), 4)", source, StringComparison.Ordinal);
        Assert.Contains("cancel.IsCancel = true", source, StringComparison.Ordinal);
        Assert.Contains("_findButton.IsDefault = _findButton.Visibility == Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("_useLocationButton.IsDefault = _useLocationButton.Visibility == Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("_useParcelButton.IsDefault = _useParcelButton.Visibility == Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("_createButton.IsDefault = _createButton.Visibility == Visibility.Visible", source, StringComparison.Ordinal);
        Assert.Contains("ClampToWorkingArea", source, StringComparison.Ordinal);
        Assert.Contains("MinWidth = Math.Min(640d, MaxWidth)", source, StringComparison.Ordinal);
        Assert.Contains("EntryModeVisibilityConverter(LocationEntryMode.BoundingBox)", source, StringComparison.Ordinal);
        Assert.Contains("EntryModeVisibilityConverter(LocationEntryMode.Radius)", source, StringComparison.Ordinal);
        Assert.Contains("EntryModeVisibilityConverter(LocationEntryMode.LocalGeometry)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EstimateAndReachabilityAreBoundedCoreHelpers()
    {
        string estimate = File.ReadAllText(Path.Combine(Root, "src", "SolidGround.Core", "Workflow", "PreFetchEstimate.cs"));
        string reachability = File.ReadAllText(Path.Combine(Root, "src", "SolidGround.Core", "Workflow", "ReachabilityProbe.cs"));
        Assert.Contains("ClipRegionFactory.BuildFetchEnvelope", estimate, StringComparison.Ordinal);
        Assert.Contains("ParcelFetchEnvelopePlanner.Build(parcel, terrainMargin)", estimate, StringComparison.Ordinal);
        Assert.Contains("FromProcessAreaOfInterest", estimate, StringComparison.Ordinal);
        Assert.Contains("openTopographyRequestCount: 0", estimate, StringComparison.Ordinal);
        Assert.Contains("new(HttpMethod.Get, endpoint)", reachability, StringComparison.Ordinal);
        Assert.DoesNotContain("while", reachability, StringComparison.Ordinal);
        Assert.Contains("CancelAfter(timeout)", reachability, StringComparison.Ordinal);
        Assert.Contains("could not complete safely", reachability, StringComparison.Ordinal);
    }

    [Fact]
    public void PaletteInjectedDialogConstructorDoesNotReferenceRevitThemeApis()
    {
        string source = Read("SolidGroundDialog.cs");
        Assert.Contains("SolidGroundDialog(SolidGroundDialogViewModel viewModel, DialogPalette palette)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UIThemeManager", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Autodesk.Revit", source, StringComparison.Ordinal);
        Assert.Contains("DialogTheme.Resolve(UIThemeManager.CurrentTheme", Read("SolidGroundDialogHost.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ViewModelProvidesAnInternalDiagnosticPropertyForBindingPathFailureCoverage()
    {
        Assert.Contains("internal string BindingPathErrorDiagnostic", Read("SolidGroundDialogViewModel.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void HostSuppliesARealSettingsEditorCallbackAndGatesUnacknowledgedCountyUse()
    {
        string host = Read("SolidGroundDialogHost.cs");
        Assert.Contains("current => RevitSettingsIo.Edit(dialogOwner, current, palette)", host, StringComparison.Ordinal);
        Assert.Contains("CountyServiceAuthorizedUseAcknowledged", host, StringComparison.Ordinal);
        Assert.Contains("BuildLookupServices", host, StringComparison.Ordinal);
        Assert.Contains("SessionApiKeyOverrides.GeocodioProvider()", host, StringComparison.Ordinal);
        Assert.Contains("SessionApiKeyOverrides.EsriProvider()", host, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsResumeReconfiguresLookupServicesWithoutDiscardingUnrelatedReviewChoices()
    {
        string source = Read("SolidGroundDialogViewModel.cs");
        Assert.Contains("ReconfigureLookupServices", source, StringComparison.Ordinal);
        Assert.Contains("lookupConfigurationChanged", source, StringComparison.Ordinal);
        Assert.Contains("PointBudget = edited.Request.Simplification.PointBudget", source, StringComparison.Ordinal);
        Assert.Contains("SelectedOutputUnit = edited.Request.OutputUnit", source, StringComparison.Ordinal);
        Assert.Contains("_flow.ChangeSource()", source, StringComparison.Ordinal);
    }

    private static string Read(string name) => File.ReadAllText(Path.Combine(Dialog, name));

    private static string FindRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "SolidGround.slnx"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not find repository root.");
    }
}
