using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;

namespace SolidGround.Core.Sources;

/// <summary>Which kind of <see cref="IParcelBoundarySource"/> produced a <see cref="ParcelBoundaryCandidate"/>.</summary>
public enum ParcelBoundarySourceKind
{
    CountyRegistry,
    LocalParcelFile,
}

/// <summary>
/// One resolved parcel: its boundary, identifying attributes, and provenance. Every candidate, from every
/// source, carries the same fixed <see cref="AccuracyLabel"/>: SolidGround describes a parcel boundary as a
/// cadastral/assessor tax-map representation, never a survey (AGENTS.md "Accuracy and product claims").
/// </summary>
public sealed record ParcelBoundaryCandidate
{
    /// <summary>The fixed accuracy label every candidate carries, from every source, unconditionally.</summary>
    public const string NotASurveyDisclaimer =
        "This boundary is a cadastral/assessor tax-map representation, not a survey.";

    public ParcelBoundaryCandidate(
        PolygonalRegion boundary,
        string parcelId,
        double computedAreaSquareMeters,
        ParcelBoundarySourceKind sourceKind,
        string sourceIdentity,
        string licenseDisclaimerText,
        string? situsAddress = null,
        string? subdivision = null,
        string? lot = null,
        string? block = null,
        string? plat = null,
        string? book = null,
        string? page = null,
        bool bookPageAreUnconfirmedProxies = false,
        string? legalDescription = null,
        double? reportedAcres = null,
        string? zoning = null,
        string? stableParcelId = null)
    {
        ArgumentNullException.ThrowIfNull(boundary);
        if (boundary.HorizontalReference.Kind != HorizontalReferenceKind.Geographic)
        {
            throw new ArgumentException("A parcel boundary candidate's geometry must be in a geographic (WGS 84) horizontal reference.", nameof(boundary));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(parcelId);
        if (!double.IsFinite(computedAreaSquareMeters) || computedAreaSquareMeters <= 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(computedAreaSquareMeters), computedAreaSquareMeters, "Computed area must be finite and positive.");
        }

        if (!Enum.IsDefined(sourceKind))
        {
            throw new ArgumentOutOfRangeException(nameof(sourceKind), sourceKind, "Unsupported parcel boundary source kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sourceIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseDisclaimerText);

        // Every optional string below: null means "not mapped/not reported"; never blank when non-null
        // (mirrors AddressGeocodeCandidate.PrecisionLabel's exact convention -- a direct call at each site, so
        // ArgumentException.ThrowIfNullOrWhiteSpace's CallerArgumentExpression-supplied ParamName names the
        // real parameter instead of a shared wrapper's own local).
        if (situsAddress is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(situsAddress);
        }

        if (subdivision is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(subdivision);
        }

        if (lot is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(lot);
        }

        if (block is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(block);
        }

        if (plat is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(plat);
        }

        if (book is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(book);
        }

        if (page is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(page);
        }

        if (legalDescription is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(legalDescription);
        }

        if (zoning is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(zoning);
        }

        if (stableParcelId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(stableParcelId);
        }
        if (reportedAcres is { } acres && (!double.IsFinite(acres) || acres <= 0d))
        {
            throw new ArgumentOutOfRangeException(nameof(reportedAcres), acres, "Reported acreage must be finite and positive when supplied.");
        }

        Boundary = boundary;
        ParcelId = parcelId;
        ComputedAreaSquareMeters = computedAreaSquareMeters;
        SourceKind = sourceKind;
        SourceIdentity = sourceIdentity;
        LicenseDisclaimerText = licenseDisclaimerText;
        SitusAddress = situsAddress;
        Subdivision = subdivision;
        Lot = lot;
        Block = block;
        Plat = plat;
        Book = book;
        Page = page;
        BookPageAreUnconfirmedProxies = bookPageAreUnconfirmedProxies;
        LegalDescription = legalDescription;
        ReportedAcres = reportedAcres;
        Zoning = zoning;
        StableParcelId = stableParcelId;
    }

    /// <summary>The parcel boundary, in a geographic (WGS 84) horizontal reference. NTS-backed but not itself an NTS type, the same package-neutral seam <c>PolygonalRegion</c> already establishes.</summary>
    public PolygonalRegion Boundary { get; }

    /// <summary>The source's own parcel identifier (for example Esri's <c>PARCEL_ID</c> or Regrid's <c>parcelnumb</c>).</summary>
    public string ParcelId { get; }

    /// <summary>Computed by SolidGround itself from <see cref="Boundary"/> -- see <see cref="ParcelBoundaryWgs84.ComputeAreaSquareMeters"/>. Never trusted from a source's own reported acreage field.</summary>
    public double ComputedAreaSquareMeters { get; }

    public ParcelBoundarySourceKind SourceKind { get; }

    /// <summary>A human-readable label for the specific source that produced this candidate (for example "Synthetic County (fixture only) (GEOID 99999)" or a configured local file's <c>SourceLabel</c>).</summary>
    public string SourceIdentity { get; }

    /// <summary>The source's own license/disclaimer text, surfaced verbatim. Host-supplied; never a SolidGround-hardcoded real license.</summary>
    public string LicenseDisclaimerText { get; }

    /// <summary>Always <see cref="NotASurveyDisclaimer"/>; not a constructor parameter. Deliberately an instance member (not static) so every candidate exposes the same uniform per-instance shape as its other properties.</summary>
#pragma warning disable CA1822 // Intentionally instance, not static: see the remark above.
    public string AccuracyLabel => NotASurveyDisclaimer;
#pragma warning restore CA1822

    public string? SitusAddress { get; }
    public string? Subdivision { get; }
    public string? Lot { get; }
    public string? Block { get; }
    public string? Plat { get; }
    public string? Book { get; }
    public string? Page { get; }

    /// <summary>True when <see cref="Book"/>/<see cref="Page"/> are unconfirmed proxies for a real plat-book reference (for example a county registry mapping both to the same combined "deed book/page" field), rather than a contractually defined book/page pair.</summary>
    public bool BookPageAreUnconfirmedProxies { get; }

    public string? LegalDescription { get; }
    public double? ReportedAcres { get; }
    public string? Zoning { get; }
    public string? StableParcelId { get; }
}

/// <summary>
/// The result of one <see cref="IParcelBoundarySource.FindAsync"/> call: zero or more candidates. Unlike
/// <c>AddressGeocodeAcquisition</c>, an empty result is not exceptional here: a point outside every known
/// parcel, or an address substring matching nothing, is a normal outcome of a map lookup, not a source
/// failure -- most WGS 84 points are not inside the currently-registered parcel fabric (street rights-of-way,
/// water, a point outside the loaded county), and most address substrings simply will not match.
/// </summary>
public sealed record ParcelBoundaryAcquisition
{
    public ParcelBoundaryAcquisition(IReadOnlyList<ParcelBoundaryCandidate> candidates, bool resultSetTruncated = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates = candidates;
        ResultSetTruncated = resultSetTruncated;
    }

    /// <summary>Zero or more candidates.</summary>
    public IReadOnlyList<ParcelBoundaryCandidate> Candidates { get; }

    /// <summary>True when the source's own paging/limit signal (Esri's <c>exceededTransferLimit</c>) indicates more matches may exist than were returned. Always false for <see cref="LocalParcelFile.LocalParcelFileSource"/> (a full local read is never paginated).</summary>
    public bool ResultSetTruncated { get; }
}

/// <summary>
/// One <see cref="ParcelBoundaryCandidate"/> plus its distance from the point <see cref="NearbyParcelBoundaryFinder.FindAsync"/>
/// was asked about -- 0 when the candidate contains (or touches) that point, otherwise the true distance to
/// its boundary in meters (<see cref="ParcelBoundaryProximity.DistanceMeters"/>).
/// </summary>
public sealed record ParcelProximityCandidate
{
    public ParcelProximityCandidate(ParcelBoundaryCandidate candidate, double distanceMeters)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        if (!double.IsFinite(distanceMeters) || distanceMeters < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(distanceMeters), distanceMeters, "Distance must be finite and non-negative.");
        }

        Candidate = candidate;
        DistanceMeters = distanceMeters;
    }

    public ParcelBoundaryCandidate Candidate { get; }

    public double DistanceMeters { get; }
}

/// <summary>
/// The result of <see cref="NearbyParcelBoundaryFinder.FindAsync"/>: zero or more candidates, ordered by
/// increasing <see cref="ParcelProximityCandidate.DistanceMeters"/> (ties broken stably, in whichever order
/// the underlying source itself returned them), plus whether the nearby (fallback) tier had to run at all.
/// </summary>
public sealed record ParcelProximityAcquisition
{
    public ParcelProximityAcquisition(IReadOnlyList<ParcelProximityCandidate> candidates, bool usedNearbyTier, bool resultSetTruncated = false)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        Candidates = candidates;
        UsedNearbyTier = usedNearbyTier;
        ResultSetTruncated = resultSetTruncated;
    }

    /// <summary>Zero or more candidates -- empty when neither tier found anything (including when the nearby tier found only candidates farther than the requested radius).</summary>
    public IReadOnlyList<ParcelProximityCandidate> Candidates { get; }

    /// <summary>
    /// True when the exact <see cref="ParcelPointQuery"/> tier returned zero candidates and the
    /// <see cref="ParcelNearbyQuery"/> fallback tier ran as a result -- regardless of whether that fallback
    /// tier itself then found anything within the requested radius. False whenever the exact tier alone
    /// already resolved at least one candidate (the fallback tier never runs in that case at all).
    /// </summary>
    public bool UsedNearbyTier { get; }

    /// <summary>
    /// Forwards whichever tier's own <see cref="ParcelBoundaryAcquisition.ResultSetTruncated"/> actually produced
    /// <see cref="Candidates"/>: the exact tier's own value when it already resolved at least one candidate
    /// (<see cref="UsedNearbyTier"/> false, so the nearby tier never ran), otherwise the nearby tier's own value.
    /// True means the source's own paging/limit signal (Esri's <c>exceededTransferLimit</c>) indicated more
    /// matches may exist than were actually returned for that one request -- so <see cref="Candidates"/>, and
    /// (when <see cref="UsedNearbyTier"/> is true) possibly the operator's true closest parcel, may be an
    /// incomplete subset of what the source actually has in range (re-check finding, minor, fixed: this signal
    /// used to be read from the nearby tier's own <see cref="ParcelBoundaryAcquisition"/> and then discarded
    /// inside <see cref="NearbyParcelBoundaryFinder.FindAsync"/>, so neither this type nor the interactive
    /// dialog could ever learn -- or tell the operator -- that the shown candidate list might be incomplete).
    /// Always false for <see cref="LocalParcelFile.LocalParcelFileSource"/> (a full local read is never
    /// paginated). See docs/architecture/parcel-boundary-sources.md's "Nearby-parcel fallback tier".
    /// </summary>
    public bool ResultSetTruncated { get; }
}
