namespace SolidGround.Tests;

/// <summary>Cross-platform contract checks for code-built WPF source; rendered-state coverage lives in the local Windows lane.</summary>
public sealed class RevitInteractiveDialogTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Dialog = Path.Combine(Root, "src", "SolidGround.Revit", "Dialog");

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
    }

    [Fact]
    public void DialogUsesManagedVectorPreviewAndNoEmbeddedBrowser()
    {
        string all = string.Join('\n', Directory.EnumerateFiles(Dialog, "*.cs").Select(File.ReadAllText));
        Assert.Contains("class ParcelBoundaryPreview", all, StringComparison.Ordinal);
        Assert.Contains("Canvas", all, StringComparison.Ordinal);
        Assert.DoesNotContain("WebView", all, StringComparison.Ordinal);
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
        Assert.Contains("EditSettings: current => RevitSettingsIo.Edit(dialogOwner, current)", host, StringComparison.Ordinal);
        Assert.Contains("CountyServiceAuthorizedUseAcknowledged", host, StringComparison.Ordinal);
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
