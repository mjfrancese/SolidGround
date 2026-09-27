using Autodesk.Revit.DB;

namespace SolidGround.Revit.Transactions;

/// <summary>
/// Wraps a Revit-thrown rejection of the generated property-line boundary (error catalogue row 20a) so the
/// command's transaction-handling code can catch one SolidGround-owned exception type instead of naming every
/// <c>Autodesk.Revit.Exceptions.*</c> family member by hand at the call site. Mirrors
/// <see cref="ToposolidCreationException"/>'s own shape exactly.
/// </summary>
internal sealed class PropertyLineCreationException : Exception
{
    internal PropertyLineCreationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Creates the <see cref="PropertyLine"/> itself, from its own independently-built <see cref="CurveLoop"/>
/// list -- never <see cref="ToposolidCreationService"/>'s already-consumed <c>profiles</c> list. See
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "PropertyLine creation (Revit)" section,
/// "Why PropertyLine creation builds its own independent CurveLoop list, not <c>profiles</c> itself" subsection,
/// for why. Every Revit API member used here is verified in
/// docs/architecture/revit-property-line-and-shared-coordinates.md's "Revit 2027 API surface used (new members,
/// beyond `revit-toposolid-creation.md`'s existing table)" section: <c>PropertyLine.Create(Document, IList&lt;CurveLoop&gt;)</c> documents
/// <c>ArgumentException</c>, <c>ArgumentNullException</c>, <c>InvalidOperationException</c>,
/// <c>ModificationForbiddenException</c>, and <c>ModificationOutsideTransactionException</c> -- a strictly
/// broader list than <c>Toposolid.Create</c>'s own doc page, which <see cref="ToposolidCreationService"/>'s
/// existing two-exception catch filter mirrors, so this filter widens to three, deliberately still excluding
/// <c>ModificationOutsideTransactionException</c> (a SolidGround programming bug -- no open transaction -- not a
/// user-addressable condition; the generic Stage-5 catch-all's own message is the honest one for that case).
/// </summary>
internal static class PropertyLineCreationService
{
    internal static PropertyLine Create(Document document, IList<CurveLoop> profiles)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(profiles);

        try
        {
            return PropertyLine.Create(document, profiles);
        }
        catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException
            or Autodesk.Revit.Exceptions.InvalidOperationException
            or Autodesk.Revit.Exceptions.ModificationForbiddenException)
        {
            throw new PropertyLineCreationException(
                $"Revit rejected the generated property line boundary: {ex.Message}", ex);
        }
    }
}
