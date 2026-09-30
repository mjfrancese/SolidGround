using SolidGround.Core.Sources;

namespace SolidGround.Core.Workflow;

/// <summary>Stages of the guided location-to-parcel workflow.</summary>
public enum LocationParcelStage
{
    Location,
    Parcel,
    Review,
}

/// <summary>An immutable identity for a lookup.  A completion is valid only for this exact input/source pair.</summary>
public sealed record LocationParcelLookupTicket(long InputRevision, long SourceRevision);

/// <summary>Revit-free state and invalidation policy for the guided location and parcel workflow.</summary>
/// <remarks>
/// This type does not perform I/O. Hosts take a ticket before beginning cancellable work and apply the
/// completion only through the matching method below. This keeps a non-cooperative, out-of-order provider
/// from replacing a newer operator choice.
/// </remarks>
public sealed class LocationParcelFlow
{
    private long _inputRevision;
    private long _sourceRevision;
    private bool _locationIsConfirmed;

    public LocationParcelStage Stage { get; private set; } = LocationParcelStage.Location;
    public long InputRevision => _inputRevision;
    public long SourceRevision => _sourceRevision;
    public IReadOnlyList<AddressGeocodeCandidate> LocationCandidates { get; private set; } = [];
    public AddressGeocodeCandidate? SelectedLocation { get; private set; }
    /// <summary>True only after the operator explicitly chose an ambiguous location.</summary>
    public bool LocationIsConfirmed => _locationIsConfirmed;
    public bool LocationRequiresConfirmation => LocationCandidates.Count > 1 && SelectedLocation is null;
    public IReadOnlyList<ParcelProximityCandidate> ParcelCandidates { get; private set; } = [];
    public ParcelProximityCandidate? SelectedParcel { get; private set; }
    public bool UsedNearbyTier { get; private set; }
    public bool ResultSetTruncated { get; private set; }

    public LocationParcelLookupTicket BeginLocationLookup() => new(_inputRevision, _sourceRevision);

    public LocationParcelLookupTicket BeginParcelLookup()
    {
        if (SelectedLocation is null)
        {
            throw new InvalidOperationException("A location must be selected before looking up parcels.");
        }

        return new(_inputRevision, _sourceRevision);
    }

    /// <summary>Invalidates location and parcel work after address or coordinate text changes.</summary>
    public void ChangeInput()
    {
        checked { _inputRevision++; }
        LocationCandidates = [];
        SelectedLocation = null;
        _locationIsConfirmed = false;
        ClearParcels();
        Stage = LocationParcelStage.Location;
    }

    /// <summary>Invalidates parcel work after a source/settings/local-file change while preserving the location.</summary>
    public void ChangeSource()
    {
        checked { _sourceRevision++; }
        ClearParcels();
        Stage = SelectedLocation is null ? LocationParcelStage.Location : LocationParcelStage.Parcel;
    }

    /// <summary>Applies a location completion only if it belongs to the latest input and source snapshot.</summary>
    public bool TryApplyLocations(LocationParcelLookupTicket ticket, IReadOnlyList<AddressGeocodeCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (!IsCurrent(ticket))
        {
            return false;
        }

        LocationCandidates = candidates;
        // A sole result is a useful location for the automatic parcel query, but it is not an operator
        // confirmation. The host uses SelectedLocation to begin that query and still requires Use this parcel.
        SelectedLocation = candidates.Count == 1 ? candidates[0] : null;
        _locationIsConfirmed = false;
        ClearParcels();
        Stage = LocationParcelStage.Parcel;
        return true;
    }

    /// <summary>Explicitly chooses one ambiguous geocode candidate and invalidates any previous parcel result.</summary>
    public bool TryUseLocation(AddressGeocodeCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!LocationCandidates.Contains(candidate))
        {
            return false;
        }

        SelectedLocation = candidate;
        _locationIsConfirmed = true;
        ClearParcels();
        Stage = LocationParcelStage.Parcel;
        return true;
    }

    /// <summary>Applies a parcel completion only for the latest request; it never confirms a parcel itself.</summary>
    public bool TryApplyParcels(LocationParcelLookupTicket ticket, ParcelProximityAcquisition acquisition)
    {
        ArgumentNullException.ThrowIfNull(acquisition);
        if (!IsCurrent(ticket) || SelectedLocation is null)
        {
            return false;
        }

        ParcelCandidates = acquisition.Candidates;
        SelectedParcel = null;
        UsedNearbyTier = acquisition.UsedNearbyTier;
        ResultSetTruncated = acquisition.ResultSetTruncated;
        Stage = LocationParcelStage.Parcel;
        return true;
    }

    /// <summary>Explicitly confirms a listed parcel, including a nearby candidate, and advances to review.</summary>
    public bool TryUseParcel(ParcelProximityCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!ParcelCandidates.Contains(candidate))
        {
            return false;
        }

        SelectedParcel = candidate;
        Stage = LocationParcelStage.Review;
        return true;
    }

    public void ReturnToParcel() => Stage = LocationParcelStage.Parcel;

    /// <summary>Returns to Location without discarding otherwise-valid lookup results.</summary>
    public void ReturnToLocation() => Stage = LocationParcelStage.Location;

    private bool IsCurrent(LocationParcelLookupTicket ticket) =>
        ticket.InputRevision == _inputRevision && ticket.SourceRevision == _sourceRevision;

    private void ClearParcels()
    {
        ParcelCandidates = [];
        SelectedParcel = null;
        UsedNearbyTier = false;
        ResultSetTruncated = false;
    }
}
