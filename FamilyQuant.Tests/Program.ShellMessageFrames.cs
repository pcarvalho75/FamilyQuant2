using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ShellMessageFrameTests(bool negativeControl = false)
    {
        const string source = """
            namespace FamilyQuant; public partial class MainWindow {
            private void Save() { MessageBox.Show("kept " + path + " done", "Saved"); }
            private void Other() { MessageBox.Show("other", "Saved"); } }
            """;
        var row = new FeatureLedger.Observation("fixture", "FamilyQuant/MainWindow.xaml.cs", 2, "code fact", "\"kept \"");
        bool read = LedgerMemberFactComparison.TryMessageFrame(row, source, "Save", out var frame, out bool caption, out _);
        Check("frozen message frames keep their exact handler and argument", read && !caption && frame!.Member.Contains(" Save ("));
        Check("message frames cannot borrow another handler's same caption", !LedgerMemberFactComparison.TryMessageFrame(row, source, "Other", out _, out _, out _));
        Check("scoped native facts retain exact frozen literal identity", LedgerMemberFactComparison.TryScopedMessageLiteral(row, source, "Save", out _, out _));
        Check("a scoped native fact cannot borrow another handler's literal", !LedgerMemberFactComparison.TryScopedMessageLiteral(row, source, "Other", out _, out _));
        if (!read) return;
        var value = LedgerMemberFactComparison.EvaluateMessage(frame!.Text, new Dictionary<string, string> { ["path"] = negativeControl ? "other.fcf" : "fixture.fcf" });
        Check("frozen message evaluation substitutes only the supplied native variable", value.Supported && value.Value == "kept fixture.fcf done");
        Check("literal contribution records retain their frozen physical line", value.Contributing.Length == 2 && value.Contributing.All(token => token.Line == 2));
        Check("an unknown dynamic message term stays unverified", !LedgerMemberFactComparison.EvaluateMessage(frame.Text, new Dictionary<string, string>()).Supported);
        var title = LedgerMemberFactComparison.EvaluateMessage(frame.Caption, new Dictionary<string, string>());
        Check("caption evaluation stays separate from message-body evaluation", title.Supported && title.Value == "Saved" && title.Contributing.Length == 1);
        var dynamicToken = new LedgerMemberFactComparison.MessageToken("Format(\"N0\")", 3, false);
        var bound = LedgerMemberFactComparison.EvaluateMessage([dynamicToken], new Dictionary<string, string> { [dynamicToken.Text] = "0" });
        Check("a bound dynamic expression does not prove its unevaluated inner literals", bound.Supported && bound.Contributing.Length == 0);
        const string dialogSource = "namespace FamilyQuant; public partial class MainWindow {\nprivate void Open() { var dialog = new OpenFileDialog { Filter = \"files|*.fcf\", Title = \"Open families\" }; } }";
        var dialogRow = row with { Detail = "\"files|*.fcf\"" };
        bool dialogRead = LedgerMemberFactComparison.TryDialogFrame(dialogRow, dialogSource, "Open", out var dialog, out _);
        Check("dialog frames preserve the literal's exact native property and handler", dialogRead && dialog!.Property == "Filter" && dialog.DialogType == "OpenFileDialog");
        Check("dialog frames cannot borrow another member's filter", !LedgerMemberFactComparison.TryDialogFrame(dialogRow, dialogSource, "Save", out _, out _));
        Check("a dialog frame evaluates its complete filter expression", dialogRead
            && LedgerMemberFactComparison.EvaluateMessage(dialog!.Expression, new Dictionary<string, string>()).Value == "files|*.fcf");
        Check("duplicate same-line dialog literals stay unverified", !LedgerMemberFactComparison.TryDialogFrame(dialogRow,
            dialogSource.Replace("Title = \"Open families\"", "Title = \"files|*.fcf\"", System.StringComparison.Ordinal), "Open", out _, out _));
        Check("renamed chooser labels preserve the complete ordered acceptance groups", SameFileFilterPatterns("Families|*.fcf|Projects|*.fqproj", "Family files|*.fcf|Project files|*.fqproj"));
        Check("a changed file extension cannot borrow the old chooser label", !SameFileFilterPatterns("Families|*.fcf", "Families|*.fqproj"));
        Check("invalid filters cannot earn a preservation receipt", !SameFileFilterPatterns("Families", "Families"));
        var conditional = new[]
        {
            new LedgerMemberFactComparison.MessageToken("(", 4, false), new("condition", 4, false), new("?", 4, false),
            new("\"chosen\"", 4, true), new(":", 4, false), new("\"other\"", 4, true), new(")", 4, false)
        };
        var selected = LedgerMemberFactComparison.EvaluateMessage(conditional, new Dictionary<string, string>(),
            new Dictionary<string, bool> { ["condition"] = true });
        Check("conditional message literals require their executed predicate", selected.Supported && selected.Value == "chosen"
            && selected.Contributing.Single().Text == "\"chosen\"");
        Check("an unrecorded predicate cannot prove either branch", !LedgerMemberFactComparison.EvaluateMessage(conditional, new Dictionary<string, string>()).Supported);
        var menu = XDocument.Parse("<Window xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><MenuItem x:Name='MenuItemRunUSearch' Header='Run USearch'/></Window>");
        var old = new LedgerMemberFactComparison.MessageValue(true, "Use Run USearch.", [new("\"Use Run USearch.\"", 1, true)], "");
        var routes = new[]
        {
            new ShellRouteCase("new", "MenuItemRunUSearch", WorkflowNames.BuildFamilies, "RoutedCommand:New", "Build view", true, [], [], ""),
            new ShellRouteCase("file-menu", "MenuFile", "File", "attached native MenuItem", "First menu", true, [], [], "")
        };
        Check("one report menu reference follows the executed renamed command", SameReportAfterMenuRename(old, "Use File > Build families.", new Dictionary<string, string>(), menu, routes));
        Check("menu migration cannot change other report facts", !SameReportAfterMenuRename(old, "Different report File > Build families.", new Dictionary<string, string>(), menu, routes));
        Check("native variable values are never rewritten as menu labels", !SameReportAfterMenuRename(old, "Use File > Build families.",
            new Dictionary<string, string> { ["path"] = "Run USearch" }, menu, routes));
        Check("a wrong current header cannot satisfy the menu-reference mapping", !SameReportAfterMenuRename(old, "Use File > Wrong.",
            new Dictionary<string, string>(), menu, [routes[0] with { Header = "Wrong" }, routes[1]]));
        Check("an unexecuted route cannot prove a report migration", !SameReportAfterMenuRename(old, "Use File > Build families.",
            new Dictionary<string, string>(), menu, [routes[0] with { Passed = false }, routes[1]]));
    }
}
