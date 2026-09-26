using SolidGround.Core.Sources.CountyParcels;

namespace SolidGround.Tests;

/// <summary>
/// A county parcel registry document is never a committed fixture (its <c>serviceBaseUrl</c> is inherently
/// <c>https://</c>, which <c>FixtureSecurityTests</c> bans anywhere under <c>Fixtures/</c>). Every test here
/// builds its own inline JSON and writes it to a uniquely named temporary file, deleted in a <c>finally</c>
/// block.
/// </summary>
public sealed class CountyParcelRegistryTests
{
    private const string ValidRegistryJson = """
        {
          "schemaVersion": 1,
          "counties": [
            {
              "geoid": "99999",
              "displayName": "Synthetic County (fixture only)",
              "serviceBaseUrl": "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer",
              "layerIndex": 0,
              "fieldMap": {
                "parcelId": "PARCEL_ID",
                "situsAddress": "SITUS_ADDR",
                "subdivision": "SUBDIVISION",
                "lot": "LOT_NUMBER",
                "block": "BLOCK",
                "plat": "PLAT_NUMBER",
                "book": "DEED_BOOK_PAGE",
                "page": "DEED_BOOK_PAGE",
                "legalDescription": "LEGAL",
                "reportedAcres": "ACRES",
                "zoning": "ZONING",
                "stableParcelId": null
              },
              "licenseDisclaimerText": "SYNTHETIC-FIXTURE-DISCLAIMER: no warranty of any kind."
            }
          ]
        }
        """;

    [Fact]
    public void LoadsAValidSyntheticRegistryKeyedByGeoid()
    {
        string path = WriteTempRegistry(ValidRegistryJson);
        try
        {
            CountyParcelRegistry registry = CountyParcelRegistry.Load(path);

            Assert.Equal(path, registry.SourcePath);
            CountyParcelRegistryEntry entry = Assert.Single(registry.EntriesByGeoid.Values);
            Assert.Equal("99999", entry.Geoid);
            Assert.Equal("Synthetic County (fixture only)", entry.DisplayName);
            Assert.Equal("https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer", entry.ServiceBaseUrl);
            Assert.Equal(0, entry.LayerIndex);
            Assert.Equal("PARCEL_ID", entry.FieldMap.ParcelId);
            Assert.Equal("SITUS_ADDR", entry.FieldMap.SitusAddress);
            Assert.Null(entry.FieldMap.StableParcelId);
            Assert.True(registry.EntriesByGeoid.ContainsKey("99999"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAnUnknownTopLevelProperty()
    {
        string json = ValidRegistryJson.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"unknownTopLevel\": true,");
        AssertRejectsWithFormatException(json);
    }

    [Fact]
    public void RejectsAnUnknownEntryProperty()
    {
        string json = ValidRegistryJson.Replace("\"geoid\": \"99999\",", "\"geoid\": \"99999\", \"unknownEntryProperty\": true,");
        AssertRejectsWithFormatException(json);
    }

    [Fact]
    public void RejectsAnUnknownFieldMapProperty()
    {
        string json = ValidRegistryJson.Replace("\"parcelId\": \"PARCEL_ID\",", "\"parcelId\": \"PARCEL_ID\", \"unknownFieldMapProperty\": true,");
        AssertRejectsWithFormatException(json);
    }

    [Theory]
    [InlineData("9999")]
    [InlineData("999999")]
    [InlineData("ABCDE")]
    [InlineData("")]
    public void RejectsANonFiveDigitGeoid(string geoid)
    {
        string json = ValidRegistryJson.Replace("\"geoid\": \"99999\",", $"\"geoid\": \"{geoid}\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("5 ASCII digits", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string DuplicateGeoidRegistryJson = """
        {
          "schemaVersion": 1,
          "counties": [
            {
              "geoid": "99999",
              "displayName": "Synthetic County (fixture only)",
              "serviceBaseUrl": "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer",
              "layerIndex": 0,
              "fieldMap": { "parcelId": "PARCEL_ID", "situsAddress": "SITUS_ADDR" },
              "licenseDisclaimerText": "SYNTHETIC-FIXTURE-DISCLAIMER: no warranty of any kind."
            },
            {
              "geoid": "99999",
              "displayName": "Synthetic County (fixture only), second entry",
              "serviceBaseUrl": "https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer",
              "layerIndex": 1,
              "fieldMap": { "parcelId": "PARCEL_ID", "situsAddress": "SITUS_ADDR" },
              "licenseDisclaimerText": "SYNTHETIC-FIXTURE-DISCLAIMER: no warranty of any kind."
            }
          ]
        }
        """;

    private const string EmptyCountiesRegistryJson = """
        {
          "schemaVersion": 1,
          "counties": []
        }
        """;

    [Fact]
    public void RejectsAnEmptyCountiesArray()
    {
        string path = WriteTempRegistry(EmptyCountiesRegistryJson);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("at least one entry", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsANegativeLayerIndex()
    {
        string json = ValidRegistryJson.Replace("\"layerIndex\": 0,", "\"layerIndex\": -1,");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("layerIndex", error.Message, StringComparison.Ordinal);
            Assert.Contains("must not be negative", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsABlankDisplayName()
    {
        string json = ValidRegistryJson.Replace(
            "\"displayName\": \"Synthetic County (fixture only)\",",
            "\"displayName\": \"\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("displayName is required", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsABlankLicenseDisclaimerText()
    {
        string json = ValidRegistryJson.Replace(
            "\"licenseDisclaimerText\": \"SYNTHETIC-FIXTURE-DISCLAIMER: no warranty of any kind.\"",
            "\"licenseDisclaimerText\": \"\"");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("licenseDisclaimerText is required", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsADuplicateGeoid()
    {
        string path = WriteTempRegistry(DuplicateGeoidRegistryJson);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("http://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer")]
    [InlineData("not-a-url")]
    public void RejectsANonHttpsServiceBaseUrl(string serviceBaseUrl)
    {
        string json = ValidRegistryJson.Replace(
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
            $"\"serviceBaseUrl\": \"{serviceBaseUrl}\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("https", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAServiceBaseUrlEndingWithATrailingSlash()
    {
        string json = ValidRegistryJson.Replace(
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer/\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("trailing slash", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAServiceBaseUrlThatAlreadyIncludesALayerSegment()
    {
        string json = ValidRegistryJson.Replace(
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
            "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer/0\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("layer segment", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsAFieldMapValueThatIsOwnerLike()
    {
        string json = ValidRegistryJson.Replace("\"legalDescription\": \"LEGAL\",", "\"legalDescription\": \"OWNER_NAME\",");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("owner", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("legalDescription", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsTheWrongSchemaVersion()
    {
        string json = ValidRegistryJson.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 2,");
        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("schemaVersion", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReportsEveryProblemInOneExceptionRatherThanShortCircuitingOnTheFirst()
    {
        string json = ValidRegistryJson
            .Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 2,")
            .Replace("\"geoid\": \"99999\",", "\"geoid\": \"bad\",")
            .Replace(
                "\"serviceBaseUrl\": \"https://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",",
                "\"serviceBaseUrl\": \"http://parcels.example-county.invalid/arcgis/rest/services/Parcels/FeatureServer\",");

        string path = WriteTempRegistry(json);
        try
        {
            CountyParcelRegistryFormatException error = Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
            Assert.Contains("schemaVersion", error.Message, StringComparison.Ordinal);
            Assert.Contains("5 ASCII digits", error.Message, StringComparison.Ordinal);
            Assert.Contains("https", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void AssertRejectsWithFormatException(string json)
    {
        string path = WriteTempRegistry(json);
        try
        {
            Assert.Throws<CountyParcelRegistryFormatException>(() => CountyParcelRegistry.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTempRegistry(string json)
    {
        string path = Path.Combine(Path.GetTempPath(), $"solidground-county-registry-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }
}
