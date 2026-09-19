using System.Globalization;
using SolidGround.Cli.Options;
using SolidGround.Cli.Processing;
using SolidGround.Cli.Rasters;
using SolidGround.Cli.Secrets;
using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;

namespace SolidGround.Cli.Commands;

/// <summary>
/// The online `fetch` command: acquires a raster set from OpenTopography for exactly one AOI and performs no
/// processing. See docs/architecture/cli-workflow.md's "Commands", "Secrets and key resolution", and "Raster
/// set persistence" sections. Several members here are <see langword="internal"/> because
/// <see cref="Commands.RunCommand"/> reuses the identical key-resolution message, acquisition-evidence
/// printing, and sidecar-building logic (`run` acquires exactly as `fetch` does, per the "Commands" section).
/// </summary>
internal static class FetchCommand
{
    /// <summary>The exact message docs/architecture/cli-workflow.md's "Secrets and key resolution" section requires, verbatim, so a missing key is reported identically by `fetch` and `run`.</summary>
    internal const string MissingApiKeyMessage =
        "error (authorization): No OpenTopography API key is configured. Set OPENTOPOGRAPHY_API_KEY in the " +
        "process environment, or run: dotnet user-secrets set OPENTOPOGRAPHY_API_KEY \"<key>\" --id solidground-cli";

    internal static async Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(host);

        // --output is validated up front, before any file read, key check, or pipeline work -- see
        // docs/architecture/cli-workflow.md's "Options and defaults" section.
        string outputDirectory = invocation.GetValue("output")!;
        ProcessCommand.ValidateOutputDirectory(outputDirectory);

        AoiSelection aoi = AoiSelection.Bind(invocation, OptionTable.Fetch, required: true)!;

        string baseName = invocation.GetValue("name") ?? "terrain";
        ProcessCommand.ValidateBaseName(baseName);
        bool overwrite = invocation.HasOption("overwrite");
        bool verbose = invocation.HasOption("verbose");
        int timeoutSeconds = ParseTimeout(invocation);

        RasterSetPaths paths = RasterSetIo.ResolvePaths(outputDirectory, baseName);
        if (!overwrite && RasterSetIo.Exists(paths))
        {
            throw new CliUsageException($"A raster set named '{baseName}' already exists in '{outputDirectory}'; pass --overwrite to replace it.");
        }

        CliOpenTopographyApiKeyProvider keyProvider = new(host.GetEnvironmentVariable);
        OpenTopographyApiKey? key = keyProvider.GetApiKey();
        if (key is null)
        {
            host.StandardError.WriteLine(MissingApiKeyMessage);
            return CliExitCodes.Authorization;
        }

        using HttpClient httpClient = new(host.HttpMessageHandlerFactory(), disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
        };
        OpenTopographyUsgs1mSource source = new(httpClient, new StaticOpenTopographyApiKeyProvider(key));

        HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;
        Wgs84BoundingBoxAoi fetchEnvelope = ClipRegionFactory.BuildFetchEnvelope(aoi, wgs84Reference);

        host.StandardOutput.WriteLine("fetch: requesting OpenTopography...");
        OpenTopographyUsgs1mAcquisition acquisition = await source.AcquireDetailedAsync(
            new ElevationSourceRequest(fetchEnvelope), cancellationToken).ConfigureAwait(false);

        if (verbose)
        {
            PrintAcquisitionEvidence(host, "fetch", acquisition);
        }

        RasterSourceSidecar sidecar = BuildSidecar(acquisition);
        await RasterSetIo.WriteAsync(paths, (ElevationGrid)acquisition.Acquisition.Data, acquisition.Evidence.WellKnownText, sidecar, cancellationToken)
            .ConfigureAwait(false);

        host.StandardOutput.WriteLine($"fetch: wrote '{paths.GridPath}', '{paths.ReferencePath}', '{paths.SourcePath}'.");
        return CliExitCodes.Success;
    }

    internal static int ParseTimeout(ParsedInvocation invocation)
    {
        string text = invocation.GetValue("timeout") ?? "300";
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds) || seconds <= 0)
        {
            throw new CliUsageException("--timeout must be a positive integer.");
        }

        return seconds;
    }

    /// <summary>Builds the `.source.json` sidecar directly from the acquisition; no CLI option is involved. See docs/architecture/cli-workflow.md's "Raster set persistence" section for the fixed property set this mirrors.</summary>
    internal static RasterSourceSidecar BuildSidecar(OpenTopographyUsgs1mAcquisition acquisition) => new(
        acquisition.Acquisition.Source.SourceName,
        acquisition.Acquisition.Source.DatasetIdentifier,
        acquisition.Acquisition.Source.CollectionPeriod,
        acquisition.Acquisition.Source.QualityLevel,
        new RasterSourceVertical(
            acquisition.Acquisition.Data.VerticalReference.Datum,
            acquisition.Acquisition.Data.VerticalReference.Unit,
            acquisition.Acquisition.Data.VerticalReference.GeoidModel),
        new RasterSourceAcquisition(
            acquisition.Evidence.RedactedRequestUri,
            (int)acquisition.Evidence.StatusCode,
            acquisition.Evidence.ContentType,
            acquisition.Evidence.ContentDispositionFileName,
            acquisition.Evidence.ArchiveEntryNames,
            acquisition.Evidence.ReferenceSource.ToString(),
            acquisition.Evidence.ResponseByteCount));

    /// <summary>
    /// Prints every field of the acquisition's own redacted evidence. Every value here is already redacted
    /// by Core before the CLI ever sees it (docs/architecture/cli-workflow.md's "Diagnostics and redaction"
    /// section); this method performs no redaction of its own and never touches the API key.
    /// </summary>
    internal static void PrintAcquisitionEvidence(CliHost host, string verb, OpenTopographyUsgs1mAcquisition acquisition)
    {
        OpenTopographyResponseEvidence evidence = acquisition.Evidence;
        host.StandardOutput.WriteLine($"{verb}: request '{evidence.RedactedRequestUri}'.");
        host.StandardOutput.WriteLine($"{verb}: status {((int)evidence.StatusCode).ToString(CultureInfo.InvariantCulture)} ({evidence.StatusCode}).");
        host.StandardOutput.WriteLine($"{verb}: content type '{evidence.ContentType ?? "(none)"}'.");
        host.StandardOutput.WriteLine($"{verb}: content-disposition file name '{evidence.ContentDispositionFileName ?? "(none)"}'.");
        host.StandardOutput.WriteLine($"{verb}: archive entries: {string.Join(", ", evidence.ArchiveEntryNames)}.");
        host.StandardOutput.WriteLine($"{verb}: coordinate reference source: {evidence.ReferenceSource}.");
        host.StandardOutput.WriteLine($"{verb}: response byte count: {evidence.ResponseByteCount.ToString(CultureInfo.InvariantCulture)}.");
    }
}
