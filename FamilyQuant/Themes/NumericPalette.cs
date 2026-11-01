using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace FamilyQuant.Themes
{
    /// <summary>Colours the existing numeric editor parts without replacing its input or spin logic.</summary>
    public static class NumericPalette
    {
        public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
            "Enabled", typeof(bool), typeof(NumericPalette), new PropertyMetadata(false, Changed));
        public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
        public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);

        private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
        {
            if (target is not FrameworkElement element) return;
            element.Loaded -= Loaded;
            if ((bool)e.NewValue)
            {
                element.Loaded += Loaded;
                if (element.IsLoaded) Apply(element);
            }
        }

        private static void Loaded(object sender, RoutedEventArgs e) => Apply((DependencyObject)sender);

        private static void Apply(DependencyObject node)
        {
            if (node is Control control)
            {
                control.SetResourceReference(Control.BackgroundProperty, "InputBrush");
                control.SetResourceReference(Control.ForegroundProperty, "TextBrush");
                control.SetResourceReference(Control.BorderBrushProperty, "EdgeBrush");
            }
            if (node is Border border)
            {
                border.SetResourceReference(Border.BackgroundProperty, "InputBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush");
            }
            if (node is Shape shape) shape.SetResourceReference(Shape.FillProperty, "TextBrush");
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                Apply(VisualTreeHelper.GetChild(node, i));
        }
    }
}
