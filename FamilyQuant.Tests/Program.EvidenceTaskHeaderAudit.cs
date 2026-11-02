using FamilyQuant.Evidence;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int EvidenceTaskHeaderAuditCommand(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Use --evidence-task-header-audit <input.fcf-or-fqproj> <new-report.json>.");
        if (File.Exists(args[2])) throw new IOException("Use a new report path.");
        string inputHash = TestFileHash.Upper(args[1]);
        var file = Path.GetExtension(args[1]).Equals(".fqproj", StringComparison.OrdinalIgnoreCase)
            ? ProjectStore.ReadFqproj(args[1]).fcf : FamilyClusterFile.Load(args[1]);
        // Use the production matcher and parser, not an independent header rule.
        var parser = typeof(EvidenceTaskDiscovery).GetMethod("Header", BindingFlags.NonPublic | BindingFlags.Static)!;
        var pattern = (Regex)typeof(EvidenceTaskDiscovery).GetField("Organism", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var rows = file.MyExtendedFastaItems.Select((protein, index) =>
        {
            bool decoy = NearestReviewed.IsDecoy(protein), contaminant = SequenceLabels.IsContaminant(protein.SequenceIdentifier);
            bool relevant = !decoy && !contaminant;
            var matches = pattern.Matches(protein.Description ?? string.Empty).Cast<Match>().ToArray();
            var ids = matches.Select(match => int.TryParse(match.Groups["id"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int taxon) ? taxon : 0).ToArray();
            bool? accepted = null; string? refusal = null;
            if (relevant)
            {
                try { parser.Invoke(null, new object[] { protein }); accepted = true; }
                catch (TargetInvocationException exception) when (exception.InnerException is InvalidDataException)
                { accepted = false; refusal = exception.InnerException.Message; }
            }
            return new { index, protein.SequenceIdentifier, header = protein.Description, decoy, contaminant, relevant,
                pairs = matches.Length, taxonIds = ids.Where(id => id > 0).Distinct().OrderBy(id => id).ToArray(),
                unparseableTaxonIds = ids.Count(id => id == 0), accepted, refusal };
        }).ToArray();
        bool unchanged = TestFileHash.Upper(args[1]) == inputHash;
        if (!unchanged) throw new InvalidDataException("The input bytes changed during the read-only audit.");
        var report = new
        {
            input = Path.GetFullPath(args[1]), inputSha256 = inputHash,
            compiledParserSha256 = TestFileHash.Upper(typeof(EvidenceTaskDiscovery).Assembly.Location), inputBytesUnchanged = unchanged,
            totalRows = rows.Length, targetRows = rows.Count(row => !row.decoy), decoyRows = rows.Count(row => row.decoy),
            ignoredContaminantTargets = rows.Count(row => !row.decoy && row.contaminant), discoveryTargets = rows.Count(row => row.relevant),
            joinedTargetHeaders = rows.Count(row => !row.decoy && row.pairs > 1),
            joinedTargetHeadersWithDifferentTaxa = rows.Count(row => !row.decoy && row.pairs > 1 && row.taxonIds.Length > 1),
            joinedDiscoveryTargetHeaders = rows.Count(row => row.relevant && row.pairs > 1),
            joinedDiscoveryTargetHeadersWithDifferentTaxa = rows.Count(row => row.relevant && row.pairs > 1 && row.taxonIds.Length > 1),
            acceptedJoinedDiscoveryHeaders = rows.Count(row => row.relevant && row.pairs > 1 && row.accepted == true),
            refusedDiscoveryHeaders = rows.Count(row => row.relevant && row.accepted == false),
            refusalBoundary = "Any rejected non-contaminant target refuses the entire task discovery before HTTP; no target is silently skipped.",
            joinedHeaders = rows.Where(row => row.pairs > 1).ToArray(), refusals = rows.Where(row => row.relevant && row.accepted == false).ToArray(),
        };
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + "\n";
        File.WriteAllText(args[2], json); Console.WriteLine(json); return 0;
    }
}
