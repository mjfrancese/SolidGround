using System.Reflection;
namespace SolidGround.Revit.Tests;

public sealed class FriendAssemblyContractTests
{
    [Fact]
    public void RevitHostGrantsTheLocalWpfRuntimeTestAssemblyInternalAccess()
    {
        Assembly hostAssembly = Assembly.LoadFrom(Path.Combine(AppContext.BaseDirectory, "SolidGround.Revit.dll"));
        string[] friendAssemblies = hostAssembly
            .GetCustomAttributesData()
            .Where(attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.InternalsVisibleToAttribute")
            .Select(attribute => (string)attribute.ConstructorArguments[0].Value!)
            .ToArray();

        Assert.Contains("SolidGround.Revit.Tests", friendAssemblies);
    }
}
