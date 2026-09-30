using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Revit.Dialog;

/// <summary>A managed-vector confirmation preview; text remains the authoritative legal-boundary description.</summary>
internal sealed class ParcelBoundaryPreview : Border
{
    private readonly Canvas _canvas = new() { Height = 176d, MinWidth = 260d };
    private readonly DialogPalette _palette;
    private SolidGroundDialogViewModel? _viewModel;
    private ParcelProximityCandidate? _cachedCandidate;
    private double _cachedExtensionMeters = double.NaN;
    private ParcelTerrainPreview? _cachedPreview;

    internal ParcelBoundaryPreview(DialogPalette palette)
    {
        _palette = palette ?? throw new ArgumentNullException(nameof(palette));
        BorderBrush = palette.ActiveBorder;
        BorderThickness = new Thickness(1);
        Padding = new Thickness(8);
        Child = _canvas;
        AutomationProperties.SetName(this, "Parcel boundary preview");
        DataContextChanged += OnDataContextChanged;
        SizeChanged += (_, _) => Render();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as SolidGroundDialogViewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Render();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SolidGroundDialogViewModel.SelectedParcelCandidate) or nameof(SolidGroundDialogViewModel.SelectedGeocodeCandidate) or nameof(SolidGroundDialogViewModel.EffectiveSettings)) Render();
    }

    private void Render()
    {
        _canvas.Children.Clear();
        ParcelProximityCandidate? selected = _viewModel?.SelectedParcelCandidate;
        if (selected is null)
        {
            AddText("Select a parcel to preview its legal outline.", 4d, 4d);
            AutomationProperties.SetHelpText(this, "No parcel is selected.");
            return;
        }

        double extensionMeters = _viewModel?.EffectiveSettings?.TerrainExtensionMeters ?? 0d;
        ParcelTerrainPreview? preview = GetPreview(selected, extensionMeters);
        if (preview is null)
        {
            AddText("The terrain-extension preview is unavailable for this boundary.", 4d, 4d);
            AutomationProperties.SetHelpText(this, "The selected legal boundary remains available, but its display-only terrain extension could not be drawn.");
            return;
        }

        PolygonalRegion legal = preview.LegalRegion;
        PolygonalRegion terrain = preview.TerrainRegion;
        PlanarEnvelope envelope = terrain.Envelope;
        double width = Math.Max(1d, _canvas.ActualWidth == 0d ? 260d : _canvas.ActualWidth);
        double height = Math.Max(1d, _canvas.ActualHeight == 0d ? 176d : _canvas.ActualHeight);
        const double inset = 18d;
        double scale = Math.Min((width - 2d * inset) / Math.Max(envelope.MaxX - envelope.MinX, double.Epsilon), (height - 2d * inset) / Math.Max(envelope.MaxY - envelope.MinY, double.Epsilon));

        if (extensionMeters > 0d)
        {
            foreach (PolygonRings polygon in terrain.Polygons)
            {
                AddRing(polygon.Shell, _palette.GrayText, true);
                foreach (IReadOnlyList<Coordinate2D> hole in polygon.Holes) AddRing(hole, _palette.GrayText, true);
            }
        }

        foreach (PolygonRings polygon in legal.Polygons)
        {
            AddRing(polygon.Shell, _palette.Highlight, false);
            foreach (IReadOnlyList<Coordinate2D> hole in polygon.Holes) AddRing(hole, _palette.Highlight, true);
        }

        if (_viewModel?.SelectedGeocodeCandidate is { } location)
        {
            Ellipse point = new() { Width = 8d, Height = 8d, Fill = _palette.ControlText, Stroke = _palette.Window, StrokeThickness = 1d };
            Coordinate2D displayPoint = preview.ToDisplay(new Coordinate2D(location.Longitude, location.Latitude));
            Canvas.SetLeft(point, X(displayPoint.X) - 4d); Canvas.SetTop(point, Y(displayPoint.Y) - 4d); _canvas.Children.Add(point);
        }

        string counts = $"Legal: {preview.LegalPolygonCount.ToString(CultureInfo.InvariantCulture)} parts, {preview.LegalHoleCount.ToString(CultureInfo.InvariantCulture)} holes; terrain: {preview.TerrainPolygonCount.ToString(CultureInfo.InvariantCulture)} parts, {preview.TerrainHoleCount.ToString(CultureInfo.InvariantCulture)} holes" + (preview.TopologyChangeCount > 0 ? $"; {preview.TopologyChangeCount.ToString(CultureInfo.InvariantCulture)} topology count changes" : string.Empty);
        AutomationProperties.SetHelpText(this, "Solid outline is the legal parcel; dot is the selected location. " + counts + (extensionMeters > 0d ? "; dashed outline is an approximate terrain extension. The exact edge follows the raster CRS after download." : "."));
        AddText("Solid: legal parcel  •  Dot: location" + (extensionMeters > 0d ? "  •  Dashed: approximate terrain extension" : string.Empty), 4d, height - 32d);
        AddText(counts, 4d, height - 18d);
        AddText("N ↑", width - 30d, 2d);

        void AddRing(IReadOnlyList<Coordinate2D> ring, Brush brush, bool dashed)
        {
            Polyline outline = new() { Stroke = brush, StrokeThickness = dashed ? 1.5d : 2d, StrokeDashArray = dashed ? [4d, 3d] : null };
            foreach (Coordinate2D point in ring) outline.Points.Add(new Point(X(point.X), Y(point.Y)));
            _canvas.Children.Add(outline);
        }
        double X(double longitude) => inset + ((longitude - envelope.MinX) * scale);
        double Y(double latitude) => height - inset - ((latitude - envelope.MinY) * scale);
    }

    private void AddText(string value, double left, double top)
    {
        TextBlock text = new() { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = 11d, Foreground = _palette.ControlText };
        Canvas.SetLeft(text, left); Canvas.SetTop(text, top); _canvas.Children.Add(text);
    }

    private ParcelTerrainPreview? GetPreview(ParcelProximityCandidate selected, double extensionMeters)
    {
        if (ReferenceEquals(_cachedCandidate, selected) && extensionMeters.Equals(_cachedExtensionMeters)) return _cachedPreview;
        _cachedCandidate = selected;
        _cachedExtensionMeters = extensionMeters;
        try { _cachedPreview = ParcelTerrainPreview.Build(selected.Candidate.Boundary, LinearDistance.Meters(extensionMeters)); }
        catch (ArgumentException) { _cachedPreview = null; }
        return _cachedPreview;
    }
}
