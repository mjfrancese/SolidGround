using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Sources;

namespace SolidGround.Revit.Dialog;

/// <summary>A small managed-vector confirmation preview for the selected legal parcel; it has no tiles or browser content.</summary>
internal sealed class ParcelBoundaryPreview : Border
{
    private readonly Canvas _canvas = new() { Height = 150, MinWidth = 260 };

    internal ParcelBoundaryPreview()
    {
        BorderBrush = SystemColors.ActiveBorderBrush;
        BorderThickness = new Thickness(1);
        Child = _canvas;
        DataContextChanged += (_, _) => Render(DataContext as SolidGroundDialogViewModel);
    }

    private void Render(SolidGroundDialogViewModel? viewModel)
    {
        _canvas.Children.Clear();
        ParcelProximityCandidate? selected = viewModel?.SelectedParcelCandidate;
        if (selected is null)
        {
            _canvas.Children.Add(new TextBlock { Text = "Select a parcel to preview its legal outline.", Margin = new Thickness(8) });
            return;
        }

        PolygonalRegion region = selected.Candidate.Boundary;
        double width = Math.Max(1d, _canvas.ActualWidth == 0d ? 260d : _canvas.ActualWidth);
        double height = Math.Max(1d, _canvas.Height);
        double spanX = Math.Max(region.Envelope.MaxX - region.Envelope.MinX, double.Epsilon);
        double spanY = Math.Max(region.Envelope.MaxY - region.Envelope.MinY, double.Epsilon);
        double scale = Math.Min((width - 16d) / spanX, (height - 16d) / spanY);
        foreach (PolygonRings polygon in region.Polygons)
        {
            Polyline outline = new() { Stroke = SystemColors.HighlightBrush, StrokeThickness = 2d };
            foreach (Coordinate2D point in polygon.Shell)
            {
                outline.Points.Add(new Point(8d + (point.X - region.Envelope.MinX) * scale, height - 8d - (point.Y - region.Envelope.MinY) * scale));
            }

            _canvas.Children.Add(outline);
        }
    }
}
