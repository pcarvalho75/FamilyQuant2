using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DeNovoViewModelTests(string workDir)
    {
        Section("De novo shared filters and source-native inspection");
        var filters = new DeNovoFilterViewModel { MinimumLength = "9", StretchLength = "6", StretchFloor = "95.5",
            PeaksMinimumAlc = "81.5", DeepNovoMinimumScore = "72.25" };
        var compute = new ComputeXicViewModel("fixture", filters: filters);
        var inspection = new DeNovoViewModel(filters);
        Check("Quantify and De novo hold the same filter model instance",
            ReferenceEquals(compute.Filters, filters) && ReferenceEquals(inspection.Filters, filters));
        foreach (string mode in new[] { "peaks-only", "peaks-all", "deepnovo", "union-only-deepnovo", "union-all-deepnovo" })
        {
            filters.ModeTag = mode;
            bool built = compute.Filters.TryBuild(out var expected, out var sources, out _);
            bool read = inspection.ReadCriteriaFromBoxes();
            Check("both screens use identical criteria for " + mode, built && read
                && inspection.Criteria.MinLength == expected.MinLength
                && inspection.Criteria.StretchLength == expected.StretchLength
                && inspection.Criteria.StretchFloor == expected.StretchFloor
                && inspection.Criteria.MinAverageLocalConfidence == expected.MinAverageLocalConfidence
                && sources.All(s => s.MinimumConfidence == (s.Source == DeNovoSource.DeepNovo ? 72.25 : 81.5)));
        }
        inspection.ApplyImport(new DeNovoImport { Kept = new() { new() { Peptide = "PEPTIDEK" } } });
        filters.ModeTag = "peaks-only";
        Check("a source changed through shared settings invalidates the previous inspection", inspection.LastImport == null);
        Check("headless inspection workflows and their DTOs live in Core",
            typeof(DeNovoImport).Assembly == typeof(DeNovoScreenOperations).Assembly
            && typeof(DeNovoScreenOperations).Assembly != typeof(DeNovoControl).Assembly);

        string root = Path.Combine(workDir, "shared-source-inspection");
        Directory.CreateDirectory(root);
        string row = "101,PEPTIDEK,{0},8,500.25000,2,12.5,1000000,99 99 99 99 99 99 99 99\n";
        File.WriteAllText(Path.Combine(root, "shared.raw.denovoOnly.csv"),
            "Scan,Peptide,ALC (%),Length,m/z,z,RT,Area,local confidence (%)\n" + string.Format(row, 85));
        File.WriteAllText(Path.Combine(root, "shared.raw.deepnovo.csv"),
            "Scan,Peptide,SCORE (%),Length,m/z,z,RT,Area,local confidence (%)\n" + string.Format(row, 77));
        var criteria = new DeNovoFilterCriteria();
        var selections = new[] { new DeNovoSourceSelection(DeNovoSource.DenovoOnly, 80, 0),
            new DeNovoSourceSelection(DeNovoSource.DeepNovo, 75, 1) };
        var imported = DeNovoSourceImport.Run(root, criteria, selections);
        var unionModel = new DeNovoViewModel(new DeNovoFilterViewModel { ModeTag = "union-only-deepnovo" }) { ImportDirectory = root };
        unionModel.SourcePathChanged();
        Check("union file preview counts every selected source", unionModel.LabelFilesFound?.ToString() == "Files found: 2");
        Check("union inspection applies separate native cutoffs and retains both source identities",
            imported.RowsRead == 2 && imported.RowsKept == 2 && imported.Kept.Select(r => r.SourceId).Distinct().Count() == 2);
        var stringent = DeNovoSourceImport.Run(root, criteria, new[] { selections[0], new DeNovoSourceSelection(DeNovoSource.DeepNovo, 78, 1) });
        Check("raising DeepNovo SCORE leaves the PEAKS ALC call untouched",
            stringent.RowsKept == 1 && stringent.Kept[0].SourceId == DeNovoSource.DenovoOnly.Id);
        var deepOnly = DeNovoSourceImport.Run(root, criteria, new[] { selections[1] });
        Check("one-source inspection also uses its explicit native cutoff", deepOnly.RowsKept == 1
            && deepOnly.AppliedCriteria?.MinAverageLocalConfidence == 75 && deepOnly.Selections[0].MinimumConfidence == 75);
        var psms = new List<PatternLabPsm> { new() { FileName = "shared.raw", ScanNumber = 101,
            PrecursorMz = 500.25, ChargeState = 2, Sequence = "AAAAAAAA", XCorr = 4 } };
        var reconciled = DeNovoInspectionAudit.AttachSources(
            DeNovoScreenOperations.Reconcile(imported.Kept, psms, "fixture", imported.RowsRead, criteria), imported);
        Check("source audit adds provenance without changing reconciliation counts",
            reconciled.SourceReviewQueue?.Count == reconciled.ReviewQueue.Count
            && reconciled.SourceReviewQueue?.Select(q => q.ConfidenceField).Distinct().Count() == 2
            && reconciled.Counts.Values.Sum() == 2);
        string csv = Path.Combine(root, "source-review.csv");
        DeNovoInspectionAudit.WriteReviewQueueCsv(csv, reconciled.SourceReviewQueue!);
        string written = File.ReadAllText(csv);
        Check("native review CSV names the program and confidence scale rather than calling SCORE ALC",
            written.StartsWith("Run,Scan,Verdict,DeNovoSequence,NativeConfidence,")
            && written.Contains("deepnovo,SCORE (%)") && written.Contains("peaks-denovo-only,ALC (%)"));
        Check("overlap metadata retains the actual source-specific cutoffs",
            reconciled.Overlap?.SourceFilterDescription.Contains("ALC (%) >= 80") == true
            && reconciled.Overlap.SourceFilterDescription.Contains("SCORE (%) >= 75"));
        SharedFilterHostTests(workDir);
    }

    private static void SharedFilterHostTests(string workDir)
    {
        EnsureApplication();
        string path = Path.Combine(workDir, "filter-host.fcf");
        new FamilyClusterFile().Save(path);
        string? oldSettings = MainWindow.ShellSettingsPathOverride;
        var oldCompute = MainWindow.ComputeDialogOverride;
        MainWindow.ShellSettingsPathOverride = Path.Combine(workDir, "filter-host-settings.json");
        DeNovoFilterViewModel? selected = null;
        MainWindow.ComputeDialogOverride = window =>
        {
            selected = window.ViewModel.Filters;
            selected.ModeTag = "union-all-deepnovo";
            selected.PeaksMinimumAlc = "83.25";
            selected.DeepNovoMinimumScore = "71.5";
            selected.MinimumLength = "9";
            return false;
        };
        var main = new MainWindow();
        try
        {
            bool opened = main.OpenProjectPath(path);
            typeof(MainWindow).GetMethod("PresentCompute", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            typeof(MainWindow).GetMethod("PresentDeNovo", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(main, null);
            var deNovo = UiField<DeNovoControl>(main, "dnc")!;
            Check("shell navigation passes the identical Quantify filter state to De novo", opened
                && selected != null && ReferenceEquals(selected, deNovo.ViewModel.Filters)
                && deNovo.ReadCriteriaFromBoxes() && deNovo.Criteria.MinLength == 9
                && deNovo.ViewModel.Filters.ModeTag == "union-all-deepnovo"
                && deNovo.ViewModel.Filters.DeepNovoMinimumScore == "71.5");
        }
        finally { main.Close(); MainWindow.ComputeDialogOverride = oldCompute; MainWindow.ShellSettingsPathOverride = oldSettings; }
    }
}
