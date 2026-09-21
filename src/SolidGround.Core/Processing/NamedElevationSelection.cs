namespace SolidGround.Core.Processing;

/// <summary>
/// One named, elevated candidate a host could select from -- Revit-free (<see cref="Id"/> is a bare
/// <see langword="long"/>, not an <c>ElementId</c>) so <see cref="NamedElevationSelector"/> is directly
/// offline-testable. <c>SolidGround.Revit</c>'s <c>LevelAndTypeResolver</c> projects each candidate
/// <c>Level</c> into one of these (<c>ElementId.Value</c>, <c>Name</c>, <c>Elevation</c>) and maps the winning
/// <see cref="Id"/> back to the real element.
/// </summary>
public sealed record NamedElevationCandidate(long Id, string Name, double Elevation);

/// <summary>
/// Selects a <see cref="NamedElevationCandidate"/> for SolidGround Issue #15's `level` selection rule
/// (orchestrator decision 9): a configured name wins on an exact ordinal match; otherwise the candidate with
/// the lowest <see cref="NamedElevationCandidate.Elevation"/> wins, ties broken by ordinal
/// <see cref="NamedElevationCandidate.Name"/>.
/// </summary>
public static class NamedElevationSelector
{
    /// <summary>
    /// Returns the candidate whose <see cref="NamedElevationCandidate.Name"/> exactly (ordinally) matches
    /// <paramref name="configuredName"/> when one exists; otherwise the candidate with the lowest
    /// <see cref="NamedElevationCandidate.Elevation"/>, ties broken by the lowest ordinal
    /// <see cref="NamedElevationCandidate.Name"/>; or <see langword="null"/> when <paramref name="candidates"/>
    /// is empty and no configured name matched. A blank <paramref name="configuredName"/> is treated the same
    /// as <see langword="null"/> (no configured override).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="candidates"/> is <see langword="null"/>.</exception>
    public static NamedElevationCandidate? SelectLowestElevation(IReadOnlyList<NamedElevationCandidate> candidates, string? configuredName)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (!string.IsNullOrWhiteSpace(configuredName))
        {
            foreach (NamedElevationCandidate candidate in candidates)
            {
                if (string.Equals(candidate.Name, configuredName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
        }

        NamedElevationCandidate? best = null;
        foreach (NamedElevationCandidate candidate in candidates)
        {
            if (best is null
                || candidate.Elevation < best.Elevation
                || (candidate.Elevation == best.Elevation && string.CompareOrdinal(candidate.Name, best.Name) < 0))
            {
                best = candidate;
            }
        }

        return best;
    }
}
