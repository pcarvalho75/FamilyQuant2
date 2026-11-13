using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant;

public enum ProjectExportKind { Report, RunSummary, PeptideTable, VisibleProteins, PanelAudit, FamilyComposition, Homology, DatabaseGap, SuggestedAdditions, Families, InterpretationInput }
public sealed record ProjectExportOption(ProjectExportKind Kind, string Section, string Label, bool Available, string Hint);

public partial class ExportWindow : Window
{
    public IReadOnlyList<ProjectExportOption> Options { get; }
    public ProjectExportKind? SelectedExport { get; private set; }
    public ReportFamilyNameChoice FamilyNames => FamilyNameChoice.SelectedIndex == 1
        ? ReportFamilyNameChoice.OtherLanguageName : ReportFamilyNameChoice.Name;

    public ExportWindow(IReadOnlyList<ProjectExportOption> options, ReportFamilyNameChoice familyNames = ReportFamilyNameChoice.Name, bool hasProject = true)
    {
        Options = options;
        InitializeComponent();
        FamilyNameChoice.SelectedIndex = familyNames == ReportFamilyNameChoice.OtherLanguageName ? 1 : 0;
        FamilyNameChoice.IsEnabled = hasProject;
        if (!hasProject) FamilyNameChoice.ToolTip = "Open a project to choose its report and Results family labels. The choice is kept when you export and save the project.";
        var grouped = (CollectionViewSource)Resources["GroupedExports"];
        grouped.Source = options;
        ExportChoices.ItemsSource = grouped.View;
        ExportChoices.SelectedItem = options.FirstOrDefault(option => option.Available);
        RefreshChoice();
    }
    private void ExportChoice_Changed(object sender, SelectionChangedEventArgs e) => RefreshChoice();
    private void RefreshChoice()
    {
        if (ExportButton == null) return;
        var option = ExportChoices.SelectedItem as ProjectExportOption;
        ExportButton.IsEnabled = option?.Available == true;
        ExportStatus.Text = option?.Label ?? "No stored data is available for export.";
        ExportStatus.ToolTip = option?.Hint ?? "Open a project or a stored homology result to export a file. No file has been written.";
    }
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (ExportChoices.SelectedItem is not ProjectExportOption { Available: true } option) return;
        SelectedExport = option.Kind;
        Close();
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
