using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    public static Action<ExportWindow>? ExportPresenterOverride { get; set; }

    private void MenuItemExport_Click(object sender, RoutedEventArgs e) => OpenExportWindow();

    internal void OpenExportWindow(AnalyticsViewModel? results = null)
    {
        if (Operations.IsBusy || saveInProgress || reportExportInProgress) return;
        if (ExportPresenterOverride == null && !ShellDialogs.IsDisplayed(this))
        { StatusModel.ShowMessage("Open the project window and use File > Export to choose a file."); return; }
        var gap = results?.DatabaseGap ?? new DatabaseGapViewModel(loadedManifest?.FamilyWorldResult, cgc?.fcf, loadedManifest?.DatabaseGapMinimumTagLength);
        bool project = cgc != null;
        bool quantified = results?.HasData ?? loadedQuant.Count > 0;
        bool report = project && loadedManifest != null && loadedCuration != null && loadedQuant.Count > 0;
        bool homology = results?.HasHomology ?? latestHomologyResult != null;
        var options = new List<ProjectExportOption>
        {
            new(ProjectExportKind.Report, "Report", "HTML, Word and supplement workbook", report,
                report ? "Opens the existing report choices and writes the Word report, supplement workbook and offline HTML report together. Undo does not revert the files; Save keeps any report-choice changes in the project."
                    : "Report export needs stored Results and the open project. Open the project and run Quantify to enable it."),
            new(ProjectExportKind.RunSummary, "Tables (CSV)", "Run summary", quantified,
                quantified ? "Writes the stored per-run and grouped signal summary to CSV with invariant decimal values. The project stays unchanged; Undo does not remove the file." : "No quantitation is stored for a run summary. Open a computed project or run Quantify to enable it."),
            new(ProjectExportKind.PeptideTable, "Tables (CSV)", "Peptide table", quantified,
                quantified ? "Writes the stored peptide table, isotope areas and family assignments to CSV. The project stays unchanged; Undo does not remove the file." : "No quantitation is stored for a peptide table. Open a computed project or run Quantify to enable it."),
            new(ProjectExportKind.VisibleProteins, "Tables (CSV)", "Visible protein table", cgc?.ButtonSaveReport.IsEnabled == true,
                cgc?.ButtonSaveReport.IsEnabled == true ? "Writes the Curate families protein table using its current display filters and values. The project stays unchanged; Undo does not remove the file." : "No protein row is visible in Curate families. Open a project and clear its search or display filters to enable this table."),
            new(ProjectExportKind.PanelAudit, "Tables (CSV)", "Panel assignment audit", project,
                project ? "Writes one CSV row per target protein with its panel labels and recorded assignment evidence. The project stays unchanged; Undo does not remove the file." : "Panel assignment audit needs an open project. Open or build families to enable it."),
            new(ProjectExportKind.FamilyComposition, "Tables (CSV)", "Family composition", cgc?.HasFullXic == true,
                cgc?.HasFullXic == true ? "Writes the current Full XIC family composition, shared signal and unattributed signal to CSV. The project stays unchanged; Undo does not remove the file." : "Family composition needs Full XIC quantitation. Open a computed project or run Quantify to enable it."),
            new(ProjectExportKind.Homology, "Tables (CSV)", "Homology assignments", homology,
                homology ? "Writes the latest standalone homology snapshot to CSV with one row per searched peptide, protein IDs, family IDs and ambiguity. No full XIC is required; the project stays unchanged and Undo does not remove the file." : "No standalone homology snapshot is available. Run Search in De novo review to enable it."),
            new(ProjectExportKind.DatabaseGap, "Tables (CSV)", "Database gap", gap.HasData,
                gap.HasData ? "Writes the selected stored Database gap table, global buckets and recorded rules to CSV. The project stays unchanged; Undo does not remove the file." : "The selected tag length has no stored Database gap table. Open Results and choose a computed length in a project with stored UniProt families results to enable it."),
            new(ProjectExportKind.SuggestedAdditions, "Suggested additions (FASTA)", "Suggested additions", gap.CanExportFasta,
                gap.CanExportFasta ? "Writes exact recorded world-hit sequences to a suggested-additions FASTA for review. Your database stays unchanged; Undo does not remove the file." : "Suggested additions need credited world proteins and verified matching stored sequences. Open a project with that evidence and choose a computed tag length in Results to enable it."),
            new(ProjectExportKind.Families, "Families (.fcf)", "Families", project,
                project ? "Writes the curated families, evidence and family list to a separate format-2 .fcf without quantitation. The .fqproj Save path and unsaved changes stay unchanged; Undo does not remove the file." : "Family export needs an open project. Open or build families to enable it."),
            new(ProjectExportKind.InterpretationInput, "Interpretation input (Markdown)", "Interpretation input", report && loadedManifest?.FamilyRunShares != null,
                report && loadedManifest?.FamilyRunShares != null ? "Writes stored Results, panel and report details to Markdown for an assistant you choose. The project stays unchanged; Undo does not remove the file." : "Interpretation input needs stored Results with family run shares. Open a computed project or run Quantify to enable it.")
        };
        var window = new ExportWindow(options, cgc?.fcf.ReportFamilyNames ?? ReportFamilyNameChoice.Name, project);
        if (ShellDialogs.IsDisplayed(this)) window.Owner = this;
        if (ExportPresenterOverride != null) ExportPresenterOverride(window);
        else window.ShowDialog();
        if (window.SelectedExport != null && cgc?.ViewModel.ApplyReportFamilyNames(window.FamilyNames) == true)
            analytics?.ViewModel.RefreshFamilyNames();
        switch (window.SelectedExport)
        {
            case ProjectExportKind.Report: MenuItemExportReport_Click(this, new RoutedEventArgs()); break;
            case ProjectExportKind.VisibleProteins: cgc?.ExportVisibleProteinTable(this); break;
            case ProjectExportKind.PanelAudit: MenuItemExportPanelAudit_Click(this, new RoutedEventArgs()); break;
            case ProjectExportKind.FamilyComposition: MenuItemExportComposition_Click(this, new RoutedEventArgs()); break;
            case ProjectExportKind.Families: MenuItemSaveFCF_Click(this, new RoutedEventArgs()); break;
            case ProjectExportKind.InterpretationInput: MenuItemExportInterpretationInput_Click(this, new RoutedEventArgs()); break;
            case ProjectExportKind.RunSummary: ExportResultsTable(results, ResultsExportKind.Summary); break;
            case ProjectExportKind.PeptideTable: ExportResultsTable(results, ResultsExportKind.Peptides); break;
            case ProjectExportKind.Homology: ExportResultsTable(results, ResultsExportKind.Homology); break;
            case ProjectExportKind.DatabaseGap: ExportResultsTable(results, ResultsExportKind.DatabaseGap); break;
            case ProjectExportKind.SuggestedAdditions: ExportResultsTable(results, ResultsExportKind.SuggestedAdditions); break;
        }
    }

    private async void ExportResultsTable(AnalyticsViewModel? model, ResultsExportKind kind)
    {
        model ??= new AnalyticsViewModel(kind is ResultsExportKind.DatabaseGap or ResultsExportKind.SuggestedAdditions ? null : loadedQuant,
            loadedManifest, kind is ResultsExportKind.DatabaseGap or ResultsExportKind.SuggestedAdditions ? cgc?.fcf : null, homology: latestHomologyResult);
        model.ExportKind = kind;
        if (!model.CanExport) { StatusModel.ShowMessage(model.ExportUnavailableReason); return; }
        bool fasta = kind == ResultsExportKind.SuggestedAdditions;
        var dialog = new SaveFileDialog { Filter = fasta ? "FASTA files (*.fasta)|*.fasta|All files (*.*)|*.*" : FileDialogFilters.Csv, AddExtension = true,
            DefaultExt = fasta ? ".fasta" : ".csv", FileName = kind switch {
                ResultsExportKind.Summary => "familyquant_analytics_summary.csv",
                ResultsExportKind.Homology => "FamilyQuant_DeNovo_Homology.csv",
                ResultsExportKind.DatabaseGap => "familyquant_database_gap.csv",
                ResultsExportKind.SuggestedAdditions => "familyquant_suggested_additions.fasta",
                _ => "familyquant_analytics_peptides.csv" }, Title = kind switch {
                    ResultsExportKind.Peptides => "Export peptide table", ResultsExportKind.Summary => "Export run summary", _ => "Export results" } };
        if (ShellDialogs.ShowFile(this, "results." + kind, dialog)) await model.ExportAsync(dialog.FileName, Operations);
        StatusModel.ShowMessage(model.Status);
    }
}
