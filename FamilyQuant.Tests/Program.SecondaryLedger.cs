using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly string[] SecondaryLedgerFrozenSources =
    {
        "FamilyQuant/ComputeXicWindow.xaml", "FamilyQuant/ComputeXicWindow.xaml.cs", "FamilyQuant/FullXicComputer.cs",
        "FamilyQuant/SuperClusterPreviewWindow.cs", "FamilyQuant/LockManagerWindow.cs", "FamilyQuant/IdentityScanWindow.cs",
        "FamilyQuant/IdentityScanChart.cs", "FamilyQuant/GridCopy.cs", "FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs",
    };
    private sealed record SecondaryLedgerAuditRow(string Id, string Kind, string Source, int Line, string Detail, string State);
    private sealed record SecondaryLedgerResult(string Id, string Kind, string Source, int Line, bool Matched,
        string Before, string Actual, string CurrentPath, string Symbol, string Reason, string Compared);

    private static FeatureLedger.Observation[] SecondaryOpenCohort(string root, string audit,
        FeatureLedger.Inventory inventory, string? ownStep = null)
    {
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(audit, "report.json")));
        if (report.RootElement.GetProperty("inventorySha256").GetString()
            != FeatureLedger.Fingerprint(inventory))
            throw new InvalidDataException("The native master audit uses another frozen inventory.");
        var rows = JsonSerializer.Deserialize<List<SecondaryLedgerAuditRow>>(File.ReadAllText(Path.Combine(audit, "mapping.json")),
            FeatureLedgerEvidence.JsonOptions)!;
        var selected = rows.Where(row => row.State == "OPEN" && SecondaryLedgerFrozenSources.Contains(row.Source, StringComparer.Ordinal)
            && row.Source != "FamilyQuant/FullXicComputer.cs" && row.Kind is not ("tooltip" or "help fact"))
            .ToDictionary(row => row.Id, StringComparer.Ordinal);
        foreach (var pair in selected)
        {
            var frozen = inventory.Observations.Single(row => row.Id == pair.Key);
            var captured = pair.Value;
            if (frozen.Source != captured.Source || frozen.Kind != captured.Kind || frozen.Detail != captured.Detail || frozen.Line != captured.Line)
                throw new InvalidDataException("A native audit observation differs from the frozen inventory: " + pair.Key);
        }
        var registry = FeatureLedgerEvidence.Load(root, inventory);
        var already = registry.Entries.Values.Where(entry => entry.Valid && entry.Step != ownStep)
            .Select(entry => entry.Row.ObservationId).ToHashSet(StringComparer.Ordinal);
        return inventory.Observations.Where(row => selected.ContainsKey(row.Id) && !FeatureLedger.IsApprovedDrop(row)
            && !already.Contains(row.Id)).OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
    }

    private static string[] SecondaryCurrentSources(string source) => source switch
    {
        "FamilyQuant/ComputeXicWindow.xaml" => new[] { source, "FamilyQuant/QuantifyControl.xaml", "FamilyQuant/DeNovoFilterControl.xaml" },
        "FamilyQuant/ComputeXicWindow.xaml.cs" => new[] { source, "FamilyQuant/ComputeXicViewModel.cs", "FamilyQuant/QuantifyControl.xaml.cs", "FamilyQuant/DeNovoFilterViewModel.cs" },
        "FamilyQuant/SuperClusterPreviewWindow.cs" => new[] { source, "FamilyQuant/GroupingReviewViewModel.cs", "FamilyQuant/KeywordReviewViewModel.cs" },
        "FamilyQuant/LockManagerWindow.cs" => new[] { source, "FamilyQuant/LockManagerWindow.View.cs", "FamilyQuant/SecondaryWindows.ViewModels.cs" },
        "FamilyQuant/IdentityScanWindow.cs" => new[] { source, "FamilyQuant/SecondaryWindows.ViewModels.cs" },
        "FamilyQuant/DeNovo/Ms1EnvelopeDetector.cs" => new[] { "FamilyQuant.Core/DeNovo/Ms1EnvelopeDetector.cs" },
        _ => new[] { source },
    };

    private static int SecondaryLedgerCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--negative-control")
            throw new ArgumentException("Supply frozen source root, native master-audit folder, fresh output, date and optional --negative-control.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), audit = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        const string step = "FL-secondary-declarations";
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Secondary output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = SecondaryOpenCohort(root, audit, inventory, step);
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in cohort.Select(row => row.Source).Distinct(StringComparer.Ordinal))
        {
            string path = Path.Combine(frozenRoot, source);
            if (FeatureLedgerEvidence.Hash(path) != inventory.Files.Single(file => file.Path == source).Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source);
            frozen[source] = File.ReadAllText(path);
        }
        var current = cohort.SelectMany(row => SecondaryCurrentSources(row.Source)).Distinct(StringComparer.Ordinal)
            .Where(path => File.Exists(Path.Combine(root, path))).OrderBy(path => path, StringComparer.Ordinal)
            .ToDictionary(path => path, path => File.ReadAllText(Path.Combine(root, path)), StringComparer.Ordinal);
        SecondaryLedgerResult Compare(FeatureLedger.Observation row)
        {
            var sources = SecondaryCurrentSources(row.Source).Where(current.ContainsKey)
                .ToDictionary(path => path, path => current[path], StringComparer.Ordinal);
            if (row.Source.EndsWith(".xaml", StringComparison.Ordinal))
            {
                var match = LedgerDeclarationComparison.Compare(row, XDocument.Parse(frozen[row.Source]),
                    sources.ToDictionary(pair => pair.Key, pair => XDocument.Parse(pair.Value), StringComparer.Ordinal));
                return new(row.Id, row.Kind, row.Source, row.Line, match.Matched, match.Before, match.Actual,
                    match.CurrentPath, match.Symbol, match.Reason, match.Before + " -> " + match.CurrentPath + ": " + match.Symbol + " = " + match.Actual);
            }
            var member = LedgerMemberFactComparison.Compare(row, frozen[row.Source], sources);
            if (!member.Matched && ShellPartialRelocation(row.Source, member, frozen[row.Source], sources) is { } mapping)
                member = LedgerMemberFactComparison.Compare(row, frozen[row.Source], sources, mapping);
            return new(row.Id, row.Kind, row.Source, row.Line, member.Matched, member.Before, member.Actual,
                member.CurrentPath, member.Symbol, member.Reason, member.FrozenMember + " -> " + member.CurrentPath + ": " + member.Symbol
                    + "; complete member SHA256=" + member.FrozenMemberSha256 + "/" + member.CurrentMemberSha256
                    + "; literal occurrences=" + member.FrozenOccurrences.ToString(CultureInfo.InvariantCulture) + "/"
                    + member.CurrentOccurrences.ToString(CultureInfo.InvariantCulture));
        }
        var baseline = cohort.Select(Compare).ToArray();
        string? negativeId = null;
        if (args.Length == 6)
        {
            var candidate = baseline.FirstOrDefault(result => result.Matched)
                ?? throw new InvalidDataException("No exact declaration available for the negative control.");
            negativeId = candidate.Id;
            if (candidate.CurrentPath.EndsWith(".xaml", StringComparison.Ordinal))
            {
                var row = cohort.Single(row => row.Id == candidate.Id);
                string left = row.Detail[..row.Detail.IndexOf(" = ", StringComparison.Ordinal)];
                int dot = left.LastIndexOf('.'); string name = left[..dot], property = left[(dot + 1)..];
                var document = XDocument.Parse(current[candidate.CurrentPath]);
                var element = document.Root!.DescendantsAndSelf().Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == name));
                element.Attributes().Single(attribute => attribute.Name.LocalName == property).Value = "changed-secondary-negative-control";
                current[candidate.CurrentPath] = document.ToString();
            }
            else
            {
                string text = current[candidate.CurrentPath];
                if (!text.Contains(candidate.Before, StringComparison.Ordinal)) throw new InvalidDataException("Negative-control token was absent.");
                current[candidate.CurrentPath] = text.Replace(candidate.Before, "\"changed-secondary-negative-control\"", StringComparison.Ordinal);
            }
        }
        var results = new List<SecondaryLedgerResult>();
        var assertions = new Dictionary<string, string>(StringComparer.Ordinal);
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            foreach (var row in cohort)
            {
                var result = Compare(row); results.Add(result);
                if (result.Matched || row.Id == negativeId) Check("secondary exact declaration " + row.Id + ": " + result.Symbol, result.Matched);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var group in results.Where(result => result.Matched).GroupBy(result => result.Kind).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                string assertion = "secondary declaration kind " + group.Key + ": all listed explicit properties or complete member tokens agree";
                Check(assertion, group.All(result => result.Before == result.Actual)); assertions[group.Key] = assertion;
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, results);
        SecondaryLedgerRegister(root, frozenRoot, audit, output, args[4], step, nameof(SecondaryLedgerCommand), results, assertions, log, data,
            current.Keys, new[] { "FamilyQuant.Tests/Program.SecondaryLedger.cs", "FamilyQuant.Tests/Program.Commands.SecondaryLedger.cs",
                "FamilyQuant.Tests/Program.ShellLedgerComparison.cs", "FamilyQuant.Tests/LedgerDeclarationComparison.cs", "FamilyQuant.Tests/LedgerMemberFactComparison.cs" });
        Console.WriteLine("Secondary OPEN cohort " + cohort.Length.ToString(CultureInfo.InvariantCulture) + ": "
            + results.Count(result => result.Matched).ToString(CultureInfo.InvariantCulture) + " declaration matches; "
            + results.Count(result => !result.Matched).ToString(CultureInfo.InvariantCulture) + " remain unproved.");
        return failed == 0 ? 0 : 1;
    }
}
