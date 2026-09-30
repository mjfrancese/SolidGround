using System.Globalization;
using SolidGround.Cli.Options;
using SolidGround.Core.Accuracy;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Rasters;
using SolidGround.Core.Terrain;
using SolidGround.Core.Units;

namespace SolidGround.Cli.Commands;

/// <summary>
/// Offline comparison yardstick. It intentionally accepts only an identical projected CRS today: Core's
/// existing managed transform contract is geographic-to-projected, so pretending it can safely transform an
/// arbitrary projected CSV would be a false capability. A future projected-to-projected transform may extend
/// this command behind its own tested contract.
/// </summary>
internal static class ScoreCommand
{
    internal static Task<int> RunAsync(ParsedInvocation invocation, CliHost host, CancellationToken cancellationToken)
    {
        string ascPath = invocation.GetValue("reference-asc")!;
        string prjPath = invocation.GetValue("reference-prj")!;
        string verticalDatum = RequireNonBlank(invocation.GetValue("reference-vertical-datum"), "--reference-vertical-datum");
        LengthUnit referenceVerticalUnit = ProcessCommand.ParseLengthUnit("reference-vertical-unit", invocation.GetValue("reference-vertical-unit")!);
        LengthUnit reportUnit = invocation.HasOption("unit") ? ProcessCommand.ParseLengthUnit("unit", invocation.GetValue("unit")!) : LengthUnit.Meter;
        bool bundleGiven = invocation.HasOption("bundle");
        bool pointsGiven = invocation.HasOption("points");
        if (bundleGiven == pointsGiven)
        {
            throw new CliUsageException("exactly one of --bundle or --points is required.");
        }

        string prjText = ProcessCommand.ReadOperandFile("--reference-prj", prjPath);
        WellKnownTextReference parsedPrj;
        try { parsedPrj = WellKnownTextReferenceParser.Parse(prjText); }
        catch (FormatException ex) { throw new CliUsageException($"--reference-prj '{prjPath}' could not be parsed: {ex.Message}", ex); }
        if (parsedPrj.Horizontal.Kind != HorizontalReferenceKind.Projected)
        {
            throw new CliUsageException("--reference-prj must declare a projected horizontal reference.");
        }

        ElevationGrid reference;
        try
        {
            using StringReader reader = new(ProcessCommand.ReadOperandFile("--reference-asc", ascPath));
            reference = AaiGridParser.Parse(reader, parsedPrj.Horizontal, new VerticalReference(verticalDatum, referenceVerticalUnit));
        }
        catch (FormatException ex) { throw new CliUsageException($"--reference-asc '{ascPath}' could not be parsed: {ex.Message}", ex); }

        TerrainSample[] surface;
        string verticalAlignment;
        if (bundleGiven)
        {
            (surface, verticalAlignment) = ReadBundle(invocation.GetValue("bundle")!, reference);
        }
        else
        {
            (surface, verticalAlignment) = ReadExternalCsv(invocation, reference);
        }
        cancellationToken.ThrowIfCancellationRequested();

        TerrainErrorReport report = TerrainErrorAnalyzer.AnalyzeSurface(reference, surface, reportUnit);
        if (report.ComparedCellCount == 0)
        {
            host.StandardError.WriteLine(
                "error (processing): no-score: the supplied surface covers no measurable reference cells; " +
                "check for collinear, out-of-domain, or ineligible triangles.");
            return Task.FromResult(CliExitCodes.Processing);
        }
        host.StandardOutput.WriteLine($"score: maximum absolute vertical residual {report.MaximumAbsoluteResidual.ToString("R", CultureInfo.InvariantCulture)} {UnitText(report.Unit)}.");
        host.StandardOutput.WriteLine($"score: RMS vertical residual {report.RootMeanSquareResidual.ToString("R", CultureInfo.InvariantCulture)} {UnitText(report.Unit)}.");
        host.StandardOutput.WriteLine($"score: compared {report.ComparedCellCount.ToString(CultureInfo.InvariantCulture)} reference cell(s); uncovered {report.UncoveredCellCount.ToString(CultureInfo.InvariantCulture)}.");
        foreach ((TerrainCoverageGapReason reason, int count) in report.UncoveredByReason.Where(pair => pair.Value > 0).OrderBy(pair => pair.Key))
        {
            host.StandardOutput.WriteLine($"score: uncovered {reason}: {count.ToString(CultureInfo.InvariantCulture)}.");
        }
        host.StandardOutput.WriteLine($"score: vertical datum {verticalAlignment}.");
        return Task.FromResult(CliExitCodes.Success);
    }

    private static (TerrainSample[] Samples, string Alignment) ReadBundle(string documentPath, ElevationGrid reference)
    {
        if (!documentPath.EndsWith(TerrainExportBundleRenderer.DocumentFileSuffix, StringComparison.Ordinal))
        {
            throw new CliUsageException($"--bundle '{documentPath}' must end with '{TerrainExportBundleRenderer.DocumentFileSuffix}'.");
        }
        string pointsPath = documentPath[..^TerrainExportBundleRenderer.DocumentFileSuffix.Length] + TerrainExportBundleRenderer.PointsFileSuffix;
        TerrainExportPayload payload;
        try { payload = TerrainExportBundleReader.Read(File.ReadAllBytes(documentPath), File.ReadAllBytes(pointsPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TerrainExportException)
        {
            throw new CliUsageException("--bundle or its paired points file could not be read as a valid SolidGround export bundle.", ex);
        }
        if (payload.Provenance.LocalFrame.ProjectedHorizontalReference != reference.HorizontalReference)
        {
            throw new CliUsageException("--bundle's projected CRS does not equal --reference-prj; projected-to-projected transformation is not supported by this scorer.");
        }
        if (!string.Equals(payload.Provenance.SourceVerticalReference.Datum, reference.VerticalReference.Datum, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--bundle's declared vertical datum does not match the reference vertical datum; no vertical datum transformation is available.");
        }
        return ([.. payload.Samples.Select(sample => new TerrainSample(payload.Provenance.LocalFrame.ToSource(sample.Position)))], $"aligned ('{reference.VerticalReference.Datum}')");
    }

    private static (TerrainSample[] Samples, string Alignment) ReadExternalCsv(ParsedInvocation invocation, ElevationGrid reference)
    {
        string[] required = ["external-epsg", "external-axis-order", "external-horizontal-unit", "external-vertical-unit"];
        foreach (string option in required)
        {
            if (!invocation.HasOption(option)) { throw new CliUsageException($"--{option} is required with --points."); }
        }
        string epsg = invocation.GetValue("external-epsg")!;
        if (!string.Equals(epsg, reference.HorizontalReference.CoordinateReferenceSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--external-epsg does not match the reference CRS; projected-to-projected transformation is not supported by this scorer.");
        }
        LengthUnit horizontalUnit = ProcessCommand.ParseLengthUnit("external-horizontal-unit", invocation.GetValue("external-horizontal-unit")!);
        LengthUnit verticalUnit = ProcessCommand.ParseLengthUnit("external-vertical-unit", invocation.GetValue("external-vertical-unit")!);
        string axis = invocation.GetValue("external-axis-order")!;
        if (axis is not ("easting-northing" or "northing-easting")) { throw new CliUsageException("--external-axis-order must be easting-northing or northing-easting."); }
        string? datum = invocation.GetValue("external-vertical-datum");
        if (datum is not null && !string.Equals(datum, reference.VerticalReference.Datum, StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("--external-vertical-datum does not match the reference datum; no vertical datum transformation is available.");
        }
        string csv = ProcessCommand.ReadOperandFile("--points", invocation.GetValue("points")!);
        string[] lines = csv.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2 || !string.Equals(lines[0], "x,y,z", StringComparison.Ordinal))
        {
            throw new CliUsageException("--points must be UTF-8/invariant CSV with an exact x,y,z header and at least one sample.");
        }
        LengthUnit referenceHorizontalUnit = reference.HorizontalReference.Unit.LinearUnit!.Value;
        TerrainSample[] samples = new TerrainSample[lines.Length - 1];
        for (int index = 1; index < lines.Length; index++)
        {
            string[] fields = lines[index].Split(',');
            if (fields.Length != 3 || !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                || !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double z)
                || !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z))
            {
                throw new CliUsageException($"--points line {index.ToString(CultureInfo.InvariantCulture)} must contain three finite invariant-culture numbers.");
            }
            if (axis == "northing-easting") { (x, y) = (y, x); }
            samples[index - 1] = new TerrainSample(new Coordinate3D(
                LengthConverter.Convert(x, horizontalUnit, referenceHorizontalUnit),
                LengthConverter.Convert(y, horizontalUnit, referenceHorizontalUnit),
                LengthConverter.Convert(z, verticalUnit, reference.VerticalReference.Unit)));
        }
        return (samples, datum is null ? "undeclared and not aligned (scored as-is)" : $"aligned ('{reference.VerticalReference.Datum}')");
    }

    private static string RequireNonBlank(string? value, string option) => !string.IsNullOrWhiteSpace(value) ? value : throw new CliUsageException($"{option} must not be blank.");
    private static string UnitText(LengthUnit unit) => unit switch
    {
        LengthUnit.Meter => "metre (exactly 1 m)",
        LengthUnit.UsSurveyFoot => "U.S. survey foot (exactly 1200/3937 m)",
        LengthUnit.InternationalFoot => "international foot (exactly 0.3048 m)",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };
}
