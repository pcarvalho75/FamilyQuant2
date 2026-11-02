using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DatabaseGapTests(string output)
    {
        Section("Database gap - stored Core composition and suggested additions");
        Check("Results supplies the Database gap presentation", typeof(App).Assembly.GetType("FamilyQuant.DatabaseGapControl") != null);
        Check("Results holds the stored Database gap table", typeof(AnalyticsViewModel).GetProperty("DatabaseGap") != null);
        Check("the composition CSV has a dedicated export choice", Enum.GetNames<ResultsExportKind>().Contains("DatabaseGap"));
        Check("suggested FASTA additions have a dedicated export choice", Enum.GetNames<ResultsExportKind>().Contains("SuggestedAdditions"));
        Check("Database gap has specific Help for its stored rules", typeof(App).Assembly.GetType("FamilyQuant.DatabaseGapHelp") != null);
        string help = (string)typeof(App).Assembly.GetType("FamilyQuant.DatabaseGapHelp")!.GetField("Text")!.GetValue(null)!;
        Check("Database gap Help defines right per family even with additional credited families", help.Contains(
            "A credit counts as right for a family when that family is among the known answers, even if the peptide was also credited to another family.", StringComparison.Ordinal));
        EnsureApplication(); Directory.CreateDirectory(output);
        var missing = new DatabaseGapViewModel(null);
        var legacy = new DatabaseGapViewModel(new FamilyWorldSummary());
        Check("missing and legacy tables explain absence without inventing zero family parts", !missing.HasData && !legacy.HasData
            && missing.Rows.Count == 0 && legacy.EmptyMessage.Contains("not shown as zero", StringComparison.Ordinal));
        var file = TaskFile();
        using var taxonomy = new TaskTaxonomyHandler();
        var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
        var task = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
        using var fetch = new WorldFetchHandler();
        var fetched = CoreOperation.RunAsync("Synthetic stored world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, fetch))
            .GetAwaiter().GetResult();
        if (fetched.Status != OperationStatus.Succeeded) throw new InvalidOperationException("Synthetic stored-world fetch failed: " + fetched.Status);
        var stored = FamilyWorldFetcher.Stored(file);
        foreach (var entry in stored.Entries) entry.Sequence = string.Concat(Enumerable.Repeat("ACDEFGHIKLMNPQRSTVWY", 7));
        stored.ContentSha256 = FamilyWorldFetcher.ContentHash(stored);
        var world = FamilyWorld.FromSnapshot(stored);
        var summary = new FamilyWorldSummary { CompositionRules = FamilyWorldErrors.CompositionRule, MinimumTagLength = 9,
            MinIdentity = .8, MinimumKnownCredits = 10, ScopeFamilies = stored.Families.ToList(), WorldFingerprint = world.Fingerprint,
            WorldRelease = stored.Release, MeasuredIdentifiedEnvelope = 1000, MeasuredDeNovoEnvelope = 500,
            SharedIdentifiedEnvelope = 10, SharedDatabaseDeNovoEnvelope = 20, SharedEnvelope = 30,
            UnattributedIdentifiedEnvelope = 40, UnexplainedEnvelope = 50,
            Rules = FamilyWorldPass.RuleText(new FamilyWorldPassResult { MinimumTagLength = 9, MinIdentity = .8,
                CreditableFamilies = stored.Families.ToArray() }) };
        var first = new FamilyWorldCompositionRow { Family = stored.Families[0], IdentifiedEnvelope = 125.25,
            DatabaseDeNovoEnvelope = 30.125, WorldDeNovoEnvelope = 200.5, WorldKnownCredited = 12, WorldKnownCreditedTrue = 11,
            WorldRightRate = 11d / 12, WorldDecoyRate = .000008, WorldEvidence = "measured" };
        var low = new FamilyWorldCompositionRow { Family = "Low-evidence synthetic row", WorldDeNovoEnvelope = 50.25,
            WorldKnownCredited = 9, WorldKnownCreditedTrue = 8, WorldRightRate = null, WorldEvidence = "too little evidence: 9 known credits" };
        summary.Composition.AddRange(new[] { low, first, new FamilyWorldCompositionRow { Family = "Curated synthetic row",
            Kind = FamilyWorldCompositionRow.CuratedKind, IdentifiedEnvelope = 75, CuratedUserIds = new() { 8 } } });
        // This is a presentation fixture, with an explicitly closing synthetic measured basis.
        summary.MeasuredIdentifiedEnvelope = summary.Composition.Sum(r => r.IdentifiedEnvelope)
            + summary.SharedIdentifiedEnvelope + summary.UnattributedIdentifiedEnvelope;
        summary.MeasuredDeNovoEnvelope = summary.Composition.Sum(r => r.DatabaseDeNovoEnvelope + r.WorldDeNovoEnvelope)
            + summary.SharedDatabaseDeNovoEnvelope + summary.SharedEnvelope + summary.UnexplainedEnvelope;
        summary.Families.Add(new FamilyWorldFamilySummary { Family = stored.Families[0], WorldProteins = stored.Entries.Select(e => e.Accession).ToList() });
        string source = file.ToJsonText(), table = JsonSerializer.Serialize(summary);
        var model = new DatabaseGapViewModel(summary, file);
        Check("ranking sorts the same Core row objects by database gap without allocating new amounts", model.ChartRows[0] == first
            && model.Rows[0].Data == first && model.ChartRows[1] == low);
        Check("global buckets use all five supplied Core values and unexplained stays apart", model.GlobalRows.Select(r => r.Envelope)
            .SequenceEqual(new[] { 50d, 10, 20, 30, 40 }) && model.GlobalRows[0].Label == "Global unexplained de novo"
            && typeof(FamilyWorldCompositionRow).GetProperty("UnexplainedEnvelope") == null);
        Check("known-answer rate keeps its supplied counts and hides rates below the stored evidence floor",
            model.Rows[0].RightRate.Contains("11/12", StringComparison.Ordinal) && model.Rows[1].RightRate.Contains("8/9", StringComparison.Ordinal)
            && model.Rows[1].RightRate.Contains("too little evidence", StringComparison.Ordinal) && !model.Rows[1].RightRate.Contains("99", StringComparison.Ordinal));
        Check("CSV is exactly the Core composition writer including all global rows and rules", model.BuildCsv() == summary.BuildCompositionCsv()
            && model.BuildCsv().Contains("tags of at least 9", StringComparison.Ordinal) && model.BuildCsv().Contains("only families", StringComparison.Ordinal));
        Check("matching verified stored world enables suggested additions", model.CanExportFasta && model.FastaProblem.Length == 0);
        string fasta = model.BuildSuggestedFasta();
        var records = fasta.Split('>').Skip(1).Select(record => record.Split('\n'))
            .ToDictionary(lines => lines[0].Split(' ')[0], lines => string.Concat(lines.Skip(1)), StringComparer.Ordinal);
        Check("FASTA contains exact stored hit accessions and sequences once in deterministic order", fasta.Split('\n').Count(line => line.StartsWith('>')) == 2
            && stored.Entries.All(e => records.TryGetValue(e.Accession, out string? sequence) && sequence == e.Sequence)
            && fasta == model.BuildSuggestedFasta());
        string project = Path.Combine(output, "stored-fixture.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest { FamilyWorldResult = summary });
        var reopened = ProjectStore.ReadFqproj(project);
        var offline = new DatabaseGapViewModel(reopened.manifest.FamilyWorldResult, reopened.fcf);
        Check("offline reopen regenerates identical Core CSV and suggested FASTA", offline.BuildCsv() == model.BuildCsv()
            && offline.BuildSuggestedFasta() == fasta);
        var changed = JsonSerializer.Deserialize<FamilyWorldSummary>(table)!; changed.WorldFingerprint = "mismatch";
        Check("mismatched world cannot substitute a sequence or export a partial FASTA", !new DatabaseGapViewModel(changed, file).CanExportFasta);
        var absent = JsonSerializer.Deserialize<FamilyWorldSummary>(table)!; absent.Families[0].WorldProteins.Add("not-a-stored-accession");
        Check("one missing hit refuses the entire suggested FASTA", !new DatabaseGapViewModel(absent, file).CanExportFasta);
        Check("a table without world sequences keeps its CSV and refuses FASTA", new DatabaseGapViewModel(summary).HasData
            && !new DatabaseGapViewModel(summary).CanExportFasta);
        var priorCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var otherCulture = new DatabaseGapViewModel(summary, file);
            Check("CSV and FASTA bytes do not change with display culture", otherCulture.BuildCsv() == model.BuildCsv()
                && otherCulture.BuildSuggestedFasta() == fasta);
        }
        finally { CultureInfo.CurrentCulture = priorCulture; }
        var exportModel = new AnalyticsViewModel(Array.Empty<QuantRow>(), new QuantManifest { FamilyWorldResult = summary }, file);
        Check("a stored composition without XIC rows opens on its available CSV export", exportModel.ExportKind == ResultsExportKind.DatabaseGap
            && exportModel.CanExport);
        foreach (var kind in new[] { ResultsExportKind.DatabaseGap, ResultsExportKind.SuggestedAdditions })
        {
            exportModel.ExportKind = kind;
            string a = Path.Combine(output, kind + "-1"), b = Path.Combine(output, kind + "-2");
            exportModel.Export(a); exportModel.Export(b);
            Check(kind + " export reads the same stored table even without XIC rows and repeats exact bytes", exportModel.CanExport
                && File.ReadAllBytes(a).SequenceEqual(File.ReadAllBytes(b))
                && File.ReadAllText(a) == (kind == ResultsExportKind.DatabaseGap ? summary.BuildCompositionCsv() : fasta));
        }
        Check("presentation and export leave both project evidence and the stored table unchanged", source == file.ToJsonText()
            && table == JsonSerializer.Serialize(summary));
        var previousContext = System.Threading.SynchronizationContext.Current;
        var previousPresenter = ShellDialogs.FilePresenter;
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext());
        var results = new AnalyticsControl(exportModel);
        var resultsWindow = new Window { Content = results, Width = 1180, Height = 1000, Left = -30000, Top = -30000,
            ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            resultsWindow.Show(); PumpDispatcher();
            var host = AppOperationHost.For(results); resultsWindow.UpdateLayout(); PumpDispatcher();
            var choice = (ComboBox)results.FindName("ComboBoxExportKind");
            var button = (Button)results.FindName("ButtonExport");
            Check("native Results hosts the supplied gap model and hides the missing-XIC instruction", ((ContentControl)results.FindName("DatabaseGapHost")).Content
                is DatabaseGapControl gap && ReferenceEquals(gap.ViewModel, exportModel.DatabaseGap)
                && ((System.Windows.FrameworkElement)results.FindName("BorderEmpty")).Visibility == Visibility.Collapsed);
            foreach (var kind in new[] { ResultsExportKind.DatabaseGap, ResultsExportKind.SuggestedAdditions })
            {
                string target = Path.Combine(output, "native-" + kind + (kind == ResultsExportKind.DatabaseGap ? ".csv" : ".fasta"));
                string route = "", filter = "";
                ShellDialogs.FilePresenter = (_, id, dialog) => { route = id; filter = dialog.Filter; dialog.FileName = target; return true; };
                choice.SelectedItem = choice.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == kind.ToString()); PumpDispatcher();
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); FinishBusyOperation(host);
                Check("native " + kind + " selector and button write the supplied result through the correct file route",
                    route == "results." + kind && File.ReadAllText(target) == (kind == ResultsExportKind.DatabaseGap ? summary.BuildCompositionCsv() : fasta)
                    && filter.StartsWith(kind == ResultsExportKind.DatabaseGap ? "CSV" : "FASTA", StringComparison.Ordinal));
            }
        }
        finally { resultsWindow.Close(); PumpDispatcher(); ShellDialogs.FilePresenter = previousPresenter;
            System.Threading.SynchronizationContext.SetSynchronizationContext(previousContext); }
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var control = new DatabaseGapControl(model);
                var host = new Window { Content = control, Title = "Synthetic Database gap fixture", Width = 1024, Height = 1040,
                    Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
                    WindowStartupLocation = WindowStartupLocation.Manual };
                try
                {
                    host.Show(); host.UpdateLayout(); PumpDispatcher();
                    Check(theme + " native gap view shows its rule and all Core rows", ((TextBlock)control.FindName("TextBlockRules")).IsVisible
                        && ((TextBlock)control.FindName("TextBlockRules")).Text.Contains("9 residues", StringComparison.Ordinal)
                        && ((DataGrid)control.FindName("DataGridFamilies")).Items.Count == 3
                        && WalkTree(control).OfType<DatabaseGapChart>().Single().Rows.SequenceEqual(model.ChartRows));
                    System.Threading.Thread.Sleep(350); PumpDispatcher();
                    ScreenHarness.Save(host, Path.Combine(output, "database-gap-" + theme + ".png"), 1024, 1040);
                }
                finally { host.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
