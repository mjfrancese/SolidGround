using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;
using SolidGround.Revit.Processing;
using SolidGround.Revit.Settings;

namespace SolidGround.Revit.Tests;

public sealed class PreparedTerrainBindingTests : IDisposable
{
    private readonly string temporaryDirectory = Path.Combine(Path.GetTempPath(), "SolidGround.PreparedTerrainBindingTests", Guid.NewGuid().ToString("N"));

    public PreparedTerrainBindingTests() => Directory.CreateDirectory(temporaryDirectory);

    [Fact]
    public void ComputeIsDeterministicAndBindsTheEffectiveTerrainAndNativeTargetInputs()
    {
        RevitSettings settings = FetchSettings();
        AreaOfInterest aoi = BoundingBox();
        AddressParcelProvenance provenance = AddressParcel();

        string expected = Compute(settings, aoi, provenance);
        string actual = Compute(settings, aoi, provenance);

        Assert.Equal(expected, actual);
        Assert.Matches("^[0-9A-F]{64}$", actual);
        Assert.NotEqual(expected, Compute(settings with { TerrainExtensionMeters = 2d }, aoi, provenance));
        Assert.NotEqual(expected, Compute(settings with { Request = settings.Request with { Output = settings.Request.Output with { BaseName = "another-run" } } }, aoi, provenance));
        Assert.NotEqual(expected, Compute(settings with { Request = settings.Request with { Simplification = settings.Request.Simplification with { PointBudget = 1_001 } } }, aoi, provenance));
        Assert.NotEqual(expected, Compute(settings with { Request = settings.Request with { LocalOrigin = new LocalOriginRequest(LocalOriginKind.Explicit, 1d, 2d, 3d) } }, aoi, provenance));
        Assert.NotEqual(expected, Compute(settings, new Wgs84BoundingBoxAoi(-90.1d, 39.9d, -89.9d, 40.1d), provenance));
        Assert.NotEqual(expected, Compute(settings, new Wgs84RadiusAoi(40d, -90d, LinearDistance.Meters(100d)), provenance));
        Assert.NotEqual(expected, Compute(settings, aoi, AddressParcel(queryText: "synthetic query B")));
        Assert.NotEqual(expected, Compute(settings, aoi, provenance, documentGuid: "document-b"));
        Assert.NotEqual(expected, Compute(settings, aoi, provenance, level: new TargetProjectLevel(7, "level-8", "Ignored display name", 3d)));
        Assert.NotEqual(expected, Compute(settings, aoi, provenance, toposolidTypeId: 12));
        Assert.NotEqual(expected, Compute(settings, aoi, provenance, shortCurveToleranceInternal: 0.125d));
        Assert.NotEqual(expected, Compute(settings, aoi, provenance, credentialRevision: 9));
    }

    [Fact]
    public void ComputeExcludesUiOnlyPreferencesAndVolatileAddressRetrievalDate()
    {
        RevitSettings settings = FetchSettings();
        AreaOfInterest aoi = BoundingBox();
        AddressParcelProvenance provenance = AddressParcel();

        string baseline = Compute(settings, aoi, provenance);
        RevitSettings presentationOnly = settings with
        {
            Target = new RevitTargetSettings("Level display change", "Type display change"),
            SharedCoordinates = new RevitSharedCoordinatesSettings(true),
            AddressAndParcel = new RevitAddressAndParcelSettings(AddressGeocoderProvider.Esri, "C:\\unrelated\\parcel.geojson", "00001", "C:\\unrelated\\local.geojson", "Display label", "Display disclaimer", 250d),
            DistanceDisplayFormat = DistanceDisplayFormat.Metres,
        };

        Assert.Equal(baseline, Compute(presentationOnly, aoi, provenance));
        Assert.Equal(baseline, Compute(settings, aoi, AddressParcel(retrievalDate: new DateOnly(2026, 10, 2))));
        Assert.Equal(baseline, Compute(settings, aoi, provenance, level: new TargetProjectLevel(7, "level-7", "Renamed level", 2d)));
    }

    [Fact]
    public void ComputeUsesResolvedParcelGeometryWithoutReadingAnUnrelatedConfiguredParcelPath()
    {
        string unavailableConfiguredPath = Path.Combine(temporaryDirectory, "configured-parcel-does-not-exist.geojson");
        RevitSettings settings = FetchSettings() with
        {
            Request = FetchSettings().Request with
            {
                AreaOfInterest = new AoiSettings
                {
                    Kind = AreaOfInterestKind.Parcel,
                    Parcel = new ParcelAoiSettings { Path = unavailableConfiguredPath, Format = "geojson", BufferMeters = 0d },
                },
            },
        };
        HorizontalReference reference = new("EPSG:4326", "WGS 84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
        AreaOfInterest resolvedParcel = new ParcelGeometryAoi(ParcelGeometryFormat.Wkt, "POLYGON ((-90 40, -89.9 40, -89.9 40.1, -90 40.1, -90 40))", reference, LinearDistance.Meters(2d));

        string binding = Compute(settings, resolvedParcel, AddressParcel());
        AreaOfInterest changedResolvedParcel = new ParcelGeometryAoi(ParcelGeometryFormat.Wkt, "POLYGON ((-90 40, -89.8 40, -89.8 40.1, -90 40.1, -90 40))", reference, LinearDistance.Meters(2d));

        Assert.Matches("^[0-9A-F]{64}$", binding);
        Assert.NotEqual(binding, Compute(settings, changedResolvedParcel, AddressParcel()));
    }

    [Fact]
    public void ProcessBindingTracksRequiredInputContentAndOptionalSidecarState()
    {
        string asc = Path.Combine(temporaryDirectory, "terrain.asc");
        string prj = Path.Combine(temporaryDirectory, "terrain.prj");
        string defaultSidecar = Path.ChangeExtension(asc, ".source.json");
        File.WriteAllText(asc, "ncols 1\nnrows 1\n1\n");
        File.WriteAllText(prj, "LOCAL_CS[\"first\"]");
        RevitSettings settings = ProcessSettings(asc, prj, sourceJson: null);

        string absentSidecar = Compute(settings, BoundingBox(), null);
        File.WriteAllText(asc, "ncols 1\nnrows 1\n2\n");
        string changedAsc = Compute(settings, BoundingBox(), null);
        File.WriteAllText(prj, "LOCAL_CS[\"second\"]");
        string changedPrj = Compute(settings, BoundingBox(), null);
        File.WriteAllText(defaultSidecar, "{\"dataset\":\"synthetic\"}");
        string appearedSidecar = Compute(settings, BoundingBox(), null);
        File.Delete(defaultSidecar);
        string absentAgain = Compute(settings, BoundingBox(), null);

        Assert.NotEqual(absentSidecar, changedAsc);
        Assert.NotEqual(changedAsc, changedPrj);
        Assert.NotEqual(changedPrj, appearedSidecar);
        Assert.Equal(changedPrj, absentAgain);
        File.Delete(prj);
        Assert.ThrowsAny<IOException>(() => Compute(settings, BoundingBox(), null));
    }

    [Fact]
    public void ExplicitProcessSidecarIsRequiredAndItsContentIsBound()
    {
        string asc = Path.Combine(temporaryDirectory, "terrain.asc");
        string prj = Path.Combine(temporaryDirectory, "terrain.prj");
        string source = Path.Combine(temporaryDirectory, "terrain.source.json");
        File.WriteAllText(asc, "ncols 1\nnrows 1\n1\n");
        File.WriteAllText(prj, "LOCAL_CS[\"synthetic\"]");
        File.WriteAllText(source, "{\"dataset\":\"one\"}");
        RevitSettings settings = ProcessSettings(asc, prj, source);

        string first = Compute(settings, BoundingBox(), null);
        File.WriteAllText(source, "{\"dataset\":\"two\"}");
        string changed = Compute(settings, BoundingBox(), null);
        File.Delete(source);

        Assert.NotEqual(first, changed);
        Assert.ThrowsAny<IOException>(() => Compute(settings, BoundingBox(), null));
    }

    public void Dispose()
    {
        if (Directory.Exists(temporaryDirectory))
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static string Compute(
        RevitSettings settings,
        AreaOfInterest aoi,
        AddressParcelProvenance? addressParcel,
        string documentGuid = "document-a",
        TargetProjectLevel? level = null,
        long toposolidTypeId = 11,
        double shortCurveToleranceInternal = 0.01d,
        double vertexToleranceInternal = 0.001d,
        long credentialRevision = 8)
        => PreparedTerrainBinding.Compute(
            settings,
            aoi,
            addressParcel,
            documentGuid,
            level ?? new TargetProjectLevel(7, "level-7", "Level 7", 2d),
            toposolidTypeId,
            shortCurveToleranceInternal,
            vertexToleranceInternal,
            credentialRevision);

    private static RevitSettings FetchSettings() => new(
        new TerrainRequestSettings
        {
            Mode = TerrainAcquisitionMode.Fetch,
            AreaOfInterest = new AoiSettings
            {
                Kind = AreaOfInterestKind.BoundingBox,
                BoundingBox = new BoundingBoxAoiSettings { West = -90d, South = 40d, East = -89d, North = 41d },
            },
            LocalOrigin = new LocalOriginRequest(LocalOriginKind.Centroid, 0d, 0d, 0d),
            OutputUnit = LengthUnit.UsSurveyFoot,
            Simplification = new SimplificationSettings { Method = SimplificationMethod.CurvatureAware, PointBudget = 1_000, CoverageFloorFraction = 0.2d },
            Output = new OutputSettings { Directory = "C:\\output", BaseName = "terrain" },
            NetworkTimeoutSeconds = 300,
        },
        new RevitTargetSettings("Level 7", "Toposolid"),
        new RevitSharedCoordinatesSettings(false),
        new RevitAddressAndParcelSettings(AddressGeocoderProvider.Census, null, null, null, null, null, null),
        TerrainExtensionMeters: 1d,
        DistanceDisplayFormat: DistanceDisplayFormat.UsSurveyFeet);

    private static RevitSettings ProcessSettings(string asc, string prj, string? sourceJson) => FetchSettings() with
    {
        Request = FetchSettings().Request with
        {
            Mode = TerrainAcquisitionMode.Process,
            Process = new ProcessInputSettings
            {
                Asc = asc,
                Prj = prj,
                SourceJson = sourceJson,
                SourceName = "Synthetic source",
                Dataset = "synthetic-dataset",
                VerticalDatum = "NAVD88",
                VerticalUnit = LengthUnit.Meter,
                Geoid = "GEOID18",
                CollectionStart = "2026-01-01",
                CollectionEnd = "2026-01-02",
                QualityLevel = "synthetic",
            },
        },
    };

    private static Wgs84BoundingBoxAoi BoundingBox() => new(-90d, 40d, -89d, 41d);

    private static AddressParcelProvenance AddressParcel(string queryText = "synthetic query A", DateOnly? retrievalDate = null) => new(
        retrievalDate ?? new DateOnly(2026, 10, 1),
        new GeocodeProvenance(AddressGeocoderProvider.Census, queryText, "Census attribution"),
        new ParcelProvenance(ParcelBoundarySourceKind.LocalParcelFile, "Synthetic source", "parcel-1", "stable-1", "Lot 1", "Synthetic license"));
}
