using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ScopedSubclassFlowCommands() =>
    [new("--test-scoped-subclass-flow", 100, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-scoped-subclass-flow", (args, _) =>
    {
        ScopedSubclassFlowTests(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.ScopedSubclassFlow"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped ====="); return failed == 0 ? 0 : 1;
    })];
    private static void ScopedSubclassFlowTests(string output)
    {
        EnsureApplication(); Directory.CreateDirectory(output);
        var file = EvidenceMatchingFile(); var family = file.FamilyPanel!.Families[0];
        family.Subclasses.Add(new() { Key = "invented-part", DisplayName = "Invented subdivision", RequiredPfam = new() { "PF-invented" } });
        file.EvidenceSource!.ReferencePanel!.Coverage = new() { ScopeTerms = new() { "ACTION ALPHA" }, WholeFamilies = new() { "Stored group r" } };
        file.PanelProteinAssignments = file.MyExtendedFastaItems.Select((protein, index) => (protein, index))
            .Where(row => !NearestReviewed.IsDecoy(row.protein)).Select(row => new PanelProteinAssignment
            {
                ProteinIndex = row.index, Identifier = row.protein.SequenceIdentifier, FamilyUserId = row.protein.UserID,
                IsApplied = row.index < 2, PanelFamilyKey = row.index < 2 ? family.Key : null,
            }).ToList();
        file.PanelProteinAssignmentsPanelFingerprint = PanelDerivedDataCurrentness.FamilyFingerprint(file.FamilyPanel);
        file.PanelProteinAssignmentsFingerprint = "invented stand-in";
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "Action alpha" } };
        Check("Z15a5 actual subclass search demonstrates its full-reference prerequisite", Throws<InvalidDataException>(() => SubclassAssignment.BuildCandidate(file, options)));
        var session = new EvidenceGroupingSession(file, "subclass scope fixture");
        var input = new EvidenceReviewInput(EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems),
            new[] { new ReviewProteinInput(0, file.MyExtendedFastaItems[0].SequenceIdentifier, 1, family.Key, "UniProt family", "Invented stored annotation", true, "", Array.Empty<string>(), true) },
            new[] { new ReviewFamilyInput(family.Key, "Invented family", 1, 1) }, Array.Empty<ReviewMarkerInput>(), Array.Empty<ReviewArchitectureInput>());
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, null);
        var ordinary = new GroupingReviewViewModel(preview, input, _ => input, initialScope: "Action alpha", evidenceMode: true);
        Check("Z15a5 fixture is otherwise ready to apply", ordinary.CanConfirm && ReferencePanelUseRequirements.HasSubclasses(file.FamilyPanel));
        string before = file.ToJsonText(); string refusal = "";
        try { session.Apply(ordinary.Evidence!.CaptureSelection()); }
        catch (InvalidOperationException error) { refusal = error.Message; }
        Check("Z15a5 backend refuses before grouping and names Full panel", refusal == ReferencePanelUseRequirements.SubclassApply && file.ToJsonText() == before);
        var oldTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var picker = new ReferencePanelScopeWindow(new[] { "Action alpha" }, ReferencePanelUseRequirements.HasSubclasses(file.FamilyPanel))
                    { ShowActivated = false, ShowInTaskbar = false };
                picker.Loaded += (_, _) => picker.Dispatcher.BeginInvoke(new Action(() =>
                {
                    Check("Z15a5 " + theme + " subclass project starts with Full panel and its one-line reason", picker.FullPanelBox.IsChecked == true
                        && !picker.ScopeBox.IsEnabled && picker.Explanation.Text == ReferencePanelUseRequirements.SubclassChoice);
                    ScreenHarness.Save(picker, Path.Combine(output, "subclass-full-choice-" + theme + ".png"), 600, 300);
                    picker.FetchButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }));
                Check("Z15a5 " + theme + " native fetch choice requests the complete panel", picker.ShowDialog() == true && picker.Request?.FullPanel == true);
                var review = new SuperClusterPreviewWindow(preview, input, _ => input, initialScope: "Action alpha", evidenceMode: true,
                    fullPanelRequirement: session.FullPanelRequirement) { ShowActivated = false, ShowInTaskbar = false };
                try
                {
                    review.Show(); PumpDispatcher();
                    Check("Z15a5 " + theme + " scoped review explains Full panel before enabling Apply", ((TextBlock)review.FindName("applyHint")).Text == ReferencePanelUseRequirements.SubclassApply
                        && !((Button)review.FindName("createButton")).IsEnabled && file.ToJsonText() == before);
                    ScreenHarness.Save(review, Path.Combine(output, "subclass-before-apply-" + theme + ".png"), 1366, 768);
                }
                finally { review.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(oldTheme); }
        // These invented entries are the original full fixture; only the coverage marker was added above.
        file.EvidenceSource.ReferencePanel.Coverage = null;
        var complete = new EvidenceGroupingSession(file, "full subclass fixture");
        var ready = new GroupingReviewViewModel(preview, input, _ => input, initialScope: "Action alpha", evidenceMode: true, fullPanelRequirement: complete.FullPanelRequirement);
        var labels = SubclassAssignment.BuildCandidate(file, options).Labels;
        Check("Z15a5 Full panel restores the unchanged subclass calculation and Apply eligibility", ready.CanConfirm && complete.FullPanelRequirement() == ""
            && labels.Count(label => label.Status == ProteinSubclassStatus.Assigned && label.SubclassKey == "invented-part") == 2);
    }
}
