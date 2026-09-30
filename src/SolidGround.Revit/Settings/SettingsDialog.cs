using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;
using SolidGround.Core.Processing;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Settings;

/// <summary>Small modal editor for persistent operator preferences. It never receives a Revit document.</summary>
internal sealed class SettingsDialog : Window
{
    private readonly Dictionary<string, FrameworkElement> automationElements = new(StringComparer.Ordinal);
    private readonly RevitSettings current;
    private readonly UiSettingsDraft draft;
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
    private readonly ComboBox geocoderProvider;
    private readonly PasswordBox openTopographyKey;
    private readonly PasswordBox geocodioKey;
    private readonly PasswordBox esriKey;
    private readonly TextBlock sessionKeyStatus;
    private readonly TextBlock error;
    private DistanceDisplayFormat extensionFormat;

    private SettingsDialog(Window? owner, UiSettingsDraft draft, RevitSettings current, DialogPalette? palette)
    {
        this.draft = draft;
        this.current = current;
        Owner = owner;
        Title = "SolidGround settings";
        Width = 720;
        Height = 580;
        WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;

        DialogPalette colors = palette ?? new DialogPalette(SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.ControlTextBrush, SystemColors.WindowBrush, SystemColors.HighlightBrush, SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.ActiveBorderBrush);
        Background = colors.Window;
        Foreground = colors.WindowText;
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
            Label("Local parcel file"), FileField(localParcelPath, "Parcel data (*.geojson;*.json)|*.geojson;*.json|All files|*.*"), Label("Local source label"), localParcelLabel,
            Label("Local license or disclaimer"), localParcelLicense,
            Label("County registry file"), FileField(countyRegistryPath, "County registry (*.json)|*.json|All files|*.*"), countyAuthorization,
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
        Register("localParcelPath", localParcelPath); Register("countyRegistryPath", countyRegistryPath); Register("geocoderProvider", geocoderProvider);
        Register("openTopographyKey", openTopographyKey); Register("geocodioKey", geocodioKey); Register("esriKey", esriKey);
        Register("exportDirectory", exportDirectory); Register("exportBaseName", exportBaseName); Register("networkTimeout", timeout); Register("nearbyRadius", nearbyRadius); Register("coverageFloor", coverageFloor); Register("simplificationMethod", simplificationMethod);
        shell.Children.Add(pages);

        error = new TextBlock { Foreground = colors.Error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 6) };
        Grid.SetRow(error, 2);
        shell.Children.Add(error);
        StackPanel footer = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button cancel = new() { Content = "Cancel", MinWidth = 100, Margin = new Thickness(6) };
        cancel.Click += (_, _) => { DialogResult = false; Close(); };
        Button save = new() { Content = "Save settings", MinWidth = 120, Margin = new Thickness(6), IsDefault = true };
        save.Click += (_, _) => Save();
        footer.Children.Add(cancel); footer.Children.Add(save);
        Grid.SetRow(footer, 3); shell.Children.Add(footer);
        Content = shell;
    }

    internal RevitSettings? Result { get; private set; }

    /// <summary>Constructs the actual editor without showing it, for the Windows-only rendered-control lane.</summary>
    internal static SettingsDialog CreateForTesting(UiSettingsDraft draft, RevitSettings current, DialogPalette palette) => new(null, draft, current, palette);

    /// <summary>Named live controls used by the local WPF binding/accessibility tests.</summary>
    internal IReadOnlyDictionary<string, FrameworkElement> AutomationElements => automationElements;

    internal static RevitSettings? ShowModal(Window? owner, UiSettingsDraft draft, RevitSettings current, DialogPalette? palette = null)
    {
        SettingsDialog dialog = new(owner, draft, current, palette);
        return dialog.ShowDialog() == true ? dialog.Result : null;
    }

    private void Save()
    {
        if (!int.TryParse(pointBudget.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int budget) || budget <= 0)
        {
            error.Text = "Maximum terrain points must be a positive whole number."; pointBudget.Focus(); return;
        }
        double extensionMeters;
        try { extensionMeters = DistanceDisplayConverter.ParseMeters(extension.Text, (DistanceDisplayFormat)displayFormat.SelectedItem); }
        catch (FormatException)
        {
            error.Text = "Terrain extension must be a finite nonnegative value in the selected display format."; extension.Focus(); return;
        }
        TerrainAcquisitionMode mode = (TerrainAcquisitionMode)acquisitionMode.SelectedItem;
        if (!int.TryParse(timeout.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int timeoutSeconds) || timeoutSeconds <= 0)
        {
            error.Text = "Network timeout must be a positive whole number of seconds."; timeout.Focus(); return;
        }
        if (!double.TryParse(coverageFloor.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double coverage) || !double.IsFinite(coverage) || coverage is < 0d or > 1d)
        {
            error.Text = "Sampler coverage fraction must be between 0 and 1."; coverageFloor.Focus(); return;
        }
        double? nearby = null;
        if (!string.IsNullOrWhiteSpace(nearbyRadius.Text) && (!double.TryParse(nearbyRadius.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsedNearby) || !double.IsFinite(parsedNearby) || parsedNearby <= 0d))
        {
            error.Text = "Nearby parcel search distance must be a positive number of metres or blank."; nearbyRadius.Focus(); return;
        }
        else if (!string.IsNullOrWhiteSpace(nearbyRadius.Text)) nearby = double.Parse(nearbyRadius.Text, NumberStyles.Float, CultureInfo.InvariantCulture);
        ProcessInputSettings? process = mode == TerrainAcquisitionMode.Process
            ? (current.Request.Process ?? new ProcessInputSettings { Asc = ascPath.Text }) with { Asc = ascPath.Text, Prj = BlankAsNull(prjPath.Text), SourceJson = BlankAsNull(sidecarPath.Text) }
            : null;
        RevitAddressAndParcelSettings address = current.AddressAndParcel with
        {
            GeocoderProvider = (AddressGeocoderProvider)geocoderProvider.SelectedItem, NearbySearchRadiusMeters = nearby,
            CountyRegistryPath = BlankAsNull(countyRegistryPath.Text), CountyServiceAuthorizedUseAcknowledged = countyAuthorization.IsChecked == true, LocalParcelFilePath = BlankAsNull(localParcelPath.Text), LocalParcelFileSourceLabel = BlankAsNull(localParcelLabel.Text), LocalParcelFileLicenseDisclaimerText = BlankAsNull(localParcelLicense.Text),
        };
        RevitSettings proposed = current with
        {
            Request = current.Request with { Mode = mode, Process = process, OutputUnit = (LengthUnit)outputUnit.SelectedItem, NetworkTimeoutSeconds = timeoutSeconds, Simplification = current.Request.Simplification with { PointBudget = budget, CoverageFloorFraction = coverage, Method = (SimplificationMethod)simplificationMethod.SelectedItem }, Output = current.Request.Output with { Directory = exportDirectory.Text, BaseName = exportBaseName.Text } },
            AddressAndParcel = address, TerrainExtensionMeters = extensionMeters, DistanceDisplayFormat = (DistanceDisplayFormat)displayFormat.SelectedItem,
        };
        try
        {
            UiSettingsStore.Save(draft, proposed);
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
            error.Text = ex.Message;
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

    private void UseSessionKey()
    {
        if (string.IsNullOrWhiteSpace(openTopographyKey.Password)) { error.Text = "Enter a nonblank key before using it for this Revit session."; openTopographyKey.Focus(); return; }
        SessionApiKeyOverrides.UseOpenTopography(openTopographyKey.Password);
        openTopographyKey.Password = string.Empty;
        sessionKeyStatus.Text = "A session key is active until Revit exits or you clear it.";
        error.Text = string.Empty;
    }

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
