using SolidGround.Core.Sources;
using SolidGround.Core.Workflow;

namespace SolidGround.Tests;

public sealed class LocationParcelFlowTests
{
    [Fact]
    public void UniqueLocationAdvancesForParcelLookupWithoutConfirmingTheLocation()
    {
        LocationParcelFlow flow = new();
        LocationParcelLookupTicket ticket = flow.BeginLocationLookup();
        AddressGeocodeCandidate candidate = new(41.59, -93.60, "Example address", "Example source");

        Assert.True(flow.TryApplyLocations(ticket, [candidate]));

        Assert.Equal(LocationParcelStage.Parcel, flow.Stage);
        Assert.Equal(candidate, flow.SelectedLocation);
        Assert.False(flow.LocationIsConfirmed);
    }

    [Fact]
    public void LateLocationCompletionCannotOverwriteChangedInput()
    {
        LocationParcelFlow flow = new();
        LocationParcelLookupTicket stale = flow.BeginLocationLookup();
        flow.ChangeInput();

        bool applied = flow.TryApplyLocations(stale, [new AddressGeocodeCandidate(41.59, -93.60, "Stale", "Example source")]);

        Assert.False(applied);
        Assert.Empty(flow.LocationCandidates);
        Assert.Equal(LocationParcelStage.Location, flow.Stage);
    }

    [Fact]
    public void SourceChangePreservesLocationButInvalidatesParcelRevision()
    {
        LocationParcelFlow flow = new();
        LocationParcelLookupTicket locationTicket = flow.BeginLocationLookup();
        AddressGeocodeCandidate location = new(41.59, -93.60, "Example address", "Example source");
        Assert.True(flow.TryApplyLocations(locationTicket, [location]));
        LocationParcelLookupTicket parcelTicket = flow.BeginParcelLookup();

        flow.ChangeSource();

        Assert.Equal(location, flow.SelectedLocation);
        Assert.Equal(LocationParcelStage.Parcel, flow.Stage);
        Assert.NotEqual(parcelTicket.SourceRevision, flow.SourceRevision);
    }

    [Fact]
    public void ExplicitAreaCanAdvanceToReviewWithoutInventingAParcel()
    {
        LocationParcelFlow flow = new();

        flow.UseExplicitArea();

        Assert.Equal(LocationParcelStage.Review, flow.Stage);
        Assert.Null(flow.SelectedParcel);
        Assert.Empty(flow.ParcelCandidates);
    }
}
