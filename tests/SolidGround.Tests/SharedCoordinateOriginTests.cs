using SolidGround.Core.Geometry;
using SolidGround.Core.Metadata;
using SolidGround.Core.Transformations;
using SolidGround.Core.Units;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="SharedCoordinateOrigin.Resolve"/> (SolidGround Issue #30, PH3-3): pins the exact unit
/// contract "Unit convention for the shared-coordinates value" (docs/architecture/revit-property-line-and-shared-coordinates.md)
/// depends on -- <see cref="LocalCoordinateFrame.Origin"/>'s own native unit, never
/// <see cref="LocalCoordinateFrame.OutputUnit"/> -- including the US survey foot case specifically, since that
/// is both the shipped default <c>OutputUnit</c> and the value most likely to be reached for by mistake at the
/// shared-coordinates write's own Revit-side call site. Mirrors <see cref="LocalCoordinateFrameHorizontalTests"/>'s
/// own construction style: a <c>ProjectedReference()</c>/<c>VerticalReference()</c> pair built directly in test
/// code, no fixture file.
/// </summary>
public sealed class SharedCoordinateOriginTests
{
    // The example site's own recorded local origin (matching docs/architecture/revit-property-line-and-shared-coordinates.md's
    // own placement-record example).
    private static readonly Coordinate3D ExampleSiteOrigin = new(449674.0, 4604563.0, 183.10);

    [Fact]
    public void ResolveReturnsTheOriginAndItsOwnNativeUnitsUnchanged()
    {
        LocalCoordinateFrame frame = new(ExampleSiteOrigin, ProjectedReference(), VerticalReference(), LengthUnit.Meter);

        SharedCoordinateOrigin.Resolved resolved = SharedCoordinateOrigin.Resolve(frame);

        Assert.Equal(ExampleSiteOrigin, resolved.Origin);
        Assert.Equal(LengthUnit.Meter, resolved.HorizontalUnit);
        Assert.Equal(LengthUnit.Meter, resolved.VerticalUnit);
    }

    [Fact]
    public void ResolveIgnoresOutputUnitEvenWhenOutputUnitIsTheShippedDefault()
    {
        // The identical frame and origin as the test above, except OutputUnit is the shipped default
        // (LengthConverter.DefaultOutputUnit -- US survey foot), the exact value most likely to be reached for
        // by mistake since it is what context.Settings.Request.OutputUnit/revitUnit already carry in scope at
        // nearby Stage 5 call sites. Resolve must return the identical Coordinate3D and the identical
        // HorizontalUnit/VerticalUnit (meter) as the test above -- proving OutputUnit has zero effect on this
        // value.
        LocalCoordinateFrame frame = new(ExampleSiteOrigin, ProjectedReference(), VerticalReference(), LengthConverter.DefaultOutputUnit);

        SharedCoordinateOrigin.Resolved resolved = SharedCoordinateOrigin.Resolve(frame);

        Assert.Equal(ExampleSiteOrigin, resolved.Origin);
        Assert.Equal(LengthUnit.Meter, resolved.HorizontalUnit);
        Assert.Equal(LengthUnit.Meter, resolved.VerticalUnit);
    }

    [Fact]
    public void ResolveThrowsForANullFrame()
    {
        Assert.Throws<ArgumentNullException>(() => SharedCoordinateOrigin.Resolve(null!));
    }

    [Fact]
    public void ResolvePairsHorizontalAndVerticalUnitsToTheirOwnDistinctAxis()
    {
        // Every test above pairs a meter horizontal reference with a meter vertical reference, so none of them
        // can tell resolved.HorizontalUnit and resolved.VerticalUnit apart from one another -- a regression that
        // swapped the two fields inside Resolve would pass unnoticed. This case gives each axis its own distinct
        // unit (a non-metric horizontal reference, e.g. a State Plane zone recorded in US survey feet, paired
        // with a metric vertical reference) -- exactly the mismatch docs/architecture/revit-property-line-and-shared-coordinates.md's
        // "Unit convention for the shared-coordinates value" section names as process mode's real, reachable
        // case and the single most likely implementer mistake here.
        LocalCoordinateFrame frame = new(
            ExampleSiteOrigin, ProjectedReference(LengthUnit.UsSurveyFoot), VerticalReference(LengthUnit.Meter), LengthUnit.Meter);

        SharedCoordinateOrigin.Resolved resolved = SharedCoordinateOrigin.Resolve(frame);

        Assert.Equal(LengthUnit.UsSurveyFoot, resolved.HorizontalUnit);
        Assert.Equal(LengthUnit.Meter, resolved.VerticalUnit);
    }

    private static HorizontalReference ProjectedReference(LengthUnit unit = LengthUnit.Meter) => new(
        "EPSG:26915", "NAD83(2011)", HorizontalReferenceKind.Projected, HorizontalUnit.Linear(unit), HorizontalAxisOrder.EastingNorthing);

    private static VerticalReference VerticalReference(LengthUnit unit = LengthUnit.Meter) => new("NAVD88", unit, "Geoid12B");
}
