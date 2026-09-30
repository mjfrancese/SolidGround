using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using Microsoft.Win32;
using SolidGround.Core.Configuration;
using SolidGround.Core.Processing;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Core.Units;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Settings;

/// <summary>Explicit choices that change the in-memory settings draft.</summary>
internal enum SettingsRecoveryAction
{
    RestoreDefaults,
    ReapplyDraft,
}

/// <summary>Small modal editor for persistent operator preferences. It never receives a Revit document.</summary>
internal sealed class SettingsDialog : Window
{
    private readonly Dictionary<string, FrameworkElement> automationElements = new(StringComparer.Ordinal);
    private RevitSettings current;
    private UiSettingsDraft draft;
    private readonly TextBox pointBudget;
    private readonly TextBox extension;
    private readonly TextBox exportDirectory;
    private readonly TextBox exportBaseName;
    private readonly TextBox timeout;
    private readonly TextBox nearbyRadius;
    private readonly TextBox coverageFloor;
    private readonly ComboBox simplificationMethod;
    private readonly ComboBox outputUnit;
    private readonly ComboBox displayFormat;
    private readonly ComboBox acquisitionMode;
    private readonly TextBox ascPath;
    private readonly TextBox prjPath;
    private readonly TextBox sidecarPath;
    private readonly TextBox localParcelPath;
    private readonly TextBox localParcelLabel;
    private readonly TextBox localParcelLicense;
    private readonly TextBox countyRegistryPath;
    private readonly CheckBox countyAuthorization;
    private readonly TextBox countyName;
    private readonly TextBox countyGeoid;
    private readonly TextBox countyServiceUrl;
    private readonly TextBox countyAttribution;
    private readonly TextBox countyLicense;
    private readonly ComboBox countyLayer;
    private readonly ComboBox countyParcelIdField;
    private readonly ComboBox countySitusAddressField;
    private readonly ComboBox countyLegalDescriptionField;
    private readonly TextBlock countyMetadataStatus;
    private readonly ComboBox geocoderProvider;
    private readonly PasswordBox openTopographyKey;
    private readonly PasswordBox geocodioKey;
    private readonly PasswordBox esriKey;
    private readonly TextBlock sessionKeyStatus;
    private readonly TextBlock error;
    private readonly Button saveCountyRegistration;
    private readonly Func<SettingsRecoveryAction, bool>? confirmRecovery;
    private DistanceDisplayFormat extensionFormat;
    private CountyParcelServiceMetadata? countyMetadata;
    private static readonly HttpClient CountyMetadataHttpClient = new();
    private static readonly TimeSpan CountyMetadataDeadline = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? countyMetadataFetchCancellation;
    private int countyMetadataRevision;
    private bool suppressCountyServiceUrlInvalidation;

    private SettingsDialog(Window? owner, UiSettingsDraft draft, RevitSettings current, DialogPalette? palette, Func<SettingsRecoveryAction, bool>? confirmRecovery = null)
    {
        this.draft = draft;
        this.current = current;
        this.confirmRecovery = confirmRecovery;
        Owner = owner;
        Title = "SolidGround settings";
        Width = 720;
        Height = 580;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        ClampSettingsWorkArea();

        DialogPalette colors = palette ?? new DialogPalette(SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.ControlTextBrush, SystemColors.WindowBrush, SystemColors.HighlightBrush, SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.ActiveBorderBrush);
        Background = colors.Window;
        Foreground = colors.WindowText;
        DialogControlStyles.Apply(this, colors);
        Grid shell = new() { Margin = new Thickness(18) };
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.Children.Add(new TextBlock { Text = "Preferences and source setup", FontSize = 20, Margin = new Thickness(0, 0, 0, 12) });

        TabControl pages = new();
        Grid.SetRow(pages, 1);
        pointBudget = Text(current.Request.Simplification.PointBudget.ToString(CultureInfo.InvariantCulture));
        extensionFormat = current.DistanceDisplayFormat;
        extension = Text(DistanceDisplayConverter.FormatMeters(current.TerrainExtensionMeters, extensionFormat));
        outputUnit = Choice([LengthUnit.UsSurveyFoot, LengthUnit.InternationalFoot, LengthUnit.Meter], current.Request.OutputUnit);
        displayFormat = Choice(Enum.GetValues<DistanceDisplayFormat>(), current.DistanceDisplayFormat);
        displayFormat.SelectionChanged += (_, _) => ChangeExtensionFormat();
        pages.Items.Add(Page("Terrain", Panel(
            Label("Output unit"), outputUnit, Label("Distance display"), displayFormat,
            Label("Maximum terrain points"), pointBudget, Label("Terrain beyond property line (selected display units)"), extension,
            new TextBlock { Text = "The extension affects terrain only; the legal parcel boundary remains unchanged.", TextWrapping = TextWrapping.Wrap })));

        acquisitionMode = Choice([TerrainAcquisitionMode.Fetch, TerrainAcquisitionMode.Process], current.Request.Mode);
        ascPath = Text(current.Request.Process?.Asc ?? string.Empty);
        prjPath = Text(current.Request.Process?.Prj ?? string.Empty);
        sidecarPath = Text(current.Request.Process?.SourceJson ?? string.Empty);
        localParcelPath = Text(current.AddressAndParcel.LocalParcelFilePath ?? string.Empty);
        localParcelLabel = Text(current.AddressAndParcel.LocalParcelFileSourceLabel ?? string.Empty);
        localParcelLicense = Text(current.AddressAndParcel.LocalParcelFileLicenseDisclaimerText ?? string.Empty, true);
        countyRegistryPath = Text(current.AddressAndParcel.CountyRegistryPath ?? string.Empty);
        countyAuthorization = new CheckBox { Content = "I am authorized to use this county parcel service and accept its license/disclaimer.", IsChecked = current.AddressAndParcel.CountyServiceAuthorizedUseAcknowledged };
        countyName = Text(string.Empty);
        countyGeoid = Text(current.AddressAndParcel.CountyGeoidOverride ?? string.Empty);
        countyServiceUrl = Text(string.Empty);
        countyServiceUrl.TextChanged += (_, _) => InvalidateCountyMetadataForChangedUrl();
        countyAttribution = Text(string.Empty, true);
        countyLicense = Text(string.Empty, true);
        countyLayer = new ComboBox { MinWidth = 360, DisplayMemberPath = nameof(CountyParcelServiceLayerMetadata.DisplayName) };
        countyLayer.SelectionChanged += (_, _) => PopulateCountyFieldChoices();
        countyParcelIdField = new ComboBox { MinWidth = 360 };
        countySitusAddressField = new ComboBox { MinWidth = 360 };
        countyLegalDescriptionField = new ComboBox { MinWidth = 360 };
        countyMetadataStatus = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = colors.GrayText };
        Button fetchCountyMetadata = new() { Content = "Fetch service metadata", Margin = new Thickness(0, 4, 6, 4) };
        fetchCountyMetadata.Click += async (_, _) => await FetchCountyMetadataAsync();
        saveCountyRegistration = new Button { Content = "Save county registration", Margin = new Thickness(0, 4, 6, 4), IsEnabled = false };
        saveCountyRegistration.Click += (_, _) => SaveCountyRegistration();
        geocoderProvider = Choice(Enum.GetValues<AddressGeocoderProvider>(), current.AddressAndParcel.GeocoderProvider);
        openTopographyKey = new PasswordBox { MinWidth = 360 };
        geocodioKey = new PasswordBox { MinWidth = 360 };
        esriKey = new PasswordBox { MinWidth = 360 };
        sessionKeyStatus = new TextBlock { Text = SessionApiKeyOverrides.HasOpenTopography ? "A session key is active until Revit exits or you clear it." : "No session key is active; an environment key may still be available.", TextWrapping = TextWrapping.Wrap };
        Button useKey = new() { Content = "Use key for this Revit session", Margin = new Thickness(0, 4, 6, 4) };
        useKey.Click += (_, _) => UseSessionKey();
        Button clearKey = new() { Content = "Clear session key", Margin = new Thickness(0, 4, 6, 4) };
        clearKey.Click += (_, _) => { SessionApiKeyOverrides.ClearOpenTopography(); openTopographyKey.Password = string.Empty; sessionKeyStatus.Text = "No session key is active; an environment key may still be available."; };
        pages.Items.Add(Page("Sources", Panel(
            Label("Elevation mode"), acquisitionMode, Label("Local raster (.asc)"), FileField(ascPath, "AAIGrid (*.asc)|*.asc|All files|*.*"),
            Label("Projection sidecar (.prj)"), FileField(prjPath, "Projection (*.prj)|*.prj|All files|*.*"), Label("Source metadata sidecar (.source.json)"), FileField(sidecarPath, "Source metadata (*.json)|*.json|All files|*.*"),
            Label("Local parcel file (GeoJSON or WKT)"), FileField(localParcelPath, "Parcel data (*.geojson;*.json;*.wkt)|*.geojson;*.json;*.wkt|All files|*.*"), Label("Local source label"), localParcelLabel,
            Label("Local license or disclaimer"), localParcelLicense,
            Label("County registry file"), FileField(countyRegistryPath, "County registry (*.json)|*.json|All files|*.*"),
            new Separator { Margin = new Thickness(0, 12, 0, 8) },
            new TextBlock { Text = "County ArcGIS parcel service", FontWeight = FontWeights.SemiBold },
            new TextBlock { Text = "Fetch the county's advertised metadata, choose the parcel layer and fields, then save a local registration. No county configuration requires JSON editing.", TextWrapping = TextWrapping.Wrap },
            Label("County source identity"), countyName, Label("County GEOID (five digits)"), countyGeoid,
            Label("ArcGIS FeatureServer or MapServer URL"), countyServiceUrl, fetchCountyMetadata, countyMetadataStatus,
            Label("Parcel layer"), countyLayer, Label("Parcel ID field"), countyParcelIdField,
            Label("Situs address field"), countySitusAddressField, Label("Legal-description field"), countyLegalDescriptionField,
            Label("County attribution (shown with parcel results)"), countyAttribution,
            Label("County license/disclaimer (shown verbatim with parcel results)"), countyLicense,
            countyAuthorization, saveCountyRegistration,
            Label("OpenTopography session key"), openTopographyKey, sessionKeyStatus, Horizontal(useKey, clearKey),
            Label("Geocoder provider"), geocoderProvider,
            Label("Geocodio session key"), geocodioKey, KeyButtons(geocodioKey, SessionApiKeyOverrides.UseGeocodio, SessionApiKeyOverrides.ClearGeocodio),
            Label("Esri session key"), esriKey, KeyButtons(esriKey, SessionApiKeyOverrides.UseEsri, SessionApiKeyOverrides.ClearEsri),
            new TextBlock { Text = "Keys are available only until this Revit session ends and are never written to settings, logs, exports, or provenance.", TextWrapping = TextWrapping.Wrap })));

        exportDirectory = Text(current.Request.Output.Directory);
        exportBaseName = Text(current.Request.Output.BaseName);
        pages.Items.Add(Page("Files", Panel(Label("Export folder"), FolderField(exportDirectory), Label("Base name"), exportBaseName)));
        timeout = Text(current.Request.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture));
        nearbyRadius = Text(current.AddressAndParcel.NearbySearchRadiusMeters?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty);
        coverageFloor = Text(current.Request.Simplification.CoverageFloorFraction.ToString("R", CultureInfo.InvariantCulture));
        simplificationMethod = Choice(new[] { SimplificationMethod.CurvatureAware, SimplificationMethod.UniformSampler }, current.Request.Simplification.Method);
        pages.Items.Add(Page("Advanced", Panel(Label("Simplification method"), simplificationMethod, Label("Network timeout (seconds)"), timeout, Label("Nearby parcel search distance (metres; blank uses default)"), nearbyRadius, Label("Sampler coverage fraction (0 through 1)"), coverageFloor, new TextBlock { Text = "The local-origin policy and full process metadata are preserved unless changed by a dedicated source workflow.", TextWrapping = TextWrapping.Wrap })));
        Register("pointBudget", pointBudget); Register("terrainExtension", extension); Register("outputUnit", outputUnit); Register("distanceDisplayFormat", displayFormat);
        Register("acquisitionMode", acquisitionMode); Register("rasterPath", ascPath); Register("projectionPath", prjPath); Register("sourceSidecarPath", sidecarPath);
        Register("localParcelPath", localParcelPath); Register("localParcelLabel", localParcelLabel); Register("localParcelLicense", localParcelLicense);
        Register("countyRegistryPath", countyRegistryPath); Register("countyAuthorization", countyAuthorization); Register("geocoderProvider", geocoderProvider);
        Register("countyName", countyName); Register("countyGeoid", countyGeoid); Register("countyServiceUrl", countyServiceUrl); Register("countyAttribution", countyAttribution); Register("countyLicense", countyLicense);
        Register("countyLayer", countyLayer); Register("countyParcelIdField", countyParcelIdField); Register("countySitusAddressField", countySitusAddressField); Register("countyLegalDescriptionField", countyLegalDescriptionField);
        Register("openTopographyKey", openTopographyKey); Register("geocodioKey", geocodioKey); Register("esriKey", esriKey);
        Register("exportDirectory", exportDirectory); Register("exportBaseName", exportBaseName); Register("networkTimeout", timeout); Register("nearbyRadius", nearbyRadius); Register("coverageFloor", coverageFloor); Register("simplificationMethod", simplificationMethod);
        shell.Children.Add(pages);

        error = new TextBlock { Foreground = colors.Error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) };
        Grid.SetRow(error, 2);
        shell.Children.Add(error);
        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button restoreDefaults = new() { Content = "Restore defaults", MinWidth = 108, Margin = new Thickness(6) };
        restoreDefaults.Click += (_, _) => RestoreDefaults();
        Button reloadSavedSettings = new() { Content = "Reload saved", MinWidth = 104, Margin = new Thickness(6) };
        reloadSavedSettings.Click += (_, _) => ReloadSavedSettings();
        Button reapplyDraft = new() { Content = "Reapply draft", MinWidth = 104, Margin = new Thickness(6) };
        reapplyDraft.Click += (_, _) => ReapplyDraft();
        Button cancel = new() { Content = "Cancel", MinWidth = 100, Margin = new Thickness(6) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        Button save = new() { Content = "Save settings", MinWidth = 120, Margin = new Thickness(6), IsDefault = true };
        save.Style = (Style)Resources[DialogControlStyles.PrimaryButtonStyleKey];
        save.Click += (_, _) => Save();
        Register("restoreDefaults", restoreDefaults); Register("reloadSavedSettings", reloadSavedSettings); Register("reapplyDraft", reapplyDraft);
        footer.Children.Add(restoreDefaults); footer.Children.Add(reloadSavedSettings); footer.Children.Add(reapplyDraft); footer.Children.Add(cancel); footer.Children.Add(save);
        Grid.SetRow(footer, 3); shell.Children.Add(footer);
        Content = shell;
    }

    internal RevitSettings? Result { get; private set; }

    /// <summary>Constructs the actual editor without showing it, for the Windows-only rendered-control lane.</summary>
    internal static SettingsDialog CreateForTesting(UiSettingsDraft draft, RevitSettings current, DialogPalette palette, Func<SettingsRecoveryAction, bool>? confirmRecovery = null) => new(null, draft, current, palette, confirmRecovery);

    /// <summary>Named live controls used by the local WPF binding/accessibility tests.</summary>
    internal IReadOnlyDictionary<string, FrameworkElement> AutomationElements => automationElements;

    internal static RevitSettings? ShowModal(Window? owner, UiSettingsDraft draft, RevitSettings current, DialogPalette? palette = null)
    {
        SettingsDialog dialog = new(owner, draft, current, palette);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    /// <summary>Shows the editor as an owned Revit child without constructing a WPF owner window.</summary>
    internal static RevitSettings? ShowModal(IntPtr ownerHandle, UiSettingsDraft draft, RevitSettings current, DialogPalette palette)
    {
        SettingsDialog dialog = new(null, draft, current, palette);
        if (ownerHandle != IntPtr.Zero)
        {
            _ = new WindowInteropHelper(dialog) { Owner = ownerHandle };
        }
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Save()
    {
        if (!TryBuildSettings(current, out RevitSettings? proposed)) return;
        Persist(proposed);
    }

    private bool TryBuildSettings(RevitSettings baseline, [NotNullWhen(true)] out RevitSettings? proposed)
    {
        proposed = null;
        if (!int.TryParse(pointBudget.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int budget) || budget <= 0)
        {
            error.Text = "Maximum terrain points must be a positive whole number."; pointBudget.Focus(); return false;
        }
        double extensionMeters;
        try { extensionMeters = DistanceDisplayConverter.ParseMeters(extension.Text, (DistanceDisplayFormat)displayFormat.SelectedItem); }
        catch (FormatException)
        {
            error.Text = "Terrain extension must be a finite nonnegative value in the selected display format."; extension.Focus(); return false;
        }
        TerrainAcquisitionMode mode = (TerrainAcquisitionMode)acquisitionMode.SelectedItem;
        if (!int.TryParse(timeout.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int timeoutSeconds) || timeoutSeconds <= 0)
        {
            error.Text = "Network timeout must be a positive whole number of seconds."; timeout.Focus(); return false;
        }
        if (!double.TryParse(coverageFloor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double coverage) || !double.IsFinite(coverage) || coverage is < 0d or > 1d)
        {
            error.Text = "Sampler coverage fraction must be between 0 and 1."; coverageFloor.Focus(); return false;
        }
        double? nearby = null;
        if (!string.IsNullOrWhiteSpace(nearbyRadius.Text) && (!double.TryParse(nearbyRadius.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNearby) || !double.IsFinite(parsedNearby) || parsedNearby <= 0d))
        {
            error.Text = "Nearby parcel search distance must be a positive number of metres or blank."; nearbyRadius.Focus(); return false;
        }
        else if (!string.IsNullOrWhiteSpace(nearbyRadius.Text)) nearby = double.Parse(nearbyRadius.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
        ProcessInputSettings? process = mode == TerrainAcquisitionMode.Process
            ? (baseline.Request.Process ?? new ProcessInputSettings { Asc = ascPath.Text }) with { Asc = ascPath.Text, Prj = BlankAsNull(prjPath.Text), SourceJson = BlankAsNull(sidecarPath.Text) }
            : null;
        RevitAddressAndParcelSettings address = baseline.AddressAndParcel with
        {
            GeocoderProvider = (AddressGeocoderProvider)geocoderProvider.SelectedItem, NearbySearchRadiusMeters = nearby,
            CountyRegistryPath = BlankAsNull(countyRegistryPath.Text), CountyServiceAuthorizedUseAcknowledged = countyAuthorization.IsChecked == true, LocalParcelFilePath = BlankAsNull(localParcelPath.Text), LocalParcelFileSourceLabel = BlankAsNull(localParcelLabel.Text), LocalParcelFileLicenseDisclaimerText = BlankAsNull(localParcelLicense.Text),
        };
        proposed = baseline with
        {
            Request = baseline.Request with { Mode = mode, Process = process, OutputUnit = (LengthUnit)outputUnit.SelectedItem, NetworkTimeoutSeconds = timeoutSeconds, Simplification = baseline.Request.Simplification with { PointBudget = budget, CoverageFloorFraction = coverage, Method = (SimplificationMethod)simplificationMethod.SelectedItem }, Output = baseline.Request.Output with { Directory = exportDirectory.Text, BaseName = exportBaseName.Text } },
            AddressAndParcel = address, TerrainExtensionMeters = extensionMeters, DistanceDisplayFormat = (DistanceDisplayFormat)displayFormat.SelectedItem,
        };
        return true;
    }

    private void Persist(RevitSettings proposed)
    {
        try
        {
            draft = UiSettingsStore.Save(draft, proposed);
            current = proposed;
            if (!string.IsNullOrWhiteSpace(openTopographyKey.Password))
            {
                SessionApiKeyOverrides.UseOpenTopography(openTopographyKey.Password);
                openTopographyKey.Password = string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(geocodioKey.Password)) SessionApiKeyOverrides.UseGeocodio(geocodioKey.Password);
            if (!string.IsNullOrWhiteSpace(esriKey.Password)) SessionApiKeyOverrides.UseEsri(esriKey.Password);
            Result = proposed; DialogResult = true; Close();
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            error.Text = ex is SettingsFileConflictException
                ? ex.Message + " Use Reload saved to discard this draft, or Reapply draft to save these controls over the latest file."
                : ex.Message;
        }
    }

    private void RestoreDefaults()
    {
        if (!ConfirmRecovery(SettingsRecoveryAction.RestoreDefaults)) return;
        RevitSettings defaults = UiSettingsStore.CreateDefault();
        current = defaults;
        draft = draft with { Settings = defaults };
        ApplySettings(defaults);
        error.Text = "Defaults are staged in this dialog only. Choose Save settings to write them.";
    }

    private void ReloadSavedSettings()
    {
        if (!UiSettingsStore.TryLoad(draft.Path, out UiSettingsDraft? loaded, out string? loadError))
        {
            error.Text = loadError ?? "Saved settings could not be reloaded.";
            return;
        }

        draft = loaded!;
        current = RevitSettingsIo.RebaseInputPaths(draft.Settings, draft.Path);
        ApplySettings(current);
        error.Text = "Saved settings reloaded. Unsaved changes in this dialog were discarded.";
    }

    private void ReapplyDraft()
    {
        if (!ConfirmRecovery(SettingsRecoveryAction.ReapplyDraft)) return;
        if (!UiSettingsStore.TryLoad(draft.Path, out UiSettingsDraft? loaded, out string? loadError))
        {
            error.Text = loadError ?? "Saved settings could not be reloaded for reapply.";
            return;
        }

        RevitSettings latest = RevitSettingsIo.RebaseInputPaths(loaded!.Settings, loaded.Path);
        if (!TryBuildSettings(latest, out RevitSettings? proposed)) return;
        draft = loaded;
        current = latest;
        Persist(proposed);
    }

    private static TabItem Page(string header, UIElement content) => new() { Header = header, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
    private static StackPanel Panel(params UIElement[] children) { StackPanel panel = new() { Margin = new Thickness(12) }; foreach (UIElement child in children) panel.Children.Add(child); return panel; }
    private static TextBlock Label(string text) => new() { Text = text, Margin = new Thickness(0, 8, 0, 2) };
    private static TextBox Text(string text, bool multiline = false)
    {
        // FrameworkElement.MinHeight rejects NaN (unlike Width/Height's Auto sentinel). Leave the
        // default alone for one-line editors and give only the multi-line notice editor a minimum.
        TextBox box = new() { Text = text, MinWidth = 360, TextWrapping = multiline ? TextWrapping.Wrap : TextWrapping.NoWrap, AcceptsReturn = multiline };
        if (multiline) box.MinHeight = 64;
        return box;
    }
    private static ComboBox Choice<T>(IEnumerable<T> values, T selected) { ComboBox box = new() { ItemsSource = values.ToArray(), SelectedItem = selected, MinWidth = 240 }; return box; }
    private static StackPanel Horizontal(params UIElement[] children) { StackPanel panel = new() { Orientation = Orientation.Horizontal }; foreach (UIElement child in children) panel.Children.Add(child); return panel; }
    private static StackPanel FileField(TextBox box, string filter)
    {
        Button browse = new() { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => { OpenFileDialog dialog = new() { Filter = filter, CheckFileExists = true }; if (dialog.ShowDialog() == true) box.Text = dialog.FileName; };
        return Horizontal(box, browse);
    }
    private static StackPanel FolderField(TextBox box)
    {
        Button browse = new() { Content = "Browse…", Margin = new Thickness(6, 0, 0, 0) };
        browse.Click += (_, _) => { OpenFolderDialog dialog = new(); if (dialog.ShowDialog() == true) box.Text = dialog.FolderName; };
        return Horizontal(box, browse);
    }
    private static StackPanel KeyButtons(PasswordBox key, Action<string> use, Action clear)
    {
        Button useButton = new() { Content = "Use for this Revit session", Margin = new Thickness(0, 4, 6, 4) };
        useButton.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(key.Password)) { use(key.Password); key.Password = string.Empty; } };
        Button clearButton = new() { Content = "Clear session key", Margin = new Thickness(0, 4, 6, 4) };
        clearButton.Click += (_, _) => { clear(); key.Password = string.Empty; };
        return Horizontal(useButton, clearButton);
    }
    private static string? BlankAsNull(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private bool ConfirmRecovery(SettingsRecoveryAction action)
    {
        if (confirmRecovery is not null) return confirmRecovery(action);
        string prompt = action == SettingsRecoveryAction.RestoreDefaults
            ? "Replace every settings control with SolidGround defaults? Nothing is written until you choose Save settings."
            : "Reapply the current controls over the latest saved settings? This explicitly replaces saved values represented by this dialog.";
        return MessageBox.Show(this, prompt, "SolidGround settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void ApplySettings(RevitSettings settings)
    {
        pointBudget.Text = settings.Request.Simplification.PointBudget.ToString(CultureInfo.InvariantCulture);
        extensionFormat = settings.DistanceDisplayFormat;
        displayFormat.SelectedItem = extensionFormat;
        extension.Text = DistanceDisplayConverter.FormatMeters(settings.TerrainExtensionMeters, extensionFormat);
        outputUnit.SelectedItem = settings.Request.OutputUnit;
        acquisitionMode.SelectedItem = settings.Request.Mode;
        ascPath.Text = settings.Request.Process?.Asc ?? string.Empty;
        prjPath.Text = settings.Request.Process?.Prj ?? string.Empty;
        sidecarPath.Text = settings.Request.Process?.SourceJson ?? string.Empty;
        localParcelPath.Text = settings.AddressAndParcel.LocalParcelFilePath ?? string.Empty;
        localParcelLabel.Text = settings.AddressAndParcel.LocalParcelFileSourceLabel ?? string.Empty;
        localParcelLicense.Text = settings.AddressAndParcel.LocalParcelFileLicenseDisclaimerText ?? string.Empty;
        countyRegistryPath.Text = settings.AddressAndParcel.CountyRegistryPath ?? string.Empty;
        countyAuthorization.IsChecked = settings.AddressAndParcel.CountyServiceAuthorizedUseAcknowledged;
        countyGeoid.Text = settings.AddressAndParcel.CountyGeoidOverride ?? string.Empty;
        geocoderProvider.SelectedItem = settings.AddressAndParcel.GeocoderProvider;
        exportDirectory.Text = settings.Request.Output.Directory;
        exportBaseName.Text = settings.Request.Output.BaseName;
        timeout.Text = settings.Request.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
        nearbyRadius.Text = settings.AddressAndParcel.NearbySearchRadiusMeters?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty;
        coverageFloor.Text = settings.Request.Simplification.CoverageFloorFraction.ToString("R", CultureInfo.InvariantCulture);
        simplificationMethod.SelectedItem = settings.Request.Simplification.Method;

        countyName.Text = string.Empty;
        countyAttribution.Text = string.Empty;
        countyLicense.Text = string.Empty;
        suppressCountyServiceUrlInvalidation = true;
        try { countyServiceUrl.Text = string.Empty; }
        finally { suppressCountyServiceUrlInvalidation = false; }
        InvalidateCountyMetadataForChangedUrl();
    }

    private void ClampSettingsWorkArea()
    {
        Rect workArea = SystemParameters.WorkArea;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Width = Math.Min(Width, MaxWidth);
        Height = Math.Min(Height, MaxHeight);
    }

    private void UseSessionKey()
    {
        if (string.IsNullOrWhiteSpace(openTopographyKey.Password)) { error.Text = "Enter a nonblank key before using it for this Revit session."; openTopographyKey.Focus(); return; }
        SessionApiKeyOverrides.UseOpenTopography(openTopographyKey.Password);
        openTopographyKey.Password = string.Empty;
        sessionKeyStatus.Text = "A session key is active until Revit exits or you clear it.";
        error.Text = string.Empty;
    }

    /// <summary>Lets the Windows-only rendered-control lane populate the same live selectors without a network request.</summary>
    internal void SetCountyMetadataForTesting(CountyParcelServiceMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        InvalidateCountyMetadataForChangedUrl();
        suppressCountyServiceUrlInvalidation = true;
        try { countyServiceUrl.Text = metadata.ServiceBaseUri.AbsoluteUri; }
        finally { suppressCountyServiceUrlInvalidation = false; }
        ApplyCountyMetadata(metadata);
    }

    private async Task FetchCountyMetadataAsync()
    {
        if (!Uri.TryCreate(countyServiceUrl.Text.Trim(), UriKind.Absolute, out Uri? serviceUri)
            || !string.Equals(serviceUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(serviceUri.Query)
            || !string.IsNullOrEmpty(serviceUri.Fragment))
        {
            error.Text = "Enter an absolute https ArcGIS service URL without a query or fragment before fetching metadata.";
            countyServiceUrl.Focus();
            return;
        }

        countyMetadataFetchCancellation?.Cancel();
        CancellationTokenSource requestCancellation = new();
        requestCancellation.CancelAfter(CountyMetadataDeadline);
        countyMetadataFetchCancellation = requestCancellation;
        int requestRevision = ++countyMetadataRevision;
        saveCountyRegistration.IsEnabled = false;
        try
        {
            countyMetadataStatus.Text = "Retrieving county service metadata…";
            CountyParcelServiceMetadata metadata = await new CountyParcelServiceMetadataClient(CountyMetadataHttpClient)
                .FetchAsync(serviceUri, requestCancellation.Token);
            if (requestCancellation.IsCancellationRequested || requestRevision != countyMetadataRevision) return;
            ApplyCountyMetadata(metadata);
            error.Text = string.Empty;
        }
        catch (Exception ex) when (ex is HttpRequestException or FormatException or JsonException or TaskCanceledException)
        {
            if (requestRevision != countyMetadataRevision) return;
            if (requestCancellation.IsCancellationRequested)
            {
                countyMetadata = null;
                saveCountyRegistration.IsEnabled = false;
                countyMetadataStatus.Text = "County metadata retrieval timed out. Check the service and try again.";
                error.Text = "County metadata fetch exceeded its 15-second deadline.";
                return;
            }
            countyMetadata = null;
            countyLayer.ItemsSource = null;
            countyParcelIdField.ItemsSource = null;
            countySitusAddressField.ItemsSource = null;
            countyLegalDescriptionField.ItemsSource = null;
            countyMetadataStatus.Text = "Could not retrieve usable parcel-layer metadata.";
            error.Text = $"County metadata fetch failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(countyMetadataFetchCancellation, requestCancellation)) countyMetadataFetchCancellation = null;
            requestCancellation.Dispose();
        }
    }

    private void InvalidateCountyMetadataForChangedUrl()
    {
        if (suppressCountyServiceUrlInvalidation) return;
        countyMetadataFetchCancellation?.Cancel();
        ++countyMetadataRevision;
        countyMetadata = null;
        countyLayer.ItemsSource = null;
        countyParcelIdField.ItemsSource = null;
        countySitusAddressField.ItemsSource = null;
        countyLegalDescriptionField.ItemsSource = null;
        saveCountyRegistration.IsEnabled = false;
        countyMetadataStatus.Text = "County service URL changed. Fetch metadata again before saving its registration.";
    }

    private void ApplyCountyMetadata(CountyParcelServiceMetadata metadata)
    {
        countyMetadata = metadata;
        countyLayer.ItemsSource = metadata.Layers.OrderBy(layer => layer.LayerIndex).ToArray();
        countyLayer.SelectedIndex = countyLayer.Items.Count == 0 ? -1 : 0;
        saveCountyRegistration.IsEnabled = countyLayer.SelectedIndex >= 0;
        countyMetadataStatus.Text = $"Loaded {metadata.Layers.Count.ToString(CultureInfo.InvariantCulture)} advertised layer(s).";
    }

    private void PopulateCountyFieldChoices()
    {
        if (countyLayer.SelectedItem is not CountyParcelServiceLayerMetadata layer)
        {
            return;
        }

        string[] fields = layer.Fields.OrderBy(field => field, StringComparer.OrdinalIgnoreCase).ToArray();
        countyParcelIdField.ItemsSource = fields;
        countySitusAddressField.ItemsSource = fields;
        countyLegalDescriptionField.ItemsSource = new[] { string.Empty }.Concat(fields).ToArray();
        countyParcelIdField.SelectedItem = PreferField(fields, "parcel", "pin", "account", "apn") ?? fields.FirstOrDefault();
        countySitusAddressField.SelectedItem = PreferField(fields, "situs", "address", "siteaddr") ?? fields.FirstOrDefault();
        countyLegalDescriptionField.SelectedItem = PreferField(fields, "legal", "description") ?? string.Empty;
    }

    private void SaveCountyRegistration()
    {
        if (countyMetadata is null || countyLayer.SelectedItem is not CountyParcelServiceLayerMetadata layer)
        {
            error.Text = "Fetch county service metadata and select a parcel layer before saving a registration.";
            return;
        }

        string? parcelId = countyParcelIdField.SelectedItem as string;
        string? situsAddress = countySitusAddressField.SelectedItem as string;
        string? legalDescription = countyLegalDescriptionField.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(countyName.Text) || !IsCountyGeoid(countyGeoid.Text)
            || string.IsNullOrWhiteSpace(parcelId) || string.IsNullOrWhiteSpace(situsAddress)
            || string.IsNullOrWhiteSpace(countyAttribution.Text) || string.IsNullOrWhiteSpace(countyLicense.Text)
            || countyAuthorization.IsChecked != true)
        {
            error.Text = "County source identity, a five-digit GEOID, parcel ID and situs-address fields, attribution, license/disclaimer, and explicit authorized use are required. Legal description is optional.";
            return;
        }

        if (!Uri.TryCreate(countyServiceUrl.Text.Trim(), UriKind.Absolute, out Uri? serviceUri)
            || Uri.Compare(serviceUri, countyMetadata.ServiceBaseUri, UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            error.Text = "Fetch metadata again after changing the county service URL.";
            return;
        }

        CountyParcelRegistryEntry entry = new()
        {
            Geoid = countyGeoid.Text.Trim(),
            DisplayName = countyName.Text.Trim() + " — " + countyAttribution.Text.Trim(),
            ServiceBaseUrl = countyMetadata.ServiceBaseUri.AbsoluteUri.TrimEnd('/'),
            LayerIndex = layer.LayerIndex,
            FieldMap = new CountyParcelFieldMap { ParcelId = parcelId, SitusAddress = situsAddress, LegalDescription = string.IsNullOrWhiteSpace(legalDescription) ? null : legalDescription },
            LicenseDisclaimerText = countyLicense.Text.Trim(),
        };

        try
        {
            CountyParcelServiceMetadataValidator.ValidateRegistration(countyMetadata, entry);
            string path = ResolveCountyRegistryPath();
            IReadOnlyList<CountyParcelRegistryEntry> entries = File.Exists(path)
                ? CountyParcelRegistry.Load(path).EntriesByGeoid.Values
                    .Where(existing => !string.Equals(existing.Geoid, entry.Geoid, StringComparison.Ordinal))
                    .Append(entry)
                    .OrderBy(existing => existing.Geoid, StringComparer.Ordinal)
                    .ToArray()
                : [entry];
            CountyParcelRegistry.Write(path, new CountyParcelRegistryDocument
            {
                SchemaVersion = CountyParcelRegistry.CurrentSchemaVersion,
                Counties = entries,
            });
            _ = CountyParcelRegistry.Load(path);
            countyRegistryPath.Text = path;
            countyMetadataStatus.Text = "County registration saved locally. Save settings to use this registry in future runs.";
            error.Text = string.Empty;
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.Text = $"County registration could not be saved: {ex.Message}";
        }
    }

    private string ResolveCountyRegistryPath()
    {
        if (!string.IsNullOrWhiteSpace(countyRegistryPath.Text)) return countyRegistryPath.Text.Trim();
        string folder = Path.GetDirectoryName(draft.Path) ?? throw new InvalidOperationException("The per-user settings path has no parent folder.");
        return Path.Combine(folder, "county-parcel-registry.json");
    }

    private static string? PreferField(IEnumerable<string> fields, params string[] fragments) => fields.FirstOrDefault(field => fragments.Any(fragment => field.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    private static bool IsCountyGeoid(string value) => value.Length == 5 && value.All(char.IsAsciiDigit);

    private void ChangeExtensionFormat()
    {
        DistanceDisplayFormat next = (DistanceDisplayFormat)displayFormat.SelectedItem;
        try
        {
            double meters = DistanceDisplayConverter.ParseMeters(extension.Text, extensionFormat);
            extension.Text = DistanceDisplayConverter.FormatMeters(meters, next);
            extensionFormat = next;
        }
        catch (FormatException)
        {
            // Keep invalid text visible for correction; Save will focus it and report the field error.
            extensionFormat = next;
        }
    }

    private void Register(string name, FrameworkElement element)
    {
        element.Name = name;
        AutomationProperties.SetAutomationId(element, name);
        automationElements.Add(name, element);
    }
}
