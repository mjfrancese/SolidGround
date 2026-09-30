using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Tests;

public sealed class DialogControlStateTests
{
    [Fact]
    public void ActualDialogInstallsTheWindowLocalStatefulControlResources()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialog dialog = new(SolidGroundDialogBindingTests.CreateViewModel(), WpfTestSupport.CreatePalette());
            Assert.IsType<Style>(dialog.Resources[typeof(TextBox)]);
            Assert.IsType<Style>(dialog.Resources[DialogControlStyles.PrimaryButtonStyleKey]);
            Assert.NotNull(dialog.Resources[DialogControlStyles.SurfaceBrushKey]);
            Assert.NotNull(dialog.Resources[DialogControlStyles.FocusBrushKey]);
            Assert.NotNull(dialog.Resources[DialogControlStyles.ErrorBrushKey]);
        });
    }

    [Fact]
    public void ActualDialogRendersSemanticPalettesAndAvailableControlStatesWithMeasuredContrast()
    {
        StaTestHost.Run(() =>
        {
            foreach ((string name, DialogPalette palette) in Palettes())
            {
                SolidGroundDialog dialog = new(SolidGroundDialogBindingTests.CreateViewModel(), palette);
                try
                {
                    dialog.Show();
                    dialog.UpdateLayout();
                    TextBox input = FindVisual<TextBox>(dialog).First();
                    ComboBox combo = FindVisual<ComboBox>(dialog).First();
                    Button primary = FindVisual<Button>(dialog).First(button => ReferenceEquals(button.Style, dialog.Resources[DialogControlStyles.PrimaryButtonStyleKey]));

                    AssertContrastAtLeast(primary.Foreground, primary.Background, palette.UsesSystemColors ? 4.49d : 4.5d, name + " primary action");
                    AssertContrastAtLeast(input.Foreground, input.Background, 4.5d, name + " input text");
                    Render(dialog, name + "-normal");

                    input.Focus();
                    dialog.UpdateLayout();
                    AssertContrastAtLeast(input.BorderBrush, input.Background, 3d, name + " keyboard focus border");
                    Render(dialog, name + "-focus");

                    BindingExpression invalidBinding = Assert.IsType<BindingExpression>(BindingOperations.SetBinding(
                        input,
                        TextBox.TextProperty,
                        new Binding(nameof(InvalidTextSource.Value)) { Source = new InvalidTextSource(), Mode = BindingMode.OneWay }));
                    Validation.MarkInvalid(invalidBinding, new ValidationError(new ExceptionValidationRule(), invalidBinding, "Synthetic validation error", null));
                    dialog.UpdateLayout();
                    AssertContrastAtLeast(input.BorderBrush, input.Background, 3d, name + " invalid border");
                    Render(dialog, name + "-invalid");

                    combo.SelectedIndex = 0;
                    combo.IsDropDownOpen = true;
                    primary.IsEnabled = false;
                    dialog.UpdateLayout();
                    AssertContrastAtLeast(primary.Foreground, primary.Background, 4.5d, name + " disabled action");
                    Render(dialog, name + "-disabled-selected-open");

                    AssertStateTriggers(dialog);
                }
                finally
                {
                    dialog.Close();
                }
            }
        });
    }

    private static IEnumerable<(string Name, DialogPalette Palette)> Palettes()
    {
        yield return ("light", new DialogPalette(
            Brush(0xF8, 0xF9, 0xFA), Brush(0x1B, 0x1B, 0x1F), Brush(0x1B, 0x1B, 0x1F), Brushes.White,
            Brush(0x0B, 0x57, 0xD0), Brush(0xB3, 0x26, 0x1E), Brush(0x4A, 0x4E, 0x54), Brush(0x74, 0x77, 0x75))
        {
            SurfaceRaised = Brush(0xF1, 0xF3, 0xF4),
            OnAccent = Brushes.White,
            Selection = Brush(0xD3, 0xE3, 0xFD),
            OnSelection = Brush(0x10, 0x2A, 0x43),
            Focus = Brush(0x00, 0x5F, 0xCC),
            DisabledText = Brush(0x55, 0x59, 0x5D),
            DisabledSurface = Brush(0xE2, 0xE5, 0xE8),
        });
        yield return ("dark", new DialogPalette(
            Brush(0x1B, 0x1B, 0x1F), Brush(0xF4, 0xF0, 0xF4), Brush(0xF4, 0xF0, 0xF4), Brush(0x24, 0x24, 0x28),
            Brush(0xA8, 0xC7, 0xFA), Brush(0xF2, 0xB8, 0xB5), Brush(0xC9, 0xC5, 0xCA), Brush(0xA9, 0xA4, 0xAA))
        {
            SurfaceRaised = Brush(0x30, 0x30, 0x34),
            OnAccent = Brush(0x00, 0x2B, 0x5C),
            Selection = Brush(0x17, 0x4A, 0x7C),
            OnSelection = Brushes.White,
            Focus = Brush(0xA8, 0xC7, 0xFA),
            DisabledText = Brush(0xCB, 0xC7, 0xCC),
            DisabledSurface = Brush(0x38, 0x38, 0x3D),
        });
        yield return ("high-contrast", new DialogPalette(
            SystemColors.WindowBrush, SystemColors.WindowTextBrush, SystemColors.ControlTextBrush, SystemColors.WindowBrush,
            SystemColors.HighlightBrush, SystemColors.WindowTextBrush, SystemColors.GrayTextBrush, SystemColors.ActiveBorderBrush)
        {
            SurfaceRaised = SystemColors.ControlBrush,
            OnAccent = SystemColors.HighlightTextBrush,
            Selection = SystemColors.HighlightBrush,
            OnSelection = SystemColors.HighlightTextBrush,
            Focus = SystemColors.HighlightBrush,
            DisabledText = SystemColors.GrayTextBrush,
            DisabledSurface = SystemColors.ControlBrush,
            UsesSystemColors = true,
        });
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue) => new(Color.FromRgb(red, green, blue));

    private static void AssertStateTriggers(SolidGroundDialog dialog)
    {
        Style buttonStyle = Assert.IsType<Style>(dialog.Resources[typeof(Button)]);
        Style inputStyle = Assert.IsType<Style>(dialog.Resources[typeof(TextBox)]);
        Assert.Contains(buttonStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsMouseOverProperty);
        Assert.Contains(buttonStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == ButtonBase.IsPressedProperty);
        Assert.Contains(buttonStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsEnabledProperty);
        Assert.Contains(inputStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == UIElement.IsKeyboardFocusWithinProperty);
        Assert.Contains(inputStyle.Triggers.OfType<Trigger>(), trigger => trigger.Property == Validation.HasErrorProperty);
    }

    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T matching)
            {
                yield return matching;
            }

            foreach (T nested in FindVisual<T>(child))
            {
                yield return nested;
            }
        }
    }

    private static void Render(Window window, string name)
    {
        double scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        RenderTargetBitmap bitmap = new(
            (int)Math.Ceiling(window.ActualWidth * scale),
            (int)Math.Ceiling(window.ActualHeight * scale),
            96d * scale,
            96d * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        Assert.True(bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0);

        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "SolidGround.Revit.Tests");
        Directory.CreateDirectory(outputDirectory);
        using FileStream stream = File.Create(Path.Combine(outputDirectory, "dialog-" + name + ".png"));
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static void AssertContrastAtLeast(Brush foreground, Brush background, double minimum, string pair)
    {
        Color foregroundColor = Assert.IsType<SolidColorBrush>(foreground).Color;
        Color backgroundColor = Assert.IsType<SolidColorBrush>(background).Color;
        double ratio = ContrastRatio(foregroundColor, backgroundColor);
        Assert.True(ratio >= minimum, $"{pair} contrast was {ratio:F2}:1, below {minimum:F1}:1.");
    }

    private static double ContrastRatio(Color first, Color second)
    {
        double ratio = (RelativeLuminance(first) + 0.05d) / (RelativeLuminance(second) + 0.05d);
        return ratio < 1d ? 1d / ratio : ratio;
    }

    private static double RelativeLuminance(Color color)
    {
        static double Linearize(byte channel)
        {
            double normalized = channel / 255d;
            return normalized <= 0.04045d ? normalized / 12.92d : Math.Pow((normalized + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(color.R)) + (0.7152d * Linearize(color.G)) + (0.0722d * Linearize(color.B));
    }

    private sealed class InvalidTextSource
    {
        private readonly string value = "Synthetic value";

        public string Value => value;
    }
}
