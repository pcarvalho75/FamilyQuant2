using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using FamilyQuant.Operations;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ReferencePanelExpansionCommands() =>
    [
        new("--test-reference-panel-expansion", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-reference-panel-expansion", (args, _) =>
        {
            ReferencePanelExpansionTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelExpansion"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        }),
        new("--test-reference-panel-expansion-ui", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-reference-panel-expansion-ui", (args, _) =>
        {
            ReferencePanelExpansionUiTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.PanelExpansionUi"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
        }),
    ];
    private static void ReferencePanelExpansionTests(string output) => ReferencePanelScopeDatabaseTests(output, false, expansion: true);
    private static void ReferencePanelExpansionUiTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output); var oldTheme = Themes.ThemeManager.Current;
        var (edited, editedEntries) = ScopedPanelDatabase(true); var editedHost = new ClusterGenerationControl(edited);
        using (var editHandler = new ScopedPanelHandler(editedEntries) { OnQuery = _ => edited.EvidenceSource!.Release = "Concurrent source edit" })
            Check("Z15a2 in-place live source edits cannot be overwritten by staged panel expansion", Throws<InvalidOperationException>(() =>
                FinishReviewWork(() => editedHost.ViewModel.FetchEvidenceAsync(editHandler, new OperationContext(), retryPanelOnly: true,
                    panelRequest: new(new[] { "Chosen phase" })))) && edited.EvidenceSource!.Release == "Concurrent source edit" && edited.EvidenceSource.ReferencePanel == null);
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var (file, entries) = ScopedPanelDatabase(true); using var initialHandler = new ScopedPanelHandler(entries);
                file.EvidenceSource!.ReferencePanel = ReferencePanelFetcher.FetchForScopeAsync(file, new(new[] { "Chosen phase" }), initialHandler).GetAwaiter().GetResult();
                using var expansion = new ScopedPanelHandler(entries);
                var host = new ClusterGenerationControl(file); string proteins = JsonSerializer.Serialize(file.MyExtendedFastaItems);
                var session = new EvidenceGroupingSession(file, "scope UI fixture");
                var input = session.Propose("\"Chosen phase\"");
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, null);
                var window = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "\"Chosen phase\"", evidenceMode: true,
                    fetchMissingPanel: (scope, operation) => session.RefreshPanelAsync(async () => await host.ViewModel.FetchEvidenceAsync(expansion, operation,
                        retryPanelOnly: true, panelRequest: new(WordCluster.ParseKeywords(scope)))), panelCoverage: session.PanelCoverageStatus, panelCovered: session.PanelScopeCovered)
                    { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    window.Show(); PumpDispatcher(); var model = (GroupingReviewViewModel)window.DataContext;
                    model.ScopeText = "\"Chosen phase\", \"Later phase\"";
                    FinishReviewWork(model.UpdateProposalAsync);
                    Check("Z15a " + theme + " Update rejects missing coverage without downloading", expansion.Queries.Count == 0
                        && model.Status.Contains("Fetch missing panel", StringComparison.Ordinal) && !model.CanConfirm);
                    var fetchButton = (Button)window.FindName("FetchMissingPanelButton");
                    Check("Z15a " + theme + " missing-panel action is visible beside Update", fetchButton.IsVisible && fetchButton.IsEnabled
                        && fetchButton.TransformToAncestor(window).Transform(new Point()).Y < 300);
                    ScreenHarness.Save(window, Path.Combine(output, "scope-missing-" + theme + ".png"), 1366, 768);
                    FinishReviewWork(window.FetchMissingPanelAsync);
                    Check("Z15a " + theme + " explicit Fetch missing panel stores expansion without grouping", file.EvidenceSource.ReferencePanel!.Coverage!.ScopeTerms.Count == 2
                        && model.Evidence == null && model.Status.Contains("Update proposal", StringComparison.Ordinal) && JsonSerializer.Serialize(file.MyExtendedFastaItems) == proteins,
                        "coverage=" + file.EvidenceSource.ReferencePanel.Coverage.ScopeTerms.Count + ", empty=" + (model.Evidence == null) + ", same_proteins=" + (JsonSerializer.Serialize(file.MyExtendedFastaItems) == proteins) + ", status=" + model.Status);
                    int requests = expansion.Queries.Count; FinishReviewWork(model.UpdateProposalAsync);
                    Check("Z15a " + theme + " explicit Update after expansion stays offline", model.Evidence != null && expansion.Queries.Count == requests
                        && model.PanelCoverageText == "Reference panel covers this scope.");
                    ScreenHarness.Save(window, Path.Combine(output, "scope-expanded-" + theme + ".png"), 1366, 768);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
    }
}
