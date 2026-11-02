using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

/// <summary>Real import, reconciliation, selection and review-table parity through the production
/// view model and Core. Reads SEPro/CSV inputs, but never extracts RAW or runs a talk producer.</summary>
internal static class DeNovoEngineSnapshot
{
    internal const string Argument = "--denovo-engine-snapshot";

    internal static int Run(string[] args)
    {
        if (args.Length != 5)
            throw new ArgumentException("Supply legacy PEAKS folder, full-source folder, SEPro package and empty output folder.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("The snapshot output folder must be empty: " + output);
        Directory.CreateDirectory(output);
        List<PatternLabPsm> psms = SeproPsmAdapter.Load(args[3]);
        foreach (var item in new[]
        {
            (Mode: "peaks-only", Root: args[1]),
            (Mode: "peaks-all", Root: args[2]),
            (Mode: "deepnovo", Root: args[2]),
            (Mode: "union-all-deepnovo", Root: args[2]),
        })
        {
            string folder = Path.Combine(output, item.Mode);
            Directory.CreateDirectory(folder);
            var filters = new DeNovoFilterViewModel { ModeTag = item.Mode };
            if (!filters.TryBuild(out var criteria, out var selections, out string error))
                throw new InvalidDataException(error);
            DeNovoImport import = DeNovoSourceImport.Run(item.Root, criteria, selections);
            var model = new DeNovoViewModel(filters);
            model.ApplyImport(import);
            List<string> beforeReconciliation = model.PeptidesForHomology();
            DeNovoReconciliation reconciliation = DeNovoInspectionAudit.AttachSources(
                DeNovoScreenOperations.Reconcile(import.Kept, psms, args[3], import.RowsRead, criteria), import);
            model.ApplyReconciliation(reconciliation);
            model.ExportSummaryCsv(Path.Combine(folder, "summary.csv"));
            model.ExportReviewQueueCsv(Path.Combine(folder, "review.csv"));
            model.ExportOverlapStudyCsv(Path.Combine(folder, "overlap.csv"));
            var data = new
            {
                item.Mode,
                Criteria = criteria,
                Selections = selections,
                import.FilesFound,
                import.RowsRead,
                import.RowsKept,
                import.PerRun,
                Imported = import.Kept,
                reconciliation.Counts,
                reconciliation.Rows,
                reconciliation.ReviewQueue,
                reconciliation.SourceReviewQueue,
                reconciliation.Overlap,
                QueriesBefore = beforeReconciliation,
                QueriesAfter = model.PeptidesForHomology(),
                KeptByRun = model.KeptRowsByRun().OrderBy(p => p.Key, StringComparer.Ordinal)
                    .Select(p => new { Run = p.Key, Rows = p.Value }).ToArray(),
                Runs = model.KeptRunNames(),
                Matches = model.RawMatches,
                model.Status,
                model.PerRunBreakdown,
                model.RawMatchPreview,
                model.LabelRowsRead,
                model.LabelRowsKept,
                model.LabelReviewQueueCount,
                model.LabelOverlapScan,
                model.LabelOverlapPrecursor,
                model.LabelOverlapExact,
                model.LabelOverlapDivergent,
                model.CanReconcile,
                model.CanSearch,
                model.CanExtract,
            };
            File.WriteAllText(Path.Combine(folder, "inspection.json"), JsonSerializer.Serialize(data), new UTF8Encoding(false));
            Console.WriteLine(item.Mode + ": read " + import.RowsRead.ToString(CultureInfo.InvariantCulture)
                + ", kept " + import.RowsKept.ToString(CultureInfo.InvariantCulture)
                + ", review " + model.ReviewQueue.Count.ToString(CultureInfo.InvariantCulture)
                + ", queries " + model.PeptidesForHomology().Count.ToString(CultureInfo.InvariantCulture));
        }
        Console.WriteLine("De novo snapshot: " + output);
        return 0;
    }
}
