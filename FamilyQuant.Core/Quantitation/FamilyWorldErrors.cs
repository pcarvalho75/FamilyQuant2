using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace FamilyQuant
{
    /// <summary>
    /// One known answer: a de novo tag a de novo engine accepted on a scan the database search identified, at a
    /// compatible precursor (the stored spectrum table). The tag carries the engine's real errors; the family is
    /// known from the identified peptide.
    /// </summary>
    public sealed class FamilyWorldKnownAnswer
    {
        public string Tag { get; init; } = string.Empty;
        public string Identified { get; init; } = string.Empty;
        /// <summary>How many accepted calls (scan, source) gave this pair.</summary>
        public int Calls { get; set; }
        public SortedSet<string> Sources { get; } = new SortedSet<string>(StringComparer.Ordinal);
        /// <summary>The UniProt families of the database proteins holding the identified peptide.</summary>
        public SortedSet<string> KnownFamilies { get; } = FamilyWorld.NewFamilySet();
        /// <summary>Holders of the identified peptide that carry no UniProt family.</summary>
        public int UnlabelledHolders { get; set; }
        /// <summary>Why the pair is not a known answer, or empty.</summary>
        public string Exclusion { get; set; } = string.Empty;
        /// <summary>What the tag was credited to in the world without the sample's species, or null if not searched.</summary>
        public FamilyWorldCredit? Credit { get; set; }

        public bool IsKnown => Exclusion.Length == 0;
        public bool Landed => Credit?.Outcome == FamilyWorldOutcome.Credited;
        /// <summary>Strict: every credited family is a known one.</summary>
        public bool Right => Credit != null && FamilyWorld.IsRight(Credit, KnownFamilies);

        /// <summary>This answer as it reads under a higher minimum tag length: a tag shorter than it is too short,
        /// any other keeps its credit (B10; a tag's search does not depend on the minimum).</summary>
        public FamilyWorldKnownAnswer AtMinimumLength(int minimumTagLength)
        {
            var copy = new FamilyWorldKnownAnswer
            {
                Tag = Tag, Identified = Identified, Calls = Calls, UnlabelledHolders = UnlabelledHolders, Exclusion = Exclusion,
                Credit = Credit == null || !IsKnown || Tag.Length >= minimumTagLength ? Credit
                    : new FamilyWorldCredit { Outcome = FamilyWorldOutcome.TooShort },
            };
            copy.Sources.UnionWith(Sources);
            copy.KnownFamilies.UnionWith(KnownFamilies);
            return copy;
        }
    }

    /// <summary>One outcome of the second pass, as stored.</summary>
    public sealed class FamilyWorldOutcomeSummary
    {
        public string Outcome { get; set; } = string.Empty;
        public double Envelope { get; set; }
        public int Peptides { get; set; }
    }

    /// <summary>One family in scope, with the error beside its number: the decoy rate of its credits and the
    /// known-answer right rate, or what evidence it lacks.</summary>
    public sealed class FamilyWorldFamilySummary
    {
        public string Family { get; set; } = string.Empty;
        public double UniqueEnvelope { get; set; }
        public double SplitEnvelope { get; set; }
        public double CreditedEnvelope { get; set; }
        public int Peptides { get; set; }
        public int UniquePeptides { get; set; }
        public double DecoyEnvelope { get; set; }
        /// <summary>Decoy envelope over credited envelope when supported in [0,1]. The status names
        /// absent/non-finite credit or decoy domination; underlying envelopes remain available.</summary>
        private double? decoyRate;
        public double? DecoyRate { get => FamilyWorldDecoyRates.Recorded(decoyRate, CreditedEnvelope, DecoyEnvelope); set => decoyRate = value; }
        [JsonIgnore] public string DecoyRateStatus => FamilyWorldDecoyRates.Status(CreditedEnvelope, DecoyEnvelope, decoyRate);
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? DecoyPeptides { get; set; }
        /// <summary>Known answers whose family is this one, how many landed on a family, and how many were right.</summary>
        public int KnownPairs { get; set; }
        public int KnownLanded { get; set; }
        public int KnownRight { get; set; }
        /// <summary>Known answers credited to this family, and how many of them truly were this family.</summary>
        public int KnownCredited { get; set; }
        public int KnownCreditedTrue { get; set; }
        /// <summary>KnownCreditedTrue over KnownCredited: how often a credit to this family is right. Null under the
        /// evidence floor.</summary>
        public double? RightRate { get; set; }
        /// <summary>"measured", "too little evidence: n credits" or "no known answer".</summary>
        public string Evidence { get; set; } = string.Empty;
        /// <summary>The world proteins the credited tags hit, by identifier.</summary>
        public List<string> WorldProteins { get; set; } = new List<string>();
    }

    /// <summary>One row of the result table (B7): a family's three parts, each counting only the peptides that
    /// reach this family alone, with the family-world part's error beside it.</summary>
    public sealed class FamilyWorldCompositionRow
    {
        public const string UniProtKind = "uniprot";
        public const string CuratedKind = "curated";

        public string Family { get; set; } = string.Empty;
        /// <summary><see cref="UniProtKind"/> for a UniProt family; <see cref="CuratedKind"/> for a curated group whose
        /// members carry no UniProt family, named by its members.</summary>
        public string Kind { get; set; } = UniProtKind;
        /// <summary>The curated groups (User IDs) whose members carry this family.</summary>
        public List<int> CuratedUserIds { get; set; } = new List<int>();
        public double IdentifiedEnvelope { get; set; }
        public int IdentifiedPeptides { get; set; }
        /// <summary>De novo peptides the project's own database places in this family.</summary>
        public double DatabaseDeNovoEnvelope { get; set; }
        public int DatabaseDeNovoPeptides { get; set; }
        /// <summary>De novo peptides only the family world places in this family: the database gap.</summary>
        public double WorldDeNovoEnvelope { get; set; }
        public int WorldDeNovoPeptides { get; set; }
        private double? worldDecoyRate;
        public double? WorldDecoyRate { get => FamilyWorldDecoyRates.Recorded(worldDecoyRate); set => worldDecoyRate = value; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? WorldTargetPeptides { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? WorldDecoyPeptides { get; set; }
        private string? worldDecoyRateUnavailableReason;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? WorldDecoyRateUnavailableReason
        {
            get => FamilyWorldDecoyRates.Recorded(worldDecoyRate).HasValue ? null
                : worldDecoyRate is > 1 ? FamilyWorldDecoyRates.DecoyExceedsCredited : worldDecoyRateUnavailableReason;
            set => worldDecoyRateUnavailableReason = value;
        }
        [JsonIgnore] public string WorldDecoyRateStatus => FamilyWorldDecoyRates.RecordedStatus(worldDecoyRate, WorldDecoyRateUnavailableReason);
        public int WorldKnownCredited { get; set; }
        public int WorldKnownCreditedTrue { get; set; }
        public double? WorldRightRate { get; set; }
        public string WorldEvidence { get; set; } = string.Empty;

        [JsonIgnore]
        public double TotalEnvelope => IdentifiedEnvelope + DatabaseDeNovoEnvelope + WorldDeNovoEnvelope;
    }

    /// <summary>The quick estimate for one minimum tag length (B10): how much signal lands, how much the reversed
    /// copies win, and how often a landed known answer is right.</summary>
    public sealed class FamilyWorldLengthEstimate
    {
        public int MinimumTagLength { get; set; }
        public double ResidualEnvelope { get; set; }
        public double CreditedEnvelope { get; set; }
        /// <summary>CreditedEnvelope over ResidualEnvelope.</summary>
        public double LandedFraction { get; set; }
        public double DecoyEnvelope { get; set; }
        /// <summary>DecoyEnvelope over CreditedEnvelope only when the rate is supported in [0,1].</summary>
        private double? decoyRate;
        public double? DecoyRate { get => FamilyWorldDecoyRates.Recorded(decoyRate, CreditedEnvelope, DecoyEnvelope); set => decoyRate = value; }
        [JsonIgnore] public string DecoyRateStatus => FamilyWorldDecoyRates.Status(CreditedEnvelope, DecoyEnvelope, decoyRate);
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? TargetPeptides { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? DecoyPeptides { get; set; }
        public int KnownLanded { get; set; }
        public int KnownRight { get; set; }
        /// <summary>KnownRight over KnownLanded; null when no known answer landed.</summary>
        public double? RightRate { get; set; }
        /// <summary>Families whose right rate is shown (at least the evidence floor of known credits).</summary>
        public int FamiliesMeasured { get; set; }
    }

    /// <summary>The stored table of the family world: the second pass, its rules and, per family, the error beside
    /// every number. It is what the result screen and its export read.</summary>
    public sealed class FamilyWorldSummary
    {
        public string Method { get; set; } = string.Empty;
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public HomologyScienceOptions? Science { get; set; }
        public string KnownAnswerMethod { get; set; } = string.Empty;
        public string Rules { get; set; } = string.Empty;
        public int MinimumTagLength { get; set; }
        public double MinIdentity { get; set; }
        public List<string> ScopeFamilies { get; set; } = new List<string>();
        public string WorldFingerprint { get; set; } = string.Empty;
        public string WorldRelease { get; set; } = string.Empty;
        public int WorldProteins { get; set; }
        /// <summary>The taxa removed from the world for the known-answer test: the sample's own.</summary>
        public List<string> KnownAnswerExcludedTaxa { get; set; } = new List<string>();
        public int MinimumKnownCredits { get; set; }
        public double ResidualEnvelope { get; set; }
        public int ResidualPeptides { get; set; }
        public double SharedEnvelope { get; set; }
        public List<FamilyWorldOutcomeSummary> Outcomes { get; set; } = new List<FamilyWorldOutcomeSummary>();
        public int KnownPairs { get; set; }
        public int KnownLanded { get; set; }
        public int KnownRight { get; set; }
        public List<FamilyWorldFamilySummary> Families { get; set; } = new List<FamilyWorldFamilySummary>();

        /// <summary>B10: the complete table for every minimum tag length a person can choose (ascending, the default's
        /// included and equal to this one), so choosing a length needs no fetch and no new Compute. Null on a table
        /// computed for one length only, and then absent from the stored JSON.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FamilyWorldSummary>? Lengths { get; set; }

        /// <summary>B10: the quick estimate per minimum tag length, in the order of <see cref="Lengths"/>.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<FamilyWorldLengthEstimate>? LengthEstimates { get; set; }

        /// <summary>The complete table at a minimum tag length: one of <see cref="Lengths"/>, or this table itself when it
        /// was computed for that length only; null when the length was not computed.</summary>
        public FamilyWorldSummary? ForLength(int minimumTagLength) =>
            Lengths?.FirstOrDefault(t => t.MinimumTagLength == minimumTagLength)
            ?? (Lengths == null && MinimumTagLength == minimumTagLength ? this : null);

        /// <summary>The first pass's rule, in plain words, beside <see cref="Rules"/>.</summary>
        public string CompositionRules { get; set; } = string.Empty;
        /// <summary>The measured basis the composition closes on: identified envelope plus de novo measured
        /// envelope, each deduplicated peptide once (FamilyEnvelopeQuant's MeasuredTotalEnvelopeReferenceCalibrated).</summary>
        public double MeasuredIdentifiedEnvelope { get; set; }
        public double MeasuredDeNovoEnvelope { get; set; }
        [JsonIgnore]
        public double MeasuredTotalEnvelope => MeasuredIdentifiedEnvelope + MeasuredDeNovoEnvelope;
        public List<FamilyWorldCompositionRow> Composition { get; set; } = new List<FamilyWorldCompositionRow>();
        /// <summary>First-pass peptides whose curated groups reach several families: one shared row each.</summary>
        public double SharedIdentifiedEnvelope { get; set; }
        public double SharedDatabaseDeNovoEnvelope { get; set; }
        /// <summary>Identified peptides the database search placed in no curated group.</summary>
        public double UnattributedIdentifiedEnvelope { get; set; }
        /// <summary>The one global unexplained bucket: de novo envelope neither the database nor the family world
        /// credits (every outcome of the second pass but a credit).</summary>
        public double UnexplainedEnvelope { get; set; }

        /// <summary>
        /// The result table's export: one row per family with its three parts and the family-world error, then the
        /// global rows that close on the measured total. Every row states both passes' rules.
        /// </summary>
        public string BuildCompositionCsv()
        {
            string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            string Opt(double? v) => v.HasValue ? Num(v.Value) : string.Empty;
            string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
            string Count(int? v) => v.HasValue ? Int(v.Value) : "not recorded";
            string Fraction(double v) => MeasuredTotalEnvelope > 0 ? Num(v / MeasuredTotalEnvelope) : string.Empty;
            string Q(string v) => CsvFormatting.AlwaysQuoted(v);
            string rule = Q(CompositionRules + " | " + Rules);
            var csv = new StringBuilder();
            csv.Append(string.Join(",", new[]
            {
                "row", "kind", "family", "curated_user_ids", "identified_envelope", "database_de_novo_envelope",
                "family_world_de_novo_envelope", "total_envelope", "fraction_of_measured_total", "family_world_decoy_rate",
                "family_world_known_credited", "family_world_known_credited_true", "family_world_right_rate",
                "family_world_evidence", "rule", "family_world_decoy_rate_status", "family_world_target_peptides", "family_world_decoy_peptides",
            }.Select(Q))).Append('\n');
            foreach (FamilyWorldCompositionRow r in Composition)
            {
                var evidence = FamilyWorldDecoyRates.ForComposition(r, Families.FirstOrDefault(f => string.Equals(f.Family, r.Family, StringComparison.OrdinalIgnoreCase)));
                csv.Append(string.Join(",", Q("family"), Q(r.Kind), Q(r.Family),
                    Q(string.Join(" ", r.CuratedUserIds.Select(id => id.ToString(CultureInfo.InvariantCulture)))),
                    Num(r.IdentifiedEnvelope), Num(r.DatabaseDeNovoEnvelope), Num(r.WorldDeNovoEnvelope), Num(r.TotalEnvelope),
                    Fraction(r.TotalEnvelope), Opt(r.WorldDecoyRate), Int(r.WorldKnownCredited), Int(r.WorldKnownCreditedTrue),
                    Opt(r.WorldRightRate), Q(r.WorldEvidence), rule, Q(evidence.Status), Count(evidence.TargetPeptides), Count(evidence.DecoyPeptides))).Append('\n');
            }
            void Global(string label, double envelope) =>
                csv.Append(string.Join(",", Q("global"), Q(string.Empty), Q(label), Q(string.Empty), string.Empty, string.Empty, string.Empty,
                    Num(envelope), Fraction(envelope), string.Empty, string.Empty, string.Empty, string.Empty, Q(string.Empty), rule,
                    Q(string.Empty), string.Empty, string.Empty)).Append('\n');
            Global("identified, shared between families", SharedIdentifiedEnvelope);
            Global("de novo in the database, shared between families", SharedDatabaseDeNovoEnvelope);
            Global("de novo in the family world, shared between families", SharedEnvelope);
            Global("identified, in no family", UnattributedIdentifiedEnvelope);
            Global("de novo, unexplained", UnexplainedEnvelope);
            return csv.ToString();
        }

        /// <summary>
        /// The export: one row per family with its error beside it, then the rows that close on the unattributed
        /// envelope (family parts + shared + every other outcome = 1). Every row states the rules.
        /// </summary>
        public string BuildCsv()
        {
            string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
            string Opt(double? v) => v.HasValue ? Num(v.Value) : string.Empty;
            string Int(int v) => v.ToString(CultureInfo.InvariantCulture);
            string Count(int? v) => v.HasValue ? Int(v.Value) : "not recorded";
            string Fraction(double v) => ResidualEnvelope > 0 ? Num(v / ResidualEnvelope) : string.Empty;
            string Q(string v) => CsvFormatting.AlwaysQuoted(v);
            string rule = Q(Rules);
            var csv = new StringBuilder();
            csv.Append(string.Join(",", new[]
            {
                "row", "family", "envelope", "fraction_of_unattributed", "split_envelope", "credited_envelope_every_family",
                "peptides", "unique_peptides", "decoy_envelope", "decoy_rate", "known_credited", "known_credited_true",
                "right_rate", "evidence", "world_proteins_hit", "rule", "decoy_rate_status", "target_peptides", "decoy_peptides",
            }.Select(Q))).Append('\n');
            foreach (FamilyWorldFamilySummary f in Families)
                csv.Append(string.Join(",", Q("family"), Q(f.Family), Num(f.UniqueEnvelope), Fraction(f.UniqueEnvelope), Num(f.SplitEnvelope),
                    Num(f.CreditedEnvelope), Int(f.Peptides), Int(f.UniquePeptides), Num(f.DecoyEnvelope), Opt(f.DecoyRate),
                    Int(f.KnownCredited), Int(f.KnownCreditedTrue), Opt(f.RightRate), Q(f.Evidence), Int(f.WorldProteins.Count), rule,
                    Q(f.DecoyRateStatus), Int(f.Peptides), Count(f.DecoyPeptides))).Append('\n');
            foreach (FamilyWorldOutcomeSummary o in Outcomes.Where(o => o.Outcome != nameof(FamilyWorldOutcome.Credited)))
                csv.Append(string.Join(",", Q("closing"), Q(FamilyWorldErrors.OutcomeLabel(o.Outcome)), Num(o.Envelope), Fraction(o.Envelope),
                    string.Empty, string.Empty, Int(o.Peptides), string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                    string.Empty, string.Empty, string.Empty, rule, Q(string.Empty), string.Empty, string.Empty)).Append('\n');
            return csv.ToString();
        }
    }

    /// <summary>
    /// B6: the error beside every family-world claim. The decoy rate comes from the second pass itself; the right
    /// rate from the known answers, searched against the world without the sample's own species, under the very
    /// rules of the pass. A family with too few known credits shows that instead of a rate.
    /// </summary>
    public static class FamilyWorldErrors
    {
        /// <summary>Below this many known answers credited to a family, its right rate is not shown.</summary>
        public const int DefaultMinimumKnownCredits = 10;

        /// <summary>The shared bucket, stored among the outcomes so the stored rows close by themselves.</summary>
        public const string SharedOutcome = "Shared";

        public const string KnownAnswerMethod =
            "known answers v1: accepted de novo calls on scans the database search identified, at a compatible " +
            "precursor; the family is the UniProt family of the database proteins holding the identified peptide " +
            "(the proteins tied at the best score); the tag is searched in the family world without the sample's " +
            "own species under the pass's rules; a credit to a family is right when the identified peptide's " +
            "holders carry it; 2026-11-02";

        /// <summary>The quick estimate of one complete table.</summary>
        public static FamilyWorldLengthEstimate Estimate(FamilyWorldSummary table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            double credited = table.Outcomes.Where(o => o.Outcome == nameof(FamilyWorldOutcome.Credited)).Sum(o => o.Envelope);
            double decoy = table.Outcomes.Where(o => o.Outcome == nameof(FamilyWorldOutcome.Decoy)).Sum(o => o.Envelope);
            return new FamilyWorldLengthEstimate
            {
                MinimumTagLength = table.MinimumTagLength,
                ResidualEnvelope = table.ResidualEnvelope,
                CreditedEnvelope = credited,
                LandedFraction = table.ResidualEnvelope > 0 ? credited / table.ResidualEnvelope : 0,
                DecoyEnvelope = decoy,
                DecoyRate = FamilyWorldDecoyRates.Calculate(credited, decoy),
                TargetPeptides = table.Outcomes.Where(o => o.Outcome == nameof(FamilyWorldOutcome.Credited)).Sum(o => o.Peptides),
                DecoyPeptides = table.Outcomes.Where(o => o.Outcome == nameof(FamilyWorldOutcome.Decoy)).Sum(o => o.Peptides),
                KnownLanded = table.KnownLanded,
                KnownRight = table.KnownRight,
                RightRate = table.KnownLanded > 0 ? table.KnownRight / (double)table.KnownLanded : null,
                FamiliesMeasured = table.Families.Count(f => f.RightRate.HasValue),
            };
        }

        /// <summary>The label of an outcome in the export and on screen.</summary>
        public static string OutcomeLabel(string outcome) => outcome switch
        {
            SharedOutcome => "shared between families",
            nameof(FamilyWorldOutcome.Decoy) => "won by a reversed copy",
            nameof(FamilyWorldOutcome.OutOfScope) => "landed on families out of scope only",
            nameof(FamilyWorldOutcome.LandedNoFamily) => "landed on proteins with no family",
            nameof(FamilyWorldOutcome.TooShort) => "tag shorter than the minimum length",
            nameof(FamilyWorldOutcome.NotLanded) => "not found in the family world",
            nameof(FamilyWorldOutcome.Rejected) => "sequence not readable",
            _ => outcome,
        };

        /// <summary>The UniProt families of every protein a family-world file holds, by accession (the file's own
        /// entries first, then its panel), filled in from the sample curation's own evidence where the world lacks
        /// the protein.</summary>
        public static Dictionary<string, IReadOnlyList<string>> Labels(IEnumerable<FamilyWorldProtein> world, FamilyClusterFile sample)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            var labels = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (FamilyWorldProtein p in world) if (!labels.ContainsKey(p.Accession)) labels.Add(p.Accession, p.Families);
            foreach (FastaItemExtended item in sample.MyExtendedFastaItems ?? new List<FastaItemExtended>())
            {
                if (item?.Evidence == null) continue;
                string accession = FamilyWorld.BaseAccession(FamilyNaming.Accession(item.SequenceIdentifier));
                if (!labels.ContainsKey(accession)) labels.Add(accession, FamilyWorld.FamiliesOf(item.Evidence));
            }
            return labels;
        }

        /// <summary>The known answers of a project, in (tag, identified) order, each with its known families or the
        /// reason it is not one. Nothing is searched in the world here.</summary>
        public static List<FamilyWorldKnownAnswer> KnownAnswers(IEnumerable<SpectrumComparisonRow> rows, FamilyClusterFile sample,
            IReadOnlyDictionary<string, IReadOnlyList<string>> labels, OperationContext? operation = null)
            => KnownAnswersCore(rows, sample, labels, operation, null);

        public static List<FamilyWorldKnownAnswer> KnownAnswersFromResults(IEnumerable<SpectrumComparisonRow> rows, FamilyClusterFile sample,
            IReadOnlyDictionary<string, IReadOnlyList<string>> labels, IReadOnlyDictionary<string, HomologyResult> holders,
            OperationContext? operation = null)
            => KnownAnswersCore(rows, sample, labels, operation, holders ?? throw new ArgumentNullException(nameof(holders)));

        private static List<FamilyWorldKnownAnswer> KnownAnswersCore(IEnumerable<SpectrumComparisonRow> rows, FamilyClusterFile sample,
            IReadOnlyDictionary<string, IReadOnlyList<string>> labels, OperationContext? operation,
            IReadOnlyDictionary<string, HomologyResult>? replay)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (labels == null) throw new ArgumentNullException(nameof(labels));
            var pairs = new SortedDictionary<(string Tag, string Identified), FamilyWorldKnownAnswer>();
            foreach (SpectrumComparisonRow row in rows)
            {
                string identified = DeNovoReconciler.NormalizeSequence(row.PatternLabCleanSequence ?? string.Empty);
                if (identified.Length == 0) continue;
                void Add(string source, string clean, bool? accepted)
                {
                    string tag = DeNovoReconciler.NormalizeSequence(clean ?? string.Empty);
                    if (tag.Length == 0 || accepted != true) return;
                    if (!pairs.TryGetValue((tag, identified), out FamilyWorldKnownAnswer? pair))
                        pairs[(tag, identified)] = pair = new FamilyWorldKnownAnswer { Tag = tag, Identified = identified };
                    pair.Calls++;
                    pair.Sources.Add(source);
                }
                Add(DeNovoSource.DenovoOnly.DisplayName, row.PeaksDeNovoCleanSequence, row.PeaksDeNovoAccepted);
                Add(DeNovoSource.DeepNovo.DisplayName, row.DeepNovoCleanSequence, row.DeepNovoAccepted);
            }

            // The production identified rule on the sample's own database: the proteins tied at the best score.
            HomologyDatabase database = HomologyDatabase.Build(sample.MyExtendedFastaItems ?? new List<FastaItemExtended>());
            var options = new HomologyOptions { TopHitsPerPeptide = int.MaxValue, BestScoreOnly = true };
            string[] requested = pairs.Values.Select(p => p.Identified).Distinct(StringComparer.Ordinal).ToArray();
            IEnumerable<HomologyResult> found = replay == null
                ? HomologySearch.Search(requested, database, options, operation,
                    progressStage: "world-known-holder-queries", progressLabel: "Known-answer holders searched", progressBatchSize: 128)
                : requested.Select(sequence => replay.TryGetValue(sequence, out var result) && result.NormalizedPeptide == sequence
                    ? result : throw new ArgumentException("Replay lacks identified holder query: " + sequence, nameof(replay)));
            Dictionary<string, HomologyResult> holders = found.Where(r => !r.IsRejected).ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
            foreach (FamilyWorldKnownAnswer pair in pairs.Values)
            {
                if (!holders.TryGetValue(pair.Identified, out HomologyResult? r) || r.Hits.Count == 0 || r.Hits.Any(h => h.IsDecoy))
                {
                    pair.Exclusion = "no target holds the identified peptide";
                    continue;
                }
                if (r.Hits.Any(h => h.ProteinId.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)))
                {
                    pair.Exclusion = "a contaminant holds the identified peptide";
                    continue;
                }
                foreach (HomologyHit hit in r.Hits)
                {
                    string accession = FamilyWorld.BaseAccession(FamilyNaming.Accession(hit.ProteinId));
                    if (labels.TryGetValue(accession, out IReadOnlyList<string>? families) && families.Count > 0) pair.KnownFamilies.UnionWith(families);
                    else pair.UnlabelledHolders++;
                }
                if (pair.KnownFamilies.Count == 0) pair.Exclusion = "no protein holding it has a UniProt family";
            }
            return pairs.Values.ToList();
        }

        /// <summary>Searches every known tag in the world without the sample's species and credits it under the
        /// pass's rules. A tag under the minimum length is not searched.</summary>
        public static void Credit(IReadOnlyList<FamilyWorldKnownAnswer> answers, FamilyWorld withoutSpecies,
            IReadOnlyCollection<string> creditable, FamilyWorldOptions options, OperationContext? operation = null)
            => CreditCore(answers, withoutSpecies, creditable, options, operation, null);

        public static void CreditFromResults(IReadOnlyList<FamilyWorldKnownAnswer> answers, FamilyWorld withoutSpecies,
            IReadOnlyCollection<string> creditable, FamilyWorldOptions options, IReadOnlyDictionary<string, HomologyResult> results,
            OperationContext? operation = null)
            => CreditCore(answers, withoutSpecies, creditable, options, operation, results ?? throw new ArgumentNullException(nameof(results)));

        private static void CreditCore(IReadOnlyList<FamilyWorldKnownAnswer> answers, FamilyWorld withoutSpecies,
            IReadOnlyCollection<string> creditable, FamilyWorldOptions options, OperationContext? operation,
            IReadOnlyDictionary<string, HomologyResult>? replay)
        {
            if (answers == null) throw new ArgumentNullException(nameof(answers));
            if (withoutSpecies == null) throw new ArgumentNullException(nameof(withoutSpecies));
            if (creditable == null) throw new ArgumentNullException(nameof(creditable));
            if (options == null) throw new ArgumentNullException(nameof(options));
            List<FamilyWorldKnownAnswer> known = answers.Where(a => a.IsKnown).ToList();
            List<string> searched = known.Select(a => a.Tag).Where(t => t.Length >= options.MinimumTagLength).Distinct(StringComparer.Ordinal).ToList();
            var credits = new Dictionary<string, FamilyWorldCredit>(StringComparer.Ordinal);
            IEnumerable<HomologyResult> found = replay == null
                ? HomologySearch.Search(searched, withoutSpecies.Database, options.SearchOptions(), operation,
                    progressStage: "world-known-credit-queries", progressLabel: "Known-answer tags searched", progressBatchSize: 128)
                : searched.Select(sequence => replay.TryGetValue(sequence, out var result) && result.NormalizedPeptide == sequence
                    ? result : throw new ArgumentException("Replay lacks known-answer tag: " + sequence, nameof(replay)));
            foreach (HomologyResult r in found)
                credits[r.IsRejected ? r.Peptide : r.NormalizedPeptide] = withoutSpecies.Credit(r, creditable, options.MinimumTagLength);
            foreach (FamilyWorldKnownAnswer a in known)
                a.Credit = a.Tag.Length < options.MinimumTagLength
                    ? new FamilyWorldCredit { Outcome = FamilyWorldOutcome.TooShort }
                    : credits[a.Tag];
        }

        public const string CompositionRule =
            "first pass: the project's curated groups, each mapped to the UniProt families its members carry (a group " +
            "whose members carry none keeps its own row); a peptide reaching one family is that family's own, a peptide " +
            "reaching several goes to a shared row; fractions of the measured total";

        /// <summary>
        /// Completes the result table (B7): the first pass's peptides mapped from curated groups to UniProt families,
        /// beside the family-world part already in <paramref name="summary"/>. A peptide shared between curated groups
        /// that map to one UniProt family is that family's own. The rows close on the measured total: family parts,
        /// the three shared rows, the identified peptides in no family and the one unexplained de novo bucket.
        /// </summary>
        public static void Compose(FamilyWorldSummary summary, FamilyPeptideAttribution firstPass, FamilyClusterFile curation,
            IReadOnlyDictionary<string, IReadOnlyList<string>> labels)
        {
            if (summary == null) throw new ArgumentNullException(nameof(summary));
            if (firstPass == null) throw new ArgumentNullException(nameof(firstPass));
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            if (labels == null) throw new ArgumentNullException(nameof(labels));

            List<FastaItemExtended> targets = (curation.MyExtendedFastaItems ?? new List<FastaItemExtended>())
                .Where(i => i != null && !SequenceLabels.IsDecoy(i.SequenceIdentifier ?? string.Empty)).ToList();
            var groupFamilies = new Dictionary<int, SortedSet<string>>();
            foreach (FastaItemExtended item in targets)
            {
                if (!groupFamilies.TryGetValue(item.UserID, out SortedSet<string>? families)) groupFamilies[item.UserID] = families = FamilyWorld.NewFamilySet();
                string accession = FamilyWorld.BaseAccession(FamilyNaming.Accession(item.SequenceIdentifier));
                if (labels.TryGetValue(accession, out IReadOnlyList<string>? carried)) families.UnionWith(carried);
            }
            Dictionary<int, string> names = FamilyNaming.Names(targets);

            var rows = new Dictionary<string, FamilyWorldCompositionRow>(StringComparer.OrdinalIgnoreCase);
            FamilyWorldCompositionRow UniProtRow(string family) => rows.TryGetValue("u\u001f" + family, out FamilyWorldCompositionRow? row)
                ? row : rows["u\u001f" + family] = new FamilyWorldCompositionRow { Family = family, Kind = FamilyWorldCompositionRow.UniProtKind };
            FamilyWorldCompositionRow CuratedRow(int group) => rows.TryGetValue("c\u001f" + group.ToString(CultureInfo.InvariantCulture), out FamilyWorldCompositionRow? row)
                ? row : rows["c\u001f" + group.ToString(CultureInfo.InvariantCulture)] = new FamilyWorldCompositionRow
                {
                    // A group with no description (a contaminant entry, say) is named by its first member's identifier.
                    Family = names.TryGetValue(group, out string? name) && name.Length > 0 ? name
                        : targets.FirstOrDefault(t => t.UserID == group)?.SequenceIdentifier ?? "group " + group.ToString(CultureInfo.InvariantCulture),
                    Kind = FamilyWorldCompositionRow.CuratedKind,
                    CuratedUserIds = { group },
                };
            foreach (var (group, families) in groupFamilies.OrderBy(kv => kv.Key))
                foreach (string family in families)
                    UniProtRow(family).CuratedUserIds.Add(group);

            summary.CompositionRules = CompositionRule;
            summary.MeasuredIdentifiedEnvelope = 0;
            summary.MeasuredDeNovoEnvelope = 0;
            summary.SharedIdentifiedEnvelope = 0;
            summary.SharedDatabaseDeNovoEnvelope = 0;
            summary.UnattributedIdentifiedEnvelope = 0;
            foreach (FamilyAttributedPeptide attributed in firstPass.Peptides)
            {
                NativePeptideQuant p = attributed.Peptide;
                bool identified = string.Equals(p.Side, NativeQuantFractions.SideIdentified, StringComparison.Ordinal);
                double envelope = p.EnvelopeSum;
                if (identified) summary.MeasuredIdentifiedEnvelope += envelope;
                else summary.MeasuredDeNovoEnvelope += envelope;
                if (attributed.FamilyUserIds.Count == 0)
                {
                    if (identified) summary.UnattributedIdentifiedEnvelope += envelope;
                    continue; // an unattributed de novo peptide is the family world's
                }
                var reached = new List<FamilyWorldCompositionRow>();
                foreach (int group in attributed.FamilyUserIds)
                {
                    if (groupFamilies.TryGetValue(group, out SortedSet<string>? families) && families.Count > 0)
                        reached.AddRange(families.Select(UniProtRow));
                    else reached.Add(CuratedRow(group));
                }
                reached = reached.Distinct().ToList();
                if (reached.Count > 1)
                {
                    if (identified) summary.SharedIdentifiedEnvelope += envelope;
                    else summary.SharedDatabaseDeNovoEnvelope += envelope;
                    continue;
                }
                if (identified)
                {
                    reached[0].IdentifiedEnvelope += envelope;
                    reached[0].IdentifiedPeptides++;
                }
                else
                {
                    reached[0].DatabaseDeNovoEnvelope += envelope;
                    reached[0].DatabaseDeNovoPeptides++;
                }
            }
            foreach (FamilyWorldFamilySummary f in summary.Families)
            {
                FamilyWorldCompositionRow row = UniProtRow(f.Family);
                row.WorldDeNovoEnvelope = f.UniqueEnvelope;
                row.WorldDeNovoPeptides = f.UniquePeptides;
                row.WorldDecoyRate = f.DecoyRate;
                row.WorldDecoyRateUnavailableReason = f.DecoyRate.HasValue ? null : f.DecoyRateStatus;
                row.WorldTargetPeptides = f.Peptides;
                row.WorldDecoyPeptides = f.DecoyPeptides;
                row.WorldKnownCredited = f.KnownCredited;
                row.WorldKnownCreditedTrue = f.KnownCreditedTrue;
                row.WorldRightRate = f.RightRate;
                row.WorldEvidence = f.Evidence;
            }
            summary.UnexplainedEnvelope = summary.Outcomes
                .Where(o => o.Outcome != nameof(FamilyWorldOutcome.Credited) && o.Outcome != SharedOutcome).Sum(o => o.Envelope);
            summary.Composition = rows.Values
                .Where(r => r.TotalEnvelope > 0 || r.WorldKnownCredited > 0)
                .OrderByDescending(r => r.TotalEnvelope).ThenBy(r => r.Kind, StringComparer.Ordinal)
                .ThenBy(r => r.Family, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Family, StringComparer.Ordinal)
                .ThenBy(r => r.CuratedUserIds.FirstOrDefault()).ToList();
            foreach (FamilyWorldCompositionRow r in summary.Composition) r.CuratedUserIds.Sort();
        }

        /// <summary>The stored table: the pass, and per family in scope the decoy rate and the known-answer right
        /// rate, or what evidence it lacks.</summary>
        public static FamilyWorldSummary Summarize(FamilyWorldPassResult pass, IReadOnlyList<FamilyWorldKnownAnswer> answers,
            string worldRelease, IEnumerable<string> excludedTaxa, int minimumKnownCredits = DefaultMinimumKnownCredits)
        {
            if (pass == null) throw new ArgumentNullException(nameof(pass));
            if (answers == null) throw new ArgumentNullException(nameof(answers));
            if (minimumKnownCredits < 1) throw new ArgumentOutOfRangeException(nameof(minimumKnownCredits));
            var summary = new FamilyWorldSummary
            {
                Method = pass.Method,
                Science = pass.Science?.Clone(),
                KnownAnswerMethod = KnownAnswerMethod,
                Rules = FamilyWorldPass.RuleText(pass),
                MinimumTagLength = pass.MinimumTagLength,
                MinIdentity = pass.MinIdentity,
                ScopeFamilies = pass.CreditableFamilies.ToList(),
                WorldFingerprint = pass.WorldFingerprint,
                WorldRelease = worldRelease ?? string.Empty,
                WorldProteins = pass.WorldProteins,
                KnownAnswerExcludedTaxa = (excludedTaxa ?? Enumerable.Empty<string>()).Distinct(StringComparer.Ordinal).OrderBy(t => t, StringComparer.Ordinal).ToList(),
                MinimumKnownCredits = minimumKnownCredits,
                ResidualEnvelope = pass.ResidualEnvelope,
                ResidualPeptides = pass.ResidualPeptides,
                SharedEnvelope = pass.SharedEnvelope,
            };
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>())
                summary.Outcomes.Add(new FamilyWorldOutcomeSummary { Outcome = o.ToString(), Envelope = pass.Envelope(o), Peptides = pass.Peptides(o) });
            summary.Outcomes.Add(new FamilyWorldOutcomeSummary
            {
                Outcome = SharedOutcome,
                Envelope = pass.SharedEnvelope,
                Peptides = pass.Sequences.Where(s => s.Credit.Outcome == FamilyWorldOutcome.Credited && s.Credit.Families.Count > 1).Sum(s => s.Peptides),
            });

            List<FamilyWorldKnownAnswer> known = answers.Where(a => a.IsKnown && a.Credit != null).ToList();
            summary.KnownPairs = known.Count;
            summary.KnownLanded = known.Count(a => a.Landed);
            summary.KnownRight = known.Count(a => a.Right);

            var scope = new HashSet<string>(pass.CreditableFamilies, StringComparer.OrdinalIgnoreCase);
            var rows = new Dictionary<string, FamilyWorldFamilySummary>(StringComparer.OrdinalIgnoreCase);
            FamilyWorldFamilySummary Row(string family) => rows.TryGetValue(family, out FamilyWorldFamilySummary? row)
                ? row : rows[family] = new FamilyWorldFamilySummary { Family = family };
            foreach (FamilyWorldFamilyRow f in pass.Families)
            {
                FamilyWorldFamilySummary row = Row(f.Family);
                row.UniqueEnvelope = f.UniqueEnvelope;
                row.SplitEnvelope = f.SplitEnvelope;
                row.CreditedEnvelope = f.CreditedEnvelope;
                row.Peptides = f.Peptides;
                row.UniquePeptides = f.UniquePeptides;
                row.DecoyEnvelope = f.DecoyEnvelope;
                row.DecoyPeptides = f.DecoyPeptides;
                row.WorldProteins = f.Proteins.ToList();
            }
            foreach (FamilyWorldKnownAnswer a in known)
            {
                foreach (string family in a.KnownFamilies.Where(scope.Contains))
                {
                    FamilyWorldFamilySummary row = Row(family);
                    row.KnownPairs++;
                    if (a.Landed) row.KnownLanded++;
                    if (a.Right) row.KnownRight++;
                }
                if (!a.Landed) continue;
                foreach (string family in a.Credit!.Families)
                {
                    FamilyWorldFamilySummary row = Row(family);
                    row.KnownCredited++;
                    if (a.KnownFamilies.Contains(family)) row.KnownCreditedTrue++;
                }
            }
            foreach (FamilyWorldFamilySummary row in rows.Values)
            {
                row.DecoyRate = FamilyWorldDecoyRates.Calculate(row.CreditedEnvelope, row.DecoyEnvelope);
                row.DecoyPeptides ??= 0;
                if (row.KnownCredited >= minimumKnownCredits)
                {
                    row.RightRate = row.KnownCreditedTrue / (double)row.KnownCredited;
                    row.Evidence = "measured";
                }
                else row.Evidence = row.KnownCredited == 0
                    ? "no known answer"
                    : "too little evidence: " + row.KnownCredited.ToString(CultureInfo.InvariantCulture) + " known credits";
            }
            summary.Families.AddRange(rows.Values.OrderByDescending(r => r.CreditedEnvelope)
                .ThenBy(r => r.Family, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Family, StringComparer.Ordinal));
            return summary;
        }
    }
}
