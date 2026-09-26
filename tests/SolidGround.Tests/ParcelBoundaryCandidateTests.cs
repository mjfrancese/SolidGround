using SolidGround.Core.Aois;
using SolidGround.Core.Metadata;
using SolidGround.Core.Sources;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class ParcelBoundaryCandidateTests
{
    private const string GeographicSquareWkt = "POLYGON((-93.6041 41.5910, -93.6036 41.5910, -93.6036 41.5914, -93.6041 41.5914, -93.6041 41.5910))";
    private const string ProjectedSquareWkt = "POLYGON((449655.33 4604544.62, 449695.33 4604544.62, 449695.33 4604584.62, 449655.33 4604584.62, 449655.33 4604544.62))";

    [Fact]
    public void ConstructorRejectsANonGeographicBoundary()
    {
        PolygonalRegion projectedBoundary = ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, ProjectedSquareWkt, ProjectedReference());

        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            projectedBoundary, "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer."));
    }

    [Fact]
    public void ConstructorRejectsANullBoundary()
    {
        Assert.Throws<ArgumentNullException>(() => new ParcelBoundaryCandidate(
            null!, "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsABlankParcelId(string parcelId)
    {
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), parcelId, 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer."));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConstructorRejectsANonPositiveOrNonFiniteComputedArea(double computedAreaSquareMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", computedAreaSquareMeters, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsABlankSourceIdentity(string sourceIdentity)
    {
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, sourceIdentity, "Test disclaimer."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsABlankLicenseDisclaimerText(string disclaimer)
    {
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", disclaimer));
    }

    [Fact]
    public void ConstructorRejectsAnUndefinedSourceKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, (ParcelBoundarySourceKind)99, "Test Source", "Test disclaimer."));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsABlankOptionalStringWhenSupplied(string blankValue)
    {
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            situsAddress: blankValue));
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            subdivision: blankValue));
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            legalDescription: blankValue));
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            zoning: blankValue));
        Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            stableParcelId: blankValue));
    }

    [Fact]
    public void ConstructorReportsTheRealParameterNameWhenAnOptionalStringIsBlank()
    {
        // Each optional string's blank check must name its own constructor parameter, not a shared
        // wrapper's own local -- otherwise every one of these would report "value" regardless of which
        // field was actually blank.
        ArgumentException situsAddressError = Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            situsAddress: "   "));
        Assert.Equal("situsAddress", situsAddressError.ParamName);

        ArgumentException zoningError = Assert.Throws<ArgumentException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            zoning: "   "));
        Assert.Equal("zoning", zoningError.ParamName);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void ConstructorRejectsANonPositiveOrNonFiniteReportedAcresWhenSupplied(double reportedAcres)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelBoundaryCandidate(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.",
            reportedAcres: reportedAcres));
    }

    [Fact]
    public void OptionalStringsDefaultToNull()
    {
        ParcelBoundaryCandidate candidate = new(
            GeographicBoundary(), "PARCEL-1", 100d, ParcelBoundarySourceKind.CountyRegistry, "Test Source", "Test disclaimer.");

        Assert.Null(candidate.SitusAddress);
        Assert.Null(candidate.Subdivision);
        Assert.Null(candidate.Lot);
        Assert.Null(candidate.Block);
        Assert.Null(candidate.Plat);
        Assert.Null(candidate.Book);
        Assert.Null(candidate.Page);
        Assert.False(candidate.BookPageAreUnconfirmedProxies);
        Assert.Null(candidate.LegalDescription);
        Assert.Null(candidate.ReportedAcres);
        Assert.Null(candidate.Zoning);
        Assert.Null(candidate.StableParcelId);
    }

    [Theory]
    [InlineData(ParcelBoundarySourceKind.CountyRegistry)]
    [InlineData(ParcelBoundarySourceKind.LocalParcelFile)]
    public void AccuracyLabelIsAlwaysTheFixedConstantRegardlessOfSourceKind(ParcelBoundarySourceKind sourceKind)
    {
        ParcelBoundaryCandidate candidate = new(
            GeographicBoundary(), "PARCEL-1", 100d, sourceKind, "Test Source", "Test disclaimer.");

        Assert.Equal(ParcelBoundaryCandidate.NotASurveyDisclaimer, candidate.AccuracyLabel);
        Assert.Equal("This boundary is a cadastral/assessor tax-map representation, not a survey.", candidate.AccuracyLabel);
    }

    [Fact]
    public void ParcelBoundaryAcquisitionAllowsZeroCandidates()
    {
        // Direct test of the documented divergence from AddressGeocodeAcquisition, which requires at least
        // one candidate: a parcel lookup's empty result is a normal outcome, never an exceptional one.
        ParcelBoundaryAcquisition acquisition = new([]);

        Assert.Empty(acquisition.Candidates);
        Assert.False(acquisition.ResultSetTruncated);
    }

    [Fact]
    public void ParcelBoundaryAcquisitionRejectsANullCandidateList()
    {
        Assert.Throws<ArgumentNullException>(() => new ParcelBoundaryAcquisition(null!));
    }

    [Fact]
    public void ParcelBoundaryAcquisitionCarriesResultSetTruncated()
    {
        ParcelBoundaryAcquisition acquisition = new([], resultSetTruncated: true);

        Assert.True(acquisition.ResultSetTruncated);
    }

    private static PolygonalRegion GeographicBoundary() =>
        ParcelGeometryParser.Parse(ParcelGeometryFormat.Wkt, GeographicSquareWkt, GeographicReference());

    private static HorizontalReference GeographicReference() => new(
        "EPSG:4326", "WGS84", HorizontalReferenceKind.Geographic, HorizontalUnit.DecimalDegrees, HorizontalAxisOrder.LongitudeLatitude);

    private static HorizontalReference ProjectedReference() => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(LengthUnit.Meter), HorizontalAxisOrder.EastingNorthing);
}
