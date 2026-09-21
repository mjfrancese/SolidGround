using Autodesk.Revit.DB;

namespace SolidGround.Revit.Transactions;

/// <summary>
/// A snapshot of the document-wide shared-coordinate state SolidGround must never move:
/// <see cref="BasePoint"/>/survey point position and shared position, the active <see cref="SiteLocation"/>'s
/// place name, and the active <see cref="ProjectLocation"/>'s name. See SolidGround Issue #15's design record
/// §2.4 row 29. Every Revit API member used here is verified against
/// <c>apidump/out/Autodesk.Revit.DB.{BasePoint,ProjectLocation,SiteLocation,Document}.txt</c>.
/// </summary>
internal sealed record OrphanSnapshot(
    XYZ BasePointPosition,
    XYZ BasePointSharedPosition,
    XYZ SurveyPointPosition,
    XYZ SurveyPointSharedPosition,
    string? SiteLocationPlaceName,
    string ActiveProjectLocationName);

/// <summary>
/// Captures and compares <see cref="OrphanSnapshot"/>s before and after the toposolid-creation transaction.
/// Always logged, never itself a rollback trigger: by the time <see cref="Unchanged"/> is called (design
/// record §6.6 step 2), the transaction has already durably committed.
/// </summary>
internal static class OrphanCheck
{
    internal static OrphanSnapshot Capture(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        BasePoint basePoint = BasePoint.GetProjectBasePoint(document);
        BasePoint surveyPoint = BasePoint.GetSurveyPoint(document);
        ProjectLocation activeLocation = document.ActiveProjectLocation;
        SiteLocation siteLocation = activeLocation.GetSiteLocation();

        return new OrphanSnapshot(
            basePoint.Position,
            basePoint.SharedPosition,
            surveyPoint.Position,
            surveyPoint.SharedPosition,
            siteLocation.PlaceName,
            activeLocation.Name);
    }

    internal static bool Unchanged(OrphanSnapshot before, OrphanSnapshot after, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        List<string> changed = [];
        if (!before.BasePointPosition.IsAlmostEqualTo(after.BasePointPosition))
        {
            changed.Add("BasePoint.Position");
        }

        if (!before.BasePointSharedPosition.IsAlmostEqualTo(after.BasePointSharedPosition))
        {
            changed.Add("BasePoint.SharedPosition");
        }

        if (!before.SurveyPointPosition.IsAlmostEqualTo(after.SurveyPointPosition))
        {
            changed.Add("SurveyPoint.Position");
        }

        if (!before.SurveyPointSharedPosition.IsAlmostEqualTo(after.SurveyPointSharedPosition))
        {
            changed.Add("SurveyPoint.SharedPosition");
        }

        if (!string.Equals(before.SiteLocationPlaceName, after.SiteLocationPlaceName, StringComparison.Ordinal))
        {
            changed.Add("SiteLocation.PlaceName");
        }

        if (!string.Equals(before.ActiveProjectLocationName, after.ActiveProjectLocationName, StringComparison.Ordinal))
        {
            changed.Add("ActiveProjectLocation.Name");
        }

        if (changed.Count == 0)
        {
            problem = null;
            return true;
        }

        problem = "Unexpected change(s) in shared coordinate state: " + string.Join(", ", changed);
        return false;
    }
}
