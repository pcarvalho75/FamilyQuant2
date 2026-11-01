using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using Microsoft.Win32;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

using System.Windows.Data;

namespace FamilyQuant;

public partial class DeNovoControl
{
    public const string CacheFolderName = DeNovoScreenOperations.CacheFolderName;
    public static Func<string, DeNovoFilterCriteria, Task<DeNovoImport>>? ImportOverride;
    public static Func<string, string>? CachePathForRunOverride
    { get => DeNovoScreenOperations.CachePathForRunOverride; set => DeNovoScreenOperations.CachePathForRunOverride = value; }
    public DeNovoFilterCriteria Criteria => ViewModel.Criteria;
    public HomologyOptions HomologySettings => ViewModel.HomologySettings;
    public DeNovoSource ActiveDeNovoSource => ViewModel.ActiveDeNovoSource;
    public DeNovoImport? LastImport => ViewModel.LastImport;
    public DeNovoReconciliation? LastReconciliation => ViewModel.LastReconciliation;
    public DeNovoXicExtraction? LastExtraction => ViewModel.LastExtraction;
    public DeNovoHomology? LastHomology => ViewModel.LastHomology;
    public DeNovoPartition? LastPartition => ViewModel.LastPartition;
    public IReadOnlyList<PeaksDeNovoRow> FilteredRows => ViewModel.FilteredRows;
    public IReadOnlyList<PatternLabPsm> Psms => ViewModel.Psms;
    public IReadOnlyList<ReconciledRow> Reconciled => ViewModel.Reconciled;
    public IReadOnlyList<ReviewQueueRow> ReviewQueue => ViewModel.ReviewQueue;
    public IReadOnlyList<DeNovoRunRawMatch> RawMatches => ViewModel.RawMatches;
    public void ApplyImport(DeNovoImport import) => ViewModel.ApplyImport(import);
    public void ApplyReconciliation(DeNovoReconciliation outcome) => ViewModel.ApplyReconciliation(outcome);
    public void ApplyExtraction(DeNovoXicExtraction outcome) => ViewModel.ApplyExtraction(outcome);
    public void ApplyHomology(DeNovoHomology outcome) => ViewModel.ApplyHomology(outcome);
    public void ApplyPartition(DeNovoPartition outcome) => ViewModel.ApplyPartition(outcome);
    public void ExportSummaryCsv(string path) => ViewModel.ExportSummaryCsv(path);
    public void ExportReviewQueueCsv(string path) => ViewModel.ExportReviewQueueCsv(path);
    public void ExportOverlapStudyCsv(string path) => ViewModel.ExportOverlapStudyCsv(path);
    public void ExportHomologyCsv(string path) => ViewModel.ExportHomologyCsv(path);
    public void SavePartitionCsv(string path) => ViewModel.SavePartitionCsv(path);
    public bool ReadCriteriaFromBoxes() => ViewModel.ReadCriteriaFromBoxes();
    public bool ReadHomologySettingsFromBoxes() => ViewModel.ReadHomologySettingsFromBoxes();
    public List<string> KeptRunNames() => ViewModel.KeptRunNames();
    public Dictionary<string, List<PeaksDeNovoRow>> KeptRowsByRun() => ViewModel.KeptRowsByRun();
    public List<string> PeptidesForHomology() => ViewModel.PeptidesForHomology();
        public static DeNovoImport ImportAndFilter(string directory, DeNovoFilterCriteria criteria,
                                                   DeNovoSource? source = null) =>
            DeNovoScreenOperations.ImportAndFilter(directory, criteria, source);

        public static DeNovoReconciliation Reconcile(IReadOnlyList<PeaksDeNovoRow> filtered, string sepr2Path,
                                                      int? rawDeNovoRows = null,
                                                      DeNovoFilterCriteria? criteria = null) =>
            DeNovoScreenOperations.Reconcile(filtered, sepr2Path, rawDeNovoRows, criteria);

        public static DeNovoReconciliation Reconcile(IReadOnlyList<PeaksDeNovoRow> filtered,
                                                     List<PatternLabPsm> psms, string source,
                                                     int? rawDeNovoRows = null,
                                                     DeNovoFilterCriteria? criteria = null) =>
            DeNovoScreenOperations.Reconcile(filtered, psms, source, rawDeNovoRows, criteria);

        public static string CachePathFor(string runName, string rawDirectory) =>
            DeNovoScreenOperations.CachePathFor(runName, rawDirectory);

        public static List<DeNovoRunRawMatch> MatchRunsToRaw(IEnumerable<string> runs, string rawDirectory) =>
            DeNovoScreenOperations.MatchRunsToRaw(runs, rawDirectory);

        public static DeNovoXicExtraction ExtractXic(IReadOnlyList<DeNovoRunRawMatch> matches,
                                                     IReadOnlyDictionary<string, List<PeaksDeNovoRow>> keptByRun,
                                                     string rawDirectory,
                                                     Action<string>? progress) =>
            DeNovoScreenOperations.ExtractXic(matches, keptByRun, rawDirectory, progress);

        public static DeNovoHomology SearchHomology(IReadOnlyList<string> peptides, FamilyClusterFile fcf,
                                                    HomologyOptions options, string fcfPath) =>
            DeNovoScreenOperations.SearchHomology(peptides, fcf, options, fcfPath);

        public static DeNovoPartition BuildPartition(IReadOnlyList<DeNovoRunRawMatch> matches,
                                                     IReadOnlyDictionary<string, DeNovoBatchStats> xicByRun,
                                                     string? plpPath, string? xic2Path,
                                                     Action<string>? progress) =>
            DeNovoScreenOperations.BuildPartition(matches, xicByRun, plpPath, xic2Path, progress);
}
