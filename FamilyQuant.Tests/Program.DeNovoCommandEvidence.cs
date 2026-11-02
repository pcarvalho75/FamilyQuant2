using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DeNovoCommandEvidence(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Supply frozen source, frozen app, trusted package, fresh output and date timestamp.");
        if (!NativeProofDesktop.IsPrivate) return IsolatedNativeProof(new[] { "--isolated-native-proof" }.Concat(args).ToArray());
        string root = FindLedgerRoot()!, frozenRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Use a fresh output directory.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var cohort = inventory.Observations.Where(row => !FeatureLedger.IsApprovedDrop(row) && row.Source.StartsWith("FamilyQuant/DeNovoControl.xaml", StringComparison.Ordinal)).ToArray();
        foreach (var file in inventory.Files.Where(file => cohort.Any(row => row.Source == file.Path)))
            if (FeatureLedgerEvidence.Hash(Path.Combine(frozenRoot, file.Path)) != file.Sha256) throw new InvalidDataException("Frozen source hash differs: " + file.Path);
        string oldSource = File.ReadAllText(Path.Combine(frozenRoot, "FamilyQuant/DeNovoControl.xaml.cs"));
        var statements = Regex.Matches(oldSource, @"(?m)^\s*SetStatus\([\s\S]*?\);").Cast<Match>()
            .Select(match => new { First = 1 + oldSource.AsSpan(0, match.Index).Count('\n'), Last = 1 + oldSource.AsSpan(0, match.Index + match.Length).Count('\n') }).ToArray();
        var oldConsole = Console.Out; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        var rows = new List<FeatureLedgerEvidence.Row>();
        var sources = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "DeNovo*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*DeNovo*.xaml"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core"), "DeNovo*.cs"))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/DeNovo"), "*.cs", SearchOption.AllDirectories))
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core/Homology"), "*.cs"))
            .Concat(new[] { Path.Combine(root, "FamilyQuant/ShellDialogs.cs"), Path.Combine(root, "FamilyQuant/AppOperationFiles.cs") })
            .Where(File.Exists).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/')).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string log = Path.Combine(output, "execution.log"); const string runId = "EV-actual-commands";
        try
        {
            Console.SetOut(new LedgerProofWriter(oldConsole, transcript));
            int result = DeNovoCommandProbe(["--denovo-command-probe", args[2], args[3], args[4]]);
            if (result != 0) throw new InvalidDataException("Actual command checks did not pass; no receipt emitted.");
            using var probe = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "command-probe.json")));
            using var facts = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "changed-copy-facts-41.json")));
            var snapshots = probe.RootElement.GetProperty("snapshots").EnumerateArray().ToArray();
            var oldTrace = probe.RootElement.GetProperty("oldTrace").EnumerateArray().ToArray();
            var newTrace = probe.RootElement.GetProperty("newTrace").EnumerateArray().ToArray();
            var choices = probe.RootElement.GetProperty("chooserCalls").EnumerateArray().ToArray();
            string[] commands = ["ButtonLoadAndFilter", "ButtonReconcile", "ButtonSearchHomology", "ButtonBrowsePeaksDirectory", "ButtonBrowseSeproPackage", "ButtonBrowseHomologyFcf",
                "ButtonExportSummary", "ButtonExportReviewQueue", "ButtonExportOverlapStudy"];
            var factById = facts.RootElement.EnumerateArray().ToDictionary(fact => fact.GetProperty("id").GetString()!, StringComparer.Ordinal);
            var sourceHashes = sources.ToDictionary(path => path, path => FeatureLedgerEvidence.Hash(Path.Combine(root, path)), StringComparer.Ordinal);
            foreach (var row in cohort)
            {
                string? assertion = null, compared = null, target = null; string mapping = "FamilyQuant/DeNovoControl.xaml.cs", symbol = "FamilyQuant.DeNovoControl";
                if (factById.TryGetValue(row.Id, out var fact))
                {
                    if (fact.GetProperty("state").GetString() != "fact present") continue;
                    assertion = "Changed-copy user fact " + row.Id + ": " + fact.GetProperty("fact").GetString();
                    compared = "Frozen " + row.Detail + " -> current user fact: " + fact.GetProperty("fact").GetString() + "; observed current status: " + fact.GetProperty("currentStatus").GetString();
                    target = nameof(DeNovoChangedCopyFacts); mapping = "FamilyQuant/DeNovoSession.cs";
                    symbol = "FamilyQuant.DeNovoSession." + (row.Line <= 541 ? "SourceChanged" : row.Line <= 767 ? "ApplyImport" : "ApplyHomology");
                }
                else if (row.Kind is "handler" or "handler definition" or "control")
                {
                    string? command = commands.SingleOrDefault(command => row.Detail.StartsWith(command + ".Click = ", StringComparison.Ordinal)
                        || row.Detail.Contains(" " + command + "_Click(", StringComparison.Ordinal)
                        || row.Detail == "Button " + command);
                    if (command == null) continue;
                    bool Valid(string side) => snapshots.Any(snapshot => snapshot.GetProperty("side").GetString() == side && snapshot.GetProperty("command").GetString() == command)
                        || snapshots.Any(snapshot => snapshot.GetProperty("side").GetString() == side + "-chooser" && snapshot.GetProperty("command").GetString() == command)
                        || snapshots.Any(snapshot => snapshot.GetProperty("side").GetString() == side + "-export" && snapshot.GetProperty("command").GetString() == command);
                    bool accepted = !command.StartsWith("ButtonBrowse", StringComparison.Ordinal) || choices.Where(choice => choice.TryGetProperty("accepted", out var accepted) && accepted.GetBoolean())
                        .Count(choice => choice.GetProperty("command").GetString() == command) == 2;
                    bool written = !command.StartsWith("ButtonExport", StringComparison.Ordinal) || choices.Where(choice => choice.TryGetProperty("written", out var written) && written.GetBoolean())
                        .Count(choice => choice.GetProperty("command").GetString() == command) == 2;
                    bool passedRoute = Valid("frozen") && Valid("current") && accepted && written;
                    assertion = "Actual command observation " + row.Id + ": frozen/current " + command + " success route";
                    Check(assertion, passedRoute);
                    if (!passedRoute) continue;
                    compared = row.Detail + " -> actual routed button success in both versions, enabled before invocation, with recorded data/status; native chooser acceptance/export file required when applicable.";
                    target = nameof(DeNovoCommandEvidence); mapping = row.Source.EndsWith(".xaml", StringComparison.Ordinal) ? "FamilyQuant/DeNovoControl.xaml" : "FamilyQuant/DeNovoControl.xaml.cs";
                    symbol = "FamilyQuant.DeNovoControl." + command + (row.Kind == "control" ? "" : "_Click");
                }
                else if (row.Kind == "code fact")
                {
                    var enclosing = statements.Where(statement => row.Line >= statement.First && row.Line <= statement.Last).ToArray();
                    if (enclosing.Length != 1) continue;
                    var observed = oldTrace.Where(trace => trace.GetProperty("frozenLine").GetInt32() >= enclosing[0].First && trace.GetProperty("frozenLine").GetInt32() <= enclosing[0].Last).ToArray();
                    if (observed.Length == 0) continue;
                    string? literal;
                    try { literal = JsonSerializer.Deserialize<string>(row.Detail); }
                    catch (JsonException) { continue; }
                    if (string.IsNullOrEmpty(literal)) continue;
                    observed = observed.Where(trace => trace.GetProperty("text").GetString()!.Contains(literal, StringComparison.Ordinal)).ToArray();
                    if (observed.Length == 0) continue;
                    string CanonicalMessage(string value)
                    {
                        foreach (string file in new[] { "summary.csv", "review.csv", "overlap.csv" })
                        { value = value.Replace(Path.Combine(output, "frozen-" + file), "{chosen-" + file + "}", StringComparison.Ordinal)
                            .Replace(Path.Combine(output, "current-" + file), "{chosen-" + file + "}", StringComparison.Ordinal); }
                        return value;
                    }
                    var matchedMessages = observed.Select(trace => trace.GetProperty("text").GetString()!).ToArray();
                    bool agrees = matchedMessages.All(message => newTrace.Count(trace => CanonicalMessage(trace.GetProperty("text").GetString()!) == CanonicalMessage(message))
                        == oldTrace.Count(trace => CanonicalMessage(trace.GetProperty("text").GetString()!) == CanonicalMessage(message)));
                    if (!agrees) continue;
                    assertion = "Executed status observation " + row.Id + ": complete frozen/current messages agree at the recorded frozen call site";
                    Check(assertion, true); target = nameof(DeNovoCommandEvidence); mapping = "FamilyQuant/DeNovoViewModel.cs";
                    string owner = LedgerMemberFactComparison.Compare(row, oldSource, new Dictionary<string, string>()).FrozenMember;
                    if (owner.Contains("ButtonLoadAndFilter_Click", StringComparison.Ordinal)) symbol = "FamilyQuant.DeNovoViewModel.ImportAsync";
                    else if (owner.Contains("ButtonReconcile_Click", StringComparison.Ordinal)) symbol = "FamilyQuant.DeNovoViewModel.ReconcileAsync";
                    else if (owner.Contains("ButtonSearchHomology_Click", StringComparison.Ordinal)) symbol = "FamilyQuant.DeNovoViewModel.SearchAsync";
                    else { mapping = "FamilyQuant/DeNovoControl.Dialogs.cs"; symbol = "FamilyQuant.DeNovoControl dialog/export status producer; frozen member " + owner; }
                    compared = row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail + " -> actual PDB call-site range "
                        + enclosing[0].First.ToString(CultureInfo.InvariantCulture) + ".." + enclosing[0].Last.ToString(CultureInfo.InvariantCulture)
                        + "; complete matching messages=" + JsonSerializer.Serialize(matchedMessages) + "; chosen output filenames are compared as recorded fixture arguments.";
                }
                if (assertion == null || compared == null || target == null) continue;
                rows.Add(new(row.Id, sources.Select(path => new FeatureLedgerEvidence.Implementation(path, path == mapping ? symbol : "Actual command dependency", sourceHashes[path])).ToList(),
                    new() { new(runId, "test", target, assertion, log, "") }, "Actual command success/cancel/failure fixture; no fresh RAW or biological accuracy claim.", null, compared));
            }
            JsonNode? Canonical(JsonNode? node)
            {
                if (node is JsonObject obj)
                {
                    obj.Remove("ElapsedSeconds");
                    foreach (string key in obj.Select(pair => pair.Key).ToArray()) obj[key] = Canonical(obj[key]?.DeepClone());
                }
                else if (node is JsonArray array)
                    for (int index = 0; index < array.Count; index++) array[index] = Canonical(array[index]?.DeepClone());
                else if (node is JsonValue value && value.TryGetValue<string>(out string? text))
                    return JsonValue.Create(text.Replace(output, "{fixture}", StringComparison.Ordinal)
                        .Replace(JsonSerializer.Serialize(output)[1..^1], "{fixture}", StringComparison.Ordinal));
                return node;
            }
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "canonical-command-data.json"), new
            {
                snapshots = Canonical(JsonNode.Parse(probe.RootElement.GetProperty("snapshots").GetRawText())),
                facts = Canonical(JsonNode.Parse(facts.RootElement.GetRawText())),
                observations = rows.Select(row => new { row.ObservationId, compared = Canonical(JsonValue.Create(row.Compared)) }).ToArray(),
                exportedFiles = Directory.GetFiles(output, "*.csv").OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                    .Select(path => new { name = Path.GetFileName(path), sha256 = FeatureLedgerEvidence.Hash(path) }).ToArray(),
            });
            Console.WriteLine("Actual command supported observations: " + rows.Count.ToString(CultureInfo.InvariantCulture));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(oldConsole); }
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        var artifacts = Directory.GetFiles(output, "*.json").Concat(Directory.GetFiles(output, "*.csv")).Append(log).ToList();
        foreach (string relative in sources.Concat(new[] { "FamilyQuant.Tests/Program.DeNovoCommandProbe.cs", "FamilyQuant.Tests/Program.DeNovoCommandEvidence.cs",
            "FamilyQuant.Tests/Program.DeNovoChangedCopyFacts.cs", "FamilyQuant.Tests/NativeOwnedDialogDriver.cs", "FamilyQuant.Tests/NativeProofDesktop.cs", "FamilyQuant.Tests/Program.Commands.DeNovoLedger.cs" }))
        {
            string copy = Path.Combine(archive, relative.Replace('/', '_')); File.Copy(Path.Combine(root, relative), copy); artifacts.Add(copy);
        }
        foreach (string binary in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(ProjectStore).Assembly.Location, Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll") })
        {
            string copy = Path.Combine(archive, Path.GetFileName(binary)); File.Copy(binary, copy); artifacts.Add(copy);
        }
        foreach (string relative in new[] { "FamilyQuant/DeNovoControl.xaml", "FamilyQuant/DeNovoControl.xaml.cs" })
        {
            string copy = Path.Combine(archive, "frozen_" + relative.Replace('/', '_')); File.Copy(Path.Combine(frozenRoot, relative), copy); artifacts.Add(copy);
        }
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD"); using var git = Process.Start(start)!;
        string commit = git.StandardOutput.ReadToEnd().Trim(); git.WaitForExit(); if (git.ExitCode != 0) throw new IOException("Cannot record execution commit.");
        var run = new FeatureLedgerEvidence.Run(runId, Environment.CommandLine, args[5], commit,
            artifacts.Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList(), root);
        int batch = 0;
        foreach (var group in rows.Chunk(200))
        {
            string step = "EV-denovo-commands-" + (++batch).ToString(CultureInfo.InvariantCulture);
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, group.ToList()));
        }
        return failed == 0 ? 0 : 1;
    }
}
