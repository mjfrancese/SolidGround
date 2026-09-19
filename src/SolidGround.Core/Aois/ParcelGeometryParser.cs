using System.Globalization;
using System.Text.Json;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using SolidGround.Core.Metadata;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace SolidGround.Core.Aois;

/// <summary>
/// Parses a parcel boundary supplied as WKT or GeoJSON into a validated <see cref="PolygonalRegion"/>. This
/// parser is generic: it has no knowledge of any elevation source. WKT is read with NetTopologySuite's
/// <see cref="WKTReader"/>; GeoJSON (RFC 7946) is read with a bounded, hand-written reader over the inbox
/// <see cref="JsonDocument"/> rather than a further NetTopologySuite.IO package, so SolidGround can apply its
/// own validation (indexed error messages, the <c>crs</c> rule, and format-fixed axis order) without pulling
/// in Newtonsoft.Json or NetTopologySuite.Features. See docs/architecture/aoi-normalization-and-clipping.md
/// for the full rationale.
/// </summary>
public static class ParcelGeometryParser
{
    /// <summary>Parses <paramref name="aoi"/>'s geometry text using its own declared format and horizontal reference.</summary>
    public static PolygonalRegion Parse(ParcelGeometryAoi aoi)
    {
        ArgumentNullException.ThrowIfNull(aoi);
        return Parse(aoi.Format, aoi.Geometry, aoi.HorizontalReference);
    }

    /// <summary>Parses <paramref name="text"/> as <paramref name="format"/>, interpreting its ordinates under <paramref name="reference"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="text"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not a supported enum value.</exception>
    /// <exception cref="ParcelGeometryException">The text is not a well-formed, valid polygonal geometry.</exception>
    public static PolygonalRegion Parse(ParcelGeometryFormat format, string text, HorizontalReference reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(reference);

        return format switch
        {
            ParcelGeometryFormat.Wkt => ParseWkt(text, reference),
            ParcelGeometryFormat.GeoJson => ParseGeoJson(text, reference),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported parcel geometry format."),
        };
    }

    // ---- WKT -----------------------------------------------------------------------------------------

    /// <summary>
    /// Parses WKT with NetTopologySuite's <see cref="WKTReader"/>. <see cref="WKTReader"/> happily parses a
    /// third (Z) or fourth (M) ordinate — both a tagged <c>POLYGON Z (...)</c> and a legacy untagged
    /// three-ordinate ring — but only X and Y ever reach <see cref="PolygonalRegion"/>: the third and fourth
    /// ordinates are dropped when <see cref="PolygonalRegion.FromGeometry"/> builds its 2D
    /// <see cref="SolidGround.Core.Geometry.Coordinate2D"/> contract, mirroring how the GeoJSON path below
    /// ignores a position's third element.
    /// </summary>
    private static PolygonalRegion ParseWkt(string text, HorizontalReference reference)
    {
        text = StripLeadingBom(text);
        RejectEwktSridPrefix(text);

        WKTReader reader = new(GeometryInterop.Services);

        NtsGeometry geometry;
        try
        {
            geometry = reader.Read(text);
        }
        catch (ParseException ex)
        {
            throw new ParcelGeometryException($"The WKT parcel geometry could not be parsed: {TruncateForMessage(ex.Message)}", ex);
        }
        catch (ArgumentException ex)
        {
            // NTS's WKTReader throws a plain ArgumentException, with no ring index, for an unclosed ring
            // ("points must form a closed linestring"). A ring that is closed but too short is instead
            // accepted here and rejected later by PolygonalRegion.FromGeometry's IsValidOp check, which does
            // report a coordinate.
            throw new ParcelGeometryException(
                "The WKT parcel geometry is malformed: every ring must repeat its first coordinate as its last coordinate.",
                ex);
        }

        EnsureNoTrailingContent(text, geometry.IsEmpty);

        if (RequiresAxisSwap(reference))
        {
            geometry.Apply(SwapXyFilter.Instance);
            geometry.GeometryChanged();
        }

        return PolygonalRegion.FromGeometry(geometry, reference);
    }

    /// <summary>
    /// Strips one leading UTF-8 byte-order-mark character (U+FEFF), if present. Shared by both <see
    /// cref="ParseWkt"/> and <see cref="ParseGeoJson"/>: <see cref="char.IsWhiteSpace(char)"/> — and so <see
    /// cref="MemoryExtensions.TrimStart(ReadOnlySpan{char})"/>, which <see cref="RejectEwktSridPrefix"/> uses —
    /// does not treat U+FEFF as whitespace, since its Unicode category is Cf (format), not a space separator.
    /// Left unstripped, a BOM merges into <see cref="WKTReader"/>'s first token and makes it reject otherwise
    /// well-formed WKT with a confusing "Unknown type" message; confirmed against the actual System.Text.Json
    /// runtime, an unstripped BOM instead makes <see cref="JsonDocument.Parse(string,JsonDocumentOptions)"/>
    /// throw a <see cref="JsonException"/> ("'0xEF' is an invalid start of a value"), because it transcodes the
    /// string to UTF-8 and the BOM's first UTF-8 byte is not valid at the start of a JSON value. A leading BOM
    /// is common: it is the default save format for plain-text files written by common Windows tools such as
    /// Notepad and Excel.
    /// </summary>
    private static string StripLeadingBom(string text) => text.Length > 0 && text[0] == '\uFEFF' ? text[1..] : text;

    /// <summary>
    /// Truncates <paramref name="message"/> to a short, fixed length, without splitting a UTF-16 surrogate pair
    /// across the cut. Guards every SolidGround-authored message that might otherwise echo attacker-chosen-length
    /// content verbatim: NetTopologySuite's <see cref="WKTReader"/> tokenizer reads a maximal run of
    /// non-delimiter characters as one token and echoes that whole token back verbatim in <see
    /// cref="ParseException.Message"/> (for example <c>"Unknown type: &lt;token&gt;"</c>), and the GeoJSON
    /// reader below similarly echoes a document's own <c>type</c> value and object member names. Left
    /// untruncated, any of these could make <see cref="ParcelGeometryException.Message"/> balloon to the size of
    /// the entire input, contradicting this class's contract that a message never contains the complete original
    /// input text. A plain fixed-length code-unit slice is not by itself safe here: if a supplementary-plane
    /// character (for example an emoji) straddles the cut point, slicing at a fixed <see cref="string.Length"/>
    /// count would keep the character's high surrogate while dropping its paired low surrogate, leaving an
    /// unpaired surrogate immediately before the appended ellipsis and making the result ill-formed UTF-16.
    /// </summary>
    private static string TruncateForMessage(string message)
    {
        const int MaxLength = 200;
        if (message.Length <= MaxLength)
        {
            return message;
        }

        // Back off by one code unit when the cut would otherwise land between a surrogate pair, so the
        // truncated text is always well-formed UTF-16 (the low surrogate is simply dropped along with it).
        int cutLength = char.IsHighSurrogate(message[MaxLength - 1]) ? MaxLength - 1 : MaxLength;
        return string.Concat(message.AsSpan(0, cutLength), "…");
    }

    /// <summary>
    /// Rejects any WKT text that begins with a PostGIS EWKT <c>SRID=...;</c> prefix, before <see
    /// cref="WKTReader"/> ever sees it. <see cref="WKTReader"/> accepts that prefix as an extension of plain
    /// OGC WKT and stores the parsed numeral on the returned geometry's <c>SRID</c> property, but
    /// SolidGround's documented parcel format is plain OGC WKT: the caller's own <paramref name="reference"/>
    /// (its own <see cref="HorizontalReference.CoordinateReferenceSystem"/>) is the only source of truth for
    /// coordinate reference system, mirroring how the GeoJSON path validates a <c>crs</c> member against it
    /// rather than trusting an embedded declaration. Left unrejected, the prefix is also unsafe on its own
    /// terms — confirmed against the actual NetTopologySuite 2.6.0 runtime: an SRID numeral outside the <see
    /// cref="int"/> range (for example <c>SRID=99999999999999999999;...</c>) makes <see cref="WKTReader"/>
    /// throw a raw <see cref="OverflowException"/> from <c>Convert.ToInt32(double)</c>, and a prefix with no
    /// terminating <c>;</c> makes it throw a raw <see cref="NullReferenceException"/> — neither of which is a
    /// <see cref="ParcelGeometryException"/> as this class's documented contract promises. No standard OGC WKT
    /// geometry tag begins with the four letters <c>SRID</c>, so this check can never reject valid plain WKT.
    /// </summary>
    private static void RejectEwktSridPrefix(string text)
    {
        if (text.AsSpan().TrimStart().StartsWith("SRID", StringComparison.OrdinalIgnoreCase))
        {
            throw new ParcelGeometryException(
                "The WKT parcel geometry begins with an 'SRID=' prefix (the PostGIS EWKT extension); " +
                "SolidGround requires plain OGC WKT with no embedded coordinate reference system. Supply the " +
                "coordinate reference system via the AOI's own horizontal reference instead.");
        }
    }

    /// <summary>
    /// Rejects any non-whitespace content in <paramref name="text"/> left over after its first complete WKT
    /// geometry. <see cref="WKTReader.Read(string)"/> stops as soon as it has parsed one geometry and never
    /// checks that the whole input was consumed, so two concatenated <c>POLYGON (...)</c> literals or
    /// arbitrary trailing text would otherwise be silently accepted with everything past the first geometry
    /// dropped. This mirrors the trailing-content guard <see cref="WellKnownTextReferenceParser"/> already
    /// applies to its own WKT grammar. Every standard non-empty WKT geometry keeps its entire body inside the
    /// one parenthesis pair that opens right after its tag (a <c>GEOMETRYCOLLECTION</c> or <c>MULTIPOLYGON</c>
    /// nests every member inside that same outer pair), so matching parenthesis depth from the first
    /// <c>(</c> finds exactly where the geometry ends without re-implementing WKT's grammar. That scan is
    /// wrong, though, for a bare <c>TAG EMPTY</c> geometry (for example a lone <c>POLYGON EMPTY</c>): confirmed
    /// against the actual NetTopologySuite 2.6.0 runtime, <see cref="WKTReader"/> accepts the literal
    /// <c>EMPTY</c> keyword in place of a geometry's parenthesized body — never a paren-based form like
    /// <c>POLYGON ()</c>, which throws a <see cref="ParseException"/> — so that geometry's own text has no
    /// parenthesis at all, and scanning for the first <c>(</c> anywhere in <paramref name="text"/> would
    /// instead find (and wrongly bound the geometry to) a parenthesis belonging only to unrelated trailing
    /// content. But <paramref name="geometryIsEmpty"/> alone does not imply that bare form: a paren-list
    /// aggregate such as <c>GEOMETRYCOLLECTION (POLYGON EMPTY, POLYGON EMPTY)</c> or <c>MULTIPOLYGON (EMPTY,
    /// EMPTY)</c> is empty too (<c>Geometry.IsEmpty</c> is true whenever every member is empty) while still
    /// keeping real parentheses of its own, and the depth-matching scan is exactly correct for that form —
    /// balancing nested parens does not care whether the content inside is empty. The two forms are told
    /// apart by whichever comes first in <paramref name="text"/>: a <c>(</c> found before the first
    /// <c>EMPTY</c> keyword can only belong to the geometry's own paren-list (nothing between a geometry's
    /// tag and its own opening parenthesis could contain one), so the depth-matching scan applies; otherwise
    /// the geometry takes the bare form and its own true end is that first <c>EMPTY</c> keyword.
    /// </summary>
    private static void EnsureNoTrailingContent(string text, bool geometryIsEmpty)
    {
        const string EmptyKeyword = "EMPTY";
        int openParenIndex = text.IndexOf('(');
        int emptyKeywordIndex = geometryIsEmpty ? text.IndexOf(EmptyKeyword, StringComparison.OrdinalIgnoreCase) : -1;
        bool usesBareEmptyForm = geometryIsEmpty && (openParenIndex < 0 || emptyKeywordIndex < openParenIndex);

        int geometryEnd;
        if (usesBareEmptyForm)
        {
            geometryEnd = emptyKeywordIndex + EmptyKeyword.Length;
        }
        else if (openParenIndex >= 0)
        {
            int depth = 0;
            int index = openParenIndex;
            for (; index < text.Length; index++)
            {
                if (text[index] == '(')
                {
                    depth++;
                }
                else if (text[index] == ')')
                {
                    depth--;
                }

                if (depth == 0)
                {
                    index++;
                    break;
                }
            }

            geometryEnd = index;
        }
        else
        {
            geometryEnd = text.Length;
        }

        if (!string.IsNullOrWhiteSpace(text[geometryEnd..]))
        {
            throw new ParcelGeometryException(
                $"The WKT parcel geometry has {(text.Length - geometryEnd).ToString(CultureInfo.InvariantCulture)} " +
                "unexpected character(s) of trailing content after its first complete geometry, starting at " +
                $"position {geometryEnd.ToString(CultureInfo.InvariantCulture)}; SolidGround requires exactly one WKT geometry.");
        }
    }

    /// <summary>
    /// True when <paramref name="reference"/>'s axis order stores latitude/northing before longitude/easting,
    /// so WKT's X-then-Y ordinate order must be swapped into X=east/longitude, Y=north/latitude before
    /// <see cref="PolygonalRegion.FromGeometry"/> interprets it.
    /// </summary>
    private static bool RequiresAxisSwap(HorizontalReference reference) =>
        reference.AxisOrder is HorizontalAxisOrder.LatitudeLongitude or HorizontalAxisOrder.NorthingEasting;

    /// <summary>Swaps every coordinate's X and Y ordinate in place. Z, if present, is left untouched.</summary>
    private sealed class SwapXyFilter : ICoordinateFilter
    {
        public static readonly SwapXyFilter Instance = new();

        public void Filter(Coordinate coord) => (coord.X, coord.Y) = (coord.Y, coord.X);
    }

    // ---- GeoJSON ---------------------------------------------------------------------------------------

    private static PolygonalRegion ParseGeoJson(string text, HorizontalReference reference)
    {
        if (IsAxisOrderUnsupportedForGeoJson(reference))
        {
            throw new ParcelGeometryException(
                "GeoJSON positions are always ordered [longitude/easting, latitude/northing]. Supply a horizontal " +
                "reference whose axis order agrees: LongitudeLatitude for a geographic reference, or EastingNorthing " +
                "for a projected reference.");
        }

        using JsonDocument document = ParseJsonDocument(StripLeadingBom(text));
        JsonElement root = document.RootElement;
        EnsureNoDuplicatePropertyNames(root, "$");
        EnsureIsObject(root, "$");
        ValidateCrs(root, reference, "$");

        GeometryFactory factory = GeometryInterop.Services.CreateGeometryFactory();
        List<Polygon> polygons = [];
        ParseGeoJsonTopLevel(root, factory, polygons, "$", reference);

        if (polygons.Count == 0)
        {
            throw new ParcelGeometryException("The GeoJSON parcel geometry contained no polygonal geometry.");
        }

        NtsGeometry combined = polygons.Count == 1
            ? polygons[0]
            : factory.CreateMultiPolygon([.. polygons]);

        return PolygonalRegion.FromGeometry(combined, reference);
    }

    private static bool IsAxisOrderUnsupportedForGeoJson(HorizontalReference reference) =>
        reference.Kind == HorizontalReferenceKind.Geographic
            ? reference.AxisOrder == HorizontalAxisOrder.LatitudeLongitude
            : reference.AxisOrder == HorizontalAxisOrder.NorthingEasting;

    private static JsonDocument ParseJsonDocument(string text)
    {
        try
        {
            return JsonDocument.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new ParcelGeometryException($"The GeoJSON parcel geometry could not be parsed as JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Recursively rejects any JSON object anywhere in <paramref name="element"/>'s tree that repeats a
    /// member name. <see cref="JsonDocument"/> does not reject duplicate names on its own: it silently keeps
    /// every occurrence, and the last one wins whenever a property is looked up by name.
    /// </summary>
    private static void EnsureNoDuplicatePropertyNames(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                HashSet<string> seen = new(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (!seen.Add(property.Name))
                    {
                        throw new ParcelGeometryException($"The GeoJSON value at {path} has a duplicate '{TruncateForMessage(property.Name)}' member.");
                    }

                    EnsureNoDuplicatePropertyNames(property.Value, $"{path}.{property.Name}");
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    EnsureNoDuplicatePropertyNames(item, $"{path}[{index.ToString(CultureInfo.InvariantCulture)}]");
                    index++;
                }

                break;
        }
    }

    /// <summary>
    /// The exact <c>crs</c> member <c>name</c> values (case-insensitive) <see cref="ValidateCrs"/> recognizes as
    /// WGS 84: the pre-RFC7946 GeoJSON crs convention's own CRS84 URN and OGC URI forms, and the common
    /// EPSG:4326 URN and short forms. Matching is exact, not a substring search, so a name that merely contains
    /// "CRS84" or "4326" somewhere — for example the unrelated EPSG code "urn:ogc:def:crs:EPSG::104326", or
    /// arbitrary text such as "Fantasy Local Grid 4326-East" — is correctly rejected rather than silently
    /// treated as an agreeing WGS 84 declaration. "Exact" is after trimming incidental leading/trailing
    /// whitespace from the declared name, intentionally, so a value padded with stray whitespace from
    /// hand-edited JSON (for example " EPSG:4326 ") is still recognized; internal whitespace is not trimmed
    /// and so is never tolerated (for example "EPSG: 4326" is still correctly rejected).
    /// </summary>
    private static readonly HashSet<string> RecognizedWgs84CrsNames =
    [
        "CRS84",
        "EPSG:4326",
        "URN:OGC:DEF:CRS:OGC:1.3:CRS84",
        "URN:OGC:DEF:CRS:OGC::CRS84",
        "URN:OGC:DEF:CRS:EPSG::4326",
        "HTTP://WWW.OPENGIS.NET/DEF/CRS/OGC/1.3/CRS84",
    ];

    /// <summary>
    /// Validates an optional <c>crs</c> member on the GeoJSON object at <paramref name="path"/> (the document
    /// root, a Feature, a FeatureCollection, or a Geometry — the older, pre-RFC7946 GeoJSON convention this
    /// parser otherwise tries to support allows a <c>crs</c> member on any of those). GeoJSON's own default
    /// coordinate reference system is WGS 84 (CRS84, equivalent to EPSG:4326); SolidGround accepts an explicit
    /// <c>crs</c> member only when it names one of those and the caller's own <paramref name="reference"/>
    /// agrees that the data is geographic, so there is never a silent mismatch between the two. The caller is
    /// responsible for invoking this at every level a <c>crs</c> member could legally appear — <see
    /// cref="ParseGeoJson"/> validates the root, <see cref="ParseFeature"/> validates each Feature, and <see
    /// cref="ParseGeoJsonGeometry"/> validates each Geometry, including every recursively nested
    /// GeometryCollection member — so a <c>crs</c> declared anywhere but the root cannot be silently ignored.
    /// The <c>crs</c> object's own <c>type</c> member must be the string <c>"name"</c> (the pre-RFC7946
    /// convention's discriminator between a <c>name</c> object and a <c>link</c> object) before its
    /// <c>properties.name</c> is ever consulted; a <c>link</c> object, or any other or missing <c>type</c>, is
    /// therefore never recognized, even if it happens to carry a coincidental <c>properties.name</c> value.
    /// </summary>
    private static void ValidateCrs(JsonElement element, HorizontalReference reference, string path)
    {
        if (!element.TryGetProperty("crs", out JsonElement crsElement) || crsElement.ValueKind == JsonValueKind.Null)
        {
            return;
        }

        string? name = null;
        if (crsElement.ValueKind == JsonValueKind.Object
            && crsElement.TryGetProperty("type", out JsonElement crsTypeElement)
            && crsTypeElement.ValueKind == JsonValueKind.String
            && crsTypeElement.GetString() == "name"
            && crsElement.TryGetProperty("properties", out JsonElement properties)
            && properties.ValueKind == JsonValueKind.Object
            && properties.TryGetProperty("name", out JsonElement nameElement)
            && nameElement.ValueKind == JsonValueKind.String)
        {
            name = nameElement.GetString();
        }

        bool recognized = name is not null && RecognizedWgs84CrsNames.Contains(name.Trim().ToUpperInvariant());
        if (!recognized || reference.Kind != HorizontalReferenceKind.Geographic)
        {
            throw new ParcelGeometryException(
                $"The GeoJSON value at {path} declares a 'crs' member SolidGround does not recognize as WGS 84 " +
                "(CRS84 or EPSG:4326) together with a geographic horizontal reference. Supply the horizontal " +
                "reference explicitly instead of relying on an embedded crs member.");
        }
    }

    /// <summary>Dispatches a top-level GeoJSON value: Polygon, MultiPolygon, GeometryCollection, Feature, or FeatureCollection.</summary>
    private static void ParseGeoJsonTopLevel(JsonElement element, GeometryFactory factory, List<Polygon> output, string path, HorizontalReference reference)
    {
        EnsureIsObject(element, path);
        string type = ReadRequiredString(element, "type", path);
        switch (type)
        {
            case "Feature":
                ParseFeature(element, factory, output, path, reference);
                break;

            case "FeatureCollection":
                JsonElement features = RequireProperty(element, "features", path);
                RequireArray(features, $"{path}.features");
                int featureIndex = 0;
                foreach (JsonElement feature in features.EnumerateArray())
                {
                    ParseFeature(feature, factory, output, $"feature {featureIndex.ToString(CultureInfo.InvariantCulture)}", reference);
                    featureIndex++;
                }

                break;

            default:
                ParseGeoJsonGeometry(element, factory, output, path, reference);
                break;
        }
    }

    private static void ParseFeature(JsonElement featureElement, GeometryFactory factory, List<Polygon> output, string path, HorizontalReference reference)
    {
        EnsureIsObject(featureElement, path);
        ValidateCrs(featureElement, reference, path);
        if (!featureElement.TryGetProperty("geometry", out JsonElement geometryElement) || geometryElement.ValueKind == JsonValueKind.Null)
        {
            throw new ParcelGeometryException($"The GeoJSON feature at {path} has a null or missing geometry.");
        }

        ParseGeoJsonGeometry(geometryElement, factory, output, $"{path}.geometry", reference);
    }

    /// <summary>Parses a GeoJSON geometry object: Polygon, MultiPolygon, or a GeometryCollection of those (recursive).</summary>
    private static void ParseGeoJsonGeometry(JsonElement element, GeometryFactory factory, List<Polygon> output, string path, HorizontalReference reference)
    {
        EnsureIsObject(element, path);
        ValidateCrs(element, reference, path);
        string type = ReadRequiredString(element, "type", path);
        switch (type)
        {
            case "Polygon":
                output.Add(ParsePolygonCoordinates(RequireProperty(element, "coordinates", path), factory, path));
                break;

            case "MultiPolygon":
                JsonElement polygonsElement = RequireProperty(element, "coordinates", path);
                RequireArray(polygonsElement, $"{path}.coordinates");
                if (polygonsElement.GetArrayLength() == 0)
                {
                    throw new ParcelGeometryException($"The GeoJSON value at {path}.coordinates is empty; SolidGround requires non-empty polygonal geometry.");
                }

                int polygonIndex = 0;
                foreach (JsonElement polygonCoordinates in polygonsElement.EnumerateArray())
                {
                    output.Add(ParsePolygonCoordinates(polygonCoordinates, factory, $"{path}, polygon {polygonIndex.ToString(CultureInfo.InvariantCulture)}"));
                    polygonIndex++;
                }

                break;

            case "GeometryCollection":
                JsonElement geometries = RequireProperty(element, "geometries", path);
                RequireArray(geometries, $"{path}.geometries");
                if (geometries.GetArrayLength() == 0)
                {
                    throw new ParcelGeometryException($"The GeoJSON value at {path}.geometries is empty; SolidGround requires non-empty polygonal geometry.");
                }

                int memberIndex = 0;
                foreach (JsonElement member in geometries.EnumerateArray())
                {
                    ParseGeoJsonGeometry(member, factory, output, $"{path}, member {memberIndex.ToString(CultureInfo.InvariantCulture)}", reference);
                    memberIndex++;
                }

                break;

            default:
                throw new ParcelGeometryException(
                    $"The GeoJSON geometry at {path} has unsupported type '{TruncateForMessage(type)}'; only Polygon, MultiPolygon, and " +
                    "GeometryCollection (of polygonal members) are supported here.");
        }
    }

    private static Polygon ParsePolygonCoordinates(JsonElement coordinates, GeometryFactory factory, string path)
    {
        RequireArray(coordinates, $"{path}.coordinates");

        LinearRing? shell = null;
        List<LinearRing> holes = [];
        int ringIndex = 0;
        foreach (JsonElement ringElement in coordinates.EnumerateArray())
        {
            LinearRing ring = ParseRing(ringElement, factory, $"{path}, ring {ringIndex.ToString(CultureInfo.InvariantCulture)}");
            if (ringIndex == 0)
            {
                shell = ring;
            }
            else
            {
                holes.Add(ring);
            }

            ringIndex++;
        }

        if (shell is null)
        {
            throw new ParcelGeometryException($"The GeoJSON polygon at {path} has no rings.");
        }

        return factory.CreatePolygon(shell, [.. holes]);
    }

    private static LinearRing ParseRing(JsonElement ringElement, GeometryFactory factory, string path)
    {
        RequireArray(ringElement, path);

        List<Coordinate> positions = [];
        int positionIndex = 0;
        foreach (JsonElement positionElement in ringElement.EnumerateArray())
        {
            positions.Add(ParsePosition(positionElement, $"{path}, position {positionIndex.ToString(CultureInfo.InvariantCulture)}"));
            positionIndex++;
        }

        if (positions.Count < 4)
        {
            throw new ParcelGeometryException(
                $"The GeoJSON ring at {path} has {positions.Count.ToString(CultureInfo.InvariantCulture)} position(s), but a ring requires at least 4.");
        }

        Coordinate first = positions[0];
        Coordinate last = positions[^1];
        if (first.X != last.X || first.Y != last.Y)
        {
            throw new ParcelGeometryException(
                $"The GeoJSON ring at {path} is not closed: its first position ({GeometryInterop.FormatOrdinate(first.X)}, {GeometryInterop.FormatOrdinate(first.Y)}) " +
                $"must exactly equal its last position ({GeometryInterop.FormatOrdinate(last.X)}, {GeometryInterop.FormatOrdinate(last.Y)}).");
        }

        return factory.CreateLinearRing([.. positions]);
    }

    private static Coordinate ParsePosition(JsonElement positionElement, string path)
    {
        if (positionElement.ValueKind != JsonValueKind.Array)
        {
            throw new ParcelGeometryException($"The GeoJSON position at {path} must be an array of numbers.");
        }

        int count = positionElement.GetArrayLength();
        if (count is < 2 or > 3)
        {
            throw new ParcelGeometryException(
                $"The GeoJSON position at {path} has {count.ToString(CultureInfo.InvariantCulture)} element(s), but a position must have 2 or 3.");
        }

        double[] values = new double[count];
        int i = 0;
        foreach (JsonElement valueElement in positionElement.EnumerateArray())
        {
            if (valueElement.ValueKind != JsonValueKind.Number || !valueElement.TryGetDouble(out double value) || !double.IsFinite(value))
            {
                throw new ParcelGeometryException($"The GeoJSON position at {path} has a non-finite-number element at index {i.ToString(CultureInfo.InvariantCulture)}.");
            }

            values[i] = value;
            i++;
        }

        // The third ordinate (Z), when present, is intentionally ignored: PolygonalRegion is a 2D contract.
        return new Coordinate(values[0], values[1]);
    }

    private static void EnsureIsObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ParcelGeometryException($"The GeoJSON value at {path} must be a JSON object.");
        }
    }

    private static void RequireArray(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ParcelGeometryException($"The GeoJSON value at {path} must be a JSON array.");
        }
    }

    private static JsonElement RequireProperty(JsonElement obj, string propertyName, string path)
    {
        if (!obj.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new ParcelGeometryException($"The GeoJSON value at {path} is missing the required '{propertyName}' member.");
        }

        return value;
    }

    private static string ReadRequiredString(JsonElement obj, string propertyName, string path)
    {
        JsonElement value = RequireProperty(obj, propertyName, path);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new ParcelGeometryException($"The GeoJSON value at {path}'s '{propertyName}' member must be a string.");
        }

        return value.GetString() ?? throw new ParcelGeometryException($"The GeoJSON value at {path}'s '{propertyName}' member must be a string.");
    }
}
