using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using A = DocumentFormat.OpenXml.Drawing;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelImportGracefulCommandTable() =>
    [
        new("--test-panel-import-graceful", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-panel-import-graceful", (_, _) =>
        {
            PanelImportGracefulTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "panel-import-graceful"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private const string GracefulSheet = "1. Proposta de famílias";

    private static void PanelImportGracefulTests(string workDir)
    {
        Section("Z12p1 a consensus family sheet imports gracefully");
        Directory.CreateDirectory(workDir);
        string path = Path.Combine(workDir, "consenso inventado.xlsx");
        GracefulWorkbook(path);
        string original = TestFileHash.Upper(path);
        var reader = new PanelSpreadsheetReader(); var importer = new FamilyPanelSpreadsheetImporter();

        var preview = reader.ReadPreview(path);
        Check("Z12p1 preview lists every tab, flags the hidden one and opens the first visible sheet",
            preview.SheetNames.SequenceEqual(new[] { GracefulSheet, "2. Legenda", "3. Fontes", "Notas internas" })
            && preview.HiddenSheetNames.SequenceEqual(new[] { "Notas internas" }) && preview.SheetName == GracefulSheet, string.Join("|", preview.SheetNames));
        Check("Z12p1 the header row below a title is proposed from Excel's filter, with a reason",
            preview.ProposedHeaderRow == 2 && preview.HeaderReason.Contains("filter", StringComparison.Ordinal), preview.HeaderReason);
        var title = preview.Rows[0].Cells;
        Check("Z12p1 preview is a dense grid with merged title text and row numbers as in Excel",
            preview.Rows.Select(r => r.RowNumber).SequenceEqual(Enumerable.Range(1, preview.Rows.Count)) && preview.Rows.All(r => r.Cells.Count == preview.ColumnCount)
            && title.Take(6).All(c => c.MergeRange == "A1:F1" && c.Text == "Consenso de famílias de toxinas (exemplo inventado)") && title[6] is { Text: "", MergeRange: "" }
            && preview.Rows[4].Cells.All(c => c.Text.Length == 0));
        PanelPreviewCell Sigla(int row) => preview.Rows[row - 1].Cells[0];
        Check("Z12p1 preview shows direct, theme-tinted and indexed fill colours with plain colour words",
            Sigla(3) is { Fill: "#F4B183", FillName: "orange" } && Sigla(7) is { Fill: "#CCFFCC", FillName: "light green" }
            && Sigla(8) is { Fill: "#D9D9D9", FillName: "light grey" } && Sigla(13) is { Fill: "#8E44AD", FillName: "purple" }
            && Sigla(4).FillName == "light blue" && Sigla(4).Fill != "#5B9BD5" && preview.Rows[2].Cells[1].Fill.Length == 0,
            string.Join(" ", new[] { 3, 4, 7, 8, 13 }.Select(r => Sigla(r).Fill + "=" + Sigla(r).FillName)));
        Check("Z12p1 a formula without a saved result and an Excel error are notes, not exceptions",
            preview.Problems.Any(p => p.Kind == PanelImportProblemKind.FormulaWithoutResult && p.RowNumber == 13 && p.ColumnLetter == "F")
            && preview.Problems.Any(p => p.Kind == PanelImportProblemKind.CellError && p.RowNumber == 9 && p.Value == "#N/A")
            && preview.Problems.All(p => p.Severity == PanelImportSeverity.Note));
        var legend = reader.ReadPreview(path, "2. Legenda");
        Check("Z12p1 without a filter the first row of several labels above data is proposed", legend.ProposedHeaderRow == 3
            && legend.HeaderReason.StartsWith("Row 3 is the first row with several different labels", StringComparison.Ordinal), legend.HeaderReason);
        var sources = reader.ReadPreview(path, "3. Fontes");
        Check("Z12p1 rows kept in view by frozen panes outrank the label heuristic", sources.ProposedHeaderRow == 2
            && sources.HeaderReason.Contains("stay in view", StringComparison.Ordinal), sources.HeaderReason);
        var missingSheet = reader.ReadPreview(path, "Não existe");
        Check("Z12p1 a missing sheet is a sentence and the tabs are still listed", missingSheet.SheetNames.Count == 4
            && missingSheet.Problems.Single() is { Kind: PanelImportProblemKind.SheetMissing, Severity: PanelImportSeverity.Blocking, Message: "The workbook has no sheet named \"Não existe\"." });
        string notWorkbook = Path.Combine(workDir, "not a workbook.xlsx");
        File.WriteAllText(notWorkbook, "Sigla;Nome\nAChE;Acetilcolinesterase\n");
        var unreadable = reader.ReadPreview(notWorkbook);
        var absent = reader.ReadPreview(Path.Combine(workDir, "absent.xlsx"));
        Check("Z12p1 an unreadable or missing file is a plain sentence, never an exception",
            unreadable.Problems.Single() is { Kind: PanelImportProblemKind.FileUnreadable, Severity: PanelImportSeverity.Blocking } u && u.Message.Contains("save a copy as an .xlsx", StringComparison.Ordinal)
            && absent.Problems.Single().Message == "The file was not found. It may have been moved or renamed.");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
        {
            bool strictReadRefused = Throws<IOException>(() => File.ReadAllBytes(path));
            var whileOpen = reader.ReadPreview(path);
            Check("Z12p1 a workbook held open for writing, as Excel holds it, can still be read", strictReadRefused && whileOpen.Problems.Count(p => p.Severity == PanelImportSeverity.Blocking) == 0
                && whileOpen.SourceSha256 == original);
        }

        var table = reader.ReadTable(path, GracefulSheet, 2);
        Check("Z12p1 accented headers, column letters and an unnamed populated column are read", table.Headers.SequenceEqual(new[]
            { "Sigla", "Nome (português)", "Nome (inglês)", "DECISÃO FamilyQuant", "Domínio(s) Pfam", "Notas e decisões", "Column G" })
            && table.ColumnLetters.SequenceEqual(new[] { "A", "B", "C", "D", "E", "F", "G" })
            && table.Problems.Any(p => p.Kind == PanelImportProblemKind.UnnamedColumn && p.Message == "Column G has values but no name in row 2, so it is called \"Column G\"."),
            string.Join("|", table.Headers));
        Check("Z12p1 the empty row is listed and dropped; rich text, line breaks and vertical merges are kept", table.EmptyRows.SequenceEqual(new[] { 5 })
            && table.Rows.Select(r => r.RowNumber).SequenceEqual(new[] { 3, 4, 6, 7, 8, 9, 10, 11, 12, 13 })
            && table.Rows[0].Cells["Notas e decisões"] == "Núcleo do veneno;\nlinha dois"
            && table.Rows.Single(r => r.RowNumber == 10).Cells["Notas e decisões"] == "Lectinas: ver subclasses"
            && table.Rows.Single(r => r.RowNumber == 10).Merges["Notas e decisões"] == "F9:F10");

        var proposal = importer.Propose(table);
        var roles = proposal.Choices.ColumnHeaders;
        Check("Z12p1 columns are proposed from value shapes with reasons; decision and colour are left to the user",
            roles[PanelImportFields.FamilyKey] == "Sigla" && roles[PanelImportFields.DisplayName] == "Nome (português)"
            && roles[PanelImportFields.OtherName] == "Nome (inglês)" && roles[PanelImportFields.Pfam] == "Domínio(s) Pfam"
            && roles[PanelImportFields.Notes] == "Notas e decisões" && !roles.ContainsKey(PanelImportFields.Decision) && !roles.ContainsKey(PanelImportFields.ColorCategory)
            && proposal.Reasons[PanelImportFields.FamilyKey] == "\"Sigla\" holds short, distinct codes, so it is proposed as the abbreviation."
            && proposal.Reasons[PanelImportFields.Pfam] == "Most cells in \"Domínio(s) Pfam\" hold Pfam accessions such as PF00135, so it is proposed for Pfam domains."
            && proposal.ColorColumns.SequenceEqual(new[] { "Sigla" }) && proposal.Choices.ImportVersion == PanelImportRules.Current
            && proposal.Choices.HeaderRowNumber == 2 && proposal.Choices.SourceSha256 == original,
            string.Join("|", roles.Select(r => r.Key + "=" + r.Value)));

        var choices = proposal.Choices;
        var quantifyOnly = importer.Check(table, choices, Array.Empty<FamilyPanelDecisionMapping>(), "Consenso");
        var quantifyRows = quantifyOnly.Rows.ToDictionary(r => r.RowNumber);
        Check("Z12p1 without a decision column the section title and repeated header rows are skipped as notes",
            quantifyRows[6].Action == FamilyPanelRowAction.Skip && quantifyRows[12].Action == FamilyPanelRowAction.Skip
            && quantifyOnly.Problems.Any(p => p is { Kind: PanelImportProblemKind.SectionRow, RowNumber: 6, Severity: PanelImportSeverity.Note }
                && p.Message == "Row 6 is one merged cell across columns (A6:F6, \"Enzimas raras\"), like a section title, so it was skipped. Choose Import to keep it as a family.")
            && quantifyOnly.Problems.Any(p => p is { Kind: PanelImportProblemKind.RepeatedHeaderRow, RowNumber: 12 })
            && quantifyOnly.Problems.Any(p => p is { Kind: PanelImportProblemKind.EmptyRow, RowNumber: 5, Message: "Row 5 is empty and was skipped." }));
        var duplicate = quantifyOnly.Problems.Single(p => p.Kind == PanelImportProblemKind.DuplicateKey);
        Check("Z12p1 a repeated abbreviation names both rows and suggests a rename, and nothing is imported yet",
            !quantifyOnly.CanImport && duplicate is { RowNumber: 11, Column: "Sigla", ColumnLetter: "A", Value: "HYAL", Suggestion: "HYAL-2", Severity: PanelImportSeverity.Blocking }
            && duplicate.OtherRowNumbers.SequenceEqual(new[] { 7 })
            && duplicate.Message == "Row 11, column \"Sigla\": the abbreviation \"HYAL\" is also used in row 7. Rename it (for example \"HYAL-2\"), merge it into the family of row 7, or skip it.",
            duplicate.Message);
        Check("Z12p1 every problem is a sentence naming its row and, where it has one, its column",
            quantifyOnly.Problems.All(p => p.Message.Length > 0 && p.Message.EndsWith(".", StringComparison.Ordinal)
                && (p.RowNumber == 0 || p.Message.Contains("Row " + p.RowNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || p.Message.Contains("Rows " + p.RowNumber.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
                    || p.Message.StartsWith("Column " + p.ColumnLetter, StringComparison.Ordinal))
                && (p.Column.Length == 0 || p.Kind is PanelImportProblemKind.UnnamedColumn || p.Message.Contains("\"" + p.Column + "\"", StringComparison.Ordinal))
                && !p.Message.Contains("Exception", StringComparison.Ordinal)),
            string.Join(" / ", quantifyOnly.Problems.Select(p => p.Message)));
        choices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 11, Action = FamilyPanelRowAction.Import, Key = "HYAL-2" } };
        var noDecision = importer.Check(table, choices, Array.Empty<FamilyPanelDecisionMapping>(), "Consenso");
        Check("Z12p1 the decision column is optional: every family is Quantify with no mapping",
            noDecision.Panel is { } p0 && p0.Families.Select(f => f.Key).SequenceEqual(new[] { "AChE", "LAAO", "HYAL", "Outras raras", "CTL", "Gal-CTL", "HYAL-2", "KAZ" })
            && p0.Families.All(f => f.Decision == FamilyPanelDecision.Quantify && f.SourceDecision.Length == 0) && p0.DecisionMappings.Count == 0,
            string.Join(" / ", noDecision.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).Select(p => p.Message)));
        var lectin = noDecision.Panel!.Families.Single(f => f.Key == "CTL");
        Check("Z12p1 names, the other-language name, Pfam names and every original cell are kept",
            lectin.DisplayName == "Lectina tipo C" && PanelImportFields.OtherNameText(noDecision.Panel, lectin) == "C-type lectin"
            && lectin.Names.Single().Name == "Nome (inglês)" && lectin.Pfam.Single() is { Accession: "PF00059", Name: "Lectin_C" }
            && lectin.TextFields.Select(t => t.Name).SequenceEqual(table.Headers) && lectin.TextFields.Single(t => t.Name == "Notas e decisões").Text == "Lectinas: ver subclasses"
            && lectin.TextFields.Single(t => t.Name == "Notas e decisões").Text == noDecision.Panel.Families.Single(f => f.Key == "Gal-CTL").TextFields.Single(t => t.Name == "Notas e decisões").Text
            && noDecision.Panel.Families[0].TextFields.Single(t => t.Name == "Notas e decisões").Text == "Núcleo do veneno;\nlinha dois");

        choices.ColumnHeaders[PanelImportFields.Decision] = "DECISÃO FamilyQuant";
        var proposed = importer.ProposeDecisions(table, choices);
        var byLabel = proposed.ToDictionary(d => d.SourceValue);
        Check("Z12p1 every free-text decision label gets a proposal with its rows and a reason",
            proposed.Select(d => d.SourceValue).SequenceEqual(new[] { "Núcleo", "Padrão", "Opcional", "Fundir em CTL", "Excluir" })
            && byLabel["Núcleo"].RowNumbers.SequenceEqual(new[] { 3, 9 }) && byLabel["Núcleo"].Mapping.Decision == FamilyPanelDecision.Quantify
            && byLabel["Fundir em CTL"].Mapping is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "CTL" }
            && byLabel["Excluir"].Mapping.Decision == FamilyPanelDecision.NotAFamily
            && byLabel["Fundir em CTL"].Reason == "\"Fundir em CTL\" names the family CTL and a word for merging, so it is proposed as Merge into CTL.",
            string.Join(" / ", proposed.Select(d => d.SourceValue + "=" + d.Mapping.Decision)));
        var unmapped = importer.Check(table, choices, proposed.Take(4).Select(d => d.Mapping).ToList(), "Consenso");
        Check("Z12p1 an unmapped label blocks with a sentence naming its first row and column",
            !unmapped.CanImport && unmapped.Problems.Single(p => p.Kind == PanelImportProblemKind.DecisionNotMapped && p.Severity == PanelImportSeverity.Blocking).Message
                == "Row 11, column \"DECISÃO FamilyQuant\": the label \"Excluir\" has no meaning yet. Choose Quantify, Report under others, Not a family or Merge into family for it."
            && unmapped.DecisionValues.SequenceEqual(new[] { "Núcleo", "Padrão", "Opcional", "Fundir em CTL", "Excluir" }));
        var decisions = proposed.Select(d => d.Mapping).ToList();
        var mapped = importer.Check(table, choices, decisions, "Consenso");
        var panel = mapped.Panel;
        Check("Z12p1 mapped labels, a merge into another row and an empty decision import as chosen",
            panel != null && panel.Families.Single(f => f.Key == "Gal-CTL") is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "CTL", SourceDecision: "Fundir em CTL" }
            && panel.Families.Single(f => f.Key == "HYAL-2").Decision == FamilyPanelDecision.NotAFamily
            && panel.Families.Single(f => f.Key == "KAZ") is { Decision: FamilyPanelDecision.Quantify, SourceDecision: "" }
            && mapped.Problems.Any(p => p.Message == "Row 13 has an empty decision in column \"DECISÃO FamilyQuant\", so that family is quantified."),
            string.Join(" / ", mapped.Problems.Select(p => p.Message)));

        choices.ColumnHeaders[PanelImportFields.ColorCategory] = "Sigla";
        var unnamedColours = importer.Check(table, choices, decisions, "Consenso");
        Check("Z12p1 fill colours become a category only once the user names each colour",
            !unnamedColours.CanImport && unnamedColours.Colors.Select(c => c.Color).SequenceEqual(new[] { "#F4B183", Sigla(4).Fill, "#CCFFCC", "#D9D9D9", "#8E44AD" })
            && unnamedColours.Colors[0].RowNumbers.SequenceEqual(new[] { 3, 9 }) && unnamedColours.Colors[0].ColorName == "orange"
            && unnamedColours.Problems.Count(p => p.Kind == PanelImportProblemKind.ColorNotNamed) == 5
            && unnamedColours.Problems.First(p => p.Kind == PanelImportProblemKind.ColorNotNamed).Message
                == "Row 3, column \"Sigla\": the colour #F4B183 (orange) has no category name yet. Type what this colour stands for in your sheet.");
        choices.ColorLabels = unnamedColours.Colors.Select((c, i) => new FamilyPanelColorLabel { Color = c.Color.ToLowerInvariant(), Label = "Grupo " + (i + 1).ToString(CultureInfo.InvariantCulture) }).ToList();
        choices.ColorCategoryName = "Cor da sigla";
        var coloured = importer.Check(table, choices, decisions, "Consenso");
        Check("Z12p1 named colours are kept as a text field, never as a rule",
            coloured.Panel is { } p1 && p1.Families.Single(f => f.Key == "CTL").TextFields.Last() is { Name: "Cor da sigla", Text: "Grupo 1" }
            && p1.Families.Single(f => f.Key == "Gal-CTL").TextFields.Last().Text.Length == 0
            && p1.Families.All(f => f.Decision == mapped.Panel!.Families.Single(m => m.Key == f.Key).Decision));
        panel = coloured.Panel!;

        choices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 11, Action = FamilyPanelRowAction.MergeInto, Key = "HYAL-2", MergeTargetFamilyKey = "HYAL" },
            new FamilyPanelRowChoice { RowNumber = 12, Action = FamilyPanelRowAction.Skip } };
        var merged = importer.Check(table, choices, decisions, "Consenso");
        choices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 11, Action = FamilyPanelRowAction.Skip } };
        var skipped = importer.Check(table, choices, decisions, "Consenso");
        Check("Z12p1 a repeated abbreviation can instead be merged into the first row's family or skipped",
            merged.Panel?.Families.Single(f => f.Key == "HYAL-2") is { Decision: FamilyPanelDecision.MergeIntoAnotherFamily, MergeTargetFamilyKey: "HYAL" }
            && merged.Rows.Single(r => r.RowNumber == 11) is { ChosenByUser: true } && skipped.Panel?.Families.Count == 7
            && skipped.Rows.Single(r => r.RowNumber == 11).Action == FamilyPanelRowAction.Skip);
        choices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 6, Action = FamilyPanelRowAction.Import },
            new FamilyPanelRowChoice { RowNumber = 11, Action = FamilyPanelRowAction.Skip }, new FamilyPanelRowChoice { RowNumber = 40, Action = FamilyPanelRowAction.Skip } };
        var kept = importer.Check(table, choices, decisions, "Consenso");
        Check("Z12p1 keeping a section row and choosing a missing row are sentences, not exceptions",
            !kept.CanImport && kept.Problems.Any(p => p is { Kind: PanelImportProblemKind.ChoiceRowMissing, RowNumber: 40 }
                && p.Message == "A choice refers to row 40, which is not a row of this table. Choose again."));
        choices.RowChoices = new() { new FamilyPanelRowChoice { RowNumber = 11, Action = FamilyPanelRowAction.Import, Key = "HYAL-2" } };

        Check("Z12p1 Import returns the same panel as Check", JsonSerializer.Serialize(importer.Import(table, choices, decisions, "Consenso")) == JsonSerializer.Serialize(panel));
        string json = JsonSerializer.Serialize(panel);
        Check("Z12p1 a graceful import replays exactly from the stored choices", json == JsonSerializer.Serialize(importer.Replay(path, panel)));
        var file = new FamilyClusterFile { FamilyPanel = panel, EvidenceRecipe = new Evidence.EvidenceRecipe { FamilyPanel = panel } };
        string project = Path.Combine(workDir, "graceful.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        Check("Z12p1 project and recipe keep the row, colour and decision choices", json == JsonSerializer.Serialize(reopened.FamilyPanel)
            && json == JsonSerializer.Serialize(reopened.EvidenceRecipe?.FamilyPanel) && json == JsonSerializer.Serialize(importer.Replay(path, reopened.FamilyPanel!)));
        var legacy = new FamilyPanel { Import = new FamilyPanelImport { SourceSha256 = "hash", ColumnHeaders = new() { [PanelImportFields.FamilyKey] = "Key" } } };
        string legacyJson = JsonSerializer.Serialize(legacy.Import);
        Check("Z12p1 panels imported before keep their exact JSON", !legacyJson.Contains("ImportVersion", StringComparison.Ordinal)
            && !legacyJson.Contains("RowChoices", StringComparison.Ordinal) && !legacyJson.Contains("Color", StringComparison.Ordinal)
            && JsonSerializer.Serialize(JsonSerializer.Deserialize<FamilyPanelImport>(legacyJson)) == legacyJson);
        var legacyChoices = JsonSerializer.Deserialize<FamilyPanelImport>(JsonSerializer.Serialize(choices))!;
        legacyChoices.ImportVersion = PanelImportRules.Legacy; legacyChoices.RowChoices = null;
        legacyChoices.ColumnHeaders.Remove(PanelImportFields.ColorCategory);
        var legacyCheck = importer.Check(table, legacyChoices, decisions);
        Check("Z12p1 the J1b rules still apply to version 0 choices: no skipped layout rows, no trimming",
            legacyCheck.Rows.All(r => r.Action == FamilyPanelRowAction.Import) && !legacyCheck.CanImport
            && legacyCheck.Problems.Any(p => p.Kind == PanelImportProblemKind.DecisionNotMapped && p.Value == "Enzimas raras"));

        var stale = JsonSerializer.Deserialize<FamilyPanelImport>(JsonSerializer.Serialize(choices))!;
        stale.SourceSha256 = new string('0', 64);
        var notChosen = JsonSerializer.Deserialize<FamilyPanelImport>(JsonSerializer.Serialize(choices))!;
        notChosen.ColumnHeaders.Remove(PanelImportFields.DisplayName); notChosen.ColumnHeaders[PanelImportFields.Pfam] = "Pfam";
        var staleCheck = importer.Check(table, stale, decisions); var notChosenCheck = importer.Check(table, notChosen, decisions);
        Check("Z12p1 a changed workbook and unchosen or vanished columns are sentences",
            staleCheck.Problems.Single(p => p.Severity == PanelImportSeverity.Blocking).Message
                == "The workbook \"consenso inventado.xlsx\" changed after these choices were made, so they are not applied to it. Read the file again and check the choices."
            && notChosenCheck.Problems.Where(p => p.Severity == PanelImportSeverity.Blocking).Select(p => p.Message).SequenceEqual(new[]
                { "Choose the column that holds the name.", "The column \"Pfam\" chosen for the Pfam domains is not in row 2 of sheet \"1. Proposta de famílias\". Choose the column again." }),
            string.Join(" / ", notChosenCheck.Problems.Select(p => p.Message)));
        var emptyHeader = reader.ReadTable(path, GracefulSheet, 5);
        Check("Z12p1 an empty header row is a blocking sentence rather than an exception", emptyHeader.Headers.Count == 0
            && importer.Check(emptyHeader, new FamilyPanelImport { HeaderRowNumber = 5 }, decisions).Problems.Single().Message
                == "Row 5 is empty, so it cannot hold the column names. Choose the row that does.");
        string duplicatePath = Path.Combine(workDir, "duplicate headers.xlsx");
        DuplicateHeaderWorkbook(duplicatePath);
        var duplicateHeaders = reader.ReadTable(duplicatePath, "Folha", 1);
        Check("Z12p1 a repeated header is renamed with its column letter and reported", duplicateHeaders.Headers.SequenceEqual(new[] { "Sigla", "Nome", "Nome (column C)" })
            && duplicateHeaders.Problems.Single().Message == "Column C has the same name as an earlier column, \"Nome\", so it is called \"Nome (column C)\".");

        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string portuguese = JsonSerializer.Serialize(importer.Check(reader.ReadTable(path, GracefulSheet, 2), choices, decisions, "Consenso"));
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("Z12p1 check results do not depend on display culture", portuguese == JsonSerializer.Serialize(importer.Check(reader.ReadTable(path, GracefulSheet, 2), choices, decisions, "Consenso")));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Check("Z12p1 reading, checking and replaying never change the workbook", original == TestFileHash.Upper(path));
        PanelColorNameTests();
    }

    private static void PanelColorNameTests()
    {
        var words = new[] { "#FFFFFF", "#E7E6E6", "#D9D9D9", "#808080", "#000000", "#F4B183", "#FCE4D6", "#FFF2CC", "#FFC000", "#C6EFCE", "#DDEBF7", "#1F4E78", "#8E44AD", "#FF0000", "#843C0C", "" }
            .Select(PanelColorNames.Describe).ToArray();
        Check("Z12p1 colour words describe the colour only", words.SequenceEqual(new[] { "white", "light grey", "light grey", "grey", "black", "orange",
            "light orange", "light yellow", "yellow", "light green", "light blue", "dark blue", "purple", "red", "brown", "" }), string.Join(", ", words));
    }

    private static void GracefulWorkbook(string path)
    {
        using var book = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var main = book.AddWorkbookPart(); main.Workbook = new Workbook();
        var strings = main.AddNewPart<SharedStringTablePart>();
        strings.SharedStringTable = new SharedStringTable(new SharedStringItem(new Run(new Text("Núcleo do veneno;\n") { Space = SpaceProcessingModeValues.Preserve }), new Run(new Text("linha dois"))));
        var styles = main.AddNewPart<WorkbookStylesPart>();
        static Fill Solid(ForegroundColor color) => new(new PatternFill(color) { PatternType = PatternValues.Solid });
        styles.Stylesheet = new Stylesheet(new Fonts(new Font()),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                Solid(new ForegroundColor { Rgb = "FFF4B183" }), Solid(new ForegroundColor { Theme = 4, Tint = 0.3999755851924192 }),
                Solid(new ForegroundColor { Indexed = 42 }), Solid(new ForegroundColor { Rgb = "FFD9D9D9" }), Solid(new ForegroundColor { Theme = 5 })),
            new Borders(new Border()),
            new CellFormats(new CellFormat(), new CellFormat { FillId = 2 }, new CellFormat { FillId = 3 }, new CellFormat { FillId = 4 },
                new CellFormat { FillId = 5 }, new CellFormat { FillId = 6 }));
        var theme = main.AddNewPart<ThemePart>();
        theme.Theme = new A.Theme(new A.ThemeElements(new A.ColorScheme(
            new A.Dark1Color(new A.SystemColor { Val = A.SystemColorValues.WindowText, LastColor = "000000" }),
            new A.Light1Color(new A.SystemColor { Val = A.SystemColorValues.Window, LastColor = "FFFFFF" }),
            new A.Dark2Color(new A.RgbColorModelHex { Val = "1F3864" }), new A.Light2Color(new A.RgbColorModelHex { Val = "EEEEEE" }),
            new A.Accent1Color(new A.RgbColorModelHex { Val = "5B9BD5" }), new A.Accent2Color(new A.RgbColorModelHex { Val = "8E44AD" }),
            new A.Accent3Color(new A.RgbColorModelHex { Val = "A5A5A5" }), new A.Accent4Color(new A.RgbColorModelHex { Val = "FFC000" }),
            new A.Accent5Color(new A.RgbColorModelHex { Val = "4472C4" }), new A.Accent6Color(new A.RgbColorModelHex { Val = "70AD47" }),
            new A.Hyperlink(new A.RgbColorModelHex { Val = "0563C1" }), new A.FollowedHyperlinkColor(new A.RgbColorModelHex { Val = "954F72" }))
            { Name = "Invented" })) { Name = "Invented theme" };
        var sheets = main.Workbook.AppendChild(new Sheets());
        string[] header = { "Sigla", "Nome (português)", "Nome (inglês)", "DECISÃO FamilyQuant", "Domínio(s) Pfam", "Notas e decisões" };
        var families = new SheetData(
            Row(1, ("A", "Consenso de famílias de toxinas (exemplo inventado)", 0u)),
            Row(2, header.Select((h, i) => (((char)('A' + i)).ToString(), h, 0u)).ToArray()),
            Row(3, ("A", "AChE", 1u), ("B", "Acetilcolinesterase", 0u), ("C", "Acetylcholinesterase", 0u), ("D", "Núcleo", 0u), ("E", "Carboxylesterase (PF00135)", 0u), ("G", "obs", 0u)),
            Row(4, ("A", " LAAO ", 2u), ("B", "L-aminoácido oxidase", 0u), ("C", "L-amino acid oxidase", 0u), ("D", "Padrão", 0u), ("E", "Amino_oxidase (PF01593)", 0u)),
            Row(5, ("A", "", 3u)),
            Row(6, ("A", "Enzimas raras", 0u)),
            Row(7, ("A", "HYAL", 3u), ("B", "Hialuronidase", 0u), ("C", "Hyaluronidase", 0u), ("D", "Opcional", 0u), ("E", "Glyco_hydro_56 (PF01630)", 0u)),
            Row(8, ("A", "Outras raras", 4u), ("B", "Outras raras", 0u), ("C", "Endopeptidase, sulfhydryl oxidase, carboxypeptidase, AMP (antimicrobial peptide, short), TCTP", 0u), ("D", "Opcional", 0u)),
            Row(9, ("A", "CTL", 1u), ("B", "Lectina tipo C", 0u), ("C", "C-type lectin", 0u), ("D", "Núcleo", 0u), ("E", "Lectin_C (PF00059)", 0u), ("F", "Lectinas: ver subclasses", 0u)),
            Row(10, ("A", "Gal-CTL", 0u), ("B", "Lectina ligante de galactose", 0u), ("C", "Galactose-binding lectin", 0u), ("D", "Fundir em CTL", 0u), ("E", "Lectin_C (PF00059)", 0u)),
            Row(11, ("A", "HYAL", 0u), ("B", "Hialuronidase 2", 0u), ("C", "Hyaluronidase 2", 0u), ("D", "Excluir", 0u)),
            Row(12, header.Select((h, i) => (((char)('A' + i)).ToString(), h, 0u)).ToArray()),
            Row(13, ("A", "KAZ", 5u), ("B", "Inibidor tipo Kazal", 0u), ("C", "Kazal-type inhibitor", 0u), ("E", "Kazal_1 (PF00050)", 0u)),
            Row(20, ("A", "", 1u)));
        var row3 = families.Elements<Row>().Single(r => r.RowIndex! == 3);
        row3.InsertBefore(new Cell(new CellValue("0")) { CellReference = "F3", DataType = CellValues.SharedString }, row3.Elements<Cell>().Last());
        families.Elements<Row>().Single(r => r.RowIndex! == 9).Append(new Cell(new CellValue("#N/A")) { CellReference = "G9", DataType = CellValues.Error });
        families.Elements<Row>().Single(r => r.RowIndex! == 13).Append(new Cell { CellReference = "F13", CellFormula = new CellFormula("1+1") });
        AddSheet(main, sheets, GracefulSheet, 1, new Worksheet(families, new AutoFilter { Reference = "A2:F13" },
            new MergeCells(new MergeCell { Reference = "A1:F1" }, new MergeCell { Reference = "A6:F6" }, new MergeCell { Reference = "F9:F10" })));
        AddSheet(main, sheets, "2. Legenda", 2, new Worksheet(new SheetData(Row(1, ("A", "Legenda das cores", 0u)), Row(3, ("A", "Cor", 0u), ("B", "Significado", 0u)),
            Row(4, ("A", "Laranja", 1u), ("B", "Famílias principais", 0u)), Row(5, ("A", "Cinza", 4u), ("B", "Raras", 0u)))));
        AddSheet(main, sheets, "3. Fontes", 3, new Worksheet(new SheetViews(new SheetView(new Pane { VerticalSplit = 2, TopLeftCell = "A3", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen }) { WorkbookViewId = 0 }),
            new SheetData(Row(1, ("A", "Fontes", 0u), ("B", "versão 3", 0u)), Row(2, ("A", "Referência", 0u), ("B", "Ano", 0u)), Row(3, ("A", "Damm", 0u), ("B", "2021", 0u)))));
        AddSheet(main, sheets, "Notas internas", 4, new Worksheet(new SheetData(Row(1, ("A", "rascunho", 0u)))), hidden: true);
        main.Workbook.Save();
    }

    private static void DuplicateHeaderWorkbook(string path)
    {
        using var book = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        var main = book.AddWorkbookPart(); main.Workbook = new Workbook();
        var sheets = main.Workbook.AppendChild(new Sheets());
        AddSheet(main, sheets, "Folha", 1, new Worksheet(new SheetData(Row(1, ("A", "Sigla", 0u), ("B", "Nome", 0u), ("C", "Nome", 0u)),
            Row(2, ("A", "PLA2", 0u), ("B", "Fosfolipase A2", 0u), ("C", "Phospholipase A2", 0u)))));
        main.Workbook.Save();
    }

    private static void AddSheet(WorkbookPart main, Sheets sheets, string name, uint id, Worksheet worksheet, bool hidden = false)
    {
        var part = main.AddNewPart<WorksheetPart>();
        part.Worksheet = worksheet;
        sheets.Append(new Sheet { Id = main.GetIdOfPart(part), SheetId = id, Name = name, State = hidden ? SheetStateValues.Hidden : null });
    }

    private static Row Row(uint number, params (string Column, string Text, uint Style)[] cells) => new(cells.Select(c =>
        new Cell(new InlineString(new Text(c.Text) { Space = SpaceProcessingModeValues.Preserve }))
        { CellReference = c.Column + number.ToString(CultureInfo.InvariantCulture), DataType = CellValues.InlineString, StyleIndex = c.Style == 0 ? null : c.Style }))
    { RowIndex = number };
}
