using FamilyQuant.Operations;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed class GatedPanelHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (Uri.UnescapeDataString(request.RequestUri!.Query).Contains("reviewed:true", StringComparison.Ordinal))
            {
                Started.TrySetResult();
                await Release.Task.WaitAsync(token);
            }
            return await base.SendAsync(request, token);
        }
    }

    private static void DomainsFirstTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (string scenario in new[] { "success", "cancel", "release drift", "source edit", "panel failure" })
            {
                Themes.ThemeManager.Apply(theme);
                var file = AtomicFetchFile();
                var screen = new ClusterGenerationControl(file);
                var window = new Window { Content = screen, Width = 1366, Height = 820, ShowActivated = false, ShowInTaskbar = false };
                var protein = file.MyExtendedFastaItems[0]; string before = file.ToJsonText();
                using var inner = new EvidenceAndPanelHandler(failPanel: scenario == "panel failure");
                if (scenario == "release drift") inner.PanelRelease = "other";
                using var handler = new GatedPanelHandler(inner);
                using var cancel = new CancellationTokenSource();
                var operation = new OperationContext(cancel.Token);
                int changes = 0; screen.ViewModel.DocumentChanged += (_, _) => changes++;
                Exception? failure = null;
                FamilyQuant.EvidenceFetchOutcome? outcome = null;
                Task? work = null;
                try
                {
                    window.Show(); PumpDispatcher();
                    var grid = (DataGrid)screen.FindName("DataGridResultGrid");
                    var column = grid.Columns.Single(candidate => candidate.Header as string == "Domains");
                    column.Visibility = Visibility.Visible;
                    FinishReviewWork(async () =>
                    {
                        var fetch = screen.ViewModel.FetchEvidenceAsync(handler, operation,
                            options: new Evidence.UniProtFetchOptions { DelayAsync = (_, _) => Task.CompletedTask });
                        work = fetch;
                        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
                        grid.ScrollIntoView(protein, column); window.UpdateLayout(); PumpDispatcher();
                        string text = (column.GetCellContent(protein) as TextBlock)?.Text ?? "";
                        Check("Z13b " + theme + " " + scenario + " actual Domains cell shows fetched Pfam while panel HTTP waits", text == "Supplied domain"
                            && screen.ViewModel.DomainPreview.TryGetValue(protein, out var domains) && domains == "Supplied domain"
                            && file.ToJsonText() == before && !operation.HasCommitted && changes == 0, text);
                        if (scenario == "success") ScreenHarness.Save(window, Path.Combine(output, "domains-" + theme + "-panel-pending.png"), 1366, 820);
                        if (scenario == "cancel") cancel.Cancel();
                        if (scenario == "source edit") protein.Description += " changed by the user";
                        handler.Release.TrySetResult();
                        try { outcome = await fetch; }
                        catch (Exception error) { failure = error; }
                    });
                    if (scenario is "success" or "panel failure")
                    {
                        Check("Z13b " + theme + " " + scenario + " completes the guarded document update and clears its transient display", failure == null
                            && operation.HasCommitted && changes == 1 && screen.ViewModel.DomainPreview.Count == 0 && protein.Domains == "Supplied domain"
                            && (scenario == "success" ? outcome!.Panel?.IsComplete == true : outcome!.PanelFailure != null));
                    }
                    else
                    {
                        grid.ScrollIntoView(protein, column); window.UpdateLayout(); PumpDispatcher();
                        Check("Z13b " + theme + " " + scenario + " clears early display and preserves uncommitted source evidence", failure != null
                            && !operation.HasCommitted && changes == 0 && screen.ViewModel.DomainPreview.Count == 0 && protein.Domains == "Stored domain"
                            && (scenario == "source edit" ? protein.Description.EndsWith("changed by the user", StringComparison.Ordinal) : file.ToJsonText() == before)
                            && (column.GetCellContent(protein) as TextBlock)?.Text == "Stored domain", failure?.GetType().Name);
                    }
                }
                catch (Exception error) { Check("Z13b " + theme + " " + scenario + " native domain display", false, error.ToString()); }
                finally
                {
                    handler.Release.TrySetResult(); cancel.Cancel();
                    if (work != null && !work.IsCompleted) FinishReviewWork(async () => { try { await work; } catch (Exception) { } });
                    window.Close(); PumpDispatcher();
                }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
