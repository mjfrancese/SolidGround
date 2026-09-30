using System.Windows;
using System.Globalization;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using SolidGround.Core.Configuration;
using SolidGround.Core.Sources.CountyParcels;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

public sealed class SettingsDialogBindingTests
{
    [Fact]
    public void StagedCountyRegistryLeavesTheExistingSettingsBytesAndNoOwnedSnapshotOnCancelOrConflict()
    {
        StaTestHost.Run(() =>
        {
            using SettingsSandbox sandbox = new();
            RevitSettings initial = UiSettingsStore.CreateDefault();
            UiSettingsDraft draft = UiSettingsStore.Save(new UiSettingsDraft(initial, SettingsFileVersion.Missing, sandbox.Path), initial);

            SettingsDialog cancelDialog = CreateCountyDialog(draft, initial);
            StageCountyRegistration(cancelDialog);
            string originalBytes = File.ReadAllText(sandbox.Path);
            FindButton(cancelDialog, "Cancel").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(originalBytes, File.ReadAllText(sandbox.Path));
            Assert.Empty(OwnedCountySnapshots(sandbox));

            SettingsDialog conflictDialog = CreateCountyDialog(draft, initial);
            StageCountyRegistration(conflictDialog);
            RevitSettings external = WithPointBudget(initial, 23_456);
            _ = UiSettingsStore.Save(draft, external);
            string externalBytes = File.ReadAllText(sandbox.Path);
            FindButton(conflictDialog, "Save settings").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(externalBytes, File.ReadAllText(sandbox.Path));
            Assert.Empty(OwnedCountySnapshots(sandbox));
        });
    }

    [Fact]
    public void ActualRecoveryControlsStageDefaultsReloadSavedSettingsAndReapplyTheVisibleDraft()
    {
        StaTestHost.Run(() =>
        {
            using SettingsSandbox sandbox = new();
            RevitSettings initial = WithPointBudget(UiSettingsStore.CreateDefault(), 12_345);
            UiSettingsDraft draft = UiSettingsStore.Save(new UiSettingsDraft(initial, SettingsFileVersion.Missing, sandbox.Path), initial);
            List<SettingsRecoveryAction> confirmedActions = [];
            SettingsDialog dialog = SettingsDialog.CreateForTesting(
                draft,
                initial,
                WpfTestSupport.CreatePalette(),
                action => { confirmedActions.Add(action); return true; });
            TextBox pointBudget = Assert.IsType<TextBox>(dialog.AutomationElements["pointBudget"]);
            Button restoreDefaults = Assert.IsType<Button>(dialog.AutomationElements["restoreDefaults"]);
            Button reloadSavedSettings = Assert.IsType<Button>(dialog.AutomationElements["reloadSavedSettings"]);
            Button reapplyDraft = Assert.IsType<Button>(dialog.AutomationElements["reapplyDraft"]);

            string persistedInitial = File.ReadAllText(sandbox.Path);
            pointBudget.Text = "54321";
            restoreDefaults.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(UiSettingsStore.CreateDefault().Request.Simplification.PointBudget.ToString(CultureInfo.InvariantCulture), pointBudget.Text);
            Assert.Equal(persistedInitial, File.ReadAllText(sandbox.Path));

            RevitSettings external = WithPointBudget(initial, 23_456);
            _ = UiSettingsStore.Save(draft, external);
            reloadSavedSettings.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("23456", pointBudget.Text);

            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? refreshed, out string? error), error);
            RevitSettings newerExternal = WithPointBudget(external, 34_567);
            _ = UiSettingsStore.Save(refreshed!, newerExternal);
            pointBudget.Text = "45678";
            reapplyDraft.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(UiSettingsStore.TryLoad(sandbox.Path, out UiSettingsDraft? applied, out error), error);
            Assert.Equal(45_678, applied!.Settings.Request.Simplification.PointBudget);
            Assert.Equal([SettingsRecoveryAction.RestoreDefaults, SettingsRecoveryAction.ReapplyDraft], confirmedActions);
        });
    }

    [Fact]
    public void EveryNamedActualSettingsControlHasAnActiveWpfBinding()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings current = UiSettingsStore.CreateDefault();
            UiSettingsDraft draft = new(current, SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), "solidground-wpf-test-settings.json"));
            SettingsDialog dialog = SettingsDialog.CreateForTesting(draft, current, WpfTestSupport.CreatePalette());
            string[] expectedNames =
            [
                "pointBudget", "terrainExtension", "outputUnit", "distanceDisplayFormat",
                "acquisitionMode", "rasterPath", "projectionPath", "sourceSidecarPath",
                "localParcelPath", "localParcelLabel", "localParcelLicense", "countyRegistryPath", "countyAuthorization", "geocoderProvider",
                "countyName", "countyGeoid", "countyServiceUrl", "countyAttribution", "countyLicense",
                "countyLayer", "countyParcelIdField", "countySitusAddressField", "countyLegalDescriptionField",
                "useCountySource",
                "openTopographyKey", "geocodioKey", "esriKey", "exportDirectory", "exportBaseName",
                "networkTimeout", "nearbyRadius", "coverageFloor", "simplificationMethod",
                "restoreDefaults", "reloadSavedSettings", "reapplyDraft",
            ];
            Assert.Equal(expectedNames.OrderBy(name => name), dialog.AutomationElements.Keys.OrderBy(name => name));

            foreach ((string name, FrameworkElement element) in dialog.AutomationElements)
            {
                Assert.Equal(name, element.Name);
                Assert.Equal(name, AutomationProperties.GetAutomationId(element));
                string sourcePath = element switch
                {
                    TextBox => nameof(TextBox.Text),
                    ComboBox => nameof(ComboBox.SelectedItem),
                    PasswordBox => nameof(PasswordBox.Password),
                    CheckBox => nameof(CheckBox.IsChecked),
                    Button => nameof(Button.IsEnabled),
                    _ => throw new Xunit.Sdk.XunitException($"Unexpected Settings control type: {element.GetType().FullName}"),
                };
                BindingOperations.SetBinding(element, FrameworkElement.TagProperty, new Binding(sourcePath) { Source = element });
            }

            WpfTestSupport.DrainDataBindingQueue();

            foreach ((string name, FrameworkElement element) in dialog.AutomationElements)
            {
                BindingExpression binding = Assert.IsType<BindingExpression>(
                    BindingOperations.GetBindingExpression(element, FrameworkElement.TagProperty));
                Assert.True(binding.Status == BindingStatus.Active && !binding.HasError, $"{name} status was {binding.Status}.");
            }
        });
    }

    private static RevitSettings WithPointBudget(RevitSettings settings, int pointBudget) =>
        settings with { Request = settings.Request with { Simplification = settings.Request.Simplification with { PointBudget = pointBudget } } };

    private static SettingsDialog CreateCountyDialog(UiSettingsDraft draft, RevitSettings current) =>
        SettingsDialog.CreateForTesting(draft, current, WpfTestSupport.CreatePalette(), _ => true);

    private static void StageCountyRegistration(SettingsDialog dialog)
    {
        dialog.SetCountyMetadataForTesting(new CountyParcelServiceMetadata(
            new Uri("https://synthetic.invalid/arcgis/rest/services/parcels/FeatureServer"),
            [new CountyParcelServiceLayerMetadata(0, "Synthetic parcels", new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "parcel_id", "situs_address" })]));
        Assert.IsType<TextBox>(dialog.AutomationElements["countyName"]).Text = "Synthetic county";
        Assert.IsType<TextBox>(dialog.AutomationElements["countyGeoid"]).Text = "12345";
        Assert.IsType<TextBox>(dialog.AutomationElements["countyAttribution"]).Text = "Synthetic attribution";
        Assert.IsType<TextBox>(dialog.AutomationElements["countyLicense"]).Text = "Synthetic license";
        Assert.IsType<CheckBox>(dialog.AutomationElements["countyAuthorization"]).IsChecked = true;
        Assert.IsType<Button>(dialog.AutomationElements["useCountySource"]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }

    private static Button FindButton(DependencyObject root, string content) =>
        FindAllLogical(root)
            .OfType<Button>()
            .Single(button => string.Equals(button.Content as string, content, StringComparison.Ordinal));

    private static IEnumerable<DependencyObject> FindAllLogical(DependencyObject root)
    {
        yield return root;
        foreach (object child in LogicalTreeHelper.GetChildren(root))
        {
            if (child is DependencyObject childObject)
            {
                foreach (DependencyObject nested in FindAllLogical(childObject)) yield return nested;
            }
        }
    }

    private static IEnumerable<string> OwnedCountySnapshots(SettingsSandbox sandbox) =>
        Directory.EnumerateFiles(sandbox.DirectoryPath, "county-parcel-registry.*.json", SearchOption.TopDirectoryOnly);

    private sealed class SettingsSandbox : IDisposable
    {
        internal SettingsSandbox()
        {
            DirectoryPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SolidGround-WpfRuntimeTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(DirectoryPath);
            Path = System.IO.Path.Combine(DirectoryPath, "settings.json");
        }

        internal string DirectoryPath { get; }

        internal string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
            }
        }
    }
}
