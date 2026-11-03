using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    /// <summary>The step Compute runs at completion (B5, B6), on B4's synthetic stored world: the table is attached
    /// when the curation carries a verified world, nothing changes without one, and a damaged world is noted.</summary>
    private static void FamilyWorldComputeTests()
    {
        FamilyClusterFile file = TaskFile();
        using (var taxonomy = new TaskTaxonomyHandler())
        using (var handler = new WorldFetchHandler())
        {
            EvidenceTaskProposal proposal = EvidenceTaskDiscovery.DiscoverAsync(file, taxonomy).GetAwaiter().GetResult();
            EvidenceTaskChoice task = EvidenceTaskDiscovery.Confirm(file, proposal, proposal.ProposedScopeTerms);
            CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context, handler)).GetAwaiter().GetResult();
        }
        FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(file);
        FamilyWorld world = FamilyWorld.FromSnapshot(snapshot);
        Check("Compute family world: B4's stored entries are the world, by their accessions, with the snapshot's hash and scope",
            world.Proteins.Select(p => p.Id).SequenceEqual(snapshot.Entries.Select(e => e.Accession)) && world.Fingerprint == snapshot.ContentSha256
            && world.ScopeFamilies.SequenceEqual(snapshot.Families) && world.Proteins.All(p => p.Taxon.Length > 0 && p.LineageTaxa.Count > 0));
        Check("Compute family world: the sample's own species leaves the known-answer world through the stored taxon or lineage",
            FamilyWorldAnalysis.SampleTaxa(file).SetEquals(new[] { "21", "22" })
            && world.Proteins.Count(p => FamilyWorld.IsOfTaxa(p, FamilyWorldAnalysis.SampleTaxa(file))) == 1);

        int scan = 0;
        QuantRow Row(string side, string sequence, double m0, double envelope) => new QuantRow
        {
            Side = side, Run = "r1", Scan = ++scan, Charge = 2, Sequence = sequence,
            PrecursorMz = Math.Round(600.0 + scan * 1e-3, 5), IsotopeAreas = new[] { m0, 0.0, 0.0, 0.0 }, EnvelopeSum = envelope,
        };
        FullXicComputeResult Computed() => new FullXicComputeResult
        {
            Rows = new List<QuantRow>
            {
                Row(NativeQuantFractions.SideIdentified, "ACDEFGHIK", 10, 30),
                Row(NativeQuantFractions.SideDeNovo, "PEPTIDK", 5, 11),
                Row(NativeQuantFractions.SideDeNovo, "WWWWWWWWWWK", 4, 9),
            },
            Manifest = new QuantManifest(),
            SpectrumComparisonRows = new List<SpectrumComparisonRow>
            {
                new() { PatternLabCleanSequence = "ACDEFGHIK", PeaksDeNovoCleanSequence = "ACDEFGHIK", PeaksDeNovoAccepted = true },
            },
        };

        FullXicComputeResult attached = Computed();
        FamilyWorldAnalysis.AttachToCompute(file, attached);
        FamilyWorldSummary? table = attached.Manifest.FamilyWorldResult;
        Check("Compute family world: with a stored world the table is attached, with the snapshot's release, hash and rules",
            table != null && table.WorldRelease == snapshot.Release && table.WorldFingerprint == snapshot.ContentSha256
            && table.MinimumTagLength == FamilyWorldOptions.DefaultMinimumTagLength && table.ScopeFamilies.SequenceEqual(snapshot.Families)
            && attached.Notes.Any(n => n.StartsWith("Family world: ", StringComparison.Ordinal)), attached.Notes.LastOrDefault());
        Check("Compute family world: the residual's outcomes are stored and the table closes on the measured total",
            table != null && table.ResidualPeptides == 2
            && table.Outcomes.Single(o => o.Outcome == nameof(FamilyWorldOutcome.TooShort)).Peptides == 1
            && table.Outcomes.Single(o => o.Outcome == nameof(FamilyWorldOutcome.NotLanded)).Peptides == 1
            && Math.Abs(table.Composition.Sum(r => r.TotalEnvelope) + table.SharedIdentifiedEnvelope + table.SharedDatabaseDeNovoEnvelope
                + table.SharedEnvelope + table.UnattributedIdentifiedEnvelope + table.UnexplainedEnvelope - table.MeasuredTotalEnvelope) < 1e-9);

        var replayMapping = FamilyEnvelopeQuant.BuildAttributionFromCuration(attached.Rows, file);
        var replayFirst = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(attached.Rows), replayMapping);
        var replayOptions = new FamilyWorldOptions();
        int[] replayLengths = [7, 8];
        var residualQueries = replayFirst.UnattributedDeNovoPeptides.Select(p => DeNovoReconciler.NormalizeSequence(p.Sequence))
            .Where(s => s.Length >= 7).Distinct(StringComparer.Ordinal).ToArray();
        var residualResults = Homology.HomologySearch.Search(residualQueries, world.Database, replayOptions.SearchOptions())
            .ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
        var normalPasses = FamilyWorldPass.RunLengths(replayFirst, world, replayOptions, replayLengths);
        var replayPasses = FamilyWorldPass.RunLengthsFromResults(replayFirst, world, replayOptions, replayLengths, residualResults);
        var replayJson = new System.Text.Json.JsonSerializerOptions
            { NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        Check("J47 cached world results preserve every native pass byte", System.Text.Json.JsonSerializer.Serialize(normalPasses, replayJson)
            == System.Text.Json.JsonSerializer.Serialize(replayPasses, replayJson));
        var holderDb = Homology.HomologyDatabase.Build(file.MyExtendedFastaItems);
        var holderResults = Homology.HomologySearch.Search(attached.SpectrumComparisonRows.Select(r => DeNovoReconciler.NormalizeSequence(r.PatternLabCleanSequence)), holderDb,
            new Homology.HomologyOptions { TopHitsPerPeptide = int.MaxValue, BestScoreOnly = true })
            .ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
        var taxa = FamilyWorldAnalysis.SampleTaxa(file); var own = FamilyWorldAnalysis.SampleAccessions(file);
        var without = FamilyWorld.Build(world.Proteins.Where(p => !FamilyWorld.IsOfTaxa(p, taxa) && !own.Contains(p.Accession)), world.ScopeFamilies);
        var labels = FamilyWorldErrors.Labels(world.Proteins, file);
        var known = FamilyWorldErrors.KnownAnswersFromResults(attached.SpectrumComparisonRows, file, labels, holderResults);
        var knownOptions = replayOptions.Clone(); knownOptions.MinimumTagLength = 7;
        var tagResults = Homology.HomologySearch.Search(known.Where(a => a.IsKnown && a.Tag.Length >= 7).Select(a => a.Tag),
            without.Database, knownOptions.SearchOptions()).ToDictionary(r => r.NormalizedPeptide, StringComparer.Ordinal);
        var normalComplete = FamilyWorldAnalysis.CompleteLengths(replayFirst, normalPasses, file, attached.SpectrumComparisonRows,
            world, snapshot.Release, null, replayOptions, FamilyWorldErrors.DefaultMinimumKnownCredits);
        var replayComplete = FamilyWorldAnalysis.CompleteLengthsFromResults(replayFirst, replayPasses, file, attached.SpectrumComparisonRows,
            world, snapshot.Release, null, replayOptions, FamilyWorldErrors.DefaultMinimumKnownCredits, holderResults, tagResults);
        Check("J47 cached holders/tags preserve every native known-answer/gap/composition byte",
            System.Text.Json.JsonSerializer.Serialize(normalComplete.Summary) == System.Text.Json.JsonSerializer.Serialize(replayComplete.Summary));
        bool missingReplay = false;
        try { FamilyWorldPass.RunLengthsFromResults(replayFirst, world, replayOptions, replayLengths,
            new Dictionary<string, Homology.HomologyResult>()); } catch (ArgumentException) { missingReplay = true; }
        Check("J47 missing cached residual is refused rather than counted as zero", residualQueries.Length > 0 && missingReplay);

        var reuseMethod = typeof(FamilyWorldAnalysis).GetMethod("AttachToComputeWithAttribution",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        Check("J38 Compute can pass its complete curation attribution into the world analysis", reuseMethod != null);
        if (reuseMethod != null)
        {
            var attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(attached.Rows, file);
            var reused = new FullXicComputeResult { Rows = attached.Rows, SpectrumComparisonRows = attached.SpectrumComparisonRows,
                Manifest = new QuantManifest() };
            reuseMethod.Invoke(null, new object?[] { file, reused, attribution, null });
            Check("J38 reused attribution preserves every stored world field and note",
                System.Text.Json.JsonSerializer.Serialize(reused.Manifest.FamilyWorldResult) == System.Text.Json.JsonSerializer.Serialize(table)
                && reused.Notes.SequenceEqual(attached.Notes));
            var regrouped = FamilyClusterFile.FromJsonText(file.ToJsonText());
            regrouped.MyExtendedFastaItems[0].UserID += 1000;
            var fresh = new FullXicComputeResult { Rows = attached.Rows, SpectrumComparisonRows = attached.SpectrumComparisonRows,
                Manifest = new QuantManifest() };
            var stale = new FullXicComputeResult { Rows = attached.Rows, SpectrumComparisonRows = attached.SpectrumComparisonRows,
                Manifest = new QuantManifest() };
            FamilyWorldAnalysis.AttachToCompute(regrouped, fresh);
            reuseMethod.Invoke(null, new object?[] { regrouped, stale, attribution, null });
            Check("J38 stale grouping rejects reuse and retains the ordinary fresh-search result",
                System.Text.Json.JsonSerializer.Serialize(stale.Manifest.FamilyWorldResult)
                == System.Text.Json.JsonSerializer.Serialize(fresh.Manifest.FamilyWorldResult) && stale.Notes.SequenceEqual(fresh.Notes));
        }

        FullXicComputeResult plain = Computed();
        FamilyWorldAnalysis.AttachToCompute(TaskFile(), plain);
        Check("Compute family world: without a stored world nothing is attached and nothing is said",
            plain.Manifest.FamilyWorldResult == null && plain.Notes.Count == 0);

        FamilyClusterFile damaged = FamilyClusterFile.FromJsonText(file.ToJsonText());
        damaged.EvidenceSource!.FamilyWorld!.Entries[0].Sequence += "A";
        FullXicComputeResult refused = Computed();
        int rows = refused.Rows.Count;
        FamilyWorldAnalysis.AttachToCompute(damaged, refused);
        Check("Compute family world: a damaged stored world is refused with a warning and the quantitation is kept",
            refused.Manifest.FamilyWorldResult == null && refused.Rows.Count == rows
            && refused.Notes.Single().StartsWith("WARNING: the family-world pass did not run", StringComparison.Ordinal), refused.Notes.FirstOrDefault());
    }
}
