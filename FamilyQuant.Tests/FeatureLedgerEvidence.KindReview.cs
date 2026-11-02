using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class FeatureLedgerEvidence
{
    internal static int ReviewCommand(string[] args)
    {
        if (args.Length != 7 || args[2] is not ("REOPENED" or "ACCEPTED"))
            throw new ArgumentException("Supply kind, REOPENED/ACCEPTED, reviewer, reason, date timestamp and output directory.");
        string root = FindRoot() ?? throw new DirectoryNotFoundException("Source checkout not found.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        if (!inventory.Observations.Any(row => row.Kind == args[1])) throw new ArgumentException("Unknown frozen observation kind.");
        string path = Path.Combine(root, "docs/feature-ledger-kind-reviews.json");
        var reviews = File.Exists(path) ? JsonSerializer.Deserialize<List<KindReview>>(File.ReadAllText(path), JsonOptions)! : new();
        var review = new KindReview(args[1], args[2], args[3], args[4], args[5]);
        var candidate = reviews.Append(review).ToArray();
        var registry = Load(root, inventory, suppliedReviews: candidate);
        if (registry.FatalProblems.Count > 0) throw new InvalidDataException(string.Join("; ", registry.FatalProblems));
        string ledger = Path.Combine(root, "docs/FEATURE-LEDGER.md");
        string updated = Apply(File.ReadAllText(ledger), registry);
        Directory.CreateDirectory(args[6]);
        if (File.Exists(path)) File.Copy(path, Path.Combine(args[6], "previous-kind-reviews.json"), false);
        WriteJson(path, candidate);
        string temporary = ledger + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, updated, new System.Text.UTF8Encoding(false)); File.Move(temporary, ledger, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Console.WriteLine("Recorded independent kind review: " + args[1] + " " + args[2]);
        return Report(root, inventory, updated, registry, args[6]);
    }
}
