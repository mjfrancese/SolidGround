using System.Globalization;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Core.Sources.OpenTopography;

/// <summary>
/// One EPSG projected coordinate system this type can synthesize WKT for: a single UTM zone (10N through
/// 19N, which span the conterminous United States, though zone 19N also reaches Puerto Rico) under one of
/// the four verified NAD83 realizations. Because of that overlap, <see cref="OpenTopographyUsgs1mSource"/>
/// pairs a zone 19N result with the declared NAVD88 reference only when the request lies north of its
/// <see cref="OpenTopographyUsgs1mSource.MinimumConusLatitudeForZone19N"/> conterminous-latitude guard.
/// <see cref="ProjectedName"/>,
/// <see cref="GeographicName"/>, and <see cref="DatumName"/> are the literal ESRI-style names
/// <see cref="NorthAmericanUtmWellKnownText.Create"/> interpolates into its fixed WKT1 template;
/// <see cref="GeographicEpsgCode"/> is recorded for documentation only and is never interpolated into the
/// synthesized text (the template's <c>GEOGCS</c> carries no <c>AUTHORITY</c> child, matching the committed
/// fixture). See docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis" section for
/// the epsg.io citation each of the 40 rows built from this record is verified against.
/// </summary>
public sealed record NorthAmericanUtmDefinition(
    int EpsgCode,
    string Realization,
    int Zone,
    string ProjectedName,
    string GeographicName,
    string DatumName,
    int GeographicEpsgCode);

/// <summary>
/// Synthesizes ESRI-style WKT1 <c>COMPD_CS</c> text for one of 40 verified NAD83-family UTM zone codes --
/// zones 10N through 19N, which span the conterminous United States, under NAD83, NAD83(HARN), NAD83(NSRS2007), or
/// NAD83(2011) -- paired with a declared vertical reference, from fixed literal templates -- the same shape
/// as the committed <c>example-site-synthetic.prj</c> fixture -- rather than from any general-purpose
/// WKT-generation library. Zone 19N also reaches Puerto Rico, which is why <see cref="OpenTopographyUsgs1mSource"/>
/// pairs a zone 19N result with the declared NAVD88 reference only when the request lies north of its
/// <see cref="OpenTopographyUsgs1mSource.MinimumConusLatitudeForZone19N"/> conterminous-latitude guard. Only
/// the matched <see cref="NorthAmericanUtmDefinition"/>'s projected, geographic,
/// and datum names, the zone's derived central meridian, and the EPSG code are interpolated into the
/// horizontal (<c>PROJCS</c>/<c>GEOGCS</c>) portion; the vertical (<c>VERT_CS</c>) portion interpolates the
/// declared datum name (in the <c>COMPD_CS</c> name, the <c>VERT_CS</c> name, and the <c>VERT_DATUM</c> name)
/// and a fixed per-unit literal chosen by the vertical unit. Every other token, including every numeric
/// literal of the projection, is copied verbatim from the fixture, so no floating-point formatting decision
/// can change the text. See docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis"
/// section.
///
/// SolidGround supports only zones 10N-19N: the declared NAVD88 vertical reference this source pairs with
/// every synthesized horizontal reference (see <see cref="OpenTopographyUsgs1mSourceOptions.DeclaredVerticalReference"/>)
/// is documented for the conterminous United States, while Alaska, Hawaii, Puerto Rico, the Virgin Islands,
/// and the territories use per-project or local vertical datums per the USGS Lidar Base Specification and
/// FAQ -- so a zone outside that range is never guessed at, even though EPSG registers codes for it.
/// </summary>
public static class NorthAmericanUtmWellKnownText
{
    private const int MinimumZone = 10;
    private const int MaximumZone = 19;

    /// <summary>
    /// The four verified NAD83 realizations, each carrying the EPSG projected code for its own zone 10N, the
    /// ESRI-style name templates <see cref="Create"/> interpolates, and the realization's own geographic EPSG
    /// code (documentation only). Every one of the resulting 40 codes was read from
    /// <c>https://epsg.io/&lt;code&gt;.wkt</c> on 2026-09-20 and confirmed to name "&lt;realization&gt; / UTM
    /// zone &lt;N&gt;N" over the GRS 1980 spheroid, Transverse Mercator, central meridian <c>6 * zone -
    /// 183</c>, false easting 500000, false northing 0, scale factor 0.9996, latitude of origin 0, unit
    /// metre -- see docs/architecture/opentopography-usgs1m-source.md's "GeoKey to WKT synthesis" section for
    /// the full 40-row citation table.
    /// </summary>
    private static readonly (string Realization, int EpsgCodeForZone10, string ProjectedNameFormat, string GeographicName, string DatumName, int GeographicEpsgCode)[] Realizations =
    [
        ("NAD83", 26910, "NAD_1983_UTM_Zone_{0}N", "GCS_North_American_1983", "NAD83", 4269),
        ("NAD83(HARN)", 3740, "NAD_1983_HARN_UTM_Zone_{0}N", "GCS_North_American_1983_HARN", "NAD83(HARN)", 4152),
        ("NAD83(NSRS2007)", 3717, "NAD_1983_NSRS2007_UTM_Zone_{0}N", "GCS_NAD_1983_NSRS2007", "NAD83(NSRS2007)", 4759),
        ("NAD83(2011)", 6339, "NAD_1983_2011_UTM_Zone_{0}N", "GCS_NAD_1983_2011", "NAD83(2011)", 6318),
    ];

    private static readonly IReadOnlyList<NorthAmericanUtmDefinition> DefinitionsValue = BuildDefinitions();

    private static readonly Dictionary<int, NorthAmericanUtmDefinition> DefinitionsByCode =
        DefinitionsValue.ToDictionary(definition => definition.EpsgCode);

    private static readonly IReadOnlyList<int> SupportedEpsgCodesValue =
        [.. DefinitionsValue.Select(definition => definition.EpsgCode)];

    private static IReadOnlyList<NorthAmericanUtmDefinition> BuildDefinitions()
    {
        var definitions = new List<NorthAmericanUtmDefinition>();
        foreach ((string realization, int epsgCodeForZone10, string projectedNameFormat, string geographicName, string datumName, int geographicEpsgCode) in Realizations)
        {
            for (int zone = MinimumZone; zone <= MaximumZone; zone++)
            {
                int epsgCode = epsgCodeForZone10 + (zone - MinimumZone);
                string zoneText = zone.ToString(CultureInfo.InvariantCulture);
                string projectedName = string.Format(CultureInfo.InvariantCulture, projectedNameFormat, zoneText);
                definitions.Add(new NorthAmericanUtmDefinition(epsgCode, realization, zone, projectedName, geographicName, datumName, geographicEpsgCode));
            }
        }

        return [.. definitions.OrderBy(definition => definition.EpsgCode)];
    }

    /// <summary>Every verified NAD83-family UTM zone definition this type can synthesize WKT for, ascending by <see cref="NorthAmericanUtmDefinition.EpsgCode"/>.</summary>
    public static IReadOnlyList<NorthAmericanUtmDefinition> Definitions => DefinitionsValue;

    /// <summary>Every EPSG code in <see cref="Definitions"/>, ascending: the 40 supported NAD83-family UTM zone codes (10N-19N).</summary>
    public static IReadOnlyList<int> SupportedEpsgCodes => SupportedEpsgCodesValue;

    /// <summary>Reports whether <paramref name="epsgCode"/> is one of <see cref="SupportedEpsgCodes"/>.</summary>
    public static bool IsSupported(int epsgCode) => DefinitionsByCode.ContainsKey(epsgCode);

    /// <summary>Looks up the <see cref="NorthAmericanUtmDefinition"/> for <paramref name="epsgCode"/>, without throwing when it is unsupported.</summary>
    public static bool TryGetDefinition(int epsgCode, out NorthAmericanUtmDefinition? definition) =>
        DefinitionsByCode.TryGetValue(epsgCode, out definition);

    /// <summary>Returns the UTM zone number (10-19) that <paramref name="epsgCode"/> names.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="epsgCode"/> is not one of <see cref="SupportedEpsgCodes"/>.</exception>
    public static int ZoneOf(int epsgCode) => GetDefinitionOrThrow(epsgCode).Zone;

    private static NorthAmericanUtmDefinition GetDefinitionOrThrow(int epsgCode)
    {
        if (!TryGetDefinition(epsgCode, out NorthAmericanUtmDefinition? definition))
        {
            throw new ArgumentOutOfRangeException(
                nameof(epsgCode), epsgCode,
                "EPSG code must be one of the 40 supported NAD83-family UTM zone codes: zones 10N to 19N (the " +
                "conterminous United States) on NAD83, NAD83(HARN), NAD83(NSRS2007), or NAD83(2011).");
        }

        return definition!;
    }

    /// <summary>
    /// Builds the ESRI-style WKT1 <c>COMPD_CS</c> text for <paramref name="epsgCode"/>'s NAD83-family UTM
    /// zone, with a <c>VERT_CS</c> half built from <paramref name="vertical"/>. The text always uses "\n"
    /// line endings (never "\r\n", regardless of how this source file itself was checked out) and always
    /// ends with a single trailing "\n". <c>Create(26915, new VerticalReference("NAVD88", LengthUnit.Meter))</c>
    /// returns bytes identical to the committed <c>example-site-synthetic.prj</c> fixture.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="vertical"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="epsgCode"/> is not one of <see cref="SupportedEpsgCodes"/>.</exception>
    public static string Create(int epsgCode, VerticalReference vertical)
    {
        ArgumentNullException.ThrowIfNull(vertical);
        NorthAmericanUtmDefinition definition = GetDefinitionOrThrow(epsgCode);
        int centralMeridian = (6 * definition.Zone) - 183;

        string centralMeridianText = centralMeridian.ToString(CultureInfo.InvariantCulture) + ".0";
        string codeText = epsgCode.ToString(CultureInfo.InvariantCulture);
        string datum = vertical.Datum;
        string verticalUnitLiteral = VerticalUnitLiteral(vertical.Unit);

        string[] lines =
        [
            $"COMPD_CS[\"{definition.ProjectedName} + {datum}_height\",",
            $"    PROJCS[\"{definition.ProjectedName}\",",
            $"        GEOGCS[\"{definition.GeographicName}\",",
            $"            DATUM[\"{definition.DatumName}\",",
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
