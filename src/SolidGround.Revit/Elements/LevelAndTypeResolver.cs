using Autodesk.Revit.DB;
using SolidGround.Core.Processing;

namespace SolidGround.Revit.Elements;

/// <summary>
/// Thin adapter projecting every existing <see cref="Level"/>/<see cref="ToposolidType"/> in a
/// <see cref="Document"/> into a Revit-free <see cref="NamedElevationCandidate"/>/<see cref="NamedCandidate"/>,
/// and mapping a winning candidate's <c>Id</c> back to the real element. Never calls
/// <see cref="Level.Create(Document, double)"/>, never creates or duplicates a <see cref="ToposolidType"/>.
/// </summary>
/// <remarks>
/// SolidGround Issue #31 (PH3-4), Stage D: this class no longer applies the "configured name wins, else the
/// default rule" selection itself (see <c>ResolveLevel</c>/<c>ResolveToposolidType</c>, removed) -- the
/// interactive dialog now owns that decision, calling <see cref="NamedElevationSelector.SelectLowestElevation"/>/
/// <see cref="NamedSelector.SelectFirstByOrdinalName"/> directly against <see cref="ListLevels"/>'/
/// <see cref="ListToposolidTypes"/>'s own candidate lists to prefill its Level/ToposolidType choosers
/// (`SolidGroundDialogHost.ShowModal`), so the operator can see and override the pick before <c>Preflight</c>
/// ever runs. <c>CreateToposolidCommand</c>'s Document Preflight stage then maps the dialog's confirmed
/// <c>NamedElevationCandidate</c>/<c>NamedCandidate</c> back to a real element via <see cref="FindLevelById"/>/
/// <see cref="FindToposolidTypeById"/> -- the same <c>Document</c>, same synchronous call, no transaction opened
/// on any path that could invalidate an element reference, so no defensive re-verification is performed (owner
/// decision 4). See docs/architecture/revit-interactive-dialog.md and
/// docs/architecture/revit-toposolid-creation.md's "Command flow" and "Level and ToposolidType selection".
/// </remarks>
internal static class LevelAndTypeResolver
{
    internal static IReadOnlyList<NamedElevationCandidate> ListLevels(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<NamedElevationCandidate> candidates = [];
        foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(Level)).ToElements())
        {
            if (element is Level level)
            {
                candidates.Add(new NamedElevationCandidate(level.Id.Value, level.Name, level.Elevation));
            }
        }

        return candidates;
    }

    internal static IReadOnlyList<NamedCandidate> ListToposolidTypes(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        List<NamedCandidate> candidates = [];
        foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(ToposolidType)).ToElements())
        {
            if (element is ToposolidType toposolidType)
            {
                candidates.Add(new NamedCandidate(toposolidType.Id.Value, toposolidType.Name));
            }
        }

        return candidates;
    }

    /// <summary><see langword="null"/> only when no <see cref="Level"/> in <paramref name="document"/> has this exact <paramref name="id"/> (not expected to be reachable from <c>CreateToposolidCommand</c>'s own flow -- see remarks above).</summary>
    internal static Level? FindLevelById(Document document, long id)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(Level)).ToElements())
        {
            if (element is Level level && level.Id.Value == id)
            {
                return level;
            }
        }

        return null;
    }

    /// <summary><see langword="null"/> only when no <see cref="ToposolidType"/> in <paramref name="document"/> has this exact <paramref name="id"/> (not expected to be reachable from <c>CreateToposolidCommand</c>'s own flow -- see remarks above).</summary>
    internal static ToposolidType? FindToposolidTypeById(Document document, long id)
    {
        ArgumentNullException.ThrowIfNull(document);

        foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(ToposolidType)).ToElements())
        {
            if (element is ToposolidType toposolidType && toposolidType.Id.Value == id)
            {
                return toposolidType;
            }
        }

        return null;
    }
}
