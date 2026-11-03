using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int PanelSpreadsheetLive(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("Supply workbook, panel-choice JSON, new output directory and expected row count.");
        string input = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        int expected = int.Parse(args[4], CultureInfo.InvariantCulture);
        if (Directory.Exists(output) || output.StartsWith(Path.GetDirectoryName(input)! + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use a new output directory outside the input folder.");
        var configuration = JsonSerializer.Deserialize<FamilyPanel>(File.ReadAllText(args[2]))
            ?? throw new InvalidDataException("No panel choices were supplied.");
        var choice = configuration.Import ?? throw new InvalidDataException("Choose the sheet and headers.");
        string original = TestFileHash.Upper(input);
        Directory.CreateDirectory(output);
        string copy = Path.Combine(output, Path.GetFileName(input)); File.Copy(input, copy);
        var reader = new PanelSpreadsheetReader(); var importer = new FamilyPanelSpreadsheetImporter();
        var table = reader.ReadTable(copy, choice.FamilySheet, choice.HeaderRowNumber);
        var panel = importer.Import(table, choice, configuration.DecisionMappings, configuration.Name);
        Check("J1b live: every expected workbook row is imported", panel.Families.Count == expected && table.Rows.Count == expected);
        Check("J1b live: every column and cell remains exact in the panel", panel.Families.Zip(table.Rows).All(pair =>
            pair.First.TextFields.Select(t => (t.Name, t.Text)).SequenceEqual(table.Headers.Select(h => (h, pair.Second.Cells[h])))));
        var file = new FamilyClusterFile { FamilyPanel = panel, EvidenceRecipe = new Evidence.EvidenceRecipe { FamilyPanel = FamilyPanelValidation.Clone(panel) } };
        string project = Path.Combine(output, "panel.fqproj");
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        var reopened = ProjectStore.ReadFqproj(project).fcf;
        Check("J1b live: saved project reopens with identical panel and decision choices", JsonSerializer.Serialize(panel) == JsonSerializer.Serialize(reopened.FamilyPanel));
        var replayed = importer.Replay(copy, reopened.EvidenceRecipe!.FamilyPanel!);
        Check("J1b live: recipe snapshot replays every stored choice and source value", JsonSerializer.Serialize(panel) == JsonSerializer.Serialize(replayed));
        Check("J1b live: original and read-only copy remain byte-identical", TestFileHash.Upper(input) == original && TestFileHash.Upper(copy) == original);
        File.WriteAllText(Path.Combine(output, "validation.json"), JsonSerializer.Serialize(new
        {
            Input = input, InputSha256 = original, Copy = copy, Project = project, ImportedRows = panel.Families.Count,
            ExactTextFields = panel.Families.Sum(f => f.TextFields.Count), Panel = panel,
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
