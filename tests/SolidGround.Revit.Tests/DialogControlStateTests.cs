using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    public void ActualDialogUsesProductionPalettesAndExposesEffectiveControlStates()
    {
        StaTestHost.Run(() => AssertProductionControlStates(render: false));
    }

    [Fact]
    public void ActualDialogRendersSemanticPalettesAndAvailableControlStatesWithMeasuredContrast()
    {
        StaTestHost.Run(() =>
        {
            if (!ReferenceVisualRenders())
            {
                Assert.Skip("RenderTargetBitmap produced an all-transparent reference DrawingVisual in this local test session; rendered PNG evidence is not verified here.");
            }

            AssertProductionControlStates(render: true);
        });
    }

    private static void AssertProductionControlStates(bool render)
    {
        foreach ((string name, DialogPalette palette) in Palettes())
        {
            SolidGroundDialog dialog = new(SolidGroundDialogBindingTests.CreateViewModel(), palette);
            try
            {
                Border surface = CaptureContent(dialog);
                Assert.Same(palette.ControlBackground, surface.Resources[DialogControlStyles.SurfaceBrushKey]);
                TextBox input = FindVisual<TextBox>(surface).First();
                ComboBox combo = FindVisual<ComboBox>(surface).First();
                Button primary = FindVisual<Button>(surface).First(button => ReferenceEquals(button.Style, surface.Resources[DialogControlStyles.PrimaryButtonStyleKey]));

                AssertContrastAtLeast(primary.Foreground, primary.Background, palette.UsesSystemColors ? 4.49d : 4.5d, name + " primary action");
                AssertContrastAtLeast(input.Foreground, input.Background, 4.5d, name + " input text");
                if (render) Render(surface, name + "-normal");

                BindingExpression invalidBinding = Assert.IsType<BindingExpression>(BindingOperations.SetBinding(
                    input,
                    TextBox.TextProperty,
                    new Binding(nameof(InvalidTextSource.Value)) { Source = new InvalidTextSource(), Mode = BindingMode.OneWay }));
                Validation.MarkInvalid(invalidBinding, new ValidationError(new ExceptionValidationRule(), invalidBinding, "Synthetic validation error", null));
                surface.UpdateLayout();
                AssertContrastAtLeast(input.BorderBrush, input.Background, 3d, name + " invalid border");
                if (render) Render(surface, name + "-invalid");

                combo.SelectedIndex = 0;
                primary.IsEnabled = false;
                surface.UpdateLayout();
                Assert.False(primary.IsEnabled);
                AssertContrastAtLeast(primary.Foreground, primary.Background, 4.5d, name + " disabled action");
                if (render) Render(surface, name + "-disabled-selected-open");

                AssertStyleDeclaresPointerAndKeyboardStates(surface);
            }
            finally
            {
                dialog.Close();
            }
        }
    }

    private static IEnumerable<(string Name, DialogPalette Palette)> Palettes()
    {
        yield return ("light", DialogTheme.GetPalette(dark: false, highContrast: false));
        yield return ("dark", DialogTheme.GetPalette(dark: true, highContrast: false));
        yield return ("high-contrast", DialogTheme.GetPalette(dark: false, highContrast: true));
    }

    private static void AssertStyleDeclaresPointerAndKeyboardStates(Border surface)
    {
        Style buttonStyle = Assert.IsType<Style>(surface.Resources[typeof(Button)]);
        Style inputStyle = Assert.IsType<Style>(surface.Resources[typeof(TextBox)]);
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

    private static Border CaptureContent(Window window)
    {
        FrameworkElement content = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
        window.Content = null;
        Border surface = new()
        {
            Width = window.Width,
            Height = window.Height,
            Background = window.Background,
            DataContext = window.DataContext,
            Child = content,
        };
        foreach (object key in window.Resources.Keys)
        {
            surface.Resources[key] = window.Resources[key];
        }

        surface.Measure(new Size(surface.Width, surface.Height));
        surface.Arrange(new Rect(0d, 0d, surface.Width, surface.Height));
        surface.UpdateLayout();
        return surface;
    }

    private static void Render(FrameworkElement content, string name)
    {
        Render(content, content.ActualWidth, content.ActualHeight, name);
    }

    private static void Render(Visual content, double width, double height, string name)
    {
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        double scale = VisualTreeHelper.GetDpi(content).DpiScaleX;
        RenderTargetBitmap bitmap = new(
            (int)Math.Ceiling(width * scale),
            (int)Math.Ceiling(height * scale),
            96d * scale,
            96d * scale,
            PixelFormats.Pbgra32);
        bitmap.Render(content);
        AssertRenderedContent(bitmap, name);

        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "TestResults", "SolidGround.Revit.Tests");
        Directory.CreateDirectory(outputDirectory);
        using FileStream stream = File.Create(Path.Combine(outputDirectory, "dialog-" + name + ".png"));
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
    }

    private static bool ReferenceVisualRenders()
    {
        DrawingVisual reference = new();
        using (DrawingContext drawing = reference.RenderOpen())
        {
            drawing.DrawRectangle(Brushes.Red, null, new Rect(0d, 0d, 24d, 24d));
        }

        RenderTargetBitmap bitmap = new(24, 24, 96d, 96d, PixelFormats.Pbgra32);
        bitmap.Render(reference);
        return Describe(bitmap).IsVisible;
    }

    private static void AssertRenderedContent(RenderTargetBitmap bitmap, string name)
    {
        RenderDescription description = Describe(bitmap);
        Assert.True(description.IsVisible, $"{name} render contained no visible WPF content ({bitmap.PixelWidth}x{bitmap.PixelHeight}, {description.NonTransparent} nontransparent pixels, {description.NonBlack} nonblack pixels, {description.OpaqueColors} opaque colors).");
    }

    private static RenderDescription Describe(RenderTargetBitmap bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        HashSet<uint> opaqueColors = new();
        int nonTransparent = 0;
        int nonBlack = 0;
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            byte blue = pixels[offset];
            byte green = pixels[offset + 1];
            byte red = pixels[offset + 2];
            byte alpha = pixels[offset + 3];
            if (alpha == 0)
            {
                continue;
            }

            nonTransparent++;
            if (red != 0 || green != 0 || blue != 0)
            {
                nonBlack++;
            }

            if (alpha == byte.MaxValue && opaqueColors.Count < 256)
            {
                opaqueColors.Add(((uint)red << 16) | ((uint)green << 8) | blue);
            }
        }

        return new RenderDescription(
            nonTransparent,
            nonBlack,
            opaqueColors.Count,
            nonTransparent > pixels.Length / 64 && nonBlack > pixels.Length / 128 && opaqueColors.Count >= 8);
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

    private sealed record RenderDescription(int NonTransparent, int NonBlack, int OpaqueColors, bool IsVisible);
}
