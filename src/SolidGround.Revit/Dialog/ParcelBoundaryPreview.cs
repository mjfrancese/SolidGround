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
    private Wgs84BoundingBoxAoi? _cachedRequestEnvelope;

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

        PolygonalRegion legal = selected.Candidate.Boundary;
        double extensionMeters = _viewModel?.EffectiveSettings?.TerrainExtensionMeters ?? 0d;
        Wgs84BoundingBoxAoi? requestEnvelope = GetRequestEnvelope(selected, extensionMeters);
        PlanarEnvelope envelope = requestEnvelope is null ? legal.Envelope : Union(legal.Envelope, requestEnvelope);
        double width = Math.Max(1d, _canvas.ActualWidth == 0d ? 260d : _canvas.ActualWidth);
        double height = Math.Max(1d, _canvas.ActualHeight == 0d ? 176d : _canvas.ActualHeight);
        const double inset = 18d;
        double scale = Math.Min((width - 2d * inset) / Math.Max(envelope.MaxX - envelope.MinX, double.Epsilon), (height - 2d * inset) / Math.Max(envelope.MaxY - envelope.MinY, double.Epsilon));

        if (requestEnvelope is not null && extensionMeters > 0d)
        {
            Rectangle request = new() { Width = (requestEnvelope.EastLongitude - requestEnvelope.WestLongitude) * scale, Height = (requestEnvelope.NorthLatitude - requestEnvelope.SouthLatitude) * scale, Stroke = _palette.GrayText, StrokeThickness = 1.5d, StrokeDashArray = [4d, 3d] };
            Canvas.SetLeft(request, X(requestEnvelope.WestLongitude));
            Canvas.SetTop(request, Y(requestEnvelope.NorthLatitude));
            _canvas.Children.Add(request);
        }

        foreach (PolygonRings polygon in legal.Polygons)
        {
            AddRing(polygon.Shell, false);
            foreach (IReadOnlyList<Coordinate2D> hole in polygon.Holes) AddRing(hole, true);
        }

        if (_viewModel?.SelectedGeocodeCandidate is { } location)
        {
            Ellipse point = new() { Width = 8d, Height = 8d, Fill = _palette.ControlText, Stroke = _palette.Window, StrokeThickness = 1d };
            Canvas.SetLeft(point, X(location.Longitude) - 4d); Canvas.SetTop(point, Y(location.Latitude) - 4d); _canvas.Children.Add(point);
        }

        string geometry = legal.PolygonCount > 1 ? $"{legal.PolygonCount.ToString(CultureInfo.InvariantCulture)} legal parts" : "one legal part";
        string holes = legal.HoleCount == 0 ? "no holes" : $"{legal.HoleCount.ToString(CultureInfo.InvariantCulture)} holes";
        AutomationProperties.SetHelpText(this, $"Solid outline is the legal parcel ({geometry}, {holes}); dot is the selected location" + (requestEnvelope is not null && extensionMeters > 0d ? "; dashed rectangle is the source request envelope, not the projected terrain-buffer boundary." : "."));
        AddText("Solid: legal parcel  •  Dot: location" + (requestEnvelope is not null && extensionMeters > 0d ? "  •  Dashed: source request envelope" : string.Empty), 4d, height - 18d);
        AddText("N ↑", width - 30d, 2d);

        void AddRing(IReadOnlyList<Coordinate2D> ring, bool hole)
        {
            Polyline outline = new() { Stroke = _palette.Highlight, StrokeThickness = hole ? 1.5d : 2d, StrokeDashArray = hole ? [2d, 2d] : null };
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

    private static PlanarEnvelope Union(PlanarEnvelope legal, Wgs84BoundingBoxAoi terrain) => new(Math.Min(legal.MinX, terrain.WestLongitude), Math.Min(legal.MinY, terrain.SouthLatitude), Math.Max(legal.MaxX, terrain.EastLongitude), Math.Max(legal.MaxY, terrain.NorthLatitude));

    private Wgs84BoundingBoxAoi? GetRequestEnvelope(ParcelProximityCandidate selected, double extensionMeters)
    {
        if (ReferenceEquals(_cachedCandidate, selected) && extensionMeters.Equals(_cachedExtensionMeters)) return _cachedRequestEnvelope;
        _cachedCandidate = selected;
        _cachedExtensionMeters = extensionMeters;
        try { _cachedRequestEnvelope = ParcelFetchEnvelopePlanner.Build(ParcelBoundaryAoiFactory.FromCandidate(selected.Candidate, LinearDistance.Zero), LinearDistance.Meters(extensionMeters)); }
        catch (ParcelFetchEnvelopePlanningException) { _cachedRequestEnvelope = null; }
        return _cachedRequestEnvelope;
    }
}
