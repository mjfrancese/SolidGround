using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using SolidGround.Core.Aois;
using SolidGround.Core.Provenance;

namespace SolidGround.Revit.Dialog;

/// <summary>A bounded content panel for the Review-stage floor task. The owning dialog keeps the one shared footer.</summary>
internal sealed class BuildingFloorReferencePanel : Grid
{
    private readonly BuildingFloorReferenceViewModel viewModel;
    private readonly DialogPalette palette;
    private GroundPointPreview preview = null!;
    private TextBlock outlineStatus = null!;
    private StackPanel estimateFields = null!;
    private StackPanel knownFields = null!;
    private StackPanel provisionalFields = null!;
    private string? legalGeometryToken;

    internal BuildingFloorReferencePanel(BuildingFloorReferenceViewModel viewModel, DialogPalette palette)
    {
        this.viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
        DataContext = viewModel;
        Margin = new Thickness(0, 8, 0, 8);

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        TextBlock heading = Text("Building floor reference", FontWeights.Bold);
        AutomationProperties.SetName(heading, "Building floor reference");
        Children.Add(heading);

        TextBlock instruction = Text("Choose how to put the unfinished first-floor plane on the selected project level.");
        instruction.Margin = new Thickness(0, 4, 0, 12);
        Grid.SetRow(instruction, 1);
        Children.Add(instruction);

        StackPanel modes = new() { Margin = new Thickness(0, 0, 0, 12) };
        modes.Children.Add(ModeChoice(FloorReferenceMode.EstimatedGradeRise, "Estimate from ground beside the entrance", "Select exterior ground and measure vertically to the unfinished first floor."));
        modes.Children.Add(ModeChoice(FloorReferenceMode.KnownElevation, "Enter a known floor elevation", "Use a finite source-datum elevation you can describe and acknowledge."));
        modes.Children.Add(ModeChoice(FloorReferenceMode.ProvisionalGround, "Use a provisional preview for now", "Uses the legal-area ground median. First-floor elevation remains unknown."));
        Grid.SetRow(modes, 2);
        Children.Add(modes);

        StackPanel fields = new();
        estimateFields = BuildEstimateFields();
        knownFields = BuildKnownFields();
        provisionalFields = BuildProvisionalFields();
        fields.Children.Add(estimateFields);
        fields.Children.Add(knownFields);
        fields.Children.Add(provisionalFields);
        Grid.SetRow(fields, 3);
        Children.Add(fields);

        StackPanel status = new() { Margin = new Thickness(0, 12, 0, 0) };
        TextBlock validation = Text(string.Empty);
        validation.Foreground = palette.Error;
        validation.SetBinding(TextBlock.TextProperty, new Binding(nameof(BuildingFloorReferenceViewModel.ValidationMessage)));
        validation.SetBinding(VisibilityProperty, new Binding(nameof(BuildingFloorReferenceViewModel.ValidationMessage)) { Converter = TextVisibilityConverter.Instance });
        AutomationProperties.SetLiveSetting(validation, AutomationLiveSetting.Assertive);
        status.Children.Add(validation);
        TextBlock explanation = Text(viewModel.ExistingCoordinatesExplanation);
        explanation.Foreground = palette.Error;
        explanation.SetBinding(VisibilityProperty, new Binding(nameof(BuildingFloorReferenceViewModel.IsFloorPlacementBlocked)) { Converter = BoolVisibilityConverter.Instance });
        status.Children.Add(explanation);
        Button originalElevations = new()
        {
            Content = "Keep original source elevations",
            Margin = new Thickness(0, 6, 0, 0),
            Foreground = palette.ControlText,
        };
        originalElevations.SetBinding(Button.CommandProperty, new Binding(nameof(BuildingFloorReferenceViewModel.UseOriginalSourceElevationsCommand)));
        AutomationProperties.SetName(originalElevations, "Keep original source elevations");
        AutomationProperties.SetHelpText(originalElevations, "Confirm the historical source-elevation placement without preparing ground data.");
        status.Children.Add(originalElevations);
        Grid.SetRow(status, 4);
        Children.Add(status);

        this.viewModel.PropertyChanged += OnViewModelPropertyChanged;
        this.viewModel.PreparedChanged += OnPreparedChanged;
        preview.PointChanged += OnPreviewPointChanged;
        preview.PointAccepted += (_, _) => { /* Enter accepts the pin only; the dialog footer confirms the floor. */ };
        UpdateModeVisibility();
    }

    private StackPanel BuildEstimateFields()
    {
        StackPanel panel = new() { Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(Text("Select the ground beside your entrance, then enter the vertical distance up to the unfinished first floor."));
        TextBlock previewNotice = Text("Load ground preview prepares terrain for this run. It does not change the Revit document.");
        previewNotice.Margin = new Thickness(0, 4, 0, 8);
        panel.Children.Add(previewNotice);
        preview = new GroundPointPreview(palette) { Margin = new Thickness(0, 4, 0, 8) };
        AutomationProperties.SetName(preview, "Choose ground beside entrance");
        panel.Children.Add(preview);
        TextBlock coarseNotice = Text("Only coarse map tiles are requested for optional approximate outlines. They help orientation only; DEM ground data does not identify a door or building height.");
        coarseNotice.Margin = new Thickness(0, 2, 0, 4);
        panel.Children.Add(coarseNotice);
        outlineStatus = Text(string.Empty);
        outlineStatus.SetBinding(TextBlock.TextProperty, new Binding(nameof(BuildingFloorReferenceViewModel.OutlineStatus)));
        AutomationProperties.SetLiveSetting(outlineStatus, AutomationLiveSetting.Polite);
        panel.Children.Add(outlineStatus);
        TextBlock outlineAttribution = Text(string.Empty);
        outlineAttribution.SetBinding(TextBlock.TextProperty, new Binding(nameof(BuildingFloorReferenceViewModel.OutlineAttributionSummary)));
        panel.Children.Add(outlineAttribution);
        Button retryOutlines = new() { Content = "Retry outlines", Foreground = palette.ControlText, Margin = new Thickness(0, 3, 0, 4) };
        retryOutlines.SetBinding(Button.CommandProperty, new Binding(nameof(BuildingFloorReferenceViewModel.RetryOutlinesCommand)));
        retryOutlines.SetBinding(Button.IsEnabledProperty, new Binding(nameof(BuildingFloorReferenceViewModel.CanRetryOutlines)));
        AutomationProperties.SetName(retryOutlines, "Retry approximate building outlines");
        AutomationProperties.SetHelpText(retryOutlines, "Retries optional approximate building outlines without changing the selected ground point.");
        panel.Children.Add(retryOutlines);
        TextBox rise = Input("Measured rise to unfinished first floor", nameof(BuildingFloorReferenceViewModel.MeasuredRiseText));
        StackPanel riseField = Field("Measured rise to unfinished first floor", rise, true);
        riseField.SetBinding(VisibilityProperty, new Binding(nameof(BuildingFloorReferenceViewModel.HasValidSelection)) { Converter = BoolVisibilityConverter.Instance });
        panel.Children.Add(riseField);
        TextBlock hint = Text(string.Empty);
        hint.SetBinding(TextBlock.TextProperty, new Binding(nameof(BuildingFloorReferenceViewModel.MeasurementHint)));
        hint.SetBinding(VisibilityProperty, new Binding(nameof(BuildingFloorReferenceViewModel.HasValidSelection)) { Converter = BoolVisibilityConverter.Instance });
        panel.Children.Add(hint);
        CheckBox below = new() { Content = "The unfinished floor is below exterior grade", Foreground = palette.ControlText, Margin = new Thickness(0, 6, 0, 0) };
        below.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(BuildingFloorReferenceViewModel.BelowGrade)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(below, "Floor is below exterior grade");
        below.SetBinding(VisibilityProperty, new Binding(nameof(BuildingFloorReferenceViewModel.HasValidSelection)) { Converter = BoolVisibilityConverter.Instance });
        panel.Children.Add(below);
        TextBlock selectedHeight = Text(string.Empty);
        selectedHeight.SetBinding(TextBlock.TextProperty, new Binding(nameof(BuildingFloorReferenceViewModel.SelectedSampleHeight)));
        selectedHeight.SetBinding(VisibilityProperty, new Binding(nameof(BuildingFloorReferenceViewModel.HasValidSelection)) { Converter = BoolVisibilityConverter.Instance });
        panel.Children.Add(selectedHeight);
        Expander coordinateEntry = new() { Header = "Enter coordinates", Foreground = palette.ControlText, Margin = new Thickness(0, 8, 0, 0) };
        StackPanel coordinates = new();
        coordinates.Children.Add(Field("Latitude", Input("Ground-point latitude", nameof(BuildingFloorReferenceViewModel.CoordinateLatitudeText)), true));
        coordinates.Children.Add(Field("Longitude", Input("Ground-point longitude", nameof(BuildingFloorReferenceViewModel.CoordinateLongitudeText)), true));
        Button placeCoordinates = new() { Content = "Place ground point", Foreground = palette.ControlText, Margin = new Thickness(0, 4, 0, 0) };
        placeCoordinates.SetBinding(Button.CommandProperty, new Binding(nameof(BuildingFloorReferenceViewModel.PlaceEnteredCoordinatesCommand)));
        AutomationProperties.SetName(placeCoordinates, "Place entered ground coordinates");
        coordinates.Children.Add(placeCoordinates);
        coordinateEntry.Content = coordinates;
        panel.Children.Add(coordinateEntry);
        return panel;
    }

    private StackPanel BuildKnownFields()
    {
        StackPanel panel = new() { Margin = new Thickness(0, 4, 0, 0) };
        TextBlock sourceReference = Text(string.Empty);
        sourceReference.SetBinding(TextBlock.TextProperty, new Binding(nameof(BuildingFloorReferenceViewModel.SourceReferenceSummary)));
        panel.Children.Add(sourceReference);
        TextBox elevation = Input("Known floor elevation", nameof(BuildingFloorReferenceViewModel.KnownElevationText));
        StackPanel elevationField = Field("Known floor elevation", elevation, true);
        Label elevationCaption = (Label)elevationField.Children[0];
        elevationCaption.SetBinding(ContentControl.ContentProperty, new Binding(nameof(BuildingFloorReferenceViewModel.KnownElevationLabel)));
        panel.Children.Add(elevationField);
        TextBox source = Input("Elevation source description", nameof(BuildingFloorReferenceViewModel.KnownSourceDescription));
        panel.Children.Add(Field("Elevation source description", source, true));
        CheckBox datum = new() { Content = "I confirm this elevation uses the stated source vertical datum", Foreground = palette.ControlText, Margin = new Thickness(0, 6, 0, 0) };
        datum.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(BuildingFloorReferenceViewModel.KnownDatumAcknowledged)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(datum, "Confirm known elevation source datum");
        panel.Children.Add(datum);
        return panel;
    }

    private StackPanel BuildProvisionalFields()
    {
        StackPanel panel = new() { Margin = new Thickness(0, 4, 0, 0) };
        TextBlock notice = Text("Ground-centered preview; first-floor elevation not set. The legal-area median is temporary and is not survey authority.");
        notice.Foreground = palette.Error;
        panel.Children.Add(notice);
        return panel;
    }

    private RadioButton ModeChoice(FloorReferenceMode mode, string label, string help)
    {
        RadioButton option = new() { Content = label, GroupName = "floor-reference-mode", Foreground = palette.ControlText, Margin = new Thickness(0, 4, 0, 0) };
        option.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(BuildingFloorReferenceViewModel.Mode))
        {
            Mode = BindingMode.TwoWay,
            Converter = FloorModeConverter.For(mode),
        });
        AutomationProperties.SetName(option, label);
        AutomationProperties.SetHelpText(option, help);
        return option;
    }

    private void OnPreparedChanged(object? sender, EventArgs e)
    {
        if (viewModel.PreparedTerrain is null)
        {
            preview.IsPlacementEnabled = false;
            preview.SelectedPoint = null;
            preview.IsSelectionValid = false;
            preview.SelectionStatus = "Load ground preview before placing a ground point.";
            return;
        }

        PolygonalRegion context = BuildingFloorReferenceViewModel.GetLegalRegionWgs84(viewModel.PreparedTerrain);
        string nextToken = context.Geometry.AsText();
        bool resetView = legalGeometryToken is null || !string.Equals(legalGeometryToken, nextToken, StringComparison.Ordinal);
        preview.SetContext(context, viewModel.VisibleOutlines, resetView);
        legalGeometryToken = nextToken;
        preview.IsPlacementEnabled = true;
        preview.SelectedPoint = viewModel.SelectedPoint is { } point ? new SolidGround.Core.Geometry.Coordinate2D(point.Wgs84Longitude, point.Wgs84Latitude) : null;
        preview.IsSelectionValid = viewModel.SelectedSample is not null;
        preview.SelectionStatus = viewModel.SelectedSample is null ? "Ground preview ready. Choose a point beside the entrance." : "Usable ground height selected.";
        _ = viewModel.LoadOutlinesAsync();
    }

    private void OnPreviewPointChanged(SolidGround.Core.Geometry.Coordinate2D point)
    {
        viewModel.SelectPoint(point);
        preview.IsSelectionValid = viewModel.SelectedSample is not null;
        preview.SelectionStatus = viewModel.ValidationMessage ?? "Usable ground height selected.";
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BuildingFloorReferenceViewModel.Mode))
        {
            UpdateModeVisibility();
        }
        else if (e.PropertyName == nameof(BuildingFloorReferenceViewModel.VisibleOutlines) && viewModel.PreparedTerrain is not null)
        {
            preview.SetContext(BuildingFloorReferenceViewModel.GetLegalRegionWgs84(viewModel.PreparedTerrain), viewModel.VisibleOutlines);
        }
        else if (e.PropertyName is nameof(BuildingFloorReferenceViewModel.SelectedPoint)
            or nameof(BuildingFloorReferenceViewModel.SelectedSample))
        {
            preview.SelectedPoint = viewModel.SelectedPoint is { } point
                ? new SolidGround.Core.Geometry.Coordinate2D(point.Wgs84Longitude, point.Wgs84Latitude)
                : null;
            preview.IsSelectionValid = viewModel.SelectedSample is not null;
            preview.SelectionStatus = viewModel.ValidationMessage ?? (viewModel.SelectedSample is null
                ? "Ground preview ready. Choose a point beside the entrance."
                : "Usable ground height selected.");
        }
    }

    private void UpdateModeVisibility()
    {
        estimateFields.Visibility = viewModel.ShowEstimate ? Visibility.Visible : Visibility.Collapsed;
        knownFields.Visibility = viewModel.ShowKnownElevation ? Visibility.Visible : Visibility.Collapsed;
        provisionalFields.Visibility = viewModel.ShowProvisional ? Visibility.Visible : Visibility.Collapsed;
    }

    private TextBlock Text(string text, FontWeight? weight = null) => new()
    {
        Text = text,
        Foreground = palette.ControlText,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 3, 0, 3),
        FontWeight = weight ?? FontWeights.Normal,
    };

    private static TextBox Input(string name, string path)
    {
        TextBox input = new() { MinWidth = 180d, Margin = new Thickness(0, 3, 0, 0) };
        input.SetBinding(TextBox.TextProperty, new Binding(path) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        AutomationProperties.SetName(input, name);
        return input;
    }

    private StackPanel Field(string label, Control control, bool required)
    {
        StackPanel field = new() { Margin = new Thickness(0, 7, 0, 0) };
        Label caption = new() { Content = required ? label + " (required)" : label, Foreground = palette.ControlText, Target = control };
        field.Children.Add(caption);
        field.Children.Add(control);
        return field;
    }

    private sealed class BoolVisibilityConverter : IValueConverter
    {
        internal static readonly BoolVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is true ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class TextVisibilityConverter : IValueConverter
    {
        internal static readonly TextVisibilityConverter Instance = new();
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class FloorModeConverter(FloorReferenceMode mode) : IValueConverter
    {
        private readonly FloorReferenceMode mode = mode;
        internal static FloorModeConverter For(FloorReferenceMode mode) => new(mode);
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => Equals(value, mode);
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => value is true ? mode : Binding.DoNothing;
    }
}
