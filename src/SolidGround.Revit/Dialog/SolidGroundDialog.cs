using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
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
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        ArgumentNullException.ThrowIfNull(palette);
        Background = palette.Window;
        Foreground = palette.WindowText;
        BorderBrush = palette.ActiveBorder;
        DialogControlStyles.Apply(this, palette);

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

    private static StackPanel BuildLocationPanel(DialogPalette palette)
    {
        StackPanel panel = new() { Margin = new Thickness(0, 8, 0, 8) };
        panel.Children.Add(Text("Find a location", palette, FontWeights.Bold));
        panel.Children.Add(Text("A location is used only to find and confirm a parcel.", palette));
        ComboBox mode = new() { ItemsSource = Enum.GetValues<LocationEntryMode>(), Margin = new Thickness(0, 16, 0, 8) };
        mode.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.EntryMode)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(mode, "Location input type");
        panel.Children.Add(mode);

        TextBox address = Input("Street address", nameof(SolidGroundDialogViewModel.AddressText), palette);
        address.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowAddress)) { Converter = BooleanToVisibilityConverter.Instance });
        panel.Children.Add(address);
        StackPanel coordinates = new() { Orientation = Orientation.Horizontal };
        coordinates.Children.Add(Input("Latitude", nameof(SolidGroundDialogViewModel.LatitudeText), palette));
        coordinates.Children.Add(Input("Longitude", nameof(SolidGroundDialogViewModel.LongitudeText), palette));
        coordinates.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowCoordinates)) { Converter = BooleanToVisibilityConverter.Instance });
        panel.Children.Add(coordinates);

        StackPanel other = new();
        other.Children.Add(Text("Other area options", palette, FontWeights.SemiBold));
        other.Children.Add(Input("West", nameof(SolidGroundDialogViewModel.WestText), palette));
        other.Children.Add(Input("South", nameof(SolidGroundDialogViewModel.SouthText), palette));
        other.Children.Add(Input("East", nameof(SolidGroundDialogViewModel.EastText), palette));
        other.Children.Add(Input("North", nameof(SolidGroundDialogViewModel.NorthText), palette));
        other.Children.Add(Input("Radius metres", nameof(SolidGroundDialogViewModel.RadiusMetersText), palette));
        other.Children.Add(Input("Paste local GeoJSON or WKT", nameof(SolidGroundDialogViewModel.LocalGeometryText), palette));
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

        ListBox parcels = new() { DisplayMemberPath = $"{nameof(ParcelProximityCandidate.Candidate)}.{nameof(ParcelBoundaryCandidate.ParcelId)}", MinHeight = 130, Margin = new Thickness(0, 8, 0, 8) };
        parcels.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.ParcelCandidates)));
        parcels.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedParcelCandidate)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(parcels, "Parcel candidates");
        panel.Children.Add(parcels);
        TextBlock containment = Text("", palette); containment.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.ContainmentLabel)));
        panel.Children.Add(containment);
        panel.Children.Add(Text("Use this parcel confirms the displayed legal boundary. Nearby parcels are never selected automatically.", palette));
        panel.Children.Add(new ParcelBoundaryPreview { Margin = new Thickness(0, 12, 0, 0), DataContext = _viewModel });
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
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.ContainmentLabel), palette));
        panel.Children.Add(BoundText(nameof(SolidGroundDialogViewModel.AccuracyDisclaimer), palette));

        ComboBox level = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 8, 0, 0) };
        level.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.LevelCandidates)));
        level.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedLevel)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(level, "Level"); panel.Children.Add(level);
        ComboBox type = new() { DisplayMemberPath = "Name", Margin = new Thickness(0, 8, 0, 0) };
        type.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.ToposolidTypeCandidates)));
        type.SetBinding(Selector.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedToposolidType)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(type, "Toposolid type"); panel.Children.Add(type);
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

    private static Button Secondary(string label, string command, DialogPalette palette, string name)
    {
        Button button = new() { Content = label, Margin = new Thickness(0, 0, 8, 0) };
        button.SetBinding(Button.CommandProperty, new Binding(command)); AutomationProperties.SetName(button, name); return button;
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
    private sealed class StepPanelConverter(IReadOnlyDictionary<SolidGroundDialogStep, FrameworkElement> panels) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is SolidGroundDialogStep step && panels.TryGetValue(step, out FrameworkElement? panel) ? panel : null;
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
