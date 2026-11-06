using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void GroupingDiscoverySafetyTests()
    {
        EnsureApplication();
        var previousTheme = Themes.ThemeManager.Current;
        foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
        foreach (bool progressFailure in new[] { false, true })
        {
            Themes.ThemeManager.Apply(theme);
            var file = TaskFile();
            string before = file.ToJsonText();
            var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
            var pending = new TaskCompletionSource<EvidenceTaskScopeInput>(TaskCreationOptions.RunContinuationsAsynchronously);
            var facts = new EvidenceTaskScopeInput("Fixture clade", new[] { new ReviewTaskTaxon("Fixture taxon", 21) },
                new[] { new ReviewTaskKeyword("Fixture scope", 2) }, 2, new[] { "Fixture scope" }, "fixture", "Fixture evidence", 3);
            var errors = new List<Exception>(); bool injected = false;
            var window = new SuperClusterPreviewWindow(preview, null, null, evidenceMode: true,
                discoverTask: _ => pending.Task) { Width = 1180, Height = 740, ShowActivated = false, ShowInTaskbar = false };
            if (progressFailure) window.Operations.ProgressReported += (_, _) =>
            {
                if (!injected) { injected = true; throw new IOException("Injected grouping progress failure"); }
            };
            DispatcherUnhandledExceptionEventHandler capture = (_, args) => { errors.Add(args.Exception); args.Handled = true; };
            Application.Current.DispatcherUnhandledException += capture;
            try
            {
                window.Show(); PumpDispatcher();
                var model = (GroupingReviewViewModel)window.DataContext;
                if (!progressFailure)
                {
                    foreach (var size in new[] { (1180.0, 740.0), (900.0, 560.0), (1180.25, 740.25) })
                    {
                        window.Width = size.Item1; window.Height = size.Item2;
                        window.UpdateLayout(); PumpDispatcher();
                    }
                    Check("Z11a " + theme + " automatic discovery busy card renders without dispatcher exceptions", errors.Count == 0
                        && model.IsBusy && window.Operations.IsBusy && WalkTree(window).OfType<BusyOverlay>().Single().IsVisible,
                        errors.FirstOrDefault()?.ToString());
                    pending.SetResult(facts);
                    FinishReviewWork(async () => { while (model.IsBusy || window.Operations.IsBusy) await Task.Delay(10); });
                    Check("Z11a " + theme + " automatic scope completes without changing the project", model.HasTaskScope
                        && errors.Count == 0 && file.ToJsonText() == before, errors.FirstOrDefault()?.ToString());
                }
                else
                {
                    Check("Z11a " + theme + " unexpected operation errors remain in the review window", injected && errors.Count == 0
                        && !model.IsBusy && !window.Operations.IsBusy && model.Status.Contains("Injected grouping progress failure", StringComparison.Ordinal)
                        && window.Operations.LogText.Contains("Injected grouping progress failure", StringComparison.Ordinal)
                        && window.IsVisible && file.ToJsonText() == before,
                        errors.FirstOrDefault()?.ToString() ?? model.Status);
                }
            }
            catch (Exception error) { Check("Z11a " + theme + " review safety", false, error.ToString()); }
            finally
            {
                pending.TrySetResult(facts);
                FinishReviewWork(async () => { while (window.Operations.IsBusy) await Task.Delay(10); });
                window.Close(); PumpDispatcher();
                Application.Current.DispatcherUnhandledException -= capture;
            }
        }
        Themes.ThemeManager.Apply(previousTheme);
        foreach (string scenario in new[] { "empty", "one taxon", "missing header", "retired taxon", "server failure" })
        {
            var file = TaskFile();
            if (scenario == "empty") file.MyExtendedFastaItems.Clear();
            if (scenario == "one taxon") file.MyExtendedFastaItems = file.MyExtendedFastaItems.Take(1).ToList();
            if (scenario == "missing header") file.MyExtendedFastaItems[0].Description = "Fixture without a taxonomy identifier";
            string before = file.ToJsonText();
            using var inner = new TaskTaxonomyHandler();
            using var handler = new TaxonomyRefusalHandler(inner)
            {
                Refuse = uri => uri.AbsolutePath.StartsWith("/taxonomy/", StringComparison.Ordinal),
                FailuresRemaining = scenario == "retired taxon" ? 1 : scenario == "server failure" ? 5 : 0,
                Status = scenario == "retired taxon" ? HttpStatusCode.NotFound : HttpStatusCode.ServiceUnavailable,
            };
            var session = new EvidenceGroupingSession(file, "fixture", discoverTask: (source, token) =>
                Evidence.EvidenceTaskDiscovery.DiscoverAsync(source, handler, cancellationToken: token,
                    options: new Evidence.UniProtFetchOptions { DelayAsync = (_, _) => Task.CompletedTask }));
            var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
            var window = new SuperClusterPreviewWindow(preview, null, session.Propose, evidenceMode: true,
                discoverTask: session.DiscoverTaskAsync) { ShowInTaskbar = false, ShowActivated = false };
            try
            {
                window.Show(); PumpDispatcher();
                var model = (GroupingReviewViewModel)window.DataContext;
                FinishReviewWork(async () => { while (model.IsBusy || window.Operations.IsBusy) await Task.Delay(10); });
                string expected = scenario switch
                {
                    "empty" => "no non-contaminant target proteins",
                    "missing header" => "OS= and OX= header",
                    "retired taxon" => "HTTP 404",
                    "server failure" => "HTTP 503",
                    _ => "Review the database taxa",
                };
                Check("Z11a " + scenario + " stays in the review with a result or useful error", window.IsVisible
                    && model.HasTaskScope == (scenario == "one taxon") && model.Status.Contains(expected, StringComparison.OrdinalIgnoreCase)
                    && file.ToJsonText() == before, model.Status);
            }
            finally { window.Close(); PumpDispatcher(); }
        }
    }
}
