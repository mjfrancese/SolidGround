using SolidGround.Core.Sources;

namespace SolidGround.Core.Provenance;

/// <summary>
/// Builds an <see cref="AddressParcelProvenance"/> from an operator's confirmed selections in SolidGround
/// Issue #31 (PH3-4)'s interactive Revit dialog -- Revit-free so it is directly, offline testable. See
/// docs/architecture/revit-interactive-dialog.md "AOI and provenance: two AOI paths, dialog-resolved or
/// settings-driven".
/// </summary>
public static class AddressParcelProvenanceFactory
{
    /// <summary>
    /// Returns <see langword="null"/> when neither a geocode nor a parcel lookup contributed (mirroring
    /// <see cref="AddressParcelProvenance"/>'s own "construct no <see cref="AddressParcelProvenance"/> at all"
    /// contract) -- for example a run whose area of interest came entirely from the settings file, with no
    /// address or parcel lookup performed at all. Otherwise builds the record from whichever half(s) apply.
    /// </summary>
    /// <param name="retrievalDate">
    /// When this address/parcel resolution was performed. Supplied by the caller -- this factory never reads
    /// a clock itself, matching <see cref="AddressParcelProvenance.RetrievalDate"/>'s own documented
    /// caller-supplies-the-clock-value convention.
    /// </param>
    /// <param name="addressWasGeocoded">
    /// True only when <paramref name="selectedGeocodeCandidate"/> came from an actual
    /// <see cref="IAddressGeocoder.GeocodeAsync"/> call; false when the operator entered a "latitude,
    /// longitude" pair directly (<see cref="LatitudeLongitudePointParser"/>), in which case no
    /// <see cref="GeocodeProvenance"/> is built even when <paramref name="selectedGeocodeCandidate"/> is
    /// non-null (a synthetic, not-actually-geocoded candidate standing in for that point).
    /// </param>
    /// <param name="addressQueryText">
    /// The operator-entered address text submitted to the geocoder, verbatim
    /// (<see cref="GeocodeProvenance.QueryText"/>). Required, and must be non-blank, when
    /// <paramref name="addressWasGeocoded"/> is true; ignored otherwise.
    /// </param>
    /// <param name="selectedGeocodeCandidate">
    /// The confirmed geocode candidate, when <paramref name="addressWasGeocoded"/> is true; otherwise ignored
    /// (may be null or a synthetic direct-point candidate).
    /// </param>
    /// <param name="selectedParcelCandidate">The confirmed parcel candidate, or null when no parcel lookup contributed.</param>
    /// <exception cref="ArgumentException"><paramref name="addressWasGeocoded"/> is true and <paramref name="addressQueryText"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="addressWasGeocoded"/> is true and <paramref name="selectedGeocodeCandidate"/> is null.</exception>
    public static AddressParcelProvenance? Create(
        DateOnly retrievalDate,
        bool addressWasGeocoded,
        AddressGeocoderProvider geocoderProvider,
        string? addressQueryText,
        AddressGeocodeCandidate? selectedGeocodeCandidate,
        ParcelBoundaryCandidate? selectedParcelCandidate)
    {
        GeocodeProvenance? geocode = null;
        if (addressWasGeocoded)
        {
            ArgumentNullException.ThrowIfNull(selectedGeocodeCandidate);
            ArgumentException.ThrowIfNullOrWhiteSpace(addressQueryText);
            geocode = new GeocodeProvenance(geocoderProvider, addressQueryText, selectedGeocodeCandidate.Attribution);
        }

        ParcelProvenance? parcel = selectedParcelCandidate is null
            ? null
            : new ParcelProvenance(
                selectedParcelCandidate.SourceKind,
                selectedParcelCandidate.SourceIdentity,
                selectedParcelCandidate.ParcelId,
                selectedParcelCandidate.StableParcelId,
                selectedParcelCandidate.LegalDescription,
                selectedParcelCandidate.LicenseDisclaimerText);

        return geocode is null && parcel is null ? null : new AddressParcelProvenance(retrievalDate, geocode, parcel);
    }
}
