using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using FamilyQuant.Panels;
using FamilyQuant.DeNovo;
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
    private static IEnumerable<TestCommand> PanelEvidenceLiveCommandTable() =>
    [
        new("--panel-evidence-propose", 75, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-evidence-propose", (a, _) => PanelEvidenceProposeCommand(a)),
        new("--panel-evidence-apply", 76, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--panel-evidence-apply", (a, _) => PanelEvidenceApplyCommand(a)),
    ];

    private static readonly JsonSerializerOptions PanelJson = new() { WriteIndented = true };
    private static (FamilyClusterFile File, List<QuantRow> Quant, QuantManifest Manifest) ReadPanelInput(string path)
    {
        if (path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase))
        { var stored = ProjectStore.ReadFqproj(path); return (stored.fcf, stored.quant, stored.manifest); }
        return (FamilyClusterFile.Load(path), new(), new());
    }

    private static int PanelEvidenceProposeCommand(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("--panel-evidence-propose <copied-evidence-project> <panel-project> <original-database.fcf> <scope> <new-output-directory>");
        if (Directory.Exists(args[5])) throw new IOException("Choose a new output directory.");
        var input = ReadPanelInput(args[1]);
        var panel = ReadPanelInput(args[2]).File.FamilyPanel ?? throw new InvalidDataException("The panel input has no family panel.");
        var original = FamilyClusterFile.Load(args[3]);
        bool hash = input.File.SourceSha256.Length > 0 && string.Equals(input.File.SourceSha256, original.SourceSha256, StringComparison.OrdinalIgnoreCase);
        bool members = input.File.MyExtendedFastaItems.Select(p => (p.SequenceIdentifier, p.Sequence))
            .SequenceEqual(original.MyExtendedFastaItems.Select(p => (p.SequenceIdentifier, p.Sequence)));
        Check("native panel input declares the original database hash", hash);
        Check("native panel input retains every original identifier and sequence", members);
        if (!hash || !members) return 1;
        var options = new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(args[4]) };
        input.File.FamilyPanel = FamilyPanelValidation.Clone(panel);
        var references = PanelEvidenceClassifier.References(input.File, options);
        var search = NearestReviewed.Search(NearestReviewed.Targets(input.File), references,
            minIdentityPercent: Math.Min(options.AnnotatedIdentityPercent, options.UnannotatedIdentityPercent),
            minQueryAlignedPercent: options.MinQueryAlignedPercent,
            operation: new OperationContext(progress: new PanelConsoleProgress()));
        input.File.EvidenceSource ??= new EvidenceProvenance();
        input.File.EvidenceSource.NearestReviewed = search;
        var proposal = PanelEvidenceClassifier.Propose(input.File, options, search, references);
        Directory.CreateDirectory(args[5]);
        string prepared = Path.Combine(args[5], "source-panel.fqproj");
        ProjectStore.WriteFqproj(prepared, input.File, input.Quant, input.Manifest);
        var reopened = ProjectStore.ReadFqproj(prepared).fcf;
        Check("native prepared project preserves proposal source and panel", EvidenceClassifier.Fingerprint(reopened.MyExtendedFastaItems) == proposal.SourceFingerprint
            && JsonSerializer.Serialize(reopened.FamilyPanel) == JsonSerializer.Serialize(input.File.FamilyPanel));
        WritePanelProposal(args[5], proposal);
        File.WriteAllText(Path.Combine(args[5], "provenance.json"), JsonSerializer.Serialize(new
        {
            EvidenceProject = Path.GetFullPath(args[1]), EvidenceProjectSha256 = TestFileHash.Upper(args[1]),
            OriginalDatabase = Path.GetFullPath(args[3]), OriginalDatabaseFileSha256 = TestFileHash.Upper(args[3]),
            DeclaredDatabaseSha256 = input.File.SourceSha256, input.File.SourceSequenceCount,
            PanelProject = Path.GetFullPath(args[2]), PanelProjectSha256 = TestFileHash.Upper(args[2]),
            Targets = proposal.Proteins.Count, ReviewedReferences = references.Count, Scope = options.ScopeTerms,
            search.Method, search.CoverageMethod, search.TieMethod,
            ProgramVersion = AppVersionInfo.Current(), CoreAssemblySha256 = TestFileHash.Upper(typeof(FamilyClusterFile).Assembly.Location),
            TestAssemblySha256 = TestFileHash.Upper(typeof(Program).Assembly.Location), PreparedProject = prepared,
        }, PanelJson));
        Console.WriteLine(proposal.Proteins.Count.ToString(CultureInfo.InvariantCulture) + " targets; "
            + proposal.Proteins.Count(p => p.SuggestedSelected).ToString(CultureInfo.InvariantCulture) + " proposed; "
            + proposal.PanelContext!.Links.Count(l => l.NeedsConfirmation).ToString(CultureInfo.InvariantCulture) + " links require confirmation.");
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int PanelEvidenceApplyCommand(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("--panel-evidence-apply <source-panel.fqproj> <explicit-link-choices.json> <scope> <new-output-directory>");
        if (Directory.Exists(args[4])) throw new IOException("Choose a new output directory.");
        var input = ReadPanelInput(args[1]);
        var panel = input.File.FamilyPanel ?? throw new InvalidDataException("No panel is stored.");
        var options = new EvidenceClassificationOptions { ScopeTerms = WordCluster.ParseKeywords(args[3]) };
        var references = PanelEvidenceClassifier.References(input.File, options);
        var search = input.File.EvidenceSource?.NearestReviewed ?? throw new InvalidDataException("Prepare a tie-aware search first.");
        var initial = PanelEvidenceClassifier.Propose(input.File, options, search, references);
        using var choiceJson = JsonDocument.Parse(File.ReadAllText(args[2]));
        foreach (var entry in choiceJson.RootElement.EnumerateArray())
            if (!entry.TryGetProperty(nameof(PanelFamilyLinkSelection.LinkKey), out _)
                || !entry.TryGetProperty(nameof(PanelFamilyLinkSelection.PanelFamilyKey), out _))
                throw new InvalidDataException("Every explicit choice must supply LinkKey and PanelFamilyKey (null means leave outside).");
        var choices = JsonSerializer.Deserialize<List<PanelFamilyLinkSelection>>(choiceJson.RootElement.GetRawText())!;
        var selectedPanel = PanelEvidenceClassifier.ConfirmLinks(panel, initial.PanelContext!.Links, choices);
        var proposal = PanelEvidenceClassifier.Propose(input.File, options, search, references, selectedPanel);
        if (proposal.PanelContext!.Links.Any(l => l.NeedsConfirmation))
            throw new InvalidOperationException("Confirm every ambiguous link, including explicit outside choices, before applying.");
        string before = input.File.ToJsonText();
        var result = EvidenceApply.Apply(input.File, proposal, new EvidenceApplySelection
        {
            SourceFingerprint = proposal.SourceFingerprint, FamilyPanel = selectedPanel,
            SelectedProteinIndices = proposal.Proteins.Where(p => p.SuggestedSelected && !input.File.LockedUserIds.Contains(input.File.MyExtendedFastaItems[p.ProteinIndex].UserID))
                .Select(p => p.ProteinIndex).ToArray(), SessionSearch = search, SessionReferences = references,
        }, AppVersionInfo.Current());
        Check("native panel apply produces current per-target evidence", result.Changed && PanelAssignmentEvidenceStore.Current(input.File) != null);
        var subclasses = SubclassAssignment.ComputeCurrent(input.File);
        SubclassAssignment.CommitCandidate(input.File, subclasses);
        Check("native panel apply recomputes current subclass labels", SubclassAssignment.IsCurrent(input.File));
        var replay = FamilyClusterFile.FromJsonText(before);
        EvidenceApply.ApplyRecipe(replay, input.File.EvidenceRecipe!);
        Check("native panel recipe replays the exact applied curation", replay.ToJsonText() == input.File.ToJsonText());
        Directory.CreateDirectory(args[4]);
        ProjectStore.WriteFqproj(Path.Combine(args[4], "assigned.fqproj"), input.File, input.Quant, input.Manifest);
        File.WriteAllText(Path.Combine(args[4], "recipe.json"), JsonSerializer.Serialize(input.File.EvidenceRecipe, PanelJson));
        File.WriteAllText(Path.Combine(args[4], "assignment-evidence.json"), JsonSerializer.Serialize(PanelAssignmentEvidenceStore.RequireCurrent(input.File), PanelJson));
        File.WriteAllText(Path.Combine(args[4], "family-map.json"), JsonSerializer.Serialize(PanelAssignmentEvidenceStore.GetCurrentFamilyMap(input.File), PanelJson));
        var facts = PanelAssignmentEvidenceStore.RequireCurrent(input.File);
        var counts = new StringBuilder("Panel key,Decision,Merge target,Effective key,Applied database proteins\r\n");
        foreach (var family in input.File.FamilyPanel!.Families)
        {
            string? effective = FamilyPanelValidation.ResolveFamilyKey(input.File.FamilyPanel, family.Key);
            counts.Append(string.Join(",", new[] { family.Key, family.Decision.ToString(), family.MergeTargetFamilyKey, effective,
                (effective == null ? 0 : facts.Count(f => f.IsApplied && f.PanelFamilyKey == effective)).ToString(CultureInfo.InvariantCulture) }
                .Select(value => SpreadsheetSafeCsv.Encode(value)))).Append("\r\n");
        }
        File.WriteAllText(Path.Combine(args[4], "family-assignment-counts.csv"), counts.ToString(), new UTF8Encoding(true));
        var outside = new StringBuilder("Identifier,Reason\r\n");
        foreach (var fact in facts.Where(f => !f.IsApplied)) outside.Append(string.Join(",", new[] { fact.Identifier, fact.Reason }
            .Select(value => SpreadsheetSafeCsv.Encode(value)))).Append("\r\n");
        File.WriteAllText(Path.Combine(args[4], "outside-panel.csv"), outside.ToString(), new UTF8Encoding(true));
        Console.WriteLine(facts.Count(f => f.IsApplied).ToString(CultureInfo.InvariantCulture) + " applied database proteins; "
            + facts.Count(f => !f.IsApplied).ToString(CultureInfo.InvariantCulture) + " outside the panel.");
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void WritePanelProposal(string directory, EvidenceProposal proposal)
    {
        File.WriteAllText(Path.Combine(directory, "proposal.json"), JsonSerializer.Serialize(proposal, PanelJson));
        File.WriteAllText(Path.Combine(directory, "link-proposals.json"), JsonSerializer.Serialize(proposal.PanelContext!.Links, PanelJson));
        var csv = new StringBuilder("Protein index,Identifier,Panel family key,Tier,Reason\r\n");
        foreach (var row in proposal.Proteins)
            csv.Append(string.Join(",", new[] { row.ProteinIndex.ToString(CultureInfo.InvariantCulture), row.Identifier, row.Family, row.Tier.ToString(), row.Reason }
                .Select(value => SpreadsheetSafeCsv.Encode(value)))).Append("\r\n");
        File.WriteAllText(Path.Combine(directory, "proposal.csv"), csv.ToString(), new UTF8Encoding(true));
    }

    private sealed class PanelConsoleProgress : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value) => Console.WriteLine(value.Stage + ": " + value.Message);
    }
}
