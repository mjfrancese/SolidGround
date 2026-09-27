using SolidGround.Core.Sources;

namespace SolidGround.Tests;

public sealed class ParcelBoundaryQueryTests
{
    [Theory]
    [InlineData(double.NaN, -93.603806d)]
    [InlineData(90.0001d, -93.603806d)]
    [InlineData(-90.0001d, -93.603806d)]
    public void ParcelPointQueryRejectsAnOutOfRangeOrNonFiniteLatitude(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelPointQuery(latitude, longitude));
    }

    [Theory]
    [InlineData(41.591194d, double.NaN)]
    [InlineData(41.591194d, 180.0001d)]
    [InlineData(41.591194d, -180.0001d)]
    public void ParcelPointQueryRejectsAnOutOfRangeOrNonFiniteLongitude(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelPointQuery(latitude, longitude));
    }

    [Fact]
    public void ParcelPointQueryAcceptsThePublicExampleSiteCoordinate()
    {
        var query = new ParcelPointQuery(41.591194d, -93.603806d);

        Assert.Equal(41.591194d, query.Latitude);
        Assert.Equal(-93.603806d, query.Longitude);
        Assert.IsAssignableFrom<ParcelBoundaryQuery>(query);
    }

    [Fact]
    public void ParcelAddressQueryRejectsANullSearchText()
    {
        Assert.Throws<ArgumentNullException>(() => new ParcelAddressQuery(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ParcelAddressQueryRejectsABlankSearchText(string searchText)
    {
        Assert.Throws<ArgumentException>(() => new ParcelAddressQuery(searchText));
    }

    [Fact]
    public void ParcelAddressQueryAcceptsANonBlankSearchText()
    {
        var query = new ParcelAddressQuery("100 Example Loop");

        Assert.Equal("100 Example Loop", query.SearchText);
        Assert.IsAssignableFrom<ParcelBoundaryQuery>(query);
    }

    // ------------------------------------------------------------------------------------------------
    // ParcelNearbyQuery (nearby-parcel fallback tier, SolidGround Issue #31 follow-up: a geocoded point
    // commonly lands a few meters outside its true parcel). Mirrors ParcelPointQuery's own validation
    // exactly, plus a radius bound.
    // ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(double.NaN, -93.603806d, 30d)]
    [InlineData(90.0001d, -93.603806d, 30d)]
    [InlineData(-90.0001d, -93.603806d, 30d)]
    public void ParcelNearbyQueryRejectsAnOutOfRangeOrNonFiniteLatitude(double latitude, double longitude, double radiusMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelNearbyQuery(latitude, longitude, radiusMeters));
    }

    [Theory]
    [InlineData(41.591194d, double.NaN, 30d)]
    [InlineData(41.591194d, 180.0001d, 30d)]
    [InlineData(41.591194d, -180.0001d, 30d)]
    public void ParcelNearbyQueryRejectsAnOutOfRangeOrNonFiniteLongitude(double latitude, double longitude, double radiusMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelNearbyQuery(latitude, longitude, radiusMeters));
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ParcelNearbyQueryRejectsANonFiniteOrNonPositiveRadius(double radiusMeters)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParcelNearbyQuery(41.591194d, -93.603806d, radiusMeters));
    }

    [Fact]
    public void ParcelNearbyQueryAcceptsThePublicExampleSiteCoordinateAndADocumentedRadius()
    {
        var query = new ParcelNearbyQuery(41.591194d, -93.603806d, 30d);

        Assert.Equal(41.591194d, query.Latitude);
        Assert.Equal(-93.603806d, query.Longitude);
        Assert.Equal(30d, query.RadiusMeters);
        Assert.IsAssignableFrom<ParcelBoundaryQuery>(query);
    }
}
