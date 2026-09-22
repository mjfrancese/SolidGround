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
            if (field.IsLengthSpec)
            {
                Assert.Equal(typeof(double), field.ClrType);
            }
        }

        int lengthSpecCount = ExtensibleStorageProvenanceSchema.Fields.Count(field => field.IsLengthSpec);
        Assert.Equal(5, lengthSpecCount);
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
