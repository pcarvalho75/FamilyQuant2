using LiteDB;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The PatternLab side of the three-way partition: how much extracted ion current each RUN's
    /// identified peptides account for, in absolute area on the M+0 base.
    ///
    /// WHY THERE ARE TWO SOURCES AND NOT ONE. The natural source is the .plp, because that is what
    /// the application quantifies from. It only answers this question when its matrix resolves per
    /// run and its values can be turned back into absolute areas - which needs a recorded, per-run,
    /// non-averaged divisor. A project that merged technical replicates has ONE column for three
    /// acquisitions and one averaged divisor, and no arithmetic recovers the three runs from that.
    /// The .xic2 is then the authority, because it still holds every run's quants separately.
    ///
    /// A refusal is a first-class answer here. <see cref="Refusal"/> says in words why a source
    /// cannot resolve per run, so the caller can record which branch it took instead of discovering
    /// later that it summed an average three times.
    ///
    /// RUNS COME BACK KEYED BY NAME. Never by package index and never by position: the package order
    /// inside this project's .xic2 is permuted against the RAW file names (package 1 is run _02),
    /// so index pairing yields another run's numbers with nothing looking wrong.
    /// </summary>
    public sealed class PatternLabRunQuants
    {
        private static readonly Dictionary<string, double> Empty =
            new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        private PatternLabRunQuants(string source, Dictionary<string, double> areaByRun,
                                    Dictionary<string, double> storedTicByRun, string? refusal)
        {
            Source = source;
            AreaByRun = areaByRun;
            StoredTicByRun = storedTicByRun;
            Refusal = refusal;
        }

        /// <summary>The provenance token to stamp on the values, one of the ThreeWayPartition
        /// Source* constants.</summary>
        public string Source { get; }

        /// <summary>Summed quant area per normalized run name. Empty when <see cref="Refusal"/> is set.</summary>
        public IReadOnlyDictionary<string, double> AreaByRun { get; }

        /// <summary>
        /// The divisor the exporter stored for each run, when the source carries one. This is NOT the
        /// partition's TIC: it is a THIRD source for the same quantity and exists only so a measured
        /// TIC can be checked against what the export recorded. The partition takes its denominator
        /// from the .plp record or from the RAW, never from here. And it is a DIVISOR, not certified
        /// ion current: the .xic2's NormalizationFactor carries no tag saying which normalization
        /// produced it, so on a project normalized by anything but TIC this holds that other divisor.
        /// The cross-check against a RAW-measured TIC is what earns it the name, per project.
        /// </summary>
        public IReadOnlyDictionary<string, double> StoredTicByRun { get; }

        /// <summary>Why this source cannot answer per run, or null when it can.</summary>
        public string? Refusal { get; }

        public bool IsAvailable => Refusal == null;

        private static PatternLabRunQuants No(string source, string why) =>
            new PatternLabRunQuants(source, Empty, Empty, why);

        /// <summary>
        /// The .plp branch. Answers only when the matrix has one column per run AND the file records
        /// what its values were divided by, because the partition needs absolute areas and a stored
        /// value is `area / applied`.
        /// </summary>
        public static PatternLabRunQuants FromPlp(PatternLabProject? plp)
        {
            const string source = ThreeWayPartition.SourcePlpMatrix;

            if (plp == null || plp.MySparseMatrix == null)
                return No(source, "No project is loaded.");

            var rows = plp.MySparseMatrix.theMatrixInRows;
            if (rows == null || rows.Count == 0)
                return No(source, "This project carries no quantitation rows.");

            string? recorded = plp.RecordedDenominatorTag;
            if (string.IsNullOrEmpty(recorded))
            {
                return No(source,
                    "This project does not record which divisor it was normalized by, so its values " +
                    "cannot be turned back into the absolute areas the partition needs.");
            }

            NormalizationInfo? info = plp.NormalizationInfo;
            if (info != null && info.IsAggregated)
            {
                return No(source,
                    "This project merged technical replicates, so each stored divisor is an AVERAGE of " +
                    "the merged runs and the individual runs cannot be recovered from it.");
            }

            var labels = plp.SecondaryLabels;
            if (labels == null || labels.Count == 0)
                return No(source, "This project carries no per-run information.");
            if (labels.Any(l => l.IsAggregated))
                return No(source, "At least one run's divisors are averages over merged technical replicates.");

            // One matrix row per run, and the rows must denote DIFFERENT runs. A merged project has a
            // single row whose name is the search result, not an acquisition, and normalizing it gives
            // one name for three runs.
            var areaByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            for (int r = 0; r < rows.Count; r++)
            {
                string run = DeNovoReconciler.NormalizeRunName(rows[r].FileName ?? string.Empty);
                if (run.Length == 0)
                    return No(source, "A quantitation row carries no run name.");
                if (areaByRun.ContainsKey(run))
                {
                    return No(source,
                        "Two quantitation rows denote the same run (\"" + run + "\"), so the matrix does " +
                        "not resolve one column per acquisition.");
                }

                double scale;
                if (string.Equals(recorded, NormalizationInfo.TagNone, StringComparison.OrdinalIgnoreCase))
                {
                    scale = 1.0;   // the values ARE areas
                }
                else
                {
                    SecondaryLabel? label = MatchLabel(labels, rows[r].FileName);
                    if (label == null)
                    {
                        return No(source,
                            "A quantitation row could not be matched to exactly one run (\"" +
                            (rows[r].FileName ?? string.Empty) + "\"), so its divisor is unknown.");
                    }

                    double? applied = label.AppliedDivisor;
                    if (!applied.HasValue || applied.Value <= 0)
                    {
                        return No(source,
                            "Run \"" + label.Name + "\" does not record the divisor that was applied to it, " +
                            "so its values cannot be returned to absolute areas.");
                    }

                    scale = applied.Value;
                }

                double total = 0;
                foreach (var cell in rows[r].TheData)
                {
                    // A negative stored value is the not-quantified sentinel, not a small area.
                    if (cell.Value < 0) continue;
                    total += cell.Value * scale;
                }

                areaByRun[run] = total;
            }

            var ticByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (SecondaryLabel label in labels)
            {
                string run = DeNovoReconciler.NormalizeRunName(label.Name ?? string.Empty);
                if (run.Length == 0 || ticByRun.ContainsKey(run)) continue;
                if (label.TotalTic.HasValue && label.TotalTic.Value > 0) ticByRun[run] = label.TotalTic.Value;
            }

            return new PatternLabRunQuants(source, areaByRun, ticByRun, null);
        }

        /// <summary>
        /// The .xic2 branch: LiteDB, collection "QuantPackage" for the run names and the recorded
        /// divisor, collection "Quants" for the per-precursor stored areas.
        ///
        /// Read through BsonDocument rather than through XQuant2's own types, because XQuant2.dll is
        /// not referenced (it was 19 months stale and unused) and because only five fields are wanted
        /// out of a document whose MyIons array is the bulk of the file. The projection keeps the
        /// chromatograms out of this process entirely.
        ///
        /// The file is opened READ ONLY. It belongs to another program and this is a report.
        /// </summary>
        public static PatternLabRunQuants FromXic2(string xic2Path)
        {
            const string source = ThreeWayPartition.SourceXic2Quant;

            if (string.IsNullOrWhiteSpace(xic2Path)) return No(source, "No .xic2 path was given.");
            if (!File.Exists(xic2Path)) return No(source, "The .xic2 was not found: " + xic2Path);

            var areaByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var ticByRun = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            var runByPackage = new Dictionary<int, string>();

            using (var db = new LiteDatabase(new ConnectionString { Filename = xic2Path, ReadOnly = true }))
            {
                foreach (BsonDocument doc in db.GetCollection("QuantPackage").FindAll())
                {
                    int index = doc["Index"].AsInt32;
                    string run = DeNovoReconciler.NormalizeRunName(doc["FileName"].AsString ?? string.Empty);
                    if (run.Length == 0)
                        return No(source, "A quant package in the .xic2 carries no file name.");
                    if (runByPackage.ContainsKey(index))
                        return No(source, "Two quant packages share the index " +
                                          index.ToString(CultureInfo.InvariantCulture) + ".");
                    if (areaByRun.ContainsKey(run))
                        return No(source, "Two quant packages denote the same run (\"" + run + "\").");

                    runByPackage[index] = run;
                    areaByRun[run] = 0;

                    double storedTic = doc["NormalizationFactor"].AsDouble;
                    if (storedTic > 0) ticByRun[run] = storedTic;
                }

                if (runByPackage.Count == 0)
                    return No(source, "The .xic2 holds no quant packages.");

                // Projected server-side: without this every document would arrive with its whole
                // chromatogram, which is the entire 59 MB of the motivating file.
                foreach (BsonDocument doc in db.GetCollection("Quants").Query()
                             .Select("{QuantPackageIndex:$.QuantPackageIndex, QuantArea:$.QuantArea}")
                             .ToEnumerable())
                {
                    int pkg = doc["QuantPackageIndex"].AsInt32;
                    if (!runByPackage.TryGetValue(pkg, out string? run))
                    {
                        return No(source, "A stored quant belongs to package " +
                                          pkg.ToString(CultureInfo.InvariantCulture) +
                                          ", which the .xic2 does not describe.");
                    }

                    double area = doc["QuantArea"].AsDouble;

                    // -1 is XQuant's not-quantified sentinel. Summing it would subtract one count per
                    // precursor that had no cluster, which is a different quantity from the one this
                    // column reports.
                    if (area < 0) continue;

                    areaByRun[run] += area;
                }
            }

            return new PatternLabRunQuants(source, areaByRun, ticByRun, null);
        }

        /// <summary>
        /// The .plp first, the .xic2 only when the .plp refuses, with the reason for the fall-back
        /// written into <paramref name="branchNote"/>. This is the order the partition wants: the
        /// application quantifies from the .plp, so when the .plp can answer, the plot and the tables
        /// are reading the same numbers.
        /// </summary>
        public static PatternLabRunQuants Preferred(PatternLabProject? plp, string? xic2Path, out string branchNote)
        {
            PatternLabRunQuants fromPlp = FromPlp(plp);
            if (fromPlp.IsAvailable)
            {
                branchNote = "PatternLab quants taken from the .plp matrix, one column per run.";
                return fromPlp;
            }

            PatternLabRunQuants fromXic2 = FromXic2(xic2Path ?? string.Empty);
            branchNote = fromXic2.IsAvailable
                ? "PatternLab quants taken from the .xic2 stored quants, because the .plp cannot " +
                  "resolve per run: " + fromPlp.Refusal
                : "Neither source resolved per run. .plp: " + fromPlp.Refusal + " .xic2: " + fromXic2.Refusal;
            return fromXic2;
        }

        /// <summary>
        /// Finds the one SecondaryLabel describing a matrix row, or null when there is not exactly
        /// one. Same two spellings as <see cref="NormalizationPlan"/> has to reconcile: the peptide
        /// export writes a bare file name into the label and a full path into the row.
        /// </summary>
        private static SecondaryLabel? MatchLabel(List<SecondaryLabel> labels, string? rowFileName)
        {
            if (string.IsNullOrEmpty(rowFileName)) return null;

            var exact = labels.Where(l => string.Equals(l.Name, rowFileName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (exact.Count == 1) return exact[0];
            if (exact.Count > 1) return null;

            string rowRun = DeNovoReconciler.NormalizeRunName(rowFileName!);
            var byRun = labels
                .Where(l => string.Equals(DeNovoReconciler.NormalizeRunName(l.Name ?? string.Empty), rowRun,
                                          StringComparison.OrdinalIgnoreCase))
                .ToList();

            return byRun.Count == 1 ? byRun[0] : null;
        }
    }
}
