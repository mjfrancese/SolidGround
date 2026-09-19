namespace SolidGround.Core.Exports;

/// <summary>
/// Two deterministically rendered files produced from one <see cref="TerrainExportPayload"/> by
/// <see cref="TerrainExportBundleRenderer.Render"/>: the export document and its points file. Both file names
/// share the same base name; the document's own <c>points.file</c> entry repeats the points file name so the
/// bundle stays relocatable. See docs/architecture/provenance-and-deterministic-exports.md's "Decisions"
/// section for why an export is always these two files together, never one alone.
/// </summary>
/// <remarks>
/// A plain class, not a record: <see cref="DocumentBytes"/>/<see cref="PointsBytes"/> are
/// <see cref="ReadOnlyMemory{T}"/>, whose equality compares the underlying array reference, offset, and
/// length, not byte content, so record-synthesized equality would silently treat two separately rendered but
/// byte-identical bundles as unequal. Equality on this type is therefore reference identity; compare rendered
/// output with <c>DocumentBytes.Span.SequenceEqual(...)</c>/<c>PointsBytes.Span.SequenceEqual(...)</c> (or
/// <c>.ToArray()</c>), never <c>==</c>/<c>Equals</c>, matching <see cref="TerrainExportPayload"/>'s own
/// precedent of using a class for exactly this reason.
/// </remarks>
public sealed class TerrainExportBundle
{
    public TerrainExportBundle(string documentFileName, ReadOnlyMemory<byte> documentBytes, string pointsFileName, ReadOnlyMemory<byte> pointsBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pointsFileName);

        DocumentFileName = documentFileName;
        DocumentBytes = documentBytes;
        PointsFileName = pointsFileName;
        PointsBytes = pointsBytes;
    }

    /// <summary>The export document's file name: <c>"{baseName}"</c> plus <see cref="TerrainExportBundleRenderer.DocumentFileSuffix"/>.</summary>
    public string DocumentFileName { get; }

    /// <summary>The export document's exact rendered bytes (UTF-8, no byte-order mark).</summary>
    public ReadOnlyMemory<byte> DocumentBytes { get; }

    /// <summary>The points file's file name: <c>"{baseName}"</c> plus <see cref="TerrainExportBundleRenderer.PointsFileSuffix"/>.</summary>
    public string PointsFileName { get; }

    /// <summary>The points file's exact rendered bytes (UTF-8, no byte-order mark); empty when the payload retained zero samples.</summary>
    public ReadOnlyMemory<byte> PointsBytes { get; }
}
