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
    StartNewSettings,
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
    private readonly TextBox processSourceName;
    private readonly TextBox processDataset;
    private readonly TextBox processVerticalDatum;
    private readonly ComboBox processVerticalUnit;
    private readonly TextBox processGeoid;
    private readonly TextBox processCollectionStart;
    private readonly TextBox processCollectionEnd;
    private readonly TextBox processQualityLevel;
    private readonly ComboBox originKind;
    private readonly TextBox originX;
    private readonly TextBox originY;
    private readonly TextBox originZ;
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
    private double canonicalExtensionMeters;
    private bool extensionEdited;
    private bool updatingExtensionText;
    private CountyParcelServiceMetadata? countyMetadata;
    private CountyParcelRegistryDocument? stagedCountyRegistry;
    private string? stagedCountyRegistryPath;
    private static readonly HttpClient CountyMetadataHttpClient = new();
    private static readonly TimeSpan CountyMetadataDeadline = TimeSpan.FromSeconds(15);
    private CancellationTokenSource? countyMetadataFetchCancellation;
    private int countyMetadataRevision;
    private bool suppressCountyServiceUrlInvalidation;
    private bool isShownModally;
    private bool savingBlockedUntilStartNew;
    private readonly string? initialRecoveryError;

    private SettingsDialog(Window? owner, UiSettingsDraft draft, RevitSettings current, DialogPalette? palette, Func<SettingsRecoveryAction, bool>? confirmRecovery = null, bool savingBlockedUntilStartNew = false, string? initialRecoveryError = null)
    {
        this.draft = draft;
        this.current = current;
        this.confirmRecovery = confirmRecovery;
        this.savingBlockedUntilStartNew = savingBlockedUntilStartNew;
        this.initialRecoveryError = initialRecoveryError;
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
        canonicalExtensionMeters = current.TerrainExtensionMeters;
        extension = Text(DistanceDisplayConverter.FormatMeters(canonicalExtensionMeters, extensionFormat));
        extension.TextChanged += (_, _) => { if (!updatingExtensionText) extensionEdited = true; };
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
        processSourceName = Text(current.Request.Process?.SourceName ?? string.Empty);
        processDataset = Text(current.Request.Process?.Dataset ?? string.Empty);
        processVerticalDatum = Text(current.Request.Process?.VerticalDatum ?? string.Empty);
        processVerticalUnit = new ComboBox { ItemsSource = new object?[] { null, LengthUnit.UsSurveyFoot, LengthUnit.InternationalFoot, LengthUnit.Meter }, SelectedItem = current.Request.Process?.VerticalUnit, MinWidth = 240 };
        processGeoid = Text(current.Request.Process?.Geoid ?? string.Empty);
        processCollectionStart = Text(current.Request.Process?.CollectionStart ?? string.Empty);
        processCollectionEnd = Text(current.Request.Process?.CollectionEnd ?? string.Empty);
        processQualityLevel = Text(current.Request.Process?.QualityLevel ?? string.Empty);
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
        saveCountyRegistration = new Button { Content = "Use this county source", Margin = new Thickness(0, 4, 6, 4), IsEnabled = false };
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
        originKind = Choice(Enum.GetValues<LocalOriginKind>(), current.Request.LocalOrigin.Kind);
        originX = Text(current.Request.LocalOrigin.X.ToString("R", CultureInfo.InvariantCulture));
        originY = Text(current.Request.LocalOrigin.Y.ToString("R", CultureInfo.InvariantCulture));
        originZ = Text(current.Request.LocalOrigin.Z.ToString("R", CultureInfo.InvariantCulture));
        pages.Items.Add(Page("Advanced", Panel(Label("Simplification method"), simplificationMethod, Label("Network timeout (seconds)"), timeout, Label("Nearby parcel search distance (metres; blank uses default)"), nearbyRadius, Label("Sampler coverage fraction (0 through 1)"), coverageFloor,
            new Separator { Margin = new Thickness(0, 12, 0, 8) }, new TextBlock { Text = "Local origin", FontWeight = FontWeights.SemiBold }, Label("Origin kind"), originKind, Label("Explicit origin X (source horizontal units)"), originX, Label("Explicit origin Y (source horizontal units)"), originY, Label("Explicit origin Z (source elevation units)"), originZ,
            new TextBlock { Text = "Explicit coordinates are used only when Origin kind is Explicit; they are stored at full precision in source units.", TextWrapping = TextWrapping.Wrap },
            new Separator { Margin = new Thickness(0, 12, 0, 8) }, new TextBlock { Text = "Local process source metadata", FontWeight = FontWeights.SemiBold }, new TextBlock { Text = "Used for a local .asc input when a source sidecar does not provide these details.", TextWrapping = TextWrapping.Wrap },
            Label("Source name"), processSourceName, Label("Dataset"), processDataset, Label("Vertical datum"), processVerticalDatum, Label("Vertical unit"), processVerticalUnit, Label("Geoid model"), processGeoid, Label("Collection start (yyyy-MM-dd)"), processCollectionStart, Label("Collection end (yyyy-MM-dd)"), processCollectionEnd, Label("Quality level"), processQualityLevel)));
        Register("pointBudget", pointBudget); Register("terrainExtension", extension); Register("outputUnit", outputUnit); Register("distanceDisplayFormat", displayFormat);
        Register("acquisitionMode", acquisitionMode); Register("rasterPath", ascPath); Register("projectionPath", prjPath); Register("sourceSidecarPath", sidecarPath);
        Register("processSourceName", processSourceName); Register("processDataset", processDataset); Register("processVerticalDatum", processVerticalDatum); Register("processVerticalUnit", processVerticalUnit); Register("processGeoid", processGeoid); Register("processCollectionStart", processCollectionStart); Register("processCollectionEnd", processCollectionEnd); Register("processQualityLevel", processQualityLevel);
        Register("localParcelPath", localParcelPath); Register("localParcelLabel", localParcelLabel); Register("localParcelLicense", localParcelLicense);
        Register("countyRegistryPath", countyRegistryPath); Register("countyAuthorization", countyAuthorization); Register("geocoderProvider", geocoderProvider);
        Register("countyName", countyName); Register("countyGeoid", countyGeoid); Register("countyServiceUrl", countyServiceUrl); Register("countyAttribution", countyAttribution); Register("countyLicense", countyLicense);
        Register("countyLayer", countyLayer); Register("countyParcelIdField", countyParcelIdField); Register("countySitusAddressField", countySitusAddressField); Register("countyLegalDescriptionField", countyLegalDescriptionField);
        Register("useCountySource", saveCountyRegistration);
        Register("openTopographyKey", openTopographyKey); Register("geocodioKey", geocodioKey); Register("esriKey", esriKey);
        Register("exportDirectory", exportDirectory); Register("exportBaseName", exportBaseName); Register("networkTimeout", timeout); Register("nearbyRadius", nearbyRadius); Register("coverageFloor", coverageFloor); Register("simplificationMethod", simplificationMethod); Register("originKind", originKind); Register("originX", originX); Register("originY", originY); Register("originZ", originZ);
        shell.Children.Add(pages);

        error = new TextBlock { Foreground = colors.Error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) };
        AutomationProperties.SetName(error, "Settings error status");
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);
        Grid.SetRow(error, 2);
        shell.Children.Add(error);
        WrapPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button restoreDefaults = new() { Content = "Restore defaults", MinWidth = 108, Margin = new Thickness(6) };
        restoreDefaults.Click += (_, _) => RestoreDefaults();
        Button reloadSavedSettings = new() { Content = "Reload saved", MinWidth = 104, Margin = new Thickness(6) };
        reloadSavedSettings.Click += (_, _) => ReloadSavedSettings();
        Button reapplyDraft = new() { Content = "Reapply draft", MinWidth = 104, Margin = new Thickness(6) };
        reapplyDraft.Click += (_, _) => ReapplyDraft();
        Button startNewSettings = new() { Content = "Start new settings", MinWidth = 132, Margin = new Thickness(6), Visibility = savingBlockedUntilStartNew ? Visibility.Visible : Visibility.Collapsed };
        startNewSettings.Click += (_, _) => StartNewSettings();
        Button cancel = new() { Content = "Cancel", MinWidth = 100, Margin = new Thickness(6) };
        cancel.Click += (_, _) => CancelEditor();
        Button save = new() { Content = "Save settings", MinWidth = 120, Margin = new Thickness(6), IsDefault = true };
        save.Style = (Style)Resources[DialogControlStyles.PrimaryButtonStyleKey];
        save.Click += (_, _) => Save();
        Register("restoreDefaults", restoreDefaults); Register("reloadSavedSettings", reloadSavedSettings); Register("reapplyDraft", reapplyDraft); Register("startNewSettings", startNewSettings);
        footer.Children.Add(restoreDefaults); footer.Children.Add(reloadSavedSettings); footer.Children.Add(reapplyDraft); footer.Children.Add(startNewSettings); footer.Children.Add(cancel); footer.Children.Add(save);
        Grid.SetRow(footer, 3); shell.Children.Add(footer);
        Content = shell;
        if (savingBlockedUntilStartNew)
        {
            error.Text = (initialRecoveryError ?? "Saved settings need repair.") + " The saved bytes are preserved. Choose Start new settings to stage replacement values, then Save settings to authorize a write.";
        }
    }

    internal RevitSettings? Result { get; private set; }

    /// <summary>Constructs the actual editor without showing it, for the Windows-only rendered-control lane.</summary>
    internal static SettingsDialog CreateForTesting(UiSettingsDraft draft, RevitSettings current, DialogPalette palette, Func<SettingsRecoveryAction, bool>? confirmRecovery = null, bool savingBlockedUntilStartNew = false, string? initialRecoveryError = null) => new(null, draft, current, palette, confirmRecovery, savingBlockedUntilStartNew, initialRecoveryError);

    /// <summary>Named live controls used by the local WPF binding/accessibility tests.</summary>
    internal IReadOnlyDictionary<string, FrameworkElement> AutomationElements => automationElements;

    internal static RevitSettings? ShowModal(Window? owner, UiSettingsDraft draft, RevitSettings current, DialogPalette? palette = null, bool savingBlockedUntilStartNew = false, string? initialRecoveryError = null)
    {
        SettingsDialog dialog = new(owner, draft, current, palette, savingBlockedUntilStartNew: savingBlockedUntilStartNew, initialRecoveryError: initialRecoveryError);
        dialog.isShownModally = true;
        try { return dialog.ShowDialog() == true ? dialog.Result : null; }
        finally { dialog.isShownModally = false; }
    }

    /// <summary>Shows the editor as an owned Revit child without constructing a WPF owner window.</summary>
    internal static RevitSettings? ShowModal(IntPtr ownerHandle, UiSettingsDraft draft, RevitSettings current, DialogPalette palette, bool savingBlockedUntilStartNew = false, string? initialRecoveryError = null)
    {
        SettingsDialog dialog = new(null, draft, current, palette, savingBlockedUntilStartNew: savingBlockedUntilStartNew, initialRecoveryError: initialRecoveryError);
        if (ownerHandle != IntPtr.Zero)
        {
            _ = new WindowInteropHelper(dialog) { Owner = ownerHandle };
        }
        dialog.isShownModally = true;
        try { return dialog.ShowDialog() == true ? dialog.Result : null; }
        finally { dialog.isShownModally = false; }
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
        try { extensionMeters = extensionEdited ? DistanceDisplayConverter.ParseMeters(extension.Text, (DistanceDisplayFormat)displayFormat.SelectedItem) : canonicalExtensionMeters; }
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
        LocalOriginKind selectedOriginKind = (LocalOriginKind)originKind.SelectedItem;
        double originXValue = current.Request.LocalOrigin.X;
        double originYValue = current.Request.LocalOrigin.Y;
        double originZValue = current.Request.LocalOrigin.Z;
        if (selectedOriginKind == LocalOriginKind.Explicit
            && (!double.TryParse(originX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out originXValue) || !double.IsFinite(originXValue)
                || !double.TryParse(originY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out originYValue) || !double.IsFinite(originYValue)
                || !double.TryParse(originZ.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out originZValue) || !double.IsFinite(originZValue)))
        {
            error.Text = "Explicit local-origin X, Y, and Z must be finite source-unit coordinates.";
            originX.Focus();
            return false;
        }
        ProcessInputSettings? process = mode == TerrainAcquisitionMode.Process
            ? (baseline.Request.Process ?? new ProcessInputSettings { Asc = ascPath.Text }) with
            {
                Asc = ascPath.Text, Prj = BlankAsNull(prjPath.Text), SourceJson = BlankAsNull(sidecarPath.Text),
                SourceName = BlankAsNull(processSourceName.Text), Dataset = BlankAsNull(processDataset.Text), VerticalDatum = BlankAsNull(processVerticalDatum.Text), VerticalUnit = processVerticalUnit.SelectedItem as LengthUnit?, Geoid = BlankAsNull(processGeoid.Text),
                CollectionStart = BlankAsNull(processCollectionStart.Text), CollectionEnd = BlankAsNull(processCollectionEnd.Text), QualityLevel = BlankAsNull(processQualityLevel.Text),
            }
            : null;
        RevitAddressAndParcelSettings address = baseline.AddressAndParcel with
        {
            GeocoderProvider = (AddressGeocoderProvider)geocoderProvider.SelectedItem, NearbySearchRadiusMeters = nearby,
            CountyRegistryPath = stagedCountyRegistryPath ?? BlankAsNull(countyRegistryPath.Text), CountyServiceAuthorizedUseAcknowledged = countyAuthorization.IsChecked == true, LocalParcelFilePath = BlankAsNull(localParcelPath.Text), LocalParcelFileSourceLabel = BlankAsNull(localParcelLabel.Text), LocalParcelFileLicenseDisclaimerText = BlankAsNull(localParcelLicense.Text),
        };
        proposed = baseline with
        {
            Request = baseline.Request with { Mode = mode, Process = process, LocalOrigin = new LocalOriginRequest(selectedOriginKind, originXValue, originYValue, originZValue), OutputUnit = (LengthUnit)outputUnit.SelectedItem, NetworkTimeoutSeconds = timeoutSeconds, Simplification = baseline.Request.Simplification with { PointBudget = budget, CoverageFloorFraction = coverage, Method = (SimplificationMethod)simplificationMethod.SelectedItem }, Output = baseline.Request.Output with { Directory = exportDirectory.Text, BaseName = exportBaseName.Text } },
            AddressAndParcel = address, TerrainExtensionMeters = extensionMeters, DistanceDisplayFormat = (DistanceDisplayFormat)displayFormat.SelectedItem,
        };
        return true;
    }

    private void Persist(RevitSettings proposed)
    {
        if (savingBlockedUntilStartNew)
        {
            error.Text = "Saved settings need repair and remain untouched. Choose Start new settings, then Save settings to authorize replacement.";
            return;
        }
        bool stagedRegistryWritten = false;
        try
        {
            UiSettingsStore.ValidateForSave(proposed);
            stagedRegistryWritten = PublishStagedCountyRegistry();
            draft = UiSettingsStore.Save(draft, proposed);
            current = proposed;
            stagedCountyRegistry = null;
            stagedCountyRegistryPath = null;
            if (!string.IsNullOrWhiteSpace(openTopographyKey.Password))
            {
                SessionApiKeyOverrides.UseOpenTopography(openTopographyKey.Password);
                openTopographyKey.Password = string.Empty;
            }
            if (!string.IsNullOrWhiteSpace(geocodioKey.Password)) SessionApiKeyOverrides.UseGeocodio(geocodioKey.Password);
            if (!string.IsNullOrWhiteSpace(esriKey.Password)) SessionApiKeyOverrides.UseEsri(esriKey.Password);
            Result = proposed;
            if (isShownModally)
            {
                DialogResult = true;
                Close();
            }
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
        {
            if (stagedRegistryWritten) DiscardStagedCountyRegistryFile();
            error.Text = ex is SettingsFileConflictException
                ? ex.Message + " Use Reload saved to discard this draft, or Reapply draft to save these controls over the latest file."
                : ex.Message;
        }
    }

    private void RestoreDefaults()
    {
        if (!ConfirmRecovery(SettingsRecoveryAction.RestoreDefaults)) return;
        RevitSettings defaults = UiSettingsStore.CreateDefault();
        RevitAddressAndParcelSettings preservedSources = current.AddressAndParcel with
        {
            GeocoderProvider = defaults.AddressAndParcel.GeocoderProvider,
            NearbySearchRadiusMeters = defaults.AddressAndParcel.NearbySearchRadiusMeters,
        };
        RevitSettings preferenceDefaults = current with
        {
            Request = defaults.Request with
            {
                Mode = current.Request.Mode,
                Process = current.Request.Process,
                AreaOfInterest = current.Request.AreaOfInterest,
                LocalOrigin = current.Request.LocalOrigin,
            },
            AddressAndParcel = preservedSources,
            TerrainExtensionMeters = defaults.TerrainExtensionMeters,
            DistanceDisplayFormat = defaults.DistanceDisplayFormat,
        };
        DiscardStagedCountyRegistry();
        current = preferenceDefaults;
        draft = draft with { Settings = preferenceDefaults };
        ApplySettings(preferenceDefaults);
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
        DiscardStagedCountyRegistry();
        current = RevitSettingsIo.RebaseInputPaths(draft.Settings, draft.Path);
        savingBlockedUntilStartNew = false;
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

    private void StartNewSettings()
    {
        if (!savingBlockedUntilStartNew || !ConfirmRecovery(SettingsRecoveryAction.StartNewSettings)) return;
        DiscardStagedCountyRegistry();
        RevitSettings defaults = UiSettingsStore.CreateDefault();
        SettingsFileVersion expectedVersion = AtomicSettingsFile.Read(draft.Path).Version;
        current = defaults;
        draft = new UiSettingsDraft(defaults, expectedVersion, draft.Path);
        savingBlockedUntilStartNew = false;
        ApplySettings(defaults);
        error.Text = "New settings are staged. The previous bytes remain untouched until you choose Save settings.";
    }

    private void CancelEditor()
    {
        DiscardStagedCountyRegistry();
        if (isShownModally)
        {
            DialogResult = false;
            Close();
        }
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
        string prompt = action switch
        {
            SettingsRecoveryAction.RestoreDefaults => "Reset display format, output unit, point budget, simplifier, network timeout, nearby distance, export folder/base name, terrain extension, and geocoder preference. Local raster/process metadata, local origin, parcel sources, county registrations, and license text stay unchanged. Nothing is written until you choose Save settings.",
            SettingsRecoveryAction.StartNewSettings => "Stage a new settings document? The unreadable saved bytes remain untouched until you choose Save settings.",
            _ => "Reapply the current controls over the latest saved settings? This explicitly replaces saved values represented by this dialog.",
        };
        return MessageBox.Show(this, prompt, "SolidGround settings", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    private void ApplySettings(RevitSettings settings)
    {
        pointBudget.Text = settings.Request.Simplification.PointBudget.ToString(CultureInfo.InvariantCulture);
        extensionFormat = settings.DistanceDisplayFormat;
        displayFormat.SelectedItem = extensionFormat;
        canonicalExtensionMeters = settings.TerrainExtensionMeters;
        extensionEdited = false;
        SetExtensionText(DistanceDisplayConverter.FormatMeters(canonicalExtensionMeters, extensionFormat));
        outputUnit.SelectedItem = settings.Request.OutputUnit;
        acquisitionMode.SelectedItem = settings.Request.Mode;
        ascPath.Text = settings.Request.Process?.Asc ?? string.Empty;
        prjPath.Text = settings.Request.Process?.Prj ?? string.Empty;
        sidecarPath.Text = settings.Request.Process?.SourceJson ?? string.Empty;
        processSourceName.Text = settings.Request.Process?.SourceName ?? string.Empty;
        processDataset.Text = settings.Request.Process?.Dataset ?? string.Empty;
        processVerticalDatum.Text = settings.Request.Process?.VerticalDatum ?? string.Empty;
        processVerticalUnit.SelectedItem = settings.Request.Process?.VerticalUnit;
        processGeoid.Text = settings.Request.Process?.Geoid ?? string.Empty;
        processCollectionStart.Text = settings.Request.Process?.CollectionStart ?? string.Empty;
        processCollectionEnd.Text = settings.Request.Process?.CollectionEnd ?? string.Empty;
        processQualityLevel.Text = settings.Request.Process?.QualityLevel ?? string.Empty;
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
        originKind.SelectedItem = settings.Request.LocalOrigin.Kind;
        originX.Text = settings.Request.LocalOrigin.X.ToString("R", CultureInfo.InvariantCulture);
        originY.Text = settings.Request.LocalOrigin.Y.ToString("R", CultureInfo.InvariantCulture);
        originZ.Text = settings.Request.LocalOrigin.Z.ToString("R", CultureInfo.InvariantCulture);

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
            IReadOnlyList<CountyParcelRegistryEntry> existingEntries = stagedCountyRegistry?.Counties
                ?? LoadExistingCountyRegistryEntries();
            IReadOnlyList<CountyParcelRegistryEntry> entries = existingEntries
                    .Where(existing => !string.Equals(existing.Geoid, entry.Geoid, StringComparison.Ordinal))
                    .Append(entry)
                    .OrderBy(existing => existing.Geoid, StringComparer.Ordinal)
                    .ToArray();
            stagedCountyRegistry = new CountyParcelRegistryDocument
            {
                SchemaVersion = CountyParcelRegistry.CurrentSchemaVersion,
                Counties = entries,
            };
            stagedCountyRegistryPath ??= CreateOwnedCountyRegistryPath();
            countyMetadataStatus.Text = "County source is staged. Save settings to publish and activate its new local registry.";
            error.Text = string.Empty;
        }
        catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            error.Text = $"County registration could not be saved: {ex.Message}";
        }
    }

    private CountyParcelRegistryEntry[] LoadExistingCountyRegistryEntries()
    {
        if (string.IsNullOrWhiteSpace(countyRegistryPath.Text)) return [];
        return CountyParcelRegistry.Load(countyRegistryPath.Text.Trim()).EntriesByGeoid.Values.ToArray();
    }

    private string CreateOwnedCountyRegistryPath()
    {
        string folder = Path.GetDirectoryName(draft.Path) ?? throw new InvalidOperationException("The per-user settings path has no parent folder.");
        return Path.Combine(folder, $"county-parcel-registry.{Guid.NewGuid():N}.json");
    }

    private bool PublishStagedCountyRegistry()
    {
        if (stagedCountyRegistry is null) return false;
        if (string.IsNullOrWhiteSpace(stagedCountyRegistryPath)) throw new InvalidOperationException("The staged county registry has no owned output path.");
        CountyParcelRegistry.Write(stagedCountyRegistryPath, stagedCountyRegistry);
        _ = CountyParcelRegistry.Load(stagedCountyRegistryPath);
        return true;
    }

    private void DiscardStagedCountyRegistry()
    {
        DiscardStagedCountyRegistryFile();
        stagedCountyRegistry = null;
        stagedCountyRegistryPath = null;
    }

    private void DiscardStagedCountyRegistryFile()
    {
        if (string.IsNullOrWhiteSpace(stagedCountyRegistryPath)) return;
        string folder = Path.GetDirectoryName(draft.Path) ?? string.Empty;
        string expectedPrefix = Path.Combine(Path.GetFullPath(folder), "county-parcel-registry.");
        string candidate = Path.GetFullPath(stagedCountyRegistryPath);
        if (!candidate.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase)
            || !candidate.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (File.Exists(candidate)) File.Delete(candidate);
    }

    private static string? PreferField(IEnumerable<string> fields, params string[] fragments) => fields.FirstOrDefault(field => fragments.Any(fragment => field.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

    private static bool IsCountyGeoid(string value) => value.Length == 5 && value.All(char.IsAsciiDigit);

    private void ChangeExtensionFormat()
    {
        DistanceDisplayFormat next = (DistanceDisplayFormat)displayFormat.SelectedItem;
        try
        {
            if (extensionEdited) canonicalExtensionMeters = DistanceDisplayConverter.ParseMeters(extension.Text, extensionFormat);
            SetExtensionText(DistanceDisplayConverter.FormatMeters(canonicalExtensionMeters, next));
            extensionFormat = next;
            extensionEdited = false;
        }
        catch (FormatException)
        {
            // Keep invalid text visible for correction; Save will focus it and report the field error.
            extensionFormat = next;
        }
    }

    private void SetExtensionText(string value)
    {
        updatingExtensionText = true;
        try { extension.Text = value; }
        finally { updatingExtensionText = false; }
    }

    private void Register(string name, FrameworkElement element)
    {
        element.Name = name;
        AutomationProperties.SetAutomationId(element, name);
        AutomationProperties.SetName(element, AccessibleName(name));
        AutomationProperties.SetHelpText(element, AccessibleHelpText(name));
        automationElements.Add(name, element);
    }

    private static string AccessibleName(string name) => name switch
    {
        "pointBudget" => "Maximum terrain points",
        "terrainExtension" => "Terrain beyond property line",
        "outputUnit" => "Output unit",
        "distanceDisplayFormat" => "Distance display format",
        "acquisitionMode" => "Elevation mode",
        "rasterPath" => "Local raster file",
        "projectionPath" => "Projection sidecar file",
        "sourceSidecarPath" => "Source metadata sidecar file",
        "processSourceName" => "Local process source name",
        "processDataset" => "Local process dataset",
        "processVerticalDatum" => "Local process vertical datum",
        "processVerticalUnit" => "Local process vertical unit",
        "processGeoid" => "Local process geoid model",
        "processCollectionStart" => "Local process collection start date",
        "processCollectionEnd" => "Local process collection end date",
        "processQualityLevel" => "Local process quality level",
        "localParcelPath" => "Local parcel file",
        "localParcelLabel" => "Local parcel source label",
        "localParcelLicense" => "Local parcel license or disclaimer",
        "countyRegistryPath" => "County registry file",
        "countyAuthorization" => "County service authorized use acknowledgement",
        "countyName" => "County source identity",
        "countyGeoid" => "County GEOID",
        "countyServiceUrl" => "County ArcGIS service URL",
        "countyAttribution" => "County attribution",
        "countyLicense" => "County license or disclaimer",
        "countyLayer" => "County parcel layer",
        "countyParcelIdField" => "County parcel ID field",
        "countySitusAddressField" => "County situs address field",
        "countyLegalDescriptionField" => "County legal description field",
        "useCountySource" => "Use this county source",
        "geocoderProvider" => "Geocoder provider",
        "openTopographyKey" => "OpenTopography session key",
        "geocodioKey" => "Geocodio session key",
        "esriKey" => "Esri session key",
        "exportDirectory" => "Export folder",
        "exportBaseName" => "Export base name",
        "networkTimeout" => "Network timeout in seconds",
        "nearbyRadius" => "Nearby parcel search distance in metres",
        "coverageFloor" => "Sampler coverage fraction",
        "simplificationMethod" => "Simplification method",
        "originKind" => "Local origin kind",
        "originX" => "Explicit local origin X in source horizontal units",
        "originY" => "Explicit local origin Y in source horizontal units",
        "originZ" => "Explicit local origin Z in source elevation units",
        "restoreDefaults" => "Restore defaults",
        "reloadSavedSettings" => "Reload saved",
        "reapplyDraft" => "Reapply draft",
        "startNewSettings" => "Start new settings",
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Every rendered Settings control needs a human-readable accessible name."),
    };

    private static string AccessibleHelpText(string name) => name switch
    {
        "pointBudget" => "Sets the maximum number of terrain points retained for the Revit toposolid.",
        "terrainExtension" => "Adds terrain outside the parcel boundary and retains its exact value in metres.",
        "outputUnit" => "Sets the unit stored in terrain output and provenance.",
        "distanceDisplayFormat" => "Changes how distances are displayed without changing their stored metre values.",
        "acquisitionMode" => "Chooses whether elevation is fetched or supplied from a local raster.",
        "rasterPath" => "Selects the local ASCII grid raster used in local-process mode.",
        "projectionPath" => "Selects the projection sidecar for the local raster.",
        "sourceSidecarPath" => "Selects optional source metadata for the local raster.",
        "localParcelPath" => "Selects the GeoJSON or WKT parcel boundary file.",
        "localParcelLabel" => "Identifies the source of the local parcel file in the operator view and provenance.",
        "localParcelLicense" => "Shows the local parcel source license or disclaimer before use.",
        "countyRegistryPath" => "Shows the per-user county registry created after settings are saved.",
        "countyAuthorization" => "Must be acknowledged before an authorized county parcel service can be used.",
        "countyServiceUrl" => "HTTPS ArcGIS service URL used to discover parcel layers and fields.",
        "countyLayer" => "Selects the parcel layer returned by the county ArcGIS service.",
        "countyParcelIdField" => "Maps the stable county parcel identifier field.",
        "countySitusAddressField" => "Optionally maps the county situs-address field.",
        "countyLegalDescriptionField" => "Optionally maps the county legal-description field.",
        "useCountySource" => "Stages this authorized county source and writes its per-user registry only when settings are saved.",
        "openTopographyKey" => "Session-only OpenTopography key. It is never written to settings.",
        "geocodioKey" => "Session-only Geocodio key. It is never written to settings.",
        "esriKey" => "Session-only Esri key. It is never written to settings.",
        "exportDirectory" => "Selects the folder for exported terrain files.",
        "exportBaseName" => "Sets the base name for exported terrain files.",
        "networkTimeout" => "Sets the network request timeout in seconds.",
        "nearbyRadius" => "Sets the distance searched when a geocoded point misses its parcel.",
        "coverageFloor" => "Reserves this fraction of the point budget for broad terrain coverage.",
        "originKind" => "Chooses how the reversible local coordinate origin is determined.",
        "originX" => "Sets the explicit origin X coordinate in the source horizontal unit.",
        "originY" => "Sets the explicit origin Y coordinate in the source horizontal unit.",
        "originZ" => "Sets the explicit origin Z elevation in the source elevation unit.",
        "restoreDefaults" => "Restores preference defaults in the draft while retaining registered sources and local paths.",
        "reloadSavedSettings" => "Reloads the latest saved settings after a conflict and replaces the current draft.",
        "reapplyDraft" => "Retries saving the current draft against the latest settings digest after confirmation.",
        "startNewSettings" => "Explicitly permits replacing an unreadable settings file when this draft is saved.",
        _ => AccessibleName(name),
    };
}
