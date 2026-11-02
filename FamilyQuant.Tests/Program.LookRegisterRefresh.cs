using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly string[] RefreshLookSteps =
        { "LK-copy-1", "LK-copy-next-1", "LK-copy-next-2", "LK-copy-next-3", "LK-style-roles" };

    private static FeatureLedger.Observation[] FixedLookCopyCohort(FeatureLedgerEvidence.Register register, FeatureLedger.Inventory inventory)
    {
        if (register.SchemaVersion != 1 || register.FrozenCommit != FeatureLedger.ReferenceCommit
            || !RefreshLookSteps.Take(4).Contains(register.Step, StringComparer.Ordinal)
            || register.Rows.Count == 0 || register.Rows.Select(row => row.ObservationId).Distinct(StringComparer.Ordinal).Count() != register.Rows.Count)
            throw new InvalidDataException("Require an existing LK copy register with unique frozen observations.");
        var byId = inventory.Observations.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var cohort = register.Rows.Select(row => byId.TryGetValue(row.ObservationId, out var observation)
            ? observation : throw new InvalidDataException("Unknown frozen observation: " + row.ObservationId)).ToArray();
        if (cohort.Any(row => FeatureLedger.IsApprovedDrop(row) || row.Kind is not ("help fact" or "tooltip")))
            throw new InvalidDataException("The fixed LK copy cohort contains an unsupported kind or approved retirement.");
        return cohort;
    }

    private static int RefreshLookRegisters(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen source, fresh output and date timestamp.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        string inputs = Path.Combine(output, "inputs"); Directory.CreateDirectory(inputs);
        var originals = new Dictionary<string, FeatureLedgerEvidence.Register>(StringComparer.Ordinal);
        foreach (string step in RefreshLookSteps)
        {
            string copy = Path.Combine(inputs, step + ".json");
            File.Copy(Path.Combine(root, "docs/feature-ledger-evidence", step + ".json"), copy);
            var register = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(copy), FeatureLedgerEvidence.JsonOptions)!;
            if (register.Step != step || register.FrozenCommit != FeatureLedger.ReferenceCommit)
                throw new InvalidDataException("The register identity differs: " + step);
            originals.Add(step, register);
        }
        string styles = Path.Combine(inputs, "LOOK-STYLE-ROLES.json");
        File.Copy(Path.Combine(root, "docs/LOOK-STYLE-ROLES.json"), styles);
        foreach (string step in RefreshLookSteps)
        {
            string packet = Path.Combine(output, "runs", step);
            int result = step == "LK-style-roles"
                ? LookStyleRoleCommand(new[] { "--prove-look-style-roles", args[1], styles, packet, args[3] })
                : LookCopyReceiptCommand(new[] { "--prove-look-copy", args[1], inputs, packet, args[3] }, Path.Combine(inputs, step + ".json"));
            if (result != 0) return result;
            string produced = Path.Combine(packet, step + ".json");
            var regenerated = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(produced), FeatureLedgerEvidence.JsonOptions)!;
            bool same = regenerated.Step == step && originals[step].Rows.Select(row => row.ObservationId)
                .SequenceEqual(regenerated.Rows.Select(row => row.ObservationId), StringComparer.Ordinal);
            Check("LK fixed refresh retains every ordered row in " + step, same);
            if (!same) return 1;
            File.Copy(produced, Path.Combine(output, step + ".json"));
        }
        Console.WriteLine("LK refresh: five exact named registers regenerated, no audit or OPEN-row filter; unsupported observations retained.");
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int LookRefreshCohortTests()
    {
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var register = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(Path.Combine(root,
            "docs/feature-ledger-evidence/LK-copy-1.json")), FeatureLedgerEvidence.JsonOptions)!;
        var selected = FixedLookCopyCohort(register, inventory);
        Check("LK refresh pins the existing row cohort even when receipts are stale or states change",
            selected.Select(row => row.Id).SequenceEqual(register.Rows.Select(row => row.ObservationId), StringComparer.Ordinal));
        bool Reject(FeatureLedgerEvidence.Register input)
        {
            try { FixedLookCopyCohort(input, inventory); return false; }
            catch (InvalidDataException) { return true; }
        }
        Check("LK refresh refuses duplicate observations", Reject(register with { Rows = register.Rows.Concat(register.Rows.Take(1)).ToList() }));
        Check("LK refresh refuses another register identity", Reject(register with { Step = "other" }));
        Check("LK refresh refuses another frozen version", Reject(register with { FrozenCommit = "other" }));
        Check("LK refresh refuses an empty cohort", Reject(register with { Rows = new() }));
        Check("LK refresh refuses unknown observations", Reject(register with { Rows = new() { register.Rows[0] with { ObservationId = "unknown" } } }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
