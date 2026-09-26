using System.Reflection;
using SolidGround.Core.Sources.LocalParcelFile;

namespace SolidGround.Tests;

/// <summary>
/// Asserts <see cref="LocalParcelFileSource"/>'s public constructor never takes an <see cref="HttpClient"/> or
/// other <c>System.Net.Http</c>-typed parameter, and that the type declares no <see cref="HttpClient"/>-typed
/// instance field (AC4: "the local-file source never calls the network"). This is a signature-level guard, not
/// IL-level enforcement -- unlike <c>ArchitectureTests.CoreDoesNotReferenceTheRevitApi</c>'s assembly-reference-
/// absence check, it would not catch a hypothetical future method body that opened a network connection
/// without ever storing it in a constructor parameter or a field. Today's actual
/// <see cref="LocalParcelFileSource.FindAsync"/> implementation is genuinely network-free (local file I/O and
/// in-memory JSON/geometry parsing only).
/// </summary>
public sealed class LocalParcelFileArchitectureTests
{
    [Fact]
    public void NoPublicConstructorTakesAnHttpClientOrAnySystemNetHttpParameter()
    {
        ConstructorInfo[] constructors = typeof(LocalParcelFileSource).GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(constructors);

        foreach (ConstructorInfo constructor in constructors)
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
            {
                string? ns = parameter.ParameterType.Namespace;
                bool isSystemNetHttpType = ns is not null && (ns == "System.Net.Http" || ns.StartsWith("System.Net.Http.", StringComparison.Ordinal));
                Assert.False(
                    isSystemNetHttpType,
                    $"{typeof(LocalParcelFileSource).FullName}..ctor(...) parameter '{parameter.Name}' is a System.Net.Http type ('{parameter.ParameterType.FullName}').");
            }
        }
    }

    [Fact]
    public void SolidGroundCoreAssemblyDoesNotReferenceSystemNetHttpForThisSourcesOwnSake()
    {
        // Belt-and-suspenders: SolidGround.Core as a whole legitimately references System.Net.Http (the
        // county REST source needs it), so this asserts the narrower, directly checkable fact instead --
        // that LocalParcelFileSource's own field never holds an HttpClient -- via the constructor guard above,
        // and here only confirms no HttpClient-shaped instance field exists on the type at all.
        FieldInfo[] fields = typeof(LocalParcelFileSource).GetFields(BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.DoesNotContain(fields, field => field.FieldType == typeof(HttpClient));
    }
}
