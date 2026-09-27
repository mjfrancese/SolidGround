using System.Globalization;
using Autodesk.Revit.DB;
using SolidGround.Core.Transformations;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Transactions;

/// <summary>
/// Detects whether a document already appears to have shared coordinates set. Preflight-safe: read-only, no
/// transaction. See docs/architecture/revit-property-line-and-shared-coordinates.md's "Shared-coordinates
/// detection" section for the full reasoning, the corrected proxy, and the 2026-09-27 live-evidence finding
/// that required this fix (manual evidence Step 14.5): a brand-new document opened from Revit 2027's own
/// default template (Default_I_ENU.rte) already carries a never-touched, uncoordinated survey point's usual
/// startup state at the internal origin, so that state is not read here, in any form, by this class or by
/// <see cref="SharedCoordinateDetection"/>. The actual decision is delegated entirely to that Revit-free,
/// unit-tested function -- this class only reads the raw Revit values it needs and hands them across.
/// <see cref="BasePoint.IsShared"/> is a fixed type discriminant (always <see langword="true"/> for the survey
/// point, always <see langword="false"/> for the project base point), not a usable runtime flag -- this
/// repository's own pre-existing <see cref="OrphanCheck"/> already relies on that same distinction.
/// </summary>
internal static class SharedCoordinatesDetector
{
    /// <summary>
    /// A small, fixed angle tolerance (radians). See docs/architecture/revit-property-line-and-shared-coordinates.md's
    /// "Shared-coordinates detection" section: <see cref="ProjectPosition.Angle"/> is either exactly its
    /// startup value (0) or a real value a user or a prior SolidGround write actually set, so this does not
    /// need the same caller-supplied, machine-specific tolerance the length axes use.
    /// </summary>
    private const double AngleToleranceRadians = 1e-9;

    /// <summary>
    /// Reads <paramref name="document"/>'s own <see cref="ProjectPosition"/>, survey point position, and
    /// <see cref="Document.ProjectLocations"/> count, then defers the actual decision to
    /// <see cref="SharedCoordinateDetection.LooksAlreadyCoordinated"/>. Logs every raw value read and the
    /// final result.
    /// </summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="lengthToleranceInternal">
    /// <c>Application.VertexTolerance</c> (Revit-internal decimal feet). The caller already reads this once,
    /// at Preflight, via <c>commandData.Application.Application.VertexTolerance</c> (see
    /// <c>LogAndReadGeometryTolerances</c>) -- passed in here rather than read a second time.
    /// </param>
    internal static bool LooksAlreadyCoordinated(Document document, double lengthToleranceInternal)
    {
        ArgumentNullException.ThrowIfNull(document);

        ProjectPosition projectPosition = document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero);
        XYZ surveyPointPosition = BasePoint.GetSurveyPoint(document).Position;
        int projectLocationCount = document.ProjectLocations.Size;

        bool result = SharedCoordinateDetection.LooksAlreadyCoordinated(
            projectPosition.EastWest, projectPosition.NorthSouth, projectPosition.Elevation, projectPosition.Angle,
            surveyPointPosition.X, surveyPointPosition.Y, surveyPointPosition.Z,
            projectLocationCount, lengthToleranceInternal, AngleToleranceRadians);

        AddInLog.Info(
            $"SharedCoordinatesDetector read ProjectPosition(EastWest={projectPosition.EastWest.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"NorthSouth={projectPosition.NorthSouth.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"Elevation={projectPosition.Elevation.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"Angle={projectPosition.Angle.ToString("R", CultureInfo.InvariantCulture)}), survey point Position=" +
            $"({surveyPointPosition.X.ToString("R", CultureInfo.InvariantCulture)}, {surveyPointPosition.Y.ToString("R", CultureInfo.InvariantCulture)}, " +
            $"{surveyPointPosition.Z.ToString("R", CultureInfo.InvariantCulture)}), ProjectLocations.Size={projectLocationCount}, " +
            $"lengthToleranceInternal={lengthToleranceInternal.ToString("R", CultureInfo.InvariantCulture)}. LooksAlreadyCoordinated={result}.");

        return result;
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
    /// (Revit-internal decimal feet) as this model's shared coordinates, at zero rotation. Leaves the survey
    /// point's own prior startup state exactly as it was -- a 2026-09-27 live Revit 2027 session (manual
    /// evidence Step 14.5) found that Revit 2027's own default template already ships a brand-new document
    /// with that state set, so touching it here could never distinguish "SolidGround just wrote this" from
    /// "this document was never touched," and would additionally overwrite whatever setting the user's own
    /// document already had. This run's own write is instead detected afterward purely through the resulting
    /// non-zero <see cref="ProjectPosition"/> -- see docs/architecture/revit-property-line-and-shared-coordinates.md's
    /// "Why <c>Write</c> no longer sets <c>Clipped</c>" section.
    /// </summary>
    /// <exception cref="SharedCoordinatesWriteException">
    /// Revit rejected the write. <see cref="ProjectLocation.SetProjectPosition"/> documents
    /// <see cref="Autodesk.Revit.Exceptions.ArgumentNullException"/> and
    /// <see cref="Autodesk.Revit.Exceptions.InvalidOperationException"/>.
    /// </exception>
    internal static ProjectPosition Write(Document document, double eastWestInternal, double northSouthInternal, double elevationInternal)
    {
        ArgumentNullException.ThrowIfNull(document);

        ProjectPosition position = new(eastWestInternal, northSouthInternal, elevationInternal, angle: 0d);
        try
        {
            document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, position);
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
