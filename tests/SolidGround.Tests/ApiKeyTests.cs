using System.Reflection;
using SolidGround.Core.Http;

namespace SolidGround.Tests;

/// <summary>
/// SolidGround Issue #27 (PH3-0): <see cref="ApiKey"/> is the provider-agnostic generalization of
/// <c>SolidGround.Core.Sources.OpenTopography.OpenTopographyApiKey</c> (left untouched and independent; see
/// <c>docs/architecture/shared-http-redaction-and-key-resolution.md</c>). These tests mirror
/// <c>OpenTopographyConfigurationTests.OpenTopographyApiKeyTests</c> and
/// <c>ArchitectureTests.OpenTopographyApiKeyExposesNoPublicMemberThatReturnsTheRawKey</c> exactly, applied to
/// this new type.
/// </summary>
public sealed class ApiKeyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ConstructorRejectsNullOrBlankValues(string? value)
    {
        Assert.Throws<ArgumentException>(() => new ApiKey(value!));
    }

    [Fact]
    public void ToStringNeverReturnsTheRawValue()
    {
        var key = new ApiKey("super-secret-value");

        Assert.Equal("[REDACTED]", key.ToString());
    }

    [Fact]
    public void ExposesNoPublicMemberThatReturnsTheRawKey()
    {
        Type keyType = typeof(ApiKey);

        foreach (PropertyInfo property in keyType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.NotEqual(typeof(string), property.PropertyType);
        }

        foreach (MethodInfo method in keyType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.Name == nameof(ToString))
            {
                continue;
            }

            Assert.NotEqual(typeof(string), method.ReturnType);
        }

        Assert.Equal("[REDACTED]", new ApiKey("super-secret-value").ToString());
    }
}
