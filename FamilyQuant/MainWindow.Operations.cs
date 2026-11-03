using System.Windows;
using System.Windows.Input;

namespace FamilyQuant;

public partial class MainWindow
{
    public AppOperationHost Operations { get; } = new();
    private IInputElement? focusBeforeOperation;
    private void InitializeOperations()
    {
        AppOperationHost.SetScope(this, Operations);
        ShellBusyOverlay.DataContext = Operations;
        Operations.PropertyChanged += (_, _) =>
        {
            bool wasEnabled = MainMenu.IsEnabled;
            MainMenu.IsEnabled = GridApp.IsEnabled = NavigationRail.IsEnabled = !Operations.IsBusy;
            if (wasEnabled != MainMenu.IsEnabled) RefreshShellMenuState();
            if (wasEnabled && Operations.IsBusy)
            {
                focusBeforeOperation = Keyboard.FocusedElement;
                Keyboard.ClearFocus();
            }
            else if (!Operations.IsBusy && focusBeforeOperation != null)
            {
                Keyboard.Focus(focusBeforeOperation);
                focusBeforeOperation = null;
            }
            RefreshNavigationState();
        };
        PreviewKeyDown += (_, args) =>
        {
            if (Operations.IsBusy && args.Key == Key.Escape) { Operations.Cancel(); args.Handled = true; }
        };
        CommandManager.AddPreviewCanExecuteHandler(this, (_, args) =>
        {
            if (!Operations.IsBusy) return;
            args.CanExecute = false;
            args.Handled = true;
        });
    }
}
