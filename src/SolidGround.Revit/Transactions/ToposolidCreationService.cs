using Autodesk.Revit.DB;

namespace SolidGround.Revit.Transactions;

/// <summary>
/// The two ways <see cref="ToposolidCreationService.Create"/> can build a <see cref="Toposolid"/>. See
/// SolidGround Issue #15's design record §2.4 row 27 and Appendix A UNVERIFIED item 4: manual test step 8a
/// decides whether <see cref="CombinedOverload"/> stays the shipped default, or whether
/// <see cref="ProfilesThenSlabShapeEditor"/> replaces it, "with the reason recorded" per the locked design.
/// </summary>
internal enum ToposolidCreationStrategy
{
    /// <summary>Option A (the shipped default): <c>Toposolid.Create(document, profiles, points, topoTypeId, levelId)</c> in one call.</summary>
    CombinedOverload,

    /// <summary>Option B: <c>Toposolid.Create(document, profiles, topoTypeId, levelId)</c>, then <c>SlabShapeEditor.Enable()</c>/<c>AddPoints(points)</c>.</summary>
    ProfilesThenSlabShapeEditor,
}

/// <summary>
/// Wraps a Revit-thrown rejection of the generated boundary or points (error catalogue row 18) so the
/// command's transaction-handling code can catch one SolidGround-owned exception type instead of naming
/// every <c>Autodesk.Revit.Exceptions.*</c> family member by hand at the call site.
/// </summary>
internal sealed class ToposolidCreationException : Exception
{
    internal ToposolidCreationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Creates the <see cref="Toposolid"/> itself, dispatching on <see cref="ToposolidCreationStrategy"/>. Never
/// calls <see cref="Document.Regenerate"/> -- that is the command's own single call site, shared by both
/// strategies (design record §0.4 item 6/§6.5). Every Revit API member used here is verified against
/// <c>apidump/out/Autodesk.Revit.DB.{Toposolid,SlabShapeEditor}.txt</c> and
/// <c>apidump/out/Autodesk.Revit.Exceptions.{ArgumentException,InvalidOperationException}.txt</c>.
/// </summary>
internal static class ToposolidCreationService
{
    /// <summary>The shipped default strategy (design record §2.4 row 27), pending manual test step 8a's evidence.</summary>
    internal const ToposolidCreationStrategy DefaultStrategy = ToposolidCreationStrategy.CombinedOverload;

    /// <exception cref="ToposolidCreationException">
    /// Revit rejected <paramref name="profiles"/> or <paramref name="points"/> with an
    /// <see cref="Autodesk.Revit.Exceptions.ArgumentException"/> or
    /// <see cref="Autodesk.Revit.Exceptions.InvalidOperationException"/>.
    /// </exception>
    internal static Toposolid Create(
        Document document,
        IList<CurveLoop> profiles,
        IList<XYZ> points,
        ElementId toposolidTypeId,
        ElementId levelId,
        ToposolidCreationStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(toposolidTypeId);
        ArgumentNullException.ThrowIfNull(levelId);

        try
        {
            return strategy switch
            {
                ToposolidCreationStrategy.CombinedOverload =>
                    Toposolid.Create(document, profiles, points, toposolidTypeId, levelId),
                ToposolidCreationStrategy.ProfilesThenSlabShapeEditor =>
                    CreateViaSlabShapeEditor(document, profiles, points, toposolidTypeId, levelId),
                _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, "Unsupported toposolid creation strategy."),
            };
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException or Autodesk.Revit.Exceptions.InvalidOperationException)
        {
            throw new ToposolidCreationException(
                $"Revit rejected the generated toposolid boundary or points: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Option B. <see cref="SlabShapeEditor.Enable"/> returns <see langword="void"/> (not fluent) -- confirmed
    /// against the installed dump -- so this is deliberately two statements, never a
    /// <c>.Enable().AddPoints(...)</c> chain (design record §0.1 item 2).
    /// </summary>
    private static Toposolid CreateViaSlabShapeEditor(
        Document document, IList<CurveLoop> profiles, IList<XYZ> points, ElementId toposolidTypeId, ElementId levelId)
    {
        Toposolid toposolid = Toposolid.Create(document, profiles, toposolidTypeId, levelId);
        SlabShapeEditor editor = toposolid.GetSlabShapeEditor();
        editor.Enable();
        editor.AddPoints(points);
        return toposolid;
    }
}
