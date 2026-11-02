using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class FeatureLedgerEvidence
{
    internal sealed record Artifact(string Path, string Sha256);
    internal sealed record Run(string Id, string Command, string ExecutedAt, string Commit, List<Artifact> Artifacts, string? WorkingDirectory = null);
    internal sealed record Implementation(string Path, string Symbol, string Sha256);
    internal sealed record Proof(string RunId, string Kind, string Target, string Assertion, string Artifact, string InspectedBy);
    internal sealed record Row(string ObservationId, List<Implementation> Implementation, List<Proof> Proofs, string? Note = null,
        string? KindReceiptId = null, string? Compared = null);
    internal sealed record KindReceipt(string Id, string Kind, List<Proof> Proofs);
    internal sealed record Register(int SchemaVersion, string Step, string FrozenCommit, List<Run> Runs, List<Row> Rows,
        List<KindReceipt>? KindReceipts = null);
    internal sealed record KindReview(string Kind, string Decision, string Reviewer, string Reason, string ReviewedAt);
    internal sealed record Entry(string Step, Row Row, List<string> Problems)
    {
        public bool Valid => Row.Proofs.Count > 0 && Problems.Count == 0;
        public string Reference => "evidence:" + Step + ":" + Row.ObservationId;
    }
    internal static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    internal sealed class Registry
    {
        internal Dictionary<string, Entry> Entries { get; } = new(StringComparer.Ordinal);
        internal List<string> FatalProblems { get; } = new();
        internal Dictionary<string, string> KindsById { get; } = new(StringComparer.Ordinal);
        internal HashSet<string> ReopenedKinds { get; } = new(StringComparer.Ordinal);
        internal bool IsKindBlocked(string id) => KindsById.TryGetValue(id, out string? kind) && ReopenedKinds.Contains(kind);
        internal bool IsVerified(string id, string reference) => FatalProblems.Count == 0 && Entries.TryGetValue(reference, out var entry)
            && entry.Row.ObservationId == id && entry.Valid && !IsKindBlocked(id);
    }

    internal static string? FindRoot()
    {
        foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var folder = new DirectoryInfo(start); folder != null; folder = folder.Parent)
                if (File.Exists(Path.Combine(folder.FullName, "docs/feature-ledger-scan.json"))) return folder.FullName;
        return null;
    }

    internal static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    internal static void WriteJson(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, JsonOptions) + "\n", new UTF8Encoding(false));

    internal static Registry Load(string root, FeatureLedger.Inventory inventory, IEnumerable<Register>? supplied = null,
        IEnumerable<KindReview>? suppliedReviews = null)
    {
        var registry = new Registry();
        foreach (var observation in inventory.Observations) registry.KindsById[observation.Id] = observation.Kind;
        try
        {
            string reviewsPath = Path.Combine(root, "docs/feature-ledger-kind-reviews.json");
            var reviews = suppliedReviews ?? (File.Exists(reviewsPath)
                ? JsonSerializer.Deserialize<List<KindReview>>(File.ReadAllText(reviewsPath), JsonOptions) ?? new()
                : new List<KindReview>());
            foreach (var group in reviews.GroupBy(review => review.Kind))
            {
                var latest = group.Last();
                if (!inventory.Observations.Any(row => row.Kind == latest.Kind) || latest.Decision is not ("REOPENED" or "ACCEPTED")
                    || string.IsNullOrWhiteSpace(latest.Reviewer) || string.IsNullOrWhiteSpace(latest.Reason)
                    || !DateTimeOffset.TryParse(latest.ReviewedAt, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _))
                    registry.FatalProblems.Add("Invalid kind review: " + latest.Kind);
                else if (latest.Decision == "REOPENED") registry.ReopenedKinds.Add(latest.Kind);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException) { registry.FatalProblems.Add("Kind review history: " + ex.Message); }
        var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var logs = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        string CachedHash(string path)
        {
            if (!hashes.TryGetValue(path, out string? value)) hashes[path] = value = Hash(path);
            return value;
        }
        bool Matches(string path, string expected) => Regex.IsMatch(expected ?? "", "^[0-9A-Fa-f]{64}$")
            && File.Exists(path) && string.Equals(CachedHash(path), expected, StringComparison.OrdinalIgnoreCase);
        string ResolveArtifact(string path)
        {
            string full = Path.GetFullPath(Path.IsPathFullyQualified(path) ? path : Path.Combine(root, path));
            if (full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("Orion", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Artifact path is outside this mission.");
            return full;
        }
        var registers = new List<Register>();
        if (supplied != null) registers.AddRange(supplied);
        else
        {
            string folder = Path.Combine(root, "docs/feature-ledger-evidence");
            if (Directory.Exists(folder))
                foreach (string path in Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
                    try { registers.Add(JsonSerializer.Deserialize<Register>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Empty register.")); }
                    catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
                    { registry.FatalProblems.Add(Path.GetFileName(path) + ": " + ex.Message); }
        }
        var known = inventory.Observations.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var register in registers.OrderBy(r => r.Step, StringComparer.Ordinal))
        {
            if (register.SchemaVersion != 1 || register.FrozenCommit != FeatureLedger.ReferenceCommit
                || !Regex.IsMatch(register.Step ?? "", "^[A-Za-z0-9-]+$") || register.Rows == null || register.Runs == null)
            { registry.FatalProblems.Add("Invalid register header: " + register.Step); continue; }
            if (register.Runs.Any(r => r == null || string.IsNullOrWhiteSpace(r.Id)) || register.Rows.Any(r => r == null))
            { registry.FatalProblems.Add("Null or unnamed record in register: " + register.Step); continue; }
            var runs = register.Runs.GroupBy(r => r.Id).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
            foreach (var storedRow in register.Rows)
            {
                var row = storedRow;
                var problems = new List<string>();
                if (row.Implementation == null || row.Proofs == null)
                { registry.FatalProblems.Add("Malformed evidence row: " + row.ObservationId); continue; }
                if (!known.TryGetValue(row.ObservationId, out var observation))
                { problems.Add("Unknown observation."); registry.FatalProblems.Add("Unknown observation: " + row.ObservationId); }
                else if (FeatureLedger.IsApprovedDrop(observation)) problems.Add("Approved DROP is not behavioral verification.");
                if (!string.IsNullOrWhiteSpace(row.KindReceiptId))
                {
                    var kinds = register.KindReceipts?.Where(kind => kind.Id == row.KindReceiptId).ToArray() ?? Array.Empty<KindReceipt>();
                    if (kinds.Length != 1 || kinds[0].Kind != observation?.Kind || kinds[0].Proofs == null || kinds[0].Proofs.Count == 0)
                        problems.Add("Missing, ambiguous or wrong-kind shared receipt.");
                    else if (string.IsNullOrWhiteSpace(row.Compared)) problems.Add("A kind receipt requires this row's exact comparison description.");
                    else row = row with { Proofs = row.Proofs.Concat(kinds[0].Proofs).Distinct().ToList() };
                }
                if (row.Proofs.Count > 0 && row.Implementation.Count == 0) problems.Add("Missing implementation mapping.");
                if (row.Proofs.Count > 0)
                foreach (var source in row.Implementation)
                {
                    try
                    {
                        if (Path.IsPathFullyQualified(source.Path)) throw new InvalidDataException("Implementation paths must be repository-relative.");
                        string full = Path.GetFullPath(Path.Combine(root, source.Path));
                        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("Implementation leaves this worktree.");
                        if (full.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(p => p.Equals("Orion", StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidDataException("Implementation is outside this mission.");
                        if (string.IsNullOrWhiteSpace(source.Symbol) || !Matches(full, source.Sha256)) problems.Add("Implementation hash/symbol differs: " + source.Path);
                    }
                    catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or UnauthorizedAccessException)
                    { problems.Add(ex.Message); }
                }
                foreach (var proof in row.Proofs)
                {
                    if (!runs.TryGetValue(proof.RunId, out var candidates) || candidates.Length != 1)
                    { problems.Add("Unknown or duplicate run: " + proof.RunId); continue; }
                    var run = candidates[0];
                    if (string.IsNullOrWhiteSpace(run.Command) || !Regex.IsMatch(run.Commit ?? "", "^[0-9A-Fa-f]{7,40}$")
                        || !DateTimeOffset.TryParse(run.ExecutedAt, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out _)
                        || run.Artifacts == null) problems.Add("Run lacks command, commit or dated provenance: " + run.Id);
                    try
                    {
                        if (run.Artifacts != null)
                        foreach (var declared in run.Artifacts)
                            if (!Matches(ResolveArtifact(declared.Path), declared.Sha256))
                                problems.Add("A declared execution artifact changed: " + declared.Path);
                        string full = ResolveArtifact(proof.Artifact);
                        var artifact = run.Artifacts?.SingleOrDefault(a => ResolveArtifact(a.Path).Equals(full, StringComparison.OrdinalIgnoreCase));
                        if (artifact == null || !Matches(full, artifact.Sha256))
                        { problems.Add("Missing or changed artifact: " + proof.Artifact); continue; }
                        if (string.IsNullOrWhiteSpace(proof.Assertion)) problems.Add("Missing observation-specific assertion.");
                        if (proof.Kind == "test")
                        {
                            bool knownMethod = typeof(Program).GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                                .Any(m => m.Name == proof.Target);
                            if (!knownMethod) problems.Add("Unknown executed test target: " + proof.Target);
                            if (!logs.TryGetValue(full, out var lines)) logs[full] = lines = File.ReadAllLines(full);
                            string wanted = Regex.Replace(proof.Assertion.Trim(), "^PASS\\s+", "");
                            bool assertion = lines.Any(line => Regex.IsMatch(line.TrimStart(), "^PASS\\s+")
                                && Regex.Replace(line.Trim(), "^PASS\\s+", "") == wanted);
                            var summaries = lines.SelectMany(line => Regex.Matches(line, @"(\d+) passed, (\d+) failed(?:, (\d+) skipped)?").Cast<Match>()).ToArray();
                            bool green = summaries.Length > 0 && summaries[^1].Groups[2].Value == "0"
                                && !lines.Any(line => Regex.IsMatch(line.TrimStart(), "^FAIL\\s|^Unhandled exception"));
                            if (!assertion || !green) problems.Add("Assertion is absent or its execution was not green: " + proof.Assertion);
                        }
                        else if (proof.Kind == "render")
                        {
                            if (!Path.GetExtension(full).Equals(".png", StringComparison.OrdinalIgnoreCase)
                                || string.IsNullOrWhiteSpace(proof.Target) || string.IsNullOrWhiteSpace(proof.InspectedBy))
                                problems.Add("Render requires a PNG, a target and a named inspector.");
                            using (var stream = File.OpenRead(full))
                            {
                                byte[] signature = new byte[8];
                                if (stream.Read(signature) != 8 || !signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                                    problems.Add("Render artifact is not a PNG.");
                            }
                            if (!row.Proofs.Any(p => p.Kind == "test") && observation?.Kind is ("handler" or "handler definition" or "shortcut" or "format/export" or "export/storage fact"))
                                problems.Add("This observation requires an executed behavior/format test, not a render alone.");
                        }
                        else problems.Add("Unknown proof kind: " + proof.Kind);
                    }
                    catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
                    { problems.Add(ex.Message); }
                }
                var entry = new Entry(register.Step!, row, problems.Distinct(StringComparer.Ordinal).ToList());
                if (!registry.Entries.TryAdd(entry.Reference, entry)) registry.FatalProblems.Add("Duplicate evidence row: " + entry.Reference);
            }
        }
        return registry;
    }
}
