using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// The product's family-world analysis (D11) on the world B4 actually stores, not B1's interim world: the
    /// snapshot is verified offline, the sample's own species leaves the known-answer world through each entry's
    /// stored taxon and lineage, and the result is printed beside the halves of the B1 study.
    /// </summary>
    internal static class FamilyWorldB4Check
    {
        internal const string Argument = "--family-world-b4";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
                string projectPath = At(1, "C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj");
                string worldPath = At(2, "D:/FamilyQuant-data/out-evidence/b4-ana-world.fcf");
                string outDir = At(3, "D:/FamilyQuant-data/out-opus/b6/b4-world");
                Directory.CreateDirectory(outDir);
                RunOn(projectPath, worldPath, outDir, check);
            }
            catch (Exception ex) { check("family world on B4's stored world completed", false, ex.GetBaseException().ToString()); }
        }

        private static string N(long v) => v.ToString("N0", CultureInfo.InvariantCulture);
        private static string P(double v) => (100 * v).ToString("F2", CultureInfo.InvariantCulture) + "%";

        private static void RunOn(string projectPath, string worldPath, string outDir, Action<string, bool, string?> check)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            void Log(string text) => Console.WriteLine("        [" + watch.Elapsed.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s] " + text);
            var (sample, quant, manifest) = ProjectStore.ReadFqproj(projectPath);
            FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(FamilyClusterFile.Load(worldPath));
            FamilyWorld world = FamilyWorld.FromSnapshot(snapshot);
            SortedSet<string> taxa = FamilyWorldAnalysis.SampleTaxa(sample);
            HashSet<string> accessions = FamilyWorldAnalysis.SampleAccessions(sample);
            int own = world.Proteins.Count(p => FamilyWorld.IsOfTaxa(p, taxa) || accessions.Contains(p.Accession));
            Log("B4 world: " + N(world.Proteins.Count) + " entries, " + N(world.ScopeFamilies.Count) + " families in scope, UniProt " + snapshot.Release +
                ", hash " + snapshot.ContentSha256 + "; clade " + snapshot.Task.Proposal.Clade.ScientificName + "; " + N(own) +
                " entries of the sample's taxa " + string.Join(",", taxa) + " or database, left out of the known-answer world");

            FamilyWorldAnalysisResult analysis = FamilyWorldAnalysis.Run(quant, sample, manifest.SpectrumComparisonRows, world, snapshot.Release);
            FamilyWorldSummary table = analysis.Summary;
            Log("analysis done");
            double Share(string outcome) => table.ResidualEnvelope <= 0 ? 0
                : table.Outcomes.Where(o => o.Outcome == outcome).Sum(o => o.Envelope) / table.ResidualEnvelope;
            Console.WriteLine("        ON B4'S WORLD (" + table.Rules + ")");
            foreach (FamilyWorldOutcomeSummary o in table.Outcomes)
                Console.WriteLine("          " + o.Outcome + ": " + P(Share(o.Outcome)) + " of the residual, " + N(o.Peptides) + " peptides");
            double credited = table.Outcomes.Single(o => o.Outcome == nameof(FamilyWorldOutcome.Credited)).Envelope;
            double decoy = table.Outcomes.Single(o => o.Outcome == nameof(FamilyWorldOutcome.Decoy)).Envelope;
            Console.WriteLine("          landed on a family in scope: " + P(Share(nameof(FamilyWorldOutcome.Credited))) + "; decoys as a share of it: " +
                P(credited <= 0 ? 0 : decoy / credited));

            int HalfOf(FamilyWorldKnownAnswer a) => SHA256.HashData(Encoding.UTF8.GetBytes(a.Identified))[0] & 1;
            List<FamilyWorldKnownAnswer> known = analysis.KnownAnswers.Where(a => a.IsKnown).ToList();
            string Rate(IEnumerable<FamilyWorldKnownAnswer> set)
            {
                List<FamilyWorldKnownAnswer> landed = set.Where(a => a.Landed).ToList();
                int right = landed.Count(a => a.Right);
                return N(right) + " of " + N(landed.Count) + (landed.Count > 0 ? " (" + P(right / (double)landed.Count) + ")" : "");
            }
            Console.WriteLine("          known answers (" + N(known.Count) + " pairs): right among landed, pooled " + Rate(known) +
                "; table half " + Rate(known.Where(a => HalfOf(a) == 0)) + "; held-out half " + Rate(known.Where(a => HalfOf(a) == 1)));
            foreach (FamilyWorldOutcome o in Enum.GetValues<FamilyWorldOutcome>())
                Console.WriteLine("            " + o + ": " + N(known.Count(a => a.Credit?.Outcome == o)));
            Console.WriteLine("          by family: own share of the residual | decoy rate | known credits, truly it | right rate or evidence");
            foreach (FamilyWorldFamilySummary f in table.Families)
                Console.WriteLine("            " + f.Family + ": " + P(f.UniqueEnvelope / table.ResidualEnvelope) + " | " +
                    FamilyWorldDecoyRates.Format(FamilyWorldDecoyRates.ForFamily(f), CultureInfo.InvariantCulture) + " | " + N(f.KnownCredited) + ", " + N(f.KnownCreditedTrue) +
                    " | " + (f.RightRate.HasValue ? P(f.RightRate.Value) : f.Evidence));
            Console.WriteLine("          result table, own parts of the measured total (identified | de novo in the database | de novo only in the family world):");
            foreach (FamilyWorldCompositionRow r in table.Composition.Where(r => r.Kind == FamilyWorldCompositionRow.UniProtKind).Take(12))
                Console.WriteLine("            " + r.Family + ": " + P(r.IdentifiedEnvelope / table.MeasuredTotalEnvelope) + " | " +
                    P(r.DatabaseDeNovoEnvelope / table.MeasuredTotalEnvelope) + " | " + P(r.WorldDeNovoEnvelope / table.MeasuredTotalEnvelope));
            Console.WriteLine("          de novo unexplained: " + P(table.UnexplainedEnvelope / table.MeasuredTotalEnvelope) + " of the measured total");

            double closure = table.Composition.Sum(r => r.TotalEnvelope) + table.SharedIdentifiedEnvelope + table.SharedDatabaseDeNovoEnvelope
                + table.SharedEnvelope + table.UnattributedIdentifiedEnvelope + table.UnexplainedEnvelope;
            check("family world on B4's world: the table closes on the measured total",
                Math.Abs(closure - table.MeasuredTotalEnvelope) <= 1e-9 * table.MeasuredTotalEnvelope, null);
            check("family world on B4's world: hit identifiers are B4's accessions and the fingerprint its content hash",
                table.WorldFingerprint == snapshot.ContentSha256
                && table.Families.SelectMany(f => f.WorldProteins).All(id => snapshot.Entries.Any(e => e.Accession == id)), null);
            check("family world on B4's world: every family row has its decoy rate and right rate, or says why not",
                table.Families.All(f => (f.DecoyRate.HasValue || f.DecoyRateStatus is FamilyWorldDecoyRates.NoCreditedSignal or FamilyWorldDecoyRates.DecoyExceedsCredited)
                    && (f.RightRate.HasValue || f.Evidence.Length > 0)), null);
            // Ungated: the same rules with the whole genus out of the known-answer world, through each entry's stored
            // lineage, as for a species with no close relative in the world. The genus comes from UniProt's taxonomy.
            using (var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(120) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("FamilyQuant");
                var genera = new SortedSet<string>(taxa.Select(t => FamilyWorldCheck.Genus(client, t, outDir, Log).Id).Where(id => id.Length > 0), StringComparer.Ordinal);
                if (genera.Count > 0)
                {
                    bool OfGenus(FamilyWorldProtein p) => FamilyWorld.IsOfTaxa(p, genera) || FamilyWorld.IsOfTaxa(p, taxa) || accessions.Contains(p.Accession);
                    PeptideFamilyAttribution firstAttribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(quant, sample);
                    FamilyPeptideAttribution firstPass = FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(quant), firstAttribution);
                    FamilyWorldAnalysisResult genus = FamilyWorldAnalysis.Complete(firstPass, analysis.Pass, sample, manifest.SpectrumComparisonRows,
                        world, snapshot.Release, OfGenus, new FamilyWorldOptions(), FamilyWorldErrors.DefaultMinimumKnownCredits);
                    List<FamilyWorldKnownAnswer> genusKnown = genus.KnownAnswers.Where(a => a.IsKnown).ToList();
                    Console.WriteLine("          WITHOUT THE GENUS " + string.Join(",", genera) + " (ungated; " + N(world.Proteins.Count(OfGenus)) +
                        " entries left out): right among landed, pooled " + Rate(genusKnown) + "; table half " +
                        Rate(genusKnown.Where(a => HalfOf(a) == 0)) + "; held-out half " + Rate(genusKnown.Where(a => HalfOf(a) == 1)));
                    foreach (FamilyWorldFamilySummary f in genus.Summary.Families.Where(f => f.KnownCredited > 0))
                        Console.WriteLine("            " + f.Family + ": " + N(f.KnownCredited) + ", " + N(f.KnownCreditedTrue) + " | " +
                            (f.RightRate.HasValue ? P(f.RightRate.Value) : f.Evidence));
                    File.WriteAllText(Path.Combine(outDir, "family_world_errors_without_genus.csv"), genus.Summary.BuildCsv(), new UTF8Encoding(false));
                }
            }
            File.WriteAllText(Path.Combine(outDir, "family_world_errors.csv"), table.BuildCsv(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "family_world_composition.csv"), table.BuildCompositionCsv(), new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(outDir, "family_world_pass.csv"), FamilyWorldPass.BuildCsv(analysis.Pass), new UTF8Encoding(false));
            Log("written to " + outDir);
        }
    }
}
