using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolidGround.Core.Aois;
using SolidGround.Core.Hosting;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Units;
using SolidGround.Core.Workflow;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Dialog;

/// <summary>View-model for the three-stage, non-mutating guided creation dialog.</summary>
/// <remarks>
/// The view model never references the Revit API. Lookup requests are asynchronous and take immutable
/// <see cref="LocationParcelLookupTicket"/> values from <see cref="LocationParcelFlow"/>; late or cancelled
/// completions therefore cannot replace a newer input or source selection.
/// </remarks>
internal sealed partial class SolidGroundDialogViewModel : ObservableObject
{
    private readonly SolidGroundDialogInputs _inputs;
    private readonly LocationParcelFlow _flow = new();
    private IAddressGeocoder _geocoder;
    private AddressGeocoderProvider _geocoderProvider;
    private IParcelBoundarySource? _parcelSource;
    private double _nearbySearchRadiusMeters;
    private int _networkTimeoutSeconds;
    private long _credentialRevision;
    private long _operationRevision;
    private RevitSettings? _effectiveSettings;
    private AreaOfInterest? _explicitAreaOfInterest;
    private PreFetchEstimate? _cachedEstimate;
    private string _estimateSummary = string.Empty;
    private bool _addressWasGeocoded;
    private string? _geocodedAddress;
    private bool _suppressRadiusInputInvalidation;
    private bool _synchronizingLocationSelection;

    internal event EventHandler? CloseRequested;

    internal SolidGroundDialogViewModel(SolidGroundDialogInputs inputs)
    {
        _inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
        _effectiveSettings = inputs.Settings;
        _geocoder = inputs.Geocoder;
        _geocoderProvider = inputs.GeocoderProvider;
        _parcelSource = inputs.ParcelSource;
        _nearbySearchRadiusMeters = inputs.NearbySearchRadiusMeters;
        _networkTimeoutSeconds = inputs.NetworkTimeoutSeconds;
        _credentialRevision = inputs.InitialCredentialRevision;
        SelectedLevel = NamedElevationSelector.SelectLowestElevation(inputs.LevelCandidates, inputs.ConfiguredLevelName);
        SelectedToposolidType = NamedSelector.SelectFirstByOrdinalName(inputs.ToposolidTypeCandidates, inputs.ConfiguredToposolidTypeName);
        PointBudget = inputs.PrefilledPointBudget;
        SelectedOutputUnit = Enum.IsDefined(inputs.PrefilledOutputUnit) ? inputs.PrefilledOutputUnit : LengthConverter.DefaultOutputUnit;
        // This is deliberately a current-run decision, never restored from old settings.
        WriteSharedCoordinatesIfAbsent = false;
        RefreshEstimate();
    }

    public SolidGroundDialogStep CurrentStep => _flow.Stage switch
    {
        LocationParcelStage.Location => SolidGroundDialogStep.Location,
        LocationParcelStage.Parcel => SolidGroundDialogStep.Parcel,
        _ => SolidGroundDialogStep.Review,
    };

    public string StageAnnouncement => CurrentStep switch
    {
        SolidGroundDialogStep.Location => "Location, step 1 of 3",
        SolidGroundDialogStep.Parcel => "Parcel, step 2 of 3",
        _ => "Review, step 3 of 3",
    };

    public IReadOnlyList<NamedElevationCandidate> LevelCandidates => _inputs.LevelCandidates;
    public IReadOnlyList<NamedCandidate> ToposolidTypeCandidates => _inputs.ToposolidTypeCandidates;
    public IReadOnlyList<AddressGeocodeCandidate> GeocodeCandidates => _flow.LocationCandidates;
    /// <summary>The current resolved location, retained while a configured parcel source is replaced.</summary>
    public AddressGeocodeCandidate? ConfirmedLocation => _flow.SelectedLocation;
    /// <summary>The actual provider attribution for the selected address result, shown once that result is used.</summary>
    public string LocationAttribution => _addressWasGeocoded && ConfirmedLocation is { } location
        ? location.Attribution
        : string.Empty;
    public IReadOnlyList<ParcelProximityCandidate> ParcelCandidates => _flow.ParcelCandidates;
    public bool HasMultipleLocations => _flow.LocationRequiresConfirmation;
    public bool CanSelectLocation => CurrentStep == SolidGroundDialogStep.Parcel && _flow.LocationRequiresConfirmation;
    public bool ShowCoordinates => EntryMode == LocationEntryMode.Coordinates;
    public bool ShowOtherAreaOptions => EntryMode is LocationEntryMode.BoundingBox or LocationEntryMode.Radius or LocationEntryMode.LocalGeometry;
    public bool ShowAddress => EntryMode == LocationEntryMode.Address;
    public bool UsedNearbyTier => _flow.UsedNearbyTier;
    public string? NearbyTierNoticeText => UsedNearbyTier
        ? $"No parcel contains this point. Choose a nearby parcel explicitly (within {_nearbySearchRadiusMeters.ToString("N0", CultureInfo.InvariantCulture)} m)."
        : null;
    public string ContainmentLabel => SelectedParcelCandidate is null ? string.Empty :
        SelectedParcelCandidate.DistanceMeters == 0d ? "Contains the resolved location" :
        $"Nearby: {SelectedParcelCandidate.DistanceMeters.ToString("N1", CultureInfo.InvariantCulture)} m from the resolved location";
    public string SelectedParcelDetail => SelectedParcelCandidate is not { } selected ? string.Empty :
        DescribeParcel(selected);
    public string SelectedParcelSourceTerms => SelectedParcelCandidate is not { } selected ? string.Empty :
        $"Source: {selected.Candidate.SourceIdentity}. {selected.Candidate.LicenseDisclaimerText}";
    public string AccuracyDisclaimer => _inputs.ParcelSource is null
        ? ParcelBoundaryCandidate.NotASurveyDisclaimer
        : ParcelBoundaryCandidate.NotASurveyDisclaimer;
    public RevitSettings? EffectiveSettings => _effectiveSettings;
    public string SettingsSummary => _effectiveSettings is null ? "Settings were loaded for this run." :
        $"{_effectiveSettings.Request.Mode} · {PointBudget.ToString(CultureInfo.InvariantCulture)} points · {SelectedOutputUnit}";
    public string SourceSummary => EffectiveMode == TerrainAcquisitionMode.Fetch
        ? "USGS 1 m elevation through OpenTopography; Find does not request elevation. " + OpenTopographyUsgs1mSource.AttributionNotice
        : "Local elevation input will be read only after Create toposolid.";
    public string ExtensionSummary => _effectiveSettings is null
        ? "Terrain extension is a saved Settings preference and is read-only here."
        : $"Terrain extension: {TerrainExtensionDisplay}.";
    public double TerrainExtensionMeters => _effectiveSettings?.TerrainExtensionMeters ?? 0d;
    public string TerrainExtensionDisplay => DistanceDisplayConverter.FormatMeters(
        TerrainExtensionMeters, _effectiveSettings?.DistanceDisplayFormat ?? DistanceDisplayFormat.UsSurveyFeet);
    /// <summary>Cached estimate for the current confirmed AOI; it never performs planner work during binding reads.</summary>
    public string EstimateSummary => _estimateSummary;
    public string RadiusLabel => $"Radius ({RadiusDisplayUnit})";
    public string? NativePointBudgetWarning => RevitIniToposolidThresholds.ExceedsNativeThreshold(PointBudget, _inputs.RevitIniThresholds)
        ? RevitIniToposolidThresholds.DescribeExceedance(PointBudget, _inputs.RevitIniThresholds.NativeToposolidMaxPointThreshold!.Value, _inputs.RevitIniPath)
        : null;

    private TerrainAcquisitionMode EffectiveMode => _effectiveSettings?.Request.Mode ?? _inputs.Mode;

    // Deliberately internal and non-bound: #53's binding tests use it to prove they detect a real path
    // accessibility error, while the dialog's actual Binding(nameof(...)) properties remain public.
    internal string BindingPathErrorDiagnostic => ErrorText ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddress), nameof(ShowCoordinates), nameof(ShowOtherAreaOptions))]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private LocationEntryMode _entryMode = LocationEntryMode.Address;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _addressText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _latitudeText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand))]
    private string _longitudeText = string.Empty;

    // Other area fields intentionally accept content rather than a JSON settings-file path.
    [ObservableProperty] private string _westText = string.Empty;
    [ObservableProperty] private string _southText = string.Empty;
    [ObservableProperty] private string _eastText = string.Empty;
    [ObservableProperty] private string _northText = string.Empty;
    [ObservableProperty] private string _radiusMetersText = string.Empty;
    [ObservableProperty] private string _localGeometryText = string.Empty;
    [ObservableProperty] private string _localGeometryFormat = "GeoJSON or WKT";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseLocationCommand), nameof(UseParcelCommand), nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(ContainmentLabel), nameof(HasMultipleLocations), nameof(ConfirmedLocation))]
    private AddressGeocodeCandidate? _selectedGeocodeCandidate;

    partial void OnSelectedGeocodeCandidateChanged(AddressGeocodeCandidate? value)
    {
        if (_synchronizingLocationSelection || _flow.SelectedLocation is not { } committed || Equals(value, committed))
        {
            return;
        }

        _synchronizingLocationSelection = true;
        try
        {
            SelectedGeocodeCandidate = committed;
        }
        finally
        {
            _synchronizingLocationSelection = false;
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UseParcelCommand), nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(ContainmentLabel), nameof(SelectedParcelDetail), nameof(SelectedParcelSourceTerms))]
    private ParcelProximityCandidate? _selectedParcelCandidate;

    [ObservableProperty] private string? _errorText;
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindCommand), nameof(UseLocationCommand), nameof(UseParcelCommand), nameof(CreateCommand))]
    private bool _isBusy;
    [ObservableProperty] private NamedElevationCandidate? _selectedLevel;
    [ObservableProperty] private NamedCandidate? _selectedToposolidType;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsSummary), nameof(NativePointBudgetWarning))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private int _pointBudget;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SettingsSummary))]
    private LengthUnit _selectedOutputUnit;
    [ObservableProperty] private bool _writeSharedCoordinatesIfAbsent;
    [ObservableProperty] private SolidGroundDialogResult? _result;

    partial void OnAddressTextChanged(string value) => InvalidateInput();
    partial void OnLatitudeTextChanged(string value) => InvalidateInput();
    partial void OnLongitudeTextChanged(string value) => InvalidateInput();
    partial void OnEntryModeChanged(LocationEntryMode value) => InvalidateInput();
    partial void OnWestTextChanged(string value) => InvalidateInput();
    partial void OnSouthTextChanged(string value) => InvalidateInput();
    partial void OnEastTextChanged(string value) => InvalidateInput();
    partial void OnNorthTextChanged(string value) => InvalidateInput();
    partial void OnRadiusMetersTextChanged(string value)
    {
        if (!_suppressRadiusInputInvalidation)
        {
            InvalidateInput();
        }
    }
    partial void OnLocalGeometryTextChanged(string value) => InvalidateInput();
    partial void OnLocalGeometryFormatChanged(string value) => InvalidateInput();

    private void InvalidateInput()
    {
        FindCancelCommand.Execute(null);
        InvalidateCurrentOperation();
        _addressWasGeocoded = false;
        _geocodedAddress = null;
        _explicitAreaOfInterest = null;
        _flow.ChangeInput();
        SelectedGeocodeCandidate = null;
        SelectedParcelCandidate = null;
        RefreshEstimate();
        NotifyFlowChanged();
    }

    private bool CanFind() => !IsBusy && CurrentStep == SolidGroundDialogStep.Location;

    [RelayCommand(CanExecute = nameof(CanFind), IncludeCancelCommand = true, AllowConcurrentExecutions = true)]
    private async Task FindAsync(CancellationToken cancellationToken)
    {
        ErrorText = null;
        if (EntryMode is LocationEntryMode.BoundingBox or LocationEntryMode.Radius or LocationEntryMode.LocalGeometry)
        {
            if (string.IsNullOrWhiteSpace(LocalGeometryText) && EntryMode == LocationEntryMode.LocalGeometry)
            {
                ErrorText = "Paste GeoJSON or WKT geometry before using this area.";
                return;
            }

            if (!TryBuildExplicitArea(out AreaOfInterest? area, out string? error))
            {
                ErrorText = error;
                return;
            }

            _explicitAreaOfInterest = area;
            _flow.UseExplicitArea();
            RefreshEstimate();
            NotifyFlowChanged();
            return;
        }

        AddressGeocodeCandidate? directCandidate = TryGetDirectCoordinates(out string? validationError);
        if (validationError is not null)
        {
            ErrorText = validationError;
            return;
        }

        LocationParcelLookupTicket ticket = _flow.BeginLocationLookup();
        long operation = BeginOperation();
        try
        {
            IReadOnlyList<AddressGeocodeCandidate> candidates;
            if (directCandidate is not null)
            {
                candidates = [directCandidate];
            }
            else
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(_networkTimeoutSeconds));
                IAddressGeocoder geocoder = _geocoder;
                AddressGeocodeAcquisition acquisition = await geocoder
                    .GeocodeAsync(new AddressGeocodeRequest(AddressText.Trim()), timeout.Token)
                    .ConfigureAwait(true);
                candidates = acquisition.Candidates;
            }

            if (cancellationToken.IsCancellationRequested || !IsCurrentOperation(operation) || !_flow.TryApplyLocations(ticket, candidates))
            {
                return;
            }

            _addressWasGeocoded = directCandidate is null;
            _geocodedAddress = directCandidate is null ? AddressText.Trim() : null;

            SelectedGeocodeCandidate = _flow.SelectedLocation;
            if (candidates.Count == 0)
            {
                ErrorText = "No address matches were returned. Correct the address and try Find again.";
                NotifyFlowChanged();
                return;
            }

            NotifyFlowChanged();
            if (candidates.Count == 1)
            {
                await FindParcelsAsync(cancellationToken).ConfigureAwait(true);
            }
        }
        catch (AddressGeocoderException ex) { SetErrorIfCurrent(ticket, operation, ex.Message); }
        catch (OperationCanceledException) { SetErrorIfCurrent(ticket, operation, "The location lookup was cancelled."); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            SetErrorIfCurrent(ticket, operation, "The location lookup could not complete safely.");
        }
        finally
        {
            if (IsCurrentOperation(operation))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanUseLocation() => !IsBusy && CurrentStep == SolidGroundDialogStep.Parcel && SelectedGeocodeCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanUseLocation))]
    private async Task UseLocationAsync()
    {
        if (SelectedGeocodeCandidate is null || !_flow.TryUseLocation(SelectedGeocodeCandidate))
        {
            return;
        }

        SelectedParcelCandidate = null;
        NotifyFlowChanged();
        await FindParcelsAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private async Task FindParcelsAsync(CancellationToken cancellationToken)
    {
        IParcelBoundarySource? parcelSource = _parcelSource;
        if (parcelSource is null)
        {
            ErrorText = "No parcel source is configured. Select Settings to configure an authorized county service or a local boundary file.";
            return;
        }

        LocationParcelLookupTicket ticket = _flow.BeginParcelLookup();
        AddressGeocodeCandidate location = _flow.SelectedLocation!;
        long operation = BeginOperation();
        try
        {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(_networkTimeoutSeconds));
            double nearbySearchRadiusMeters = _nearbySearchRadiusMeters;
            ParcelProximityAcquisition acquisition = await NearbyParcelBoundaryFinder.FindAsync(
                parcelSource, location.Latitude, location.Longitude, nearbySearchRadiusMeters, timeout.Token).ConfigureAwait(true);
            if (!cancellationToken.IsCancellationRequested && IsCurrentOperation(operation) && _flow.TryApplyParcels(ticket, acquisition))
            {
                SelectedParcelCandidate = null;
                if (acquisition.Candidates.Count == 0)
                {
                    ErrorText = "No parcel boundary was found for this location. Try another address or coordinate, or use Settings to verify the configured parcel source.";
                }
                NotifyFlowChanged();
            }
        }
        catch (ParcelBoundarySourceException ex) { SetErrorIfCurrent(ticket, operation, ex.Message); }
        catch (OperationCanceledException) { SetErrorIfCurrent(ticket, operation, "The parcel lookup was cancelled."); }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            SetErrorIfCurrent(ticket, operation, "The parcel lookup could not complete safely.");
        }
        finally
        {
            if (IsCurrentOperation(operation))
            {
                IsBusy = false;
            }
        }
    }

    private bool CanUseParcel() => !IsBusy && CurrentStep == SolidGroundDialogStep.Parcel && SelectedParcelCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanUseParcel))]
    private void UseParcel()
    {
        if (SelectedParcelCandidate is not null && _flow.TryUseParcel(SelectedParcelCandidate))
        {
            RefreshEstimate();
            NotifyFlowChanged();
        }
    }

    [RelayCommand]
    private void Back()
    {
        FindCancelCommand.Execute(null);
        InvalidateCurrentOperation();
        if (CurrentStep == SolidGroundDialogStep.Review)
        {
            _flow.ReturnToParcel();
        }
        else if (CurrentStep == SolidGroundDialogStep.Parcel)
        {
            _flow.ReturnToLocation();
            SelectedGeocodeCandidate = null;
        }

        NotifyFlowChanged();
    }

    [RelayCommand]
    private void EditSettings()
    {
        if (_inputs.Settings is null || _inputs.EditSettings is null)
        {
            return;
        }

        RevitSettings? edited;
        try
        {
            edited = _inputs.EditSettings(_effectiveSettings ?? _inputs.Settings);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            ErrorText = "Settings could not be opened or applied safely. No dialog choices were changed.";
            return;
        }
        if (edited is null)
        {
            if (SessionApiKeyOverrides.Revision != _credentialRevision)
            {
                ReconfigureAfterSessionCredentialChange();
            }
            return;
        }

        RevitSettings previous = _effectiveSettings ?? _inputs.Settings;
        DistanceDisplayFormat previousDistanceFormat = previous.DistanceDisplayFormat;
        double? radiusMetersBeforeEdit = TryParseRadiusMeters(previousDistanceFormat);
        bool geocoderChanged = previous.AddressAndParcel.GeocoderProvider != edited.AddressAndParcel.GeocoderProvider;
        bool lookupConfigurationChanged = !Equals(previous.AddressAndParcel, edited.AddressAndParcel);
        SolidGroundDialogLookupServices services;
        try
        {
            services = _inputs.ReconfigureLookupServices?.Invoke(edited) ??
                new SolidGroundDialogLookupServices(_geocoder, _geocoderProvider, _parcelSource, _nearbySearchRadiusMeters, _networkTimeoutSeconds, _credentialRevision);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            ApplySettingsPresentation(edited, radiusMetersBeforeEdit);
            FindCancelCommand.Execute(null);
            InvalidateCurrentOperation();
            _flow.ChangeSource();
            SelectedParcelCandidate = null;
            RefreshEstimate();
            NotifyFlowChanged();
            ErrorText = "Settings were saved, but the updated lookup source could not be configured. Reopen Settings and correct the source before searching.";
            return;
        }
        lookupConfigurationChanged |= services.CredentialRevision != _credentialRevision;
        _geocoder = services.Geocoder;
        _geocoderProvider = services.GeocoderProvider;
        _parcelSource = services.ParcelSource;
        _nearbySearchRadiusMeters = services.NearbySearchRadiusMeters;
        _networkTimeoutSeconds = services.NetworkTimeoutSeconds;
        _credentialRevision = services.CredentialRevision;
        ApplySettingsPresentation(edited, radiusMetersBeforeEdit);
        if (lookupConfigurationChanged)
        {
            // The revision is the authority for non-cooperative requests. Cancellation is best-effort only.
            FindCancelCommand.Execute(null);
            InvalidateCurrentOperation();
            _flow.ChangeSource();
            bool mustRegeocode = geocoderChanged && _addressWasGeocoded &&
                !string.IsNullOrWhiteSpace(_geocodedAddress) && _flow.SelectedLocation is not null;
            if (mustRegeocode)
            {
                EntryMode = LocationEntryMode.Address;
                AddressText = _geocodedAddress!;
                // EntryMode/AddressText invalidate the old provider snapshot. The new provider must produce
                // the candidate that later reaches provenance; retaining the old candidate would misstate it.
                _ = FindAsync(CancellationToken.None);
            }
            SelectedParcelCandidate = null;
            RefreshEstimate();
            if (!mustRegeocode && _flow.SelectedLocation is not null)
            {
                _ = FindParcelsAsync(CancellationToken.None);
            }
        }
        NotifyFlowChanged();
    }

    private void ReconfigureAfterSessionCredentialChange()
    {
        RevitSettings settings = _effectiveSettings ?? _inputs.Settings!;
        SolidGroundDialogLookupServices services;
        try
        {
            services = _inputs.ReconfigureLookupServices?.Invoke(settings) ??
                new SolidGroundDialogLookupServices(
                    _geocoder, _geocoderProvider, _parcelSource, _nearbySearchRadiusMeters, _networkTimeoutSeconds,
                    SessionApiKeyOverrides.Revision);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            FindCancelCommand.Execute(null);
            InvalidateCurrentOperation();
            _flow.ChangeSource();
            SelectedParcelCandidate = null;
            RefreshEstimate();
            NotifyFlowChanged();
            ErrorText = "A session credential changed, but updated lookup services could not be configured. Open Settings and correct the source before searching.";
            return;
        }

        _geocoder = services.Geocoder;
        _geocoderProvider = services.GeocoderProvider;
        _parcelSource = services.ParcelSource;
        _nearbySearchRadiusMeters = services.NearbySearchRadiusMeters;
        _networkTimeoutSeconds = services.NetworkTimeoutSeconds;
        _credentialRevision = SessionApiKeyOverrides.Revision;
        FindCancelCommand.Execute(null);
        InvalidateCurrentOperation();
        _flow.ChangeSource();
        SelectedParcelCandidate = null;
        RefreshEstimate();
        if (_flow.SelectedLocation is not null)
        {
            _ = FindParcelsAsync(CancellationToken.None);
        }
        NotifyFlowChanged();
    }

    private bool CanCreate() => !IsBusy && CurrentStep == SolidGroundDialogStep.Review && SelectedLevel is not null && SelectedToposolidType is not null &&
        PointBudget is >= SimplificationSettings.MinPointBudget and <= SimplificationSettings.MaxPointBudget &&
        !RevitIniToposolidThresholds.ExceedsNativeThreshold(PointBudget, _inputs.RevitIniThresholds) &&
        (_flow.SelectedParcel is not null || _explicitAreaOfInterest is not null);

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        if (SelectedLevel is null || SelectedToposolidType is null)
        {
            return;
        }

        AreaOfInterest? aoi = null;
        AddressParcelProvenance? provenance = null;
        DialogAoiSource source = DialogAoiSource.UseSettingsFile;
        if (_flow.SelectedParcel is { } parcel)
        {
            source = DialogAoiSource.FindParcel;
            aoi = ParcelBoundaryAoiFactory.FromCandidate(parcel.Candidate, LinearDistance.Meters(0));
            provenance = AddressParcelProvenanceFactory.Create(
                DateOnly.FromDateTime(DateTime.UtcNow), _addressWasGeocoded, _geocoderProvider, _geocodedAddress,
                _addressWasGeocoded ? _flow.SelectedLocation : null, parcel.Candidate);
        }
        else if (_explicitAreaOfInterest is not null)
        {
            source = DialogAoiSource.ExplicitArea;
            aoi = _explicitAreaOfInterest;
        }

        Result = new SolidGroundDialogResult(source, aoi, SelectedLevel, SelectedToposolidType, SelectedOutputUnit,
            PointBudget, WriteSharedCoordinatesIfAbsent, provenance, _effectiveSettings);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        FindCancelCommand.Execute(null);
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private AddressGeocodeCandidate? TryGetDirectCoordinates(out string? error)
    {
        error = null;
        string pair = EntryMode == LocationEntryMode.Coordinates ? $"{LatitudeText},{LongitudeText}" : AddressText;
        if (LatitudeLongitudePointParser.TryParse(pair, out double latitude, out double longitude))
        {
            return new AddressGeocodeCandidate(latitude, longitude,
                latitude.ToString("F6", CultureInfo.InvariantCulture) + ", " + longitude.ToString("F6", CultureInfo.InvariantCulture),
                "Coordinates entered directly by the operator; not resolved through an address provider.");
        }

        if (EntryMode == LocationEntryMode.Coordinates || LatitudeLongitudePointParser.LooksLikeAttemptedCoordinatePair(pair))
        {
            error = LatitudeLongitudePointParser.CoordinatePairOutOfRangeMessage;
        }
        else if (string.IsNullOrWhiteSpace(AddressText))
        {
            error = "Enter a street address or select Coordinates.";
        }

        return null;
    }

    private bool TryBuildExplicitArea(out AreaOfInterest? area, out string? error)
    {
        area = null;
        error = null;
        try
        {
            switch (EntryMode)
            {
                case LocationEntryMode.BoundingBox:
                    area = new Wgs84BoundingBoxAoi(
                        ParseNumber(WestText, "West"), ParseNumber(SouthText, "South"),
                        ParseNumber(EastText, "East"), ParseNumber(NorthText, "North"));
                    return true;
                case LocationEntryMode.Radius:
                    area = new Wgs84RadiusAoi(
                        ParseNumber(LatitudeText, "Latitude"), ParseNumber(LongitudeText, "Longitude"),
                        LinearDistance.Meters(ParseRadiusMeters()));
                    return true;
                case LocationEntryMode.LocalGeometry:
                    ParcelGeometryFormat format = LocalGeometryText.TrimStart().StartsWith('{')
                        ? ParcelGeometryFormat.GeoJson : ParcelGeometryFormat.Wkt;
                    ParcelGeometryAoi polygon = new(format, LocalGeometryText, Wgs84Reference, LinearDistance.Zero);
                    _ = ParcelGeometryParser.Parse(polygon);
                    area = polygon;
                    return true;
                default:
                    error = "Choose BoundingBox, Radius, or LocalGeometry before using another area.";
                    return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static double ParseNumber(string text, string label)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
        {
            throw new FormatException($"{label} must be a finite number.");
        }

        return value;
    }

    private long BeginOperation()
    {
        long operation = Interlocked.Increment(ref _operationRevision);
        IsBusy = true;
        return operation;
    }

    private void InvalidateCurrentOperation()
    {
        Interlocked.Increment(ref _operationRevision);
        IsBusy = false;
    }

    private bool IsCurrentOperation(long operation) => operation == Interlocked.Read(ref _operationRevision);

    private void SetErrorIfCurrent(LocationParcelLookupTicket ticket, long operation, string message)
    {
        if (IsCurrentOperation(operation) && _flow.IsCurrent(ticket))
        {
            ErrorText = message;
        }
    }

    private void ApplySettingsPresentation(RevitSettings edited, double? radiusMetersBeforeEdit)
    {
        _effectiveSettings = edited;
        if (radiusMetersBeforeEdit is { } radiusMeters)
        {
            _suppressRadiusInputInvalidation = true;
            try
            {
                RadiusMetersText = DistanceDisplayConverter.FormatMeters(radiusMeters, edited.DistanceDisplayFormat);
            }
            finally
            {
                _suppressRadiusInputInvalidation = false;
            }
        }

        PointBudget = edited.Request.Simplification.PointBudget;
        SelectedOutputUnit = edited.Request.OutputUnit;
        OnPropertyChanged(nameof(EffectiveSettings));
        OnPropertyChanged(nameof(SettingsSummary));
        OnPropertyChanged(nameof(SourceSummary));
        OnPropertyChanged(nameof(ExtensionSummary));
        OnPropertyChanged(nameof(TerrainExtensionMeters));
        OnPropertyChanged(nameof(TerrainExtensionDisplay));
        OnPropertyChanged(nameof(RadiusLabel));
        RefreshEstimate();
        OnPropertyChanged(nameof(NativePointBudgetWarning));
    }

    private static readonly HorizontalReference Wgs84Reference = new(
        "WGS 84", "World Geodetic System 1984", HorizontalReferenceKind.Geographic,
        HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private void RefreshEstimate()
    {
        _cachedEstimate = null;
        AreaOfInterest? area = CurrentAreaOfInterest();
        if (area is null)
        {
            _estimateSummary = EffectiveMode == TerrainAcquisitionMode.Fetch
                ? "Confirm an area to estimate the OpenTopography fetch envelope."
                : "Confirm an area to estimate the local terrain envelope; no raster is read and no HTTP request is made.";
            OnPropertyChanged(nameof(EstimateSummary));
            return;
        }

        try
        {
            LinearDistance? terrainMargin = area is ParcelGeometryAoi ? LinearDistance.Meters(TerrainExtensionMeters) : null;
            _cachedEstimate = EffectiveMode == TerrainAcquisitionMode.Fetch
                ? PreFetchEstimator.FromAreaOfInterest(area, terrainMargin)
                : PreFetchEstimator.FromProcessAreaOfInterest(area, terrainMargin);
            _estimateSummary = DescribeEstimate(_cachedEstimate);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            _estimateSummary = "The current area could not be estimated: " + ex.Message;
        }

        OnPropertyChanged(nameof(EstimateSummary));
    }

    private static string DescribeEstimate(PreFetchEstimate estimate) =>
        $"Estimated {estimate.ApproximateOneMeterSamples.ToString("N0", CultureInfo.InvariantCulture)} one-metre samples across " +
        $"{estimate.EnvelopeSquareMeters.ToString("N0", CultureInfo.InvariantCulture)} m². {estimate.Label}";

    private static string DescribeParcel(ParcelProximityCandidate selected)
    {
        ParcelBoundaryCandidate candidate = selected.Candidate;
        string description = $"Parcel {candidate.ParcelId} · {candidate.ComputedAreaSquareMeters.ToString("N0", CultureInfo.InvariantCulture)} m² · " +
            (selected.DistanceMeters == 0d
                ? "Contains the resolved location"
                : $"Nearby: {selected.DistanceMeters.ToString("N1", CultureInfo.InvariantCulture)} m from the resolved location");

        if (candidate.LegalDescription is { } legalDescription)
        {
            description += " · Legal description: " + legalDescription;
        }

        return description;
    }

    private AreaOfInterest? CurrentAreaOfInterest() => _explicitAreaOfInterest ??
        (_flow.SelectedParcel is { } parcel ? ParcelBoundaryAoiFactory.FromCandidate(parcel.Candidate, LinearDistance.Zero) : null);

    private double ParseRadiusMeters() => DistanceDisplayConverter.ParseMeters(RadiusMetersText, EffectiveDistanceDisplayFormat);

    private double? TryParseRadiusMeters(DistanceDisplayFormat format)
    {
        if (string.IsNullOrWhiteSpace(RadiusMetersText)) return null;
        try { return DistanceDisplayConverter.ParseMeters(RadiusMetersText, format); }
        catch (FormatException) { return null; }
    }

    private DistanceDisplayFormat EffectiveDistanceDisplayFormat => _effectiveSettings?.DistanceDisplayFormat ?? DistanceDisplayFormat.Metres;

    private string RadiusDisplayUnit => EffectiveDistanceDisplayFormat switch
    {
        DistanceDisplayFormat.UsSurveyFeet => "US survey ft",
        DistanceDisplayFormat.InternationalFeet => "international ft",
        DistanceDisplayFormat.InternationalInches => "in",
        DistanceDisplayFormat.FeetAndInches => "ft/in",
        _ => "m",
    };

    private void NotifyFlowChanged()
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(StageAnnouncement));
        OnPropertyChanged(nameof(GeocodeCandidates));
        OnPropertyChanged(nameof(ConfirmedLocation));
        OnPropertyChanged(nameof(LocationAttribution));
        OnPropertyChanged(nameof(ParcelCandidates));
        OnPropertyChanged(nameof(HasMultipleLocations));
        OnPropertyChanged(nameof(CanSelectLocation));
        OnPropertyChanged(nameof(UsedNearbyTier));
        OnPropertyChanged(nameof(NearbyTierNoticeText));
        UseLocationCommand.NotifyCanExecuteChanged();
        UseParcelCommand.NotifyCanExecuteChanged();
        CreateCommand.NotifyCanExecuteChanged();
        FindCommand.NotifyCanExecuteChanged();
    }
}
