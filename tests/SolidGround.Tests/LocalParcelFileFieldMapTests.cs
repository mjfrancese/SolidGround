using SolidGround.Core.Sources.LocalParcelFile;

namespace SolidGround.Tests;

public sealed class LocalParcelFileFieldMapTests
{
    [Fact]
    public void RegridStandardDefaultUsesTheDocumentedRegridFieldNames()
    {
        LocalParcelFileFieldMap fieldMap = LocalParcelFileFieldMap.RegridStandardDefault;

        Assert.Equal("parcelnumb", fieldMap.ParcelId);
        Assert.Equal("address", fieldMap.SitusAddress);
        Assert.Equal("subdivision", fieldMap.Subdivision);
        Assert.Equal("lot", fieldMap.Lot);
        Assert.Equal("block", fieldMap.Block);
        Assert.Equal("plat", fieldMap.Plat);
        Assert.Equal("book", fieldMap.Book);
        Assert.Equal("page", fieldMap.Page);
        Assert.Equal("legaldesc", fieldMap.LegalDescription);
        Assert.Equal("ll_gisacre", fieldMap.ReportedAcres);
        Assert.Equal("zoning", fieldMap.Zoning);
        Assert.Equal("ll_uuid", fieldMap.StableParcelId);
    }

    [Fact]
    public void RegridStandardDefaultValidatesCleanly()
    {
        LocalParcelFileFieldMap.RegridStandardDefault.Validate();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("mailadd")]
    [InlineData("OWNER_NAME")]
    public void ValidateThrowsWhenParcelIdIsOwnerLike(string ownerLikeName)
    {
        LocalParcelFileFieldMap fieldMap = new() { ParcelId = ownerLikeName };

        Assert.Throws<ArgumentException>(fieldMap.Validate);
    }

    [Fact]
    public void ValidateThrowsWhenAnOptionalFieldIsOwnerLike()
    {
        LocalParcelFileFieldMap fieldMap = new() { LegalDescription = "owner" };

        Assert.Throws<ArgumentException>(fieldMap.Validate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateThrowsWhenParcelIdIsBlank(string blankValue)
    {
        LocalParcelFileFieldMap fieldMap = new() { ParcelId = blankValue };

        Assert.Throws<ArgumentException>(fieldMap.Validate);
    }

    [Fact]
    public void ValidateAllowsANullOptionalField()
    {
        LocalParcelFileFieldMap fieldMap = new() { Zoning = null, StableParcelId = null };

        fieldMap.Validate();
    }
}
