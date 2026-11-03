using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelMultiScopeAuditCommands() =>
    [new("--test-panel-multiscope-audit", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-panel-multiscope-audit", (_, _) =>
        {
            PanelMultiScopeAuditTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void PanelMultiScopeAuditTests()
    {
        Section("J42 panel accuracy check with multiple scope phrases");
        const string sequence = "ACDEFGHIKLMNPQRSTVWY";
        ProteinEvidence Evidence(string accession, string keyword) => new()
        {
            Accession = accession, Reviewed = true, Family = "source group", Keywords = new() { keyword },
            Pfam = new() { new() { Accession = "domain-a" } },
        };
        var file = new FamilyClusterFile
        {
            FamilyPanel = new FamilyPanel { Families = new() { new() { Key = "first", DisplayName = "First group",
                Pfam = new() { new() { Accession = "domain-a" } } } } },
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "sp|P00011|LOCAL", Sequence = sequence, UserID = 1,
                    Evidence = Evidence("P00011", "outside") },
                new() { SequenceIdentifier = "sp|P00012|SCOPED", Sequence = sequence, UserID = 2,
                    Evidence = Evidence("P00012", "first scope") },
                new() { SequenceIdentifier = SequenceLabels.ContaminantPrefix + "fixture", Sequence = sequence, UserID = 3,
                    Evidence = Evidence("fixture", "first scope") },
            },
            EvidenceSource = new EvidenceProvenance { ReferencePanel = new ReferencePanel
            {
                Entries = new() { new PanelEntry { Accession = "P00011", Sequence = sequence,
                    Evidence = Evidence("P00011", "second scope") } },
            } },
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "first scope", "second scope" } };
        var panel = PanelEvidenceClassifier.References(file, options);
        var all = NearestReviewed.References(file);
        var scoped = NearestReviewed.References(file, evidence => EvidenceClassifier.IsInScope(evidence, options.ScopeTerms));
        Check("J42 fixture isolates the panel-specific reviewed reference set", panel.Any(r => r.Accession == "P00011" && r.Identifier.Length == 0)
            && !all.Any(r => r.Accession == "P00011" && r.Identifier.Length == 0) && scoped.Count > panel.Count);
        var search = NearestReviewed.Search(NearestReviewed.Targets(file), panel);
        file.EvidenceSource!.NearestReviewed = search;
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.J42", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string project = Path.Combine(root, "project.fqproj"), draft = Path.Combine(root, "draft.fcf"), output = Path.Combine(root, "audit");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        file.Save(draft);
        bool auditPassed = false;
        try
        {
            auditPassed = PanelEvidenceAuditCommand(new[] { "--panel-evidence-audit", project, draft,
                "\"first scope\", \"second scope\"", output }) == 0;
        }
        catch (InvalidOperationException) { }
        Check("J42 the native two-phrase audit accepts its own current panel references", auditPassed
            && File.Exists(Path.Combine(output, "leave-one-out.csv")) && File.Exists(Path.Combine(output, "leave-one-out.json")));

        var method = typeof(EvidenceApply).GetMethod("ValidateSearch", BindingFlags.NonPublic | BindingFlags.Static)!;
        bool Validate(NeighbourSearch candidate, IReadOnlyList<NeighbourReference>? references, bool allowPanel = true)
        {
            try { method.Invoke(null, new object?[] { file, candidate, references, options, null, false, allowPanel }); return true; }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { return false; }
        }
        Check("J42 the saved panel search is rediscovered without an explicit reference list", Validate(search, null));
        Check("J42 ordinary non-panel validation is not broadened", !Validate(search, panel, false));
        var subset = panel.Where(reference => reference.Identifier.Length == 0).ToArray();
        var subsetSearch = NearestReviewed.Search(NearestReviewed.Targets(file), subset);
        Check("J42 an explicitly selected current panel subset retains its established behavior", Validate(subsetSearch, subset)
            && PanelEvidenceClassifier.Propose(file, options, subsetSearch, subset).Proteins.Count == file.MyExtendedFastaItems.Count);
        var changedQuery = FamilyClusterFile.FromJsonText(file.ToJsonText());
        changedQuery.MyExtendedFastaItems[0].Sequence += "A";
        Check("J42 a changed target sequence still invalidates the stored search", Throws<InvalidOperationException>(() =>
            PanelEvidenceClassifier.Propose(changedQuery, options, search, PanelEvidenceClassifier.References(changedQuery, options))));
        var changedReference = FamilyClusterFile.FromJsonText(file.ToJsonText());
        changedReference.EvidenceSource!.ReferencePanel!.Entries[0].Sequence += "A";
        Check("J42 a changed panel reference still invalidates the stored search", Throws<InvalidOperationException>(() =>
            PanelEvidenceClassifier.Propose(changedReference, options, search, PanelEvidenceClassifier.References(changedReference, options))));
        var narrowed = new EvidenceClassificationOptions { ScopeTerms = new[] { "first scope" } };
        Check("J42 a changed scope still invalidates the stored search", Throws<InvalidOperationException>(() =>
            PanelEvidenceClassifier.Propose(file, narrowed, search, PanelEvidenceClassifier.References(file, narrowed))));
        var mixed = panel.Concat(all.Where(r => r.Identifier == "sp|P00011|LOCAL")).ToArray();
        var mixedSearch = NearestReviewed.Search(NearestReviewed.Targets(file), mixed);
        Check("J42 an arbitrary mixed reference set is not accepted as the panel set", !Validate(mixedSearch, mixed));
    }
}
