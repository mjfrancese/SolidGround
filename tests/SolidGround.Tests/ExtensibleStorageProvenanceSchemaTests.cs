using SolidGround.Core.Provenance;

namespace SolidGround.Tests;

/// <summary>
/// Tests for <see cref="ExtensibleStorageProvenanceSchema"/>'s field-list contract (SolidGround Issue #16).
/// </summary>
public sealed class ExtensibleStorageProvenanceSchemaTests
{
    private static readonly Type[] SupportedClrTypes = [typeof(int), typeof(string), typeof(bool), typeof(double)];

    [Fact]
    public void FieldListHasNoDuplicateNamesOrTypes()
    {
        IReadOnlyList<ProvenanceFieldDefinition> fields = ExtensibleStorageProvenanceSchema.Fields;

        Assert.Equal(36, fields.Count);

        HashSet<string> names = new(StringComparer.Ordinal);
        foreach (ProvenanceFieldDefinition field in fields)
        {
            Assert.True(names.Add(field.Name), $"Duplicate field name '{field.Name}'.");
            Assert.Contains(field.ClrType, SupportedClrTypes);
        }
    }

    [Fact]
    public void EveryLengthSpecFieldIsADoubleField()
    {
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchema.Fields)
        {
            if (field.Spec == ProvenanceFieldSpec.Length)
            {
                Assert.Equal(typeof(double), field.ClrType);
            }
        }

        int lengthSpecCount = ExtensibleStorageProvenanceSchema.Fields.Count(field => field.Spec == ProvenanceFieldSpec.Length);
        Assert.Equal(5, lengthSpecCount);
    }

    [Fact]
    public void EveryDoubleFieldCarriesALengthOrNumberSpec()
    {
        // Revit 2027's SchemaBuilder.Finish() requires every floating-point field to carry a spec
        // (FieldBuilder.AddSimpleField's own documented remark: "Make sure to set the unit type if the field
        // contains floating-point values"). A 2026-09-23 live-Revit failure showed metersPerOutputUnit had
        // been left with no spec at all -- this test locks the fix so no double field can regress to
        // ProvenanceFieldSpec.None again, whatever new fields are added later.
        foreach (ProvenanceFieldDefinition field in ExtensibleStorageProvenanceSchema.Fields)
        {
            if (field.ClrType == typeof(double))
            {
                Assert.True(
                    field.Spec is ProvenanceFieldSpec.Length or ProvenanceFieldSpec.Number,
                    $"Field '{field.Name}' is a double field but carries spec {field.Spec}; " +
                    $"it must be {ProvenanceFieldSpec.Length} or {ProvenanceFieldSpec.Number}.");
            }
        }
    }

    [Fact]
    public void MetersPerOutputUnitCarriesTheNumberSpec()
    {
        // The exact field named in the 2026-09-23 SchemaBuilder.Finish() failure ("Units are required for
        // field metersPerOutputUnit"): a unitless ratio, not a length, so it must use ProvenanceFieldSpec.Number
        // rather than ProvenanceFieldSpec.Length or ProvenanceFieldSpec.None.
        ProvenanceFieldDefinition field = ExtensibleStorageProvenanceSchema.Fields.Single(candidate => candidate.Name == "metersPerOutputUnit");

        Assert.Equal(typeof(double), field.ClrType);
        Assert.Equal(ProvenanceFieldSpec.Number, field.Spec);
    }

    [Fact]
    public void FieldCountStaysUnderRevitsTwoHundredAndFiftySixFieldLimit()
    {
        Assert.True(
            ExtensibleStorageProvenanceSchema.Fields.Count < 256,
            "SchemaBuilder.Finish() throws when more than 256 fields were added to the schema.");
    }

    [Fact]
    public void SchemaNameContainsNoPunctuationRevitMightReject()
    {
        // the owner's other add-in's own SchemaBuilder.SetSchemaName call sites never pass a dotted name; no source states
        // AcceptableName's exact rule, so this locks the underscores-only convention design record §2 chose
        // instead of the dotted display name a losing proposal used.
        //
        // The exact-literal assertion below also guards the character loop itself: an empty or all-whitespace
        // SchemaName would make the loop body never execute, so a bare per-character check alone would still
        // pass against a regressed-to-empty constant (round 2 finding).
        Assert.Equal("SolidGround_Provenance_Toposolid", ExtensibleStorageProvenanceSchema.SchemaName);

        foreach (char character in ExtensibleStorageProvenanceSchema.SchemaName)
        {
            bool isAllowed = char.IsAsciiLetterOrDigit(character) || character == '_';
            Assert.True(isAllowed, $"SchemaName contains disallowed character '{character}'.");
        }
    }
}
