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
        FamilyWorldRateTests();
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
