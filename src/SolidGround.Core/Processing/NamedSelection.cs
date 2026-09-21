namespace SolidGround.Core.Processing;

/// <summary>
/// One named candidate a host could select from -- Revit-free (<see cref="Id"/> is a bare
/// <see langword="long"/>, not an <c>ElementId</c>) so <see cref="NamedSelector"/> is directly
/// offline-testable. <c>SolidGround.Revit</c>'s <c>LevelAndTypeResolver</c> projects each candidate
/// <c>ToposolidType</c> into one of these (<c>ElementId.Value</c>, <c>Name</c>) and maps the winning
/// <see cref="Id"/> back to the real element.
/// </summary>
public sealed record NamedCandidate(long Id, string Name);

/// <summary>
/// Selects a <see cref="NamedCandidate"/> for SolidGround Issue #15's `toposolidType` selection rule
/// (orchestrator decision 9): a configured name wins on an exact ordinal match; otherwise the first candidate
/// by ordinal <see cref="NamedCandidate.Name"/> wins.
/// </summary>
public static class NamedSelector
{
    /// <summary>
    /// Returns the candidate whose <see cref="NamedCandidate.Name"/> exactly (ordinally) matches
    /// <paramref name="configuredName"/> when one exists; otherwise the first candidate by ordinal
    /// <see cref="NamedCandidate.Name"/>; or <see langword="null"/> when <paramref name="candidates"/> is
    /// empty and no configured name matched. A blank <paramref name="configuredName"/> is treated the same as
    /// <see langword="null"/> (no configured override).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is <see langword="null"/>.</exception>
    public static NamedCandidate? SelectFirstByOrdinalName(IReadOnlyList<NamedCandidate> candidates, string? configuredName)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            foreach (NamedCandidate candidate in candidates)
            {
                if (string.Equals(candidate.Name, configuredName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        NamedCandidate? first = null;
        foreach (NamedCandidate candidate in candidates)
        {
            if (first is null || string.CompareOrdinal(candidate.Name, first.Name) < 0)
            {
                first = candidate;
            }
        }

        return first;
    }
}
