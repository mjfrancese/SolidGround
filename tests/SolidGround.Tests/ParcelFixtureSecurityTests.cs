using System.Text.Json;
using SolidGround.Core.Sources;

namespace SolidGround.Tests;

/// <summary>
/// New, additive fixture-security test (distinct from the untouched <see cref="FixtureSecurityTests"/>): scans
/// every committed JSON/GeoJSON fixture for any JSON property name that is owner-like per
/// <see cref="OwnerFieldNameGuard.IsOwnerLike"/> -- the same full check (fragments included)
/// <c>CountyParcelRegistry.Load</c> and <see cref="Core.Sources.LocalParcelFile.LocalParcelFileFieldMap.Validate"/>
/// already apply, uniformly, per AC2's "any other owner field" wording.
/// </summary>
public sealed class ParcelFixtureSecurityTests
{
    [Fact]
    public void NoCommittedJsonOrGeoJsonFixtureContainsAnOwnerLikePropertyName()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        List<string> violations = [];

        foreach (string path in Directory.EnumerateFiles(fixtureDirectory, "*", SearchOption.AllDirectories))
        {
            string extension = Path.GetExtension(path);
            if (!string.Equals(extension, ".json", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(extension, ".geojson", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string content = File.ReadAllText(path);
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(content);
            }
            catch (JsonException)
            {
                continue;
            }

            using (document)
            {
                foreach (string propertyName in EnumeratePropertyNames(document.RootElement))
                {
                    if (OwnerFieldNameGuard.IsOwnerLike(propertyName))
                    {
                        violations.Add($"{Path.GetFileName(path)}: property '{propertyName}'");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0, "Owner-like JSON property name(s) found in committed fixtures: " + string.Join(", ", violations));
    }

    [Fact]
    public void TheUnderlyingDetectionMechanismFlagsAnOwnerLikePropertyNameWhenPresent()
    {
        // A self-check proving the scan above would catch a real violation, using a non-committed, inline
        // JSON literal (never a Fixtures/ file) so this proof never itself becomes the violation it detects.
        using JsonDocument document = JsonDocument.Parse("""{"nested":{"owner":"SYNTHETIC OWNER"}}""");

        bool foundAnOwnerLikeName = EnumeratePropertyNames(document.RootElement).Any(OwnerFieldNameGuard.IsOwnerLike);

        Assert.True(foundAnOwnerLikeName);
    }

    private static IEnumerable<string> EnumeratePropertyNames(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    yield return property.Name;
                    foreach (string nested in EnumeratePropertyNames(property.Value))
                    {
                        yield return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    foreach (string nested in EnumeratePropertyNames(item))
                    {
                        yield return nested;
                    }
                }

                break;
        }
    }
}
