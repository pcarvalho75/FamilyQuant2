using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using FamilyQuant.Report;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using XL = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private const long CurationStringLimit = 1_073_741_791;
    private const string CurationProofScratch = "D:/fq-tmp/proof";

    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationStringLimitProofCommands() =>
    [new("--proof-curation-past-string-limit", 21115, TestCommandPhase.Raw,
        args => args is ["--proof-curation-past-string-limit"] or ["--proof-curation-past-string-limit", "--small"],
        (args, _) => RunCurationStringLimitProof(args.Length == 2))];

    // Never called by DefaultSuite. --small follows the same native workflow with eight short
    // reference entries for driver debugging; it cannot claim proof past the string limit.
    private static int RunCurationStringLimitProof(bool small)
    {
        string root = Path.GetFullPath(Path.Combine(CurationProofScratch, "run-" + Guid.NewGuid().ToString("N")));
        string? previousTemp = Environment.GetEnvironmentVariable("TEMP"), previousTmp = Environment.GetEnvironmentVariable("TMP");
        var isolation = typeof(ApplicationPaths).GetProperty("LocalDataDirectoryOverride", BindingFlags.Public | BindingFlags.Static);
        if (isolation?.CanWrite != true)
        {
            Console.WriteLine("FAIL Z12p5f4: CONTRACT Z12p5f0 LocalDataDirectoryOverride is required before native proof execution.");
            return 1;
        }
        object? previousData = isolation.GetValue(null);
        string? previousSettings = MainWindow.ShellSettingsPathOverride;
        var previousMessages = ShellMessages.Presenter;
        var previousChoice = MainWindow.UnsavedProjectChoiceOverride;
        var previousReportPath = MainWindow.SelectReportPathOverride;
        var previousReportPresenter = MainWindow.ReportExportPresenterOverride;
        var previousReportFactory = MainWindow.ReportDocumentFactoryOverride;
        var previousReportStager = MainWindow.ReportFileStagerOverride;
        var previousContext = SynchronizationContext.Current;
        MainWindow? shell = null;
        try
        {
            Directory.CreateDirectory(root);
            string temp = Path.Combine(root, "temp"); Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("TEMP", temp); Environment.SetEnvironmentVariable("TMP", temp);
            isolation.SetValue(null, Path.Combine(root, "app-data"));
            RequireCurationProof(Path.GetFullPath(ApplicationPaths.LocalDataDirectory).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
                "native application data is isolated below the owned run directory");
            MainWindow.ShellSettingsPathOverride = Path.Combine(root, "shell.json");
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            ShellMessages.Presenter = (_, message) => { Console.WriteLine("Native message: " + message.Text); return MessageBoxResult.OK; };
            Console.WriteLine(small ? "SMALL DRIVER SMOKE ONLY - does not prove the string limit" : "Z12p5f4 synthetic proof past the string limit");
            Console.WriteLine("All inputs are synthetic. Scratch: " + root);
            Console.WriteLine("Private-memory peaks are sampled every 20 ms per stage, in bytes; times use Stopwatch.");
            int entries = small ? 8 : 16_384, sequenceLength = small ? 128 : 65_536;
            string original = Path.Combine(root, "synthetic.fcf");
            long characters = MeasureCurationProof("generate-streamed-json", () => GenerateCurationProof(original, entries, sequenceLength));
            if (!small)
            {
                RequireCurationProof(characters > CurationStringLimit, "generated JSON exceeds 1,073,741,791 characters");
                RequireCurationProof((long)entries * sequenceLength > CurationStringLimit,
                    "reference sequence characters alone exceed the limit, independent of indentation");
            }
            Console.WriteLine(FormattableString.Invariant($"JSON characters={characters}; UTF-8 bytes={new FileInfo(original).Length}; reference entries={entries}; sequence length={sequenceLength}"));

            MeasureCurationProof("fcf-open", () =>
            {
                var loaded = FamilyClusterFile.Load(original);
                VerifyCurationProof(loaded, entries, sequenceLength, imported: false, calculated: false);
            });
            EnsureApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            shell = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                Width = 1024, Height = 768, WindowStartupLocation = WindowStartupLocation.Manual };
            shell.Show(); PumpDispatcher();
            MeasureCurationProof("shell-fcf-open", () =>
            {
                RequireCurationProof(shell.OpenProjectPath(original), "native shell opens the generated family file");
                AwaitCurationProof(CurationProofControl(shell).FullXicReady);
            });
            MeasureCurationProof("wizard-family-list-import", () => ImportCurationProof(CurationProofControl(shell).ViewModel, root));
            MeasureCurationProof("recalculate-family-shares", () => RecalculateCurationProof(CurationProofControl(shell), root));

            string project = Path.Combine(root, "synthetic.fqproj");
            MeasureCurationProof("prepare-stored-results", () =>
            {
                var document = CurationProofControl(shell).fcf;
                // Supplied synthetic assignment facts are a report prerequisite. This storage proof
                // makes no claim about automatic family inference; the real accessor checks currency.
                document.PanelProteinAssignments = document.MyExtendedFastaItems.Select((protein, index) => new PanelProteinAssignment
                {
                    ProteinIndex = index, Identifier = protein.SequenceIdentifier, FamilyUserId = protein.UserID,
                    IsApplied = true, PanelFamilyKey = index == 0 ? "SYN_A" : "SYN_B"
                }).ToList();
                MakePanelAssignmentsCurrent(document, new EvidenceClassificationOptions());
                var rows = new[] { Qr("identified", "synthetic-run", 1, 2, "ACDEFGHIK", 100, 100),
                    Qr("identified", "synthetic-run", 2, 2, "LMNPQRSTV", 300, 300) };
                var manifest = new QuantManifest { PerRunTic = new() { ["synthetic-run"] = 1000 }, FamilyMapping = new() { MinIdentity = .99 } };
                ProjectStore.WriteFqproj(project, document, rows, manifest);
                RequireCurationProof(shell.OpenProjectPath(project), "native shell opens the synthetic stored Results");
                AwaitCurationProof(CurationProofControl(shell).FullXicReady);
            });
            MeasureCurationProof("native-report-export", () =>
            {
                string report = Path.Combine(root, "synthetic-report.docx");
                MainWindow.SelectReportPathOverride = () => report;
                MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                // Keep the production snapshot, report builder, Word writer, workbook and HTML writer.
                MainWindow.ReportDocumentFactoryOverride = null; MainWindow.ReportFileStagerOverride = null;
                RequireCurationProof(AwaitCurationProof(shell.ExportReportAsync()), "native report export succeeds");
                RequireCurationProof(new FileInfo(report).Length > 0 && new FileInfo(ReportSupplementWorkbook.PathForReport(report)).Length > 0,
                    "native report and companion workbook were published");
            });
            string saved = Path.Combine(root, "saved.fcf");
            string expected = MeasureCurationProof("streamed-content-fingerprint", () => CurationProofControl(shell).fcf.JsonContentSha256());
            MeasureCurationProof("fcf-save", () => CurationProofControl(shell).fcf.Save(saved));
            MeasureCurationProof("fcf-reopen-and-verify", () =>
            {
                var reopened = FamilyClusterFile.Load(saved);
                VerifyCurationProof(reopened, entries, sequenceLength, imported: true, calculated: true);
                RequireCurationProof(reopened.JsonContentSha256() == expected, "saved and reopened complete curation has the same streamed SHA-256");
            });
            Console.WriteLine(small ? "PASS small native workflow smoke; large proof NOT RUN" : "PASS Z12p5f4 proof past the string limit");
            return 0;
        }
        catch (Exception error)
        {
            Console.WriteLine("FAIL Z12p5f4 " + error);
            return 1;
        }
        finally
        {
            try
            {
                if (shell != null) { shell.Close(); PumpDispatcher(); }
            }
            finally
            {
                MainWindow.ShellSettingsPathOverride = previousSettings;
                MainWindow.UnsavedProjectChoiceOverride = previousChoice; ShellMessages.Presenter = previousMessages;
                MainWindow.SelectReportPathOverride = previousReportPath; MainWindow.ReportExportPresenterOverride = previousReportPresenter;
                MainWindow.ReportDocumentFactoryOverride = previousReportFactory; MainWindow.ReportFileStagerOverride = previousReportStager;
                SynchronizationContext.SetSynchronizationContext(previousContext);
                isolation.SetValue(null, previousData);
                Environment.SetEnvironmentVariable("TEMP", previousTemp); Environment.SetEnvironmentVariable("TMP", previousTmp);
                string allowed = Path.GetFullPath(CurationProofScratch).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!root.StartsWith(allowed, StringComparison.OrdinalIgnoreCase)) throw new IOException("Refusing cleanup outside the proof scratch root.");
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
                Console.WriteLine("CLEANUP removed owned proof run: " + !Directory.Exists(root));
            }
        }
    }

    private static long GenerateCurationProof(string path, int entries, int sequenceLength)
    {
        var seed = new FamilyClusterFile
        {
            SourceSequenceCount = 2, EvidenceSource = new() { Provider = "Synthetic", Release = "synthetic-proof" },
            ReportDetails = new() { Title = "Synthetic storage proof", Authors = "Synthetic fixture", DatabaseKind = "Synthetic database" },
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "SYNTHETIC_A", Sequence = "TTACDEFGHIKGG", Description = "Synthetic alpha", UserID = 1, USearchID = 1,
                    Evidence = new() { Accession = "SYNTHETIC_A", Reviewed = true, Family = "Synthetic alpha" } },
                new() { SequenceIdentifier = "SYNTHETIC_B", Sequence = "TTLMNPQRSTVGG", Description = "Synthetic beta", UserID = 2, USearchID = 2,
                    Evidence = new() { Accession = "SYNTHETIC_B", Reviewed = true, Family = "Synthetic beta" } }
            }
        };
        // Only this bounded two-protein seed is materialized. Reference entries are generated one at
        // a time, flushing the writer after each entry to bound its internal buffer too.
        var properties = JsonSerializer.SerializeToElement(seed, FamilyClusterFile.CurationJsonOptions);
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024))
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = FamilyClusterFile.CurationJsonOptions.WriteIndented }))
        {
            writer.WriteStartObject();
            foreach (var property in properties.EnumerateObject())
            {
                if (property.Name != nameof(FamilyClusterFile.EvidenceSource)) { property.WriteTo(writer); continue; }
                writer.WritePropertyName(property.Name); writer.WriteStartObject();
                writer.WriteString("Provider", "Synthetic"); writer.WriteString("Release", "synthetic-proof");
                writer.WritePropertyName("ReferencePanel"); writer.WriteStartObject();
                writer.WriteString("Provider", "Synthetic"); writer.WriteString("Release", "synthetic-proof");
                writer.WritePropertyName("Families"); writer.WriteStartArray(); writer.WriteStringValue("Synthetic alpha"); writer.WriteStringValue("Synthetic beta"); writer.WriteEndArray();
                writer.WritePropertyName("Entries"); writer.WriteStartArray();
                string sequence = new('A', sequenceLength);
                for (int index = 0; index < entries; index++)
                {
                    string accession = "SYNTHETIC_REF_" + index.ToString("D5", CultureInfo.InvariantCulture);
                    JsonSerializer.Serialize(writer, new PanelEntry { Accession = accession, Sequence = sequence,
                        Evidence = new() { Accession = accession, Reviewed = true, Family = index % 2 == 0 ? "Synthetic alpha" : "Synthetic beta" } },
                        FamilyClusterFile.CurationJsonOptions);
                    writer.Flush();
                }
                writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndObject();
            }
            writer.WriteEndObject();
        }
        long characters = 0;
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 64 * 1024);
        char[] buffer = new char[64 * 1024];
        for (int count; (count = reader.Read(buffer, 0, buffer.Length)) != 0;) characters += count;
        return characters;
    }

    private static void VerifyCurationProof(FamilyClusterFile file, int entries, int sequenceLength, bool imported, bool calculated)
    {
        RequireCurationProof(file.MyExtendedFastaItems.Count == 2 && file.GroupedFastaItems.Count == 2, "two synthetic proteins and families survive");
        var panel = file.EvidenceSource?.ReferencePanel ?? throw new InvalidDataException("Synthetic reference panel was lost.");
        RequireCurationProof(panel.Entries.Count == entries && panel.Families.SequenceEqual(new[] { "Synthetic alpha", "Synthetic beta" }), "reference count and families survive");
        for (int index = 0; index < entries; index++)
        {
            var entry = panel.Entries[index];
            if (entry.Accession != "SYNTHETIC_REF_" + index.ToString("D5", CultureInfo.InvariantCulture)
                || entry.Sequence.Length != sequenceLength || entry.Sequence.AsSpan().IndexOfAnyExcept('A') >= 0
                || entry.Evidence.Accession != entry.Accession || entry.Evidence.Reviewed != true
                || entry.Evidence.Family != (index % 2 == 0 ? "Synthetic alpha" : "Synthetic beta"))
                throw new InvalidDataException("Synthetic reference entry differs at index " + index.ToString(CultureInfo.InvariantCulture));
        }
        if (imported) RequireCurationProof(file.FamilyPanel?.Families.Select(family => family.Key).SequenceEqual(new[] { "SYN_A", "SYN_B" }) == true,
            "imported synthetic family list survives");
        if (calculated) RequireCurationProof(Math.Abs(file.MyExtendedFastaItems[0].XICUser - .25) < 1e-12
            && Math.Abs(file.MyExtendedFastaItems[1].XICUser - .75) < 1e-12, "recalculated 25% and 75% shares survive");
    }

    private static void ImportCurationProof(CurationViewModel curation, string root)
    {
        string path = Path.Combine(root, "synthetic-family-list.xlsx");
        using (var workbook = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook))
        {
            var book = workbook.AddWorkbookPart(); book.Workbook = new XL.Workbook();
            var sheet = book.AddNewPart<WorksheetPart>(); var data = new XL.SheetData(); sheet.Worksheet = new XL.Worksheet(data);
            book.Workbook.Append(new XL.Sheets(new XL.Sheet { Id = book.GetIdOfPart(sheet), Name = "Families", SheetId = 1 }));
            string[][] rows = [["Abbreviation", "Name"], ["SYN_A", "Synthetic alpha"], ["SYN_B", "Synthetic beta"]];
            foreach (var values in rows)
            {
                var row = new XL.Row();
                foreach (string value in values) row.Append(new XL.Cell { DataType = XL.CellValues.InlineString, InlineString = new XL.InlineString(new XL.Text(value)) });
                data.Append(row);
            }
            sheet.Worksheet.Save(); book.Workbook.Save();
        }
        using var wizard = new FamilyListImportViewModel(new FamilyListImportTransaction(curation), matcher: new FamilyListStoredEvidenceMatches(curation.Document));
        AwaitCurationProof(wizard.LoadAsync(path));
        for (int expectedPage = 2; expectedPage <= 4; expectedPage++)
        {
            RequireCurationProof(AwaitCurationProof(wizard.NextAsync()) && wizard.Page == expectedPage,
                "wizard reaches page " + expectedPage.ToString(CultureInfo.InvariantCulture) + ": " + wizard.Status);
        }
        RequireCurationProof(wizard.MatchFamilies.Count == 2 && wizard.MatchFamilies.All(family => family.Suggestions.Count > 0),
            "actual stored-evidence adapter supplies both synthetic suggestions");
        wizard.SkipAllMatches();
        RequireCurationProof(AwaitCurationProof(wizard.NextAsync()) && wizard.Page == 5, "wizard reaches import summary");
        bool committed = AwaitCurationProof(wizard.NextAsync());
        RequireCurationProof(committed && wizard.Imported && wizard.Page == 6 && wizard.CanUndo && curation.UndoStack.Count == 1,
            FormattableString.Invariant($"wizard transaction commits one undoable family-list import: result={committed}, imported={wizard.Imported}, page={wizard.Page}, canUndo={wizard.CanUndo}, undoCount={curation.UndoStack.Count}, status={wizard.Status}"));
    }

    private static void RecalculateCurationProof(ClusterGenerationControl control, string root)
    {
        string path = Path.Combine(root, "synthetic.plp");
        File.WriteAllText(path, "###Description : Synthetic proof\n###SparseMatrix\n#ClassDescription\t1\tSynthetic\n"
            + "#D:\\fq-tmp\\proof\\synthetic-run.sepr2\n1 0:100 1:300\n###Index\n0\tACDEFGHIK\tSYNTHETIC_A\n1\tLMNPQRSTV\tSYNTHETIC_B\n");
        control.ViewModel.Quantitation.CurrentPLP = new PatternLabProject(new FileInfo(path));
        foreach (var item in control.fcf.MyExtendedFastaItems) item.XICUser = -1;
        var panel = (CurationQuantitationPanel)control.FindName("QuantitationPanel");
        ((ComboBox)panel.FindName("ComboBoxNormalization")).SelectedIndex = (int)NormalizationMode.IdentifiedSignal;
        ((ComboBox)panel.FindName("ComboBoxSharedPeptides")).SelectedIndex = (int)SharedPeptidePolicy.FamilyUnique;
        ((Button)panel.FindName("ButtonRecalculateQuantitation")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        AwaitCurationProof(Task.Run(async () => { while (control.Dispatcher.Invoke(() => control.Operations.IsBusy)) await Task.Delay(20); }));
        RequireCurationProof(Math.Abs(control.fcf.MyExtendedFastaItems[0].XICUser - .25) < 1e-12
            && Math.Abs(control.fcf.MyExtendedFastaItems[1].XICUser - .75) < 1e-12, "native Recalculate publishes expected 25% and 75% family shares");
    }

    private static ClusterGenerationControl CurationProofControl(MainWindow shell) =>
        ((Grid)shell.FindName("GridApp")).Children.OfType<ClusterGenerationControl>().Single();

    private static void AwaitCurationProof(Task task)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (watch.Elapsed > TimeSpan.FromMinutes(8)) throw new TimeoutException("Synthetic native stage exceeded eight minutes.");
            PumpDispatcher(); Thread.Sleep(10);
        }
        task.GetAwaiter().GetResult(); PumpDispatcher();
    }
    private static T AwaitCurationProof<T>(Task<T> task) { AwaitCurationProof((Task)task); return task.GetAwaiter().GetResult(); }
    private static void RequireCurationProof(bool value, string claim)
    { if (!value) throw new InvalidDataException(claim); Console.WriteLine("  PASS " + claim); }

    private static void MeasureCurationProof(string name, Action action) => MeasureCurationProof(name, () => { action(); return true; });
    private static T MeasureCurationProof<T>(string name, Func<T> action)
    {
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        using var process = Process.GetCurrentProcess();
        long peak = process.PrivateMemorySize64;
        void Sample()
        {
            process.Refresh(); long current = process.PrivateMemorySize64;
            long previous;
            do { previous = Interlocked.Read(ref peak); if (current <= previous) return; }
            while (Interlocked.CompareExchange(ref peak, current, previous) != previous);
        }
        using var timer = new Timer(_ => Sample(), null, 0, 20);
        var watch = Stopwatch.StartNew();
        string status = "FAIL";
        try { T result = action(); status = "PASS"; return result; }
        finally
        {
            timer.DisposeAsync().AsTask().GetAwaiter().GetResult(); Sample();
            Console.WriteLine(FormattableString.Invariant($"MEASURE {name} status={status} seconds={watch.Elapsed.TotalSeconds:F3} sampledPeakPrivateBytes={Interlocked.Read(ref peak)}"));
        }
    }
}
