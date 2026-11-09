using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ResultsLedgerCase(string Id, string Assertion, string Symbol, bool Passed);

    private static int ResultsPreservationCommand(string[] args, string workDir)
    {
        if (args.Length != 3) throw new ArgumentException("Supply a fresh output directory and a timestamp from date.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Proof output must be fresh.");
        Directory.CreateDirectory(output);
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        List<ResultsLedgerCase> cases;
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            cases = ResultsPreservationTests(Path.Combine(output, "native"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), values = Path.Combine(output, "actual-checks.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(values, cases);
        string snapshot = Path.Combine(output, "executed"); Directory.CreateDirectory(snapshot);
        var artifactPaths = new List<string> { log, values };
        foreach (string path in new[] { "FamilyQuant/AnalyticsControl.xaml", "FamilyQuant/AnalyticsControl.xaml.cs",
            "FamilyQuant/AnalyticsViewModel.cs", "FamilyQuant.Tests/Program.ResultsPreservation.cs",
            "FamilyQuant.Tests/Program.Commands.ResultsPreservation.cs" })
        {
            string copy = Path.Combine(snapshot, path.Replace('/', '_'));
            File.Copy(Path.Combine(root, path), copy); artifactPaths.Add(copy);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location })
        {
            string copy = Path.Combine(snapshot, Path.GetFileName(path));
            File.Copy(path, copy); artifactPaths.Add(copy);
        }
        artifactPaths.AddRange(Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(p => !artifactPaths.Contains(p, StringComparer.OrdinalIgnoreCase)));
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true,
            UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        const string runId = "RS-native-results";
        var observations = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!
            .Observations.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[2], commit,
            artifactPaths.Select(p => new FeatureLedgerEvidence.Artifact(p, FeatureLedgerEvidence.Hash(p))).ToList(), root);
        var rows = cases.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new FeatureLedgerEvidence.Row(c.Id,
            new[] { "FamilyQuant/AnalyticsControl.xaml", "FamilyQuant/AnalyticsControl.xaml.cs", "FamilyQuant/AnalyticsViewModel.cs" }
                .Select(p => new FeatureLedgerEvidence.Implementation(p, c.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, p)))).ToList(),
            c.Passed ? new() { new(runId, "test", nameof(ResultsPreservationTests), c.Assertion, log, "") } : new(),
            "Native synthetic Results interaction. Copy improvements are explicitly mapped for independent semantic review. Export refusal proves the required-study guard, not a complete evidence pack.",
            Compared: observations[c.Id].Detail + " -> " + c.Symbol + "; " + c.Assertion)).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "RS-interactions.json"),
            new FeatureLedgerEvidence.Register(1, "RS-interactions", FeatureLedger.ReferenceCommit, new() { run }, rows));
        return failed == 0 ? 0 : 1;
    }

    private static List<ResultsLedgerCase> ResultsPreservationTests(string output)
    {
        Section("Results preservation - native retained controls and displaced handlers");
        EnsureApplication(); Directory.CreateDirectory(output);
        var cases = new List<ResultsLedgerCase>();
        void Prove(string id, string behavior, bool matches, string symbol)
        {
            string assertion = "RS " + id + ": " + behavior;
            Check(assertion, matches); cases.Add(new(id, assertion, symbol, matches));
        }
        var previousTheme = Themes.ThemeManager.Current;
        var previousContext = System.Threading.SynchronizationContext.Current;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var previousFile = ShellDialogs.FilePresenter;
        var previousFolder = ShellDialogs.FolderPresenter;
        var model = new AnalyticsViewModel(SyntheticAnalyticsRows(), new QuantManifest
            { DerivedMethod = FamilyQuant.DeNovo.NativeQuantFractions.Method, PerRunTic = new() { ["ui_01"] = 5e8, ["ui_02"] = 4e8 } }, BuildSyntheticFcf());
        var control = new AnalyticsControl(model);
        var window = new Window { Content = control, Width = 1180, Height = 800, Left = -30000, Top = -30000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            window.Show(); window.UpdateLayout(); PumpDispatcher(); window.UpdateLayout(); PumpDispatcher();
            var top = (ComboBox)control.FindName("ComboBoxFamilyTopN");
            var percent = (CheckBox)control.FindName("CheckBoxFamilyPercent");
            var exports = (ComboBox)control.FindName("ComboBoxExportKind");
            var button = (Button)control.FindName("ButtonExport");
            var host = AppOperationHost.For(control);
            window.UpdateLayout(); PumpDispatcher();
            Console.WriteLine("Initial family selector: visible=" + top.IsVisible + "; enabled=" + top.IsEnabled
                + "; selected=" + ((top.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "none") + "; model=" + model.FamilyTopN);
            Prove("FQ15-155754DC695E", "Analytics heading is presented as Results", WalkTree(control).OfType<TextBlock>()
                .Any(text => text.IsVisible && text.Text == "Results"), "AnalyticsControl / Results heading");
            Prove("FQ15-152CD0CBD7E6", "family chart retains its visible section label", WalkTree(control).OfType<TextBlock>()
                .Any(text => text.IsVisible && text.Text == "Family chart"), "AnalyticsControl / family chart label");
            void Tooltip(string id, FrameworkElement element, string expected, string symbol) => Prove(id,
                "retained tooltip: " + expected, element.ToolTip is string actual && actual == expected
                    && actual.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 6, symbol);
            Tooltip("FQ15-6E6228F56C09", top, "Choose the largest families as separate bars: 10, 15, 20, 30 or all families, with 15 as the default; the remainder shares one bar. This changes the chart and subsequent figure exports without editing project quantities; unavailable until quantitation exists.", "ComboBoxFamilyTopN.ToolTip");
            Prove("FQ15-1F69AF99C92A", "family limit selector is displayed and defaults to fifteen", top.IsVisible && top.IsEnabled
                && (string?)((ComboBoxItem)top.SelectedItem).Tag == "15", "ComboBoxFamilyTopN / ChartChoiceChanged");
            var options = new[] {
                (10, "Top 10 families", "FQ15-1F00D6FFE5EE", "FQ15-777E1F0E05BC"),
                (15, "Top 15 families", "FQ15-54521B6EF355", "FQ15-B81F95D7A694"),
                (20, "Top 20 families", "FQ15-A376C13A408D", "FQ15-1A7280F9C040"),
                (30, "Top 30 families", "FQ15-4A6B08256B3F", "FQ15-075F89E875C2"),
                (0, "All families", "FQ15-905C4A500397", "FQ15-3FF0D958178F") };
            bool allChanged = true;
            foreach (var (limit, label, controlId, textId) in options)
            {
                var item = top.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == limit.ToString(CultureInfo.InvariantCulture));
                top.SelectedItem = item; PumpDispatcher();
                bool changed = model.FamilyTopN == limit;
                allChanged &= changed;
                Prove(controlId, "family limit " + limit + " can be selected through its native control", changed,
                    "ComboBoxFamilyTopN / ChartChoiceChanged");
                Prove(textId, "family limit retains its label " + label, (string?)item.Content == label,
                    "ComboBoxFamilyTopN.Items");
                var tooltip = limit switch {
                    10 => ("FQ15-803482A42F63", "Show the ten largest families separately and combine all remaining families in one bar, suited to presentation slides. This changes chart layout and subsequent figure exports without editing quantities."),
                    15 => ("FQ15-EFA52B9AC7C2", "Show the fifteen largest families separately and combine all remaining families in one bar; this is the default. This changes chart layout and subsequent figure exports without editing quantities."),
                    20 => ("FQ15-CA12594472E1", "Show the twenty largest families separately and combine all remaining families in one bar, suited to a paper figure. This changes chart layout and subsequent figure exports without editing quantities."),
                    30 => ("FQ15-7EF21BC4741F", "Show the thirty largest families separately and combine all remaining families in one bar; labels need more screen space. This changes chart layout and subsequent figure exports without editing quantities."),
                    _ => ("FQ15-3652AC893FCA", "Give every family its own chart bar, allowing scrolling when needed. This changes chart layout and subsequent figure exports without editing quantities.") };
                Tooltip(tooltip.Item1, item, tooltip.Item2, "ComboBoxFamilyTopN / tag " + limit + " / ToolTip");
            }
            Prove("FQ15-DF754B3A82A5", "selection event applies all five family limits", allChanged, "ChartChoiceChanged");
            percent.IsChecked = true; PumpDispatcher();
            bool checkedApplied = model.FamilyAsPercent && WalkTree(control).OfType<FamilyContributionChart>().Single().IsPercentOfTotal;
            Prove("FQ15-1826BA8181A2", "percent control remains enabled and changes the chart", percent.IsVisible && percent.IsEnabled && checkedApplied,
                "CheckBoxFamilyPercent / ChartChoiceChanged");
            Prove("FQ15-A338D170136B", "Checked event switches the displayed chart to measured-signal percentages", checkedApplied, "ChartChoiceChanged");
            percent.IsChecked = false; PumpDispatcher();
            bool uncheckedApplied = !model.FamilyAsPercent && !WalkTree(control).OfType<FamilyContributionChart>().Single().IsPercentOfTotal;
            Prove("FQ15-C213AEB5CD1C", "Unchecked event restores the envelope chart", uncheckedApplied, "ChartChoiceChanged");
            Prove("FQ15-C968838F81DD", "percent handler replacement applies checked and unchecked choices", checkedApplied && uncheckedApplied, "ChartChoiceChanged");
            Prove("FQ15-E25869E2C149", "percent label names the measured signal", (string?)percent.Content == "Percent of measured signal"
                && checkedApplied && uncheckedApplied, "CheckBoxFamilyPercent.Content");
            Tooltip("FQ15-59366EF0C808", percent,
                "Show family bars as percentages of measured peptide signal instead of signal amounts; count-in-every-family bars can overlap and their percentages must not be summed. This changes charts and subsequent figure exports without editing quantities; disabled until quantitation exists.", "CheckBoxFamilyPercent.ToolTip");

            foreach (var item in new[] {
                (ResultsExportKind.Peptides, "FQ15-26786D42C42C", "FQ15-523631E1F20E", "FQ15-44CB7F99655E"),
                (ResultsExportKind.Summary, "FQ15-9DEFC5CA51C6", "FQ15-4CC6F55B2F33", "FQ15-6C182BB4B81F"),
                (ResultsExportKind.Figures, "FQ15-8F08DA795CB5", "FQ15-9AB13C0AE2A4", "FQ15-C7CC0C2ABDF0"),
                (ResultsExportKind.EvidencePack, "FQ15-C571D9176653", "FQ15-D56E685BBF02", "FQ15-187ED75059DC") })
            {
                exports.SelectedItem = exports.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == item.Item1.ToString());
                PumpDispatcher();
                var copy = item.Item1 switch {
                    ResultsExportKind.Peptides => ("FQ15-3BCC49F296C6", "Peptide table (CSV)", "FQ15-21F91C29B9E2", "Select the peptide table with run, sequence, charge, mass, isotope areas, envelope and family assignments for CSV export. A file is written only when Export is confirmed, and Undo does not remove it."),
                    ResultsExportKind.Summary => ("FQ15-C7B652959EE3", "Run summary (CSV)", "FQ15-7EE1B55F5E41", "Select per-run TIC, peptide counts, envelopes and recovery shares for CSV export in invariant decimal format. A file is written only when Export is confirmed, and Undo does not remove it."),
                    ResultsExportKind.Figures => ("FQ15-9D6165ED150A", "Screen figures (PNG)", "FQ15-2C55FFA436E6", "Select the stored result's run, peptide and family graphs for PNG export, with standard figures at 1920 by 1080 pixels in the Light palette and complete data beside bounded figures. Export writes the chosen folder's files, and Undo does not remove them."),
                    _ => ("FQ15-3CDBA38C569B", "Presentation evidence pack", "FQ15-9F0785257B19", "Select the complete figure set, exact CSV and JSON numbers and the cutoff README for an evidence-pack export. Export writes files only when the required computed studies are present, and Undo does not remove them.") };
                var selected = (ComboBoxItem)exports.SelectedItem;
                Prove(copy.Item1, "export label retained as " + copy.Item2, (string?)selected.Content == copy.Item2,
                    "ComboBoxExportKind / " + item.Item1 + " / Content");
                Tooltip(copy.Item3, selected, copy.Item4, "ComboBoxExportKind / " + item.Item1 + " / ToolTip");
                Prove(item.Item2, item.Item1 + " export remains selectable and enabled", model.ExportKind == item.Item1 && button.IsVisible && button.IsEnabled,
                    "ComboBoxExportKind / ButtonExport / ExportChoiceChanged");
                var routes = new List<string>();
                string destination = "";
                ShellDialogs.FilePresenter = (_, id, dialog) => { routes.Add(id); dialog.FileName = destination; return true; };
                ShellDialogs.FolderPresenter = (_, id, dialog) => { routes.Add(id); dialog.FolderName = destination; return true; };
                for (int repeat = 1; repeat <= 2; repeat++)
                {
                    destination = Path.Combine(output, item.Item1 + "-" + repeat);
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FinishBusyOperation(host);
                }
                bool nativeRoute = routes.SequenceEqual(Enumerable.Repeat("results." + item.Item1, 2));
                Console.WriteLine(item.Item1 + " export outcome: " + model.Status);
                string first = Path.Combine(output, item.Item1 + "-1"), second = Path.Combine(output, item.Item1 + "-2");
                bool completed;
                if (item.Item1 == ResultsExportKind.EvidencePack)
                    completed = !Directory.Exists(first) && !Directory.Exists(second) && model.Status.StartsWith("The export did not finish.", StringComparison.Ordinal)
                        && model.Status.Contains("no stored de novo/search-engine overlap study", StringComparison.Ordinal);
                else if (item.Item1 == ResultsExportKind.Figures)
                {
                    string[] expected = { "family_contribution.png", "isotopes_credited.png", "percent_ion_current_by_mass.png", "recovery.png", "xic_by_mass.png" };
                    completed = Directory.GetFiles(first).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(expected)
                        && expected.All(name => File.ReadAllBytes(Path.Combine(first, name)).SequenceEqual(File.ReadAllBytes(Path.Combine(second, name)))
                            && PngSize(Path.Combine(first, name)) == (1920, 1080));
                }
                else completed = File.Exists(first) && new FileInfo(first).Length > 100
                    && File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second));
                string behavior = item.Item1 == ResultsExportKind.EvidencePack
                    ? "native evidence-pack route refuses missing required studies and publishes no folder"
                    : "native " + item.Item1 + " export writes byte-identical output twice";
                Prove(item.Item3, behavior, nativeRoute && completed, "ExportClicked / AnalyticsViewModel.ExportAsync");
                Prove(item.Item4, behavior + " through the replacement handler", nativeRoute && completed,
                    "ExportClicked / AnalyticsViewModel.ExportAsync");
            }
            foreach (var theme in new[] { Themes.AppTheme.Light, Themes.AppTheme.Dark })
            {
                Themes.ThemeManager.Apply(theme); PumpDispatcher();
                ScreenHarness.Save(window, Path.Combine(output, "results-" + theme + ".png"), 1180, 800);
            }
        }
        finally
        {
            window.Close(); PumpDispatcher();
            ShellDialogs.FilePresenter = previousFile; ShellDialogs.FolderPresenter = previousFolder;
            Themes.ThemeManager.Apply(previousTheme);
            System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        return cases;

        static (int, int) PngSize(string path)
        {
            using var stream = File.OpenRead(path);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            return (frame.PixelWidth, frame.PixelHeight);
        }
    }
}
