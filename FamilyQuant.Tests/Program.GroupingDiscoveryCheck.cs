using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int GroupingDiscoveryCheck(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("Use --grouping-discovery-check <scratch.fcf> <new-output-directory>.");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Use a new output directory.");
        Directory.CreateDirectory(output);
        EnsureApplication();
        byte[] before = SHA256.HashData(File.ReadAllBytes(args[1]));
        var file = FamilyClusterFile.Load(args[1]);
        var screen = new ClusterGenerationControl(file);
        var owner = new Window { Content = screen, Width = 1200, Height = 800, ShowInTaskbar = false, ShowActivated = false };
        int unhandled = 0; bool sawReview = false, sawBusy = false, hasScope = false, timedOut = false;
        string status = "";
        SuperClusterPreviewWindow? review = null;
        var clock = Stopwatch.StartNew();
        DispatcherUnhandledExceptionEventHandler capture = (_, e) =>
        {
            unhandled++;
            File.AppendAllText(Path.Combine(output, "dispatcher-exception.txt"), e.Exception + Environment.NewLine);
            var element = LayoutInformation.GetLayoutExceptionElement(owner.Dispatcher);
            var chain = new System.Collections.Generic.List<string>();
            for (DependencyObject? current = element; current != null; current = VisualTreeHelper.GetParent(current))
            {
                string detail = current is FrameworkElement view ? view.Name + " size=" + view.ActualWidth + "," + view.ActualHeight : "";
                if (current is Grid grid) detail += " cols=" + string.Join(";", grid.ColumnDefinitions.Select(c => c.Width + "/" + c.MinWidth + "/" + c.MaxWidth))
                    + " rows=" + string.Join(";", grid.RowDefinitions.Select(r => r.Height + "/" + r.MinHeight + "/" + r.MaxHeight));
                chain.Add(current.GetType().Name + " " + detail);
            }
            File.AppendAllLines(Path.Combine(output, "layout-chain.txt"), chain);
            e.Handled = true;
        };
        Application.Current.DispatcherUnhandledException += capture;
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) =>
        {
            review ??= owner.OwnedWindows.OfType<SuperClusterPreviewWindow>().FirstOrDefault();
            if (review == null) return;
            sawReview = true;
            var model = (GroupingReviewViewModel)review.DataContext;
            sawBusy |= model.IsBusy || review.Operations.IsBusy;
            hasScope = model.HasTaskScope;
            status = model.Status;
            if ((sawBusy && !model.IsBusy && !review.Operations.IsBusy) || unhandled > 0 || clock.Elapsed > TimeSpan.FromMinutes(5))
            {
                timedOut = clock.Elapsed > TimeSpan.FromMinutes(5);
                File.WriteAllText(Path.Combine(output, "operation.log"), review.Operations.LogText);
                review.Close();
            }
        }, owner.Dispatcher);
        try
        {
            owner.Show();
            timer.Start();
            // This is the native review operation reached by Group by evidence after its evidence prerequisite.
            FinishReviewWork(screen.ReviewEvidenceAsync);
            if (review != null)
                FinishReviewWork(async () =>
                {
                    while (review.Operations.IsBusy && clock.Elapsed < TimeSpan.FromMinutes(6))
                        await System.Threading.Tasks.Task.Delay(20);
                });
            bool unchanged = before.SequenceEqual(SHA256.HashData(File.ReadAllBytes(args[1])));
            File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
            {
                Proteins = file.MyExtendedFastaItems.Count,
                ProteinsWithEvidence = file.MyExtendedFastaItems.Count(p => p.Evidence != null),
                SawReview = sawReview, SawBusy = sawBusy, HasTaskScope = hasScope, UnhandledExceptions = unhandled,
                TimedOut = timedOut, SourceUnchanged = unchanged, Status = status,
            }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Review seen: " + sawReview + "; busy seen: " + sawBusy + "; task scope: " + hasScope
                + "; unhandled exceptions: " + unhandled + "; source unchanged: " + unchanged);
            return sawReview && hasScope && unhandled == 0 && unchanged && !timedOut ? 0 : 1;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "exception.txt"), error.ToString());
            Console.WriteLine(error);
            return 1;
        }
        finally
        {
            timer.Stop(); review?.Close(); owner.Close();
            Application.Current.DispatcherUnhandledException -= capture;
            PumpDispatcher();
        }
    }
}
