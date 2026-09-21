using System.Globalization;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SolidGround.Core.Aois;
using SolidGround.Core.Clipping;
using SolidGround.Core.Exports;
using SolidGround.Core.Hosting;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Rasters;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using SolidGround.Revit.Diagnostics;
using SolidGround.Revit.Elements;
using SolidGround.Revit.Geometry;
using SolidGround.Revit.Provenance;
using SolidGround.Revit.Settings;
using SolidGround.Revit.Transactions;
using CoreLengthUnit = SolidGround.Core.Units.LengthUnit;

namespace SolidGround.Revit.Commands;

/// <summary>
/// SolidGround Issue #16's fixed extension point (design record §5, orchestrator decision 1): a bare
/// nullable delegate invoked at exactly one call site, after post-create geometry verification passes and
/// before <c>transaction.Commit()</c>, inside the same transaction. <see langword="null"/> in #15; Issue #16
/// changes only the one assignment at that call site to reference its real Extensible-Storage-attaching
/// method. May throw: any exception it raises is handled by the same transaction-wide catch that rolls back
/// and derives <see cref="Result"/> from the observed <see cref="TransactionStatus"/> (orchestrator decision
/// (d)).
/// </summary>
internal delegate void ToposolidCreatedHook(
    Document document, Toposolid toposolid, TerrainExportPayload payload, PlacementRecordDraft placementDraft);

/// <summary>
/// Converts a settings-driven acquisition/processing run into a native Revit <see cref="Toposolid"/>, created
/// inside one <see cref="Transaction"/> with provable-unchanged-on-rejection semantics. See SolidGround Issue
/// #15's design record §6 for the full six-stage flow this implements. SolidGround is a site-form tool, not a
/// survey instrument.
/// </summary>
/// <remarks>
/// <c>message</c> is deliberately left at its caller-provided empty value on every return path, exactly as
/// Issue #14's Preflight-only command already did: Revit only shows its own automatic result dialog when
/// <c>message</c> is non-empty, so leaving it empty and always showing exactly one dialog constructed here is
/// what keeps every outcome to a single dialog.
/// </remarks>
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public sealed class CreateToposolidCommand : IExternalCommand
{
    private const string DialogTitle = "SolidGround";

    /// <summary>Mirrors <c>SolidGround.Cli.Rasters.RasterSetIo.SourceFileExtension</c>'s value: this project cannot reference the CLI assembly (AGENTS.md architecture rule), so the one literal is duplicated here instead.</summary>
    private const string DefaultSourceJsonExtension = ".source.json";

    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            return ExecuteCore(commandData);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("CreateToposolidCommand.Execute failed unexpectedly.", ex);
            TaskDialog.Show(
                DialogTitle,
                "SolidGround hit an unexpected problem and stopped. Nothing in the model changed." +
                Environment.NewLine + Environment.NewLine + ex.Message);
            // No Transaction was opened on any path that can reach this catch below Stage 5's own
            // exception handling, so this is always Cancelled, never Failed (design record §7.1).
            return Result.Cancelled;
        }
    }

    private static Result ExecuteCore(ExternalCommandData commandData)
    {
        // ==== Stage 1: Document Preflight (read-only, no network, no transaction) =========================
        DocumentContext? context = RunDocumentPreflight(commandData, out List<string> preflightProblems);
        if (context is null || preflightProblems.Count > 0)
        {
            ShowProblemList("SolidGround Preflight found a problem.", "Nothing changed. Correct every problem below and run this command again.", preflightProblems);
            return Result.Cancelled;
        }

        // ==== Stage 2: Acquisition (network/file I/O; the only stage using the synchronous bridge) ========
        if (context.Settings.Request.Mode == TerrainAcquisitionMode.Fetch)
        {
            AddInLog.Info(
                $"Fetch mode: Revit will be unresponsive for up to {context.Settings.Request.NetworkTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} " +
                "second(s) while SolidGround requests OpenTopography.");
        }

        (ElevationGrid Grid, TerrainProcessingOutcome Outcome) acquisition;
        try
        {
            using CancellationTokenSource cts = new(TimeSpan.FromSeconds(context.Settings.Request.NetworkTimeoutSeconds));
            acquisition = Task.Run(
                    () => RunPipelineAsync(context.Settings.Request, context.Wgs84Reference, context.Aoi, cts.Token),
                    cts.Token)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsAcquisitionFailure(ex))
        {
            ShowSingleCancelledProblem(AcquisitionFailureHeadline(ex, context.Settings.Request.NetworkTimeoutSeconds), AcquisitionFailureDetail(ex));
            return Result.Cancelled;
        }

        // ==== Stage 3: Geometry Preflight (Core-only; still no Revit API call) =============================
        LocalCoordinateFrame localFrame = acquisition.Outcome.Payload.Provenance.LocalFrame;
        LocalBoundary boundary = acquisition.Outcome.ClipResult is { } clipResult
            ? LocalBoundaryFactory.FromPolygonalRegion(clipResult.EffectiveRegion, localFrame)
            : LocalBoundaryFactory.FromGridEnvelope(acquisition.Grid, localFrame);

        // LocalBoundaryValidator.Validate's tolerance is compared directly against boundary/sample
        // coordinates, which are expressed in the pipeline's own OutputUnit (US survey foot by default), not
        // always meters -- DefaultContainmentToleranceMeters must be converted into that same unit before
        // being passed, exactly as the Stage 5 tolerance below already is (SolidGround Issue #15 review fix).
        double containmentTolerance = LengthConverter.Convert(
            LocalBoundaryValidator.DefaultContainmentToleranceMeters, CoreLengthUnit.Meter, context.Settings.Request.OutputUnit);
        LocalBoundaryValidationResult boundaryValidation = LocalBoundaryValidator.Validate(
            boundary, acquisition.Outcome.Payload.Samples, context.Settings.Request.Simplification.PointBudget, containmentTolerance);
        if (!boundaryValidation.IsValid)
        {
            ShowProblemList("SolidGround could not build a valid boundary.", "Nothing changed. Correct every problem below and run this command again.", boundaryValidation.Problems);
            return Result.Cancelled;
        }

        FileSystemTerrainExporter exporter;
        try
        {
            exporter = new FileSystemTerrainExporter(context.Settings.Request.Output.Directory, context.Settings.Request.Output.BaseName);
        }
        catch (ArgumentException ex)
        {
            // Defensive: TerrainRequestSettings.Validate() already confirmed both are valid before Stage 1 accepted this run.
            ShowSingleCancelledProblem("SolidGround could not use the configured output settings.", $"output.directory or output.baseName is invalid: {ex.Message}");
            return Result.Cancelled;
        }

        try
        {
            // ValueTask must not be blocked on directly (CA2012); AsTask() converts to the safe-to-block-on form.
            _ = exporter.ExportAsync(acquisition.Outcome.Payload, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        }
        catch (TerrainExportException ex)
        {
            ShowSingleCancelledProblem($"The terrain export could not be written to '{context.Settings.Request.Output.Directory}'.", ex.Message);
            return Result.Cancelled;
        }

        string exportDocumentFileName = context.Settings.Request.Output.BaseName + TerrainExportBundleRenderer.DocumentFileSuffix;
        string exportPointsFileName = context.Settings.Request.Output.BaseName + TerrainExportBundleRenderer.PointsFileSuffix;

        // ==== Stage 4: Geometry construction (pre-transaction; Document untouched) =========================
        ForgeTypeId revitUnit = RevitUnitConversion.ToForgeTypeId(context.Settings.Request.OutputUnit);
        AddInLog.Info(
            $"Output unit ForgeTypeId '{revitUnit.TypeId}' ({context.Settings.Request.OutputUnit}), " +
            $"{LengthConverter.MetersPerUnit(context.Settings.Request.OutputUnit).ToString("R", CultureInfo.InvariantCulture)} m/unit.");

        IList<XYZ> points = BoundaryGeometryBuilder.BuildPoints(acquisition.Outcome.Payload.Samples, revitUnit);
        double constantZInternal = points.Min(point => point.Z);
        IList<CurveLoop> profiles = BoundaryGeometryBuilder.BuildProfiles(boundary, constantZInternal, revitUnit);
        BoundingBoxXYZ expected = BoundaryGeometryBuilder.ComputeExpectedBoundingBox(points);

        if (!PostCreationVerification.AllProfilesArePlanar(profiles, out string? planarityProblem))
        {
            ShowSingleCancelledProblem("SolidGround could not build a valid boundary.", planarityProblem!);
            return Result.Cancelled;
        }

        double toleranceInternal = RevitUnitConversion.ToInternal(LocalBoundaryValidator.DefaultContainmentToleranceMeters, CoreLengthUnit.Meter);

        // ==== Stage 5: Transaction (the only stage that mutates Document) ==================================
        return RunTransaction(
            context, acquisition.Outcome, profiles, points, expected, revitUnit, constantZInternal, toleranceInternal,
            exportDocumentFileName, exportPointsFileName);
    }

    // -------------------------------------------------------------------------------------------------------
    // Stage 1
    // -------------------------------------------------------------------------------------------------------

    private sealed record DocumentContext(
        Document Document,
        RevitSettings Settings,
        string SettingsPath,
        HorizontalReference Wgs84Reference,
        AreaOfInterest Aoi,
        Level Level,
        ToposolidType ToposolidType,
        OrphanSnapshot OrphanBefore,
        int? NativeToposolidMaxPointThreshold);

    /// <summary>
    /// Read-only by construction: reads document state, the settings file, and Core-level defaults only,
    /// opens no <see cref="Transaction"/>, and never reads the OpenTopography API key's value into any
    /// string -- only whether <see cref="IOpenTopographyApiKeyProvider.GetApiKey"/> returned a non-null key
    /// at all. Every problem found is accumulated into <paramref name="problems"/> rather than stopping at
    /// the first (design record §6.1); returns <see langword="null"/> only when a structural problem (no
    /// document, or the settings file itself could not be created/decoded) makes the remaining checks
    /// impossible to run.
    /// </summary>
    private static DocumentContext? RunDocumentPreflight(ExternalCommandData commandData, out List<string> problems)
    {
        problems = [];

        Document? document = commandData.Application.ActiveUIDocument?.Document;
        if (document is null)
        {
            problems.Add("No active Revit project is open. Open a project document and run this command again.");
        }
        else if (document.IsFamilyDocument)
        {
            problems.Add("The active document is a family document. Open a project document and run this command again.");
            document = null;
        }

        string settingsPath = RevitSettingsLocator.Resolve();
        RevitSettingsIo.EnsureTemplateExists(settingsPath, out bool justCreated, out string? writeError);
        if (justCreated)
        {
            problems.Add($"A starting template was written to '{settingsPath}'. Edit it and run this command again.");
            return null;
        }

        if (writeError is not null)
        {
            problems.Add(writeError);
            return null;
        }

        if (!RevitSettingsIo.TryLoad(settingsPath, out RevitSettings? settings, out string? loadError))
        {
            problems.AddRange((loadError ?? "The settings file could not be loaded.").Split(Environment.NewLine));
            return null;
        }

        if (document is null)
        {
            // The document problem above already explains why nothing further can run.
            return null;
        }

        if (settings.Request.Mode == TerrainAcquisitionMode.Fetch && new EnvironmentOpenTopographyApiKeyProvider().GetApiKey() is null)
        {
            problems.Add("The OPENTOPOGRAPHY_API_KEY environment variable is not set (or is empty). Set it to a valid OpenTopography API key and restart Revit.");
        }

        HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;

        string? parcelGeometryText = null;
        if (settings.Request.AreaOfInterest.Kind == AreaOfInterestKind.Parcel)
        {
            string parcelPath = settings.Request.AreaOfInterest.Parcel!.Path;
            try
            {
                parcelGeometryText = File.ReadAllText(parcelPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"Could not read '{parcelPath}': {ex.Message}");
            }
        }

        AreaOfInterest? aoi = null;
        if (settings.Request.AreaOfInterest.Kind != AreaOfInterestKind.Parcel || parcelGeometryText is not null)
        {
            try
            {
                aoi = AoiSettingsFactory.Build(settings.Request.AreaOfInterest, wgs84Reference, parcelGeometryText);
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException)
            {
                problems.Add($"The configured area of interest is invalid: {ex.Message}");
            }
        }

        // Existence-only (never content) checks for the three process-mode file paths: design record §4.1
        // documents each as "existence checked at Preflight, not settings-validate, since it is a
        // file-system concern" (ProcessInputSettings's own doc comment says the same). Mirrors the parcel
        // path handling above so a process-mode file-path problem reaches this same accumulated Preflight
        // list instead of surfacing later, differently worded, from Stage 2's acquisition failure path.
        // Mode == Process guarantees Process is non-null and Asc is non-blank: TryLoad above already ran
        // settings.Request.Validate(), which rejects a null Process or blank Process.Asc before this point.
        if (settings.Request.Mode == TerrainAcquisitionMode.Process)
        {
            ProcessInputSettings process = settings.Request.Process!;
            string ascPath = process.Asc;
            string prjPath = process.Prj ?? Path.ChangeExtension(ascPath, ".prj");

            if (!File.Exists(ascPath))
            {
                problems.Add($"process.asc does not exist: '{ascPath}'.");
            }

            if (!File.Exists(prjPath))
            {
                problems.Add($"process.prj does not exist: '{prjPath}'.");
            }

            if (process.SourceJson is { } sourceJsonPath && !File.Exists(sourceJsonPath))
            {
                problems.Add($"process.sourceJson does not exist: '{sourceJsonPath}'.");
            }
        }

        Level? level = LevelAndTypeResolver.ResolveLevel(document, settings.Target.LevelName);
        if (level is null)
        {
            problems.Add("This project has no Level. SolidGround needs at least one Level to assign the created toposolid to.");
        }
        else if (!string.IsNullOrWhiteSpace(settings.Target.LevelName) && !string.Equals(level.Name, settings.Target.LevelName, StringComparison.Ordinal))
        {
            problems.Add($"No Level named '{settings.Target.LevelName}' was found in this project.");
        }

        ToposolidType? toposolidType = LevelAndTypeResolver.ResolveToposolidType(document, settings.Target.ToposolidTypeName);
        if (toposolidType is null)
        {
            problems.Add("This project has no ToposolidType. SolidGround needs at least one ToposolidType to create the toposolid with.");
        }
        else if (!string.IsNullOrWhiteSpace(settings.Target.ToposolidTypeName) && !string.Equals(toposolidType.Name, settings.Target.ToposolidTypeName, StringComparison.Ordinal))
        {
            problems.Add($"No ToposolidType named '{settings.Target.ToposolidTypeName}' was found in this project.");
        }

        int? nativeToposolidMaxPointThreshold = CheckRevitIniPointThreshold(
            commandData, settings.Request.Simplification.PointBudget, problems);

        if (problems.Count > 0 || aoi is null || level is null || toposolidType is null)
        {
            return null;
        }

        OrphanSnapshot orphanBefore = OrphanCheck.Capture(document);
        return new DocumentContext(document, settings, settingsPath, wgs84Reference, aoi, level, toposolidType, orphanBefore, nativeToposolidMaxPointThreshold);
    }

    /// <summary>
    /// SolidGround Issue #15's 2026-09-21 probe session found that Revit's combined <c>Toposolid.Create</c>
    /// overload never throws when handed more points than <c>Revit.ini</c>'s <c>NativeToposolidMaxPointThreshold</c>
    /// allows -- it silently retains only about that many <see cref="SlabShapeEditor"/> vertices (evidence:
    /// <c>evidence/EVIDENCE-PROBES.md</c> Run 2, <c>ThresholdProbe</c>/its extended sweep;
    /// <c>docs/architecture/revit-toposolid-creation.md</c>'s "Step 7"). This guards the configured point
    /// budget here, before acquisition and before any transaction, instead of only discovering the loss
    /// afterward from <see cref="PostCreationVerification"/>.
    /// </summary>
    /// <returns>
    /// The parsed <c>NativeToposolidMaxPointThreshold</c>, or <see langword="null"/> when it could not be
    /// read this session (missing/unreadable <c>Revit.ini</c>, or the key was absent/malformed) --
    /// <see cref="PostCreationVerification.Verify"/> names this value in its own message when known.
    /// </returns>
    private static int? CheckRevitIniPointThreshold(ExternalCommandData commandData, int pointBudget, List<string> problems)
    {
        string revitIniPath;
        string revitIniText;
        try
        {
            // Autodesk.Revit.ApplicationServices.Application.CurrentUsersDataFolderPath: an instance,
            // get-only `string` property, verified present in the installed Revit 2027 (27.0.10.13) dump
            // (apidump/out/Autodesk.Revit.ApplicationServices.Application.txt) and, separately, in
            // Autodesk's own 27.2.0.0-labelled Revit-API-MainReference page for this exact member
            // ("Similar to C:\Users\[UserName]\AppData\Roaming\Autodesk\[ProductType]\[ReleaseName]") --
            // matching Autodesk's "About the Revit.ini File for Installation" page's "User Profile folder"
            // location (used once Revit has been started and exited at least once) and this machine's own
            // observed path (SolidGround Issue #15 `evidence/EVIDENCE-PROBES.md` Step 1). Reached from
            // command-time code the same way AGENTS.md's "Revit 2027 rules" already reach
            // `Application.AllUsersAddinsLocation`: `commandData.Application.Application.<member>`.
            string dataFolderPath = commandData.Application.Application.CurrentUsersDataFolderPath;
            revitIniPath = Path.Combine(dataFolderPath, "Revit.ini");
            revitIniText = File.ReadAllText(revitIniPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Tolerant by design (design record for this check): a missing file, an inaccessible data
            // folder, or any other read error just logs and skips this one check -- it must never itself
            // block a run the way a real over-budget finding does.
            AddInLog.Warning($"Could not read Revit.ini to check its point-count threshold; skipping this check: {ex.GetType().Name}: {ex.Message}");
            return null;
        }

        RevitIniToposolidThresholds.Thresholds thresholds = RevitIniToposolidThresholds.Parse(revitIniText);
        AddInLog.Info(
            $"'{revitIniPath}' [Misc]: NativeToposolidMaxPointThreshold={DescribeThreshold(thresholds.NativeToposolidMaxPointThreshold)}, " +
            $"LinkToposolidMaxPointThreshold={DescribeThreshold(thresholds.LinkToposolidMaxPointThreshold)}.");

        if (thresholds.NativeToposolidMaxPointThreshold is { } nativeThreshold && pointBudget > nativeThreshold)
        {
            string budgetText = pointBudget.ToString(CultureInfo.InvariantCulture);
            string thresholdText = nativeThreshold.ToString(CultureInfo.InvariantCulture);
            problems.Add(
                $"pointBudget {budgetText} exceeds this machine's NativeToposolidMaxPointThreshold of {thresholdText} in " +
                $"'{revitIniPath}'; lower pointBudget to at most {thresholdText} or raise the Revit.ini value within " +
                "Autodesk's documented 10,000 to 50,000 range and restart Revit.");
        }

        return thresholds.NativeToposolidMaxPointThreshold;
    }

    private static string DescribeThreshold(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "(absent)";

    // -------------------------------------------------------------------------------------------------------
    // Stage 2
    // -------------------------------------------------------------------------------------------------------

    private static async Task<(ElevationGrid Grid, TerrainProcessingOutcome Outcome)> RunPipelineAsync(
        TerrainRequestSettings request, HorizontalReference wgs84Reference, AreaOfInterest aoi, CancellationToken cancellationToken)
    {
        return request.Mode == TerrainAcquisitionMode.Fetch
            ? await RunFetchPipelineAsync(request, wgs84Reference, aoi, cancellationToken).ConfigureAwait(false)
            : await RunProcessPipelineAsync(request, aoi, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(ElevationGrid Grid, TerrainProcessingOutcome Outcome)> RunFetchPipelineAsync(
        TerrainRequestSettings request, HorizontalReference wgs84Reference, AreaOfInterest aoi, CancellationToken cancellationToken)
    {
        (Wgs84BoundingBoxAoi fetchEnvelope, _) = ClipRegionFactory.BuildFetchEnvelope(aoi);

        using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(request.NetworkTimeoutSeconds) };
        OpenTopographyUsgs1mSource source = new(httpClient, new EnvironmentOpenTopographyApiKeyProvider());

        OpenTopographyUsgs1mAcquisition acquisition;
        try
        {
            acquisition = await source.AcquireDetailedAsync(
                new ElevationSourceRequest(fetchEnvelope), cancellationToken).ConfigureAwait(false);
        }
        catch (OpenTopographyException ex)
        {
            // This failure-path log line is modeled on
            // SolidGround.Cli.Commands.FetchCommand.PrintAcquisitionEvidence's "request '<uri>'." line -- but
            // the CLI only prints that line on a successful acquisition, in --verbose mode (it runs only after
            // AcquireDetailedAsync returns; CliApplication.RunAsync's own top-level catch clauses for
            // OpenTopographyAuthorizationException/OpenTopographyException print only ex.Message, never a
            // request URI, on failure). So before this line existed, neither the add-in's log nor the CLI's
            // --verbose output recorded which request an acquisition failure belonged to (SolidGround Issue
            // #15's 2026-09-21 end-to-end evidence, Scenario E, needed the add-in's log plus a separate CLI
            // cross-check to diagnose that session's HTTP 401). Every OpenTopographyException already carries
            // its own RedactedRequestUri, redacted through OpenTopographyRedaction before the exception was
            // constructed (see OpenTopographyException's own doc comment), so logging it here is always safe
            // -- never the unredacted query string.
            AddInLog.Info($"Fetch mode acquisition request (failed): '{ex.RedactedRequestUri}'.");
            throw;
        }

        // Same idea, on the success path: SolidGround.Cli.Commands.FetchCommand.PrintAcquisitionEvidence reads
        // this identical acquisition.Evidence.RedactedRequestUri value, in --verbose mode.
        AddInLog.Info($"Fetch mode acquisition request (succeeded): '{acquisition.Evidence.RedactedRequestUri}'.");

        ElevationGrid grid = (ElevationGrid)acquisition.Acquisition.Data;
        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, acquisition.Evidence.WellKnownText);

        if (transform.Definition.TargetReference != grid.HorizontalReference)
        {
            throw new InvalidOperationException(
                "The fetched grid's horizontal reference does not match the coordinate reference the WGS 84 transform was built from.");
        }

        ElevationSourceMetadata sourceMetadata = new(
            acquisition.Acquisition.Source.SourceName,
            acquisition.Acquisition.Source.DatasetIdentifier,
            acquisition.Acquisition.Source.CollectionPeriod,
            acquisition.Acquisition.Source.QualityLevel);
        ReferenceOrigins referenceOrigins = new(acquisition.Evidence.HorizontalReferenceOrigin, acquisition.Evidence.VerticalReferenceOrigin);

        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid, transform, grid.VerticalReference, referenceOrigins, sourceMetadata, aoi,
                request.LocalOrigin, request.OutputUnit, request.Simplification.Method, request.Simplification.PointBudget,
                request.Simplification.CoverageFloorFraction, cancellationToken)
            .ConfigureAwait(false);

        return (grid, outcome);
    }

    private static async Task<(ElevationGrid Grid, TerrainProcessingOutcome Outcome)> RunProcessPipelineAsync(
        TerrainRequestSettings request, AreaOfInterest aoi, CancellationToken cancellationToken)
    {
        ProcessInputSettings process = request.Process!;
        string ascPath = process.Asc;
        string prjPath = process.Prj ?? Path.ChangeExtension(ascPath, ".prj");
        string? explicitSourceJsonPath = process.SourceJson;
        string defaultSourceJsonPath = Path.ChangeExtension(ascPath, DefaultSourceJsonExtension);

        string ascText = ReadTextFile(ascPath);
        string prjText = ReadTextFile(prjPath);

        RasterSourceSidecar? sidecar = null;
        if (explicitSourceJsonPath is not null)
        {
            sidecar = RasterSourceSidecarIo.Read(ReadBytesFile(explicitSourceJsonPath), explicitSourceJsonPath);
        }
        else if (File.Exists(defaultSourceJsonPath))
        {
            sidecar = RasterSourceSidecarIo.Read(ReadBytesFile(defaultSourceJsonPath), defaultSourceJsonPath);
        }

        WellKnownTextReference parsedPrj = WellKnownTextReferenceParser.Parse(prjText);
        IHorizontalCoordinateTransform transform = ProjNetHorizontalCoordinateTransformFactory.Create(
            ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText, prjText);

        VerticalReferenceResolution.ResolvedVerticalReference resolvedVertical;
        try
        {
            resolvedVertical = VerticalReferenceResolution.Resolve(process.VerticalDatum, process.VerticalUnit, process.Geoid, sidecar, parsedPrj.Vertical);
        }
        catch (FormatException)
        {
            // Translated at this one call site (design record §6.2/§0.4 item 8): never Resolve's own
            // CLI-flavored --vertical-datum/--vertical-unit/--source-json message text (error catalogue row 12).
            throw new FormatException(
                "SolidGround could not determine this terrain's vertical reference. Set 'process.verticalDatum' " +
                "and 'process.verticalUnit', provide a 'process.sourceJson' sidecar, or use a compound .prj with a VERT_CS.");
        }

        VerticalReference verticalReference = resolvedVertical.Reference;

        ElevationGrid grid;
        using (StringReader ascReader = new(ascText))
        {
            grid = AaiGridParser.Parse(ascReader, transform.Definition.TargetReference, verticalReference);
        }

        string sourceName = process.SourceName ?? sidecar?.SourceName ?? "local-file";
        string datasetIdentifier = process.Dataset ?? sidecar?.DatasetIdentifier ?? Path.GetFileNameWithoutExtension(ascPath);
        CollectionPeriod? collectionPeriod = ParseCollectionPeriod(process) ?? sidecar?.CollectionPeriod;
        string? qualityLevel = process.QualityLevel ?? sidecar?.QualityLevel;
        ElevationSourceMetadata sourceMetadata = new(sourceName, datasetIdentifier, collectionPeriod, qualityLevel);

        ReferenceOrigins referenceOrigins = new(ReferenceOrigin.Operator, resolvedVertical.Origin);

        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid, transform, verticalReference, referenceOrigins, sourceMetadata, aoi,
                request.LocalOrigin, request.OutputUnit, request.Simplification.Method, request.Simplification.PointBudget,
                request.Simplification.CoverageFloorFraction, cancellationToken)
            .ConfigureAwait(false);

        return (grid, outcome);
    }

    private static CollectionPeriod? ParseCollectionPeriod(ProcessInputSettings process)
    {
        if (process.CollectionStart is not { } startText || process.CollectionEnd is not { } endText)
        {
            return null;
        }

        if (!DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly start)
            || !DateOnly.TryParseExact(endText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly end))
        {
            // TerrainRequestSettings.Validate() already rejected this before Stage 1 accepted the run.
            return null;
        }

        return new CollectionPeriod(start, end);
    }

    private static string ReadTextFile(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not read '{path}'.", ex);
        }
    }

    private static byte[] ReadBytesFile(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException($"Could not read '{path}'.", ex);
        }
    }

    private static bool IsAcquisitionFailure(Exception ex) =>
        ex is OpenTopographyException or FormatException or IOException or OperationCanceledException or InvalidOperationException;

    private static string AcquisitionFailureHeadline(Exception ex, int networkTimeoutSeconds) => ex switch
    {
        OperationCanceledException =>
            $"The request did not complete within {networkTimeoutSeconds.ToString(CultureInfo.InvariantCulture)} seconds.",
        IOException => "Could not read a configured file.",
        _ => "SolidGround could not acquire terrain data.",
    };

    private static string AcquisitionFailureDetail(Exception ex) => ex switch
    {
        OperationCanceledException => "The acquisition timed out. Increase 'networkTimeoutSeconds' in settings.json, or check network connectivity, and try again.",
        _ => ex.Message,
    };

    // -------------------------------------------------------------------------------------------------------
    // Stage 5 / 6
    // -------------------------------------------------------------------------------------------------------

    private static Result RunTransaction(
        DocumentContext context,
        TerrainProcessingOutcome outcome,
        IList<CurveLoop> profiles,
        IList<XYZ> points,
        BoundingBoxXYZ expected,
        ForgeTypeId revitUnit,
        double constantZInternal,
        double toleranceInternal,
        string exportDocumentFileName,
        string exportPointsFileName)
    {
        Document document = context.Document;
        ToposolidCreationFailureLog failureLog = new();

        using Transaction transaction = new(document, "SolidGround: Create Toposolid");
        transaction.SetFailureHandlingOptions(
            transaction.GetFailureHandlingOptions()
                .SetFailuresPreprocessor(new ToposolidCreationFailurePreprocessor(failureLog))
                .SetClearAfterRollback(true));

        TransactionStatus started = transaction.Start();
        if (started != TransactionStatus.Started)
        {
            ShowSingleCancelledProblem("SolidGround could not start a Revit transaction.", $"Transaction.Start() returned {started}.");
            return Result.Cancelled;
        }

        // toposolid/draft are assigned only on the path that reaches a confirmed Committed status; that
        // path falls through to ReportSuccess below, deliberately OUTSIDE this try/catch (see the comment
        // there): once Commit() has returned Committed, nothing that happens while reporting success may
        // flip Result away from Succeeded (error catalogue row 22's principle, generalized).
        Toposolid? toposolid = null;
        PlacementRecordDraft? draft = null;

        try
        {
            toposolid = ToposolidCreationService.Create(
                document, profiles, points, context.ToposolidType.Id, context.Level.Id, ToposolidCreationService.DefaultStrategy);

            document.Regenerate();

            VerificationResult verification = PostCreationVerification.Verify(
                toposolid, expected, points, ToposolidCreationService.DefaultStrategy, toleranceInternal,
                context.NativeToposolidMaxPointThreshold);

            if (!verification.Passed || failureLog.HasBlockingFailure)
            {
                // Error catalogue rows 19 and 20: a failed geometry verification and a blocking Revit
                // failure message get distinct headlines; verification takes priority when both occur.
                (string headline, string detail) = !verification.Passed
                    ? ("The created toposolid's geometry did not match the source data; the change was undone.", verification.Detail)
                    : ("Revit reported a problem while creating the toposolid.", string.Join(" | ", failureLog.Messages));
                TransactionStatus rolledBack = transaction.RollBack();
                return ShowTransactionOutcome(rolledBack, headline, detail);
            }

            draft = BuildPlacementDraft(
                context, outcome, revitUnit, constantZInternal, toposolid, exportDocumentFileName, exportPointsFileName);

            ToposolidCreatedHook? postCreationHook = null; // Issue #16 supplies a non-null value here.
            postCreationHook?.Invoke(document, toposolid, outcome.Payload, draft);

            TransactionStatus commitStatus = transaction.Commit();
            if (commitStatus != TransactionStatus.Committed)
            {
                AddInLog.Error($"SolidGround's transaction ended with status {commitStatus}, not Committed.");
                // Per Autodesk's own "Handling Failures" documentation, posted failures are processed "at the
                // end of a transaction (specifically when Transaction.Commit() or Transaction.Rollback() are
                // invoked)" -- so a blocking Revit failure can still be discovered only here, inside Commit()
                // itself, even though failureLog.HasBlockingFailure was already checked once above, right
                // after the explicit pre-Commit Regenerate() call. When that has happened, failureLog already
                // holds the specific, accumulated Revit failure text (SolidGround Issue #15 review fix); show
                // it alongside the generic headline instead of silently discarding it.
                if (failureLog.HasBlockingFailure)
                {
                    ShowProblemList(
                        "SolidGround could not confirm whether the toposolid was created. Check the document and Undo if needed.",
                        "Revit reported the following while finishing the transaction:",
                        failureLog.Messages);
                }
                else
                {
                    ShowSingleFailed("SolidGround could not confirm whether the toposolid was created. Check the document and Undo if needed.");
                }

                return Result.Failed;
            }

            // Falls through to ReportSuccess below: commitStatus == Committed is the only way this try
            // block completes without an explicit return or a caught exception.
        }
        catch (ToposolidCreationException ex)
        {
            TransactionStatus status = transaction.HasEnded() ? transaction.GetStatus() : transaction.RollBack();
            // ex.Message already restates "Revit rejected..."; the inner exception's own message is the
            // non-redundant detail for the dialog body.
            return ShowTransactionOutcome(status, "Revit rejected the generated toposolid boundary or points.", ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("SolidGround hit a problem after the transaction started.", ex);
            TransactionStatus status = transaction.HasEnded() ? transaction.GetStatus() : transaction.RollBack();
            return ShowTransactionOutcome(status, "SolidGround hit a problem while finishing the toposolid.", ex.Message);
        }

        // Reached only when commitStatus == Committed. Deliberately outside the try/catch above: a failure
        // while reporting success (writing the placement record, the orphan check, showing the dialog) must
        // never re-derive Result from TransactionStatus.RolledBack/HasEnded() -- the model change is already
        // durable. ReportSuccess is itself defensive (never lets a reporting failure escape) for the same reason.
        return ReportSuccess(context, draft!, toposolid!);
    }

    private static PlacementRecordDraft BuildPlacementDraft(
        DocumentContext context,
        TerrainProcessingOutcome outcome,
        ForgeTypeId revitUnit,
        double constantZInternal,
        Toposolid toposolid,
        string exportDocumentFileName,
        string exportPointsFileName)
    {
        TerrainProvenance provenance = outcome.Payload.Provenance;
        double roundTrip = UnitUtils.ConvertFromInternalUnits(UnitUtils.ConvertToInternalUnits(1.0, revitUnit), revitUnit) - 1.0;

        PlacementUnitConversionRecord unitConversion = new(
            LengthUnitToken(context.Settings.Request.OutputUnit),
            revitUnit.TypeId,
            LengthConverter.MetersPerUnit(context.Settings.Request.OutputUnit),
            roundTrip);

        PlacementLocalOriginRecord localOrigin = new(
            provenance.LocalFrame.Origin.X,
            provenance.LocalFrame.Origin.Y,
            provenance.LocalFrame.Origin.Elevation,
            provenance.HorizontalTransformation.TargetReference.CoordinateReferenceSystem,
            new PlacementVerticalReferenceRecord(
                provenance.SourceVerticalReference.Datum,
                LengthUnitToken(provenance.SourceVerticalReference.Unit),
                provenance.SourceVerticalReference.GeoidModel));

        PlacementBoundaryPlaneElevationRecord boundaryPlaneElevation = new(
            constantZInternal,
            "minimumRetainedSampleElevation",
            context.Level.Elevation,
            "Every boundary CurveLoop vertex shares this one internal-unit Z, the minimum of the retained terrain " +
            "samples' own local elevation (not the resolved Level's Elevation, recorded here only for reference); " +
            "terrain shape comes entirely from the points array.");

        OrphanSnapshot midTransaction = OrphanCheck.Capture(context.Document);
        PlacementRevitCoordinatesRecord revitCoordinates = new(
            InternalOrigin.Get(context.Document).Position.IsAlmostEqualTo(new XYZ(0d, 0d, 0d)),
            ToPointRecord(midTransaction.BasePointPosition),
            ToPointRecord(midTransaction.BasePointSharedPosition),
            ToPointRecord(midTransaction.SurveyPointPosition),
            ToPointRecord(midTransaction.SurveyPointSharedPosition),
            midTransaction.ActiveProjectLocationName);

        PlacementPointCountsRecord pointCounts = new(
            provenance.OriginalPointCount, provenance.RetainedPointCount, context.Settings.Request.Simplification.PointBudget);

        return new PlacementRecordDraft(
            exportDocumentFileName,
            exportPointsFileName,
            context.Level.Name,
            context.Level.Id.Value,
            context.ToposolidType.Name,
            context.ToposolidType.Id.Value,
            CreationStrategyToken(ToposolidCreationService.DefaultStrategy),
            unitConversion,
            localOrigin,
            boundaryPlaneElevation,
            revitCoordinates,
            "SolidGround made no change to ActiveProjectLocation, the project base point, the survey point, or site location during this run.",
            pointCounts);
    }

    private static PlacementPointRecord ToPointRecord(XYZ point) => new(point.X, point.Y, point.Z);

    /// <summary>
    /// The exact camelCase token <see cref="TerrainRequestSettings.JsonOptions"/>'s <c>JsonStringEnumConverter</c>
    /// would produce for <paramref name="unit"/> (for example <c>"usSurveyFoot"</c>) -- reused here so the
    /// placement record's own unit fields match settings.json's convention exactly, never
    /// <c>LengthUnitTokens</c>'s deliberately different kebab-case CLI flag tokens (design record §0.2's
    /// "two casing conventions" note).
    /// </summary>
    private static string LengthUnitToken(CoreLengthUnit unit) =>
        JsonSerializer.Serialize(unit, TerrainRequestSettings.JsonOptions).Trim('"');

    /// <summary>The camelCase token for <paramref name="strategy"/> (design record §9's example: <c>"combinedOverload"</c>), matching this record's other camelCase-token fields.</summary>
    private static string CreationStrategyToken(ToposolidCreationStrategy strategy) => strategy switch
    {
        ToposolidCreationStrategy.CombinedOverload => "combinedOverload",
        ToposolidCreationStrategy.ProfilesThenSlabShapeEditor => "profilesThenSlabShapeEditor",
        _ => strategy.ToString(),
    };

    /// <summary>
    /// Called only after a confirmed <see cref="TransactionStatus.Committed"/> status. Deliberately never
    /// lets an exception escape (beyond the two catastrophic exclusions every catch filter in this class
    /// uses): the modeling action is already durable, so a failure while writing the placement record,
    /// running the orphan check, or showing the dialog must be logged, never allowed to make the caller
    /// derive a Cancelled/Failed result for a run that actually succeeded (error catalogue row 22's
    /// principle, generalized to every post-commit step, not only the placement-record write).
    /// </summary>
    private static Result ReportSuccess(DocumentContext context, PlacementRecordDraft draft, Toposolid toposolid)
    {
        long elementId = toposolid.Id.Value;

        string? placementPath = null;
        try
        {
            PlacementRecord record = draft.ToRecord(elementId, DateTime.UtcNow);
            placementPath = PlacementRecordWriter.Write(context.Settings.Request.Output.Directory, context.Settings.Request.Output.BaseName, record);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("SolidGround created the toposolid, but could not write the placement record.", ex);
        }

        try
        {
            OrphanSnapshot orphanAfter = OrphanCheck.Capture(context.Document);
            if (OrphanCheck.Unchanged(context.OrphanBefore, orphanAfter, out string? orphanProblem))
            {
                AddInLog.Info("Orphan check: shared coordinate state unchanged.");
            }
            else
            {
                AddInLog.Warning($"Orphan check: {orphanProblem}");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("SolidGround created the toposolid, but the post-commit orphan check itself failed.", ex);
        }

        AddInLog.Info(
            $"{BuildIdentity.Current.ToLogLine()} -- created Toposolid {elementId.ToString(CultureInfo.InvariantCulture)} " +
            $"on Level '{context.Level.Name}' with ToposolidType '{context.ToposolidType.Name}'; retained " +
            $"{draft.PointCounts.Retained.ToString(CultureInfo.InvariantCulture)} of {draft.PointCounts.Original.ToString(CultureInfo.InvariantCulture)} point(s).");

        try
        {
            string body = string.Join(
                Environment.NewLine,
                $"Element id: {elementId.ToString(CultureInfo.InvariantCulture)}",
                $"Level: {context.Level.Name}",
                $"ToposolidType: {context.ToposolidType.Name}",
                $"Points retained: {draft.PointCounts.Retained.ToString(CultureInfo.InvariantCulture)} of {draft.PointCounts.Original.ToString(CultureInfo.InvariantCulture)} (budget {draft.PointCounts.Budget.ToString(CultureInfo.InvariantCulture)})",
                $"Export bundle: {Path.Combine(context.Settings.Request.Output.Directory, draft.ExportDocument)}",
                placementPath is not null ? $"Placement record: {placementPath}" : "Placement record: could not be written (see log).",
                $"Log directory: {AddInLog.LogDirectory ?? "(unavailable)"}",
                string.Empty,
                "SolidGround is a site-form tool, not a survey instrument. SolidGround made no change to " +
                "ActiveProjectLocation, the project base point, the survey point, or site location during this run.");

            AddInLog.Info("Showing success dialog.");
            TaskDialog dialog = new(DialogTitle)
            {
                MainInstruction = "SolidGround created the toposolid.",
                MainContent = body,
            };
            dialog.Show();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("SolidGround created the toposolid, but could not show the success dialog.", ex);
        }

        return Result.Succeeded;
    }

    // -------------------------------------------------------------------------------------------------------
    // Dialogs
    // -------------------------------------------------------------------------------------------------------

    private static void ShowProblemList(string mainInstruction, string bodyHeadline, IReadOnlyList<string> problems)
    {
        string body = ProblemReportDialog.BuildRejectionBody(bodyHeadline, problems, AddInLog.LogDirectory);
        AddInLog.Info($"Showing rejection dialog: {mainInstruction}");
        TaskDialog dialog = new(DialogTitle) { MainInstruction = mainInstruction, MainContent = body };
        dialog.Show();
    }

    private static void ShowSingleCancelledProblem(string mainInstruction, string detail) =>
        ShowProblemList(mainInstruction, "Nothing changed. Correct the problem below and run this command again.", [detail]);

    private static void ShowSingleFailed(string mainInstruction)
    {
        AddInLog.Info($"Showing failure dialog: {mainInstruction}");
        TaskDialog dialog = new(DialogTitle) { MainInstruction = mainInstruction };
        dialog.Show();
    }

    /// <summary>
    /// Shared by every post-<c>Start()</c> failure path: derives <see cref="Result"/> only from the observed
    /// <see cref="TransactionStatus"/> (design record §7.1), never from "an exception happened".
    /// </summary>
    private static Result ShowTransactionOutcome(TransactionStatus status, string rolledBackHeadline, string detail)
    {
        if (status == TransactionStatus.RolledBack)
        {
            AddInLog.Info($"Showing rollback dialog: {rolledBackHeadline}");
            TaskDialog dialog = new(DialogTitle) { MainInstruction = rolledBackHeadline, MainContent = detail };
            dialog.Show();
            return Result.Cancelled;
        }

        AddInLog.Error($"Transaction ended with status {status} instead of RolledBack or Committed; reporting Failed.");
        ShowSingleFailed($"SolidGround could not confirm whether the model was reverted (transaction status: {status}). Check the document and Undo if needed. {detail}");
        return Result.Failed;
    }
}
