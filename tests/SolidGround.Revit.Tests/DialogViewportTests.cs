using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Tests;

public sealed class DialogViewportTests
{
    private static readonly double[] WindowHeights = [480d, 650d, 400d, 580d];
    [Fact]
    public void StyledTabContentRemainsScrollableWhenItsWindowShrinksAndGrows()
    {
        StaTestHost.Run(() =>
        {
            Window window = new() { Width = 640d, Height = 480d };
            DialogControlStyles.Apply(window, WpfTestSupport.CreatePalette());
            ScrollViewer viewport = new()
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Border { Height = 2000d, Background = Brushes.Red },
            };
            TabControl tabs = new();
            tabs.Items.Add(new TabItem { Header = "Long page", Content = viewport });
            window.Content = tabs;
            window.Show();
            try
            {
                foreach (double height in WindowHeights)
                {
                    window.Height = height;
                    window.UpdateLayout();
                    WpfTestSupport.DrainDataBindingQueue();
                    Assert.True(viewport.ViewportHeight > 0d && viewport.ViewportHeight < height,
                        $"Viewport {viewport.ViewportHeight} must fit inside window {height}; actual={viewport.ActualHeight}, desired={viewport.DesiredSize.Height}.");
                    Assert.True(viewport.ScrollableHeight > 0d, $"Long content must scroll; extent={viewport.ExtentHeight}, viewport={viewport.ViewportHeight}.");
                    viewport.ScrollToBottom();
                    window.UpdateLayout();
                    Assert.True(viewport.VerticalOffset > 0d);
                }
            }
            finally { window.Close(); }
        });
    }
}
