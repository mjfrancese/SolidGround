using System.Globalization;
using Autodesk.Revit.DB;
using SolidGround.Revit.Diagnostics;

namespace SolidGround.Revit.Transactions;

/// <summary>One verification outcome: whether it passed, and a human-readable detail for logging or a rejection dialog.</summary>
internal sealed record VerificationResult(bool Passed, string Detail);

/// <summary>
/// Revit-side geometry sanity checks bracketing <see cref="ToposolidCreationService.Create"/>: a defensive,
/// pre-transaction planarity check on every built boundary profile (design record §7.2), and a post-create
/// bounding-box (plus opportunistic slab-shape-vertex-count) check before <c>transaction.Commit()</c> is ever
/// called (§6.5). Every Revit API member used here is verified against
/// <c>apidump/out/Autodesk.Revit.DB.{CurveLoop,BoundingBoxXYZ,Toposolid,SlabShapeEditor,
/// SlabShapeVertexArray}.txt</c> and the corrected <c>Element.get_BoundingBox(View)</c> indexed-property
/// finding recorded in <c>apidump/out/issue15-boundingbox-indexparams.txt</c> (design record §0.1 item 1).
/// </summary>
internal static class PostCreationVerification
{
    /// <summary>
    /// Cheap, purely defensive: every built <see cref="CurveLoop"/> must report a plane. This can only fail
    /// if boundary construction itself has a bug (every ring vertex was not actually given the same Z), never
    /// from terrain data -- <see cref="Geometry.BoundaryGeometryBuilder.BuildProfiles"/> makes a non-planar
    /// ring impossible by construction, so this exists to fail loudly at Preflight rather than silently
    /// inside the transaction if a construction mistake ever creeps in (design record §6.4 step 6).
    /// </summary>
    internal static bool AllProfilesArePlanar(IEnumerable<CurveLoop> profiles, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        int index = 0;
        foreach (CurveLoop loop in profiles)
        {
            if (!loop.HasPlane())
            {
                problem = $"Boundary profile loop {index} is not planar. This indicates a defect in SolidGround's boundary construction, not the terrain data.";
                return false;
            }

            index++;
        }

        problem = null;
        return true;
    }

    /// <summary>
    /// Runs after <see cref="ToposolidCreationService.Create"/> and <c>document.Regenerate()</c>, still inside
    /// the open transaction. Compares <paramref name="toposolid"/>'s real bounding box against
    /// <paramref name="expected"/> (computed purely from <paramref name="points"/>, design record §6.4 step
    /// 5): every dimension of <paramref name="expected"/> must lie within <paramref name="actual"/>'s
    /// bounding box, allowing <paramref name="toleranceInternal"/> of slack, so a boundary ring that is
    /// legitimately larger than the point cloud never trips this, while a Revit-side silent point drop or
    /// truncation (Appendix A UNVERIFIED item 4) does. A <see langword="null"/>/disabled bounding box
    /// immediately after <c>Regenerate()</c> (Appendix A UNVERIFIED item 6) is logged as a warning, not
    /// treated as a failure, for the same "general Revit convention, not independently confirmed" reason the
    /// design record gives.
    /// </summary>
    /// <param name="nativeToposolidMaxPointThreshold">
    /// The <c>Revit.ini</c> <c>NativeToposolidMaxPointThreshold</c> value <c>CreateToposolidCommand</c>'s
    /// Preflight stage already parsed and logged, or <see langword="null"/> when it could not be read this
    /// session. Named as the likely cause in the message below only when the slab-shape-vertex count comes
    /// back short of <paramref name="points"/> AND the supplied point count actually exceeded this value --
    /// SolidGround Issue #15's 2026-09-21 probe session found that combination is the likely cause: the
    /// combined <c>Toposolid.Create</c> overload never throws past that count, it just silently retains
    /// fewer vertices (<c>evidence/EVIDENCE-PROBES.md</c> Run 2, <c>ThresholdProbe</c>;
    /// <c>docs/architecture/revit-toposolid-creation.md</c>'s "Step 7"). Since Preflight already rejects a
    /// configured <c>pointBudget</c> above a known threshold before any transaction opens, a shortfall found
    /// here with the supplied point count still at or below a known threshold is not explained by this
    /// setting, and is reported differently.
    /// </param>
    internal static VerificationResult Verify(
        Toposolid toposolid,
        BoundingBoxXYZ expected,
        IList<XYZ> points,
        ToposolidCreationStrategy strategy,
        double toleranceInternal,
        int? nativeToposolidMaxPointThreshold)
    {
        ArgumentNullException.ThrowIfNull(toposolid);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(points);

        BoundingBoxXYZ? actual = toposolid.get_BoundingBox(null);
        if (actual is null)
        {
            AddInLog.Warning(
                "Toposolid.get_BoundingBox(null) returned null immediately after Regenerate(); treating this as " +
                "informational only (Appendix A UNVERIFIED item 6), not a verification failure.");
            return new VerificationResult(true, "Bounding box was not available immediately after regeneration; not treated as a failure.");
        }

        if (!Contains(actual, expected, toleranceInternal))
        {
            return new VerificationResult(
                false,
                "The created toposolid's bounding box did not cover the source terrain data's own bounding box " +
                $"within tolerance. Expected (at least) Min=({Format(expected.Min)}) Max=({Format(expected.Max)}); " +
                $"observed Min=({Format(actual.Min)}) Max=({Format(actual.Max)}).");
        }

        string vertexDetail = "Slab shape editor was not enabled; skipped the per-vertex check.";
        SlabShapeEditor editor = toposolid.GetSlabShapeEditor();
        if (editor.IsEnabled)
        {
            int vertexCount = editor.SlabShapeVertices.Size;
            vertexDetail = $"Slab shape editor reports {vertexCount} vertex(es) for {points.Count} supplied point(s).";
            if (vertexCount < points.Count)
            {
                return new VerificationResult(false, DescribeVertexShortfall(vertexCount, points.Count, nativeToposolidMaxPointThreshold));
            }
        }

        return new VerificationResult(true, $"Bounding box matched within tolerance ({strategy}). {vertexDetail}");
    }

    /// <summary>
    /// Names the likely cause and the remedy for a vertex shortfall, rather than just reporting the two
    /// counts: SolidGround Issue #15's 2026-09-21 probe session (<c>evidence/EVIDENCE-PROBES.md</c> Run 2,
    /// <c>ThresholdProbe</c>) found the combined <c>Toposolid.Create</c> overload silently caps its retained
    /// vertex count near <c>Revit.ini</c>'s <c>NativeToposolidMaxPointThreshold</c> once the supplied point
    /// count exceeds it, with no thrown exception -- the evidence never established which points are kept or
    /// by what mechanism, only the resulting count, so the message below describes the capped count, not a
    /// specific "discards points past N" selection rule. <paramref name="nativeToposolidMaxPointThreshold"/>
    /// is named as the likely cause only when <paramref name="pointCount"/> actually exceeds it. Because
    /// <c>CreateToposolidCommand</c>'s Preflight stage already rejects a configured <c>pointBudget</c> above
    /// a known threshold before any transaction opens, <paramref name="pointCount"/> can only reach this
    /// method already at or below a known threshold, or with the threshold unknown -- the "exceeded" branch
    /// exists for a hypothetical future path where that invariant no longer holds.
    /// </summary>
    private static string DescribeVertexShortfall(int vertexCount, int pointCount, int? nativeToposolidMaxPointThreshold)
    {
        string cause;
        if (nativeToposolidMaxPointThreshold is { } threshold && pointCount > threshold)
        {
            cause = "the likely cause is Revit's own Revit.ini NativeToposolidMaxPointThreshold setting, read as " +
                $"{threshold.ToString(CultureInfo.InvariantCulture)} at Preflight: Revit silently caps the retained " +
                "vertex count near that setting instead of throwing. Lower simplification.pointBudget to at most " +
                $"{threshold.ToString(CultureInfo.InvariantCulture)}, or raise the Revit.ini value within Autodesk's " +
                "documented 10,000 to 50,000 range and restart Revit, then run this command again";
        }
        else if (nativeToposolidMaxPointThreshold is { } knownThreshold)
        {
            cause = "the configured simplification.pointBudget was already at or below this machine's Revit.ini " +
                $"NativeToposolidMaxPointThreshold (read as {knownThreshold.ToString(CultureInfo.InvariantCulture)} " +
                "at Preflight), so that setting is unlikely to be the cause. Check SolidGround's log folder for " +
                "further diagnosis";
        }
        else
        {
            cause = "the likely cause is Revit's own Revit.ini NativeToposolidMaxPointThreshold setting, whose value " +
                "could not be read at Preflight this session: Revit silently caps the retained vertex count near " +
                "that setting instead of throwing. Lower simplification.pointBudget, or raise the Revit.ini value " +
                "within Autodesk's documented 10,000 to 50,000 range and restart Revit, then run this command again";
        }

        return $"The created toposolid recorded fewer slab shape vertices ({vertexCount}) than points supplied " +
            $"({pointCount}); {cause}.";
    }

    private static bool Contains(BoundingBoxXYZ actual, BoundingBoxXYZ expected, double tolerance)
    {
        XYZ actualMin = actual.Min;
        XYZ actualMax = actual.Max;
        XYZ expectedMin = expected.Min;
        XYZ expectedMax = expected.Max;

        return actualMin.X <= expectedMin.X + tolerance
            && actualMin.Y <= expectedMin.Y + tolerance
            && actualMin.Z <= expectedMin.Z + tolerance
            && actualMax.X >= expectedMax.X - tolerance
            && actualMax.Y >= expectedMax.Y - tolerance
            && actualMax.Z >= expectedMax.Z - tolerance;
    }

    private static string Format(XYZ point) => string.Create(
        CultureInfo.InvariantCulture,
        $"{point.X:R}, {point.Y:R}, {point.Z:R}");
}
