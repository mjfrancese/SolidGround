using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SolidGround.Core.Processing;
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
            root.Remove("level");
            root.Remove("toposolidType");
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

        settings = new RevitSettings(request, new RevitTargetSettings(levelName, toposolidTypeName));
        return true;
    }

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
