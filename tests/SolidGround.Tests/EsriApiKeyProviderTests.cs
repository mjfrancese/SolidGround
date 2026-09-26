using SolidGround.Core.Http;
using SolidGround.Core.Sources.Esri;

namespace SolidGround.Tests;

/// <summary>
/// Groups every test that reads or writes the real <c>ARCGIS_API_KEY</c> process environment variable (this
/// class, and <see cref="EsriGeocoderLiveTests"/>) into one non-parallelized xunit collection, so a test that
/// mutates the process-wide variable can never race with another test that reads it. Mirrors
/// <see cref="OpenTopographyEnvironmentCollectionDefinition"/> exactly.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EsriEnvironmentCollectionDefinition
{
    public const string Name = "Esri environment variable tests";
}

[Collection(EsriEnvironmentCollectionDefinition.Name)]
public sealed class EsriApiKeyProviderTests
{
    private const string VariableName = "ARCGIS_API_KEY";

    [Fact]
    public void EnvironmentProviderResolvesATrimmedKeyFromTheRealProcessEnvironment()
    {
        WithEnvironmentVariable("  my-env-key-value  ", () =>
        {
            var provider = new EnvironmentEsriApiKeyProvider();

            ApiKey? key = provider.GetApiKey();

            Assert.NotNull(key);
            string redacted = SensitiveQueryRedactor.RedactText("token=my-env-key-value;end", SensitiveQueryParameterNames.Esri, key);
            Assert.DoesNotContain("my-env-key-value", redacted, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void EnvironmentProviderReturnsNullWhenTheVariableIsAbsent()
    {
        WithEnvironmentVariable(null, () =>
        {
            var provider = new EnvironmentEsriApiKeyProvider();

            Assert.Null(provider.GetApiKey());
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EnvironmentProviderTreatsBlankAsAbsent(string value)
    {
        WithEnvironmentVariable(value, () =>
        {
            var provider = new EnvironmentEsriApiKeyProvider();

            Assert.Null(provider.GetApiKey());
        });
    }

    [Fact]
    public void StaticProviderReturnsItsConfiguredKeyOrNull()
    {
        var key = new ApiKey("configured-value");
        var configured = new StaticEsriApiKeyProvider(key);
        var unconfigured = new StaticEsriApiKeyProvider(null);

        Assert.Same(key, configured.GetApiKey());
        Assert.Null(unconfigured.GetApiKey());
    }

    private static void WithEnvironmentVariable(string? value, Action test)
    {
        string? original = Environment.GetEnvironmentVariable(VariableName);
        try
        {
            Environment.SetEnvironmentVariable(VariableName, value);
            test();
        }
        finally
        {
            Environment.SetEnvironmentVariable(VariableName, original);
        }
    }
}
