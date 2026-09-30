using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using SolidGround.Revit.Dialog;

namespace SolidGround.Revit.Tests;

public sealed class SolidGroundDialogActualBindingTests
{
    [Fact]
    public void ActualLocationControlsReactToTheViewModelEntryModeNotification()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialogViewModel viewModel = SolidGroundDialogBindingTests.CreateViewModel();
            SolidGroundDialog dialog = new(viewModel, WpfTestSupport.CreatePalette());
            dialog.Show();
            try
            {
                TextBox latitude = Descendants(dialog).OfType<TextBox>().First(box => AutomationProperties.GetName(box) == "Latitude");
                StackPanel coordinates = Ancestors(latitude).OfType<StackPanel>().Single(panel =>
                    BindingOperations.GetBindingExpression(panel, UIElement.VisibilityProperty)?.ParentBinding.Path?.Path == nameof(SolidGroundDialogViewModel.ShowCoordinates));
                viewModel.EntryMode = LocationEntryMode.Address;
                WpfTestSupport.DrainDataBindingQueue();
                Assert.Equal(Visibility.Collapsed, coordinates.Visibility);
                viewModel.EntryMode = LocationEntryMode.Coordinates;
                WpfTestSupport.DrainDataBindingQueue();
                Assert.Equal(Visibility.Visible, coordinates.Visibility);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void EveryActualPanelAndFooterBindingRootHasNoPathErrors()
    {
        StaTestHost.Run(() =>
        {
            SolidGroundDialog dialog = new(SolidGroundDialogBindingTests.CreateViewModel(), WpfTestSupport.CreatePalette());
            int bindings = 0;
            foreach (FrameworkElement root in dialog.BindingRootsForTesting)
            {
                root.DataContext = dialog.DataContext;
                root.Measure(new Size(760d, 640d));
                root.Arrange(new Rect(0d, 0d, 760d, 640d));
                root.UpdateLayout();
                WpfTestSupport.DrainDataBindingQueue();
                foreach (DependencyObject element in Descendants(root))
                {
                    LocalValueEnumerator values = element.GetLocalValueEnumerator();
                    while (values.MoveNext())
                    {
                        if (values.Current.Value is BindingExpressionBase binding)
                        {
                            bindings++;
                            binding.UpdateTarget();
                            WpfTestSupport.DrainDataBindingQueue();
                            Assert.Equal(BindingStatus.Active, binding.Status);
                            Assert.False(binding.HasError, $"{element.GetType().Name}.{values.Current.Property.Name} has a binding error.");
                        }
                    }
                }
            }

            Assert.True(bindings >= 40, $"Expected the real Location, Parcel, Review, and footer roots to expose bindings; found {bindings}.");
        });
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            foreach (DependencyObject child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
        }
    }

    private static IEnumerable<DependencyObject> Ancestors(DependencyObject child)
    {
        for (DependencyObject? current = VisualTreeHelper.GetParent(child); current is not null; current = VisualTreeHelper.GetParent(current)) yield return current;
    }
}
