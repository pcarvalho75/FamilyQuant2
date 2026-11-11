using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelLinkRoundtripCommands() =>
    [new("--test-panel-link-roundtrip", 74, TestCommandPhase.Raw, a => a.Contains("--test-panel-link-roundtrip", StringComparer.Ordinal), (_, _) =>
    {
        PanelLinkRoundtripTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-link-roundtrip"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static void PanelLinkRoundtripTests(string output)
    {
        Directory.CreateDirectory(output);
        var file = new FamilyClusterFile
        {
            FamilyPanel = new() { Families = new()
            {
                new() { Key = "A", DisplayName = "User first", Pfam = new() { new() { Accession = "PF90101" } } },
                new() { Key = "B", DisplayName = "User second", Pfam = new() { new() { Accession = "PF90101" } } },
            } },
            MyExtendedFastaItems = Enumerable.Range(1, 2).Select(i => new FastaItemExtended
            {
                SequenceIdentifier = "sp|P6100" + i + "|SYNTHETIC", Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = i, USearchID = i,
                Evidence = new() { Accession = "P6100" + i, Reviewed = true, Family = "Synthetic source family " + i,
                    Keywords = new() { "selected" }, Pfam = new() { new() { Accession = "PF90101" } } },
            }).ToList(),
        };
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        string legacyDefinition = PanelEvidenceClassifier.DefinitionFingerprint(file.FamilyPanel);
        string legacyFamily = PanelDerivedDataCurrentness.FamilyFingerprint(file.FamilyPanel);
        var initial = PanelEvidenceClassifier.Propose(file, options);
        Check("RG4 equal-domain rows first need an explicit choice", initial.PanelContext!.Links.Count(l => l.NeedsConfirmation) == 5);
        var selections = initial.PanelContext.Links.Where(l => l.NeedsConfirmation)
            .Select(l => new PanelFamilyLinkSelection { LinkKey = l.Key, PanelFamilyKey = "B" }).ToArray();
        var chosen = PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel, initial.PanelContext.Links, selections);
        string sheet = Path.Combine(output, "chosen.xlsx");
        FamilyPanelTemplate.Write(sheet, chosen);
        var direct = FamilyPanelTemplate.Read(sheet);
        Check("RG4 saved panel carries several exact source families on the chosen row", direct.Families.Single(f => f.Key == "B")
            .UniProtFamilies.SequenceEqual(new[] { "Synthetic source family 1", "Synthetic source family 2" })
            && direct.Families.Single(f => f.Key == "A").UniProtFamilies.Count == 0);
        Check("RG4 hidden metadata retains architecture and leave-out choice support", direct.EvidenceLinks.Count == chosen.EvidenceLinks.Count
            && PanelEvidenceClassifier.DefinitionFingerprint(direct) == legacyDefinition);

        var importer = new FamilyPanelSpreadsheetImporter();
        var workbook = importer.ProposeWorkbook(sheet);
        workbook.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.FamilyKey] = "Key";
        workbook.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.DisplayName] = "Name";
        workbook.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.Pfam] = "Pfam";
        workbook.FamilyProposal.Choices.ColumnHeaders[PanelImportFields.UniProtFamilies] = "UniProt family";
        var imported = importer.CheckConfirmedWorkbook(workbook, workbook.FamilyProposal.Choices, workbook.FamilyProposal.Decisions);
        Check("RG4 the saved sheet imports through the ordinary workbook route", imported.CanImport && imported.Panel != null,
            string.Join("; ", imported.Problems.Select(p => p.Message)));
        if (!imported.CanImport || imported.Panel == null) return;
        var fresh = new FamilyClusterFile { FamilyPanel = imported.Panel,
            MyExtendedFastaItems = JsonSerializer.Deserialize<List<FastaItemExtended>>(JsonSerializer.Serialize(file.MyExtendedFastaItems))! };
        string before = fresh.ToJsonText();
        var restored = PanelEvidenceClassifier.Propose(fresh, options);
        Check("RG4 a new project restores every chosen link without a question", restored.PanelContext!.Links.All(l => !l.NeedsConfirmation)
            && restored.Proteins.All(p => p.Family == "B") && fresh.FamilyPanel.EvidenceLinks.Count == 0);
        Check("RG4 proposing imported links does not modify the project", fresh.ToJsonText() == before);
        var exact = new FamilyClusterFile { FamilyPanel = FamilyPanelValidation.Clone(fresh.FamilyPanel),
            MyExtendedFastaItems = JsonSerializer.Deserialize<List<FastaItemExtended>>(JsonSerializer.Serialize(file.MyExtendedFastaItems))! };
        foreach (var protein in exact.MyExtendedFastaItems) protein.Evidence!.Family += " extra";
        Check("RG4 source-family names match the complete label rather than a substring", PanelEvidenceClassifier.Propose(exact, options)
            .PanelContext!.Links.Any(l => l.NeedsConfirmation));

        var changed = FamilyPanelValidation.Clone(chosen);
        string chosenFingerprint = PanelDerivedDataCurrentness.FamilyFingerprint(chosen);
        changed.Families[0].UniProtFamilies.Add("Synthetic source family 1");
        Check("RG4 stored user choices take precedence over row family labels", PanelEvidenceClassifier.Propose(file, options, panel: changed)
            .Proteins.All(p => p.Family == "B"));
        Check("RG4 row family labels invalidate derived family evidence", PanelDerivedDataCurrentness.FamilyFingerprint(changed) != chosenFingerprint
            && PanelDerivedDataCurrentness.FamilyFingerprint(fresh.FamilyPanel) != legacyFamily);
        var normalized = FamilyPanelValidation.Clone(fresh.FamilyPanel);
        normalized.Families.Single(f => f.Key == "B").UniProtFamilies = new() { " synthetic SOURCE family 2 ", "SYNTHETIC SOURCE FAMILY 1", "synthetic source family 1" };
        Check("RG4 harmless name case order and duplicate changes retain the rule fingerprint",
            PanelDerivedDataCurrentness.FamilyFingerprint(normalized) == PanelDerivedDataCurrentness.FamilyFingerprint(fresh.FamilyPanel));

        string project = Path.Combine(output, "new-project.fqproj");
        ProjectStore.WriteFqproj(project, fresh, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project);
        Check("RG4 the existing versioned project format persists the row rules", reopened.manifest.FormatVersion == ProjectStore.CurrentFormatVersion
            && JsonSerializer.Serialize(reopened.fcf.FamilyPanel) == JsonSerializer.Serialize(fresh.FamilyPanel)
            && PanelEvidenceClassifier.Propose(reopened.fcf, options).PanelContext!.Links.All(l => !l.NeedsConfirmation));
        var legacy = JsonSerializer.Deserialize<FamilyPanel>("{\"Families\":[{\"Key\":\"old\"}]}")!;
        Check("RG4 an older panel without the optional field opens with no named rule", legacy.Families.Single().UniProtFamilies.Count == 0);
    }
}
