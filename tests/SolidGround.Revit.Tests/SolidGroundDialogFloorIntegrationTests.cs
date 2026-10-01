using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SolidGround.Core.Aois;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Hosting;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using SolidGround.Revit.Dialog;
using SolidGround.Revit.Processing;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

public sealed class SolidGroundDialogFloorIntegrationTests
{
    [Fact]
    public async Task ConfirmedFloorReferenceReusesThePreparedSnapshotAndRequiresEstimatedWriteAcknowledgement()
    {
        SolidGroundDialogResult? preparationDraft = null;
        PreparedTerrainSnapshot snapshot = Snapshot();
        SolidGroundDialogViewModel viewModel = new(CreateInputs((draft, _) =>
        {
            preparationDraft = draft;
            return Task.FromResult(snapshot);
        }));
        ConfirmExplicitArea(viewModel);

        BuildingFloorReferenceViewModel floorTask = Assert.IsType<BuildingFloorReferenceViewModel>(viewModel.FloorReferenceTask);
        Assert.False(viewModel.CreateCommand.CanExecute(null));
        viewModel.ChangeFloorReferenceCommand.Execute(null);
        await floorTask.PrimaryCommand.ExecuteAsync(null);

        SolidGroundDialogResult draft = Assert.IsType<SolidGroundDialogResult>(preparationDraft);
        Assert.False(draft.WriteSharedCoordinatesIfAbsent);
        Assert.Null(draft.PreparedTerrain);
        Assert.Null(draft.FloorReference);

        floorTask.SelectPoint(new Coordinate2D(-90d, 40d));
        floorTask.MeasuredRiseText = "0";
        await floorTask.PrimaryCommand.ExecuteAsync(null);

        Assert.True(floorTask.IsConfirmed);
        Assert.True(viewModel.CreateCommand.CanExecute(null));
        viewModel.WriteSharedCoordinatesIfAbsent = true;
        Assert.True(viewModel.RequiresEstimatedSharedCoordinatesAcknowledgement);
        Assert.False(viewModel.CreateCommand.CanExecute(null));
        viewModel.EstimatedSharedCoordinatesAcknowledged = true;
        Assert.True(viewModel.CreateCommand.CanExecute(null));

        viewModel.CreateCommand.Execute(null);
        SolidGroundDialogResult result = Assert.IsType<SolidGroundDialogResult>(viewModel.Result);
        Assert.Same(snapshot, result.PreparedTerrain);
        Assert.NotNull(result.FloorReference);
        Assert.True(result.EstimatedSharedCoordinatesAcknowledged);
    }

    [Fact]
    public void FloorEditorHidesReviewDetailsAndItsActualBindingsResolve()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialogViewModel viewModel = new(CreateInputs((_, _) => Task.FromResult(Snapshot())));
            ConfirmExplicitArea(viewModel);
            SolidGroundDialog dialog = new(viewModel, WpfTestSupport.CreatePalette());
            dialog.Show();
            try
            {
                dialog.UpdateLayout();
                Button start = Descendants<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Set building floor reference");
                Assert.Equal(Visibility.Visible, start.Visibility);
                viewModel.ChangeFloorReferenceCommand.Execute(null);
                WpfTestSupport.DrainDataBindingQueue();
                dialog.UpdateLayout();

                StackPanel reviewDetails = Descendants<StackPanel>(dialog).Single(panel =>
                    BindingOperations.GetBindingExpression(panel, UIElement.VisibilityProperty)?.ParentBinding.Path?.Path == nameof(SolidGroundDialogViewModel.ShowReviewDetails));
                BuildingFloorReferencePanel editor = Descendants<BuildingFloorReferencePanel>(dialog).Single();
                Assert.Equal(Visibility.Collapsed, reviewDetails.Visibility);
                Assert.Equal(Visibility.Visible, editor.Visibility);
                Assert.Equal(Visibility.Visible, Descendants<Button>(dialog).Single(button => AutomationProperties.GetName(button) == "Use this floor reference").Visibility);
                AssertBindingsAreActive(dialog);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public async Task OriginalSourcePlacementRetainsRequestedOutlineContextAndPreparedTerrain()
    {
        OutlineSource outlines = new();
        PreparedTerrainSnapshot snapshot = Snapshot();
        SolidGroundDialogViewModel viewModel = new(CreateInputs((_, _) => Task.FromResult(snapshot), outlines));
        ConfirmExplicitArea(viewModel);
        BuildingFloorReferenceViewModel floorTask = Assert.IsType<BuildingFloorReferenceViewModel>(viewModel.FloorReferenceTask);

        viewModel.ChangeFloorReferenceCommand.Execute(null);
        await floorTask.PrimaryCommand.ExecuteAsync(null);
        await floorTask.LoadOutlinesAsync(TestContext.Current.CancellationToken);
        floorTask.UseOriginalSourceElevationsCommand.Execute(null);

        Assert.True(floorTask.IsConfirmed);
        Assert.Null(floorTask.Reference);
        Assert.True(viewModel.CreateCommand.CanExecute(null));
        viewModel.CreateCommand.Execute(null);

        SolidGroundDialogResult result = Assert.IsType<SolidGroundDialogResult>(viewModel.Result);
        PreparedTerrainSnapshot prepared = Assert.IsType<PreparedTerrainSnapshot>(result.PreparedTerrain);
        Assert.Same(snapshot, prepared);
        Assert.Same(snapshot.Grid, prepared.Grid);
        Assert.Equal(snapshot.Outcome.Payload.Provenance.LocalFrame, prepared.Outcome.Payload.Provenance.LocalFrame);
        Assert.Null(result.FloorReference);
        Assert.Same(outlines.Provenance, result.BuildingOutlineContext);
    }

    [Fact]
    public async Task BackRestoresConfirmedFloorReferenceButAnUpstreamBudgetChangeInvalidatesIt()
    {
        PreparedTerrainSnapshot snapshot = Snapshot();
        SolidGroundDialogViewModel viewModel = new(CreateInputs((_, _) => Task.FromResult(snapshot)));
        ConfirmExplicitArea(viewModel);
        BuildingFloorReferenceViewModel floorTask = await ConfirmEstimatedFloorReferenceAsync(viewModel);

        viewModel.ChangeFloorReferenceCommand.Execute(null);
        Assert.True(floorTask.IsEditing);
        viewModel.BackCommand.Execute(null);
        Assert.True(floorTask.IsConfirmed);
        Assert.Same(snapshot, floorTask.PreparedTerrain);
        Assert.True(viewModel.CreateCommand.CanExecute(null));

        viewModel.PointBudget++;

        Assert.False(floorTask.IsConfirmed);
        Assert.Null(floorTask.PreparedTerrain);
        Assert.False(viewModel.CreateCommand.CanExecute(null));
    }

    private static void ConfirmExplicitArea(SolidGroundDialogViewModel viewModel)
    {
        viewModel.EntryMode = LocationEntryMode.BoundingBox;
        viewModel.WestText = "-90.1";
        viewModel.SouthText = "39.9";
        viewModel.EastText = "-89.9";
        viewModel.NorthText = "40.1";
        viewModel.FindCommand.Execute(null);
        Assert.Equal(SolidGroundDialogStep.Review, viewModel.CurrentStep);
    }

    private static async Task<BuildingFloorReferenceViewModel> ConfirmEstimatedFloorReferenceAsync(SolidGroundDialogViewModel viewModel)
    {
        BuildingFloorReferenceViewModel floorTask = Assert.IsType<BuildingFloorReferenceViewModel>(viewModel.FloorReferenceTask);
        viewModel.ChangeFloorReferenceCommand.Execute(null);
        await floorTask.PrimaryCommand.ExecuteAsync(null);
        floorTask.SelectPoint(new Coordinate2D(-90d, 40d));
        floorTask.MeasuredRiseText = "0";
        await floorTask.PrimaryCommand.ExecuteAsync(null);
        Assert.True(floorTask.IsConfirmed);
        return floorTask;
    }

    private static SolidGroundDialogInputs CreateInputs(
        Func<SolidGroundDialogResult, CancellationToken, Task<PreparedTerrainSnapshot>> prepare,
        IBuildingOutlineSource? outlines = null) => new(
        Geocoder: null!,
        GeocoderProvider: AddressGeocoderProvider.Census,
        ParcelSource: null,
        LevelCandidates: [new NamedElevationCandidate(1L, "Synthetic level", 0d, "level-1")],
        ToposolidTypeCandidates: [new NamedCandidate(2L, "Synthetic toposolid")],
        ConfiguredLevelName: null,
        ConfiguredToposolidTypeName: null,
        PrefilledOutputUnit: LengthUnit.UsSurveyFoot,
        PrefilledPointBudget: 15_000,
        PrefilledWriteSharedCoordinatesIfAbsent: false,
        DocumentAlreadyHasSharedCoordinates: false,
        RevitIniThresholds: new RevitIniToposolidThresholds.Thresholds(null, null),
        RevitIniPath: "synthetic-revit.ini",
        NetworkTimeoutSeconds: 30,
        ConfiguredAreaOfInterest: UiSettingsStore.CreateDefault().Request.AreaOfInterest,
        NearbySearchRadiusMeters: 30d,
        Mode: TerrainAcquisitionMode.Fetch,
        Settings: UiSettingsStore.CreateDefault(),
        PrepareTerrain: prepare,
        BuildingOutlineSource: outlines);

    private static PreparedTerrainSnapshot Snapshot()
    {
        HorizontalReference horizontal = new("EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        ElevationGrid grid = new(horizontal, vertical, new Coordinate2D(-90d, 40d),
            1d, 1d, GridAnchorConvention.CellCenter, GridRowOrder.SouthToNorth, new double?[,] { { 100d } });
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        CoordinateOperationDefinition operation = new("synthetic", "identity");
        HorizontalTransformationDefinition transformation = new(horizontal, projected, operation, operation, "Synthetic", "1");
        LocalCoordinateFrame frame = new(new Coordinate3D(0d, 0d, 0d), projected, vertical, LengthUnit.Meter);
        TerrainProvenance provenance = new(
            TerrainProvenance.CurrentSchemaVersion,
            new ElevationSourceMetadata("Synthetic", "grid"),
            transformation,
            vertical,
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            frame,
            new SimplificationRequest(1),
            1,
            1,
            new ElevationRange(100d, 100d, LengthUnit.Meter));
        TerrainExportPayload payload = new([new LocalTerrainSample(new LocalCoordinate(0d, 0d, 100d))], provenance);
        return new PreparedTerrainSnapshot(grid, new TerrainProcessingOutcome(payload, null, null), new IdentityTransform(horizontal), null, "synthetic");
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T matching)
        {
            yield return matching;
        }

        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (T nested in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
            {
                yield return nested;
            }
        }
    }

    private static void AssertBindingsAreActive(DependencyObject root)
    {
        foreach (DependencyObject element in Descendants<DependencyObject>(root))
        {
            LocalValueEnumerator values = element.GetLocalValueEnumerator();
            while (values.MoveNext())
            {
                if (values.Current.Value is BindingExpressionBase binding)
                {
                    binding.UpdateTarget();
                    Assert.Equal(BindingStatus.Active, binding.Status);
                    Assert.False(binding.HasError, $"{element.GetType().Name}.{values.Current.Property.Name} has a binding error.");
                }
            }
        }
    }

    private sealed class IdentityTransform(HorizontalReference reference) : IHorizontalCoordinateTransform
    {
        public HorizontalTransformationDefinition Definition { get; } = new(
            reference, reference, new CoordinateOperationDefinition("synthetic", "identity"),
            new CoordinateOperationDefinition("synthetic", "identity"), "Synthetic", "1");
        public Coordinate2D Forward(Coordinate2D source) => source;
        public Coordinate2D Inverse(Coordinate2D target) => target;
    }

    private sealed class OutlineSource : IBuildingOutlineSource
    {
        internal BuildingOutlineProvenance Provenance { get; } = new(
            "Synthetic outlines", "2026-10-01", "Synthetic", new Uri("https://example.test/license"),
            "Synthetic license", "Synthetic attribution", new string('A', 64), ["033333333"]);

        public Task<BuildingOutlineAcquisition> GetAsync(Wgs84BoundingBoxAoi bounds, CancellationToken token = default) =>
            Task.FromResult(new BuildingOutlineAcquisition([], Provenance));
    }
}
