using System;
using System.Collections;
using System.Windows;

namespace FamilyQuant.Themes
{
    public enum AppTheme { Light, Dark }

    /// <summary>Applies one token set and the corresponding built-in .NET Fluent theme.</summary>
    public static class ThemeManager
    {
        public static AppTheme Current { get; private set; } = AppTheme.Light;

        public static void Apply(AppTheme theme)
        {
            Application app = Application.Current ?? throw new InvalidOperationException("An application is required.");
            app.Dispatcher.VerifyAccess();
            var tokens = new ResourceDictionary
            {
                Source = new Uri("/" + typeof(ThemeManager).Assembly.GetName().Name
                    + ";component/Themes/Tokens.xaml", UriKind.Relative)
            };
            ResourceDictionary selected = theme == AppTheme.Dark ? (ResourceDictionary)tokens["DarkTokens"] : tokens;
            foreach (DictionaryEntry entry in selected)
                if (entry.Value is not ResourceDictionary) app.Resources[entry.Key] = entry.Value;
#pragma warning disable WPF0001 // ThemeMode is experimental in .NET 10; keep the suppression local.
            app.ThemeMode = theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Current = theme;
        }
    }
}
