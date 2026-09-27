using System.Globalization;

namespace SolidGround.Core.Sources;

/// <summary>
/// Parses a "latitude, longitude" pair from free text -- SolidGround Issue #31 (PH3-4)'s interactive Revit
/// dialog accepts this shape in its address field so a site with no street address still works, and so the
/// dialog can be exercised offline against the local parcel file source (no geocoder network call needed for
/// a directly-entered point). See docs/architecture/revit-interactive-dialog.md "Content model and sections"
/// step 1.
/// </summary>
/// <remarks>
/// Deliberately independent of <c>SolidGround.Cli.Commands.ParcelCommand</c>'s own, pre-existing
/// <c>--point</c> parsing (a private method on that command, with its own <c>CliUsageException</c> message
/// text and its own tests): that method cannot be reused without either making it non-private CLI surface a
/// Revit-side caller would need to reference (impossible in any case -- <c>SolidGround.Revit</c> must never
/// reference <c>SolidGround.Cli</c>, and <c>SolidGround.Cli</c> must remain the only place that ever throws a
/// <c>CliUsageException</c>) or changing the CLI's own behavior or tests, which this issue must not do. This
/// type is a fresh, Revit-free, CLI-free Core helper instead; its contract (culture-invariant parsing, WGS 84
/// range validation) intentionally mirrors <c>ParcelCommand.ParsePoint</c>'s own, but the two implementations
/// are not unified.
/// </remarks>
public static class LatitudeLongitudePointParser
{
    /// <summary>
    /// Shown when <see cref="LooksLikeAttemptedCoordinatePair"/> is true but <see cref="TryParse"/> is false
    /// (a value was clearly entered as a coordinate pair, but is out of WGS 84 range) -- a fixed sentence that
    /// never echoes the caller's input text, matching <c>SolidGround.Cli.Commands.ParcelCommand</c>'s own
    /// "never echo the offending value" convention for its sibling <c>--point</c> parsing.
    /// </summary>
    public const string CoordinatePairOutOfRangeMessage =
        "Latitude must be between -90 and 90 degrees and longitude must be between -180 and 180 degrees.";

    /// <summary>
    /// Attempts to parse <paramref name="text"/> as an exact "latitude, longitude" pair: two comma-separated,
    /// culture-invariant, finite numbers, each within its own WGS 84 range. Returns <see langword="false"/> for
    /// anything else, including a null/blank/whitespace-only value, a value with other than two comma-separated
    /// parts (so an ordinary street address containing a comma, such as "100 EXAMPLE LOOP, TESTSITE", is never
    /// mistaken for a coordinate pair), a non-numeric part, or an out-of-range value -- callers that want to
    /// tell "not shaped like coordinates at all" apart from "shaped like coordinates, but out of range" should
    /// also consult <see cref="LooksLikeAttemptedCoordinatePair"/>.
    /// </summary>
    public static bool TryParse(string? text, out double latitude, out double longitude)
    {
        latitude = default;
        longitude = default;

        if (!TrySplitIntoTwoNumericParts(text, out double candidateLatitude, out double candidateLongitude))
        {
            return false;
        }

        if (candidateLatitude is < -90d or > 90d || candidateLongitude is < -180d or > 180d)
        {
            return false;
        }

        latitude = candidateLatitude;
        longitude = candidateLongitude;
        return true;
    }

    /// <summary>
    /// True when <paramref name="text"/> is shaped like an attempted coordinate pair -- exactly two
    /// comma-separated parts that both parse as finite, culture-invariant numbers -- regardless of whether
    /// the values are actually within WGS 84 range. Lets a caller show <see cref="CoordinatePairOutOfRangeMessage"/>
    /// for a value that was clearly meant as coordinates but is out of range, rather than silently sending it
    /// to an address geocoder (which would only ever fail to match it, after a needless network round trip).
    /// </summary>
    public static bool LooksLikeAttemptedCoordinatePair(string? text) =>
        TrySplitIntoTwoNumericParts(text, out _, out _);

    private static bool TrySplitIntoTwoNumericParts(string? text, out double first, out double second)
    {
        first = default;
        second = default;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(',');
        if (parts.Length != 2)
        {
            return false;
        }

        return TryParseFiniteDouble(parts[0], out first) && TryParseFiniteDouble(parts[1], out second);
    }

    private static bool TryParseFiniteDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
}
