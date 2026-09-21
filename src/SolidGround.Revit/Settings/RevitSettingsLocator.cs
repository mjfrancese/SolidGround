namespace SolidGround.Revit.Settings;

/// <summary>
/// Resolves the one machine-wide settings file path: <c>%ProgramData%\SolidGround\Revit\settings.json</c>,
/// built from <see cref="Environment.SpecialFolder.CommonApplicationData"/>, matching <c>AddInLog</c>'s own
/// resolution pattern (AGENTS.md "Revit add-in conventions" section 5). Never hardcodes the path.
/// </summary>
internal static class RevitSettingsLocator
{
    internal const string FileName = "settings.json";

    /// <summary>Resolves the settings file's full path. Does not check whether it exists.</summary>
    internal static string Resolve() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SolidGround",
        "Revit",
        FileName);
}
