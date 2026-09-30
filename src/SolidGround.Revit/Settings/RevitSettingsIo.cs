using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using SolidGround.Core.Processing;
using SolidGround.Core.Configuration;
using SolidGround.Core.Sources;
using SolidGround.Revit.Dialog;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Settings;

/// <summary>
/// Reads and (once, when absent) writes the one flat settings document at a caller-supplied path (normally
/// <see cref="RevitSettingsLocator.Resolve"/>'s result). See SolidGround Issue #15's design record §2.4 row
/// 22 and §4.4.
/// </summary>
/// <remarks>
/// AGENTS.md's general settings convention calls for "a mutex plus digest-conflict check plus atomic write".
/// The digest-conflict check has no live trigger here: <see cref="EnsureTemplateExists"/> only ever creates
/// the file when it is absent and never rewrites an existing one (even an invalid one), so no read-modify-
/// write cycle -- the only case a digest conflict could arise from -- ever happens in this milestone. A
/// future settings-editing feature that performs a real read-modify-write would need to add it.
/// </remarks>
internal static class RevitSettingsIo
{
    /// <summary>
    /// Loads the effective per-user UI settings without creating or rewriting a file. A missing document is a
    /// valid first-use state and returns the guided-fetch defaults; the read-only ProgramData document remains
    /// available for an explicit legacy import rather than blocking the property dialog.
    /// </summary>
    internal static RevitSettings? LoadForUi(Window? owner)
    {
        string path = RevitSettingsLocator.Resolve();
        string legacyPath = RevitSettingsLocator.ResolveLegacyImport();
        RevitSettings? legacy = null;
        string? legacyError = null;
        if (!File.Exists(path) && File.Exists(legacyPath) && TryLoad(legacyPath, out legacy, out legacyError))
        {
            RevitSettings importedLegacy = ImportLegacy(legacy!, legacyPath);
            string importSummary = DescribeLegacyImport(importedLegacy, legacyPath);
            MessageBoxResult choice = owner is null
                ? MessageBox.Show(importSummary, "Previous settings found", MessageBoxButton.YesNoCancel, MessageBoxImage.Information)
                : MessageBox.Show(owner, importSummary, "Previous settings found", MessageBoxButton.YesNoCancel, MessageBoxImage.Information);
            if (choice == MessageBoxResult.Yes)
            {
                AddInLog.Info("Operator chose a read-only legacy settings import draft.");
                return importedLegacy;
            }
            if (choice == MessageBoxResult.No)
            {
                AddInLog.Info("Operator chose Start new settings instead of legacy import.");
                return UiSettingsStore.CreateDefault();
            }
            AddInLog.Info("Operator cancelled legacy import.");
            return null;
        }
        if (!File.Exists(path) && File.Exists(legacyPath))
        {
            string legacyFailure = legacyError ?? "The previous SolidGround settings file could not be imported.";
            MessageBoxResult choice = owner is null
                ? MessageBox.Show(legacyFailure + Environment.NewLine + Environment.NewLine + "Start new settings instead? The legacy bytes will remain unchanged.", "Previous settings need repair", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(owner, legacyFailure + Environment.NewLine + Environment.NewLine + "Start new settings instead? The legacy bytes will remain unchanged.", "Previous settings need repair", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (choice == MessageBoxResult.Yes)
            {
                AddInLog.Info("Operator explicitly chose Start new after legacy import failed.");
                return UiSettingsStore.CreateDefault();
            }
            AddInLog.Info("Operator cancelled after legacy import failed.");
            return null;
        }
        if (UiSettingsStore.TryLoad(path, out UiSettingsDraft? draft, out string? error))
        {
            return RebaseInputPaths(draft!.Settings, path);
        }

        string repairError = error ?? "Could not load per-user settings; explicit repair is required.";
        AddInLog.Warning(repairError);
        throw new UiSettingsRepairRequiredException(repairError);
    }

    /// <summary>Opens the shared Settings editor without accessing or modifying a Revit document.</summary>
    internal static RevitSettings? Edit(Window? owner, RevitSettings? current, DialogPalette? palette = null)
    {
        current ??= UiSettingsStore.CreateDefault();
        string path = RevitSettingsLocator.Resolve();
        bool requiresExplicitStartNew = false;
        if (!UiSettingsStore.TryLoad(path, out UiSettingsDraft? draft, out string? error))
        {
            AddInLog.Warning(error ?? "Could not load per-user settings for editing.");
            draft = new UiSettingsDraft(current, SolidGround.Core.Configuration.AtomicSettingsFile.Read(path).Version, path);
            requiresExplicitStartNew = true;
        }

        return SettingsDialog.ShowModal(owner, draft!, current, palette, requiresExplicitStartNew, error);
    }

    /// <summary>Opens the editor as an owned child of Revit's verified main-window handle.</summary>
    internal static RevitSettings? Edit(IntPtr ownerHandle, RevitSettings? current, DialogPalette palette)
    {
        current ??= UiSettingsStore.CreateDefault();
        string path = RevitSettingsLocator.Resolve();
        bool requiresExplicitStartNew = false;
        if (!UiSettingsStore.TryLoad(path, out UiSettingsDraft? draft, out string? error))
        {
            AddInLog.Warning(error ?? "Could not load per-user settings for editing.");
            draft = new UiSettingsDraft(current, SolidGround.Core.Configuration.AtomicSettingsFile.Read(path).Version, path);
            requiresExplicitStartNew = true;
        }

        return SettingsDialog.ShowModal(ownerHandle, draft!, current, palette, requiresExplicitStartNew, error);
    }

    /// <summary>Turns persisted relative file references into absolute paths using the document that supplied them.</summary>
    internal static RevitSettings RebaseInputPaths(RevitSettings settings, string settingsDocumentPath)
    {
        string Resolve(string? value) => string.IsNullOrWhiteSpace(value) ? value ?? string.Empty : SettingsPathResolver.Resolve(settingsDocumentPath, value);
        ProcessInputSettings? process = settings.Request.Process is { } input
            ? input with { Asc = Resolve(input.Asc), Prj = string.IsNullOrWhiteSpace(input.Prj) ? null : Resolve(input.Prj), SourceJson = string.IsNullOrWhiteSpace(input.SourceJson) ? null : Resolve(input.SourceJson) }
            : null;
        AoiSettings aoi = settings.Request.AreaOfInterest;
        ParcelAoiSettings? parcel = aoi.Parcel is { } configuredParcel
            ? configuredParcel with { Path = Resolve(configuredParcel.Path) }
            : null;
        TerrainRequestSettings request = settings.Request with
        {
            Process = process,
            AreaOfInterest = aoi with { Parcel = parcel },
            Output = settings.Request.Output with { Directory = Resolve(settings.Request.Output.Directory) },
        };
        RevitAddressAndParcelSettings address = settings.AddressAndParcel with
        {
            CountyRegistryPath = string.IsNullOrWhiteSpace(settings.AddressAndParcel.CountyRegistryPath) ? null : Resolve(settings.AddressAndParcel.CountyRegistryPath),
            LocalParcelFilePath = string.IsNullOrWhiteSpace(settings.AddressAndParcel.LocalParcelFilePath) ? null : Resolve(settings.AddressAndParcel.LocalParcelFilePath),
        };
        return settings with { Request = request, AddressAndParcel = address };
    }

    private static RevitSettings ImportLegacy(RevitSettings legacy, string legacyPath)
    {
        RevitSettings rebased = RebaseInputPaths(legacy, legacyPath);
        double extension = rebased.Request.AreaOfInterest.Parcel?.BufferMeters ?? 0d;
        AoiSettings aoi = rebased.Request.AreaOfInterest;
        ParcelAoiSettings? parcel = aoi.Parcel is { } original ? original with { BufferMeters = 0d } : null;
        return rebased with
        {
            Request = rebased.Request with { AreaOfInterest = aoi with { Parcel = parcel } },
            TerrainExtensionMeters = extension,
            SharedCoordinates = new RevitSharedCoordinatesSettings(false),
        };
    }

    internal static string DescribeLegacyImport(RevitSettings legacy, string legacyPath)
    {
        ArgumentNullException.ThrowIfNull(legacy);
        ProcessInputSettings? process = legacy.Request.Process;
        string source = legacy.Request.Mode == TerrainAcquisitionMode.Process
            ? $"Process mode; raster '{process?.Asc ?? "missing"}', projection '{process?.Prj ?? "none"}', source sidecar '{process?.SourceJson ?? "none"}'."
            : "Fetch mode; any unused local process fields will remain inactive.";
        RevitAddressAndParcelSettings address = legacy.AddressAndParcel;
        string parcelSource = !string.IsNullOrWhiteSpace(address.CountyRegistryPath)
            ? $"County registry '{address.CountyRegistryPath}' (authorization acknowledgement: {address.CountyServiceAuthorizedUseAcknowledged})."
            : !string.IsNullOrWhiteSpace(address.LocalParcelFilePath)
                ? $"Local parcel source '{address.LocalParcelFilePath}' with its stored label/license text."
                : "No parcel source registration is configured.";
        return "A previous SolidGround settings file is available. Import it into this session?" + Environment.NewLine + Environment.NewLine
            + source + Environment.NewLine
            + $"Local origin: {legacy.Request.LocalOrigin.Kind}; output: {legacy.Request.OutputUnit}; terrain extension: {legacy.TerrainExtensionMeters.ToString("R", CultureInfo.InvariantCulture)} m." + Environment.NewLine
            + parcelSource + Environment.NewLine
            + "Paths shown above are rebased from the legacy file folder. Shared-coordinate writing is turned off. The legacy bytes remain unchanged until an explicit Save creates per-user settings." + Environment.NewLine + Environment.NewLine
            + "Yes: review/import. No: start new settings. Cancel: abort.";
    }
    /// <summary>How long <see cref="EnsureTemplateExists"/> waits to acquire the cross-process settings lock before reporting a lock problem (orchestrator decision (c)).</summary>
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    // Kept in exact sync with the committed §4.3 template this record's own design doc specifies, and with
    // tests/SolidGround.Tests/TerrainRequestSettingsTests.cs's ShippedTemplateText, which proves this exact
    // text decodes cleanly (JsonOptionsDecodesTheShippedTemplateTextVerbatim) after the same "level"/
    // "toposolidType" split TryLoad performs below.
    private const string TemplateJson = """
        // %ProgramData%\SolidGround\Revit\settings.json
        // SolidGround edits this file only to create it; it never rewrites an existing one.
        // Delete or rename this file to have SolidGround regenerate this template on the next run.
        {
          // "fetch": call OpenTopography live (needs OPENTOPOGRAPHY_API_KEY in Revit's own process environment).
          // "process": read a local AAIGrid .asc/.prj pair (and optional .source.json sidecar) from disk, no network.
          "mode": "process",

          // Read only when the interactive dialog's operator chooses "Use the area in the settings file"
          // (SolidGround Issue #31, PH3-4); choosing "Find a parcel" instead resolves an address/point and
          // parcel boundary interactively and ignores this section entirely for that run.
          "areaOfInterest": {
            // "boundingBox" | "radius" | "parcel" -- give exactly the matching object below.
            "kind": "parcel",
            "boundingBox": null,
            "radius": null,
            "parcel": { "path": "C:\\SolidGround\\parcel.geojson", "format": "geojson", "bufferMeters": 0.0 }
          },

          // Required when mode is "process"; ignored (may be omitted) when mode is "fetch".
          "process": {
            "asc": "C:\\SolidGround\\terrain.asc",
            "prj": null,
            "sourceJson": null,
            "sourceName": null, "dataset": null,
            "verticalDatum": null, "verticalUnit": null, "geoid": null,
            "collectionStart": null, "collectionEnd": null, "qualityLevel": null
          },

          // "southwest" | "centroid" | "explicit". x/y/z are only read when kind is "explicit".
          "localOrigin": { "kind": "southwest", "x": 0.0, "y": 0.0, "z": 0.0 },

          // "usSurveyFoot" | "internationalFoot" | "meter" -- exact 1200/3937 m and 0.3048 m definitions.
          "outputUnit": "usSurveyFoot",

          "simplification": { "method": "curvatureAware", "pointBudget": 15000, "coverageFloorFraction": 0.2 },

          // Blank/null means: pick the existing Level with the lowest elevation (ties by name).
          "level": { "name": null },
          // Blank/null means: pick the first existing ToposolidType by name.
          "toposolidType": { "name": null },

          // "writeIfAbsent": true lets SolidGround write this run's terrain origin as this model's shared
          // coordinates (ActiveProjectLocation), but ONLY when the model has none yet -- Preflight refuses when it
          // looks like the model already has shared coordinates set. Default false: unchanged from Issue #15.
          // The interactive dialog's checkbox (SolidGround Issue #31, PH3-4) prefills from this value but always
          // overrides it for the run about to happen; nothing is ever written back here.
          "sharedCoordinates": { "writeIfAbsent": false },

          // Configures the interactive dialog's own address/parcel lookup (SolidGround Issue #31, PH3-4); unused
          // when the operator chooses "Use the area in the settings file". "geocoderProvider" is one of
          // "census" | "geocodio" | "esri". "countyRegistryPath" wins when non-blank; else "localParcelFilePath"
          // wins when non-blank; else the dialog shows an inline configuration error the first time a parcel
          // lookup is attempted. A blank "countyGeoidOverride" auto-resolves the county GEOID from the
          // confirmed geocode candidate's own coordinates.
          "addressAndParcel": {
            "geocoderProvider": "census",
            "countyRegistryPath": null,
            "countyGeoidOverride": null,
            "localParcelFilePath": null,
            "localParcelFileSourceLabel": null,
            // If this is ever configured against a real, purchased Regrid Data Store export, this string must
            // name the purchase date and the license's own one-year Term, after which it requires promptly
            // ceasing all use of the data or deleting it entirely -- this exact text is what the interactive
            // dialog shows verbatim, every run (LocalParcelFileOptions.LicenseDisclaimerText; see
            // docs/architecture/source-licensing-and-attribution.md's "Regrid Data Store obligations" section
            // for the license's exact wording).
            "localParcelFileLicenseDisclaimerText": null,
            // Overrides the nearby-parcel fallback tier's own default search radius (30 m), used only when a
            // geocoded point resolves zero parcels outright. Must be a finite, positive number of meters when
            // given. Null (the default) uses NearbyParcelBoundaryFinder.DefaultRadiusMeters.
            "nearbySearchRadiusMeters": null
          },

          "output": { "directory": "C:\\ProgramData\\SolidGround\\Revit\\Exports", "baseName": "terrain" },

          "networkTimeoutSeconds": 300
        }
        """;

    /// <summary>
    /// Writes <see cref="TemplateJson"/> to <paramref name="settingsPath"/> only when the file is absent,
    /// guarded by a named cross-process <see cref="Mutex"/> so two concurrent first-launches never race. Re-
    /// checks absence inside the mutex; a concurrent winner is success (<paramref name="justCreated"/>
    /// <see langword="false"/>), not an error. Never rewrites an existing file, even an invalid one -- a
    /// decode/validation failure is always reported by <see cref="TryLoad"/> instead.
    /// </summary>
    /// <param name="justCreated"><see langword="true"/> only when this call actually wrote the file.</param>
    /// <param name="writeError">
    /// Non-null when the file is still absent afterward because of a lock timeout or a write failure. A
    /// timeout is reported as a settings-lock problem (orchestrator decision (c)), never as a permissions
    /// problem; an <see cref="UnauthorizedAccessException"/> gets its own distinct message (§0.3 item 7/§4.4),
    /// never conflated with "file simply doesn't exist yet".
    /// </param>
    internal static void EnsureTemplateExists(string settingsPath, out bool justCreated, out string? writeError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        justCreated = false;
        writeError = null;

        if (File.Exists(settingsPath))
        {
            return;
        }

        using Mutex mutex = new(initiallyOwned: false, BuildMutexName(settingsPath));
        bool acquired;
        try
        {
            acquired = mutex.WaitOne(MutexTimeout);
        }
        catch (AbandonedMutexException)
        {
            // Orchestrator decision (c): an abandoned mutex still transfers ownership to this thread; treat
            // it as a normal acquisition rather than a lock failure.
            acquired = true;
        }

        if (!acquired)
        {
            writeError =
                $"SolidGround could not acquire its settings lock within {MutexTimeout.TotalSeconds:N0} seconds " +
                $"(the lock is '{BuildMutexName(settingsPath)}'). Close any other SolidGround process that may " +
                "be starting at the same time and run this command again.";
            AddInLog.Warning(writeError);
            return;
        }

        try
        {
            if (File.Exists(settingsPath))
            {
                // A concurrent winner already created it while this thread waited for the mutex.
                return;
            }

            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string normalized = TemplateJson.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
            string tempPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, normalized, Utf8NoBom);
            File.Move(tempPath, settingsPath);
            justCreated = true;
            AddInLog.Info($"Wrote a starting settings template to '{settingsPath}'.");
        }
        catch (UnauthorizedAccessException ex)
        {
            writeError =
                $"SolidGround could not create its settings file at '{settingsPath}' because the process lacks " +
                "write access. Ask an administrator to grant write access to '%ProgramData%\\SolidGround\\Revit\\', " +
                "or create the file by hand using the template in docs/architecture/revit-toposolid-creation.md.";
            AddInLog.Error("Could not write the settings template (access denied).", ex);
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException)
        {
            writeError = $"SolidGround could not create its settings file at '{settingsPath}': {ex.Message}";
            AddInLog.Error("Could not write the settings template.", ex);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Strictly decodes <paramref name="settingsPath"/>: splits the one flat document into the
    /// <c>TerrainRequestSettings</c>-shaped portion and the Revit-only <c>level</c>/<c>toposolidType</c> name
    /// overrides (mirroring <c>TerrainRequestSettingsTests.JsonOptionsDecodesTheShippedTemplateTextVerbatim</c>'s
    /// own split), decodes the first with <see cref="TerrainRequestSettings.JsonOptions"/>, then runs
    /// <see cref="TerrainRequestSettings.Validate"/>, folding every problem -- decode or validation -- into
    /// one multi-line <paramref name="error"/>. Never itself checks file existence for AOI/process paths;
    /// that is a Preflight concern.
    /// </summary>
    internal static bool TryLoad(string settingsPath, [NotNullWhen(true)] out RevitSettings? settings, out string? error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        settings = null;
        error = null;

        string text;
        try
        {
            text = File.ReadAllText(settingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"'{settingsPath}' could not be read: {ex.Message}";
            return false;
        }

        JsonObject requestShapedPortion;
        string? levelName;
        string? toposolidTypeName;
        bool sharedCoordinatesWriteIfAbsent;
        RevitAddressAndParcelSettings addressAndParcel;
        try
        {
            JsonDocumentOptions documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
            JsonNode? node = JsonNode.Parse(text, documentOptions: documentOptions);
            if (node is not JsonObject root)
            {
                error = $"'{settingsPath}' does not contain a JSON object.";
                return false;
            }

            levelName = (string?)root["level"]?["name"];
            toposolidTypeName = (string?)root["toposolidType"]?["name"];
            sharedCoordinatesWriteIfAbsent = (bool?)root["sharedCoordinates"]?["writeIfAbsent"] ?? false;
            addressAndParcel = ParseAddressAndParcel(root["addressAndParcel"]);
            root.Remove("level");
            root.Remove("toposolidType");
            root.Remove("sharedCoordinates");
            root.Remove("addressAndParcel");
            requestShapedPortion = root;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            // Covers a syntactically malformed document and the "level"/"toposolidType" (or their "name"
            // child) being present but shaped as something other than an object/string -- neither should
            // surface as a raw, undocumented exception (error catalogue row 4).
            error = $"'{settingsPath}' could not be parsed as SolidGround settings: {ex.Message}";
            return false;
        }
        catch (AddressAndParcelDecodeException ex)
        {
            // Its own catch clause (review: distinct from the generic parse-failure branch above), since this
            // one names the exact bad "geocoderProvider" token rather than a generic parse failure.
            error = $"'{settingsPath}' could not be parsed as SolidGround settings: {ex.Message}";
            return false;
        }

        TerrainRequestSettings? request;
        try
        {
            // Same JsonSerializerOptions instance every decode of this record uses (§4.2): a JsonException
            // from an unrecognized mode/outputUnit/localOrigin.kind/simplification.method/process.verticalUnit
            // token is caught here, folded into `error` exactly like any other malformed-JSON failure, and
            // never surfaces its raw text to Validate() or the caller.
            request = requestShapedPortion.Deserialize<TerrainRequestSettings>(TerrainRequestSettings.JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"'{settingsPath}' could not be decoded: {ex.Message}";
            return false;
        }

        if (request is null)
        {
            error = $"'{settingsPath}' decoded to an empty document.";
            return false;
        }

        IReadOnlyList<string> problems = request.Validate();
        if (problems.Count > 0)
        {
            error = string.Join(Environment.NewLine, problems);
            return false;
        }

        settings = new RevitSettings(
            request,
            new RevitTargetSettings(levelName, toposolidTypeName),
            new RevitSharedCoordinatesSettings(sharedCoordinatesWriteIfAbsent),
            addressAndParcel);
        return true;
    }

    /// <summary>
    /// Decodes the "addressAndParcel" top-level section (SolidGround Issue #31, PH3-4). An absent section
    /// decodes to every field at its documented default (<see cref="AddressGeocoderProvider.Census"/>, every
    /// path/label/disclaimer/radius <see langword="null"/>) rather than being required, so an existing
    /// settings file written before this section (or before "nearbySearchRadiusMeters" specifically,
    /// SolidGround Issue #31 follow-up) existed still loads unchanged. <c>"census"|"geocodio"|"esri"</c>
    /// matches <c>SolidGround.Cli.Commands.GeocodeCommand.ParseProvider</c>'s own existing token spelling
    /// exactly, but is a small, independent switch here, not a shared call -- <c>SolidGround.Revit</c> cannot
    /// reference <c>SolidGround.Cli</c> (AGENTS.md architecture table).
    /// </summary>
    /// <exception cref="AddressAndParcelDecodeException">
    /// "geocoderProvider" is present but not one of the three documented tokens, or "nearbySearchRadiusMeters"
    /// is present but not finite and positive.
    /// </exception>
    private static RevitAddressAndParcelSettings ParseAddressAndParcel(JsonNode? node)
    {
        if (node is null)
        {
            return new RevitAddressAndParcelSettings(AddressGeocoderProvider.Census, null, null, null, null, null, null);
        }

        string? providerToken = (string?)node["geocoderProvider"];
        AddressGeocoderProvider provider = providerToken switch
        {
            null or "census" => AddressGeocoderProvider.Census,
            "geocodio" => AddressGeocoderProvider.Geocodio,
            "esri" => AddressGeocoderProvider.Esri,
            _ => throw new AddressAndParcelDecodeException(
                $"addressAndParcel.geocoderProvider '{providerToken}' is not recognized; it must be one of: census, geocodio, esri."),
        };

        double? nearbySearchRadiusMeters = (double?)node["nearbySearchRadiusMeters"];
        if (nearbySearchRadiusMeters is { } radius && (!double.IsFinite(radius) || radius <= 0d))
        {
            throw new AddressAndParcelDecodeException(
                $"addressAndParcel.nearbySearchRadiusMeters '{radius.ToString(CultureInfo.InvariantCulture)}' must be finite and positive.");
        }

        return new RevitAddressAndParcelSettings(
            provider,
            (string?)node["countyRegistryPath"],
            (string?)node["countyGeoidOverride"],
            (string?)node["localParcelFilePath"],
            (string?)node["localParcelFileSourceLabel"],
            (string?)node["localParcelFileLicenseDisclaimerText"],
            nearbySearchRadiusMeters);
    }

    /// <summary>Raised only by <see cref="ParseAddressAndParcel"/>, caught by <see cref="TryLoad"/> alone -- never surfaces past this file.</summary>
    private sealed class AddressAndParcelDecodeException(string message) : Exception(message);

    /// <summary>
    /// Derives a stable, valid <c>Global\</c> mutex name from <paramref name="settingsPath"/> (orchestrator
    /// decision (c)): a raw path is not itself a valid mutex name (backslashes, length limits), so this
    /// hashes it instead of using it verbatim.
    /// </summary>
    private static string BuildMutexName(string settingsPath)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(settingsPath));
        return "Global\\SolidGround.Revit.Settings." + Convert.ToHexString(hash);
    }
}

/// <summary>Signals the command to open the non-destructive explicit-repair editor for invalid persisted bytes.</summary>
internal sealed class UiSettingsRepairRequiredException(string message) : Exception(message);
