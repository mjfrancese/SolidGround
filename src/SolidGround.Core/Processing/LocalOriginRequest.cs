using System.Text.Json.Serialization;

namespace SolidGround.Core.Processing;

/// <summary>Which of the three local-origin forms a caller chose.</summary>
public enum LocalOriginKind
{
    Southwest,
    Centroid,
    Explicit,
}

/// <summary>
/// A host-neutral local-origin choice. See docs/architecture/cli-workflow.md's "Local origin selection and
/// its consequences" section for what each of the three forms means and the reversibility consequences the
/// CLI's own help text states verbatim. Lifted into <c>SolidGround.Core</c> for SolidGround Issue #15 and
/// renamed from <c>LocalOriginSelection</c>: the CLI's own <c>--origin</c> text-token parser stays CLI-side
/// (parsing that flag's syntax is not a Core concern) and now builds this record directly, in
/// <c>ProcessCommand.ParseOrigin</c>.
/// </summary>
/// <remarks>
/// <see cref="Kind"/> carries <c>[property: JsonRequired]</c> so <c>TerrainRequestSettings.JsonOptions</c>'s
/// decode of <c>localOrigin.kind</c> (design record §4.1: required, no default) fails fast with a
/// <see cref="System.Text.Json.JsonException"/> when the key is absent from settings JSON, instead of
/// silently binding <see cref="LocalOriginKind.Southwest"/> -- the enum's own default value -- the way a
/// plain positional-record parameter with no presence tracking otherwise would (SolidGround Issue #15 Stage
/// 2 review fix). This has no effect on ordinary C# construction (every other caller, CLI and tests alike,
/// constructs this record directly, never through JSON).
/// </remarks>
public sealed record LocalOriginRequest([property: JsonRequired] LocalOriginKind Kind, double X, double Y, double Z);
