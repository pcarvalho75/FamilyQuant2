using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryLockRuntime(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        const string source = "FamilyQuant/LockManagerWindow.cs";
        var file = new FamilyClusterFile { LockedUserIds = new() { 7, 2, 90 }, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "native-target-a", Description = "Fixture name", Sequence = "ACDEFGHIK", UserID = 2 },
            new() { SequenceIdentifier = "native-target-b", Description = "Other fixture", Sequence = "VVVVVVVVK", UserID = 7 },
        } };
        string beforeDocument = file.ToJsonText();
        object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", beforeDocument)!;
        var oldCalls = new List<string>(); var newCalls = new List<string>();
        int Count(object value) => ((IEnumerable)SecondaryRead(value, "LockedUserIds")!).Cast<object>().Count();
        var old = (Window)Activator.CreateInstance(legacy.GetType("FamilyQuant.LockManagerWindow", true)!, new object?[]
        { oldFile, (Action)(() => oldCalls.Add("after:" + Count(oldFile))), (Action)(() => oldCalls.Add("before:" + Count(oldFile))) })!;
        var current = new LockManagerWindow(file, () => newCalls.Add("after:" + file.LockedUserIds.Count), () => newCalls.Add("before:" + file.LockedUserIds.Count));
        SecondaryHost(old); SecondaryHost(current);
        try
        {
            string[] Rows(object window) => ((IEnumerable)SecondaryRead(window, "Rows")!).Cast<object>().Select(row =>
                string.Join("/", new[] { "UserId", "Members", "Presence", "Description" }.Select(name => Convert.ToString(SecondaryRead(row, name), CultureInfo.InvariantCulture)))).ToArray();
            var oldGrid = WalkTree(old).OfType<DataGrid>().Single(); var grid = WalkTree(current).OfType<DataGrid>().Single();
            Button Button(Window window, string text) => WalkTree(window).OfType<Button>().Single(button => button.Content as string == text);
            var oldRemove = Button(old, "Remove selected locks"); var remove = Button(current, "Remove selected locks");
            var oldClose = Button(old, "Close"); var close = Button(current, "Close");
            add(source, 78, 78, "native lock-manager window title", old.Title, current.Title, old.Title == current.Title, "LockManagerWindow.Title");
            foreach (var item in new[] { (129, (FrameworkElement)oldRemove, (FrameworkElement)remove), (144, oldClose, close), (168, oldGrid, grid) })
                add(source, item.Item1, item.Item1, "native lock-manager control is reachable", item.Item2.GetType().Name, item.Item3.GetType().Name,
                    item.Item2.IsVisible && item.Item3.IsVisible && item.Item2.GetType().Name == item.Item3.GetType().Name,
                    "LockManagerWindow " + item.Item3.GetType().Name, row => row.Kind == "code control");
            foreach (var caption in new[] { (131, oldRemove, remove), (146, oldClose, close) })
                add(source, caption.Item1, caption.Item1, "native lock action caption", caption.Item2.Content, caption.Item3.Content,
                    Equals(caption.Item2.Content, caption.Item3.Content), "LockManagerWindow action caption");
            for (int index = 0; index < grid.Columns.Count; index++)
            {
                int i = index;
                int[] controlLines = { 184, 194, 204, 214 }, headerLines = { 186, 196, 206, 216 }, bindingLines = { 188, 198, 208, 219 };
                string oldBinding = ((Binding)((DataGridTextColumn)oldGrid.Columns[i]).Binding).Path.Path;
                string newBinding = ((Binding)((DataGridTextColumn)grid.Columns[i]).Binding).Path.Path;
                add(source, controlLines[i], controlLines[i], "native lock-table column exists and binds its row field", oldGrid.Columns[i].GetType().Name + "/" + oldBinding,
                    grid.Columns[i].GetType().Name + "/" + newBinding, oldBinding == newBinding && oldGrid.Columns[i].GetType().Name == grid.Columns[i].GetType().Name,
                    "LockManagerWindow table column " + i, row => row.Kind == "code control");
                add(source, headerLines[i], headerLines[i], "native lock-table column caption", oldGrid.Columns[i].Header, grid.Columns[i].Header,
                    Equals(oldGrid.Columns[i].Header, grid.Columns[i].Header), "LockManagerWindow table header " + i);
                add(source, bindingLines[i], bindingLines[i], "resolved lock-table field binding and ordered row values", oldBinding + ";" + string.Join(";", Rows(old)),
                    newBinding + ";" + string.Join(";", Rows(current)), oldBinding == newBinding && Rows(old).SequenceEqual(Rows(current)), "LockManagerWindow binding " + newBinding);
            }
            add(source, 287, 298, "current and retired lock presence values are retained in ordered native rows", Rows(old), Rows(current),
                Rows(old).SequenceEqual(Rows(current)) && current.Rows.Any(row => row.Presence == "yes") && current.Rows.Any(row => row.Presence == "no, merged away"),
                "LockManagerViewModel.Rows.Presence");
            bool initiallyDisabled = !oldRemove.IsEnabled && !remove.IsEnabled;
            SecondaryCall(old, old.GetType(), "SelectFamilies", new int[] { 7, 90 }); current.SelectFamilies(7, 90); PumpDispatcher();
            add(source, 230, 230, "actual lock-table selection event enables removal only after rows are selected", "initial false; selected true",
                new { InitiallyDisabled = initiallyDisabled, FrozenEnabled = oldRemove.IsEnabled, CurrentEnabled = remove.IsEnabled },
                initiallyDisabled && oldRemove.IsEnabled && remove.IsEnabled, "LockManagerWindow table SelectionChanged");
            TextWriter previous = Console.Out; using var oldLog = new StringWriter(CultureInfo.InvariantCulture); using var newLog = new StringWriter(CultureInfo.InvariantCulture);
            try { Console.SetOut(oldLog); SecondaryClick(oldRemove); Console.SetOut(newLog); SecondaryClick(remove); }
            finally { Console.SetOut(previous); }
            PumpDispatcher();
            bool removal = oldCalls.SequenceEqual(newCalls) && newCalls.SequenceEqual(new[] { "before:3", "after:1" })
                && file.LockedUserIds.SetEquals(new[] { 2 }) && Rows(old).SequenceEqual(Rows(current)) && file.MyExtendedFastaItems.Count == 2;
            add(source, 142, 142, "actual Remove button executes one before/after callback and preserves protein rows", oldCalls, newCalls, removal,
                "LockManagerWindow.Remove button Click -> RemoveSelectedLocks");
            add(source, 351, 352, "executed lock removal retains complete frozen invariant log construction", oldLog.ToString(), newLog.ToString(),
                removal && oldLog.ToString() == newLog.ToString(), "LockManagerViewModel.Remove log");
            SecondaryClick(oldClose); SecondaryClick(close); PumpDispatcher();
            add(source, 154, 154, "actual Close buttons close their windows without another lock callback", new { Closed = true, Calls = 2 },
                new { Closed = !old.IsLoaded && !current.IsLoaded, Calls = newCalls.Count }, !old.IsLoaded && !current.IsLoaded && newCalls.Count == 2 && oldCalls.Count == 2,
                "LockManagerWindow Close button Click");
        }
        finally { current.Close(); old.Close(); }
    }
}
