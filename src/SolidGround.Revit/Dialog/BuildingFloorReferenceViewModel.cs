using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using SolidGround.Revit.Processing;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// Owns the one-run floor-reference decision.  It intentionally contains no Revit API: callers supply the
/// UI-thread level read and the prepared terrain callback, while pin movement samples that immutable snapshot.
/// </summary>
internal sealed partial class BuildingFloorReferenceViewModel : ObservableObject, IDisposable
{
    private readonly Func<CancellationToken, Task<PreparedTerrainSnapshot>> prepare;
    private readonly Func<TargetProjectLevel?> getTargetLevel;
    private readonly Func<DistanceDisplayFormat> getDisplayFormat;
    private readonly IBuildingOutlineSource? outlines;
    private readonly bool alreadyCoordinated;
    private CancellationTokenSource? prepareCancellation;
    private CancellationTokenSource? outlineCancellation;
    private long generation;
    private long outlineRequestRevision;
    private PreparedTerrainSnapshot? snapshot;
    private GroundPoint? selectedPoint;
    private ElevationGridSample? selectedSample;
    private IReadOnlyList<PolygonalRegion> visibleOutlines = [];
    private EditState? editState;

    internal BuildingFloorReferenceViewModel(
        Func<CancellationToken, Task<PreparedTerrainSnapshot>> prepare,
        Func<TargetProjectLevel?> getTargetLevel,
        Func<DistanceDisplayFormat> getDisplayFormat,
        IBuildingOutlineSource? outlines,
        bool alreadyCoordinated)
    {
        this.prepare = prepare ?? throw new ArgumentNullException(nameof(prepare));
        this.getTargetLevel = getTargetLevel ?? throw new ArgumentNullException(nameof(getTargetLevel));
        this.getDisplayFormat = getDisplayFormat ?? throw new ArgumentNullException(nameof(getDisplayFormat));
        this.outlines = outlines;
        this.alreadyCoordinated = alreadyCoordinated;
        Mode = FloorReferenceMode.EstimatedGradeRise;
    }

    internal event EventHandler? Edited;
    internal event EventHandler? ConfirmationChanged;
    internal event EventHandler? PreparedChanged;

    public bool IsEditing { get; private set; }
    public bool IsConfirmed { get; private set; }
    public bool Prepared => snapshot is not null;
    /// <summary>The immutable prepared run final creation must consume; null until the current preparation succeeds.</summary>
    public PreparedTerrainSnapshot? PreparedTerrain => snapshot;
    public BuildingFloorReference? Reference { get; private set; }
    public PreparedTerrainSnapshot? Snapshot => snapshot;
    public GroundPoint? SelectedPoint => selectedPoint;
    public ElevationGridSample? SelectedSample => selectedSample;
    public IBuildingOutlineSource? OutlineSource => outlines;
    public IReadOnlyList<PolygonalRegion> VisibleOutlines => visibleOutlines;
    public BuildingOutlineProvenance? OutlineProvenance { get; private set; }
    public string? OutlineStatus { get; private set; }
    public string OutlineAttributionSummary => OutlineProvenance is null
        ? string.Empty
        : $"Approximate building outlines: {OutlineProvenance.Attribution} Release {OutlineProvenance.Release}. {OutlineProvenance.LicenseText}";
    public bool CanRetryOutlines => outlines is not null && snapshot is not null && Mode == FloorReferenceMode.EstimatedGradeRise;
    public bool AlreadyCoordinated => alreadyCoordinated;
    public bool IsFloorPlacementBlocked => alreadyCoordinated;
    public string ExistingCoordinatesExplanation => alreadyCoordinated
        ? "This project already has shared coordinates. A floor height cannot establish its missing horizontal placement, so use original source elevations or an uncoordinated site document."
        : string.Empty;
    public string DisplayUnitLabel => getDisplayFormat() switch
    {
        DistanceDisplayFormat.UsSurveyFeet => "US survey ft",
        DistanceDisplayFormat.InternationalFeet => "ft",
        DistanceDisplayFormat.InternationalInches => "in",
        DistanceDisplayFormat.FeetAndInches => "ft/in",
        DistanceDisplayFormat.Metres => "m",
        _ => "selected units",
    };
    public string KnownElevationLabel => $"Known floor elevation ({DisplayUnitLabel})";
    public string SourceReferenceSummary => snapshot is null
        ? "Source vertical datum and unit will be shown after the ground preview is loaded."
        : $"Source elevation reference: {snapshot.Grid.VerticalReference.Datum}; native unit: {snapshot.Grid.VerticalReference.Unit}.";
    public string SelectedSampleHeight => selectedSample is null
        ? string.Empty
        : $"Selected ground height: {selectedSample.Elevation.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} {selectedSample.SourceReference.Unit} ({selectedSample.SourceReference.Datum}).";
    public string Summary => Reference is null ? "Original source elevations" : Reference.Mode switch
    {
        FloorReferenceMode.KnownElevation => $"Known first-floor elevation on {Reference.TargetLevel.Name}",
        FloorReferenceMode.EstimatedGradeRise => $"Estimated from selected ground beside the entrance on {Reference.TargetLevel.Name}",
        FloorReferenceMode.ProvisionalGround => $"Ground-centered preview; first-floor elevation not set ({Reference.TargetLevel.Name})",
        _ => "Floor reference needs review",
    };
    public string PrimaryLabel => !IsEditing ? "Change floor reference" : !Prepared
        ? "Load ground preview"
        : "Use this floor reference";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryLabel), nameof(ShowEstimate), nameof(ShowKnownElevation), nameof(ShowProvisional), nameof(CanConfirm), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private FloorReferenceMode mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string knownElevationText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string knownSourceDescription = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private bool knownDatumAcknowledged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage), nameof(CanConfirm))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private string measuredRiseText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirm), nameof(ValidationMessage))]
    [NotifyCanExecuteChangedFor(nameof(PrimaryCommand))]
    private bool belowGrade;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProvisional), nameof(CanConfirm))]
    private string? validationMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryLabel))]
    private bool isPreparing;

    [ObservableProperty] private string coordinateLatitudeText = string.Empty;
    [ObservableProperty] private string coordinateLongitudeText = string.Empty;

    public bool ShowEstimate => Mode == FloorReferenceMode.EstimatedGradeRise;
    public bool ShowKnownElevation => Mode == FloorReferenceMode.KnownElevation;
    public bool ShowProvisional => Mode == FloorReferenceMode.ProvisionalGround;
    public bool CanConfirm => !IsFloorPlacementBlocked && TryBuildReference(out _, out _);
    public string MeasurementHint => $"Required. Enter a distance in {DisplayUnitLabel}; for example, 2 ft 3 in. Choose below grade when the unfinished floor is below exterior grade.";
    public bool HasValidSelection => selectedSample is not null;

    public void BeginEdit()
    {
        editState = new EditState(
            Reference,
            IsConfirmed,
            Mode,
            KnownElevationText,
            KnownSourceDescription,
            KnownDatumAcknowledged,
            MeasuredRiseText,
            BelowGrade,
            CoordinateLatitudeText,
            CoordinateLongitudeText,
            selectedPoint,
            selectedSample);
        IsEditing = true;
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(PrimaryLabel));
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns to Review without discarding the last confirmed reference or the reusable terrain snapshot.</summary>
    public void CancelEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        CancelPendingPreparation();
        EditState? state = editState;
        if (state is not null)
        {
            Mode = state.Mode;
            KnownElevationText = state.KnownElevationText;
            KnownSourceDescription = state.KnownSourceDescription;
            KnownDatumAcknowledged = state.KnownDatumAcknowledged;
            MeasuredRiseText = state.MeasuredRiseText;
            BelowGrade = state.BelowGrade;
            CoordinateLatitudeText = state.CoordinateLatitudeText;
            CoordinateLongitudeText = state.CoordinateLongitudeText;
            selectedPoint = state.SelectedPoint;
            selectedSample = state.SelectedSample;
            Reference = state.Reference;
            IsConfirmed = state.IsConfirmed;
        }
        else
        {
            Reference = null;
            IsConfirmed = false;
        }

        editState = null;
        IsEditing = false;
        ValidationMessage = null;
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PrimaryLabel));
        OnPropertyChanged(nameof(SelectedPoint));
        OnPropertyChanged(nameof(SelectedSample));
        OnPropertyChanged(nameof(HasValidSelection));
        OnPropertyChanged(nameof(SelectedSampleHeight));
        ConfirmationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Cancels only the active preparation. A previously usable snapshot remains available.</summary>
    public void CancelPendingPreparation()
    {
        generation++;
        prepareCancellation?.Cancel();
        IsPreparing = false;
        OnPropertyChanged(nameof(PrimaryLabel));
        PrimaryCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Cancels all in-flight work when the containing dialog closes.</summary>
    public void CancelAll()
    {
        CancelPendingPreparation();
        CancelOutlineRequest();
    }

    public void Dispose() => CancelAll();

    /// <summary>Cancels an in-flight preparation and makes any old completion ineligible for selection or confirmation.</summary>
    public void Invalidate()
    {
        CancelPendingPreparation();
        snapshot = null;
        selectedPoint = null;
        selectedSample = null;
        editState = null;
        visibleOutlines = [];
        OutlineProvenance = null;
        OutlineStatus = null;
        Reference = null;
        IsConfirmed = false;
        ValidationMessage = null;
        OnPropertyChanged(nameof(Prepared));
        OnPropertyChanged(nameof(Snapshot));
        OnPropertyChanged(nameof(PreparedTerrain));
        OnPropertyChanged(nameof(SourceReferenceSummary));
        OnPropertyChanged(nameof(KnownElevationLabel));
        OnPropertyChanged(nameof(SelectedPoint));
        OnPropertyChanged(nameof(SelectedSample));
        OnPropertyChanged(nameof(HasValidSelection));
        OnPropertyChanged(nameof(SelectedSampleHeight));
        OnPropertyChanged(nameof(VisibleOutlines));
        OnPropertyChanged(nameof(OutlineProvenance));
        OnPropertyChanged(nameof(OutlineStatus));
        OnPropertyChanged(nameof(OutlineAttributionSummary));
        OnPropertyChanged(nameof(CanRetryOutlines));
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(Summary));
        PreparedChanged?.Invoke(this, EventArgs.Empty);
        ConfirmationChanged?.Invoke(this, EventArgs.Empty);
    }

    partial void OnModeChanged(FloorReferenceMode value)
    {
        InvalidateConfirmation();
        OnPropertyChanged(nameof(CanRetryOutlines));
    }

    partial void OnKnownElevationTextChanged(string value) => InvalidateConfirmation();
    partial void OnKnownSourceDescriptionChanged(string value) => InvalidateConfirmation();
    partial void OnKnownDatumAcknowledgedChanged(bool value) => InvalidateConfirmation();
    partial void OnMeasuredRiseTextChanged(string value) => InvalidateConfirmation();
    partial void OnBelowGradeChanged(bool value) => InvalidateConfirmation();

    /// <summary>Samples a WGS 84 display point locally; it never invokes preparation or outline I/O.</summary>
    public void SelectPoint(Coordinate2D wgs84Point)
    {
        if (snapshot is null)
        {
            return;
        }

        try
        {
            Coordinate2D projected = snapshot.Transform.Forward(wgs84Point);
            ElevationGridSample sample = ElevationGridSampler.SampleStrictBilinear(snapshot.Grid, projected);
            selectedPoint = new GroundPoint(wgs84Point.X, wgs84Point.Y, projected);
            selectedSample = sample;
            ValidationMessage = null;
        }
        catch (ElevationGridSamplingException)
        {
            selectedPoint = null;
            selectedSample = null;
            ValidationMessage = "No usable ground height here. Move the point.";
        }

        OnPropertyChanged(nameof(SelectedPoint));
        OnPropertyChanged(nameof(SelectedSample));
        OnPropertyChanged(nameof(HasValidSelection));
        OnPropertyChanged(nameof(SelectedSampleHeight));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(ValidationMessage));
        PrimaryCommand.NotifyCanExecuteChanged();
        Edited?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fetches optional display outlines only after current terrain is usable; failure never blocks pin placement.</summary>
    public async Task LoadOutlinesAsync(CancellationToken cancellationToken = default)
    {
        if (outlines is null || snapshot is null || Mode != FloorReferenceMode.EstimatedGradeRise)
        {
            return;
        }

        PolygonalRegion legal = GetLegalRegionWgs84(snapshot);
        long currentGeneration = generation;
        CancelOutlineRequest();
        long requestRevision = ++outlineRequestRevision;
        CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        outlineCancellation = operationCancellation;
        try
        {
            BuildingOutlineAcquisition result = await outlines.GetAsync(
                new Wgs84BoundingBoxAoi(legal.Envelope.MinX, legal.Envelope.MinY, legal.Envelope.MaxX, legal.Envelope.MaxY), operationCancellation.Token).ConfigureAwait(true);
            if (currentGeneration != generation || requestRevision != outlineRequestRevision || operationCancellation.IsCancellationRequested)
            {
                return;
            }

            visibleOutlines = result.Outlines;
            OutlineProvenance = result.Provenance;
            OutlineStatus = result.UnavailabilityReason ?? (result.Outlines.Count == 0 ? "No approximate building outlines are available here." : result.Provenance.Attribution);
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (currentGeneration == generation && requestRevision == outlineRequestRevision)
            {
                visibleOutlines = [];
                OutlineStatus = "Approximate building outlines are unavailable. Ground-point selection is still ready.";
            }
        }

        finally
        {
            if (ReferenceEquals(outlineCancellation, operationCancellation)) outlineCancellation = null;
            operationCancellation.Dispose();
        }

        OnPropertyChanged(nameof(VisibleOutlines));
        OnPropertyChanged(nameof(OutlineProvenance));
        OnPropertyChanged(nameof(OutlineStatus));
        OnPropertyChanged(nameof(OutlineAttributionSummary));
    }

    [RelayCommand(CanExecute = nameof(CanRetryOutlines), IncludeCancelCommand = true, AllowConcurrentExecutions = true)]
    private Task RetryOutlinesAsync(CancellationToken cancellationToken) => LoadOutlinesAsync(cancellationToken);

    [RelayCommand]
    private void PlaceEnteredCoordinates()
    {
        if (snapshot is null)
        {
            ValidationMessage = "Load ground preview before placing a ground point.";
            return;
        }

        if (!double.TryParse(CoordinateLatitudeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double latitude)
            || !double.TryParse(CoordinateLongitudeText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double longitude)
            || latitude is < -90d or > 90d || longitude is < -180d or > 180d)
        {
            ValidationMessage = "Enter valid latitude and longitude values before placing the ground point.";
            return;
        }

        SelectPoint(new Coordinate2D(longitude, latitude));
    }

    private void CancelOutlineRequest()
    {
        outlineRequestRevision++;
        outlineCancellation?.Cancel();
    }

    internal static PolygonalRegion GetLegalRegionWgs84(PreparedTerrainSnapshot prepared)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        PolygonalRegion region = prepared.Outcome.TerrainExtentPlan?.LegalParcelRegion ?? CreateGridRegion(prepared);
        return PolygonalRegionReprojection.Reproject(region, prepared.Transform, HorizontalTransformDirection.Inverse);
    }

    [RelayCommand(CanExecute = nameof(CanRunPrimary), IncludeCancelCommand = true, AllowConcurrentExecutions = true)]
    private async Task PrimaryAsync(CancellationToken cancellationToken)
    {
        if (!IsEditing)
        {
            BeginEdit();
            return;
        }

        if (snapshot is null)
        {
            await PrepareAsync(cancellationToken).ConfigureAwait(true);
            return;
        }

        if (!TryBuildReference(out BuildingFloorReference? reference, out string? error))
        {
            ValidationMessage = error;
            return;
        }

        Reference = reference;
        IsConfirmed = true;
        IsEditing = false;
        ValidationMessage = null;
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PrimaryLabel));
        ConfirmationChanged?.Invoke(this, EventArgs.Empty);
    }

    private bool CanRunPrimary() => !IsPreparing && (!IsEditing || !IsFloorPlacementBlocked);

    [RelayCommand]
    private void UseOriginalSourceElevations()
    {
        CancelPendingPreparation();
        CancelOutlineRequest();
        Reference = null;
        IsConfirmed = true;
        IsEditing = false;
        ValidationMessage = null;
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(PrimaryLabel));
        ConfirmationChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task PrepareAsync(CancellationToken token)
    {
        long currentGeneration = generation;
        prepareCancellation?.Cancel();
        prepareCancellation?.Dispose();
        CancellationTokenSource operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        prepareCancellation = operationCancellation;
        IsPreparing = true;
        ValidationMessage = null;
        try
        {
            PreparedTerrainSnapshot prepared = await prepare(operationCancellation.Token).ConfigureAwait(true);
            if (currentGeneration != generation || operationCancellation.IsCancellationRequested)
            {
                return;
            }

            snapshot = prepared;
            OnPropertyChanged(nameof(Prepared));
            OnPropertyChanged(nameof(Snapshot));
            OnPropertyChanged(nameof(PreparedTerrain));
            OnPropertyChanged(nameof(PrimaryLabel));
            OnPropertyChanged(nameof(CanRetryOutlines));
            OnPropertyChanged(nameof(SourceReferenceSummary));
            PreparedChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (operationCancellation.IsCancellationRequested)
        {
            // The newest input owns the panel. Cancellation is not an error state.
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or IOException or TimeoutException)
        {
            if (currentGeneration == generation)
            {
                ValidationMessage = ex.Message;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            if (currentGeneration == generation) ValidationMessage = "Ground preview could not be prepared safely. Check the current terrain settings and try again.";
        }
        finally
        {
            if (currentGeneration == generation)
            {
                IsPreparing = false;
                PrimaryCommand.NotifyCanExecuteChanged();
            }
            operationCancellation.Dispose();
            if (ReferenceEquals(prepareCancellation, operationCancellation))
            {
                prepareCancellation = null;
            }
        }
    }

    private void InvalidateConfirmation()
    {
        if (Reference is null && !IsConfirmed)
        {
            return;
        }

        Reference = null;
        IsConfirmed = false;
        OnPropertyChanged(nameof(Reference));
        OnPropertyChanged(nameof(IsConfirmed));
        OnPropertyChanged(nameof(Summary));
        ConfirmationChanged?.Invoke(this, EventArgs.Empty);
        Edited?.Invoke(this, EventArgs.Empty);
    }

    private bool TryBuildReference(out BuildingFloorReference? result, out string? error)
    {
        result = null;
        error = null;
        if (IsFloorPlacementBlocked)
        {
            error = ExistingCoordinatesExplanation;
            return false;
        }

        TargetProjectLevel? level = getTargetLevel();
        if (level is null)
        {
            error = "Choose a target project level before setting the floor reference.";
            return false;
        }

        try
        {
            switch (Mode)
            {
                case FloorReferenceMode.KnownElevation:
                    if (!KnownDatumAcknowledged)
                    {
                        error = "Confirm that the entered elevation uses the stated source datum.";
                        return false;
                    }
                    if (string.IsNullOrWhiteSpace(KnownSourceDescription))
                    {
                        error = "Describe the source of the known floor elevation.";
                        return false;
                    }
                    if (!TryParseSignedLength(KnownElevationText, getDisplayFormat(), out double knownElevation))
                    {
                        error = "Enter a finite known floor elevation in the displayed unit.";
                        return false;
                    }
                    result = BuildingFloorReference.KnownElevation(
                        LengthConverter.Convert(knownElevation, RequireFormatUnit(getDisplayFormat()), RequireSnapshot().Grid.VerticalReference.Unit),
                        RequireSnapshot().Grid.VerticalReference,
                        level,
                        KnownSourceDescription.Trim(),
                        OutlineProvenance);
                    return true;

                case FloorReferenceMode.EstimatedGradeRise:
                    if (selectedPoint is null || selectedSample is null)
                    {
                        error = "Select a usable ground point beside the entrance.";
                        return false;
                    }
                    if (!TryParseNonnegativeLength(MeasuredRiseText, getDisplayFormat(), out double rise, out LengthUnit riseUnit))
                    {
                        error = "Enter the measured rise. Leave it blank only when using a provisional preview.";
                        return false;
                    }
                    result = BuildingFloorReference.EstimatedGradeRise(selectedPoint, selectedSample, new MeasuredRise(rise, riseUnit, BelowGrade), level, OutlineProvenance);
                    return true;

                case FloorReferenceMode.ProvisionalGround:
                    PreparedTerrainSnapshot prepared = RequireSnapshot();
                    ProvisionalGroundReference provisional = TerrainPlacementReframer.ResolveProvisionalGroundReference(prepared.Outcome, prepared.Grid);
                    result = BuildingFloorReference.ProvisionalGround(provisional.Elevation, prepared.Grid.VerticalReference, level, provisional.Policy, OutlineProvenance);
                    return true;

                default:
                    error = "Choose how to set the first-floor reference.";
                    return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    private PreparedTerrainSnapshot RequireSnapshot() => snapshot ?? throw new InvalidOperationException("Load ground preview before using terrain data.");

    private static PolygonalRegion CreateGridRegion(PreparedTerrainSnapshot prepared)
    {
        var factory = new NetTopologySuite.Geometries.GeometryFactory();
        SolidGround.Core.Terrain.ElevationGrid grid = prepared.Grid;
        Coordinate2D southWest = grid.GetCellCenter(grid.RowOrder == SolidGround.Core.Terrain.GridRowOrder.SouthToNorth ? 0 : grid.RowCount - 1, 0);
        Coordinate2D northEast = grid.GetCellCenter(grid.RowOrder == SolidGround.Core.Terrain.GridRowOrder.SouthToNorth ? grid.RowCount - 1 : 0, grid.ColumnCount - 1);
        double halfX = Math.Abs(grid.CellSizeX) / 2d;
        double halfY = Math.Abs(grid.CellSizeY) / 2d;
        var coordinates = new[]
        {
            new NetTopologySuite.Geometries.Coordinate(southWest.X - halfX, southWest.Y - halfY),
            new NetTopologySuite.Geometries.Coordinate(northEast.X + halfX, southWest.Y - halfY),
            new NetTopologySuite.Geometries.Coordinate(northEast.X + halfX, northEast.Y + halfY),
            new NetTopologySuite.Geometries.Coordinate(southWest.X - halfX, northEast.Y + halfY),
            new NetTopologySuite.Geometries.Coordinate(southWest.X - halfX, southWest.Y - halfY),
        };
        return PolygonalRegion.FromGeometry(factory.CreatePolygon(coordinates), grid.HorizontalReference);
    }

    private static bool TryParseSignedLength(string value, DistanceDisplayFormat format, out double result)
    {
        result = 0d;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            bool negative = value.TrimStart().StartsWith('-');
            string unsigned = negative ? value.TrimStart()[1..].TrimStart() : value.Trim();
            double metres = DistanceDisplayConverter.ParseMeters(unsigned, format);
            result = LengthConverter.Convert(metres, LengthUnit.Meter, RequireFormatUnit(format));
            if (negative) result = -result;
            return double.IsFinite(result);
        }
        catch (FormatException) { return false; }
    }

    private static bool TryParseNonnegativeLength(string value, DistanceDisplayFormat format, out double magnitude, out LengthUnit unit)
    {
        magnitude = 0d;
        unit = RequireFormatUnit(format);
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            double metres = DistanceDisplayConverter.ParseMeters(value.Trim(), format);
            magnitude = LengthConverter.Convert(metres, LengthUnit.Meter, unit);
            return double.IsFinite(magnitude) && magnitude >= 0d;
        }
        catch (FormatException) { return false; }
    }

    private static LengthUnit RequireFormatUnit(DistanceDisplayFormat format) => format switch
    {
        DistanceDisplayFormat.UsSurveyFeet => LengthUnit.UsSurveyFoot,
        DistanceDisplayFormat.InternationalFeet or DistanceDisplayFormat.FeetAndInches or DistanceDisplayFormat.InternationalInches => LengthUnit.InternationalFoot,
        DistanceDisplayFormat.Metres => LengthUnit.Meter,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private sealed record EditState(
        BuildingFloorReference? Reference,
        bool IsConfirmed,
        FloorReferenceMode Mode,
        string KnownElevationText,
        string KnownSourceDescription,
        bool KnownDatumAcknowledged,
        string MeasuredRiseText,
        bool BelowGrade,
        string CoordinateLatitudeText,
        string CoordinateLongitudeText,
        GroundPoint? SelectedPoint,
        ElevationGridSample? SelectedSample);
}
