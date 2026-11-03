using System;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    public static Action<ReportDetailsWindow>? ReportDetailsPresenterOverride { get; set; }

    public ReportDetailsWindow? CreateReportDetailsEditor()
    {
        if (cgc == null || Operations.IsBusy || saveInProgress) return null;
        var project = cgc;
        return new ReportDetailsWindow(new(project.fcf.ReportDetails, details =>
        {
            if (!ReferenceEquals(cgc, project) || Operations.IsBusy || saveInProgress)
                throw new InvalidOperationException("The open project changed. Open Report details again.");
            if (project.ApplyReportDetails(details))
            {
                StatusModel.ShowMessage("Report details updated. Undo restores the previous text. Save the project to keep the details.");
            }
        }, project.fcf.ReportInputProvenance));
    }

    private void MenuItemReportDetails_Click(object sender, RoutedEventArgs e)
    {
        var editor = CreateReportDetailsEditor();
        if (editor == null)
        {
            ShellMessages.Report(this, "Open a project and wait for the current operation to finish before editing Report details.", "Report details");
            return;
        }
        if (ReportDetailsPresenterOverride != null) { ReportDetailsPresenterOverride(editor); return; }
        if (!ShellDialogs.IsDisplayed(this))
        {
            ShellMessages.Report(this, "Report details need a displayed application window.", "Report details", showDialog: false);
            return;
        }
        editor.Owner = this;
        editor.ShowDialog();
    }
}

public partial class ClusterGenerationControl
{
    public bool ApplyReportDetails(Report.ReportDetails details)
    {
        if (!ViewModel.ApplyReportDetails(details)) return false;
        UpdateGUI();
        return true;
    }
}
