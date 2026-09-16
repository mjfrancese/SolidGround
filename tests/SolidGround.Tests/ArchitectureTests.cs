using System.Reflection;

namespace SolidGround.Tests;

public sealed class ArchitectureTests
{
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
