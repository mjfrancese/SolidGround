using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// A bounded, WGS 84 display-only ground-point selector. It deliberately owns no terrain data, sampling, or
/// Revit API interaction; callers provide sampling status after they receive <see cref="PointChanged"/>.
/// </summary>
internal sealed class GroundPointPreview : UserControl
{
    private const double MinimumViewportHeight = 180d;
    private const double PinHitDiameter = 32d;
    private const double NudgeMeters = 0.0762d;
    private const double MinimumScale = 0.000001d;
    private const double MaximumScale = 1000000d;

    private readonly DialogPalette palette;
    private readonly Canvas plot = new() { ClipToBounds = true, MinHeight = MinimumViewportHeight, Focusable = true };
    private readonly TextBlock modeText;
    private readonly TextBlock statusText;
    private readonly Button panButton;
    private PolygonalRegion? legalRegion;
    private IReadOnlyList<PolygonalRegion> buildingOutlines = [];
    private Coordinate2D? selectedPoint;
    private Coordinate2D viewCenter;
    private double dipPerMeter = 1d;
    private bool hasView;
    private bool panMode;
    private bool draggingPin;
    private bool panning;
    private Point pointerStart;
    private Coordinate2D? pointBeforeDrag;
    private Coordinate2D viewBeforePan;
    private bool isPlacementEnabled;
    private bool isSelectionValid;
    private string selectionStatus = "Choose a ground point beside the entrance.";

    internal GroundPointPreview(DialogPalette palette)
    {
        this.palette = palette ?? throw new ArgumentNullException(nameof(palette));
        MinHeight = MinimumViewportHeight;
        Focusable = true;
        plot.Background = palette.ControlBackground;
        Border border = new()
        {
            BorderBrush = palette.ActiveBorder,
            BorderThickness = new Thickness(1),
            Background = palette.ControlBackground,
            Padding = new Thickness(8),
        };
        AutomationProperties.SetName(border, "Ground point preview");

        Grid root = new();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1d, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        StackPanel controls = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        controls.Children.Add(ControlButton("Zoom in", (_, _) => ZoomAtCenter(1.25d)));
        controls.Children.Add(ControlButton("Zoom out", (_, _) => ZoomAtCenter(0.8d)));
        panButton = ControlButton("Pan", (_, _) => SetPanMode(!panMode));
        controls.Children.Add(panButton);
        controls.Children.Add(ControlButton("Fit parcel", (_, _) => FitParcel()));
        modeText = new TextBlock
        {
            Margin = new Thickness(10, 4, 0, 0),
            Foreground = palette.ControlText,
            Text = "Mode: Place point",
        };
        AutomationProperties.SetName(modeText, "Current interaction mode");
        controls.Children.Add(modeText);
        Grid.SetRow(controls, 0);
        root.Children.Add(controls);

        Grid plotHost = new();
        plotHost.Children.Add(plot);
        TextBlock north = new()
        {
            Text = "N \u2191  approximate WGS 84 display",
            Margin = new Thickness(8),
            Foreground = palette.ControlText,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        AutomationProperties.SetName(north, "North arrow; approximate WGS 84 display");
        plotHost.Children.Add(north);
        Grid.SetRow(plotHost, 1);
        root.Children.Add(plotHost);

        statusText = new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = palette.ControlText,
        };
        AutomationProperties.SetName(statusText, "Ground point selection status");
        AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Assertive);
        Grid.SetRow(statusText, 2);
        root.Children.Add(statusText);
        border.Child = root;
        Content = border;

        plot.MouseLeftButtonDown += OnPlotMouseLeftButtonDown;
        plot.MouseMove += OnPlotMouseMove;
        plot.MouseLeftButtonUp += OnPlotMouseLeftButtonUp;
        plot.LostMouseCapture += OnPlotLostMouseCapture;
        plot.MouseWheel += OnPlotMouseWheel;
        SizeChanged += (_, _) => Render();
        UpdateStatus();
    }

    /// <summary>Raised for a coordinate selected by a click, pin drag, or keyboard nudge (X longitude, Y latitude).</summary>
    internal event Action<Coordinate2D>? PointChanged;

    /// <summary>Raised only when the focused preview accepts a valid selected point with Enter.</summary>
    internal event EventHandler? PointAccepted;

    internal Coordinate2D? SelectedPoint
    {
        get => selectedPoint;
        set
        {
            selectedPoint = value;
            Render();
        }
    }

    internal bool IsPlacementEnabled
    {
        get => isPlacementEnabled;
        set
        {
            isPlacementEnabled = value;
            UpdateStatus();
            Render();
        }
    }

    internal bool IsSelectionValid
    {
        get => isSelectionValid;
        set
        {
            isSelectionValid = value;
            UpdateStatus();
            Render();
        }
    }

    internal string SelectionStatus
    {
        get => selectionStatus;
        set
        {
            selectionStatus = value ?? string.Empty;
            UpdateStatus();
        }
    }

    /// <summary>
    /// Replaces the visible legal parcel and optional approximate building outlines. The current world selection
    /// and view remain stable unless the caller explicitly confirms a new parcel with <paramref name="resetView"/>.
    /// </summary>
    internal void SetContext(PolygonalRegion legalWgs84, IReadOnlyList<PolygonalRegion> buildingOutlines, bool resetView = false)
    {
        ArgumentNullException.ThrowIfNull(legalWgs84);
        ArgumentNullException.ThrowIfNull(buildingOutlines);
        RequireWgs84(legalWgs84, nameof(legalWgs84));
        foreach (PolygonalRegion outline in buildingOutlines)
        {
            ArgumentNullException.ThrowIfNull(outline);
            RequireWgs84(outline, nameof(buildingOutlines));
        }

        legalRegion = legalWgs84;
        this.buildingOutlines = buildingOutlines.ToArray();
        if (resetView || !hasView)
        {
            FitParcel();
            return;
        }

        Render();
    }

    /// <summary>Fits the whole legal parcel with padding, preserving any selected world coordinate.</summary>
    internal void FitParcel()
    {
        if (legalRegion is null)
        {
            Render();
            return;
        }

        PlanarEnvelope envelope = legalRegion.Envelope;
        viewCenter = new Coordinate2D((envelope.MinX + envelope.MaxX) / 2d, (envelope.MinY + envelope.MaxY) / 2d);
        double width = Math.Max(1d, plot.ActualWidth > 0d ? plot.ActualWidth : ActualWidth - 18d);
        double height = Math.Max(MinimumViewportHeight, plot.ActualHeight > 0d ? plot.ActualHeight : MinimumViewportHeight);
        (double metersPerLongitude, double metersPerLatitude) = MetersPerDegree(viewCenter.Y);
        double parcelWidthMeters = Math.Max((envelope.MaxX - envelope.MinX) * metersPerLongitude, 0.01d);
        double parcelHeightMeters = Math.Max((envelope.MaxY - envelope.MinY) * metersPerLatitude, 0.01d);
        dipPerMeter = ClampScale(Math.Min((width * 0.82d) / parcelWidthMeters, (height * 0.82d) / parcelHeightMeters));
        hasView = true;
        Render();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelPointerGesture();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            if (SelectedPoint is not null && IsSelectionValid) PointAccepted?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
            return;
        }

        if (SelectedPoint is not Coordinate2D point || !IsPlacementEnabled)
        {
            base.OnKeyDown(e);
            return;
        }

        (double longitudeMeters, double latitudeMeters) = MetersPerDegree(point.Y);
        Coordinate2D? nudged = e.Key switch
        {
            Key.Left => new Coordinate2D(point.X - (NudgeMeters / longitudeMeters), point.Y),
            Key.Right => new Coordinate2D(point.X + (NudgeMeters / longitudeMeters), point.Y),
            Key.Up => new Coordinate2D(point.X, point.Y + (NudgeMeters / latitudeMeters)),
            Key.Down => new Coordinate2D(point.X, point.Y - (NudgeMeters / latitudeMeters)),
            _ => null,
        };
        if (nudged is Coordinate2D coordinate)
        {
            SetSelectedPointFromUser(coordinate);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    private Button ControlButton(string name, RoutedEventHandler click)
    {
        Button button = new()
        {
            Content = name,
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(8, 3, 8, 3),
            Foreground = palette.ControlText,
            Background = palette.ControlBackground,
        };
        button.Click += click;
        AutomationProperties.SetName(button, name);
        return button;
    }

    private void OnPlotMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (legalRegion is null) return;
        Focus();
        Point position = e.GetPosition(plot);
        if (panMode)
        {
            panning = true;
            pointerStart = position;
            viewBeforePan = viewCenter;
            plot.CaptureMouse();
            e.Handled = true;
            return;
        }

        if (!IsPlacementEnabled) return;
        if (SelectedPoint is Coordinate2D existing && Distance(position, ToDisplay(existing)) <= PinHitDiameter / 2d)
        {
            draggingPin = true;
            pointBeforeDrag = existing;
            plot.CaptureMouse();
            e.Handled = true;
            return;
        }

        SetSelectedPointFromUser(ToWorld(position));
        e.Handled = true;
    }

    private void OnPlotMouseMove(object sender, MouseEventArgs e)
    {
        if (!plot.IsMouseCaptured) return;
        Point position = e.GetPosition(plot);
        if (draggingPin)
        {
            SetSelectedPointFromUser(ToWorld(position));
            e.Handled = true;
        }
        else if (panning)
        {
            (double longitudeMeters, double latitudeMeters) = MetersPerDegree(viewBeforePan.Y);
            viewCenter = new Coordinate2D(
                viewBeforePan.X - ((position.X - pointerStart.X) / (longitudeMeters * dipPerMeter)),
                viewBeforePan.Y + ((position.Y - pointerStart.Y) / (latitudeMeters * dipPerMeter)));
            Render();
            e.Handled = true;
        }
    }

    private void OnPlotMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!plot.IsMouseCaptured) return;
        draggingPin = false;
        panning = false;
        pointBeforeDrag = null;
        plot.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void OnPlotLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (draggingPin || panning) CancelPointerGesture();
    }

    private void OnPlotMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!IsKeyboardFocusWithin) return;
        Zoom(e.Delta > 0 ? 1.15d : 1d / 1.15d, e.GetPosition(plot));
        e.Handled = true;
    }

    private void SetPanMode(bool enabled)
    {
        panMode = enabled;
        panButton.Content = enabled ? "Place point" : "Pan";
        AutomationProperties.SetName(panButton, enabled ? "Switch to placing a point" : "Switch to pan mode");
        modeText.Text = enabled ? "Mode: Pan (drag to move the view)" : "Mode: Place point";
        UpdateStatus();
    }

    private void ZoomAtCenter(double factor) => Zoom(factor, new Point(plot.ActualWidth / 2d, plot.ActualHeight / 2d));

    private void Zoom(double factor, Point anchor)
    {
        if (!hasView || legalRegion is null) return;
        Coordinate2D anchorWorld = ToWorld(anchor);
        double newScale = ClampScale(dipPerMeter * factor);
        if (newScale.Equals(dipPerMeter)) return;
        double ratio = dipPerMeter / newScale;
        viewCenter = new Coordinate2D(
            anchorWorld.X - ((anchorWorld.X - viewCenter.X) * ratio),
            anchorWorld.Y - ((anchorWorld.Y - viewCenter.Y) * ratio));
        dipPerMeter = newScale;
        Render();
    }

    private void CancelPointerGesture()
    {
        if (draggingPin)
        {
            selectedPoint = pointBeforeDrag;
            PointChanged?.Invoke(selectedPoint!.Value);
        }
        else if (panning)
        {
            viewCenter = viewBeforePan;
        }

        draggingPin = false;
        panning = false;
        pointBeforeDrag = null;
        if (plot.IsMouseCaptured) plot.ReleaseMouseCapture();
        Render();
    }

    private void SetSelectedPointFromUser(Coordinate2D coordinate)
    {
        selectedPoint = coordinate;
        PointChanged?.Invoke(coordinate);
        Render();
    }

    private void Render()
    {
        plot.Children.Clear();
        if (legalRegion is null || !hasView)
        {
            AddMessage("The parcel boundary will appear when it is ready.");
            return;
        }

        foreach (PolygonalRegion outline in buildingOutlines) AddRegion(outline, palette.GrayText, true, 1.5d);
        AddRegion(legalRegion, palette.Highlight, false, 2.25d);
        if (selectedPoint is Coordinate2D point) AddPin(point);
    }

    private void AddRegion(PolygonalRegion region, Brush stroke, bool dashed, double thickness)
    {
        foreach (PolygonRings polygon in region.Polygons)
        {
            AddRing(polygon.Shell, stroke, dashed, thickness);
            foreach (IReadOnlyList<Coordinate2D> hole in polygon.Holes) AddRing(hole, stroke, true, thickness);
        }
    }

    private void AddRing(IReadOnlyList<Coordinate2D> ring, Brush stroke, bool dashed, double thickness)
    {
        Polyline line = new() { Stroke = stroke, StrokeThickness = thickness, StrokeDashArray = dashed ? [4d, 3d] : null, IsHitTestVisible = false };
        foreach (Coordinate2D point in ring) line.Points.Add(ToDisplay(point));
        plot.Children.Add(line);
    }

    private void AddPin(Coordinate2D point)
    {
        Point display = ToDisplay(point);
        Ellipse focusRing = new()
        {
            Width = PinHitDiameter,
            Height = PinHitDiameter,
            Stroke = palette.Focus,
            StrokeThickness = 2d,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(focusRing, display.X - (PinHitDiameter / 2d));
        Canvas.SetTop(focusRing, display.Y - (PinHitDiameter / 2d));
        AutomationProperties.SetName(focusRing, "Selected ground point focus ring");
        plot.Children.Add(focusRing);
        Ellipse pin = new()
        {
            Width = 12d,
            Height = 12d,
            Fill = IsSelectionValid ? palette.Highlight : palette.Error,
            Stroke = palette.Window,
            StrokeThickness = 2d,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(pin, display.X - 6d);
        Canvas.SetTop(pin, display.Y - 6d);
        AutomationProperties.SetName(pin, IsSelectionValid ? "Selected usable ground point" : "Selected ground point with no usable height");
        plot.Children.Add(pin);
    }

    private void AddMessage(string message)
    {
        TextBlock text = new() { Text = message, Margin = new Thickness(8), Foreground = palette.ControlText, TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
        plot.Children.Add(text);
    }

    private Point ToDisplay(Coordinate2D coordinate)
    {
        (double longitudeMeters, double latitudeMeters) = MetersPerDegree(viewCenter.Y);
        return new Point(
            (plot.ActualWidth / 2d) + ((coordinate.X - viewCenter.X) * longitudeMeters * dipPerMeter),
            (plot.ActualHeight / 2d) - ((coordinate.Y - viewCenter.Y) * latitudeMeters * dipPerMeter));
    }

    private Coordinate2D ToWorld(Point display) => ToWorldFrom(viewCenter, new Point(plot.ActualWidth / 2d, plot.ActualHeight / 2d), display);

    private Coordinate2D ToWorldFrom(Coordinate2D center, Point displayCenter, Point display)
    {
        (double longitudeMeters, double latitudeMeters) = MetersPerDegree(center.Y);
        return new Coordinate2D(
            center.X + ((display.X - displayCenter.X) / (longitudeMeters * dipPerMeter)),
            center.Y - ((display.Y - displayCenter.Y) / (latitudeMeters * dipPerMeter)));
    }

    private void UpdateStatus()
    {
        string availability = IsPlacementEnabled ? " Place a point, drag the pin, or use arrow keys to move it by 0.0762 m (3 international inches)." : " Ground point placement is unavailable until preview data is ready.";
        statusText.Text = SelectionStatus + availability;
        AutomationProperties.SetHelpText(plot, statusText.Text);
    }

    private static void RequireWgs84(PolygonalRegion region, string parameterName)
    {
        if (region.HorizontalReference.Kind != SolidGround.Core.Metadata.HorizontalReferenceKind.Geographic ||
            region.HorizontalReference.AxisOrder != SolidGround.Core.Metadata.HorizontalAxisOrder.LongitudeLatitude)
        {
            throw new ArgumentException("Ground preview accepts WGS 84 longitude/latitude geometry only.", parameterName);
        }
    }

    private static (double Longitude, double Latitude) MetersPerDegree(double latitudeDegrees)
    {
        double latitudeRadians = latitudeDegrees * Math.PI / 180d;
        double latitude = 111132.92d - (559.82d * Math.Cos(2d * latitudeRadians)) + (1.175d * Math.Cos(4d * latitudeRadians));
        double longitude = 111412.84d * Math.Cos(latitudeRadians) - (93.5d * Math.Cos(3d * latitudeRadians));
        return (Math.Max(longitude, 0.000001d), latitude);
    }

    private static double ClampScale(double value) => Math.Clamp(value, MinimumScale, MaximumScale);

    private static double Distance(Point left, Point right)
    {
        double x = left.X - right.X;
        double y = left.Y - right.Y;
        return Math.Sqrt((x * x) + (y * y));
    }
}
