namespace SolidGround.Core.Sources;

/// <summary>
/// A denylist of owner/mailing-address field names, used to reject a misconfigured field map before it could
/// ever surface owner data -- belt-and-suspenders alongside both parcel-boundary sources' own allow-list read
/// paths (only the field-map's own named slots are ever looked up on a raw response/file; see
/// docs/architecture/parcel-boundary-sources.md). Public (not internal): <c>SolidGround.Core</c> grants no
/// <c>InternalsVisibleTo</c> to <c>SolidGround.Tests</c> (only <c>SolidGround.Cli</c> gets that grant), and
/// this list is independently useful as a reusable safety utility, the same spirit as
/// <see cref="Http.SensitiveQueryParameterNames.KnownFamilies"/>.
/// </summary>
public static class OwnerFieldNameGuard
{
    /// <summary>Exact, case-insensitive owner/mailing field names known to appear in a real schema: the six commonly named Esri-style tokens plus Regrid's own named owner/mailing fields.</summary>
    public static IReadOnlyCollection<string> KnownOwnerFieldNames { get; } =
    [
        // Commonly named Esri-style owner/mailing tokens.
        "OWNER_NAME", "OWN_ADD", "OWN_CITY", "OWN_STATE", "OWN_ZIP", "CAREOF",

        // Regrid's Standard schema: ownership fields, then mailing-address fields.
        "owner", "owntype", "ownfrst", "ownlast", "owner2", "owner3", "owner4",
        "previous_owner", "unmodified_owner",
        "mailadd", "mail_city", "mail_state2", "mail_zip", "mail_country", "careof",
        "mail_address2", "mail_addno", "mail_addpref", "mail_addstr", "mail_addsttyp",
        "mail_addstsuf", "mail_unit", "mail_urbanization", "original_mailing_address",

        // Regrid's separate, always-dropped Enhanced Ownership join key/product name.
        "enhanced_ownership", "eo_owner", "eo_deedowner", "attom_id",
    ];

    /// <summary>
    /// Case-insensitive fragments that flag a county-specific owner/mailing field name not in the exact list
    /// above. <c>"own"</c> and <c>"mail"</c> only match when they begin a "word" within the field name (not
    /// immediately preceded by an ASCII letter): an unanchored substring match would wrongly reject a
    /// legitimate field such as a standard PLSS cadastral <c>TOWNSHIP</c>/<c>TOWNSHIP_RANGE</c> column, which
    /// merely contains "own" as a mid-word substring. Anchoring to a word start loses no real coverage: every
    /// named target already begins the fragment at a word start ("OWN_ADD", "OWNER_NAME", "MAIL_CITY",
    /// snake_case "owntype"/"mailadd", etc.). <c>"care_of"</c>/<c>"careof"</c>/<c>"c/o"</c>/<c>"attn"</c> keep a
    /// plain, unanchored substring match: they are longer, more specific fragments with no demonstrated
    /// collision against a legitimate parcel field name.
    /// </summary>
    private static readonly string[] WordStartFragments = ["own", "mail"];

    private static readonly string[] SubstringFragments = ["care_of", "careof", "c/o", "attn"];

    /// <summary>True when <paramref name="fieldName"/> is a known owner/mailing field name, or resembles one closely enough that SolidGround must never map it.</summary>
    public static bool IsOwnerLike(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        if (KnownOwnerFieldNames.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (SubstringFragments.Any(fragment => fieldName.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return WordStartFragments.Any(fragment => StartsWordWith(fieldName, fragment));
    }

    /// <summary>
    /// True when <paramref name="fragment"/> (ordinal case-insensitive) occurs in <paramref name="fieldName"/>
    /// at a position not immediately preceded by an ASCII letter -- the start of the string, or right after a
    /// digit, underscore, or other non-letter delimiter. Matches "OWN_ADD" (fragment at position 0) and
    /// "previous_owner"-shaped names (fragment right after "_") but not "TOWNSHIP" (fragment preceded by the
    /// letter "T").
    /// </summary>
    private static bool StartsWordWith(string fieldName, string fragment)
    {
        int searchStart = 0;
        while (true)
        {
            int index = fieldName.IndexOf(fragment, searchStart, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return false;
            }

            if (index == 0 || !char.IsAsciiLetter(fieldName[index - 1]))
            {
                return true;
            }

            searchStart = index + 1;
        }
    }
}
