using System.Windows;
using System.Windows.Media;
using Autodesk.Revit.UI;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// The small, fixed set of brushes every panel in <see cref="SolidGroundDialog"/> is painted from (SolidGround
/// Issue #31, PH3-4: docs/architecture/revit-interactive-dialog.md "Theming and accessibility").
/// </summary>
internal sealed record DialogPalette(
    Brush Window,
    Brush WindowText,
    Brush ControlText,
    Brush ControlBackground,
    Brush Highlight,
    Brush Error,
    Brush GrayText,
    Brush ActiveBorder)
{
    /// <summary>Raised surfaces, such as hoverable cards and secondary buttons.</summary>
    internal Brush SurfaceRaised { get; init; } = Brushes.Transparent;
    internal Brush OnAccent { get; init; } = Brushes.White;
    internal Brush Selection { get; init; } = Brushes.Transparent;
    internal Brush OnSelection { get; init; } = Brushes.Black;
    internal Brush OnErrorSurface { get; init; } = Brushes.Black;
    internal Brush Focus { get; init; } = Brushes.Blue;
    internal Brush DisabledText { get; init; } = Brushes.Gray;
    internal Brush DisabledSurface { get; init; } = Brushes.LightGray;

    /// <summary>True only when every role is backed by the Windows accessibility palette.</summary>
    internal bool UsesSystemColors { get; init; }
}

/// <summary>
/// Resolves which <see cref="DialogPalette"/> <see cref="SolidGroundDialog"/> paints itself with.
/// <see cref="Resolve"/> is called exactly once, at <see cref="SolidGroundDialog"/>'s constructor -- neither
/// <c>UIThemeManager</c>'s theme-changed event nor <c>SystemParameters.StaticPropertyChanged</c> is ever
/// subscribed (this dialog is modal-only; see docs/architecture/revit-interactive-dialog.md "Non-goals").
/// </summary>
internal static class DialogTheme
{
    private static readonly DialogPalette LightPalette = new(
        Window: Brush(0xF8, 0xF9, 0xFA),
        WindowText: Brush(0x1B, 0x1B, 0x1F),
        ControlText: Brush(0x1B, 0x1B, 0x1F),
        ControlBackground: Brushes.White,
        Highlight: Brush(0x0B, 0x57, 0xD0),
        Error: Brush(0xB3, 0x26, 0x1E),
        GrayText: Brush(0x4A, 0x4E, 0x54),
        ActiveBorder: Brush(0x74, 0x77, 0x75))
    {
        SurfaceRaised = Brush(0xF1, 0xF3, 0xF4),
        OnAccent = Brushes.White,
        Selection = Brush(0xD3, 0xE3, 0xFD),
        OnSelection = Brush(0x10, 0x2A, 0x43),
        OnErrorSurface = Brush(0x5F, 0x11, 0x10),
        Focus = Brush(0x00, 0x5F, 0xCC),
        DisabledText = Brush(0x55, 0x59, 0x5D),
        DisabledSurface = Brush(0xE2, 0xE5, 0xE8),
    };

    private static readonly DialogPalette DarkPalette = new(
        Window: Brush(0x1B, 0x1B, 0x1F),
        WindowText: Brush(0xF4, 0xF0, 0xF4),
        ControlText: Brush(0xF4, 0xF0, 0xF4),
        ControlBackground: Brush(0x24, 0x24, 0x28),
        Highlight: Brush(0xA8, 0xC7, 0xFA),
        Error: Brush(0xF2, 0xB8, 0xB5),
        GrayText: Brush(0xC9, 0xC5, 0xCA),
        ActiveBorder: Brush(0xA9, 0xA4, 0xAA))
    {
        SurfaceRaised = Brush(0x30, 0x30, 0x34),
        OnAccent = Brush(0x00, 0x2B, 0x5C),
        Selection = Brush(0x17, 0x4A, 0x7C),
        OnSelection = Brushes.White,
        OnErrorSurface = Brush(0xFF, 0xDA, 0xD6),
        Focus = Brush(0xA8, 0xC7, 0xFA),
        DisabledText = Brush(0xCB, 0xC7, 0xCC),
        DisabledSurface = Brush(0x38, 0x38, 0x3D),
    };

    /// <summary>
    /// Every brush comes from a <c>SystemColors.*Brush</c> member (review finding, corrected: not
    /// <c>SystemColors.ControlTextBrushKey</c>, whose type is <c>ResourceKey</c>, not <c>Brush</c>) --
    /// accessibility overrides branding, so this palette is used whenever Windows High Contrast is on,
    /// regardless of Revit's own Light/Dark theme.
    /// </summary>
    private static readonly DialogPalette HighContrastPalette = new(
        Window: SystemColors.WindowBrush,
        WindowText: SystemColors.WindowTextBrush,
        ControlText: SystemColors.ControlTextBrush,
        ControlBackground: SystemColors.WindowBrush,
        Highlight: SystemColors.HighlightBrush,
        // WPF's SystemColors has no dedicated "error"/"danger" brush; WindowTextBrush guarantees full
        // OS-configured contrast against this palette's own Window background and keeps a distinct meaning
        // from Highlight, which this dialog already uses for its own "busy"/warning state (review finding,
        // major).
        Error: SystemColors.WindowTextBrush,
        GrayText: SystemColors.GrayTextBrush,
        ActiveBorder: SystemColors.ActiveBorderBrush)
    {
        SurfaceRaised = SystemColors.ControlBrush,
        OnAccent = SystemColors.HighlightTextBrush,
        Selection = SystemColors.HighlightBrush,
        OnSelection = SystemColors.HighlightTextBrush,
        OnErrorSurface = SystemColors.WindowTextBrush,
        Focus = SystemColors.HighlightBrush,
        DisabledText = SystemColors.GrayTextBrush,
        DisabledSurface = SystemColors.ControlBrush,
        UsesSystemColors = true,
    };

    /// <summary>
    /// <paramref name="highContrast"/> wins over <paramref name="revitTheme"/>: Revit's own API carries no
    /// HighContrast concept at all (a full type-name scan of both <c>RevitAPI.dll</c>/<c>RevitAPIUI.dll</c>
    /// found zero <c>*HighContrast*</c> matches), so this is necessarily a second, independent, WPF/OS-level
    /// signal, not an alternative reading of the same one.
    /// </summary>
    internal static DialogPalette Resolve(UITheme revitTheme, bool highContrast)
    {
        if (highContrast)
        {
            return HighContrastPalette;
        }

        return revitTheme == UITheme.Dark ? DarkPalette : LightPalette;
    }

    private static SolidColorBrush Brush(byte red, byte green, byte blue)
    {
        SolidColorBrush brush = new(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
