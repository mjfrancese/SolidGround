using SolidGround.Core.Units;

namespace SolidGround.Cli.Options;

/// <summary>Whether an option takes a value or is a bare boolean switch.</summary>
internal enum OptionKind
{
    Flag,
    Value,
}

/// <summary>
/// One option's fixed shape for one command: its long name, whether it takes a value, whether it is
/// unconditionally required, the value placeholder shown in help, and its help sentence. See
/// docs/architecture/cli-workflow.md's "Options and defaults" section for the fixed table this type is
/// transcribed from.
/// </summary>
internal sealed record OptionSpec(string Name, OptionKind Kind, bool Required, string Syntax, string HelpText);

/// <summary>One command's fixed name, one-line summary, and option list, in help-display order.</summary>
internal sealed record VerbSpec(string Name, string Summary, IReadOnlyList<OptionSpec> Options);

/// <summary>
/// The CLI's fixed, hand-written option table: long names only, one verb per invocation, and a
/// deterministic order every command shares with its own help text. See docs/architecture/cli-workflow.md's
/// "Options and defaults" section for why this table carries only the mechanical, name-and-arity-level
/// facts common to every option -- whether it takes a value and whether it is unconditionally required --
/// so <c>ParsedInvocation</c> can validate an invocation without knowing what any option's value means.
/// Cross-option rules (mutually exclusive area-of-interest forms, numeric ranges, enum values, date
/// ordering, and so on) belong to each command's own option binder instead, per that same section.
/// </summary>
internal static class OptionTable
{
    internal const string Process = "process";
    internal const string Fetch = "fetch";
    internal const string Run = "run";
    internal const string Verify = "verify";

    // ---- options reused, byte-for-byte or with a small variant, across more than one verb ------------

    private static readonly OptionSpec Bbox = new(
        "bbox", OptionKind.Value, false, "<west>,<south>,<east>,<north>", "Clips to a WGS 84 bounding box.");

    private static readonly OptionSpec Center = new(
        "center", OptionKind.Value, false, "<lat>,<lon>", "The WGS 84 center of a radius AOI; requires --radius.");

    private static readonly OptionSpec Radius = new(
        "radius", OptionKind.Value, false, "<meters>", "The radius, in meters, of a center+radius AOI; requires --center.");

    private static readonly OptionSpec Parcel = new(
        "parcel", OptionKind.Value, false, "<file>", "Clips to a parcel boundary file (GeoJSON or WKT, WGS 84).");

    private static readonly OptionSpec ParcelFormat = new(
        "parcel-format", OptionKind.Value, false, "geojson|wkt", "The parcel file's format; inferred from its extension when omitted.");

    private static readonly OptionSpec Buffer = new(
        "buffer", OptionKind.Value, false, "<meters>",
        "A buffer, in meters, applied outward from the parcel boundary before clipping.");

    private static readonly OptionSpec OutputForBundle = new(
        "output", OptionKind.Value, true, "<dir>", "The directory to write the export bundle into.");

    private static readonly OptionSpec OutputForRasterSet = OutputForBundle with
    {
        HelpText = "The directory to write the raster set into.",
    };

    private static readonly OptionSpec NameForBundle = new(
        "name", OptionKind.Value, false, "<baseName>", "The export bundle's base file name.");

    private static readonly OptionSpec NameForRasterSet = NameForBundle with
    {
        HelpText = "The raster set's base file name (<name>.asc, <name>.prj, <name>.source.json).",
    };

    private static readonly OptionSpec NameForRun = NameForBundle with
    {
        HelpText = "The export bundle's base file name; also used for the raster set's file names when --save-raster is given.",
    };

    private static readonly OptionSpec OverwriteBundleOrRasterSet = new(
        "overwrite", OptionKind.Flag, false, "", "Allow replacing an existing export document or raster set.");

    private static readonly OptionSpec OverwriteRasterSetOnly = OverwriteBundleOrRasterSet with
    {
        HelpText = "Allow replacing an existing raster set.",
    };

    private static readonly OptionSpec VerboseFull = new(
        "verbose", OptionKind.Flag, false, "",
        "Print full diagnostics: redacted acquisition evidence, clip statistics, simplification diagnostics, and the provenance summary.");

    private static readonly OptionSpec VerboseAcquisitionOnly = VerboseFull with
    {
        HelpText = "Print full redacted acquisition evidence.",
    };

    private static readonly OptionSpec VerboseCoordinateOperation = VerboseFull with
    {
        HelpText = "Also print the full forward/inverse coordinate-operation definition text.",
    };

    private static readonly OptionSpec Timeout = new(
        "timeout", OptionKind.Value, false, "<seconds>", "The HTTP timeout, in seconds, for the OpenTopography request.");

    private static readonly OptionSpec Origin = new(
        "origin", OptionKind.Value, false, "southwest|centroid|<x>,<y>|<x>,<y>,<z>",
        "southwest (default): the lower-left corner of the clipped grid's envelope, snapped to a whole source unit. " +
        "centroid: the center of the clipped grid's envelope, snapped to a whole source unit. <x>,<y> or <x>,<y>,<z>: " +
        "an explicit projected coordinate in the grid's own coordinate reference system and source units, used " +
        "exactly with no snapping; z defaults to 0. Consequences: the origin is subtracted from every coordinate " +
        "before unit conversion, so exported points are relative to it; the bundle records the origin, CRS, datum, " +
        "and unit so source coordinates can be reconstructed; parcels that must be placed together in one Revit " +
        "model must share one explicit origin, because southwest and centroid differ per parcel; a non-zero z " +
        "shifts every elevation by that amount and is recorded; snapping keeps the offset a whole number of " +
        "source units.");

    private static readonly OptionSpec Unit = new(
        "unit", OptionKind.Value, false, LengthUnitTokens.Syntax,
        $"{LengthUnitTokens.TokenOf(LengthUnit.UsSurveyFoot)}: exactly 1200/3937 metres per foot; " +
        $"{LengthUnitTokens.TokenOf(LengthUnit.InternationalFoot)}: exactly 0.3048 metres per foot; " +
        $"{LengthUnitTokens.TokenOf(LengthUnit.Meter)}: 1 metre; {LengthUnitTokens.DefaultToken} (the default)");

    private static readonly OptionSpec Method = new(
        "method", OptionKind.Value, false, "curvature-aware|uniform",
        "curvature-aware retains ridges, swales, and edges; uniform is a simple uniform sampler for comparison, not terrain-aware.");

    private static readonly OptionSpec Budget = new(
        "budget", OptionKind.Value, false, "<int>", "The maximum number of retained points.");

    private static readonly OptionSpec CoverageFloor = new(
        "coverage-floor", OptionKind.Value, false, "<0..1>",
        "The fraction of the interior budget reserved for spatial spread rather than pure curvature ranking. " +
        "Always printed in the run summary because it is not recorded in the export document.");

    private static readonly OptionSpec CollectionStart = new(
        "collection-start", OptionKind.Value, false, "<yyyy-MM-dd>",
        "The dataset collection period's start date (must be given together with --collection-end).");

    private static readonly OptionSpec CollectionEnd = new(
        "collection-end", OptionKind.Value, false, "<yyyy-MM-dd>", "The dataset collection period's end date.");

    private static readonly OptionSpec QualityLevel = new(
        "quality-level", OptionKind.Value, false, "<text>", "Overrides the recorded quality level.");

    private static readonly OptionSpec SaveRaster = new(
        "save-raster", OptionKind.Flag, false, "",
        "Also write the raster set (.asc/.prj/.source.json) beside the export bundle.");

    private static readonly OptionSpec Document = new(
        "document", OptionKind.Value, true, "<file>", "The export document (<name>.solidground.json) to verify.");

    private static readonly OptionSpec Points = new(
        "points", OptionKind.Value, false, "<file>",
        "The points file. Defaults to the document's own base name with the points suffix, in the same directory.");

    // ---- per-verb option lists, in help-display order ------------------------------------------------

    private static readonly IReadOnlyList<OptionSpec> ProcessOptions =
    [
        new("asc", OptionKind.Value, true, "<file>", "The input AAIGrid (.asc) raster to process."),
        new("prj", OptionKind.Value, false, "<file>",
            "The coordinate reference sidecar for --asc. Defaults to the .asc file's own name with a .prj extension."),
        new("source-json", OptionKind.Value, false, "<file>",
            "The raster-set sidecar written by fetch. Defaults to the .asc file's own name with a .source.json " +
            "extension, used only when present."),
        new("source-name", OptionKind.Value, false, "<text>", "Overrides the recorded source name."),
        new("dataset", OptionKind.Value, false, "<text>", "Overrides the recorded dataset identifier."),
        new("vertical-datum", OptionKind.Value, false, "<text>", "Overrides the vertical datum name used for provenance."),
        new("vertical-unit", OptionKind.Value, false, LengthUnitTokens.Syntax,
            "Overrides the vertical reference's own unit (not the export output unit; see --unit)."),
        new("geoid", OptionKind.Value, false, "<text>", "Overrides the recorded geoid model name."),
        CollectionStart,
        CollectionEnd,
        QualityLevel,
        Bbox,
        Center,
        Radius,
        Parcel,
        ParcelFormat,
        Buffer,
        Origin,
        Unit,
        Method,
        Budget,
        CoverageFloor,
        OutputForBundle,
        NameForBundle,
        OverwriteBundleOrRasterSet,
        VerboseFull,
    ];

    private static readonly IReadOnlyList<OptionSpec> FetchOptions =
    [
        Bbox,
        Center,
        Radius,
        Parcel,
        ParcelFormat,
        Buffer,
        OutputForRasterSet,
        NameForRasterSet,
        OverwriteRasterSetOnly,
        Timeout,
        VerboseAcquisitionOnly,
    ];

    private static readonly IReadOnlyList<OptionSpec> RunOptions =
    [
        Bbox,
        Center,
        Radius,
        Parcel,
        ParcelFormat,
        Buffer,
        Origin,
        Unit,
        Method,
        Budget,
        CoverageFloor,
        CollectionStart,
        CollectionEnd,
        QualityLevel,
        OutputForBundle,
        NameForRun,
        OverwriteBundleOrRasterSet,
        Timeout,
        SaveRaster,
        VerboseFull,
    ];

    private static readonly IReadOnlyList<OptionSpec> VerifyOptions =
    [
        Document,
        Points,
        VerboseCoordinateOperation,
    ];

    private static readonly IReadOnlyList<VerbSpec> AllVerbs =
    [
        new(Process, "OFFLINE. Process a local AAIGrid (.asc) raster into a terrain export bundle.", ProcessOptions),
        new(Fetch, "ONLINE. Acquire a raster set from OpenTopography without processing it.", FetchOptions),
        new(Run, "ONLINE. Acquire from OpenTopography and process it into a terrain export bundle in one step.", RunOptions),
        new(Verify, "Reads an export bundle, verifies it, and prints its provenance summary.", VerifyOptions),
    ];

    /// <summary>Every verb, in a fixed, deterministic display order.</summary>
    internal static IReadOnlyList<VerbSpec> Verbs => AllVerbs;

    /// <summary>The named verb's fixed specification, or null when <paramref name="verb"/> names no command.</summary>
    internal static VerbSpec? TryGetVerb(string verb)
    {
        ArgumentNullException.ThrowIfNull(verb);
        foreach (VerbSpec spec in AllVerbs)
        {
            if (string.Equals(spec.Name, verb, StringComparison.Ordinal))
            {
                return spec;
            }
        }

        return null;
    }
}
