using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private void MenuItemOpenErrorLogs_Click(object sender, RoutedEventArgs e)
    {
        if (!CrashReportStore.TryOpenFolder(CrashReportStore.DefaultDirectory))
            ShellMessages.Report(this, "The error logs folder could not be opened.", "Error logs", showDialog: false);
    }
}
