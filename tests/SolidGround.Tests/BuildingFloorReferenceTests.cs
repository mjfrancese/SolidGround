using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Processing;
using SolidGround.Core.Provenance;
using SolidGround.Core.Sources.BuildingOutlines;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

public sealed class BuildingFloorReferenceTests
{
    [Fact]
    public void EstimatedGradeRiseKeepsTheEnteredUnitAndResolvesPhysicalFloorHeight()
    {
        VerticalReference reference = new("NAVD88", LengthUnit.Meter);
        ElevationGridSample sample = new(new Coordinate2D(500d, 600d), 100d, reference,
            [new ElevationGridSupportCell(2, 3, new Coordinate2D(500d, 600d), 100d, 1d)]);
        BuildingFloorReference floor = BuildingFloorReference.EstimatedGradeRise(
            new GroundPoint(-93d, 44d, new Coordinate2D(500d, 600d)), sample,
            new MeasuredRise(2d, LengthUnit.InternationalFoot, belowGrade: false), Level());

        Assert.Equal(100.6096d, floor.PhysicalFloorElevation);
        Assert.Equal(LengthUnit.InternationalFoot, floor.Rise!.Unit);
        Assert.False(floor.Rise.BelowGrade);
        Assert.Equal(FloorReferenceMode.EstimatedGradeRise, floor.Mode);
    }

    [Fact]
    public void StrictJsonRoundTripsProvisionalReferenceAndRejectsUnknownPolicy()
    {
        BuildingFloorReference original = BuildingFloorReference.ProvisionalGround(
            102.5d, new VerticalReference("NAVD88", LengthUnit.Meter), Level(), "legal-valid-cell-median-v1");

        BuildingFloorReference parsed = BuildingFloorReferenceJson.Deserialize(BuildingFloorReferenceJson.Serialize(original))!;

        Assert.Equal(FloorReferenceMode.ProvisionalGround, parsed.Mode);
        Assert.Equal(102.5d, parsed.ProvisionalGroundElevation);
        Assert.Equal("legal-valid-cell-median-v1", parsed.ProvisionalPolicy);
        Assert.Throws<ArgumentException>(() => BuildingFloorReference.ProvisionalGround(
            1d, new VerticalReference("NAVD88", LengthUnit.Meter), Level(), "invented-policy"));
    }

    [Fact]
    public void KnownReferenceRequiresARealSourceDescription()
    {
        Assert.Throws<ArgumentException>(() => BuildingFloorReference.KnownElevation(
            10d, new VerticalReference("NAVD88", LengthUnit.Meter), Level(), "  "));
    }

    [Fact]
    public void StandaloneOutlineJsonRoundTripsAndRejectsUnexpectedProperties()
    {
        BuildingOutlineProvenance original = Outline(new DateOnly(2026, 10, 1));

        string json = BuildingOutlineProvenanceJson.Serialize(original);
        BuildingOutlineProvenance parsed = BuildingOutlineProvenanceJson.Deserialize(json)!;

        Assert.Equal(original.Provider, parsed.Provider);
        Assert.Equal(original.TileKeys, parsed.TileKeys);
        Assert.Equal(original.DateOnlyRetrievedDate, parsed.DateOnlyRetrievedDate);
        Assert.Throws<FormatException>(() => BuildingOutlineProvenanceJson.Deserialize(json.Replace("\"provider\":", "\"extra\": true, \"provider\":", StringComparison.Ordinal)));
    }

    private static TargetProjectLevel Level() => new(42, "level-unique-id", "Level 1", 12d);

    private static BuildingOutlineProvenance Outline(DateOnly date) => new(
        "Synthetic Provider", "test-release", "MIT", new Uri("https://example.test/license"), "Synthetic MIT license text", "Synthetic attribution",
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", ["023111113", "023111112"], date);
}
