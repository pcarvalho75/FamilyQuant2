using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const string StoredWorldFastaAssertion = "Database gap stored-world project requires available suggested-additions export";

    private static int DatabaseGapCaptureCommand(string[] args)
    {
        bool negative = false; int? length = null;
        for (int index = 3; index < args.Length; index++)
            if (args[index] == "--negative-fingerprint") negative = true;
            else if (args[index] == "--minimum-length" && ++index < args.Length
                && int.TryParse(args[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
                && FamilyWorldOptions.LengthChoices.Contains(parsed)) length = parsed;
            else throw new ArgumentException("Unknown Database gap capture option or minimum length.");
        return DatabaseGapScreens(args[1], args[2], negative, length);
    }

    private static int DatabaseGapScreens(string projectPath, string output, bool oldFingerprint = false, int? selectedLength = null)
    {
        EnsureApplication();
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Screen output must be fresh.");
        Directory.CreateDirectory(output);
        string inputHash = FeatureLedgerEvidence.Hash(projectPath);
        var project = ProjectStore.ReadFqproj(projectPath);
        int length = selectedLength ?? project.manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength;
        if (oldFingerprint)
        {
            var stored = Evidence.FamilyWorldFetcher.Stored(project.fcf);
            var summary = project.manifest.FamilyWorldResult?.ForLength(length) ?? throw new InvalidDataException("No stored selected-length result to exercise.");
            // Reproduce the pre-B6 adapter's protein-list identity in memory. Input bytes stay untouched.
            summary.WorldFingerprint = FamilyWorld.Build(stored.Entries.Select(entry => new FamilyWorldProtein
            { Id = entry.Accession, Accession = entry.Accession, Sequence = entry.Sequence, Families = FamilyWorld.FamiliesOf(entry.Evidence),
                Taxon = entry.TaxonId.ToString(CultureInfo.InvariantCulture), FromPanel = entry.FromReviewedPanel }), stored.Families).Fingerprint;
            if (summary.WorldFingerprint == stored.ContentSha256) throw new InvalidDataException("Negative fixture did not change the fingerprint.");
            Console.WriteLine("Negative control planted the old protein-list fingerprint in memory: " + summary.WorldFingerprint);
        }
        var model = new DatabaseGapViewModel(project.manifest.FamilyWorldResult, project.fcf, length);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            if (project.fcf.EvidenceSource?.FamilyWorld != null)
                Check(StoredWorldFastaAssertion, model.CanExportFasta, model.FastaState);
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
                var expected = model.Summary!.Families.SelectMany(row => row.WorldProteins).ToHashSet(StringComparer.Ordinal);
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

    private static int DatabaseGapRefusalTest(string projectPath, string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Regression output must be fresh.");
        Directory.CreateDirectory(output);
        string before = FeatureLedgerEvidence.Hash(projectPath), childOutput = Path.Combine(output, "negative");
        int storedLength = ProjectStore.ReadFqproj(projectPath).manifest.FamilyWorldResult?.MinimumTagLength
            ?? throw new InvalidDataException("No stored result for refusal regression.");
        var start = new ProcessStartInfo(Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe"))
        { WorkingDirectory = Environment.CurrentDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--database-gap-screens", projectPath, childOutput, "--minimum-length",
            storedLength.ToString(CultureInfo.InvariantCulture), "--negative-fingerprint" }) start.ArgumentList.Add(argument);
        using var child = Process.Start(start)!;
        var error = child.StandardError.ReadToEndAsync(); string text = child.StandardOutput.ReadToEnd(); child.WaitForExit();
        text += error.GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(output, "negative.log"), text);
        Console.WriteLine(text);
        Check("real-project checker rejects the planted old fingerprint with its explicit availability assertion", child.ExitCode == 1
            && text.Contains("FAIL  " + StoredWorldFastaAssertion, StringComparison.Ordinal)
            && !File.Exists(Path.Combine(childOutput, "suggested_additions.fasta")));
        Check("negative-fingerprint regression leaves the project bytes unchanged", before == FeatureLedgerEvidence.Hash(projectPath));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
