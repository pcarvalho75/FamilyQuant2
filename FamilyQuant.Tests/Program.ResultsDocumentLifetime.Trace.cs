using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void TraceResultsDocumentOwner(MainWindow shell, FamilyClusterFile target)
    {
        object? engine = typeof(Binding).Assembly.GetType("MS.Internal.Data.DataBindEngine")?
            .GetProperty("CurrentDataBindEngine", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        foreach (var root in new (string Name, object? Value)[]
            { ("shell", shell), ("application", Application.Current), ("dispatcher", Dispatcher.CurrentDispatcher), ("binding engine", engine) })
        {
            if (root.Value == null) continue;
            Console.WriteLine("RETAINER " + root.Name + ": " + ResultsReferencePath(root.Value, target));
        }
    }

    private static string ResultsReferencePath(object root, object target)
    {
        var nodes = new List<(object Value, int Parent, string Step, int Depth)> { (root, -1, root.GetType().Name, 0) };
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance) { root };
        for (int index = 0; index < nodes.Count && nodes.Count < 100_000; index++)
        {
            var node = nodes[index];
            if (ReferenceEquals(node.Value, target))
            {
                var path = new List<string>();
                for (int current = index; current >= 0; current = nodes[current].Parent) path.Add(nodes[current].Step);
                path.Reverse(); return string.Join(" -> ", path);
            }
            Type type = node.Value.GetType();
            if (node.Depth >= 22 || type.IsPrimitive || type.IsEnum || node.Value is string or Type or MemberInfo
                || type.FullName?.Contains("WeakReference", StringComparison.Ordinal) == true) continue;
            void Add(object? value, string step)
            {
                if (value == null || value is string || value.GetType().IsPrimitive || value.GetType().IsEnum || !seen.Add(value)) return;
                nodes.Add((value, index, step, node.Depth + 1));
            }
            if (node.Value is Array array)
            {
                int item = 0;
                foreach (object? value in array) { Add(value, "[" + item++ + "]"); if (item >= 10_000) break; }
                continue;
            }
            for (Type? current = type; current != null; current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    try { Add(field.GetValue(node.Value), type.Name + "." + field.Name); }
                    catch (Exception error) when (error is NotSupportedException or MemberAccessException) { }
                }
        }
        return "not found within " + nodes.Count + " inspected references";
    }
}
