using FamilyQuant.Report;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    public Task<bool>? InterpretationInputTask { get; private set; }

    private async void MenuItemExportInterpretationInput_Click(object sender, RoutedEventArgs e)
    {
        InterpretationInputTask = ExportInterpretationInputAsync();
        await InterpretationInputTask;
    }

    /// <summary>Writes the stored tables, panel and details for an assistant the user chooses. The program does not interpret them.</summary>
    public async Task<bool> ExportInterpretationInputAsync()
    {
        var curation = loadedCuration;
        var manifest = loadedManifest;
        if (cgc == null || curation == null || manifest?.FamilyRunShares == null || loadedQuant.Count == 0)
        {
            ShellMessages.Report(this, "The interpretation input needs stored Results. Use Quantify, then choose File > Export > "
                + "Interpretation input (Markdown).", "Export interpretation input");
            return false;
        }
        var dialog = new SaveFileDialog { Filter = FileDialogFilters.Markdown, DefaultExt = ".md", AddExtension = true,
            FileName = ReportInterpretationInput.FileName, Title = "Save interpretation input" };
        if (!ShellDialogs.ShowFile(this, "report.interpretation-input", dialog)) return false;
        using var operation = AppOperationHost.For(this).Begin("Export interpretation input");
        try
        {
            string version = AppVersionInfo.Current();
            await Task.Run(() => AppOperationFiles.WriteFile(dialog.FileName, path => File.WriteAllText(path,
                ReportInterpretationInput.FromProject(curation, manifest, version), new UTF8Encoding(false)), operation.Context));
            ShellMessages.Report(this, "Interpretation input saved to " + dialog.FileName + ".", "Export complete", image: MessageBoxImage.Information);
            return true;
        }
        catch (OperationCanceledException) when (operation.Context.IsCancellationRequested) { return false; }
        catch (Exception ex)
        {
            ShellMessages.Report(this, "The interpretation input could not be exported: " + ex.Message,
                "Export failed", image: MessageBoxImage.Error);
            return false;
        }
    }
}
