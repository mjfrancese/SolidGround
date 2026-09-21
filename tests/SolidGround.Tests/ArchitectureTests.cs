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
    public void CoreExposesAGridTerrainSimplifierImplementingITerrainSimplifier()
    {
        Type? contract = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Simplification.ITerrainSimplifier");
        Type? simplifier = typeof(Core.AssemblyMarker)
            .Assembly
            .GetType("SolidGround.Core.Simplification.GridTerrainSimplifier");

        Assert.NotNull(contract);
        Assert.NotNull(simplifier);
        Assert.True(contract.IsAssignableFrom(simplifier));
    }

    [Fact]
    public void CoreExposesExactlyOnePublicTerrainExporterImplementingItAsFileSystemTerrainExporter()
    {
        Assembly assembly = typeof(Core.AssemblyMarker).Assembly;
        Type contract = assembly.GetType("SolidGround.Core.Exports.ITerrainExporter")!;

        Type[] implementations = [.. assembly.GetExportedTypes()
            .Where(type => type.IsClass && contract.IsAssignableFrom(type))];

        Type implementation = Assert.Single(implementations);
        Assert.Equal("SolidGround.Core.Exports.FileSystemTerrainExporter", implementation.FullName);
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

        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference.Name, "SolidGround.Revit", StringComparison.OrdinalIgnoreCase));
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

    [Fact]
    public void NoPublicStaticStringInCoreContainsACarriageReturn()
    {
        // Regression guard for the source-checkout-dependent line-ending bug (a multi-line raw string literal,
        // such as the WKT1 constant this repository fixed, silently bakes in '\r' when the source file itself
        // is checked out with CRLF line endings): no public static string value anywhere in Core -- field,
        // const, or read-only property -- may ever contain a carriage return, because such a value can flow
        // into a deterministic export and make its bytes depend on how the source was checked out.
        Assembly assembly = typeof(Core.AssemblyMarker).Assembly;
        const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.Static;

        List<string> offendingMembers = [];

        foreach (Type type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            foreach (FieldInfo field in type.GetFields(MemberFlags).OrderBy(field => field.Name, StringComparer.Ordinal))
            {
                if (field.FieldType != typeof(string))
                {
                    continue;
                }

                if (field.GetValue(null) is string value && value.Contains('\r'))
                {
                    offendingMembers.Add($"{type.FullName}.{field.Name} (field)");
                }
            }

            foreach (PropertyInfo property in type.GetProperties(MemberFlags).OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                if (property.PropertyType != typeof(string) || property.GetMethod is null)
                {
                    continue;
                }

                if (property.GetValue(null) is string value && value.Contains('\r'))
                {
                    offendingMembers.Add($"{type.FullName}.{property.Name} (property)");
                }
            }
        }

        Assert.True(
            offendingMembers.Count == 0,
            $"Public static string member(s) contain '\\r': {string.Join(", ", offendingMembers)}");
    }

    [Fact]
    public void CliReferencesNoRevitAssemblyAndNoPackageBeyondCores()
    {
        // See docs/architecture/cli-workflow.md's "Testing strategy" section: SolidGround.Cli adds no new
        // package, so every non-framework assembly it references must be SolidGround.Core itself or one
        // of Core's own existing third-party dependencies -- never a new one, and never a Revit assembly.
        AssemblyName[] references = typeof(SolidGround.Cli.CliApplication).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) == true);

        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference.Name, "SolidGround.Revit", StringComparison.OrdinalIgnoreCase));

        string[] allowedNames = ["SolidGround.Core", "NetTopologySuite", "ProjNET", "System.Private.CoreLib", "System.Runtime", "netstandard"];
        foreach (AssemblyName reference in references)
        {
            string name = reference.Name ?? string.Empty;
            bool isFrameworkAssembly = name.StartsWith("System.", StringComparison.Ordinal) || name.StartsWith("Microsoft.", StringComparison.Ordinal);
            bool isAllowedThirdParty = allowedNames.Contains(name, StringComparer.Ordinal);
            Assert.True(
                isFrameworkAssembly || isAllowedThirdParty,
                $"SolidGround.Cli references '{name}', which is neither a framework assembly nor one of: {string.Join(", ", allowedNames)}.");
        }
    }

    [Fact]
    public void TestAssemblyReferencesNeitherTheRevitApiNorTheRevitHostAssembly()
    {
        // SolidGround.Tests itself must keep building and running on the Linux solidground-pve2 CI
        // runner without Revit installed: it must never pick up a reference to the Revit API or to
        // SolidGround.Revit, whether directly or transitively through a future ProjectReference mistake.
        AssemblyName[] references = typeof(ArchitectureTests).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            references,
            reference => reference.Name?.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) == true);

        Assert.DoesNotContain(
            references,
            reference => string.Equals(reference.Name, "SolidGround.Revit", StringComparison.OrdinalIgnoreCase));
    }
}
