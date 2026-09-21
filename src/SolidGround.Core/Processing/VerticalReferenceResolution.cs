using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Core.Processing;

/// <summary>
/// Resolves `process`'s single <see cref="VerticalReference"/>, used both for <c>AaiGridParser.Parse</c> and
/// for <c>TerrainExportPayloadAssembler.Assemble</c>'s source vertical reference -- always the same value, so
/// the equality check <c>TerrainExportPayloadAssembler</c> itself performs holds by construction. See
/// docs/architecture/cli-workflow.md's "Units" section for the field-by-field precedence this implements:
/// an explicit override first, then a sidecar, then a compound `.prj`'s own `VERT_CS`; datum and unit must
/// either both resolve or neither may. `run`/`fetch` never call this: their vertical reference is always
/// supplied directly by the source (docs/architecture/cli-workflow.md's "Commands" and "Units" sections).
/// Lifted into <c>SolidGround.Core</c> for SolidGround Issue #15, keeping its <see cref="RasterSourceSidecar"/>
/// parameter (the sidecar type moved into Core in full alongside this type, so no narrower replacement record
/// was needed). The CLI's own `process` command is still the only caller today, and its own
/// <c>--vertical-datum</c>/<c>--vertical-unit</c>/<c>--source-json</c>-flavored message text is unchanged;
/// only the exception type changed, from the CLI-only <c>CliUsageException</c> (unreachable once this type
/// left <c>SolidGround.Cli</c>) to <see cref="FormatException"/>. <c>CliApplication.RunAsync</c>'s existing
/// <c>catch (FormatException ex)</c> already produces byte-identical CLI output to its former
/// <c>catch (CliUsageException ex)</c>, so no CLI call site needs to translate it back.
/// </summary>
public static class VerticalReferenceResolution
{
    /// <summary>
    /// <see cref="Resolve"/>'s result: the resolved vertical reference plus where it was labeled as having
    /// come from. See docs/architecture/cli-workflow.md's "Raster set persistence" section: `process` always
    /// labels its horizontal reference `Operator` (parsed from `--prj`), but the vertical reference's origin
    /// depends on which precedence branch actually supplied the datum and unit.
    /// </summary>
    public sealed record ResolvedVerticalReference(VerticalReference Reference, ReferenceOrigin Origin);

    /// <exception cref="FormatException">Datum or unit could not be resolved from any source.</exception>
    public static ResolvedVerticalReference Resolve(
        string? explicitDatum, LengthUnit? explicitUnit, string? explicitGeoid,
        RasterSourceSidecar? sidecar,
        VerticalReference? compoundPrjVertical)
    {
        string? datum = explicitDatum ?? sidecar?.Vertical.Datum ?? compoundPrjVertical?.Datum;
        LengthUnit? unit = explicitUnit ?? sidecar?.Vertical.Unit ?? compoundPrjVertical?.Unit;
        string? geoid = explicitGeoid ?? sidecar?.Vertical.GeoidModel ?? compoundPrjVertical?.GeoidModel;

        if (datum is null || unit is null)
        {
            throw new FormatException(
                "The vertical reference could not be determined: supply --vertical-datum and --vertical-unit, " +
                "a --source-json sidecar, or a compound .prj with a VERT_CS.");
        }

        // The sidecar's own recorded origin is trustworthy only when the sidecar is actually what supplied
        // the datum and unit: that requires both that a sidecar was given at all, and that no explicit
        // vertical override overrode it (an explicit override always wins the precedence above, and always
        // means the operator, not the sidecar, is asserting the value -- even when only the geoid override
        // was given). Every other case, including a compound .prj's own VERT_CS with no sidecar, is an
        // operator-supplied reference.
        bool explicitVerticalOverrideGiven = explicitDatum is not null || explicitUnit is not null || explicitGeoid is not null;
        ReferenceOrigin origin = sidecar is not null && !explicitVerticalOverrideGiven
            ? sidecar.VerticalReferenceOrigin
            : ReferenceOrigin.Operator;

        return new ResolvedVerticalReference(new VerticalReference(datum, unit.Value, geoid), origin);
    }
}
