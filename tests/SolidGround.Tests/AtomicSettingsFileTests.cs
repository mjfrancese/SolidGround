using SolidGround.Core.Configuration;

namespace SolidGround.Tests;

public sealed class AtomicSettingsFileTests
{
    [Fact]
    public void SaveRejectsAChangedTargetAndPreservesBothWritersBytes()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"solidground-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(path, "first");
            SettingsFileSnapshot draft = AtomicSettingsFile.Read(path);
            File.WriteAllText(path, "other-writer");

            Assert.Throws<SettingsFileConflictException>(() => AtomicSettingsFile.Save(path, draft.Version, "mine"u8.ToArray()));

            Assert.Equal("other-writer", File.ReadAllText(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void SaveFromMissingPublishesCompleteReplacementAndReturnsNewVersion()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"solidground-settings-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "settings.json");
        try
        {
            SettingsFileVersion saved = AtomicSettingsFile.Save(path, SettingsFileVersion.Missing, "complete"u8.ToArray());

            Assert.Equal("complete", File.ReadAllText(path));
            Assert.Equal(saved, AtomicSettingsFile.Read(path).Version);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
