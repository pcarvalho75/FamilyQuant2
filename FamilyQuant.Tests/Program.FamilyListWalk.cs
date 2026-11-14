using FamilyQuant.Evidence;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> FamilyListWalkCommands() =>
    [
        new("--family-list-walk", 448, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--family-list-walk", (a, _) => FamilyListWalkCommand(a)),
        new("--test-family-list-walk", 448, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--test-family-list-walk", (_, _) =>
        {
            FamilyListWalkTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static int FamilyListWalkCommand(string[] args)
    {
        // --unlock-all starts as a fresh project does, with no locked family, through the program's Unlock action.
        bool unlockAll = args.Contains("--unlock-all", StringComparer.Ordinal), fetchFamilies = args.Contains("--fetch-uniprot-families", StringComparer.Ordinal);
        args = args.Where(arg => arg is not ("--unlock-all" or "--fetch-uniprot-families")).ToArray();
        if (args.Length is not (4 or 5)) { Console.WriteLine("Use --family-list-walk <project.fqproj> <family-list.xlsx> <new-output-directory> [scope] [--unlock-all] [--fetch-uniprot-families]."); return 1; }
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) || File.Exists(output)) { Console.WriteLine("Use a fresh output directory."); return 1; }
        Directory.CreateDirectory(output);
        void Print(string line) { Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "walk.log"), line + Environment.NewLine); }
        var oldMessages = ShellMessages.Presenter; var oldChoice = MainWindow.UnsavedProjectChoiceOverride;
        var oldSave = MainWindow.SelectSaveProjectPathOverride;
        MainWindow? main = null; int exit = 1;
        try
        {
            EnsureApplication(); main = OffscreenProofWindow(new MainWindow());
            ShellMessages.Presenter = (_, message) => { Print("MESSAGE " + message.Text); return MessageBoxResult.OK; };
            MainWindow.UnsavedProjectChoiceOverride = () => UnsavedProjectChoice.Discard;
            MainWindow.SelectSaveProjectPathOverride = () => Path.Combine(output, "walked.fqproj");
            main.Show(); PumpDispatcher();
            Task<int> walk = AppOperationHost.OnUi(main, () => FamilyListWalkCoreAsync(main, args[1], args[2], output, args.Length == 5 ? args[4] : null, Print, unlockAll, fetchFamilies));
            while (!walk.IsCompleted) { PumpDispatcher(); Thread.Sleep(5); }
            exit = walk.GetAwaiter().GetResult(); PumpDispatcher();
        }
        catch (Exception error)
        { Print("RESULT " + error.Message); File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString()); }
        finally
        {
            main?.Close(); PumpDispatcher(); ShellMessages.Presenter = oldMessages;
            MainWindow.UnsavedProjectChoiceOverride = oldChoice; MainWindow.SelectSaveProjectPathOverride = oldSave;
        }
        return exit;
    }

    private static async Task<int> FamilyListWalkCoreAsync(MainWindow main, string project, string workbook, string output, string? scopeOverride, Action<string> print,
        bool unlockAll = false, bool fetchFamilies = false)
    {
        string projectHash = FileFingerprint.Sha256(project), workbookHash = FileFingerprint.Sha256(workbook);
        ClusterGenerationControl? screen = null;
        try
        {
            if (!await main.OpenProjectSafelyAsync(project)) throw new InvalidOperationException("The project could not be opened.");
            var grid = (Grid)main.FindName("GridApp"); screen = grid.Children.OfType<ClusterGenerationControl>().Single();
            await screen.FullXicReady;
            var file = screen.fcf;
            if (unlockAll)
                print("UNLOCKED " + screen.ViewModel.UnlockFamilies(file.LockedUserIds.ToArray()).ToString(CultureInfo.InvariantCulture) + " families");
            string scope = scopeOverride ?? string.Join(", ", (file.EvidenceRecipe?.Options.ScopeTerms
                ?? throw new InvalidOperationException("The project has no saved grouping scope. Supply scope as the final argument."))
                .Select(term => "\"" + term.Replace('"', ' ') + "\""));
            print("SCOPE " + scope);
            var transaction = new FamilyListImportTransaction(screen.ViewModel, afterEdit: screen.RefreshFamilyListImport);
            using var import = new FamilyListImportViewModel(transaction, matcher: new FamilyListStoredEvidenceMatches(file),
                workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
            import.PropertyChanged += (_, _) => grid.Children.Contains(screen);
            await import.LoadAsync(workbook);
            var pages = new List<string>(); var notices = new HashSet<string>(StringComparer.Ordinal);
            void Once(string key, string text) { if (notices.Add(key)) print(text); }
            while (!import.Imported)
            {
                string page = import.Page.ToString(CultureInfo.InvariantCulture) + ": " + import.Title;
                if (pages.LastOrDefault() != page) { pages.Add(page); print("IMPORT PAGE " + page); }
                foreach (var family in import.MatchFamilies)
                    if (family.MissingPfam) Once("missing:" + family.Key, "IMPORT ROW " + family.Key + " (" + family.Name + "): no Pfam code");
                    else if (!family.Suggestions.Any(match => match.Use && match.Proteins > 0))
                        Once("unmatched:" + family.Key, "IMPORT ROW " + family.Key + " (" + family.Name + "): no match");
                foreach (var finding in import.WorkbookFindings)
                    Once("finding:" + finding.Id, "IMPORT FINDING " + finding.Label + ": " + finding.State + "; " + finding.Reason);
                if (import.ReviewingDecisions)
                {
                    foreach (var decision in import.Decisions) print("IMPORT QUESTION decision " + decision.Source + ": " + decision.Decision + " " + decision.MergeTarget);
                    import.ConfirmDecisions();
                    if (import.ReviewingDecisions) throw new InvalidOperationException("Import needs a decision: " + import.Status);
                    continue;
                }
                if (import.Page == 3 && import.SpecialRows.Any(row => row.Blocking))
                {
                    var row = import.SpecialRows.First(candidate => candidate.Blocking); import.SelectedSpecialRow = row;
                    print("IMPORT QUESTION row " + row.RowNumber.ToString(CultureInfo.InvariantCulture) + ": " + row.Message + "; proposed " + row.Action);
                    if (!import.CanUseRowChoice) throw new InvalidOperationException("This row has no proposed choice.");
                    import.UseRowChoice();
                    if (import.SpecialRows.Any(candidate => candidate.RowNumber == row.RowNumber && candidate.Blocking))
                        throw new InvalidOperationException("Import needs a row choice: " + import.Status);
                    continue;
                }
                if (import.Page == 4 && import.PfamQuestions.Any(family => !family.Decided))
                {
                    var family = import.PfamQuestions.First(candidate => !candidate.Decided); import.SelectedMatchFamily = family;
                    print("IMPORT QUESTION Pfam " + family.Key + " (" + family.Name + "): "
                        + string.Join("; ", family.Suggestions.Select(match => match.Text + ", selected=" + match.Use + ", proteins=" + match.Proteins.ToString(CultureInfo.InvariantCulture))));
                    if (import.CanConfirmMatches) import.ConfirmMatches(message => { print("IMPORT QUESTION " + message + "; accepting proposal"); return true; });
                    else { print("IMPORT ANSWER no proposed match selected; continue without new matches"); import.SkipMatch(); }
                    if (!family.Decided) throw new InvalidOperationException("Import needs a Pfam choice: " + import.Status);
                    continue;
                }
                if (import.ReviewingSubclasses)
                    foreach (var choice in import.SubclassChoices) print("IMPORT QUESTION subclass " + choice.Title + ": " + choice.State + "; " + choice.ProblemText);
                if (!import.CanNext)
                    throw new InvalidOperationException("Import is waiting: " + import.DisabledReason + " " + import.WorkbookReviewDetails);
                if (!await import.NextAsync(message => { print("IMPORT QUESTION " + message + "; accepting proposal"); return true; }))
                    throw new InvalidOperationException("Import could not advance: " + import.Status);
            }
            print("IMPORT ACCEPTED " + import.Status);
            var session = new EvidenceGroupingSession(file, AppVersionInfo.Current(), screen.EvidenceSearchOverride);
            var preview = await Task.Run(() => new SuperClusterPreview(file.MyExtendedFastaItems, null, "", "", KeywordMatch.All, null, file.LockedUserIds));
            var review = new SuperClusterPreviewWindow(preview, null, session.Propose, session.CountMotif, initialScope: scope, evidenceMode: true,
                confirmPanelLinks: session.ConfirmPanelLinks, previewScope: session.PreviewScopeAsync, addToFamily: session.AddToFamily,
                removeFromFamily: session.RemoveFromFamily, updateManualChoices: session.UpdateManualChoices,
                createMatchContext: session.CreatePanelMatchContext, confirmPanelMatches: session.ConfirmPanelEvidenceMatches,
                panelCoverage: session.PanelCoverageStatus, panelCovered: session.PanelScopeCovered,
                fullPanelRequirement: session.FullPanelRequirement, hasFamilyList: true) { Owner = main };
            try
            {
                var model = (GroupingReviewViewModel)review.DataContext;
                model.PropertyChanged += (_, _) => grid.Children.Contains(screen);
                var run = typeof(SuperClusterPreviewWindow).GetMethod("RunReviewAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                await (Task)run.Invoke(review, new object[] { "Update grouping proposal", (Func<Task>)model.UpdateProposalAsync, false })!;
                var evidence = model.Evidence ?? throw new InvalidOperationException("Review grouping has no proposal: " + model.Status);
                var pending = evidence.PanelLinks.Where(row => row.Source.NeedsConfirmation).OrderBy(row => row.Source.Key, StringComparer.Ordinal).ToArray();
                print("PANEL LINKS NEEDING A CHOICE " + pending.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var row in pending) print("PANEL LINK " + row.SourceFamily + " | " + row.Architecture + " | rows: " + row.Candidates);
                var checklist = Enum.GetValues<PanelChecklistState>().ToDictionary(state => state.ToString(), state => evidence.PanelChecklist.Count(row => row.State == state));
                print("CHECKLIST " + evidence.PanelChecklistSummary);
                foreach (var state in checklist) print("CHECKLIST STATE " + state.Key + "=" + state.Value.ToString(CultureInfo.InvariantCulture));
                print("DESTINATIONS " + evidence.ListDestinationText + " " + evidence.SequenceDestinationText + " " + evidence.OutsideScopeDestinationText);
                print("APPLY ENABLED " + model.CanConfirm);
                bool applied = false, saved = false;
                if (pending.Length > 0 || !model.CanConfirm)
                    print("APPLY WAITING " + (model.IsWaitingForPanelLinks ? model.PanelLinksWaitText : model.ApplyHint));
                else
                {
                    var apply = typeof(ClusterGenerationControl).GetMethod("ApplyEvidenceChoice", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    await (Task)apply.Invoke(screen, new object[] { session, evidence.CaptureSelection() with { MotifPattern = model.MotifPattern } })!;
                    await screen.FullXicReady;
                    print("APPLY RESULT " + screen.ShellStatusText);
                    if (screen.ShellStatusText.Contains("No grouping was applied:", StringComparison.Ordinal) || file.EvidenceRecipe == null)
                        throw new InvalidOperationException("Apply grouping did not complete.");
                    applied = true;
                    if (fetchFamilies)
                    {
                        // XP4: Fetch UniProt families after Apply grouping, as the panel button does. A stored task older than UniProt's
                        // release cannot fetch, so the walk then discovers the task again, as for a project without a stored world.
                        screen.ConfirmFamilyWorldScopeOverride = proposal => EvidenceTaskDiscovery.Confirm(file, proposal, file.EvidenceRecipe!.Options.ScopeTerms);
                        await screen.FetchFamilyWorldAsync();
                        print("FETCH UNIPROT FAMILIES " + screen.ShellStatusText);
                        if (screen.ShellStatusText.Contains("could not finish", StringComparison.Ordinal) && file.EvidenceSource?.FamilyWorld != null)
                        {
                            file.EvidenceSource.FamilyWorld = null;
                            await screen.FetchFamilyWorldAsync();
                            print("FETCH UNIPROT FAMILIES " + screen.ShellStatusText);
                        }
                        print("FAMILY ASSIGNMENTS CURRENT " + (PanelAssignmentEvidenceStore.Current(file) != null));
                    }
                    var save = typeof(MainWindow).GetMethod("SaveProjectAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
                    await (Task)save.Invoke(main, new object[] { true })!;
                    saved = File.Exists(Path.Combine(output, "walked.fqproj"));
                    if (!saved) throw new IOException("The screen did not save walked.fqproj.");
                }
                File.WriteAllText(Path.Combine(output, "walk.json"), JsonSerializer.Serialize(new
                {
                    Imported = import.Imported, ImportPages = pages, PendingPanelLinks = pending.Length,
                    ChecklistStates = checklist, evidence.ProteinsInList, evidence.InScopeProteinsOutsideList, evidence.ProteinsOutsideScope,
                    ApplyEnabled = model.CanConfirm, Applied = applied, Saved = saved,
                    WaitingLine = applied ? "" : model.IsWaitingForPanelLinks ? model.PanelLinksWaitText : model.ApplyHint,
                }, new JsonSerializerOptions { WriteIndented = true }));
                print("RESULT walk completed; applied=" + applied + "; saved=" + saved);
            }
            finally { review.Close(); }
            return 0;
        }
        finally
        {
            if (screen != null) await screen.FullXicReady;
            if (projectHash != FileFingerprint.Sha256(project) || workbookHash != FileFingerprint.Sha256(workbook))
                throw new InvalidDataException("The input project or workbook changed.");
        }
    }

    private static void FamilyListWalkTests()
    {
        EnsureApplication();
        string root = Path.Combine(Path.GetTempPath(), "family-list-walk", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var file = new FamilyClusterFile
        {
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "sp|P63001|FIXTURE", Description = "First synthetic protein", Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = 1, USearchID = 1,
                    Evidence = new() { Accession = "P63001", Reviewed = true, Family = "First supplied family", Keywords = new() { "selected" }, Pfam = new() { new() { Accession = "PF12345" } } } },
                new() { SequenceIdentifier = "sp|P63002|FIXTURE", Description = "Second synthetic protein", Sequence = "WWWWWWWWWWWWWWWWWWWW", UserID = 2, USearchID = 2,
                    Evidence = new() { Accession = "P63002", Reviewed = true, Family = "Second supplied family", Keywords = new() { "selected" }, Pfam = new() { new() { Accession = "PF54321" } } } },
                new() { SequenceIdentifier = "tr|P63003|FIXTURE", Description = "Third synthetic protein", Sequence = "TTTTTTTTTTTTTTTTTTTT", UserID = 3, USearchID = 3,
                    Evidence = new() { Accession = "P63003", Reviewed = false } },
            },
            EvidenceSource = new() { ReferencePanel = new() { Families = new() { "Stored reference family" }, Entries = new()
            {
                new() { Accession = "P63004", Sequence = "YYYYYYYYYYYYYYYYYYYY", Evidence = new() { Accession = "P63004", Reviewed = true,
                    Family = "Stored reference family", Keywords = new() { "selected" }, Pfam = new() { new() { Accession = "PF98765", Name = "Stored reference domain" } } } },
            } } },
            EvidenceRecipe = new() { Options = new() { ScopeTerms = new[] { "selected" } } },
            LockedUserIds = new() { 1 },
        };
        string project = Path.Combine(root, "fixture.fqproj"); ProjectStore.WriteFqproj(project, file, Array.Empty<QuantRow>(), new QuantManifest());
        string projectHash = TestFileHash.Upper(project);
        foreach (bool tied in new[] { false, true })
        {
            string workbook = Path.Combine(root, tied ? "tied.xlsx" : "plain.xlsx"); PanelImportTemplate.Write(workbook);
            var values = new List<(string Key, string Name, string Pfam)> { ("A", "First user family", "PF12345"), ("B", "Second user family", tied ? "PF12345" : "") };
            if (!tied) values.Add(("C", "Third user family", "PF98765"));
            FillTemplate(workbook, values.Select(value => PanelImportTemplate.Columns.Select(column => column.Role switch
            { PanelImportFields.FamilyKey => value.Key, PanelImportFields.DisplayName => value.Name, PanelImportFields.Pfam => value.Pfam, _ => "" }).ToArray()).ToArray());
            string workbookHash = TestFileHash.Upper(workbook), output = Path.Combine(root, tied ? "tied" : "plain");
            int exit = FamilyListWalkCommand(tied ? new[] { "--family-list-walk", project, workbook, output } : new[] { "--family-list-walk", project, workbook, output, "--unlock-all" });
            Check("AP6-tool " + (tied ? "pending" : "ready") + " walk follows real import and grouping view models", exit == 0);
            if (exit != 0) continue;
            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "walk.json"))); var json = summary.RootElement;
            string log = File.ReadAllText(Path.Combine(output, "walk.log"));
            Check("AP6-tool " + (tied ? "pending" : "ready") + " import follows the three-page route", json.GetProperty("ImportPages").EnumerateArray()
                .Select(page => page.GetString()!.Split(':')[0]).SequenceEqual(new[] { "1", "2", "5" }));
            if (tied)
                Check("AP6-tool pending links print their rows and waiting line without applying or saving", json.GetProperty("PendingPanelLinks").GetInt32() > 0
                    && !json.GetProperty("ApplyEnabled").GetBoolean() && !json.GetProperty("Applied").GetBoolean() && !File.Exists(Path.Combine(output, "walked.fqproj"))
                    && log.Contains("PANEL LINK ", StringComparison.Ordinal) && log.Contains("Waiting for ", StringComparison.Ordinal));
            else
            {
                var walked = ProjectStore.ReadFqproj(Path.Combine(output, "walked.fqproj"));
                Check("AP6-tool enabled Apply grouping and normal Save As produce the walked project", json.GetProperty("Applied").GetBoolean()
                    && json.GetProperty("Saved").GetBoolean() && walked.fcf.FamilyPanel?.Families.Count == 3 && walked.fcf.EvidenceRecipe!.Assignments.Count > 0);
                Check("GA5-tool --unlock-all unlocks every family first, so the locked member is applied", log.Contains("UNLOCKED 1 families", StringComparison.Ordinal)
                    && walked.fcf.EvidenceRecipe!.Assignments.Any(a => a.ProteinIndex == 0));
                Check("AP6-tool prints checklist/destination states and names missing-Pfam and no-match rows once", json.GetProperty("ProteinsInList").GetInt32() == 1
                    && json.GetProperty("InScopeProteinsOutsideList").GetInt32() == 1 && json.GetProperty("ProteinsOutsideScope").GetInt32() == 1
                    && log.Split('\n').Count(line => line.StartsWith("IMPORT ROW B ", StringComparison.Ordinal)) == 1
                    && log.Split('\n').Count(line => line.StartsWith("IMPORT ROW C ", StringComparison.Ordinal)) == 1
                    && log.Contains("CHECKLIST STATE NoPfamCode=1", StringComparison.Ordinal) && log.Contains("CHECKLIST STATE NoDatabaseMatch=1", StringComparison.Ordinal));
            }
            Check("AP6-tool " + (tied ? "pending" : "ready") + " walk leaves both input files unchanged", TestFileHash.Upper(project) == projectHash && TestFileHash.Upper(workbook) == workbookHash);
        }
    }
}
