using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SolidGround.Core.Hosting;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Core.Sources.LocalParcelFile;
using SolidGround.Revit.Diagnostics;
using SolidGround.Revit.Elements;
using SolidGround.Revit.Settings;
using SolidGround.Revit.Transactions;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// Constructs <see cref="SolidGroundDialogInputs"/> from an already-open <see cref="Document"/> and the
/// already-loaded <see cref="RevitSettings"/>, shows <see cref="SolidGroundDialog"/> modally, and returns the
/// operator's confirmed <see cref="SolidGroundDialogResult"/>, or <see langword="null"/> when the operator
/// cancelled (SolidGround Issue #31, PH3-4, Stage D). Called once, from <c>CreateToposolidCommand.ExecuteCore</c>'s
/// Stage 0.5, after Stage 0 (<c>LoadDocumentAndSettings</c>) and before Stage 1 Preflight. See
/// docs/architecture/revit-interactive-dialog.md's "Result-code mapping" and "Settings interaction: prefill,
/// not override".
/// </summary>
internal static class SolidGroundDialogHost
{
    /// <summary>
    /// Owns the dialog via <see cref="WindowInteropHelper"/> against the Revit main window handle
    /// (<c>UIApplication.MainWindowHandle</c>, confirmed present in the installed Revit 2027
    /// <c>27.0.10.13</c> <c>RevitAPIUI.dll</c>: docs/architecture/phase-3-interactive-add-in-research.md).
    /// <paramref name="vertexToleranceInternal"/> is Stage 0's own already-read
    /// <c>Application.VertexTolerance</c> (Revit-internal decimal feet), threaded in rather than read a second
    /// time here -- <c>SharedCoordinatesGate.cs</c>'s own <c>LooksAlreadyCoordinated</c> doc comment already
    /// commits to a "the caller already reads this once" contract.
    /// </summary>
    internal static SolidGroundDialogResult? ShowModal(
        ExternalCommandData commandData, Document document, RevitSettings settings, double vertexToleranceInternal)
    {
        ArgumentNullException.ThrowIfNull(commandData);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        // One shared HttpClient instance lives for the dialog's lifetime, disposed on close (design record
        // "Threading and the network bridge") -- the same HttpClient backs both the geocoder and any
        // county-registry parcel source, exactly as Stage 2 acquisition's own fetch-mode HttpClient is timed.
        using HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(settings.Request.NetworkTimeoutSeconds) };
        IAddressGeocoder geocoder = AddressGeocoderFactory.Create(
            new AddressGeocoderSettings { Provider = settings.AddressAndParcel.GeocoderProvider }, httpClient);
        IParcelBoundarySource? parcelSource = BuildParcelSource(settings.AddressAndParcel, httpClient);

        (RevitIniToposolidThresholds.Thresholds thresholds, string revitIniPath) = ReadRevitIniThresholds(commandData);

        // A settings-file override wins when configured (already validated finite/positive at decode time,
        // RevitSettingsIo.ParseAddressAndParcel); otherwise Core's own documented default (SolidGround Issue
        // #31 follow-up's nearby-parcel fallback tier).
        double nearbySearchRadiusMeters = settings.AddressAndParcel.NearbySearchRadiusMeters ?? NearbyParcelBoundaryFinder.DefaultRadiusMeters;

        SolidGroundDialogInputs inputs = new(
            geocoder,
            settings.AddressAndParcel.GeocoderProvider,
            parcelSource,
            LevelAndTypeResolver.ListLevels(document),
            LevelAndTypeResolver.ListToposolidTypes(document),
            settings.Target.LevelName,
            settings.Target.ToposolidTypeName,
            settings.Request.OutputUnit,
            settings.Request.Simplification.PointBudget,
            settings.SharedCoordinates.WriteIfAbsent,
            SharedCoordinatesDetector.LooksAlreadyCoordinated(document, vertexToleranceInternal),
            thresholds,
            revitIniPath,
            settings.Request.NetworkTimeoutSeconds,
            settings.Request.AreaOfInterest,
            nearbySearchRadiusMeters,
            settings.Request.Mode,
            Settings: settings);

        SolidGroundDialogViewModel viewModel = new(inputs);
        SolidGroundDialog dialog = new(viewModel);
        _ = new WindowInteropHelper(dialog) { Owner = commandData.Application.MainWindowHandle };

        AddInLog.Info("Showing the SolidGround interactive dialog.");
        dialog.ShowDialog();

        return viewModel.Result;
    }

    /// <summary>
    /// <see cref="RevitAddressAndParcelSettings.CountyRegistryPath"/> wins when non-blank; else
    /// <see cref="RevitAddressAndParcelSettings.LocalParcelFilePath"/> wins when non-blank; else
    /// <see langword="null"/> -- <see cref="SolidGroundDialogViewModel.FindParcel"/> then shows a clear inline
    /// configuration message the first time a parcel lookup is attempted, never here at construction time. A
    /// non-blank <see cref="RevitAddressAndParcelSettings.CountyRegistryPath"/> that
    /// <see cref="CountyParcelRegistry.Load"/> itself rejects (missing/unreadable file, invalid JSON, any other
    /// documented content problem) is caught here for the identical reason (review finding, major, fixed): the
    /// failure is deferred to a <see cref="FailedParcelSource"/> stand-in rather than thrown out of
    /// <see cref="ShowModal"/>, so it can never block the dialog from opening at all -- including for an
    /// operator who only wanted the unrelated "Use the area in the settings file" AOI path and never needed a
    /// parcel source in the first place. A blank <see cref="RevitAddressAndParcelSettings.CountyGeoidOverride"/>
    /// is threaded through unchanged, so <see cref="AutoGeoidCountyParcelSource"/> resolves it from the
    /// confirmed geocode candidate's own coordinates, mirroring
    /// <c>SolidGround.Cli.Commands.ParcelCommand</c>'s own existing auto-GEOID behavior.
    /// </summary>
    private static IParcelBoundarySource? BuildParcelSource(RevitAddressAndParcelSettings settings, HttpClient httpClient)
    {
        if (!string.IsNullOrWhiteSpace(settings.CountyRegistryPath))
        {
            try
            {
                CountyParcelRegistry registry = CountyParcelRegistry.Load(settings.CountyRegistryPath);
                return new AutoGeoidCountyParcelSource(httpClient, registry, settings.CountyGeoidOverride);
            }
            catch (CountyParcelRegistryFormatException ex)
            {
                AddInLog.Warning($"Could not load the configured county parcel registry; deferring this as an inline parcel-lookup error: {ex.Message}");
                return new FailedParcelSource(ex.Message);
            }
        }

        if (!string.IsNullOrWhiteSpace(settings.LocalParcelFilePath))
        {
            return new LocalParcelFileSource(new LocalParcelFileOptions
            {
                Path = settings.LocalParcelFilePath,
                SourceLabel = settings.LocalParcelFileSourceLabel ?? "Local parcel file",
                LicenseDisclaimerText = settings.LocalParcelFileLicenseDisclaimerText
                    ?? "No license/disclaimer text was configured for this local parcel file.",
            });
        }

        return null;
    }

    /// <summary>
    /// A deferred stand-in <see cref="IParcelBoundarySource"/>, returned only when
    /// <see cref="CountyParcelRegistry.Load"/> fails inside <see cref="BuildParcelSource"/> (review finding,
    /// major, fixed). Carries the captured failure message and raises it as an
    /// <see cref="AutoGeoidCountyParcelSourceException"/> -- a <see cref="ParcelBoundarySourceException"/>
    /// subtype -- the first (and every) time <see cref="FindAsync"/> is actually called, so
    /// <see cref="SolidGroundDialogViewModel.FindParcel"/>'s existing, unchanged
    /// <c>catch (ParcelBoundarySourceException ex)</c> clause reports it inline exactly like any other
    /// parcel-lookup failure -- never at dialog-construction time.
    /// </summary>
    private sealed class FailedParcelSource(string message) : IParcelBoundarySource
    {
        public ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default) =>
            throw new AutoGeoidCountyParcelSourceException(message);
    }

    /// <summary>
    /// A second, independent read of the same small <c>Revit.ini</c> file
    /// <c>CreateToposolidCommand.CheckRevitIniPointThreshold</c> reads at Preflight (design record "AOI and
    /// provenance": "no shared cache needed, Revit.ini is not expected to change mid-session"). Tolerant by
    /// design, matching that method's own contract: a missing file, an inaccessible data folder, or any other
    /// read error yields an all-<see langword="null"/> <see cref="RevitIniToposolidThresholds.Thresholds"/>
    /// rather than throwing, so a <c>Revit.ini</c> problem never blocks the dialog itself from opening --
    /// Preflight's own guard still runs afterward regardless.
    /// </summary>
    private static (RevitIniToposolidThresholds.Thresholds Thresholds, string RevitIniPath) ReadRevitIniThresholds(ExternalCommandData commandData)
    {
        string revitIniPath;
        string revitIniText;
        try
        {
            string dataFolderPath = commandData.Application.Application.CurrentUsersDataFolderPath;
            revitIniPath = Path.Combine(dataFolderPath, "Revit.ini");
            revitIniText = File.ReadAllText(revitIniPath);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AddInLog.Warning($"Could not read Revit.ini for the dialog's own point-budget warning; skipping: {ex.GetType().Name}: {ex.Message}");
            return (new RevitIniToposolidThresholds.Thresholds(null, null), "(unknown)");
        }

        return (RevitIniToposolidThresholds.Parse(revitIniText), revitIniPath);
    }
}
