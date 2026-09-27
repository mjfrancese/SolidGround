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
    Brush ActiveBorder);

/// <summary>
/// Resolves which <see cref="DialogPalette"/> <see cref="SolidGroundDialog"/> paints itself with.
/// <see cref="Resolve"/> is called exactly once, at <see cref="SolidGroundDialog"/>'s constructor -- neither
/// <c>UIThemeManager</c>'s theme-changed event nor <c>SystemParameters.StaticPropertyChanged</c> is ever
/// subscribed (this dialog is modal-only; see docs/architecture/revit-interactive-dialog.md "Non-goals").
/// </summary>
internal static class DialogTheme
{
    private static readonly DialogPalette LightPalette = new(
        Window: new SolidColorBrush(Color.FromRgb(0xF3, 0xF3, 0xF3)),
        WindowText: Brushes.Black,
        ControlText: Brushes.Black,
        ControlBackground: Brushes.White,
        // #005A9E reads at roughly 6.4:1 against this palette's near-white Window/ControlBackground, clearing
        // the WCAG AA 4.5:1 text threshold (review finding, major); the original #0078D4 measured only
        // roughly 4.08:1 here and was never checked against that bar.
        Highlight: new SolidColorBrush(Color.FromRgb(0x00, 0x5A, 0x9E)),
        // Firebrick reads at roughly 6:1 against this palette's near-white Window/ControlBackground, well
        // clear of the WCAG AA 4.5:1 text threshold (review finding, major).
        Error: Brushes.Firebrick,
        GrayText: new SolidColorBrush(Color.FromRgb(0x60, 0x60, 0x60)),
        ActiveBorder: new SolidColorBrush(Color.FromRgb(0xB0, 0xB0, 0xB0)));

    private static readonly DialogPalette DarkPalette = new(
        Window: new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
        WindowText: Brushes.White,
        ControlText: Brushes.White,
        // A distinct, slightly darker-than-Window shade so a themed TextBox/ComboBox/ListBox surface remains
        // visually separable from the surrounding Window background (review finding, blocker: Control.Background
        // is not an inherited WPF dependency property, unlike Foreground, so it never picked up Window's own
        // dark Background and every control rendered white-on-default-light before this fix).
        ControlBackground: new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
        Highlight: new SolidColorBrush(Color.FromRgb(0x3B, 0x9C, 0xF2)),
        // Firebrick's contrast against this palette's #2D2D30 Window is only ~2:1 (fails WCAG AA); this lighter
        // red reads at roughly 4.9:1 instead (review finding, major).
        Error: new SolidColorBrush(Color.FromRgb(0xFF, 0x6B, 0x6B)),
        GrayText: new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xA0)),
        ActiveBorder: new SolidColorBrush(Color.FromRgb(0x5A, 0x5A, 0x5A)));

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
        ActiveBorder: SystemColors.ActiveBorderBrush);

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
}
