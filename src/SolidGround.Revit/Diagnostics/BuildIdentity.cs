using System.Reflection;
using System.Security.Cryptography;

namespace SolidGround.Revit.Diagnostics;

/// <summary>
/// Best-effort identity of the loaded SolidGround.Revit assembly: informational version, MVID, and a
/// SHA-256 of the assembly file on disk. Mirrors an established pattern from a related internal project's
/// own build-identity capture: every field is captured independently so one unavailable value cannot suppress the others, and capture never
/// throws. Owner decision 7 (2026-09-20, docs/architecture/revit-add-in-conventions.md) adds these three
/// fields to the eventual Extensible Storage provenance entity (Issue #16); this milestone only logs them
/// at startup (AGENTS.md "Revit add-in conventions" section 6, decisions D6/D8).
/// </summary>
internal sealed record BuildIdentity(string InformationalVersion, string ModuleVersionId, string Sha256, string AssemblyPath)
{
    private const string Unavailable = "<unavailable>";

    private static readonly Lazy<BuildIdentity> LazyCurrent = new(Capture);

    /// <summary>The current process's SolidGround.Revit build identity. Computed once and cached: hashing the assembly file is not free.</summary>
    internal static BuildIdentity Current => LazyCurrent.Value;

    /// <summary>A single log-friendly line summarizing this build's identity.</summary>
    internal string ToLogLine() =>
        $"SolidGround.Revit build identity: InformationalVersion={InformationalVersion}, MVID={ModuleVersionId}, SHA-256={Sha256}, Path={AssemblyPath}";

    private static BuildIdentity Capture()
    {
        Assembly assembly = typeof(BuildIdentity).Assembly;
        string? rawPath = TryGet(() => assembly.Location);

        return new BuildIdentity(
            TryGet(() => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) ?? Unavailable,
            TryGet(() => assembly.ManifestModule.ModuleVersionId.ToString("D")) ?? Unavailable,
            TryGet(() => string.IsNullOrEmpty(rawPath) ? null : Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(rawPath)))) ?? Unavailable,
            string.IsNullOrEmpty(rawPath) ? Unavailable : rawPath);
    }

    private static string? TryGet(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
