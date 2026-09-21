using System.Globalization;

namespace SolidGround.Core.Hosting;

/// <summary>
/// Revit-free reader for the two point-count settings AGENTS.md's "Revit 2027 rules" section names --
/// <c>NativeToposolidMaxPointThreshold</c> and <c>LinkToposolidMaxPointThreshold</c> -- both documented by
/// Autodesk's Revit 2027 "Misc Settings in Revit.ini" page as living in a <c>Revit.ini</c> file's
/// <c>[Misc]</c> section. Takes the already-read text of a <c>Revit.ini</c> file; never touches the file
/// system itself (locating and reading the real file is <c>SolidGround.Revit</c>'s Preflight-stage
/// responsibility, since only that host knows where its own <c>Revit.ini</c> lives).
/// </summary>
/// <remarks>
/// SolidGround Issue #15's 2026-09-21 probe session (<c>evidence/EVIDENCE-PROBES.md</c> Run 2,
/// <c>ThresholdProbe</c>, and <c>docs/architecture/revit-toposolid-creation.md</c>'s "Step 7") found that the
/// combined <c>Toposolid.Create</c> overload does not throw when handed more points than
/// <c>NativeToposolidMaxPointThreshold</c> allows -- it silently retains only about that many
/// <c>SlabShapeEditor</c> vertices. <c>SolidGround.Revit</c>'s <c>CreateToposolidCommand</c> Document
/// Preflight stage uses this reader to reject an over-budget run before any transaction opens, instead of
/// only discovering the loss afterward from <c>PostCreationVerification</c>.
/// </remarks>
public static class RevitIniToposolidThresholds
{
    private const string SectionName = "Misc";
    private const string NativeKeyName = "NativeToposolidMaxPointThreshold";
    private const string LinkKeyName = "LinkToposolidMaxPointThreshold";

    /// <summary>The UTF-8/UTF-16 byte order mark codepoint (U+FEFF), expressed numerically rather than as a Unicode string escape to keep this source file's own bytes unambiguous.</summary>
    private const char ByteOrderMark = (char)0xFEFF;

    /// <summary>
    /// One parsed reading of the two settings. Either field is <see langword="null"/> when its key was not
    /// found, well-formed, inside <c>[Misc]</c>.
    /// </summary>
    public sealed record Thresholds(int? NativeToposolidMaxPointThreshold, int? LinkToposolidMaxPointThreshold);

    /// <summary>
    /// Parses <paramref name="revitIniText"/> for <c>NativeToposolidMaxPointThreshold</c> and
    /// <c>LinkToposolidMaxPointThreshold</c> inside a case-insensitively matched <c>[Misc]</c> section header,
    /// tolerating a leading UTF-8 BOM character, CRLF or LF line endings, surrounding whitespace around
    /// section names/keys/values, and mixed key casing. A key found outside <c>[Misc]</c> (including before
    /// any section header at all) is ignored. Never throws on malformed input: a missing section, a missing
    /// key, or a value that does not parse as a base-10 <see cref="int"/> simply leaves that one field
    /// <see langword="null"/> in the result.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="revitIniText"/> is <see langword="null"/>.</exception>
    public static Thresholds Parse(string revitIniText)
    {
        ArgumentNullException.ThrowIfNull(revitIniText);

        int? nativeThreshold = null;
        int? linkThreshold = null;
        string? currentSection = null;

        foreach (string rawLine in revitIniText.TrimStart(ByteOrderMark).Split('\n'))
        {
            // string.Trim() with no arguments strips '\r' along with every other whitespace character, so a
            // CRLF-terminated line (rawLine ending in '\r' after the '\n' split) is handled identically to a
            // bare-LF one -- no separate CRLF-specific branch is needed.
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                currentSection = line[1..^1].Trim();
                continue;
            }

            if (!string.Equals(currentSection, SectionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            if (string.Equals(key, NativeKeyName, StringComparison.OrdinalIgnoreCase))
            {
                nativeThreshold = ParseIntOrNull(value);
            }
            else if (string.Equals(key, LinkKeyName, StringComparison.OrdinalIgnoreCase))
            {
                linkThreshold = ParseIntOrNull(value);
            }
        }

        return new Thresholds(nativeThreshold, linkThreshold);
    }

    private static int? ParseIntOrNull(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result) ? result : null;
}
