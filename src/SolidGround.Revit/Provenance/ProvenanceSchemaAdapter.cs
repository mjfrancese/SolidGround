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

            ForgeTypeId? spec = field.Spec switch
            {
                ProvenanceFieldSpec.Length => SpecTypeId.Length,
                ProvenanceFieldSpec.Number => SpecTypeId.Number,
                ProvenanceFieldSpec.None => null,
                _ => throw new ProvenanceAttachmentException(
                    $"SolidGround's Extensible Storage field '{field.Name}' has an unrecognized spec " +
                    $"'{field.Spec}'; the schema could not be published."),
            };

            if (spec is not null)
            {
                fieldBuilder.SetSpec(spec);

                // Round 1 review finding: SchemaBuilder.SetSpec(SpecTypeId.Number) succeeding does not by
                // itself confirm UnitTypeId.General is a valid unit for that spec -- RevitAPI.xml documents
                // no Number/General pairing table (docs checked 2026-09-23), so this asserts it explicitly,
                // once per Number-spec field, turning a bad pairing into a named, fail-loud
                // ProvenanceAttachmentException here instead of a later raw ArgumentException out of
                // Entity.Set/Get in ProvenanceEntityWriter. UnitUtils.IsValidUnit is a static, Document-free
                // Revit API call (RevitAPI.xml:215844-215866), so this needs no live Revit session to add.
                //
                // Round 2 review finding: UnitUtils.IsValidUnit(ForgeTypeId, ForgeTypeId) itself throws
                // Autodesk.Revit.Exceptions.ArgumentException when its first argument is not a measurable spec
                // (RevitAPI.xml:215844-215866, "specTypeId is not a measurable spec identifier") -- a different
                // failure mode than the false return value this check exists to handle, and one that would
                // otherwise reach EnsurePublishedSchema's caller unwrapped, before the try/catch around
                // builder.Finish() below ever runs. UnitUtils.IsMeasurableSpec(ForgeTypeId) only documents an
                // ArgumentNullException (never reachable here: SpecTypeId.Number is a non-null static member),
                // so checking it first turns an unmeasurable spec into the same named, fail-loud
                // ProvenanceAttachmentException as an invalid pairing, instead of letting IsValidUnit itself
                // throw one call earlier.
                if (field.Spec == ProvenanceFieldSpec.Number)
                {
                    if (!UnitUtils.IsMeasurableSpec(SpecTypeId.Number))
                    {
                        throw new ProvenanceAttachmentException(
                            $"SolidGround's Extensible Storage field '{field.Name}' uses SpecTypeId.Number, but " +
                            "Revit reports that spec is not measurable (UnitUtils.IsMeasurableSpec returned " +
                            "false); the schema could not be published.");
                    }

                    if (!UnitUtils.IsValidUnit(SpecTypeId.Number, UnitTypeId.General))
                    {
                        throw new ProvenanceAttachmentException(
                            $"SolidGround's Extensible Storage field '{field.Name}' pairs SpecTypeId.Number with " +
                            "UnitTypeId.General, but Revit reports that unit invalid for that spec; the schema " +
                            "could not be published.");
                    }
                }
            }
            else if (fieldBuilder.NeedsUnits())
            {
                // FieldBuilder.NeedsUnits() ("Checks whether the field type requires explicit unit
                // conversions", RevitAPI.xml) reflects field.ClrType's own requirement directly, independent
                // of whether SetSpec was ever called, so a no-spec field in the field-list contract is
                // asserted against it here rather than trusted. This is the exact 2026-09-23 regression:
                // metersPerOutputUnit was silently spec-less until SchemaBuilder.Finish() rejected the whole
                // schema with "Units are required for field metersPerOutputUnit" -- a real failure, but one
                // this per-field check now catches earlier, still naming the same field.
                throw new ProvenanceAttachmentException(
                    $"SolidGround's Extensible Storage field '{field.Name}' has no spec in the field-list " +
                    "contract, but FieldBuilder.NeedsUnits() reports that its value type requires one; the " +
                    "schema could not be published.");
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
            string actualSpecDescription = actualHasNoSpec ? "(none)" : actualSpec.TypeId;

            // Compares the Number spec exactly the same way as the Length spec (both are a
            // string.Equals(...TypeId, StringComparison.Ordinal) match against a non-empty actual spec); only
            // the expected TypeId differs (design record §2/§3, extended 2026-09-23 for ProvenanceFieldSpec.Number).
            string? expectedSpecTypeId = expectedField.Spec switch
            {
                ProvenanceFieldSpec.Length => SpecTypeId.Length.TypeId,
                ProvenanceFieldSpec.Number => SpecTypeId.Number.TypeId,
                ProvenanceFieldSpec.None => null,
                _ => throw new ProvenanceAttachmentException(
                    $"SolidGround's own field-list contract has field '{expectedField.Name}' with an " +
                    $"unrecognized spec '{expectedField.Spec}'."),
            };

            if (expectedSpecTypeId is null)
            {
                if (!actualHasNoSpec)
                {
                    throw new ProvenanceAttachmentException(
                        $"The Extensible Storage schema already registered under GUID '{guidText}' has field " +
                        $"'{expectedField.Name}' with spec '{actualSpecDescription}', expected no spec.");
                }
            }
            else if (actualHasNoSpec || !string.Equals(actualSpec.TypeId, expectedSpecTypeId, StringComparison.Ordinal))
            {
                throw new ProvenanceAttachmentException(
                    $"The Extensible Storage schema already registered under GUID '{guidText}' has field " +
                    $"'{expectedField.Name}' with spec '{actualSpecDescription}', expected '{expectedSpecTypeId}'.");
            }
        }
    }
}
