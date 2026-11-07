using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using FamilyQuant.Panels;
using FamilyQuant.Report;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyPanelCommandTable() =>
    [
        new("--test-family-panel", 71, TestCommandPhase.Raw,
            args => args.Contains("--test-family-panel", StringComparer.Ordinal), (_, _) =>
            {
                FamilyPanelTests();
                FamilyPanelReportTests();
                FamilyPanelSubclassTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void FamilyPanelTests()
    {
        Check("J1a project exposes the optional user family panel",
            typeof(FamilyClusterFile).GetProperty("FamilyPanel")?.PropertyType.FullName == "FamilyQuant.Panels.FamilyPanel");
        Check("J1a evidence recipe exposes the optional user family panel",
            typeof(Evidence.EvidenceRecipe).GetProperty("FamilyPanel")?.PropertyType.FullName == "FamilyQuant.Panels.FamilyPanel");
        string directory = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.Panel");
        Directory.CreateDirectory(directory);
        var file = TaskFile();
        string old = file.ToJsonText();
        Check("J1a old document bytes stay equal after open/save", FamilyClusterFile.FromJsonText(old).ToJsonText() == old
            && !old.Contains("FamilyPanel", StringComparison.Ordinal) && !old.Contains("ProteinSubclassLabels", StringComparison.Ordinal));
        string legacyPath = Path.Combine(directory, "legacy.fqproj");
        ProjectStore.WriteFqproj(legacyPath, file, new[] { new QuantRow { Sequence = "ACDEFGHIK", FamilyUserId = 1,
            EnvelopeSum = 12.5, IsotopeAreas = new[] { 12.5 } } }, new QuantManifest());
        var oldProject = ProjectStore.ReadFqproj(legacyPath);
        Check("J1a old project opens with curation bytes and stored quantities unchanged", oldProject.fcf.ToJsonText() == old
            && oldProject.quant.Single().EnvelopeSum == 12.5 && oldProject.fcf.FamilyPanel == null);
        string legacyResave = Path.Combine(directory, "legacy-resaved.fqproj");
        ProjectStore.WriteFqproj(legacyResave, oldProject.fcf, oldProject.quant, oldProject.manifest);
        using (var zip = System.IO.Compression.ZipFile.OpenRead(legacyResave))
        using (var reader = new StreamReader(zip.GetEntry(ProjectStore.CurationFormat2EntryName)!.Open()))
            // Z15b1: the resave is written as curation format 2; its content is the original curation byte for byte.
            Check("J1a old project resave preserves its original curation bytes", FamilyClusterFile.FromJsonText(reader.ReadToEnd()).ToJsonText() == old);
        string oldRecipe = JsonSerializer.Serialize(new Evidence.EvidenceRecipe());
        Check("J1a old recipe bytes stay equal after open/save", !oldRecipe.Contains("FamilyPanel", StringComparison.Ordinal)
            && JsonSerializer.Serialize(JsonSerializer.Deserialize<Evidence.EvidenceRecipe>(oldRecipe)) == oldRecipe);
        var panel = new FamilyPanel
        {
            Name = "User panel",
            Levels = new() { new() { Name = "User level", Text = "Original meaning" } },
            Import = new() { SourceName = "panel.xlsx", SourceSha256 = "fixture", FamilySheet = "Chosen sheet",
                ColumnHeaders = new() { ["key"] = "My key" } },
            DecisionMappings = new() { new() { SourceValue = "Chosen", Decision = FamilyPanelDecision.Quantify } },
            Families = new()
            {
                new() { Key = "f1", DisplayName = "Family one", SourceDecision = "Chosen",
                    Names = new() { new() { Name = "Language", Text = "Nome original" } },
                    Pfam = new() { new() { Accession = "domain-a", Name = "User domain" } },
                    Subclasses = new() { new() { Key = "s1", RequiredPfam = new() { "domain-a" }, AbsentPfam = new() { "domain-b" } } },
                    TextFields = new() { new() { Name = "Literature", Text = "1,5%; not measured\nOriginal source" } } },
                new() { Key = "f2", Decision = FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey = "f1" },
                new() { Key = "f3", Decision = FamilyPanelDecision.NotAFamily },
                new() { Key = "f4", Decision = FamilyPanelDecision.ReportUnderOthers },
            },
        };
        FamilyPanelValidation.Validate(panel);
        file.FamilyPanel = panel;
        file.EvidenceRecipe = new() { FamilyPanel = FamilyPanelValidation.Clone(panel) };
        string expected = JsonSerializer.Serialize(panel);
        Check("J1a merge/exclude decisions resolve from user data", FamilyPanelValidation.ResolveFamilyKey(panel, "f2") == "f1"
            && FamilyPanelValidation.ResolveFamilyKey(panel, "f3") == null && FamilyPanelValidation.ResolveFamilyKey(panel, "f4") == "f4");
        string path = Path.Combine(directory, "panel.fqproj");
        ProjectStore.WriteFqproj(path, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(path).fcf;
        Check("J1a fqproj retains every panel field", JsonSerializer.Serialize(reopened.FamilyPanel) == expected);
        Check("J1a recipe retains its independent panel snapshot", JsonSerializer.Serialize(reopened.EvidenceRecipe!.FamilyPanel) == expected
            && !ReferenceEquals(reopened.FamilyPanel, reopened.EvidenceRecipe.FamilyPanel));
        Check("J1a subclass required/absent lists and original text survive", reopened.FamilyPanel!.Families[0].Subclasses[0].AbsentPfam.Single() == "domain-b"
            && reopened.FamilyPanel.Families[0].TextFields[0].Text == panel.Families[0].TextFields[0].Text);
        var undo = GroupingSnapshot.CaptureEvidence(file);
        file.FamilyPanel.Families[0].DisplayName = "Changed";
        undo.Restore(file);
        Check("J1a evidence undo restores panel without aliasing", JsonSerializer.Serialize(file.FamilyPanel) == expected);
        void Refused(string name, Action<FamilyPanel> change)
        {
            var invalid = FamilyPanelValidation.Clone(panel);
            change(invalid);
            bool refused = false;
            try { FamilyPanelValidation.Validate(invalid); } catch (ArgumentException) { refused = true; }
            Check(name, refused);
        }
        Refused("J1a duplicate family keys refused", p => p.Families[1].Key = "f1");
        Refused("J1a missing merge target refused", p => p.Families[1].MergeTargetFamilyKey = "absent");
        Refused("J1a cyclic family merge refused", p => { p.Families[0].Decision = FamilyPanelDecision.MergeIntoAnotherFamily; p.Families[0].MergeTargetFamilyKey = "f2"; });
        Refused("J1a contradictory subclass refused", p => p.Families[0].Subclasses[0].AbsentPfam.Add("domain-a"));
        Refused("J1a missing subclass report target refused", p => p.Families[0].Subclasses[0].ReportAsSubclassKey = "missing");
        Refused("J1a duplicate source decision mapping refused", p => p.DecisionMappings.Add(p.DecisionMappings[0]));
        var source = TaskFile();
        source.FamilyPanel = FamilyPanelValidation.Clone(panel);
        string before = source.ToJsonText();
        var options = new Evidence.EvidenceClassificationOptions { ScopeTerms = new[] { "Leading scope" } };
        var proposal = Evidence.EvidenceClassifier.Propose(source.MyExtendedFastaItems, options);
        var applied = Evidence.EvidenceApply.Apply(source, proposal, new Evidence.EvidenceApplySelection
        {
            SourceFingerprint = proposal.SourceFingerprint,
            SelectedProteinIndices = proposal.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
        }, "fixture");
        Check("J1a native apply snapshots panel in recipe", applied.Changed && JsonSerializer.Serialize(applied.Recipe.FamilyPanel) == JsonSerializer.Serialize(source.FamilyPanel));
        var replaySource = FamilyClusterFile.FromJsonText(before);
        var replay = Evidence.EvidenceApply.ApplyRecipe(replaySource, applied.Recipe);
        Check("J1a recipe replay reproduces panel and curation", replay.Changed && replaySource.ToJsonText() == source.ToJsonText());
        source.FamilyPanel!.Name = "Different choices";
        bool stale = false;
        try { Evidence.EvidenceApply.ApplyRecipe(source, applied.Recipe); } catch (InvalidOperationException) { stale = true; }
        Check("J1a stale panel prevents silent recipe overwrite", stale);
        applied.Undo!.Restore(source);
        Check("J1a undo restores original panel and recipe", source.ToJsonText() == before);
    }
}
