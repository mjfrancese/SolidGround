using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Autodesk.Revit.UI;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;
// Autodesk.Revit.UI also declares TextBox/ComboBox (ribbon controls of the same short name); these aliases
// pin every unqualified use in this file to the WPF control instead.
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// The interactive Revit-host dialog (SolidGround Issue #31, PH3-4: see
/// docs/architecture/revit-interactive-dialog.md "Purpose and boundary"): a code-behind-only WPF
/// <see cref="Window"/>, zero <c>.xaml</c>/BAML by design. An isolated Revit add-in context has no
/// <c>App.xaml</c>, and WPF's own XAML parser can double-load a BAML-carrying assembly across two
/// contexts (<c>Nice3point/RevitToolkit#7</c>, the still-open <c>dotnet/wpf#1700</c>); building this window
/// entirely in code sidesteps that failure mode outright rather than working around it.
/// </summary>
/// <remarks>
/// This stage (Stage C) builds every one of the ten originally designed sections plus owner decision 1's own
/// step 0 AOI-source choice, full theming/accessibility, and the synchronous network bridge (see
/// <see cref="SolidGroundDialogViewModel"/>). It is not wired into <c>CreateToposolidCommand.Execute</c>: this
/// type is not constructed anywhere in this stage, so it remains unreachable from a running add-in. Wiring
/// (a <c>SolidGroundDialogHost.ShowModal</c> call site) is a later stage's own scope.
/// </remarks>
internal sealed class SolidGroundDialog : Window
{
    private readonly SolidGroundDialogViewModel _viewModel;
    private readonly SolidGroundDialogInputs _inputs;
    private readonly Dictionary<SolidGroundDialogStep, FrameworkElement> _stepPanels;

    private static readonly IValueConverter BooleanToVisibility = new BooleanToVisibilityConverter();
    private static readonly IValueConverter TextPresenceToVisibility = new TextPresenceVisibilityConverter();

    internal SolidGroundDialog(SolidGroundDialogViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        _inputs = viewModel.Inputs;
        DataContext = viewModel;

        Title = "SolidGround";
        SizeToContent = SizeToContent.WidthAndHeight;
        MinWidth = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        // Theming and accessibility (docs/architecture/revit-interactive-dialog.md "Theming and
        // accessibility"): read once, here, never subscribed to either signal -- "modal only" (see
        // DialogTheme's own remarks). highContrast wins over revitTheme's Light/Dark choice.
        UITheme revitTheme = UIThemeManager.CurrentTheme;
        bool highContrast = SystemParameters.HighContrast;
        DialogPalette palette = DialogTheme.Resolve(revitTheme, highContrast);
        Background = palette.Window;
        Foreground = palette.WindowText;
        BorderBrush = palette.ActiveBorder;

        // Numeric-binding culture (review finding, minor, fixed; docs/architecture/revit-interactive-dialog.md
        // "Theming and accessibility", "Numeric-binding culture"): bufferTextBox/pointBudgetTextBox below
        // two-way bind straight to the double/int-typed BufferMeters/PointBudget with no Converter or
        // ConverterCulture, so WPF's own implicit numeric conversion resolves its CultureInfo from this
        // Window's Language property -- whose default metadata is hardcoded en-US regardless of the OS's
        // configured culture -- not from CultureInfo.CurrentCulture. Read once, here, alongside the
        // theme/high-contrast reads above; Language is an inherited dependency property, so this one
        // assignment reaches both numeric TextBoxes (and any future one) without a per-binding change.
        Language = System.Windows.Markup.XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag);

        _stepPanels = BuildStepPanels(palette);

        TextBlock busyBanner = new()
        {
            Text = "Working -- Revit is unresponsive until this completes...",
            Foreground = palette.Highlight,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(12, 8, 12, 0),
        };
        busyBanner.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.IsBusy)) { Converter = BooleanToVisibility });

        TextBlock errorBanner = new()
        {
            Foreground = palette.Error,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 8, 12, 0),
            MaxWidth = 480,
        };
        errorBanner.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.ErrorText)));
        errorBanner.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ErrorText)) { Converter = TextPresenceToVisibility });

        ContentControl stepHost = new() { Margin = new Thickness(12), Width = 480 };
        stepHost.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SolidGroundDialogViewModel.CurrentStep)) { Converter = new StepToPanelConverter(_stepPanels) });

        FrameworkElement navigationBar = BuildNavigationBar(palette);

        StackPanel root = new();
        root.Children.Add(busyBanner);
        root.Children.Add(errorBanner);
        root.Children.Add(stepHost);
        root.Children.Add(navigationBar);

        Content = root;

        viewModel.CloseRequested += OnCloseRequested;
        Closed += (_, _) => viewModel.CloseRequested -= OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        DialogResult = _viewModel.Result is not null;
        Close();
    }

    // ------------------------------------------------------------------------------------------------------
    // Step panels
    // ------------------------------------------------------------------------------------------------------

    private Dictionary<SolidGroundDialogStep, FrameworkElement> BuildStepPanels(DialogPalette palette) => new()
    {
        [SolidGroundDialogStep.AoiSourceChoice] = BuildAoiSourceChoicePanel(palette),
        [SolidGroundDialogStep.AddressEntry] = BuildAddressEntryPanel(palette),
        [SolidGroundDialogStep.GeocodeCandidates] = BuildGeocodeCandidatesPanel(palette),
        [SolidGroundDialogStep.ParcelCandidates] = BuildParcelCandidatesPanel(palette),
        [SolidGroundDialogStep.Buffer] = BuildBufferPanel(palette),
        [SolidGroundDialogStep.PointBudget] = BuildPointBudgetPanel(palette),
        [SolidGroundDialogStep.UnitChoice] = BuildUnitChoicePanel(palette),
        [SolidGroundDialogStep.LevelAndToposolidType] = BuildLevelAndToposolidTypePanel(palette),
        [SolidGroundDialogStep.SharedCoordinatesOptIn] = BuildSharedCoordinatesOptInPanel(palette),
        [SolidGroundDialogStep.ProvenancePreview] = BuildProvenancePreviewPanel(palette),
        [SolidGroundDialogStep.PreflightSummary] = BuildPreflightSummaryPanel(palette),
    };

    private static TextBlock BuildHeader(string text, DialogPalette palette) => new()
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Foreground = palette.WindowText,
        Margin = new Thickness(0, 0, 0, 8),
        TextWrapping = TextWrapping.Wrap,
    };

    // ---- Step 0 (owner decision 1): AOI source choice --------------------------------------------------

    private StackPanel BuildAoiSourceChoicePanel(DialogPalette palette)
    {
        RadioButton findParcelRadio = new()
        {
            Content = "Find a parcel (address or point)",
            GroupName = "AoiSource",
            IsChecked = _viewModel.AoiSource == DialogAoiSource.FindParcel,
            Foreground = palette.WindowText,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetName(findParcelRadio, "Find a parcel");
        findParcelRadio.Checked += (_, _) => _viewModel.AoiSource = DialogAoiSource.FindParcel;

        RadioButton useSettingsRadio = new()
        {
            Content = "Use the area in the settings file",
            GroupName = "AoiSource",
            IsChecked = _viewModel.AoiSource == DialogAoiSource.UseSettingsFile,
            Foreground = palette.WindowText,
        };
        AutomationProperties.SetName(useSettingsRadio, "Use the area in the settings file");
        useSettingsRadio.Checked += (_, _) => _viewModel.AoiSource = DialogAoiSource.UseSettingsFile;

        TextBlock settingsSummary = new()
        {
            Text = "Settings file area of interest: " + DescribeAoiSettings(_inputs.ConfiguredAreaOfInterest),
            Foreground = palette.GrayText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(24, 6, 0, 0),
        };

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Where should this run's area of interest come from?", palette));
        panel.Children.Add(findParcelRadio);
        panel.Children.Add(useSettingsRadio);
        panel.Children.Add(settingsSummary);
        return panel;
    }

    private static string DescribeAoiSettings(AoiSettings settings) => settings.Kind switch
    {
        AreaOfInterestKind.BoundingBox => "a bounding box.",
        AreaOfInterestKind.Radius => "a center point and radius.",
        AreaOfInterestKind.Parcel => "a parcel geometry file.",
        _ => "an unrecognized configuration.",
    };

    // ---- Step 1: address entry ------------------------------------------------------------------------------

    private static StackPanel BuildAddressEntryPanel(DialogPalette palette)
    {
        TextBox addressTextBox = new()
        {
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
            Margin = new Thickness(0, 0, 0, 8),
        };
        addressTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.AddressText))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        AutomationProperties.SetName(addressTextBox, "Street address, or a latitude, longitude pair");

        Button findButton = new()
        {
            Content = "Find",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 4, 16, 4),
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        findButton.SetBinding(Button.CommandProperty, new Binding(nameof(SolidGroundDialogViewModel.GeocodeCommand)));
        AutomationProperties.SetName(findButton, "Find");

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Enter a street address, or a \"latitude, longitude\" pair.", palette));
        panel.Children.Add(addressTextBox);
        panel.Children.Add(findButton);
        return panel;
    }

    // ---- Step 2: geocode candidates --------------------------------------------------------------------------

    private static StackPanel BuildGeocodeCandidatesPanel(DialogPalette palette)
    {
        ListBox listBox = new() { Height = 140, Foreground = palette.ControlText, Background = palette.ControlBackground };
        listBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.GeocodeCandidates)));
        listBox.SetBinding(ListBox.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedGeocodeCandidate)) { Mode = BindingMode.TwoWay });
        listBox.ItemTemplate = BuildGeocodeCandidateTemplate();
        AutomationProperties.SetName(listBox, "Geocode candidates");

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Confirm the best match.", palette));
        panel.Children.Add(listBox);
        return panel;
    }

    private static DataTemplate BuildGeocodeCandidateTemplate()
    {
        FrameworkElementFactory root = new(typeof(StackPanel));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(2));

        FrameworkElementFactory addressLine = new(typeof(TextBlock));
        addressLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(AddressGeocodeCandidate.MatchedAddress)));
        addressLine.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        root.AppendChild(addressLine);

        FrameworkElementFactory precisionLine = new(typeof(TextBlock));
        precisionLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(AddressGeocodeCandidate.PrecisionLabel)));
        root.AppendChild(precisionLine);

        FrameworkElementFactory attributionLine = new(typeof(TextBlock));
        attributionLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(AddressGeocodeCandidate.Attribution)));
        attributionLine.SetValue(TextBlock.FontSizeProperty, 11d);
        attributionLine.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        root.AppendChild(attributionLine);

        return new DataTemplate { VisualTree = root };
    }

    // ---- Step 3: parcel candidates with legal-description preview -------------------------------------------

    private static StackPanel BuildParcelCandidatesPanel(DialogPalette palette)
    {
        Button findParcelButton = new()
        {
            Content = "Find parcel",
            HorizontalAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(0, 0, 0, 8),
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        findParcelButton.SetBinding(Button.CommandProperty, new Binding(nameof(SolidGroundDialogViewModel.FindParcelCommand)));
        AutomationProperties.SetName(findParcelButton, "Find parcel");

        ListBox listBox = new() { Height = 140, Foreground = palette.ControlText, Background = palette.ControlBackground };
        listBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SolidGroundDialogViewModel.ParcelCandidates)));
        listBox.SetBinding(ListBox.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedParcelCandidate)) { Mode = BindingMode.TwoWay });
        listBox.ItemTemplate = BuildParcelCandidateTemplate();
        AutomationProperties.SetName(listBox, "Parcel candidates");

        TextBlock zeroCandidatesMessage = new()
        {
            Text = "No parcel boundary was found at this point; go back and try a different address or candidate.",
            Foreground = palette.GrayText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        zeroCandidatesMessage.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowNoParcelCandidatesMessage)) { Converter = BooleanToVisibility });

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Confirm the parcel boundary.", palette));
        panel.Children.Add(findParcelButton);
        panel.Children.Add(listBox);
        panel.Children.Add(zeroCandidatesMessage);
        return panel;
    }

    private static DataTemplate BuildParcelCandidateTemplate()
    {
        FrameworkElementFactory root = new(typeof(StackPanel));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(2));

        FrameworkElementFactory idLine = new(typeof(TextBlock));
        idLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(ParcelBoundaryCandidate.ParcelId)) { StringFormat = "Parcel {0}" });
        idLine.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        root.AppendChild(idLine);

        FrameworkElementFactory situsLine = new(typeof(TextBlock));
        situsLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(ParcelBoundaryCandidate.SitusAddress)));
        root.AppendChild(situsLine);

        FrameworkElementFactory legalDescriptionLine = new(typeof(TextBlock));
        legalDescriptionLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(ParcelBoundaryCandidate.LegalDescription)));
        legalDescriptionLine.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        root.AppendChild(legalDescriptionLine);

        FrameworkElementFactory areaLine = new(typeof(TextBlock));
        areaLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(ParcelBoundaryCandidate.ComputedAreaSquareMeters)) { StringFormat = "{0:N0} m²" });
        areaLine.SetValue(TextBlock.FontSizeProperty, 11d);
        root.AppendChild(areaLine);

        FrameworkElementFactory accuracyLine = new(typeof(TextBlock));
        accuracyLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(ParcelBoundaryCandidate.AccuracyLabel)));
        accuracyLine.SetValue(TextBlock.FontSizeProperty, 11d);
        accuracyLine.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        root.AppendChild(accuracyLine);

        return new DataTemplate { VisualTree = root };
    }

    // ---- Step 4: AOI buffer -----------------------------------------------------------------------------------

    private static StackPanel BuildBufferPanel(DialogPalette palette)
    {
        TextBox bufferTextBox = new()
        {
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        bufferTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.BufferMeters))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        AutomationProperties.SetName(bufferTextBox, "AOI buffer, in meters");
        AutomationProperties.SetHelpText(
            bufferTextBox,
            "An optional margin, in meters, added around the parcel boundary before terrain is clipped to it.");

        TextBlock errorText = new() { Foreground = palette.Error, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        errorText.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.BufferErrorText)));
        errorText.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.BufferErrorText)) { Converter = TextPresenceToVisibility });

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("AOI buffer (meters)", palette));
        panel.Children.Add(bufferTextBox);
        panel.Children.Add(errorText);
        return panel;
    }

    // ---- Step 5: point budget (Revit.ini guard warning) --------------------------------------------------------

    private static StackPanel BuildPointBudgetPanel(DialogPalette palette)
    {
        TextBox pointBudgetTextBox = new()
        {
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        pointBudgetTextBox.SetBinding(TextBox.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.PointBudget))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
        });
        AutomationProperties.SetName(pointBudgetTextBox, "Point budget");
        AutomationProperties.SetHelpText(
            pointBudgetTextBox,
            "The maximum number of terrain points this toposolid keeps after simplification. A warning " +
            "appears below when this exceeds Revit's own configured limit on this machine.");

        TextBlock warningText = new()
        {
            Foreground = palette.Highlight,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            MaxWidth = 440,
        };
        warningText.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.PointBudgetWarningText)));
        warningText.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowPointBudgetWarning)) { Converter = BooleanToVisibility });

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Point budget", palette));
        panel.Children.Add(pointBudgetTextBox);
        panel.Children.Add(warningText);
        return panel;
    }

    // ---- Step 6: unit choice -----------------------------------------------------------------------------------

    private StackPanel BuildUnitChoicePanel(DialogPalette palette)
    {
        RadioButton usSurveyFootRadio = new()
        {
            Content = "U.S. survey foot",
            GroupName = "OutputUnit",
            IsChecked = _viewModel.SelectedOutputUnit == LengthUnit.UsSurveyFoot,
            Foreground = palette.WindowText,
            Margin = new Thickness(0, 0, 0, 6),
        };
        AutomationProperties.SetName(usSurveyFootRadio, "U.S. survey foot");
        usSurveyFootRadio.Checked += (_, _) => _viewModel.SelectedOutputUnit = LengthUnit.UsSurveyFoot;

        RadioButton internationalFootRadio = new()
        {
            Content = "International foot",
            GroupName = "OutputUnit",
            IsChecked = _viewModel.SelectedOutputUnit == LengthUnit.InternationalFoot,
            Foreground = palette.WindowText,
        };
        AutomationProperties.SetName(internationalFootRadio, "International foot");
        internationalFootRadio.Checked += (_, _) => _viewModel.SelectedOutputUnit = LengthUnit.InternationalFoot;

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Output unit", palette));
        panel.Children.Add(usSurveyFootRadio);
        panel.Children.Add(internationalFootRadio);
        return panel;
    }

    // ---- Step 7: level/toposolid-type ---------------------------------------------------------------------------

    private StackPanel BuildLevelAndToposolidTypePanel(DialogPalette palette)
    {
        ComboBox levelComboBox = new()
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = _viewModel.Levels,
            DisplayMemberPath = nameof(NamedElevationCandidate.Name),
            Margin = new Thickness(0, 0, 0, 16),
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        // The dropdown popup is a separately templated surface, not the closed selection box (review finding,
        // major): WPF's stock (non-Fluent) ComboBox control template paints the popup's own Border from the
        // SystemColors.WindowBrushKey dynamic resource, never from a TemplateBinding to this control's own
        // Background, so setting Background alone (as above) left the popup showing the OS's plain, unthemed
        // light surface -- with the popup's item text still inheriting this control's own (correctly themed,
        // e.g. white-on-Dark) Foreground, an unreadable combination. This local resource override re-themes
        // only this ComboBox's own popup; it does not touch any other control's use of the same system key.
        levelComboBox.Resources[SystemColors.WindowBrushKey] = palette.ControlBackground;
        levelComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedLevel)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(levelComboBox, "Level");

        ComboBox toposolidTypeComboBox = new()
        {
            Width = 280,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsSource = _viewModel.ToposolidTypes,
            DisplayMemberPath = nameof(NamedCandidate.Name),
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        toposolidTypeComboBox.Resources[SystemColors.WindowBrushKey] = palette.ControlBackground;
        toposolidTypeComboBox.SetBinding(ComboBox.SelectedItemProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedToposolidType)) { Mode = BindingMode.TwoWay });
        AutomationProperties.SetName(toposolidTypeComboBox, "Toposolid type");

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Level", palette));
        panel.Children.Add(levelComboBox);
        panel.Children.Add(BuildHeader("Toposolid type", palette));
        panel.Children.Add(toposolidTypeComboBox);
        return panel;
    }

    // ---- Step 8: shared-coordinates opt-in checkbox -------------------------------------------------------------

    private StackPanel BuildSharedCoordinatesOptInPanel(DialogPalette palette)
    {
        CheckBox checkBox = new() { Content = "Write shared coordinates if none exist yet", Foreground = palette.WindowText };
        checkBox.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(SolidGroundDialogViewModel.WriteSharedCoordinatesIfAbsent)) { Mode = BindingMode.TwoWay });
        checkBox.IsEnabled = _viewModel.SharedCoordinatesCheckboxEnabled;
        AutomationProperties.SetName(checkBox, "Write shared coordinates if none exist yet");
        AutomationProperties.SetHelpText(
            checkBox,
            "When checked, and no shared coordinates exist in this document yet, SolidGround writes this " +
            "run's parcel origin as the project's shared coordinates. Off by default; never overwrites " +
            "existing shared coordinates.");

        TextBlock disabledExplanation = new()
        {
            Text =
                "This document already appears to have shared coordinates set, so this option is " +
                "unavailable; SolidGround will not overwrite existing shared coordinates.",
            Foreground = palette.GrayText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = _viewModel.SharedCoordinatesCheckboxEnabled ? Visibility.Collapsed : Visibility.Visible,
        };

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Shared coordinates", palette));
        panel.Children.Add(checkBox);
        panel.Children.Add(disabledExplanation);
        return panel;
    }

    // ---- Step 9: provenance/accuracy preview ----------------------------------------------------------------------

    private static StackPanel BuildProvenancePreviewPanel(DialogPalette palette)
    {
        IValueConverter visibleWhenFindParcel = new EqualsVisibilityConverter(DialogAoiSource.FindParcel);
        IValueConverter visibleWhenSettingsFile = new EqualsVisibilityConverter(DialogAoiSource.UseSettingsFile);

        // Split into two mutually exclusive TextBlocks, not one AoiSource-only-gated block (review finding,
        // major): a direct "latitude, longitude" entry also leaves AoiSource at FindParcel, but
        // AddressParcelProvenanceFactory.Create never attaches address data for it (addressWasGeocoded is
        // false), so the single old "the confirmed address will be included" sentence was false for that
        // sub-case. ShowFindParcelGeocodedIntro/ShowFindParcelDirectPointIntro are the exact, mutually
        // exclusive conditions for each sub-case (both already imply AoiSource == FindParcel).
        TextBlock findParcelGeocodedIntro = new()
        {
            Text =
                "This run's area of interest came from an interactive address lookup. The confirmed " +
                "address will be included in this run's exported provenance record.",
            Foreground = palette.WindowText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        findParcelGeocodedIntro.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowFindParcelGeocodedIntro)) { Converter = BooleanToVisibility });

        TextBlock findParcelDirectPointIntro = new()
        {
            Text =
                "This run's area of interest came from coordinates entered directly, not an address lookup. " +
                "No address will be attached to this run's exported provenance record.",
            Foreground = palette.WindowText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        findParcelDirectPointIntro.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowFindParcelDirectPointIntro)) { Converter = BooleanToVisibility });

        TextBlock geocodeAttribution = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        geocodeAttribution.SetBinding(
            TextBlock.TextProperty,
            new Binding($"{nameof(SolidGroundDialogViewModel.SelectedGeocodeCandidate)}.{nameof(AddressGeocodeCandidate.Attribution)}") { StringFormat = "Address source: {0}" });
        geocodeAttribution.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenFindParcel });

        TextBlock parcelAccuracy = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
        parcelAccuracy.SetBinding(
            TextBlock.TextProperty,
            new Binding($"{nameof(SolidGroundDialogViewModel.SelectedParcelCandidate)}.{nameof(ParcelBoundaryCandidate.AccuracyLabel)}"));
        parcelAccuracy.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenFindParcel });

        TextBlock parcelDisclaimer = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        parcelDisclaimer.SetBinding(
            TextBlock.TextProperty,
            new Binding($"{nameof(SolidGroundDialogViewModel.SelectedParcelCandidate)}.{nameof(ParcelBoundaryCandidate.LicenseDisclaimerText)}"));
        parcelDisclaimer.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenFindParcel });

        TextBlock settingsFileNote = new()
        {
            Text =
                "This run's area of interest comes from the settings file; no address or parcel lookup was " +
                "performed, so no address/parcel provenance will be attached to this run.",
            Foreground = palette.WindowText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        };
        settingsFileNote.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenSettingsFile });

        // AGENTS.md "Accuracy and product claims": this exact sentence must remain, verbatim, wherever
        // SolidGround describes itself to an operator.
        TextBlock siteFormDisclaimer = new()
        {
            Text = "SolidGround is a site-form tool, not a survey instrument.",
            Foreground = palette.GrayText,
            TextWrapping = TextWrapping.Wrap,
            FontStyle = FontStyles.Italic,
        };

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Provenance and accuracy", palette));
        panel.Children.Add(findParcelGeocodedIntro);
        panel.Children.Add(findParcelDirectPointIntro);
        panel.Children.Add(geocodeAttribution);
        panel.Children.Add(parcelAccuracy);
        panel.Children.Add(parcelDisclaimer);
        panel.Children.Add(settingsFileNote);
        panel.Children.Add(siteFormDisclaimer);
        return panel;
    }

    // ---- Step 10: Preflight summary with Create/Cancel (Create/Cancel themselves live in the persistent
    // ---- navigation bar below, not this panel -- see BuildNavigationBar) ------------------------------------------

    private StackPanel BuildPreflightSummaryPanel(DialogPalette palette)
    {
        IValueConverter visibleWhenFindParcel = new EqualsVisibilityConverter(DialogAoiSource.FindParcel);
        IValueConverter visibleWhenSettingsFile = new EqualsVisibilityConverter(DialogAoiSource.UseSettingsFile);

        TextBlock parcelSummary = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        parcelSummary.SetBinding(
            TextBlock.TextProperty,
            new Binding($"{nameof(SolidGroundDialogViewModel.SelectedParcelCandidate)}.{nameof(ParcelBoundaryCandidate.ParcelId)}") { StringFormat = "Area of interest: parcel {0}" });
        parcelSummary.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenFindParcel });

        TextBlock settingsFileSummary = new()
        {
            Text = "Area of interest: " + DescribeAoiSettings(_inputs.ConfiguredAreaOfInterest),
            Foreground = palette.WindowText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };
        settingsFileSummary.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenSettingsFile });

        TextBlock bufferLine = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        bufferLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.BufferMeters)) { StringFormat = "Buffer: {0} m" });
        bufferLine.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.AoiSource)) { Converter = visibleWhenFindParcel });

        TextBlock pointBudgetLine = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        pointBudgetLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.PointBudget)) { StringFormat = "Point budget: {0}" });

        TextBlock pointBudgetWarningLine = new()
        {
            Foreground = palette.Highlight,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };
        pointBudgetWarningLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.PointBudgetWarningText)));
        pointBudgetWarningLine.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.ShowPointBudgetWarning)) { Converter = BooleanToVisibility });

        TextBlock unitLine = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        unitLine.SetBinding(TextBlock.TextProperty, new Binding(nameof(SolidGroundDialogViewModel.SelectedOutputUnit)) { StringFormat = "Output unit: {0}" });

        TextBlock levelLine = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        levelLine.SetBinding(
            TextBlock.TextProperty,
            new Binding($"{nameof(SolidGroundDialogViewModel.SelectedLevel)}.{nameof(NamedElevationCandidate.Name)}") { StringFormat = "Level: {0}" });

        TextBlock toposolidTypeLine = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        toposolidTypeLine.SetBinding(
            TextBlock.TextProperty,
            new Binding($"{nameof(SolidGroundDialogViewModel.SelectedToposolidType)}.{nameof(NamedCandidate.Name)}") { StringFormat = "Toposolid type: {0}" });

        TextBlock sharedCoordinatesLine = new() { Foreground = palette.WindowText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) };
        sharedCoordinatesLine.SetBinding(
            TextBlock.TextProperty,
            new Binding(nameof(SolidGroundDialogViewModel.WriteSharedCoordinatesIfAbsent)) { StringFormat = "Write shared coordinates if absent: {0}" });

        TextBlock preflightNote = new()
        {
            Text =
                "Clicking Create still runs SolidGround's own Preflight checks (for example a missing " +
                "process-mode file, or an unreadable Revit.ini); a rejection there still cancels this run " +
                "with nothing changed.",
            Foreground = palette.GrayText,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
        };

        StackPanel panel = new();
        panel.Children.Add(BuildHeader("Review and create", palette));
        panel.Children.Add(parcelSummary);
        panel.Children.Add(settingsFileSummary);
        panel.Children.Add(bufferLine);
        panel.Children.Add(pointBudgetLine);
        panel.Children.Add(pointBudgetWarningLine);
        panel.Children.Add(unitLine);
        panel.Children.Add(levelLine);
        panel.Children.Add(toposolidTypeLine);
        panel.Children.Add(sharedCoordinatesLine);
        panel.Children.Add(preflightNote);
        return panel;
    }

    // ---- Persistent navigation bar (every step): Cancel, Back, Next, Create --------------------------------------

    private static DockPanel BuildNavigationBar(DialogPalette palette)
    {
        Button cancelButton = new() { Content = "Cancel", Padding = new Thickness(16, 4, 16, 4), Foreground = palette.ControlText, Background = palette.ControlBackground };
        cancelButton.SetBinding(Button.CommandProperty, new Binding(nameof(SolidGroundDialogViewModel.CancelCommand)));
        AutomationProperties.SetName(cancelButton, "Cancel");

        Button backButton = new() { Content = "Back", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(12, 0, 4, 0), Foreground = palette.ControlText, Background = palette.ControlBackground };
        backButton.SetBinding(Button.CommandProperty, new Binding(nameof(SolidGroundDialogViewModel.BackCommand)));
        AutomationProperties.SetName(backButton, "Back");

        Button nextButton = new() { Content = "Next", Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(4, 0, 0, 0), Foreground = palette.ControlText, Background = palette.ControlBackground };
        nextButton.SetBinding(Button.CommandProperty, new Binding(nameof(SolidGroundDialogViewModel.NextCommand)));
        nextButton.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.CurrentStep)) { Converter = new EqualsVisibilityConverter(SolidGroundDialogStep.PreflightSummary, invert: true) });
        AutomationProperties.SetName(nextButton, "Next");

        Button createButton = new()
        {
            Content = "Create",
            Padding = new Thickness(16, 4, 16, 4),
            Margin = new Thickness(4, 0, 0, 0),
            FontWeight = FontWeights.Bold,
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        createButton.SetBinding(Button.CommandProperty, new Binding(nameof(SolidGroundDialogViewModel.CreateCommand)));
        createButton.SetBinding(VisibilityProperty, new Binding(nameof(SolidGroundDialogViewModel.CurrentStep)) { Converter = new EqualsVisibilityConverter(SolidGroundDialogStep.PreflightSummary) });
        AutomationProperties.SetName(createButton, "Create");

        DockPanel bar = new() { Margin = new Thickness(12) };
        DockPanel.SetDock(cancelButton, Dock.Left);
        bar.Children.Add(cancelButton);

        StackPanel rightGroup = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        rightGroup.Children.Add(backButton);
        rightGroup.Children.Add(nextButton);
        rightGroup.Children.Add(createButton);
        bar.Children.Add(rightGroup);

        return bar;
    }

    // ------------------------------------------------------------------------------------------------------
    // Converters (code-only; no XAML markup extension syntax needed for any of these)
    // ------------------------------------------------------------------------------------------------------

    /// <summary>Visible exactly when the bound value is a non-null, non-empty string.</summary>
    private sealed class TextPresenceVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string text && !string.IsNullOrEmpty(text) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Visible exactly when the bound value equals (or, when <paramref name="invert"/> is true, does not equal) <paramref name="expected"/>.</summary>
    private sealed class EqualsVisibilityConverter(object expected, bool invert = false) : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            bool isMatch = Equals(value, expected);
            return (invert ? !isMatch : isMatch) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }

    /// <summary>Maps a bound <see cref="SolidGroundDialogStep"/> onto its pre-built panel, so the content host swaps automatically whenever <see cref="SolidGroundDialogViewModel.CurrentStep"/> changes.</summary>
    private sealed class StepToPanelConverter(IReadOnlyDictionary<SolidGroundDialogStep, FrameworkElement> panels) : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is SolidGroundDialogStep step && panels.TryGetValue(step, out FrameworkElement? panel) ? panel : null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
