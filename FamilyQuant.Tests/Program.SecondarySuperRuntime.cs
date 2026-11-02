using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondarySuperRuntime(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add, bool negative = false)
    {
        const string source = "FamilyQuant/SuperClusterPreviewWindow.cs";
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "native-a", Description = "Copper tagged entry", Sequence = "ACDEFGHIK", UserID = 1, Domains = "DomainOne" },
            new() { SequenceIdentifier = "native-b", Description = "Other sibling", Sequence = "VVVVVVVVK", UserID = 1, Domains = "DomainTwo" },
            new() { SequenceIdentifier = "native-c", Description = "Copper secondary entry", Sequence = "AAAAAAAK", UserID = 2, Domains = "DomainOne" },
            new() { SequenceIdentifier = "native-d", Description = "Copper hidden entry", Sequence = "GGGGGGGK", UserID = 3, Domains = "DomainThree" },
        } };
        object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!;
        object oldPreview = Activator.CreateInstance(legacy.GetType("FamilyQuant.SuperClusterPreview", true)!, new object?[]
            { SecondaryRead(oldFile, "MyExtendedFastaItems"), new HashSet<int> { 1, 2 }, "copper", "", Enum.Parse(legacy.GetType("FamilyQuant.KeywordMatch", true)!, "Any"), null, null })!;
        var preview = new SuperClusterPreview(file.MyExtendedFastaItems, new HashSet<int> { 1, 2 }, "copper", "", KeywordMatch.Any, null, null);
        var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.SuperClusterPreviewWindow", true)!, oldPreview)!;
        var current = new SuperClusterPreviewWindow(preview); SecondaryHost(old); SecondaryHost(current);
        try
        {
            FrameworkElement Find(Window window, string name, string? binding = null)
            {
                var named = SecondaryNamed(window, name) ?? window.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window) as FrameworkElement;
                if (named != null) return named;
                if (binding != null) return WalkTree(window).OfType<TextBox>().Single(box => BindingOperations.GetBinding(box, TextBox.TextProperty)?.Path?.Path == binding);
                string? tip = name switch { "keywordsBox" => "Keywords separated", "excludeBox" => "Whole words", _ => null };
                if (tip != null) return WalkTree(window).OfType<TextBox>().Single(box => (box.ToolTip as string)?.StartsWith(tip, StringComparison.Ordinal) == true);
                throw new MissingMemberException(window.GetType().FullName, name);
            }
            var oldGrid = (DataGrid)Find(old, "grid"); var grid = (DataGrid)Find(current, "grid");
            Button Button(Window window, string text) => WalkTree(window).OfType<Button>().Single(button => button.IsVisible && button.Content as string == text);
            object[] Rows(object value) => ((IEnumerable)SecondaryRead(value, "Proteins")!).Cast<object>().ToArray();
            string[] Snapshot(object value) => Rows(value).Select(row => string.Join("/", new[] { "Locus", "Family", "Join", "HasKeyword" }
                .Select(property => Convert.ToString(SecondaryRead(row, property), CultureInfo.InvariantCulture)))).ToArray();
            bool Same() => Snapshot(oldPreview).SequenceEqual(Snapshot(preview));
            object? Caption(object item) => item is ComboBoxItem choice ? choice.Content : item;
            var oldMatch = WalkTree(old).OfType<ComboBox>().Single(combo => combo.Items.Cast<object>().Any(item => Caption(item) as string == "match any word"));
            var match = WalkTree(current).OfType<ComboBox>().Single(combo => combo.IsVisible && combo.Items.Cast<object>().Any(item => Caption(item) as string == "match any word"));
            add(source, 75, 75, "native keyword match selector is reachable", oldMatch.GetType().Name, match.GetType().Name,
                oldMatch.IsVisible && match.IsVisible, "SuperClusterPreviewWindow keyword match ComboBox", row => row.Kind == "code control");
            add(source, 81, 82, "native match-all/any choice captions", oldMatch.Items.Cast<object>().Select(Caption).ToArray(),
                match.Items.Cast<object>().Select(Caption).ToArray(), oldMatch.Items.Cast<object>().Select(Caption)
                    .SequenceEqual(match.Items.Cast<object>().Select(Caption)), "keyword match choice copy");
            for (int index = 0; index < grid.Columns.Count; index++)
            {
                int[] lines = { 290, 300, 301, 302, 304, 305, 306 };
                object? oldHeader = oldGrid.Columns[index].Header, header = grid.Columns[index].Header;
                add(source, lines[index], lines[index], "native keyword table header and source-row projection", oldHeader, header,
                    Equals(oldHeader, header) && Same(), "keyword grid column " + index,
                    row => row.Kind == "code fact" && row.Detail == System.Text.Json.JsonSerializer.Serialize(oldHeader));
            }
            add(source, 257, 257, "native keyword table keeps every offered source row", Snapshot(oldPreview), Snapshot(preview),
                oldGrid.IsVisible && grid.IsVisible && Same(), "keyword protein DataGrid", row => row.Kind == "code control");
            add(source, 288, 288, "native tick column remains read-only and bound to each row's choice", oldGrid.Columns[0].GetType().Name,
                grid.Columns[0].GetType().Name, oldGrid.Columns[0].IsReadOnly && grid.Columns[0].IsReadOnly && Same(), "keyword tick template column", row => row.Kind == "code control");
            add(source, 315, 315, "native keyword text columns preserve their exact header and binding set", oldGrid.Columns.Skip(1).Select(column => column.Header).ToArray(),
                grid.Columns.Skip(1).Select(column => column.Header).ToArray(), oldGrid.Columns.Skip(1).Select(column => column.Header)
                    .SequenceEqual(grid.Columns.Skip(1).Select(column => column.Header)), "keyword DataGridTextColumns", row => row.Kind == "code control");
            var actions = new[] { (117, "Untick the red ones"), (119, "Tick all"), (120, "Untick selected"), (122, "Tick selected"), (153, "Apply") };
            var effects = new List<string>();
            foreach (var action in actions.Take(4))
            {
                if (action.Item2.Contains("selected", StringComparison.Ordinal))
                {
                    oldGrid.SelectedItems.Clear(); grid.SelectedItems.Clear();
                    oldGrid.SelectedItems.Add(Rows(oldPreview)[0]); grid.SelectedItems.Add(preview.Proteins[0]);
                }
                SecondaryClick(Button(old, action.Item2)); SecondaryClick(Button(current, action.Item2)); PumpDispatcher();
                add(source, action.Item1, action.Item1, "actual keyword bulk action preserves exact frozen row choices: " + action.Item2,
                    Snapshot(oldPreview), Snapshot(preview), Same(), "keyword action " + action.Item2,
                    row => row.Kind == "code fact" && row.Detail == System.Text.Json.JsonSerializer.Serialize(action.Item2));
                effects.Add(action.Item2 + ":" + Same());
            }
            add(source, 243, 243, "native bulk-action buttons execute their bound choice changes", "four frozen/current action effects", effects,
                effects.Count == 4 && effects.All(value => value.EndsWith("True", StringComparison.Ordinal)), "keyword action buttons", row => row.Kind == "code control");
            add(source, 251, 251, "actual bulk-action Click handlers reach row choices and refresh the displayed state", "four exact row-choice comparisons", effects,
                effects.All(value => value.EndsWith("True", StringComparison.Ordinal)), "keyword action button Click handlers", row => row.Kind == "handler");
            var oldExclude = (TextBox)Find(old, "excludeBox"); var exclude = (TextBox)Find(current, "excludeBox", "ExcludeText");
            oldExclude.Text = "secondary"; exclude.Text = "secondary"; PumpDispatcher();
            add(source, 198, 198, "actual exclusion TextChanged removes only the same offered proteins", Snapshot(oldPreview), Snapshot(preview), Same(),
                "keyword exclusion text event", row => row.Kind == "handler");
            oldExclude.Text = ""; exclude.Text = ""; PumpDispatcher();
            var oldKeywords = (TextBox)Find(old, "keywordsBox"); var keywords = (TextBox)Find(current, "keywordsBox", "KeywordsText");
            oldKeywords.Text = "secondary"; keywords.Text = "secondary"; PumpDispatcher();
            add(source, 215, 215, "grouping screen text editors accept actual keyword/exclusion edits and reach the same preview rows",
                new[] { oldKeywords.Text, oldExclude.Text }, new[] { keywords.Text, exclude.Text },
                oldKeywords.IsVisible && keywords.IsVisible && oldExclude.IsVisible && exclude.IsVisible && Same()
                    && oldKeywords.Text == keywords.Text && oldExclude.Text == exclude.Text,
                "grouping screen native keyword/exclusion TextBox controls", row => row.Kind == "code control");
            add(source, 197, 197, "actual keyword TextChanged recomputes the same offered rows", Snapshot(oldPreview), Snapshot(preview), Same(),
                "keyword text event", row => row.Kind == "handler");
            oldKeywords.Text = "copper"; keywords.Text = "copper"; PumpDispatcher();
            oldMatch.SelectedIndex = 0; match.SelectedIndex = 0; PumpDispatcher();
            add(source, 199, 199, "actual match-mode SelectionChanged recomputes the same choices", Snapshot(oldPreview), Snapshot(preview), Same(),
                "keyword match SelectionChanged", row => row.Kind == "handler");
            var oldHidden = (CheckBox)Find(old, "hiddenBox"); var hidden = (CheckBox)Find(current, "hiddenBox");
            oldHidden.IsChecked = true; hidden.IsChecked = true;
            oldHidden.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); hidden.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent)); PumpDispatcher();
            add(source, 108, 108, "native hidden-family checkbox is reachable", oldHidden.GetType().Name, hidden.GetType().Name,
                oldHidden.IsVisible && hidden.IsVisible, "keyword hidden families checkbox", row => row.Kind == "code control");
            add(source, 204, 204, "actual hidden-family checkbox includes the same hidden rows", Snapshot(oldPreview), Snapshot(preview), Same(),
                "keyword hidden-family Click", row => row.Kind == "handler");
            var oldDomains = (TextBox)Find(old, "domainsBox"); var domains = (TextBox)Find(current, "domainsBox");
            var oldList = (ComboBox)Find(old, "domainsList"); var list = (ComboBox)Find(current, "domainsList");
            add(source, 125, 125, "native domain-choice editor is reachable", oldDomains.GetType().Name, domains.GetType().Name,
                oldDomains.IsVisible && domains.IsVisible, "keyword domain TextBox", row => row.Kind == "code control");
            add(source, 134, 134, "native domain list exposes the same count-sorted choices", oldList.Items.Cast<string>().ToArray(), list.Items.Cast<string>().ToArray(),
                oldList.Items.Cast<string>().SequenceEqual(list.Items.Cast<string>()), "keyword domain ComboBox", row => row.Kind == "code control");
            string chosen = oldList.Items.Cast<string>().First(); oldList.SelectedItem = chosen; list.SelectedItem = chosen; PumpDispatcher();
            add(source, 141, 141, "actual domain SelectionChanged writes the name without its count and resets selection", oldDomains.Text, domains.Text,
                oldDomains.Text == domains.Text && oldList.SelectedItem == null && list.SelectedItem == null, "keyword domain SelectionChanged", row => row.Kind == "handler");
            SecondaryClick(Button(old, "Apply")); SecondaryClick(Button(current, "Apply")); PumpDispatcher();
            add(source, 153, 153, "actual domain Apply preserves the same row choices", Snapshot(oldPreview), Snapshot(preview), Same(), "keyword domain Apply",
                row => row.Kind == "code fact" && row.Detail == "\"Apply\"");
            string[] oldCounts = oldList.Items.Cast<string>().ToArray(), counts = list.Items.Cast<string>().ToArray();
            add(source, 352, 352, "native domain count formatting and parentheses match the frozen choices", oldCounts, counts,
                oldCounts.SequenceEqual(counts), "keyword domain choices N0/count punctuation");
            var create = (Button)Find(current, "createButton"); var oldCreate = (Button)Find(old, "createButton");
            string Label(string role) => WalkTree(current).OfType<TextBlock>().First(block => block.IsVisible && block.Text.TrimEnd(':') == role).Text;
            SecondaryWording(output, "grouping-" + theme, source, new Dictionary<int, string> {
                [48] = current.Title, [178] = create.Content?.ToString() ?? "",
                [93] = Label("Keywords"), [94] = Label("Exclude words"), [146] = Label("Keep domains") });
            var words = new Dictionary<int, string> {
                [74] = keywords.ToolTip?.ToString() ?? "", [85] = exclude.ToolTip?.ToString() ?? "", [79] = match.ToolTip?.ToString() ?? "",
                [112] = hidden.ToolTip?.ToString() ?? "", [132] = domains.ToolTip?.ToString() ?? "", [139] = list.ToolTip?.ToString() ?? "",
                [180] = create.ToolTip?.ToString() ?? "", [159] = ((TextBlock)current.FindName("noDomains")).Text,
                [266] = grid.ToolTip?.ToString() ?? "", [267] = grid.ToolTip?.ToString() ?? "" };
            foreach (var action in actions) words[action.Item1] = Button(current, action.Item2).ToolTip?.ToString() ?? "";
            words[189] = Button(current, "Cancel").ToolTip?.ToString() ?? "";
            int[] headerLines = { 298, 300, 301, 303, 304, 305, 307 };
            for (int column = 0; column < headerLines.Length; column++) words[headerLines[column]] = SecondaryHeaderTip(grid.Columns[column]);
            object redItem = grid.Items.Cast<object>().First(item => SecondaryRead(item, "HasKeyword") is false);
            grid.ScrollIntoView(redItem); grid.UpdateLayout(); PumpDispatcher();
            string redHint = ((DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(redItem)).ToolTip?.ToString() ?? "[No current row tooltip]";
            words[276] = words[277] = redHint;
            var liveChoices = list.Items.Cast<string>().Take(2).ToArray();
            int suffix = liveChoices[0].LastIndexOf(" (", StringComparison.Ordinal);
            words[361] = liveChoices[0].Substring(suffix, 2);
            if (liveChoices.Length == 2)
            {
                string firstName = domains.Text;
                list.SelectedItem = liveChoices[1]; PumpDispatcher();
                words[363] = domains.Text.Substring(firstName.Length, 2);
            }
            words[185] = Button(current, "Cancel").Content?.ToString() ?? "";
            SecondaryWording(output, "grouping-copy-" + theme, source, words, includeUnchanged: true);
            add(source, 176, 176, "native confirmation control remains reachable", oldCreate.GetType().Name, create.GetType().Name,
                oldCreate.IsVisible && create.IsVisible, "grouping confirmation button", row => row.Kind == "code control");
            SecondaryClick(Button(old, "Tick all")); SecondaryClick(Button(current, "Tick all")); PumpDispatcher();
            SecondaryClick(oldCreate); SecondaryClick(create); PumpDispatcher();
            bool confirmed = (bool)SecondaryRead(old, "Confirmed")! && current.Confirmed;
            add(source, 182, 182, "actual confirmation Click records the same confirmation and closes the preview", true, confirmed,
                confirmed && !old.IsLoaded && !current.IsLoaded, "grouping confirmation Click", row => row.Kind == "handler");
        }
        finally { current.Close(); old.Close(); }
        old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.SuperClusterPreviewWindow", true)!, oldPreview)!;
        current = new SuperClusterPreviewWindow(preview);
        System.ComponentModel.CancelEventHandler stopClose = (_, e) => e.Cancel = true;
        try
        {
            string beforeFile = file.ToJsonText(), beforeOld = (string)SecondaryCall(oldFile, oldFile.GetType(), "ToJsonText")!;
            (bool ClosedByClick, bool? Result, bool Safe) CancelModal(Window window, bool inject)
            {
                bool closed = false, byClick = false, safe = false;
                window.Left = -30000; window.Top = -30000; window.ShowActivated = false; window.ShowInTaskbar = false;
                window.WindowStartupLocation = WindowStartupLocation.Manual;
                window.Closed += (_, _) => closed = true;
                window.Loaded += (_, _) => window.Dispatcher.BeginInvoke(new Action(() =>
                {
                    safe = !window.IsActive && window.Left == -30000 && window.Top == -30000 && !window.ShowActivated && !window.ShowInTaskbar;
                    if (inject) window.Closing += stopClose;
                    var button = WalkTree(window).OfType<Button>().Single(value => value.IsVisible && value.Content as string == "Cancel");
                    SecondaryClick(button); byClick = closed;
                    window.Closing -= stopClose;
                    if (!closed) window.Close();
                }), DispatcherPriority.Background);
                bool? result = window.ShowDialog();
                return (byClick, result, safe);
            }
            var before = CancelModal(old, false); var actual = CancelModal(current, negative);
            PumpDispatcher();
            bool sourceSame = file.ToJsonText() == beforeFile;
            bool oldSourceSame = (string)SecondaryCall(oldFile, oldFile.GetType(), "ToJsonText")! == beforeOld;
            bool cancelled = before.Safe && actual.Safe && before.ClosedByClick && actual.ClosedByClick && before.Result == false && actual.Result == before.Result
                && !(bool)SecondaryRead(old, "Confirmed")! && !current.Confirmed && !old.IsLoaded && !current.IsLoaded
                && sourceSame && oldSourceSame;
            add(source, 183, 183, "grouping screen Cancel control returns modal false without confirmation or source mutation through its actual Click",
                new { before.ClosedByClick, before.Result, before.Safe, SourceSame = oldSourceSame, LoadedAfter = old.IsLoaded, Confirmed = (bool)SecondaryRead(old, "Confirmed")! },
                new { actual.ClosedByClick, actual.Result, actual.Safe, SourceSame = sourceSame, LoadedAfter = current.IsLoaded, current.Confirmed }, cancelled, "SuperClusterPreviewWindow modal Cancel control and Cancel_Click",
                row => row.Kind == "code control");
        }
        finally { current.Closing -= stopClose; current.Close(); old.Close(); }
    }
}
