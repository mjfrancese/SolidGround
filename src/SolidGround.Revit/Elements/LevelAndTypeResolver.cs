using Autodesk.Revit.DB;
using SolidGround.Core.Processing;

namespace SolidGround.Revit.Elements;

/// <summary>
/// Thin adapter over <see cref="NamedElevationSelector"/>/<see cref="NamedSelector"/> (orchestrator decision
/// 9): projects every existing <see cref="Level"/>/<see cref="ToposolidType"/> into a Revit-free candidate,
/// lets the Core selector apply the "configured name wins, else the default rule" logic, then maps the
/// winning candidate back to its real element. Never calls <see cref="Level.Create(Document, double)"/>,
/// never creates or duplicates a <see cref="ToposolidType"/>. Returns <see langword="null"/> when the
/// document has no candidate of the requested kind at all, or when a non-blank configured name matches
/// nothing and there is still no candidate to fall back to -- both a Preflight rejection, not a creation, at
/// the caller (design record §7.3).
/// </summary>
internal static class LevelAndTypeResolver
{
    internal static Level? ResolveLevel(Document document, string? configuredName)
    {
        ArgumentNullException.ThrowIfNull(document);

        Dictionary<long, Level> byId = [];
        List<NamedElevationCandidate> candidates = [];
        foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(Level)).ToElements())
        {
            if (element is not Level level)
            {
                continue;
            }

            byId[level.Id.Value] = level;
            candidates.Add(new NamedElevationCandidate(level.Id.Value, level.Name, level.Elevation));
        }

        NamedElevationCandidate? winner = NamedElevationSelector.SelectLowestElevation(candidates, configuredName);
        return winner is null ? null : byId.GetValueOrDefault(winner.Id);
    }

    internal static ToposolidType? ResolveToposolidType(Document document, string? configuredName)
    {
        ArgumentNullException.ThrowIfNull(document);

        Dictionary<long, ToposolidType> byId = [];
        List<NamedCandidate> candidates = [];
        foreach (Element element in new FilteredElementCollector(document).OfClass(typeof(ToposolidType)).ToElements())
        {
            if (element is not ToposolidType toposolidType)
            {
                continue;
            }

            byId[toposolidType.Id.Value] = toposolidType;
            candidates.Add(new NamedCandidate(toposolidType.Id.Value, toposolidType.Name));
        }

        NamedCandidate? winner = NamedSelector.SelectFirstByOrdinalName(candidates, configuredName);
        return winner is null ? null : byId.GetValueOrDefault(winner.Id);
    }
}
