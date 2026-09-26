using SolidGround.Core.Sources;

namespace SolidGround.Core.Provenance;

/// <summary>
/// How this terrain export's area of interest was located, when an operator-entered address and/or a parcel
/// boundary lookup (SolidGround Issues #28/#29, Phase 3) actually resolved it, rather than a raw bounding box
/// or radius supplied directly. Null on <see cref="TerrainProvenance.AddressParcel"/> whenever neither
/// happened, and always null for a schema version 1 or 2 document (neither ever wrote this field). At least
/// one of <see cref="Geocode"/>/<see cref="Parcel"/> is non-null whenever this record itself is non-null —
/// construct no <see cref="AddressParcelProvenance"/> at all when neither a geocode nor a parcel lookup
/// contributed. See docs/architecture/address-parcel-provenance.md.
/// </summary>
public sealed record AddressParcelProvenance
{
    public AddressParcelProvenance(DateOnly retrievalDate, GeocodeProvenance? geocode, ParcelProvenance? parcel)
    {
        if (geocode is null && parcel is null)
        {
            throw new ArgumentException(
                "At least one of geocode or parcel must be supplied; construct no AddressParcelProvenance at " +
                "all when neither a geocode nor a parcel lookup contributed to this area of interest.",
                nameof(geocode));
        }

        RetrievalDate = retrievalDate;
        Geocode = geocode;
        Parcel = parcel;
    }

    /// <summary>
    /// When this address/parcel resolution was performed. Deliberately distinct from
    /// <see cref="ElevationSourceMetadata.CollectionPeriod"/> (the elevation dataset's own vintage) — this is
    /// wall-clock "when did SolidGround itself do the lookup," supplied by the caller (see
    /// docs/architecture/address-parcel-provenance.md's "Population path": nothing in Core reads the clock
    /// itself; <see cref="ExtensibleStorageProvenanceValues"/> and <c>PlacementRecordDraft.ToRecord</c>
    /// establish the same caller-supplies-the-clock-value precedent).
    /// </summary>
    public DateOnly RetrievalDate { get; }

    /// <summary>The address geocode that contributed to this area of interest, or null when none did (for example a parcel resolved directly from a latitude/longitude).</summary>
    public GeocodeProvenance? Geocode { get; }

    /// <summary>The parcel boundary lookup that contributed to this area of interest, or null when none did (for example a plain geocoded-address radius with no parcel confirmed).</summary>
    public ParcelProvenance? Parcel { get; }
}

/// <summary>One address geocode result (SolidGround Issue #28) that contributed to an area of interest.</summary>
public sealed record GeocodeProvenance
{
    public GeocodeProvenance(AddressGeocoderProvider provider, string queryText, string attribution)
    {
        if (!Enum.IsDefined(provider))
        {
            throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported address geocoder provider.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribution);

        Provider = provider;
        QueryText = queryText;
        Attribution = attribution;
    }

    /// <summary>Which geocoder produced this result.</summary>
    public AddressGeocoderProvider Provider { get; }

    /// <summary>
    /// The operator-entered address text submitted to the geocoder, verbatim
    /// (<see cref="AddressGeocodeRequest.Address"/>). Never a request URI; a plain address string never
    /// carries a key or query string on any shipped provider (Census/Geocodio/Esri all transport their key,
    /// when any, only in an Authorization header or a redacted query parameter never echoed into this type —
    /// see docs/architecture/address-geocoding.md's "Provider comparison").
    /// </summary>
    public string QueryText { get; }

    /// <summary>The geocoder's own attribution/terms text for the selected candidate (<see cref="AddressGeocodeCandidate.Attribution"/>), verbatim. Never blank.</summary>
    public string Attribution { get; }
}

/// <summary>One parcel boundary result (SolidGround Issue #29) that contributed to an area of interest.</summary>
public sealed record ParcelProvenance
{
    public ParcelProvenance(
        ParcelBoundarySourceKind sourceKind,
        string sourceIdentity,
        string parcelId,
        string? stableParcelId,
        string? legalDescription,
        string licenseDisclaimerText)
    {
        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unsupported parcel boundary source kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(parcelId);
        if (stableParcelId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stableParcelId);
        }

        if (legalDescription is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legalDescription);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(licenseDisclaimerText);

        SourceKind = sourceKind;
        SourceIdentity = sourceIdentity;
        ParcelId = parcelId;
        StableParcelId = stableParcelId;
        LegalDescription = legalDescription;
        LicenseDisclaimerText = licenseDisclaimerText;
    }

    /// <summary>Mirrors <see cref="ParcelBoundaryCandidate.SourceKind"/>.</summary>
    public ParcelBoundarySourceKind SourceKind { get; }

    /// <summary>Mirrors <see cref="ParcelBoundaryCandidate.SourceIdentity"/> (for example "Synthetic County (fixture only) (GEOID 99999)").</summary>
    public string SourceIdentity { get; }

    /// <summary>Mirrors <see cref="ParcelBoundaryCandidate.ParcelId"/> — the source's own parcel identifier.</summary>
    public string ParcelId { get; }

    /// <summary>Mirrors <see cref="ParcelBoundaryCandidate.StableParcelId"/> — an optional, source-labeled durable id meant to survive the source's own <see cref="ParcelId"/> churn.</summary>
    public string? StableParcelId { get; }

    /// <summary>Mirrors <see cref="ParcelBoundaryCandidate.LegalDescription"/>. Present only "when available" (AC1).</summary>
    public string? LegalDescription { get; }

    /// <summary>Mirrors <see cref="ParcelBoundaryCandidate.LicenseDisclaimerText"/>, surfaced verbatim. Never blank.</summary>
    public string LicenseDisclaimerText { get; }
}
