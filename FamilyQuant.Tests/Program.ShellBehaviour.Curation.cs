using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace FamilyQuant.Tests;
internal static partial class Program
{
    private static void ShellBehaviourCuration(Assembly legacy, string output,
        Action<string[], string, bool, object?, object?, string> record,
        Action<string[], string, Action> attempt)
    {
        void R(string[] ids, string name, bool pass, object? before, object? actual) => record(ids, name, pass, before, actual, pass ? "" : "Native compared values did not establish the complete old fact.");
        Window Capture(Action action, Action<Window> inspect)
        {
            var existing = Application.Current.Windows.Cast<Window>().ToArray(); Window? result = null; Exception? error = null;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                var child = Application.Current.Windows.Cast<Window>().FirstOrDefault(window => !existing.Contains(window) && window.IsLoaded);
                if (child == null) return;
                timer.Stop(); result = child;
                try { inspect(child); } catch (Exception ex) { error = ex; } finally { child.Close(); }
            };
            timer.Start();
            try
            {
                action(); var watch = System.Diagnostics.Stopwatch.StartNew();
                while (result == null && watch.Elapsed < TimeSpan.FromSeconds(12)) { PumpDispatcher(); System.Threading.Thread.Sleep(5); }
                if (error != null) throw error;
                return result ?? throw new InvalidOperationException("No owned result window appeared.");
            }
            finally { timer.Stop(); }
        }
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "A0A710", Description = "Copper tagged entry", Sequence = "PEPTIDEK", UserID = 1, USearchID = 1 },
            new() { SequenceIdentifier = "owned-b", Description = "Copper secondary entry", Sequence = "PEPTIDER", UserID = 2, USearchID = 2 },
        } };
        file.LockedUserIds.Add(1);
        object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!;
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.ClusterGenerationControl", true)!, oldFile)!;
        var current = new ClusterGenerationControl(file);
        var oldHost = SecondaryHost(new Window { Content = old }); var host = SecondaryHost(new Window { Content = current });
        try
        {
            attempt(["3F6F786470BC", "6F365062E131"], "parent lock-manager removal", () =>
            {
                object Remove(UserControl view, object document)
                {
                    int[] before = ((IEnumerable)SecondaryRead(document, "LockedUserIds")!).Cast<int>().ToArray();
                    string[] ids = ((IEnumerable)SecondaryRead(document, "MyExtendedFastaItems")!).Cast<object>().Select(item => SecondaryRead(item, "UserID")!.ToString()!).ToArray();
                    Capture(() => SecondaryClick((Button)view.FindName("ButtonManageLocks")), window =>
                    {
                        var grid = WalkTree(window).OfType<DataGrid>().Single(); grid.SelectedItems.Add(grid.Items[0]); PumpDispatcher();
                        SecondaryClick(WalkTree(window).OfType<Button>().Single(button => button.Content as string == "Remove selected locks"));
                    });
                    int[] after = ((IEnumerable)SecondaryRead(document, "LockedUserIds")!).Cast<int>().ToArray();
                    string[] finalIds = ((IEnumerable)SecondaryRead(document, "MyExtendedFastaItems")!).Cast<object>().Select(item => SecondaryRead(item, "UserID")!.ToString()!).ToArray();
                    return new { before, after, groupsUnchanged = ids.SequenceEqual(finalIds) };
                }
                var before = Remove(old, oldFile); var actual = Remove(current, file);
                R(["3F6F786470BC", "6F365062E131"], "parent lock-manager removal", System.Text.Json.JsonSerializer.Serialize(before) == System.Text.Json.JsonSerializer.Serialize(actual)
                    && file.LockedUserIds.Count == 0, before, actual);
            });
            attempt(["DE649731E000", "380C31A423D5", "7696D63AAAAF"], "curation Help replacement route", () =>
            {
                string before = "", actual = "";
                Capture(() => SecondaryClick((Button)old.FindName("ButtonHelp")), window => before = WalkTree(window).OfType<TextBox>().Single().Text);
                Capture(() => current.ShowHelp(), window => actual = WalkTree(window).OfType<TextBox>().Single().Text);
                string mode = NormalizationMode.IdentifiedSignal.DisplayName();
                R(["DE649731E000", "380C31A423D5", "7696D63AAAAF"], "curation Help replacement route",
                    before.Contains(mode, StringComparison.Ordinal) && actual.Contains(mode, StringComparison.Ordinal)
                    && before.Contains("denominator", StringComparison.OrdinalIgnoreCase) && actual.Contains("denominator", StringComparison.OrdinalIgnoreCase), before, actual);
            });
            attempt(["A6FE034CDA86", "888F04EA14C2"], "native selected-protein alignment", () =>
            {
                string Align(UserControl view)
                {
                    var grid = (DataGrid)view.FindName("DataGridResultGrid"); foreach (var item in grid.Items.Cast<object>().Take(2).ToArray()) grid.SelectedItems.Add(item); PumpDispatcher();
                    string text = ""; Capture(() => SecondaryClick((Button)view.FindName("ButtonAlignSelected")), window => text = WalkTree(window).OfType<TextBox>().Single().Text); return text;
                }
                int[] ids = file.MyExtendedFastaItems.Select(item => item.UserID).ToArray();
                string before = Align(old), actual = Align(current);
                string Alignment(string value) => value[value.IndexOf("Query >", StringComparison.Ordinal)..];
                R(["A6FE034CDA86", "888F04EA14C2"], "native selected-protein alignment", Alignment(before) == Alignment(actual) && actual.Contains("PEPTIDE", StringComparison.Ordinal)
                    && ids.SequenceEqual(file.MyExtendedFastaItems.Select(item => item.UserID)), before, actual);
            });
            attempt(["5E1A13AD8818", "5BB26FE5A141"], "recorded HTTP Fetch domains and cancellation", () =>
            {
                using var oldHttp = new EvidenceAndPanelHandler(); using var http = new EvidenceAndPanelHandler();
                old.GetType().GetProperty("FetchDomainsHandlerOverride")!.SetValue(old, oldHttp); current.FetchDomainsHandlerOverride = http;
                using (var dialogs = new NativeOwnedDialogDriver(null))
                {
                    SecondaryClick((Button)old.FindName("ButtonFetchDomains"));
                    var watch = System.Diagnostics.Stopwatch.StartNew(); while (!old.IsEnabled && watch.Elapsed < TimeSpan.FromSeconds(15)) { PumpDispatcher(); System.Threading.Thread.Sleep(5); }
                }
                SecondaryClick((Button)current.FindName("ButtonFetchDomains")); FinishBusyOperation(current.Operations); PumpDispatcher();
                string before = (string)SecondaryRead(((IEnumerable)SecondaryRead(oldFile, "MyExtendedFastaItems")!).Cast<object>().First(), "Domains")!;
                string actual = file.MyExtendedFastaItems[0].Domains;
                string state = file.ToJsonText(); using var cancelled = new EvidenceAndPanelHandler(); cancelled.DuringRequest = (_, _) => current.Operations.Cancel();
                current.FetchDomainsHandlerOverride = cancelled; SecondaryClick((Button)current.FindName("ButtonFetchDomains")); FinishBusyOperation(current.Operations); PumpDispatcher();
                R(["5E1A13AD8818", "5BB26FE5A141"], "recorded HTTP Fetch domains and cancellation", before.Length > 0 && before == actual
                    && oldHttp.ProteinRequests > 0 && http.ProteinRequests > 0 && file.MyExtendedFastaItems[0].Evidence != null && state == file.ToJsonText(),
                    new { before, oldHttp.ProteinRequests }, new { actual, http.ProteinRequests, http.PanelRequests, cancelled = state == file.ToJsonText() });
            });
            attempt(["8F2B3AC0D40F", "C9115FC95070", "13FCD8644268", "1CEC563FC75F", "2501EF0B01C9", "C24E78EB3E6D", "0F3C5F5D43F5"], "inline-to-review native keyword controls", () =>
            {
                var box = (TextBox)old.FindName("TextBoxSuperClusterWording"); var button = (Button)old.FindName("ButtonSuperCluster");
                box.Text = ""; PumpDispatcher(); bool disabled = !button.IsEnabled;
                box.Text = "copper"; PumpDispatcher(); bool enabled = button.IsEnabled;
                box.Text = ""; PumpDispatcher(); disabled &= !button.IsEnabled;
                SecondarySuperRuntime(legacy, output, Themes.AppTheme.Dark, (source, first, last, operation, before, actual, passed, symbol, include) =>
                {
                    string[] ids = operation switch
                    {
                        "actual keyword TextChanged recomputes the same offered rows" => ["8F2B3AC0D40F", "C9115FC95070"],
                        "actual exclusion TextChanged removes only the same offered proteins" => ["C24E78EB3E6D"],
                        "actual match-mode SelectionChanged recomputes the same choices" => ["13FCD8644268", "1CEC563FC75F", "2501EF0B01C9"],
                        _ => []
                    };
                    if (ids.Length > 0) R(ids, operation, passed && disabled && enabled, before, actual);
                });
                // The old button's blank/nonblank gate remains represented by the replacement review's CanApply gate.
                var noChange = file.MyExtendedFastaItems.Select(item => new FastaItemExtended
                { SequenceIdentifier = item.SequenceIdentifier, Sequence = item.Sequence, Description = item.Description, UserID = 1, USearchID = 1 }).ToList();
                var blank = new SuperClusterPreview(noChange, null, "", "", KeywordMatch.All, null, null);
                var review = SecondaryHost(new SuperClusterPreviewWindow(blank));
                try
                {
                    var apply = (Button)review.FindName("createButton");
                    var offered = (DataGrid?)review.GetType().GetField("grid", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(review)
                        ?? WalkTree(review).OfType<DataGrid>().First(value => value.IsVisible && value.Items.Count > 0);
                    offered.SelectAll();
                    SecondaryCall(review, review.GetType(), "UntickSelected_Click", review, new RoutedEventArgs()); PumpDispatcher();
                    R(["0F3C5F5D43F5"], "blank keyword Apply gate", disabled && enabled && !apply.IsEnabled, "old blank false/nonblank true", apply.IsEnabled);
                }
                finally { review.Close(); }
            });
            attempt(["C4526422A15C", "D36A80A151FF", "8921B21922FE", "4B1A5D805051"], "native shell context and workflow Help", () =>
            {
                oldHost.Content = null; host.Content = null;
                var oldMain = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.MainWindow", true)!)!;
                var main = new MainWindow();
                void Set(object target, string name, object? value) => target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.SetValue(target, value);
                void Mount(Window shell, UserControl content, string field)
                {
                    if (content.Parent is Panel panel) panel.Children.Remove(content);
                    else if (content.Parent is ContentControl parent) parent.Content = null;
                    else if (content.Parent is Decorator decorator) decorator.Child = null;
                    var grid = (Grid)shell.FindName("GridApp"); grid.Children.Clear(); grid.Children.Add(content);
                    Set(shell, "cgc", field == "cgc" ? content : null); Set(shell, "dnc", field == "dnc" ? content : null);
                    SecondaryCall(shell, shell.GetType(), "UpdateGuide"); PumpDispatcher();
                }
                string Open(Window shell, string menu)
                {
                    string text = "";
                    var item = (MenuItem)shell.FindName(menu);
                    if (!item.IsEnabled) throw new InvalidOperationException("Help command is disabled.");
                    Capture(() => typeof(MenuItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(item, null), window =>
                    { if (window.Owner != shell) throw new InvalidOperationException("Help window lost its owner."); text = WalkTree(window).OfType<TextBox>().Single().Text; });
                    return text;
                }
                SecondaryHost(oldMain); SecondaryHost(main);
                try
                {
                    Mount(oldMain, old, "cgc"); Mount(main, current, "cgc");
                    string familyBefore = Open(oldMain, "MenuItemHowToRead"), familyActual = Open(main, "MenuItemHowToRead");
                    var oldDn = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.DeNovoControl", true)!)!; var dn = new DeNovoControl();
                    Mount(oldMain, oldDn, "dnc"); Mount(main, dn, "dnc");
                    string dnBefore = Open(oldMain, "MenuItemHowToRead"), dnActual = Open(main, "MenuItemHowToRead");
                    bool context = familyBefore.Contains("denominator", StringComparison.OrdinalIgnoreCase) && familyActual.Contains("denominator", StringComparison.OrdinalIgnoreCase)
                        && dnBefore.Contains("confidence", StringComparison.OrdinalIgnoreCase) && dnActual.Contains("confidence", StringComparison.OrdinalIgnoreCase)
                        && dnBefore != familyBefore && dnActual != familyActual;
                    R(["C4526422A15C", "D36A80A151FF"], "native shell context Help", context,
                        new { familyBefore, dnBefore }, new { familyActual, dnActual });
                    string workflowBefore = Open(oldMain, "MenuItemWhereAmI"), workflowActual = Open(main, "MenuItemWhereAmI");
                    R(["8921B21922FE", "4B1A5D805051"], "native shell workflow Help", workflowBefore.Contains("WHAT DO I DO FIRST?", StringComparison.Ordinal)
                        && workflowActual.Contains("WHAT DO I DO FIRST?", StringComparison.Ordinal) && workflowBefore.Contains("You are at:", StringComparison.Ordinal)
                        && workflowActual.Contains("You are at:", StringComparison.Ordinal), workflowBefore, workflowActual);
                    Mount(main, current, "cgc"); string f1 = "";
                    Capture(() => ApplicationCommands.Help.Execute(null, main), window => f1 = WalkTree(window).OfType<TextBox>().Single().Text);
                    R(["DE649731E000", "380C31A423D5", "7696D63AAAAF"], "native Families F1 command routes to owned denominator Help",
                        main.InputBindings.OfType<KeyBinding>().Any(binding => binding.Key == Key.F1 && binding.Command == ApplicationCommands.Help)
                        && f1.Contains("denominator", StringComparison.OrdinalIgnoreCase), familyBefore, f1);
                }
                finally { oldMain.Close(); main.Close(); }
            });
        }
        finally { host.Close(); oldHost.Close(); }
        attempt(["79AFA7E12F90"], "native multiline report control", () =>
        {
            const string text = "first line\nsecond line\nthird line";
            var oldWindow = (Window)SecondaryCall(null, legacy.GetType("FamilyQuant.TextReportWindow", true)!, "Create", "Native report", text, false)!;
            var window = TextReportWindow.Create("Native report", text); SecondaryHost(oldWindow); SecondaryHost(window);
            try
            {
                var a = WalkTree(oldWindow).OfType<TextBox>().Single(); var b = WalkTree(window).OfType<TextBox>().Single();
                a.SelectAll(); b.SelectAll(); a.ScrollToEnd(); b.ScrollToEnd();
                R(["79AFA7E12F90"], "native multiline report control", a.IsReadOnly && b.IsReadOnly && a.SelectedText == text && b.SelectedText == text
                    && ApplicationCommands.Copy.CanExecute(null, a) && ApplicationCommands.Copy.CanExecute(null, b),
                    new { a.Text, a.IsReadOnly, a.SelectedText }, new { b.Text, b.IsReadOnly, b.SelectedText });
            }
            finally { oldWindow.Close(); window.Close(); }
        });
    }
}
