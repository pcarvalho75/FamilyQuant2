using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryLedgerRegister(string root, string frozenRoot, string audit, string output, string timestamp,
        string step, string target, List<SecondaryLedgerResult> results, Dictionary<string, string> assertions, string log,
        string data, IEnumerable<string> sources, IEnumerable<string> checkers, bool runtime = false)
    {
        var artifacts = new List<FeatureLedgerEvidence.Artifact> { new(log, FeatureLedgerEvidence.Hash(log)), new(data, FeatureLedgerEvidence.Hash(data)) };
        void Keep(string from, string relative)
        {
            string path = Path.Combine(output, "executed", relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(from, path, false); artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        }
        foreach (string path in sources.Concat(checkers).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)) Keep(Path.Combine(root, path), path);
        foreach (string path in results.Select(result => result.Source).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal))
            Keep(Path.Combine(frozenRoot, path), "frozen/" + path);
        foreach (string file in new[] { "report.json", "mapping.json" }) Keep(Path.Combine(audit, file), "master-audit/" + file);
        Keep(typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll");
        Keep(typeof(App).Assembly.Location, "FamilyQuant.dll");
        Keep(typeof(FullXicComputer).Assembly.Location, "FamilyQuant.Core.dll");
        foreach (string path in Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => path != log && path != data && !Path.GetRelativePath(output, path).StartsWith("executed" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)) artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        string Receipt(string kind) => "secondary-" + kind.Replace('/', '-').Replace(' ', '-');
        var rows = results.Select(result => new FeatureLedgerEvidence.Row(result.Id,
            result.Matched ? (runtime ? sources : new[] { result.CurrentPath }).Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new FeatureLedgerEvidence.Implementation(path, result.Symbol, FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList() : new(),
            new(), result.Matched ? runtime ? "Executed native control/value/action check; exact scope is recorded in the comparison." : "Declaration only; no handler execution or reachability claim." : result.Reason,
            result.Matched ? Receipt(result.Kind) : null,
            result.Matched ? result.Source + ":" + result.Line.ToString(CultureInfo.InvariantCulture) + " " + result.Compared : null)).ToList();
        var kinds = assertions.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new FeatureLedgerEvidence.KindReceipt(
            Receipt(pair.Key), pair.Key, new() { new("secondary", "test", target, pair.Value, log, "") })).ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD");
        using var process = Process.Start(start)!; string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit();
        if (process.ExitCode != 0) throw new IOException("Could not record source commit.");
        var run = new FeatureLedgerEvidence.Run("secondary", Environment.CommandLine, timestamp, commit, artifacts, root);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"),
            new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit, new() { run }, rows, kinds));
    }
}
