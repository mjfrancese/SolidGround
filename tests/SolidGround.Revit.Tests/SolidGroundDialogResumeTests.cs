using System.Windows.Threading;
using SolidGround.Core.Aois;
using SolidGround.Core.Hosting;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Units;
using SolidGround.Revit.Dialog;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

public sealed class SolidGroundDialogResumeTests
{
    [Fact]
    public void SettingsResumeReconfiguresTheSourceRejectsStaleResultsAndPreservesReviewForUnitOnlyChanges()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings initial = UiSettingsStore.CreateDefault();
            RevitSettings sourceEdited = initial with
            {
                AddressAndParcel = initial.AddressAndParcel with { LocalParcelFilePath = "synthetic-parcels.geojson" },
            };
            RevitSettings unitsEdited = sourceEdited with
            {
                Request = sourceEdited.Request with
                {
                    OutputUnit = LengthUnit.InternationalFoot,
                    Simplification = sourceEdited.Request.Simplification with { PointBudget = 12_345 },
                },
                TerrainExtensionMeters = 6d,
            };
            DelayedParcelSource staleSource = new();
            DelayedParcelSource configuredSource = new();
            int editCount = 0;
            int factoryCalls = 0;
            SolidGroundDialogViewModel viewModel = new(CreateInputs(
                initial,
                staleSource,
                current => editCount++ switch { 0 => sourceEdited, 1 => unitsEdited, _ => null },
                _ =>
                {
                    factoryCalls++;
                    return new SolidGroundDialogLookupServices(NullGeocoder.Instance, AddressGeocoderProvider.Census, configuredSource, 30d, 30, 0);
                }));

            viewModel.EntryMode = LocationEntryMode.Coordinates;
            viewModel.LatitudeText = "41.590000";
            viewModel.LongitudeText = "-93.600000";
            viewModel.FindCommand.Execute(null);
            Assert.Single(staleSource.Pending);

            viewModel.EditSettingsCommand.Execute(null);
            Assert.Equal(1, factoryCalls);
            Assert.Equal(initial.Request.Simplification.PointBudget, viewModel.PointBudget);
            Assert.Equal(LengthUnit.UsSurveyFoot, viewModel.SelectedOutputUnit);
            Assert.False(viewModel.WriteSharedCoordinatesIfAbsent);
            Assert.Single(configuredSource.Pending);

            staleSource.FailNext();
            PumpUntil(() => viewModel.FindCommand.ExecutionTask?.IsCompleted == true);
            Assert.Empty(viewModel.ParcelCandidates);
            Assert.Null(viewModel.ErrorText);
            Assert.True(viewModel.IsBusy);

            configuredSource.CompleteNext("configured-parcel");
            PumpUntil(() => viewModel.ParcelCandidates.Count == 1);
            ParcelProximityCandidate configuredParcel = Assert.Single(viewModel.ParcelCandidates);
            Assert.Equal("configured-parcel", configuredParcel.Candidate.ParcelId);
            viewModel.SelectedParcelCandidate = configuredParcel;
            Assert.Contains("configured-parcel", viewModel.SelectedParcelDetail, StringComparison.Ordinal);
            Assert.Contains("Synthetic parcel source", viewModel.SelectedParcelSourceTerms, StringComparison.Ordinal);
            Assert.Contains("Synthetic license disclaimer.", viewModel.SelectedParcelSourceTerms, StringComparison.Ordinal);
            Assert.Contains("Synthetic legal description.", viewModel.SelectedParcelDetail, StringComparison.Ordinal);
            viewModel.UseParcelCommand.Execute(null);
            Assert.Equal(SolidGroundDialogStep.Review, viewModel.CurrentStep);

            viewModel.EditSettingsCommand.Execute(null);
            Assert.Equal(2, factoryCalls);
            Assert.Equal(SolidGroundDialogStep.Review, viewModel.CurrentStep);
            Assert.Equal(configuredParcel, viewModel.SelectedParcelCandidate);
            Assert.Equal(12_345, viewModel.PointBudget);
            Assert.Equal(LengthUnit.InternationalFoot, viewModel.SelectedOutputUnit);
            Assert.Equal(6d, viewModel.TerrainExtensionMeters);

            viewModel.EditSettingsCommand.Execute(null);
            Assert.Equal(2, factoryCalls);
            Assert.Equal(SolidGroundDialogStep.Review, viewModel.CurrentStep);
            Assert.Equal(configuredParcel, viewModel.SelectedParcelCandidate);
        });
    }

    [Fact]
    public void ExplicitLocalGeometryCreatesTheCurrentZeroBufferArea()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialogViewModel viewModel = new(CreateInputs(UiSettingsStore.CreateDefault(), null, null, null));
            viewModel.EntryMode = LocationEntryMode.LocalGeometry;
            viewModel.LocalGeometryText = "POLYGON ((-93.61 41.58, -93.59 41.58, -93.59 41.60, -93.61 41.58))";

            viewModel.FindCommand.Execute(null);
            Assert.Equal(SolidGroundDialogStep.Review, viewModel.CurrentStep);
            viewModel.CreateCommand.Execute(null);

            SolidGroundDialogResult result = Assert.IsType<SolidGroundDialogResult>(viewModel.Result);
            Assert.Equal(DialogAoiSource.ExplicitArea, result.AoiSource);
            ParcelGeometryAoi area = Assert.IsType<ParcelGeometryAoi>(result.Aoi);
            Assert.Equal(0d, area.Buffer.Value);
        });
    }

    [Fact]
    public void ViewModelRetainsMeterOutputAndResolvesAnAmbiguousLocationBeforeParcelConfirmation()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings settings = UiSettingsStore.CreateDefault() with
            {
                Request = UiSettingsStore.CreateDefault().Request with { OutputUnit = LengthUnit.Meter },
            };
            ImmediateGeocoder geocoder = new(
                new AddressGeocodeCandidate(41.59d, -93.60d, "Synthetic first", "Synthetic attribution"),
                new AddressGeocodeCandidate(41.60d, -93.61d, "Synthetic second", "Synthetic attribution"));
            SolidGroundDialogViewModel viewModel = new(new SolidGroundDialogInputs(
                geocoder,
                AddressGeocoderProvider.Census,
                new ImmediateParcelSource(),
                [new NamedElevationCandidate(1, "Synthetic level", 0d)],
                [new NamedCandidate(2, "Synthetic toposolid")],
                null, null, LengthUnit.Meter, 15_000, false, false,
                new RevitIniToposolidThresholds.Thresholds(20_000, null), "synthetic-revit.ini", 30,
                settings.Request.AreaOfInterest, 30d, settings.Request.Mode, Settings: settings));

            Assert.Equal(LengthUnit.Meter, viewModel.SelectedOutputUnit);
            Assert.Contains(OpenTopographyUsgs1mSource.AttributionNotice, viewModel.SourceSummary, StringComparison.Ordinal);
            viewModel.AddressText = "Synthetic ambiguous address";
            viewModel.FindCommand.Execute(null);
            PumpUntil(() => viewModel.GeocodeCandidates.Count == 2);
            Assert.True(viewModel.HasMultipleLocations);

            viewModel.SelectedGeocodeCandidate = viewModel.GeocodeCandidates[1];
            viewModel.UseLocationCommand.Execute(null);
            PumpUntil(() => viewModel.ParcelCandidates.Count == 1);
            Assert.False(viewModel.HasMultipleLocations);
            Assert.Equal("Synthetic attribution", viewModel.LocationAttribution);
        });
    }

    [Fact]
    public void ChangingGeocoderRequeriesTheAddressBeforeAnyNewParcelLookup()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings initial = UiSettingsStore.CreateDefault();
            RevitSettings changed = initial with
            {
                AddressAndParcel = initial.AddressAndParcel with { GeocoderProvider = AddressGeocoderProvider.Geocodio },
            };
            DelayedGeocoder first = new();
            DelayedGeocoder replacement = new();
            SolidGroundDialogViewModel viewModel = new(CreateInputs(
                initial,
                null,
                _ => changed,
                _ => new SolidGroundDialogLookupServices(replacement, AddressGeocoderProvider.Geocodio, null, 30d, 30, 0)) with
            {
                Geocoder = first,
            });

            viewModel.AddressText = "Synthetic provider address";
            viewModel.FindCommand.Execute(null);
            first.CompleteNext("Synthetic initial provider result");
            PumpUntil(() => viewModel.FindCommand.ExecutionTask?.IsCompleted == true);

            viewModel.EditSettingsCommand.Execute(null);
            Assert.Single(replacement.Pending);
            replacement.CompleteNext("Synthetic replacement provider result");
            PumpUntil(() => viewModel.ConfirmedLocation?.MatchedAddress == "Synthetic replacement provider result");
            Assert.Equal(AddressGeocoderProvider.Geocodio, viewModel.EffectiveSettings!.AddressAndParcel.GeocoderProvider);
        });
    }

    private static SolidGroundDialogInputs CreateInputs(
        RevitSettings settings,
        IParcelBoundarySource? parcelSource,
        Func<RevitSettings, RevitSettings?>? edit,
        Func<RevitSettings, SolidGroundDialogLookupServices>? reconfigure) => new(
            NullGeocoder.Instance,
            AddressGeocoderProvider.Census,
            parcelSource,
            [new NamedElevationCandidate(1, "Synthetic level", 0d)],
            [new NamedCandidate(2, "Synthetic toposolid")],
            null,
            null,
            settings.Request.OutputUnit,
            settings.Request.Simplification.PointBudget,
            false,
            false,
            new RevitIniToposolidThresholds.Thresholds(20_000, null),
            "synthetic-revit.ini",
            30,
            settings.Request.AreaOfInterest,
            30d,
            settings.Request.Mode,
            Settings: settings,
            EditSettings: edit,
            ReconfigureLookupServices: reconfigure);

    private static void PumpUntil(Func<bool> completion)
    {
        DispatcherFrame frame = new();
        DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(10) };
        DateTime deadline = DateTime.UtcNow.AddSeconds(2);
        timer.Tick += (_, _) =>
        {
            if (completion() || DateTime.UtcNow >= deadline)
            {
                frame.Continue = false;
            }
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Assert.True(completion(), "The dialog lookup did not finish within two seconds.");
    }

    private sealed class NullGeocoder : IAddressGeocoder
    {
        internal static readonly NullGeocoder Instance = new();
        public ValueTask<AddressGeocodeAcquisition> GeocodeAsync(AddressGeocodeRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("This test only enters coordinates directly.");
    }

    private sealed class DelayedParcelSource : IParcelBoundarySource
    {
        internal Queue<TaskCompletionSource<ParcelBoundaryAcquisition>> Pending { get; } = [];

        public ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<ParcelBoundaryAcquisition> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(completion);
            return new ValueTask<ParcelBoundaryAcquisition>(completion.Task);
        }

        internal void CompleteNext(string parcelId)
        {
            ParcelBoundaryCandidate candidate = new(
                ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt,
                    "POLYGON ((-93.61 41.58, -93.59 41.58, -93.59 41.60, -93.61 41.58))", GeographicReference()),
                parcelId,
                100d,
                ParcelBoundarySourceKind.LocalParcelFile,
                "Synthetic parcel source",
                "Synthetic license disclaimer.",
                legalDescription: "Synthetic legal description.");
            Pending.Dequeue().SetResult(new ParcelBoundaryAcquisition([candidate]));
        }

        internal void FailNext() => Pending.Dequeue().SetException(new InvalidOperationException("Synthetic non-cooperative failure."));
    }

    private sealed class ImmediateGeocoder(params AddressGeocodeCandidate[] candidates) : IAddressGeocoder
    {
        public ValueTask<AddressGeocodeAcquisition> GeocodeAsync(AddressGeocodeRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new AddressGeocodeAcquisition(candidates));
    }

    private sealed class DelayedGeocoder : IAddressGeocoder
    {
        internal Queue<TaskCompletionSource<AddressGeocodeAcquisition>> Pending { get; } = [];

        public ValueTask<AddressGeocodeAcquisition> GeocodeAsync(AddressGeocodeRequest request, CancellationToken cancellationToken = default)
        {
            TaskCompletionSource<AddressGeocodeAcquisition> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Pending.Enqueue(completion);
            return new ValueTask<AddressGeocodeAcquisition>(completion.Task);
        }

        internal void CompleteNext(string address) => Pending.Dequeue().SetResult(
            new AddressGeocodeAcquisition([new AddressGeocodeCandidate(41.59d, -93.60d, address, "Synthetic attribution")]));
    }

    private sealed class ImmediateParcelSource : IParcelBoundarySource
    {
        public ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default)
        {
            ParcelBoundaryCandidate candidate = new(
                ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt,
                    "POLYGON ((-93.61 41.58, -93.59 41.58, -93.59 41.60, -93.61 41.58))", GeographicReference()),
                "ambiguous-flow-parcel", 100d, ParcelBoundarySourceKind.LocalParcelFile,
                "Synthetic parcel source", "Synthetic license disclaimer.",
                legalDescription: "Synthetic legal description.");
            return ValueTask.FromResult(new ParcelBoundaryAcquisition([candidate]));
        }
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic,
        HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
