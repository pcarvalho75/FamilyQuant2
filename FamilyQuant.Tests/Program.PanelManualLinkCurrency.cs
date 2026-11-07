using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void PanelManualLinkCurrencyTests()
    {
        Section("Z12k5a confirmed source links survive hand-created families");
        var source = PanelEvidenceFile();
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } };
        var references = PanelEvidenceClassifier.References(source, options);
        var links = PanelEvidenceClassifier.BuildLinks(source.FamilyPanel!, references, options);
        var choices = links.Where(link => link.NeedsConfirmation).Select(link => new PanelFamilyLinkSelection
            { LinkKey = link.Key, PanelFamilyKey = "narrow", Source = "Synthetic explicit link choice" }).ToArray();
        var confirmed = PanelEvidenceClassifier.ConfirmLinks(source.FamilyPanel!, links, choices);
        string before = JsonSerializer.Serialize(confirmed);
        string fingerprint = PanelEvidenceClassifier.DefinitionFingerprint(confirmed);
        var extended = ManualFamilyChoices.ExtendPanel(confirmed, new[] { new EvidenceManualChoice
            { Family = "hand-created", Name = "Hand-created group", NewFamily = true } });
        Check("Z12k5a hand-created definitions do not change source-link currency", PanelEvidenceClassifier.DefinitionFingerprint(extended) == fingerprint
            && extended.Families.Count == confirmed.Families.Count + 1 && JsonSerializer.Serialize(confirmed) == before);
        Check("Z12k5a confirmed choices remain current on the extended panel", choices.Length > 0
            && PanelEvidenceClassifier.BuildLinks(extended, references, options).Where(link => choices.Any(choice => choice.LinkKey == link.Key)).All(link => link.IsConfirmed));
        foreach (string field in new[] { "key", "decision", "merge target", "domain" })
        {
            var changed = FamilyPanelValidation.Clone(extended);
            var family = changed.Families.Single(row => row.Key == "broad");
            if (field == "key") family.Key = "changed-source";
            if (field == "decision") family.Decision = FamilyPanelDecision.NotAFamily;
            if (field == "merge target") family.MergeTargetFamilyKey = "narrow";
            if (field == "domain") family.Pfam.Add(new() { Accession = "domain-extra" });
            Check("Z12k5a a real source " + field + " change still makes confirmations stale", PanelEvidenceClassifier.DefinitionFingerprint(changed) != fingerprint
                && PanelEvidenceClassifier.BuildLinks(changed, references, options).Where(link => choices.Any(choice => choice.LinkKey == link.Key)).All(link => !link.IsConfirmed));
        }
        var realAddition = FamilyPanelValidation.Clone(confirmed);
        realAddition.Families.Add(new() { Key = "new-source", DisplayName = "New source group", SourceDecision = "Imported" });
        Check("Z12k5a an empty-domain source definition still changes currency", PanelEvidenceClassifier.DefinitionFingerprint(realAddition) != fingerprint);

        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            foreach (bool createNew in new[] { true, false })
            {
                Themes.ThemeManager.Apply(theme);
                string label = "Z12k5a " + theme + (createNew ? " new family" : " existing family");
                var file = PanelEvidenceFile(); string original = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "manual link currency fixture");
                var input = session.Propose("selected");
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var window = new SuperClusterPreviewWindow(preview, input, session.Propose, initialScope: "selected", evidenceMode: true,
                    confirmPanelLinks: session.ConfirmPanelLinks, addToFamily: session.AddToFamily, removeFromFamily: session.RemoveFromFamily,
                    updateManualChoices: session.UpdateManualChoices)
                { Width = 1280, Height = 780, Left = -30000, Top = -30000, ShowInTaskbar = false, ShowActivated = false,
                    WindowStartupLocation = WindowStartupLocation.Manual };
                try
                {
                    window.Show(); PumpDispatcher();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    foreach (var row in model.Evidence!.PanelLinks.Where(row => row.Source.NeedsConfirmation))
                        row.SelectedOption = row.Options.Single(option => option.Key == "narrow");
                    FinishReviewWork(model.UpdatePanelLinksAsync); PumpDispatcher();
                    Check(label + " starts with confirmed links and enabled Apply", model.CanConfirm && model.Evidence!.PanelLinks.All(row => !row.Source.NeedsConfirmation));
                    model.AddToFamily(new EvidenceManualRequest(new[] { 3 }, createNew ? "" : "narrow", createNew ? "Hand-created group" : "User second", createNew, "Synthetic hand choice"));
                    PumpDispatcher();
                    Check(label + " keeps every link resolved and the native Apply button enabled", model.CanConfirm
                        && ((Button)window.FindName("createButton")).IsEnabled && model.Evidence!.PanelLinks.All(row => !row.Source.NeedsConfirmation)
                        && file.ToJsonText() == original);
                    FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    Check(label + " retains current links through Update proposal", model.CanConfirm && model.Evidence!.PanelLinks.All(row => !row.Source.NeedsConfirmation));
                    var again = session.Proposal!.PanelContext!.Links.Where(link => link.CandidateFamilyKeys.Count > 1).Select(link => new PanelFamilyLinkSelection
                        { LinkKey = link.Key, PanelFamilyKey = "narrow", Source = "Synthetic reconfirmation" }).ToArray();
                    var reconfirmed = session.ConfirmPanelLinks(again);
                    Check(label + " reconfirmation uses the same definition as the proposal", reconfirmed.PanelLinks!.All(link => !link.NeedsConfirmation));
                    FinishReviewWork(model.UpdateProposalAsync); PumpDispatcher();
                    EvidenceApplyResult? applied = model.CanConfirm ? session.Apply(model.Evidence!.CaptureSelection()) : null;
                    Check(label + " applies the explicit hand choice with current family facts", applied?.Changed == true
                        && applied.Recipe.ManualChoices?.Single().NewFamily == createNew
                        && PanelAssignmentEvidenceStore.Current(file)?.Single(row => row.ProteinIndex == 3).EvidenceKind == PanelAssignmentEvidenceKind.AddedByHand);
                    if (applied?.Undo != null)
                    {
                        string state = file.ToJsonText();
                        applied.Undo.Restore(file);
                        Check(label + " one Undo restores the exact project", file.ToJsonText() == original);
                        EvidenceApply.ApplyRecipe(file, applied.Recipe);
                        Check(label + " saved recipe replays the exact grouping and link facts", file.ToJsonText() == state);
                    }
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
