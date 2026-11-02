using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ShellMainComparison(string Id, string Kind, int Line, bool Matched, string Member,
        string Before, string Actual, string NativeCase, string Reason, string MemberSha256,
        string Source = "FamilyQuant/MainWindow.xaml.cs");

    private static int ShellMainMessageProbe(string[] args)
    {
        if (args.Length != 4 || !DateTimeOffset.TryParse(args[3], CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces, out _)) throw new ArgumentException("Supply frozen source, fresh output and date timestamp.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output) || File.Exists(output)) throw new IOException("MainWindow proof output must be fresh.");
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        if (FeatureLedger.Fingerprint(inventory) != FeatureLedger.ReferenceInventorySha256)
            throw new InvalidDataException("Frozen inventory identity differs.");
        foreach (string source in new[] { "FamilyQuant/MainWindow.xaml", "FamilyQuant/MainWindow.xaml.cs" })
            if (FeatureLedgerEvidence.Hash(Path.Combine(args[1], source)) != inventory.Files.Single(file => file.Path == source).Sha256)
                throw new InvalidDataException("Frozen shell source hash differs: " + source);
        string frozen = File.ReadAllText(Path.Combine(args[1], "FamilyQuant/MainWindow.xaml.cs"));
        var view = XDocument.Load(Path.Combine(args[1], "FamilyQuant/MainWindow.xaml"));
        string? Handler(string menu) => view.Descendants().SingleOrDefault(node => node.Attributes()
            .Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == menu))?.Attribute("Click")?.Value;
        Directory.CreateDirectory(output);
        if (ShellRouteProbe(Path.Combine(output, "native")) != 0) return 1;
        var native = JsonSerializer.Deserialize<List<ShellRouteCase>>(File.ReadAllText(Path.Combine(output, "native/native-cases.json")), FeatureLedgerEvidence.JsonOptions)!;
        var definitions = inventory.Observations.Where(row => row.Source == "FamilyQuant/MainWindow.xaml.cs" && row.Kind == "handler definition")
            .OrderBy(row => row.Line).ToArray();
        var rows = inventory.Observations.Where(row => row.Source == "FamilyQuant/MainWindow.xaml.cs" && row.Line >= 273 && row.Line < 841
            && row.Kind is "code fact" or "format/export").OrderBy(row => row.Id, StringComparer.Ordinal).ToArray();
        var results = new List<ShellMainComparison>();
        foreach (var row in rows)
        {
            string method = Regex.Match(definitions.Last(definition => definition.Line <= row.Line).Detail, @"\b(Menu\w+)\(").Groups[1].Value;
            var cases = native.Where(record => record.Passed && Handler(record.Menu) == method).ToArray();
            ShellMainComparison result = new(row.Id, row.Kind, row.Line, false, method, row.Detail, "", "", "No executed matching branch.", "");
            if (LedgerMemberFactComparison.TryMessageFrame(row, frozen, method, out var frame, out bool caption, out string reason))
            {
                foreach (var executed in cases)
                {
                    var bindings = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["""quant.Count.ToString("N0",CultureInfo.CurrentCulture)"""] = "0",
                        ["""(familyNote!=null?familyNote+"\n\n":string.Empty)"""] = ""
                    };
                    if (executed.Dialogs.SingleOrDefault(dialog => dialog.Accepted) is { } dialog) bindings["sfd.FileName"] = dialog.FileName;
                    if (executed.ExceptionMessage != null) bindings["ex.Message"] = executed.ExceptionMessage;
                    var expected = LedgerMemberFactComparison.EvaluateMessage(caption ? frame!.Caption : frame!.Text, bindings);
                    var expectedTitle = LedgerMemberFactComparison.EvaluateMessage(frame!.Caption, bindings);
                    string? actual = null;
                    if (executed.Messages.SingleOrDefault() is { } message && expectedTitle.Supported && message.Title == expectedTitle.Value)
                        actual = caption ? message.Title : message.Text;
                    if (!caption && executed.Id == "project-success" && executed.SavedCheck is { } check)
                    {
                        string prefix = "\n\nSaved. " + check + "\n", suffix = "\n\n" + check;
                        int index = executed.Status.IndexOf(prefix, StringComparison.Ordinal);
                        if (index >= 0 && index == executed.Status.LastIndexOf(prefix, StringComparison.Ordinal)
                            && executed.Status.EndsWith(suffix, StringComparison.Ordinal))
                            actual = executed.Status[(index + prefix.Length)..^suffix.Length];
                    }
                    bool contributes = expected.Contributing.Any(token => token.Line == row.Line && token.Text == row.Detail);
                    bool matched = expected.Supported && contributes && actual != null && expected.Value == actual;
                    result = new(row.Id, row.Kind, row.Line, matched, frame.Member, expected.Value, actual ?? "", executed.Id,
                        matched ? (caption ? "Complete frozen caption equals the report from the executed native branch."
                            : "Complete frozen message expression equals the executed native report; dynamic inner literals are not credited.")
                            : expected.Supported ? "Changed, unexecuted or noncontributing frozen message fact." : expected.Reason, frame.MemberSha256);
                    if (matched) break;
                }
            }
            else if (LedgerMemberFactComparison.TryDialogFrame(row, frozen, method, out var dialogFrame, out reason))
            {
                var expected = LedgerMemberFactComparison.EvaluateMessage(dialogFrame!.Expression, new Dictionary<string, string>());
                foreach (var executed in cases)
                foreach (var dialog in executed.Dialogs.Where(dialog => dialog.Type == "Microsoft.Win32." + dialogFrame.DialogType))
                {
                    string? actual = dialogFrame.Property switch { "Filter" => dialog.Filter, "Title" => dialog.Title, "DefaultExt" => dialog.DefaultExt, _ => null };
                    bool patterns = expected.Supported && actual != null && dialogFrame.Property == "Filter"
                        && SameFileFilterPatterns(expected.Value, actual);
                    bool matched = expected.Supported && actual != null && (dialogFrame.Property == "DefaultExt"
                        ? expected.Value.TrimStart('.') == actual.TrimStart('.') : expected.Value == actual || patterns);
                    result = new(row.Id, row.Kind, row.Line, matched, dialogFrame.Member, expected.Value, actual ?? "", executed.Id,
                        matched ? patterns ? "File-filter acceptance groups agree exactly; renamed human labels are recorded in full."
                            : "Complete frozen dialog property equals the field captured from the actual native chooser."
                            : "Changed or unsupported dialog field; no borrowed label.", dialogFrame.MemberSha256);
                    if (matched) break;
                }
            }
            else result = result with { Reason = reason };
            results.Add(result);
            if (result.Matched) Check("native shell message " + row.Id + ": " + result.NativeCase + " at frozen line " + row.Line, true);
            else { skipped++; System.Console.WriteLine("  SKIP  " + row.Id + ": " + result.Reason); }
        }
        foreach (var result in ShellNativeRouteComparisons(inventory, view, native))
        {
            results.Add(result);
            if (result.Matched) Check("native shell route mapping " + result.Id + ": " + result.NativeCase, true);
            else { skipped++; Console.WriteLine("  SKIP  " + result.Id + ": " + result.Reason); }
        }
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "message-comparisons.json"), results);
        System.Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool SameFileFilterPatterns(string before, string after)
    {
        string[] left = before.Split('|'), right = after.Split('|');
        if (left.Length < 2 || right.Length < 2 || left.Length % 2 != 0 || right.Length % 2 != 0
            || left.Any(string.IsNullOrWhiteSpace) || right.Any(string.IsNullOrWhiteSpace)) return false;
        return left.Where((_, index) => index % 2 == 1).SequenceEqual(right.Where((_, index) => index % 2 == 1), StringComparer.Ordinal);
    }

    private static IEnumerable<ShellMainComparison> ShellNativeRouteComparisons(FeatureLedger.Inventory inventory, XDocument frozen,
        IReadOnlyList<ShellRouteCase> native)
    {
        var routes = new[]
        {
            (Menu: "MenuItemHome", Case: "home", Header: "Start page", Definition: true),
            (Menu: "MenuItemRunUSearch", Case: "new", Header: "New from database", Definition: true),
            (Menu: "MenuItemLoadFQC", Case: "open-fcf", Header: "Open", Definition: true),
            (Menu: "MenuItemSaveProject", Case: "project-success", Header: "Save", Definition: true),
            (Menu: "MenuItemSaveFCF", Case: "fcf-success", Header: "Families for FamilyQuant 1.5 (.fcf)", Definition: true),
            (Menu: "MenuItemComputeXic", Case: "quantify", Header: "Compute full XIC", Definition: true),
            (Menu: "MenuItemDeNovo", Case: "denovo", Header: "De novo evidence", Definition: true),
            (Menu: "MenuItemAnalytics", Case: "results", Header: "Results", Definition: true),
            (Menu: "MenuItemReanotateSEPro", Case: "sepro-cancel", Header: "Reannotate SEPro", Definition: false)
        };
        foreach (var route in routes)
        {
            var node = frozen.Descendants().Single(element => element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == route.Menu));
            string handler = node.Attribute("Click")!.Value, oldHeader = node.Attribute("Header")!.Value;
            var executed = native.Single(record => record.Id == route.Case);
            bool passed = executed.Passed && executed.Menu == route.Menu && executed.Header == route.Header;
            var mapped = inventory.Observations.Where(row => row.Source == "FamilyQuant/MainWindow.xaml"
                && (row.Kind == "handler" && row.Detail == route.Menu + ".Click = " + handler
                    || row.Kind == "option/copy" && row.Detail == route.Menu + ".Header = " + oldHeader))
                .Concat(route.Definition ? inventory.Observations.Where(row => row.Source == "FamilyQuant/MainWindow.xaml.cs"
                    && row.Kind == "handler definition" && row.Detail.Contains(" " + handler + "(", StringComparison.Ordinal)) : []);
            foreach (var row in mapped)
                yield return new(row.Id, row.Kind, row.Line, passed, handler, row.Detail,
                    executed.Menu + ".Header=" + executed.Header + "; " + executed.Dispatch + "; " + executed.Outcome, route.Case,
                    "Frozen entry point maps to the actual menu dispatch and checked native outcome. Route only; no unexecuted scientific operation claim.", "", row.Source);
        }
        var file = native.Single(record => record.Id == "file-menu");
        foreach (var row in inventory.Observations.Where(row => row.Source == "FamilyQuant/MainWindow.xaml" && row.Line == 27
            && row.Kind is "control" or "option/copy"))
            yield return new(row.Id, row.Kind, row.Line, file.Passed && file.Header == "File", "MainMenu.Items[0]", row.Detail,
                file.Outcome, file.Id, "Frozen File menu remains the first attached native menu; project commands are exercised through it.", "", row.Source);
    }
}
