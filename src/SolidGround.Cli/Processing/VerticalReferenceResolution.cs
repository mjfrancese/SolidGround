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
    /// <exception cref="CliUsageException">Datum or unit could not be resolved from any source.</exception>
    internal static VerticalReference Resolve(
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

        return new VerticalReference(datum, unit.Value, geoid);
    }
}
