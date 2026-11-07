using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FeatureLedgerReleaseDeferralTests(string root)
    {
        Section("Z17a11 D90 version-bound ledger deferrals");
        const string reason = "not re-proved for 2.1.11; receipts lost after 2.1.10; verify again after release";
        const string listFile = "docs/LEDGER-REVERIFY-2.1.11.md";
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var retained = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row)).Select(row => row.Id).ToArray();
        string first = retained[0];
        string deferred = string.Join('\n', FeatureLedger.Markdown(inventory).Split('\n').Select(line =>
        {
            if (!line.StartsWith("| FQ15-", StringComparison.Ordinal)) return line;
            var cells = line.Split('|');
            if (cells[7].Trim() == "OPEN") cells[8] = " " + reason + " ";
            return string.Join('|', cells);
        }));
        string Change(string id, int column, string value) => string.Join('\n', deferred.Split('\n').Select(line =>
        {
            if (!line.StartsWith("| " + id + " |", StringComparison.Ordinal)) return line;
            var cells = line.Split('|'); cells[column] = " " + value + " "; return string.Join('|', cells);
        }));
        string temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FamilyQuant.Ledger.Release"));
        string folder = Path.Combine(temporaryRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "docs"));
        File.Copy(Path.Combine(root, "docs/feature-ledger-scan.json"), Path.Combine(folder, "docs/feature-ledger-scan.json"));
        string listPath = Path.Combine(folder, listFile);
        void List(string[] ids) => File.WriteAllText(listPath, "# Ledger reverification for 2.1.11\n\nReason for each listed row: " + reason
            + "\n\n| Register | Rows |\n| --- | ---: |\n| fixture | " + ids.Length.ToString(CultureInfo.InvariantCulture)
            + " |\n\n| ID | Original register |\n| --- | --- |\n" + string.Join('\n', ids.Select(id => "| " + id + " | fixture |")) + "\n");
        var registry = new FeatureLedgerEvidence.Registry();
        string previousDirectory = Environment.CurrentDirectory;
        string? previousVersion = Environment.GetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable);
        try
        {
            Environment.CurrentDirectory = folder;
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "2.1.11.0");
            List(retained);
            var accepted = FeatureLedger.Validate(inventory, deferred, true, registry);
            Check("D90 listed OPEN rows with the exact 2.1.11 reason pass", accepted.Count == 0, LedgerDiagnostics.Capped(accepted));
            Check("D90 an OPEN row without the exact text fails", FeatureLedger.Validate(inventory, Change(first, 8, "pending"), true, registry)
                .Contains("Release has an open row: " + first));
            Check("D90 a changed deferral sentence fails", FeatureLedger.Validate(inventory, Change(first, 8, reason + "."), true, registry)
                .Contains("Release has an open row: " + first));
            List(retained.Skip(1).ToArray());
            Check("D90 a deferred row missing from the list fails", FeatureLedger.Validate(inventory, deferred, true, registry)
                .Contains("Release has an open row: " + first));
            List(retained);
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "2.1.12.0");
            Check("D90 the same deferred rows fail under version 2.1.12", FeatureLedger.Validate(inventory, deferred, true, registry)
                .Contains("Release has an open row: " + first));
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "2.1.11.1");
            Check("D90 a later four-part release also fails", FeatureLedger.Validate(inventory, deferred, true, registry)
                .Contains("Release has an open row: " + first));
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, "2.1.11.0");
            Check("D90 a listed ID that became VERIFIED fails the reverse list check",
                FeatureLedger.Validate(inventory, Change(first, 7, "VERIFIED"), true, registry, checkReceipts: false)
                    .Contains("D90 list entry is not OPEN in the ledger: " + first));
            var allIds = inventory.Observations.Select(row => row.Id).ToHashSet(StringComparer.Ordinal);
            string unknown = Enumerable.Range(0, 100).Select(number => "FQ15-" + number.ToString("X12", CultureInfo.InvariantCulture))
                .First(id => !allIds.Contains(id));
            List(retained.Append(unknown).ToArray());
            Check("D90 a listed ID absent from the ledger fails", FeatureLedger.Validate(inventory, deferred, true, registry)
                .Contains("D90 list entry is not OPEN in the ledger: " + unknown));
            List(retained.Append(first).ToArray());
            Check("D90 duplicate listed IDs fail", FeatureLedger.Validate(inventory, deferred, true, registry)
                .Contains("Duplicate D90 deferred ID: " + first));
            File.Delete(listPath);
            Check("D90 a missing list never authorizes deferral", FeatureLedger.Validate(inventory, deferred, true, registry)
                .Contains("Release has an open row: " + first));
            List(retained.Skip(1).ToArray());
            Check("D90 VERIFIED rows still require current executed receipts",
                FeatureLedger.Validate(inventory, Change(first, 7, "VERIFIED"), true, registry)
                    .Contains("Verified row lacks a current executed receipt: " + first));
            List(retained);
            File.WriteAllText(Path.Combine(folder, "docs/FEATURE-LEDGER.md"), deferred);
            var previousOutput = Console.Out;
            using var capture = new StringWriter(CultureInfo.InvariantCulture);
            int beforePassed = passed, beforeFailed = failed, beforeSkipped = skipped;
            bool valid = false;
            try
            {
                Console.SetOut(capture);
                valid = FeatureLedgerBaseTests(true) && failed == beforeFailed;
            }
            finally
            {
                Console.SetOut(previousOutput);
                passed = beforePassed; failed = beforeFailed; skipped = beforeSkipped;
            }
            string[] summaries = capture.ToString().Split('\n').Select(line => line.TrimEnd('\r'))
                .Where(line => line.StartsWith("Feature ledger release:", StringComparison.Ordinal)).ToArray();
            Check("D90 the release entry prints one honest verified/deferred/list/D90 summary", valid && summaries.SequenceEqual(new[]
                { "Feature ledger release: VERIFIED=0; DEFERRED=" + retained.Length.ToString(CultureInfo.InvariantCulture) + "; LIST=" + listFile + "; D90." }),
                capture.ToString());
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Environment.SetEnvironmentVariable(AppVersionInfo.ClickOnceCurrentVersionVariable, previousVersion);
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(folder)), temporaryRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ledger fixture cleanup escaped its temporary root.");
            Directory.Delete(folder, true);
        }
    }
}
