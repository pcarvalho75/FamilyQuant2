using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelEvidenceCommandTable() =>
    [
        new("--test-panel-evidence", 74, TestCommandPhase.Raw, a => a.Contains("--test-panel-evidence", StringComparer.Ordinal), (_, _) =>
        {
            PanelEvidenceTests();
            PanelEvidenceUiTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static FamilyClusterFile PanelEvidenceFile()
    {
        static FastaItemExtended ClassifiedItem(string accession, string family, string domain, bool annotated) => new()
        {
            SequenceIdentifier = "sp|" + accession + "|FIXTURE", Sequence = string.Concat(Enumerable.Repeat("ACDEFGHIK", 12)), UserID = 1, USearchID = 1,
            Evidence = new() { Accession = accession, Reviewed = annotated, Family = family,
                Keywords = annotated ? new() { "selected" } : new(),
                Pfam = new() { new() { Accession = domain, Name = "Source domain" } } },
        };
        var alpha = ClassifiedItem("P00001", "Source group one", "domain-a", true);
        alpha.Evidence!.Pfam.Add(new() { Accession = "domain-b", Name = "Companion" });
        var beta = ClassifiedItem("P00002", "Source group two", "domain-c", true);
        var unannotated = ClassifiedItem("P00003", "", "domain-a", false);
        var unsupported = ClassifiedItem("P00004", "", "domain-z", false);
        var shared = ClassifiedItem("P00005", "Source group one", "domain-a", true);
        return new FamilyClusterFile
        {
            FamilyPanel = new() { Name = "Selected panel", Families = new()
            {
                new() { Key = "broad", DisplayName = "User first", Pfam = new() { new() { Accession = "domain-a" }, new() { Accession = "domain-b" } } },
                new() { Key = "narrow", DisplayName = "User second", Pfam = new() { new() { Accession = "domain-a" } } },
                new() { Key = "other", DisplayName = "User third", Pfam = new() { new() { Accession = "domain-c" } } },
            } },
            MyExtendedFastaItems = new() { alpha, beta, unannotated, unsupported, shared },
        };
    }

    private static void PanelEvidenceTests()
    {
        var file = PanelEvidenceFile();
        var options = new EvidenceClassificationOptions { ScopeTerms = new[] { "selected", "chosen tissue" } };
        var references = NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, options.ScopeTerms));
        string before = file.ToJsonText();
        var proposal = PanelEvidenceClassifier.Propose(file, options);
        Check("J2a target labels come from user panel keys", proposal.Proteins[0].Family == "broad" && proposal.Proteins[1].Family == "other");
        Check("J2a accompanying domains distinguish a shared domain", proposal.Proteins[0].Tier == EvidenceTier.UniProtFamily && proposal.Proteins[0].Family == "broad");
        Check("J2a unsupported protein stays outside every panel family", proposal.Proteins[3].Family.Length == 0);
        Check("J2a proposals retain every target and a reason", proposal.Proteins.Count == file.MyExtendedFastaItems.Count && proposal.Proteins.All(p => p.Reason.Length > 0));
        Check("J2a proposing never changes the project", before == file.ToJsonText());
        var links = PanelEvidenceClassifier.BuildLinks(file.FamilyPanel!, references, options);
        Check("J2a shared-domain ambiguity is explicitly proposed", links.Any(l => l.NeedsConfirmation
            && l.CandidateFamilyKeys.Contains("broad") && l.CandidateFamilyKeys.Contains("narrow")));
        Check("J2a an ambiguous source/architecture link is never guessed", proposal.Proteins[4].Family.Length == 0
            && proposal.Proteins[4].Flags.Count > 0);
        var choices = links.Where(l => l.NeedsConfirmation).Select(l => new PanelFamilyLinkSelection { LinkKey = l.Key, PanelFamilyKey = "narrow" }).ToArray();
        var confirmed = PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel!, links, choices);
        Check("J2a explicit user link choices are stored once", choices.Length > 0 && confirmed.EvidenceLinks.Count == choices.Length);
        var afterChoice = PanelEvidenceClassifier.Propose(file, options, panel: confirmed);
        Check("J2a a confirmed ambiguous architecture selects the chosen panel family", afterChoice.Proteins[4].Family == "narrow");
        Check("J2a unique companion architecture retains its evidence-derived family", afterChoice.Proteins[0].Family == "broad");
        var replayPanel = JsonSerializer.Deserialize<FamilyPanel>(JsonSerializer.Serialize(confirmed))!;
        Check("J2a stored confirmations replay without another decision", JsonSerializer.Serialize(PanelEvidenceClassifier.Propose(file, options, panel: replayPanel))
            == JsonSerializer.Serialize(afterChoice));
        var unknownChoice = new[] { new PanelFamilyLinkSelection { LinkKey = choices[0].LinkKey, PanelFamilyKey = "other" } };
        Check("J2a confirmations cannot invent a candidate family", Throws<ArgumentException>(() => PanelEvidenceClassifier.ConfirmLinks(file.FamilyPanel!, links, unknownChoice)));
        var changed = FamilyPanelValidation.Clone(confirmed);
        changed.Families[0].Pfam.RemoveAt(1);
        Check("J2a changed domain definitions invalidate old link confirmations", PanelEvidenceClassifier.BuildLinks(changed, references, options)
            .Any(l => l.NeedsConfirmation && choices.Any(c => c.LinkKey == l.Key)));
        var excluded = FamilyPanelValidation.Clone(file.FamilyPanel!);
        excluded.Families[2].Decision = FamilyPanelDecision.NotAFamily;
        Check("J2a an excluded panel row never assigns proteins", PanelEvidenceClassifier.Propose(file, options, panel: excluded).Proteins[1].Family.Length == 0);
        excluded.Families[2].Decision = FamilyPanelDecision.MergeIntoAnotherFamily;
        excluded.Families[2].MergeTargetFamilyKey = "broad";
        Check("J2a merge decisions use the user's target key", PanelEvidenceClassifier.Propose(file, options, panel: excluded).Proteins[1].Family == "broad");

        var seed = NearestReviewed.Search(NearestReviewed.Targets(file), references);
        NearestNeighbour Neighbour(int query, int reference, double identity, int? covered = null, List<NearestNeighbourTie>? ties = null) => new()
        {
            Query = file.MyExtendedFastaItems[query].SequenceIdentifier,
            Reference = file.MyExtendedFastaItems[reference].SequenceIdentifier,
            Accession = file.MyExtendedFastaItems[reference].Evidence!.Accession,
            Family = file.MyExtendedFastaItems[reference].Evidence!.Family,
            QueryLength = file.MyExtendedFastaItems[query].Sequence.Length,
            ReferenceLength = file.MyExtendedFastaItems[reference].Sequence.Length,
            QueryAlignedResidues = covered ?? file.MyExtendedFastaItems[query].Sequence.Length,
            AlignmentLength = covered ?? file.MyExtendedFastaItems[query].Sequence.Length, IdentityPercent = identity,
            TiedReferences = ties,
        };
        NeighbourSearch Search(params NearestNeighbour[] neighbours) => new()
        {
            Method = seed.Method, CoverageMethod = seed.CoverageMethod, TieMethod = seed.TieMethod, MinIdentityPercent = seed.MinIdentityPercent,
            MinQueryAlignedPercent = seed.MinQueryAlignedPercent, Queries = seed.Queries, References = seed.References,
            QuerySha256 = seed.QuerySha256, ReferenceSha256 = seed.ReferenceSha256, Neighbours = neighbours.ToList(),
        };
        var seventy = PanelEvidenceClassifier.Propose(file, options, Search(Neighbour(2, 0, 70)), references);
        Check("J2a unannotated transfer accepts seventy with compatible observed domains", seventy.Proteins[2].Family == "broad"
            && seventy.Proteins[2].Tier == EvidenceTier.UnannotatedNeighbour);
        Check("J2a unannotated transfer below seventy stays outside", PanelEvidenceClassifier.Propose(file, options,
            Search(Neighbour(2, 0, 69.99)), references).Proteins[2].Family.Length == 0);
        var annotated = FamilyClusterFile.FromJsonText(before);
        annotated.MyExtendedFastaItems[2].Evidence!.Keywords.Add("selected");
        var sixty = PanelEvidenceClassifier.Propose(annotated, options, Search(Neighbour(2, 0, 60)), references);
        Check("J2a annotated transfer accepts sixty", sixty.Proteins[2].Family == "broad" && sixty.Proteins[2].Tier == EvidenceTier.ReviewedNeighbour);
        Check("J2a annotated transfer below sixty stays outside", PanelEvidenceClassifier.Propose(annotated, options,
            Search(Neighbour(2, 0, 59.99)), references).Proteins[2].Family.Length == 0);
        Check("J2a annotated domain evidence alone is not a rescue", PanelEvidenceClassifier.Propose(annotated, options).Proteins[2].Tier == EvidenceTier.Unassigned);
        var noDomains = FamilyClusterFile.FromJsonText(before);
        noDomains.MyExtendedFastaItems[2].Evidence!.Pfam.Clear();
        Check("J2a unannotated transfer still requires observed domain compatibility", PanelEvidenceClassifier.Propose(noDomains, options,
            Search(Neighbour(2, 0, 90)), references).Proteins[2].Flags.Any(f => f.Code == "domain_compatibility"));
        var conflict = FamilyClusterFile.FromJsonText(before);
        conflict.MyExtendedFastaItems[2].Evidence!.Pfam = new() { new() { Accession = "domain-c" } };
        Check("J2a accompanying-domain conflict defeats a qualifying neighbour", PanelEvidenceClassifier.Propose(conflict, options,
            Search(Neighbour(2, 0, 90)), references).Proteins[2].Family.Length == 0);
        Check("J2a source annotation precedes a conflicting neighbour", PanelEvidenceClassifier.Propose(file, options,
            Search(Neighbour(0, 1, 100)), references).Proteins[0].Family == "broad");
        Check("J2a stored self hit is refused before classification", Throws<InvalidOperationException>(() => PanelEvidenceClassifier.Propose(file, options,
            Search(Neighbour(0, 0, 100)), references)));
        var coverageOptions = new EvidenceClassificationOptions { ScopeTerms = options.ScopeTerms, MinQueryAlignedPercent = 80 };
        Check("J2a a changed coverage gate requires a matching nearest search", Throws<InvalidOperationException>(() => PanelEvidenceClassifier.Propose(file, coverageOptions,
            Search(Neighbour(2, 0, 90, 76)), references)));
        var sameSourceTie = Search(Neighbour(2, 0, 90, ties: new()
        {
            new() { Accession = file.MyExtendedFastaItems[4].Evidence!.Accession, Reference = file.MyExtendedFastaItems[4].SequenceIdentifier,
                Family = file.MyExtendedFastaItems[4].Evidence!.Family, AlignmentLength = 108, ReferenceLength = 108, QueryAlignedResidues = 108 },
        }));
        Check("J2a equally close architectures can conflict within the same source family", PanelEvidenceClassifier.Propose(file, options,
            sameSourceTie, references, confirmed).Proteins[2].Family.Length == 0);
        Check("J2a native search retains equal references even when their source family agrees", seed.TieMethod == NearestReviewed.ReferenceTieMethod
            && seed.Neighbours.Any(n => n.TiedReferences?.Any(t => t.Family == n.Family) == true));
        var legacySearch = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(seed))!;
        legacySearch.AsObject().Remove(nameof(NeighbourSearch.TieMethod));
        Check("J2a historical searches cannot silently hide same-family architecture ties", Throws<InvalidOperationException>(() => PanelEvidenceClassifier.Propose(file, options,
            JsonSerializer.Deserialize<NeighbourSearch>(legacySearch.ToJsonString())!, references)));

        var multiple = FamilyClusterFile.FromJsonText(before);
        multiple.MyExtendedFastaItems[0].Evidence!.FamilyMemberships = new()
        {
            new() { Family = "Source group one" }, new() { Family = "Additional source membership" },
        };
        Check("J2a several source memberships can converge to one panel family", PanelEvidenceClassifier.Propose(multiple, options).Proteins[0].Family == "broad");
        var applied = EvidenceApply.Apply(file, afterChoice, new EvidenceApplySelection
        {
            SourceFingerprint = afterChoice.SourceFingerprint, FamilyPanel = confirmed,
            SelectedProteinIndices = afterChoice.Proteins.Where(p => p.SuggestedSelected).Select(p => p.ProteinIndex).ToArray(),
        }, "fixture");
        Check("J2a one apply stores panel choices and locks all assigned families", applied.Changed && applied.Undo != null
            && applied.Recipe.PanelContext != null && file.FamilyPanel!.EvidenceLinks.Count == choices.Length
            && applied.Recipe.Assignments.All(a => file.LockedUserIds.Contains(a.FamilyId)));
        var currentFacts = PanelAssignmentEvidenceStore.Current(file);
        var externalResolver = PanelEvidenceClassifier.CreateCurrentResolver(file);
        var external = new ProteinEvidence { Family = "Source group one", Reviewed = true,
            Pfam = new() { new() { Accession = "domain-a" } } };
        Check("J2a current resolver maps an external reviewed record through confirmed links", externalResolver.PanelFamilyFor(external) == "narrow");
        Check("J2a current resolver leaves unsupported external contexts unresolved", externalResolver.PanelFamilyFor(new ProteinEvidence { Family = "Unseen source" }) == null);
        Check("J2a every target has separate current family evidence and a reason", currentFacts?.Count == file.MyExtendedFastaItems.Count
            && currentFacts.All(r => r.Reason.Length > 0) && currentFacts.Any(r => !r.IsApplied && r.PanelFamilyKey == null));
        Check("J2a confirmed source links are distinguished in stored assignment evidence", currentFacts?.Single(r => r.ProteinIndex == 4).EvidenceKind
            .HasFlag(PanelAssignmentEvidenceKind.ConfirmedLink) == true);
        var currentMap = PanelAssignmentEvidenceStore.GetCurrentFamilyMap(file);
        string storedSequence = file.MyExtendedFastaItems[0].Sequence;
        file.MyExtendedFastaItems[0].Sequence = null!;
        bool missingSequenceRefused = false;
        try { PanelAssignmentEvidenceStore.RequireCurrent(file); }
        catch (InvalidOperationException ex) { missingSequenceRefused = ex.Message.Contains("no stored sequence", StringComparison.Ordinal); }
        Check("J2a missing target sequence is a short refusal rather than an encoding failure", PanelAssignmentEvidenceStore.Current(file) == null
            && missingSequenceRefused && PanelAssignmentEvidenceStore.GetCurrentFamilyMap(file).Message.Contains("no stored sequence", StringComparison.Ordinal));
        file.MyExtendedFastaItems[0].Sequence = storedSequence;
        Check("J2a current UserIDs map explicitly to panel keys and decisions", currentMap.IsCurrent
            && currentMap.UserFamilies[applied.Recipe.Assignments.Single(a => a.ProteinIndex == 0).FamilyId].PanelFamilyKey == "broad"
            && currentMap.PanelRows.Count == file.FamilyPanel!.Families.Count);
        file.MyExtendedFastaItems[0].UserAnnotation = "User display edit";
        Check("J2a display edits do not discard valid family evidence", PanelAssignmentEvidenceStore.Current(file) != null);
        file.MyExtendedFastaItems[0].UserAnnotation = applied.Recipe.Assignments.Single(a => a.ProteinIndex == 0).Name;
        file.MyExtendedFastaItems[0].UserID++;
        Check("J2a resolver publication check refuses changed source membership", !externalResolver.IsCurrent(file)
            && Throws<InvalidOperationException>(() => PanelEvidenceClassifier.CreateCurrentResolver(file)));
        Check("J2a regrouping marks per-target family evidence unavailable", PanelAssignmentEvidenceStore.Current(file) == null);
        Check("J2a a current UserID with different stored panel keys is flagged and refused", !PanelAssignmentEvidenceStore.GetCurrentFamilyMap(file).IsCurrent
            && PanelAssignmentEvidenceStore.GetCurrentFamilyMap(file).ConflictingUserIds.Contains(file.MyExtendedFastaItems[0].UserID)
            && Throws<InvalidOperationException>(() => PanelAssignmentEvidenceStore.RequireCurrent(file)));
        file.MyExtendedFastaItems[0].UserID--;
        var mapPanel = FamilyPanelValidation.Clone(file.FamilyPanel!);
        mapPanel.Families[2].Decision = FamilyPanelDecision.MergeIntoAnotherFamily;
        mapPanel.Families[2].MergeTargetFamilyKey = "broad";
        mapPanel.Families.Add(new() { Key = "excluded", Decision = FamilyPanelDecision.NotAFamily });
        var pureMap = PanelAssignmentEvidenceStore.BuildFamilyMap(mapPanel,
            new[] { new PanelProteinAssignment { FamilyUserId = 90, PanelFamilyKey = "other", IsApplied = true } });
        Check("J2a merged and excluded rows stay explicit in the current map", pureMap.IsCurrent
            && pureMap.UserFamilies[90].PanelFamilyKey == "broad"
            && pureMap.PanelRows.Single(r => r.PanelRowKey == "other").Decision == FamilyPanelDecision.MergeIntoAnotherFamily
            && pureMap.PanelRows.Single(r => r.PanelRowKey == "excluded").EffectivePanelKey == null);
        var conflicting = PanelAssignmentEvidenceStore.BuildFamilyMap(file.FamilyPanel!, new[]
        {
            new PanelProteinAssignment { FamilyUserId = 91, PanelFamilyKey = "broad", IsApplied = true },
            new PanelProteinAssignment { FamilyUserId = 91, PanelFamilyKey = "other", IsApplied = true },
        });
        Check("J2a a two-panel-key group never receives a guessed mapping", conflicting.IsCurrent
            && conflicting.ConflictingUserIds.SequenceEqual(new[] { 91 }) && conflicting.UserFamilies[91].PanelFamilyKey == null);
        var replaySource = FamilyClusterFile.FromJsonText(before);
        var replayed = EvidenceApply.ApplyRecipe(replaySource, applied.Recipe);
        Check("J2a panel recipe replays all confirmations and grouping exactly", replayed.Changed && replaySource.ToJsonText() == file.ToJsonText());
        file.ProteinSubclassLabels = file.MyExtendedFastaItems.Select((p, i) => new ProteinSubclassLabel
        { ProteinIndex = i, Identifier = p.SequenceIdentifier, FamilyUserId = p.UserID, Status = ProteinSubclassStatus.Unknown, Reason = "Fixture unknown" }).ToList();
        file.ProteinSubclassLabelsPanelFingerprint = PanelDerivedDataCurrentness.Fingerprint(file.FamilyPanel!);
        string labelledState = file.ToJsonText();
        var panelUndo = GroupingSnapshot.CaptureEvidence(file);
        file.FamilyPanel!.Families[0].Subclasses.Add(new() { Key = "new-rule", RequiredPfam = new() { "domain-a" } });
        var stalePanel = PanelDerivedDataCurrentness.Check(file);
        Check("J2a subclass-only edits invalidate labels but retain current family assignments", stalePanel.IsOutOfDate
            && stalePanel.FamilyAssignments == PanelDerivedDataStatus.Current && stalePanel.SubclassLabels == PanelDerivedDataStatus.OutOfDate
            && PanelAssignmentEvidenceStore.Current(file) != null);
        panelUndo.Restore(file);
        Check("J2a panel undo restores both labels and computed-panel fingerprints", file.ToJsonText() == labelledState
            && !PanelDerivedDataCurrentness.Check(file).IsOutOfDate);
        file.ProteinSubclassLabels = null; file.ProteinSubclassLabelsPanelFingerprint = null;
        var forged = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(applied.Recipe))!;
        forged.Assignments[0].Family = "other";
        var untouched = FamilyClusterFile.FromJsonText(before);
        Check("J2a replay rejects a fabricated panel-family label", Throws<ArgumentException>(() => EvidenceApply.ApplyRecipe(untouched, forged))
            && untouched.ToJsonText() == before);
        var badFacts = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(applied.Recipe))!;
        badFacts.PanelProteinAssignments![0].Reason = "Unsupported fabricated evidence";
        Check("J2a replay regenerates per-target evidence facts", Throws<ArgumentException>(() => EvidenceApply.ApplyRecipe(FamilyClusterFile.FromJsonText(before), badFacts)));
        applied.Undo!.Restore(file);
        Check("J2a one undo restores source panel, groups, locks and recipe", file.ToJsonText() == before);
        string root = GoldenNumbers.FindRepository();
        string[] names = file.FamilyPanel!.Families.Select(f => f.DisplayName).Concat(file.MyExtendedFastaItems.SelectMany(p => p.Evidence == null
            ? Array.Empty<string>() : SourceNames(p.Evidence))).ToArray();
        var violations = Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core", "Evidence"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "FamilyQuant.Core", "Panel"), "*.cs", SearchOption.TopDirectoryOnly))
            .SelectMany(path => EvidenceRuleGuard.Violations(File.ReadAllText(path), names).Select(v => Path.GetFileName(path) + ": " + v)).ToArray();
        Check("J2a panel and evidence rules contain no fixed family, name, Pfam or scope literal", violations.Length == 0, string.Join("; ", violations));
        static IEnumerable<string> SourceNames(ProteinEvidence evidence) => new[] { evidence.Family, evidence.Subfamily }.Concat(evidence.Pfam.Select(p => p.Name));
    }

    private static void PanelEvidenceUiTests()
    {
        EnsureApplication();
        PanelManualLinkCurrencyTests();
        var previous = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var file = PanelEvidenceFile();
                string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "fixture");
                var input = session.Propose("selected");
                var preview = new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds);
                var window = new SuperClusterPreviewWindow(preview, input, session.Propose, session.CountMotif,
                    initialScope: "selected", evidenceMode: true, confirmPanelLinks: session.ConfirmPanelLinks)
                { Width = 1280, Height = 780, Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    window.Show(); PumpDispatcher(); window.UpdateLayout();
                    var model = (GroupingReviewViewModel)window.DataContext;
                    var grid = window.FindName("panelLinksGrid") as DataGrid;
                    Check(theme + " panel review exposes evidence links and reviewed supports", grid != null && model.Evidence!.HasPanelRules
                        && model.Evidence.PanelLinks.Any(r => r.Source.NeedsConfirmation && r.SupportingAccessions.Length > 0));
                    Check(theme + " ambiguous links have no preselected candidate and block Apply", model.Evidence!.PanelLinks
                        .Where(r => r.Source.NeedsConfirmation).All(r => r.SelectedOption == null) && !model.CanConfirm);
                    foreach (var row in model.Evidence.PanelLinks.Where(r => r.Source.NeedsConfirmation))
                        row.SelectedOption = row.Options.Single(o => o.Key == "narrow");
                    Check(theme + " explicit link choices require proposal update before Apply", model.CanUpdatePanelLinks && !model.CanConfirm);
                    FinishReviewWork(model.UpdatePanelLinksAsync);
                    Check(theme + " confirmed links rebuild the proposal without changing the document", model.CanConfirm && file.ToJsonText() == before
                        && model.Evidence!.PanelLinks.Where(r => r.Source.CandidateFamilyKeys.Count > 1).All(r => r.Source.IsConfirmed));
                    var applied = session.Apply(model.Evidence!.CaptureSelection());
                    Check(theme + " native session applies choices and current family facts once", applied.Changed && applied.Undo != null
                        && file.FamilyPanel!.EvidenceLinks.Count > 0 && PanelAssignmentEvidenceStore.Current(file) != null);
                    applied.Undo!.Restore(file);
                    Check(theme + " native session Undo restores the entire document", file.ToJsonText() == before);
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previous); }
        var combinedFile = PanelEvidenceFile();
        string combinedBefore = combinedFile.ToJsonText();
        var host = new ClusterGenerationControl(combinedFile);
        host.ConfirmEvidenceReviewOverride = session =>
        {
            var input = session.Propose("selected");
            var choices = input.PanelLinks!.Where(l => l.NeedsConfirmation).Select(l => new PanelFamilyLinkSelection
                { LinkKey = l.Key, PanelFamilyKey = "narrow", Source = "Fixture user choice" }).ToArray();
            return new EvidenceReviewViewModel(session.ConfirmPanelLinks(choices)).CaptureSelection();
        };
        FinishReviewWork(host.ReviewEvidenceAsync);
        Check("J2a real assignment command recomputes subclasses within one Undo entry", host.ViewModel.UndoStack.Count == 1
            && SubclassAssignment.IsCurrent(combinedFile) && !host.Operations.IsBusy);
        UiField<Button>(host, "ButtonUndo")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check("J2a one command Undo restores family choices and subclass labels together", host.ViewModel.UndoStack.Count == 0
            && combinedFile.ToJsonText() == combinedBefore);
        Check("J2a active Help explains data links, both identity gates and no domain-only fallback", EvidenceHelp.Build(panelMode: true)
            .Contains("60%", StringComparison.Ordinal) && EvidenceHelp.Build(panelMode: true).Contains("70%", StringComparison.Ordinal)
            && EvidenceHelp.Build(panelMode: true).Contains("no domain-only fallback", StringComparison.Ordinal));
    }
}
