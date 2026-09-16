namespace SolidGround.Tests;

public sealed class FixtureSecurityTests
{
    [Fact]
    public void CommittedFixturesContainNoCredentialsOrRequestUrls()
    {
        string fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        string[] forbiddenMarkers =
        [
            "authorization:",
            "bearer ",
            "api_key",
            "apikey",
            "http://",
            "https://",
        ];

        foreach (string path in Directory.EnumerateFiles(fixtureDirectory, "*", SearchOption.AllDirectories))
        {
            string content = File.ReadAllText(path);
            foreach (string marker in forbiddenMarkers)
            {
                Assert.DoesNotContain(marker, content, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
