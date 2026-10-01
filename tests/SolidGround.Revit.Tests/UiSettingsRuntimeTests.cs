using System.Text;
using System.Text.Json.Nodes;
using SolidGround.Core.Configuration;
using SolidGround.Core.Processing;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

[Collection(SessionApiKeyOverrideTestGroup.Name)]
public sealed class UiSettingsRuntimeTests
{
    [Fact]
    public void NewSettingsCenterTheLegalParcelButLoadingRetainsAnExplicitPlacementChoice()
    {
        RevitSettings defaults = UiSettingsStore.CreateDefault();
        Assert.Equal(LocalOriginKind.AreaCentroid, defaults.Request.LocalOrigin.Kind);
        using SettingsSandbox sandbox = new();
        LocalOriginRequest explicitOrigin = new(LocalOriginKind.Explicit, 123d, 456d, 789d);
        RevitSettings chosen = defaults with { Request = defaults.Request with { LocalOrigin = explicitOrigin } };
        _ = UiSettingsStore.Save(new UiSettingsDraft(chosen, SettingsFileVersion.Missing, sandbox.Path), chosen);
        Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? reloaded, out _));
        Assert.Equal(explicitOrigin, reloaded!.Settings.Request.LocalOrigin);
    }

    [Fact]
    public void TryLoadRejectsStrictMalformedInputsWithoutChangingTheOriginalBytes()
    {
        using SettingsSandbox sandbox = new();
        byte[] valid = SeedSettings(sandbox);
        foreach ((string label, byte[] bytes) in InvalidInputs(valid))
        {
            File.WriteAllBytes(sandbox.Path, bytes);
            Exception? exception = Record.Exception(() => Assert.False(UiSettingsStore.TryLoad(sandbox.Path, out _, out _), label));
            Assert.Null(exception);
            Assert.False(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? draft, out string? error), label);
            Assert.Null(draft);
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(bytes, File.ReadAllBytes(sandbox.Path));
        }
    }

    [Fact]
    public void SaveRefusesChangedBytesAndPreservesTheDraftTarget()
    {
        using SettingsSandbox sandbox = new();
        byte[] initial = SeedSettings(sandbox);
        Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? draft, out _));
        Assert.NotNull(draft);
        byte[] changed = initial.Concat([(byte)' ']).ToArray();
        File.WriteAllBytes(sandbox.Path, changed);

        Assert.Throws<SettingsFileConflictException>(() => UiSettingsStore.Save(draft!, draft!.Settings));
        Assert.Equal(changed, File.ReadAllBytes(sandbox.Path));
        Assert.Equal(sandbox.Path, draft!.Path);
    }

    [Fact]
    public void SettingsSerializationDoesNotPersistSharedCoordinateOrSessionKeyState()
    {
        using SettingsSandbox sandbox = new();
        RevitSettings settings = UiSettingsStore.CreateDefault() with { SharedCoordinates = new RevitSharedCoordinatesSettings(true) };
        UiSettingsDraft draft = new(settings, SettingsFileVersion.Missing, sandbox.Path);
        const string syntheticSessionToken = "synthetic-session-token";
        SessionApiKeyOverrides.ClearAll();
        try
        {
            SessionApiKeyOverrides.UseOpenTopography(syntheticSessionToken);
            Assert.True(SessionApiKeyOverrides.HasOpenTopography);
            UiSettingsStore.Save(draft, settings);
            byte[] bytes = File.ReadAllBytes(sandbox.Path);
            Assert.DoesNotContain(Encoding.UTF8.GetString(bytes), syntheticSessionToken, StringComparison.Ordinal);

            SessionApiKeyOverrides.ClearAll();
            Assert.False(SessionApiKeyOverrides.HasOpenTopography);
            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? loaded, out _));
            Assert.NotNull(loaded);
            Assert.False(loaded!.Settings.SharedCoordinates.WriteIfAbsent);
            Assert.False(SessionApiKeyOverrides.HasOpenTopography);
        }
        finally
        {
            SessionApiKeyOverrides.ClearAll();
        }
    }

    [Fact]
    public void SessionProvidersPreferSyntheticSessionValuesThenClearWithoutExposingThem()
    {
        SessionApiKeyOverrides.ClearAll();
        try
        {
            SessionApiKeyOverrides.UseOpenTopography("synthetic-open-topography-token");
            SessionApiKeyOverrides.UseGeocodio("synthetic-geocodio-token");
            SessionApiKeyOverrides.UseEsri("synthetic-esri-token");

            Assert.Equal("[REDACTED]", SessionApiKeyOverrides.OpenTopographyProvider().GetApiKey()!.ToString());
            Assert.Equal("[REDACTED]", SessionApiKeyOverrides.GeocodioProvider().GetApiKey()!.ToString());
            Assert.Equal("[REDACTED]", SessionApiKeyOverrides.EsriProvider().GetApiKey()!.ToString());
            Assert.True(SessionApiKeyOverrides.HasOpenTopography);
            Assert.True(SessionApiKeyOverrides.HasGeocodio);
            Assert.True(SessionApiKeyOverrides.HasEsri);

            SessionApiKeyOverrides.ClearAll();
            Assert.False(SessionApiKeyOverrides.HasOpenTopography);
            Assert.False(SessionApiKeyOverrides.HasGeocodio);
            Assert.False(SessionApiKeyOverrides.HasEsri);
        }
        finally
        {
            SessionApiKeyOverrides.ClearAll();
        }
    }

    private static byte[] SeedSettings(SettingsSandbox sandbox)
    {
        RevitSettings settings = UiSettingsStore.CreateDefault();
        UiSettingsStore.Save(new UiSettingsDraft(settings, SettingsFileVersion.Missing, sandbox.Path), settings);
        return File.ReadAllBytes(sandbox.Path);
    }

    private static IEnumerable<(string Label, byte[] Bytes)> InvalidInputs(byte[] valid)
    {
        yield return ("unknown field", Mutate(valid, root => root["unexpected"] = true));
        yield return ("missing field", Mutate(valid, root => root.Remove("request")));
        yield return ("future version", Mutate(valid, root => root["schemaVersion"] = UiSettingsDocument.CurrentSchemaVersion + 1));
        yield return ("wrong typed value", Mutate(valid, root => root["schemaVersion"] = "one"));
        string duplicate = Encoding.UTF8.GetString(valid).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1", StringComparison.Ordinal);
        yield return ("duplicate JSON field", Encoding.UTF8.GetBytes(duplicate));
        yield return ("malformed UTF-8", [0xFF, 0xFE, (byte)'{']);
    }

    private static byte[] Mutate(byte[] valid, Action<JsonObject> mutation)
    {
        JsonObject root = JsonNode.Parse(valid)!.AsObject();
        mutation(root);
        return Encoding.UTF8.GetBytes(root.ToJsonString());
    }

    private sealed class SettingsSandbox : IDisposable
    {
        private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SolidGround.Revit.Tests", Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(directory, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
