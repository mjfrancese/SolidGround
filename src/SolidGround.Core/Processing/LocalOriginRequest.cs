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
public sealed record LocalOriginRequest(LocalOriginKind Kind, double X, double Y, double Z);
