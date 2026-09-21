using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Sources;
using SolidGround.Core.Sources.OpenTopography;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="VerticalReferenceResolution"/>, lifted into <c>SolidGround.Core</c> for
/// SolidGround Issue #15 with its precedence logic unchanged: an explicit override first, then the sidecar,
/// then a compound <c>.prj</c>'s own <c>VERT_CS</c>. See docs/architecture/cli-workflow.md's "Units" section.
/// </summary>
public sealed class VerticalReferenceResolutionTests
{
    [Fact]
    public void ResolveThrowsWhenNoSourceSuppliesADatumOrUnit()
    {
        FormatException exception = Assert.Throws<FormatException>(
            () => VerticalReferenceResolution.Resolve(null, null, null, null, null));

        Assert.Contains("vertical reference", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveExplicitOverridesTheSidecarAndCompoundPrj()
    {
        RasterSourceSidecar sidecar = BuildSidecar("SidecarDatum", LengthUnit.Meter, "SidecarGeoid", ReferenceOrigin.SourceResponse);
        VerticalReference compoundPrjVertical = new("PrjDatum", LengthUnit.InternationalFoot, "PrjGeoid");

        VerticalReferenceResolution.ResolvedVerticalReference resolved = VerticalReferenceResolution.Resolve(
            "ExplicitDatum", LengthUnit.UsSurveyFoot, "ExplicitGeoid", sidecar, compoundPrjVertical);

        Assert.Equal("ExplicitDatum", resolved.Reference.Datum);
        Assert.Equal(LengthUnit.UsSurveyFoot, resolved.Reference.Unit);
        Assert.Equal("ExplicitGeoid", resolved.Reference.GeoidModel);
        Assert.Equal(ReferenceOrigin.Operator, resolved.Origin);
    }

    [Fact]
    public void ResolveFallsBackToTheSidecarWhenNoExplicitFlagIsGiven()
    {
        RasterSourceSidecar sidecar = BuildSidecar("SidecarDatum", LengthUnit.Meter, "SidecarGeoid", ReferenceOrigin.SourceResponse);
        VerticalReference compoundPrjVertical = new("PrjDatum", LengthUnit.InternationalFoot, "PrjGeoid");

        VerticalReferenceResolution.ResolvedVerticalReference resolved = VerticalReferenceResolution.Resolve(
            null, null, null, sidecar, compoundPrjVertical);

        Assert.Equal("SidecarDatum", resolved.Reference.Datum);
        Assert.Equal(LengthUnit.Meter, resolved.Reference.Unit);
        Assert.Equal("SidecarGeoid", resolved.Reference.GeoidModel);
        Assert.Equal(ReferenceOrigin.SourceResponse, resolved.Origin);
    }

    [Fact]
    public void ResolveFallsBackToTheCompoundPrjWhenNeitherExplicitNorSidecarIsGiven()
    {
        VerticalReference compoundPrjVertical = new("PrjDatum", LengthUnit.InternationalFoot, "PrjGeoid");

        VerticalReferenceResolution.ResolvedVerticalReference resolved = VerticalReferenceResolution.Resolve(
            null, null, null, null, compoundPrjVertical);

        Assert.Equal("PrjDatum", resolved.Reference.Datum);
        Assert.Equal(LengthUnit.InternationalFoot, resolved.Reference.Unit);
        Assert.Equal("PrjGeoid", resolved.Reference.GeoidModel);
        Assert.Equal(ReferenceOrigin.Operator, resolved.Origin);
    }

    private static RasterSourceSidecar BuildSidecar(string datum, LengthUnit unit, string geoidModel, ReferenceOrigin verticalOrigin) => new(
        "OpenTopography",
        "USGS1m",
        CollectionPeriod: null,
        QualityLevel: null,
        new RasterSourceVertical(datum, unit, geoidModel),
        HorizontalReferenceOrigin: ReferenceOrigin.SourceResponse,
        VerticalReferenceOrigin: verticalOrigin,
        new RasterSourceAcquisition(
            "https://example.test/api?API_Key=REDACTED",
            200,
            "application/zip",
            "USGS1m.zip",
            ["USGS1m.asc", "USGS1m.prj"],
            nameof(OpenTopographyReferenceSource.PrjSidecar),
            12345,
            MetadataRequest: null,
            new RasterSourceFetchEnvelope([withheld], [withheld], [withheld], [withheld], 110.0, false, 121.8, 154.8, 121.8, 154.8)));
}
