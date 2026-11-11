using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>The whole family-world analysis of one project: the pass, its known answers and the stored table.</summary>
    public sealed class FamilyWorldAnalysisResult
    {
        public FamilyWorldPassResult Pass { get; init; } = new FamilyWorldPassResult();
        public List<FamilyWorldKnownAnswer> KnownAnswers { get; init; } = new List<FamilyWorldKnownAnswer>();
        public FamilyWorldSummary Summary { get; init; } = new FamilyWorldSummary();
    }

    /// <summary>
    /// B5 and B6 in one call, as Compute runs them: the first pass on the curation, the second pass against the
    /// family world, the known answers searched in the world without the sample's own species, and the result
    /// table (errors and composition) that <see cref="QuantManifest.FamilyWorldResult"/> stores.
    /// </summary>
    public static class FamilyWorldAnalysis
    {
        /// <summary>The sample's own taxa: the OX= of the curation's targets, decoys and contaminants left out.</summary>
        public static SortedSet<string> SampleTaxa(FamilyClusterFile curation) =>
            new SortedSet<string>(SampleTargets(curation).SelectMany(i => FamilyWorld.TaxaOf(i.Description)), StringComparer.Ordinal);

        /// <summary>The accessions of the curation's targets: the sample's own database.</summary>
        public static HashSet<string> SampleAccessions(FamilyClusterFile curation) =>
            new HashSet<string>(SampleTargets(curation).Select(i => FamilyWorld.BaseAccession(FamilyNaming.Accession(i.SequenceIdentifier))), StringComparer.Ordinal);

        private static IEnumerable<FastaItemExtended> SampleTargets(FamilyClusterFile curation) =>
            (curation.MyExtendedFastaItems ?? new List<FastaItemExtended>()).Where(i => i != null
                && !SequenceLabels.IsDecoy(i.SequenceIdentifier ?? string.Empty)
                && !(i.SequenceIdentifier ?? string.Empty).StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal));

        /// <summary>
        /// B5 and B6: when the curation carries a stored family world (B4), the de novo envelope the curated families
        /// leave unattributed goes through the family-world pass, and the table with the error beside every family is
        /// stored in the manifest. Without a stored world nothing is added, so such a project's manifest is unchanged.
        /// A failure is noted and never loses the quantitation.
        /// </summary>
        public static void AttachToCompute(FamilyClusterFile? curation, FullXicComputeResult result)
            => AttachToComputeCore(curation, result, null, null);

        public static void AttachToCompute(FamilyClusterFile? curation, FullXicComputeResult result, OperationContext? operation)
            => AttachToComputeCore(curation, result, null, operation);

        /// <summary>Compute already attributed these same rows. Keep that complete map inside this
        /// operation rather than repeating its two searches. Other callers retain the fresh route.</summary>
        internal static void AttachToComputeWithAttribution(FamilyClusterFile? curation, FullXicComputeResult result,
            PeptideFamilyAttribution? attribution, OperationContext? operation = null)
            => AttachToComputeCore(curation, result, attribution, operation);

        private static void AttachToComputeCore(FamilyClusterFile? curation, FullXicComputeResult result,
            PeptideFamilyAttribution? attribution, OperationContext? operation)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));
            if (curation?.EvidenceSource?.FamilyWorld == null)
            {
                operation?.Report("world-phase", "UniProt families unavailable: no stored world", 0, 0);
                return;
            }
            try
            {
                FamilyWorldSummary table;
                if (attribution != null && attribution.MembershipSha256 == FamilyStudyMembership.Fingerprint(curation))
                {
                    operation?.Report("world-phase", "UniProt families phases", 0, 5);
                    operation?.Report("world-phase", "UniProt families 1 of 5: Reading stored world", 1, 5);
                    FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(curation);
                    operation?.Report("world-phase", "UniProt families 2 of 5: Reusing current peptide attribution", 2, 5);
                    FamilyPeptideAttribution firstPass = FamilyEnvelopeQuant.AttributePeptides(
                        FamilyEnvelopeQuant.PreparePeptides(result.Rows), attribution);
                    table = RunPrepared(firstPass, curation, result.SpectrumComparisonRows, FamilyWorld.FromSnapshot(snapshot),
                        snapshot.Release, null, new FamilyWorldOptions { Science = attribution.EffectiveOptions.Science?.Clone() }, FamilyWorldErrors.DefaultMinimumKnownCredits, operation,
                        FamilyWorldOptions.LengthChoices).Summary;
                }
                else table = FromStoredWorld(result.Rows, curation, result.SpectrumComparisonRows, operation).Summary;
                result.Manifest.FamilyWorldResult = table;
                double landed = table.ResidualEnvelope > 0
                    ? table.Outcomes.Where(o => o.Outcome == nameof(FamilyWorldOutcome.Credited)).Sum(o => o.Envelope) / table.ResidualEnvelope : 0;
                result.Notes.Add("UniProt families: " + landed.ToString("P2", CultureInfo.InvariantCulture) +
                    " of the de novo envelope the curated families leave unattributed is credited to a family in scope (" +
                    table.Rules + ").");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || operation?.IsCancellationRequested != true)
            {
                result.Manifest.FamilyWorldResult = null;
                result.Notes.Add("WARNING: the UniProt families pass did not run (" + ex.GetType().Name + ": " + ex.Message +
                    "). Quantitation completed without it.");
            }
        }

        /// <summary>What Compute runs when the curation carries a stored family world (B4): the snapshot is verified
        /// offline, then the whole analysis runs on it.</summary>
        public static FamilyWorldAnalysisResult FromStoredWorld(IEnumerable<QuantRow> rows, FamilyClusterFile curation,
            IEnumerable<SpectrumComparisonRow> spectra, OperationContext? operation = null)
        {
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            operation?.Report("world-phase", "UniProt families phases", 0, 5);
            operation?.Report("world-phase", "UniProt families 1 of 5: Reading stored world", 1, 5);
            FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(curation);
            return Run(rows, curation, spectra, FamilyWorld.FromSnapshot(snapshot), snapshot.Release, operation: operation,
                lengths: FamilyWorldOptions.LengthChoices);
        }

        public static FamilyWorldAnalysisResult Run(IEnumerable<QuantRow> rows, FamilyClusterFile curation,
            IEnumerable<SpectrumComparisonRow> spectra, FamilyWorld world, string worldRelease,
            Func<FamilyWorldProtein, bool>? ownSpecies = null, FamilyWorldOptions? options = null,
            int minimumKnownCredits = FamilyWorldErrors.DefaultMinimumKnownCredits, OperationContext? operation = null,
            IReadOnlyList<int>? lengths = null)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            List<QuantRow> rowList = rows.Where(r => r != null).ToList();
            operation?.Report("world-phase", "UniProt families 2 of 5: Attributing the current peptides", 2, 5);
            PeptideFamilyAttribution attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(rowList, curation,
                operation: operation, progressPrefix: "world-attribution");
            FamilyPeptideAttribution firstPass = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rowList), attribution);
            return RunPrepared(firstPass, curation, spectra, world, worldRelease, ownSpecies, options,
                minimumKnownCredits, operation, lengths);
        }

        private static FamilyWorldAnalysisResult RunPrepared(FamilyPeptideAttribution firstPass, FamilyClusterFile curation,
            IEnumerable<SpectrumComparisonRow> spectra, FamilyWorld world, string worldRelease,
            Func<FamilyWorldProtein, bool>? ownSpecies, FamilyWorldOptions? options, int minimumKnownCredits,
            OperationContext? operation, IReadOnlyList<int>? lengths)
        {
            options ??= new FamilyWorldOptions();
            operation?.Report("world-phase", "UniProt families 3 of 5: Searching residual peptides", 3, 5);
            if (lengths == null || lengths.Count == 0)
            {
                FamilyWorldPassResult pass = FamilyWorldPass.Run(firstPass, world, options, operation);
                return Complete(firstPass, pass, curation, spectra, world, worldRelease, ownSpecies, options, minimumKnownCredits, operation);
            }
            List<int> choices = lengths.Append(options.MinimumTagLength).Distinct().OrderBy(l => l).ToList();
            IReadOnlyList<FamilyWorldPassResult> passes = FamilyWorldPass.RunLengths(firstPass, world, options, choices, operation);
            return CompleteLengths(firstPass, passes, curation, spectra, world, worldRelease, ownSpecies, options, minimumKnownCredits, operation);
        }

        /// <summary>The analysis after the pass: known answers in the world without the sample's species (by default its
        /// OX= taxa, through each protein's taxon and lineage, and its own database's accessions), the errors and the
        /// composition.</summary>
        public static FamilyWorldAnalysisResult Complete(FamilyPeptideAttribution firstPass, FamilyWorldPassResult pass,
            FamilyClusterFile curation, IEnumerable<SpectrumComparisonRow> spectra, FamilyWorld world, string worldRelease,
            Func<FamilyWorldProtein, bool>? ownSpecies, FamilyWorldOptions options, int minimumKnownCredits, OperationContext? operation = null)
        {
            if (pass == null) throw new ArgumentNullException(nameof(pass));
            return CompleteLengths(firstPass, new[] { pass }, curation, spectra, world, worldRelease, ownSpecies, options,
                minimumKnownCredits, operation);
        }

        /// <summary>
        /// The analysis after the passes at several minimum tag lengths (B10). The known answers are searched once, at
        /// the lowest length, and read again under each higher one; every length gets its complete table. The result's
        /// table is the one at <see cref="FamilyWorldOptions.MinimumTagLength"/>, and when several lengths were given it
        /// carries all of them in <see cref="FamilyWorldSummary.Lengths"/> with their quick estimates.
        /// </summary>
        public static FamilyWorldAnalysisResult CompleteLengths(FamilyPeptideAttribution firstPass, IReadOnlyList<FamilyWorldPassResult> passes,
            FamilyClusterFile curation, IEnumerable<SpectrumComparisonRow> spectra, FamilyWorld world, string worldRelease,
            Func<FamilyWorldProtein, bool>? ownSpecies, FamilyWorldOptions options, int minimumKnownCredits, OperationContext? operation = null)
            => CompleteLengthsCore(firstPass, passes, curation, spectra, world, worldRelease, ownSpecies, options,
                minimumKnownCredits, operation, null, null);

        /// <summary>Reuse verified holder and known-tag replay results; every required query must be present.</summary>
        public static FamilyWorldAnalysisResult CompleteLengthsFromResults(FamilyPeptideAttribution firstPass, IReadOnlyList<FamilyWorldPassResult> passes,
            FamilyClusterFile curation, IEnumerable<SpectrumComparisonRow> spectra, FamilyWorld world, string worldRelease,
            Func<FamilyWorldProtein, bool>? ownSpecies, FamilyWorldOptions options, int minimumKnownCredits,
            IReadOnlyDictionary<string, Homology.HomologyResult> holders, IReadOnlyDictionary<string, Homology.HomologyResult> tags,
            OperationContext? operation = null)
            => CompleteLengthsCore(firstPass, passes, curation, spectra, world, worldRelease, ownSpecies, options,
                minimumKnownCredits, operation, holders ?? throw new ArgumentNullException(nameof(holders)),
                tags ?? throw new ArgumentNullException(nameof(tags)));

        private static FamilyWorldAnalysisResult CompleteLengthsCore(FamilyPeptideAttribution firstPass, IReadOnlyList<FamilyWorldPassResult> passes,
            FamilyClusterFile curation, IEnumerable<SpectrumComparisonRow> spectra, FamilyWorld world, string worldRelease,
            Func<FamilyWorldProtein, bool>? ownSpecies, FamilyWorldOptions options, int minimumKnownCredits, OperationContext? operation,
            IReadOnlyDictionary<string, Homology.HomologyResult>? cachedHolders, IReadOnlyDictionary<string, Homology.HomologyResult>? cachedTags)
        {
            if (firstPass == null) throw new ArgumentNullException(nameof(firstPass));
            if (passes == null || passes.Count == 0) throw new ArgumentException("Give at least one pass.", nameof(passes));
            if (curation == null) throw new ArgumentNullException(nameof(curation));
            if (spectra == null) throw new ArgumentNullException(nameof(spectra));
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (options == null) throw new ArgumentNullException(nameof(options));
            SortedSet<string> taxa = SampleTaxa(curation);
            HashSet<string> accessions = SampleAccessions(curation);
            ownSpecies ??= p => FamilyWorld.IsOfTaxa(p, taxa) || accessions.Contains(p.Accession);
            FamilyWorld withoutSpecies = FamilyWorld.Build(world.Proteins.Where(p => !ownSpecies(p)), world.ScopeFamilies);
            Dictionary<string, IReadOnlyList<string>> labels = FamilyWorldErrors.Labels(world.Proteins, curation);
            operation?.Report("world-phase", "UniProt families 4 of 5: Checking known answers", 4, 5);
            List<FamilyWorldKnownAnswer> answers = cachedHolders == null ? FamilyWorldErrors.KnownAnswers(spectra, curation, labels, operation)
                : FamilyWorldErrors.KnownAnswersFromResults(spectra, curation, labels, cachedHolders, operation);
            int lowest = passes.Min(p => p.MinimumTagLength);
            FamilyWorldOptions searchOptions = options.Clone();
            searchOptions.MinimumTagLength = lowest;
            if (cachedTags == null) FamilyWorldErrors.Credit(answers, withoutSpecies, passes[0].CreditableFamilies, searchOptions, operation);
            else FamilyWorldErrors.CreditFromResults(answers, withoutSpecies, passes[0].CreditableFamilies, searchOptions, cachedTags, operation);
            operation?.Report("world-phase", "UniProt families 5 of 5: Assembling stored length studies", 5, 5);

            (FamilyWorldSummary Table, List<FamilyWorldKnownAnswer> Answers) TableAt(FamilyWorldPassResult pass)
            {
                List<FamilyWorldKnownAnswer> read = answers.Select(a => a.AtMinimumLength(pass.MinimumTagLength)).ToList();
                FamilyWorldSummary table = FamilyWorldErrors.Summarize(pass, read, worldRelease, taxa, minimumKnownCredits);
                FamilyWorldErrors.Compose(table, firstPass, curation, labels);
                return (table, read);
            }

            FamilyWorldPassResult chosen = passes.FirstOrDefault(p => p.MinimumTagLength == options.MinimumTagLength) ?? passes[0];
            var (summary, chosenAnswers) = TableAt(chosen);
            if (passes.Count > 1)
            {
                // Separate objects for every length, the chosen one included, so the stored JSON has no cycle.
                summary.Lengths = passes.OrderBy(p => p.MinimumTagLength).Select(p => TableAt(p).Table).ToList();
                summary.LengthEstimates = summary.Lengths.Select(FamilyWorldErrors.Estimate).ToList();
            }
            return new FamilyWorldAnalysisResult { Pass = chosen, KnownAnswers = chosenAnswers, Summary = summary };
        }
    }
}
