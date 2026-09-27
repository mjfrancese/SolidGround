using System.Windows;
using System.Windows.Controls;

namespace SolidGround.Revit.Dialog;

/// <summary>
/// Shell for the interactive Revit-host dialog (SolidGround Issue #31, PH3-4:
/// docs/architecture/revit-interactive-dialog.md "Purpose and boundary"): a code-behind-only WPF
/// <see cref="Window"/>, zero <c>.xaml</c>/BAML by design. An isolated Revit add-in context has no
/// <c>App.xaml</c>, and WPF's own XAML parser can double-load a BAML-carrying assembly across two
/// contexts (<c>Nice3point/RevitToolkit#7</c>, the still-open <c>dotnet/wpf#1700</c>); building this window
/// entirely in code sidesteps that failure mode outright rather than working around it.
/// </summary>
/// <remarks>
/// This stage ships only a title bar and a Cancel button, proving this project's own
/// <c>UseWPF</c>/<c>CommunityToolkit.Mvvm</c> wiring compiles and that a <see cref="Window"/> bound to
/// <see cref="SolidGroundDialogViewModel"/> can be constructed. The full ten-section content model, real
/// control bindings, theming (docs/architecture/revit-interactive-dialog.md "Theming and accessibility"),
/// and <c>AutomationProperties</c> coverage are a later stage's own scope, not this one. This type is not
/// constructed anywhere in this stage: it is not shown from <c>CreateToposolidCommand</c>, the ribbon, or
/// any other command, so it is unreachable in a running add-in.
/// </remarks>
internal sealed class SolidGroundDialog : Window
{
    internal SolidGroundDialog()
    {
        Title = "SolidGround";
        SizeToContent = SizeToContent.WidthAndHeight;
        DataContext = new SolidGroundDialogViewModel();

        Button cancelButton = new() { Content = "Cancel", Margin = new Thickness(12) };
        cancelButton.Click += (_, _) => Close();

        Content = cancelButton;
    }
}
