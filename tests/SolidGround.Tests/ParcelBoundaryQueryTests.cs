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
}
