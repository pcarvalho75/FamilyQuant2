using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private async void MenuItemExportPanelAudit_Click(object sender, RoutedEventArgs e)
    {
        var file = cgc?.fcf;
        if (file == null) return;
        var dialog = new SaveFileDialog { Filter = FileDialogFilters.Csv, DefaultExt = ".csv", AddExtension = true,
            FileName = "FamilyQuant_panel_audit.csv", Title = "Save panel assignment audit" };
        if (!ShellDialogs.ShowFile(this, "panel.audit-csv", dialog)) return;
        using var operation = AppOperationHost.For(this).Begin("Export panel assignment audit");
        try
        {
            await Task.Run(() => AppOperationFiles.WriteFile(dialog.FileName,
                path => File.WriteAllText(path, PanelAuditCsv.BuildCsv(file), new UTF8Encoding(true)), operation.Context));
            ShellMessages.Report(this, "Panel assignment audit saved to " + dialog.FileName + ".", "Export complete", image: MessageBoxImage.Information);
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested) { }
        catch (Exception ex)
        {
            ShellMessages.Report(this, "The panel assignment audit could not be exported: " + ex.Message,
                "Export failed", image: MessageBoxImage.Error);
        }
    }
}
