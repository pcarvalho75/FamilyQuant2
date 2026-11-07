using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Setup;

/// <summary>Standalone Setup uses the same rule 7 defaults as the app's Themes/ToolTipDefaults.cs.</summary>
internal static class SetupToolTipDefaults
{
    internal const int InitialDelayMilliseconds = 500;
    internal const int ShowDurationMilliseconds = 20000;
    internal const double MaximumWidth = 380;

    internal static void Apply()
    {
        foreach (var type in new[] { typeof(FrameworkElement), typeof(FrameworkContentElement) })
        {
            SetDefault(ToolTipService.InitialShowDelayProperty, type, InitialDelayMilliseconds);
            SetDefault(ToolTipService.ShowDurationProperty, type, ShowDurationMilliseconds);
            SetDefault(ToolTipService.ShowOnDisabledProperty, type, true);
        }
    }

    private static void SetDefault(DependencyProperty property, Type type, object value)
    {
        // The fake installer fixture may already be hosted by the app with these same defaults.
        if (!Equals(property.GetMetadata(type).DefaultValue, value))
            property.OverrideMetadata(type, new FrameworkPropertyMetadata(value));
    }
}
