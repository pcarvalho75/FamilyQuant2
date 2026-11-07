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
    private static IEnumerable<TestCommand> PanelImportSeveralCommandTable() =>
    [
        new("--test-panel-import-several", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-panel-import-several", (_, _) =>
        {
            PanelImportSeveralTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-import-several"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static void PanelImportSeveralTests(string workDir)
    {
        Section("Z12p3 a row listing several families in one cell");
        var importer = new FamilyPanelSpreadsheetImporter();
        List<string> Parts(string cell)
        {
            var table = ListTable(("X", cell, ""));
            return importer.Check(table, ListChoices(table), Array.Empty<FamilyPanelDecisionMapping>()).Rows.Single().ProposedParts.Select(p => p.Name).ToList();
        }
        Check("Z12p3 commas, semicolons and line breaks separate names; commas inside parentheses or brackets do not",
            Parts("A, B; C\nD\r\nE").SequenceEqual(new[] { "A", "B", "C", "D", "E" })
            && Parts("AMP (antimicrobial peptide, short), TCTP").SequenceEqual(new[] { "AMP (antimicrobial peptide, short)", "TCTP" })
            && Parts("[a, b] c, (d; e)) f, g").SequenceEqual(new[] { "[a, b] c", "(d; e)) f", "g" }),
            string.Join(" | ", Parts("[a, b] c, (d; e)) f, g")));
        Check("Z12p3 leading bullets are dropped and one name is not a list",
            Parts("• Endopeptidase\n• TCTP").SequenceEqual(new[] { "Endopeptidase", "TCTP" }) && Parts("- A\n- B").SequenceEqual(new[] { "A", "B" })
            && Parts("Fosfolipase A2").Count == 0 && Parts("A,  ,\n").Count == 0 && Parts("L-amino-acid oxidase; ").Count == 0);

        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, "consenso inventado.xlsx");
        GracefulWorkbook(path);
        var table8 = new PanelSpreadsheetReader().ReadTable(path, GracefulSheet, 2);
        var proposal = importer.Propose(table8);
        var choices = proposal.Choices;
        choices.ColumnHeaders[PanelImportFields.Decision] = "DECISÃO FamilyQuant";
        var decisions = importer.ProposeDecisions(table8, choices).Select(d => d.Mapping).ToList();
        choices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 11, Action = FamilyPanelRowAction.Import, Key = "HYAL-2" } };
        var asWritten = importer.Check(table8, choices, decisions, "Consenso");
        var row8 = asWritten.Rows.Single(r => r.RowNumber == 8);
        string[] names = { "Endopeptidase", "sulfhydryl oxidase", "carboxypeptidase", "AMP (antimicrobial peptide, short)", "TCTP" };
        var note = asWritten.Problems.Single(p => p.Kind == PanelImportProblemKind.SeveralFamilies);
        Check("Z12p3 the list row is returned as such with proposed keys from its abbreviation, and nothing changes until a choice",
            asWritten.CanImport && row8 is { Action: FamilyPanelRowAction.Import, ListColumn: "Nome (inglês)" }
            && row8.ProposedParts.Select(p => p.Name).SequenceEqual(names)
            && row8.ProposedParts.Select(p => p.Key).SequenceEqual(Enumerable.Range(1, 5).Select(n => "Outras raras-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            && row8.ListLabel.Length == 0 && row8.SuggestedAction == null
            && row8.FamilyKeys.SequenceEqual(new[] { "Outras raras" })
            && note is { RowNumber: 8, Column: "Nome (inglês)", ColumnLetter: "C", Severity: PanelImportSeverity.Note }
            && note.Message == "Row 8, column \"Nome (inglês)\": the cell lists 5 names (Endopeptidase; sulfhydryl oxidase; carboxypeptidase; "
                + "AMP (antimicrobial peptide, short); TCTP). Choose one family per name, or one group reported under others; until then the row is one family named as written.",
            note.Message);

        choices.RowChoices.Add(new FamilyPanelRowChoice { RowNumber = 8, Action = FamilyPanelRowAction.Split });
        var split = importer.Check(table8, choices, decisions, "Consenso");
        var parts = split.Panel?.Families.Where(f => f.Key.StartsWith("Outras raras-", StringComparison.Ordinal)).ToList() ?? new();
        Check("Z12p3 one family per name: proposed keys and names, the row's decision and every original cell",
            split.CanImport && parts.Count == 5 && parts.Select(f => f.DisplayName).SequenceEqual(names)
            && parts.All(f => f.Decision == FamilyPanelDecision.Quantify && f.SourceDecision == "Opcional" && f.Pfam.Count == 0)
            && parts.Select(f => PanelImportFields.OtherNameText(split.Panel!, f)).SequenceEqual(names)
            && parts.All(f => f.TextFields.Single(t => t.Name == "Nome (português)").Text == "Outras raras" && f.TextFields.Count == table8.Headers.Count)
            && !split.Panel!.Families.Any(f => f.Key == "Outras raras") && split.Rows.Single(r => r.RowNumber == 8).FamilyKeys.SequenceEqual(parts.Select(f => f.Key))
            && !split.Problems.Any(p => p.Kind == PanelImportProblemKind.SeveralFamilies),
            string.Join(" / ", split.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).Select(p => p.Message)));

        choices.RowChoices[1] = new FamilyPanelRowChoice { RowNumber = 8, Action = FamilyPanelRowAction.ReportUnderOthers };
        var grouped = importer.Check(table8, choices, decisions, "Consenso");
        Check("Z12p3 one group reported under others keeps the row as one family with that decision",
            grouped.Panel?.Families.Single(f => f.Key == "Outras raras") is { Decision: FamilyPanelDecision.ReportUnderOthers, SourceDecision: "Opcional", DisplayName: "Outras raras" } g
            && g.Names.Single().Text == "Endopeptidase, sulfhydryl oxidase, carboxypeptidase, AMP (antimicrobial peptide, short), TCTP"
            && grouped.Panel!.Families.Count == 8);

        choices.RowChoices[1] = new FamilyPanelRowChoice { RowNumber = 8, Action = FamilyPanelRowAction.Split, Parts = new()
        {
            new() { Key = "ENDO", Name = "Endopeptidase" }, new() { Key = "SOX", Name = "Sulfidril oxidase" }, new() { Key = "AMP", Name = "Antimicrobial peptide" },
        } };
        var edited = importer.Check(table8, choices, decisions, "Consenso");
        var panel = edited.Panel;
        Check("Z12p3 the user's keys and names replace the proposal, and other-language names are kept only when they pair one to one",
            panel != null && panel.Families.Where(f => f.Key is "ENDO" or "SOX" or "AMP").Select(f => f.DisplayName).SequenceEqual(new[] { "Endopeptidase", "Sulfidril oxidase", "Antimicrobial peptide" })
            && panel.Families.Where(f => f.Key is "ENDO" or "SOX" or "AMP").All(f => f.Names.Count == 0));
        string json = JsonSerializer.Serialize(panel);
        Check("Z12p3 the split and its edited parts replay exactly", json == JsonSerializer.Serialize(importer.Replay(path, panel!)));
        var file = new FamilyClusterFile { FamilyPanel = panel, EvidenceRecipe = new Evidence.EvidenceRecipe { FamilyPanel = panel } };
        string project = Path.Combine(workDir, "several.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        Check("Z12p3 project and recipe keep the split choice", json == JsonSerializer.Serialize(reopened.FamilyPanel)
            && json == JsonSerializer.Serialize(reopened.EvidenceRecipe?.FamilyPanel) && json == JsonSerializer.Serialize(importer.Replay(path, reopened.FamilyPanel!)));

        choices.RowChoices[1] = new FamilyPanelRowChoice { RowNumber = 8, Action = FamilyPanelRowAction.Split, Parts = new()
            { new() { Key = "AChE", Name = "Endopeptidase" }, new() { Key = "", Name = "TCTP" }, new() { Key = "TC", Name = " " } } };
        var clash = importer.Check(table8, choices, decisions, "Consenso");
        var clashes = clash.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).ToList();
        Check("Z12p3 a part's repeated, empty abbreviation or empty name is a sentence naming the row and the part",
            !clash.CanImport && clashes.Count == 3
            && clashes[0] is { Kind: PanelImportProblemKind.DuplicateKey, RowNumber: 8, Value: "AChE", Suggestion: "AChE-2" } && clashes[0].OtherRowNumbers.SequenceEqual(new[] { 3 })
            && clashes[0].Message == "Row 8, column \"Sigla\", name 1 of the list (\"Endopeptidase\"): the abbreviation \"AChE\" is also used in row 3. Rename it (for example \"AChE-2\")."
            && clashes[1] is { Kind: PanelImportProblemKind.MissingKey, Suggestion: "Outras raras-5" }
            && clashes[1].Message == "Row 8, column \"Sigla\", name 2 of the list (\"TCTP\"): the abbreviation is empty. Type one (for example \"Outras raras-5\")."
            && clashes[2] is { Kind: PanelImportProblemKind.MissingName, Column: "Nome (português)" }
            && clashes[2].Message == "Row 8, column \"Nome (português)\", name 3 of the list: the name is empty. Type one.",
            string.Join(" / ", clashes.Select(p => p.Message)));
        choices.RowChoices[1] = new FamilyPanelRowChoice { RowNumber = 7, Action = FamilyPanelRowAction.Split };
        var notList = importer.Check(table8, choices, decisions, "Consenso");
        Check("Z12p3 splitting a row that lists one name asks for the families instead of guessing",
            !notList.CanImport && notList.Problems.Single(p => p.Severity == PanelImportSeverity.Blocking).Message
                == "Row 7, column \"Nome (português)\": the cell does not list several names, so the row cannot be split. Type the families, or choose Import, Report under others or Skip.");

        var domains = ListTable(("SVMP", "Metaloproteinase, classe P-III", "Reprolysin (PF01421); Disintegrin (PF00200)"), ("MIX", "A; B", "Kunitz_BPTI (PF00014)"));
        var domainChoices = ListChoices(domains);
        domainChoices.ColumnHeaders[PanelImportFields.Pfam] = "Pfam";
        var asIs = importer.Check(domains, domainChoices, Array.Empty<FamilyPanelDecisionMapping>());
        Check("Z12p3 a name with a comma is only offered as a list; imported as written it keeps its domains",
            asIs.CanImport && asIs.Problems.Count(p => p.Kind == PanelImportProblemKind.SeveralFamilies) == 2
            && asIs.Panel!.Families[0] is { Key: "SVMP", DisplayName: "Metaloproteinase, classe P-III" } svmp && svmp.Pfam.Select(d => d.Accession).SequenceEqual(new[] { "PF01421", "PF00200" }));
        domainChoices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 3, Action = FamilyPanelRowAction.Split } };
        var splitDomains = importer.Check(domains, domainChoices, Array.Empty<FamilyPanelDecisionMapping>());
        Check("Z12p3 a split row's domains stay text only, with a note, because they cannot be told apart",
            splitDomains.Panel?.Families.Where(f => f.Key.StartsWith("MIX-", StringComparison.Ordinal)).ToList() is { Count: 2 } mix
            && mix.All(f => f.Pfam.Count == 0 && f.TextFields.Single(t => t.Name == "Pfam").Text == "Kunitz_BPTI (PF00014)")
            && splitDomains.Problems.Any(p => p.Kind == PanelImportProblemKind.SeveralFamilies && p.Column == "Pfam"
                && p.Message == "Row 3, column \"Pfam\": the Pfam domains are kept as text only, because they cannot be told apart for the 2 families of the row."));
        var legacy = ListChoices(domains); legacy.ImportVersion = PanelImportRules.Legacy;
        var legacyCheck = importer.Check(domains, legacy, Array.Empty<FamilyPanelDecisionMapping>());
        Check("Z12p3 version 0 choices look for no lists", legacyCheck.CanImport && !legacyCheck.Problems.Any(p => p.Kind == PanelImportProblemKind.SeveralFamilies)
            && legacyCheck.Rows.All(r => r.ProposedParts.Count == 0));
        var collide = ListTable(("K", "A, B", ""), ("K-1", "Other", ""));
        var collideCheck = importer.Check(collide, ListChoices(collide), Array.Empty<FamilyPanelDecisionMapping>());
        Check("Z12p3 a proposed part key never repeats an existing abbreviation",
            collideCheck.Rows[0].ProposedParts.Select(p => p.Key).SequenceEqual(new[] { "K-1-2", "K-2" }));

        Section("Z12p3a a several-families cell read gracefully");
        var labelled = ListTable(("RARE", "Outras raras: hialuronidase (HYA); quitinase (QTN); glutaminil ciclase (GC); AMP (peptídeo antimicrobiano, curto)", ""),
            ("FET", "Fetuína, inibidor de protease", ""), ("TRI", "A, B, C", ""), ("SEMI", "A; B", ""), ("LINE", "A\nB", ""), ("QTN", "Quitinase própria", ""));
        labelled.Headers.Add("Nome (inglês)"); labelled.ColumnLetters.Add("D");
        string[] english = { "Other rare families: hyaluronidase; chitinase; glutaminyl cyclase; AMP", "Fetuin, protease inhibitor", "", "", "", "Own chitinase" };
        for (int i = 0; i < labelled.Rows.Count; i++) labelled.Rows[i].Cells["Nome (inglês)"] = english[i];
        var labelledChoices = ListChoices(labelled); labelledChoices.ColumnHeaders[PanelImportFields.OtherName] = "Nome (inglês)";
        var labelledCheck = importer.Check(labelled, labelledChoices, Array.Empty<FamilyPanelDecisionMapping>());
        var rare = labelledCheck.Rows[0];
        Check("Z12p3a a leading label is offered as the group's name and is not glued to the first family",
            rare.ListLabel == "Outras raras" && rare.ProposedParts.Select(p => p.Name).SequenceEqual(new[] { "hialuronidase", "quitinase", "glutaminil ciclase", "AMP (peptídeo antimicrobiano, curto)" }),
            rare.ListLabel + " | " + string.Join(" | ", rare.ProposedParts.Select(p => p.Name)));
        Check("Z12p3a each part takes the abbreviation in its parentheses, otherwise the row's abbreviation and a number, never a key already used",
            rare.ProposedParts.Select(p => p.Key).SequenceEqual(new[] { "HYA", "QTN-2", "GC", "RARE-4" }), string.Join(" | ", rare.ProposedParts.Select(p => p.Key)));
        Check("Z12p3a two names split by a comma alone start on Keep as one family; three names, a semicolon or a line break do not",
            labelledCheck.Rows[1].SuggestedAction == FamilyPanelRowAction.Import && labelledCheck.Rows[2].SuggestedAction == null
            && labelledCheck.Rows[3].SuggestedAction == null && labelledCheck.Rows[4].SuggestedAction == null && rare.SuggestedAction == null);
        labelledChoices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = rare.RowNumber, Action = FamilyPanelRowAction.Split } };
        var labelledSplit = importer.Check(labelled, labelledChoices, Array.Empty<FamilyPanelDecisionMapping>());
        var rareFamilies = labelledSplit.Panel?.Families.Where(f => f.Key is "HYA" or "QTN-2" or "GC" or "RARE-4").ToList() ?? new();
        Check("Z12p3a split families take their names from the same columns as every other family",
            rareFamilies.Select(f => f.DisplayName).SequenceEqual(new[] { "hialuronidase", "quitinase", "glutaminil ciclase", "AMP (peptídeo antimicrobiano, curto)" })
            && rareFamilies.Select(f => PanelImportFields.OtherNameText(labelledSplit.Panel!, f)).SequenceEqual(new[] { "hyaluronidase", "chitinase", "glutaminyl cyclase", "AMP" }),
            string.Join(" / ", labelledSplit.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).Select(p => p.Message)));
        labelledChoices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = rare.RowNumber, Action = FamilyPanelRowAction.ReportUnderOthers } };
        var labelledGroup = importer.Check(labelled, labelledChoices, Array.Empty<FamilyPanelDecisionMapping>());
        Check("Z12p3a one group reported under others is named by the label in each name column",
            labelledGroup.Panel?.Families.Single(f => f.Key == "RARE") is { Decision: FamilyPanelDecision.ReportUnderOthers, DisplayName: "Outras raras" } group
            && PanelImportFields.OtherNameText(labelledGroup.Panel, group) == "Other rare families");
    }

    private static PanelSpreadsheetTable ListTable(params (string Key, string Name, string Pfam)[] rows) => new()
    {
        SourceName = "list.xlsx", SheetName = "S", HeaderRowNumber = 1, Headers = new() { "Sigla", "Nome", "Pfam" }, ColumnLetters = new() { "A", "B", "C" },
        Rows = rows.Select((r, i) => new PanelSpreadsheetRow { RowNumber = i + 2, Cells = new() { ["Sigla"] = r.Key, ["Nome"] = r.Name, ["Pfam"] = r.Pfam } }).ToList(),
    };

    private static FamilyPanelImport ListChoices(PanelSpreadsheetTable table) => new()
    {
        FamilySheet = table.SheetName, HeaderRowNumber = table.HeaderRowNumber, ImportVersion = PanelImportRules.Current,
        ColumnHeaders = new() { [PanelImportFields.FamilyKey] = "Sigla", [PanelImportFields.DisplayName] = "Nome" },
    };
}
