using System.Globalization;
using SolidGround.Core.Aois;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ParcelGeometryParserTests
{
    [Fact]
    public void ParseAcceptsAParcelGeometryAoiDirectly()
    {
        ParcelGeometryAoi aoi = new(ParcelGeometryFormat.Wkt, "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", ProjectedReference());

        PolygonalRegion region = ParcelGeometryParser.Parse(aoi);

        Assert.Equal(16d, region.Area);
    }

    [Fact]
    public void ParseRejectsANullAoiOrNullReferenceOrBlankText()
    {
        Assert.Throws<ArgumentNullException>(() => ParcelGeometryParser.Parse(null!));
        Assert.Throws<ArgumentException>(() => ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, "   ", ProjectedReference()));
        Assert.Throws<ArgumentNullException>(() => ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, "POLYGON ((0 0, 1 0, 1 1, 0 0))", null!));
    }

    [Fact]
    public void ParseRejectsAnUnsupportedFormatEnumValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ParcelGeometryParser.Parse((ParcelGeometryFormat)99, "irrelevant", ProjectedReference()));
    }

    // ---- WKT -------------------------------------------------------------------------------------------

    [Fact]
    public void WktParsesASimplePolygon()
    {
        PolygonalRegion region = ParseWkt("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))");

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(16d, region.Area);
    }

    [Fact]
    public void WktParsesAPolygonWithAHole()
    {
        PolygonalRegion region = ParseWkt("POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (2 2, 2 3, 3 3, 3 2, 2 2))");

        Assert.Equal(1, region.HoleCount);
    }

    [Fact]
    public void WktParsesAMultiPolygon()
    {
        PolygonalRegion region = ParseWkt("MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))");

        Assert.Equal(2, region.PolygonCount);
    }

    [Fact]
    public void WktFlattensAGeometryCollectionOfPolygons()
    {
        PolygonalRegion region = ParseWkt("GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 0)), POLYGON ((5 5, 6 5, 6 6, 5 5)))");

        Assert.Equal(2, region.PolygonCount);
    }

    [Fact]
    public void WktFlattensAGeometryCollectionContainingAMultiPolygonMember()
    {
        PolygonalRegion region = ParseWkt(
            "GEOMETRYCOLLECTION (POLYGON ((0 0, 1 0, 1 1, 0 0)), " +
            "MULTIPOLYGON (((5 5, 6 5, 6 6, 5 5)), ((8 8, 9 8, 9 9, 8 8))))");

        Assert.Equal(3, region.PolygonCount);
    }

    [Fact]
    public void WktRejectsAnUnclosedRingWithACoarseMessageAndNoIndexPromised()
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt("POLYGON ((0 0, 1 0, 1 1, 0 1))"));

        Assert.Contains("repeat its first coordinate as its last", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WktRejectsAThreeCoordinateClosedRingViaTooFewPoints()
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt("POLYGON ((0 0, 1 0, 0 0))"));

        Assert.Contains("Too few points", error.Message, StringComparison.Ordinal);
        Assert.Contains("(0, 0)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WktRejectsABowTieSelfIntersectionWithACoordinate()
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt("POLYGON ((0 0, 1 1, 1 0, 0 1, 0 0))"));

        Assert.Contains("Self-intersection", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(0.5, 0.5)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WktRejectsAHoleOutsideTheShellWithACoordinate()
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(
            "POLYGON ((0 0, 10 0, 10 10, 0 10, 0 0), (20 20, 21 20, 21 21, 20 21, 20 20))"));

        Assert.Contains("Hole lies outside shell", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(20, 20)", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POINT (0 0)")]
    [InlineData("LINESTRING (0 0, 1 1)")]
    [InlineData("POLYGON EMPTY")]
    [InlineData("GEOMETRYCOLLECTION (POINT (0 0), POLYGON ((0 0, 1 0, 1 1, 0 0)))")]
    public void WktRejectsNonPolygonalOrEmptyOrMixedGeometry(string wkt)
    {
        Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));
    }

    [Fact]
    public void WktRejectsTrailingGarbageAfterAValidPolygon()
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(
            () => ParseWkt("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)) THIS_IS_TRAILING_GARBAGE_THAT_SHOULD_NOT_BE_HERE"));

        Assert.Contains("trailing content", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WktRejectsTwoConcatenatedPolygonsInsteadOfSilentlyKeepingOnlyTheFirst()
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(
            () => ParseWkt("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0)) POLYGON ((100 100, 101 100, 101 101, 100 100))"));

        Assert.Contains("trailing content", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("POLYGON EMPTY POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")]
    [InlineData("MULTIPOLYGON EMPTY MULTIPOLYGON (((0 0, 1 0, 1 1, 0 0)))")]
    [InlineData("GEOMETRYCOLLECTION EMPTY POLYGON ((0 0, 1 0, 1 1, 0 0))")]
    public void WktRejectsTrailingContentAfterAnEmptyGeometryInsteadOfReportingItAsEmpty(string wkt)
    {
        // A bare "TAG EMPTY" geometry has no parenthesis of its own, so the trailing-content scan must not
        // latch onto a parenthesis belonging only to this unrelated, appended second geometry and thereby
        // consume (and silently discard) it as if it were part of the first, empty geometry.
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));

        Assert.Contains("trailing content", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("GEOMETRYCOLLECTION (POLYGON EMPTY, POLYGON EMPTY)")]
    [InlineData("GEOMETRYCOLLECTION (POLYGON EMPTY, MULTIPOLYGON EMPTY)")]
    [InlineData("MULTIPOLYGON (EMPTY, EMPTY)")]
    [InlineData("GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POLYGON EMPTY))")]
    public void WktReportsAnAllEmptyAggregateGeometryAsEmptyRatherThanAsTrailingContent(string wkt)
    {
        // Unlike the bare "TAG EMPTY" cases above, each of these is one complete, well-formed WKT literal
        // whose own text genuinely does contain parentheses (NTS's WKTReader also accepts a paren-list of
        // EMPTY members), and geometry.IsEmpty is true only because every member nested inside is itself
        // empty. The trailing-content scan must recognize this as the geometry's own paren-list form —
        // finding no trailing content at all — and let PolygonalRegion.FromGeometry report the accurate
        // "is empty" diagnosis, rather than misreading the geometry's own closing parentheses as unexpected
        // trailing content.
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));

        Assert.Contains("is empty", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("trailing content", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WktRejectsRealTrailingContentAfterAnAllEmptyAggregateGeometry()
    {
        // Real trailing content after a paren-list all-empty aggregate must still be reported accurately: the
        // depth-matching scan (now also used for this form) must find the aggregate's own true end, at its
        // outermost closing parenthesis (position 49, right after the geometry's own closing ')'), rather
        // than latching onto the first "EMPTY" keyword nested inside it (which would instead wrongly report
        // the closing ", POLYGON EMPTY)" of the geometry's own second member as if it were garbage).
        const string wkt = "GEOMETRYCOLLECTION (POLYGON EMPTY, POLYGON EMPTY) ZZZGARBAGE";
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));

        Assert.Contains("trailing content", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("11 unexpected character(s)", error.Message, StringComparison.Ordinal);
        Assert.Contains("starting at position 49", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WktToleratesTrailingWhitespaceAfterAValidPolygon()
    {
        PolygonalRegion region = ParseWkt("POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))  \n  ");

        Assert.Equal(16d, region.Area);
    }

    [Theory]
    [InlineData("POLYGON Z ((0 0 1, 4 0 2, 4 4 3, 0 4 4, 0 0 1))")]
    [InlineData("POLYGON ((0 0 1, 4 0 2, 4 4 3, 0 4 4, 0 0 1))")]
    public void WktParsesZVariantsAndDropsTheZOrdinate(string wkt)
    {
        PolygonalRegion region = ParseWkt(wkt);

        PolygonRings polygon = Assert.Single(region.Polygons);
        Assert.Equal(new Coordinate2D(0, 0), polygon.Shell[0]);
        Assert.Equal(16d, region.Area);
    }

    [Fact]
    public void WktSwapsLatitudeLongitudeAxisOrderIntoLonLat()
    {
        HorizontalReference latitudeLongitude = GeographicReference(HorizontalAxisOrder.LatitudeLongitude);

        PolygonalRegion region = ParcelGeometryParser.Parse(
            ParcelGeometryFormat.Wkt,
            "POLYGON (([withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld], [withheld] [withheld]))",
            latitudeLongitude);

        PolygonRings polygon = Assert.Single(region.Polygons);
        Assert.Equal(new Coordinate2D([withheld], [withheld]), polygon.Shell[0]);
    }

    [Fact]
    public void WktRejectsGeographicRangeViolations()
    {
        Assert.Throws<ParcelGeometryException>(() => ParseWkt("POLYGON ((0 95, 1 95, 1 96, 0 96, 0 95))", GeographicReference()));
    }

    [Fact]
    public void WktParsingIsInvariantUnderFrFrCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            PolygonalRegion region = ParseWkt("POLYGON ((0 0, 1.5 0, 1.5 1.5, 0 1.5, 0 0))");

            Assert.Equal(2.25d, region.Area, 12);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void WktParsingIsDeterministicAcrossRepeatedCalls()
    {
        const string wkt = "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 1 2, 2 2, 2 1, 1 1))";

        PolygonalRegion first = ParseWkt(wkt);
        PolygonalRegion second = ParseWkt(wkt);

        Assert.Equal(first.Polygons.Count, second.Polygons.Count);
        Assert.Equal(first.Polygons[0].Shell, second.Polygons[0].Shell);
        Assert.Equal(first.Polygons[0].Holes[0], second.Polygons[0].Holes[0]);
    }

    [Fact]
    public void WktErrorMessageNeverContainsTheFullInputText()
    {
        string longWkt = "POLYGON ((" + string.Join(", ", Enumerable.Range(0, 5000).Select(i => $"{i} {i}")) + "))";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(longWkt));

        Assert.True(longWkt.Length > 20000);
        Assert.True(error.Message.Length < 1000);
        Assert.DoesNotContain(longWkt, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WktParseExceptionMessageIsBoundedEvenForAHugeMalformedToken()
    {
        // Unlike the unclosed-ring case above (caught as a plain ArgumentException with a short canned
        // message), an unrecognized leading token is caught as a NetTopologySuite ParseException, whose own
        // Message echoes the offending token back verbatim. NTS's tokenizer reads a maximal run of
        // non-delimiter characters as one token, so a huge malformed token must not make
        // ParcelGeometryException.Message balloon to the size of the input.
        string hugeToken = new string('X', 5_000_000);
        string wkt = hugeToken + " ((0 0, 4 0, 4 4, 0 4, 0 0))";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));

        Assert.True(error.Message.Length < 1000);
        Assert.DoesNotContain(hugeToken, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WktParseExceptionMessageDoesNotSplitASurrogatePairAtTheTruncationBoundary()
    {
        // TruncateForMessage's fixed 200-code-unit cut must not split a UTF-16 surrogate pair. NTS's own
        // "Unknown type: " prefix is 14 characters, so a malformed token with a supplementary-plane character
        // (here an emoji, a surrogate pair) at token-relative index 185 places that character's high surrogate
        // exactly at message index 199 — the last code unit a naive fixed-length cut would otherwise keep —
        // with its paired low surrogate at message index 200, just past the cut.
        string emoji = "\U0001F600";
        string hugeToken = new string('X', 185) + emoji;
        string wkt = hugeToken + " ((0 0, 4 0, 4 4, 0 4, 0 0))";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));

        Assert.True(error.Message.Length < 1000);
        Assert.True(IsWellFormedUtf16(error.Message));
    }

    // ---- WKT: PostGIS EWKT "SRID=...;" prefix is rejected, not silently trusted or left to crash ---------

    [Theory]
    [InlineData("SRID=99999999999999999999;POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")] // SRID numeral overflows Int32
    [InlineData("SRID=2147483648;POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")] // Int32.MaxValue + 1
    [InlineData("srid=4326;POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")] // lowercase
    [InlineData("  SRID=4326;POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")] // leading whitespace
    [InlineData("SRID=4326 POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))")] // missing ';' (WKTReader throws NullReferenceException)
    public void WktRejectsAnEwktSridPrefixWithAParcelGeometryExceptionInsteadOfAnUnrelatedRawException(string wkt)
    {
        // NTS's WKTReader accepts the PostGIS "SRID=<n>;" extension, but an out-of-Int32-range numeral makes it
        // throw a raw OverflowException and a missing ';' makes it throw a raw NullReferenceException — neither
        // caught by ParseWkt's ParseException/ArgumentException handling, so both would otherwise escape as an
        // undocumented exception type instead of ParcelGeometryException.
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseWkt(wkt));

        Assert.Contains("SRID=", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void WktRejectsAnEwktSridPrefixEvenWhenItNamesTheSameCrsAsTheHorizontalReference()
    {
        // SolidGround's declared parcel format is plain OGC WKT: the horizontal reference is always supplied
        // out of band by the caller, never read back out of the WKT text itself, so even a well-formed,
        // agreeing SRID prefix is rejected rather than silently accepted (or silently mismatched, as an
        // EPSG:26915-declared reference paired with an unchecked "SRID=4326;" prefix would otherwise be).
        Assert.Throws<ParcelGeometryException>(
            () => ParseWkt("SRID=4326;POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))", GeographicReference()));
    }

    // ---- WKT: a leading UTF-8 byte-order mark does not defeat parsing or the SRID check -------------------

    [Fact]
    public void WktAcceptsALeadingByteOrderMark()
    {
        // U+FEFF is not whitespace under char.IsWhiteSpace, so it survives TrimStart and, left unstripped,
        // merges into WKTReader's first token, rejecting otherwise well-formed WKT. A leading BOM is the
        // default save format for plain-text files written by common Windows tools such as Notepad and Excel.
        PolygonalRegion region = ParseWkt("﻿POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))");

        Assert.Equal(16d, region.Area);
    }

    [Fact]
    public void WktRejectsAnEwktSridPrefixEvenBehindALeadingByteOrderMark()
    {
        // Stripping the BOM must happen before the SRID check, not instead of it, so a BOM cannot be used to
        // sneak an EWKT SRID prefix past RejectEwktSridPrefix either.
        Assert.Throws<ParcelGeometryException>(
            () => ParseWkt("﻿SRID=4326;POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0))"));
    }

    // ---- GeoJSON ---------------------------------------------------------------------------------------

    [Fact]
    public void GeoJsonParsesAPolygon()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[4,0],[4,4],[0,4],[0,0]]]}""";

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(1, region.PolygonCount);
        Assert.Equal(16d, region.Area);
    }

    [Fact]
    public void GeoJsonParsesAPolygonWithAHole()
    {
        const string json = """
            {"type":"Polygon","coordinates":[
                [[0,0],[10,0],[10,10],[0,10],[0,0]],
                [[2,2],[2,3],[3,3],[3,2],[2,2]]
            ]}
            """;

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(1, region.HoleCount);
    }

    [Fact]
    public void GeoJsonParsesAMultiPolygon()
    {
        const string json = """
            {"type":"MultiPolygon","coordinates":[
                [[[0,0],[1,0],[1,1],[0,0]]],
                [[[5,5],[6,5],[6,6],[5,5]]]
            ]}
            """;

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(2, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonParsesAFeature()
    {
        const string json = """{"type":"Feature","properties":{},"geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}""";

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(1, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonParsesAFeatureCollectionOfTwoPolygonFeatures()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
                {"type":"Feature","properties":{},"geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}},
                {"type":"Feature","properties":{},"geometry":{"type":"Polygon","coordinates":[[[5,5],[6,5],[6,6],[5,5]]]}}
            ]}
            """;

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(2, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonFlattensAFeatureCollectionFeatureThatIsAMultiPolygon()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
                {"type":"Feature","properties":{},"geometry":{"type":"MultiPolygon","coordinates":[
                    [[[0,0],[1,0],[1,1],[0,0]]],
                    [[[5,5],[6,5],[6,6],[5,5]]]
                ]}}
            ]}
            """;

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(2, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonParsesAGeometryCollection()
    {
        const string json = """
            {"type":"GeometryCollection","geometries":[
                {"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]},
                {"type":"Polygon","coordinates":[[[5,5],[6,5],[6,6],[5,5]]]}
            ]}
            """;

        PolygonalRegion region = ParseGeoJson(json);

        Assert.Equal(2, region.PolygonCount);
    }

    [Theory]
    [InlineData("""{"type":"Point","coordinates":[0,0]}""")]
    [InlineData("""{"type":"LineString","coordinates":[[0,0],[1,1]]}""")]
    [InlineData("""{"type":"Feature","properties":{},"geometry":{"type":"Point","coordinates":[0,0]}}""")]
    public void GeoJsonRejectsNonPolygonalTopLevelTypes(string json)
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("unsupported type", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoJsonUnsupportedTypeErrorMessageIsBoundedEvenForAHugeTypeValue()
    {
        // Mirrors WktParseExceptionMessageIsBoundedEvenForAHugeMalformedToken: ParseGeoJsonGeometry's
        // unsupported-type branch echoes the document's own "type" value verbatim into the exception message,
        // so a huge "type" value must not make ParcelGeometryException.Message balloon to the size of the input.
        string hugeType = new string('T', 5_000_000);
        string json = "{\"type\":\"" + hugeType + "\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.True(error.Message.Length < 1000);
        Assert.DoesNotContain(hugeType, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeoJsonRejectsAnEmptyMultiPolygonMemberInAGeometryCollection()
    {
        const string json = """
            {"type":"GeometryCollection","geometries":[
                {"type":"MultiPolygon","coordinates":[]},
                {"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}
            ]}
            """;

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("is empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoJsonRejectsAnEmptyGeometryCollectionMemberNestedInAnotherGeometryCollection()
    {
        const string json = """
            {"type":"GeometryCollection","geometries":[
                {"type":"GeometryCollection","geometries":[]},
                {"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}
            ]}
            """;

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("is empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoJsonRejectsAFeatureCollectionFeatureThatIsAnEmptyMultiPolygon()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
                {"type":"Feature","properties":{},"geometry":{"type":"MultiPolygon","coordinates":[]}},
                {"type":"Feature","properties":{},"geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}
            ]}
            """;

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("is empty", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoJsonRejectsAnUnclosedRingWithRingIndexAndCoordinates()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,1]]]}""";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("ring 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("not closed", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("(0, 0)", error.Message, StringComparison.Ordinal);
        Assert.Contains("(0, 1)", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeoJsonRejectsAThreePositionRingWithRingIndex()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[1,0],[0,0]]]}""";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("ring 0", error.Message, StringComparison.Ordinal);
        Assert.Contains("at least 4", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("""{"type":"Polygon","coordinates":[[[0],[1,0],[1,1],[0,0]]]}""")]
    [InlineData("""{"type":"Polygon","coordinates":[[["a",0],[1,0],[1,1],[0,0]]]}""")]
    public void GeoJsonRejectsAMalformedPositionWithPositionIndex(string json)
    {
        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("position 0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeoJsonRejectsANullGeometryInAFeature()
    {
        const string json = """{"type":"Feature","properties":{},"geometry":null}""";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("null or missing geometry", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoJsonRejectsMalformedJson()
    {
        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson("{not valid json"));
    }

    [Fact]
    public void GeoJsonRejectsTrailingContent()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}   garbage""";

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));
    }

    [Fact]
    public void GeoJsonRejectsADuplicateKey()
    {
        const string json = """{"type":"Polygon","type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GeoJsonDuplicateKeyErrorMessageIsBoundedEvenForAHugeDuplicatedPropertyName()
    {
        // Mirrors WktParseExceptionMessageIsBoundedEvenForAHugeMalformedToken: EnsureNoDuplicatePropertyNames
        // echoes the duplicated member's own name verbatim into the exception message, so a huge duplicated
        // property name must not make ParcelGeometryException.Message balloon to the size of the input.
        string hugeKey = new string('K', 5_000_000);
        string json = "{\"" + hugeKey + "\":1,\"" + hugeKey + "\":2,\"type\":\"Polygon\"," +
            "\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.True(error.Message.Length < 1000);
        Assert.DoesNotContain(hugeKey, error.Message, StringComparison.Ordinal);
    }

    // ---- GeoJSON: a leading UTF-8 byte-order mark does not defeat parsing --------------------------------

    [Fact]
    public void GeoJsonAcceptsALeadingByteOrderMark()
    {
        // Mirrors WktAcceptsALeadingByteOrderMark. Left unstripped, System.Text.Json's JsonDocument.Parse
        // rejects a leading BOM outright ("'0xEF' is an invalid start of a value"), because it transcodes to
        // UTF-8 before parsing; a leading BOM is nonetheless the default save format for plain-text files
        // written by common Windows tools such as Notepad and Excel.
        PolygonalRegion region = ParseGeoJson("﻿" + """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""");

        Assert.Equal(1, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonAcceptsACrs84DeclarationWithAGeographicReference()
    {
        const string json = """
            {"type":"Polygon","crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:OGC:1.3:CRS84"}},
             "coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}
            """;

        PolygonalRegion region = ParseGeoJson(json, GeographicReference());

        Assert.Equal(1, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonRejectsAnEpsg3857Crs()
    {
        const string json = """
            {"type":"Polygon","crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:EPSG::3857"}},
             "coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}
            """;

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));
    }

    [Theory]
    [InlineData("urn:ogc:def:crs:EPSG::104326")] // a different, unrelated EPSG code that merely contains "4326"
    [InlineData("Fantasy Local Grid 4326-East")] // arbitrary text containing "4326"
    [InlineData("NOT_URN_CRS84_REALLY_SOMETHING_ELSE")] // arbitrary text containing "CRS84"
    public void GeoJsonRejectsACrsNameThatOnlyContainsCrs84Or4326AsASubstring(string crsName)
    {
        // ValidateCrs must match the crs member's name exactly, not merely check whether it contains "CRS84"
        // or "4326" anywhere: none of these names is actually CRS84 or EPSG:4326, so none may be silently
        // treated as an agreeing WGS 84 declaration.
        string json = "{\"type\":\"Polygon\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"" + crsName +
            "\"}},\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}";

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));
    }

    [Theory]
    [InlineData("""{"type":"link","properties":{"name":"CRS84","href":"http://example.com/crs"}}""")] // "link" framing with a coincidental properties.name
    [InlineData("""{"properties":{"name":"CRS84"}}""")] // no "type" member on the crs object at all
    [InlineData("""{"type":"bogus","properties":{"name":"CRS84"}}""")] // an arbitrary, unrecognized type discriminator
    public void GeoJsonRejectsACrsObjectWhoseOwnTypeIsNotNameEvenWithARecognizedPropertiesName(string crsObject)
    {
        // ValidateCrs must require the crs object's own "type" member to be the string "name" (the
        // pre-RFC7946 crs convention's discriminator between a "name" object and a "link" object) before ever
        // consulting properties.name; otherwise a self-contradictory or malformed crs declaration is silently
        // treated as if it were "type":"name" whenever it happens to carry a recognized properties.name value.
        string json = "{\"type\":\"Polygon\",\"crs\":" + crsObject + ",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}";

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));
    }

    [Theory]
    [InlineData("CRS84")]
    [InlineData("EPSG:4326")]
    [InlineData("urn:ogc:def:crs:EPSG::4326")]
    [InlineData("urn:ogc:def:crs:OGC::CRS84")]
    [InlineData("http://www.opengis.net/def/crs/OGC/1.3/CRS84")]
    [InlineData("crs84")] // lowercase, locking in case-insensitive matching for every recognized form
    public void GeoJsonAcceptsOtherRecognizedWgs84CrsNameForms(string crsName)
    {
        string json = "{\"type\":\"Polygon\",\"crs\":{\"type\":\"name\",\"properties\":{\"name\":\"" + crsName +
            "\"}},\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}";

        PolygonalRegion region = ParseGeoJson(json, GeographicReference());

        Assert.Equal(1, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonAcceptsARecognizedCrsNamePaddedWithIncidentalWhitespace()
    {
        // ValidateCrs trims incidental leading/trailing whitespace before the exact-match comparison
        // (intentional, so a value padded with stray whitespace from hand-edited JSON is still recognized);
        // internal whitespace is a different, still-rejected case, covered next.
        const string json = """{"type":"Polygon","crs":{"type":"name","properties":{"name":" EPSG:4326 "}},"coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""";

        PolygonalRegion region = ParseGeoJson(json, GeographicReference());

        Assert.Equal(1, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonRejectsARecognizedCrsNameWithInternalWhitespace()
    {
        const string json = """{"type":"Polygon","crs":{"type":"name","properties":{"name":"EPSG: 4326"}},"coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""";

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));
    }

    [Fact]
    public void GeoJsonRejectsACrs84DeclarationWithAProjectedReference()
    {
        const string json = """
            {"type":"Polygon","crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:OGC:1.3:CRS84"}},
             "coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}
            """;

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, ProjectedReference()));
    }

    [Fact]
    public void GeoJsonRejectsAnEpsg3857CrsDeclaredOnAFeaturesGeometryRatherThanTheRoot()
    {
        // The older, pre-RFC7946 GeoJSON crs convention this parser otherwise tries to support allows a crs
        // member on any GeoJSON object, not just the document root; ValidateCrs must be applied there too, or
        // this mismatched crs would be silently ignored purely because of where it was declared.
        const string json = """
            {"type":"Feature","properties":{},"geometry":{"type":"Polygon",
             "crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:EPSG::3857"}},
             "coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}
            """;

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));

        Assert.Contains("$.geometry", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeoJsonRejectsAnEpsg3857CrsDeclaredOnAFeatureInsideAFeatureCollection()
    {
        const string json = """
            {"type":"FeatureCollection","features":[
                {"type":"Feature","crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:EPSG::3857"}},
                 "properties":{},"geometry":{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}
            ]}
            """;

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));

        Assert.Contains("feature 0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GeoJsonRejectsAnEpsg3857CrsDeclaredOnAGeometryCollectionMember()
    {
        const string json = """
            {"type":"GeometryCollection","geometries":[
                {"type":"Polygon","crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:EPSG::3857"}},
                 "coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}
            ]}
            """;

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference()));
    }

    [Fact]
    public void GeoJsonAcceptsAnAgreeingCrs84DeclarationNestedOnAFeaturesGeometry()
    {
        // The nested-crs check must actually validate, not just reject everything it finds nested: an
        // agreeing declaration (CRS84 with a geographic reference) at the same nested position finding 4
        // exercises above is still accepted.
        const string json = """
            {"type":"Feature","properties":{},"geometry":{"type":"Polygon",
             "crs":{"type":"name","properties":{"name":"urn:ogc:def:crs:OGC:1.3:CRS84"}},
             "coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}}
            """;

        PolygonalRegion region = ParseGeoJson(json, GeographicReference());

        Assert.Equal(1, region.PolygonCount);
    }

    [Fact]
    public void GeoJsonRejectsAGeographicReferenceWithLatitudeLongitudeAxisOrder()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""";

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, GeographicReference(HorizontalAxisOrder.LatitudeLongitude)));
    }

    [Fact]
    public void GeoJsonRejectsAProjectedReferenceWithNorthingEastingAxisOrder()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[1,0],[1,1],[0,0]]]}""";

        Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json, ProjectedReference(HorizontalAxisOrder.NorthingEasting)));
    }

    [Fact]
    public void GeoJsonIgnoresTheZOrdinate()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0,100],[4,0,200],[4,4,300],[0,4,400],[0,0,100]]]}""";

        PolygonalRegion region = ParseGeoJson(json);

        PolygonRings polygon = Assert.Single(region.Polygons);
        Assert.Equal(new Coordinate2D(0, 0), polygon.Shell[0]);
        Assert.Equal(16d, region.Area);
    }

    [Fact]
    public void GeoJsonParsingIsInvariantUnderFrFrCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            const string json = """{"type":"Polygon","coordinates":[[[0,0],[1.5,0],[1.5,1.5],[0,1.5],[0,0]]]}""";

            PolygonalRegion region = ParseGeoJson(json);

            Assert.Equal(2.25d, region.Area, 12);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public void GeoJsonParsingIsDeterministicAcrossRepeatedCalls()
    {
        const string json = """{"type":"Polygon","coordinates":[[[0,0],[4,0],[4,4],[0,4],[0,0]],[[1,1],[1,2],[2,2],[2,1],[1,1]]]}""";

        PolygonalRegion first = ParseGeoJson(json);
        PolygonalRegion second = ParseGeoJson(json);

        Assert.Equal(first.Polygons[0].Shell, second.Polygons[0].Shell);
        Assert.Equal(first.Polygons[0].Holes[0], second.Polygons[0].Holes[0]);
    }

    [Fact]
    public void GeoJsonErrorMessageNeverContainsTheFullInputText()
    {
        string positions = string.Join(",", Enumerable.Range(0, 5000).Select(i => $"[{i},{i}]"));
        string json = "{\"type\":\"Polygon\",\"coordinates\":[[" + positions + "]]}";

        ParcelGeometryException error = Assert.Throws<ParcelGeometryException>(() => ParseGeoJson(json));

        Assert.True(json.Length > 20000);
        Assert.True(error.Message.Length < 1000);
        Assert.DoesNotContain(positions, error.Message, StringComparison.Ordinal);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------

    private static PolygonalRegion ParseWkt(string wkt, HorizontalReference? reference = null) =>
        ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, wkt, reference ?? ProjectedReference());

    private static PolygonalRegion ParseGeoJson(string json, HorizontalReference? reference = null) =>
        ParcelGeometryParser.Parse(ParcelGeometryFormat.GeoJson, json, reference ?? GeographicReference());

    private static HorizontalReference GeographicReference(HorizontalAxisOrder axisOrder = HorizontalAxisOrder.LongitudeLatitude) => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, axisOrder);

    private static HorizontalReference ProjectedReference(HorizontalAxisOrder axisOrder = HorizontalAxisOrder.EastingNorthing) => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), axisOrder);

    /// <summary>True when every high surrogate in <paramref name="value"/> is immediately followed by its paired low surrogate, and no low surrogate appears unpaired.</summary>
    private static bool IsWellFormedUtf16(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                {
                    return false;
                }

                i++;
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }
}
