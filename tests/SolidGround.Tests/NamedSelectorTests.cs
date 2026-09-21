using SolidGround.Core.Processing;

namespace SolidGround.Tests;

/// <summary>
/// Direct tests for <see cref="NamedSelector"/>, SolidGround Issue #15's Revit-free `toposolidType` selection
/// rule (orchestrator decision 9): a configured name wins on an exact ordinal match; otherwise the first
/// candidate by ordinal name wins.
/// </summary>
public sealed class NamedSelectorTests
{
    [Fact]
    public void ReturnsNullForAnEmptyCandidateList()
    {
        NamedCandidate? result = NamedSelector.SelectFirstByOrdinalName([], configuredName: null);

        Assert.Null(result);
    }

    [Fact]
    public void PicksTheFirstByOrdinalName()
    {
        NamedCandidate[] candidates =
        [
            new(1, "Site - Proposed"),
            new(2, "Site - Existing"),
        ];

        NamedCandidate? result = NamedSelector.SelectFirstByOrdinalName(candidates, configuredName: null);

        Assert.Equal(2, result?.Id);
        Assert.Equal("Site - Existing", result?.Name);
    }

    [Fact]
    public void ConfiguredNameWinsEvenWhenNotFirst()
    {
        NamedCandidate[] candidates =
        [
            new(1, "Site - Existing"),
            new(2, "Site - Proposed"),
        ];

        NamedCandidate? result = NamedSelector.SelectFirstByOrdinalName(candidates, configuredName: "Site - Proposed");

        Assert.Equal(2, result?.Id);
    }

    [Fact]
    public void ConfiguredNameThatMatchesNothingFallsBackToTheDefaultRule()
    {
        NamedCandidate[] candidates =
        [
            new(1, "Site - Existing"),
            new(2, "Site - Proposed"),
        ];

        NamedCandidate? result = NamedSelector.SelectFirstByOrdinalName(candidates, configuredName: "Does Not Exist");

        Assert.Equal(1, result?.Id);
    }
}
