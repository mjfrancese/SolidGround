using SolidGround.Cli.Rasters;
using SolidGround.Core.Metadata;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Processing;

/// <summary>
/// Resolves `process`'s single <see cref="VerticalReference"/>, used both for <c>AaiGridParser.Parse</c> and
/// for <c>TerrainExportPayloadAssembler.Assemble</c>'s source vertical reference -- always the same value, so
/// the equality check <c>TerrainExportPayloadAssembler</c> itself performs holds by construction. See
/// docs/architecture/cli-workflow.md's "Units" section for the field-by-field precedence this implements:
/// an explicit CLI option first, then the `--source-json` sidecar, then a compound `.prj`'s own `VERT_CS`;
/// datum and unit must either both resolve or neither may. `run`/`fetch` never call this: their vertical
/// reference is always supplied directly by the source (docs/architecture/cli-workflow.md's "Commands" and
/// "Units" sections).
/// </summary>
internal static class VerticalReferenceResolution
{
    /// <summary>
    /// <see cref="Resolve"/>'s result: the resolved vertical reference plus where it was labeled as having
    /// come from. See docs/architecture/cli-workflow.md's "Raster set persistence" section: `process` always
    /// labels its horizontal reference `Operator` (parsed from `--prj`), but the vertical reference's origin
    /// depends on which precedence branch actually supplied the datum and unit.
    /// </summary>
    internal sealed record ResolvedVerticalReference(VerticalReference Reference, ReferenceOrigin Origin);

    /// <exception cref="CliUsageException">Datum or unit could not be resolved from any source.</exception>
    internal static ResolvedVerticalReference Resolve(
        string? cliVerticalDatum, LengthUnit? cliVerticalUnit, string? cliGeoid,
        RasterSourceSidecar? sidecar,
        VerticalReference? compoundPrjVertical)
    {
        string? datum = cliVerticalDatum ?? sidecar?.Vertical.Datum ?? compoundPrjVertical?.Datum;
        LengthUnit? unit = cliVerticalUnit ?? sidecar?.Vertical.Unit ?? compoundPrjVertical?.Unit;
        string? geoid = cliGeoid ?? sidecar?.Vertical.GeoidModel ?? compoundPrjVertical?.GeoidModel;

        if (datum is null || unit is null)
        {
            throw new CliUsageException(
                "The vertical reference could not be determined: supply --vertical-datum and --vertical-unit, " +
                "a --source-json sidecar, or a compound .prj with a VERT_CS.");
        }

        // The sidecar's own recorded origin is trustworthy only when the sidecar is actually what supplied
        // the datum and unit: that requires both that a sidecar was given at all, and that no CLI vertical
        // flag overrode it (a CLI flag always wins the precedence above, and always means the operator, not
        // the sidecar, is asserting the value -- even when only --geoid was given). Every other case,
        // including a compound .prj's own VERT_CS with no sidecar, is an operator-supplied reference.
        bool cliVerticalFlagGiven = cliVerticalDatum is not null || cliVerticalUnit is not null || cliGeoid is not null;
        ReferenceOrigin origin = sidecar is not null && !cliVerticalFlagGiven
            ? sidecar.VerticalReferenceOrigin
            : ReferenceOrigin.Operator;

        return new ResolvedVerticalReference(new VerticalReference(datum, unit.Value, geoid), origin);
    }
}
