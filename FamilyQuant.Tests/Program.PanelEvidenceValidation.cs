using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEvidenceValidationCommandTable() =>
    [
        new("--test-panel-evidence-validation", 79, TestCommandPhase.Raw, a => a.Contains("--test-panel-evidence-validation", StringComparer.Ordinal), (_, _) =>
        {
            PanelEvidenceValidationTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--panel-evidence-audit", 80, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-evidence-audit", (a, _) => PanelEvidenceAuditCommand(a)),
        new("--panel-draft-comparison", 81, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-draft-comparison", (a, _) => PanelEvidenceAuditCommand(a)),
    ];

    private static void PanelEvidenceValidationTests()
    {
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Families = new() { new() { Key = "target", Pfam = new() { new() { Accession = "domain-a" } } } } },
            MyExtendedFastaItems = Enumerable.Range(11, 2).Select(n => new FastaItemExtended
            {
                SequenceIdentifier = "sp|P000" + n.ToString(CultureInfo.InvariantCulture) + "|FIXTURE",
                Sequence = string.Concat(Enumerable.Repeat("ACDEFGHIK", 12)), UserID = 1, USearchID = 1,
                Evidence = new() { Accession = "P000" + n.ToString(CultureInfo.InvariantCulture), Reviewed = true,
                    Family = "source-family", Keywords = new() { "chosen" }, Pfam = new() { new() { Accession = "domain-a" } } },
            }).ToList(),
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "chosen" } };
        var references = PanelEvidenceClassifier.References(file, options);
        var search = NearestReviewed.Search(NearestReviewed.Targets(file), references);
        string before = file.ToJsonText();
        var right = PanelEvidenceValidation.LeaveOneOut(file, options, search);
        Check("J2b source-label-masked LOO uses another reviewed entry", right.ReviewedEntries == 2 && right.Right == 2
            && right.Rows.All(r => r.TrainingReferences == 1 && r.NeighbourAccession != FamilyNaming.Accession(r.Identifier)));
        var noHits = new NeighbourSearch { Method = search.Method, TieMethod = search.TieMethod, CoverageMethod = search.CoverageMethod,
            MinIdentityPercent = search.MinIdentityPercent, MinQueryAlignedPercent = search.MinQueryAlignedPercent,
            Queries = search.Queries, References = search.References, QuerySha256 = search.QuerySha256, ReferenceSha256 = search.ReferenceSha256 };
        var abstained = PanelEvidenceValidation.LeaveOneOut(file, options, noHits);
        Check("J2b a hidden own family cannot rescue a query without a neighbour", abstained.Right == 0 && abstained.Abstained == 2 && abstained.RightRate == 0);
        var noExpectation = FamilyClusterFile.FromJsonText(before);
        noExpectation.FamilyPanel!.Families[0].Pfam.Clear();
        var unknown = PanelEvidenceValidation.LeaveOneOut(noExpectation, options, search);
        Check("J2b unresolved expectations stay explicit and never earn a right call", unknown.Unscorable == 2 && unknown.Scorable == 0 && unknown.RightRate == null);
        Check("J2b validation changes no source value", file.ToJsonText() == before);
        var draft = FamilyClusterFile.FromJsonText(before);
        foreach (var protein in draft.MyExtendedFastaItems) protein.UserAnnotation = "target";
        draft.MyExtendedFastaItems[1].UserAnnotation = "Undeclared label";
        draft.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "sp|P00999|OLD", Sequence = "ACDE", UserID = 2, UserAnnotation = "target" });
        var proposal = PanelEvidenceClassifier.Propose(file, options, search, references);
        var differences = PanelEvidenceValidation.CompareDraft(file, proposal, draft);
        Check("J2b every draft difference has a program reason, including unknown labels and missing entries", differences.Count == 2
            && differences.All(d => d.Reason.Length > 0) && differences.Any(d => !d.DraftMappingKnown)
            && differences.Any(d => d.Kind == "Absent from current source"));
        draft.MyExtendedFastaItems[0].UserAnnotation = "";
        draft.MyExtendedFastaItems[0].UserID = 99;
        var withBlank = PanelEvidenceValidation.CompareDraft(file, proposal, draft);
        Check("J2b blank draft annotation means outside, not an unresolved named family", withBlank.Any(d => d.Identifier == file.MyExtendedFastaItems[0].SequenceIdentifier
            && d.DraftMappingKnown && d.Kind == "Added"));
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string first = PanelEvidenceValidation.BuildLeaveOneOutCsv(right) + PanelEvidenceValidation.BuildDraftCsv(differences);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J2b LOO and draft CSV bytes are culture invariant", first == PanelEvidenceValidation.BuildLeaveOneOutCsv(right) + PanelEvidenceValidation.BuildDraftCsv(differences));
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static int PanelEvidenceAuditCommand(string[] args)
    {
        int initialPassed = passed, initialFailed = failed, initialSkipped = skipped;
        if (args.Length < 5 || args.Length > 7)
            throw new ArgumentException("--panel-evidence-audit <program-project> <draft.fcf> <scope> <new-output-directory> [draft-label-map.json] [link-choices.json]");
        if (Directory.Exists(args[4])) throw new IOException("Choose a new output directory.");
        var input = ReadPanelInput(args[1]);
        var options = new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(args[3]) };
        var search = input.File.EvidenceSource?.NearestReviewed ?? throw new InvalidDataException("The project needs its current tie-aware search.");
        var references = PanelEvidenceClassifier.References(input.File, options);
        var proposal = PanelEvidenceClassifier.Propose(input.File, options, search, references);
        if (args.Length == 7)
        {
            var choices = JsonSerializer.Deserialize<List<PanelFamilyLinkSelection>>(File.ReadAllText(args[6]))!;
            input.File.FamilyPanel = PanelEvidenceClassifier.ConfirmLinks(input.File.FamilyPanel!, proposal.PanelContext!.Links, choices);
            proposal = PanelEvidenceClassifier.Propose(input.File, options, search, references);
        }
        var draft = FamilyClusterFile.Load(args[2]);
        var labelMap = args.Length >= 6 ? JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(args[5])) : null;
        bool draftOnly = args[0] == "--panel-draft-comparison";
        var loo = draftOnly ? null : PanelEvidenceValidation.LeaveOneOut(input.File, options, search, (done, total) =>
        { if (done % 20 == 0 || done == total) Console.WriteLine("Leave one out: " + done.ToString(CultureInfo.InvariantCulture) + "/" + total.ToString(CultureInfo.InvariantCulture)); });
        var differences = PanelEvidenceValidation.CompareDraft(input.File, proposal, draft, labelMap);
        if (loo != null) Check("native panel audit accounts for every reviewed entry", loo.Right + loo.Wrong + loo.Abstained + loo.Unscorable == loo.ReviewedEntries);
        Check("native panel audit explains every draft difference", differences.All(d => d.Reason.Length > 0));
        Directory.CreateDirectory(args[4]);
        if (loo != null)
        {
            File.WriteAllText(Path.Combine(args[4], "leave-one-out.csv"), PanelEvidenceValidation.BuildLeaveOneOutCsv(loo), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(args[4], "leave-one-out.json"), JsonSerializer.Serialize(loo, PanelJson));
        }
        File.WriteAllText(Path.Combine(args[4], "draft-differences.csv"), PanelEvidenceValidation.BuildDraftCsv(differences), new UTF8Encoding(true));
        File.WriteAllText(Path.Combine(args[4], "draft-differences.json"), JsonSerializer.Serialize(differences, PanelJson));
        File.WriteAllText(Path.Combine(args[4], "provenance.json"), JsonSerializer.Serialize(new
        {
            Input = Path.GetFullPath(args[1]), InputSha256 = TestFileHash.Upper(args[1]), Draft = Path.GetFullPath(args[2]), DraftSha256 = TestFileHash.Upper(args[2]),
            Scope = options.ScopeTerms, input.File.SourceSha256, proposal.SourceFingerprint, SearchMethod = search.Method, search.TieMethod,
            CoreAssemblySha256 = TestFileHash.Upper(typeof(FamilyClusterFile).Assembly.Location),
            UnconfirmedLinks = proposal.PanelContext!.Links.Count(l => l.NeedsConfirmation),
            ExpectedBasis = "Source annotations mapped through the user's panel; not independent biological truth.",
            Training = "Own entry and isoforms removed from reference support; own source family masked; scope and domains retained.",
            DraftLabelMap = labelMap,
            LinkChoices = args.Length == 7 ? Path.GetFullPath(args[6]) : null,
            LinkChoicesSha256 = args.Length == 7 ? TestFileHash.Upper(args[6]) : null,
        }, PanelJson));
        if (loo != null) Console.WriteLine(loo.Right.ToString(CultureInfo.InvariantCulture) + " right / " + loo.Scorable.ToString(CultureInfo.InvariantCulture)
            + " scorable; " + loo.Wrong.ToString(CultureInfo.InvariantCulture) + " wrong; " + loo.Abstained.ToString(CultureInfo.InvariantCulture)
            + " abstained; " + loo.Unscorable.ToString(CultureInfo.InvariantCulture) + " unresolved expectations; "
            + differences.Count.ToString(CultureInfo.InvariantCulture) + " draft differences.");
        Console.WriteLine($"===== {passed - initialPassed} passed, {failed - initialFailed} failed, {skipped - initialSkipped} skipped =====");
        return failed == initialFailed ? 0 : 1;
    }
}
