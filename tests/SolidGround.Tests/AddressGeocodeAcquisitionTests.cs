using SolidGround.Core.Sources;

namespace SolidGround.Tests;

public sealed class AddressGeocodeAcquisitionTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RequestConstructorRejectsNullOrBlankAddress(string? address)
    {
        // ArgumentException.ThrowIfNullOrWhiteSpace throws ArgumentNullException for a literal null and
        // plain ArgumentException for "" or whitespace; ThrowsAny accepts either, since both are the caller's
        // signal that the argument was rejected.
        Assert.ThrowsAny<ArgumentException>(() => new AddressGeocodeRequest(address!));
    }

    [Theory]
    [InlineData(-90.5, 0d)]
    [InlineData(90.5, 0d)]
    [InlineData(double.NaN, 0d)]
    [InlineData(double.PositiveInfinity, 0d)]
    [InlineData(0d, -180.5)]
    [InlineData(0d, 180.5)]
    [InlineData(0d, double.NaN)]
    [InlineData(0d, double.NegativeInfinity)]
    public void CandidateConstructorRejectsOutOfRangeLatitudeOrLongitude(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddressGeocodeCandidate(latitude, longitude, "matched", "attribution"));
    }

    [Theory]
    [InlineData(null, "attribution")]
    [InlineData("", "attribution")]
    [InlineData("matched", null)]
    [InlineData("matched", "")]
    public void CandidateConstructorRejectsBlankMatchedAddressOrAttribution(string? matchedAddress, string? attribution)
    {
        // See RequestConstructorRejectsNullOrBlankAddress's comment: ThrowIfNullOrWhiteSpace throws
        // ArgumentNullException for null and ArgumentException for "".
        Assert.ThrowsAny<ArgumentException>(() => new AddressGeocodeCandidate(0d, 0d, matchedAddress!, attribution!));
    }

    [Fact]
    public void CandidateConstructorAcceptsNullScoreAndNullPrecisionLabel()
    {
        // Reuses the public example-site coordinate (AGENTS.md "Test fixture and verification"), the only
        // real-world-shaped coordinate this file may contain.
        var candidate = new AddressGeocodeCandidate(41.591194, -93.603806, "matched", "attribution");

        Assert.Null(candidate.Score);
        Assert.Null(candidate.PrecisionLabel);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CandidateConstructorRejectsNonFiniteScore(double score)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AddressGeocodeCandidate(0d, 0d, "matched", "attribution", score: score));
    }

    [Fact]
    public void AcquisitionConstructorRejectsAnEmptyCandidateList()
    {
        Assert.Throws<ArgumentException>(() => new AddressGeocodeAcquisition([]));
    }

    [Fact]
    public void AcquisitionConstructorRejectsNullCandidateList()
    {
        Assert.Throws<ArgumentNullException>(() => new AddressGeocodeAcquisition(null!));
    }
}
