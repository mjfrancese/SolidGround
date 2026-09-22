using System.Globalization;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Provenance;

namespace SolidGround.Revit.Provenance;

/// <summary>
/// Publishes, or validates an already-published, Extensible Storage <see cref="Schema"/> for
/// <see cref="ExtensibleStorageProvenanceSchema"/> (SolidGround Issue #16 design record §3). The field-list
/// contract itself lives in <c>SolidGround.Core</c> (AGENTS.md "Provenance decision"); this is the only place
/// that turns it into a real <c>SchemaBuilder</c> call. Every member used here is verified against
/// the "Revit 2027 API surface" table of <c>docs/architecture/revit-extensible-storage-provenance.md</c> (reflected against the installed Revit 2027 <c>RevitAPI.dll</c> 27.0.10.13).
/// </summary>
internal static class ProvenanceSchemaAdapter
{
    /// <summary>
    /// Returns SolidGround's own published schema, publishing it for the first time this Revit session if
    /// <see cref="Schema.Lookup(Guid)"/> does not yet know about it. When a schema is already registered
    /// under <see cref="ExtensibleStorageProvenanceSchema.SchemaGuid"/>, its shape is compared exactly against
    /// the expected contract (the owner's other add-in's own <c>RequireExactSchema</c> precedent) rather than trusted as-is.
    /// </summary>
    /// <exception cref="ProvenanceAttachmentException">
    /// An already-registered schema under this GUID has drifted from the expected name, vendor id, access
    /// levels, or field set, or a fresh <c>SchemaBuilder</c> precondition or <c>Finish()</c> call failed.
    /// </exception>
    internal static Schema EnsurePublishedSchema()
    {
        Schema? existing = Schema.Lookup(ExtensibleStorageProvenanceSchema.SchemaGuid);
        if (existing is not null)
        {
            RequireExactSchema(existing);
            return existing;
        }

        return PublishSchema();
    }

    private static Schema PublishSchema()
    {
        // Defensive preconditions on SolidGround's own compile-time constants (design record §3): these are
        // never expected to fail, but SchemaBuilder.Finish()'s own documented exception list ("SchemaName is
        // not set", "VendorId is not set for a restricted access level") gives no early, field-specific
        // signal, so checking the two static validators plus AcceptableName first gives a precise failure
        // reason instead of a bare Finish() rejection.
        if (!SchemaBuilder.GUIDIsValid(ExtensibleStorageProvenanceSchema.SchemaGuid))
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround's Extensible Storage schema GUID '{ExtensibleStorageProvenanceSchema.SchemaGuidText}' " +
                "failed SchemaBuilder.GUIDIsValid; the schema could not be published.");
        }

        if (!SchemaBuilder.VendorIdIsValid(ExtensibleStorageProvenanceSchema.VendorId))
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround's Extensible Storage vendor id '{ExtensibleStorageProvenanceSchema.VendorId}' " +
                "failed SchemaBuilder.VendorIdIsValid; the schema could not be published.");
        }

        SchemaBuilder builder = new(ExtensibleStorageProvenanceSchema.SchemaGuid);

        if (!builder.AcceptableName(ExtensibleStorageProvenanceSchema.SchemaName))
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround's Extensible Storage schema name '{ExtensibleStorageProvenanceSchema.SchemaName}' " +
                "failed SchemaBuilder.AcceptableName; the schema could not be published.");
        }

        builder.SetSchemaName(ExtensibleStorageProvenanceSchema.SchemaName);
        builder.SetVendorId(ExtensibleStorageProvenanceSchema.VendorId);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        builder.SetDocumentation(ExtensibleStorageProvenanceSchema.Documentation);

        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchema.Fields)
        {
            FieldBuilder fieldBuilder = builder.AddSimpleField(field.Name, field.ClrType);
            fieldBuilder.SetDocumentation(field.Documentation);
            if (field.IsLengthSpec)
            {
                fieldBuilder.SetSpec(SpecTypeId.Length);
            }
        }

        try
        {
            return builder.Finish();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            throw new ProvenanceAttachmentException(
                $"SolidGround could not finish publishing its Extensible Storage schema " +
                $"'{ExtensibleStorageProvenanceSchema.SchemaName}' (GUID '{ExtensibleStorageProvenanceSchema.SchemaGuidText}'): {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Compares <paramref name="schema"/> against the expected contract field-by-field, by name (via
    /// <see cref="Schema.ListFields"/>, never by position -- design record §3), throwing on the first drift
    /// found, in the order: schema name, vendor id, read/write access level, field count, then each field's
    /// presence, <see cref="Field.ValueType"/>, and spec. Never silently adapts to a drifted schema (the owner's other add-in's
    /// own precedent, backed by a real historical bug about trusting an unvalidated predecessor read).
    /// </summary>
    private static void RequireExactSchema(Schema schema)
    {
        string guidText = ExtensibleStorageProvenanceSchema.SchemaGuidText;

        if (!string.Equals(schema.SchemaName, ExtensibleStorageProvenanceSchema.SchemaName, StringComparison.Ordinal))
        {
            throw new ProvenanceAttachmentException(
                $"The Extensible Storage schema already registered under GUID '{guidText}' has SchemaName " +
                $"'{schema.SchemaName}', expected '{ExtensibleStorageProvenanceSchema.SchemaName}'.");
        }

        // Revit stores vendor ids upper-cased (RevitAPI.xml), so SolidGround's own valid schema must not be
        // misjudged as drifted after its first SetVendorId call -- compared case-insensitively for this
        // reason (design record §3, round 2 finding 1/4); SchemaName above has no such normalization.
        if (!string.Equals(schema.VendorId, ExtensibleStorageProvenanceSchema.VendorId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ProvenanceAttachmentException(
                $"The Extensible Storage schema already registered under GUID '{guidText}' has VendorId " +
                $"'{schema.VendorId}', expected '{ExtensibleStorageProvenanceSchema.VendorId}' (case-insensitive).");
        }

        if (schema.ReadAccessLevel != AccessLevel.Public)
        {
            throw new ProvenanceAttachmentException(
                $"The Extensible Storage schema already registered under GUID '{guidText}' has ReadAccessLevel " +
                $"{schema.ReadAccessLevel}, expected {AccessLevel.Public}.");
        }

        if (schema.WriteAccessLevel != AccessLevel.Vendor)
        {
            throw new ProvenanceAttachmentException(
                $"The Extensible Storage schema already registered under GUID '{guidText}' has WriteAccessLevel " +
                $"{schema.WriteAccessLevel}, expected {AccessLevel.Vendor}.");
        }

        IList<Field> actualFields = schema.ListFields();
        if (actualFields.Count != ExtensibleStorageProvenanceSchema.Fields.Count)
        {
            throw new ProvenanceAttachmentException(
                $"The Extensible Storage schema already registered under GUID '{guidText}' has " +
                $"{actualFields.Count.ToString(CultureInfo.InvariantCulture)} field(s), expected " +
                $"{ExtensibleStorageProvenanceSchema.Fields.Count.ToString(CultureInfo.InvariantCulture)}.");
        }

        Dictionary<string, Field> actualFieldsByName = new(StringComparer.Ordinal);
        foreach (Field actualField in actualFields)
        {
            actualFieldsByName[actualField.FieldName] = actualField;
        }

        foreach (ProvenanceFieldDefinition expectedField in ExtensibleStorageProvenanceSchema.Fields)
        {
            if (!actualFieldsByName.TryGetValue(expectedField.Name, out Field? actualField))
            {
                throw new ProvenanceAttachmentException(
                    $"The Extensible Storage schema already registered under GUID '{guidText}' is missing field '{expectedField.Name}'.");
            }

            if (actualField.ValueType != expectedField.ClrType)
            {
                throw new ProvenanceAttachmentException(
                    $"The Extensible Storage schema already registered under GUID '{guidText}' has field " +
                    $"'{expectedField.Name}' typed {actualField.ValueType}, expected {expectedField.ClrType}.");
            }

            ForgeTypeId actualSpec = actualField.GetSpecTypeId();
            bool actualHasNoSpec = actualSpec.Empty();
            bool actualIsLengthSpec = !actualHasNoSpec && string.Equals(actualSpec.TypeId, SpecTypeId.Length.TypeId, StringComparison.Ordinal);

            if (expectedField.IsLengthSpec && !actualIsLengthSpec)
            {
                throw new ProvenanceAttachmentException(
                    $"The Extensible Storage schema already registered under GUID '{guidText}' has field " +
                    $"'{expectedField.Name}' with spec '{actualSpec.TypeId}', expected '{SpecTypeId.Length.TypeId}'.");
            }

            if (!expectedField.IsLengthSpec && !actualHasNoSpec)
            {
                throw new ProvenanceAttachmentException(
                    $"The Extensible Storage schema already registered under GUID '{guidText}' has field " +
                    $"'{expectedField.Name}' with spec '{actualSpec.TypeId}', expected no spec.");
            }
        }
    }
}
