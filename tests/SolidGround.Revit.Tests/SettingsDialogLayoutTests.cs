using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Globalization;
using SolidGround.Core.Processing;
using SolidGround.Core.Configuration;
using SolidGround.Core.Sources;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

[Collection(SessionApiKeyOverrideTestGroup.Name)]
public sealed class SettingsDialogLayoutTests
{
    private static readonly double[] ResizeHeights = [480d, 650d, 520d, 580d];

    [Fact]
    public void TerrainExtensionIsProminentAndOptionalInTheOperatorDisplayUnits()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings settings = UiSettingsStore.CreateDefault();
            SettingsDialog dialog = SettingsDialog.CreateForTesting(
                new UiSettingsDraft(settings, SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
                settings,
                WpfTestSupport.CreatePalette());

            TabControl pages = FindLogical<TabControl>(dialog).Single();
            pages.SelectedIndex = 0;
            ScrollViewer terrainViewport = Assert.IsType<ScrollViewer>(Assert.IsType<TabItem>(pages.SelectedItem).Content);
            List<string> terrainText = FindLogical<TextBlock>(terrainViewport)
                .Select(block => block.Text)
                .ToList();
            int extensionLabel = terrainText.IndexOf("Terrain beyond property line (optional; selected display units)");
            int pointBudgetLabel = terrainText.IndexOf("Maximum terrain points");

            Assert.True(extensionLabel >= 0 && extensionLabel < pointBudgetLabel);
            Assert.Contains(terrainText, text => text.StartsWith("Optional. The default is 0", StringComparison.Ordinal));
            Assert.Equal("Adds terrain outside the parcel boundary and retains its exact value in metres.",
                AutomationProperties.GetHelpText(dialog.AutomationElements["terrainExtension"]));
        });
    }

    [Fact]
    public void OptionalSourcesAreCollapsedUntilTheOperatorNeedsThem()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings settings = UiSettingsStore.CreateDefault();
            SettingsDialog dialog = SettingsDialog.CreateForTesting(
                new UiSettingsDraft(settings, SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
                settings,
                WpfTestSupport.CreatePalette());

            TabControl pages = FindLogical<TabControl>(dialog).Single();
            pages.SelectedIndex = 1;
            ScrollViewer sourcesViewport = Assert.IsType<ScrollViewer>(Assert.IsType<TabItem>(pages.SelectedItem).Content);
            Expander[] disclosures = FindLogical<Expander>(sourcesViewport).ToArray();

            Assert.Equal(3, disclosures.Length);
            Assert.All(disclosures, disclosure => Assert.False(disclosure.IsExpanded));
            Assert.Equal("Use a local elevation raster (required only for Local raster mode)", ((TextBlock)disclosures[0].Header).Text);
            Assert.Equal("Add a local parcel file (optional)", ((TextBlock)disclosures[1].Header).Text);
            Assert.Equal("Register an authorized county parcel service (optional)", ((TextBlock)disclosures[2].Header).Text);
        });
    }

    [Fact]
    public void OnlyTheSelectedKeyedGeocoderShowsItsSessionPasswordField()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings settings = UiSettingsStore.CreateDefault();
            SettingsDialog dialog = SettingsDialog.CreateForTesting(
                new UiSettingsDraft(settings, SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
                settings,
                WpfTestSupport.CreatePalette());
            dialog.Show();
            try
            {
                TabControl pages = FindLogical<TabControl>(dialog).Single();
                pages.SelectedIndex = 1;
                dialog.UpdateLayout();
                ComboBox provider = Assert.IsType<ComboBox>(dialog.AutomationElements["geocoderProvider"]);
                PasswordBox geocodio = Assert.IsType<PasswordBox>(dialog.AutomationElements["geocodioKey"]);
                PasswordBox esri = Assert.IsType<PasswordBox>(dialog.AutomationElements["esriKey"]);

                Assert.False(geocodio.IsVisible);
                Assert.False(esri.IsVisible);
                provider.SelectedItem = AddressGeocoderProvider.Geocodio;
                dialog.UpdateLayout();
                Assert.True(geocodio.IsVisible);
                Assert.False(esri.IsVisible);
                provider.SelectedItem = AddressGeocoderProvider.Esri;
                dialog.UpdateLayout();
                Assert.False(geocodio.IsVisible);
                Assert.True(esri.IsVisible);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void ActualGeocoderKeyStatusRefreshesAfterUseClearAndProviderChangesWithoutShowingTheKey()
    {
        StaTestHost.Run(() =>
        {
            SessionApiKeyOverrides.ClearAll();
            RevitSettings settings = UiSettingsStore.CreateDefault();
            SettingsDialog dialog = SettingsDialog.CreateForTesting(
                new UiSettingsDraft(settings, SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
                settings,
                WpfTestSupport.CreatePalette());
            dialog.Show();
            try
            {
                TabControl pages = FindLogical<TabControl>(dialog).Single();
                pages.SelectedIndex = 1;
                ComboBox provider = Assert.IsType<ComboBox>(dialog.AutomationElements["geocoderProvider"]);
                PasswordBox geocodio = Assert.IsType<PasswordBox>(dialog.AutomationElements["geocodioKey"]);
                provider.SelectedItem = AddressGeocoderProvider.Geocodio;
                dialog.UpdateLayout();

                StackPanel geocodioSection = FindLogical<StackPanel>(dialog).Single(panel => LogicalTreeHelper.GetChildren(panel).OfType<PasswordBox>().Contains(geocodio));
                Button use = FindLogical<Button>(geocodioSection).Single(button => button.Content as string == "Use for this Revit session");
                Button clear = FindLogical<Button>(geocodioSection).Single(button => button.Content as string == "Clear session key");
                geocodio.Password = "synthetic-geocodio-token";
                use.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(string.Empty, geocodio.Password);
                Assert.Contains("session key is active", FindStatus(dialog, "Geocodio"));
                Assert.DoesNotContain("synthetic-geocodio-token", FindStatus(dialog, "Geocodio"));

                clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string cleared = FindStatus(dialog, "Geocodio");
                Assert.DoesNotContain("session key is active", cleared);
                Assert.DoesNotContain("synthetic-geocodio-token", cleared);
                Assert.True(cleared.StartsWith("No Geocodio key is available", StringComparison.Ordinal)
                    || cleared.StartsWith("A Geocodio environment key is available", StringComparison.Ordinal));

                provider.SelectedItem = AddressGeocoderProvider.Census;
                dialog.UpdateLayout();
                Assert.Equal("Census is keyless; no geocoder key is needed.", FindStatus(dialog, "Census"));
            }
            finally
            {
                dialog.Close();
                SessionApiKeyOverrides.ClearAll();
            }
        });
    }

    [Fact]
    public void SourcesPageUsesTheBoundedViewportAndKeepsSaveAndCancelVisible()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings settings = UiSettingsStore.CreateDefault();
            SettingsDialog dialog = SettingsDialog.CreateForTesting(
                new UiSettingsDraft(settings, SettingsFileVersion.Missing, Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")),
                settings,
                WpfTestSupport.CreatePalette());
            dialog.Width = 640d;
            dialog.Height = 480d;
            dialog.Show();
            try
            {
                TabControl pages = FindLogical<TabControl>(dialog).Single();
                pages.SelectedIndex = 1;
                ScrollViewer sourceViewport = Assert.IsType<ScrollViewer>(Assert.IsType<TabItem>(pages.SelectedItem).Content);
                Grid shell = Assert.IsType<Grid>(dialog.Content);
                Button cancel = FindButton(dialog, "Cancel");
                Button save = FindButton(dialog, "Save settings");
                foreach (double height in ResizeHeights)
                {
                    dialog.Height = height;
                    dialog.UpdateLayout();
                    Assert.True(sourceViewport.ViewportHeight > 0d && sourceViewport.ScrollableHeight > 0d,
                        $"Sources content must scroll inside the finite tab viewport at height {height}. Extent={sourceViewport.ExtentHeight}, viewport={sourceViewport.ViewportHeight}.");
                    sourceViewport.ScrollToBottom();
                    dialog.UpdateLayout();
                    Assert.True(sourceViewport.VerticalOffset > 0d);
                    AssertFooterIsInsideShell(cancel, shell);
                    AssertFooterIsInsideShell(save, shell);
                }
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static Button FindButton(DependencyObject root, string content) =>
        FindLogical<Button>(root).Single(button => button.Content as string == content);

    private static string FindStatus(DependencyObject root, string provider) =>
        FindLogical<TextBlock>(root)
            .Select(block => block.Text)
            .Single(text => text.StartsWith($"A {provider}", StringComparison.Ordinal)
                || text.StartsWith($"No {provider}", StringComparison.Ordinal)
                || text.StartsWith($"{provider} is keyless", StringComparison.Ordinal));

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

    private static void AssertFooterIsInsideShell(Button footerControl, FrameworkElement shell)
    {
        Rect bounds = footerControl.TransformToAncestor(shell).TransformBounds(new Rect(footerControl.RenderSize));
        Assert.True(bounds.Top >= 0d && bounds.Bottom <= shell.ActualHeight,
            $"{footerControl.Content} must remain inside the dialog shell. Bounds={bounds}; shell height={shell.ActualHeight}.");
    }
}
