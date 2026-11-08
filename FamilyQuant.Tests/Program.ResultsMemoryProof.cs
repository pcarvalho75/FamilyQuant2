using FamilyQuant.Evidence;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsMemoryProofCommands() =>
    [new("--proof-results-report-memory", 21117, TestCommandPhase.Raw,
        args => args.Length is 2 or 3 && args[0] == "--proof-results-report-memory" && (args.Length == 2 || args[2] == "--small"),
        (args, _) => ResultsMemoryProof(args[1], args.Length == 3))];

    private static int ResultsMemoryProof(string output, bool small)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Choose a fresh Results memory proof output folder.");
        Directory.CreateDirectory(output);
        var field = typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.CurationLifetimeProbe")!
            .GetField("Created", BindingFlags.NonPublic | BindingFlags.Static)!;
        object? previousObserver = field.GetValue(null);
        var previousOutput = Console.Out;
        var tracker = new ResultsMemoryRoots();
        var writer = new ResultsMemoryWriter(previousOutput, output, tracker);
        try
        {
            field.SetValue(null, (Action<object>)tracker.Created);
            Console.SetOut(writer);
            return RunCurationStringLimitProof(small);
        }
        finally
        {
            Console.SetOut(previousOutput);
            field.SetValue(null, previousObserver);
        }
    }

    private sealed class ResultsMemoryRoots
    {
        private readonly List<(WeakReference<object> Value, string Site)> roots = new();

        internal void Created(object value)
        {
            string site = string.Join(" <- ", new StackTrace(1, false).GetFrames()
                .Select(frame => frame.GetMethod()).Where(method => method?.DeclaringType?.Namespace?.StartsWith("FamilyQuant", StringComparison.Ordinal) == true)
                .Select(method => method!.DeclaringType!.Name + "." + method.Name).Take(8));
            lock (roots) roots.Add((new WeakReference<object>(value), site));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal void Report(string stage, TextWriter output)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            var heap = GC.GetGCMemoryInfo();
            output.WriteLine("ROOTS " + stage + " managedBytes=" + GC.GetTotalMemory(false)
                + " committedBytes=" + heap.TotalCommittedBytes + " fragmentedBytes=" + heap.FragmentedBytes);
            var panels = new Dictionary<ReferencePanel, List<string>>(ReferenceEqualityComparer.Instance);
            lock (roots)
            {
                foreach (var root in roots)
                {
                    if (!root.Value.TryGetTarget(out object? value)) continue;
                    var panel = value is FamilyClusterFile file ? file.EvidenceSource?.ReferencePanel : (value as EvidenceProvenance)?.ReferencePanel;
                    if (panel == null || panel.Entries.Sum(entry => (long)entry.Sequence.Length) < 100_000_000) continue;
                    if (!panels.TryGetValue(panel, out var sites)) panels[panel] = sites = new();
                    sites.Add(value.GetType().Name + " " + root.Site);
                }
            }
            output.WriteLine("ROOTS " + stage + " distinctLargePanels=" + panels.Count);
            foreach (var panel in panels)
                output.WriteLine("ROOT " + stage + " referenceCharacters=" + panel.Key.Entries.Sum(entry => (long)entry.Sequence.Length)
                    + " sites=" + string.Join(" | ", panel.Value));
            output.Flush();
        }
    }

    private sealed class ResultsMemoryWriter(TextWriter inner, string output, ResultsMemoryRoots roots) : TextWriter
    {
        private string? scratch;
        public override Encoding Encoding => inner.Encoding;
        public override void Write(char value) => inner.Write(value);
        public override void Flush() => inner.Flush();

        public override void WriteLine(string? value)
        {
            inner.WriteLine(value); inner.Flush();
            const string marker = "Scratch: ";
            int index = value?.IndexOf(marker, StringComparison.Ordinal) ?? -1;
            if (index >= 0) scratch = Path.GetFullPath(value![(index + marker.Length)..]);
            if (value?.StartsWith("MEASURE ", StringComparison.Ordinal) != true) return;
            string stage = value.Split(' ')[1];
            if (stage == "prepare-stored-results" && value.Contains("status=PASS", StringComparison.Ordinal))
                File.Copy(Path.Combine(RequireScratch(), "synthetic.fqproj"), Path.Combine(output, "synthetic.fqproj"));
            if (stage == "native-report-export" && value.Contains("status=PASS", StringComparison.Ordinal)) CaptureArtifacts();
            roots.Report(stage, inner);
        }

        private void CaptureArtifacts()
        {
            string report = Path.Combine(RequireScratch(), "synthetic-report.docx");
            foreach (string path in new[] { report, ReportSupplementWorkbook.PathForReport(report), HtmlReportWriter.PathForReport(report) })
            {
                using var stream = File.OpenRead(path);
                string hash = Convert.ToHexString(SHA256.HashData(stream));
                inner.WriteLine("ARTIFACT " + Path.GetFileName(path) + " sha256=" + hash + " bytes=" + stream.Length);
                File.Copy(path, Path.Combine(output, Path.GetFileName(path)));
            }
        }

        private string RequireScratch()
        {
            string allowed = Path.GetFullPath(CurationProofScratch).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (scratch == null || !scratch.StartsWith(allowed, StringComparison.OrdinalIgnoreCase))
                throw new IOException("The proof report is outside its owned temporary run.");
            return scratch;
        }
    }
}
