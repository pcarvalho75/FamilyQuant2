using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int DatabaseGapScreens(string projectPath, string output)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Screen output must be fresh.");
        Directory.CreateDirectory(output);
        string inputHash = FeatureLedgerEvidence.Hash(projectPath);
        var project = ProjectStore.ReadFqproj(projectPath);
        var model = new DatabaseGapViewModel(project.manifest.FamilyWorldResult, project.fcf);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var control = new DatabaseGapControl(model);
                var host = new Window { Content = control, Width = 1024, Height = 1040, Left = -30000, Top = -30000,
                    ShowInTaskbar = false, ShowActivated = false, WindowStartupLocation = WindowStartupLocation.Manual };
                try
                {
                    host.Show(); host.UpdateLayout(); PumpDispatcher();
                    System.Threading.Thread.Sleep(350); PumpDispatcher();
                    ScreenHarness.Save(host, Path.Combine(output, "database-gap-" + theme + ".png"), 1024, 1040);
                }
                finally { host.Close(); PumpDispatcher(); }
            }
            if (model.HasData) File.WriteAllText(Path.Combine(output, "database_gap.csv"), model.BuildCsv());
            if (model.CanExportFasta)
            {
                string fastaPath = Path.Combine(output, "suggested_additions.fasta");
                File.WriteAllText(fastaPath, model.BuildSuggestedFasta());
                var stored = Evidence.FamilyWorldFetcher.Stored(project.fcf);
                var sequences = stored.Entries.ToDictionary(entry => entry.Accession, entry => entry.Sequence, StringComparer.Ordinal);
                var expected = project.manifest.FamilyWorldResult!.Families.SelectMany(row => row.WorldProteins).ToHashSet(StringComparer.Ordinal);
                var actual = new Dictionary<string, StringBuilder>(StringComparer.Ordinal); string? accession = null;
                bool validHeaders = true;
                foreach (string line in File.ReadAllLines(fastaPath))
                    if (line.StartsWith('>'))
                    {
                        accession = line[1..].Split(' ')[0]; actual.Add(accession, new StringBuilder());
                        validHeaders &= line.EndsWith("world_sha256=" + stored.ContentSha256, StringComparison.Ordinal);
                    }
                    else if (accession != null) actual[accession].Append(line);
                    else throw new InvalidDataException("FASTA sequence preceded its header.");
                Check("real-project FASTA contains exactly the recorded hits and exact verified snapshot sequences", validHeaders
                    && expected.SetEquals(actual.Keys) && actual.All(pair => sequences.TryGetValue(pair.Key, out string? sequence)
                        && pair.Value.ToString() == sequence));
            }
            var printed = new List<string> { model.Rules, model.Basis, model.ValidationRule, model.Scope, model.Provenance };
            printed.AddRange(model.GlobalRows.Select(row => row.Label + ": " + row.Envelope.ToString("G6", CultureInfo.CurrentCulture)));
            printed.Add("Family | identified | database de novo | family-world de novo | decoy rate | right rate (true/credited)");
            printed.AddRange(model.Rows.Select(row => row.FamilyLabel + " | " + row.Data.IdentifiedEnvelope.ToString("G6", CultureInfo.CurrentCulture)
                + " | " + row.Data.DatabaseDeNovoEnvelope.ToString("G6", CultureInfo.CurrentCulture) + " | "
                + row.Data.WorldDeNovoEnvelope.ToString("G6", CultureInfo.CurrentCulture) + " | " + row.DecoyRate + " | " + row.RightRate));
            File.WriteAllLines(Path.Combine(output, "printed-values.txt"), printed);
            File.WriteAllText(Path.Combine(output, "help.txt"), DatabaseGapHelp.Text);
            Check("Database gap capture leaves the supplied project bytes unchanged", inputHash == FeatureLedgerEvidence.Hash(projectPath));
            Console.WriteLine("Database gap: " + model.Rows.Count + " stored family rows; FASTA available: " + model.CanExportFasta
                + "; " + model.FastaState + "; " + Path.GetFullPath(projectPath));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
