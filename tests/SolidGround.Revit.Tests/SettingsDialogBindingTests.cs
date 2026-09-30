using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using SolidGround.Core.Configuration;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

public sealed class SettingsDialogBindingTests
{
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
                "openTopographyKey", "geocodioKey", "esriKey", "exportDirectory", "exportBaseName",
                "networkTimeout", "nearbyRadius", "coverageFloor", "simplificationMethod",
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
}
