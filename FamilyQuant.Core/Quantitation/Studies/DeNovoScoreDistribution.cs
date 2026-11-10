using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>One PEAKS ALC bin at the spectrum-row grain.</summary>
    public sealed class DeNovoScoreBin
    {
        public double LowerInclusive { get; set; }
        public double UpperBound { get; set; }
        public bool UpperInclusive { get; set; }
        public string Label { get; set; } = string.Empty;
        public int RawRows { get; set; }
        public int PassingAllCriteria { get; set; }
    }

    /// <summary>
    /// The score census behind the de novo acceptance plot. These are PEAKS de-novo-only spectrum
    /// rows, not unique sequences, quantified representatives, or peptide-level FDR estimates.
    /// </summary>
    public sealed class DeNovoScoreDistribution
    {
        public string ScoreName { get; set; } = "PEAKS ALC (%)";
        public double BinWidth { get; set; } = 5;
        public int RawRows { get; set; }
        public int OutOfRangeScoreRows { get; set; }
        public int RowsPassingLength { get; set; }
        public int RowsAtOrAboveAlc { get; set; }
        public int RowsPassingLocalStretch { get; set; }
        public int RowsPassingLengthAndAlc { get; set; }
        public int RowsPassingAllCriteria { get; set; }
        public int MinLength { get; set; }
        public double MinAverageLocalConfidence { get; set; }
        public int StretchLength { get; set; }
        public int StretchFloor { get; set; }
        public List<DeNovoScoreBin> Bins { get; set; } = new List<DeNovoScoreBin>();

        public void Validate()
        {
            if (RawRows < 0 || OutOfRangeScoreRows < 0 || RowsPassingAllCriteria < 0)
                throw new InvalidOperationException("De novo score-distribution counts cannot be negative.");
            if (Bins == null || Bins.Count == 0)
                throw new InvalidOperationException("The de novo score distribution has no bins.");
            if (Bins.Sum(b => b.RawRows) != RawRows)
                throw new InvalidOperationException("The de novo ALC bins do not sum to the raw-row count.");
            if (Bins.Sum(b => b.PassingAllCriteria) != RowsPassingAllCriteria)
                throw new InvalidOperationException("The retained ALC bins do not sum to the accepted-row count.");
            if (RowsPassingAllCriteria > RowsPassingLengthAndAlc
                || RowsPassingLengthAndAlc > RowsAtOrAboveAlc
                || RowsPassingAllCriteria > RowsPassingLocalStretch
                || RowsPassingAllCriteria > RowsPassingLength)
                throw new InvalidOperationException("The de novo score-distribution stage counts are inconsistent.");
        }
    }

    public static class DeNovoScoreDistributionStudy
    {
        public const double DefaultBinWidth = 5;

        public static DeNovoScoreDistribution Build(IEnumerable<PeaksDeNovoRow> rows,
                                                     DeNovoFilterCriteria? criteria = null,
                                                     double binWidth = DefaultBinWidth)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (!(binWidth > 0) || binWidth > 100)
                throw new ArgumentOutOfRangeException(nameof(binWidth), "The ALC bin width must be in (0, 100].");

            DeNovoFilterCriteria c = criteria ?? new DeNovoFilterCriteria();
            List<PeaksDeNovoRow> source = rows.ToList();
            int binCount = (int)Math.Ceiling(100.0 / binWidth);
            var bins = new List<DeNovoScoreBin>(binCount);
            for (int i = 0; i < binCount; i++)
            {
                double lower = i * binWidth;
                double upper = Math.Min(100, lower + binWidth);
                bool last = i == binCount - 1;
                bins.Add(new DeNovoScoreBin
                {
                    LowerInclusive = lower,
                    UpperBound = upper,
                    UpperInclusive = last,
                    Label = last
                        ? lower.ToString("0.#", CultureInfo.InvariantCulture) + "-" +
                          upper.ToString("0.#", CultureInfo.InvariantCulture)
                        : lower.ToString("0.#", CultureInfo.InvariantCulture) + "-<" +
                          upper.ToString("0.#", CultureInfo.InvariantCulture),
                });
            }

            var result = new DeNovoScoreDistribution
            {
                BinWidth = binWidth,
                RawRows = source.Count,
                MinLength = c.MinLength,
                MinAverageLocalConfidence = c.MinAverageLocalConfidence,
                StretchLength = c.StretchLength,
                StretchFloor = c.StretchFloor,
                Bins = bins,
            };

            foreach (PeaksDeNovoRow row in source)
            {
                if (row.Alc < 0 || row.Alc > 100) result.OutOfRangeScoreRows++;
                int index = (int)Math.Floor(row.Alc / binWidth);
                index = Math.Max(0, Math.Min(binCount - 1, index));
                DeNovoScoreBin bin = bins[index];
                bin.RawRows++;

                bool passLength = row.Length >= c.MinLength;
                bool passAlc = row.Alc >= c.MinAverageLocalConfidence;
                bool passStretch = DeNovoFilter.HasStretch(row.LocalConfidence, c.StretchLength, c.StretchFloor);
                bool passAll = passLength && passAlc && passStretch;

                if (passLength) result.RowsPassingLength++;
                if (passAlc) result.RowsAtOrAboveAlc++;
                if (passStretch) result.RowsPassingLocalStretch++;
                if (passLength && passAlc) result.RowsPassingLengthAndAlc++;
                if (passAll)
                {
                    result.RowsPassingAllCriteria++;
                    bin.PassingAllCriteria++;
                }
            }

            result.Validate();
            return result;
        }
    }

    /// <summary>
    /// One source-specific confidence distribution suitable for persistence and graph regeneration.
    /// Unlike the legacy PEAKS-only distribution above, every field is generic and the source's exact
    /// confidence-field name travels with it. A PEAKS ALC distribution and a DeepNovo SCORE distribution
    /// are therefore separate panels/records and can never be silently pooled into one ALC histogram.
    /// </summary>
    public sealed class DeNovoSourceScoreDistribution
    {
        public string SourceId { get; set; } = string.Empty;
        public string SourceDisplayName { get; set; } = string.Empty;
        public string ConfidenceField { get; set; } = string.Empty;
        public double MinimumConfidence { get; set; }
        public double BinWidth { get; set; } = 5;
        public int RawRows { get; set; }
        public int OutOfRangeScoreRows { get; set; }
        public int RowsPassingLength { get; set; }
        public int RowsAtOrAboveConfidence { get; set; }
        public int RowsPassingLocalStretch { get; set; }
        public int RowsPassingLengthAndConfidence { get; set; }
        public int RowsPassingAllCriteria { get; set; }
        public int MinLength { get; set; }
        public int StretchLength { get; set; }
        public int StretchFloor { get; set; }
        public List<DeNovoScoreBin> Bins { get; set; } = new List<DeNovoScoreBin>();

        public string CutoffLabel => ConfidenceField + " >= " +
            MinimumConfidence.ToString("0.###", CultureInfo.InvariantCulture);

        public void Validate()
        {
            if (Bins == null || Bins.Count == 0)
                throw new InvalidOperationException("The source confidence distribution has no bins.");
            if (Bins.Sum(b => b.RawRows) != RawRows)
                throw new InvalidOperationException("The source confidence bins do not sum to the raw-row count.");
            if (Bins.Sum(b => b.PassingAllCriteria) != RowsPassingAllCriteria)
                throw new InvalidOperationException("The retained source confidence bins do not sum to the accepted-row count.");
            if (RowsPassingAllCriteria > RowsPassingLengthAndConfidence
                || RowsPassingLengthAndConfidence > RowsAtOrAboveConfidence
                || RowsPassingAllCriteria > RowsPassingLocalStretch
                || RowsPassingAllCriteria > RowsPassingLength)
                throw new InvalidOperationException("The source confidence stage counts are inconsistent.");
        }
    }

    public static class DeNovoSourceScoreDistributionStudy
    {
        public static DeNovoSourceScoreDistribution FromLegacy(DeNovoScoreDistribution distribution)
        {
            if (distribution == null) throw new ArgumentNullException(nameof(distribution));
            distribution.Validate();
            return new DeNovoSourceScoreDistribution
                {
                    SourceId = DeNovoSource.DenovoOnly.Id,
                    SourceDisplayName = DeNovoSource.DenovoOnly.DisplayName,
                    ConfidenceField = DeNovoSource.DenovoOnly.ConfidenceColumn,
                    MinimumConfidence = distribution.MinAverageLocalConfidence,
                    RawRows = distribution.RawRows,
                    RowsPassingLength = distribution.RowsPassingLength,
                    RowsAtOrAboveConfidence = distribution.RowsAtOrAboveAlc,
                    RowsPassingLocalStretch = distribution.RowsPassingLocalStretch,
                    RowsPassingLengthAndConfidence = distribution.RowsPassingLengthAndAlc,
                    RowsPassingAllCriteria = distribution.RowsPassingAllCriteria,
                    MinLength = distribution.MinLength,
                    StretchLength = distribution.StretchLength,
                    StretchFloor = distribution.StretchFloor,
                    Bins = distribution.Bins,
                };
        }

        public static DeNovoSourceScoreDistribution Build(
            IEnumerable<PeaksDeNovoRow> rows,
            DeNovoSourceSelection selection,
            DeNovoFilterCriteria? criteria = null,
            double binWidth = DeNovoScoreDistributionStudy.DefaultBinWidth)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (!(binWidth > 0) || binWidth > 100)
                throw new ArgumentOutOfRangeException(nameof(binWidth),
                    "The confidence bin width must be in (0, 100].");

            DeNovoFilterCriteria c = criteria ?? new DeNovoFilterCriteria();
            List<PeaksDeNovoRow> source = rows.ToList();
            int binCount = (int)Math.Ceiling(100.0 / binWidth);
            var bins = new List<DeNovoScoreBin>(binCount);
            for (int i = 0; i < binCount; i++)
            {
                double lower = i * binWidth;
                double upper = Math.Min(100, lower + binWidth);
                bool last = i == binCount - 1;
                bins.Add(new DeNovoScoreBin
                {
                    LowerInclusive = lower,
                    UpperBound = upper,
                    UpperInclusive = last,
                    Label = last
                        ? lower.ToString("0.#", CultureInfo.InvariantCulture) + "-" +
                          upper.ToString("0.#", CultureInfo.InvariantCulture)
                        : lower.ToString("0.#", CultureInfo.InvariantCulture) + "-<" +
                          upper.ToString("0.#", CultureInfo.InvariantCulture),
                });
            }

            var result = new DeNovoSourceScoreDistribution
            {
                SourceId = selection.Source.Id,
                SourceDisplayName = selection.Source.DisplayName,
                ConfidenceField = selection.Source.ConfidenceColumn,
                MinimumConfidence = selection.MinimumConfidence,
                BinWidth = binWidth,
                RawRows = source.Count,
                MinLength = c.MinLength,
                StretchLength = c.StretchLength,
                StretchFloor = c.StretchFloor,
                Bins = bins,
            };

            foreach (PeaksDeNovoRow row in source)
            {
                double confidence = row.ConfidenceValue;
                if (confidence < 0 || confidence > 100) result.OutOfRangeScoreRows++;
                int index = (int)Math.Floor(confidence / binWidth);
                index = Math.Max(0, Math.Min(binCount - 1, index));
                DeNovoScoreBin bin = bins[index];
                bin.RawRows++;

                bool passLength = row.Length >= c.MinLength;
                bool passConfidence = confidence >= selection.MinimumConfidence;
                bool passStretch = DeNovoFilter.HasStretch(row.LocalConfidence, c.StretchLength, c.StretchFloor);
                bool passAll = passLength && passConfidence && passStretch;

                if (passLength) result.RowsPassingLength++;
                if (passConfidence) result.RowsAtOrAboveConfidence++;
                if (passStretch) result.RowsPassingLocalStretch++;
                if (passLength && passConfidence) result.RowsPassingLengthAndConfidence++;
                if (passAll)
                {
                    result.RowsPassingAllCriteria++;
                    bin.PassingAllCriteria++;
                }
            }

            result.Validate();
            return result;
        }
    }

    /// <summary>One confidence threshold of a score sweep: how many DISTINCT clean peptide sequences of a
    /// source survive at or above the threshold, split by whether the sequence is also in the reference
    /// de-novo-only set.</summary>
    public sealed class DeNovoSweepPoint
    {
        public double Threshold { get; set; }
        public int DistinctCleanTotal { get; set; }

        /// <summary>Of <see cref="DistinctCleanTotal"/>, how many clean sequences are also in the
        /// reference (PEAKS de novo only) clean-sequence set.</summary>
        public int OverlapWithReference { get; set; }

        /// <summary>Of <see cref="DistinctCleanTotal"/>, how many are new (not in the reference set).</summary>
        public int NewNotInReference { get; set; }

        public double OverlapFraction =>
            DistinctCleanTotal > 0 ? (double)OverlapWithReference / DistinctCleanTotal : 0.0;
    }

    /// <summary>One panel of a score sweep: a single source (de novo all, or DeepNovo) swept over its own
    /// confidence field. The field name is carried per panel so a chart never implies SCORE equals ALC.</summary>
    public sealed class DeNovoScoreSweepPanel
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ConfidenceField { get; set; } = string.Empty;
        public int DistinctCleanSequences { get; set; }
        public List<DeNovoSweepPoint> Points { get; set; } = new List<DeNovoSweepPoint>();
    }

    /// <summary>
    /// The score-sweep-with-overlap figure's data: for each of two panels (de novo all, DeepNovo), the
    /// distinct clean-sequence count surviving at each confidence threshold, split into the part also
    /// present in the PEAKS de-novo-only reference set and the part new to the source, plus the overlap
    /// fraction.
    ///
    /// It is pure sequence bookkeeping over the CSV rows: no XIC, no RAW, no extraction. The clean
    /// sequence rule is <see cref="DeNovoReconciler.NormalizeSequence"/> (strip parenthetical
    /// modification tokens, keep letters only, fold I to L). The exports were floored at 50% confidence,
    /// so <see cref="DataFloor"/> is 50 and the axis starts there.
    /// </summary>
    public sealed class DeNovoScoreSweep
    {
        public double DataFloor { get; set; } = 50;
        public double MinThreshold { get; set; } = 50;
        public double MaxThreshold { get; set; } = 100;
        public double Step { get; set; } = 5;

        /// <summary>The reference cutoff: clean sequences from de novo only with confidence at or above this
        /// are the overlap reference. Defaults to ALC 80, the cutoff the current deck uses.</summary>
        public double ReferenceCutoff { get; set; } = 80;
        public string ReferenceField { get; set; } = "ALC (%)";
        public string ReferenceDisplayName { get; set; } = "de novo only";
        public int ReferenceCleanSequences { get; set; }

        public DeNovoScoreSweepPanel DeNovoAll { get; set; } = new DeNovoScoreSweepPanel();
        public DeNovoScoreSweepPanel DeepNovo { get; set; } = new DeNovoScoreSweepPanel();
    }

    public static class DeNovoScoreSweepStudy
    {
        /// <summary>The reference cutoff the current deck uses: ALC 80. Documented as a parameter so a
        /// caller can change the overlap reference without touching the sweep itself.</summary>
        public const double DefaultReferenceCutoff = 80;

        /// <summary>
        /// Builds the two-panel sweep. Panels are labelled with the source's own confidence field so the
        /// figure never implies DeepNovo SCORE is on the ALC scale. The reference (overlap) set is the
        /// clean sequences of <paramref name="referenceRows"/> whose confidence is at or above
        /// <paramref name="referenceCutoff"/>.
        /// </summary>
        public static DeNovoScoreSweep Build(
            IEnumerable<PeaksDeNovoRow> referenceRows,
            DeNovoSource referenceSource,
            IEnumerable<PeaksDeNovoRow> deNovoAllRows,
            DeNovoSource deNovoAllSource,
            IEnumerable<PeaksDeNovoRow> deepNovoRows,
            DeNovoSource deepNovoSource,
            double referenceCutoff = DefaultReferenceCutoff,
            double min = 50, double max = 100, double step = 5)
        {
            if (referenceRows == null) throw new ArgumentNullException(nameof(referenceRows));
            if (deNovoAllRows == null) throw new ArgumentNullException(nameof(deNovoAllRows));
            if (deepNovoRows == null) throw new ArgumentNullException(nameof(deepNovoRows));
            if (referenceSource == null) throw new ArgumentNullException(nameof(referenceSource));
            if (deNovoAllSource == null) throw new ArgumentNullException(nameof(deNovoAllSource));
            if (deepNovoSource == null) throw new ArgumentNullException(nameof(deepNovoSource));
            if (!(step > 0)) throw new ArgumentOutOfRangeException(nameof(step), "The step must be positive.");
            if (max < min) throw new ArgumentException("The threshold range is empty.", nameof(max));

            Dictionary<string, double> referenceMax = MaxConfidenceByCleanSequence(referenceRows);
            var referenceSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, double> kv in referenceMax)
                if (kv.Value >= referenceCutoff) referenceSet.Add(kv.Key);

            var sweep = new DeNovoScoreSweep
            {
                DataFloor = min,
                MinThreshold = min,
                MaxThreshold = max,
                Step = step,
                ReferenceCutoff = referenceCutoff,
                ReferenceField = referenceSource.ConfidenceColumn,
                ReferenceDisplayName = referenceSource.DisplayName,
                ReferenceCleanSequences = referenceSet.Count,
                DeNovoAll = BuildPanel(deNovoAllSource, deNovoAllRows, referenceSet, min, max, step),
                DeepNovo = BuildPanel(deepNovoSource, deepNovoRows, referenceSet, min, max, step),
            };
            return sweep;
        }

        private static DeNovoScoreSweepPanel BuildPanel(DeNovoSource source,
            IEnumerable<PeaksDeNovoRow> rows, HashSet<string> referenceSet,
            double min, double max, double step)
        {
            Dictionary<string, double> maxConf = MaxConfidenceByCleanSequence(rows);

            var panel = new DeNovoScoreSweepPanel
            {
                DisplayName = source.DisplayName,
                ConfidenceField = source.ConfidenceColumn,
                DistinctCleanSequences = maxConf.Count,
            };

            int steps = (int)Math.Round((max - min) / step);
            for (int i = 0; i <= steps; i++)
            {
                double t = min + i * step;
                int total = 0, overlap = 0;
                foreach (KeyValuePair<string, double> kv in maxConf)
                {
                    if (kv.Value < t) continue;
                    total++;
                    if (referenceSet.Contains(kv.Key)) overlap++;
                }
                panel.Points.Add(new DeNovoSweepPoint
                {
                    Threshold = t,
                    DistinctCleanTotal = total,
                    OverlapWithReference = overlap,
                    NewNotInReference = total - overlap,
                });
            }
            return panel;
        }

        /// <summary>The maximum confidence over all rows carrying a given clean sequence. A clean sequence
        /// survives at threshold t when its maximum confidence is at or above t, so this collapses the
        /// row grain to the distinct-sequence grain once.</summary>
        private static Dictionary<string, double> MaxConfidenceByCleanSequence(IEnumerable<PeaksDeNovoRow> rows)
        {
            var maxConf = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (PeaksDeNovoRow row in rows)
            {
                string clean = DeNovoReconciler.NormalizeSequence(row.Peptide);
                if (clean.Length == 0) continue;
                if (!maxConf.TryGetValue(clean, out double c) || row.Alc > c) maxConf[clean] = row.Alc;
            }
            return maxConf;
        }

        /// <summary>The per-threshold numbers as CSV, so they are inspectable beside the figure.</summary>
        public static string BuildTableCsv(DeNovoScoreSweep sweep)
        {
            if (sweep == null) throw new ArgumentNullException(nameof(sweep));
            var b = new StringBuilder();
            b.Append("panel,confidence_field,threshold,distinct_clean_total,also_in_denovo_only,")
             .Append("new_not_in_denovo_only,overlap_fraction\n");
            AppendPanelCsv(b, sweep.DeNovoAll);
            AppendPanelCsv(b, sweep.DeepNovo);
            return b.ToString();
        }

        private static void AppendPanelCsv(StringBuilder b, DeNovoScoreSweepPanel panel)
        {
            foreach (DeNovoSweepPoint p in panel.Points)
            {
                b.Append('"').Append(panel.DisplayName.Replace("\"", "\"\"")).Append('"').Append(',')
                 .Append('"').Append(panel.ConfidenceField.Replace("\"", "\"\"")).Append('"').Append(',')
                 .Append(p.Threshold.ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                 .Append(p.DistinctCleanTotal.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(p.OverlapWithReference.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(p.NewNotInReference.ToString(CultureInfo.InvariantCulture)).Append(',')
                 .Append(p.OverlapFraction.ToString("0.####", CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        /// <summary>The per-threshold numbers as a small Markdown table, for a human-readable supplement.</summary>
        public static string BuildTableMarkdown(DeNovoScoreSweep sweep)
        {
            if (sweep == null) throw new ArgumentNullException(nameof(sweep));
            var b = new StringBuilder();
            b.Append("# De novo score sweep with de-novo-only overlap\n\n");
            b.Append("Distinct CLEAN peptide sequences (parenthetical modification tokens stripped, letters ")
             .Append("only, I folded to L) surviving at or above each confidence threshold, split into the ")
             .Append("part also present in the PEAKS de-novo-only reference set and the part new to the ")
             .Append("source. Pure sequence bookkeeping from the CSVs: no XIC extraction.\n\n");
            b.Append("Overlap reference: ").Append(sweep.ReferenceDisplayName).Append(" clean sequences with ")
             .Append(sweep.ReferenceField).Append(" >= ")
             .Append(sweep.ReferenceCutoff.ToString("0.#", CultureInfo.InvariantCulture)).Append(" (")
             .Append(sweep.ReferenceCleanSequences.ToString("N0", CultureInfo.InvariantCulture))
             .Append(" sequences). The exports were floored at ")
             .Append(sweep.DataFloor.ToString("0.#", CultureInfo.InvariantCulture))
             .Append("% confidence, so the sweep starts there. ALC and DeepNovo SCORE are different scales ")
             .Append("and each panel is swept over its own field.\n\n");
            AppendPanelMarkdown(b, sweep.DeNovoAll);
            AppendPanelMarkdown(b, sweep.DeepNovo);
            return b.ToString();
        }

        private static void AppendPanelMarkdown(StringBuilder b, DeNovoScoreSweepPanel panel)
        {
            b.Append("## ").Append(panel.DisplayName).Append(" (swept over ").Append(panel.ConfidenceField)
             .Append(")\n\n");
            b.Append("| Threshold | Distinct clean | Also in de novo only | New | Overlap fraction |\n");
            b.Append("|---|---|---|---|---|\n");
            foreach (DeNovoSweepPoint p in panel.Points)
            {
                b.Append("| ").Append(p.Threshold.ToString("0.#", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(p.DistinctCleanTotal.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(p.OverlapWithReference.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append(p.NewNotInReference.ToString("N0", CultureInfo.InvariantCulture)).Append(" | ")
                 .Append((p.OverlapFraction * 100).ToString("F1", CultureInfo.InvariantCulture)).Append("% |\n");
            }
            b.Append('\n');
        }
    }
}
