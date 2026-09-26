using SolidGround.Core.Sources;

namespace SolidGround.Tests;

public sealed class OwnerFieldNameGuardTests
{
    [Theory]
    [InlineData("OWNER_NAME")]
    [InlineData("OWN_ADD")]
    [InlineData("OWN_CITY")]
    [InlineData("OWN_STATE")]
    [InlineData("OWN_ZIP")]
    [InlineData("CAREOF")]
    [InlineData("Owner_Name")]
    public void FiresOnEveryAc2NamedToken(string fieldName)
    {
        Assert.True(OwnerFieldNameGuard.IsOwnerLike(fieldName));
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("owntype")]
    [InlineData("ownfrst")]
    [InlineData("ownlast")]
    [InlineData("owner2")]
    [InlineData("previous_owner")]
    [InlineData("unmodified_owner")]
    [InlineData("mailadd")]
    [InlineData("mail_city")]
    [InlineData("mail_state2")]
    [InlineData("mail_zip")]
    [InlineData("mail_country")]
    [InlineData("careof")]
    [InlineData("mail_addno")]
    [InlineData("mail_unit")]
    [InlineData("original_mailing_address")]
    [InlineData("enhanced_ownership")]
    [InlineData("eo_owner")]
    [InlineData("eo_deedowner")]
    [InlineData("attom_id")]
    public void FiresOnRepresentativeRegridNamedFields(string fieldName)
    {
        Assert.True(OwnerFieldNameGuard.IsOwnerLike(fieldName));
    }

    [Theory]
    [InlineData("county")]
    [InlineData("address")]
    [InlineData("SITUS_ADDR")]
    [InlineData("SUBDIVISION")]
    [InlineData("ACRES")]
    [InlineData("ZONING")]
    [InlineData("ll_gisacre")]
    [InlineData("ll_uuid")]
    public void DoesNotFireOnKnownFalsePositiveRiskNames(string fieldName)
    {
        Assert.False(OwnerFieldNameGuard.IsOwnerLike(fieldName));
    }

    [Theory]
    [InlineData("TOWNSHIP")]
    [InlineData("TOWNSHIP_RANGE")]
    public void DoesNotFireOnAStandardPlssFieldContainingOwnAsAMidWordSubstring(string fieldName)
    {
        // "own" is a plain substring of "TOWNSHIP" ("t-[own]-ship"); the word-start anchor must not reject it.
        Assert.False(OwnerFieldNameGuard.IsOwnerLike(fieldName));
    }

    [Theory]
    [InlineData("OWN_ADD")]
    [InlineData("previous_owner")]
    public void StillFiresOnRealOwnerShapedNamesAfterTheWordStartAnchor(string fieldName)
    {
        // Proves the word-start anchor did not also remove real coverage.
        Assert.True(OwnerFieldNameGuard.IsOwnerLike(fieldName));
    }

    [Fact]
    public void ThrowsForANullOrBlankFieldName()
    {
        Assert.Throws<ArgumentException>(() => OwnerFieldNameGuard.IsOwnerLike(""));
        Assert.Throws<ArgumentException>(() => OwnerFieldNameGuard.IsOwnerLike("   "));
        Assert.Throws<ArgumentNullException>(() => OwnerFieldNameGuard.IsOwnerLike(null!));
    }
}
