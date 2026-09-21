using Autodesk.Revit.DB;
using SolidGround.Core.Geometry;
using SolidGround.Core.Units;

namespace SolidGround.Revit.Geometry;

/// <summary>
/// Centralizes the one mapping from SolidGround's <see cref="LengthUnit"/> to Revit's <see cref="ForgeTypeId"/>
/// unit identifiers, and the one place a bare <see cref="double"/> in that unit becomes a Revit-internal
/// value (orchestrator decision 6). See SolidGround Issue #15's design record §8. Every member here is
/// verified against <c>apidump/out/Autodesk.Revit.DB.UnitTypeId.txt</c> and
/// <c>apidump/out/Autodesk.Revit.DB.UnitUtils.txt</c>.
/// </summary>
internal static class RevitUnitConversion
{
    /// <summary>
    /// Maps a SolidGround output unit to the matching Revit unit identifier: <see cref="UnitTypeId.UsSurveyFeet"/>
    /// (exactly 1200/3937 m), <see cref="UnitTypeId.Feet"/> (exactly 0.3048 m, the international foot), or
    /// <see cref="UnitTypeId.Meters"/>. Never assumes which of <see cref="UnitTypeId.Feet"/>/
    /// <see cref="UnitTypeId.UsSurveyFeet"/> is numerically Revit's own internal foot (design record §8,
    /// Appendix A UNVERIFIED item 3) -- this mapping is definition-driven, not internal-unit-driven.
    /// </summary>
    internal static ForgeTypeId ToForgeTypeId(LengthUnit unit) => unit switch
    {
        LengthUnit.UsSurveyFoot => UnitTypeId.UsSurveyFeet,
        LengthUnit.InternationalFoot => UnitTypeId.Feet,
        LengthUnit.Meter => UnitTypeId.Meters,
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unsupported length unit."),
    };

    /// <summary>Converts one scalar value already expressed in <paramref name="unit"/> into Revit-internal units.</summary>
    internal static double ToInternal(double value, LengthUnit unit) =>
        UnitUtils.ConvertToInternalUnits(value, ToForgeTypeId(unit));

    /// <summary>
    /// Converts a horizontal-only local coordinate (already expressed in <paramref name="unit"/>, per
    /// <c>LocalCoordinateFrame.ToLocalHorizontal</c>/<c>LocalBoundary</c>'s Z-less contract) into a
    /// Revit-internal <see cref="XYZ"/> with <c>Z = 0</c>. Callers that need a real boundary elevation combine
    /// this result's X/Y with their own internal-unit Z (see <c>BoundaryGeometryBuilder</c>, design record
    /// §7.2's planarity fix).
    /// </summary>
    internal static XYZ ToInternalHorizontal(LocalCoordinate2D point, LengthUnit unit)
    {
        ForgeTypeId forgeTypeId = ToForgeTypeId(unit);
        return new XYZ(
            UnitUtils.ConvertToInternalUnits(point.X, forgeTypeId),
            UnitUtils.ConvertToInternalUnits(point.Y, forgeTypeId),
            0d);
    }
}
