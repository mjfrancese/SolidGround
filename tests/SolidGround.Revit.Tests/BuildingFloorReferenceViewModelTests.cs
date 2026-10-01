using SolidGround.Core.Geometry;
using SolidGround.Core.Exports;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Provenance;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Revit.Dialog;
using SolidGround.Revit.Processing;

namespace SolidGround.Revit.Tests;

public sealed class BuildingFloorReferenceViewModelTests
{
    [Fact]
    public async Task BlankMeasuredRiseCannotBecomeZeroButExplicitZeroConfirmsEstimatedReference()
    {
        BuildingFloorReferenceViewModel viewModel = CreateViewModel();
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.SelectPoint(new Coordinate2D(-90d, 40d));

        await viewModel.PrimaryCommand.ExecuteAsync(null);
        Assert.Null(viewModel.Reference);
        Assert.True(viewModel.IsEditing);
        Assert.Contains("Enter the measured rise", viewModel.ValidationMessage, StringComparison.Ordinal);

        viewModel.MeasuredRiseText = "0";
        await viewModel.PrimaryCommand.ExecuteAsync(null);

        BuildingFloorReference reference = Assert.IsType<BuildingFloorReference>(viewModel.Reference);
        Assert.Equal(FloorReferenceMode.EstimatedGradeRise, reference.Mode);
        Assert.Equal(0d, reference.Rise!.Magnitude);
        Assert.False(reference.Rise.BelowGrade);
        Assert.True(viewModel.IsConfirmed);
    }

    [Fact]
    public async Task KnownSignedHeightConvertsTheDisplayedUnitToTheDeclaredSourceUnit()
    {
        BuildingFloorReferenceViewModel viewModel = CreateViewModel();
        viewModel.BeginEdit();
        viewModel.Mode = FloorReferenceMode.KnownElevation;
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.KnownElevationText = "-12";
        viewModel.KnownSourceDescription = "Survey benchmark field book";
        viewModel.KnownDatumAcknowledged = true;

        await viewModel.PrimaryCommand.ExecuteAsync(null);

        BuildingFloorReference reference = Assert.IsType<BuildingFloorReference>(viewModel.Reference);
        Assert.Equal(FloorReferenceMode.KnownElevation, reference.Mode);
        Assert.Equal(-3.6576d, reference.PhysicalFloorElevation!.Value, 8);
        Assert.Equal("Survey benchmark field book", reference.KnownSourceDescription);
    }

    [Fact]
    public async Task CancellingANonCooperatingPreparationPreventsItsLateSnapshotFromApplying()
    {
        TaskCompletionSource<PreparedTerrainSnapshot> delayed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        BuildingFloorReferenceViewModel viewModel = new(
            _ => { calls++; return delayed.Task; },
            Level,
            () => DistanceDisplayFormat.InternationalFeet,
            outlines: null,
            alreadyCoordinated: false);
        viewModel.BeginEdit();
        Task loading = viewModel.PrimaryCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsPreparing);
        viewModel.CancelPendingPreparation();
        delayed.SetResult(Snapshot());
        await loading;

        Assert.Equal(1, calls);
        Assert.False(viewModel.Prepared);
        Assert.Null(viewModel.PreparedTerrain);
        Assert.False(viewModel.IsPreparing);
    }

    [Fact]
    public async Task CancelEditRestoresTheLastConfirmedReferenceAndKeepsItsPreparedSnapshot()
    {
        BuildingFloorReferenceViewModel viewModel = CreateViewModel();
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.SelectPoint(new Coordinate2D(-90d, 40d));
        viewModel.MeasuredRiseText = "0";
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        BuildingFloorReference confirmed = Assert.IsType<BuildingFloorReference>(viewModel.Reference);
        PreparedTerrainSnapshot snapshot = Assert.IsType<PreparedTerrainSnapshot>(viewModel.PreparedTerrain);

        viewModel.BeginEdit();
        viewModel.MeasuredRiseText = "4";
        Assert.Null(viewModel.Reference);
        Assert.False(viewModel.IsConfirmed);
        viewModel.CancelEdit();

        Assert.Same(confirmed, viewModel.Reference);
        Assert.Same(snapshot, viewModel.PreparedTerrain);
        Assert.True(viewModel.IsConfirmed);
        Assert.False(viewModel.IsEditing);
    }

    [Fact]
    public async Task CancelEditRestoresTheEntireFloorDraftAndSelectedPin()
    {
        BuildingFloorReferenceViewModel viewModel = CreateViewModel();
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.SelectPoint(new Coordinate2D(-90d, 40d));
        viewModel.MeasuredRiseText = "0";
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        GroundPoint originalPoint = Assert.IsType<GroundPoint>(viewModel.SelectedPoint);

        viewModel.BeginEdit();
        viewModel.MeasuredRiseText = "4";
        viewModel.SelectPoint(new Coordinate2D(-89d, 40d));
        Assert.Null(viewModel.SelectedSample);
        viewModel.CancelEdit();

        Assert.Equal("0", viewModel.MeasuredRiseText);
        Assert.Equal(originalPoint, viewModel.SelectedPoint);
        Assert.NotNull(viewModel.SelectedSample);
        Assert.True(viewModel.IsConfirmed);
    }

    [Fact]
    public async Task InvalidationDiscardsTheEditBaselineSoCancelCannotResurrectStaleReference()
    {
        BuildingFloorReferenceViewModel viewModel = CreateViewModel();
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.SelectPoint(new Coordinate2D(-90d, 40d));
        viewModel.MeasuredRiseText = "0";
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.BeginEdit();

        viewModel.Invalidate();
        viewModel.CancelEdit();

        Assert.Null(viewModel.Reference);
        Assert.False(viewModel.IsConfirmed);
        Assert.Null(viewModel.PreparedTerrain);
    }

    [Fact]
    public async Task InvalidSupportDoesNotFetchAgainAndCannotEnableAnEstimatedReference()
    {
        int fetches = 0;
        BuildingFloorReferenceViewModel viewModel = new(
            _ => { fetches++; return Task.FromResult(Snapshot(null)); },
            Level,
            () => DistanceDisplayFormat.InternationalFeet,
            outlines: null,
            alreadyCoordinated: false);
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.SelectPoint(new Coordinate2D(-90d, 40d));
        viewModel.MeasuredRiseText = "2";

        Assert.Equal(1, fetches);
        Assert.Null(viewModel.SelectedSample);
        Assert.False(viewModel.CanConfirm);
        Assert.Contains("No usable ground height", viewModel.ValidationMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CoordinateEntrySamplesThePreparedGridLocallyWithoutAnotherPreparation()
    {
        int fetches = 0;
        BuildingFloorReferenceViewModel viewModel = new(
            _ => { fetches++; return Task.FromResult(Snapshot()); },
            Level,
            () => DistanceDisplayFormat.InternationalFeet,
            outlines: null,
            alreadyCoordinated: false);
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        viewModel.CoordinateLatitudeText = "40";
        viewModel.CoordinateLongitudeText = "-90";
        viewModel.PlaceEnteredCoordinatesCommand.Execute(null);

        Assert.Equal(1, fetches);
        Assert.NotNull(viewModel.SelectedSample);
        Assert.Equal(-90d, viewModel.SelectedPoint!.Wgs84Longitude);
        Assert.Equal(40d, viewModel.SelectedPoint.Wgs84Latitude);
    }

    [Fact]
    public async Task SafePreparationFailureSurfacesThePreflightMessageAndUnknownFailureIsRedacted()
    {
        BuildingFloorReferenceViewModel specific = new(
            _ => Task.FromException<PreparedTerrainSnapshot>(new InvalidOperationException("OpenTopography API key is required.")),
            Level, () => DistanceDisplayFormat.InternationalFeet, null, false);
        specific.BeginEdit();
        await specific.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal("OpenTopography API key is required.", specific.ValidationMessage);

        BuildingFloorReferenceViewModel unknown = new(
            _ => Task.FromException<PreparedTerrainSnapshot>(new NotSupportedException("sensitive internal endpoint")),
            Level, () => DistanceDisplayFormat.InternationalFeet, null, false);
        unknown.BeginEdit();
        await unknown.PrimaryCommand.ExecuteAsync(null);
        Assert.Equal("Ground preview could not be prepared safely. Check the current terrain settings and try again.", unknown.ValidationMessage);
    }

    [Fact]
    public async Task RequestedOutlineProvenanceIsRetainedInTheConfirmedFloorReference()
    {
        OutlineSource source = new();
        BuildingFloorReferenceViewModel viewModel = new(
            _ => Task.FromResult(Snapshot()), Level, () => DistanceDisplayFormat.InternationalFeet, source, false);
        viewModel.BeginEdit();
        await viewModel.PrimaryCommand.ExecuteAsync(null);
        await viewModel.LoadOutlinesAsync(TestContext.Current.CancellationToken);
        viewModel.SelectPoint(new Coordinate2D(-90d, 40d));
        viewModel.MeasuredRiseText = "0";
        await viewModel.PrimaryCommand.ExecuteAsync(null);

        Assert.Same(source.Provenance, Assert.IsType<BuildingFloorReference>(viewModel.Reference).BuildingOutline);
    }

    private static BuildingFloorReferenceViewModel CreateViewModel() => new(
        _ => Task.FromResult(Snapshot()),
        Level,
        () => DistanceDisplayFormat.InternationalFeet,
        outlines: null,
        alreadyCoordinated: false);

    private static TargetProjectLevel Level() => new(42L, "level-42", "First Floor", 0d);

    private static PreparedTerrainSnapshot Snapshot(double? elevation = 100d)
    {
        HorizontalReference reference = new("EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        VerticalReference vertical = new("NAVD88", LengthUnit.Meter);
        ElevationGrid grid = new(reference, vertical, new Coordinate2D(-90d, 40d), 1d, 1d, GridAnchorConvention.CellCenter, GridRowOrder.SouthToNorth, new double?[,] { { elevation } });
        HorizontalReference projected = new("EPSG:26915", "NAD83", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
        CoordinateOperationDefinition operation = new("synthetic", "identity");
        HorizontalTransformationDefinition transformation = new(reference, projected, operation, operation, "Synthetic", "1");
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
        return new PreparedTerrainSnapshot(grid, new TerrainProcessingOutcome(payload, null, null), new IdentityTransform(reference), null, "synthetic");
    }

    private sealed class IdentityTransform(HorizontalReference reference) : IHorizontalCoordinateTransform
    {
        public HorizontalTransformationDefinition Definition { get; } = new(
            reference,
            reference,
            new CoordinateOperationDefinition("synthetic", "identity"),
            new CoordinateOperationDefinition("synthetic", "identity"),
            "Synthetic", "1");
        public Coordinate2D Forward(Coordinate2D source) => source;
        public Coordinate2D Inverse(Coordinate2D target) => target;
    }

    private sealed class OutlineSource : IBuildingOutlineSource
    {
        internal BuildingOutlineProvenance Provenance { get; } = new(
            "Synthetic outlines", "2026-10-01", "Synthetic", new Uri("https://example.test/license"),
            "Synthetic license", "Synthetic attribution", new string('A', 64), ["033333333"]);

        public Task<BuildingOutlineAcquisition> GetAsync(SolidGround.Core.Aois.Wgs84BoundingBoxAoi bounds, CancellationToken token = default) =>
            Task.FromResult(new BuildingOutlineAcquisition([], Provenance));
    }
}
