using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SolidGround.Core.Aois;
using SolidGround.Core.Hosting;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// View-model for the interactive Revit-host dialog (SolidGround Issue #31, PH3-4: see
/// docs/architecture/revit-interactive-dialog.md). A plain <see cref="ObservableObject"/> subclass -- never
/// <see langword="ObservableRecipient"/>; no <c>IMessenger</c>/<c>WeakReferenceMessenger</c> anywhere in this
/// feature (see "MVVM shape (and why no messenger)") -- so it never depends on the Revit API at all: every
/// input arrives through <see cref="SolidGroundDialogInputs"/> as a plain Core type or primitive, and every
/// decision this type makes calls only Core types (<see cref="NamedElevationSelector"/>,
/// <see cref="NamedSelector"/>, <see cref="LinearDistance"/>, <see cref="RevitIniToposolidThresholds"/>,
/// <see cref="ParcelBoundaryAoiFactory"/>, <see cref="AddressParcelProvenanceFactory"/>,
/// <see cref="LatitudeLongitudePointParser"/>, <see cref="SimplificationSettings"/>). This type itself never
/// references the Revit API; within this feature, only <see cref="SolidGroundDialog"/> (which reads
/// <c>UIThemeManager.CurrentTheme</c> once, at
/// construction) and its <see cref="DialogTheme"/> helper are Revit-API-typed.
/// </summary>
/// <remarks>
/// Stage D wires this type in (review finding, minor, fixed): <see cref="SolidGroundDialogHost.ShowModal"/>
/// constructs it and shows <see cref="SolidGroundDialog"/> modally from
/// <c>CreateToposolidCommand.ExecuteCore</c>'s Stage 0.5, after Stage 0 (<c>LoadDocumentAndSettings</c>) and
/// before Stage 1 Preflight -- see docs/architecture/revit-interactive-dialog.md's "Result-code mapping". It
/// is therefore reachable from the ribbon via <c>CreateToposolidCommand.Execute</c>.
/// </remarks>
internal sealed partial class SolidGroundDialogViewModel : ObservableObject
{
    private readonly SolidGroundDialogInputs _inputs;

    /// <summary>
    /// True only when <see cref="SelectedGeocodeCandidate"/> came from an actual
    /// <see cref="IAddressGeocoder.GeocodeAsync"/> call; false when the operator entered a "latitude,
    /// longitude" pair directly (<see cref="LatitudeLongitudePointParser"/>), so
    /// <see cref="AddressParcelProvenanceFactory.Create"/> never attributes a real geocoding provider to a
    /// point nobody actually geocoded (docs/architecture/revit-interactive-dialog.md "AOI and provenance:
    /// two AOI paths, dialog-resolved or settings-driven"). Exposed to the view through
    /// <see cref="AddressWasGeocoded"/>; changes are manually notified by
    /// <see cref="NotifyAddressWasGeocodedChanged"/>.
    /// </summary>
    private bool _addressWasGeocoded;

    /// <summary>The address text actually submitted to <see cref="SolidGroundDialogInputs.Geocoder"/>, or null when <see cref="_addressWasGeocoded"/> is false.</summary>
    private string? _geocodedAddressText;

    /// <summary>
    /// The trimmed address text that actually produced whichever candidate is currently confirmed in
    /// <see cref="SelectedGeocodeCandidate"/> -- set alongside both success branches inside <see cref="Geocode"/>
    /// (a real geocode, or a validated direct "latitude, longitude" entry) -- compared against the live
    /// <see cref="AddressText"/> by <see cref="CanGoNext"/>'s <see cref="SolidGroundDialogStep.AddressEntry"/>
    /// case (review finding, blocker: editing <see cref="AddressText"/> after confirming a candidate, without a
    /// fresh successful <see cref="Geocode"/>, previously left the stale candidate/parcel pairing reachable all
    /// the way to <see cref="Create"/>). Deliberately a separate field from <see cref="_geocodedAddressText"/>,
    /// which is left <see langword="null"/> on the direct-point-entry path by contract -- reusing it here would
    /// wrongly keep Next disabled forever after a valid direct-point confirmation. Null before any candidate has
    /// ever been confirmed.
    /// </summary>
    /// <remarks>
    /// Live-session finding (docs/architecture/revit-interactive-dialog.md "Stage E live-session findings and
    /// Stage F fixes", defect A): as a bare field, this needed some *other* notifying property's setter to
    /// re-evaluate <c>NextCommand</c> for it, and both of <see cref="Geocode"/>'s success branches happened to
    /// assign <see cref="SelectedGeocodeCandidate"/> (which does re-evaluate <c>NextCommand</c> via its own
    /// <c>NotifyCanExecuteChangedFor</c>) *before* assigning this field -- so <c>NextCommand</c> was
    /// re-evaluated one statement too early, while this field still held its previous (stale) value, and
    /// nothing re-evaluated it again afterward. Next stayed disabled even once every real precondition was
    /// satisfied. Now an <c>[ObservableProperty]</c> in its own right, with its own
    /// <c>NotifyCanExecuteChangedFor(nameof(NextCommand))</c>: whichever success branch assigns it (in whatever
    /// order relative to <see cref="SelectedGeocodeCandidate"/>) re-evaluates <c>NextCommand</c> itself, at the
    /// point both pieces of state are already final -- structurally, not by depending on statement order.
    /// </remarks>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string? _confirmedAddressText;

    /// <summary>
    /// This dialog's fixed step order when <see cref="AoiSource"/> is <see cref="DialogAoiSource.FindParcel"/>:
    /// every one of the ten originally designed sections.
    /// </summary>
    private static readonly SolidGroundDialogStep[] FindParcelStepOrder =
    [
        SolidGroundDialogStep.AoiSourceChoice,
        SolidGroundDialogStep.AddressEntry,
        SolidGroundDialogStep.GeocodeCandidates,
        SolidGroundDialogStep.ParcelCandidates,
        SolidGroundDialogStep.Buffer,
        SolidGroundDialogStep.PointBudget,
        SolidGroundDialogStep.UnitChoice,
        SolidGroundDialogStep.LevelAndToposolidType,
        SolidGroundDialogStep.SharedCoordinatesOptIn,
        SolidGroundDialogStep.ProvenancePreview,
        SolidGroundDialogStep.PreflightSummary,
    ];

    /// <summary>
    /// This dialog's fixed step order when <see cref="AoiSource"/> is <see cref="DialogAoiSource.UseSettingsFile"/>:
    /// address entry, geocode candidates, parcel candidates, and buffer (steps 1-4) never apply, since the run's
    /// area of interest is not being resolved interactively at all.
    /// </summary>
    private static readonly SolidGroundDialogStep[] SettingsFileStepOrder =
    [
        SolidGroundDialogStep.AoiSourceChoice,
        SolidGroundDialogStep.PointBudget,
        SolidGroundDialogStep.UnitChoice,
        SolidGroundDialogStep.LevelAndToposolidType,
        SolidGroundDialogStep.SharedCoordinatesOptIn,
        SolidGroundDialogStep.ProvenancePreview,
        SolidGroundDialogStep.PreflightSummary,
    ];

    /// <summary>
    /// The one, small, clearly named place this dialog's AOI-source choice (owner decision 1, implemented
    /// with its recommended default) actually changes the flow -- everything else in this view-model
    /// navigates by index into whichever of the two fixed arrays above this property returns, so changing the
    /// skip rule later means editing only this property and the two arrays above it.
    /// </summary>
    internal SolidGroundDialogStep[] ActiveStepOrder =>
        AoiSource == DialogAoiSource.FindParcel ? FindParcelStepOrder : SettingsFileStepOrder;

    /// <summary>Raised exactly once, by <see cref="Create"/> or <see cref="Cancel"/>, telling the view to close itself.</summary>
    internal event EventHandler? CloseRequested;

    /// <summary>Exposed read-only so <see cref="SolidGroundDialog"/> can render display-only text (for example the settings-file AOI summary) without duplicating these values as separate bound properties.</summary>
    internal SolidGroundDialogInputs Inputs => _inputs;

    /// <summary>True once <see cref="FindParcel"/> has completed a lookup that returned zero candidates -- lets the view show docs/architecture/revit-interactive-dialog.md "Content model and sections" step 3's "no parcel boundary was found" message only after a real attempt, never merely because the operator has not searched yet.</summary>
    private bool _parcelLookupAttempted;

    /// <summary>See <see cref="_parcelLookupAttempted"/>. Manually notified from <see cref="FindParcel"/> (not itself an <c>[ObservableProperty]</c>, since <see cref="_parcelLookupAttempted"/> is plain private state, not bound directly).</summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> (live-session finding, fixed;
    /// docs/architecture/revit-interactive-dialog.md "Stage E live-session findings and Stage F fixes", defect
    /// C): <see cref="SolidGroundDialog"/> reaches this (and every other hand-written computed property this
    /// same fix touches) only through a string-Path <c>System.Windows.Data.Binding</c>, which WPF resolves
    /// against this (<see langword="internal"/>) class's runtime instance via reflection -- and that
    /// resolution only ever discovers <see langword="public"/> members, never <see langword="internal"/> ones,
    /// even though the *enclosing type* being <see langword="internal"/> is irrelevant to it. An
    /// <see langword="internal"/> property bound this way fails silently (a "BindingExpression path error:
    /// '...' property not found" trace line, never surfaced to the operator) and the bound dependency property
    /// is left at its own default -- <see cref="System.Windows.Visibility.Visible"/> for a
    /// <c>Visibility</c> binding (this property's own case: the zero-candidates message stayed visible
    /// unconditionally), or an empty string for a <c>Text</c> binding (see
    /// <see cref="PointBudgetWarningText"/>/<see cref="PointBudgetRangeErrorText"/>'s own remarks). A
    /// standalone, non-committed WPF reproduction outside this repository confirmed both failure modes and
    /// confirmed an otherwise-identical <see langword="public"/> property updates correctly; this test project
    /// cannot construct a real <see cref="SolidGroundDialogViewModel"/>/WPF <c>Binding</c> itself (see "Tests"),
    /// so <c>SolidGroundDialogViewModelPropertiesReferencedByAWpfBindingAreAllPublicNotInternal</c> instead
    /// asserts, from source text, that every property named in a <see cref="SolidGroundDialog"/>
    /// <c>new Binding(nameof(...))</c> call is declared <see langword="public"/> here.
    /// </remarks>
    public bool ShowNoParcelCandidatesMessage => _parcelLookupAttempted && ParcelCandidates.Count == 0;

    /// <summary>
    /// Non-null exactly when <see cref="FindParcel"/>'s most recent lookup fell back to the nearby-parcel
    /// tier (SolidGround Issue #31 follow-up: the exact point-in-parcel query found nothing, most often
    /// because a geocoded point landed a few meters into the street frontage or right-of-way) and that
    /// fallback tier found at least one candidate -- names the actual configured search radius
    /// (<see cref="SolidGroundDialogInputs.NearbySearchRadiusMeters"/>), never a re-hardcoded "30", so a
    /// configured override is reflected here too. <see langword="null"/> both before any lookup and whenever
    /// the exact tier alone already resolved a candidate (<see cref="UsedNearbyTier"/> false) or the nearby
    /// tier itself found nothing (<see cref="ShowNoParcelCandidatesMessage"/> covers that case instead).
    /// </summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> -- a hand-written computed property bound by a
    /// WPF string-Path <c>Binding</c> (see <see cref="ShowNoParcelCandidatesMessage"/>'s own remarks for the
    /// full mechanism this repeats).
    /// </remarks>
    public string? NearbyTierNoticeText => UsedNearbyTier && ParcelCandidates.Count > 0
        ? $"No parcel contains this point. These nearby parcels are within " +
          $"{_inputs.NearbySearchRadiusMeters.ToString("N0", CultureInfo.InvariantCulture)} m, nearest first; confirm the right one." +
          (ResultSetTruncated
              ? " The parcel source reported more nearby matches than it returned, so the closest parcel might not be listed here."
              : string.Empty)
        : null;

    internal SolidGroundDialogViewModel(SolidGroundDialogInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        _inputs = inputs;

        // Never LengthUnit.Meter (AGENTS.md's two documented foot definitions only); a settings file
        // configured with Meter falls back to UsSurveyFoot here, same as the design's own documented fallback.
        _selectedOutputUnit = inputs.PrefilledOutputUnit == LengthUnit.InternationalFoot
            ? LengthUnit.InternationalFoot
            : LengthUnit.UsSurveyFoot;
        _pointBudget = inputs.PrefilledPointBudget;

        // Disabled-and-forced-off when the document already looks coordinated -- the operator cannot enable
        // what SharedCoordinatesCheckboxEnabled reports as disabled (see the panel builder for the inline
        // explanatory text this pairs with).
        _writeSharedCoordinatesIfAbsent = !inputs.DocumentAlreadyHasSharedCoordinates && inputs.PrefilledWriteSharedCoordinatesIfAbsent;

        _selectedLevel = NamedElevationSelector.SelectLowestElevation(inputs.LevelCandidates, inputs.ConfiguredLevelName);
        _selectedToposolidType = NamedSelector.SelectFirstByOrdinalName(inputs.ToposolidTypeCandidates, inputs.ConfiguredToposolidTypeName);
    }

    // ------------------------------------------------------------------------------------------------------
    // Bound state
    // ------------------------------------------------------------------------------------------------------

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(CreateCommand))]
    private SolidGroundDialogStep _currentStep = SolidGroundDialogStep.AoiSourceChoice;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand))]
    [NotifyPropertyChangedFor(nameof(ShowFindParcelGeocodedIntro), nameof(ShowFindParcelDirectPointIntro))]
    private DialogAoiSource _aoiSource = DialogAoiSource.FindParcel;

    // NotifyCanExecuteChangedFor also names NextCommand (review finding, blocker's own recommended companion
    // fix): CanGoNext's AddressEntry case now also compares AddressText against ConfirmedAddressText, so Next's
    // enabled state must be re-evaluated as the operator types, not only at the next unrelated command
    // re-evaluation.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GeocodeCommand), nameof(NextCommand))]
    private string _addressText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GeocodeCommand), nameof(FindParcelCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorText;

    [ObservableProperty]
    private ObservableCollection<AddressGeocodeCandidate> _geocodeCandidates = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(FindParcelCommand))]
    private AddressGeocodeCandidate? _selectedGeocodeCandidate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNoParcelCandidatesMessage), nameof(NearbyTierNoticeText))]
    private ObservableCollection<ParcelProximityCandidate> _parcelCandidates = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private ParcelProximityCandidate? _selectedParcelCandidate;

    /// <summary>
    /// True once <see cref="FindParcel"/> has completed a lookup whose exact point-in-parcel tier returned
    /// zero candidates, so the nearby-parcel fallback tier ran (SolidGround Issue #31 follow-up; see
    /// <see cref="SolidGround.Core.Sources.NearbyParcelBoundaryFinder"/>) -- regardless of whether that
    /// fallback tier itself then found anything within its own search radius. Reset to <see langword="false"/>
    /// whenever <see cref="SelectedGeocodeCandidate"/> changes, alongside <see cref="ParcelCandidates"/> itself
    /// (<see cref="OnSelectedGeocodeCandidateChanged"/>), so a stale notice from an earlier confirmed point can
    /// never linger for a search that has not run yet against the newly confirmed one.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NearbyTierNoticeText))]
    private bool _usedNearbyTier;

    /// <summary>
    /// True when <see cref="FindParcel"/>'s most recent lookup's own <see cref="ParcelProximityAcquisition.ResultSetTruncated"/>
    /// was true -- the source's own paging/limit signal (Esri's <c>exceededTransferLimit</c>) indicating more
    /// matches may exist than were actually returned for that lookup, so <see cref="ParcelCandidates"/> may be
    /// an incomplete subset of what the source actually has nearby (re-check finding, minor, fixed: this signal
    /// used to be discarded entirely -- see <see cref="ParcelProximityAcquisition.ResultSetTruncated"/>'s own
    /// remarks). Surfaced as a caveat appended to <see cref="NearbyTierNoticeText"/>, not a separate bound
    /// property, following that property's own established idiom. Reset to <see langword="false"/> alongside
    /// <see cref="UsedNearbyTier"/> whenever <see cref="SelectedGeocodeCandidate"/> changes, so a stale
    /// truncation caveat from an earlier lookup can never linger for a search that has not yet run against the
    /// newly confirmed point.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NearbyTierNoticeText))]
    private bool _resultSetTruncated;

    [ObservableProperty]
    private double _bufferMeters;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string? _bufferErrorText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPointBudgetWarning))]
    [NotifyPropertyChangedFor(nameof(PointBudgetWarningText))]
    [NotifyPropertyChangedFor(nameof(PointBudgetRangeErrorText))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private int _pointBudget;

    [ObservableProperty]
    private LengthUnit _selectedOutputUnit;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private NamedElevationCandidate? _selectedLevel;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private NamedCandidate? _selectedToposolidType;

    [ObservableProperty]
    private bool _writeSharedCoordinatesIfAbsent;

    /// <summary>Non-null only after <see cref="Create"/> runs; the caller reads this once <see cref="CloseRequested"/> fires.</summary>
    [ObservableProperty]
    private SolidGroundDialogResult? _result;

    // ------------------------------------------------------------------------------------------------------
    // Read-only, computed state (never itself an [ObservableProperty]; each depends on an
    // [ObservableProperty] above that already lists it under [NotifyPropertyChangedFor])
    // ------------------------------------------------------------------------------------------------------

    internal IReadOnlyList<NamedElevationCandidate> Levels => _inputs.LevelCandidates;

    internal IReadOnlyList<NamedCandidate> ToposolidTypes => _inputs.ToposolidTypeCandidates;

    internal bool SharedCoordinatesCheckboxEnabled => !_inputs.DocumentAlreadyHasSharedCoordinates;

    /// <summary>
    /// Read once from settings at dialog-construction time (<see cref="SolidGroundDialogInputs.Mode"/>) and
    /// never changed afterward, so unlike <see cref="AoiSource"/> it needs no change notification.
    /// </summary>
    internal TerrainAcquisitionMode Mode => _inputs.Mode;

    /// <summary>
    /// True exactly when this run is <see cref="TerrainAcquisitionMode.Fetch"/> -- gates the provenance-preview
    /// panel's OpenTopography attribution <c>TextBlock</c>, mutually exclusive with
    /// <see cref="ShowProcessModeSourceNote"/>. See
    /// docs/architecture/source-licensing-and-attribution.md's "Dialog: attribution shown once per run" section.
    /// </summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> -- a hand-written computed property bound by a
    /// WPF string-Path <c>Binding</c> (see <see cref="ShowNoParcelCandidatesMessage"/>'s own remarks for the
    /// full mechanism this repeats).
    /// </remarks>
    public bool ShowFetchModeSourceAttribution => Mode == TerrainAcquisitionMode.Fetch;

    /// <summary>
    /// True exactly when this run is <see cref="TerrainAcquisitionMode.Process"/> -- gates the
    /// provenance-preview panel's fixed, conditional process-mode sentence. See
    /// <see cref="ShowFetchModeSourceAttribution"/>.
    /// </summary>
    /// <remarks><see langword="public"/>, not <see langword="internal"/> -- see <see cref="ShowFetchModeSourceAttribution"/>'s own remarks.</remarks>
    public bool ShowProcessModeSourceNote => Mode == TerrainAcquisitionMode.Process;

    /// <summary>
    /// Visible exactly when <see cref="PointBudget"/> exceeds this machine's own configured
    /// <c>NativeToposolidMaxPointThreshold</c> -- the same rule <c>CreateToposolidCommand</c>'s Preflight
    /// guard applies (<see cref="RevitIniToposolidThresholds.ExceedsNativeThreshold"/>), so ignoring this
    /// warning and proceeding anyway always leads to the identical predicted Preflight rejection.
    /// </summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> (live-session finding, fixed; defect D -- both
    /// this step 5 binding and its step 10 preflight-summary sibling never showed the warning at all, since the
    /// bound <c>Visibility</c> silently stuck at its own default: see <see cref="ShowNoParcelCandidatesMessage"/>'s
    /// remarks for the full mechanism).
    /// </remarks>
    public bool ShowPointBudgetWarning => RevitIniToposolidThresholds.ExceedsNativeThreshold(PointBudget, _inputs.RevitIniThresholds);

    /// <summary>The exact sentence Preflight's own rejection uses (<see cref="RevitIniToposolidThresholds.DescribeExceedance"/>), or null when <see cref="ShowPointBudgetWarning"/> is false.</summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> (live-session finding, fixed; defect D): unlike
    /// a <c>Visibility</c> binding's silent fallback to <see cref="System.Windows.Visibility.Visible"/>, this
    /// property's own bound <c>TextBlock.Text</c> fell back to an empty string while <see langword="internal"/>
    /// -- indistinguishable, to an operator, from "no warning applies" -- which is exactly why the live session
    /// described this as the warning text never appearing at all, not as it appearing when it should not (see
    /// <see cref="ShowNoParcelCandidatesMessage"/>'s remarks for the full mechanism).
    /// </remarks>
    public string? PointBudgetWarningText => _inputs.RevitIniThresholds.NativeToposolidMaxPointThreshold is { } native && ShowPointBudgetWarning
        ? RevitIniToposolidThresholds.DescribeExceedance(PointBudget, native, _inputs.RevitIniPath)
        : null;

    /// <summary>
    /// Non-null exactly when <see cref="PointBudget"/> falls outside <see cref="SimplificationSettings.MinPointBudget"/>/
    /// <see cref="SimplificationSettings.MaxPointBudget"/>'s own inclusive bound -- the identical bound
    /// <c>TerrainRequestSettings.Validate()</c> already enforces for every settings-file-sourced value. Unlike
    /// <see cref="PointBudgetWarningText"/> (this machine's own <c>Revit.ini</c>-native threshold, tolerant-by-
    /// design whenever that file could not be read this session), this check is always on: it depends on
    /// nothing but the entered number, so it can never be silently skipped the way the Revit.ini-native warning
    /// can (SolidGround Issue #31, PH3-4, Stage D, re-check finding: the dialog's own Point Budget step
    /// previously enforced only <c>PointBudget &gt; 0</c> in <see cref="CanGoNext"/>, so a value above 50,000
    /// was caught only after the whole ten-step wizard completed and the operator pressed Create, by
    /// <c>CreateToposolidCommand</c>'s post-merge <c>effectiveSettings.Request.Validate()</c> call -- discarding
    /// the entire interactive session, with no retained state, for a mistake this step could have caught
    /// immediately instead). Deliberately reuses <see cref="SimplificationSettings"/>'s own literal bound rather
    /// than a second, dialog-local constant, so the two can never drift apart.
    /// </summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> (live-session finding, fixed; defect D, the
    /// buffer-panel-like always-on error: the same empty-string-fallback failure
    /// <see cref="PointBudgetWarningText"/>'s remarks describe. <see cref="BufferErrorText"/>'s own,
    /// visually-identical inline error was checked and confirmed unaffected: it is an
    /// <c>[ObservableProperty]</c>-backed, generator-emitted <see langword="public"/> property, never a
    /// hand-written <see langword="internal"/> one, so it was never exposed to this failure mode.)
    /// </remarks>
    public string? PointBudgetRangeErrorText =>
        PointBudget is >= SimplificationSettings.MinPointBudget and <= SimplificationSettings.MaxPointBudget
            ? null
            : $"pointBudget must be between {SimplificationSettings.MinPointBudget.ToString(CultureInfo.InvariantCulture)} " +
              $"and {SimplificationSettings.MaxPointBudget.ToString(CultureInfo.InvariantCulture)} inclusive.";

    /// <summary>
    /// Exposes <see cref="_addressWasGeocoded"/> to the view so the provenance-preview panel
    /// (<see cref="ShowFindParcelGeocodedIntro"/>, <see cref="ShowFindParcelDirectPointIntro"/>) can
    /// distinguish a geocoded address from a directly entered "latitude, longitude" pair, even though both
    /// leave <see cref="AoiSource"/> at <see cref="DialogAoiSource.FindParcel"/> and both leave
    /// <see cref="SelectedGeocodeCandidate"/> non-null (review finding, major). Manually notified by
    /// <see cref="NotifyAddressWasGeocodedChanged"/>, since <see cref="_addressWasGeocoded"/> is plain private
    /// state, not itself an <c>[ObservableProperty]</c>.
    /// </summary>
    internal bool AddressWasGeocoded => _addressWasGeocoded;

    /// <summary>
    /// True exactly when the provenance-preview panel should show the geocoded-address wording: an
    /// interactive parcel lookup (<see cref="AoiSource"/> is <see cref="DialogAoiSource.FindParcel"/>) whose
    /// confirmed point actually came from <see cref="AddressWasGeocoded"/>. See
    /// <see cref="ShowFindParcelDirectPointIntro"/> for the mutually exclusive direct-entry sibling (review
    /// finding, major: the panel previously showed one unconditional "the confirmed address will be included"
    /// sentence regardless of this distinction, which is false whenever the operator entered coordinates
    /// directly -- <see cref="AddressParcelProvenanceFactory.Create"/> never attaches address data in that
    /// case).
    /// </summary>
    /// <remarks>
    /// <see langword="public"/>, not <see langword="internal"/> (live-session finding, fixed; defect B: with
    /// both this and <see cref="ShowFindParcelDirectPointIntro"/> stuck <see langword="internal"/>, *both*
    /// bound <c>Visibility</c> values silently stuck at <see cref="System.Windows.Visibility.Visible"/> -- see
    /// <see cref="ShowNoParcelCandidatesMessage"/>'s remarks for the full mechanism -- so the step 9 panel
    /// showed both the geocoded-address and the direct-point-entry sentence together, regardless of which path
    /// was actually taken, alongside the always-visible settings-file sentence).
    /// </remarks>
    public bool ShowFindParcelGeocodedIntro => AoiSource == DialogAoiSource.FindParcel && AddressWasGeocoded;

    /// <summary>
    /// True exactly when the provenance-preview panel should show the direct-point-entry wording: an
    /// interactive parcel lookup whose confirmed point was entered directly as coordinates and never geocoded.
    /// See <see cref="ShowFindParcelGeocodedIntro"/>.
    /// </summary>
    /// <remarks><see langword="public"/>, not <see langword="internal"/> (live-session finding, fixed; defect B) -- see <see cref="ShowFindParcelGeocodedIntro"/>'s own remarks.</remarks>
    public bool ShowFindParcelDirectPointIntro => AoiSource == DialogAoiSource.FindParcel && !AddressWasGeocoded;

    /// <summary>
    /// Manually raises property-change notifications for <see cref="AddressWasGeocoded"/> and its two
    /// dependent provenance-preview visibility properties -- <see cref="_addressWasGeocoded"/> is plain
    /// private state, not itself an <c>[ObservableProperty]</c>, so nothing notifies these automatically.
    /// Called from both assignment sites inside <see cref="Geocode"/>.
    /// </summary>
    private void NotifyAddressWasGeocodedChanged()
    {
        OnPropertyChanged(nameof(AddressWasGeocoded));
        OnPropertyChanged(nameof(ShowFindParcelGeocodedIntro));
        OnPropertyChanged(nameof(ShowFindParcelDirectPointIntro));
    }

    // ------------------------------------------------------------------------------------------------------
    // Buffer validation: reuses LinearDistance.Meters's own constructor guard rather than reimplementing it
    // (docs/architecture/revit-interactive-dialog.md "Content model and sections" step 4).
    // ------------------------------------------------------------------------------------------------------

    partial void OnBufferMetersChanged(double value)
    {
        try
        {
            _ = LinearDistance.Meters(value);
            BufferErrorText = null;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            BufferErrorText = ex.Message;
        }
    }

    // ------------------------------------------------------------------------------------------------------
    // Geocode / FindParcel: the synchronous network bridge (docs/architecture/revit-interactive-dialog.md
    // "Threading and the network bridge"). Deliberately near-duplicated, not shared through a common
    // higher-order helper -- FindParcel "mirrors this exactly", per that section's own wording, and keeping
    // both bodies literal and independent is what lets
    // SolidGroundDialogNetworkLookupsCatchTimeoutAndNeverRethrowInsideTheirOwnMethodBodies check each one on
    // its own, real merits.
    // ------------------------------------------------------------------------------------------------------

    private bool CanGeocode() => !IsBusy && !string.IsNullOrWhiteSpace(AddressText);

    [RelayCommand(CanExecute = nameof(CanGeocode))]
    private void Geocode()
    {
        ErrorText = null;
        string addressText = AddressText.Trim();

        if (LatitudeLongitudePointParser.TryParse(addressText, out double latitude, out double longitude))
        {
            // A direct point entry never touches the network at all -- see
            // docs/architecture/revit-interactive-dialog.md "Content model and sections" step 1: this is what
            // lets the dialog be exercised offline against the local parcel file source.
            AddressGeocodeCandidate syntheticCandidate = new(
                latitude,
                longitude,
                FormatCoordinatePair(latitude, longitude),
                "Coordinates entered directly by the operator; not resolved through any address geocoding provider.");
            GeocodeCandidates = new ObservableCollection<AddressGeocodeCandidate>([syntheticCandidate]);
            SelectedGeocodeCandidate = syntheticCandidate;
            _addressWasGeocoded = false;
            _geocodedAddressText = null;
            // The property setter (not the bare _confirmedAddressText field), so its own
            // NotifyCanExecuteChangedFor(nameof(NextCommand)) re-evaluates Next here -- see this property's own
            // remarks: SelectedGeocodeCandidate's assignment above already re-evaluated NextCommand one
            // statement too early, while this value still held its previous, stale content.
            ConfirmedAddressText = addressText;
            NotifyAddressWasGeocodedChanged();
            return;
        }

        if (LatitudeLongitudePointParser.LooksLikeAttemptedCoordinatePair(addressText))
        {
            ErrorText = LatitudeLongitudePointParser.CoordinatePairOutOfRangeMessage;
            return;
        }

        IsBusy = true;
        // Review finding (docs/architecture/revit-interactive-dialog.md "Threading and the network bridge"):
        // a WPF property change alone cannot paint before a blocking call on the same UI thread with no
        // message loop pumping. This pumps one Render-priority frame explicitly, so IsBusy's visual actually
        // has a chance to appear before the freeze (Manual Evidence step 1 confirms this live).
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        AddInLog.Info(
            $"Address lookup: Revit will be unresponsive for up to {_inputs.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} " +
            "second(s) while SolidGround looks up this address.");
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(_inputs.NetworkTimeoutSeconds));
        try
        {
            // IAddressGeocoder.GeocodeAsync returns ValueTask<T>, not Task<T>, so the bridge needs this extra
            // .AsTask() call before Task.Run -- Task.Run(Func<ValueTask<T>>) would otherwise bind to
            // Task.Run<TResult>(Func<TResult>) with TResult inferred as ValueTask<T> itself, which does not
            // compile back down to a plain T (docs/architecture/revit-interactive-dialog.md "Threading and
            // the network bridge", review finding, blocker, corrected here).
            AddressGeocodeAcquisition acquisition = Task.Run(
                    () => _inputs.Geocoder.GeocodeAsync(new AddressGeocodeRequest(addressText), cts.Token).AsTask(),
                    cts.Token)
                .GetAwaiter().GetResult();
            GeocodeCandidates = new ObservableCollection<AddressGeocodeCandidate>(acquisition.Candidates);
            SelectedGeocodeCandidate = GeocodeCandidates[0];
            _addressWasGeocoded = true;
            _geocodedAddressText = addressText;
            // The property setter, not the bare field -- see the identical comment on this call's sibling in
            // the direct-point-entry branch above.
            ConfirmedAddressText = addressText;
            NotifyAddressWasGeocodedChanged();
        }
        catch (AddressGeocoderException ex)
        {
            ErrorText = ex.Message; // Shown inline; never rethrown.
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Every shipped IAddressGeocoder implementation rethrows the raw, unwrapped
            // OperationCanceledException/TaskCanceledException -- never its own AddressGeocoderException --
            // when the caller's own token (cts.Token here) is the one that fired
            // (docs/architecture/revit-interactive-dialog.md "Threading and the network bridge", review
            // finding, blocker: CensusGeocoder.cs:98, EsriGeocoder.cs:100, GeocodioGeocoder.cs:95 all
            // confirmed). Without this clause, that raw exception would escape this method uncaught.
            ErrorText =
                $"The address lookup did not complete within {_inputs.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} " +
                "second(s). Check network connectivity and retry.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Invalidates any previously found parcel candidates whenever the confirmed geocode candidate changes --
    /// whether from a fresh <see cref="Geocode"/> call or the operator picking a different existing item from
    /// the bound <c>GeocodeCandidates</c> list after navigating back -- so <see cref="Create"/> can never pair
    /// a parcel found for a different location with the address/point actually confirmed (review finding,
    /// major, see docs/architecture/revit-interactive-dialog.md "MVVM shape (and why no messenger)":
    /// <see cref="CanGoNext"/>'s <see cref="SolidGroundDialogStep.ParcelCandidates"/> case only checked
    /// <see cref="SelectedParcelCandidate"/> for non-null, never that it corresponded to the current
    /// <see cref="SelectedGeocodeCandidate"/>, so a stale parcel/geocode pairing could otherwise reach
    /// <see cref="Create"/> silently). CommunityToolkit.Mvvm's source generator invokes this automatically on
    /// every write to <see cref="SelectedGeocodeCandidate"/>, including both call sites inside
    /// <see cref="Geocode"/> and the <c>GeocodeCandidates</c> list's own two-way-bound selection.
    /// </summary>
    partial void OnSelectedGeocodeCandidateChanged(AddressGeocodeCandidate? value)
    {
        ParcelCandidates = [];
        SelectedParcelCandidate = null;
        _parcelLookupAttempted = false;
        UsedNearbyTier = false;
        ResultSetTruncated = false;
        OnPropertyChanged(nameof(ShowNoParcelCandidatesMessage));
    }

    private bool CanFindParcel() => !IsBusy && SelectedGeocodeCandidate is not null;

    [RelayCommand(CanExecute = nameof(CanFindParcel))]
    private void FindParcel()
    {
        ErrorText = null;

        if (SelectedGeocodeCandidate is not { } geocodeCandidate)
        {
            return;
        }

        if (_inputs.ParcelSource is not { } parcelSource)
        {
            // A configuration problem, not a lookup failure -- reported the first time a parcel lookup is
            // attempted, never at settings-load time (mirrors how a missing OPENTOPOGRAPHY_API_KEY is only
            // ever reported, never thrown, at the point a fetch is attempted).
            ErrorText =
                "No parcel boundary source is configured for this dialog; set addressAndParcel in the " +
                "settings file and reopen SolidGround.";
            return;
        }

        IsBusy = true;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
        AddInLog.Info(
            $"Parcel lookup: Revit will be unresponsive for up to {_inputs.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} " +
            "second(s) while SolidGround looks up this parcel.");
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(_inputs.NetworkTimeoutSeconds));
        try
        {
            // NearbyParcelBoundaryFinder (SolidGround Issue #31 follow-up) tries the exact point-in-parcel
            // query first and only falls back to its own nearby-parcel tier when that returns zero
            // candidates -- see docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier".
            // Same ValueTask<T>-to-Task<T> bridge as Geocode's own identical call above.
            ParcelProximityAcquisition acquisition = Task.Run(
                    () => NearbyParcelBoundaryFinder.FindAsync(
                        parcelSource, geocodeCandidate.Latitude, geocodeCandidate.Longitude,
                        _inputs.NearbySearchRadiusMeters, cts.Token).AsTask(),
                    cts.Token)
                .GetAwaiter().GetResult();
            // Set before ParcelCandidates below, so that assignment's own NotifyPropertyChangedFor(NearbyTierNoticeText)
            // already reads UsedNearbyTier's/ResultSetTruncated's final value for this lookup, not the previous
            // lookup's.
            UsedNearbyTier = acquisition.UsedNearbyTier;
            ResultSetTruncated = acquisition.ResultSetTruncated;
            ParcelCandidates = new ObservableCollection<ParcelProximityCandidate>(acquisition.Candidates);
            // No default selection here (docs/architecture/revit-interactive-dialog.md "Content model and
            // sections" step 3, unlike Geocode's own best-match default): a zero-candidate result is a
            // normal, non-exceptional outcome, and even a single candidate still requires the operator's own
            // explicit confirmation -- including every nearby-tier candidate, never auto-selected regardless
            // of how close it is.
            SelectedParcelCandidate = null;
            _parcelLookupAttempted = true;
            OnPropertyChanged(nameof(ShowNoParcelCandidatesMessage));
        }
        catch (ParcelBoundarySourceException ex)
        {
            ErrorText = ex.Message;
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            ErrorText =
                $"The parcel lookup did not complete within {_inputs.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} " +
                "second(s). Check network connectivity and retry.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string FormatCoordinatePair(double latitude, double longitude) =>
        latitude.ToString("F6", CultureInfo.InvariantCulture) + ", " + longitude.ToString("F6", CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------------------------------------------
    // Navigation
    // ------------------------------------------------------------------------------------------------------

    private bool CanGoNext() => CurrentStep switch
    {
        SolidGroundDialogStep.AoiSourceChoice => true,
        // Also requires AddressText to still match ConfirmedAddressText (review finding, blocker): without
        // this, editing the address after confirming a candidate -- without a fresh successful Geocode --
        // left the stale SelectedGeocodeCandidate/SelectedParcelCandidate pairing reachable all the way to
        // Create, silently building the AOI and AddressParcelProvenance for a different, earlier-confirmed
        // location than what is currently displayed in the address field. Reads the generated property, not
        // the bare _confirmedAddressText field, now that field is [ObservableProperty]-backed (CommunityToolkit.Mvvm's
        // own analyzer, MVVMTK0034, disallows referencing it directly once it is).
        SolidGroundDialogStep.AddressEntry => SelectedGeocodeCandidate is not null && AddressText.Trim() == ConfirmedAddressText,
        SolidGroundDialogStep.GeocodeCandidates => SelectedGeocodeCandidate is not null,
        SolidGroundDialogStep.ParcelCandidates => SelectedParcelCandidate is not null,
        SolidGroundDialogStep.Buffer => BufferErrorText is null,
        // Re-check finding, minor, fixed (SolidGround Issue #31, PH3-4, Stage D): this used to enforce only a
        // bare positivity check; PointBudgetRangeErrorText is null exactly when PointBudget also satisfies
        // SimplificationSettings.MinPointBudget/.MaxPointBudget's own inclusive bound (which subsumes that old
        // check, since MinPointBudget is 1), so an out-of-range entry is now caught in this same step, not only
        // after the whole wizard completes and Create runs CreateToposolidCommand's post-merge
        // effectiveSettings.Request.Validate() call.
        SolidGroundDialogStep.PointBudget => PointBudgetRangeErrorText is null,
        SolidGroundDialogStep.UnitChoice => true,
        SolidGroundDialogStep.LevelAndToposolidType => SelectedLevel is not null && SelectedToposolidType is not null,
        SolidGroundDialogStep.SharedCoordinatesOptIn => true,
        SolidGroundDialogStep.ProvenancePreview => true,
        SolidGroundDialogStep.PreflightSummary => false, // Create replaces Next on the final step.
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private void Next()
    {
        SolidGroundDialogStep[] order = ActiveStepOrder;
        int index = Array.IndexOf(order, CurrentStep);
        if (index < 0 || index >= order.Length - 1)
        {
            return;
        }

        CurrentStep = order[index + 1];
    }

    private bool CanGoBack() => ActiveStepOrder is [var first, ..] && first != CurrentStep;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        SolidGroundDialogStep[] order = ActiveStepOrder;
        int index = Array.IndexOf(order, CurrentStep);
        if (index <= 0)
        {
            return;
        }

        CurrentStep = order[index - 1];
    }

    // ------------------------------------------------------------------------------------------------------
    // Create / Cancel
    // ------------------------------------------------------------------------------------------------------

    private bool CanCreate() => CurrentStep == SolidGroundDialogStep.PreflightSummary;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private void Create()
    {
        if (SelectedLevel is not { } level || SelectedToposolidType is not { } toposolidType)
        {
            return;
        }

        AreaOfInterest? aoi = null;
        AddressParcelProvenance? addressParcel = null;
        if (AoiSource == DialogAoiSource.FindParcel)
        {
            if (SelectedParcelCandidate is not { } selectedProximityCandidate)
            {
                return;
            }

            // The confirmed parcel candidate is recorded exactly as before this fallback tier existed --
            // ParcelProximityCandidate.DistanceMeters/whether the nearby tier was used are dialog-only
            // display state, never threaded into the AOI or provenance.
            ParcelBoundaryCandidate parcelCandidate = selectedProximityCandidate.Candidate;
            aoi = ParcelBoundaryAoiFactory.FromCandidate(parcelCandidate, LinearDistance.Meters(BufferMeters));

            // The caller supplies the clock value -- this view-model is not SolidGround.Core, but it follows
            // that project's own established convention anyway (CreateToposolidCommand.ReportSuccess's
            // identical DateTime.UtcNow call), so AddressParcelProvenanceFactory itself stays clock-free and
            // directly testable with an injected date.
            addressParcel = AddressParcelProvenanceFactory.Create(
                DateOnly.FromDateTime(DateTime.UtcNow),
                _addressWasGeocoded,
                _inputs.GeocoderProvider,
                _geocodedAddressText,
                _addressWasGeocoded ? SelectedGeocodeCandidate : null,
                parcelCandidate);
        }

        Result = new SolidGroundDialogResult(
            AoiSource,
            aoi,
            level,
            toposolidType,
            SelectedOutputUnit,
            PointBudget,
            WriteSharedCoordinatesIfAbsent,
            addressParcel);

        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }
}
