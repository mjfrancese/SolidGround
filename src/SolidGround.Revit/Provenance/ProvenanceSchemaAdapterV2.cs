using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using SolidGround.Core.Provenance;

namespace SolidGround.Revit.Provenance;

/// <summary>Publishes and validates the immutable v2 schema; it never alters the v1 schema.</summary>
internal static class ProvenanceSchemaAdapterV2
{
    internal static Schema EnsurePublishedSchema()
    {
        Schema? existing = Schema.Lookup(ExtensibleStorageProvenanceSchemaV2.SchemaGuid);
        if (existing is not null)
        {
            RequireSchema(existing);
            return existing;
        }

        SchemaBuilder builder = new(ExtensibleStorageProvenanceSchemaV2.SchemaGuid);
        if (!SchemaBuilder.GUIDIsValid(ExtensibleStorageProvenanceSchemaV2.SchemaGuid) ||
            !SchemaBuilder.VendorIdIsValid(ExtensibleStorageProvenanceSchemaV2.VendorId) ||
            !builder.AcceptableName(ExtensibleStorageProvenanceSchemaV2.SchemaName))
        {
            throw new ProvenanceAttachmentException("SolidGround v2 Extensible Storage schema constants are not accepted by the Revit 2027 SchemaBuilder.");
        }

        builder.SetSchemaName(ExtensibleStorageProvenanceSchemaV2.SchemaName);
        builder.SetVendorId(ExtensibleStorageProvenanceSchemaV2.VendorId);
        builder.SetReadAccessLevel(AccessLevel.Public);
        builder.SetWriteAccessLevel(AccessLevel.Vendor);
        builder.SetDocumentation(ExtensibleStorageProvenanceSchemaV2.Documentation);
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchemaV2.Fields)
        {
            FieldBuilder fieldBuilder = builder.AddSimpleField(field.Name, field.ClrType);
            fieldBuilder.SetDocumentation(field.Documentation);
            if (field.Spec == ProvenanceFieldSpec.Length)
            {
                fieldBuilder.SetSpec(SpecTypeId.Length);
                RequireMeasurableUnit(field.Name, SpecTypeId.Length, UnitTypeId.Meters);
            }
            else if (field.Spec == ProvenanceFieldSpec.Number)
            {
                fieldBuilder.SetSpec(SpecTypeId.Number);
                RequireMeasurableUnit(field.Name, SpecTypeId.Number, UnitTypeId.General);
            }
        }

        Schema schema = builder.Finish();
        RequireSchema(schema);
        return schema;
    }

    internal static void RequireSchema(Schema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (schema.GUID != ExtensibleStorageProvenanceSchemaV2.SchemaGuid ||
            !string.Equals(schema.SchemaName, ExtensibleStorageProvenanceSchemaV2.SchemaName, StringComparison.Ordinal) ||
            !string.Equals(schema.VendorId, ExtensibleStorageProvenanceSchemaV2.VendorId, StringComparison.OrdinalIgnoreCase) ||
            schema.ReadAccessLevel != AccessLevel.Public || schema.WriteAccessLevel != AccessLevel.Vendor)
        {
            throw new ProvenanceAttachmentException("A registered SolidGround v2 Extensible Storage schema does not match the required GUID, name, vendor, or access levels.");
        }

        Dictionary<string, Field> actual = schema.ListFields().ToDictionary(field => field.FieldName, StringComparer.Ordinal);
        if (actual.Count != ExtensibleStorageProvenanceSchemaV2.Fields.Count)
        {
            throw new ProvenanceAttachmentException("A registered SolidGround v2 Extensible Storage schema has a different field shape.");
        }

        foreach (ProvenanceFieldDefinition expected in ExtensibleStorageProvenanceSchemaV2.Fields)
        {
            if (!actual.TryGetValue(expected.Name, out Field? field) || field.ValueType != expected.ClrType)
            {
                throw new ProvenanceAttachmentException($"A registered SolidGround v2 Extensible Storage schema has a missing or mistyped '{expected.Name}' field.");
            }

            ForgeTypeId actualSpec = field.GetSpecTypeId();
            ForgeTypeId? requiredSpec = expected.Spec switch
            {
                ProvenanceFieldSpec.None => null,
                ProvenanceFieldSpec.Length => SpecTypeId.Length,
                ProvenanceFieldSpec.Number => SpecTypeId.Number,
                _ => throw new ProvenanceAttachmentException($"SolidGround v2 field '{expected.Name}' has an unknown specification."),
            };
            if (requiredSpec is null ? !actualSpec.Empty() : actualSpec.Empty() || !string.Equals(actualSpec.TypeId, requiredSpec.TypeId, StringComparison.Ordinal))
            {
                throw new ProvenanceAttachmentException($"A registered SolidGround v2 Extensible Storage schema has an incompatible specification for '{expected.Name}'.");
            }
        }
    }

    private static void RequireMeasurableUnit(string fieldName, ForgeTypeId spec, ForgeTypeId unit)
    {
        if (!UnitUtils.IsMeasurableSpec(spec) || !UnitUtils.IsValidUnit(spec, unit))
        {
            throw new ProvenanceAttachmentException($"SolidGround v2 field '{fieldName}' has an invalid Revit measurable spec/unit pair.");
        }
    }
}
