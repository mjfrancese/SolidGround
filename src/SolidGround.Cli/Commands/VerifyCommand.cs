using System.Globalization;
using SolidGround.Cli.Options;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Provenance;
using SolidGround.Core.Transformations;

namespace SolidGround.Cli.Commands;

/// <summary>
/// The offline `verify` command: reads a written export bundle strictly, prints its provenance summary, and
/// confirms that reconstructing every sample's source coordinate and converting it back is bit-exact. See
/// docs/architecture/cli-workflow.md's "Commands" and "Exit codes and error classes" sections: `verify`'s own
/// exit-code set is the closed <c>{0, 2, 5, 130}</c> (130 like every command, on cancellation), and its
/// file-reading convention (I/O failure -> 5, invalid content -> 2) is the reverse of `process`/`run`'s.
/// </summary>
internal static class VerifyCommand
{
    internal static Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(host);

        string documentPath = invocation.GetValue("document")!;
        if (!documentPath.EndsWith(TerrainExportBundleRenderer.DocumentFileSuffix, StringComparison.Ordinal))
        {
            throw new CliUsageException($"--document '{documentPath}' must end with '{TerrainExportBundleRenderer.DocumentFileSuffix}'.");
        }

        string defaultPointsPath =
            documentPath[..^TerrainExportBundleRenderer.DocumentFileSuffix.Length] + TerrainExportBundleRenderer.PointsFileSuffix;
        string pointsPath = invocation.GetValue("points") ?? defaultPointsPath;
        bool verbose = invocation.HasOption("verbose");

        byte[] documentBytes;
        byte[] pointsBytes;
        try
        {
            documentBytes = File.ReadAllBytes(documentPath);
            pointsBytes = File.ReadAllBytes(pointsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reversed from every other command's own "unreadable input file" rule (docs/architecture/
            // cli-workflow.md's "Exit codes and error classes" section): verify treats an I/O failure
            // reading its bundle as exit 5, not 2. A fixed CLI-authored message, never ex.Message, matching
            // every sibling read-failure path (ProcessCommand.ReadOperandFile/ReadOperandBytes,
            // AoiSelection.ReadOperandFile, CliOpenTopographyApiKeyProvider.GetApiKey), since an inner
            // exception's own wording is platform-dependent.
            host.StandardError.WriteLine($"error (processing): '{documentPath}' or '{pointsPath}' could not be read.");
            return Task.FromResult(CliExitCodes.Processing);
        }

        cancellationToken.ThrowIfCancellationRequested();

        TerrainExportPayload payload;
        try
        {
            payload = TerrainExportBundleReader.Read(documentBytes, pointsBytes);
        }
        catch (TerrainExportException ex)
        {
            host.StandardError.WriteLine($"error (usage): {ex.Message}");
            return Task.FromResult(CliExitCodes.Usage);
        }

        TerrainProvenance provenance = payload.Provenance;
        PrintSummary(host, provenance);

        LocalCoordinateFrame frame = provenance.LocalFrame;
        double minSourceX = double.PositiveInfinity;
        double maxSourceX = double.NegativeInfinity;
        double minSourceY = double.PositiveInfinity;
        double maxSourceY = double.NegativeInfinity;
        double minElevation = double.PositiveInfinity;
        double maxElevation = double.NegativeInfinity;
        bool roundTripMismatch = false;

        foreach (LocalTerrainSample sample in payload.Samples)
        {
            Coordinate3D source = frame.ToSource(sample.Position);
            minSourceX = Math.Min(minSourceX, source.X);
            maxSourceX = Math.Max(maxSourceX, source.X);
            minSourceY = Math.Min(minSourceY, source.Y);
            maxSourceY = Math.Max(maxSourceY, source.Y);
            minElevation = Math.Min(minElevation, source.Elevation);
            maxElevation = Math.Max(maxElevation, source.Elevation);

            LocalCoordinate roundTrip = frame.ToLocal(source);
            if (BitConverter.DoubleToInt64Bits(roundTrip.X) != BitConverter.DoubleToInt64Bits(sample.Position.X)
                || BitConverter.DoubleToInt64Bits(roundTrip.Y) != BitConverter.DoubleToInt64Bits(sample.Position.Y)
                || BitConverter.DoubleToInt64Bits(roundTrip.Elevation) != BitConverter.DoubleToInt64Bits(sample.Position.Elevation))
            {
                roundTripMismatch = true;
            }
        }

        if (roundTripMismatch)
        {
            host.StandardError.WriteLine("error (usage): the local-to-source coordinate round trip is not bit-exact for at least one sample.");
            return Task.FromResult(CliExitCodes.Usage);
        }

        if (payload.Samples.Count > 0)
        {
            host.StandardOutput.WriteLine(
                $"verify: source coordinate envelope x [{minSourceX.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"{maxSourceX.ToString("R", CultureInfo.InvariantCulture)}], y [{minSourceY.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"{maxSourceY.ToString("R", CultureInfo.InvariantCulture)}].");
            host.StandardOutput.WriteLine(
                $"verify: reconstructed elevation range [{minElevation.ToString("R", CultureInfo.InvariantCulture)}, " +
                $"{maxElevation.ToString("R", CultureInfo.InvariantCulture)}].");
        }

        host.StandardOutput.WriteLine("verify: the local-to-source coordinate round trip is bit-exact for every sample.");

        if (verbose)
        {
            CoordinateOperationDefinition forward = provenance.HorizontalTransformation.ForwardOperation;
            CoordinateOperationDefinition inverse = provenance.HorizontalTransformation.InverseOperation;
            host.StandardOutput.WriteLine($"verify: forward operation ({forward.Format}): {forward.Definition}");
            host.StandardOutput.WriteLine($"verify: inverse operation ({inverse.Format}): {inverse.Definition}");
        }

        return Task.FromResult(CliExitCodes.Success);
    }

    private static void PrintSummary(CliHost host, TerrainProvenance provenance)
    {
        host.StandardOutput.WriteLine($"verify: source '{provenance.Source.SourceName}' dataset '{provenance.Source.DatasetIdentifier}'.");
        host.StandardOutput.WriteLine(
            $"verify: horizontal datum '{provenance.HorizontalTransformation.TargetReference.Datum}', vertical datum " +
            $"'{provenance.SourceVerticalReference.Datum}' ({provenance.SourceVerticalReference.Unit}).");
        if (provenance.ElevationRange is { } range)
        {
            host.StandardOutput.WriteLine(
                $"verify: elevation range {range.Minimum.ToString("R", CultureInfo.InvariantCulture)} to " +
                $"{range.Maximum.ToString("R", CultureInfo.InvariantCulture)} ({range.Unit}).");
        }

        host.StandardOutput.WriteLine(
            $"verify: {provenance.RetainedPointCount.ToString(CultureInfo.InvariantCulture)} of " +
            $"{provenance.OriginalPointCount.ToString(CultureInfo.InvariantCulture)} points retained.");
    }
}
