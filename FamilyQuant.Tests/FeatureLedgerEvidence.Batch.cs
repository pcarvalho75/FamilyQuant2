using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class FeatureLedgerEvidence
{
    // Scoped to one transaction, just as Load already shares these reads across registers.
    internal sealed class ValidationReadCache
    {
        internal Dictionary<string, string> Hashes { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, string[]> Logs { get; } = new(StringComparer.OrdinalIgnoreCase);
        internal Dictionary<string, HashSet<string>> SourceEolHashes { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    internal static int ImportBatch(string root, FeatureLedger.Inventory inventory, string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("Supply output directory, one or more registers, and optional --replace-existing.");
        bool replace = args[^1] == "--replace-existing";
        string[] paths = args.Skip(2).Take(args.Length - 2 - (replace ? 1 : 0)).ToArray();
        if (paths.Length == 0 || paths.Any(p => p.StartsWith("--", StringComparison.Ordinal)))
            throw new ArgumentException("Supply output directory, one or more registers, and optional --replace-existing.");
        string output = args[1], folder = Path.Combine(root, "docs/feature-ledger-evidence");
        string ledgerPath = Path.Combine(root, "docs/FEATURE-LEDGER.md");
        string markdown = File.ReadAllText(ledgerPath);
        var files = new Dictionary<string, Register>(StringComparer.OrdinalIgnoreCase);
        var parseProblems = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var bytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        foreach (string path in Directory.GetFiles(folder, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            try
            {
                texts[path] = File.ReadAllText(path); bytes[path] = File.ReadAllBytes(path);
                files[path] = JsonSerializer.Deserialize<Register>(texts[path], JsonOptions) ?? throw new InvalidDataException("Empty register.");
            }
            catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
            { parseProblems[path] = Path.GetFileName(path) + ": " + ex.Message; }
        }
        var reads = new ValidationReadCache();
        var reviews = Load(root, inventory, Array.Empty<Register>());
        var validated = new Dictionary<Register, Registry>(ReferenceEqualityComparer.Instance);
        Registry ValidateRegister(Register register)
        {
            if (!validated.TryGetValue(register, out var result))
                validated[register] = result = Load(root, inventory, new[] { register }, Array.Empty<KindReview>(), reads);
            return result;
        }
        Registry Combine(IEnumerable<Register> registers, bool includeParseProblems)
        {
            var result = new Registry();
            foreach (var pair in reviews.KindsById) result.KindsById.Add(pair.Key, pair.Value);
            result.ReopenedKinds.UnionWith(reviews.ReopenedKinds);
            result.FatalProblems.AddRange(reviews.FatalProblems);
            if (includeParseProblems) result.FatalProblems.AddRange(parseProblems.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value));
            foreach (var register in registers.OrderBy(r => r.Step, StringComparer.Ordinal))
            {
                var part = ValidateRegister(register);
                result.FatalProblems.AddRange(part.FatalProblems);
                foreach (var entry in part.Entries)
                    if (!result.Entries.TryAdd(entry.Key, entry.Value)) result.FatalProblems.Add("Duplicate evidence row: " + entry.Key);
            }
            return result;
        }
        var changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var backups = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        Registry? final = null;
        foreach (string path in paths)
        {
            string fullInput = Path.GetFullPath(path);
            string incomingText = texts.TryGetValue(fullInput, out string? stagedInput) ? stagedInput : File.ReadAllText(path);
            var incoming = JsonSerializer.Deserialize<Register>(incomingText, JsonOptions) ?? throw new InvalidDataException("Empty register.");
            var validation = Combine(new[] { incoming }, false);
            if (validation.FatalProblems.Count > 0) throw new InvalidDataException(string.Join("; ", validation.FatalProblems));
            if (validation.Entries.Values.Any(e => e.Row.Proofs.Count > 0 && !e.Valid))
                throw new InvalidDataException("Incoming proof is incomplete or stale: " + string.Join("; ", validation.Entries.Values.SelectMany(e => e.Problems).Take(6)));
            string target = Path.Combine(folder, incoming.Step + ".json");
            string text = JsonSerializer.Serialize(incoming, JsonOptions) + "\n";
            if (texts.TryGetValue(target, out string? previous) && previous != text)
            {
                if (!replace) throw new InvalidOperationException("A register for this step already exists. Use an explicit replacement after re-execution.");
                string backup = Path.Combine(output, "previous-" + incoming.Step + ".json");
                if (backups.ContainsKey(backup) || File.Exists(backup) || Directory.Exists(backup))
                    throw new IOException("The file '" + backup + "' already exists.");
                backups.Add(backup, bytes[target]);
            }
            texts[target] = text; bytes[target] = new UTF8Encoding(false).GetBytes(text);
            files[target] = incoming; parseProblems.Remove(target); changes[target] = text;
            final = Combine(files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Value), true);
            // Apply each intermediate state to preserve planned-check changes even if a later
            // replacement reopens that row. Rendering still happens only after the final state.
            markdown = Apply(markdown, final);
            var errors = FeatureLedger.Validate(inventory, markdown, evidenceOverride: final);
            if (errors.Count > 0) throw new InvalidDataException("Proposed ledger failed its guard: " + string.Join("; ", errors.Take(6)));
        }
        CommitBatch(ledgerPath, markdown, changes, backups, output);
        return Report(root, inventory, markdown, final!, output, reuseValidatedRegistry: true);
    }

    private static void CommitBatch(string ledgerPath, string markdown, Dictionary<string, string> changes,
        Dictionary<string, byte[]> backups, string output)
    {
        var originals = changes.Keys.Append(ledgerPath).ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null, StringComparer.OrdinalIgnoreCase);
        var written = new List<string>();
        var createdBackups = new List<string>();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ledgerPath)!);
            Directory.CreateDirectory(output);
            foreach (var backup in backups)
            {
                using var stream = new FileStream(backup.Key, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                createdBackups.Add(backup.Key); stream.Write(backup.Value);
            }
            foreach (var change in changes)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(change.Key)!);
                AtomicWrite(change.Key, change.Value); written.Add(change.Key);
            }
            AtomicWrite(ledgerPath, markdown); written.Add(ledgerPath);
        }
        catch
        {
            foreach (string path in written.AsEnumerable().Reverse())
                if (originals[path] is { } original) File.WriteAllBytes(path, original);
                else if (File.Exists(path)) File.Delete(path);
            foreach (string path in createdBackups) File.Delete(path);
            throw;
        }
    }

    private static void AtomicWrite(string path, string text)
    {
        string staged = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(staged, text, new UTF8Encoding(false)); File.Move(staged, path, true); }
        finally { if (File.Exists(staged)) File.Delete(staged); }
    }
}
