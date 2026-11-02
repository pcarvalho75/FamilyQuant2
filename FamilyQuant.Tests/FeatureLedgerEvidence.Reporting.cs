using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class FeatureLedgerEvidence
{
    internal static int Command(string[] args)
    {
        if (args.Length < 2) throw new ArgumentException("Supply an explicit output directory or a register and output directory.");
        string root = FindRoot() ?? throw new DirectoryNotFoundException("Source checkout not found.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        if (args[0] == "--feature-ledger-evidence-import-batch") return ImportBatch(root, inventory, args);
        string ledgerPath = Path.Combine(root, "docs/FEATURE-LEDGER.md");
        string markdown = File.ReadAllText(ledgerPath);
        if (args[0] == "--feature-ledger-evidence-import")
        {
            if (args.Length is not (3 or 4) || args.Length == 4 && args[3] != "--replace-existing")
                throw new ArgumentException("Supply register path, report output directory, and optional --replace-existing.");
            var incoming = JsonSerializer.Deserialize<Register>(File.ReadAllText(args[1]), JsonOptions) ?? throw new InvalidDataException("Empty register.");
            var validation = Load(root, inventory, new[] { incoming });
            if (validation.FatalProblems.Count > 0) throw new InvalidDataException(string.Join("; ", validation.FatalProblems));
            if (validation.Entries.Values.Any(e => e.Row.Proofs.Count > 0 && !e.Valid))
                throw new InvalidDataException("Incoming proof is incomplete or stale: " + string.Join("; ", validation.Entries.Values.SelectMany(e => e.Problems).Take(6)));
            string folder = Path.Combine(root, "docs/feature-ledger-evidence");
            Directory.CreateDirectory(folder);
            string target = Path.Combine(folder, incoming.Step + ".json");
            string text = JsonSerializer.Serialize(incoming, JsonOptions) + "\n";
            if (File.Exists(target) && File.ReadAllText(target) != text)
            {
                if (args.Length != 4) throw new InvalidOperationException("A register for this step already exists. Use an explicit replacement after re-execution.");
                Directory.CreateDirectory(args[2]);
                File.Copy(target, Path.Combine(args[2], "previous-" + incoming.Step + ".json"), false);
            }
            File.WriteAllText(target, text, new UTF8Encoding(false));
            markdown = Apply(markdown, Load(root, inventory));
            var errors = FeatureLedger.Validate(inventory, markdown);
            if (errors.Count > 0) throw new InvalidDataException("Proposed ledger failed its guard: " + string.Join("; ", errors.Take(6)));
            string staged = ledgerPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllText(staged, markdown, new UTF8Encoding(false)); File.Move(staged, ledgerPath, true); }
            finally { if (File.Exists(staged)) File.Delete(staged); }
        }
        else if (args[0] != "--feature-ledger-evidence") throw new ArgumentException("Unknown evidence command.");
        string output = args[0] == "--feature-ledger-evidence-import" ? args[2] : args[1];
        Directory.CreateDirectory(output);
        var registry = Load(root, inventory);
        return Report(root, inventory, markdown, registry, output);
    }

    internal static string Apply(string markdown, Registry registry)
    {
        if (registry.FatalProblems.Count > 0) throw new InvalidDataException(string.Join("; ", registry.FatalProblems));
        var best = registry.Entries.Values.Where(e => e.Valid && !registry.IsKindBlocked(e.Row.ObservationId)).GroupBy(e => e.Row.ObservationId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Step, StringComparer.Ordinal).First(), StringComparer.Ordinal);
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (!lines[i].StartsWith("| FQ15-", StringComparison.Ordinal)) continue;
            string[] cells = lines[i].Split('|').Select(c => c.Trim()).ToArray();
            if (cells.Length != 10 || cells[2] == "DROP") continue;
            if (best.TryGetValue(cells[1], out var entry))
            {
                cells[7] = "VERIFIED"; cells[8] = entry.Reference;
                var test = entry.Row.Proofs.FirstOrDefault(p => p.Kind == "test");
                if (test != null && typeof(Program).GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
                    .Any(m => m.Name == test.Target)) cells[6] = "test:" + test.Target;
            }
            else if (cells[7] == "VERIFIED") { cells[7] = "OPEN"; cells[8] = "pending: execution evidence missing or stale"; }
            lines[i] = "| " + string.Join(" | ", cells.Skip(1).Take(8)) + " |";
        }
        string updated = string.Join('\n', lines);
        int verified = lines.Count(line => line.StartsWith("| FQ15-", StringComparison.Ordinal) && line.Split('|')[7].Trim() == "VERIFIED");
        int open = lines.Count(line => line.StartsWith("| FQ15-", StringComparison.Ordinal) && line.Split('|')[7].Trim() == "OPEN");
        return System.Text.RegularExpressions.Regex.Replace(updated, @"Authorized DROP rows: \*\*296\*\*;[^\n]*",
            "Authorized DROP rows: **296**; proposed VERIFIED retained rows: **" + verified.ToString(CultureInfo.InvariantCulture)
            + "**; OPEN retained rows: **" + open.ToString(CultureInfo.InvariantCulture) + "**.");
    }

    internal static string CurrentPath(string root, string source)
    {
        if (File.Exists(Path.Combine(root, source))) return source;
        string core = source.Replace("FamilyQuant/", "FamilyQuant.Core/", StringComparison.Ordinal);
        return File.Exists(Path.Combine(root, core)) ? core : "";
    }
    internal static string Owner(FeatureLedger.Observation row) => row.Source switch
    {
        var source when source.Contains("ClusterGenerationControl", StringComparison.Ordinal) => "C1/C5/C13",
        var source when source.Contains("HelpContent", StringComparison.Ordinal) => "C13",
        var source when source.Contains("ProjectStore", StringComparison.Ordinal) => "X7",
        var source when source.Contains("FullXicComputer", StringComparison.Ordinal) || source.Contains("FamilyEnvelopeQuant", StringComparison.Ordinal) => "X5",
        var source when source.Contains("Presentation", StringComparison.Ordinal) => "X9 (protected)",
        var source when source.Contains("DeNovoControl", StringComparison.Ordinal) => "X6/C13",
        var source when source.Contains("DeNovo/", StringComparison.Ordinal) || source.Contains("Homology/", StringComparison.Ordinal) => "EV/X6",
        var source when source.Contains("Evidence", StringComparison.Ordinal) || source.Contains("ProteinDomains", StringComparison.Ordinal) => "EV evidence workflow",
        var source when source.Contains("USearchTools", StringComparison.Ordinal) || source.Contains("Redundancy", StringComparison.Ordinal) => "AS/X14/X15",
        var source when source.Contains("WordCluster", StringComparison.Ordinal) || source.Contains("SuperCluster", StringComparison.Ordinal) => "AS grouping / LK review",
        var source when source.Contains("NormalizationMode", StringComparison.Ordinal) || source.Contains("SharedPeptidePolicy", StringComparison.Ordinal)
            || source.Contains("QuantitationProfile", StringComparison.Ordinal) => "RS/C5",
        var source when source.Contains("MainWindow", StringComparison.Ordinal) || source.Contains("ComputeXicWindow", StringComparison.Ordinal) => "SH",
        var source when source.Contains("Window", StringComparison.Ordinal) || source.Contains("GridCopy", StringComparison.Ordinal) || source.Contains("ReportView", StringComparison.Ordinal) => "SH/N6 secondary views",
        var source when source.Contains("Analytics", StringComparison.Ordinal) || source.Contains("Chart", StringComparison.Ordinal) => "RS",
        _ => "SH/N6 preservation queue"
    };

    internal static int Report(string root, FeatureLedger.Inventory inventory, string markdown, Registry registry, string output, bool reuseValidatedRegistry = false)
    {
        var ledgerProblems = FeatureLedger.Validate(inventory, markdown, evidenceOverride: reuseValidatedRegistry ? registry : null);
        var states = markdown.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("| FQ15-", StringComparison.Ordinal))
            .Select(l => l.Split('|').Select(c => c.Trim()).ToArray()).Where(c => c.Length == 10)
            .GroupBy(c => c[1]).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var entriesById = registry.Entries.Values.GroupBy(e => e.Row.ObservationId).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var paths = inventory.Files.ToDictionary(file => file.Path, file => CurrentPath(root, file.Path), StringComparer.Ordinal);
        var mappings = inventory.Observations.Select(observation =>
        {
            string current = paths[observation.Source];
            states.TryGetValue(observation.Id, out var declared);
            bool drop = FeatureLedger.IsApprovedDrop(observation);
            bool verified = !drop && declared?[7] == "VERIFIED" && registry.IsVerified(observation.Id, declared[8]);
            var entries = entriesById.GetValueOrDefault(observation.Id) ?? Array.Empty<Entry>();
            return new
            {
                observation.Id, observation.Kind, observation.Source, observation.Line, observation.Detail,
                currentPath = current, movement = current == "" ? "unmapped" : current == observation.Source ? "same-path" : "moved-to-Core",
                owner = Owner(observation), state = drop ? "APPROVED_DROP" : verified ? "VERIFIED" : "OPEN",
                declaredState = declared?[7] ?? "MISSING", evidence = verified ? declared![8] : "",
                mappings = entries.SelectMany(e => e.Row.Implementation).Distinct().ToArray(),
                proofs = verified ? registry.Entries[declared![8]].Row.Proofs.ToArray() : Array.Empty<Proof>(),
                notes = entries.Select(e => e.Row.Note).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToArray(),
                problems = entries.SelectMany(e => e.Problems).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray()
            };
        }).ToArray();
        var counts = mappings.GroupBy(r => r.Kind).OrderBy(g => g.Key, StringComparer.Ordinal).Select(group => new
        {
            kind = group.Key, verified = group.Count(r => r.state == "VERIFIED"), open = group.Count(r => r.state == "OPEN"),
            approvedDrop = group.Count(r => r.state == "APPROVED_DROP")
        }).ToArray();
        var sample = mappings.Where(r => r.state == "VERIFIED").OrderBy(r => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(FeatureLedger.ReferenceInventorySha256 + "|" + r.Id))), StringComparer.Ordinal).Take(200).ToArray();
        var report = new
        {
            frozenCommit = FeatureLedger.ReferenceCommit, inventorySha256 = FeatureLedger.Fingerprint(inventory),
            verified = mappings.Count(r => r.state == "VERIFIED"), open = mappings.Count(r => r.state == "OPEN"),
            approvedDrop = mappings.Count(r => r.state == "APPROVED_DROP"), counts,
            retainedMovement = mappings.Where(r => r.state != "APPROVED_DROP").GroupBy(r => r.movement)
                .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new { movement = g.Key, count = g.Count() }).ToArray(),
            sampleSeed = FeatureLedger.ReferenceInventorySha256, sampleCount = sample.Length,
            reopenedKinds = registry.ReopenedKinds.OrderBy(kind => kind, StringComparer.Ordinal).ToArray(),
            ledgerProblems = ledgerProblems.OrderBy(p => p, StringComparer.Ordinal).ToArray(),
            fatalProblems = registry.FatalProblems.OrderBy(p => p, StringComparer.Ordinal).ToArray()
        };
        WriteJson(Path.Combine(output, "report.json"), report);
        WriteJson(Path.Combine(output, "mapping.json"), mappings);
        WriteJson(Path.Combine(output, "review-sample-50.json"), sample.Take(50).ToArray());
        WriteJson(Path.Combine(output, "review-sample-200.json"), sample);
        WriteJson(Path.Combine(output, "source-files.json"), inventory.Files.Select(file => new
        { frozenPath = file.Path, frozenSha256 = file.Sha256, file.Observations, sourceFileNow = paths[file.Path] }).ToArray());
        var tsv = new StringBuilder("kind\tVERIFIED\tOPEN\tAPPROVED_DROP\n");
        foreach (var count in counts) tsv.Append(count.kind).Append('\t').Append(count.verified.ToString(CultureInfo.InvariantCulture)).Append('\t')
            .Append(count.open.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(count.approvedDrop.ToString(CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(Path.Combine(output, "counts-by-kind.tsv"), tsv.ToString(), new UTF8Encoding(false));
        var owners = new StringBuilder("suggested_owner\tOPEN\n");
        foreach (var group in mappings.Where(row => row.state == "OPEN").GroupBy(row => row.owner).OrderBy(g => g.Key, StringComparer.Ordinal))
            owners.Append(group.Key).Append('\t').Append(group.Count().ToString(CultureInfo.InvariantCulture)).Append('\n');
        File.WriteAllText(Path.Combine(output, "open-by-owner.tsv"), owners.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Ledger evidence: {report.verified} VERIFIED, {report.open} OPEN, {report.approvedDrop} APPROVED_DROP.");
        Console.WriteLine($"Review sample: {sample.Length} rows; deterministic seed {FeatureLedger.ReferenceInventorySha256}.");
        bool stale = mappings.Any(r => r.declaredState == "VERIFIED" && r.state != "VERIFIED");
        return registry.FatalProblems.Count == 0 && ledgerProblems.Count == 0 && !stale ? 0 : 1;
    }
}
