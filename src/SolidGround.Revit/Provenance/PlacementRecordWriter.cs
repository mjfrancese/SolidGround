using SolidGround.Core.Provenance;

namespace SolidGround.Revit.Provenance;

/// <summary>
/// Writes a <see cref="PlacementRecord"/> next to the export bundle, only after a confirmed
/// <c>Committed</c> transaction status (design record §6.6 step 1/§9). The deterministic rendering itself
/// lives in <see cref="PlacementRecordRenderer"/> (moved to <c>SolidGround.Core</c> for SolidGround Issue #15's
/// review fix so it is directly, Revit-free offline testable); this class adds only the on-disk write.
/// </summary>
internal static class PlacementRecordWriter
{
    /// <summary>Renders <paramref name="record"/> and writes it to <c>&lt;outputDirectory&gt;\&lt;baseName&gt;.revit-placement.json</c>, returning the full path written.</summary>
    internal static string Write(string outputDirectory, string baseName, PlacementRecord record)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentNullException.ThrowIfNull(record);

        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, baseName + PlacementRecordRenderer.FileSuffix);
        File.WriteAllBytes(path, PlacementRecordRenderer.Render(record));
        return path;
    }
}
