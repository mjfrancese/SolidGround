using System.Windows.Threading;
using System.Reflection;
using System.Net.Http;
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

[Collection(SessionApiKeyOverrideTestGroup.Name)]
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
                    return new SolidGroundDialogLookupServices(NullGeocoder.Instance, AddressGeocoderProvider.Census, configuredSource, 30d, 30, SessionApiKeyOverrides.Revision);
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
                _ => new SolidGroundDialogLookupServices(replacement, AddressGeocoderProvider.Geocodio, null, 30d, 30, SessionApiKeyOverrides.Revision)) with
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

    [Fact]
    public void ChangingTheAddressBeforeProviderSettingsChangeDoesNotRestoreOrQueryTheOldAddress()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings initial = UiSettingsStore.CreateDefault();
            RevitSettings changed = initial with
            {
                AddressAndParcel = initial.AddressAndParcel with { GeocoderProvider = AddressGeocoderProvider.Geocodio },
            };
            DelayedGeocoder original = new();
            DelayedGeocoder replacement = new();
            SolidGroundDialogViewModel viewModel = new(CreateInputs(
                initial,
                null,
                _ => changed,
                _ => new SolidGroundDialogLookupServices(replacement, AddressGeocoderProvider.Geocodio, null, 30d, 30, SessionApiKeyOverrides.Revision)) with
            {
                Geocoder = original,
            });
            viewModel.AddressText = "Synthetic found A";
            viewModel.FindCommand.Execute(null);
            original.CompleteNext("Synthetic found A");
            PumpUntil(() => viewModel.FindCommand.ExecutionTask?.IsCompleted == true);

            viewModel.AddressText = "Synthetic edited B";
            viewModel.EditSettingsCommand.Execute(null);

            Assert.Equal("Synthetic edited B", viewModel.AddressText);
            Assert.Empty(replacement.Pending);
            Assert.Equal(SolidGroundDialogStep.Location, viewModel.CurrentStep);
        });
    }

    [Fact]
    public void CommittedAmbiguousLocationStaysSynchronizedWithParcelLookupAndProvenanceUntilBack()
    {
        StaTestHost.Run(() =>
        {
            AddressGeocodeCandidate first = new(41.59d, -93.60d, "Synthetic first", "Synthetic first attribution");
            AddressGeocodeCandidate second = new(41.60d, -93.61d, "Synthetic second", "Synthetic second attribution");
            SolidGroundDialogViewModel viewModel = new(new SolidGroundDialogInputs(
                new ImmediateGeocoder(first, second), AddressGeocoderProvider.Census, new ImmediateParcelSource(),
                [new NamedElevationCandidate(1, "Synthetic level", 0d)], [new NamedCandidate(2, "Synthetic toposolid")],
                null, null, LengthUnit.UsSurveyFoot, 15_000, false, false,
                new RevitIniToposolidThresholds.Thresholds(20_000, null), "synthetic-revit.ini", 30,
                UiSettingsStore.CreateDefault().Request.AreaOfInterest, 30d, TerrainAcquisitionMode.Fetch,
                Settings: UiSettingsStore.CreateDefault()));
            viewModel.AddressText = "Synthetic ambiguous address";
            viewModel.FindCommand.Execute(null);
            PumpUntil(() => viewModel.GeocodeCandidates.Count == 2);
            Assert.True(viewModel.CanSelectLocation);

            viewModel.SelectedGeocodeCandidate = first;
            viewModel.UseLocationCommand.Execute(null);
            PumpUntil(() => viewModel.ParcelCandidates.Count == 1);
            Assert.False(viewModel.CanSelectLocation);

            viewModel.SelectedGeocodeCandidate = second;
            Assert.Equal(first, viewModel.SelectedGeocodeCandidate);
            Assert.Equal(first, viewModel.ConfirmedLocation);

            viewModel.SelectedParcelCandidate = Assert.Single(viewModel.ParcelCandidates);
            viewModel.UseParcelCommand.Execute(null);
            viewModel.CreateCommand.Execute(null);
            Assert.Equal("Synthetic first attribution", viewModel.Result!.AddressParcel!.Geocode!.Attribution);

            viewModel.BackCommand.Execute(null);
            viewModel.BackCommand.Execute(null);
            Assert.Equal(SolidGroundDialogStep.Location, viewModel.CurrentStep);
            Assert.Null(viewModel.SelectedGeocodeCandidate);
        });
    }

    [Fact]
    public void AutomaticSingleLocationCannotBeDeselectedOrReplacedDuringOrAfterParcelLookup()
    {
        StaTestHost.Run(() =>
        {
            AddressGeocodeCandidate resolved = new(41.59d, -93.60d, "Synthetic automatic", "Synthetic automatic attribution");
            AddressGeocodeCandidate other = new(41.60d, -93.61d, "Synthetic other", "Synthetic other attribution");
            DelayedParcelSource source = new();
            SolidGroundDialogViewModel viewModel = new(new SolidGroundDialogInputs(
                new ImmediateGeocoder(resolved), AddressGeocoderProvider.Census, source,
                [new NamedElevationCandidate(1, "Synthetic level", 0d)], [new NamedCandidate(2, "Synthetic toposolid")],
                null, null, LengthUnit.UsSurveyFoot, 15_000, false, false,
                new RevitIniToposolidThresholds.Thresholds(20_000, null), "synthetic-revit.ini", 30,
                UiSettingsStore.CreateDefault().Request.AreaOfInterest, 30d, TerrainAcquisitionMode.Fetch,
                Settings: UiSettingsStore.CreateDefault()));
            viewModel.AddressText = "Synthetic automatic address";
            viewModel.FindCommand.Execute(null);
            Assert.Single(source.Pending);
            Assert.False(viewModel.CanSelectLocation);

            viewModel.SelectedGeocodeCandidate = null;
            Assert.Equal(resolved, viewModel.SelectedGeocodeCandidate);
            viewModel.SelectedGeocodeCandidate = other;
            Assert.Equal(resolved, viewModel.SelectedGeocodeCandidate);

            source.CompleteNext("automatic-parcel");
            PumpUntil(() => viewModel.ParcelCandidates.Count == 1);
            viewModel.SelectedGeocodeCandidate = null;
            Assert.Equal(resolved, viewModel.SelectedGeocodeCandidate);
            viewModel.SelectedGeocodeCandidate = other;
            Assert.Equal(resolved, viewModel.SelectedGeocodeCandidate);
            Assert.Equal(resolved, viewModel.ConfirmedLocation);
        });
    }

    [Fact]
    public void CancelledSettingsEditWithSessionCredentialChangeRevokesCreateAndRejectsOldParcelCompletion()
    {
        StaTestHost.Run(() =>
        {
            SessionApiKeyOverrides.ClearAll();
            try
            {
                RevitSettings settings = UiSettingsStore.CreateDefault();
                SolidGroundDialogViewModel confirmed = new(CreateInputs(
                    settings,
                    new ImmediateParcelSource(),
                    _ =>
                    {
                        SessionApiKeyOverrides.UseGeocodio("synthetic-session-key");
                        return null;
                    },
                    _ => new SolidGroundDialogLookupServices(
                        NullGeocoder.Instance, AddressGeocoderProvider.Census, new ImmediateParcelSource(), 30d, 30, SessionApiKeyOverrides.Revision)) with
                {
                    InitialCredentialRevision = SessionApiKeyOverrides.Revision,
                });
                confirmed.EntryMode = LocationEntryMode.Coordinates;
                confirmed.LatitudeText = "41.59";
                confirmed.LongitudeText = "-93.60";
                confirmed.FindCommand.Execute(null);
                PumpUntil(() => confirmed.ParcelCandidates.Count == 1);
                confirmed.SelectedParcelCandidate = Assert.Single(confirmed.ParcelCandidates);
                confirmed.UseParcelCommand.Execute(null);
                Assert.Equal(SolidGroundDialogStep.Review, confirmed.CurrentStep);

                confirmed.EditSettingsCommand.Execute(null);

                Assert.Equal(SolidGroundDialogStep.Parcel, confirmed.CurrentStep);
                Assert.Null(confirmed.SelectedParcelCandidate);
                Assert.False(confirmed.CreateCommand.CanExecute(null));

                DelayedParcelSource oldSource = new();
                DelayedParcelSource replacementSource = new();
                SolidGroundDialogViewModel pending = new(CreateInputs(
                    settings,
                    oldSource,
                    _ =>
                    {
                        SessionApiKeyOverrides.ClearGeocodio();
                        return null;
                    },
                    _ => new SolidGroundDialogLookupServices(
                        NullGeocoder.Instance, AddressGeocoderProvider.Census, replacementSource, 30d, 30, SessionApiKeyOverrides.Revision)) with
                {
                    InitialCredentialRevision = SessionApiKeyOverrides.Revision,
                });
                pending.EntryMode = LocationEntryMode.Coordinates;
                pending.LatitudeText = "41.59";
                pending.LongitudeText = "-93.60";
                pending.FindCommand.Execute(null);
                Assert.Single(oldSource.Pending);

                pending.EditSettingsCommand.Execute(null);
                Assert.Single(replacementSource.Pending);
                oldSource.CompleteNext("old-session-parcel");
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Assert.Empty(pending.ParcelCandidates);

                replacementSource.CompleteNext("current-session-parcel");
                PumpUntil(() => pending.ParcelCandidates.Count == 1);
                Assert.Equal("current-session-parcel", Assert.Single(pending.ParcelCandidates).Candidate.ParcelId);
            }
            finally
            {
                SessionApiKeyOverrides.ClearAll();
            }
        });
    }

    [Fact]
    public void EmptyParcelLookupKeepsTheParcelStepAndExplainsHowToRecover()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialogViewModel viewModel = new(CreateInputs(
                UiSettingsStore.CreateDefault(), new EmptyParcelSource(), null, null));
            viewModel.EntryMode = LocationEntryMode.Coordinates;
            viewModel.LatitudeText = "41.59";
            viewModel.LongitudeText = "-93.60";

            viewModel.FindCommand.Execute(null);
            PumpUntil(() => viewModel.FindCommand.ExecutionTask?.IsCompleted == true);

            Assert.Equal(SolidGroundDialogStep.Parcel, viewModel.CurrentStep);
            Assert.Empty(viewModel.ParcelCandidates);
            Assert.Contains("No parcel boundary was found", viewModel.ErrorText, StringComparison.Ordinal);
            Assert.Contains("Settings", viewModel.ErrorText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void BackInvalidatesAnInFlightParcelLookup()
    {
        StaTestHost.Run(() =>
        {
            DelayedParcelSource source = new();
            SolidGroundDialogViewModel viewModel = new(CreateInputs(UiSettingsStore.CreateDefault(), source, null, null));
            viewModel.EntryMode = LocationEntryMode.Coordinates;
            viewModel.LatitudeText = "41.59";
            viewModel.LongitudeText = "-93.60";
            viewModel.FindCommand.Execute(null);
            Assert.Single(source.Pending);

            viewModel.BackCommand.Execute(null);
            Assert.Equal(SolidGroundDialogStep.Location, viewModel.CurrentStep);

            source.CompleteNext("late-parcel");
            PumpUntil(() => viewModel.FindCommand.ExecutionTask?.IsCompleted == true);
            Assert.Equal(SolidGroundDialogStep.Location, viewModel.CurrentStep);
            Assert.Empty(viewModel.ParcelCandidates);
            Assert.False(viewModel.IsBusy);
        });
    }

    [Fact]
    public void FailedSettingsReconfigurationRevokesTheConfirmedParcelButKeepsTheLocationEditable()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings initial = UiSettingsStore.CreateDefault();
            RevitSettings saved = initial with
            {
                AddressAndParcel = initial.AddressAndParcel with { LocalParcelFilePath = "requires-a-new-source.geojson" },
            };
            SolidGroundDialogViewModel viewModel = new(CreateInputs(
                initial,
                new ImmediateParcelSource(),
                _ => saved,
                _ => throw new InvalidOperationException("Synthetic source configuration failure.")));
            viewModel.EntryMode = LocationEntryMode.Coordinates;
            viewModel.LatitudeText = "41.59";
            viewModel.LongitudeText = "-93.60";
            viewModel.FindCommand.Execute(null);
            PumpUntil(() => viewModel.ParcelCandidates.Count == 1);
            viewModel.SelectedParcelCandidate = Assert.Single(viewModel.ParcelCandidates);
            viewModel.UseParcelCommand.Execute(null);
            Assert.Equal(SolidGroundDialogStep.Review, viewModel.CurrentStep);

            viewModel.EditSettingsCommand.Execute(null);

            Assert.Equal(SolidGroundDialogStep.Parcel, viewModel.CurrentStep);
            Assert.NotNull(viewModel.ConfirmedLocation);
            Assert.Null(viewModel.SelectedParcelCandidate);
            Assert.False(viewModel.CreateCommand.CanExecute(null));
            Assert.Contains("Settings", viewModel.ErrorText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DisplayUnitChangePreservesTheCurrentExplicitAreaAndConfirmedParcel()
    {
        StaTestHost.Run(() =>
        {
            RevitSettings metres = UiSettingsStore.CreateDefault() with { DistanceDisplayFormat = DistanceDisplayFormat.Metres };
            RevitSettings feet = metres with { DistanceDisplayFormat = DistanceDisplayFormat.UsSurveyFeet };
            SolidGroundDialogViewModel explicitArea = new(CreateInputs(metres, null, _ => feet, null));
            explicitArea.EntryMode = LocationEntryMode.Radius;
            explicitArea.LatitudeText = "41.59";
            explicitArea.LongitudeText = "-93.60";
            explicitArea.RadiusMetersText = "10";
            explicitArea.FindCommand.Execute(null);
            Assert.Equal(SolidGroundDialogStep.Review, explicitArea.CurrentStep);

            explicitArea.EditSettingsCommand.Execute(null);

            Assert.Equal(SolidGroundDialogStep.Review, explicitArea.CurrentStep);
            Assert.Equal(10d, DistanceDisplayConverter.ParseMeters(explicitArea.RadiusMetersText, DistanceDisplayFormat.UsSurveyFeet), 8);
            explicitArea.CreateCommand.Execute(null);
            Assert.Equal(10d, Assert.IsType<Wgs84RadiusAoi>(explicitArea.Result!.Aoi).Radius.Value, 8);

            SolidGroundDialogViewModel parcel = new(CreateInputs(metres, new ImmediateParcelSource(), _ => feet, null));
            parcel.EntryMode = LocationEntryMode.Radius;
            parcel.LatitudeText = "41.59";
            parcel.LongitudeText = "-93.60";
            parcel.RadiusMetersText = "10";
            parcel.EntryMode = LocationEntryMode.Coordinates;
            parcel.FindCommand.Execute(null);
            PumpUntil(() => parcel.ParcelCandidates.Count == 1);
            ParcelProximityCandidate confirmed = Assert.Single(parcel.ParcelCandidates);
            parcel.SelectedParcelCandidate = confirmed;
            parcel.UseParcelCommand.Execute(null);

            parcel.EditSettingsCommand.Execute(null);

            Assert.Equal(SolidGroundDialogStep.Review, parcel.CurrentStep);
            Assert.Equal(confirmed, parcel.SelectedParcelCandidate);
        });
    }

    [Fact]
    public void CorrectedSearchStartsBeforeAnOlderNonCooperativeLookupFinishes()
    {
        StaTestHost.Run(() =>
        {
            DelayedGeocoder geocoder = new();
            SolidGroundDialogViewModel viewModel = new(CreateInputs(
                UiSettingsStore.CreateDefault(), new ImmediateParcelSource(), null, null) with { Geocoder = geocoder });
            viewModel.AddressText = "Synthetic stale address";
            viewModel.FindCommand.Execute(null);
            Assert.Single(geocoder.Pending);

            viewModel.AddressText = "Synthetic corrected address";
            Assert.True(viewModel.FindCommand.CanExecute(null));
            viewModel.FindCommand.Execute(null);
            Assert.Equal(2, geocoder.Pending.Count);

            geocoder.FailNext();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Assert.True(viewModel.IsBusy);
            Assert.Null(viewModel.ErrorText);

            geocoder.CompleteNext("Synthetic corrected result");
            PumpUntil(() => viewModel.ConfirmedLocation?.MatchedAddress == "Synthetic corrected result" && !viewModel.IsBusy);
            Assert.Null(viewModel.ErrorText);
            Assert.False(viewModel.IsBusy);
        });
    }

    [Fact]
    public async Task MissingLocalParcelTermsAreDeferredAsAnActionableSettingsError()
    {
        RevitAddressAndParcelSettings settings = new(
            AddressGeocoderProvider.Census, null, null, "synthetic-missing-parcels.geojson", null, null, null);
        MethodInfo build = typeof(SolidGroundDialogHost).GetMethod("BuildParcelSource", BindingFlags.NonPublic | BindingFlags.Static)!;
        using HttpClient client = new();
        IParcelBoundarySource source = Assert.IsAssignableFrom<IParcelBoundarySource>(build.Invoke(null, [settings, client]));

        ParcelBoundarySourceException error = await Assert.ThrowsAnyAsync<ParcelBoundarySourceException>(
            () => source.FindAsync(new ParcelPointQuery(41.59d, -93.60d), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("Settings", error.Message, StringComparison.Ordinal);
        Assert.Contains("source label", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("license", error.Message, StringComparison.OrdinalIgnoreCase);
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
            InitialCredentialRevision: SessionApiKeyOverrides.Revision,
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

        internal void FailNext() => Pending.Dequeue().SetException(new InvalidOperationException("Synthetic non-cooperative failure."));
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

    private sealed class EmptyParcelSource : IParcelBoundarySource
    {
        public ValueTask<ParcelBoundaryAcquisition> FindAsync(ParcelBoundaryQuery query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ParcelBoundaryAcquisition([]));
    }

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic,
        HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
