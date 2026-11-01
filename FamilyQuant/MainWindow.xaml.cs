using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using FamilyQuant.DeNovo;

namespace FamilyQuant
{
    public partial class MainWindow : Window
    {
        // Legacy private names remain available to the screen harness; operations use the same instances.
        ClusterGenerationControl? cgc;
        USearchLib? usearch;
        HomeControl? home;
        DeNovoControl? dnc;
        AnalyticsControl? analytics;
        string? lastFcfPath;
        FamilyClusterFile? loadedCuration;
        List<QuantRow> loadedQuant = new List<QuantRow>();
        QuantManifest? loadedManifest;
        string? lastSpectrumComparisonCsvPath;
        private bool saveInProgress;
        private bool analyticsInProgress;
        private bool spectrumViewInProgress;
        private readonly ShellViewPresenter viewPresenter;
        private readonly ShellDocumentCommands documentCommands;
        private readonly ShellHelpPresenter helpPresenter;

        public MainWindow()
        {
            InitializeComponent();
            viewPresenter = new ShellViewPresenter(this);
            documentCommands = new ShellDocumentCommands(this);
            helpPresenter = new ShellHelpPresenter(this);
            InitializeShellCommands();
            InitializeNavigation();
            TextBlockGuide.DataContext = StatusModel;
            UpdateThemeMenu();
            Title += " " + AppVersion();
            InitializeDocumentLifecycle();
            ShowHome();
        }

        private void MenuThemeLight_Click(object sender, RoutedEventArgs e) => ApplyTheme(Themes.AppTheme.Light);
        private void MenuThemeDark_Click(object sender, RoutedEventArgs e) => ApplyTheme(Themes.AppTheme.Dark);
        private void MenuThemeSystem_Click(object sender, RoutedEventArgs e) => ApplyTheme(Themes.AppTheme.System);
        private void ApplyTheme(Themes.AppTheme theme)
        {
            Themes.ThemeManager.Apply(theme, savePreference: true);
            UpdateThemeMenu();
        }
        private void UpdateThemeMenu()
        {
            MenuThemeLight.IsChecked = Themes.ThemeManager.Current == Themes.AppTheme.Light;
            MenuThemeDark.IsChecked = Themes.ThemeManager.Current == Themes.AppTheme.Dark;
            MenuThemeSystem.IsChecked = Themes.ThemeManager.Current == Themes.AppTheme.System;
        }
        private static string AppVersion() => AppVersionInfo.Current();

        private void PresentHome() => viewPresenter.PresentHome();
        private void MenuItemHome_Click(object sender, RoutedEventArgs e) => viewPresenter.MenuItemHome_Click(sender, e);
        private void PresentCurationOrOpen() => viewPresenter.PresentCurationOrOpen();
        private void PresentBuild() => viewPresenter.PresentBuild();
        private void PresentDeNovo() => viewPresenter.PresentDeNovo();
        private void PresentResults() => viewPresenter.PresentResults();
        private void PresentCompute() => viewPresenter.PresentCompute();
        private void MenuItemCluster_Click(object sender, RoutedEventArgs e) => documentCommands.MenuItemCluster_Click(sender, e);
        public bool OpenProjectPath(string path) => documentCommands.OpenProjectPath(path);
        private void MenuItemSaveFCF_Click(object sender, RoutedEventArgs e) => documentCommands.MenuItemSaveFCF_Click(sender, e);
        private void MenuItemSaveProject_Click(object sender, RoutedEventArgs e) => documentCommands.MenuItemSaveProject_Click(sender, e);
        private Task SaveProjectAsync(bool saveAs) => documentCommands.SaveProjectAsync(saveAs);
        private Task<(bool Completed, string? Note)> RefreshSpectrumFamiliesAsync() => documentCommands.RefreshSpectrumFamiliesAsync();
        private void MenuItemSpectrumComparison_Click(object sender, RoutedEventArgs e) => documentCommands.MenuItemSpectrumComparison_Click(sender, e);
        private void MenuItemReanotateSEPro_Click(object sender, RoutedEventArgs e) => documentCommands.MenuItemReanotateSEPro_Click(sender, e);
        private string CurrentContextHelp() => helpPresenter.CurrentContextHelp();
        private string ProjectStatusText() => helpPresenter.ProjectStatusText();
        private void MenuItemHowToRead_Click(object sender, RoutedEventArgs e) => helpPresenter.MenuItemHowToRead_Click(sender, e);
        private void MenuItemWhereAmI_Click(object sender, RoutedEventArgs e) => helpPresenter.MenuItemWhereAmI_Click(sender, e);
        private string WorkflowText() => helpPresenter.WorkflowText();
    }
}
