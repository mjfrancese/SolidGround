namespace SolidGround.Revit.Settings;

/// <summary>
/// Resolves the editable per-user settings snapshot. The prior machine-wide path remains available only as a
/// read-only legacy import source; settings authored by the add-in never require elevation.
/// </summary>
internal static class RevitSettingsLocator
{
    internal const string FileName = "settings.json";

    /// <summary>Resolves the editable per-user settings file's full path. Does not check whether it exists.</summary>
    internal static string Resolve() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SolidGround",
        "Revit",
        FileName);

    /// <summary>Resolves the read-only pre-Issue-60 import source. Never write to this location.</summary>
    internal static string ResolveLegacyImport() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "SolidGround",
        "Revit",
        FileName);
}
