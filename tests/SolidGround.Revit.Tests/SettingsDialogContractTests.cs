using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.Json.Nodes;
using SolidGround.Core.Configuration;
using SolidGround.Core.Processing;
using SolidGround.Core.Units;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

public sealed class SettingsDialogContractTests
{
    [Fact]
    public void ActualSettingsControlsExposeReadableAutomationNamesHelpAndAssertiveErrorStatus()
    {
        StaTestHost.Run(() =>
        {
            SettingsDialog dialog = CreateDialog(UiSettingsStore.CreateDefault(), SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"));
            foreach ((string id, FrameworkElement element) in dialog.AutomationElements)
            {
                AutomationPeer peer = UIElementAutomationPeer.CreatePeerForElement((UIElement)element)!;
                Assert.False(string.IsNullOrWhiteSpace(peer.GetName()));
                Assert.NotEqual(id, peer.GetName());
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(element)));
            }

            TextBlock error = FindLogical<TextBlock>(dialog).Single(element => AutomationProperties.GetName(element) == "Settings error status");
            Assert.Equal(AutomationLiveSetting.Assertive, AutomationProperties.GetLiveSetting(error));
        });
    }

    [Fact]
    public void CorruptSettingsRequireStartNewBeforeActualSaveAndPreserveBytesUntilThen()
    {
        StaTestHost.Run(() =>
        {
            using Sandbox sandbox = new();
            byte[] corrupt = "{ not-json"u8.ToArray();
            File.WriteAllBytes(sandbox.Path, corrupt);
            RevitSettings defaults = UiSettingsStore.CreateDefault();
            SettingsDialog dialog = SettingsDialog.CreateForTesting(new UiSettingsDraft(defaults, AtomicSettingsFile.Read(sandbox.Path).Version, sandbox.Path), defaults, WpfTestSupport.CreatePalette(), _ => true, savingBlockedUntilStartNew: true, initialRecoveryError: "bad schemaVersion");
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(corrupt, File.ReadAllBytes(sandbox.Path));
            Assert.Null(dialog.Result);
            FindButton(dialog, "Start new settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(corrupt, File.ReadAllBytes(sandbox.Path));
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? loaded, out string? error), error);
            Assert.NotNull(loaded);
        });
    }

    [Fact]
    public void FutureSettingsVersionCannotSaveAndCancelLeavesItsBytesUntouched()
    {
        StaTestHost.Run(() =>
        {
            using Sandbox sandbox = new();
            RevitSettings defaults = UiSettingsStore.CreateDefault();
            _ = UiSettingsStore.Save(new UiSettingsDraft(defaults, SettingsFileVersion.Missing, sandbox.Path), defaults);
            JsonObject future = JsonNode.Parse(File.ReadAllText(sandbox.Path))!.AsObject();
            future["schemaVersion"] = 999;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(future.ToJsonString());
            File.WriteAllBytes(sandbox.Path, bytes);
            SettingsDialog dialog = SettingsDialog.CreateForTesting(new UiSettingsDraft(defaults, AtomicSettingsFile.Read(sandbox.Path).Version, sandbox.Path), defaults, WpfTestSupport.CreatePalette(), _ => true, savingBlockedUntilStartNew: true, initialRecoveryError: "future schemaVersion");
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindButton(dialog, "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(bytes, File.ReadAllBytes(sandbox.Path));
        });
    }

    [Fact]
    public void ReloadSavedSettingsClearsRepairBlockAndPermitsActualSave()
    {
        StaTestHost.Run(() =>
        {
            using Sandbox sandbox = new();
            RevitSettings settings = UiSettingsStore.CreateDefault();
            UiSettingsDraft draft = UiSettingsStore.Save(new UiSettingsDraft(settings, SettingsFileVersion.Missing, sandbox.Path), settings);
            SettingsDialog dialog = SettingsDialog.CreateForTesting(draft, settings, WpfTestSupport.CreatePalette(), _ => true, savingBlockedUntilStartNew: true, initialRecoveryError: "repair required");
            ((TextBox)dialog.AutomationElements["pointBudget"]).Text = "12345";
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(15_000, UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? unchanged, out _) ? unchanged!.Settings.Request.Simplification.PointBudget : -1);
            FindButton(dialog, "Reload saved").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            ((TextBox)dialog.AutomationElements["pointBudget"]).Text = "12345";
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? saved, out string? error), error);
            Assert.Equal(12_345, saved!.Settings.Request.Simplification.PointBudget);
        });
    }

    [Fact]
    public void LocalParcelProvenanceErrorsKeepBytesAndFocusTheMissingActualControl()
    {
        StaTestHost.Run(() =>
        {
            using Sandbox sandbox = new();
            RevitSettings settings = UiSettingsStore.CreateDefault();
            UiSettingsDraft draft = UiSettingsStore.Save(new UiSettingsDraft(settings, SettingsFileVersion.Missing, sandbox.Path), settings);
            SettingsDialog dialog = CreateDialog(settings, draft.ExpectedVersion, sandbox.Path);
            dialog.Show();
            try
            {
                TextBox path = (TextBox)dialog.AutomationElements["localParcelPath"];
                TextBox label = (TextBox)dialog.AutomationElements["localParcelLabel"];
                TextBox license = (TextBox)dialog.AutomationElements["localParcelLicense"];
                path.Text = "synthetic.geojson";
                string before = File.ReadAllText(sandbox.Path);
                FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(before, File.ReadAllText(sandbox.Path));
                Assert.Same(label, Keyboard.FocusedElement);
                label.Text = "Synthetic parcel source";
                FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(license, Keyboard.FocusedElement);
                Assert.Equal(before, File.ReadAllText(sandbox.Path));
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void RestoreDefaultsPreservesProcessOriginAndParcelProvenanceWhenActualSaveRoundTrips()
    {
        StaTestHost.Run(() =>
        {
            using Sandbox sandbox = new();
            RevitSettings baseline = UiSettingsStore.CreateDefault();
            RevitSettings configured = baseline with
            {
                Request = baseline.Request with
                {
                    Mode = TerrainAcquisitionMode.Process,
                    LocalOrigin = new LocalOriginRequest(LocalOriginKind.Explicit, 1.23456789d, -2.34567891d, 3.45678912d),
                    Process = new ProcessInputSettings
                    {
                        Asc = "synthetic.asc",
                        SourceName = "Synthetic source",
                        Dataset = "Synthetic dataset",
                        VerticalDatum = "NAVD88",
                        VerticalUnit = LengthUnit.Meter,
                        Geoid = "Synthetic geoid",
                        CollectionStart = "2026-01-01",
                        CollectionEnd = "2026-01-02",
                        QualityLevel = "QL2",
                    },
                },
                AddressAndParcel = baseline.AddressAndParcel with
                {
                    LocalParcelFilePath = "synthetic.geojson",
                    LocalParcelFileSourceLabel = "Synthetic parcel source",
                    LocalParcelFileLicenseDisclaimerText = "Synthetic license",
                },
            };
            UiSettingsDraft draft = UiSettingsStore.Save(new UiSettingsDraft(configured, SettingsFileVersion.Missing, sandbox.Path), configured);
            SettingsDialog dialog = CreateDialog(configured, draft.ExpectedVersion, sandbox.Path);
            FindButton(dialog, "Restore defaults").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? loaded, out string? error), error);
            Assert.Equal(configured.Request.Mode, loaded!.Settings.Request.Mode);
            Assert.Equal(configured.Request.LocalOrigin, loaded.Settings.Request.LocalOrigin);
            Assert.Equal(configured.Request.Process, loaded.Settings.Request.Process);
            Assert.Equal(configured.AddressAndParcel.LocalParcelFilePath, loaded.Settings.AddressAndParcel.LocalParcelFilePath);
            Assert.Equal(configured.AddressAndParcel.LocalParcelFileLicenseDisclaimerText, loaded.Settings.AddressAndParcel.LocalParcelFileLicenseDisclaimerText);
        });
    }

    [Fact]
    public void ActualProcessMetadataAndExplicitOriginEditsRoundTripThroughSettingsJson()
    {
        StaTestHost.Run(() =>
        {
            using Sandbox sandbox = new();
            RevitSettings settings = UiSettingsStore.CreateDefault();
            UiSettingsDraft draft = UiSettingsStore.Save(new UiSettingsDraft(settings, SettingsFileVersion.Missing, sandbox.Path), settings);
            SettingsDialog dialog = CreateDialog(settings, draft.ExpectedVersion, sandbox.Path);
            ((ComboBox)dialog.AutomationElements["acquisitionMode"]).SelectedItem = TerrainAcquisitionMode.Process;
            ((TextBox)dialog.AutomationElements["rasterPath"]).Text = "synthetic.asc";
            ((TextBox)dialog.AutomationElements["processSourceName"]).Text = "Edited synthetic source";
            ((TextBox)dialog.AutomationElements["processDataset"]).Text = "Edited dataset";
            ((TextBox)dialog.AutomationElements["processVerticalDatum"]).Text = "NAVD88";
            ((ComboBox)dialog.AutomationElements["processVerticalUnit"]).SelectedItem = LengthUnit.Meter;
            ((TextBox)dialog.AutomationElements["processGeoid"]).Text = "Edited geoid";
            ((TextBox)dialog.AutomationElements["processCollectionStart"]).Text = "2026-02-01";
            ((TextBox)dialog.AutomationElements["processCollectionEnd"]).Text = "2026-02-02";
            ((TextBox)dialog.AutomationElements["processQualityLevel"]).Text = "QL1";
            ((ComboBox)dialog.AutomationElements["originKind"]).SelectedItem = LocalOriginKind.Explicit;
            ((TextBox)dialog.AutomationElements["originX"]).Text = "1.234567890123";
            ((TextBox)dialog.AutomationElements["originY"]).Text = "-2.345678901234";
            ((TextBox)dialog.AutomationElements["originZ"]).Text = "3.456789012345";
            FindButton(dialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? loaded, out string? error), error);
            Assert.Equal("Edited synthetic source", loaded!.Settings.Request.Process!.SourceName);
            Assert.Equal("Edited dataset", loaded.Settings.Request.Process.Dataset);
            Assert.Equal(LengthUnit.Meter, loaded.Settings.Request.Process.VerticalUnit);
            Assert.Equal(new LocalOriginRequest(LocalOriginKind.Explicit, 1.234567890123d, -2.345678901234d, 3.456789012345d), loaded.Settings.Request.LocalOrigin);
        });
    }

    private static SettingsDialog CreateDialog(RevitSettings settings, SettingsFileVersion version, string path) =>
        SettingsDialog.CreateForTesting(new UiSettingsDraft(settings, version, path), settings, WpfTestSupport.CreatePalette(), _ => true);

    private static Button FindButton(DependencyObject root, string content) => FindLogical<Button>(root).Single(button => button.Content as string == content);

    private static IEnumerable<T> FindLogical<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching) yield return matching;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject childObject)
            {
                foreach (T nested in FindLogical<T>(childObject)) yield return nested;
            }
        }
    }

    private sealed class Sandbox : IDisposable
    {
        internal Sandbox()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SolidGround-WpfContractTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(DirectoryPath, "settings.json");
        }

        internal string DirectoryPath { get; }
        internal string Path { get; }
        public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    }
}
