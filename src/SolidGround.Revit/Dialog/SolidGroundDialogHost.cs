using System.Windows.Interop;
using System.Windows;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using SolidGround.Core.Hosting;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Core.Sources.Census;
using SolidGround.Core.Sources.Esri;
using SolidGround.Core.Sources.Geocodio;
using SolidGround.Core.Sources.LocalParcelFile;
using SolidGround.Revit.Diagnostics;
using SolidGround.Revit.Elements;
using SolidGround.Revit.Settings;
using SolidGround.Revit.Transactions;
using SolidGround.Revit.Processing;
using SolidGround.Core.Sources.BuildingOutlines;

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
        ExternalCommandData commandData, Document document, RevitSettings settings, double vertexToleranceInternal,
        Func<SolidGroundDialogResult, CancellationToken, Task<PreparedTerrainSnapshot>>? prepareTerrain = null)
    {
        ArgumentNullException.ThrowIfNull(commandData);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(settings);

        // One shared HttpClient instance lives for the dialog's lifetime, disposed on close (design record
        // "Threading and the network bridge") -- the same HttpClient backs both the geocoder and any
        // county-registry parcel source, exactly as Stage 2 acquisition's own fetch-mode HttpClient is timed.
        // Each lookup applies its snapshot's configured timeout through a linked CancellationTokenSource.
        // Keep the shared client unbounded so a Settings edit can change that timeout before the next lookup.
        using HttpClient httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
        using HttpClient outlineClient = MicrosoftGlobalMlBuildingOutlineSource.CreateHttpClient();
        (RevitIniToposolidThresholds.Thresholds thresholds, string revitIniPath) = ReadRevitIniThresholds(commandData);
        DialogPalette palette = DialogTheme.Resolve(UIThemeManager.CurrentTheme, SystemParameters.HighContrast);

        SolidGroundDialog? dialogOwner = null;
        SolidGroundDialogInputs inputs = BuildInputs(
            settings,
            httpClient,
            LevelAndTypeResolver.ListLevels(document),
            LevelAndTypeResolver.ListToposolidTypes(document),
            SharedCoordinatesDetector.LooksAlreadyCoordinated(document, vertexToleranceInternal),
            thresholds,
            revitIniPath,
            current => RevitSettingsIo.Edit(dialogOwner, current, palette),
            prepareTerrain,
            new MicrosoftGlobalMlBuildingOutlineSource(outlineClient));

        SolidGroundDialogViewModel viewModel = new(inputs);
        // Revit theme access remains in the Revit-only host. The palette-injected dialog constructor is kept
        // free of UIThemeManager so the local WPF test lane can render it without loading RevitAPIUI.
        dialogOwner = new SolidGroundDialog(viewModel, palette);
        _ = new WindowInteropHelper(dialogOwner) { Owner = commandData.Application.MainWindowHandle };

        AddInLog.Info("Showing the SolidGround interactive dialog.");
        dialogOwner.ShowDialog();

        return viewModel.Result;
    }

    /// <summary>
    /// Builds the dialog's immutable preference input and the initial immutable lookup-services snapshot. The
    /// callback deliberately creates a fresh snapshot from the post-save settings instead of retaining the
    /// source objects assembled at dialog-open time: configuring a previously missing county registry, changing
    /// provider, or entering a session-only keyed-provider value must work without closing and reopening the
    /// guided dialog. <see cref="SessionApiKeyOverrides"/> stays process/session scoped and is never written to
    /// <paramref name="settings"/>.
    /// </summary>
    private static SolidGroundDialogInputs BuildInputs(
        RevitSettings settings,
        HttpClient httpClient,
        IReadOnlyList<NamedElevationCandidate> levelCandidates,
        IReadOnlyList<NamedCandidate> toposolidTypeCandidates,
        bool documentAlreadyHasSharedCoordinates,
        RevitIniToposolidThresholds.Thresholds thresholds,
        string revitIniPath,
        Func<RevitSettings, RevitSettings?> editSettings,
        Func<SolidGroundDialogResult, CancellationToken, Task<PreparedTerrainSnapshot>>? prepareTerrain,
        IBuildingOutlineSource? buildingOutlineSource)
    {
        SolidGroundDialogLookupServices initialServices = BuildLookupServices(settings, httpClient);
        return new SolidGroundDialogInputs(
            initialServices.Geocoder,
            initialServices.GeocoderProvider,
            initialServices.ParcelSource,
            levelCandidates,
            toposolidTypeCandidates,
            settings.Target.LevelName,
            settings.Target.ToposolidTypeName,
            settings.Request.OutputUnit,
            settings.Request.Simplification.PointBudget,
            settings.SharedCoordinates.WriteIfAbsent,
            documentAlreadyHasSharedCoordinates,
            thresholds,
            revitIniPath,
            settings.Request.NetworkTimeoutSeconds,
            settings.Request.AreaOfInterest,
            initialServices.NearbySearchRadiusMeters,
            settings.Request.Mode,
            InitialCredentialRevision: initialServices.CredentialRevision,
            Settings: settings,
            EditSettings: editSettings,
            ReconfigureLookupServices: updated => BuildLookupServices(updated, httpClient),
            PrepareTerrain: prepareTerrain,
            BuildingOutlineSource: buildingOutlineSource);
    }

    /// <summary>
    /// Creates one lookup snapshot from the actual post-save settings. Keyed geocoders intentionally use the
    /// Revit session override providers, whose current value wins over an environment value without persisting
    /// the key. County authorization is enforced by <see cref="BuildParcelSource"/> for every snapshot.
    /// </summary>
    private static SolidGroundDialogLookupServices BuildLookupServices(RevitSettings settings, HttpClient httpClient)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(httpClient);

        IAddressGeocoder geocoder = settings.AddressAndParcel.GeocoderProvider switch
        {
            AddressGeocoderProvider.Census => new CensusGeocoder(httpClient),
            AddressGeocoderProvider.Geocodio => new GeocodioGeocoder(httpClient, SessionApiKeyOverrides.GeocodioProvider()),
            AddressGeocoderProvider.Esri => new EsriGeocoder(httpClient, SessionApiKeyOverrides.EsriProvider()),
            _ => throw new ArgumentOutOfRangeException(nameof(settings), settings.AddressAndParcel.GeocoderProvider, "Unsupported address geocoder provider."),
        };

        // A settings-file override wins when configured (already validated finite/positive at decode time,
        // RevitSettingsIo.ParseAddressAndParcel); otherwise Core's documented default applies.
        double nearbySearchRadiusMeters = settings.AddressAndParcel.NearbySearchRadiusMeters ?? NearbyParcelBoundaryFinder.DefaultRadiusMeters;
        return new SolidGroundDialogLookupServices(
            geocoder,
            settings.AddressAndParcel.GeocoderProvider,
            BuildParcelSource(settings.AddressAndParcel, httpClient),
            nearbySearchRadiusMeters,
            settings.Request.NetworkTimeoutSeconds,
            SessionApiKeyOverrides.Revision);
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
            if (!settings.CountyServiceAuthorizedUseAcknowledged)
            {
                return new FailedParcelSource("County parcel service use has not been acknowledged. Open Settings, confirm you are authorized to use this service, and save before searching.");
            }
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
            if (string.IsNullOrWhiteSpace(settings.LocalParcelFileSourceLabel) ||
                string.IsNullOrWhiteSpace(settings.LocalParcelFileLicenseDisclaimerText))
            {
                return new FailedParcelSource(
                    "The local parcel source requires its actual source label and license/disclaimer text. Open Settings, provide both values, and save before searching.");
            }

            return new LocalParcelFileSource(new LocalParcelFileOptions
            {
                Path = settings.LocalParcelFilePath,
                SourceLabel = settings.LocalParcelFileSourceLabel,
                LicenseDisclaimerText = settings.LocalParcelFileLicenseDisclaimerText,
            });
        }

        return null;
    }

    /// <summary>
    /// A deferred stand-in <see cref="IParcelBoundarySource"/> for an invalid county registry or incomplete
    /// local-file provenance. Carries the captured failure message and raises it as an
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
