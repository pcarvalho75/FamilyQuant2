using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ShellMainReceipts(string[] args)
    {
        string root = FindLedgerRoot()!, output = args.Length >= 3 ? Path.GetFullPath(args[2]) : "";
        var original = Console.Out;
        using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        int result;
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            result = ShellMainMessageProbe(args);
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "comparisons.log");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false));
        if (result != 0) return result;
        var comparisons = JsonSerializer.Deserialize<List<ShellMainComparison>>(File.ReadAllText(Path.Combine(output, "message-comparisons.json")), FeatureLedgerEvidence.JsonOptions)!;
        string Normalize(string text) => text.Replace(output, "<fixture>", StringComparison.Ordinal)
            .Replace(output.Replace('\\', '/'), "<fixture>", StringComparison.Ordinal);
        var canonical = comparisons.Select(row => row with { Before = Normalize(row.Before), Actual = Normalize(row.Actual) }).ToArray();
        string values = Path.Combine(output, "comparisons.json");
        FeatureLedgerEvidence.WriteJson(values, canonical);
        var native = JsonSerializer.Deserialize<List<ShellRouteCase>>(File.ReadAllText(Path.Combine(output, "native/native-cases.json")), FeatureLedgerEvidence.JsonOptions)!;
        string nativeCanonical = Path.Combine(output, "native-cases-canonical.json");
        FeatureLedgerEvidence.WriteJson(nativeCanonical, native.Select(record => record with
        {
            Status = Normalize(record.Status),
            Dialogs = record.Dialogs.Select(dialog => dialog with { FileName = Normalize(dialog.FileName) }).ToList(),
            Messages = record.Messages.Select(message => message with { Text = Normalize(message.Text) }).ToList(),
            ExceptionMessage = record.ExceptionMessage == null ? null : Normalize(record.ExceptionMessage),
            Bindings = record.Bindings?.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value), StringComparer.Ordinal),
            ConsoleText = Normalize(record.ConsoleText)
        }));
        var artifacts = new List<FeatureLedgerEvidence.Artifact>();
        void Artifact(string path) => artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        foreach (string path in new[] { log, values, nativeCanonical, Path.Combine(output, "message-comparisons.json"),
            Path.Combine(output, "native/native-cases.json"), Path.Combine(output, "wording-for-paulo.tsv") }) Artifact(path);
        string executed = Path.Combine(output, "executed"); Directory.CreateDirectory(executed);
        foreach (string file in new[] { "LedgerMemberFactComparison.cs", "LedgerMemberFactComparison.Messages.cs", "Program.ShellRouteNative.cs",
            "Program.ShellMainMessages.cs", "Program.ShellMainReceipts.cs", "Program.ShellMessageFrames.cs", "Program.Commands.ShellRoutes.cs",
            "Program.ShellSeproFixture.cs", "Program.Commands.ShellSeproFixture.cs", "Program.ShellProofWindows.cs",
            "Program.Commands.ShellProofWindows.cs", "FamilyQuant.Tests.csproj" })
        {
            string target = Path.Combine(executed, file); File.Copy(Path.Combine(root, "FamilyQuant.Tests", file), target); Artifact(target);
        }
        foreach (string assembly in new[] { typeof(Program).Assembly.Location, typeof(MainWindow).Assembly.Location, typeof(FamilyClusterFile).Assembly.Location })
        {
            string target = Path.Combine(executed, Path.GetFileName(assembly)); File.Copy(assembly, target); Artifact(target);
        }
        foreach (string file in new[] { "SeproPckg2.dll", "PatternTools.dll" })
        {
            string target = Path.Combine(executed, file); File.Copy(Path.Combine(root, "FamilyQuant/DLLs", file), target); Artifact(target);
            Artifact(Path.Combine(root, "FamilyQuant/DLLs", file));
        }
        Artifact(Path.Combine(root, "FamilyQuant.Tests/FamilyQuant.Tests.csproj"));
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        var rowsById = inventory.Observations.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var implementationHashes = ShellMainImplementation(root).ToDictionary(path => path,
            path => FeatureLedgerEvidence.Hash(Path.Combine(root, path)), StringComparer.Ordinal);
        // Receipt assertions are the actual per-row PASS lines, not a broad suite label.
        var rows = comparisons.Select(row =>
        {
            string assertion = row.Source.EndsWith(".xaml", StringComparison.Ordinal) || row.Kind == "handler definition"
                ? "native shell route mapping " + row.Id + ": " + row.NativeCase
                : "native shell message " + row.Id + ": " + row.NativeCase + " at frozen line " + row.Line.ToString(CultureInfo.InvariantCulture);
            var implementation = row.Matched ? implementationHashes.Select(pair => new FeatureLedgerEvidence.Implementation(pair.Key,
                row.NativeCase + ": actual MainWindow dispatch, dialog or report frame", pair.Value)).ToList() : new();
            return new FeatureLedgerEvidence.Row(row.Id, implementation,
                row.Matched ? new() { new("native-shell-main", "test", nameof(ShellMainMessageProbe), assertion, log, "") } : new(),
                row.Reason, Compared: row.Matched ? row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + rowsById[row.Id].Detail
                    + "; frozen handler " + row.Member + (row.MemberSha256.Length == 0 ? "" : " SHA256=" + row.MemberSha256)
                    + "; native case " + row.NativeCase + "; before=" + row.Before + "; actual=" + row.Actual : null);
        }).ToList();
        var git = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        git.ArgumentList.Add("rev-parse"); git.ArgumentList.Add("HEAD");
        using var head = Process.Start(git)!; string commit = head.StandardOutput.ReadToEnd().Trim(); head.WaitForExit();
        if (head.ExitCode != 0) throw new IOException("Could not record actual execution head.");
        var run = new FeatureLedgerEvidence.Run("native-shell-main", Environment.CommandLine, args[3], commit, artifacts, root);
        var register = new FeatureLedgerEvidence.Register(1, "SH-main-routes", FeatureLedger.ReferenceCommit, new() { run }, rows);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "SH-main-routes.json"), register);
        var validation = FeatureLedgerEvidence.Load(root, inventory, [register]);
        var problems = validation.FatalProblems.Concat(validation.Entries.Values.Where(entry => entry.Row.Proofs.Count > 0)
            .SelectMany(entry => entry.Problems)).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "candidate-validation.json"), new { valid = problems.Length == 0, problems });
        if (problems.Length > 0) throw new InvalidDataException("Native MainWindow candidate failed validation: " + string.Join("; ", problems.Take(5)));
        Console.WriteLine("SH-main-routes: " + rows.Count(row => row.Proofs.Count > 0) + " supported; "
            + rows.Count(row => row.Proofs.Count == 0) + " remain unproven.");
        return 0;
    }

    private static string[] ShellMainImplementation(string root)
    {
        string[] appNames = ["MainWindow", "ShellDialogs", "ShellDocumentState", "HomeControl", "ComputeXicViewModel", "QuantifyControl"];
        string[] testNames = ["LedgerMemberFactComparison", "Program.ShellRouteNative", "Program.ShellMainMessages", "Program.ShellMainReceipts", "Program.ShellSeproFixture", "Program.ShellProofWindows"];
        var app = Directory.GetFiles(Path.Combine(root, "FamilyQuant"), "*", SearchOption.TopDirectoryOnly)
            .Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".xaml", StringComparison.Ordinal))
            .Where(path => appNames.Any(name => Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal)));
        var tests = Directory.GetFiles(Path.Combine(root, "FamilyQuant.Tests"), "*.cs", SearchOption.TopDirectoryOnly)
            .Where(path => testNames.Any(name => Path.GetFileName(path).StartsWith(name + ".", StringComparison.Ordinal)));
        var core = Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => Path.GetFileName(path).StartsWith("FamilyQuantFile", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("ShellProjectOperations", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("ProjectStore", StringComparison.Ordinal)
                || path.Contains(Path.DirectorySeparatorChar + "Storage" + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        return app.Concat(tests).Concat(core).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Distinct(StringComparer.Ordinal).OrderBy(path => path, StringComparer.Ordinal).ToArray();
    }
}
