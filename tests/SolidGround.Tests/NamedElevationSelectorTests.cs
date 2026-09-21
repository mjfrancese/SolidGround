using SolidGround.Core.Processing;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="NamedElevationSelector"/>, SolidGround Issue #15's Revit-free `level`
/// selection rule (orchestrator decision 9): a configured name wins on an exact ordinal match; otherwise the
/// lowest-elevation candidate wins, ties broken by ordinal name.
/// </summary>
public sealed class NamedElevationSelectorTests
{
    [Fact]
    public void ReturnsNullForAnEmptyCandidateList()
    {
        NamedElevationCandidate? result = NamedElevationSelector.SelectLowestElevation([], configuredName: null);

        Assert.Null(result);
    }

    [Fact]
    public void PicksTheLowestElevation()
    {
        NamedElevationCandidate[] candidates =
        [
            new(1, "Level 2", 10d),
            new(2, "Level 1", 0d),
            new(3, "Roof", 20d),
        ];

        NamedElevationCandidate? result = NamedElevationSelector.SelectLowestElevation(candidates, configuredName: null);

        Assert.Equal(2, result?.Id);
    }

    [Fact]
    public void BreaksElevationTiesByOrdinalName()
    {
        NamedElevationCandidate[] candidates =
        [
            new(1, "Level B", 0d),
            new(2, "Level A", 0d),
        ];

        NamedElevationCandidate? result = NamedElevationSelector.SelectLowestElevation(candidates, configuredName: null);

        Assert.Equal(2, result?.Id);
        Assert.Equal("Level A", result?.Name);
    }

    [Fact]
    public void ConfiguredNameWinsEvenWhenNotLowest()
    {
        NamedElevationCandidate[] candidates =
        [
            new(1, "Level 1", 0d),
            new(2, "Level 2", 10d),
        ];

        NamedElevationCandidate? result = NamedElevationSelector.SelectLowestElevation(candidates, configuredName: "Level 2");

        Assert.Equal(2, result?.Id);
    }

    [Fact]
    public void ConfiguredNameThatMatchesNothingFallsBackToTheDefaultRule()
    {
        NamedElevationCandidate[] candidates =
        [
            new(1, "Level 1", 0d),
            new(2, "Level 2", 10d),
        ];

        NamedElevationCandidate? result = NamedElevationSelector.SelectLowestElevation(candidates, configuredName: "Does Not Exist");

        Assert.Equal(1, result?.Id);
    }

    [Fact]
    public void ComparisonIsOrdinalNotCurrentCulture()
    {
        // Ordinal orders by UTF-16 code unit: every uppercase letter (e.g. 'Z' = 0x5A) sorts before every
        // lowercase letter (e.g. 'a' = 0x61), unlike a linguistic/culture-aware comparer, which would
        // typically sort "apple" before "Zebra" alphabetically regardless of case.
        NamedElevationCandidate[] candidates =
        [
            new(1, "apple", 0d),
            new(2, "Zebra", 0d),
        ];

        NamedElevationCandidate? result = NamedElevationSelector.SelectLowestElevation(candidates, configuredName: null);

        Assert.Equal(2, result?.Id);
        Assert.Equal("Zebra", result?.Name);
    }
}
