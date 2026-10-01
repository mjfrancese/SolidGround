using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Tests;

public sealed class GroundPointPreviewTests
{
    [Fact]
    public void ArrangedPreviewKeepsWorldSelectionThroughZoomFitResizeAndLateOutlines()
    {
        StaTestHost.Run(() =>
        {
            GroundPointPreview preview = new(WpfTestSupport.CreatePalette())
            {
                IsPlacementEnabled = true,
                IsSelectionValid = true,
                SelectedPoint = new Coordinate2D(-93.6038d, 41.5912d),
            };
            preview.SetContext(CreateParcel(), [], resetView: true);
            Window host = new() { Width = 540d, Height = 390d, Content = preview };
            host.Show();
            try
            {
                host.UpdateLayout();
                Canvas plot = Descendants<Canvas>(preview).Single(canvas => canvas.ClipToBounds);
                Assert.True(plot.ActualHeight >= 180d, $"The arranged preview must retain its 180 DIP minimum; actual {plot.ActualHeight}.");
                Coordinate2D original = preview.SelectedPoint!.Value;

                Click(preview, "Zoom in");
                host.Width = 760d;
                host.Height = 560d;
                host.UpdateLayout();
                Click(preview, "Fit parcel");
                preview.SetContext(CreateParcel(), [CreateOutline()], resetView: false);
                host.UpdateLayout();

                Assert.Equal(original, preview.SelectedPoint);
                Assert.Contains(Descendants<Polyline>(preview), line => line.StrokeDashArray is not null);
                Assert.Contains(Descendants<Ellipse>(preview), ellipse => AutomationProperties.GetName(ellipse) == "Selected usable ground point");
                Assert.Contains(Descendants<TextBlock>(preview), text => text.Text.Contains("WGS 84 display", StringComparison.Ordinal));
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void FocusedArrowNudgesTheSelectedWorldPointAndEnterAcceptsWithoutClosingHost()
    {
        StaTestHost.Run(() =>
        {
            GroundPointPreview preview = new(WpfTestSupport.CreatePalette())
            {
                IsPlacementEnabled = true,
                IsSelectionValid = true,
                SelectedPoint = new Coordinate2D(-93.6038d, 41.5912d),
            };
            preview.SetContext(CreateParcel(), [], resetView: true);
            Window host = new() { Width = 520d, Height = 380d, Content = preview };
            int changes = 0;
            int accepted = 0;
            preview.PointChanged += _ => changes++;
            preview.PointAccepted += (_, _) => accepted++;
            host.Show();
            try
            {
                host.UpdateLayout();
                preview.Focus();
                RaiseKey(preview, Key.Right);
                Coordinate2D nudged = preview.SelectedPoint!.Value;
                Assert.True(nudged.X > -93.6038d);
                Assert.Equal(41.5912d, nudged.Y, 10);
                double movedMeters = (nudged.X + 93.6038d) * Wgs84Ellipsoid.MetersPerDegreeLongitude(41.5912d);
                Assert.InRange(movedMeters, 0.075d, 0.078d);
                Assert.Equal(1, changes);
                Assert.Contains(Descendants<TextBlock>(preview), text => text.Text.Contains("0.0762 m (3 international inches)", StringComparison.Ordinal));

                RaiseKey(preview, Key.Enter);
                Assert.Equal(1, accepted);
                Assert.True(host.IsVisible, "Accepting a point must not close the containing dialog.");

                RaiseKey(preview, Key.Escape);
                Assert.True(host.IsVisible, "Escape from the preview must be handled without closing the containing dialog.");
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void StatusAndPinExposeAccessibleNamesForInvalidSelection()
    {
        StaTestHost.Run(() =>
        {
            GroundPointPreview preview = new(WpfTestSupport.CreatePalette())
            {
                IsPlacementEnabled = true,
                IsSelectionValid = false,
                SelectionStatus = "No usable ground height here. Move the point.",
                SelectedPoint = new Coordinate2D(-93.6038d, 41.5912d),
            };
            preview.SetContext(CreateParcel(), [], resetView: true);
            Window host = new() { Width = 520d, Height = 380d, Content = preview };
            host.Show();
            try
            {
                host.UpdateLayout();
                Assert.Contains(Descendants<Ellipse>(preview), ellipse => AutomationProperties.GetName(ellipse) == "Selected ground point with no usable height");
                TextBlock status = Descendants<TextBlock>(preview).Single(text => AutomationProperties.GetName(text) == "Ground point selection status");
                Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(status));
                Assert.Contains("No usable ground height here", status.Text, StringComparison.Ordinal);
            }
            finally
            {
                host.Close();
            }
        });
    }

    private static void Click(DependencyObject root, string name)
    {
        Button button = Descendants<Button>(root).Single(control => AutomationProperties.GetName(control) == name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static void RaiseKey(UIElement element, Key key)
    {
        PresentationSource? source = PresentationSource.FromVisual(element);
        Assert.NotNull(source);
        KeyEventArgs eventArgs = new(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
        element.RaiseEvent(eventArgs);
        Assert.True(eventArgs.Handled, $"{key} should be handled by the focused ground-point preview.");
    }

    private static PolygonalRegion CreateParcel() => Parse("POLYGON ((-93.6042 41.5909, -93.6034 41.5909, -93.6034 41.5915, -93.6042 41.5915, -93.6042 41.5909))");

    private static PolygonalRegion CreateOutline() => Parse("POLYGON ((-93.6040 41.5910, -93.6037 41.5910, -93.6037 41.5913, -93.6040 41.5913, -93.6040 41.5910))");

    private static PolygonalRegion Parse(string wkt) => ParcelGeometryParser.Parse(
        ParcelGeometryFormat.Wkt,
        wkt,
        new HorizontalReference("EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching) yield return matching;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (T nested in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return nested;
        }
    }
}
