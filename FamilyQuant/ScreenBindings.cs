using System.Reflection;
using System.Windows;
using System.Windows.Data;

namespace FamilyQuant;

/// <summary>Resolve declared view bindings immediately for detached/headless controls as well as windows.</summary>
internal static class ScreenBindings
{
    internal static void Attach(object view, object model)
    {
        foreach (var field in view.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (field.GetValue(view) is not FrameworkElement element) continue;
            var values = element.GetLocalValueEnumerator();
            while (values.MoveNext())
            {
                var property = values.Current.Property;
                var binding = BindingOperations.GetBinding(element, property);
                if (binding?.Path == null || binding.Source != null || binding.RelativeSource != null || !string.IsNullOrEmpty(binding.ElementName)) continue;
                BindingOperations.SetBinding(element, property, new Binding(binding.Path.Path)
                {
                    Source = model, Mode = binding.Mode, UpdateSourceTrigger = binding.UpdateSourceTrigger,
                    StringFormat = binding.StringFormat, Converter = binding.Converter,
                    ConverterParameter = binding.ConverterParameter, ConverterCulture = binding.ConverterCulture
                });
            }
        }
    }
}
