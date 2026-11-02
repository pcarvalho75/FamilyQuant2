using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class WorldNativeProgress : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => Console.WriteLine(value.Stage + ": " + value.Message);
    }

    private static int FamilyWorldFetchCommand(string[] args)
    {
        try
        {
            if (args.Length != 4) throw new ArgumentException("Use --family-world-fetch <input.fcf> <new-output.fcf> <confirmed-scope>.");
            if (File.Exists(args[2])) throw new IOException("Use a new output path.");
            string inputHash = TestFileHash.Upper(args[1]);
            var file = FamilyClusterFile.Load(args[1]);
            var proposal = EvidenceTaskDiscovery.DiscoverAsync(file, progress: new DeliveryProgress("Taxonomy")).GetAwaiter().GetResult();
            var task = EvidenceTaskDiscovery.Confirm(file, proposal, WordCluster.ParseKeywords(args[3]));
            Console.WriteLine("Confirmed clade: " + task.Proposal.Clade.ScientificName + " (" + task.Proposal.Clade.TaxonId.ToString(CultureInfo.InvariantCulture) + ")");
            var result = CoreOperation.RunAsync("Family world", context => FamilyWorldFetcher.FetchAndStoreAsync(file, task, context),
                progress: new WorldNativeProgress()).GetAwaiter().GetResult();
            if (result.Status != OperationStatus.Succeeded) throw result.Failure?.Cause ?? new InvalidOperationException("The world fetch was cancelled.");
            if (TestFileHash.Upper(args[1]) != inputHash) throw new InvalidDataException("The input file changed while the world was fetched.");
            string? parent = Path.GetDirectoryName(Path.GetFullPath(args[2]));
            if (parent != null) Directory.CreateDirectory(parent);
            file.Save(args[2]);
            var reopened = FamilyClusterFile.Load(args[2]);
            var world = FamilyWorldFetcher.Stored(reopened);
            string expected = JsonSerializer.Serialize(result.Value);
            if (JsonSerializer.Serialize(world) != expected) throw new InvalidDataException("The stored world changed during saving.");
            string repeated = args[2] + ".offline-rerun.fcf";
            if (File.Exists(repeated)) throw new IOException("The offline replay path already exists.");
            reopened.Save(repeated);
            if (TestFileHash.Upper(args[2]) != TestFileHash.Upper(repeated)) throw new InvalidDataException("The offline saved file differs from its first write.");
            var report = new
            {
                input = Path.GetFullPath(args[1]), inputSha256 = inputHash, output = Path.GetFullPath(args[2]),
                sourceBytes = new FileInfo(args[1]).Length, savedBytes = new FileInfo(args[2]).Length,
                growthBytes = new FileInfo(args[2]).Length - new FileInfo(args[1]).Length,
                world.Provider, world.Release, world.RetrievedAtUtc, clade = world.Task.Proposal.Clade,
                scopeTerms = world.Task.ScopeTerms, families = world.Families.Count, entries = world.Entries.Count,
                reviewed = world.Entries.Count(e => e.Evidence.Reviewed == true), unreviewed = world.Entries.Count(e => e.Evidence.Reviewed == false),
                fromReviewedPanel = world.Entries.Count(e => e.FromReviewedPanel), fromClade = world.Entries.Count(e => e.FromClade),
                taxonomyComplete = world.Entries.All(e => e.TaxonId > 0 && e.LineageTaxonIds.Count > 0),
                world.ContentSha256, queries = world.Queries, offlineResaveByteIdentical = true,
                savedFileSha256 = TestFileHash.Upper(args[2]), fileCheck = file.LastSaveCheck,
            };
            string json = JsonSerializer.Serialize(report, DeliveryJson);
            File.WriteAllText(args[2] + ".world-report.json", json);
            Console.WriteLine(json);
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex.GetBaseException()); return 1; }
    }
}
