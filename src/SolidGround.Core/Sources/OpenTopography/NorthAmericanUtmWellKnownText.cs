using System.Globalization;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// Synthesizes ESRI-style WKT1 <c>COMPD_CS</c> text for a NAD83 UTM zone (EPSG 26901-26923, zones 1-23),
/// paired with a declared vertical reference, from fixed literal templates -- the same shape as the
/// committed <c>example-site-synthetic.prj</c> fixture -- rather than from any general-purpose WKT-generation
/// library. Only the zone number, its derived central meridian, and the EPSG code are interpolated into
/// the horizontal (<c>PROJCS</c>/<c>GEOGCS</c>) portion; the vertical (<c>VERT_CS</c>) portion interpolates
/// the declared datum name (in the <c>COMPD_CS</c> name, the <c>VERT_CS</c> name, and the <c>VERT_DATUM</c>
/// name) and a fixed per-unit literal chosen by the vertical unit. Every other token, including every
/// numeric literal of the projection, is copied verbatim from the fixture, so no floating-point formatting
/// decision can change the text. See docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT
/// synthesis" section.
/// </summary>
public static class NorthAmericanUtmWellKnownText
{
    private const int MinimumZone = 1;
    private const int MaximumZone = 23;
    private const int MinimumEpsgCode = 26900 + MinimumZone;
    private const int MaximumEpsgCode = 26900 + MaximumZone;

    private static readonly IReadOnlyList<int> SupportedEpsgCodesValue =
        Enumerable.Range(MinimumEpsgCode, MaximumEpsgCode - MinimumEpsgCode + 1).ToArray();

    /// <summary>Every NAD83 UTM zone EPSG code this type can synthesize WKT for: 26901 through 26923 (zones 1 through 23).</summary>
    public static IReadOnlyList<int> SupportedEpsgCodes => SupportedEpsgCodesValue;

    /// <summary>Reports whether <paramref name="epsgCode"/> is one of <see cref="SupportedEpsgCodes"/>.</summary>
    public static bool IsSupported(int epsgCode) => epsgCode is >= MinimumEpsgCode and <= MaximumEpsgCode;

    /// <summary>Returns the UTM zone number (1-23) that <paramref name="epsgCode"/> names.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="epsgCode"/> is not one of <see cref="SupportedEpsgCodes"/>.</exception>
    public static int ZoneOf(int epsgCode)
    {
        if (!IsSupported(epsgCode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(epsgCode), epsgCode,
                $"EPSG code must be a NAD83 UTM zone code between {MinimumEpsgCode} and {MaximumEpsgCode}.");
        }

        return epsgCode - 26900;
    }

    /// <summary>
    /// Builds the ESRI-style WKT1 <c>COMPD_CS</c> text for <paramref name="epsgCode"/>'s NAD83 UTM zone,
    /// with a <c>VERT_CS</c> half built from <paramref name="vertical"/>. The text always uses "\n" line
    /// endings (never "\r\n", regardless of how this source file itself was checked out) and always ends
    /// with a single trailing "\n". <c>Create(26915, new VerticalReference("NAVD88", LengthUnit.Meter))</c>
    /// returns bytes identical to the committed <c>example-site-synthetic.prj</c> fixture.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="vertical"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="epsgCode"/> is not one of <see cref="SupportedEpsgCodes"/>.</exception>
    public static string Create(int epsgCode, VerticalReference vertical)
    {
        ArgumentNullException.ThrowIfNull(vertical);
        int zone = ZoneOf(epsgCode);
        int centralMeridian = 6 * zone - 183;

        string zoneText = zone.ToString(CultureInfo.InvariantCulture);
        string centralMeridianText = centralMeridian.ToString(CultureInfo.InvariantCulture) + ".0";
        string codeText = epsgCode.ToString(CultureInfo.InvariantCulture);
        string datum = vertical.Datum;
        string verticalUnitLiteral = VerticalUnitLiteral(vertical.Unit);

        string[] lines =
        [
            $"COMPD_CS[\"NAD_1983_UTM_Zone_{zoneText}N + {datum}_height\",",
            $"    PROJCS[\"NAD_1983_UTM_Zone_{zoneText}N\",",
            "        GEOGCS[\"GCS_North_American_1983\",",
            "            DATUM[\"NAD83\",",
            "                SPHEROID[\"GRS_1980\",6378137.0,298.257222101]],",
            "            PRIMEM[\"Greenwich\",0.0],",
            "            UNIT[\"Degree\",0.0174532925199433]],",
            "        PROJECTION[\"Transverse_Mercator\"],",
            "        PARAMETER[\"False_Easting\",500000.0],",
            "        PARAMETER[\"False_Northing\",0.0],",
            $"        PARAMETER[\"Central_Meridian\",{centralMeridianText}],",
            "        PARAMETER[\"Scale_Factor\",0.9996],",
            "        PARAMETER[\"Latitude_Of_Origin\",0.0],",
            "        UNIT[\"Meter\",1.0],",
            $"        AUTHORITY[\"EPSG\",\"{codeText}\"]],",
            $"    VERT_CS[\"{datum}_height\",",
            $"        VERT_DATUM[\"{datum}\",2005],",
            $"        UNIT[{verticalUnitLiteral}]]]",
        ];

        return (string.Join('\n', lines) + "\n").ReplaceLineEndings("\n");
    }

    /// <summary>
    /// The literal <c>UNIT[...]</c> argument text for a vertical reference's unit. Each factor is a fixed
    /// literal copied from the unit's own documented definition (AGENTS.md's "Data and numeric contracts"),
    /// never computed or formatted from <see cref="LengthConverter.MetersPerUnit(LengthUnit)"/>, so the
    /// synthesized text never depends on floating-point formatting.
    /// </summary>
    private static string VerticalUnitLiteral(LengthUnit unit) => unit switch
    {
        LengthUnit.Meter => "\"Meter\",1.0",
        LengthUnit.UsSurveyFoot => "\"Foot_US\",0.3048006096012192",
        LengthUnit.InternationalFoot => "\"Foot\",0.3048",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit for a vertical WKT unit clause."),
    };
}
