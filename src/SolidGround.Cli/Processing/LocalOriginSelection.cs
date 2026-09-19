using System.Globalization;

namespace SolidGround.Cli.Processing;

/// <summary>Which of the three <c>--origin</c> forms an operator chose.</summary>
internal enum LocalOriginKind
{
    Southwest,
    Centroid,
    Explicit,
}

/// <summary>
/// The parsed <c>--origin</c> value. See docs/architecture/cli-workflow.md's "Local origin selection and its
/// consequences" section for what each of the three forms means and the reversibility consequences the
/// CLI's own help text states verbatim.
/// </summary>
internal sealed record LocalOriginSelection(LocalOriginKind Kind, double X, double Y, double Z)
{
    /// <exception cref="CliUsageException">The text is not "southwest", "centroid", or 2-3 comma-separated finite invariant-culture doubles.</exception>
    internal static LocalOriginSelection Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (text == "southwest")
        {
            return new LocalOriginSelection(LocalOriginKind.Southwest, 0, 0, 0);
        }

        if (text == "centroid")
        {
            return new LocalOriginSelection(LocalOriginKind.Centroid, 0, 0, 0);
        }

        string[] parts = text.Split(',');
        double z = 0d;
        if (parts.Length is 2 or 3
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
            && (parts.Length == 2 || double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
            && double.IsFinite(x) && double.IsFinite(y) && double.IsFinite(z))
        {
            return new LocalOriginSelection(LocalOriginKind.Explicit, x, y, z);
        }

        throw new CliUsageException("--origin must be 'southwest', 'centroid', or an <x>,<y>[,<z>] coordinate.");
    }
}
