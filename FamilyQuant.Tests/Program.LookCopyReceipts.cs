using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record CopyAuditRow(string Id, string Kind, string Source, int Line, string Detail, string State);
    private sealed record PublishedHelp(string Method, string Context, string Text, string Artifact);
    private sealed record CopyReceiptResult(string Id, string Kind, bool Matched, string Mode, string Frozen,
        string Actual, string CurrentPath, string Symbol, string Context, string Reason, string FrozenMember = "");

    private static int LookCopyReceiptCommand(string[] args, string? fixedRegister = null)
    {
        if (args.Length is not (5 or 6)) throw new ArgumentException("Supply frozen source root, native audit folder, fresh output, a timestamp from date and optional LK register prefix.");
        string prefix = args.Length == 6 ? args[5] : "LK-copy";
        if (!Regex.IsMatch(prefix, "^LK-[A-Za-z0-9-]+$", RegexOptions.CultureInvariant)) throw new ArgumentException("Use an LK-owned register prefix.");
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), audit = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        FeatureLedger.Observation[] cohort;
        if (fixedRegister != null)
        {
            var register = JsonSerializer.Deserialize<FeatureLedgerEvidence.Register>(File.ReadAllText(fixedRegister), FeatureLedgerEvidence.JsonOptions)!;
            cohort = FixedLookCopyCohort(register, inventory);
        }
        else
        {
        using var auditSummary = JsonDocument.Parse(File.ReadAllText(Path.Combine(audit, "report.json")));
        if (auditSummary.RootElement.GetProperty("frozenCommit").GetString() != FeatureLedger.ReferenceCommit
            || auditSummary.RootElement.GetProperty("inventorySha256").GetString() != FeatureLedger.ReferenceInventorySha256)
            throw new InvalidDataException("Audit identifies another frozen inventory.");
        var auditRows = JsonSerializer.Deserialize<List<CopyAuditRow>>(File.ReadAllText(Path.Combine(audit, "mapping.json")), FeatureLedgerEvidence.JsonOptions)!;
        var open = auditRows.Where(r => r.State == "OPEN" && r.Kind is "help fact" or "tooltip").ToDictionary(r => r.Id, StringComparer.Ordinal);
        var existing = FeatureLedgerEvidence.Load(root, inventory);
        if (existing.FatalProblems.Count > 0) throw new InvalidDataException(string.Join("; ", existing.FatalProblems));
        var proved = existing.Entries.Values.Where(e => e.Valid && !existing.IsKindBlocked(e.Row.ObservationId))
            .Select(e => e.Row.ObservationId).ToHashSet(StringComparer.Ordinal);
        cohort = inventory.Observations.Where(r => open.ContainsKey(r.Id) && !proved.Contains(r.Id)
            && !FeatureLedger.IsApprovedDrop(r)).OrderBy(r => r.Id, StringComparer.Ordinal).ToArray();
        foreach (var row in cohort)
        {
            var entry = open[row.Id];
            if ((entry.Kind, entry.Source, entry.Line, entry.Detail) != (row.Kind, row.Source, row.Line, row.Detail))
                throw new InvalidDataException("Audit row differs from frozen inventory: " + row.Id);
        }
        }
        var frozen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string source in cohort.Select(r => r.Source).Distinct(StringComparer.Ordinal))
        {
            string path = Path.Combine(frozenRoot, source);
            if (!File.Exists(path) || FeatureLedgerEvidence.Hash(path) != inventory.Files.Single(f => f.Path == source).Sha256)
                throw new InvalidDataException("Frozen source hash differs: " + source);
            frozen[source] = File.ReadAllText(path);
        }
        var modulePaths = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (string source in cohort.Select(r => r.Source).Distinct(StringComparer.Ordinal))
            modulePaths[source] = source switch
            {
                "FamilyQuant/HelpContent.cs" => new[] { "FamilyQuant.Core/HelpContent.cs" },
                "FamilyQuant/ClusterGenerationControl.xaml" => new[] { "FamilyQuant/ClusterGenerationControl.xaml", "FamilyQuant/CurationQuantitationPanel.xaml" },
                "FamilyQuant/ComputeXicWindow.xaml" => new[] { "FamilyQuant/QuantifyControl.xaml", "FamilyQuant/DeNovoFilterControl.xaml" },
                _ => new[] { FeatureLedgerEvidence.CurrentPath(root, source) }
            };
        var allCurrent = modulePaths.Values.SelectMany(p => p).Where(p => p.Length > 0 && File.Exists(Path.Combine(root, p)))
            .Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var sources = allCurrent.ToDictionary(p => p, p => File.ReadAllText(Path.Combine(root, p)), StringComparer.Ordinal);
        var documents = allCurrent.Where(p => p.EndsWith(".xaml", StringComparison.Ordinal))
            .ToDictionary(p => p, p => XDocument.Parse(sources[p]), StringComparer.Ordinal);
        var results = new List<CopyReceiptResult>();
        var artifacts = new List<string>();
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            LookCopyComparisonTests(); LedgerDeclarationComparisonTests(); LedgerMemberFactComparisonTests();
            EnsureApplication();
            var pages = PublishCopyHelp(output, artifacts);
            foreach (var row in cohort)
            {
                CopyReceiptResult result;
                if (row.Kind == "help fact")
                {
                    var current = modulePaths[row.Source].Where(sources.ContainsKey).ToDictionary(p => p, p => sources[p], StringComparer.Ordinal);
                    var binding = LedgerMemberFactComparison.Compare(row, frozen[row.Source], current);
                    string method = binding.FrozenMember.StartsWith("FamilyQuant.HelpContent::", StringComparison.Ordinal)
                        ? binding.FrozenMember.Contains(" BuildDeNovo (", StringComparison.Ordinal) ? nameof(HelpContent.BuildDeNovo)
                        : binding.FrozenMember.Contains(" Build (", StringComparison.Ordinal) ? nameof(HelpContent.Build) : "" : "";
                    var published = LedgerPublishedCopyComparison.Compare(row, binding,
                        pages.Where(p => p.Method == method).Select(p => new LedgerPublishedCopyComparison.Publication(binding.FrozenMember,
                            "FamilyQuant.Core/HelpContent.cs", "HelpContent." + method + " -> TextReportWindow report.Text", p.Context, p.Text)).ToArray());
                    result = published.Matched
                        ? new(row.Id, row.Kind, true, "published-help", row.Detail, published.Actual, published.CurrentPath, published.Symbol, published.Context, published.Reason, binding.FrozenMember)
                        : new(row.Id, row.Kind, binding.Matched, "member-declaration", row.Detail, binding.Actual, binding.CurrentPath, binding.Symbol, "", binding.Matched ? binding.Reason : published.Reason + " " + binding.Reason, binding.FrozenMember);
                }
                else
                {
                    var current = modulePaths[row.Source].Where(documents.ContainsKey).ToDictionary(p => p, p => documents[p], StringComparer.Ordinal);
                    var declaration = LedgerDeclarationComparison.Compare(row, XDocument.Parse(frozen[row.Source]), current);
                    result = new(row.Id, row.Kind, declaration.Matched, "tooltip-declaration", row.Detail, declaration.Actual,
                        declaration.CurrentPath, declaration.Symbol, "", declaration.Reason);
                }
                results.Add(result);
                if (result.Matched) Check("LK copy " + row.Id + ": " + result.Mode, true);
                else { skipped++; Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
            }
            foreach (var group in results.Where(r => r.Matched).GroupBy(r => (r.Kind, r.Mode)).OrderBy(g => g.Key.Kind, StringComparer.Ordinal).ThenBy(g => g.Key.Mode, StringComparer.Ordinal))
                Check(CopyKindAssertion(group.Key.Kind, group.Key.Mode), group.All(r => r.Matched));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), data = Path.Combine(output, "comparisons.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, results);
        artifacts.AddRange(new[] { log, data });
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        foreach (string path in allCurrent.Concat(new[] { "FamilyQuant/TextReportWindow.cs", "FamilyQuant/SecondaryWindowView.cs",
            "FamilyQuant/ClusterGenerationControl.xaml.cs", "FamilyQuant/DeNovoControl.xaml.cs",
            "FamilyQuant.Tests/Program.LookCopyReceipts.cs", "FamilyQuant.Tests/Program.LookCopyPreservation.cs", "FamilyQuant.Tests/LedgerPublishedCopyComparison.cs",
            "FamilyQuant.Tests/LedgerMemberFactComparison.cs", "FamilyQuant.Tests/LedgerDeclarationComparison.cs", "FamilyQuant.Tests/Program.Commands.LookLedger.cs" }).Distinct(StringComparer.Ordinal))
        {
            string target = Path.Combine(archive, path.Replace('/', '_')); File.Copy(Path.Combine(root, path), target); artifacts.Add(target);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(HelpContent).Assembly.Location }
            .Concat(fixedRegister == null ? new[] { Path.Combine(audit, "mapping.json"), Path.Combine(audit, "report.json") } : new[] { fixedRegister }))
        {
            string target = Path.Combine(archive, Path.GetFileName(path)); File.Copy(path, target); artifacts.Add(target);
        }
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD")
        { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record executing commit.");
        const string runId = "LK-copy";
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[4], commit,
            artifacts.Select(p => new FeatureLedgerEvidence.Artifact(p, FeatureLedgerEvidence.Hash(p))).ToList(), root);
        int batch = 0;
        foreach (var chunk in fixedRegister == null ? results.Chunk(200) : new[] { results.ToArray() })
        {
            string step = fixedRegister == null ? prefix + "-" + (++batch).ToString(CultureInfo.InvariantCulture)
                : Path.GetFileNameWithoutExtension(fixedRegister);
            var receipts = chunk.Where(r => r.Matched).GroupBy(r => (r.Kind, r.Mode)).Select(g => new FeatureLedgerEvidence.KindReceipt(
                CopyReceiptId(g.Key.Kind, g.Key.Mode), g.Key.Kind, new() { new(runId, "test", nameof(LookCopyReceiptCommand), CopyKindAssertion(g.Key.Kind, g.Key.Mode), log, "") })).ToList();
            var rows = chunk.Select(result =>
            {
                var mapping = new List<FeatureLedgerEvidence.Implementation>();
                if (result.CurrentPath.Length > 0) mapping.Add(new(result.CurrentPath, result.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, result.CurrentPath))));
                if (result.Mode == "published-help" && result.Matched)
                    foreach (string path in new[] { "FamilyQuant/TextReportWindow.cs", "FamilyQuant/SecondaryWindowView.cs", "FamilyQuant/ClusterGenerationControl.xaml.cs", "FamilyQuant/DeNovoControl.xaml.cs" })
                        mapping.Add(new(path, "Published Help text route", FeatureLedgerEvidence.Hash(Path.Combine(root, path))));
                return new FeatureLedgerEvidence.Row(result.Id, mapping, new(), result.Reason + " No handler, new scientific result or DPI claim.",
                    result.Matched ? CopyReceiptId(result.Kind, result.Mode) : null,
                    result.Matched ? result.FrozenMember + ": " + result.Frozen + " -> " + result.CurrentPath + ": " + result.Symbol + "; " + result.Context + "; actual " + result.Actual : null);
            }).ToList();
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows, receipts));
        }
        return failed == 0 ? 0 : 1;
    }

    private static string CopyReceiptId(string kind, string mode) => "copy-" + kind.Replace(' ', '-') + "-" + mode;
    private static string CopyKindAssertion(string kind, string mode) => "LK copy kind " + kind + " / " + mode + ": every listed comparison retains its scoped published fact or exact declaration";

    private static List<PublishedHelp> PublishCopyHelp(string output, List<string> artifacts)
    {
        var pages = new List<PublishedHelp>();
        void Publish(string method, string context, string text)
        {
            var window = TextReportWindow.Create("Preservation fixture: " + context, text, true);
            try
            {
                var root = (FrameworkElement)window.Content;
                LayoutAt(root, new Size(860, 640), -1);
                var report = WalkTree(root).OfType<TextBox>().Single(box => box.IsReadOnly);
                Check("native Help text control publishes " + context, report.Text == text && report.IsReadOnly);
                string path = Path.Combine(output, "help-" + pages.Count.ToString("D2", CultureInfo.InvariantCulture) + ".txt");
                File.WriteAllText(path, report.Text, new UTF8Encoding(false)); artifacts.Add(path);
                pages.Add(new(method, context, report.Text, path));
            }
            finally { window.Close(); }
        }
        var profiles = new List<(string Name, QuantitationProfile? Profile)> { ("no-PatternLab", null) };
        foreach (string tag in new[] { "NONE", "TIC", "ID" })
        {
            string path = WriteDenominatorFixture(output, "profile-" + tag + ".plp", tag);
            profiles.Add(("recorded-" + tag, QuantitationProfile.Describe(new PatternLabProject(path)))); artifacts.Add(path);
        }
        foreach (var profile in profiles)
        foreach (var mode in Enum.GetValues<NormalizationMode>())
        foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
            Publish(nameof(HelpContent.Build), profile.Name + "/" + mode + "/" + policy, HelpContent.Build(profile.Profile, mode, policy));
        foreach (var source in new[] { DeNovoSource.DenovoOnly, DeNovoSource.DeNovoAll, DeNovoSource.DeepNovo })
        {
            Publish(nameof(HelpContent.BuildDeNovo), source.Id + "/default", HelpContent.BuildDeNovo(null, source));
            Publish(nameof(HelpContent.BuildDeNovo), source.Id + "/custom", HelpContent.BuildDeNovo(new DeNovoFilterCriteria
                { MinLength = 11, MinAverageLocalConfidence = 55, StretchLength = 3, StretchFloor = 77 }, source));
        }
        return pages;
    }
}
