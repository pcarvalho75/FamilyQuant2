using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReportInputCommandTable() =>
    [new("--test-report-inputs", 428, TestCommandPhase.Raw,
        args => args.Contains("--test-report-inputs", StringComparer.Ordinal), (_, _) => RunReportInputTests()),
     new("--capture-report-inputs", 429, TestCommandPhase.Raw,
        args => args.Length == 5 && args[0] == "--capture-report-inputs", (args, _) => CaptureReportInputs(args))];

    private static int CaptureReportInputs(string[] args)
    {
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new IOException("Use a fresh provenance output directory.");
        string before = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[3])));
        var facts = ReportInputCapture.Read(args[1], args[2], args[3]);
        Directory.CreateDirectory(output);
        var file = new FamilyClusterFile { ReportInputProvenance = facts };
        string project = Path.Combine(output, "input-provenance.fqproj");
        var manifest = new QuantManifest { ScientificInputFingerprints = new() { [Path.GetFullPath(args[2])] = FileFingerprint.Sha256(args[2]) } };
        ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), manifest);
        string json = JsonSerializer.Serialize(facts, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "input-provenance.json"), json);
        var reopened = ProjectStore.ReadFqproj(project).fcf.ReportInputProvenance;
        Check("native provenance capture preserves the source parameter file", before == facts.SearchParameterSha256
            && before == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[3]))));
        Check("native stored provenance reopens with every parsed setting", JsonSerializer.Serialize(reopened) == JsonSerializer.Serialize(facts));
        Check("native capture stores the chosen database file's own digest", reopened?.DatabaseFileSha256 == FileFingerprint.Sha256(args[1]));
        Check("native capture stores the package digest in the quant manifest", ProjectStore.ReadFqproj(project).manifest.ScientificInputFingerprints[Path.GetFullPath(args[2])] == FileFingerprint.Sha256(args[2]));
        Console.WriteLine("Metadata-only project: " + facts.SearchParameters.Count.ToString(CultureInfo.InvariantCulture) + " parsed settings; no Results or RAW computation: " + output);
        return failed == 0 ? 0 : 1;
    }

    private static int RunReportInputTests()
    {
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.ReportInputs.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string sepro = Path.Combine(folder, "search.sepr2"), parameters = Path.Combine(folder, "search.params"), database = Path.Combine(folder, "database.fcf");
        var databaseFile = new FamilyClusterFile { SourceSha256 = new string('b', 64) };
        databaseFile.Save(database);
        File.WriteAllText(sepro, "Synthetic input placeholder");
        string text = "# Search settings\r\nmass_tolerance = 12.5 # a comment\r\nvariable_mod = 1.234 M\r\nvariable_mod = 2.345 Q\r\npath = data.fasta\r\n[ENZYME_INFO]\r\n0. name 0 - -\r\n";
        File.WriteAllText(parameters, text, new UTF8Encoding(true));
        var facts = ReportInputCapture.Read(database, sepro);
        Check("input provenance uses the chosen database and package basenames", facts.DatabaseFileName == "database.fcf" && facts.SeproFileName == "search.sepr2");
        Check("the chosen database file has its own SHA256 distinct from its original FASTA hash", facts.DatabaseFileSha256 == FileFingerprint.Sha256(database)
            && facts.DatabaseFileSha256 != databaseFile.SourceSha256);
        var envelope = ShellProjectOperations.Read(database);
        var envelopeHash = typeof(ShellProjectContent).GetProperty("DatabaseFileSha256");
        Check("the read envelope captures the digest without changing curation", envelopeHash?.GetValue(envelope) as string == FileFingerprint.Sha256(database)
            && envelope.Curation.ToJsonText() == databaseFile.ToJsonText());
        Check("the adjacent parameter file is captured with its exact original-byte SHA256", facts.SearchParameterFileName == "search.params"
            && facts.SearchParameterSha256 == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(parameters))));
        Check("key-value search settings preserve order, decimal text and duplicate keys", facts.SearchParameters.SequenceEqual(new[]
        {
            new ReportSearchParameter("mass_tolerance", "12.5"), new ReportSearchParameter("variable_mod", "1.234 M"),
            new ReportSearchParameter("variable_mod", "2.345 Q"), new ReportSearchParameter("path", "data.fasta")
        }));
        string baseline = JsonSerializer.Serialize(facts);
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "pt-BR", "en-US" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                Check("source facts and parsed values are identical under " + culture, JsonSerializer.Serialize(ReportInputCapture.Read(database, sepro)) == baseline);
            }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        File.WriteAllText(Path.Combine(folder, "second.params"), "another = value");
        Check("ambiguous adjacent parameter files are not guessed", ReportInputCapture.Read(null, sepro).SearchParameterSha256 == "");
        Check("an explicit parameter file resolves ambiguity", ReportInputCapture.Read(null, sepro, parameters).SearchParameterSha256 == facts.SearchParameterSha256);
        var slot = typeof(FamilyClusterFile).GetProperty("ReportInputProvenance");
        Check("read-only input facts have a project slot", slot?.PropertyType == typeof(ReportInputProvenance));
        if (slot != null)
        {
            var file = new FamilyClusterFile();
            string original = file.ToJsonText();
            Check("absent input facts do not change old curation", !original.Contains("ReportInputProvenance", StringComparison.Ordinal));
            slot.SetValue(file, facts);
            string project = Path.Combine(folder, "inputs.fqproj");
            ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
            File.Delete(sepro); File.Delete(parameters); File.Delete(Path.Combine(folder, "second.params"));
            var loaded = ProjectStore.ReadFqproj(project);
            Check("all input provenance survives project reopening without source files", JsonSerializer.Serialize(slot.GetValue(loaded.fcf)) == baseline);
        }
        Check("old projects state which input facts are missing", ReportInputCapture.Describe(null).Contains("Database file: Not recorded", StringComparison.Ordinal)
            && ReportInputCapture.Describe(null).Contains("Identification package: Not recorded", StringComparison.Ordinal)
            && ReportInputCapture.Describe(null).Contains("Search parameter file: Not recorded", StringComparison.Ordinal));
        ReportInputShellTests(folder);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static void ReportInputShellTests(string folder)
    {
        EnsureApplication();
        string input = Path.Combine(folder, "native-source.fcf"), package = Path.Combine(folder, "native.sepr2"), parameters = Path.Combine(folder, "native.params");
        new FamilyClusterFile { SourceSha256 = new string('b', 64) }.Save(input);
        string openedHash = FileFingerprint.Sha256(input);
        File.WriteAllText(package, "Synthetic package; injected compute does not parse it.");
        File.WriteAllText(parameters, "mass_tolerance = 12.5\n");
        var oldFactory = MainWindow.QuantifyViewModelFactoryOverride;
        var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        var shell = new MainWindow();
        try
        {
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            Check("opening a family file preserves its exact curation", shell.OpenProjectPath(input)
                && shell.NavigationModel.Document?.ToJsonText() == FamilyClusterFile.Load(input).ToJsonText());
            File.AppendAllText(input, "\n"); // A later disk change must not replace the bytes actually opened.
            MainWindow.SelectSaveProjectPathOverride = () => Path.Combine(package, "cannot-save.fqproj");
            var save = (Task)typeof(MainWindow).GetMethod("SaveProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(shell, new object[] { false })!;
            FinishBusyTask(save); save.GetAwaiter().GetResult();
            Check("failed Save does not attach partially prepared input facts", shell.NavigationModel.Document?.ReportInputProvenance == null);
            string beforeCompute = Path.Combine(folder, "before-compute.fqproj");
            MainWindow.SelectSaveProjectPathOverride = () => beforeCompute;
            save = (Task)typeof(MainWindow).GetMethod("SaveProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(shell, new object[] { false })!;
            FinishBusyTask(save); save.GetAwaiter().GetResult();
            Check("Save retains the chosen-file digest before any Results exist", ProjectStore.ReadFqproj(beforeCompute).fcf.ReportInputProvenance?.DatabaseFileSha256 == openedHash);
            MainWindow.QuantifyViewModelFactoryOverride = (manifest, curation) => new ComputeXicViewModel("fixture", manifest, curation,
                compute: (_, _, _) => Task.FromResult(new FullXicComputeResult { Rows = new(), Manifest = new()
                { ScientificInputFingerprints = new() { [Path.GetFullPath(package)] = FileFingerprint.Sha256(package) } } }))
                { RawDirectory = folder, SeproPackage = package, SearchParametersPath = parameters };
            MainWindow.NavigateShellCommand.Execute(ShellDestination.Quantify, shell);
            var screen = ((System.Windows.Controls.Grid)shell.FindName("GridApp")).Children.OfType<QuantifyControl>().Single();
            screen.ViewModel.ComputeAsync().GetAwaiter().GetResult();
            var facts = shell.NavigationModel.Document!.ReportInputProvenance;
            Check("successful native Quantify attaches the exact staged source facts", shell.DocumentState.IsModified && facts?.SeproFileName == "native.sepr2"
                && facts.DatabaseFileName == "native-source.fcf"
                && facts.DatabaseFileSha256 == openedHash && facts.DatabaseFileSha256 != shell.NavigationModel.Document.SourceSha256
                && facts.SearchParameters.SequenceEqual(new[] { new ReportSearchParameter("mass_tolerance", "12.5") }));
            string computed = Path.Combine(folder, "computed.fqproj");
            ProjectStore.WriteFqproj(computed, shell.NavigationModel.Document, screen.ViewModel.Result!.Rows, screen.ViewModel.Result.Manifest);
            Check("the package SHA256 remains stored in ScientificInputFingerprints on reopen", ProjectStore.ReadFqproj(computed).manifest.ScientificInputFingerprints[Path.GetFullPath(package)] == FileFingerprint.Sha256(package));
            string before = JsonSerializer.Serialize(facts);
            MainWindow.NavigateShellCommand.Execute(ShellDestination.Families, shell);
            MainWindow.QuantifyViewModelFactoryOverride = (manifest, curation) => new ComputeXicViewModel("fixture", manifest, curation,
                compute: (_, _, _) => Task.FromException<FullXicComputeResult>(new IOException("Synthetic computation failure")))
                { RawDirectory = folder, SeproPackage = package, SearchParametersPath = parameters };
            File.WriteAllText(parameters, "mass_tolerance = 99\n");
            MainWindow.NavigateShellCommand.Execute(ShellDestination.Quantify, shell);
            screen = ((System.Windows.Controls.Grid)shell.FindName("GridApp")).Children.OfType<QuantifyControl>().Single();
            screen.ViewModel.ComputeAsync().GetAwaiter().GetResult();
            Check("a failed native computation keeps previous source facts", JsonSerializer.Serialize(shell.NavigationModel.Document.ReportInputProvenance) == before);
        }
        finally
        {
            shell.Close(); MainWindow.QuantifyViewModelFactoryOverride = oldFactory; MainWindow.UnsavedProjectChoiceOverride = oldChoice; MainWindow.SelectSaveProjectPathOverride = oldSave;
        }
    }
}
