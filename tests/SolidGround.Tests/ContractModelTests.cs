using SolidGround.Core.Aois;
using SolidGround.Core.Exports;
using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Simplification;
using SolidGround.Core.Sources;
using SolidGround.Core.Terrain;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ContractModelTests
{
    [Fact]
    public void ParcelAoiCarriesItsHorizontalReferenceAndRejectsMissingAoi()
    {
        ParcelGeometryAoi parcel = new(
            ParcelGeometryFormat.Wkt,
            "POLYGON ((-93.61 41.58, -93.60 41.58, -93.60 41.60, -93.61 41.60, -93.61 41.58))",
            GeographicReference(),
            LinearDistance.Meters(2.5d));

        Assert.Equal(HorizontalReferenceKind.Geographic, parcel.HorizontalReference.Kind);
        Assert.Equal(LinearDistance.Meters(2.5d), parcel.Buffer);
        Assert.Throws<ArgumentNullException>(() => new ElevationSourceRequest(null!));
    }

    [Fact]
    public void ParcelAoiDefaultsItsBufferToZeroWhenNoneIsSupplied()
    {
        ParcelGeometryAoi parcel = new(
            ParcelGeometryFormat.Wkt,
            "POLYGON ((-93.61 41.58, -93.60 41.58, -93.60 41.60, -93.61 41.60, -93.61 41.58))",
            GeographicReference());

        Assert.Equal(LinearDistance.Zero, parcel.Buffer);
    }

    [Fact]
    public void RadiusAoiCarriesItsLinearDistanceAndRejectsANonPositiveRadius()
    {
        Wgs84RadiusAoi radius = new(41.591194, -93.603806, LinearDistance.Meters(100d));

        Assert.Equal(LinearDistance.Meters(100d), radius.Radius);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Wgs84RadiusAoi(41.591194, -93.603806, LinearDistance.Zero));
    }

    [Fact]
    public void GridUsesNullableMissingCellsAndDeterministicAaiGridCellCenters()
    {
        double?[,] input = { { 100d, null }, { 99d, 98d } };
        ElevationGrid grid = new(ProjectedReference(), VerticalReference(), new Coordinate2D(10d, 20d), 2d, 2d, GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, input);
        input[0, 0] = 999d;

        Assert.Equal(100d, grid.GetElevation(0, 0));
        Assert.Null(grid.GetElevation(0, 1));
        Assert.Equal(new Coordinate2D(11d, 23d), grid.GetCellCenter(0, 0));
        Assert.Equal(new Coordinate2D(11d, 21d), grid.GetCellCenter(1, 0));
    }

    [Fact]
    public void GridComputesItsCornerEnvelopeConsistentlyWithBothAnchorConventions()
    {
        double?[,] input = { { 1d, 2d }, { 3d, 4d }, { 5d, 6d } };

        ElevationGrid cornerAnchored = new(ProjectedReference(), VerticalReference(), new Coordinate2D(10d, 20d), 2d, 3d, GridAnchorConvention.LowerLeftCorner, GridRowOrder.NorthToSouth, input);
        PlanarEnvelope cornerEnvelope = cornerAnchored.GetCornerEnvelope();

        Assert.Equal(10d, cornerEnvelope.MinX);
        Assert.Equal(20d, cornerEnvelope.MinY);
        Assert.Equal(14d, cornerEnvelope.MaxX);
        Assert.Equal(29d, cornerEnvelope.MaxY);

        ElevationGrid centerAnchored = new(ProjectedReference(), VerticalReference(), new Coordinate2D(10d, 20d), 2d, 3d, GridAnchorConvention.CellCenter, GridRowOrder.NorthToSouth, input);
        PlanarEnvelope centerEnvelope = centerAnchored.GetCornerEnvelope();

        Assert.Equal(9d, centerEnvelope.MinX);
        Assert.Equal(18.5d, centerEnvelope.MinY);
        Assert.Equal(13d, centerEnvelope.MaxX);
        Assert.Equal(27.5d, centerEnvelope.MaxY);
    }

    [Fact]
    public void LocalFrameRoundTripsMixedHorizontalAndVerticalUnits()
    {
        LocalCoordinateFrame frame = new(new Coordinate3D(100d, 200d, 300d), ProjectedReference(LengthUnit.UsSurveyFoot), VerticalReference(LengthUnit.InternationalFoot), LengthUnit.Meter);
        Coordinate3D source = new(101d, 202d, 304d);

        LocalCoordinate local = frame.ToLocal(source);

        Assert.Equal(1200d / 3937d, local.X, 12);
        Assert.Equal(2d * (1200d / 3937d), local.Y, 12);
        Assert.Equal(1.2192d, local.Elevation, 12);
        Assert.Equal(source, frame.ToSource(local));
    }

    [Fact]
    public void LocalFrameDefaultsItsOutputUnitToUsSurveyFootWhenNoneIsSupplied()
    {
        LocalCoordinateFrame frame = new(new Coordinate3D(0d, 0d, 0d), ProjectedReference(), VerticalReference());

        Assert.Equal(LengthUnit.UsSurveyFoot, frame.OutputUnit);
    }

    [Fact]
    public void LocalFrameRejectsGeographicHorizontalReferences()
    {
        Assert.Throws<ArgumentException>(() => new LocalCoordinateFrame(new Coordinate3D(0d, 0d, 0d), GeographicReference(), VerticalReference(), LengthUnit.UsSurveyFoot));
    }

    [Fact]
    public void HorizontalReferenceRequiresAnAxisOrderCompatibleWithItsKind()
    {
        Assert.Throws<ArgumentException>(() => new HorizontalReference("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.EastingNorthing));
        Assert.Throws<ArgumentException>(() => new HorizontalReference("EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.LongitudeLatitude));
        Assert.Equal(HorizontalAxisOrder.LongitudeLatitude, GeographicReference().AxisOrder);
        Assert.Equal(HorizontalAxisOrder.EastingNorthing, ProjectedReference().AxisOrder);
    }

    [Fact]
    public void SourceMetadataAcceptsAValidCollectionPeriodAndRejectsABlankQualityLevel()
    {
        CollectionPeriod period = new(new DateOnly(2017, 2, 17), new DateOnly(2017, 2, 27));
        ElevationSourceMetadata metadata = new("OpenTopography", "USGS1m", period, "QL2");

        Assert.Equal(period, metadata.CollectionPeriod);
        Assert.Throws<ArgumentException>(() => new CollectionPeriod(new DateOnly(2017, 2, 27), new DateOnly(2017, 2, 17)));
        Assert.Throws<ArgumentException>(() => new ElevationSourceMetadata("source", "dataset", period, " "));
    }

    [Fact]
    public void SourceMetadataAcceptsAnAbsentCollectionPeriodAndQualityLevel()
    {
        ElevationSourceMetadata metadata = new("OpenTopography", "USGS1m", null, null);

        Assert.Null(metadata.CollectionPeriod);
        Assert.Null(metadata.QualityLevel);

        ElevationSourceMetadata defaulted = new("OpenTopography", "USGS1m");
        Assert.Null(defaulted.CollectionPeriod);
        Assert.Null(defaulted.QualityLevel);
    }

    [Fact]
    public void SimplificationResultCarriesRequestAndEnforcesRequestedBudget()
    {
        TerrainSample[] retained = [new(new Coordinate3D(1d, 2d, 3d))];
        SimplificationRequest request = new(1, SimplificationMethod.CurvatureAware);
        SimplificationResult result = new(3, retained, request);

        Assert.Equal(request, result.Request);
        Assert.Equal(2, result.RemovedPointCount);
        Assert.True(typeof(ITerrainSimplifier).IsInterface);
        Assert.Throws<ArgumentException>(() => new SimplificationResult(3, retained.Append(new TerrainSample(new Coordinate3D(4d, 5d, 6d))), request));
    }

    [Fact]
    public void ProvenanceKeepsSourceReferencesConsistentWithTheLocalFrame()
    {
        TerrainProvenance provenance = CreateProvenance(retainedPointCount: 1);

        Assert.Equal("EPSG:4326", provenance.SourceHorizontalReference.CoordinateReferenceSystem);
        Assert.Equal("NAVD88", provenance.SourceVerticalReference.Datum);
        Assert.Equal(LengthUnit.InternationalFoot, provenance.ElevationRange!.Unit);
        Assert.Equal(15000, provenance.SimplificationRequest.PointBudget);
        Assert.Throws<ArgumentException>(() => CreateProvenance(1, new VerticalReference("NGVD29", LengthUnit.InternationalFoot)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateProvenance(2, simplificationRequest: new SimplificationRequest(1)));
    }

    [Fact]
    public void ProvenanceRetainsVersionedForwardAndInverseTransformationMetadata()
    {
        TerrainProvenance provenance = CreateProvenance(retainedPointCount: 1);

        Assert.Equal("PROJJSON", provenance.HorizontalTransformation.ForwardOperation.Format);
        Assert.Equal("inverse operation", provenance.HorizontalTransformation.InverseOperation.Definition);
        Assert.Equal("candidate-engine", provenance.HorizontalTransformation.EngineName);
        Assert.Throws<ArgumentException>(() => CreateProvenance(1, transformationTarget: ProjectedReference(LengthUnit.UsSurveyFoot)));
    }

    [Fact]
    public void ExportPayloadRequiresTheRetainedProvenanceCountAndReceiptIdentifier()
    {
        TerrainProvenance provenance = CreateProvenance(retainedPointCount: 1);
        TerrainExportPayload payload = new([new LocalTerrainSample(new LocalCoordinate(0d, 0d, 0d))], provenance);

        Assert.Single(payload.Samples);
        Assert.Throws<ArgumentException>(() => new TerrainExportPayload([], provenance));
        Assert.Throws<ArgumentException>(() => new TerrainExportReceipt(" "));
    }

    [Fact]
    public void AddressParcelProvenanceRequiresAtLeastOneOfGeocodeOrParcel()
    {
        Assert.Throws<ArgumentException>(() => new AddressParcelProvenance(new DateOnly(2026, 9, 21), null, null));
    }

    [Fact]
    public void AddressParcelProvenanceAcceptsGeocodeOnlyWithNoParcel()
    {
        GeocodeProvenance geocode = CreateGeocodeProvenance();
        AddressParcelProvenance addressParcel = new(new DateOnly(2026, 9, 21), geocode, null);

        Assert.Equal(geocode, addressParcel.Geocode);
        Assert.Null(addressParcel.Parcel);
    }

    [Fact]
    public void AddressParcelProvenanceAcceptsParcelOnlyWithNoGeocode()
    {
        ParcelProvenance parcel = CreateParcelProvenance();
        AddressParcelProvenance addressParcel = new(new DateOnly(2026, 9, 21), null, parcel);

        Assert.Null(addressParcel.Geocode);
        Assert.Equal(parcel, addressParcel.Parcel);
    }

    [Theory]
    [InlineData(" ", "This product uses the Census Bureau Data API but is not endorsed or certified by the Census Bureau.")]
    [InlineData("100 Example Loop", " ")]
    public void GeocodeProvenanceRejectsBlankQueryTextOrBlankAttribution(string queryText, string attribution)
    {
        Assert.Throws<ArgumentException>(() => new GeocodeProvenance(AddressGeocoderProvider.Census, queryText, attribution));
    }

    [Fact]
    public void GeocodeProvenanceRejectsAnUndefinedProviderValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeocodeProvenance(
            (AddressGeocoderProvider)999,
            "100 Example Loop",
            "This product uses the Census Bureau Data API but is not endorsed or certified by the Census Bureau."));
    }

    [Fact]
    public void ParcelProvenanceRejectsBlankRequiredStringsButAllowsNullOptionalStrings()
    {
        Assert.Throws<ArgumentException>(() => new ParcelProvenance(
            ParcelBoundarySourceKind.CountyRegistry, " ", "99-99-999-999", null, null, "Synthetic fixture data; no real license applies."));
        Assert.Throws<ArgumentException>(() => new ParcelProvenance(
            ParcelBoundarySourceKind.CountyRegistry, "Synthetic County (fixture only) (GEOID 99999)", " ", null, null, "Synthetic fixture data; no real license applies."));
        Assert.Throws<ArgumentException>(() => new ParcelProvenance(
            ParcelBoundarySourceKind.CountyRegistry, "Synthetic County (fixture only) (GEOID 99999)", "99-99-999-999", null, null, " "));

        ParcelProvenance parcel = new(
            ParcelBoundarySourceKind.CountyRegistry,
            "Synthetic County (fixture only) (GEOID 99999)",
            "99-99-999-999",
            stableParcelId: null,
            legalDescription: null,
            licenseDisclaimerText: "Synthetic fixture data; no real license applies.");

        Assert.Null(parcel.StableParcelId);
        Assert.Null(parcel.LegalDescription);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParcelProvenanceRejectsABlankOptionalStringWhenSupplied(string blankValue)
    {
        // Mirrors ParcelBoundaryCandidateTests.ConstructorRejectsABlankOptionalStringWhenSupplied: null is
        // allowed for these two optional strings, but a supplied blank value must still be rejected. The
        // test above only proves the null-is-allowed half of that contract.
        Assert.Throws<ArgumentException>(() => new ParcelProvenance(
            ParcelBoundarySourceKind.CountyRegistry,
            "Synthetic County (fixture only) (GEOID 99999)",
            "99-99-999-999",
            stableParcelId: blankValue,
            legalDescription: null,
            licenseDisclaimerText: "Synthetic fixture data; no real license applies."));
        Assert.Throws<ArgumentException>(() => new ParcelProvenance(
            ParcelBoundarySourceKind.CountyRegistry,
            "Synthetic County (fixture only) (GEOID 99999)",
            "99-99-999-999",
            stableParcelId: null,
            legalDescription: blankValue,
            licenseDisclaimerText: "Synthetic fixture data; no real license applies."));
    }

    [Fact]
    public void ParcelProvenanceRejectsAnUndefinedSourceKindValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelProvenance(
            (ParcelBoundarySourceKind)999,
            "Synthetic County (fixture only) (GEOID 99999)",
            "99-99-999-999",
            null,
            null,
            "Synthetic fixture data; no real license applies."));
    }

    [Fact]
    public void TerrainProvenanceAddressParcelIsNullByDefaultAndSettableWhenSupplied()
    {
        TerrainProvenance defaulted = CreateProvenance(retainedPointCount: 1);
        Assert.Null(defaulted.AddressParcel);

        AddressParcelProvenance addressParcel = new(new DateOnly(2026, 9, 21), CreateGeocodeProvenance(), null);
        TerrainProvenance withAddressParcel = CreateProvenance(retainedPointCount: 1, addressParcel: addressParcel);

        Assert.Equal(addressParcel, withAddressParcel.AddressParcel);
    }

    private static TerrainProvenance CreateProvenance(
        int retainedPointCount,
        VerticalReference? sourceVerticalReference = null,
        SimplificationRequest? simplificationRequest = null,
        HorizontalReference? transformationTarget = null,
        AddressParcelProvenance? addressParcel = null)
    {
        VerticalReference vertical = VerticalReference();
        return new TerrainProvenance(
            1,
            new ElevationSourceMetadata("OpenTopography", "USGS1m", new CollectionPeriod(new DateOnly(2017, 2, 17), new DateOnly(2017, 2, 27)), "QL2"),
            Transformation(transformationTarget),
            sourceVerticalReference ?? vertical,
            ReferenceOrigin.Operator,
            ReferenceOrigin.Operator,
            new LocalCoordinateFrame(new Coordinate3D(10d, 20d, 30d), ProjectedReference(), vertical, LengthUnit.UsSurveyFoot),
            simplificationRequest ?? new SimplificationRequest(),
            5,
            retainedPointCount,
            new ElevationRange(1d, 3d, LengthUnit.InternationalFoot),
            addressParcel);
    }

    private static GeocodeProvenance CreateGeocodeProvenance() => new(
        AddressGeocoderProvider.Census,
        "100 Example Loop",
        "This product uses the Census Bureau Data API but is not endorsed or certified by the Census Bureau.");

    private static ParcelProvenance CreateParcelProvenance() => new(
        ParcelBoundarySourceKind.LocalParcelFile,
        "Local Regrid Standard export (fixture only)",
        "99-99-999-999",
        "00000000-0000-0000-0000-000000000000",
        null,
        "Synthetic fixture data; no real license applies.");

    private static HorizontalReference GeographicReference() => new("EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference(LengthUnit unit = LengthUnit.Meter) => new("EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(unit), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit = LengthUnit.InternationalFoot) => new("NAVD88", unit, "Geoid12B");

    private static HorizontalTransformationDefinition Transformation(HorizontalReference? target = null) => new(
        GeographicReference(),
        target ?? ProjectedReference(),
        new CoordinateOperationDefinition("PROJJSON", "forward operation"),
        new CoordinateOperationDefinition("PROJJSON", "inverse operation"),
        "candidate-engine",
        "1.0");
}
