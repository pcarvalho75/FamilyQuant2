using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> EvidenceUnlockIdentityCommands() =>
    [new("--test-evidence-unlock-identity", 4321, TestCommandPhase.Raw, args => args.Length > 0 && args[0] == "--test-evidence-unlock-identity", (args, _) =>
    {
        EvidenceUnlockIdentityTests(args.Length > 1 ? Path.GetFullPath(args[1]) : Path.Combine(Path.GetTempPath(), "unlock-identity", Guid.NewGuid().ToString("N")));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    })];

    private static FamilyClusterFile IdentityUnlockFile(bool panel = false)
    {
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "sp|P70001|FIRST", Sequence = "ACDEFGHIK", Description = "Synthetic first protein", UserID = 7, USearchID = 7,
                Evidence = new() { Accession = "P70001", Reviewed = true, Family = "First invented family", Keywords = new() { "scope" } } },
            new() { SequenceIdentifier = "sp|P70002|SECOND", Sequence = "LMNPQRSTVK", Description = "Synthetic second protein", UserID = 9, USearchID = 9,
                Evidence = new() { Accession = "P70002", Reviewed = true, Family = "Second invented family", Keywords = new() { "scope" } } },
        } };
        if (panel)
        {
            file.MyExtendedFastaItems[0].Evidence!.Pfam.Add(new() { Accession = "PF01234" });
            file.MyExtendedFastaItems[1].Evidence!.Pfam.Add(new() { Accession = "PF05678" });
            file.FamilyPanel = new() { Families = new()
            {
                new() { Key = "first", DisplayName = "First group", Pfam = new() { new() { Accession = "PF01234" } } },
                new() { Key = "second", DisplayName = "Second group", Pfam = new() { new() { Accession = "PF05678" } } },
            } };
            file.EvidenceSource = new() { Provider = "UniProt", Release = "fixture", ReferencePanel = new()
                { Provider = "UniProt", Release = "fixture", Families = new() { "First invented family", "Second invented family" } } };
        }
        var session = new EvidenceGroupingSession(file, "identity fixture");
        session.Apply(new EvidenceReviewViewModel(session.Propose("scope")).CaptureSelection());
        file.LockedUserIds.Add(99);
        return file;
    }

    private static string IdentityReplayState(string json)
    {
        var value = JsonNode.Parse(json)!;
        // A lock set's insertion order depends on freed HashSet slots, not on the saved scientific state.
        value[nameof(FamilyClusterFile.LockedUserIds)] = JsonSerializer.SerializeToNode(value[nameof(FamilyClusterFile.LockedUserIds)]!.Deserialize<int[]>()!.Order());
        return value.ToJsonString();
    }

    private static void EvidenceUnlockIdentityTests(string output)
    {
        Section("Z12p5l2 family identity through explicit unlock and rebuild");
        EnsureApplication(); Directory.CreateDirectory(output);
        var oldMessages = ShellMessages.Presenter;
        ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
        try
        {
            foreach (string mode in new[] { "stored", "legacy", "saved-after-unlock", "panel" })
            {
                var source = IdentityUnlockFile(panel: mode == "panel");
                if (mode == "legacy") source.EvidenceRecipe!.FamilyIdentities = null;
                string project = Path.Combine(output, mode + ".fqproj");
                ProjectStore.WriteFqproj(project, source, Array.Empty<QuantRow>(), new QuantManifest());
                string sourceHash = FileFingerprint.Sha256(project);
                var file = ProjectStore.ReadFqproj(project).fcf;
                var members = file.MyExtendedFastaItems.ToDictionary(protein => protein.SequenceIdentifier, protein => protein.UserID, StringComparer.Ordinal);
                string lockedState = file.ToJsonText();
                var control = new ClusterGenerationControl(file);
                var host = new Window { Content = control, Width = 1024, Height = 768, Left = -30000, Top = -30000,
                    ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                try
                {
                    host.Show(); PumpDispatcher(); FinishBusyTask(control.FullXicReady);
                    EvidenceGroupingSession? session = null;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        session = new EvidenceGroupingSession(file, "identity fixture");
                        var input = session.Propose("scope");
                        var currentSession = session;
                        var review = new SuperClusterPreviewWindow(new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds),
                            input, session.Propose, initialScope: "scope", evidenceMode: true,
                            unlockEvidenceFamilies: scope => currentSession.UnlockEvidenceFamilies(scope, families => control.ViewModel.UnlockFamilies(families)))
                        { Owner = host, Width = 1024, Height = 768, Left = -30000, Top = -30000, ShowActivated = false,
                            ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual };
                        try
                        {
                            review.Show(); PumpDispatcher();
                            int undoCount = control.ViewModel.UndoStack.Count;
                            ((Button)review.FindName("UnlockEvidenceFamiliesButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); PumpDispatcher();
                            Check("Z12p5l2 " + mode + " one explicit unlock preserves members and unrelated locks", file.LockedUserIds.SetEquals(new[] { 99 })
                                && control.ViewModel.UndoStack.Count == undoCount + 1 && file.MyExtendedFastaItems.All(protein => members[protein.SequenceIdentifier] == protein.UserID));
                            if (attempt == 0)
                            {
                                control.ViewModel.Undo();
                                Check("Z12p5l2 " + mode + " one Undo restores locks and original identity metadata exactly", file.ToJsonText() == lockedState);
                            }
                        }
                        finally { review.Close(); PumpDispatcher(); }
                    }
                    if (mode == "saved-after-unlock")
                    {
                        string unlocked = Path.Combine(output, "unlocked.fqproj");
                        ProjectStore.WriteFqproj(unlocked, file, Array.Empty<QuantRow>(), new QuantManifest());
                        file = ProjectStore.ReadFqproj(unlocked).fcf;
                        control = new ClusterGenerationControl(file); host.Content = control;
                        PumpDispatcher(); FinishBusyTask(control.FullXicReady);
                        session = new EvidenceGroupingSession(file, "identity fixture");
                    }
                    var selected = new EvidenceReviewViewModel(session!.Propose("scope")).CaptureSelection();
                    string unlockedState = file.ToJsonText();
                    var apply = typeof(ClusterGenerationControl).GetMethod("ApplyEvidenceChoice", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    var operation = (System.Threading.Tasks.Task)apply.Invoke(control, new object[] { session, selected })!;
                    FinishBusyTask(operation); operation.GetAwaiter().GetResult();
                    bool same = file.MyExtendedFastaItems.All(protein => members[protein.SequenceIdentifier] == protein.UserID);
                    Check("Z12p5l2 " + mode + " native Apply reuses the same family numbers and exact members", same && file.LockedUserIds.Contains(99)
                        && members.Values.All(file.LockedUserIds.Contains));
                    var recipe = JsonSerializer.Deserialize<EvidenceRecipe>(JsonSerializer.Serialize(file.EvidenceRecipe))!;
                    string appliedState = file.ToJsonText();
                    File.WriteAllText(Path.Combine(output, mode + "-applied.json"), appliedState);
                    control.ViewModel.Undo();
                    Check("Z12p5l2 " + mode + " grouping Undo restores the exact unlocked state", file.ToJsonText() == unlockedState);
                    EvidenceApply.ApplyRecipe(file, recipe);
                    File.WriteAllText(Path.Combine(output, mode + "-replayed.json"), file.ToJsonText());
                    Check("Z12p5l2 " + mode + " recipe replays every stored field and lock value", IdentityReplayState(file.ToJsonText()) == IdentityReplayState(appliedState));
                    Check("Z12p5l2 " + mode + " source project bytes are unchanged", FileFingerprint.Sha256(project) == sourceHash);
                    File.WriteAllText(Path.Combine(output, mode + "-result.json"), JsonSerializer.Serialize(new
                        { Before = members, After = file.MyExtendedFastaItems.ToDictionary(protein => protein.SequenceIdentifier, protein => protein.UserID), Same = same },
                        new JsonSerializerOptions { WriteIndented = true }));
                }
                finally { host.Close(); PumpDispatcher(); }
            }
            EvidenceUnlockIdentityGuards();
        }
        finally { ShellMessages.Presenter = oldMessages; }
    }

    private static void EvidenceUnlockIdentityGuards()
    {
        EvidenceApplyResult Repair(FamilyClusterFile file)
        {
            var session = new EvidenceGroupingSession(file, "identity guard fixture");
            return session.Apply(new EvidenceReviewViewModel(session.Propose("scope")).CaptureSelection());
        }
        var consumed = IdentityUnlockFile();
        var oldIds = consumed.MyExtendedFastaItems.Select(protein => protein.UserID).ToArray();
        new CurationViewModel(consumed).UnlockFamilies(oldIds);
        Repair(consumed);
        consumed.LockedUserIds.ExceptWith(oldIds);
        Repair(consumed);
        Check("Z12p5l2 retention is consumed so a later ordinary unlock still allocates fresh numbers", consumed.MyExtendedFastaItems.All(protein => !oldIds.Contains(protein.UserID)));

        var stale = IdentityUnlockFile();
        var sourceIds = stale.MyExtendedFastaItems.Select(protein => protein.UserID).ToArray();
        new CurationViewModel(stale).UnlockFamilies(sourceIds);
        for (int index = 0; index < stale.MyExtendedFastaItems.Count; index++) stale.MyExtendedFastaItems[index].UserID = 20 + index;
        stale.MyExtendedFastaItems.Add(new() { SequenceIdentifier = "unrelated-member", Sequence = "AAAAAAAAK", UserID = sourceIds[0], USearchID = sourceIds[0] });
        Repair(stale);
        Check("Z12p5l2 a retained identity without its original members cannot claim an unrelated bucket", stale.MyExtendedFastaItems.Take(2).All(protein => !sourceIds.Contains(protein.UserID))
            && stale.MyExtendedFastaItems[2].UserID == sourceIds[0]);

        var revoked = IdentityUnlockFile();
        new CurationViewModel(revoked).UnlockFamilies(revoked.MyExtendedFastaItems.Select(protein => protein.UserID).ToArray());
        var applied = Repair(revoked);
        applied.Undo!.Restore(revoked);
        string changedSource = "";
        var progress = new InlineOperationProgress(value =>
        {
            if (value.Stage != "evidence-apply" || value.Completed != 1) return;
            var identities = JsonSerializer.SerializeToNode(revoked.EvidenceRecipe!.FamilyIdentities)!.AsArray();
            foreach (var identity in identities) identity!["ReuseAfterExplicitUnlock"] = false;
            revoked.EvidenceRecipe.FamilyIdentities = identities.Deserialize<List<EvidenceFamilyIdentity>>();
            changedSource = revoked.ToJsonText();
        });
        var operation = new FamilyQuant.Operations.OperationContext(progress: progress);
        bool refused = false;
        try { EvidenceApply.ApplyRecipe(revoked, applied.Recipe, operation); }
        catch (ArgumentException) { refused = true; }
        Check("Z12p5l2 publication refuses a revoked retention choice without partial grouping", refused && !operation.HasCommitted && changedSource.Length > 0
            && revoked.ToJsonText() == changedSource);
    }
}
