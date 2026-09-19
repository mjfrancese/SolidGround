namespace SolidGround.Core.Exports;

/// <summary>
/// Writes a rendered <see cref="TerrainExportBundle"/> to two files on the local file system. This is a
/// development and fallback destination -- Revit's toposolid points-file import is exactly this shape -- not
/// a production publishing pipeline. See docs/architecture/provenance-and-deterministic-exports.md's
/// "Boundary: what #9 and Phase 2 still own" section for what stays outside Core: destination selection,
/// argument parsing, and any Revit-side write.
/// </summary>
public sealed class FileSystemTerrainExporter : ITerrainExporter
{
    /// <summary>Validates and stores the output directory and base name used by every <see cref="ExportAsync"/> call.</summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="outputDirectory"/> is null or blank, or <paramref name="baseName"/> fails the base name rule
    /// documented on <see cref="TerrainExportBundleRenderer.Render"/>.
    /// </exception>
    public FileSystemTerrainExporter(string outputDirectory, string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        TerrainExportBaseName.Validate(baseName, nameof(baseName));

        OutputDirectory = outputDirectory;
        BaseName = baseName;
    }

    /// <summary>The directory this exporter writes into. Created on first export if it does not already exist.</summary>
    public string OutputDirectory { get; }

    /// <summary>The file name stem shared by both rendered files.</summary>
    public string BaseName { get; }

    /// <summary>
    /// Renders <paramref name="payload"/> via <see cref="TerrainExportBundleRenderer.Render"/> and writes the
    /// points file, then the export document, to <see cref="OutputDirectory"/>, overwriting either file
    /// silently. Honors <paramref name="cancellationToken"/> before any I/O.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="payload"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> is already canceled; nothing is written.</exception>
    /// <exception cref="TerrainExportException">
    /// <paramref name="payload"/> cannot be rendered, or <see cref="OutputDirectory"/> could not be created or
    /// a file could not be written because of an I/O or access error.
    /// </exception>
    public async ValueTask<TerrainExportReceipt> ExportAsync(TerrainExportPayload payload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        TerrainExportBundle bundle = TerrainExportBundleRenderer.Render(payload, BaseName);

        try
        {
            Directory.CreateDirectory(OutputDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TerrainExportException($"Could not create the terrain export output directory '{OutputDirectory}'.", ex);
        }

        string pointsPath = Path.Combine(OutputDirectory, bundle.PointsFileName);
        string documentPath = Path.Combine(OutputDirectory, bundle.DocumentFileName);

        await WriteFileAsync(pointsPath, bundle.PointsBytes, cancellationToken).ConfigureAwait(false);
        await WriteFileAsync(documentPath, bundle.DocumentBytes, cancellationToken).ConfigureAwait(false);

        return new TerrainExportReceipt(Path.GetFullPath(documentPath));
    }

    private static async Task WriteFileAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(path, bytes.ToArray(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new TerrainExportException($"Could not write the terrain export file '{path}'.", ex);
        }
    }
}
