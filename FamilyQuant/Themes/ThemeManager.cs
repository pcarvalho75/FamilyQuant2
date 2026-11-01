using System;
using System.Collections;
using System.IO;
using System.Security;
using System.Windows;
using Microsoft.Win32;

namespace FamilyQuant.Themes
{
    public enum AppTheme { Light, Dark, System }

    /// <summary>Applies one token set and the corresponding built-in .NET Fluent theme.</summary>
    public static class ThemeManager
    {
        public static AppTheme Current { get; private set; } = AppTheme.Dark;
        public static AppTheme Effective { get; private set; } = AppTheme.Dark;
        public static event EventHandler? ThemeChanged;
        private static bool initialized;

        /// <summary>Only real app startup enables profile writes; render and test hosts never call this.</summary>
        public static void Initialize()
        {
            if (initialized) return;
            initialized = true;
            Apply(ThemeSettings.Load(ThemeSettings.DefaultPath), false);
            SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
            Application.Current.Exit += (_, _) => SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        }

        public static AppTheme Resolve(AppTheme selection, bool systemUsesLight) =>
            selection == AppTheme.System ? (systemUsesLight ? AppTheme.Light : AppTheme.Dark) : selection;

        public static void Apply(AppTheme theme, bool savePreference = false)
        {
            Application app = Application.Current ?? throw new InvalidOperationException("An application is required.");
            app.Dispatcher.VerifyAccess();
            if (!Enum.IsDefined(theme)) throw new ArgumentOutOfRangeException(nameof(theme));
            AppTheme effective = Resolve(theme, SystemUsesLight());
            var tokens = new ResourceDictionary
            {
                Source = new Uri("/" + typeof(ThemeManager).Assembly.GetName().Name
                    + ";component/Themes/Tokens.xaml", UriKind.Relative)
            };
            ResourceDictionary selected = effective == AppTheme.Light ? (ResourceDictionary)tokens["LightTokens"] : tokens;
            foreach (DictionaryEntry entry in selected)
                if (entry.Value is not ResourceDictionary) app.Resources[entry.Key] = entry.Value;
#pragma warning disable WPF0001 // ThemeMode is experimental in .NET 10; keep the suppression local.
            app.ThemeMode = theme == AppTheme.System ? ThemeMode.System
                : theme == AppTheme.Dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Current = theme;
            Effective = effective;
            if (savePreference && initialized) ThemeSettings.Save(ThemeSettings.DefaultPath, theme);
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        private static bool SystemUsesLight()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { return true; }
        }

        private static void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
        {
            Application? app = Application.Current;
            if (app == null || app.Dispatcher.HasShutdownStarted) return;
            app.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Current == AppTheme.System) Apply(AppTheme.System);
            }));
        }
    }
}
