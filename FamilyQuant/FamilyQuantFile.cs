using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace FamilyQuant
{
    public class FamilyClusterFile
    {

        Dictionary<string, List<string>> ProteinPeptideDictionary { get; set; } = new Dictionary<string, List<string>>();

        /// <summary>
        /// Peptide name -> sparse-matrix index id. Replaces a linear Find over every index entry that
        /// used to run once per peptide per family (O(families x peptides x indexes)).
        /// </summary>
        Dictionary<string, int> PeptideIndexIds { get; set; } = new Dictionary<string, int>();

        /// <summary>
        /// Per-peptide XIC totals memoised for the currently loaded PLP. Peptides shared between families
        /// - the normal case in a redundant venom database - were otherwise re-extracted from the sparse
        /// matrix once per family, and again for each of the two groupings (USearch and User).
        /// </summary>
        Dictionary<int, double> PeptideXicTotals { get; set; } = new Dictionary<int, double>();
        Dictionary<int, double> PeptideNormalizedXicTotals { get; set; } = new Dictionary<int, double>();

        /// <summary>
        /// Per-peptide totals with each RUN rescaled to a different denominator (Phase 4). Separate from
        /// <see cref="PeptideXicTotals"/> on purpose: that cache holds the file's own values and must
        /// stay untouched, so the unrescaled path keeps producing exactly the numbers it always did.
        /// </summary>
        Dictionary<int, double> PeptideRescaledTotals { get; set; } = new Dictionary<int, double>();

        /// <summary>Which denominator <see cref="PeptideRescaledTotals"/> was built for; null when unbuilt.</summary>
        string? RescaledTotalsTag { get; set; }

        /// <summary>
        /// The project the lookups and caches above were built from. Everything cached is only valid for
        /// that project, so it is compared before use rather than trusting callers to re-run
        /// <see cref="SetLookupDictionary"/> after switching PLPs.
        /// </summary>
        PatternLabProject? LookupSource { get; set; }

        public int ClusteringIdentity { get; set; }

        /// <summary>SHA-256 of the FASTA/.T-R content used to create this clustering.</summary>
        public string SourceSha256 { get; set; } = string.Empty;

        /// <summary>Number of FASTA records in the source at clustering time.</summary>
        public int SourceSequenceCount { get; set; }

        /// <summary>Algorithm semantics, not merely the executable name.</summary>
        public string ClusteringMethod { get; set; } = string.Empty;

        /// <summary>
        /// Cosine threshold of the last word-based reclustering applied to these families, or null if the
        /// grouping is still the raw USEARCH one. Persisted, so reopening a file shows how its families
        /// were built instead of silently looking like a plain identity clustering.
        /// </summary>
        public double? WordSimilarity { get; set; }

        /// <summary>Set when the families were merged by keyword rather than by a similarity threshold.</summary>
        public bool IsKeywordSuperCluster { get; set; }

        /// <summary>
        /// Families the user curated BY HAND, as UserIDs. Automatic reclustering must never merge a
        /// locked family into anything: the bench report that forced this was a phospholipase A2
        /// inhibitor separated manually from the toxin and re-merged by the next word reclustering,
        /// legitimately under its own similarity rules, because the two DESCRIPTIONS are nearly
        /// identical - which is exactly why the split had to be manual in the first place.
        /// Persisted with the file, so the protection survives a save and reload.
        /// </summary>
        public HashSet<int> LockedUserIds { get; set; } = new HashSet<int>();

        /// <summary>Where the proteins' domains came from, such as "UniProt 2026_03"; empty until fetched.
        /// Annotation changes between releases, so the file records which one its domains are.</summary>
        public string DomainSource { get; set; } = string.Empty;

        public List<FastaItemExtended> MyExtendedFastaItems { get; set; } = new List<FastaItemExtended>();

        /// <summary>How the current grouping was produced, for display.</summary>
        public string GroupingDescription()
        {
            if (IsKeywordSuperCluster) return "keywords";
            if (WordSimilarity.HasValue) return WordSimilarity.Value.ToString("0.##", CultureInfo.CurrentCulture);
            return "none";
        }

        /// <summary>
        /// The families as they currently stand, on the curated grouping.
        ///
        /// This grouped by USearchID. That field now tracks the last automatic grouping (it moves with
        /// reclustering for unlocked families and diverges from UserID only where a family was
        /// renumbered by hand), so grouping by it would report the automatic grouping rather than the
        /// curated one that CalculateGroupingScore and CalculateFDR are meant to score. The curated
        /// grouping is UserID, so this groups by UserID.
        /// </summary>
        [JsonIgnore]
        public List<IGrouping<int, FastaItemExtended>> GroupedFastaItems
        {
            get
            {
                return (from mf in MyExtendedFastaItems
                        group mf by mf.UserID into thegroups
                        select thegroups).ToList();
            }
        }

        public FamilyClusterFile()
        {

        }

        /// <summary>
        /// Rebuilds the lookups if this is not the project they were built from, so a stale cache can
        /// never survive a change of PLP.
        /// </summary>
        private void EnsureLookupsFor(PatternLabProject MyPLP)
        {
            if (!ReferenceEquals(LookupSource, MyPLP))
            {
                SetLookupDictionary(MyPLP);
            }
        }

        /// <summary>
        /// Non-redundant peptides mapping to any protein of the family. A peptide shared between the
        /// family's members must be counted once, not once per protein.
        /// </summary>
        private List<string> GetPeptidesInGroup(IGrouping<int, FastaItemExtended> theGroup)
        {
            List<string> peptidesInGroup = new List<string>();

            foreach (var item in theGroup)
            {
                if (ProteinPeptideDictionary.TryGetValue(item.SequenceIdentifier, out var mapped))
                {
                    peptidesInGroup.AddRange(mapped);
                }
            }

            // Distinct() keeps first-occurrence order, so the summation order - and with it the exact
            // floating-point total - is the same as before this was factored out.
            return peptidesInGroup.Distinct().ToList();
        }

        /// <summary>
        /// How many families of the given grouping each peptide maps to. A peptide counted in every
        /// family it touches makes the families overlap, so this is what the shared-peptide policy uses
        /// to keep the column additive.
        /// </summary>
        private Dictionary<string, int> CountFamiliesPerPeptide(List<IGrouping<int, FastaItemExtended>> groupedItems)
        {
            var counts = new Dictionary<string, int>();

            foreach (var group in groupedItems)
            {
                foreach (string pep in GetPeptidesInGroup(group))
                {
                    counts[pep] = counts.TryGetValue(pep, out int c) ? c + 1 : 1;
                }
            }

            return counts;
        }

        /// <summary>
        /// Builds the protein -> peptides map and the peptide -> index-id map for a freshly loaded PLP,
        /// and drops any memoised XIC values from a previous project.
        /// </summary>
        public void SetLookupDictionary(PatternLabProject MyPLP)
        {
            Console.WriteLine("=== Setting up lookup dictionaries ===");

            ProteinPeptideDictionary = new Dictionary<string, List<string>>();
            PeptideIndexIds = new Dictionary<string, int>();
            PeptideXicTotals = new Dictionary<int, double>();
            PeptideNormalizedXicTotals = new Dictionary<int, double>();
            PeptideRescaledTotals = new Dictionary<int, double>();
            RescaledTotalsTag = null;
            LookupSource = MyPLP;

            Console.WriteLine($"PLP has {MyPLP.MyIndex.TheIndexes.Count} index entries");

            int duplicateNames = 0;

            foreach (var item in MyPLP.MyIndex.TheIndexes)
            {
                string[] locci;

                if (item.Description.Contains("Mappable proteins : "))
                {
                    string mappable = Regex.Split(item.Description, "Mappable proteins : ")[1];
                    locci = Regex.Split(mappable, ", ");
                }
                else
                {
                    locci = Regex.Split(item.Description, " ").Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
                }

                // Keep the FIRST entry for a repeated peptide name, matching the old List.Find behaviour.
                // A duplicate used to throw out of Add() and abort the whole PLP load.
                if (!PeptideIndexIds.ContainsKey(item.Name))
                {
                    PeptideIndexIds.Add(item.Name, item.ID);
                }
                else
                {
                    duplicateNames++;
                }

                foreach (string locus in locci)
                {
                    if (ProteinPeptideDictionary.TryGetValue(locus, out var peptides))
                    {
                        peptides.Add(item.Name);
                    }
                    else
                    {
                        ProteinPeptideDictionary.Add(locus, new List<string>() { item.Name });
                    }
                }
            }

            Console.WriteLine($"ProteinPeptideDictionary created with {ProteinPeptideDictionary.Count} proteins");
            Console.WriteLine($"PeptideIndexIds created with {PeptideIndexIds.Count} peptides");

            if (duplicateNames > 0)
            {
                Console.WriteLine($"Note: {duplicateNames} repeated peptide name(s) in the index; the first occurrence was kept.");
            }
        }

        public void AttributeXICToGroups(PatternLabProject MyPLP, GroupingType groupingType)
        {
            AttributeXICToGroups(MyPLP, groupingType, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
        }

        public void AttributeXICToGroups(PatternLabProject MyPLP, GroupingType groupingType, NormalizationMode mode)
        {
            AttributeXICToGroups(MyPLP, groupingType, mode, SharedPeptidePolicy.FamilyUnique);
        }

        /// <summary>
        /// Fills the XIC column for every family.
        ///
        /// <see cref="NormalizationMode.IdentifiedSignal"/> divides each family by the total over all
        /// families, so the column adds up to 1. <see cref="NormalizationMode.TotalChromatogram"/> leaves
        /// each family as its own share of the run's chromatogram, so the column adds up to the part of
        /// the chromatogram that was identified AND assigned - never to 1, the remainder being the
        /// unidentified sample plus, under <see cref="SharedPeptidePolicy.FamilyUnique"/>, the shared
        /// signal that no single family owns.
        ///
        /// The policy matters more than it looks: counting a shared peptide in every family it touches
        /// makes the families overlap, and on a redundant venom database that pushes the column's total
        /// well past 100% of the chromatogram. Only the additive policies may be read as a composition.
        /// </summary>
        public void AttributeXICToGroups(PatternLabProject MyPLP, GroupingType groupingType,
                                         NormalizationMode mode, SharedPeptidePolicy sharedPolicy)
        {
            EnsureLookupsFor(MyPLP);

            // PHASE 4. A mode that names a denominator may need the file's values re-expressed under it.
            //
            // When the file is ALREADY under that denominator the plan requires no rescaling and every
            // number below is computed exactly as it was before Phase 4 - that is what keeps the
            // historical TIC behaviour bit-identical. Only a genuine conversion touches the values.
            //
            // An unavailable plan is NOT silently downgraded here: the UI greys the item out, and
            // anything that reaches this point with an impossible mode falls back to unscaled values
            // rather than inventing a conversion.
            NormalizationPlan? plan = null;
            string? targetTag = mode.ToDenominatorTag();

            if (targetTag != null)
            {
                plan = NormalizationPlan.For(MyPLP, targetTag);
                if (!plan.IsAvailable || !plan.RequiresRescaling) plan = null;
            }

            var groupedItems = groupingType == GroupingType.USearch
                ? (from mf in MyExtendedFastaItems
                   group mf by mf.USearchID into thegroups
                   select thegroups).ToList()
                : (from mf in MyExtendedFastaItems
                   group mf by mf.UserID into thegroups
                   select thegroups).ToList();

            var familiesPerPeptide = CountFamiliesPerPeptide(groupedItems);

            // First pass: calculate all XICs
            var groupXICs = new Dictionary<int, double>();
            double totalXIC = 0;

            foreach (var group in groupedItems)
            {
                double groupXIC = GetGroupXIC(group, MyPLP, familiesPerPeptide, sharedPolicy, plan);
                groupXICs[group.Key] = groupXIC;
                totalXIC += groupXIC;
            }

            if (totalXIC <= 0)
            {
                Console.WriteLine("Warning: no peptide of this database was found in the project - every family scored 0.");
            }

            // In any denominator mode the value is already a fraction of one run's signal, so the only
            // adjustment is averaging over the runs: a peptide seen in three runs contributed three
            // per-run fractions, and a run where it was absent contributed a true zero.
            //
            // `targetTag != null` is exactly "this mode names a denominator", which now covers the two
            // Phase 4 modes as well. The run count is still QuantitationProfile.FileCount, unchanged, so
            // the TIC mode divides by the same number it always did.
            bool denominatorMode = targetTag != null;

            int runs = denominatorMode
                ? Math.Max(1, QuantitationProfile.Describe(MyPLP).FileCount)
                : 1;

            // Second pass: normalize and assign. A zero total means nothing matched (a protein-level PLP,
            // or a database unrelated to the project); zero the column instead of leaving the previous
            // project's abundances in place, which then got saved into the .fcf as if they were current.
            foreach (var group in groupedItems)
            {
                double value;

                if (totalXIC <= 0)
                    value = 0;
                else if (denominatorMode)
                    value = groupXICs[group.Key] / runs;
                else
                    value = groupXICs[group.Key] / totalXIC;

                foreach (FastaItemExtended item in group)
                {
                    if (groupingType == GroupingType.USearch)
                        item.XIC = value;
                    else
                        item.XICUser = value;
                }
            }
        }

        public double GetGroupXIC(IGrouping<int, FastaItemExtended> theGroup, PatternLabProject MyPLP)
        {
            return GetGroupXIC(theGroup, MyPLP, null, SharedPeptidePolicy.CountInEveryFamily);
        }

        /// <summary>
        /// Total quantitation of one family. <paramref name="familiesPerPeptide"/> is required by every
        /// policy except <see cref="SharedPeptidePolicy.CountInEveryFamily"/>, which needs no weighting.
        /// </summary>
        public double GetGroupXIC(IGrouping<int, FastaItemExtended> theGroup, PatternLabProject MyPLP,
                                  Dictionary<string, int>? familiesPerPeptide, SharedPeptidePolicy sharedPolicy)
        {
            return GetGroupXIC(theGroup, MyPLP, familiesPerPeptide, sharedPolicy, null);
        }

        /// <summary>
        /// As above, with an optional <paramref name="plan"/> that re-expresses each run's values under a
        /// different denominator before they are summed (Phase 4). A null plan means "use the file's own
        /// values", which is the pre-Phase-4 behaviour exactly.
        /// </summary>
        public double GetGroupXIC(IGrouping<int, FastaItemExtended> theGroup, PatternLabProject MyPLP,
                                  Dictionary<string, int>? familiesPerPeptide, SharedPeptidePolicy sharedPolicy,
                                  NormalizationPlan? plan)
        {
            EnsureLookupsFor(MyPLP);

            if (plan != null) EnsureRescaledTotals(MyPLP, plan);

            double xic = 0;

            foreach (string pep in GetPeptidesInGroup(theGroup))
            {
                if (!PeptideIndexIds.TryGetValue(pep, out int indexId)) continue;

                double weight = 1;

                if (sharedPolicy != SharedPeptidePolicy.CountInEveryFamily && familiesPerPeptide != null)
                {
                    int families = familiesPerPeptide.TryGetValue(pep, out int f) ? f : 1;

                    if (families > 1)
                    {
                        if (sharedPolicy == SharedPeptidePolicy.FamilyUnique) continue;
                        weight = 1.0 / families;   // SplitEqually
                    }
                }

                double peptideXic;

                if (plan != null)
                {
                    // Rescaled totals are precomputed for every dim in one pass over the matrix, so a
                    // peptide absent from the table genuinely contributed nothing.
                    PeptideRescaledTotals.TryGetValue(indexId, out peptideXic);
                }
                else if (!PeptideXicTotals.TryGetValue(indexId, out peptideXic))
                {
                    peptideXic = MyPLP.MySparseMatrix.ExtractDimValues(indexId, 0, true).Sum();
                    PeptideXicTotals[indexId] = peptideXic;
                }

                xic += weight * peptideXic;
            }

            return xic;
        }

        /// <summary>
        /// Builds, once per denominator, the per-peptide totals with each RUN scaled by the plan.
        ///
        /// ONE PASS OVER THE ROWS, not one extraction per peptide. The scale belongs to a ROW, so the
        /// natural loop is rows-then-values, and it visits each stored value exactly once. Doing it the
        /// other way round - per peptide, per row - would reintroduce the O(dims x rows) sweep that
        /// SparseMatrix.ExtractDimValues was optimised to avoid.
        ///
        /// Rows are visited in matrix order and each dim accumulates in that same order, which is the
        /// order ExtractDimValues(...).Sum() would have used. With a scale of 1 the result is therefore
        /// identical down to the floating-point rounding, not merely close.
        /// </summary>
        private void EnsureRescaledTotals(PatternLabProject MyPLP, NormalizationPlan plan)
        {
            if (RescaledTotalsTag == plan.TargetTag && PeptideRescaledTotals.Count > 0) return;

            var totals = new Dictionary<int, double>();
            var rows = MyPLP.MySparseMatrix.theMatrixInRows;
            double[]? scales = plan.ScaleByRow;

            for (int r = 0; r < rows.Count; r++)
            {
                // A plan built for a different matrix would index out of range; treat a short scale
                // array as "no scaling for this row" rather than throwing inside a recalculation.
                double s = scales != null && r < scales.Length ? scales[r] : 1.0;

                foreach (var cell in rows[r].TheData)
                {
                    totals.TryGetValue(cell.Dim, out double running);
                    totals[cell.Dim] = running + cell.Value * s;
                }
            }

            PeptideRescaledTotals = totals;
            RescaledTotalsTag = plan.TargetTag;
        }

        /// <summary>
        /// The serialization options every curation write uses, so a .fcf on disk and the curation.json
        /// entry of a .fqproj are byte-for-byte the same text for the same file. Shared, not re-created
        /// per call, precisely so the two paths cannot drift.
        /// </summary>
        public static readonly JsonSerializerOptions CurationJsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        /// <summary>Serialises the curation exactly as <see cref="Save"/> writes it. The .fqproj container
        /// stores this text verbatim in its curation.json entry, so nothing about curation changes when a
        /// project is saved as a container instead of a plain file.</summary>
        public string ToJsonText() => JsonSerializer.Serialize(this, CurationJsonOptions);

        public static FamilyClusterFile Load(string fileName)
        {
            string theFile = File.ReadAllText(fileName);

            try
            {
                return FromJsonText(theFile);
            }
            catch (InvalidDataException)
            {
                // Re-raise with the file name the caller can act on; the JSON-shaped failures keep their
                // own type so the corrupt-file path still throws loudly.
                throw new InvalidDataException("\"" + fileName + "\" is not a valid Family Cluster File.");
            }
        }

        /// <summary>
        /// Deserialises curation from its JSON text and applies the pre-lock migration. Both
        /// <see cref="Load"/> (reading a .fcf) and the .fqproj container (reading its curation.json entry)
        /// go through here, so a project opened either way arrives with identical families, locks and
        /// flags, migration included.
        /// </summary>
        public static FamilyClusterFile FromJsonText(string theFile)
        {
            FamilyClusterFile loaded = JsonSerializer.Deserialize<FamilyClusterFile>(theFile)
                ?? throw new InvalidDataException("The text is not a valid Family Cluster File.");

            // Migration for files written before curation locks existed - but ONLY for files that
            // never went through a reclustering. In a never-reclustered file the two ids start equal,
            // so a divergent row can only mean the USER typed it: that is curation, and it must
            // arrive protected. Both sides of the split are locked, matching what the live edit
            // handler records.
            //
            // The reclustering condition is not decoration. Build 1.0.8.0 already wrote only UserID
            // and renumbered clusters densely, so a file saved by it AFTER a word reclustering
            // diverges on nearly every row - and inferring curation from that would lock nearly every
            // family, permanently and invisibly, trading the reported defects for a worse one. The
            // final review before deploy caught exactly this. WordSimilarity and
            // IsKeywordSuperCluster are persisted by every build that could recluster, so they are
            // the reliable witnesses. The residual, documented in the release notes: curation typed
            // into a 1.0.8.0 file AFTER a reclustering cannot be told apart from the renumbering and
            // does not arrive locked - re-typing the number once in the current build locks it.
            if (!theFile.Contains("\"LockedUserIds\"")
                && loaded.WordSimilarity == null && !loaded.IsKeywordSuperCluster)
            {
                foreach (var item in loaded.MyExtendedFastaItems)
                {
                    if (item.UserID != item.USearchID)
                    {
                        loaded.LockedUserIds.Add(item.UserID);
                        loaded.LockedUserIds.Add(item.USearchID);
                    }
                }
            }

            return loaded;
        }

        public void Save(string fileName)
        {
            // Written to a temporary file and moved into place, so a process killed, a power loss or a
            // full disk mid-write leaves the previous .fcf intact rather than a truncated one that
            // FromJsonText would reject, losing all curation. Mirrors ProjectStore.WriteFqproj. The shared
            // options keep a .fcf and a .fqproj's curation.json byte-identical for the same file.
            string temp = fileName + ".tmp";
            File.WriteAllText(temp, ToJsonText());
            File.Move(temp, fileName, overwrite: true);
        }

        /// <summary>
        /// Splits the current families into target-only, decoy-only and mixed.
        /// </summary>
        public GroupingScore CalculateGroupingScore(string decoyTag = "Reverse_")
        {
            var score = new GroupingScore();

            foreach (var group in GroupedFastaItems)
            {
                int decoys = group.Count(a => (a.SequenceIdentifier ?? string.Empty).StartsWith(decoyTag));

                score.TotalGroups++;

                if (decoys == 0)
                    score.TargetOnlyGroups++;
                else if (decoys == group.Count())
                    score.DecoyOnlyGroups++;
                else
                {
                    score.MixedGroups++;
                    score.MixedFamilies.Add(MixedFamily.From(group.Key, group, decoyTag));
                }
            }

            // Smallest first: the family that holds a floor up on its own is usually one short peptide.
            score.MixedFamilies = score.MixedFamilies
                .OrderBy(f => f.Targets.Count + f.Decoys.Count)
                .ThenBy(f => f.Targets.FirstOrDefault() ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            return score;
        }

        /// <summary>
        /// Fraction of families that mix target and decoy sequences.
        ///
        /// This is NOT a statistical FDR and it does NOT measure over-merging. A reversed database
        /// reproduces the target database's internal redundancy - if A and B are similar then Reverse_A
        /// and Reverse_B are similar too - so decoy families collapse at exactly the same rate as target
        /// families and putting two decoys together is not an error. The only event this can see is a
        /// target aligning to a decoy, which marks the alignment twilight zone: the identity below which
        /// alignments are no better than noise. Fusions of two unrelated TARGET families are invisible
        /// to it. Treat it as a floor ("do not cluster below this identity"), never as a budget to spend.
        /// </summary>
        public double CalculateFDR(string decoyTag = "Reverse_")
        {
            GroupingScore score = CalculateGroupingScore(decoyTag);

            Console.WriteLine("Total groups = {0}, Incorrect Grouping = {1}", score.TotalGroups, score.MixedGroups);

            return score.Score;
        }

        /// <summary>
        /// Clusters the database across a range of identities and reports the groups and the mixing at
        /// each one, so a threshold can be chosen from the whole curve instead of a single optimiser
        /// answer. Runs USEARCH once per step, from the strictest identity down to the loosest.
        /// </summary>
        public static List<IdentityScanRow> ScanIdentities(string fileName, int from = 95, int to = 10, int step = 5, string decoyTag = "Reverse_", bool exhaustive = true)
        {
            if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Step must be positive.");
            if (from < to) throw new ArgumentException("'from' must be greater than or equal to 'to'.", nameof(from));

            // USEARCH takes an identity in (0, 100]; -id 0 is not a clustering, it is an error.
            if (from > 100) throw new ArgumentOutOfRangeException(nameof(from), "Identity cannot exceed 100%.");
            if (to < 1) throw new ArgumentOutOfRangeException(nameof(to), "Identity must be at least 1%.");

            var fi = new FileInfo(fileName);
            var rows = new List<IdentityScanRow>();

            for (int identity = from; identity >= to; identity -= step)
            {
                FamilyClusterFile clustered = USearchTools.GenerateFCF(fi, identity, exhaustive);
                GroupingScore score = clustered.CalculateGroupingScore(decoyTag);
                int nonContaminantTargets = clustered.GroupedFastaItems.Count(g => g.Any(i =>
                    !(i.SequenceIdentifier ?? string.Empty).StartsWith(decoyTag, StringComparison.Ordinal)
                    && !(i.SequenceIdentifier ?? string.Empty).StartsWith("contaminant_", StringComparison.OrdinalIgnoreCase)));

                rows.Add(new IdentityScanRow()
                {
                    Identity = identity,
                    Groups = score.TotalGroups,
                    TargetGroups = score.TargetOnlyGroups + score.MixedGroups,
                    NonContaminantTargetGroups = nonContaminantTargets,
                    MixedGroups = score.MixedGroups,
                    Score = score.Score,
                    TargetRelativeScore = score.TargetRelativeScore,
                    MixedFamilies = score.MixedFamilies
                });

                Console.WriteLine("Identity {0}% -> {1} groups, {2} mixed", identity, score.TotalGroups, score.MixedGroups);
            }

            return rows;
        }

        /// <summary>
        /// Finds the LOWEST USEARCH sequence-identity threshold (0-100) whose family grouping keeps the
        /// target/decoy mixing score at or below <paramref name="acceptableScore"/>. Lower identity
        /// yields fewer, larger families but more mixing, so the constraint holds over an upper interval
        /// of identities and we binary-search for its lower edge (one USEARCH run per probe).
        ///
        /// The default of 0 stops at the last identity showing NO target/decoy mixing at all, i.e. just
        /// above the alignment twilight zone. Any budget above 0 walks the search straight INTO that
        /// zone: the score only leaves zero once alignments stop being better than noise, so a non-zero
        /// allowance is spent buying biologically meaningless merges. See <see cref="CalculateFDR"/> for
        /// why this score cannot see the merges that actually matter, and use <see cref="ScanIdentities"/>
        /// to inspect the whole curve before committing to a threshold.
        ///
        /// With <paramref name="acceptableScore"/> above 0 the answer is a heuristic: the score is a
        /// RATIO, and lowering the identity can merge two already-mixed families into one, dropping
        /// numerator and denominator together so the score falls (2/100 -> 1/99). The satisfying set is
        /// then not a single upper interval and the bisection can settle on either edge. At the default
        /// of 0 the predicate reduces to "no mixed family at all", which has no such hole.
        /// </summary>
        public static double DivideAndConquer(string fileName, double acceptableScore = 0.0, string decoyTag = "Reverse_", bool exhaustive = true)
        {
            return DivideAndConquerDetailed(fileName, acceptableScore, decoyTag, exhaustive).Identity;
        }

        /// <summary>
        /// <see cref="DivideAndConquer"/>, also returning the score of the identity just below the answer:
        /// the families that mix there are WHY the search stopped where it did. One short peptide matching
        /// its own reversed decoy can set the floor on its own, and a bare number cannot show that.
        /// </summary>
        public static DivideAndConquerResult DivideAndConquerDetailed(string fileName, double acceptableScore = 0.0, string decoyTag = "Reverse_", bool exhaustive = true)
        {
            var fi = new FileInfo(fileName);

            int lo = 0;     // assumed to FAIL the constraint (loosest clustering)
            int hi = 100;   // strictest clustering, expected to SATISFY the constraint

            // Every failing probe, so the one at the final lo (always hi - 1, unless lo never moved off
            // the unprobed 0) can be handed back without another USEARCH run.
            var failed = new Dictionary<int, GroupingScore>();

            GroupingScore strictest = Probe(fi, hi, decoyTag, exhaustive);

            // A database with no decoys scores 0 at EVERY identity, so the search would walk all the way
            // down to 1% and report it as "no mixing found". That is a missing measurement, not a result.
            if (strictest.DecoyOnlyGroups + strictest.MixedGroups == 0)
            {
                throw new InvalidOperationException(
                    "This database contains no decoy sequences (identifiers starting with \"" + decoyTag +
                    "\"), so the target/decoy score is 0 at every identity and cannot single one out.\n\n" +
                    "Use a target-decoy database (.T-R), or choose the threshold from 'Scan identity range' " +
                    "and the biology of your proteins.");
            }

            // If even the strictest clustering can't meet the target, nothing will.
            if (strictest.Score > acceptableScore)
            {
                throw new InvalidOperationException(
                    "Clustering at 100% identity already mixes target and decoy sequences (score " +
                    strictest.Score.ToString("P2") + "), so no identity threshold can reach the requested " +
                    "limit of " + acceptableScore.ToString("P2") + ". Check that the decoy prefix is \"" +
                    decoyTag + "\" and that the database is not duplicated.");
            }

            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                GroupingScore probe = Probe(fi, mid, decoyTag, exhaustive);

                if (probe.Score <= acceptableScore)
                    hi = mid;   // satisfied: try to go lower (fewer groups)
                else
                {
                    lo = mid;   // failed: need stricter clustering
                    failed[mid] = probe;
                }
            }

            Console.WriteLine("Optimum identity: {0}% (lowest identity with score <= {1:P2})", hi, acceptableScore);

            failed.TryGetValue(lo, out GroupingScore? below);
            return new DivideAndConquerResult
            {
                Identity = hi,
                IdentityBelow = below != null ? lo : (int?)null,
                ScoreBelow = below,
            };
        }

        private static GroupingScore Probe(FileInfo fi, int identity, string decoyTag, bool exhaustive = true)
        {
            GroupingScore score = USearchTools.GenerateFCF(fi, identity, exhaustive).CalculateGroupingScore(decoyTag);

            Console.WriteLine(
                "Identity {0}% -> {1} groups ({2} with targets, {3} mixed), score {4:P2} (of target families {5:P2})",
                identity, score.TotalGroups, score.TargetOnlyGroups + score.MixedGroups, score.MixedGroups,
                score.Score, score.TargetRelativeScore);

            return score;
        }
    }
}
