using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Themes
{
    /// <summary>Design rule 7: one tooltip behaviour everywhere, so a window only writes ToolTip="...". Timing and
    /// display on disabled controls are set here once for every element; width, wrapping and theme colours come from
    /// the implicit ToolTip style in Controls.xaml. See docs/TOOLTIPS.md.</summary>
    public static class ToolTipDefaults
    {
        public const int InitialDelayMilliseconds = 500;
        public const int ShowDurationMilliseconds = 20000;
        public const double MaximumWidth = 380;
        private static bool applied;

        /// <summary>Called by App before any window exists. Elements and styles may still set their own values.</summary>
        public static void Apply()
        {
            if (applied) return;
            applied = true;
            foreach (var type in new[] { typeof(FrameworkElement), typeof(FrameworkContentElement) })
            {
                ToolTipService.InitialShowDelayProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(InitialDelayMilliseconds));
                ToolTipService.ShowDurationProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(ShowDurationMilliseconds));
                ToolTipService.ShowOnDisabledProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(true));
            }
        }
    }
}
