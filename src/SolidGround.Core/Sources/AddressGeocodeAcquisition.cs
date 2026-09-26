namespace SolidGround.Core.Sources;

/// <summary>
/// A single-line street address to resolve. The only input shape all three shipped providers accept
/// natively (Census's <c>address</c> parameter, Geocodio's <c>q</c> parameter, Esri's <c>SingleLine</c>
/// parameter).
/// </summary>
public sealed record AddressGeocodeRequest
{
    public AddressGeocodeRequest(string address)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        Address = address;
    }

    public string Address { get; }
}

/// <summary>
/// One ranked, approximate geocode candidate. <see cref="Latitude"/> and <see cref="Longitude"/> are WGS 84
/// geographic coordinates -- stated here in this type's own documentation, the same way
/// <c>Wgs84BoundingBoxAoi</c>'s own axis-named properties never repeat "Wgs84" in the property name itself.
/// SolidGround describes every geocode result as approximate, never survey-grade (AGENTS.md "Accuracy and
/// product claims").
/// </summary>
public sealed record AddressGeocodeCandidate
{
    public AddressGeocodeCandidate(
        double latitude,
        double longitude,
        string matchedAddress,
        string attribution,
        string? precisionLabel = null,
        double? score = null)
    {
        if (!double.IsFinite(latitude) || latitude is < -90d or > 90d)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "Latitude must be finite and between -90 and 90 degrees.");
        }

        if (!double.IsFinite(longitude) || longitude is < -180d or > 180d)
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "Longitude must be finite and between -180 and 180 degrees.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(matchedAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(attribution);
        if (precisionLabel is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(precisionLabel);
        }

        if (score is { } value && !double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(score), value, "Score must be finite when supplied.");
        }

        Latitude = latitude;
        Longitude = longitude;
        MatchedAddress = matchedAddress;
        Attribution = attribution;
        PrecisionLabel = precisionLabel;
        Score = score;
    }

    public double Latitude { get; }
    public double Longitude { get; }
    public string MatchedAddress { get; }

    /// <summary>The candidate's attribution text: the provider's own per-candidate string (Geocodio's <c>source</c>) or a fixed provider-wide notice (Census's terms-of-service notice; Esri's "Powered by Esri"). Never blank.</summary>
    public string Attribution { get; }

    /// <summary>The provider's own precision/match-level label (Geocodio's <c>accuracy_type</c>; Esri's <c>attributes.Addr_type</c>), or null when the provider's schema has no such field (Census).</summary>
    public string? PrecisionLabel { get; }

    /// <summary>The provider's own numeric ranking score, or null when the provider supplies none (Census). Not comparable across providers: Geocodio's is 0.0-1.0, Esri's is 1-100.</summary>
    public double? Score { get; }
}

/// <summary>
/// A non-empty, best-match-first list of geocode candidates. There is no successful empty result: a
/// provider that found nothing throws its own no-candidates exception instead of returning an empty
/// acquisition. Ranking is conveyed purely by list order; no candidate carries a separate rank field. Each
/// provider is responsible for producing that order correctly -- see docs/architecture/address-geocoding.md.
/// </summary>
public sealed record AddressGeocodeAcquisition
{
    public AddressGeocodeAcquisition(IReadOnlyList<AddressGeocodeCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            throw new ArgumentException(
                "At least one candidate is required; a zero-candidate result must be reported as a thrown exception, not an empty acquisition.",
                nameof(candidates));
        }

        Candidates = candidates;
    }

    public IReadOnlyList<AddressGeocodeCandidate> Candidates { get; }
}
