using System.Security.Cryptography;
using System.Text;

namespace SolidGround.Core.Configuration;

/// <summary>An immutable comparison token for the exact bytes currently stored at a settings path.</summary>
public sealed record SettingsFileVersion
{
    private SettingsFileVersion(string value) => Value = value;

    /// <summary>A sentinel distinct from every SHA-256 digest.</summary>
    public static SettingsFileVersion Missing { get; } = new("missing");

    public string Value { get; }

    public static SettingsFileVersion FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        return new SettingsFileVersion(Convert.ToHexString(SHA256.HashData(bytes)));
    }
}

/// <summary>Bytes and their optimistic-concurrency token captured as one settings draft is loaded.</summary>
public sealed record SettingsFileSnapshot(byte[]? Bytes, SettingsFileVersion Version);

/// <summary>
/// Digest-bound atomic persistence for settings. It deliberately stores bytes only; domain-specific strict
/// decoding happens before this class is called, so Save never decodes a competing writer's replacement.
/// </summary>
public static class AtomicSettingsFile
{
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(5);

    public static SettingsFileSnapshot Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return new SettingsFileSnapshot(null, SettingsFileVersion.Missing);
        }

        byte[] bytes = File.ReadAllBytes(path);
        return new SettingsFileSnapshot(bytes, SettingsFileVersion.FromBytes(bytes));
    }

    /// <exception cref="SettingsFileConflictException">The on-disk bytes changed since the draft was loaded.</exception>
    public static SettingsFileVersion Save(string path, SettingsFileVersion expectedVersion, byte[] replacementBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(expectedVersion);
        ArgumentNullException.ThrowIfNull(replacementBytes);

        using Mutex mutex = new(initiallyOwned: false, BuildMutexName(path));
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(MutexTimeout); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired)
            {
                throw new IOException("SolidGround could not acquire the settings lock within 5 seconds.");
            }

            EnsureExpected(path, expectedVersion);
            string directory = Path.GetDirectoryName(path) ?? throw new ArgumentException("The settings path must include a directory.", nameof(path));
            Directory.CreateDirectory(directory);
            string stagedPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(stagedPath, replacementBytes);
                EnsureExpected(path, expectedVersion);
                File.Move(stagedPath, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(stagedPath))
                {
                    File.Delete(stagedPath);
                }
            }

            return SettingsFileVersion.FromBytes(replacementBytes);
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static void EnsureExpected(string path, SettingsFileVersion expected)
    {
        SettingsFileVersion observed = Read(path).Version;
        if (!string.Equals(observed.Value, expected.Value, StringComparison.Ordinal))
        {
            throw new SettingsFileConflictException("Saved settings changed after this draft was loaded. Reload saved settings or keep this draft to reapply it.");
        }
    }

    private static string BuildMutexName(string path) => "Global\\SolidGround.Revit.Settings." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path))));
}

public sealed class SettingsFileConflictException(string message) : IOException(message);
