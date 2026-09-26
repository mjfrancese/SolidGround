using System.Text;
using System.Text.Json;

namespace SolidGround.Cli;

/// <summary>
/// Renders one JSON document deterministically for <c>geocode</c>/<c>parcel</c>, reusing the exact
/// <see cref="Utf8JsonWriter"/> discipline <c>RasterSourceSidecarIo.Write</c>/<c>TerrainExportBundleRenderer.Render</c>
/// already use for files (see docs/architecture/cli-workflow.md's "Determinism" section), adapted to an
/// in-memory buffer because <see cref="CliHost.StandardOutput"/> is a <see cref="TextWriter"/>, not a
/// <see cref="Stream"/>. This is the only new stdout-JSON convention this CLI introduces, shared by both verbs
/// rather than duplicated per command.
/// </summary>
internal static class CliJsonOutput
{
    private static readonly JsonWriterOptions Options = new()
    {
        Indented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        NewLine = "\n",
    };

    /// <summary>
    /// Returns UTF-8-decoded text with exactly one trailing <c>'\n'</c>, matching the file writers' own "one
    /// trailing newline after dispose" convention. Callers write the result via
    /// <c>host.StandardOutput.Write(...)</c>, never <c>WriteLine</c>, so no second newline is added.
    /// </summary>
    internal static string Render(Action<Utf8JsonWriter> writeBody)
    {
        ArgumentNullException.ThrowIfNull(writeBody);

        using MemoryStream buffer = new();
        using (Utf8JsonWriter writer = new(buffer, Options))
        {
            writeBody(writer);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}
