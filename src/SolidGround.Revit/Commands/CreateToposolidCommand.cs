using System.Globalization;
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
using SolidGround.Revit.Dialog;
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
        // ==== Stage 0: load settings + open-document check (hoisted; SolidGround Issue #31, PH3-4, Stage D) ===
        // Hoisted out of RunDocumentPreflight so the interactive dialog (Stage 0.5) can use the loaded settings
        // and the read-once geometry tolerances before Preflight formally runs. See
        // docs/architecture/revit-toposolid-creation.md's "Command flow" and
        // docs/architecture/revit-interactive-dialog.md's "Result-code mapping".
        LoadResult loaded = LoadDocumentAndSettings(commandData);
        if (loaded.Document is null || loaded.Settings is null || loaded.Problems.Count > 0)
        {
            ShowProblemList("SolidGround Preflight found a problem.", "Nothing changed. Correct every problem below and run this command again.", loaded.Problems);
            return Result.Cancelled;
        }

        // ==== Stage 0.5: interactive dialog (SolidGround Issue #31, PH3-4) =====================================
        SolidGroundDialogResult? dialogResult = SolidGroundDialogHost.ShowModal(
            commandData, loaded.Document, loaded.Settings, loaded.VertexToleranceInternal);
        if (dialogResult is null)
        {
            // Operator cancelled (window X button, Esc, or the Cancel button) -- no TaskDialog, they already know.
            return Result.Cancelled;
        }

        // The operator's own dialog choices override the settings file's values for this run only -- never
        // written back (docs/architecture/revit-interactive-dialog.md's "Settings interaction: prefill, not
        // override"). RevitSettings/TerrainRequestSettings/SimplificationSettings/RevitSharedCoordinatesSettings
        // are all already sealed records with init-only properties, so these `with` expressions are ordinary,
        // already-idiomatic C#.
        RevitSettings effectiveSettings = loaded.Settings with
        {
            Request = loaded.Settings.Request with
            {
                OutputUnit = dialogResult.OutputUnit,
                Simplification = loaded.Settings.Request.Simplification with { PointBudget = dialogResult.PointBudget },
            },
            SharedCoordinates = new RevitSharedCoordinatesSettings(dialogResult.WriteSharedCoordinatesIfAbsent),
        };

        // Re-validate the dialog-merged settings (review fix, SolidGround Issue #31, PH3-4, Stage D): the
        // dialog's own Point Budget step only enforces PointBudget > 0
        // (docs/architecture/revit-interactive-dialog.md's "Settings interaction: prefill, not override"), so a
        // dialog-supplied override could otherwise bypass the 1-50000 bound TerrainRequestSettings.Validate()
        // already enforces for every settings-file-sourced value (RevitSettingsIo.TryLoad's own call, above, at
        // Stage 0). Reusing that exact rule here -- rather than duplicating a second literal bound into the
        // dialog/WPF layer -- keeps this a single source of truth and restores the invariant that every
        // TerrainRequestSettings reaching Stage 2 satisfies Validate(), regardless of which of the two paths
        // above (settings file only, or dialog-overridden) produced it.
        IReadOnlyList<string> effectiveSettingsProblems = effectiveSettings.Request.Validate();
        if (effectiveSettingsProblems.Count > 0)
        {
            ShowProblemList("SolidGround Preflight found a problem.", "Nothing changed. Correct every problem below and run this command again.", effectiveSettingsProblems);
            return Result.Cancelled;
        }

        // ==== Stage 1: Document Preflight (slimmed -- AOI/level/type now dialog-supplied on the FindParcel
        // ==== path; still settings-derived on the UseSettingsFile path) ==========================================
        DocumentContext? context = RunDocumentPreflight(
            commandData, loaded.Document, effectiveSettings, loaded.SettingsPath, dialogResult,
            loaded.ShortCurveToleranceInternal, loaded.VertexToleranceInternal, out List<string> preflightProblems);
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
                    () => RunPipelineAsync(context.Settings.Request, context.Wgs84Reference, context.Aoi, dialogResult.AddressParcel, cts.Token),
                    cts.Token)
                .GetAwaiter().GetResult();
        }
        catch (Exception ex) when (IsAcquisitionFailure(ex))
        {
            ShowSingleCancelledProblem(AcquisitionFailureHeadline(ex, context.Settings.Request.NetworkTimeoutSeconds), AcquisitionFailureDetail(ex));
            return Result.Cancelled;
        }

        // ==== Stage 3: Geometry Preflight (Core-only; still no Revit API call) =============================
        // ForgeTypeId revitUnit moves here, to the top of Stage 3 (SolidGround Issue #30, PH3-3: see
        // docs/architecture/revit-property-line-and-shared-coordinates.md's "Geometry cleanup contract" >
        // "Tolerance sourcing"): it depends only on context.Settings.Request.OutputUnit, not on any acquisition
        // result, and LocalBoundaryCleaner.Clean's own dedupe pass below needs the OutputUnit-converted
        // tolerances before Stage 4 is ever reached.
        ForgeTypeId revitUnit = RevitUnitConversion.ToForgeTypeId(context.Settings.Request.OutputUnit);
        AddInLog.Info(
            $"Output unit ForgeTypeId '{revitUnit.TypeId}' ({context.Settings.Request.OutputUnit}), " +
            $"{LengthConverter.MetersPerUnit(context.Settings.Request.OutputUnit).ToString("R", CultureInfo.InvariantCulture)} m/unit.");

        LocalCoordinateFrame localFrame = acquisition.Outcome.Payload.Provenance.LocalFrame;
        LocalBoundary rawBoundary = acquisition.Outcome.ClipResult is { } clipResult
            ? LocalBoundaryFactory.FromPolygonalRegion(clipResult.EffectiveRegion, localFrame)
            : LocalBoundaryFactory.FromGridEnvelope(acquisition.Grid, localFrame);

        // Geometry cleanup (SolidGround Issue #30, PH3-3): dedupe/collinear-collapse runs unconditionally, on
        // every run regardless of AOI kind or the shared-coordinates opt-in, after LocalBoundaryFactory and
        // before LocalBoundaryValidator.Validate. Both Revit-native tolerances were already read and logged at
        // Stage 1 Preflight; converting them into the pipeline's own OutputUnit is what this stage's own
        // revitUnit above exists for. See docs/architecture/revit-property-line-and-shared-coordinates.md's
        // "Geometry cleanup contract" section for the full contract, including why collinearityTolerance
        // deliberately reuses vertexTolerance's own value.
        double vertexTolerance = UnitUtils.ConvertFromInternalUnits(context.VertexToleranceInternal, revitUnit);
        const double ShortCurveToleranceMargin = 2.0; // conservative multiplier: unit-conversion/local-origin
                                                       // floating-point noise cannot reintroduce an edge only
                                                       // technically above Revit's own raw minimum.
        double minimumEdgeLength = UnitUtils.ConvertFromInternalUnits(context.ShortCurveToleranceInternal, revitUnit) * ShortCurveToleranceMargin;
        double collinearityTolerance = vertexTolerance;
        AddInLog.Info(
            $"Geometry cleanup tolerances ({context.Settings.Request.OutputUnit}): vertexTolerance={vertexTolerance.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"collinearityTolerance={collinearityTolerance.ToString("R", CultureInfo.InvariantCulture)}, minimumEdgeLength={minimumEdgeLength.ToString("R", CultureInfo.InvariantCulture)}.");
        LocalBoundary boundary = LocalBoundaryCleaner.Clean(rawBoundary, vertexTolerance, collinearityTolerance, minimumEdgeLength);

        // LocalBoundaryValidator.Validate's tolerance is compared directly against boundary/sample
        // coordinates, which are expressed in the pipeline's own OutputUnit (US survey foot by default), not
        // always meters -- DefaultContainmentToleranceMeters must be converted into that same unit before
        // being passed, exactly as the Stage 5 tolerance below already is (SolidGround Issue #15 review fix).
        double containmentTolerance = LengthConverter.Convert(
            LocalBoundaryValidator.DefaultContainmentToleranceMeters, CoreLengthUnit.Meter, context.Settings.Request.OutputUnit);
        LocalBoundaryValidationResult boundaryValidation = LocalBoundaryValidator.Validate(
            boundary, acquisition.Outcome.Payload.Samples, context.Settings.Request.Simplification.PointBudget, containmentTolerance, minimumEdgeLength);
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
        IList<XYZ> points = BoundaryGeometryBuilder.BuildPoints(acquisition.Outcome.Payload.Samples, revitUnit);
        double constantZInternal = points.Min(point => point.Z);
        IList<CurveLoop> profiles = BoundaryGeometryBuilder.BuildProfiles(boundary, constantZInternal, revitUnit);
        BoundingBoxXYZ expected = BoundaryGeometryBuilder.ComputeExpectedBoundingBox(points);

        if (!PostCreationVerification.AllProfilesArePlanar(profiles, out string? planarityProblem))
        {
            ShowSingleCancelledProblem("SolidGround could not build a valid boundary.", planarityProblem!);
            return Result.Cancelled;
        }

        // PropertyLine creation is gated to parcel areas of interest only (SolidGround Issue #30 owner decision
        // 3, 2026-09-26): a bounding-box/radius run never builds propertyLineProfiles at all, so it can never
        // itself reject that run's otherwise-valid Toposolid boundary. isParcelAoi is evaluated inline here and
        // again at its Stage 5 call sites (RunTransaction), deliberately not cached as a new DocumentContext
        // field -- see docs/architecture/revit-property-line-and-shared-coordinates.md's "AOI-kind gate: how the
        // command knows, and what a non-parcel run does" section. Recomputed against context.Aoi's own resolved
        // type (SolidGround Issue #31, PH3-4, Stage D), not context.Settings.Request.AreaOfInterest.Kind: once
        // the interactive dialog can supply an AOI directly (the FindParcel path), that settings-derived kind no
        // longer names every reachable run's real AOI type -- context.Aoi always does, on both dialog paths.
        bool isParcelAoi = context.Aoi is ParcelGeometryAoi;
        IList<CurveLoop>? propertyLineProfiles = null;
        if (isParcelAoi) // a second, independent CurveLoop list -- never `profiles`, already consumed below.
        {
            propertyLineProfiles = BoundaryGeometryBuilder.BuildProfiles(boundary, constantZInternal, revitUnit);
            if (!PostCreationVerification.BoundaryIsValidPropertyLine(propertyLineProfiles, out string? propertyLineProblem))
            {
                ShowSingleCancelledProblem("SolidGround could not build a valid boundary.", propertyLineProblem!);
                return Result.Cancelled;
            }
        }

        double toleranceInternal = RevitUnitConversion.ToInternal(LocalBoundaryValidator.DefaultContainmentToleranceMeters, CoreLengthUnit.Meter);

        // ==== Stage 5: Transaction (the only stage that mutates Document) ==================================
        return RunTransaction(
            context, acquisition.Outcome, profiles, points, expected, revitUnit, constantZInternal, toleranceInternal,
            exportDocumentFileName, exportPointsFileName, propertyLineProfiles);
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
        int? NativeToposolidMaxPointThreshold,
        double ShortCurveToleranceInternal,
        double VertexToleranceInternal);

    /// <summary>
    /// <see cref="LoadDocumentAndSettings"/>'s own return shape (SolidGround Issue #31, PH3-4, Stage D: Stage
    /// 0, hoisted out of <see cref="RunDocumentPreflight"/> so the interactive dialog can use
    /// <see cref="Settings"/>/<see cref="VertexToleranceInternal"/> before Preflight formally runs). Mirrors
    /// <see cref="DocumentContext"/>'s own "accumulate every problem, structural failures return early" shape.
    /// </summary>
    private sealed record LoadResult(
        Document? Document,
        RevitSettings? Settings,
        string SettingsPath,
        List<string> Problems,
        double ShortCurveToleranceInternal,
        double VertexToleranceInternal);

    /// <summary>
    /// Read-only by construction: the document-null/family-document check, <see cref="RevitSettingsLocator.Resolve"/>,
    /// <see cref="RevitSettingsIo.EnsureTemplateExists"/>/<see cref="RevitSettingsIo.TryLoad"/>, and the
    /// read-once geometry-tolerance read (<see cref="LogAndReadGeometryTolerances"/>) -- moved here, hoisted
    /// earlier than <see cref="RunDocumentPreflight"/>, so <c>ExecuteCore</c>'s Stage 0.5 interactive dialog can
    /// read the loaded settings and <see cref="LoadResult.VertexToleranceInternal"/> before Preflight formally
    /// runs (SolidGround Issue #31, PH3-4, Stage D; a review finding in the accepted design: an earlier draft
    /// put the dialog before Preflight without ever saying where the dialog's own already-coordinated check
    /// would get its length tolerance from). Also guards "this document has at least one Level/ToposolidType"
    /// here, before the dialog opens, rather than only discovering an empty chooser after the operator has
    /// already navigated to that step -- the interactive dialog itself always resolves a specific Level/
    /// ToposolidType from whatever candidates exist (see docs/architecture/revit-interactive-dialog.md's
    /// "Content model and sections" step 7), so it has no way to report "this project has none at all" on its
    /// own. Returns a structural, <see langword="null"/>-<see cref="LoadResult.Document"/>/<see cref="LoadResult.Settings"/>
    /// result only when a problem (no document, or the settings file itself could not be created/decoded) makes
    /// every later check impossible to run -- exactly <see cref="RunDocumentPreflight"/>'s own former contract
    /// for the same class of problem.
    /// </summary>
    private static LoadResult LoadDocumentAndSettings(ExternalCommandData commandData)
    {
        List<string> problems = [];

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
            return new LoadResult(null, null, settingsPath, problems, 0, 0);
        }

        if (writeError is not null)
        {
            problems.Add(writeError);
            return new LoadResult(null, null, settingsPath, problems, 0, 0);
        }

        if (!RevitSettingsIo.TryLoad(settingsPath, out RevitSettings? settings, out string? loadError))
        {
            problems.AddRange((loadError ?? "The settings file could not be loaded.").Split(Environment.NewLine));
            return new LoadResult(null, null, settingsPath, problems, 0, 0);
        }

        if (document is null)
        {
            // The document problem above already explains why nothing further can run.
            return new LoadResult(null, settings, settingsPath, problems, 0, 0);
        }

        (double shortCurveToleranceInternal, double vertexToleranceInternal) = LogAndReadGeometryTolerances(commandData);

        if (LevelAndTypeResolver.ListLevels(document).Count == 0)
        {
            problems.Add("This project has no Level. SolidGround needs at least one Level to assign the created toposolid to.");
        }

        if (LevelAndTypeResolver.ListToposolidTypes(document).Count == 0)
        {
            problems.Add("This project has no ToposolidType. SolidGround needs at least one ToposolidType to create the toposolid with.");
        }

        return new LoadResult(document, settings, settingsPath, problems, shortCurveToleranceInternal, vertexToleranceInternal);
    }

    /// <summary>
    /// Read-only by construction: reads document state and Core-level defaults only, opens no
    /// <see cref="Transaction"/>, and never reads the OpenTopography API key's value into any string -- only
    /// whether <see cref="IOpenTopographyApiKeyProvider.GetApiKey"/> returned a non-null key at all. Every
    /// problem found is accumulated into <paramref name="problems"/> rather than stopping at the first (design
    /// record §6.1). <paramref name="settings"/> is <c>ExecuteCore</c>'s own dialog-merged
    /// <c>effectiveSettings</c> (SolidGround Issue #31, PH3-4, Stage D) -- still named <c>settings</c> here,
    /// unchanged, so this method's own source text keeps reading identically to before that stage.
    /// </summary>
    /// <param name="dialogResult">
    /// The confirmed interactive-dialog result (Stage 0.5, always non-null by the time this runs). Supplies
    /// <see cref="DocumentContext.Level"/>/<see cref="DocumentContext.ToposolidType"/> unconditionally (the
    /// dialog resolves both on every run, regardless of <see cref="SolidGroundDialogResult.AoiSource"/>); supplies
    /// <see cref="DocumentContext.Aoi"/> directly when <see cref="SolidGroundDialogResult.AoiSource"/> is
    /// <see cref="DialogAoiSource.FindParcel"/>, or leaves this method to derive it from
    /// <paramref name="settings"/>'s own <c>areaOfInterest</c> section -- unchanged from before this issue --
    /// when it is <see cref="DialogAoiSource.UseSettingsFile"/> (owner decision 1's refinement: both AOI paths
    /// remain available; see docs/architecture/revit-interactive-dialog.md's "AOI and provenance"). No
    /// defensive re-verification that the dialog's chosen Level/ToposolidType still belong to <paramref name="document"/>
    /// is performed (owner decision 4): the same already-open <see cref="Document"/>, inside the same
    /// synchronous call, with no <see cref="Transaction"/> opened on any path that could invalidate an element
    /// reference.
    /// </param>
    private static DocumentContext? RunDocumentPreflight(
        ExternalCommandData commandData,
        Document document,
        RevitSettings settings,
        string settingsPath,
        SolidGroundDialogResult dialogResult,
        double shortCurveToleranceInternal,
        double vertexToleranceInternal,
        out List<string> problems)
    {
        problems = [];

        if (settings.Request.Mode == TerrainAcquisitionMode.Fetch && new EnvironmentOpenTopographyApiKeyProvider().GetApiKey() is null)
        {
            problems.Add("The OPENTOPOGRAPHY_API_KEY environment variable is not set (or is empty). Set it to a valid OpenTopography API key and restart Revit.");
        }

        HorizontalReference wgs84Reference = WellKnownTextReferenceParser.Parse(ProjNetHorizontalCoordinateTransformFactory.Wgs84WellKnownText).Horizontal;

        // AOI: dialog-supplied directly on the FindParcel path; derived from settings, exactly as before this
        // issue, on the UseSettingsFile path (owner decision 1's refinement -- see this method's own doc
        // comment above and docs/architecture/revit-interactive-dialog.md's "AOI and provenance").
        AreaOfInterest? aoi;
        if (dialogResult.AoiSource == DialogAoiSource.FindParcel)
        {
            aoi = dialogResult.Aoi;
        }
        else
        {
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

            aoi = null;
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

        // Level/ToposolidType: the interactive dialog already resolved both, on every run (SolidGround Issue
        // #31, PH3-4, Stage D) -- this maps its confirmed NamedElevationCandidate/NamedCandidate ids back to
        // the real elements (LoadDocumentAndSettings's own "at least one exists" guard already ran before the
        // dialog ever opened). Neither branch below is expected to be reachable in practice (see this method's
        // own doc comment on owner decision 4); each is still a fail-loud Preflight problem, never a crash, if
        // it somehow is.
        Level? level = LevelAndTypeResolver.FindLevelById(document, dialogResult.Level.Id);
        if (level is null)
        {
            problems.Add("SolidGround could not find the previously selected Level in this document.");
        }

        ToposolidType? toposolidType = LevelAndTypeResolver.FindToposolidTypeById(document, dialogResult.ToposolidType.Id);
        if (toposolidType is null)
        {
            problems.Add("SolidGround could not find the previously selected ToposolidType in this document.");
        }

        int? nativeToposolidMaxPointThreshold = CheckRevitIniPointThreshold(
            commandData, settings.Request.Simplification.PointBudget, problems);

        // Error catalogue row 9b (SolidGround Issue #30, PH3-3): a Preflight-only, pre-transaction,
        // document-state-dependent refusal, alongside row 9a's own identical precedent. See
        // docs/architecture/revit-property-line-and-shared-coordinates.md's "Preflight refusal" section.
        // 2026-09-27 live-evidence fix: LooksAlreadyCoordinated no longer reads the survey point's clipped
        // state at all (see that method's own doc comment and "Shared-coordinates detection" in the note
        // above); it reuses vertexToleranceInternal -- now a parameter, read once at Stage 0
        // (LoadDocumentAndSettings), not a local variable read here -- as its length tolerance instead of
        // reading Application.VertexTolerance a second time.
        if (settings.SharedCoordinates.WriteIfAbsent)
        {
            bool looksAlreadyCoordinated = SharedCoordinatesDetector.LooksAlreadyCoordinated(document, vertexToleranceInternal);
            if (looksAlreadyCoordinated)
            {
                problems.Add(
                    "sharedCoordinates.writeIfAbsent is enabled, but this model already appears to have shared " +
                    "coordinates set (a non-zero shared project position or angle, a moved survey point, or more than " +
                    "one project location). SolidGround will not overwrite existing shared coordinates. Set " +
                    "sharedCoordinates.writeIfAbsent to false to run without writing shared coordinates.");
            }
        }

        if (problems.Count > 0 || aoi is null || level is null || toposolidType is null)
        {
            return null;
        }

        OrphanSnapshot orphanBefore = OrphanCheck.Capture(document);
        return new DocumentContext(
            document, settings, settingsPath, wgs84Reference, aoi, level, toposolidType, orphanBefore,
            nativeToposolidMaxPointThreshold, shortCurveToleranceInternal, vertexToleranceInternal);
    }

    /// <summary>
    /// Reads and logs <c>Application.ShortCurveTolerance</c>/<c>.VertexTolerance</c> once, at Stage 0
    /// (<see cref="LoadDocumentAndSettings"/>; called from <c>RunDocumentPreflight</c> itself before SolidGround
    /// Issue #31, PH3-4, Stage D hoisted the call here so the interactive dialog's own already-coordinated
    /// check could use the same one read): Revit-internal decimal feet, unconverted -- <c>SolidGround.Core</c>
    /// never references either <see cref="Autodesk.Revit.ApplicationServices.Application"/> member directly.
    /// Reached from command-time code the same way <c>CheckRevitIniPointThreshold</c> above already reaches
    /// <c>CurrentUsersDataFolderPath</c>: <c>commandData.Application.Application.&lt;member&gt;</c>. See
    /// docs/architecture/revit-property-line-and-shared-coordinates.md's "Geometry cleanup contract" section,
    /// "Tolerance sourcing" subsection.
    /// </summary>
    private static (double ShortCurveToleranceInternal, double VertexToleranceInternal) LogAndReadGeometryTolerances(ExternalCommandData commandData)
    {
        double shortCurveToleranceInternal = commandData.Application.Application.ShortCurveTolerance;
        double vertexToleranceInternal = commandData.Application.Application.VertexTolerance;
        AddInLog.Info(
            $"Application.ShortCurveTolerance={shortCurveToleranceInternal.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"Application.VertexTolerance={vertexToleranceInternal.ToString("R", CultureInfo.InvariantCulture)} (Revit-internal decimal feet).");
        return (shortCurveToleranceInternal, vertexToleranceInternal);
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

        // SolidGround Issue #31, PH3-4, Stage D: this comparison and its problem-line text moved into Core
        // (RevitIniToposolidThresholds.ExceedsNativeThreshold/.DescribeExceedance, landed Stage A) so
        // SolidGroundDialog's own inline point-budget warning can share the identical rule and wording and
        // never drift apart from Preflight's own rejection -- a pure extraction, not a reword; the sentence
        // itself is unchanged.
        if (RevitIniToposolidThresholds.ExceedsNativeThreshold(pointBudget, thresholds))
        {
            problems.Add(RevitIniToposolidThresholds.DescribeExceedance(pointBudget, thresholds.NativeToposolidMaxPointThreshold!.Value, revitIniPath));
        }

        return thresholds.NativeToposolidMaxPointThreshold;
    }

    private static string DescribeThreshold(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "(absent)";

    // -------------------------------------------------------------------------------------------------------
    // Stage 2
    // -------------------------------------------------------------------------------------------------------

    private static async Task<(ElevationGrid Grid, TerrainProcessingOutcome Outcome)> RunPipelineAsync(
        TerrainRequestSettings request, HorizontalReference wgs84Reference, AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel, CancellationToken cancellationToken)
    {
        return request.Mode == TerrainAcquisitionMode.Fetch
            ? await RunFetchPipelineAsync(request, wgs84Reference, aoi, addressParcel, cancellationToken).ConfigureAwait(false)
            : await RunProcessPipelineAsync(request, aoi, addressParcel, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<(ElevationGrid Grid, TerrainProcessingOutcome Outcome)> RunFetchPipelineAsync(
        TerrainRequestSettings request, HorizontalReference wgs84Reference, AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel, CancellationToken cancellationToken)
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
            acquisition.Acquisition.Source.QualityLevel,
            acquisition.Acquisition.Source.Attribution);
        ReferenceOrigins referenceOrigins = new(acquisition.Evidence.HorizontalReferenceOrigin, acquisition.Evidence.VerticalReferenceOrigin);

        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid, transform, grid.VerticalReference, referenceOrigins, sourceMetadata, aoi,
                request.LocalOrigin, request.OutputUnit, request.Simplification.Method, request.Simplification.PointBudget,
                request.Simplification.CoverageFloorFraction, cancellationToken, addressParcel)
            .ConfigureAwait(false);

        return (grid, outcome);
    }

    private static async Task<(ElevationGrid Grid, TerrainProcessingOutcome Outcome)> RunProcessPipelineAsync(
        TerrainRequestSettings request, AreaOfInterest aoi, AddressParcelProvenance? addressParcel, CancellationToken cancellationToken)
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
        ElevationSourceMetadata sourceMetadata = new(sourceName, datasetIdentifier, collectionPeriod, qualityLevel, sidecar?.Attribution);

        ReferenceOrigins referenceOrigins = new(ReferenceOrigin.Operator, resolvedVertical.Origin);

        TerrainProcessingOutcome outcome = await TerrainProcessingPipeline.RunAsync(
                grid, transform, verticalReference, referenceOrigins, sourceMetadata, aoi,
                request.LocalOrigin, request.OutputUnit, request.Simplification.Method, request.Simplification.PointBudget,
                request.Simplification.CoverageFloorFraction, cancellationToken, addressParcel)
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
        string exportPointsFileName,
        IList<CurveLoop>? propertyLineProfiles)
    {
        Document document = context.Document;
        ToposolidCreationFailureLog failureLog = new();

        // Re-evaluated from context.Aoi (the same expression Stage 4 already uses, SolidGround Issue #31,
        // PH3-4, Stage D), rather than threaded as a new parameter -- see
        // docs/architecture/revit-property-line-and-shared-coordinates.md's "AOI-kind gate: how the command
        // knows, and what a non-parcel run does" section.
        bool isParcelAoi = context.Aoi is ParcelGeometryAoi;

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

            // PropertyLine creation is gated to parcel areas of interest only (owner decision 3, 2026-09-26):
            // propertyLineProfiles is non-null here exactly when isParcelAoi (Stage 4 only ever constructs it
            // under the identical condition) -- a second, independently-built CurveLoop list, never `profiles`
            // above, which ToposolidCreationService.Create already consumed. A non-parcel run attempts no
            // Revit API call here at all, so PropertyLineCreationException can structurally never be thrown for
            // it. See docs/architecture/revit-property-line-and-shared-coordinates.md's "PropertyLine creation
            // (Revit)" section.
            PropertyLine? propertyLine = isParcelAoi
                ? PropertyLineCreationService.Create(document, propertyLineProfiles!)
                : null;

            document.Regenerate();

            VerificationResult verification = PostCreationVerification.Verify(
                toposolid, expected, points, ToposolidCreationService.DefaultStrategy, toleranceInternal,
                context.NativeToposolidMaxPointThreshold);

            // Mirrors sharedCoordinatesVerification's own off-path sentinel below -- both are "this optional
            // element/write was never attempted this run" idioms, not independently invented cases.
            VerificationResult propertyLineVerification = propertyLine is not null
                ? PostCreationVerification.VerifyPropertyLine(propertyLine)
                : new VerificationResult(true, "No property line was created for this bounding-box/radius area of interest.");

            // Shared-coordinates write (SolidGround Issue #30, PH3-3): default off (context.Settings.SharedCoordinates
            // .WriteIfAbsent); Preflight's row-9b refusal already means reaching this line implies the document
            // looked uncoordinated at Preflight time. resolvedOrigin.HorizontalUnit/.VerticalUnit -- Origin's
            // own native unit -- convert this write, NEVER context.Settings.Request.OutputUnit/revitUnit (see
            // docs/architecture/revit-property-line-and-shared-coordinates.md's "Unit convention for the
            // shared-coordinates value" section: mixing the two would silently corrupt the anchor by the
            // US-survey-foot/meter ratio with no exception anywhere).
            ProjectPosition? sharedCoordinatesWritten = null;
            VerificationResult sharedCoordinatesVerification = new(true, "Shared coordinates were not written this run.");

            // Also requires the toposolid/property-line verification and failureLog to already be known-good
            // (review fix): otherwise a SharedCoordinatesWriteException thrown from this block would be caught
            // by the Stage-5 catch clause below, which picks its headline purely from the exception's runtime
            // type and never looks at verification/propertyLineVerification -- masking an already-known, higher-
            // priority failure behind the lower-priority shared-coordinates message, contrary to the fixed
            // toposolid/property-line/shared-coordinates/blocking-failure order this method's own ordered check
            // below (and docs/architecture/revit-property-line-and-shared-coordinates.md's "Transaction flow"
            // section) establishes. Skipping the write here leaves sharedCoordinatesVerification at its passing
            // sentinel above, so that ordered check still reports the true, first cause. See
            // docs/architecture/revit-property-line-and-shared-coordinates.md's "The write itself and its
            // source value" section.
            if (context.Settings.SharedCoordinates.WriteIfAbsent && verification.Passed && propertyLineVerification.Passed && !failureLog.HasBlockingFailure)
            {
                SharedCoordinateOrigin.Resolved resolvedOrigin = SharedCoordinateOrigin.Resolve(outcome.Payload.Provenance.LocalFrame);
                double eastWestInternal = RevitUnitConversion.ToInternal(resolvedOrigin.Origin.X, resolvedOrigin.HorizontalUnit);
                double northSouthInternal = RevitUnitConversion.ToInternal(resolvedOrigin.Origin.Y, resolvedOrigin.HorizontalUnit);
                double elevationInternal = RevitUnitConversion.ToInternal(resolvedOrigin.Origin.Elevation, resolvedOrigin.VerticalUnit);

                ProjectPosition requested = SharedCoordinatesWriter.Write(document, eastWestInternal, northSouthInternal, elevationInternal);
                sharedCoordinatesWritten = requested;

                // NEW Regenerate() call (distinct from the one above): whether a same-transaction read
                // immediately reflects SetProjectPosition's own transform update is unconfirmed by any
                // documentation source, so this call removes the dependency on that assumption rather than
                // depending on it. See docs/architecture/revit-property-line-and-shared-coordinates.md's "The
                // write itself and its source value" section's "Why a second document.Regenerate() call is
                // required before verification." passage.
                document.Regenerate();

                bool sharedCoordinatesWriteVerified = SharedCoordinatesWriter.VerifyWritten(
                    document, requested, toleranceInternal, out string? sharedCoordinatesProblem);
                sharedCoordinatesVerification = new VerificationResult(
                    sharedCoordinatesWriteVerified, sharedCoordinatesProblem ?? "Shared-coordinates write verified.");
            }

            if (!verification.Passed || !propertyLineVerification.Passed || !sharedCoordinatesVerification.Passed || failureLog.HasBlockingFailure)
            {
                // Widened from the shipped 2-way ternary to an explicit, ordered 4-way choice (SolidGround
                // Issue #30): toposolid verification, then property-line verification, then shared-coordinates
                // verification, then the blocking-Revit-failure fallback, in that fixed order. No partial
                // element either way: the whole transaction (Toposolid + PropertyLine, when attempted, + any
                // shared-coordinates write) rolls back together. For a non-parcel run, propertyLineVerification
                // .Passed is always true (the sentinel above), so it can never itself select the second branch.
                (string headline, string detail) =
                    !verification.Passed ? ("The created toposolid's geometry did not match the source data; the change was undone.", verification.Detail)
                    : !propertyLineVerification.Passed ? ("The created property line did not verify; the change was undone.", propertyLineVerification.Detail)
                    : !sharedCoordinatesVerification.Passed ? ("The shared-coordinates write did not verify; the change was undone.", sharedCoordinatesVerification.Detail)
                    : ("Revit reported a problem while finishing this run.", string.Join(" | ", failureLog.Messages));
                TransactionStatus rolledBack = transaction.RollBack();
                return ShowTransactionOutcome(rolledBack, headline, detail);
            }

            draft = BuildPlacementDraft(
                context, outcome, revitUnit, constantZInternal, toposolid, propertyLine, sharedCoordinatesWritten,
                exportDocumentFileName, exportPointsFileName);

            ToposolidCreatedHook? postCreationHook = ProvenanceEntityWriter.Attach; // Issue #16.
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
                //
                // Headline broadened the same way row 19's was (SolidGround Issue #30, PH3-3, review fix):
                // by this point a parcel-AOI run's PropertyLine, and an opted-in run's shared-coordinates
                // write, may also be mid-flight and equally unconfirmed, not only the toposolid.
                if (failureLog.HasBlockingFailure)
                {
                    ShowProblemList(
                        "SolidGround could not confirm whether this run's changes were created. Check the document and Undo if needed.",
                        "Revit reported the following while finishing the transaction:",
                        failureLog.Messages);
                }
                else
                {
                    ShowSingleFailed("SolidGround could not confirm whether this run's changes were created. Check the document and Undo if needed.");
                }

                return Result.Failed;
            }

            // Falls through to ReportSuccess below: commitStatus == Committed is the only way this try
            // block completes without an explicit return or a caught exception.
        }
        catch (Exception ex) when (ex is ToposolidCreationException or PropertyLineCreationException or SharedCoordinatesWriteException)
        {
            TransactionStatus status = transaction.HasEnded() ? transaction.GetStatus() : transaction.RollBack();
            // SolidGround Issue #30 review fix: log the full exception here, not only the dialog's own
            // non-redundant detail below. ex.Message is the only place SharedCoordinatesWriter.Write's and
            // .VerifyWritten's own distinct wording ("...write:" vs "...write during verification:") survives
            // -- the dialog body intentionally shows just the inner exception's message (see the next comment)
            // -- so without this line a log reader could never tell which of the two calls actually failed.
            AddInLog.Error("SolidGround's transaction was rolled back after Revit rejected a creation or write.", ex);
            // ex.Message already restates "Revit rejected..."; the inner exception's own message is the
            // non-redundant detail for the dialog body. Branches on the exception's runtime type: a non-parcel
            // run never calls PropertyLineCreationService.Create at all, so that branch is exercised only by a
            // parcel-AOI run (SolidGround Issue #30), and the shared-coordinates branch is exercised only when
            // sharedCoordinates.writeIfAbsent is enabled (SolidGround Issue #30 review fix: without this branch,
            // a real SetProjectPosition rejection fell through to the generic Stage-5 catch-all below and
            // blamed "the toposolid," even though it -- and any PropertyLine -- had already been validly
            // created and only the shared-coordinates write itself failed; error catalogue row 20b).
            string headline = ex switch
            {
                PropertyLineCreationException => "Revit rejected the generated property line boundary.",
                SharedCoordinatesWriteException => "Revit rejected the shared-coordinates write.",
                _ => "Revit rejected the generated toposolid boundary or points.",
            };
            return ShowTransactionOutcome(status, headline, ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Error("SolidGround hit a problem after the transaction started.", ex);
            TransactionStatus status = transaction.HasEnded() ? transaction.GetStatus() : transaction.RollBack();
            // Headline broadened the same way row 19's was (SolidGround Issue #30, PH3-3, review fix): this
            // catch-all can also be reached from PropertyLine creation, the shared-coordinates write, or the
            // Issue #16 provenance hook, not only from finishing the toposolid itself.
            return ShowTransactionOutcome(status, "SolidGround hit a problem while finishing this run.", ex.Message);
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
        PropertyLine? propertyLine,
        ProjectPosition? sharedCoordinatesWritten,
        string exportDocumentFileName,
        string exportPointsFileName)
    {
        TerrainProvenance provenance = outcome.Payload.Provenance;
        double roundTrip = UnitUtils.ConvertFromInternalUnits(UnitUtils.ConvertToInternalUnits(1.0, revitUnit), revitUnit) - 1.0;

        PlacementUnitConversionRecord unitConversion = new(
            LengthUnitTokens.SettingsToken(context.Settings.Request.OutputUnit),
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
                LengthUnitTokens.SettingsToken(provenance.SourceVerticalReference.Unit),
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

        PlacementExtensibleStorageRecord extensibleStorage = new(
            ExtensibleStorageProvenanceSchema.SchemaGuidText, ExtensibleStorageProvenanceSchema.CurrentVersion);

        // SolidGround Issue #30 (PH3-3). PropertyLine: created is always present; ElementId/AreaInternal are
        // null exactly when created is false (every non-parcel-AOI run, and structurally the only reachable
        // state for those AOI kinds).
        PlacementPropertyLineRecord propertyLineRecord = propertyLine is not null
            ? new PlacementPropertyLineRecord(true, propertyLine.Id.Value, propertyLine.Area)
            : new PlacementPropertyLineRecord(false, null, null);

        // sharedCoordinatesWrite.eastWest/northSouth/elevation are recorded in Origin's own native unit -- NOT
        // always meters (process mode can select a non-metric horizontal/vertical unit; see
        // docs/architecture/revit-property-line-and-shared-coordinates.md's "Unit convention for the
        // shared-coordinates value" section) and NOT the raw Revit-internal double actually passed to
        // ProjectPosition's constructor -- the identical values already recorded above in
        // localOrigin.sourceX/sourceY/sourceElevation, chosen so a reader can compare the two side by side
        // without first learning Revit's internal-foot convention; horizontalUnit/verticalUnit name that native
        // unit explicitly so the value is never ambiguous. angleInternal is the one exception, recorded
        // Revit-internal (radians): an angle has no length unit to convert into. Verified is always true here: a
        // false sharedCoordinatesVerification already rolled back the whole transaction before
        // BuildPlacementDraft was ever called, so no placement record reaches disk for that run.
        PlacementSharedCoordinatesWriteRecord sharedCoordinatesWriteRecord;
        if (sharedCoordinatesWritten is { } written)
        {
            SharedCoordinateOrigin.Resolved resolvedOrigin = SharedCoordinateOrigin.Resolve(provenance.LocalFrame);
            sharedCoordinatesWriteRecord = new PlacementSharedCoordinatesWriteRecord(
                Attempted: true,
                EastWest: resolvedOrigin.Origin.X,
                NorthSouth: resolvedOrigin.Origin.Y,
                Elevation: resolvedOrigin.Origin.Elevation,
                AngleInternal: written.Angle,
                HorizontalUnit: LengthUnitTokens.SettingsToken(resolvedOrigin.HorizontalUnit),
                VerticalUnit: LengthUnitTokens.SettingsToken(resolvedOrigin.VerticalUnit),
                Verified: true);
        }
        else
        {
            sharedCoordinatesWriteRecord = new PlacementSharedCoordinatesWriteRecord(false, null, null, null, null, null, null, null);
        }

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
            sharedCoordinatesWritten is null
                ? "SolidGround made no change to ActiveProjectLocation, the project base point, the survey point, or site location during this run."
                // 2026-09-27 live-evidence fix (manual evidence Step 14.5 re-run): the prior sentence claimed
                // the survey point was "otherwise left unchanged," but the same live session's log showed
                // ProjectLocation.SetProjectPosition moves the survey point's own internal Position from
                // (0, 0, 0) to the negative of the newly written east-west/north-south as an intrinsic side
                // effect of that one Revit API call -- see docs/architecture/revit-property-line-and-shared-coordinates.md's
                // "Why Write no longer sets Clipped" section. The sentence below states that move plainly
                // instead of denying it.
                : "SolidGround wrote this run's terrain origin as this model's shared coordinates (sharedCoordinates.writeIfAbsent). Revit moved the survey point to the new shared origin as part of that write; SolidGround made no other change to the project base point or site location.",
            pointCounts,
            extensibleStorage,
            propertyLineRecord,
            sharedCoordinatesWriteRecord);
    }

    private static PlacementPointRecord ToPointRecord(XYZ point) => new(point.X, point.Y, point.Z);

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
            if (draft.SharedCoordinatesWrite.Attempted)
            {
                // Skip Unchanged's comparison, which would otherwise misreport this run's own intended write as
                // an unexpected change (SolidGround Issue #30, PH3-3); log the new position directly instead.
                ProjectPosition current = context.Document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
                AddInLog.Info(
                    "SolidGround wrote shared coordinates this run (sharedCoordinates.writeIfAbsent): " +
                    $"EastWest={current.EastWest.ToString("R", CultureInfo.InvariantCulture)}, " +
                    $"NorthSouth={current.NorthSouth.ToString("R", CultureInfo.InvariantCulture)}, " +
                    $"Elevation={current.Elevation.ToString("R", CultureInfo.InvariantCulture)} (decimal feet).");
            }
            else if (OrphanCheck.Unchanged(context.OrphanBefore, orphanAfter, out string? orphanProblem))
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
            // SolidGround Issue #30 (PH3-3): computed at render time from draft.PropertyLine -- the same
            // structural field BuildPlacementDraft already threads through and therefore already has in scope
            // -- rather than a second, persisted free-text copy of the sentence.
            string propertyLineStatement = draft.PropertyLine.Created
                ? $"Property line: element id {draft.PropertyLine.ElementId!.Value.ToString(CultureInfo.InvariantCulture)}."
                : "Property line: not created (this area of interest is not a parcel boundary).";

            string body = string.Join(
                Environment.NewLine,
                $"Element id: {elementId.ToString(CultureInfo.InvariantCulture)}",
                $"Level: {context.Level.Name}",
                $"ToposolidType: {context.ToposolidType.Name}",
                $"Points retained: {draft.PointCounts.Retained.ToString(CultureInfo.InvariantCulture)} of {draft.PointCounts.Original.ToString(CultureInfo.InvariantCulture)} (budget {draft.PointCounts.Budget.ToString(CultureInfo.InvariantCulture)})",
                propertyLineStatement,
                $"Export bundle: {Path.Combine(context.Settings.Request.Output.Directory, draft.ExportDocument)}",
                placementPath is not null ? $"Placement record: {placementPath}" : "Placement record: could not be written (see log).",
                $"Log directory: {AddInLog.LogDirectory ?? "(unavailable)"}",
                string.Empty,
                // Built from draft.SharedCoordinatesStatement (now two-valued, SolidGround Issue #30) instead of
                // a second, independent, hardcoded copy of the disclaimer sentence, so the two can never drift
                // apart again.
                "SolidGround is a site-form tool, not a survey instrument. " + draft.SharedCoordinatesStatement);

            AddInLog.Info("Showing success dialog.");
            CreationCompletionPresenter.Show(context.Document, toposolid.Id, body, context.Settings.Request.Output.Directory);
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
        if (problems.Any(problem => problem.Contains("OpenTopography", StringComparison.OrdinalIgnoreCase)
            || problem.Contains("OPENTOPOGRAPHY_API_KEY", StringComparison.Ordinal)))
        {
            dialog.FooterText = "<a href=\"https://portal.opentopography.org/\">Request an API key through myOpenTopo</a>. " +
                "USGS 1 m requires academic authorization or enterprise access.";
        }
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
