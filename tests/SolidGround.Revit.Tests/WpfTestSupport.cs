using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Tests;

internal static class WpfTestSupport
{
    internal static void DrainDataBindingQueue() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    internal static DialogPalette CreatePalette() => new(
        Window: Brushes.White,
        WindowText: Brushes.Black,
        ControlText: Brushes.Black,
        ControlBackground: Brushes.White,
        Highlight: new SolidColorBrush(Color.FromRgb(0x00, 0x5A, 0x9E)),
        Error: Brushes.Firebrick,
        GrayText: Brushes.DimGray,
        ActiveBorder: Brushes.Gray);
}
