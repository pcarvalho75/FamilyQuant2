using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> LedgerAddedContractsCommands() =>
    [new("--test-ledger-added-contracts", 10514, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ledger-added-contracts", (args, _) =>
        {
            LedgerAddedContractsTests(args.Length > 1 ? args[1] : null);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void LedgerAddedContractsTests(string? output = null)
    {
        Section("Added ledger contracts survive native regeneration");
        string repo = FindLedgerRoot() ?? throw new DirectoryNotFoundException("Ledger source checkout unavailable.");
        string root = output ?? Path.Combine(Path.GetTempPath(), "FamilyQuant.LedgerAddedContracts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var utf8 = new UTF8Encoding(false);
        const string heading = "## Added contracts after the frozen source";
        string canonical = File.ReadAllText(Path.Combine(repo, "docs", "FEATURE-LEDGER.md"));
        int start = canonical.IndexOf("\n" + heading, StringComparison.Ordinal);
        if (start < 0) throw new InvalidDataException("The canonical added-contracts section is missing.");
        string section = canonical[(start + 1)..];
        string lfSection = section.Replace("\r\n", "\n", StringComparison.Ordinal);
        Check("regeneration fixture includes the actual FQ2 database contract", section.Contains("| FQ2-DATABASE-ENTRIES |", StringComparison.Ordinal));
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Dictionary<string, string> Protected(string folder) => Directory.GetFiles(Path.Combine(folder, "docs"), "*", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(path) is "FEATURE-LEDGER.md" or "feature-ledger-scan.json"
                || Path.GetRelativePath(folder, path).Replace('\\', '/').StartsWith("docs/feature-ledger-evidence/", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetRelativePath(folder, path).Replace('\\', '/'), Hash, StringComparer.Ordinal);
        var protectedBefore = Protected(repo);
        string frozen = Path.Combine(root, "frozen"), target = Path.Combine(root, "generated");
        Directory.CreateDirectory(Path.Combine(frozen, "FamilyQuant"));
        File.WriteAllText(Path.Combine(frozen, "FamilyQuant", "Fixture.xaml"),
            "<Window xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\"><Button Content=\"Stored fixture\" ToolTip=\"Frozen fixture fact\" /></Window>\n", utf8);
        int Generate()
        {
            string[] args = { FeatureLedger.GenerateArgument, frozen, target };
            if (!TestCommandRegistry.TryRun(args, TestCommandPhase.Raw, root, out int exit))
                throw new InvalidOperationException("The native ledger generator command is not registered.");
            return exit;
        }
        Check("the registered generator creates a fresh isolated ledger", Generate() == 0);
        string ledger = Path.Combine(target, "docs", "FEATURE-LEDGER.md"), scan = Path.Combine(target, "docs", "feature-ledger-scan.json");
        byte[] baseline = File.ReadAllBytes(ledger), baselineScan = File.ReadAllBytes(scan);
        Check("isolated source produces frozen observation rows", File.ReadAllText(ledger).Split('\n').Any(line => line.StartsWith("| FQ15-", StringComparison.Ordinal)));
        string receipt = Path.Combine(target, "docs", "feature-ledger-evidence", "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(receipt)!);
        File.WriteAllText(receipt, "{\"fixture\":\"opaque preservation bytes, never acceptance evidence\"}\n", utf8);
        string receiptHash = Hash(receipt), frozenHash = Hash(Path.Combine(frozen, "FamilyQuant", "Fixture.xaml"));
        foreach (var variant in new[]
        {
            (Name: "canonical checkout", Section: section),
            (Name: "LF", Section: lfSection),
            (Name: "CRLF with an unrelated future contract", Section: (lfSection + "| FQ2-FUTURE-FIXTURE | ADD | Preserve opaque Unicode \u03b1 | fixture only |\n").Replace("\n", "\r\n", StringComparison.Ordinal)),
            (Name: "no final newline", Section: lfSection.TrimEnd('\n', '\r')),
        })
        {
            byte[] suffix = utf8.GetBytes("\n" + variant.Section);
            File.WriteAllBytes(ledger, baseline.Concat(suffix).ToArray());
            int exit = Generate();
            byte[] actual = File.ReadAllBytes(ledger);
            Check(variant.Name + ": registered generation succeeds", exit == 0);
            Check(variant.Name + ": added-contract section survives byte for byte", actual.Length == baseline.Length + suffix.Length
                && actual.AsSpan(baseline.Length).SequenceEqual(suffix));
            Check(variant.Name + ": frozen FQ15 output is unchanged", actual.Length >= baseline.Length && actual.AsSpan(0, baseline.Length).SequenceEqual(baseline));
            Check(variant.Name + ": scan and receipt bytes are unchanged", File.ReadAllBytes(scan).SequenceEqual(baselineScan) && Hash(receipt) == receiptHash);
            Check(variant.Name + ": repeated native generation is byte-identical", Generate() == 0 && File.ReadAllBytes(ledger).SequenceEqual(actual));
        }
        File.WriteAllBytes(ledger, baseline.Concat(utf8.GetBytes("Inline mention: " + heading + "\n")).ToArray());
        Check("an inline heading mention does not become an added-contract section", Generate() == 0 && File.ReadAllBytes(ledger).SequenceEqual(baseline));
        File.WriteAllBytes(ledger, baseline.Concat(utf8.GetBytes("\n" + section + "\n" + section)).ToArray());
        byte[] duplicate = File.ReadAllBytes(ledger);
        bool refused = false;
        try { Generate(); } catch (InvalidDataException) { refused = true; }
        Check("ambiguous duplicate sections are refused", refused);
        Check("duplicate refusal preserves ledger, scan and receipts", File.ReadAllBytes(ledger).SequenceEqual(duplicate)
            && File.ReadAllBytes(scan).SequenceEqual(baselineScan) && Hash(receipt) == receiptHash);
        var protectedAfter = Protected(repo);
        bool canonicalUnchanged = protectedBefore.Count == protectedAfter.Count
            && protectedBefore.All(pair => protectedAfter.TryGetValue(pair.Key, out string? hash) && hash == pair.Value);
        Check("regeneration changes no frozen source or canonical ledger, scan or receipts", canonicalUnchanged
            && Hash(Path.Combine(frozen, "FamilyQuant", "Fixture.xaml")) == frozenHash);
        File.WriteAllText(Path.Combine(root, "preservation.json"), JsonSerializer.Serialize(new
        {
            CanonicalArtifactsUnchanged = canonicalUnchanged, ProtectedArtifacts = protectedBefore,
            FrozenSourceUnchanged = Hash(Path.Combine(frozen, "FamilyQuant", "Fixture.xaml")) == frozenHash,
            GeneratedScanUnchanged = File.ReadAllBytes(scan).SequenceEqual(baselineScan), ReceiptUnchanged = Hash(receipt) == receiptHash,
        }, new JsonSerializerOptions { WriteIndented = true }), utf8);
    }
}
