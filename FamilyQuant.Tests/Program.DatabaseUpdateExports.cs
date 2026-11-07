using FamilyQuant.DatabaseUpdate;
using FamilyQuant.Report;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void DatabaseUpdateExportTests()
    {
        Section("Database update Help, recorded Methods and change CSV");
        var rows = new List<DatabaseUpdateRow>
        {
            new() { Kind = DatabaseUpdateKind.Added, NewIndex = 0, NewIdentifier = "invented_new", FamilyUserId = 0, FamilySearchId = 12,
                FamilyName = "Invented, quoted \"family\"", Placement = DatabaseUpdatePlacementKind.NearestKeptNeighbour, PlacedBy = "invented_kept",
                PlacedByIdentityPercent = 87.125, PlacedByAlignmentLength = 91, JoinedLockedFamily = true, OtherLinkedFamilies = new() { 2, 5 }, Note = "=supplied note" },
            new() { Kind = DatabaseUpdateKind.Removed, OldIndex = 1, OldIdentifier = "invented_removed", Note = "Supplied note, with\na new line" },
            new() { Kind = DatabaseUpdateKind.Ambiguous, Step = DatabaseUpdateStep.SameSequence, Picked = true, AmbiguousCase = 3,
                OldIndex = 2, NewIndex = 4, OldIdentifier = "invented_old", NewIdentifier = "invented_choice", Decoy = true, Contaminant = true }
        };
        string original = JsonSerializer.Serialize(rows), baseline = DatabaseUpdateCsv.Build(rows);
        var culture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string name in new[] { "en-US", "pt-BR", "fr-FR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
                Check(name + " change CSV is invariant and does not mutate the rows", DatabaseUpdateCsv.Build(rows) == baseline && JsonSerializer.Serialize(rows) == original);
            }
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using var parser = new TextFieldParser(new StringReader(baseline)) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        string[] headers = parser.ReadFields()!, added = parser.ReadFields()!, removed = parser.ReadFields()!, picked = parser.ReadFields()!;
        Check("CSV retains zero IDs, nullable sides, exact identity and placement metadata", headers.Length == 20 && added.Length == 20
            && added[10] == "0" && added[15] == "87.125" && added[16] == "91" && added[17] == "true" && added[18] == "2;5"
            && removed[7].Length == 0 && removed[11].Length == 0 && picked[2] == "true" && picked[3] == "3" && picked[4] == "true" && picked[5] == "true");
        Check("CSV quotes user text and guards spreadsheet formulas", added[12] == rows[0].FamilyName && added[19] == "'=supplied note"
            && removed[19] == rows[1].Note);
        string path = Path.Combine(Path.GetTempPath(), "FamilyQuant-update-" + Guid.NewGuid().ToString("N") + ".csv");
        try
        {
            DatabaseUpdateCsv.Write(path, rows);
            byte[] bytes = File.ReadAllBytes(path);
            Check("CSV file is the same UTF-8 export without a byte-order mark", File.ReadAllText(path) == baseline && bytes[0] == (byte)'K');
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        var file = new FamilyClusterFile();
        var plain = ReportMethods.Build(file, new QuantManifest());
        Check("projects without an update keep their Methods free of update claims", plain.Blocks.All(b => b.Id != "methods-database-updates"));
        file.DatabaseUpdates = new List<DatabaseUpdateRecord> { new() { Method = "Recorded fixture comparison and placement", UpdatedAtUtc = DateTimeOffset.UnixEpoch,
            Previous = new() { FileName = "old.fasta", Sha256 = "old-hash" }, New = new() { FileName = "new.fasta", Sha256 = "new-hash" },
            Counts = new() { Added = 3, Removed = 2, SequenceChanged = 1, NamesKept = 4 }, Rows = rows } };
        string before = file.ToJsonText();
        var methods = ReportMethods.Build(file, new QuantManifest());
        var table = methods.Blocks.OfType<ReportTable>().Single(b => b.Id == "methods-database-updates");
        Check("Methods prints the recorded method, source hashes and counts without recomputing", table.Rows.Count == 1
            && table.Rows[0][2] == "old-hash" && table.Rows[0][4] == "new-hash" && table.Rows[0][5] == file.DatabaseUpdates[0].Method
            && table.Rows[0].Skip(6).SequenceEqual(new[] { "3", "2", "1", "4" }) && file.ToJsonText() == before);
        string help = DatabaseUpdateHelp.Text;
        Check("database Help covers save, preview, cancellation, name choices and stale results", help.Contains("File > Update database", StringComparison.Ordinal)
            && help.Contains("Keep the old name", StringComparison.Ordinal) && help.Contains("Cancel", StringComparison.Ordinal)
            && help.Contains("Save as keeps both", StringComparison.Ordinal) && help.Contains("Undo steps are cleared", StringComparison.Ordinal)
            && help.Contains("Quantify again", StringComparison.Ordinal) && help.Contains("Search PatternLab again", StringComparison.Ordinal)
            && HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique).Contains(help, StringComparison.Ordinal));
    }
}
