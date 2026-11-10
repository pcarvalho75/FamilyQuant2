using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ResultsExportFact(int Line, string Fact, bool Passed, string Actual);

    private static List<ResultsExportFact> ResultsExportFactTests(string output)
    {
        Section("Results export facts - native empty, dialogs, success and failure");
        EnsureApplication(); Directory.CreateDirectory(output);
        var facts = new List<ResultsExportFact>();
        void Fact(int line, string fact, bool passes, string actual) => facts.Add(new(line, fact, passes, actual));
        bool Has(string text, params string[] phrases) => phrases.All(phrase => text.Contains(phrase, StringComparison.OrdinalIgnoreCase));
        var empty = new AnalyticsControl(Array.Empty<QuantRow>(), null);
        PumpDispatcher();
        string emptyText = ((TextBlock)empty.FindName("TextBlockEmpty")).Text;
        Fact(188, "empty view explains that Results needs the program's own full isotope-envelope quantitation",
            Has(emptyText, "FamilyQuant", "isotope", "full XIC"), emptyText);
        Fact(189, "empty view directs the loaded project to Quantify's computation", Has(emptyText, "project", "Quantify", "compute"), emptyText);
        Fact(190, "empty view says compute first and return", Has(emptyText, "compute", "return"), emptyText);
        Fact(191, "empty view explains plain fcf and saved fqproj quantitation state", Has(emptyText, ".fcf", ".fqproj"), emptyText);
        Fact(192, "empty view explains a project saved before its first computation", Has(emptyText, "before", "computed"), emptyText);
        Fact(193, "empty view explains that computation enables the graphs", Has(emptyText, "compute", "graphs"), emptyText);

        var previousContext = System.Threading.SynchronizationContext.Current;
        var previousCulture = CultureInfo.CurrentCulture;
        var previousFile = ShellDialogs.FilePresenter; var previousFolder = ShellDialogs.FolderPresenter;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var rows = SyntheticAnalyticsRows();
        var manifest = ResultsCompleteFixtureManifest(rows);
        var model = new AnalyticsViewModel(rows, manifest, BuildSyntheticFcf());
        var control = new AnalyticsControl(model);
        var button = (Button)control.FindName("ButtonExport");
        var window = new Window { Content = control, Width = 1180, Height = 800, Left = -30000, Top = -30000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        try
        {
            // Raising the native handler on a detached view must refuse before opening a dialog.
            int dialogCalls = 0;
            ShellDialogs.FilePresenter = (_, _, _) => { dialogCalls++; return false; };
            ShellDialogs.FolderPresenter = (_, _, _) => { dialogCalls++; return false; };
            foreach (var kind in new[] { ResultsExportKind.Peptides })
            {
                Select(kind); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                string status = model.Status;
                int[] lines = { 426, 427, 428 };
                foreach (int line in lines)
                    Fact(line, kind + " detached view refuses dialogs and names the next step", dialogCalls == 0
                        && Has(status, "project window", "File > Export"), status);
            }
            window.Show(); window.UpdateLayout(); PumpDispatcher();
            var chartObjects = ((StackPanel)control.FindName("PanelCharts")).Children.Cast<object>().ToArray();
            var data = model.Data;
            var host = AppOperationHost.For(control);
            foreach (var kind in new[] { ResultsExportKind.Peptides, ResultsExportKind.Summary })
            {
                Select(kind);
                string title = "", filter = "", extension = "", suggested = ""; bool addExtension = false;
                ShellDialogs.FilePresenter = (_, _, dialog) =>
                { title = dialog.Title; filter = dialog.Filter; extension = dialog.DefaultExt; addExtension = dialog.AddExtension; suggested = dialog.FileName; return false; };
                ShellDialogs.FolderPresenter = (_, _, dialog) => { title = dialog.Title; return false; };
                string beforeCancel = model.Status;
                ChooseResultsExport(model, kind); PumpDispatcher();
                Check(kind + " cancelled native chooser preserves the previous status", model.Status == beforeCancel && !host.IsBusy);
                if (kind is ResultsExportKind.Peptides or ResultsExportKind.Summary)
                {
                    bool peptide = kind == ResultsExportKind.Peptides;
                    Fact(peptide ? 320 : 328, "native chooser retains its CSV default name and describes the selected table",
                        suggested == (peptide ? "familyquant_analytics_peptides.csv" : "familyquant_analytics_summary.csv")
                        && Has(title, peptide ? "peptide" : "summary"), title + " / " + suggested);
                    Fact(434, "CSV chooser retains CSV and all-files filters", filter == "CSV files (*.csv)|*.csv|All files (*.*)|*.*", filter);
                    Fact(438, "CSV chooser retains its extension and automatic extension behavior",
                        extension.TrimStart('.') == "csv" && addExtension, extension + "; AddExtension=" + addExtension);
                }
                var folders = new List<string>();
                for (int repeat = 1; repeat <= 2; repeat++)
                {
                    string path = Path.Combine(output, kind + "-" + repeat.ToString(CultureInfo.InvariantCulture)); folders.Add(path);
                    ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = path; return true; };
                    ShellDialogs.FolderPresenter = (_, _, dialog) => { dialog.FolderName = path; return true; };
                    var exportWatch = System.Diagnostics.Stopwatch.StartNew();
                    ChooseResultsExport(model, kind); FinishBusyOperation(host);
                    Console.WriteLine(FormattableString.Invariant($"MEASURE Results export kind={kind} repeat={repeat} seconds={exportWatch.Elapsed.TotalSeconds:F3}"));
                }
                string success = model.Status;
                bool sameCharts = ReferenceEquals(data, model.Data) && chartObjects.SequenceEqual(((StackPanel)control.FindName("PanelCharts")).Children.Cast<object>());
                if (kind is ResultsExportKind.Peptides or ResultsExportKind.Summary)
                {
                    bool csv = File.Exists(folders[0]) && File.ReadAllBytes(folders[0]).SequenceEqual(File.ReadAllBytes(folders[1]));
                    string expectedCsv = kind == ResultsExportKind.Peptides ? AnalyticsCsv.BuildPeptideCsv(model.Data!.Peptides)
                        : AnalyticsCsv.BuildSummaryCsv(model.Data!.Fractions, model.Data.Grouped);
                    Check(kind + " native route writes exact Core CSV twice under French display culture", csv && File.ReadAllText(folders[0]) == expectedCsv);
                    Fact(kind == ResultsExportKind.Peptides ? 322 : 330, "native export writes and describes the selected table",
                        csv && Has(success, kind == ResultsExportKind.Peptides ? "peptide" : "summary"), success);
                    foreach (int line in new[] { 446, 447, 448 }) Fact(line, "CSV success names its path, invariant decimals and unchanged charts",
                        csv && success.Contains(folders[1], StringComparison.Ordinal) && Has(success, "dot", "decimal", "language", "unchanged") && sameCharts, success);
                }
            }

            // A file in place of the parent folder forces a real IO refusal without changing permissions.
            string blocker = Path.Combine(output, "blocked-parent"); File.WriteAllText(blocker, "retained", new UTF8Encoding(false));
            foreach (var kind in new[] { ResultsExportKind.Peptides })
            {
                Select(kind); string path = Path.Combine(blocker, "export");
                ShellDialogs.FilePresenter = (_, _, dialog) => { dialog.FileName = path; return true; };
                ShellDialogs.FolderPresenter = (_, _, dialog) => { dialog.FolderName = path; return true; };
                ChooseResultsExport(model, kind); FinishBusyOperation(host);
                string status = model.Status;
                bool refused = File.ReadAllText(blocker) == "retained" && !File.Exists(path) && !Directory.Exists(path)
                    && Has(status, "did not finish", "Reason", "write", "folder", "Excel");
                foreach (int line in new[] { 452, 453, 454 })
                    Fact(line, "IO refusal keeps existing files and gives the reason and specific recovery advice", refused, status);
            }
            var incomplete = new AnalyticsViewModel(rows, new QuantManifest { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, PerRunTic = manifest.PerRunTic }, BuildSyntheticFcf());
            incomplete.ExportKind = ResultsExportKind.EvidencePack;
            string missingPack = Path.Combine(output, "incomplete-pack"); incomplete.Export(missingPack);
            foreach (int line in new[] { 412, 413 })
                Fact(line, "incomplete pack names the missing study, refuses partial publication and explains retry", !Directory.Exists(missingPack)
                    && Has(incomplete.Status, "did not finish", "overlap study", "partial", "recompute", "try again"), incomplete.Status);
        }
        finally
        {
            window.Close(); PumpDispatcher(); ShellDialogs.FilePresenter = previousFile; ShellDialogs.FolderPresenter = previousFolder;
            CultureInfo.CurrentCulture = previousCulture; System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        foreach (var fact in facts) Check("RS export fact frozen line " + fact.Line + ": " + fact.Fact, fact.Passed, fact.Passed ? null : fact.Actual);
        return facts;

        void Select(ResultsExportKind kind)
        { model.ExportKind = kind; PumpDispatcher(); }
    }

    private static QuantManifest ResultsCompleteFixtureManifest(IReadOnlyList<QuantRow> rows) => new()
    {
        DerivedMethod = NativeQuantFractions.Method,
        PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 },
        DeNovoOverlap = new DeNovoOverlapSummary(),
        DeNovoScoreDistribution = DeNovoScoreDistributionStudy.Build(Array.Empty<PeaksDeNovoRow>()),
        DeNovoIdentification = new DeNovoIdentificationSummary(), IdentificationConcordance = new IdentificationConcordanceSummary(),
        ReferenceEnvelopeModelDiagnostics = ReferenceEnvelopeEstimator.Evaluate(NativeQuantFractions.DedupToPeptides(rows)),
        PresentationIdentitySweep = new() { new() { Identity = 80, Groups = 3, TargetGroups = 3 } }
    };
}
