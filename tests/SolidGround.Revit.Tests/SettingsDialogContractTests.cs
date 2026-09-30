using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
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
