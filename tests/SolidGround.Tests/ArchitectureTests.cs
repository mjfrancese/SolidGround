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
}
