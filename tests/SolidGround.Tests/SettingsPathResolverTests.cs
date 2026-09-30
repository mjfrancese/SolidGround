namespace SolidGround.Tests;

public sealed class SettingsPathResolverTests
{
    [Fact]
    public void ResolvesRelativePathAgainstTheSettingsDocumentDirectory()
    {
        string settingsPath = Path.Combine("C:", "operator", "SolidGround", "Revit", "settings.json");

        Type resolver = RequireResolver();
        string resolved = (string)resolver.GetMethod("Resolve")!.Invoke(null, [settingsPath, Path.Combine("rasters", "terrain.asc")])!;

        Assert.Equal(Path.Combine("C:", "operator", "SolidGround", "Revit", "rasters", "terrain.asc"), resolved);
    }

    [Fact]
    public void LeavesAnAbsolutePathAlone()
    {
        string settingsPath = Path.Combine("C:", "operator", "SolidGround", "Revit", "settings.json");
        string absolute = Path.Combine("D:", "terrain", "terrain.asc");

        Type resolver = RequireResolver();
        Assert.Equal(absolute, resolver.GetMethod("Resolve")!.Invoke(null, [settingsPath, absolute]));
    }

    [Fact]
    public void RejectsAPathWithoutASettingsDocumentDirectory()
    {
        Type resolver = RequireResolver();
        Assert.ThrowsAny<Exception>(() => resolver.GetMethod("Resolve")!.Invoke(null, ["settings.json", "terrain.asc"]));
    }

    private static Type RequireResolver() => Type.GetType("SolidGround.Core.Configuration.SettingsPathResolver, SolidGround.Core")
        ?? throw new InvalidOperationException("SettingsPathResolver has not been added.");
}
