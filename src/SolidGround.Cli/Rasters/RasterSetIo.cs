using System.Text;
using SolidGround.Core.Rasters;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;

namespace SolidGround.Cli.Rasters;

/// <summary>
/// The three sibling file paths that make up one raster set, derived from a shared output directory and
/// base name. See docs/architecture/cli-workflow.md's "Raster set persistence" section.
/// </summary>
internal sealed record RasterSetPaths(string GridPath, string ReferencePath, string SourcePath);

/// <summary>
/// Writes the three files that make up a raster set (<c>.asc</c>, <c>.prj</c>, <c>.source.json</c>) written
/// by <c>fetch</c> and, with <c>--save-raster</c>, by <c>run</c>. See docs/architecture/cli-workflow.md's
/// "Raster set persistence" section for the file triple's shape and the <c>.prj</c> file's verbatim-copy
/// requirement, and its "Known limitations and follow-ups" section for why -- like
/// <c>FileSystemTerrainExporter</c> in Core -- these three writes are not atomic: there is no
/// temp-file-then-rename step, so a cancellation or crash during (rather than before) a write can leave a
/// truncated file on disk.
/// </summary>
internal static class RasterSetIo
{
    internal const string GridFileExtension = ".asc";
    internal const string ReferenceFileExtension = ".prj";
    internal const string SourceFileExtension = ".source.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Computes the raster set's three file paths from an output directory and a shared base name.</summary>
    internal static RasterSetPaths ResolvePaths(string outputDirectory, string baseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        return new RasterSetPaths(
            Path.Combine(outputDirectory, baseName + GridFileExtension),
            Path.Combine(outputDirectory, baseName + ReferenceFileExtension),
            Path.Combine(outputDirectory, baseName + SourceFileExtension));
    }

    /// <summary>
    /// True when any of the raster set's three files already exists. A command checks this itself, before
    /// <see cref="WriteAsync"/> does any work, so it can honor <c>--overwrite</c> the same way the export
    /// bundle's own existence check does.
    /// </summary>
    internal static bool Exists(RasterSetPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        return File.Exists(paths.GridPath) || File.Exists(paths.ReferencePath) || File.Exists(paths.SourcePath);
    }

    /// <summary>
    /// Writes the grid as an Esri ASCII raster, the horizontal reference's well-known text verbatim, and the
    /// source sidecar, to <paramref name="paths"/>, creating its directory first if needed.
    /// </summary>
    /// <exception cref="CliProcessingException">
    /// The output directory could not be created, or a file could not be written, because of an I/O or
    /// access error.
    /// </exception>
    internal static async Task WriteAsync(
        RasterSetPaths paths,
        ElevationGrid grid,
        string wellKnownText,
        RasterSourceSidecar sidecar,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(wellKnownText);
        ArgumentNullException.ThrowIfNull(sidecar);
        cancellationToken.ThrowIfCancellationRequested();

        string? directory = Path.GetDirectoryName(paths.GridPath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new CliProcessingException($"Could not create the raster set output directory '{directory}'.", ex);
            }
        }

        await WriteFileAsync(paths.GridPath, RenderGrid(grid), cancellationToken).ConfigureAwait(false);
        await WriteFileAsync(paths.ReferencePath, Utf8NoBom.GetBytes(wellKnownText), cancellationToken).ConfigureAwait(false);
        await WriteFileAsync(paths.SourcePath, RenderSidecar(sidecar), cancellationToken).ConfigureAwait(false);
    }

    private static byte[] RenderGrid(ElevationGrid grid)
    {
        using MemoryStream stream = new();
        using (StreamWriter writer = new(stream, Utf8NoBom, leaveOpen: true))
        {
            AaiGridWriter.Write(grid, writer);
        }

        return stream.ToArray();
    }

    private static byte[] RenderSidecar(RasterSourceSidecar sidecar)
    {
        using MemoryStream stream = new();
        RasterSourceSidecarIo.Write(sidecar, stream);
        return stream.ToArray();
    }

    private static async Task WriteFileAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            await File.WriteAllBytesAsync(path, bytes, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliProcessingException($"Could not write the raster set file '{path}'.", ex);
        }
    }
}
