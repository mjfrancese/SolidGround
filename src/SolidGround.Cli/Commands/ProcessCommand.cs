using System.Globalization;
using SolidGround.Cli.Options;
using SolidGround.Cli.Processing;
using SolidGround.Cli.Rasters;
using SolidGround.Core.Clipping;
using SolidGround.Core.Exports;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Rasters;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Commands;

/// <summary>
/// The offline `process` command: parses an already-downloaded AAIGrid raster, optionally clips it, and
/// writes an export bundle. See docs/architecture/cli-workflow.md's "Commands" and "Options and defaults"
/// sections. Several members here are <see langword="internal"/> rather than <see langword="private"/>
/// because <see cref="Commands.RunCommand"/> shares the identical processing-option parsing, base-name rule,
/// and diagnostics printing (`run` calls the same processing pipeline `process` does, per the "Commands"
/// section, so their option handling and diagnostics must not drift apart either).
/// </summary>
internal static class ProcessCommand
{
    internal static async Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(host);

        // --output is validated up front, before any file read, key check, or pipeline work -- see
        // docs/architecture/cli-workflow.md's "Options and defaults" section. Shared with FetchCommand and
        // RunCommand so all three commands reject a blank --output identically.
        string outputDirectory = invocation.GetValue("output")!;
        ValidateOutputDirectory(outputDirectory);

        string ascPath = invocation.GetValue("asc")!;
        string prjPath = invocation.GetValue("prj") ?? Path.ChangeExtension(ascPath, ".prj");
        string? explicitSourceJsonPath = invocation.GetValue("source-json");
        string defaultSourceJsonPath = Path.ChangeExtension(ascPath, RasterSetIo.SourceFileExtension);
        string sourceJsonPath = explicitSourceJsonPath ?? defaultSourceJsonPath;

        AoiSelection? aoi = AoiSelection.Bind(invocation, OptionTable.Process, required: false);
        LocalOriginSelection origin = ParseOrigin(invocation);
        LengthUnit outputUnit = ParseLengthUnitValue("unit", invocation.GetValue("unit") ?? "us-survey-foot");
        SimplificationMethod method = ParseMethod(invocation);
        int budget = ParseBudget(invocation);
        double coverageFloor = ParseCoverageFloor(invocation);

        string baseName = invocation.GetValue("name") ?? "terrain";
        ValidateBaseName(baseName);
        bool overwrite = invocation.HasOption("overwrite");
        bool verbose = invocation.HasOption("verbose");

        string documentPath = Path.Combine(outputDirectory, baseName + TerrainExportBundleRenderer.DocumentFileSuffix);
        string pointsPath = Path.Combine(outputDirectory, baseName + TerrainExportBundleRenderer.PointsFileSuffix);
        if (!overwrite && (File.Exists(documentPath) || File.Exists(pointsPath)))
        {
            throw new CliUsageException($"'{documentPath}' already exists; pass --overwrite to replace it.");
        }

        string ascText = ReadOperandFile("--asc", ascPath);
        string prjText = ReadOperandFile("--prj", prjPath);

        RasterSourceSidecar? sidecar = null;
        if (explicitSourceJsonPath is not null)
        {
            sidecar = RasterSourceSidecarIo.Read(ReadOperandBytes("--source-json", sourceJsonPath), sourceJsonPath);
        }
        else if (File.Exists(defaultSourceJsonPath))
        {
            sidecar = RasterSourceSidecarIo.Read(ReadOperandBytes("--source-json", defaultSourceJsonPath), defaultSourceJsonPath);
        }

        WellKnownTextReference parsedPrj;
        try
        {
            parsedPrj = WellKnownTextReferenceParser.Parse(prjText);
        }
        catch (FormatException ex)
        {
            throw new CliUsageException($"--prj '{prjPath}' could not be parsed: {ex.Message}", ex);
        }

        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, prjText);

        string? cliVerticalDatum = invocation.GetValue("vertical-datum");
        LengthUnit? cliVerticalUnit = invocation.HasOption("vertical-unit")
            ? ParseLengthUnitValue("vertical-unit", invocation.GetValue("vertical-unit")!)
            : null;
        string? cliGeoid = invocation.GetValue("geoid");
        VerticalReferenceResolution.ResolvedVerticalReference resolvedVertical = VerticalReferenceResolution.Resolve(
            cliVerticalDatum, cliVerticalUnit, cliGeoid, sidecar, parsedPrj.Vertical);
        VerticalReference verticalReference = resolvedVertical.Reference;

        host.StandardOutput.WriteLine($"process: read '{ascPath}'.");

        ElevationGrid grid;
        using (StringReader ascReader = new(ascText))
        {
            try
            {
                grid = AaiGridParser.Parse(ascReader, transform.Definition.TargetReference, verticalReference);
            }
            catch (FormatException ex)
            {
                throw new CliUsageException($"--asc '{ascPath}' could not be parsed: {ex.Message}", ex);
            }
        }

        string sourceName = invocation.GetValue("source-name") ?? sidecar?.SourceName ?? "local-file";
        string datasetIdentifier = invocation.GetValue("dataset") ?? sidecar?.DatasetIdentifier ?? Path.GetFileNameWithoutExtension(ascPath);
        CollectionPeriod? collectionPeriod = ParseCollectionPeriod(invocation) ?? sidecar?.CollectionPeriod;
        string? qualityLevel = invocation.GetValue("quality-level") ?? sidecar?.QualityLevel;
        ElevationSourceMetadata sourceMetadata = new(sourceName, datasetIdentifier, collectionPeriod, qualityLevel);

        cancellationToken.ThrowIfCancellationRequested();

        ReferenceOrigins referenceOrigins = new(ReferenceOrigin.Operator, resolvedVertical.Origin);
        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid, transform, verticalReference, referenceOrigins, sourceMetadata, aoi, origin, outputUnit, method, budget, coverageFloor, cancellationToken)
            .ConfigureAwait(false);

        PrintClipStage(host, "process", verbose, aoi, grid, outcome.ClipResult);
        FetchCommand.PrintReferenceLine(
            host, "process", transform.Definition.TargetReference, ReferenceOrigin.Operator, verticalReference, resolvedVertical.Origin);
        host.StandardOutput.WriteLine(
            $"process: simplified to {outcome.Payload.Provenance.RetainedPointCount.ToString(CultureInfo.InvariantCulture)} of " +
            $"{outcome.Payload.Provenance.OriginalPointCount.ToString(CultureInfo.InvariantCulture)} points ({method}).");
        if (verbose && outcome.SimplificationDiagnostics is { } diagnostics)
        {
            PrintSimplificationDiagnostics(host, "process", diagnostics);
        }

        host.StandardOutput.WriteLine($"process: coverage floor {coverageFloor.ToString("R", CultureInfo.InvariantCulture)}.");

        FileSystemTerrainExporter exporter;
        try
        {
            exporter = new FileSystemTerrainExporter(outputDirectory, baseName);
        }
        catch (ArgumentException ex)
        {
            // Defensive: --output and --name are already validated above, so this should never fire; never
            // ex.Message regardless, per the same reasoning as AoiSelection's bbox/radius catches.
            throw new CliUsageException("--output or --name is invalid.", ex);
        }

        TerrainExportReceipt receipt = await exporter.ExportAsync(outcome.Payload, cancellationToken).ConfigureAwait(false);
        host.StandardOutput.WriteLine($"process: wrote '{receipt.DestinationIdentifier}'.");

        if (verbose)
        {
            PrintProvenanceSummary(host, "process", outcome.Payload.Provenance);
        }

        return CliExitCodes.Success;
    }

    // ---- shared by RunCommand (identical processing options and diagnostics) --------------------------

    internal static LocalOriginSelection ParseOrigin(ParsedInvocation invocation) =>
        LocalOriginSelection.Parse(invocation.GetValue("origin") ?? "southwest");

    internal static LengthUnit ParseLengthUnitValue(string optionName, string text) => text switch
    {
        "us-survey-foot" => LengthUnit.UsSurveyFoot,
        "international-foot" => LengthUnit.InternationalFoot,
        "meter" => LengthUnit.Meter,
        _ => throw new CliUsageException($"--{optionName} must be one of: us-survey-foot, international-foot, meter."),
    };

    internal static SimplificationMethod ParseMethod(ParsedInvocation invocation)
    {
        string text = invocation.GetValue("method") ?? "curvature-aware";
        return text switch
        {
            "curvature-aware" => SimplificationMethod.CurvatureAware,
            "uniform" => SimplificationMethod.UniformSampler,
            _ => throw new CliUsageException("--method must be one of: curvature-aware, uniform."),
        };
    }

    internal static int ParseBudget(ParsedInvocation invocation)
    {
        string text = invocation.GetValue("budget") ?? "15000";
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int budget) || budget <= 0)
        {
            throw new CliUsageException("--budget must be a positive integer.");
        }

        return budget;
    }

    internal static double ParseCoverageFloor(ParsedInvocation invocation)
    {
        string text = invocation.GetValue("coverage-floor") ?? "0.2";
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            || !double.IsFinite(value) || value < 0d || value > 1d)
        {
            throw new CliUsageException("--coverage-floor must be a finite number between 0 and 1.");
        }

        return value;
    }

    /// <exception cref="CliUsageException">Only one of the pair was given, or the end precedes the start.</exception>
    internal static CollectionPeriod? ParseCollectionPeriod(ParsedInvocation invocation)
    {
        string? startText = invocation.GetValue("collection-start");
        string? endText = invocation.GetValue("collection-end");
        if (startText is null && endText is null)
        {
            return null;
        }

        if (startText is null || endText is null)
        {
            throw new CliUsageException("--collection-start and --collection-end must be given together.");
        }

        DateOnly start = ParseExactDate("collection-start", startText);
        DateOnly end = ParseExactDate("collection-end", endText);
        if (start > end)
        {
            throw new CliUsageException("--collection-end must be on or after --collection-start.");
        }

        return new CollectionPeriod(start, end);
    }

    private static DateOnly ParseExactDate(string optionName, string text)
    {
        // Deliberately DateOnly.TryParseExact, never the bare DateOnly.Parse/TryParse(string, out _)
        // overloads, whose accepted separator, component order, and calendar all default to
        // CultureInfo.CurrentCulture when no IFormatProvider is given -- see docs/architecture/cli-workflow.md's
        // "Options and defaults" section.
        if (!DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
        {
            throw new CliUsageException($"--{optionName} must be a valid yyyy-MM-dd date.");
        }

        return date;
    }

    /// <summary>
    /// Validates <c>--output</c> up front -- non-blank -- before any file read, key check, or pipeline work,
    /// naming <c>--output</c> itself rather than relying on a downstream path or parameter name. Shared by
    /// `process`, `fetch`, and `run` so all three reject a blank <c>--output</c> identically. See
    /// docs/architecture/cli-workflow.md's "Options and defaults" section.
    /// </summary>
    /// <exception cref="CliUsageException"><paramref name="outputDirectory"/> is empty or all-whitespace.</exception>
    internal static void ValidateOutputDirectory(string outputDirectory)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new CliUsageException("--output must not be empty or all-whitespace.");
        }
    }

    /// <summary>The CLI's own copy of the base-name rule <c>TerrainExportBundleRenderer</c>/<c>FileSystemTerrainExporter</c> enforce internally (that helper is `internal` to Core), so an invalid name is rejected before any work begins. See docs/architecture/cli-workflow.md's "Options and defaults" section.</summary>
    /// <exception cref="CliUsageException"><paramref name="baseName"/> fails the five-part rule.</exception>
    internal static void ValidateBaseName(string baseName)
    {
        if (string.IsNullOrEmpty(baseName) || baseName.Any(char.IsWhiteSpace))
        {
            throw new CliUsageException("--name must be non-empty and cannot contain whitespace.");
        }

        if (!baseName.All(IsAllowedBaseNameCharacter))
        {
            throw new CliUsageException("--name can contain only ASCII letters, digits, '.', '_', and '-'.");
        }

        if (baseName[0] is '.' or '-')
        {
            throw new CliUsageException("--name cannot start with '.' or '-'.");
        }

        if (baseName.EndsWith(TerrainExportBundleRenderer.DocumentFileSuffix, StringComparison.Ordinal)
            || baseName.EndsWith(TerrainExportBundleRenderer.PointsFileSuffix, StringComparison.Ordinal))
        {
            throw new CliUsageException("--name cannot already end with '.solidground.json' or '.points.csv'.");
        }
    }

    private static bool IsAllowedBaseNameCharacter(char c) =>
        (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c is '.' or '_' or '-';

    /// <summary>Reads an operator-supplied path for `process`/`run` (never `verify`), naming the option and path on failure. See docs/architecture/cli-workflow.md's "Exit codes and error classes" section.</summary>
    internal static string ReadOperandFile(string optionName, string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliUsageException($"{optionName} '{path}' could not be read.", ex);
        }
    }

    internal static byte[] ReadOperandBytes(string optionName, string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CliUsageException($"{optionName} '{path}' could not be read.", ex);
        }
    }

    internal static void PrintClipStage(CliHost host, string verb, bool verbose, AoiSelection? aoi, ElevationGrid sourceGrid, GridClipResult? clipResult)
    {
        if (aoi is null)
        {
            host.StandardOutput.WriteLine($"{verb}: no AOI given; processing the whole grid.");
            return;
        }

        if (clipResult is null)
        {
            return;
        }

        host.StandardOutput.WriteLine(
            $"{verb}: clipped to {clipResult.OutputRowCount.ToString(CultureInfo.InvariantCulture)}x" +
            $"{clipResult.OutputColumnCount.ToString(CultureInfo.InvariantCulture)} cells " +
            $"({clipResult.RetainedElevationCount.ToString(CultureInfo.InvariantCulture)} retained).");

        if (verbose)
        {
            host.StandardOutput.WriteLine(
                $"{verb}: clip statistics: input {sourceGrid.RowCount.ToString(CultureInfo.InvariantCulture)}x" +
                $"{sourceGrid.ColumnCount.ToString(CultureInfo.InvariantCulture)}, output " +
                $"{clipResult.OutputRowCount.ToString(CultureInfo.InvariantCulture)}x{clipResult.OutputColumnCount.ToString(CultureInfo.InvariantCulture)}, " +
                $"retained {clipResult.RetainedElevationCount.ToString(CultureInfo.InvariantCulture)}, source-NODATA " +
                $"{clipResult.NoDataCellsInsideRegion.ToString(CultureInfo.InvariantCulture)}, region-excluded " +
                $"{clipResult.ExcludedCellCount.ToString(CultureInfo.InvariantCulture)}.");
        }
    }

    internal static void PrintSimplificationDiagnostics(CliHost host, string verb, SimplificationDiagnostics diagnostics)
    {
        host.StandardOutput.WriteLine(
            $"{verb}: simplification diagnostics: candidates {diagnostics.CandidatePointCount.ToString(CultureInfo.InvariantCulture)}, " +
            $"structural {diagnostics.StructuralPointCount.ToString(CultureInfo.InvariantCulture)}/" +
            $"{diagnostics.StructuralCandidateCount.ToString(CultureInfo.InvariantCulture)}, curvature-selected " +
            $"{diagnostics.CurvatureSelectedPointCount.ToString(CultureInfo.InvariantCulture)}, coverage-floor " +
            $"{diagnostics.CoverageFloorPointCount.ToString(CultureInfo.InvariantCulture)}, uniform " +
            $"{diagnostics.UniformlySampledPointCount.ToString(CultureInfo.InvariantCulture)}, interior-exhausted " +
            $"{diagnostics.InteriorCandidatesExhausted}.");
        host.StandardOutput.WriteLine(
            $"{verb}: removed-candidate curvature max {diagnostics.MaxRemovedCurvatureMagnitude.ToString("R", CultureInfo.InvariantCulture)} mean " +
            $"{diagnostics.MeanRemovedCurvatureMagnitude.ToString("R", CultureInfo.InvariantCulture)}, removed-candidate elevation residual max " +
            $"{diagnostics.MaxRemovedElevationResidual.ToString("R", CultureInfo.InvariantCulture)} mean " +
            $"{diagnostics.MeanRemovedElevationResidual.ToString("R", CultureInfo.InvariantCulture)}.");
    }

    internal static void PrintProvenanceSummary(CliHost host, string verb, TerrainProvenance provenance)
    {
        host.StandardOutput.WriteLine($"{verb}: source '{provenance.Source.SourceName}' dataset '{provenance.Source.DatasetIdentifier}'.");
        if (provenance.Source.CollectionPeriod is { } period)
        {
            host.StandardOutput.WriteLine(
                $"{verb}: collection period {period.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} to " +
                $"{period.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.");
        }

        if (provenance.Source.QualityLevel is { } qualityLevel)
        {
            host.StandardOutput.WriteLine($"{verb}: quality level '{qualityLevel}'.");
        }

        host.StandardOutput.WriteLine(
            $"{verb}: horizontal reference '{provenance.HorizontalTransformation.TargetReference.CoordinateReferenceSystem}' datum " +
            $"'{provenance.HorizontalTransformation.TargetReference.Datum}'.");
        host.StandardOutput.WriteLine(
            $"{verb}: vertical reference datum '{provenance.SourceVerticalReference.Datum}' unit {provenance.SourceVerticalReference.Unit} " +
            $"geoid '{provenance.SourceVerticalReference.GeoidModel ?? "(none)"}'.");
        host.StandardOutput.WriteLine(
            $"{verb}: local origin ({provenance.LocalFrame.Origin.X.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"{provenance.LocalFrame.Origin.Y.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"{provenance.LocalFrame.Origin.Elevation.ToString("R", CultureInfo.InvariantCulture)}) output unit {provenance.LocalFrame.OutputUnit}.");
        host.StandardOutput.WriteLine(
            $"{verb}: points {provenance.RetainedPointCount.ToString(CultureInfo.InvariantCulture)} of " +
            $"{provenance.OriginalPointCount.ToString(CultureInfo.InvariantCulture)} retained.");
        if (provenance.ElevationRange is { } range)
        {
            host.StandardOutput.WriteLine(
                $"{verb}: elevation range {range.Minimum.ToString("R", CultureInfo.InvariantCulture)} to " +
                $"{range.Maximum.ToString("R", CultureInfo.InvariantCulture)} ({range.Unit}).");
        }
    }
}
