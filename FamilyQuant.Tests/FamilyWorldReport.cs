using FamilyQuant.Evidence;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests
{
    /// <summary>Reads the family-world table a project stores (what Compute attached) and prints and exports it, so the
    /// numbers a person sees come from the stored project, not from a recomputation.</summary>
    internal static class FamilyWorldReport
    {
        internal const string Argument = "--family-world-report";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
                string projectPath = At(1, "D:/FamilyQuant-data/out-opus/b6/computed/computed.fqproj");
                string outDir = At(2, "D:/FamilyQuant-data/out-opus/b6/computed-report");
                Directory.CreateDirectory(outDir);

                var (curation, _, manifest) = ProjectStore.ReadFqproj(projectPath);
                FamilyWorldSummary? table = manifest.FamilyWorldResult;
                check("family world report: the project stores the family-world table", table != null, projectPath);
                if (table == null) return;
                FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(curation);
                check("family world report: the table was made on the world the project's curation stores",
                    table.WorldFingerprint == snapshot.ContentSha256 && table.WorldRelease == snapshot.Release
                    && table.ScopeFamilies.SequenceEqual(snapshot.Families), table.WorldFingerprint);
                double closure = table.Composition.Sum(r => r.TotalEnvelope) + table.SharedIdentifiedEnvelope + table.SharedDatabaseDeNovoEnvelope
                    + table.SharedEnvelope + table.UnattributedIdentifiedEnvelope + table.UnexplainedEnvelope;
                check("family world report: the stored table closes on its measured total",
                    Math.Abs(closure - table.MeasuredTotalEnvelope) <= 1e-9 * table.MeasuredTotalEnvelope, null);

                string P(double v) => (100 * v).ToString("F2", CultureInfo.InvariantCulture) + "%";
                string N(int v) => v.ToString("N0", CultureInfo.InvariantCulture);
                double Of(string outcome) => table.ResidualEnvelope <= 0 ? 0
                    : table.Outcomes.Where(o => o.Outcome == outcome).Sum(o => o.Envelope) / table.ResidualEnvelope;
                Console.WriteLine("        Rules: " + table.Rules);
                Console.WriteLine("        World: " + N(table.WorldProteins) + " entries, UniProt " + table.WorldRelease + ", hash " + table.WorldFingerprint +
                    "; families in scope: " + string.Join("; ", table.ScopeFamilies));
                Console.WriteLine("        Unattributed de novo envelope " + table.ResidualEnvelope.ToString("E6", CultureInfo.InvariantCulture) + " (" +
                    N(table.ResidualPeptides) + " peptides): credited " + P(Of(nameof(FamilyWorldOutcome.Credited))) + ", decoys " +
                    P(Of(nameof(FamilyWorldOutcome.Decoy))) + ", too short " + P(Of(nameof(FamilyWorldOutcome.TooShort))) + ", out of scope " +
                    P(Of(nameof(FamilyWorldOutcome.OutOfScope))) + ", no family " + P(Of(nameof(FamilyWorldOutcome.LandedNoFamily))) +
                    ", not found " + P(Of(nameof(FamilyWorldOutcome.NotLanded))));
                Console.WriteLine("        Known answers: " + N(table.KnownRight) + " right of " + N(table.KnownLanded) + " landed" +
                    (table.KnownLanded > 0 ? " (" + P(table.KnownRight / (double)table.KnownLanded) + ")" : "") + ", " + N(table.KnownPairs) + " pairs");
                Console.WriteLine("        Per family: own share of the residual | decoy rate | known credits, truly it | right rate or evidence");
                foreach (FamilyWorldFamilySummary f in table.Families)
                    Console.WriteLine("          " + f.Family + ": " + P(f.UniqueEnvelope / table.ResidualEnvelope) + " | " +
                        FamilyWorldDecoyRates.Format(FamilyWorldDecoyRates.ForFamily(f), CultureInfo.InvariantCulture) + " | " + N(f.KnownCredited) + ", " +
                        N(f.KnownCreditedTrue) + " | " + (f.RightRate.HasValue ? P(f.RightRate.Value) : f.Evidence));
                Console.WriteLine("        Result table, own parts of the measured total (identified | de novo in the database | de novo only in the family world):");
                foreach (FamilyWorldCompositionRow r in table.Composition.Take(15))
                    Console.WriteLine("          " + r.Family + (r.Kind == FamilyWorldCompositionRow.CuratedKind ? " (curated)" : "") + ": " +
                        P(r.IdentifiedEnvelope / table.MeasuredTotalEnvelope) + " | " + P(r.DatabaseDeNovoEnvelope / table.MeasuredTotalEnvelope) +
                        " | " + P(r.WorldDeNovoEnvelope / table.MeasuredTotalEnvelope));
                Console.WriteLine("        Global: shared " + P((table.SharedIdentifiedEnvelope + table.SharedDatabaseDeNovoEnvelope + table.SharedEnvelope) /
                    table.MeasuredTotalEnvelope) + ", identified in no family " + P(table.UnattributedIdentifiedEnvelope / table.MeasuredTotalEnvelope) +
                    ", de novo unexplained " + P(table.UnexplainedEnvelope / table.MeasuredTotalEnvelope) + " of the measured total");
                File.WriteAllText(Path.Combine(outDir, "family_world_errors.csv"), table.BuildCsv(), new UTF8Encoding(false));
                File.WriteAllText(Path.Combine(outDir, "family_world_composition.csv"), table.BuildCompositionCsv(), new UTF8Encoding(false));
                Console.WriteLine("        Written to " + outDir);
            }
            catch (Exception ex) { check("family world report completed", false, ex.GetBaseException().ToString()); }
        }
    }
}
