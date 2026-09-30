using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using SolidGround.Core.Sources;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using ToggleButton = System.Windows.Controls.Primitives.ToggleButton;

namespace SolidGround.Revit.Dialog;

/// <summary>Code-built, modal WPF surface for Location, Parcel, and Review.</summary>
/// <remarks>No lookup or elevation request is made by this view; its view-model owns cancellable Core I/O.</remarks>
internal sealed class SolidGroundDialog : Window
{
    private readonly SolidGroundDialogViewModel _viewModel;
    private readonly Dictionary<SolidGroundDialogStep, FrameworkElement> _panels;
    private readonly Button _findButton;
    private readonly Button _useLocationButton;
    private readonly Button _useParcelButton;
    private readonly Button _createButton;

    /// <summary>Actual panel and shell roots used only by the local WPF binding lane.</summary>
    internal IEnumerable<FrameworkElement> BindingRootsForTesting => _panels.Values.Append((FrameworkElement)Content);
    private readonly List<Grid> _parcelLayouts = [];

    /// <summary>
    /// Constructs from a caller-resolved palette. This constructor deliberately contains no Revit UI type
    /// reference, allowing the local STA WPF tests to render it without loading RevitAPIUI.
    /// </summary>
    internal SolidGroundDialog(SolidGroundDialogViewModel viewModel, DialogPalette palette)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        Title = "SolidGround — Create terrain";
        Width = 760;
        Height = 640;
        MinWidth = 640;
        MinHeight = 480;
        ResizeMode = ResizeMode.CanResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        ArgumentNullException.ThrowIfNull(palette);
        Background = palette.Window;
        Foreground = palette.WindowText;
        BorderBrush = palette.ActiveBorder;
        DialogControlStyles.Apply(this, palette);
        Loaded += (_, _) => ClampToWorkingArea();
        SizeChanged += (_, _) => UpdateParcelLayouts();

        _panels = new()
        {
            [SolidGroundDialogStep.Location] = BuildLocationPanel(palette),
            [SolidGroundDialogStep.Parcel] = BuildParcelPanel(palette),
            [SolidGroundDialogStep.Review] = BuildReviewPanel(palette),
        };

        _findButton = Primary("Find", nameof(SolidGroundDialogViewModel.FindCommand), palette, "Find location");
        _useLocationButton = Primary("Use this location", nameof(SolidGroundDialogViewModel.UseLocationCommand), palette, "Use selected location");
        _useParcelButton = Primary("Use this parcel", nameof(SolidGroundDialogViewModel.UseParcelCommand), palette, "Use selected parcel");
        _createButton = Primary("Create toposolid", nameof(SolidGroundDialogViewModel.CreateCommand), palette, "Create toposolid");

        Content = BuildShell(palette);
        UpdateParcelLayouts();
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
        UpdatePrimaryButtons();
    }

    private Grid BuildShell(DialogPalette palette)
    {
        TextBlock stage = Text("", palette, FontWeights.SemiBold);
        stage.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.StageAnnouncement)));
        AutomationProperties.SetName(stage, "Current stage");

        Button settings = Secondary("Settings", nameof(SolidGroundDialogViewModel.EditSettingsCommand), palette, "Edit SolidGround settings");
        DockPanel header = new() { Height = 72, Margin = new Thickness(24, 16, 24, 0) };
        header.Children.Add(settings);
        DockPanel.SetDock(settings, Dock.Right);
        header.Children.Add(new StackPanel { Children = { Text("SolidGround — Create terrain", palette, FontWeights.Bold), stage } });

        TextBlock error = Text("", palette);
        error.Foreground = palette.Error;
        error.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.ErrorText)));
        error.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ErrorText)) { Converter = TextPresenceVisibilityConverter.Instance });
        AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Assertive);

        TextBlock busy = Text("Looking up location or parcel…", palette, FontWeights.SemiBold);
        busy.Foreground = palette.Highlight;
        busy.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.IsBusy)) { Converter = BooleanToVisibilityConverter.Instance });
        AutomationProperties.SetLiveSetting(busy, AutomationLiveSetting.Polite);

        ContentControl content = new() { Margin = new Thickness(24, 8, 24, 8) };
        content.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SolidGroundDialogViewModel.CurrentStep)) { Converter = new StepPanelConverter(_panels) });
        ScrollViewer scroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Add(root, header, 0); Add(root, error, 1); Add(root, busy, 2); Add(root, scroll, 3); Add(root, BuildFooter(palette), 4);
        return root;
    }

    private StackPanel BuildLocationPanel(DialogPalette palette)
    {
        StackPanel panel = new() { Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(Text("Find a location", palette, FontWeights.Bold));
        panel.Children.Add(Text("A location is used only to find and confirm a parcel.", palette));
        ComboBox mode = new() { ItemsSource = Enum.GetValues<LocationEntryMode>(), Margin = new Thickness(0, 16, 0, 8) };
        mode.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.EntryMode)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(mode, "Location input type");
        panel.Children.Add(mode);

        TextBox address = Input("Street address", nameof(SolidGroundDialogViewModel.AddressText), palette);
        StackPanel addressField = Field("Street address", address, palette);
        addressField.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowAddress)) { Converter = BooleanToVisibilityConverter.Instance });
        panel.Children.Add(addressField);
        StackPanel coordinates = new() { Orientation = Orientation.Horizontal };
        TextBox latitude = Input("Latitude", nameof(SolidGroundDialogViewModel.LatitudeText), palette);
        TextBox longitude = Input("Longitude", nameof(SolidGroundDialogViewModel.LongitudeText), palette);
        coordinates.Children.Add(Field("Latitude", latitude, palette));
        coordinates.Children.Add(Field("Longitude", longitude, palette));
        coordinates.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowCoordinates)) { Converter = BooleanToVisibilityConverter.Instance });
        panel.Children.Add(coordinates);

        StackPanel other = new();
        other.Children.Add(Text("Other area options", palette, FontWeights.SemiBold));
        other.Children.Add(Text("Use a bounding box, point and radius, or a local polygon instead of a parcel. This does not create a property line.", palette));
        StackPanel boundingBox = new();
        boundingBox.Children.Add(Field("West longitude", Input("West longitude", nameof(SolidGroundDialogViewModel.WestText), palette), palette));
        boundingBox.Children.Add(Field("South latitude", Input("South latitude", nameof(SolidGroundDialogViewModel.SouthText), palette), palette));
        boundingBox.Children.Add(Field("East longitude", Input("East longitude", nameof(SolidGroundDialogViewModel.EastText), palette), palette));
        boundingBox.Children.Add(Field("North latitude", Input("North latitude", nameof(SolidGroundDialogViewModel.NorthText), palette), palette));
        boundingBox.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.EntryMode)) { Converter = new EntryModeVisibilityConverter(LocationEntryMode.BoundingBox) });
        other.Children.Add(boundingBox);
        StackPanel radius = new() { Orientation = Orientation.Horizontal };
        radius.Children.Add(Field("Center latitude", Input("Center latitude", nameof(SolidGroundDialogViewModel.LatitudeText), palette), palette));
        radius.Children.Add(Field("Center longitude", Input("Center longitude", nameof(SolidGroundDialogViewModel.LongitudeText), palette), palette));
        radius.Children.Add(BoundField(nameof(SolidGroundDialogViewModel.RadiusLabel), Input("Radius", nameof(SolidGroundDialogViewModel.RadiusMetersText), palette), palette));
        radius.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.EntryMode)) { Converter = new EntryModeVisibilityConverter(LocationEntryMode.Radius) });
        other.Children.Add(radius);
        TextBox geometry = Input("Local GeoJSON or WKT", nameof(SolidGroundDialogViewModel.LocalGeometryText), palette);
        Button browseGeometry = Secondary("Browse GeoJSON or WKT…", null, palette, "Browse local GeoJSON or WKT");
        browseGeometry.Click += (_, _) => BrowseGeometry(geometry);
        StackPanel localGeometry = new();
        localGeometry.Children.Add(Field("Paste local GeoJSON or WKT", geometry, palette));
        localGeometry.Children.Add(browseGeometry);
        localGeometry.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.EntryMode)) { Converter = new EntryModeVisibilityConverter(LocationEntryMode.LocalGeometry) });
        other.Children.Add(localGeometry);
        other.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowOtherAreaOptions)) { Converter = BooleanToVisibilityConverter.Instance });
        panel.Children.Add(other);
        panel.Children.Add(Text("Use Settings to change terrain extension, elevation source, access key, or export defaults.", palette));
        return panel;
    }

    private StackPanel BuildParcelPanel(DialogPalette palette)
    {
        StackPanel panel = new();
        panel.Children.Add(Text("Confirm location and parcel", palette, FontWeights.Bold));
        TextBlock nearby = Text("", palette); nearby.Foreground = palette.Highlight;
        nearby.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.NearbyTierNoticeText)));
        nearby.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.NearbyTierNoticeText)) { Converter = TextPresenceVisibilityConverter.Instance });
        panel.Children.Add(nearby);

        ListBox locations = new() { DisplayMemberPath = nameof(AddressGeocodeCandidate.MatchedAddress), MinHeight = 70, Margin = new Thickness(0, 8, 0, 8) };
        locations.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.GeocodeCandidates)));
        locations.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedGeocodeCandidate)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(locations, "Location candidates");
        panel.Children.Add(locations);

        StackPanel parcelDetails = new();
        parcelDetails.Children.Add(Text("Select a parcel", palette, FontWeights.SemiBold));
        ListBox parcels = new() { MinHeight = 130, Margin = new Thickness(0, 8, 0, 8), ItemTemplate = ParcelCandidateTemplate(palette) };
        parcels.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.ParcelCandidates)));
        parcels.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedParcelCandidate)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(parcels, "Parcel candidates");
        parcelDetails.Children.Add(parcels);
        TextBlock containment = Text("", palette); containment.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.ContainmentLabel)));
        parcelDetails.Children.Add(containment);
        parcelDetails.Children.Add(BoundText("SelectedParcelDetail", palette));
        parcelDetails.Children.Add(BoundText("SelectedParcelSourceTerms", palette));
        parcelDetails.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.LocationAttribution), palette));
        Button map = Secondary("Open selected coordinate in map", null, palette, "Open selected coordinate in the default browser");
        map.ToolTip = "Opens the selected coordinates in your default browser. No address is sent.";
        map.Click += (_, _) => OpenCoordinateMap();
        parcelDetails.Children.Add(map);
        parcelDetails.Children.Add(Text("Use this parcel confirms the displayed legal boundary. Nearby parcels are never selected automatically.", palette));
        ParcelBoundaryPreview preview = new(palette) { Margin = new Thickness(16, 12, 0, 0), DataContext = _viewModel };
        Grid parcelContent = new() { Margin = new Thickness(0, 8, 0, 0) };
        parcelContent.Children.Add(parcelDetails);
        parcelContent.Children.Add(preview);
        _parcelLayouts.Add(parcelContent);
        panel.Children.Add(parcelContent);
        return panel;
    }

    private static StackPanel BuildReviewPanel(DialogPalette palette)
    {
        StackPanel panel = new();
        panel.Children.Add(Text("Review and create", palette, FontWeights.Bold));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.SettingsSummary), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.ExtensionSummary), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.EstimateSummary), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.SourceSummary), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.LocationAttribution), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.ContainmentLabel), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.AccuracyDisclaimer), palette));
        TextBlock budgetWarning = BoundText(nameof(SolidGroundDialogViewModel.NativePointBudgetWarning), palette);
        budgetWarning.Foreground = palette.Error;
        budgetWarning.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.NativePointBudgetWarning)) { Converter = TextPresenceVisibilityConverter.Instance });
        panel.Children.Add(budgetWarning);
        Button portal = Secondary("Request OpenTopography access", null, palette, "Open OpenTopography access portal");
        portal.ToolTip = "Opens OpenTopography in your default browser to request or manage access. SolidGround does not send your settings.";
        portal.Click += (_, _) => OpenExternal("https://portal.opentopography.org/");
        panel.Children.Add(portal);

        ComboBox level = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 8, 0, 0) };
        level.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.LevelCandidates)));
        level.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedLevel)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(level, "Level"); panel.Children.Add(Field("Level", level, palette));
        ComboBox type = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 8, 0, 0) };
        type.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.ToposolidTypeCandidates)));
        type.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedToposolidType)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(type, "Toposolid type"); panel.Children.Add(Field("Toposolid type", type, palette));
        CheckBox shared = new() { Content = "Write shared coordinates if none exist", Margin = new Thickness(0, 12, 0, 0) };
        shared.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SolidGroundDialogViewModel.WriteSharedCoordinatesIfAbsent)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(shared, "Write shared coordinates if none exist"); panel.Children.Add(shared);
        panel.Children.Add(Text("SolidGround is a site-form tool, not a survey instrument.", palette));
        return panel;
    }

    private DockPanel BuildFooter(DialogPalette palette)
    {
        Button cancel = Secondary("Cancel", nameof(SolidGroundDialogViewModel.CancelCommand), palette, "Cancel"); cancel.IsCancel = true;
        Button back = Secondary("Back", nameof(SolidGroundDialogViewModel.BackCommand), palette, "Back");
        StackPanel right = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(_findButton); right.Children.Add(_useLocationButton); right.Children.Add(_useParcelButton); right.Children.Add(_createButton);
        DockPanel footer = new() { Height = 64, Margin = new Thickness(24, 8, 24, 8), LastChildFill = false };
        DockPanel.SetDock(cancel, Dock.Left); DockPanel.SetDock(back, Dock.Left); DockPanel.SetDock(right, Dock.Right);
        footer.Children.Add(cancel); footer.Children.Add(back); footer.Children.Add(right);
        return footer;
    }

    private static TextBlock Text(string text, DialogPalette palette, FontWeight? weight = null) => new()
    {
        Text = text, Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4), FontWeight = weight ?? FontWeights.Normal,
    };

    private static TextBlock BoundText(string path, DialogPalette palette)
    {
        TextBlock value = Text("", palette); value.SetBinding(TextBlock.TextProperty, new Binding(path)); return value;
    }

    private static TextBox Input(string name, string path, DialogPalette palette)
    {
        TextBox input = new() { Margin = new Thickness(0, 4, 8, 4), MinWidth = 170 };
        input.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(input, name); return input;
    }

    private Button Primary(string label, string command, DialogPalette palette, string name)
    {
        Button button = new() { Content = label, MinWidth = 132, Margin = new Thickness(8, 0, 0, 0), Style = (Style)Resources[DialogControlStyles.PrimaryButtonStyleKey] };
        button.SetBinding(Button.CommandProperty, new Binding(command)); AutomationProperties.SetName(button, name); return button;
    }

    private static Button Secondary(string label, string? command, DialogPalette palette, string name)
    {
        Button button = new() { Content = label, Margin = new Thickness(0, 0, 8, 0) };
        if (command is not null) button.SetBinding(Button.CommandProperty, new Binding(command));
        AutomationProperties.SetName(button, name); return button;
    }

    private static void Add(Grid grid, UIElement child, int row) { Grid.SetRow(child, row); grid.Children.Add(child); }

    private void OnCloseRequested(object? sender, EventArgs e) => Close();
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdatePrimaryButtons();

    private void UpdatePrimaryButtons()
    {
        _findButton.Visibility = _viewModel.CurrentStep == SolidGroundDialogStep.Location ? Visibility.Visible : Visibility.Collapsed;
        _useLocationButton.Visibility = _viewModel.CurrentStep == SolidGroundDialogStep.Parcel && _viewModel.HasMultipleLocations ? Visibility.Visible : Visibility.Collapsed;
        _useParcelButton.Visibility = _viewModel.CurrentStep == SolidGroundDialogStep.Parcel && !_viewModel.HasMultipleLocations ? Visibility.Visible : Visibility.Collapsed;
        _createButton.Visibility = _viewModel.CurrentStep == SolidGroundDialogStep.Review ? Visibility.Visible : Visibility.Collapsed;
        _findButton.IsDefault = _findButton.Visibility == Visibility.Visible;
        _useLocationButton.IsDefault = _useLocationButton.Visibility == Visibility.Visible;
        _useParcelButton.IsDefault = _useParcelButton.Visibility == Visibility.Visible;
        _createButton.IsDefault = _createButton.Visibility == Visibility.Visible;
    }

    private static StackPanel Field(string caption, Control control, DialogPalette palette)
    {
        StackPanel field = new() { Margin = new Thickness(0, 4, 8, 4) };
        field.Children.Add(Label(caption, control, palette));
        field.Children.Add(control);
        return field;
    }

    private static StackPanel BoundField(string captionPath, Control control, DialogPalette palette)
    {
        StackPanel field = new() { Margin = new Thickness(0, 4, 8, 4) };
        TextBlock caption = Text(string.Empty, palette);
        caption.SetBinding(TextBlock.TextProperty, new Binding(captionPath));
        field.Children.Add(caption);
        field.Children.Add(control);
        return field;
    }

    private static Label Label(string caption, Control target, DialogPalette palette) => new() { Content = caption, Target = target, Foreground = palette.WindowText, Margin = new Thickness(0, 4, 0, 2) };

    private static DataTemplate ParcelCandidateTemplate(DialogPalette palette)
    {
        FrameworkElementFactory panel = new(typeof(StackPanel));
        FrameworkElementFactory id = new(typeof(TextBlock)); id.SetBinding(TextBlock.TextProperty, new Binding("Candidate.ParcelId")); id.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold); panel.AppendChild(id);
        FrameworkElementFactory status = new(typeof(TextBlock)); status.SetBinding(TextBlock.TextProperty, new Binding("DistanceMeters") { StringFormat = "Nearby distance: {0:N1} m (0 means contains the selected location)" }); status.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); panel.AppendChild(status);
        FrameworkElementFactory area = new(typeof(TextBlock)); area.SetBinding(TextBlock.TextProperty, new Binding("Candidate.ComputedAreaSquareMeters") { StringFormat = "Legal area: {0:N0} m²" }); panel.AppendChild(area);
        FrameworkElementFactory source = new(typeof(TextBlock)); source.SetBinding(TextBlock.TextProperty, new Binding("Candidate.SourceIdentity") { StringFormat = "Source: {0}" }); source.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap); panel.AppendChild(source);
        return new DataTemplate { VisualTree = panel };
    }

    private void BrowseGeometry(TextBox target)
    {
        OpenFileDialog dialog = new() { Filter = "Boundary files (*.geojson;*.json;*.wkt)|*.geojson;*.json;*.wkt|All files (*.*)|*.*", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        try { target.Text = File.ReadAllText(dialog.FileName); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { _viewModel.ErrorText = "The selected boundary file could not be read."; }
    }

    private void OpenCoordinateMap()
    {
        if (_viewModel.SelectedGeocodeCandidate is not { } point) return;
        string latitude = point.Latitude.ToString("R", CultureInfo.InvariantCulture);
        string longitude = point.Longitude.ToString("R", CultureInfo.InvariantCulture);
        OpenExternal($"https://www.openstreetmap.org/?mlat={latitude}&mlon={longitude}#map=18/{latitude}/{longitude}");
    }

    private static void OpenExternal(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    private void ClampToWorkingArea()
    {
        Rect workArea = SystemParameters.WorkArea;
        MaxWidth = Math.Max(1d, workArea.Width - 16d);
        MaxHeight = Math.Max(1d, workArea.Height - 16d);
        MinWidth = Math.Min(640d, MaxWidth);
        MinHeight = Math.Min(480d, MaxHeight);
        Width = Math.Min(Width, MaxWidth); Height = Math.Min(Height, MaxHeight);
    }

    private void UpdateParcelLayouts()
    {
        bool wide = ActualWidth >= 700d;
        foreach (Grid layout in _parcelLayouts)
        {
            if (layout.Children.Count != 2) continue;
            layout.ColumnDefinitions.Clear(); layout.RowDefinitions.Clear();
            if (wide)
            {
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55d, GridUnitType.Star) });
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45d, GridUnitType.Star) });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(layout.Children[0], 0); Grid.SetColumn(layout.Children[0], 0);
                Grid.SetRow(layout.Children[1], 0); Grid.SetColumn(layout.Children[1], 1);
            }
            else
            {
                layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1d, GridUnitType.Star) });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.SetRow(layout.Children[0], 0); Grid.SetColumn(layout.Children[0], 0);
                Grid.SetRow(layout.Children[1], 1); Grid.SetColumn(layout.Children[1], 0);
            }
        }
    }

    private sealed class BooleanToVisibilityConverter : IValueConverter
    {
        internal static readonly BooleanToVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed class TextPresenceVisibilityConverter : IValueConverter
    {
        internal static readonly TextPresenceVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed class EntryModeVisibilityConverter(LocationEntryMode mode) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is LocationEntryMode current && current == mode ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
    private sealed class StepPanelConverter(IReadOnlyDictionary<SolidGroundDialogStep, FrameworkElement> panels) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is SolidGroundDialogStep step && panels.TryGetValue(step, out FrameworkElement? panel) ? panel : null;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
