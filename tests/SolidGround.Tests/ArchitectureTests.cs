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
    public void EveryDeployedAssemblyIsManagedAndNoNativeRuntimesDirectoryExists()
    {
        // Guards the "no native binaries" rule (AGENTS.md's dependency policy): every package this solution
        // references, including NetTopologySuite, must be pure managed code with no native runtime asset.
        string baseDirectory = AppContext.BaseDirectory;
        string[] dllPaths = Directory.GetFiles(baseDirectory, "*.dll", SearchOption.TopDirectoryOnly);
        Assert.NotEmpty(dllPaths);

        foreach (string dllPath in dllPaths)
        {
            // A native (non-managed) DLL throws BadImageFormatException here instead of returning a name.
            AssemblyName name = AssemblyName.GetAssemblyName(dllPath);
            Assert.NotNull(name.Name);
        }

        string runtimesDirectory = Path.Combine(baseDirectory, "runtimes");
        Assert.False(
            Directory.Exists(runtimesDirectory),
            $"A native 'runtimes' directory was found at '{runtimesDirectory}'; SolidGround must not depend on native runtime assets.");
    }

    [Fact]
    public void ProjNetTypesNeverAppearInAnyPublicCoreSignature()
    {
        // Extends the CoreDoesNotReferenceTheRevitApi-style encapsulation pattern above: decision #2 for
        // SolidGround Issue #6 requires that no ProjNET type or null ever escapes SolidGround's own adapter.
        // This checks the assembly-surface level, independent of any specific unit test's own coverage.
        Assembly assembly = typeof(Core.AssemblyMarker).Assembly;

        foreach (Type type in assembly.GetExportedTypes())
        {
            foreach (ConstructorInfo constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    AssertNotProjNetType(parameter.ParameterType, $"{type.FullName}..ctor(...) parameter '{parameter.Name}'");
                }
            }

            const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(MemberFlags))
            {
                AssertNotProjNetType(method.ReturnType, $"{type.FullName}.{method.Name}(...) return type");
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertNotProjNetType(parameter.ParameterType, $"{type.FullName}.{method.Name}(...) parameter '{parameter.Name}'");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(MemberFlags))
            {
                AssertNotProjNetType(property.PropertyType, $"{type.FullName}.{property.Name}");
            }

            foreach (FieldInfo field in type.GetFields(MemberFlags))
            {
                AssertNotProjNetType(field.FieldType, $"{type.FullName}.{field.Name}");
            }

            foreach (EventInfo eventInfo in type.GetEvents(MemberFlags))
            {
                if (eventInfo.EventHandlerType is not null)
                {
                    AssertNotProjNetType(eventInfo.EventHandlerType, $"{type.FullName}.{eventInfo.Name}");
                }
            }
        }
    }

    private static void AssertNotProjNetType(Type type, string location)
    {
        Type effectiveType = type.IsByRef || type.IsPointer ? type.GetElementType()! : type;

        if (effectiveType.IsArray)
        {
            AssertNotProjNetType(effectiveType.GetElementType()!, location);
            return;
        }

        string? ns = effectiveType.Namespace;
        bool isProjNetType = ns is not null && (ns == "ProjNet" || ns.StartsWith("ProjNet.", StringComparison.Ordinal));
        Assert.False(isProjNetType, $"{location} exposes ProjNET type '{effectiveType.FullName}'.");

        if (effectiveType.IsGenericType)
        {
            foreach (Type typeArgument in effectiveType.GetGenericArguments())
            {
                AssertNotProjNetType(typeArgument, location);
            }
        }
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
