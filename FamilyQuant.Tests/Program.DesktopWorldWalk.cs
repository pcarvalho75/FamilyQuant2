using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DesktopWorldWalkCommands() =>
    [new("--desktop-world-walk", 10461, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--desktop-world-walk", (args, _) => DesktopWorldWalk(args))];

    private static int DesktopWorldWalk(string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("Use --desktop-world-walk <copied-project> <copied-raw-folder> <copied-sepr2> <copied-denovo-root> <output> <confirmed-scope>.");
        string output = Path.GetFullPath(args[5]); Directory.CreateDirectory(output);
        string logPath = Path.Combine(output, "desktop-walk.log");
        if (!NativeProofDesktop.IsPrivate) return NativeProofDesktop.Run(args, logPath, 3_600_000);
        using var log = new StreamWriter(logPath) { AutoFlush = true };
        var previous = Console.Out; var previousError = Console.Error; var previousContext = SynchronizationContext.Current;
        Console.SetOut(log); Console.SetError(log);
        Window? host = null;
        try
        {
            EnsureApplication();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) =>
            { if (sender is Window window) { window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = window.Top = -30000; } }), true);
            string sourceHash = TestFileHash.Upper(args[1]);
            var (file, originalRows, manifest) = ProjectStore.ReadFqproj(args[1]);
            var curation = new ClusterGenerationControl(file);
            host = new Window { Width = 1440, Height = 1000, Content = curation, ShowActivated = false, ShowInTaskbar = false,
                Left = -30000, Top = -30000 };
            host.Show(); PumpDispatcher();
            curation.Operations.PropertyChanged += (_, _) => Console.WriteLine("Curation: " + curation.Operations.Message);
            if (!file.MyExtendedFastaItems.Any(p => !string.IsNullOrWhiteSpace(p.Evidence?.Family)))
                WaitWorldWalk(curation.FetchProteinEvidenceAsync(), TimeSpan.FromMinutes(15));
            bool confirmedDialog = false;
            var confirm = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            confirm.Tick += (_, _) =>
            {
                var dialog = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.Title == "Confirm UniProt families scope");
                if (dialog?.Content is not StackPanel panel) return;
                panel.Children.OfType<TextBox>().Single().Text = args[6];
                confirmedDialog = true;
                panel.Children.OfType<StackPanel>().Single().Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };
            confirm.Start();
            try
            {
                ((Button)curation.FindName("ButtonFetchFamilyWorld")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitWorldWalk(curation.FamilyWorldFetchCompletion, TimeSpan.FromMinutes(20));
            }
            finally { confirm.Stop(); }
            var world = FamilyWorldFetcher.Stored(file);
            Console.WriteLine("Actual fetch button completed. Scope dialog: " + confirmedDialog + "; release " + world.Release
                + "; entries " + world.Entries.Count + "; families " + world.Families.Count + "; hash " + world.ContentSha256);
            SaveWorldWalkImage(host, Path.Combine(output, "curation-world.png"));
            ProjectStore.WriteFqproj(Path.Combine(output, "fetched.fqproj"), file, originalRows, manifest);
            var offline = ProjectStore.ReadFqproj(Path.Combine(output, "fetched.fqproj"));
            if (JsonSerializer.Serialize(FamilyWorldFetcher.Stored(offline.fcf)) != JsonSerializer.Serialize(world))
                throw new InvalidDataException("Offline reopen changed the world.");
            var compute = new ComputeXicViewModel(AppVersionInfo.Current(), manifest, offline.fcf)
            { RawDirectory = args[2], SeproPackage = args[3], DeNovoDirectory = args[4] };
            var quantify = new QuantifyControl(compute); host.Content = quantify; PumpDispatcher();
            compute.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(compute.Status)) Console.WriteLine("Compute: " + compute.Status); };
            var computeButton = (Button)quantify.FindName("ButtonCompute");
            computeButton.Command.Execute(computeButton.CommandParameter);
            var watch = Stopwatch.StartNew();
            while (compute.IsBusy && watch.Elapsed < TimeSpan.FromMinutes(35)) { PumpDispatcher(); Thread.Sleep(20); }
            if (compute.IsBusy || compute.Result == null) throw new InvalidOperationException("Compute did not complete: " + compute.Status);
            var result = compute.Result;
            ProjectStore.WriteFqproj(Path.Combine(output, "computed.fqproj"), offline.fcf, result.Rows, result.Manifest);
            var reopened = ProjectStore.ReadFqproj(Path.Combine(output, "computed.fqproj"));
            var analytics = new AnalyticsControl(reopened.quant, reopened.manifest, reopened.fcf);
            host.Content = analytics; PumpDispatcher();
            var gap = analytics.ViewModel.DatabaseGap;
            if (!gap.HasData || gap.Rows.Count == 0) throw new InvalidDataException("The computed Database gap screen has no table.");
            Console.WriteLine("Database gap displayed: " + gap.Rows.Count + " rows; FASTA " + gap.FastaState);
            SaveWorldWalkImage(host, Path.Combine(output, "database-gap.png"));
            analytics.ViewModel.ExportKind = ResultsExportKind.DatabaseGap;
            string csv = Path.Combine(output, "database-gap.csv");
            ExportWorldWalk(analytics, csv);
            string repeatedCsv = Path.Combine(output, "database-gap-repeat.csv"); ExportWorldWalk(analytics, repeatedCsv);
            if (!File.ReadAllBytes(csv).SequenceEqual(File.ReadAllBytes(repeatedCsv))) throw new InvalidDataException("Repeated CSV differs.");
            if (!gap.CanExportFasta) throw new InvalidDataException("Real-data FASTA cannot be exported: " + gap.FastaProblem);
            analytics.ViewModel.ExportKind = ResultsExportKind.SuggestedAdditions;
            string fasta = Path.Combine(output, "suggested-additions.fasta"); ExportWorldWalk(analytics, fasta);
            string repeatedFasta = Path.Combine(output, "suggested-additions-repeat.fasta"); ExportWorldWalk(analytics, repeatedFasta);
            if (!File.ReadAllBytes(fasta).SequenceEqual(File.ReadAllBytes(repeatedFasta))) throw new InvalidDataException("Repeated FASTA differs.");
            if (!File.ReadLines(fasta).Any(line => line.StartsWith('>'))) throw new InvalidDataException("FASTA has no entries.");
            if (sourceHash != TestFileHash.Upper(args[1])) throw new InvalidDataException("The copied input was changed.");
            Console.WriteLine("PASS actual fetch/scope, save/offline reopen, Compute command, Database gap screen, native CSV and FASTA dialogs.");
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex); return 1; }
        finally { host?.Close(); Console.SetOut(previous); Console.SetError(previousError); SynchronizationContext.SetSynchronizationContext(previousContext); }
    }

    private static void WaitWorldWalk(Task task, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (!task.IsCompleted && watch.Elapsed < timeout) { PumpDispatcher(); Thread.Sleep(20); }
        if (!task.IsCompleted) throw new TimeoutException("Desktop world walk timed out.");
        task.GetAwaiter().GetResult(); PumpDispatcher();
    }

    private static void ExportWorldWalk(AnalyticsControl screen, string path)
    {
        using var driver = new NativeOwnedDialogDriver(path);
        ((Button)screen.FindName("ButtonExport")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var watch = Stopwatch.StartNew();
        while (!File.Exists(path) && watch.Elapsed < TimeSpan.FromSeconds(45)) { PumpDispatcher(); Thread.Sleep(10); }
        if (!driver.SawDialog || driver.TimedOut || !File.Exists(path)) throw new IOException("Native export failed: " + screen.ViewModel.Status);
        while (AppOperationHost.For(screen).IsBusy && watch.Elapsed < TimeSpan.FromSeconds(45)) { PumpDispatcher(); Thread.Sleep(10); }
    }

    private static void SaveWorldWalkImage(Window host, string path)
    {
        host.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1440, 1000, 96, 96, PixelFormats.Pbgra32); bitmap.Render(host);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); png.Save(stream);
    }
}
