using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int QuantifyScreens(string output)
    {
        EnsureApplication();
        Directory.CreateDirectory(output);
        string package = Path.Combine(output, "synthetic-input.sepr2");
        File.WriteAllText(package, "Synthetic UI fixture; never passed to a parser.");
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            Themes.ThemeManager.Apply(theme);
            var model = new ComputeXicViewModel("synthetic fixture", compute: (_, _, _) => Task.FromResult(new FullXicComputeResult
            {
                Rows = new() { new() { Side = "identified" }, new() { Side = "denovo" } },
                Manifest = new() { PerRunTic = new() { ["synthetic_run"] = 10000 } },
                Notes = new() { "Synthetic fixture. No RAW file was read." },
                TicShareSummary = new() { "Synthetic run: the primary recovery estimate and M+0 diagnostic remain separate." }
            })) { RawDirectory = output, SeproPackage = package };
            var window = new Window { Content = new QuantifyControl(model), Width = size.Item1, Height = size.Item2,
                Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize };
            window.Show();
            try
            {
                PumpDispatcher();
                ScreenHarness.Save(window, Path.Combine(output, $"quantify-{theme}-{size.Item1}-inputs.png"), size.Item1, size.Item2);
                model.ComputeAsync().GetAwaiter().GetResult();
                foreach (var expander in WalkTree(window).OfType<Expander>()) expander.IsExpanded = true;
                PumpDispatcher();
                System.Threading.Thread.Sleep(350);
                PumpDispatcher();
                ScreenHarness.Save(window, Path.Combine(output, $"quantify-{theme}-{size.Item1}-complete.png"), size.Item1, size.Item2);
            }
            finally { window.Close(); }
        }
        Console.WriteLine("Quantify: 8 synthetic state captures, both themes and sizes: " + output);
        return 0;
    }

    private static void QuantifyViewModelTests(string workDir)
    {
        Section("Quantify view model - one request, publication and refusal");
        string package = Path.Combine(workDir, "quantify-fixture.sepr2");
        File.WriteAllText(package, "fixture; the injected engine never reads this");
        var result = new FullXicComputeResult
        {
            Rows = new() { new QuantRow { Side = "identified" } },
            Manifest = new QuantManifest { PerRunTic = new() { ["fixture_run"] = 1000 } },
            Notes = new() { "Fixture diagnostic detail" },
            TicShareSummary = new() { "Fixture recovery reading" },
        };
        int calls = 0, completions = 0;
        ComputeXicRequest? captured = null;
        var model = new ComputeXicViewModel("fixture", compute: (request, _, _) =>
        { calls++; captured = request; return Task.FromResult(result); });
        model.Completed += _ => completions++;
        model.ComputeAsync().GetAwaiter().GetResult();
        Check("Quantify refuses missing inputs before calling the engine", calls == 0 && !model.Computed && !model.IsBusy);
        model.RawDirectory = workDir;
        model.SeproPackage = package;
        model.Filters.ModeTag = "union-all-deepnovo";
        model.Filters.PeaksMinimumAlc = "81.5";
        model.Filters.DeepNovoMinimumScore = "72.25";
        model.ComputeAsync().GetAwaiter().GetResult();
        Check("Quantify passes the unchanged production extraction basis", captured?.Options.IsotopeCount == 6
            && captured.Options.MinPointsForQuant == 3 && captured.DeNovoDirectory == null);
        Check("Quantify preserves distinct source cutoffs and ordered union scope", captured != null
            && captured.Options.DeNovoSelections!.Select(s => s.Source.Id).SequenceEqual(new[] { DeNovoSource.DeNovoAll.Id, DeNovoSource.DeepNovo.Id })
            && captured.Options.DeNovoSelections![0].MinimumConfidence == 81.5
            && captured.Options.DeNovoSelections![1].MinimumConfidence == 72.25);
        Check("Quantify publishes one completed result with the run summary and every detail", calls == 1 && completions == 1
            && ReferenceEquals(model.Result, result) && !model.IsBusy && model.RunSummary.Contains("1 quant rows")
            && model.Details.Contains("Fixture diagnostic detail") && model.Details.Contains("Fixture recovery reading"));
        model.Filters.MinimumLength = "invalid";
        model.ComputeAsync().GetAwaiter().GetResult();
        Check("a refused retry keeps completed results and never calls the engine", calls == 1 && ReferenceEquals(model.Result, result));
        var waiting = new TaskCompletionSource<FullXicComputeResult>();
        var cancellable = new ComputeXicViewModel("fixture", compute: (_, _, _) => waiting.Task)
            { RawDirectory = workDir, SeproPackage = package };
        Task running = cancellable.ComputeAsync();
        Check("Quantify blocks re-entry and editing throughout an operation", cancellable.IsBusy && !cancellable.CanEdit
            && !cancellable.ComputeCommand.CanExecute(null) && cancellable.ComputeAsync().IsCompleted);
        cancellable.CancelCommand.Execute(null);
        waiting.SetResult(result);
        running.GetAwaiter().GetResult();
        Check("cancellation refuses late publication and restores command state", !cancellable.Computed && !cancellable.IsBusy
            && cancellable.Status.StartsWith("Cancelled") && cancellable.ComputeCommand.CanExecute(null));
        var failedModel = new ComputeXicViewModel("fixture", compute: (_, _, _) => Task.FromException<FullXicComputeResult>(new IOException("fixture error")))
            { RawDirectory = workDir, SeproPackage = package };
        failedModel.ComputeAsync().GetAwaiter().GetResult();
        Check("an engine failure remains visible and publishes no scientific zero", !failedModel.Computed && !failedModel.IsBusy
            && failedModel.Status.Contains("fixture error"));
        var restored = new DeNovoFilterViewModel(new QuantManifest
        {
            DeNovoSourceScoreDistributions = new() { new() { MinLength = 9, StretchLength = 6, StretchFloor = 95 } },
        });
        Check("stored length and stretch filters restore with the source settings", restored.TryBuild(out var criteria, out _, out _)
            && criteria.MinLength == 9 && criteria.StretchLength == 6 && criteria.StretchFloor == 95);
    }
}
