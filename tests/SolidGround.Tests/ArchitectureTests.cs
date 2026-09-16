using System.Reflection;

namespace SolidGround.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void CoreExposesAPluggableElevationSourceContract()
    {
        Type? contract = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Sources.IElevationSource");

        Assert.NotNull(contract);
        Assert.True(contract.IsInterface);
    }

    [Fact]
    public void ElevationSourceContractDefinesAnAcquisitionOperation()
    {
        Type contract = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Sources.IElevationSource")!;

        Assert.NotNull(contract.GetMethod("AcquireAsync"));
    }

    [Fact]
    public void CoreExposesAReversibleHorizontalTransformationContract()
    {
        Type? contract = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Transformations.IHorizontalCoordinateTransform");

        Assert.NotNull(contract);
        Assert.NotNull(contract.GetMethod("Forward"));
        Assert.NotNull(contract.GetMethod("Inverse"));
    }

    [Fact]
    public void CoreExposesAnAaiGridParser()
    {
        Type? parser = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Rasters.AaiGridParser");

        Assert.NotNull(parser);
        Assert.NotNull(parser.GetMethod("Parse"));
    }

    [Fact]
    public void CoreDoesNotReferenceTheRevitApi()
    {
        AssemblyName[] references = typeof(Core.AssemblyMarker)
            .Assembly
            .GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) == true);
    }

    [Fact]
    public void OpenTopographyApiKeyExposesNoPublicMemberThatReturnsTheRawKey()
    {
        Type keyType = typeof(Core.Sources.OpenTopography.OpenTopographyApiKey);

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

        Assert.Equal("[REDACTED]", new Core.Sources.OpenTopography.OpenTopographyApiKey("super-secret-value").ToString());
    }
}
