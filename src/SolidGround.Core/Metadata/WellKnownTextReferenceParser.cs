using System.Globalization;
using System.Text;
using SolidGround.Core.Units;

namespace SolidGround.Core.Metadata;

/// <summary>
/// The horizontal reference, and optional vertical reference, described by a parsed Well-Known Text
/// coordinate reference system string.
/// </summary>
public sealed record WellKnownTextReference
{
    public WellKnownTextReference(HorizontalReference horizontal, VerticalReference? vertical, string text)
    {
        ArgumentNullException.ThrowIfNull(horizontal);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        Horizontal = horizontal;
        Vertical = vertical;
        Text = text;
    }

    public HorizontalReference Horizontal { get; }
    public VerticalReference? Vertical { get; }
    public string Text { get; }
}

/// <summary>
/// Parses a Well-Known Text coordinate reference system definition (WKT1, in its ESRI and OGC forms, and
/// the common WKT2 keyword aliases) into SolidGround's horizontal and vertical reference contracts. This
/// parser is generic: it has no knowledge of OpenTopography or any other elevation source.
/// </summary>
public static class WellKnownTextReferenceParser
{
    private static readonly HashSet<string> GeographicKeywords = ["GEOGCS", "GEOGCRS", "GEODCRS"];
    private static readonly HashSet<string> ProjectedKeywords = ["PROJCS", "PROJCRS", "PROJECTEDCRS"];
    private static readonly HashSet<string> BaseGeographicKeywords = ["BASEGEOGCRS"];
    private static readonly HashSet<string> CompoundKeywords = ["COMPD_CS", "COMPOUNDCRS"];
    private static readonly HashSet<string> VerticalKeywords = ["VERT_CS", "VERTCRS", "VERTICALCRS"];
    private static readonly HashSet<string> VerticalDatumKeywords = ["VERT_DATUM", "VDATUM", "VERTICALDATUM"];
    private static readonly HashSet<string> DatumKeywords = ["DATUM"];
    private static readonly HashSet<string> UnitKeywords = ["UNIT", "LENGTHUNIT"];
    private static readonly HashSet<string> AuthorityKeywords = ["AUTHORITY", "ID"];
    private static readonly HashSet<string> HorizontalRootKeywords = [.. GeographicKeywords, .. ProjectedKeywords];

    /// <summary>Parses a WKT coordinate reference system definition.</summary>
    /// <exception cref="ArgumentException">The supplied text is null or blank.</exception>
    /// <exception cref="FormatException">The text is not a well-formed, supported WKT coordinate reference system.</exception>
    public static WellKnownTextReference Parse(string wkt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(wkt);

        WktNode root = new WktParser(wkt).ParseRoot();
        return Interpret(root, wkt);
    }

    private static WellKnownTextReference Interpret(WktNode root, string originalText)
    {
        string rootKeyword = root.Keyword.ToUpperInvariant();

        if (CompoundKeywords.Contains(rootKeyword))
        {
            WktNode horizontalNode = FindChildNode(root, HorizontalRootKeywords)
                ?? throw new FormatException($"'{root.Keyword}' must contain a projected or geographic child coordinate reference system.");
            WktNode? verticalNode = FindChildNode(root, VerticalKeywords);
            HorizontalReference horizontal = InterpretHorizontal(horizontalNode);
            VerticalReference? vertical = verticalNode is null ? null : InterpretVertical(verticalNode);
            return new WellKnownTextReference(horizontal, vertical, originalText);
        }

        if (HorizontalRootKeywords.Contains(rootKeyword))
        {
            return new WellKnownTextReference(InterpretHorizontal(root), null, originalText);
        }

        throw new FormatException(
            $"'{root.Keyword}' is not a supported root coordinate reference system keyword. " +
            "Expected a projected, geographic, or compound coordinate reference system.");
    }

    private static HorizontalReference InterpretHorizontal(WktNode node)
    {
        string keyword = node.Keyword.ToUpperInvariant();
        string name = FirstStringArgument(node)
            ?? throw new FormatException($"'{node.Keyword}' must begin with a quoted name.");
        string identifier = FindAuthorityIdentifier(node) ?? name;

        if (GeographicKeywords.Contains(keyword))
        {
            string datumName = FindDatumName(node);
            return new HorizontalReference(
                identifier,
                datumName,
                HorizontalReferenceKind.Geographic,
                HorizontalUnit.DecimalDegrees,
                HorizontalAxisOrder.LongitudeLatitude);
        }

        if (ProjectedKeywords.Contains(keyword))
        {
            WktNode geographicBase = FindChildNode(node, GeographicKeywords)
                ?? FindChildNode(node, BaseGeographicKeywords)
                ?? throw new FormatException($"'{node.Keyword}' must contain a nested geographic coordinate reference system that supplies its datum.");
            string datumName = FindDatumName(geographicBase);
            LengthUnit unit = FindDirectUnit(node)
                ?? throw new FormatException($"'{node.Keyword}' must contain a direct UNIT or LENGTHUNIT child describing its linear unit.");
            return new HorizontalReference(
                identifier,
                datumName,
                HorizontalReferenceKind.Projected,
                HorizontalUnit.Linear(unit),
                HorizontalAxisOrder.EastingNorthing);
        }

        throw new FormatException($"'{node.Keyword}' is not a supported projected or geographic coordinate reference system keyword.");
    }

    private static VerticalReference InterpretVertical(WktNode node)
    {
        WktNode datumNode = FindChildNode(node, VerticalDatumKeywords)
            ?? throw new FormatException($"'{node.Keyword}' must contain a vertical datum child (VERT_DATUM, VDATUM, or VERTICALDATUM).");
        string datumName = FirstStringArgument(datumNode)
            ?? throw new FormatException("A vertical datum node must begin with a quoted name.");
        LengthUnit unit = FindDirectUnit(node)
            ?? throw new FormatException($"'{node.Keyword}' must contain a direct UNIT or LENGTHUNIT child describing its linear unit.");
        return new VerticalReference(datumName, unit);
    }

    private static string FindDatumName(WktNode geographicNode)
    {
        WktNode datumNode = FindChildNode(geographicNode, DatumKeywords)
            ?? throw new FormatException($"'{geographicNode.Keyword}' must contain a DATUM child.");
        return FirstStringArgument(datumNode)
            ?? throw new FormatException("A DATUM node must begin with a quoted name.");
    }

    private static string? FindAuthorityIdentifier(WktNode node)
    {
        WktNode? authorityNode = FindChildNode(node, AuthorityKeywords);
        if (authorityNode is null || authorityNode.Arguments.Count < 2 || authorityNode.Arguments[0] is not string authorityName)
        {
            return null;
        }

        string code = authorityNode.Arguments[1] switch
        {
            string s => s,
            double d => FormatAuthorityCode(d),
            _ => throw new FormatException($"'{authorityNode.Keyword}' must supply a string or numeric code as its second argument."),
        };

        return $"{authorityName}:{code}";
    }

    private static string FormatAuthorityCode(double value) =>
        value == Math.Floor(value) && double.IsFinite(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);

    private static LengthUnit? FindDirectUnit(WktNode node)
    {
        WktNode? unitNode = FindChildNode(node, UnitKeywords);
        return unitNode is null ? null : InterpretLengthUnit(unitNode);
    }

    private static LengthUnit InterpretLengthUnit(WktNode unitNode)
    {
        // Resolved through FirstStringArgument, consistent with InterpretHorizontal/FindDatumName/
        // InterpretVertical, so a present-but-blank quoted unit name (for example UNIT["",1.0]) is treated
        // the same as a missing one instead of silently accepting a blank name paired with a recognized
        // conversion factor.
        string? unitName = FirstStringArgument(unitNode);
        if (unitNode.Arguments.Count < 2 || unitName is null || unitNode.Arguments[1] is not double factor)
        {
            throw new FormatException($"'{unitNode.Keyword}' must supply a unit name and a numeric conversion factor.");
        }

        if (factor == 1d)
        {
            return LengthUnit.Meter;
        }

        if (Math.Abs(factor - LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot)) < 1e-12)
        {
            return LengthUnit.InternationalFoot;
        }

        if (Math.Abs(factor - LengthConverter.MetersPerUnit(LengthUnit.UsSurveyFoot)) < 1e-12)
        {
            return LengthUnit.UsSurveyFoot;
        }

        throw new FormatException(
            $"Unsupported linear unit '{unitName}' with conversion factor {factor.ToString("G17", CultureInfo.InvariantCulture)}. " +
            "SolidGround supports only meter, US survey foot " +
            $"({LengthConverter.MetersPerUnit(LengthUnit.UsSurveyFoot).ToString("R", CultureInfo.InvariantCulture)} m), and international foot " +
            $"({LengthConverter.MetersPerUnit(LengthUnit.InternationalFoot).ToString("R", CultureInfo.InvariantCulture)} m).");
    }

    /// <summary>
    /// Returns the node's first quoted-string argument, or null when there is none. A present-but-blank
    /// quoted string (for example <c>PROJCS["",...]</c>) is treated the same as a missing one so every
    /// caller's <c>?? throw new FormatException(...)</c> guard fires for it too, instead of letting a blank
    /// name or datum reach a downstream constructor that throws the undocumented <see cref="ArgumentException"/>.
    /// </summary>
    private static string? FirstStringArgument(WktNode node)
    {
        string? value = node.Arguments.OfType<string>().FirstOrDefault();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static WktNode? FindChildNode(WktNode parent, HashSet<string> keywords)
    {
        foreach (object argument in parent.Arguments)
        {
            if (argument is WktNode child && keywords.Contains(child.Keyword.ToUpperInvariant()))
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>One parsed WKT node: a keyword followed by bracketed string, numeric, or nested-node arguments.</summary>
    private sealed record WktNode(string Keyword, IReadOnlyList<object> Arguments);

    /// <summary>A minimal hand-written recursive-descent tokenizer/parser for WKT1 and WKT2 node syntax.</summary>
    private sealed class WktParser
    {
        private readonly string text;
        private int position;

        public WktParser(string text)
        {
            this.text = text;
            position = 0;
        }

        public WktNode ParseRoot()
        {
            SkipWhitespace();
            WktNode node = ParseNode();
            SkipWhitespace();
            if (position != text.Length)
            {
                throw Error("Unexpected trailing content after the root coordinate reference system node.");
            }

            return node;
        }

        private WktNode ParseNode()
        {
            string keyword = ParseKeyword();
            SkipWhitespace();
            char open = ExpectOpenBracket();
            char expectedClose = open == '[' ? ']' : ')';
            List<object> arguments = [];
            SkipWhitespace();
            if (Peek() == expectedClose)
            {
                Advance();
                return new WktNode(keyword, arguments);
            }

            while (true)
            {
                SkipWhitespace();
                arguments.Add(ParseArgument());
                SkipWhitespace();
                char next = Peek();
                if (next == ',')
                {
                    Advance();
                    continue;
                }

                if (next == expectedClose)
                {
                    Advance();
                    break;
                }

                throw Error($"Expected ',' or '{expectedClose}'.");
            }

            return new WktNode(keyword, arguments);
        }

        private object ParseArgument()
        {
            char c = Peek();
            if (c == '"')
            {
                return ParseQuotedString();
            }

            if (c is '-' or '+' or '.' || char.IsAsciiDigit(c))
            {
                return ParseNumber();
            }

            if (char.IsLetter(c) || c == '_')
            {
                // Could be a nested node (KEYWORD[...]) or a bare enumerated token such as the axis
                // direction in AXIS["Easting",EAST]. Peek past the identifier to tell them apart.
                int start = position;
                string identifier = ParseKeyword();
                SkipWhitespace();
                if (Peek() is '[' or '(')
                {
                    position = start;
                    return ParseNode();
                }

                return identifier;
            }

            throw Error(c == '\0' ? "Unexpected end of WKT text inside an argument list." : $"Unexpected character '{c}' in an argument.");
        }

        private string ParseKeyword()
        {
            int start = position;
            if (position >= text.Length || !(char.IsLetter(text[position]) || text[position] == '_'))
            {
                throw Error("Expected a coordinate reference system keyword.");
            }

            while (position < text.Length && (char.IsLetterOrDigit(text[position]) || text[position] == '_'))
            {
                position++;
            }

            return text[start..position];
        }

        private char ExpectOpenBracket()
        {
            char c = Peek();
            if (c is not ('[' or '('))
            {
                throw Error("Expected '[' or '(' after a keyword.");
            }

            Advance();
            return c;
        }

        private string ParseQuotedString()
        {
            int start = position;
            Advance();
            var builder = new StringBuilder();
            while (true)
            {
                if (position >= text.Length)
                {
                    throw Error("Unterminated quoted string.", start);
                }

                char c = text[position];
                if (c == '"')
                {
                    if (position + 1 < text.Length && text[position + 1] == '"')
                    {
                        builder.Append('"');
                        position += 2;
                        continue;
                    }

                    position++;
                    break;
                }

                builder.Append(c);
                position++;
            }

            return builder.ToString();
        }

        private double ParseNumber()
        {
            int start = position;
            if (Peek() is '+' or '-')
            {
                position++;
            }

            bool hasDigits = false;
            while (position < text.Length && char.IsAsciiDigit(text[position]))
            {
                position++;
                hasDigits = true;
            }

            if (position < text.Length && text[position] == '.')
            {
                position++;
                while (position < text.Length && char.IsAsciiDigit(text[position]))
                {
                    position++;
                    hasDigits = true;
                }
            }

            if (!hasDigits)
            {
                throw Error("Expected a number.", start);
            }

            if (position < text.Length && text[position] is 'e' or 'E')
            {
                int exponentStart = position;
                int scan = position + 1;
                if (scan < text.Length && text[scan] is '+' or '-')
                {
                    scan++;
                }

                int exponentDigitsStart = scan;
                while (scan < text.Length && char.IsAsciiDigit(text[scan]))
                {
                    scan++;
                }

                if (scan > exponentDigitsStart)
                {
                    position = scan;
                }
                else
                {
                    position = exponentStart;
                }
            }

            string token = text[start..position];
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value))
            {
                throw Error($"'{token}' is not a valid finite number.", start);
            }

            return value;
        }

        private char Peek() => position < text.Length ? text[position] : '\0';

        private void Advance() => position++;

        private void SkipWhitespace()
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }
        }

        private FormatException Error(string message) => Error(message, position);

        private static FormatException Error(string message, int at) => new($"{message} (at character {at} of the supplied WKT text.)");
    }
}
