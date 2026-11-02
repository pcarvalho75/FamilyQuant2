using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// How a number in the partition came to exist. Every value carries one, because the three
    /// components of this plot arrive by three different routes and a reader who cannot tell them
    /// apart is being asked to trust a measurement and an arithmetic identity equally.
    /// </summary>
    public enum PartitionProvenance
    {
        /// <summary>Computed here, from the RAW, by the machinery named in the source token.</summary>
        Measured,

        /// <summary>Read out of a file that recorded it (a .plp denominator, a .xic2 stored quant).</summary>
        Recorded,

        /// <summary>Neither measured nor read: obtained from the others by arithmetic. The remainder
        /// is always this, and it is why the remainder can be negative.</summary>
        Derived
    }

    /// <summary>
    /// One number plus the two things that qualify it: how it was obtained and by what. The source
    /// token is not decoration - "recorded" alone does not distinguish a per-run divisor read from a
    /// .plp from a per-run quant summed out of a .xic2, and those are different measurements.
    /// </summary>
    public readonly struct PartitionValue
    {
        private readonly string? source;

        public PartitionValue(double value, PartitionProvenance provenance, string source)
        {
            Value = value;
            Provenance = provenance;
            this.source = string.IsNullOrWhiteSpace(source) ? null : source;
        }

        public double Value { get; }

        public PartitionProvenance Provenance { get; }

        /// <summary>Machine-readable token naming the producer. Never empty: a value whose source was
        /// not stated reports "unspecified" rather than a blank column that reads as an oversight.</summary>
        public string Source => source ?? "unspecified";

        /// <summary>What goes in the CSV and on screen, e.g. "measured:raw-tic-trim0-0". Lower case
        /// and invariant, because it is compared by machine as often as it is read by a person.</summary>
        public string Label =>
            Provenance.ToString().ToLowerInvariant() + ":" + Source;

        public static PartitionValue Measured(double value, string source) =>
            new PartitionValue(value, PartitionProvenance.Measured, source);

        public static PartitionValue Recorded(double value, string source) =>
            new PartitionValue(value, PartitionProvenance.Recorded, source);

        public static PartitionValue Derived(double value, string source) =>
            new PartitionValue(value, PartitionProvenance.Derived, source);

        public override string ToString() =>
            Value.ToString("R", CultureInfo.InvariantCulture) + " [" + Label + "]";
    }

    /// <summary>
    /// One run's ion current split three ways, or the same split over every run at once.
    ///
    /// THE BAR IS THE TIC AND THE PARTS ARE EXHAUSTIVE. PatternLab explains some of the run,
    /// the de novo addition explains some more, and the remainder is what is still without an owner.
    /// The shares therefore sum to one by construction rather than by luck, and the remainder is
    /// derived precisely so that they do.
    ///
    /// EXHAUSTIVE IS ARITHMETIC; COMPARABLE IS NOT, AND HAS TO BE EARNED TWICE. Once on the BASE -
    /// both numerators are monoisotopic, which is why the de novo envelope sum is a separate column
    /// and not a segment. And once on the GRAIN - the .xic2 side holds one integrated area per
    /// peptide, charge and run, so the de novo side is summed over distinct extracted PEAKS rather
    /// than over MS2 anchors. Summing per anchor adds one integrated area once per MS2 scan that
    /// fired inside the elution peak, which on the motivating export inflated the de novo slice by
    /// 1.8 to 3.0 percent per run, silently and always in the same direction: the shares still summed
    /// to one, the remainder never went negative, and nothing failed.
    ///
    /// THE REMAINDER IS NEVER CLAMPED. A negative remainder is a real finding: it says the two
    /// numerators together claim more signal than the denominator holds, which happens when the same
    /// ion current is counted on both sides (a de novo precursor that PatternLab also quantified) or
    /// when the TIC and the quants came from different acquisitions. Clamping it to zero would hide
    /// exactly the defect the plot exists to expose, so the sign survives and
    /// <see cref="NegativeRemainder"/> flags it for the chart to draw differently.
    /// </summary>
    public sealed class ThreeWayPartitionRow
    {
        /// <summary>The normalized run name, or <see cref="ThreeWayPartition.GroupedRunName"/> for the
        /// aggregate row.</summary>
        public string RunName { get; init; } = string.Empty;

        /// <summary>True only for the row produced by <see cref="ThreeWayPartition.Grouped"/>.</summary>
        public bool IsGrouped { get; init; }

        /// <summary>The denominator: this run's total ion current area.</summary>
        public PartitionValue Tic { get; init; }

        /// <summary>The identified slice: the sum of the per-peptide XIC quants of this run, on the
        /// M+0 base.</summary>
        public PartitionValue PatternLab { get; init; }

        /// <summary>The de novo addition: the sum of M+0 over the DISTINCT EXTRACTED PEAKS of this
        /// run's kept de novo rows. One integrated peak contributes once, however many MS2 scans
        /// sampled it, which is the grain the slice beside it is on - see
        /// <see cref="DeNovoBatchExtractor.DistinctPeaks"/>.</summary>
        public PartitionValue DeNovo { get; init; }

        /// <summary>TIC minus the two numerators. Always <see cref="PartitionProvenance.Derived"/>.</summary>
        public PartitionValue Remainder { get; init; }

        /// <summary>
        /// DIAGNOSTIC ONLY, and it is deliberately absent from the chart. The de novo envelope
        /// (M+0 + M+1 + M+2) has no counterpart on the PatternLab side, whose stored quants are M+0.
        /// Putting it in the partition would compare two different bases across one bar, so it lives
        /// in the CSV, in its own column, next to numbers it is not part of.
        /// </summary>
        public double DeNovoEnvelope { get; init; }

        /// <summary>Computed, never stored, so it cannot drift out of step with the value it
        /// describes.</summary>
        public bool NegativeRemainder => Remainder.Value < 0;

        public double SharePatternLab => PatternLab.Value / Tic.Value;

        public double ShareDeNovo => DeNovo.Value / Tic.Value;

        public double ShareRemainder => Remainder.Value / Tic.Value;

        public double SharePatternLabPercent => SharePatternLab * 100.0;

        public double ShareDeNovoPercent => ShareDeNovo * 100.0;

        public double ShareRemainderPercent => ShareRemainder * 100.0;

        /// <summary>Must be 1 to within <see cref="ThreeWayPartition.ClosureEpsilon"/>. It is not
        /// exactly 1 in binary floating point even though it is exactly 1 in arithmetic, which is
        /// why the tolerance exists and why it is tiny rather than generous.</summary>
        public double ShareSum => SharePatternLab + ShareDeNovo + ShareRemainder;

        public bool Closes => Math.Abs(ShareSum - 1.0) <= ThreeWayPartition.ClosureEpsilon;
    }

    /// <summary>
    /// Builds the three-way partition from values already keyed by run.
    ///
    /// IT TAKES DICTIONARIES, NOT FILES. Every input is a plain map from run name to number, so the
    /// arithmetic, the validation and the CSV can be exercised without a RAW, a .plp or a .xic2 in
    /// sight. The file readers live elsewhere and hand their answers in.
    ///
    /// RUNS ARE MATCHED BY NAME, ALWAYS. Keys are put through
    /// <see cref="DeNovoReconciler.NormalizeRunName"/> on the way in, so "x.raw", "x.RAW",
    /// "C:\runs\x.raw.sqt" and "x" all denote one run and the three sides can be fed in any order.
    /// This is not tidiness: the package order inside this project's .xic2 is PERMUTED relative to
    /// the RAW file names (its package 1 is run _02), so any positional pairing silently combines
    /// one run's TIC with another run's quants and produces a plausible wrong picture.
    ///
    /// A RUN ON ONE SIDE AND NOT THE OTHER IS AN ERROR. Filling the gap with zero would render a
    /// bar in which the missing component simply looks small, which is indistinguishable from a
    /// measurement and is worse than a refusal that names the run.
    /// </summary>
    public static class ThreeWayPartition
    {
        /// <summary>Name of the aggregate row. Not a run name, and chosen so it cannot collide with
        /// one.</summary>
        public const string GroupedRunName = "all runs";

        /// <summary>Tolerance on the closure identity. Tight on purpose: the shares are three
        /// divisions of numbers that sum exactly, so anything beyond a few ulps means the row was not
        /// built by this class.</summary>
        public const double ClosureEpsilon = 1e-12;

        // ---- source tokens ---------------------------------------------------------------------
        // Constants rather than literals at the call sites, because the tests assert on them and a
        // typo in one of two spellings would make the assertion pass against the wrong branch.

        /// <summary>TIC integrated here from the RAW with the trims proved correct for this project.</summary>
        public const string SourceRawTic = "raw-tic-trim0-0";

        /// <summary>TIC read from the .plp's ###Normalization record for this run.</summary>
        public const string SourcePlpRecordedTic = "plp-normalization-tic";

        /// <summary>PatternLab quants summed per run out of the .xic2's stored QuantArea (M+0).</summary>
        public const string SourceXic2Quant = "xic2-quantarea-m0";

        /// <summary>PatternLab quants summed per run out of the .plp matrix, one column per run.</summary>
        public const string SourcePlpMatrix = "plp-matrix-per-run";

        /// <summary>De novo M+0 summed over the run's DISTINCT EXTRACTED PEAKS - not over its rows and
        /// not over its MS2 anchors, which would count one integrated peak once per MS2 event that
        /// fired inside it. The grain is named in the token because it is the thing that makes this
        /// slice comparable with the .xic2 slice beside it.</summary>
        public const string SourceDeNovoM0 = "denovo-xic-m0-distinct-peaks";

        // ---- native (Q3) source tokens ---------------------------------------------------------
        // FamilyQuant's OWN extraction on both sides, reduced to one max-M+0 area per (sequence,
        // charge, run) so no elution peak is counted twice. Distinct from SourceXic2Quant (which the
        // F6 partition reads for its identified slice) precisely because these are measured here, not
        // read from PatternLab's stored file. See NativeQuantFractions.

        /// <summary>Identified (DDA) native M+0, one area per peptide, charge and run.</summary>
        public const string SourceNativeIdentifiedM0 = "native-identified-xic-m0-peptide-dedup";

        /// <summary>Identified (DDA) native full-isotope envelope, one area per peptide, charge and run.</summary>
        public const string SourceNativeIdentifiedEnvelope = "native-identified-xic-envelope-peptide-dedup";

        /// <summary>De novo native M+0, one area per peptide, charge and run.</summary>
        public const string SourceNativeDeNovoM0 = "native-denovo-xic-m0-peptide-dedup";

        /// <summary>De novo native full-isotope envelope, one area per peptide, charge and run.</summary>
        public const string SourceNativeDeNovoEnvelope = "native-denovo-xic-envelope-peptide-dedup";

        /// <summary>The remainder identity.</summary>
        public const string SourceRemainder = "tic-minus-patternlab-minus-denovo";

        /// <summary>An aggregate whose parts all came from the same producer.</summary>
        public const string SourceSummedPrefix = "sum-of-runs:";

        /// <summary>An aggregate whose parts did not, so the aggregate cannot claim either.</summary>
        public const string SourceSummedMixed = "sum-of-runs:mixed";

        public static List<ThreeWayPartitionRow> Build(
            IReadOnlyDictionary<string, PartitionValue> tic,
            IReadOnlyDictionary<string, PartitionValue> patternLab,
            IReadOnlyDictionary<string, PartitionValue> deNovo,
            IReadOnlyDictionary<string, double>? deNovoEnvelope = null)
        {
            if (tic == null) throw new ArgumentNullException(nameof(tic));
            if (patternLab == null) throw new ArgumentNullException(nameof(patternLab));
            if (deNovo == null) throw new ArgumentNullException(nameof(deNovo));

            Dictionary<string, PartitionValue> ticByRun = ByRun(tic, "total ion current");
            Dictionary<string, PartitionValue> plByRun = ByRun(patternLab, "PatternLab quants");
            Dictionary<string, PartitionValue> dnByRun = ByRun(deNovo, "de novo areas");
            Dictionary<string, double> envByRun = deNovoEnvelope == null
                ? new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
                : ByRun(deNovoEnvelope, "de novo envelope");

            if (ticByRun.Count == 0)
                throw new ArgumentException("The partition needs at least one run; no total ion current was supplied.",
                    nameof(tic));

            var runs = ticByRun.Keys
                .Concat(plByRun.Keys)
                .Concat(dnByRun.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rows = new List<ThreeWayPartitionRow>(runs.Count);

            foreach (string run in runs)
            {
                PartitionValue t = Require(ticByRun, run, "total ion current");
                PartitionValue p = Require(plByRun, run, "PatternLab quants");
                PartitionValue d = Require(dnByRun, run, "de novo areas");

                if (!(t.Value > 0) || double.IsNaN(t.Value) || double.IsInfinity(t.Value))
                {
                    throw new ArgumentException(
                        "Run \"" + run + "\" has a total ion current of " +
                        t.Value.ToString("R", CultureInfo.InvariantCulture) +
                        ". The partition divides by it, so a value that is not a positive number would " +
                        "turn every share into infinity or NaN.");
                }

                // The numerators are only subtracted, but a NaN or Infinity would flow into the
                // remainder, the shares, the CSV and the chart without ever failing closure.
                if (double.IsNaN(p.Value) || double.IsInfinity(p.Value))
                    throw new ArgumentException("Run \"" + run + "\" has a PatternLab quant of " +
                        p.Value.ToString("R", CultureInfo.InvariantCulture) + ", which is not a finite number.");
                if (double.IsNaN(d.Value) || double.IsInfinity(d.Value))
                    throw new ArgumentException("Run \"" + run + "\" has a de novo area of " +
                        d.Value.ToString("R", CultureInfo.InvariantCulture) + ", which is not a finite number.");

                envByRun.TryGetValue(run, out double envelope);

                rows.Add(new ThreeWayPartitionRow
                {
                    RunName = run,
                    Tic = t,
                    PatternLab = p,
                    DeNovo = d,
                    Remainder = PartitionValue.Derived(t.Value - p.Value - d.Value, SourceRemainder),
                    DeNovoEnvelope = envelope,
                });
            }

            return rows;
        }

        /// <summary>
        /// The three runs as one bar. Components are summed, and the remainder is derived from the
        /// summed components by the same identity - which is also the sum of the per-run remainders,
        /// so the aggregate cannot disagree with the rows it aggregates.
        ///
        /// A component's provenance survives the sum only when every run agreed on it. Otherwise the
        /// aggregate says "mixed", because an aggregate of one measured and two recorded numbers is
        /// neither.
        /// </summary>
        public static ThreeWayPartitionRow Grouped(IReadOnlyList<ThreeWayPartitionRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (rows.Count == 0)
                throw new ArgumentException("There is nothing to group: the partition has no rows.", nameof(rows));

            PartitionValue tic = Combine(rows.Select(r => r.Tic));
            PartitionValue pl = Combine(rows.Select(r => r.PatternLab));
            PartitionValue dn = Combine(rows.Select(r => r.DeNovo));

            return new ThreeWayPartitionRow
            {
                RunName = GroupedRunName,
                IsGrouped = true,
                Tic = tic,
                PatternLab = pl,
                DeNovo = dn,
                Remainder = PartitionValue.Derived(tic.Value - pl.Value - dn.Value, SourceRemainder),
                DeNovoEnvelope = rows.Sum(r => r.DeNovoEnvelope),
            };
        }

        // ---- CSV -------------------------------------------------------------------------------

        /// <summary>The header, spelled once so the writer and any reader cannot drift apart.</summary>
        public static readonly string[] CsvHeaders =
        {
            "run",
            "tic", "tic_provenance",
            "patternlab", "patternlab_provenance",
            "denovo", "denovo_provenance",
            "remainder", "remainder_provenance",
            "share_patternlab_pct", "share_denovo_pct", "share_remainder_pct",
            "negative_remainder_flag",
            "denovo_envelope",
        };

        /// <summary>
        /// The partition as CSV, per-run rows in the order given and the grouped row last.
        ///
        /// The grouped row is computed here rather than accepted from the caller, so a file can never
        /// carry an aggregate that does not belong to the rows above it.
        ///
        /// EVERY NUMBER IS INVARIANT. .NET writes a double in its shortest round-trippable form, so a
        /// value read back out of this file is the same bits that went in - which is what lets the
        /// closure identity be checked on the file rather than only in memory. On a comma-decimal
        /// machine the current culture would emit "0,5" and shift every later column of the row.
        /// </summary>
        public static string BuildCsv(IReadOnlyList<ThreeWayPartitionRow> rows)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            // The doc's promise only holds if a caller-supplied aggregate can never slip in as a data
            // row and then be aggregated again; per-run rows are the only input this accepts.
            var perRun = rows.Where(r => r != null && !r.IsGrouped).ToList();

            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", CsvHeaders.Select(Field)));

            foreach (ThreeWayPartitionRow row in perRun) csv.AppendLine(Line(row));
            if (perRun.Count > 0) csv.AppendLine(Line(Grouped(perRun)));

            return csv.ToString();
        }

        public static void WriteCsv(string path, IReadOnlyList<ThreeWayPartitionRow> rows)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A file path is required.", nameof(path));

            File.WriteAllText(path, BuildCsv(rows));
        }

        private static string Line(ThreeWayPartitionRow row)
        {
            string[] cells =
            {
                Field(row.RunName),
                Number(row.Tic.Value), Field(row.Tic.Label),
                Number(row.PatternLab.Value), Field(row.PatternLab.Label),
                Number(row.DeNovo.Value), Field(row.DeNovo.Label),
                Number(row.Remainder.Value), Field(row.Remainder.Label),
                Number(row.SharePatternLabPercent),
                Number(row.ShareDeNovoPercent),
                Number(row.ShareRemainderPercent),
                row.NegativeRemainder ? "true" : "false",
                Number(row.DeNovoEnvelope),
            };

            return string.Join(",", cells);
        }

        private static string Number(double value) => CsvFormatting.RoundTrip(value);

        /// <summary>Quotes and doubles embedded quotes, RFC 4180, as the family export does. Run names
        /// come off a disk and may hold anything.</summary>
        private static string Field(string? value) => CsvFormatting.AlwaysQuoted(value);

        // ---- validation ------------------------------------------------------------------------

        private static Dictionary<string, PartitionValue> ByRun(
            IReadOnlyDictionary<string, PartitionValue> source, string side)
        {
            var map = new Dictionary<string, PartitionValue>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in source)
            {
                string run = DeNovoReconciler.NormalizeRunName(kv.Key ?? string.Empty);
                if (run.Length == 0)
                    throw new ArgumentException("The " + side + " carry an entry with no run name.");
                if (map.ContainsKey(run))
                    throw new ArgumentException(
                        "Two entries of the " + side + " denote the same run \"" + run +
                        "\". Run names are normalized before matching, so \"x.raw\" and \"x.RAW\" are one run " +
                        "and one of the two values would have been silently discarded.");

                map.Add(run, kv.Value);
            }

            return map;
        }

        private static Dictionary<string, double> ByRun(
            IReadOnlyDictionary<string, double> source, string side)
        {
            var map = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var kv in source)
            {
                string run = DeNovoReconciler.NormalizeRunName(kv.Key ?? string.Empty);
                if (run.Length == 0)
                    throw new ArgumentException("The " + side + " carry an entry with no run name.");
                if (map.ContainsKey(run))
                    throw new ArgumentException(
                        "Two entries of the " + side + " denote the same run \"" + run + "\".");

                map.Add(run, kv.Value);
            }

            return map;
        }

        private static PartitionValue Require(Dictionary<string, PartitionValue> map, string run, string side)
        {
            if (map.TryGetValue(run, out PartitionValue value)) return value;

            throw new ArgumentException(
                "Run \"" + run + "\" has no " + side + ". Every run must be present on all three sides; " +
                "treating the missing side as zero would draw a bar in which an unmeasured component " +
                "merely looks small.");
        }

        private static PartitionValue Combine(IEnumerable<PartitionValue> values)
        {
            var list = values.ToList();
            double total = list.Sum(v => v.Value);

            PartitionProvenance kind = list[0].Provenance;
            string source = list[0].Source;
            bool uniform = list.All(v => v.Provenance == kind && string.Equals(v.Source, source, StringComparison.Ordinal));

            return uniform
                ? new PartitionValue(total, kind, SourceSummedPrefix + source)
                : new PartitionValue(total, PartitionProvenance.Derived, SourceSummedMixed);
        }
    }
}
