using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Evidence.Positions;
using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlCoverageCommands() =>
    [new("--inspect-html-coverage-source", 10496, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--inspect-html-coverage-source", (args, _) =>
    {
        if (args.Length != 3) throw new ArgumentException("Use --inspect-html-coverage-source <copied-project> <new-output.json>.");
        if (File.Exists(args[2])) throw new IOException("Fresh inspection output required.");
        string sha = FileFingerprint.Sha256(args[1]); var saved = ProjectStore.ReadFqproj(args[1]);
        var loc = saved.manifest.FamilyPeptideLocalization;
        var facts = new { ProjectSha256 = sha, NoCompute = true, HasLocalization = loc != null,
            RankedProteins = loc?.Families.Sum(f => f.Proteins.Count) ?? 0,
            StoredPeptideIntervals = loc?.Segments.Sum(s => s.Intervals.Count) ?? 0,
            ProteinsWithStoredPositionEvidence = saved.fcf.MyExtendedFastaItems.Count(p => p.Evidence?.Positions != null) };
        if (sha != FileFingerprint.Sha256(args[1])) throw new InvalidDataException("Read-only coverage inspection changed input.");
        File.WriteAllText(args[2], JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(facts)); return 0;
    }),
     new("--test-html-coverage", 10496, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-html-coverage", (_, _) =>
    { HtmlCoverageTests(); Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1; })];

    private static void HtmlCoverageTests()
    {
        const string sequence = "ACDEFGHIKLMNPQRSTVWY";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sequence)));
        var positions = new ProteinPositionEvidence { SequenceLength = sequence.Length, SequenceSha256 = hash,
            Pfam = new() { new() { Name = "Region 2", Accession = "PF99999", Start = 2, End = 10,
                Fragments = new() { new() { Start = 2, End = 4 }, new() { Start = 8, End = 10 } } } },
            Features = new() { new() { Type = "Signal", Description = "product 1", Start = 1, End = 2, StartModifier = "EXACT", EndModifier = "EXACT" },
                new() { Type = "Chain", Start = 3, End = 19, StartModifier = "UNKNOWN", EndModifier = "EXACT" } } };
        var project = new FamilyClusterFile { MyExtendedFastaItems = new() { new() { SequenceIdentifier = "protein1", Sequence = sequence, UserID = 7,
            Evidence = new ProteinEvidence { Positions = positions } } } };
        var localization = new FamilyPeptideLocalizationResult { MembershipSha256 = FamilyStudyMembership.Fingerprint(project), Families = new()
        { new() { FamilyUserId = 7, Proteins = new() { new() { Rank = 1, ProteinIdentifier = "protein1", ProteinLength = sequence.Length, ProteinSequenceSha256 = hash, CreditedSignal = 10 } } } },
            Segments = new() { new() { FamilyUserId = 7, ProteinIdentifier = "protein1", NormalizedSequence = "ACDE", Side = NativeQuantFractions.SideIdentified,
                MatchKind = PeptideLocalizationMatchKind.ExactContainment, Intervals = new() { new() { Start = 1, End = 4 } } },
                new() { FamilyUserId = 7, ProteinIdentifier = "protein1", NormalizedSequence = "FGKL", Side = NativeQuantFractions.SideDeNovo,
                    MatchKind = PeptideLocalizationMatchKind.Aligned, Intervals = new() { new() { Start = 5, End = 6 }, new() { Start = 9, End = 10 } } } } };
        var manifest = new QuantManifest { FamilyPeptideLocalization = localization };
        string proteinHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("protein1"))).ToLowerInvariant();
        string id = "figure:protein_coverage_7_" + proteinHash + ".png";
        var figure = new ReportFigure(id, "Coverage protein1", new byte[] { 1, 2 }, "Stored caption 20 residues", 100, 100);
        var doc = new ReportDocument { Sections = new[] { new ReportSection("family:test", "Family", new ReportBlock[] { figure }) } };
        var fields = new Dictionary<string, HtmlReportField>();
        string Add(string text, HtmlReportReference source, string unit, double? value)
        { string key = "coverage" + fields.Count; fields.Add(key, new(text, value, unit, source)); return key; }
        IReadOnlyList<HtmlReportCoverage> Build(ReportSelection? selection = null) => HtmlReportCoverageBuilder.Build(doc, project, manifest, new[] { 7 }, Add, selection, "family:test");
        var result = Build();
        Check("M1g stored Word coverage produces a live strip", result.Count == 1 && result[0].Length == sequence.Length && result[0].Png == null);
        if (result.Count == 0) return;
        var live = result[0];
        Check("M1g preserves stored gapped peptide blocks and exact/aligned kinds", live.Peptides.Count == 3
            && live.Peptides.Count(p => p.Kind == nameof(PeptideLocalizationMatchKind.Aligned)) == 2
            && !live.Peptides.Any(p => p.Start == 5 && p.End == 10));
        Check("M1g domains retain fragments and never fill their enclosing gap", live.Domains.Count(p => p.Kind == "domain") == 2
            && !live.Domains.Any(p => p.Start == 2 && p.End == 10));
        Check("M1g exact features draw while uncertain features have no invented interval", live.Domains.Count(p => p.Kind == "feature") == 1);
        Check("M1g all printed stored labels resolve exactly", fields.Values.Where(f => f.Source.Kind == "stored-value")
            .All(f => f.Text == HtmlReportStoredValues.Display(f.Source, project, manifest)));
        Check("M1g field ids cover every hover range", live.Peptides.Concat(live.Domains).All(p => p.Range != null && fields.ContainsKey(p.Range)));
        Check("M1g family exclusion removes all coverage", Build(new() { ExcludedIds = new[] { "family:test" } }).Count == 0);
        Check("M1g block exclusion removes coverage", Build(new() { ExcludedIds = new[] { id } }).Count == 0);
        positions.SequenceSha256 = "different";
        Check("M1g same length with wrong position SHA draws no domains", Build().Single().Domains.Count == 0);
        localization.Segments.Clear();
        Check("M1g no usable stored intervals keeps the native PNG", Build().Single().Png == Convert.ToBase64String(figure.PngBytes).Insert(0, "data:image/png;base64,"));
        manifest.FamilyPeptideLocalization = null;
        Check("M1g absent localization keeps selected native figure only", Build().Single().Length == null && Build().Single().Png != null);
    }
}
