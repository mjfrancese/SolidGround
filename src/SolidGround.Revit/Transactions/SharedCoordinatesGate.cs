using System.Globalization;
using Autodesk.Revit.DB;

namespace SolidGround.Revit.Transactions;

/// <summary>
/// Detects whether a document already appears to have shared coordinates set. Preflight-safe: read-only, no
/// transaction. See docs/architecture/revit-property-line-and-shared-coordinates.md's "Shared-coordinates
/// detection" section for the full reasoning and the owner-accepted residual risk this proxy carries (owner
/// decision 1, 2026-09-26). No direct Revit API member answers this question; <see cref="BasePoint.IsShared"/>
/// is a fixed type discriminant (always <see langword="true"/> for the survey point, always
/// <see langword="false"/> for the project base point), not a usable runtime flag -- this repository's own
/// pre-existing <see cref="OrphanCheck"/> already relies on that same distinction.
/// </summary>
internal static class SharedCoordinatesDetector
{
    /// <summary>
    /// Biased toward refusing: a false positive here just refuses a legitimate write; a false negative would
    /// silently clobber real shared coordinates -- the harm owner decision 1 exists to prevent. Two
    /// independent signals, OR'd together: the survey point still sits at the internal origin and is
    /// unclipped (<c>Document.ResetSharedCoordinates()</c>'s own doc comment states this is exactly the
    /// never-coordinated baseline), or the document already carries more than the one default "Internal"
    /// <see cref="ProjectLocation"/>.
    /// </summary>
    internal static bool LooksAlreadyCoordinated(Document document)
    {
        ArgumentNullException.ThrowIfNull(document);

        BasePoint surveyPoint = BasePoint.GetSurveyPoint(document);
        bool looksNeverCoordinated = surveyPoint.Position.IsAlmostEqualTo(XYZ.Zero) && !surveyPoint.Clipped;
        bool hasExtraProjectLocations = document.ProjectLocations.Size > 1;
        return !looksNeverCoordinated || hasExtraProjectLocations;
    }
}

/// <summary>
/// Wraps a Revit-thrown rejection of the shared-coordinates write (error catalogue row 20b), mirroring
/// <see cref="ToposolidCreationException"/>/<see cref="PropertyLineCreationException"/>'s own shape exactly, so
/// the command's transaction-handling code can catch one SolidGround-owned exception type instead of naming
/// every <c>Autodesk.Revit.Exceptions.*</c> family member by hand at the call site.
/// </summary>
internal sealed class SharedCoordinatesWriteException : Exception
{
    internal SharedCoordinatesWriteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Writes and verifies this run's own opt-in shared-coordinates write, inside the open transaction only. See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Shared-coordinates detection, write, and
/// verification" section.
/// </summary>
internal static class SharedCoordinatesWriter
{
    /// <summary>
    /// <see cref="ProjectPosition.Angle"/> is always exactly the <c>0d</c> literal this method passes, never a
    /// computed value (owner decision 2, 2026-09-26: no grid-convergence correction), so an exact-equality-scale
    /// tolerance is correct in <see cref="VerifyWritten"/> and catches any unexpected Revit-side rotation side
    /// effect without being sensitive to the floating-point noise the other three axes' own tolerance already
    /// absorbs.
    /// </summary>
    private const double AngleToleranceRadians = 1e-9;

    /// <summary>
    /// Writes <paramref name="eastWestInternal"/>/<paramref name="northSouthInternal"/>/<paramref name="elevationInternal"/>
    /// (Revit-internal decimal feet) as this model's shared coordinates, at zero rotation, then explicitly
    /// clips the survey point rather than relying on an unconfirmed automatic side effect of
    /// <see cref="ProjectLocation.SetProjectPosition"/> -- see docs/architecture/revit-property-line-and-shared-coordinates.md's
    /// "Why <c>Write</c> also sets <c>Clipped</c>" section for why an unclipped survey point at the internal
    /// origin is exactly what <see cref="SharedCoordinatesDetector.LooksAlreadyCoordinated"/> treats as "never
    /// coordinated," and why that would make a second run against this same document's own prior write
    /// self-defeating without this line.
    /// </summary>
    /// <exception cref="SharedCoordinatesWriteException">
    /// Revit rejected the write. <see cref="ProjectLocation.SetProjectPosition"/> documents
    /// <see cref="Autodesk.Revit.Exceptions.ArgumentNullException"/> and
    /// <see cref="Autodesk.Revit.Exceptions.InvalidOperationException"/>; <see cref="BasePoint.Clipped"/>'s
    /// setter documents the same <see cref="Autodesk.Revit.Exceptions.InvalidOperationException"/> for a
    /// non-shared <see cref="BasePoint"/>, structurally unreachable here since
    /// <see cref="BasePoint.GetSurveyPoint"/> always returns the shared survey point.
    /// </exception>
    internal static ProjectPosition Write(Document document, double eastWestInternal, double northSouthInternal, double elevationInternal)
    {
        ArgumentNullException.ThrowIfNull(document);

        ProjectPosition position = new(eastWestInternal, northSouthInternal, elevationInternal, angle: 0d);
        try
        {
            document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position);
            BasePoint.GetSurveyPoint(document).Clipped = true;
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentNullException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            throw new SharedCoordinatesWriteException($"Revit rejected the shared-coordinates write: {ex.Message}", ex);
        }

        return position;
    }

    /// <summary>
    /// The caller must call <c>document.Regenerate()</c> between <see cref="Write"/> and this method: whether
    /// that is actually load-bearing (as opposed to already-reflected) is unconfirmed by any documentation
    /// source, so this design inserts it defensively rather than depending on an unconfirmed same-transaction
    /// read-after-write assumption. See docs/architecture/revit-property-line-and-shared-coordinates.md's "The
    /// write itself and its source value" section's "Why a second <c>document.Regenerate()</c> call is
    /// required before verification." passage.
    /// </summary>
    /// <exception cref="SharedCoordinatesWriteException">
    /// Revit rejected the read-back. <see cref="ProjectLocation.GetProjectPosition"/> documents the identical
    /// <see cref="Autodesk.Revit.Exceptions.ArgumentNullException"/> and
    /// <see cref="Autodesk.Revit.Exceptions.InvalidOperationException"/> pair that
    /// <see cref="ProjectLocation.SetProjectPosition"/> documents, which <see cref="Write"/>'s own doc comment
    /// above already translates the same way for that call.
    /// </exception>
    internal static bool VerifyWritten(Document document, ProjectPosition requested, double toleranceInternal, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(requested);

        ProjectPosition actual;
        try
        {
            actual = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentNullException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            throw new SharedCoordinatesWriteException($"Revit rejected the shared-coordinates write during verification: {ex.Message}", ex);
        }

        bool matches = Math.Abs(actual.EastWest - requested.EastWest) <= toleranceInternal
            && Math.Abs(actual.NorthSouth - requested.NorthSouth) <= toleranceInternal
            && Math.Abs(actual.Elevation - requested.Elevation) <= toleranceInternal
            && Math.Abs(actual.Angle - requested.Angle) <= AngleToleranceRadians;

        problem = matches
            ? null
            : string.Create(
                CultureInfo.InvariantCulture,
                $"The shared-coordinates write did not verify: requested (EW={requested.EastWest:R}, NS={requested.NorthSouth:R}, " +
                $"Elev={requested.Elevation:R}, Angle={requested.Angle:R}), read back (EW={actual.EastWest:R}, NS={actual.NorthSouth:R}, " +
                $"Elev={actual.Elevation:R}, Angle={actual.Angle:R}).");
        return matches;
    }
}
