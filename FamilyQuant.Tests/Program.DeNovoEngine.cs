using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DeNovoEngineTidyTests()
    {
        Check("de novo retained-run and homology selection belongs to Core",
            typeof(DeNovoReconciler).Assembly.GetType("FamilyQuant.DeNovo.DeNovoInspectionSelection") != null);
        var rows = new List<ReconciledRow>();
        ReconciledRow Row(string run, int scan, string peptide, ReconciliationVerdict verdict)
        {
            bool deep = scan == 5;
            var dn = new PeaksDeNovoRow
            {
                RunName = run, Scan = scan, Peptide = peptide, Length = 8, Charge = 2, Mz = 500,
                Alc = 85, LocalConfidence = scan == 4 ? Array.Empty<int>() : new[] { 99, 95 },
                SourceId = deep ? DeNovoSource.DeepNovo.Id : DeNovoSource.DeNovoAll.Id,
                ConfidenceField = deep ? "SCORE (%)" : "ALC (%)",
            };
            PatternLabPsm? psm = verdict == ReconciliationVerdict.DeNovoOnly ? null : new()
            {
                FileName = run, ScanNumber = scan, ChargeState = 2,
                PrecursorMz = verdict == ReconciliationVerdict.CoIsolatedExtra ? 600 : 500.005,
                Sequence = verdict == ReconciliationVerdict.Conflict ? "MMMMMMMK" : peptide,
                XCorr = verdict == ReconciliationVerdict.EqualButWeakPsm ? 1 : 4,
            };
            return new ReconciledRow { DeNovo = dn, Verdict = verdict, CounterpartPsm = psm,
                ScanCounterpartPsm = psm, AnyChargeScanCounterpartPsm = psm };
        }
        rows.Add(Row("Z.raw.sqt", 1, "IPEPTIDEK", ReconciliationVerdict.DeNovoOnly));
        rows.Add(Row("a.RAW", 2, "C(+57.02)EPTIDEK", ReconciliationVerdict.CoIsolatedExtra));
        rows.Add(Row("r", 3, "VVVVVVVK", ReconciliationVerdict.RedundantEqual));
        rows.Add(Row("r", 4, "MMMMMMMK", ReconciliationVerdict.EqualButWeakPsm));
        rows.Add(Row("r", 5, "GGGGGGGK", ReconciliationVerdict.Conflict));
        rows.Add(Row("z.raw", 6, "LPEPTLDEK", ReconciliationVerdict.CoIsolatedExtra));
        rows.Add(Row("", 7, "AAAAAAAK", ReconciliationVerdict.DeNovoOnly));
        rows.Add(Row("b.raw", 8, "C(+57.02", ReconciliationVerdict.DeNovoOnly));
        var result = new DeNovoReconciliation { Rows = rows };
        var filtered = rows.Select(r => r.DeNovo).ToList();
        string before = JsonSerializer.Serialize(rows);
        var byRun = DeNovoInspectionSelection.KeptRowsByRun(result);
        Check("retained runs normalize stacked extensions and merge case without dropping row order",
            byRun.Count == 3 && byRun["z"].Select(r => r.Scan).SequenceEqual(new[] { 1, 6 })
            && ReferenceEquals(byRun["Z"][0], rows[0].DeNovo));
        Check("only additive verdicts enter extraction, while an empty run is not invented",
            byRun.Values.SelectMany(r => r).Select(r => r.Scan).OrderBy(i => i).SequenceEqual(new[] { 1, 2, 6, 8 }));
        Check("kept run names are deterministic and case-insensitive",
            DeNovoInspectionSelection.KeptRunNames(result).SequenceEqual(new[] { "a", "b", "Z" }));
        Check("homology queries remove PTM notation, fold I/L, deduplicate and sort ordinally",
            DeNovoInspectionSelection.PeptidesForHomology(filtered, result)
                .SequenceEqual(new[] { "AAAAAAAK", "CEPTLDEK", "LPEPTLDEK" }));
        Check("no reconciliation uses filtered rows but an empty completed reconciliation does not fall back",
            DeNovoInspectionSelection.PeptidesForHomology(filtered, null).Count == 6
            && DeNovoInspectionSelection.PeptidesForHomology(filtered, new DeNovoReconciliation()).Count == 0
            && DeNovoInspectionSelection.KeptRowsByRun(null).Count == 0);
        var legacy = DeNovoReviewTableBuilder.Build(rows);
        var source = DeNovoReviewTableBuilder.BuildWithSources(rows);
        Check("both Core review tables contain only weak equalities and comparable alternatives",
            legacy.Select(r => r.Scan).SequenceEqual(new[] { 4, 5 })
            && source.Select(r => r.Scan).SequenceEqual(new[] { 4, 5 }));
        Check("review table keeps missing local confidence distinct and retains exact precursor metadata",
            legacy[0].MinResidueConfidence == 0 && legacy[1].MinResidueConfidence == 95
            && legacy[1].PpmApart > 9.999 && legacy[1].PpmApart < 10.001
            && legacy[1].VerdictLabel == "OverlappingAlternativeSequence");
        Check("source review keeps native source names and score fields without score conversion",
            source[0].SourceId == DeNovoSource.DeNovoAll.Id && source[0].ConfidenceField == "ALC (%)"
            && source[1].Source == DeNovoSource.DeepNovo.Id && source[1].ConfidenceField == "SCORE (%)"
            && source[1].Alc == 85);
        Check("the legacy report facade returns the exact Core table rows",
            JsonSerializer.Serialize(legacy) == JsonSerializer.Serialize(DeNovoReconciliationReport.BuildReviewQueue(rows)));
        Check("selection and table projection do not mutate source rows",
            before == JsonSerializer.Serialize(rows));
        var import = new DeNovoImport
        {
            SourceId = "peaks-denovo-all+deepnovo", Kept = filtered, RowsRead = filtered.Count,
            Selections = new[] { new DeNovoSourceSelection(DeNovoSource.DeNovoAll, 80),
                new DeNovoSourceSelection(DeNovoSource.DeepNovo, 80, 1) },
            AppliedCriteria = new DeNovoFilterCriteria(),
        };
        var psms = rows.Where(r => r.CounterpartPsm != null).Select(r => r.CounterpartPsm!).ToList();
        var composed = DeNovoInspectionAudit.AttachSources(DeNovoScreenOperations.Reconcile(
            filtered, psms, "fixture", filtered.Count, import.AppliedCriteria), import);
        var workflow = DeNovoScreenOperations.ReconcileForInspection(
            filtered, psms, "fixture", filtered.Count, import.AppliedCriteria, import);
        Check("the Core inspection workflow preserves decision rows and source-aware table projection",
            JsonSerializer.Serialize(composed) == JsonSerializer.Serialize(workflow));
        var model = new DeNovoViewModel();
        model.ApplyImport(import);
        model.ApplyReconciliation(workflow);
        Check("the production session uses the same Core selections and review rows",
            model.PeptidesForHomology().SequenceEqual(DeNovoInspectionSelection.PeptidesForHomology(filtered, workflow))
            && model.KeptRunNames().SequenceEqual(DeNovoInspectionSelection.KeptRunNames(workflow))
            && ReferenceEquals(model.ReviewQueueItems, workflow.SourceReviewQueue));
    }
}
