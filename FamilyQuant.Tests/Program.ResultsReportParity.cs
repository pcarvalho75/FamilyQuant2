using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ResultsReportParityCommands() =>
    [new("--proof-results-report-from-project", 21119, TestCommandPhase.Raw,
        args => args is ["--proof-results-report-from-project", _, _],
        (args, _) => ResultsReportParity(args[1], args[2]))];

    private static int ResultsReportParity(string input, string output)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        string owned = Path.GetFullPath(".worker-comm").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!input.StartsWith(owned, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(input) != "synthetic.fqproj"
            || !output.StartsWith(owned, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Use only this worktree's retained synthetic proof project and an owned output folder.");
        if (Directory.Exists(output)) throw new IOException("Use a fresh report parity output folder.");
        Directory.CreateDirectory(output);
        string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        string before = Hash(input);
        var context = SynchronizationContext.Current;
        var choice = MainWindow.UnsavedProjectChoiceOverride;
        var select = MainWindow.SelectReportPathOverride;
        var presenter = MainWindow.ReportExportPresenterOverride;
        var factory = MainWindow.ReportDocumentFactoryOverride;
        var stager = MainWindow.ReportFileStagerOverride;
        EnsureApplication();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var shell = new MainWindow { Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
            Width = 1024, Height = 768, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            shell.Show(); PumpDispatcher();
            RequireCurationProof(shell.OpenProjectPath(input), "native parity opens the retained synthetic project");
            AwaitCurationProof(CurationProofControl(shell).FullXicReady);
            string report = Path.Combine(output, "synthetic-report.docx");
            MainWindow.SelectReportPathOverride = () => report;
            MainWindow.ReportExportPresenterOverride = editor => editor.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            MainWindow.ReportDocumentFactoryOverride = null; MainWindow.ReportFileStagerOverride = null;
            RequireCurationProof(AwaitCurationProof(shell.ExportReportAsync()), "native report parity export succeeds");
            foreach (string path in new[] { report, ReportSupplementWorkbook.PathForReport(report), HtmlReportWriter.PathForReport(report) })
                Console.WriteLine("ARTIFACT " + Path.GetFileName(path) + " sha256=" + Hash(path) + " bytes=" + new FileInfo(path).Length);
            RequireCurationProof(Hash(input) == before, "native parity export leaves its exact input bytes unchanged");
            return 0;
        }
        finally
        {
            shell.Close(); PumpDispatcher();
            MainWindow.UnsavedProjectChoiceOverride = choice;
            MainWindow.SelectReportPathOverride = select; MainWindow.ReportExportPresenterOverride = presenter;
            MainWindow.ReportDocumentFactoryOverride = factory; MainWindow.ReportFileStagerOverride = stager;
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }
}
