using SolidGround.Core.Sources.OpenTopography;

namespace SolidGround.Tests;

/// <summary>
/// Groups every test that reads or writes the real <c>OPENTOPOGRAPHY_API_KEY</c> process environment
/// variable (this class, and <see cref="OpenTopographyLiveTests"/>) into one non-parallelized xunit
/// collection, so a test that mutates the process-wide variable can never race with another test that
/// reads it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class OpenTopographyEnvironmentCollectionDefinition
{
    public const string Name = "OpenTopography environment variable tests";
}

public sealed class OpenTopographyApiKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsNullOrBlankValues(string? value)
    {
        Assert.Throws<ArgumentException>(() => new OpenTopographyApiKey(value!));
    }

    [Fact]
    public void ToStringNeverReturnsTheRawValue()
    {
        var key = new OpenTopographyApiKey("super-secret-value");

        Assert.Equal("[REDACTED]", key.ToString());
    }

    [Fact]
    public void StaticProviderReturnsItsConfiguredKeyOrNull()
    {
        var key = new OpenTopographyApiKey("configured-value");
        var configured = new StaticOpenTopographyApiKeyProvider(key);
        var unconfigured = new StaticOpenTopographyApiKeyProvider(null);

        Assert.Same(key, configured.GetApiKey());
        Assert.Null(unconfigured.GetApiKey());
    }
}

[Collection(OpenTopographyEnvironmentCollectionDefinition.Name)]
public sealed class OpenTopographyEnvironmentApiKeyProviderTests
{
    private const string VariableName = "OPENTOPOGRAPHY_API_KEY";

    [Fact]
    public void ReadsAndTrimsTheConfiguredEnvironmentVariable()
    {
        WithEnvironmentVariable("  my-env-key-value  ", () =>
        {
            var provider = new EnvironmentOpenTopographyApiKeyProvider();

            OpenTopographyApiKey? key = provider.GetApiKey();

            Assert.NotNull(key);
            string redacted = OpenTopographyRedaction.RedactText("token=my-env-key-value;end", key);
            Assert.DoesNotContain("my-env-key-value", redacted, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TreatsAnEmptyOrWhitespaceVariableAsNoKey(string value)
    {
        WithEnvironmentVariable(value, () =>
        {
            var provider = new EnvironmentOpenTopographyApiKeyProvider();

            Assert.Null(provider.GetApiKey());
        });
    }

    [Fact]
    public void TreatsAnUnsetVariableAsNoKey()
    {
        WithEnvironmentVariable(null, () =>
        {
            var provider = new EnvironmentOpenTopographyApiKeyProvider();

            Assert.Null(provider.GetApiKey());
        });
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

public sealed class OpenTopographyUsgs1mSourceOptionsTests
{
    [Fact]
    public void DefaultsMatchOpenTopographysDocumentedContract()
    {
        var options = new OpenTopographyUsgs1mSourceOptions();

        Assert.Equal(new Uri("https://portal.opentopography.org/API/usgsdem"), options.EndpointUri);
        Assert.Equal(OpenTopographyUsgs1mSourceOptions.DefaultEndpointUri, options.EndpointUri);
        Assert.Equal(250d, options.MaximumAreaSquareKilometers);
        Assert.Equal(256L * 1024 * 1024, options.MaximumResponseBytes);
    }

    [Fact]
    public void ConstructionRejectsANullEndpoint()
    {
        Assert.Throws<ArgumentNullException>(() => new OpenTopographyUsgs1mSourceOptions { EndpointUri = null! });
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ConstructionRejectsANonPositiveOrNonFiniteMaximumArea(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenTopographyUsgs1mSourceOptions { MaximumAreaSquareKilometers = value });
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void ConstructionRejectsANonPositiveMaximumResponseSize(long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new OpenTopographyUsgs1mSourceOptions { MaximumResponseBytes = value });
    }

    [Fact]
    public void WithExpressionsValidateJustLikeConstruction()
    {
        var options = new OpenTopographyUsgs1mSourceOptions();

        Assert.Throws<ArgumentOutOfRangeException>(() => options with { MaximumAreaSquareKilometers = -1d });
        Assert.Throws<ArgumentOutOfRangeException>(() => options with { MaximumResponseBytes = 0L });
        Assert.Throws<ArgumentNullException>(() => options with { EndpointUri = null! });
    }
}
