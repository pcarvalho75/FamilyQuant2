using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Markup;

namespace FamilyQuant
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static bool displayCultureApplied;
        private CrashHandling? crashHandling;

        public App()
        {
            if (Assembly.GetEntryAssembly() == typeof(App).Assembly)
                crashHandling = new CrashHandling(this, new CrashReportStore(CrashReportStore.DefaultDirectory));
            ApplyDisplayCulture();
            Themes.ToolTipDefaults.Apply();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (Assembly.GetEntryAssembly() == typeof(App).Assembly)
            {
                Themes.ThemeManager.Initialize();
                MainWindow = new MainWindow();
                MainWindow.SetResourceReference(Window.IconProperty, "AppWindowIcon");
                MainWindow.Show();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            crashHandling?.Dispose();
            base.OnExit(e);
        }

        /// <summary>
        /// Makes every binding format numbers in the machine's own language.
        ///
        /// WPF does NOT follow CultureInfo.CurrentCulture for display: FrameworkElement.Language
        /// defaults to en-US no matter what the operating system is set to, so a StringFormat of P3
        /// wrote "12.111%" on a Brazilian machine where every other program writes "12,111%". Nothing
        /// here touches how files are READ or WRITTEN - the .plp is parsed invariantly and the CSV is
        /// written invariantly, both on purpose and both unchanged. This is display only.
        ///
        /// Idempotent because OverrideMetadata throws if it is called twice, and the test harness
        /// constructs more than one Application over the life of a run.
        /// </summary>
        public static void ApplyDisplayCulture()
        {
            if (displayCultureApplied) return;
            displayCultureApplied = true;

            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
        }
    }
}
