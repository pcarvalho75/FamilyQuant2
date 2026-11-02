using System;
using System.Windows;
using System.Windows.Input;

namespace FamilyQuant;

public partial class MainWindow
{
    public ShellViewModel NavigationModel { get; } = new();
    public static readonly RoutedUICommand NavigateShellCommand = new("Navigate", "NavigateShell", typeof(MainWindow));
    public static Func<ComputeXicWindow, bool?>? ComputeDialogOverride { get; set; }

    private void InitializeNavigation()
    {
        NavigationRail.SetModel(NavigationModel);
        NavigationModel.NavigationRequested += async destination =>
        {
            switch (destination)
            {
                case ShellDestination.Start: PresentHome(); break;
                case ShellDestination.Families: PresentCurationOrOpen(); break;
                case ShellDestination.Build:
                    if (await ConfirmCanReplaceAsync()) PresentBuild();
                    break;
                case ShellDestination.Quantify: PresentCompute(); break;
                case ShellDestination.Results: PresentResults(); break;
                case ShellDestination.DeNovo: PresentDeNovo(); break;
            }
        };
        CommandBindings.Add(new CommandBinding(NavigateShellCommand,
            (_, e) => { if (e.Parameter is ShellDestination destination) NavigationModel.RequestNavigate(destination); e.Handled = true; },
            (_, e) => { e.CanExecute = e.Parameter is ShellDestination destination && NavigationModel.CanNavigate(destination); e.Handled = true; }));
        foreach (var shortcut in new[]
        {
            (Key.D0, ShellDestination.Start), (Key.D1, ShellDestination.Families),
            (Key.D2, ShellDestination.Quantify), (Key.D3, ShellDestination.Results), (Key.D4, ShellDestination.DeNovo)
        }) InputBindings.Add(new KeyBinding(NavigateShellCommand, shortcut.Item1, ModifierKeys.Alt)
            { CommandParameter = shortcut.Item2, CommandTarget = this });
    }

    private void ShowHome() => NavigationModel.RequestNavigate(ShellDestination.Start);
    private void ShowCurationOrOpen() => NavigationModel.RequestNavigate(ShellDestination.Families);
    private void MenuRunUSearch_Click(object sender, RoutedEventArgs e) => NavigationModel.RequestNavigate(ShellDestination.Build);
    private void MenuItemDeNovo_Click(object sender, RoutedEventArgs e) => NavigationModel.RequestNavigate(ShellDestination.DeNovo);
    private void MenuItemAnalytics_Click(object sender, RoutedEventArgs e) => NavigationModel.RequestNavigate(ShellDestination.Results);
    private void MenuItemComputeXic_Click(object sender, RoutedEventArgs e) => NavigationModel.RequestNavigate(ShellDestination.Quantify);
    private void RefreshNavigationState()
    {
        NavigationModel.UpdateProjectState(cgc != null, saveInProgress || analyticsInProgress || IsQuantifyBusy || Operations.IsBusy);
        NavigationModel.SetHomologyState(latestHomologyResult != null);
        NavigationModel.SetDocument(cgc?.fcf, lastFcfPath, loadedQuant.Count > 0);
        CommandManager.InvalidateRequerySuggested();
    }
}
