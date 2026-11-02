using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// Prepares a real project for Compute's family-world step, through the program only: the curation's evidence
    /// is fetched, the task read from its headers and confirmed with the given scope (or the proposed one), and B4
    /// fetches and stores the family world. The project is written again unchanged otherwise, as the input of
    /// <see cref="FullXicPipelineSnapshot"/>, whose Compute then attaches the result table (B6).
    /// </summary>
    internal static class FamilyWorldProject
    {
        internal const string Argument = "--family-world-project";

        internal static int Run(string[] args)
        {
            int flag = Array.IndexOf(args, Argument);
            string At(int offset, string fallback) => TestArguments.EmptyFallsBack(args, flag, offset, fallback);
            string projectPath = At(1, "C:/Users/paulo/Desktop/FamilyQuant/BrProt_Talk/all+DeepNovo/scientific_results.fqproj");
            string scopeText = args.Length > flag + 2 ? args[flag + 2] : string.Empty;
            string outDir = Path.GetFullPath(At(3, "D:/FamilyQuant-data/out-opus/b6/project"));
            string input = Path.Combine(outDir, "input-with-world.fqproj");
            if (File.Exists(input)) throw new IOException("The prepared project already exists: " + input);
            Directory.CreateDirectory(outDir);

            var (curation, quant, manifest) = ProjectStore.ReadFqproj(projectPath);
            EvidenceFetchResult fetched = EvidenceFetcher.FetchAsync(curation).GetAwaiter().GetResult();
            Console.WriteLine("Evidence: " + fetched.Found.ToString(CultureInfo.InvariantCulture) + " of " +
                fetched.Asked.ToString(CultureInfo.InvariantCulture) + " accessions, UniProt " + fetched.Provenance.Release);

            EvidenceTaskProposal proposal = EvidenceTaskDiscovery.DiscoverAsync(curation).GetAwaiter().GetResult();
            Console.WriteLine("Task read from the headers: clade " + proposal.Clade.ScientificName + " (" +
                proposal.Clade.TaxonId.ToString(CultureInfo.InvariantCulture) + "), proposed scope " + string.Join(", ", proposal.ProposedScopeTerms) +
                "; keywords " + string.Join(", ", proposal.Keywords.Take(5).Select(k => k.Keyword + " " + k.ReviewedProteins.ToString(CultureInfo.InvariantCulture))));
            var scope = scopeText.Trim().Length == 0 ? proposal.ProposedScopeTerms : WordCluster.ParseKeywords(scopeText);
            EvidenceTaskChoice task = EvidenceTaskDiscovery.Confirm(curation, proposal, scope);
            Console.WriteLine("Scope confirmed: " + string.Join(", ", task.ScopeTerms));

            OperationResult<FamilyWorldSnapshot> world = CoreOperation.RunAsync("Family world",
                context => FamilyWorldFetcher.FetchAndStoreAsync(curation, task, context)).GetAwaiter().GetResult();
            if (world.Status != OperationStatus.Succeeded)
                throw new InvalidDataException("The family world was not fetched: " + world.Status + " " + world.Failure?.Message);
            FamilyWorldSnapshot snapshot = FamilyWorldFetcher.Stored(curation);
            Console.WriteLine("Family world: " + snapshot.Families.Count.ToString(CultureInfo.InvariantCulture) + " families, " +
                snapshot.Entries.Count.ToString(CultureInfo.InvariantCulture) + " entries (" +
                snapshot.Entries.Count(e => e.FromReviewedPanel).ToString(CultureInfo.InvariantCulture) + " from the reviewed panel), UniProt " +
                snapshot.Release + ", hash " + snapshot.ContentSha256);
            Console.WriteLine("Families: " + string.Join("; ", snapshot.Families));

            ProjectStore.WriteFqproj(input, curation, quant, manifest);
            var (reopened, reopenedQuant, _) = ProjectStore.ReadFqproj(input);
            FamilyWorldSnapshot stored = FamilyWorldFetcher.Stored(reopened);
            if (stored.ContentSha256 != snapshot.ContentSha256 || reopenedQuant.Count != quant.Count)
                throw new InvalidDataException("The prepared project did not read back with the same world and quant.");
            Console.WriteLine("Prepared: " + input);
            return 0;
        }
    }
}
