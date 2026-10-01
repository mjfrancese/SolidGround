using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using SolidGround.Revit.Dialog;
using SolidGround.Revit.Processing;

namespace SolidGround.Revit.Tests;

public sealed class BuildingFloorReferencePanelTests
{
    [Fact]
    public void ArrangedPanelDisclosesExactlyOneModeAtATimeAndHasNoNestedViewport()
    {
        StaTestHost.Run(() =>
        {
            BuildingFloorReferenceViewModel viewModel = new(
                _ => Task.FromResult<PreparedTerrainSnapshot>(null!),
                () => new TargetProjectLevel(1, "level", "First Floor", 0d),
                () => DistanceDisplayFormat.InternationalFeet,
                outlines: null,
                alreadyCoordinated: false);
            BuildingFloorReferencePanel panel = new(viewModel, WpfTestSupport.CreatePalette());
            Window host = new() { Width = 640d, Height = 480d, Content = panel };
            host.Show();
            try
            {
                host.UpdateLayout();
                RadioButton[] choices = Descendants<RadioButton>(panel).ToArray();
                Assert.Equal(3, choices.Length);
                Assert.Contains(choices, choice => AutomationProperties.GetName(choice) == "Estimate from ground beside the entrance");
                Assert.Contains(Descendants<TextBox>(panel), box => AutomationProperties.GetName(box) == "Measured rise to unfinished first floor");
                Assert.False(Descendants<TextBox>(panel).Single(box => AutomationProperties.GetName(box) == "Measured rise to unfinished first floor").IsVisible);
                Assert.Contains(Descendants<Expander>(panel), expander => Equals(expander.Header, "Enter coordinates"));
                Assert.DoesNotContain(Descendants<ScrollViewer>(panel), viewer => viewer.TemplatedParent is not TextBox);

                RadioButton known = choices.Single(choice => AutomationProperties.GetName(choice) == "Enter a known floor elevation");
                known.IsChecked = true;
                host.UpdateLayout();
                Assert.Contains(Descendants<TextBox>(panel), box => AutomationProperties.GetName(box) == "Known floor elevation" && box.IsVisible);
                Assert.False(Descendants<TextBox>(panel).Single(box => AutomationProperties.GetName(box) == "Measured rise to unfinished first floor").IsVisible);
                Assert.Contains(Descendants<Label>(panel), label => Equals(label.Content, "Known floor elevation (ft)"));
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void CoordinatedProjectExplainsTheBlockAndOffersTheExplicitHistoricalAlternative()
    {
        StaTestHost.Run(() =>
        {
            BuildingFloorReferenceViewModel viewModel = new(
                _ => Task.FromResult<PreparedTerrainSnapshot>(null!),
                () => new TargetProjectLevel(1, "level", "First Floor", 0d),
                () => DistanceDisplayFormat.InternationalFeet,
                outlines: null,
                alreadyCoordinated: true);
            BuildingFloorReferencePanel panel = new(viewModel, WpfTestSupport.CreatePalette());
            Window host = new() { Width = 640d, Height = 480d, Content = panel };
            host.Show();
            try
            {
                host.UpdateLayout();
                Button fallback = Descendants<Button>(panel).Single(button => AutomationProperties.GetName(button) == "Keep original source elevations");
                Assert.True(fallback.IsVisible);
                Assert.NotNull(fallback.Command);
                Assert.True(fallback.IsEnabled);
                IInvokeProvider invoke = Assert.IsAssignableFrom<IInvokeProvider>(new ButtonAutomationPeer(fallback).GetPattern(PatternInterface.Invoke));
                invoke.Invoke();
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.True(viewModel.IsConfirmed);
                Assert.Null(viewModel.Reference);
                Assert.Contains(Descendants<TextBlock>(panel), text => text.Text.Contains("shared coordinates", StringComparison.Ordinal));
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public void InvalidationImmediatelyDisablesAndClearsTheArrangedGroundPreview()
    {
        StaTestHost.Run(() =>
        {
            BuildingFloorReferenceViewModel viewModel = new(
                _ => Task.FromResult(Snapshot()),
                () => new TargetProjectLevel(1, "level", "First Floor", 0d),
                () => DistanceDisplayFormat.InternationalFeet,
                outlines: null,
                alreadyCoordinated: false);
            BuildingFloorReferencePanel panel = new(viewModel, WpfTestSupport.CreatePalette());
            Window host = new() { Width = 640d, Height = 480d, Content = panel };
            host.Show();
            try
            {
                viewModel.BeginEdit();
                viewModel.PrimaryCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                host.UpdateLayout();
                GroundPointPreview preview = Descendants<GroundPointPreview>(panel).Single();
                Assert.True(preview.IsPlacementEnabled);

                viewModel.Invalidate();
                host.UpdateLayout();

                Assert.False(preview.IsPlacementEnabled);
                Assert.Null(preview.SelectedPoint);
                Assert.False(preview.IsSelectionValid);
                Assert.Contains("Load ground preview", preview.SelectionStatus, StringComparison.Ordinal);
            }
            finally
            {
                host.Close();
            }
        });
    }

    private static PreparedTerrainSnapshot Snapshot()
    {
        HorizontalReference reference = new("EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        ElevationGrid grid = new(reference, new VerticalReference("NAVD88", LengthUnit.Meter), new Coordinate2D(-90d, 40d), 1d, 1d, GridAnchorConvention.CellCenter, GridRowOrder.SouthToNorth, new double?[,] { { 100d } });
        return new PreparedTerrainSnapshot(grid, new TerrainProcessingOutcome(null!, null, null), new IdentityTransform(reference), null);
    }

    private sealed class IdentityTransform(HorizontalReference reference) : IHorizontalCoordinateTransform
    {
        public HorizontalTransformationDefinition Definition { get; } = new(reference, reference,
            new CoordinateOperationDefinition("synthetic", "identity"), new CoordinateOperationDefinition("synthetic", "identity"), "Synthetic", "1");
        public Coordinate2D Forward(Coordinate2D source) => source;
        public Coordinate2D Inverse(Coordinate2D target) => target;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching) yield return matching;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (T nested in Descendants<T>(VisualTreeHelper.GetChild(root, index))) yield return nested;
        }
    }
}
