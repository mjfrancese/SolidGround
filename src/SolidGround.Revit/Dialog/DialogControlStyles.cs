using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// Installs the shared semantic WPF control resources for a SolidGround modal window.
/// The styles are window-local: Revit's application resources and other add-ins cannot inherit them.
/// </summary>
internal static class DialogControlStyles
{
    internal const string SurfaceBrushKey = "SolidGround.SurfaceBrush";
    internal const string AccentBrushKey = "SolidGround.AccentBrush";
    internal const string FocusBrushKey = "SolidGround.FocusBrush";
    internal const string ErrorBrushKey = "SolidGround.ErrorBrush";
    internal const string PrimaryButtonStyleKey = "SolidGround.PrimaryButtonStyle";

    /// <summary>
    /// Adds semantic brushes and stateful implicit control styles to <paramref name="window"/>.
    /// Call after assigning the window palette and before its controls are realized.
    /// </summary>
    internal static void Apply(Window window, DialogPalette palette)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(palette);

        ResourceDictionary resources = window.Resources;
        AddBrushes(resources, palette);
        resources[typeof(Button)] = ButtonStyle(palette, primary: false);
        resources[PrimaryButtonStyleKey] = ButtonStyle(palette, primary: true);
        resources[typeof(TextBox)] = TextBoxStyle(palette);
        resources[typeof(PasswordBox)] = PasswordBoxStyle(palette);
        resources[typeof(ComboBox)] = ComboBoxStyle(palette);
        resources[typeof(ComboBoxItem)] = SelectorItemStyle(typeof(ComboBoxItem), palette);
        resources[typeof(ListBox)] = ListBoxStyle(palette);
        resources[typeof(ListBoxItem)] = SelectorItemStyle(typeof(ListBoxItem), palette);
        resources[typeof(CheckBox)] = CheckBoxStyle(palette);
        resources[typeof(RadioButton)] = CheckBoxStyle(typeof(RadioButton), palette);
        resources[typeof(TabControl)] = TabControlStyle(palette);
        resources[typeof(TabItem)] = TabItemStyle(palette);
        resources[typeof(ToolTip)] = ToolTipStyle(palette);

        // These keys keep an active Windows High Contrast scheme dynamic after the window opens.
        // SystemColors brushes are system-owned resources rather than SolidGround brand colors.
        if (palette.UsesSystemColors)
        {
            window.SetResourceReference(Window.BackgroundProperty, SystemColors.WindowBrushKey);
            window.SetResourceReference(Window.ForegroundProperty, SystemColors.WindowTextBrushKey);
            window.SetResourceReference(Window.BorderBrushProperty, SystemColors.ActiveBorderBrushKey);
        }
    }

    private static void AddBrushes(ResourceDictionary resources, DialogPalette palette)
    {
        resources[SurfaceBrushKey] = palette.ControlBackground;
        resources[AccentBrushKey] = palette.Highlight;
        resources[FocusBrushKey] = palette.Focus;
        resources[ErrorBrushKey] = palette.Error;
        resources["SolidGround.SurfaceRaisedBrush"] = palette.SurfaceRaised;
        resources["SolidGround.TextPrimaryBrush"] = palette.ControlText;
        resources["SolidGround.TextSecondaryBrush"] = palette.GrayText;
        resources["SolidGround.BorderBrush"] = palette.ActiveBorder;
        resources["SolidGround.OnAccentBrush"] = palette.OnAccent;
        resources["SolidGround.SelectionBrush"] = palette.Selection;
        resources["SolidGround.OnSelectionBrush"] = palette.OnSelection;
        resources["SolidGround.DisabledTextBrush"] = palette.DisabledText;
        resources["SolidGround.DisabledSurfaceBrush"] = palette.DisabledSurface;
    }

    private static Style ButtonStyle(DialogPalette palette, bool primary)
    {
        Style style = new(typeof(Button));
        style.Setters.Add(new Setter(Control.BackgroundProperty, primary ? palette.Highlight : palette.ControlBackground));
        style.Setters.Add(new Setter(Control.ForegroundProperty, primary ? palette.OnAccent : palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, primary ? palette.Highlight : palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 6, 12, 6)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
        style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(palette)));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, primary ? palette.Focus : palette.SurfaceRaised), new Setter(Control.BorderBrushProperty, palette.Focus) } });
        style.Triggers.Add(new Trigger { Property = ButtonBase.IsPressedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, primary ? palette.OnAccent : palette.Selection), new Setter(Control.ForegroundProperty, primary ? palette.Highlight : palette.OnSelection), new Setter(Control.BorderBrushProperty, palette.Focus) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(Control.BackgroundProperty, palette.DisabledSurface), new Setter(Control.ForegroundProperty, palette.DisabledText), new Setter(Control.BorderBrushProperty, palette.ActiveBorder) } });
        return style;
    }

    private static Style TextBoxStyle(DialogPalette palette)
    {
        Style style = InputStyle(typeof(TextBox), palette, TextBoxTemplate(palette));
        style.Setters.Add(new Setter(TextBox.CaretBrushProperty, palette.ControlText));
        style.Setters.Add(new Setter(Validation.ErrorTemplateProperty, ValidationTemplate(palette)));
        return style;
    }

    private static Style PasswordBoxStyle(DialogPalette palette)
    {
        Style style = InputStyle(typeof(PasswordBox), palette, PasswordBoxTemplate(palette));
        style.Setters.Add(new Setter(Validation.ErrorTemplateProperty, ValidationTemplate(palette)));
        return style;
    }

    private static Style InputStyle(Type targetType, DialogPalette palette, ControlTemplate template)
    {
        Style style = new(targetType);
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.ControlBackground));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        AddInputTriggers(style, palette);
        return style;
    }

    private static Style ComboBoxStyle(DialogPalette palette)
    {
        Style style = new(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.ControlBackground));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 6, 5)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
        style.Setters.Add(new Setter(Control.TemplateProperty, ComboBoxTemplate(palette)));
        AddInputTriggers(style, palette);
        style.Triggers.Add(new Trigger { Property = ComboBox.IsDropDownOpenProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, palette.Focus), new Setter(Control.BackgroundProperty, palette.SurfaceRaised) } });
        return style;
    }

    private static Style ListBoxStyle(DialogPalette palette)
    {
        Style style = new(typeof(ListBox));
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.ControlBackground));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(Control.TemplateProperty, ListBoxTemplate()));
        AddInputTriggers(style, palette);
        return style;
    }

    private static Style SelectorItemStyle(Type targetType, DialogPalette palette)
    {
        Style style = new(targetType);
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(Control.TemplateProperty, SelectorItemTemplate(targetType, palette)));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, palette.SurfaceRaised) } });
        style.Triggers.Add(new Trigger { Property = Selector.IsSelectedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, palette.Selection), new Setter(Control.ForegroundProperty, palette.OnSelection) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(Control.BackgroundProperty, palette.DisabledSurface), new Setter(Control.ForegroundProperty, palette.DisabledText) } });
        return style;
    }

    private static Style CheckBoxStyle(DialogPalette palette) => CheckBoxStyle(typeof(CheckBox), palette);

    private static Style CheckBoxStyle(Type targetType, DialogPalette palette)
    {
        Style style = new(targetType);
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.ControlBackground));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
        style.Setters.Add(new Setter(Control.TemplateProperty, CheckBoxTemplate(targetType, palette)));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.ForegroundProperty, palette.Focus) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(Control.ForegroundProperty, palette.DisabledText) } });
        return style;
    }

    private static Style TabControlStyle(DialogPalette palette)
    {
        Style style = new(typeof(TabControl));
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.ControlBackground));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(Control.TemplateProperty, TabControlTemplate()));
        return style;
    }

    private static Style TabItemStyle(DialogPalette palette)
    {
        Style style = new(typeof(TabItem));
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.SurfaceRaised));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 7, 12, 7)));
        style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, FocusVisualStyle(palette)));
        style.Setters.Add(new Setter(Control.TemplateProperty, TabItemTemplate()));
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, palette.Selection), new Setter(Control.BorderBrushProperty, palette.Focus) } });
        style.Triggers.Add(new Trigger { Property = TabItem.IsSelectedProperty, Value = true, Setters = { new Setter(Control.BackgroundProperty, palette.ControlBackground), new Setter(Control.ForegroundProperty, palette.ControlText), new Setter(Control.BorderBrushProperty, palette.Focus), new Setter(Control.BorderThicknessProperty, new Thickness(2)) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(Control.BackgroundProperty, palette.DisabledSurface), new Setter(Control.ForegroundProperty, palette.DisabledText) } });
        return style;
    }

    private static Style ToolTipStyle(DialogPalette palette)
    {
        Style style = new(typeof(ToolTip));
        style.Setters.Add(new Setter(Control.BackgroundProperty, palette.SurfaceRaised));
        style.Setters.Add(new Setter(Control.ForegroundProperty, palette.ControlText));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, palette.ActiveBorder));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 5, 8, 5)));
        style.Setters.Add(new Setter(Control.TemplateProperty, ToolTipTemplate()));
        return style;
    }

    private static void AddInputTriggers(Style style, DialogPalette palette)
    {
        style.Triggers.Add(new Trigger { Property = UIElement.IsMouseOverProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, palette.Focus) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, palette.Focus), new Setter(Control.BorderThicknessProperty, new Thickness(2)) } });
        style.Triggers.Add(new Trigger { Property = Validation.HasErrorProperty, Value = true, Setters = { new Setter(Control.BorderBrushProperty, palette.Error), new Setter(Control.BorderThicknessProperty, new Thickness(2)) } });
        style.Triggers.Add(new Trigger { Property = UIElement.IsEnabledProperty, Value = false, Setters = { new Setter(Control.BackgroundProperty, palette.DisabledSurface), new Setter(Control.ForegroundProperty, palette.DisabledText) } });
    }

    private static ControlTemplate ButtonTemplate(DialogPalette palette)
    {
        FrameworkElementFactory border = ControlBorder("Border");
        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ContentControl.ContentTemplateProperty));
        content.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(ContentControl.ContentTemplateSelectorProperty));
        content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, TemplateBinding(Control.HorizontalContentAlignmentProperty));
        content.SetBinding(ContentPresenter.VerticalAlignmentProperty, TemplateBinding(Control.VerticalContentAlignmentProperty));
        content.SetBinding(ContentPresenter.MarginProperty, TemplateBinding(Control.PaddingProperty));
        border.AppendChild(content);
        return new ControlTemplate(typeof(Button)) { VisualTree = border };
    }

    private static ControlTemplate TextBoxTemplate(DialogPalette palette)
    {
        FrameworkElementFactory border = ControlBorder("Border");
        FrameworkElementFactory host = new(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetBinding(ScrollViewer.PaddingProperty, TemplateBinding(Control.PaddingProperty));
        border.AppendChild(host);
        return new ControlTemplate(typeof(TextBox)) { VisualTree = border };
    }

    private static ControlTemplate PasswordBoxTemplate(DialogPalette palette)
    {
        FrameworkElementFactory border = ControlBorder("Border");
        FrameworkElementFactory host = new(typeof(ScrollViewer)) { Name = "PART_ContentHost" };
        host.SetBinding(ScrollViewer.PaddingProperty, TemplateBinding(Control.PaddingProperty));
        border.AppendChild(host);
        return new ControlTemplate(typeof(PasswordBox)) { VisualTree = border };
    }

    private static ControlTemplate ComboBoxTemplate(DialogPalette palette)
    {
        FrameworkElementFactory root = new(typeof(Grid));
        FrameworkElementFactory border = ControlBorder("Border");
        FrameworkElementFactory row = new(typeof(DockPanel));
        row.SetValue(DockPanel.LastChildFillProperty, true);
        FrameworkElementFactory toggle = new(typeof(ToggleButton)) { Name = "DropDownToggle" };
        toggle.SetValue(DockPanel.DockProperty, Dock.Right);
        toggle.SetValue(ToggleButton.ContentProperty, "⌄");
        toggle.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        toggle.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        toggle.SetValue(Control.PaddingProperty, new Thickness(7, 0, 7, 0));
        toggle.SetValue(UIElement.FocusableProperty, false);
        toggle.SetValue(KeyboardNavigation.IsTabStopProperty, false);
        toggle.SetBinding(Control.ForegroundProperty, TemplateBinding(Control.ForegroundProperty));
        toggle.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(ComboBox.IsDropDownOpen)) { RelativeSource = TemplatedParent(), Mode = BindingMode.TwoWay });
        row.AppendChild(toggle);
        FrameworkElementFactory selection = new(typeof(ContentPresenter));
        selection.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ComboBox.SelectionBoxItemProperty));
        selection.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ComboBox.SelectionBoxItemTemplateProperty));
        selection.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(ComboBox.ItemTemplateSelectorProperty));
        selection.SetBinding(ContentPresenter.MarginProperty, TemplateBinding(Control.PaddingProperty));
        selection.SetBinding(ContentPresenter.VerticalAlignmentProperty, TemplateBinding(Control.VerticalContentAlignmentProperty));
        row.AppendChild(selection);
        border.AppendChild(row);
        root.AppendChild(border);

        FrameworkElementFactory popup = new(typeof(Popup)) { Name = "PART_Popup" };
        popup.SetValue(Popup.AllowsTransparencyProperty, true);
        popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
        popup.SetBinding(Popup.IsOpenProperty, new Binding(nameof(ComboBox.IsDropDownOpen)) { RelativeSource = TemplatedParent() });
        popup.SetBinding(Popup.PlacementTargetProperty, new Binding { RelativeSource = TemplatedParent() });
        FrameworkElementFactory popupBorder = new(typeof(Border));
        popupBorder.SetValue(Border.BackgroundProperty, palette.ControlBackground);
        popupBorder.SetValue(Border.BorderBrushProperty, palette.ActiveBorder);
        popupBorder.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        popupBorder.SetBinding(FrameworkElement.WidthProperty, TemplateBinding(FrameworkElement.ActualWidthProperty));
        popupBorder.SetBinding(FrameworkElement.MaxHeightProperty, TemplateBinding(ComboBox.MaxDropDownHeightProperty));
        FrameworkElementFactory scroll = new(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        FrameworkElementFactory items = new(typeof(ItemsPresenter));
        scroll.AppendChild(items);
        popupBorder.AppendChild(scroll);
        popup.AppendChild(popupBorder);
        root.AppendChild(popup);
        return new ControlTemplate(typeof(ComboBox)) { VisualTree = root };
    }

    private static ControlTemplate ListBoxTemplate()
    {
        FrameworkElementFactory border = ControlBorder("Border");
        FrameworkElementFactory scroll = new(typeof(ScrollViewer));
        scroll.SetValue(ScrollViewer.CanContentScrollProperty, true);
        scroll.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        scroll.AppendChild(new FrameworkElementFactory(typeof(ItemsPresenter)));
        border.AppendChild(scroll);
        return new ControlTemplate(typeof(ListBox)) { VisualTree = border };
    }

    private static ControlTemplate TabControlTemplate()
    {
        FrameworkElementFactory border = ControlBorder("Border");
        FrameworkElementFactory layout = new(typeof(DockPanel));
        FrameworkElementFactory headers = new(typeof(TabPanel)) { Name = "HeaderPanel" };
        headers.SetValue(DockPanel.DockProperty, Dock.Top);
        headers.SetValue(Panel.IsItemsHostProperty, true);
        layout.AppendChild(headers);
        FrameworkElementFactory content = new(typeof(ContentPresenter)) { Name = "PART_SelectedContentHost" };
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(TabControl.SelectedContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(TabControl.SelectedContentTemplateProperty));
        content.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(TabControl.SelectedContentTemplateSelectorProperty));
        content.SetBinding(ContentPresenter.MarginProperty, TemplateBinding(Control.PaddingProperty));
        layout.AppendChild(content);
        border.AppendChild(layout);
        return new ControlTemplate(typeof(TabControl)) { VisualTree = border };
    }

    private static ControlTemplate TabItemTemplate()
    {
        FrameworkElementFactory border = ControlBorder("TabBorder");
        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ContentControl.ContentTemplateProperty));
        content.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(ContentControl.ContentTemplateSelectorProperty));
        content.SetBinding(ContentPresenter.HorizontalAlignmentProperty, TemplateBinding(Control.HorizontalContentAlignmentProperty));
        content.SetBinding(ContentPresenter.VerticalAlignmentProperty, TemplateBinding(Control.VerticalContentAlignmentProperty));
        content.SetBinding(ContentPresenter.MarginProperty, TemplateBinding(Control.PaddingProperty));
        border.AppendChild(content);
        return new ControlTemplate(typeof(TabItem)) { VisualTree = border };
    }

    private static ControlTemplate ToolTipTemplate()
    {
        FrameworkElementFactory border = ControlBorder("ToolTipBorder");
        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ContentControl.ContentTemplateProperty));
        content.SetBinding(ContentPresenter.MarginProperty, TemplateBinding(Control.PaddingProperty));
        border.AppendChild(content);
        return new ControlTemplate(typeof(ToolTip)) { VisualTree = border };
    }

    private static ControlTemplate SelectorItemTemplate(Type targetType, DialogPalette palette)
    {
        FrameworkElementFactory border = new(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetBinding(Border.PaddingProperty, TemplateBinding(Control.PaddingProperty));
        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ContentControl.ContentTemplateProperty));
        content.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, TemplateBinding(ContentControl.ContentTemplateSelectorProperty));
        border.AppendChild(content);
        return new ControlTemplate(targetType) { VisualTree = border };
    }

    private static ControlTemplate CheckBoxTemplate(Type targetType, DialogPalette palette)
    {
        FrameworkElementFactory root = new(typeof(StackPanel));
        root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        FrameworkElementFactory box = new(typeof(Border)) { Name = "CheckBoxBorder" };
        box.SetValue(Border.WidthProperty, 16d);
        box.SetValue(Border.HeightProperty, 16d);
        box.SetValue(Border.MarginProperty, new Thickness(0, 0, 7, 0));
        box.SetValue(Border.BackgroundProperty, palette.ControlBackground);
        box.SetValue(Border.BorderBrushProperty, palette.ActiveBorder);
        box.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        FrameworkElementFactory mark = new(typeof(TextBlock)) { Name = "CheckMark" };
        mark.SetValue(TextBlock.TextProperty, "✓");
        mark.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        mark.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        mark.SetValue(TextBlock.ForegroundProperty, Brushes.Transparent);
        box.AppendChild(mark);
        root.AppendChild(box);
        FrameworkElementFactory content = new(typeof(ContentPresenter));
        content.SetBinding(ContentPresenter.ContentProperty, TemplateBinding(ContentControl.ContentProperty));
        content.SetBinding(ContentPresenter.ContentTemplateProperty, TemplateBinding(ContentControl.ContentTemplateProperty));
        content.SetBinding(ContentPresenter.VerticalAlignmentProperty, TemplateBinding(Control.VerticalContentAlignmentProperty));
        root.AppendChild(content);
        ControlTemplate template = new(targetType) { VisualTree = root };
        template.Triggers.Add(new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true, Setters = { new Setter(Border.BackgroundProperty, palette.Highlight, "CheckBoxBorder"), new Setter(Border.BorderBrushProperty, palette.Highlight, "CheckBoxBorder"), new Setter(TextBlock.ForegroundProperty, palette.OnAccent, "CheckMark") } });
        template.Triggers.Add(new Trigger { Property = ToggleButton.IsCheckedProperty, Value = null, Setters = { new Setter(TextBlock.ForegroundProperty, palette.ControlText, "CheckMark") } });
        template.Triggers.Add(new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true, Setters = { new Setter(Border.BorderBrushProperty, palette.Focus, "CheckBoxBorder"), new Setter(Border.BorderThicknessProperty, new Thickness(2), "CheckBoxBorder") } });
        return template;
    }

    private static ControlTemplate ValidationTemplate(DialogPalette palette)
    {
        FrameworkElementFactory border = new(typeof(Border));
        border.SetValue(Border.BorderBrushProperty, palette.Error);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.AppendChild(new FrameworkElementFactory(typeof(AdornedElementPlaceholder)));
        return new ControlTemplate { VisualTree = border };
    }

    private static Style FocusVisualStyle(DialogPalette palette)
    {
        Style style = new(typeof(Control));
        FrameworkElementFactory border = new(typeof(Border));
        border.SetValue(Border.BorderBrushProperty, palette.Focus);
        border.SetValue(Border.BorderThicknessProperty, new Thickness(2));
        border.SetValue(Border.MarginProperty, new Thickness(-3));
        style.Setters.Add(new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control)) { VisualTree = border }));
        return style;
    }

    private static FrameworkElementFactory ControlBorder(string name)
    {
        FrameworkElementFactory border = new(typeof(Border)) { Name = name };
        border.SetBinding(Border.BackgroundProperty, TemplateBinding(Control.BackgroundProperty));
        border.SetBinding(Border.BorderBrushProperty, TemplateBinding(Control.BorderBrushProperty));
        border.SetBinding(Border.BorderThicknessProperty, TemplateBinding(Control.BorderThicknessProperty));
        return border;
    }

    private static Binding TemplateBinding(DependencyProperty property) => new(property.Name) { RelativeSource = TemplatedParent() };
    private static RelativeSource TemplatedParent() => new(RelativeSourceMode.TemplatedParent);
}
