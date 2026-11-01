using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant;

public partial class MainWindow
{
    private bool routingCopy;

    private void InitializeShellCommands()
    {
        shellSettingsPath = ShellSettingsPathOverride ?? Path.Combine(ApplicationPaths.LocalDataDirectory, "shell.json");
        persistShellSettings = ShellSettingsPathOverride != null ||
            System.Reflection.Assembly.GetEntryAssembly() == typeof(MainWindow).Assembly;
        shellState = persistShellSettings ? ShellSessionState.Load(shellSettingsPath) : new();
        void Bind(RoutedCommand command, Action run, Func<bool>? allowed = null) =>
            CommandBindings.Add(new CommandBinding(command, (_, e) => { run(); e.Handled = true; },
                (_, e) => { e.CanExecute = allowed?.Invoke() ?? true; e.Handled = true; }));
        Bind(ApplicationCommands.New, () => MenuRunUSearch_Click(this, new RoutedEventArgs()), () => NavigationModel.CanNavigate(ShellDestination.Build));
        Bind(ApplicationCommands.Open, () => MenuItemCluster_Click(this, new RoutedEventArgs()), () => !saveInProgress && !IsQuantifyBusy);
        Bind(ApplicationCommands.Save, () => MenuItemSaveProject_Click(this, new RoutedEventArgs()), () => cgc != null && !saveInProgress);
        Bind(ApplicationCommands.SaveAs, () => MenuItemSaveAs_Click(this, new RoutedEventArgs()), () => cgc != null && !saveInProgress);
        Bind(ApplicationCommands.Undo, () => cgc?.ButtonUndo.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)),
            () => cgc?.ButtonUndo.IsEnabled == true);
        Bind(ApplicationCommands.Copy, CopyVisibleSelection, () => !routingCopy && CopyGrid()?.SelectedItems.Count > 0);
        Bind(ApplicationCommands.Find, () =>
        {
            ShowCurationOrOpen();
            cgc?.TextBoxSearch.Focus();
            cgc?.TextBoxSearch.SelectAll();
        }, () => cgc != null);
        Bind(ApplicationCommands.Help, () => MenuItemHowToRead_Click(this, new RoutedEventArgs()));
        Bind(ApplicationCommands.Close, Close, () => !saveInProgress);
        foreach (var shortcut in new[]
        {
            (ApplicationCommands.New, Key.N, ModifierKeys.Control),
            (ApplicationCommands.Open, Key.O, ModifierKeys.Control),
            (ApplicationCommands.Save, Key.S, ModifierKeys.Control),
            (ApplicationCommands.SaveAs, Key.S, ModifierKeys.Control | ModifierKeys.Shift),
            (ApplicationCommands.Undo, Key.Z, ModifierKeys.Control),
            (ApplicationCommands.Copy, Key.C, ModifierKeys.Control),
            (ApplicationCommands.Find, Key.F, ModifierKeys.Control),
            (ApplicationCommands.Help, Key.F1, ModifierKeys.None)
        }) InputBindings.Add(new KeyBinding(shortcut.Item1, shortcut.Item2, shortcut.Item3));
        RefreshRecentMenu();
    }

    private DataGrid? CopyGrid()
    {
        DependencyObject? focus = Keyboard.FocusedElement as DependencyObject;
        while (focus != null)
        {
            if (focus is DataGrid grid) return grid;
            focus = focus is Visual ? VisualTreeHelper.GetParent(focus) : LogicalTreeHelper.GetParent(focus);
        }
        return cgc != null && GridApp.Children.Contains(cgc) ? cgc.DataGridResultGrid : null;
    }

    private void CopyVisibleSelection()
    {
        if (routingCopy || CopyGrid() is not DataGrid grid) return;
        routingCopy = true;
        try { ApplicationCommands.Copy.Execute(null, grid); }
        finally { routingCopy = false; }
    }

    private void RefreshShellMenuState()
    {
        bool project = cgc != null;
        MenuItemSaveAs.IsEnabled = project;
        MenuItemExport.IsEnabled = project;
        MenuItemExportComposition.IsEnabled = cgc?.ButtonExportComposition.IsEnabled == true;
        MenuItemExportFamilyTable.IsEnabled = cgc?.ButtonSaveReport.IsEnabled == true;
        MenuItemHideDecoys.IsEnabled = project;
        MenuItemHideContaminants.IsEnabled = project;
        MenuItemAnalytics.IsEnabled = project && !saveInProgress && !analyticsInProgress;
        MenuItemHideDecoys.IsChecked = cgc?.CheckBoxExcludeReverse.IsChecked == true;
        MenuItemHideContaminants.IsChecked = cgc?.CheckBoxExcludeContaminants.IsChecked == true;
        UpdateThemeMenu();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshRecentMenu()
    {
        MenuItemRecent.Items.Clear();
        foreach (string path in shellState.RecentProjects)
        {
            bool exists = File.Exists(path);
            var item = new MenuItem
            {
                Header = Path.GetFileName(path).Replace("_", "__"), IsEnabled = exists,
                ToolTip = exists ? "Opens this recent project: " + path : "This file is unavailable. Last path: " + path,
                Tag = path
            };
            item.Click += async (_, _) => await OpenProjectSafelyAsync(path);
            MenuItemRecent.Items.Add(item);
        }
        if (MenuItemRecent.Items.Count == 0)
            MenuItemRecent.Items.Add(new MenuItem { Header = "No recent projects", IsEnabled = false,
                ToolTip = "Open or save a project to add it to this list." });
    }

    private string? ChooseOpenProjectPath()
    {
        if (SelectOpenPathOverride != null) return SelectOpenPathOverride();
        if (PresentationSource.FromVisual(this) == null) return null;
        var dialog = new OpenFileDialog
        {
            Filter = "FamilyQuant project or Family Cluster File (*.fqproj;*.fcf)|*.fqproj;*.fcf|" +
                "FamilyQuant project (*.fqproj)|*.fqproj|Family Cluster File (*.fcf)|*.fcf",
            Title = "Open a project or Family Cluster File"
        };
        PrepareFileDialog(dialog);
        if (dialog.ShowDialog(this) != true) return null;
        RememberFolder(dialog.FileName);
        return dialog.FileName;
    }

    private string? ChooseSaveProjectPath(bool saveAs)
    {
        if (!saveAs && !string.IsNullOrWhiteSpace(lastFcfPath) && ProjectStore.IsProjectPath(lastFcfPath)) return lastFcfPath;
        if (SelectSaveProjectPathOverride != null) return SelectSaveProjectPathOverride();
        if (PresentationSource.FromVisual(this) == null) return null;
        var dialog = new SaveFileDialog
        {
            Filter = "FamilyQuant project (*.fqproj)|*.fqproj", DefaultExt = ".fqproj", AddExtension = true,
            Title = "Save project as", FileName = string.IsNullOrWhiteSpace(lastFcfPath) ? "project.fqproj"
                : Path.GetFileNameWithoutExtension(lastFcfPath) + ".fqproj"
        };
        PrepareFileDialog(dialog);
        if (dialog.ShowDialog(this) != true) return null;
        RememberFolder(dialog.FileName);
        return dialog.FileName;
    }

    private void ReportShellMessage(string message, string title, MessageBoxImage image)
    {
        StatusModel.ShowMessage(message);
        if (PresentationSource.FromVisual(this) != null)
            MessageBox.Show(this, message, title, MessageBoxButton.OK, image);
    }

    private void MenuItemRecent_SubmenuOpened(object sender, RoutedEventArgs e) => RefreshRecentMenu();
    private void MenuView_SubmenuOpened(object sender, RoutedEventArgs e) => RefreshShellMenuState();
    private void MenuItemFamilies_Click(object sender, RoutedEventArgs e) => ShowCurationOrOpen();
    private void MenuItemHideDecoys_Click(object sender, RoutedEventArgs e)
    {
        if (cgc != null) cgc.CheckBoxExcludeReverse.IsChecked = MenuItemHideDecoys.IsChecked;
    }
    private void MenuItemHideContaminants_Click(object sender, RoutedEventArgs e)
    {
        if (cgc != null) cgc.CheckBoxExcludeContaminants.IsChecked = MenuItemHideContaminants.IsChecked;
    }
    private void MenuItemExportFamilyTable_Click(object sender, RoutedEventArgs e)
    {
        if (cgc?.ButtonSaveReport.IsEnabled == true) cgc.ButtonSaveReport.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private void MenuItemExportComposition_Click(object sender, RoutedEventArgs e)
    {
        if (cgc?.ButtonExportComposition.IsEnabled == true) cgc.ButtonExportComposition.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    private async void MenuItemSaveAs_Click(object sender, RoutedEventArgs e) => await SaveProjectAsync(saveAs: true);
}
