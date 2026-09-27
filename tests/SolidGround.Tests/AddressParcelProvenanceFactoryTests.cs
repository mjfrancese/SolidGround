using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Provenance;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests <see cref="AddressParcelProvenanceFactory.Create"/> against every observable behavior SolidGround
/// Issue #31 (PH3-4)'s interactive dialog needs from it: building the geocode half only when an address was
/// actually geocoded (not when the operator entered a "latitude, longitude" pair directly), the parcel half
/// from whatever candidate was confirmed, a caller-supplied retrieval date (never a clock read inside this
/// factory), and returning null when neither half applies. See
/// docs/architecture/revit-interactive-dialog.md "AOI and provenance: two AOI paths, dialog-resolved or
/// settings-driven".
/// </summary>
public sealed class AddressParcelProvenanceFactoryTests
{
    private static readonly DateOnly FixedRetrievalDate = new(2026, 9, 27);

    [Fact]
    public void ReturnsNullWhenNeitherAGeocodeNorAParcelCandidateIsSupplied()
    {
        AddressParcelProvenance? result = AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: false,
            AddressGeocoderProvider.Census,
            addressQueryText: null,
            selectedGeocodeCandidate: null,
            selectedParcelCandidate: null);

        Assert.Null(result);
    }

    [Fact]
    public void BuildsBothHalvesWhenAnAddressWasGeocodedAndAParcelWasConfirmed()
    {
        AddressGeocodeCandidate geocodeCandidate = GeocodeCandidate();
        ParcelBoundaryCandidate parcelCandidate = ParcelCandidate();

        AddressParcelProvenance? result = AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: true,
            AddressGeocoderProvider.Census,
            addressQueryText: "100 EXAMPLE LOOP",
            selectedGeocodeCandidate: geocodeCandidate,
            selectedParcelCandidate: parcelCandidate);

        Assert.NotNull(result);
        Assert.Equal(FixedRetrievalDate, result.RetrievalDate);

        Assert.NotNull(result.Geocode);
        Assert.Equal(AddressGeocoderProvider.Census, result.Geocode.Provider);
        Assert.Equal("100 EXAMPLE LOOP", result.Geocode.QueryText);
        Assert.Equal(geocodeCandidate.Attribution, result.Geocode.Attribution);

        Assert.NotNull(result.Parcel);
        Assert.Equal(parcelCandidate.SourceKind, result.Parcel.SourceKind);
        Assert.Equal(parcelCandidate.SourceIdentity, result.Parcel.SourceIdentity);
        Assert.Equal(parcelCandidate.ParcelId, result.Parcel.ParcelId);
        Assert.Equal(parcelCandidate.StableParcelId, result.Parcel.StableParcelId);
        Assert.Equal(parcelCandidate.LegalDescription, result.Parcel.LegalDescription);
        Assert.Equal(parcelCandidate.LicenseDisclaimerText, result.Parcel.LicenseDisclaimerText);
    }

    [Fact]
    public void OmitsTheGeocodeHalfWhenTheOperatorEnteredCoordinatesDirectlyInsteadOfAnAddress()
    {
        // A direct "latitude, longitude" entry produces a synthetic AddressGeocodeCandidate standing in for
        // the point (see LatitudeLongitudePointParser/SolidGroundDialogViewModel), but addressWasGeocoded is
        // false in that case -- no IAddressGeocoder was ever actually called, so no GeocodeProvenance (which
        // names a real provider) should be recorded, even though a parcel was confirmed at that point.
        ParcelBoundaryCandidate parcelCandidate = ParcelCandidate();

        AddressParcelProvenance? result = AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: false,
            AddressGeocoderProvider.Census,
            addressQueryText: null,
            selectedGeocodeCandidate: GeocodeCandidate(),
            selectedParcelCandidate: parcelCandidate);

        Assert.NotNull(result);
        Assert.Null(result.Geocode);
        Assert.NotNull(result.Parcel);
        Assert.Equal(parcelCandidate.ParcelId, result.Parcel.ParcelId);
    }

    [Fact]
    public void OmitsTheParcelHalfWhenNoParcelCandidateWasConfirmed()
    {
        AddressGeocodeCandidate geocodeCandidate = GeocodeCandidate();

        AddressParcelProvenance? result = AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: true,
            AddressGeocoderProvider.Esri,
            addressQueryText: "100 EXAMPLE LOOP",
            selectedGeocodeCandidate: geocodeCandidate,
            selectedParcelCandidate: null);

        Assert.NotNull(result);
        Assert.NotNull(result.Geocode);
        Assert.Equal(AddressGeocoderProvider.Esri, result.Geocode.Provider);
        Assert.Null(result.Parcel);
    }

    [Fact]
    public void ThrowsWhenAddressWasGeocodedButNoGeocodeCandidateIsSupplied()
    {
        Assert.Throws<ArgumentNullException>(() => AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: true,
            AddressGeocoderProvider.Census,
            addressQueryText: "100 EXAMPLE LOOP",
            selectedGeocodeCandidate: null,
            selectedParcelCandidate: null));
    }

    [Fact]
    public void ThrowsWhenAddressWasGeocodedButTheQueryTextIsNull()
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws the ArgumentNullException subtype for a null
        // value specifically, so this case is a separate assertion from the blank-but-non-null cases below
        // (xUnit's Assert.Throws<T> requires an exact type match, not merely an assignable one).
        Assert.Throws<ArgumentNullException>(() => AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: true,
            AddressGeocoderProvider.Census,
            addressQueryText: null,
            selectedGeocodeCandidate: GeocodeCandidate(),
            selectedParcelCandidate: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ThrowsWhenAddressWasGeocodedButTheQueryTextIsBlank(string addressQueryText)
    {
        Assert.Throws<ArgumentException>(() => AddressParcelProvenanceFactory.Create(
            FixedRetrievalDate,
            addressWasGeocoded: true,
            AddressGeocoderProvider.Census,
            addressQueryText,
            selectedGeocodeCandidate: GeocodeCandidate(),
            selectedParcelCandidate: null));
    }

    private static AddressGeocodeCandidate GeocodeCandidate() => new(
        41.591194, -93.603806, "100 EXAMPLE LOOP", "Test attribution.", "Exact");

    private static ParcelBoundaryCandidate ParcelCandidate() => new(
        GeographicBoundary(),
        "PARCEL-1",
        1600d,
        ParcelBoundarySourceKind.CountyRegistry,
        "Synthetic County (fixture only) (GEOID 99999)",
        "Test disclaimer.",
        legalDescription: "LOT 1 EXAMPLE SUBDIVISION",
        stableParcelId: "STABLE-1");

    private static PolygonalRegion GeographicBoundary() =>
        ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, GeographicSquareWkt, GeographicReference());

    private const string GeographicSquareWkt =
        "POLYGON((-93.6041 41.5910, -93.6036 41.5910, -93.6036 41.5914, -93.6041 41.5914, -93.6041 41.5910))";

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);
}
