using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Tests;

public sealed class CreationDialogLayoutAndPreviewTests
{
    [Fact]
    public void CreationViewportStretchesTheActiveStepWithoutAHorizontalViewport()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialog dialog = new(SolidGroundDialogBindingTests.CreateViewModel(), WpfTestSupport.CreatePalette())
            {
                Width = 640d,
                Height = 480d,
            };
            dialog.Show();
            try
            {
                ScrollViewer viewport = Descendants<ScrollViewer>(dialog).Single(viewer => viewer.Content is ContentControl);
                ContentControl content = Assert.IsType<ContentControl>(viewport.Content);
                dialog.UpdateLayout();

                Assert.Equal(HorizontalAlignment.Stretch, content.HorizontalContentAlignment);
                Assert.Equal(ScrollBarVisibility.Disabled, viewport.HorizontalScrollBarVisibility);
                Assert.True(viewport.ViewportHeight > 0d && viewport.ViewportHeight < dialog.ActualHeight);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ParcelPreviewCentersAWideOutlineAndKeepsLegendOutsideThePlot()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialogViewModel viewModel = SolidGroundDialogBindingTests.CreateViewModel();
            viewModel.SelectedParcelCandidate = new ParcelProximityCandidate(
                new ParcelBoundaryCandidate(
                    ParcelGeometryParser.Parse(
                        ParcelGeometryFormat.Wkt,
                        "POLYGON ((-93.640 41.580, -93.600 41.580, -93.600 41.590, -93.640 41.590, -93.640 41.580))",
                        new HorizontalReference("EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude)),
                    "synthetic-wide-preview",
                    1d,
                    ParcelBoundarySourceKind.LocalParcelFile,
                    "Synthetic parcel source",
                    "Synthetic license disclaimer."),
                0d);

            ParcelBoundaryPreview preview = new(WpfTestSupport.CreatePalette()) { DataContext = viewModel };
            Window host = new() { Width = 400d, Height = 300d, Content = preview };
            host.Show();
            try
            {
                host.UpdateLayout();
                Canvas plot = Descendants<Canvas>(preview).Single();
                Polyline outline = Assert.Single(plot.Children.OfType<Polyline>(), line => line.StrokeDashArray is null);
                double top = outline.Points.Min(point => point.Y);
                double bottom = plot.ActualHeight - outline.Points.Max(point => point.Y);

                Assert.InRange(Math.Abs(top - bottom), 0d, 2d);
                Assert.Empty(plot.Children.OfType<TextBlock>());
                Assert.True(plot.ClipToBounds);
                Assert.Contains(Descendants<TextBlock>(preview), text => text.Text.StartsWith("Solid: legal parcel", StringComparison.Ordinal));
            }
            finally
            {
                host.Close();
            }
        });
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching) yield return matching;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (T nested in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return nested;
        }
    }
}
