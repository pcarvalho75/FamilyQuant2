using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static class IdentitySearchStudy
{
    internal const string Argument = "--identity-search-study";
    private static readonly UTF8Encoding Utf8 = new(false);

    internal sealed record Probe(int Identity, int Groups, int TargetGroups, int MixedGroups)
    {
        public double Score => Groups == 0 ? 0 : (double)MixedGroups / Groups;
        public double TargetRelativeScore => TargetGroups == 0 ? 0 : (double)MixedGroups / TargetGroups;
    }

    internal static int Run(string[] args)
    {
        if (args.Length != 4 || args[0] != Argument || (args[3] != "fast" && args[3] != "exhaustive"))
            throw new ArgumentException("Supply input FASTA, empty output directory and fast/exhaustive mode.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        bool exhaustive = args[3] == "exhaustive";
        string input = Path.GetFullPath(args[1]);
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("An identity study never replaces an existing result packet.");
        string sourceHash = Hash(input);
        Directory.CreateDirectory(output);

        const double acceptableScore = 0;
        long pairwiseBefore = USearchTools.PairwiseAlignmentRunCount;
        Console.WriteLine("Scan grid: 95 through 10, step 5; acceptable mixing score: 0.");
        var scan = FamilyClusterFile.ScanIdentities(input, from: 95, to: 10, step: 5, exhaustive: exhaustive);
        if (exhaustive && (USearchTools.GetPairwiseEdgesForTest(new FileInfo(input))?.Count ?? 0) == 0)
            throw new InvalidDataException("The exhaustive study has no pairwise alignments; fallback cannot prove equivalence.");
        WriteRows(Path.Combine(output, "scan.tsv"), scan.Select(r => new Probe(r.Identity, r.Groups, r.TargetGroups, r.MixedGroups)));

        DivideAndConquerResult divided;
        using var captured = new StringWriter(CultureInfo.InvariantCulture);
        TextWriter original = Console.Out;
        try
        {
            Console.SetOut(new TeeWriter(original, captured));
            divided = FamilyClusterFile.DivideAndConquerDetailed(input, acceptableScore, exhaustive: exhaustive);
        }
        finally
        {
            Console.SetOut(original);
            File.WriteAllText(Path.Combine(output, "divide-full.log"), captured.ToString(), Utf8);
        }

        var probes = ReadProbes(captured.ToString());
        WriteRows(Path.Combine(output, "divide-probes.tsv"), probes);
        if (sourceHash != Hash(input)) throw new InvalidDataException("The study input changed during the run.");
        int? lowestGrid = LowestAcceptable(scan, acceptableScore);
        var summary = new
        {
            SourceSha256 = sourceHash,
            Mode = args[3],
            AcceptableMixingScore = acceptableScore,
            ScanFrom = 95,
            ScanTo = 10,
            ScanStep = 5,
            ScanGrid = scan.Select(r => r.Identity).ToArray(),
            LowestAcceptableScanIdentity = lowestGrid,
            DivideIdentity = divided.Identity,
            DivideIdentityBelow = divided.IdentityBelow,
            DivideProbes = probes.Select(p => p.Identity).ToArray(),
            PairwiseAlignmentRuns = USearchTools.PairwiseAlignmentRunCount - pairwiseBefore,
            SameAnswer = lowestGrid == divided.Identity
        };
        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "summary.json"), json + "\n", Utf8);
        Console.WriteLine(json);
        return 0;
    }

    internal static int? LowestAcceptable(IEnumerable<IdentityScanRow> rows, double acceptableScore) =>
        rows.Where(r => r.Score <= acceptableScore).Select(r => (int?)r.Identity).Min();

    internal static List<Probe> ReadProbes(string log)
    {
        var matches = Regex.Matches(log,
            @"^Identity (\d+)% -> (\d+) groups \((\d+) with targets, (\d+) mixed\), score ",
            RegexOptions.Multiline | RegexOptions.CultureInvariant);
        if (matches.Count == 0) throw new InvalidDataException("The detailed search did not record its probes.");
        return matches.Select(m => new Probe(Parse(m, 1), Parse(m, 2), Parse(m, 3), Parse(m, 4))).ToList();

        static int Parse(Match match, int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    }

    private static void WriteRows(string path, IEnumerable<Probe> rows)
    {
        using var writer = new StreamWriter(path, false, Utf8);
        writer.WriteLine("identity\tgroups\ttarget_groups\tmixed_groups\tscore\ttarget_relative_score");
        foreach (var row in rows)
            writer.WriteLine(FormattableString.Invariant($"{row.Identity}\t{row.Groups}\t{row.TargetGroups}\t{row.MixedGroups}\t{row.Score:R}\t{row.TargetRelativeScore:R}"));
    }

    private static string Hash(string path) => TestFileHash.Upper(path);

    private sealed class TeeWriter(TextWriter first, TextWriter second) : TextWriter
    {
        public override Encoding Encoding => first.Encoding;
        public override void Write(char value) { first.Write(value); second.Write(value); }
        public override void Write(string? value) { first.Write(value); second.Write(value); }
        public override void WriteLine(string? value) { first.WriteLine(value); second.WriteLine(value); }
        public override void Flush() { first.Flush(); second.Flush(); }
    }
}
