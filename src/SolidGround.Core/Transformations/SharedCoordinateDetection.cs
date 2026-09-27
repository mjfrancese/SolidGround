namespace SolidGround.Core.Transformations;

/// <summary>
/// Revit-free decision for whether a document already appears to have shared coordinates set (SolidGround
/// Issue #30, PH3-3, 2026-09-27 live-evidence fix). Takes only plain numbers, deliberately never a "clipped"
/// flag of any kind, so the decision can be pinned by an offline unit test: a live Revit 2027 session (manual
/// evidence Step 14.5, 2026-09-27) found that a brand-new document opened from Revit's own default template
/// (Default_I_ENU.rte) reports its survey point already carrying that startup state true at the internal
/// origin, so a proxy that read it refused the opt-in write on a genuinely uncoordinated model. See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Shared-coordinates detection" section for
/// the full reasoning, the corrected proxy, and this exact live finding.
/// </summary>
public static class SharedCoordinateDetection
{
    /// <summary>
    /// <see langword="true"/> (already coordinated) when <paramref name="projectLocationCount"/> is greater
    /// than 1, OR any of <paramref name="projectPositionEastWest"/>/<paramref name="projectPositionNorthSouth"/>/
    /// <paramref name="projectPositionElevation"/> exceeds <paramref name="lengthTolerance"/> in absolute value,
    /// OR <paramref name="projectPositionAngle"/> exceeds <paramref name="angleTolerance"/> in absolute value,
    /// OR the survey point (<paramref name="surveyPointX"/>, <paramref name="surveyPointY"/>,
    /// <paramref name="surveyPointZ"/>) is farther than <paramref name="lengthTolerance"/> from the origin.
    /// Never considers a "clipped" flag of any kind -- there is no such parameter here to pass one through even
    /// by mistake.
    /// </summary>
    /// <param name="projectPositionEastWest">
    /// <c>Document.ActiveProjectLocation.GetProjectPosition(XYZ.Zero).EastWest</c>, Revit-internal decimal feet.
    /// </param>
    /// <param name="projectPositionNorthSouth">The same <c>ProjectPosition</c>'s <c>NorthSouth</c>, Revit-internal decimal feet.</param>
    /// <param name="projectPositionElevation">The same <c>ProjectPosition</c>'s <c>Elevation</c>, Revit-internal decimal feet.</param>
    /// <param name="projectPositionAngle">The same <c>ProjectPosition</c>'s <c>Angle</c>, radians.</param>
    /// <param name="surveyPointX"><c>BasePoint.GetSurveyPoint(document).Position.X</c>, Revit-internal decimal feet.</param>
    /// <param name="surveyPointY">The same survey point's <c>Position.Y</c>, Revit-internal decimal feet.</param>
    /// <param name="surveyPointZ">The same survey point's <c>Position.Z</c>, Revit-internal decimal feet.</param>
    /// <param name="projectLocationCount">
    /// <c>Document.ProjectLocations.Size</c>. Always at least 1: a document always has the default "Internal"
    /// <c>ProjectLocation</c>.
    /// </param>
    /// <param name="lengthTolerance">
    /// A non-negative length tolerance, Revit-internal decimal feet (the caller's own
    /// <c>Application.VertexTolerance</c>), reused for both the project-position axes and the survey-point
    /// distance check.
    /// </param>
    /// <param name="angleTolerance">A non-negative angle tolerance, radians.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Any of the nine double parameters is not finite (<see cref="double.NaN"/> or infinite);
    /// <paramref name="lengthTolerance"/> or <paramref name="angleTolerance"/> is negative; or
    /// <paramref name="projectLocationCount"/> is less than 1.
    /// </exception>
    public static bool LooksAlreadyCoordinated(
        double projectPositionEastWest,
        double projectPositionNorthSouth,
        double projectPositionElevation,
        double projectPositionAngle,
        double surveyPointX,
        double surveyPointY,
        double surveyPointZ,
        int projectLocationCount,
        double lengthTolerance,
        double angleTolerance)
    {
        RequireFinite(projectPositionEastWest, nameof(projectPositionEastWest));
        RequireFinite(projectPositionNorthSouth, nameof(projectPositionNorthSouth));
        RequireFinite(projectPositionElevation, nameof(projectPositionElevation));
        RequireFinite(projectPositionAngle, nameof(projectPositionAngle));
        RequireFinite(surveyPointX, nameof(surveyPointX));
        RequireFinite(surveyPointY, nameof(surveyPointY));
        RequireFinite(surveyPointZ, nameof(surveyPointZ));
        RequireFinite(lengthTolerance, nameof(lengthTolerance));
        RequireFinite(angleTolerance, nameof(angleTolerance));

        if (lengthTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lengthTolerance), lengthTolerance, "lengthTolerance must not be negative.");
        }

        if (angleTolerance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(angleTolerance), angleTolerance, "angleTolerance must not be negative.");
        }

        if (projectLocationCount < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(projectLocationCount), projectLocationCount,
                "projectLocationCount must be at least 1: Document.ProjectLocations always contains at least the default \"Internal\" location.");
        }

        if (projectLocationCount > 1)
        {
            return true;
        }

        if (Math.Abs(projectPositionEastWest) > lengthTolerance
            || Math.Abs(projectPositionNorthSouth) > lengthTolerance
            || Math.Abs(projectPositionElevation) > lengthTolerance)
        {
            return true;
        }

        if (Math.Abs(projectPositionAngle) > angleTolerance)
        {
            return true;
        }

        double surveyPointDistance = Math.Sqrt(
            (surveyPointX * surveyPointX) + (surveyPointY * surveyPointY) + (surveyPointZ * surveyPointZ));
        return surveyPointDistance > lengthTolerance;
    }

    private static void RequireFinite(double value, string paramName)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"{paramName} must be finite.");
        }
    }
}
