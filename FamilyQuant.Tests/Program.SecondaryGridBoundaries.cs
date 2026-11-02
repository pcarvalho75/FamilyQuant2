using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static ModifierKeys secondaryModifiers;
    private static Exception? secondaryBoundaryError;
    private static int secondaryBoundaryCalls;
    private static readonly List<string> SecondaryBoundaryMessages = new();
    private static ModifierKeys SecondaryModifierBoundary() => secondaryModifiers;
    private static void SecondaryClipboardClear() { secondaryBoundaryCalls++; throw new ExternalException("Synthetic clipboard contention."); }
    private static void SecondaryClipboardText(string _) => SecondaryClipboardClear();
    private static Process? SecondaryBrowserStart(ProcessStartInfo _) { secondaryBoundaryCalls++; throw secondaryBoundaryError!; }
    private static MessageBoxResult SecondaryMessageBoundary(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    { SecondaryBoundaryMessages.Add(title + "/" + buttons + "/" + (int)image + "/" + message); return MessageBoxResult.OK; }
    private static MethodInfo SecondaryBoundary(string name) => typeof(Program).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static void SecondaryGridBoundaries(Assembly legacy, string output, Themes.AppTheme theme, bool negative, SecondaryAdd add)
    {
        const string source = "FamilyQuant/GridCopy.cs";
        var trace = new List<SecondaryInjection>();
        Type frozenType = legacy.GetType("FamilyQuant.GridCopy", true)!;
        var messages = new List<string>();
        Action<ShellMessage> capture = message => messages.Add(message.Title + "/" + message.Buttons + "/" + (int)message.Image + "/" + message.Text);
        var savedClipboard = GridCopy.ClipboardOverride; var savedBrowser = GridCopy.OpenUrlOverride;
        var frozenClipboard = frozenType.GetProperty("ClipboardOverride")!.GetValue(null);
        var frozenBrowser = frozenType.GetProperty("OpenUrlOverride")!.GetValue(null);
        GridCopy.ClipboardOverride = null; GridCopy.OpenUrlOverride = null;
        frozenType.GetProperty("ClipboardOverride")!.SetValue(null, null); frozenType.GetProperty("OpenUrlOverride")!.SetValue(null, null);
        ShellMessages.Reported += capture;
        try
        {
            MethodInfo Platform(Type type, string name, params Type[] args) => type.GetMethod(name, args)!;
            var oldValues = new List<string>(); var values = new List<string>();
            foreach (var type in new[] { frozenType, typeof(GridCopy) })
            {
                bool frozen = type == frozenType;
                var clipboard = type.GetMethod("ToClipboard", BindingFlags.Static | BindingFlags.NonPublic)!;
                var replacements = new Dictionary<MethodBase, MethodInfo>
                {
                    [Platform(typeof(Clipboard), "Clear")] = SecondaryBoundary(nameof(SecondaryClipboardClear)),
                    [Platform(typeof(Clipboard), "SetText", typeof(string))] = SecondaryBoundary(nameof(SecondaryClipboardText)),
                };
                if (frozen) replacements.Add(Platform(typeof(MessageBox), "Show", typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage)), SecondaryBoundary(nameof(SecondaryMessageBoundary)));
                var copy = SecondaryInject(clipboard, replacements, trace);
                foreach (string text in new[] { "", "native text" })
                {
                    SecondaryBoundaryMessages.Clear(); messages.Clear(); secondaryBoundaryCalls = 0;
                    copy.Invoke(null, frozen ? new object[] { text } : new object[] { text, new DependencyObject() });
                    (frozen ? oldValues : values).Add(secondaryBoundaryCalls + "/" + string.Join(";", frozen ? SecondaryBoundaryMessages : messages));
                }
            }
            bool clipboardMatches = oldValues.SequenceEqual(values) && values.Count == 2 && values.All(value => value.StartsWith("1/Copy/OK/" + (int)MessageBoxImage.Warning + "/Nothing was copied:", StringComparison.Ordinal)
                && value.Contains("Next step: try again in a moment.", StringComparison.Ordinal));
            add(source, 172, 173, "compiled clipboard catch executes for Clear and SetText contention with one warning and recovery advice", oldValues, values,
                clipboardMatches, "GridCopy.ToClipboard compiled IL; injected Clipboard.Clear/SetText only, plus frozen modal capture");
            oldValues = new(); values = new();
            foreach (var type in new[] { frozenType, typeof(GridCopy) })
            {
                bool frozen = type == frozenType;
                var replacements = new Dictionary<MethodBase, MethodInfo>
                    { [Platform(typeof(Process), "Start", typeof(ProcessStartInfo))] = SecondaryBoundary(nameof(SecondaryBrowserStart)) };
                if (frozen) replacements.Add(Platform(typeof(MessageBox), "Show", typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage)), SecondaryBoundary(nameof(SecondaryMessageBoundary)));
                var open = SecondaryInject(type.GetMethod("OpenUrl", BindingFlags.Static | BindingFlags.NonPublic)!, replacements, trace);
                foreach (Exception error in new Exception[] { new Win32Exception("Synthetic browser unavailable."), new InvalidOperationException("Synthetic browser unavailable."), new ArgumentException("Unrelated failure.") })
                {
                    secondaryBoundaryError = error; SecondaryBoundaryMessages.Clear(); messages.Clear(); secondaryBoundaryCalls = 0;
                    string escaped = "none";
                    try { open.Invoke(null, frozen ? new object[] { "https://www.uniprot.org/uniprotkb/P00001" } : new object[] { "https://www.uniprot.org/uniprotkb/P00001", new DependencyObject() }); }
                    catch (TargetInvocationException ex) when (ex.InnerException is ArgumentException) { escaped = ex.InnerException.GetType().Name; }
                    (frozen ? oldValues : values).Add(secondaryBoundaryCalls + "/" + escaped + "/" + string.Join(";", frozen ? SecondaryBoundaryMessages : messages));
                }
                SecondaryBoundaryMessages.Clear(); messages.Clear(); secondaryBoundaryCalls = 0;
                open.Invoke(null, frozen ? new object[] { "" } : new object[] { "", new DependencyObject() });
                (frozen ? oldValues : values).Add("empty/" + secondaryBoundaryCalls + "/" + (frozen ? SecondaryBoundaryMessages.Count : messages.Count));
            }
            bool browserMatches = oldValues.SequenceEqual(values) && values.Take(2).All(value => value.StartsWith("1/none/Open in UniProt/OK/" + (int)MessageBoxImage.Warning + "/", StringComparison.Ordinal)
                && value.Contains("Next step: copy the UniProt code and paste it into uniprot.org.", StringComparison.Ordinal))
                && values[2] == "1/ArgumentException/" && values[3] == "empty/0/0";
            add(source, 186, 187, "compiled browser exception filter reports Win32/invalid-operation failures, propagates unrelated errors and skips empty URLs", oldValues, values,
                browserMatches, "GridCopy.OpenUrl compiled IL; injected Process.Start only, plus frozen modal capture");
            SecondaryShiftSelection(frozenType, trace, negative, add);
            FeatureLedgerEvidence.WriteJson(System.IO.Path.Combine(output, "injected-grid-boundaries-" + theme + ".json"), trace);
        }
        finally
        {
            ShellMessages.Reported -= capture; GridCopy.ClipboardOverride = savedClipboard; GridCopy.OpenUrlOverride = savedBrowser;
            frozenType.GetProperty("ClipboardOverride")!.SetValue(null, frozenClipboard); frozenType.GetProperty("OpenUrlOverride")!.SetValue(null, frozenBrowser);
            secondaryBoundaryError = null; SecondaryBoundaryMessages.Clear(); secondaryModifiers = ModifierKeys.None;
        }
    }

    private static void SecondaryShiftSelection(Type frozenType, List<SecondaryInjection> trace, bool negative, SecondaryAdd add)
    {
        var states = new List<string[]>();
        foreach (var type in new[] { frozenType, typeof(GridCopy) })
        {
            var row = new SecondaryCopyRow("native", "native selected cell"); var copied = new List<string>();
            var grid = new CountingGrid { ItemsSource = new[] { row }, AutoGenerateColumns = false, CanUserAddRows = false };
            grid.Columns.Add(new DataGridTextColumn { Binding = new Binding(nameof(SecondaryCopyRow.Description)) });
            type.GetProperty("ClipboardOverride")!.SetValue(null, (Action<string>)copied.Add);
            SecondaryCall(null, type, "Attach", grid, null, (Func<object, string?>)(value => ((SecondaryCopyRow)value).Description), "Copy description");
            var host = SecondaryHost(new Window { Content = grid });
            try
            {
                grid.SelectedItem = row; grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[0]); grid.UpdateLayout();
                object store = typeof(UIElement).GetProperty("EventHandlersStore", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(grid)!;
                var handlers = (Array)store.GetType().GetMethod("GetRoutedEventHandlers", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
                    .Invoke(store, new object[] { Keyboard.PreviewKeyDownEvent })!;
                var handler = handlers.Cast<object>().Select(info => (Delegate)info.GetType().GetProperty("Handler")!.GetValue(info)!)
                    .Single(value => value.Method.DeclaringType?.Assembly == type.Assembly);
                if (!XicReferencedMethods(type.GetMethod("Attach")!).Contains(handler.Method)) throw new InvalidOperationException("The injected callback is not linked from the actual Attach method.");
                var callback = SecondaryInject(handler.Method, new Dictionary<MethodBase, MethodInfo>
                    { [typeof(Keyboard).GetProperty("Modifiers")!.GetMethod!] = SecondaryBoundary(nameof(SecondaryModifierBoundary)) }, trace);
                var run = (KeyEventHandler)callback.CreateDelegate(typeof(KeyEventHandler), handler.Target);
                DependencyObject? Header(DependencyObject parent)
                {
                    if (parent is DataGridRowHeader) return parent;
                    for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++) if (Header(VisualTreeHelper.GetChild(parent, index)) is { } found) return found;
                    return null;
                }
                string CopyState()
                {
                    int before = grid.OwnCopies; copied.Clear(); ApplicationCommands.Copy.Execute(null, grid);
                    return grid.OwnCopies > before ? "row" : copied.SingleOrDefault() ?? "none";
                }
                var effects = new List<string>();
                foreach (Key key in new[] { Key.Up, Key.Down, Key.Home, Key.End, Key.PageUp, Key.PageDown, Key.Left, Key.Right, Key.Tab })
                {
                    ((UIElement)Header(grid)!).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent });
                    string before = CopyState(); secondaryModifiers = negative && type == typeof(GridCopy) ? ModifierKeys.None : ModifierKeys.Shift;
                    run(grid, new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    effects.Add(key + "/" + before + "/" + CopyState());
                }
                states.Add(effects.ToArray());
            }
            finally { host.Close(); type.GetProperty("ClipboardOverride")!.SetValue(null, null); }
        }
        bool matched = states[0].SequenceEqual(states[1]) && states[1].Take(6).All(value => value.EndsWith("/row/row", StringComparison.Ordinal))
            && states[1].Skip(6).All(value => value.EndsWith("/row/native selected cell", StringComparison.Ordinal));
        add("FamilyQuant/GridCopy.cs", 51, 51, "actual Attach callback with injected Shift preserves row copy on vertical extension and restores column copy on horizontal/Tab navigation",
            states[0], states[1], matched, "GridCopy.Attach linked PreviewKeyDown callback; only Keyboard.Modifiers getter injected");
    }
}
