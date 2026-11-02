using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class ShellMetadataSnapshot
{
    private static void ObserveHomeInteractions(JsonElement[] inventory, HomeControl home, List<Fact> facts)
    {
        var initial = inventory.Single(row => row.GetProperty("Source").GetString() == "FamilyQuant/HomeControl.xaml.cs"
            && row.GetProperty("Line").GetInt32() == 38);
        var project = (TextBlock)home.FindName("TextBlockHomeProject");
        facts.Add(HomeFact(initial, "TextBlockHomeProject", "Text", project.GetType().FullName!, project.Text,
            project.Text == "No project open." ? "" : "Initial project status changed."));
        home.SetProjectState(true, "Synthetic navigation fixture");
        var counts = new int[5];
        home.BuildFamiliesRequested += () => counts[0]++;
        home.CurateRequested += () => counts[1]++;
        home.ComputeXicRequested += () => counts[2]++;
        home.AnalyticsRequested += () => counts[3]++;
        home.DeNovoRequested += () => counts[4]++;
        string[] anchors = ["ButtonHomeBuild", "ButtonHomeCurate", "ButtonHomeCompute", "ButtonHomeAnalytics", "ButtonHomeDeNovo"];
        string[] methods = ["ButtonHomeBuild_Click", "ButtonHomeCurate_Click", "ButtonHomeCompute_Click", "ButtonHomeAnalytics_Click", "ButtonHomeDeNovo_Click"];
        for (int index = 0; index < anchors.Length; index++)
        {
            Array.Clear(counts);
            var button = (Button)home.FindName(anchors[index]);
            bool enabled = button.IsEnabled;
            if (enabled) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            bool expected = enabled && counts[index] == 1 && counts.Sum() == 1;
            string value = "enabled=" + enabled + ";event counts=" + string.Join(",", counts);
            foreach (var row in inventory.Where(row => row.GetProperty("Source").GetString() is "FamilyQuant/HomeControl.xaml" or "FamilyQuant/HomeControl.xaml.cs")
                .Where(row => row.GetProperty("Kind").GetString() is "handler" or "handler definition")
                .Where(row => row.GetProperty("Detail").GetString()!.Contains(methods[index] + "(", StringComparison.Ordinal)
                    || row.GetProperty("Detail").GetString() == anchors[index] + ".Click = " + methods[index]))
                facts.Add(HomeFact(row, anchors[index], "$interaction", typeof(HomeControl).FullName!, value,
                    expected ? "" : "Native click did not emit exactly its own event once."));
        }
    }

    private static Fact HomeFact(JsonElement row, string anchor, string property, string type, string value, string reason) =>
        new(row.GetProperty("Id").GetString()!, row.GetProperty("Source").GetString()!, row.GetProperty("Line").GetInt32(),
            row.GetProperty("Kind").GetString()!, row.GetProperty("Detail").GetString()!, anchor, property, type, value, reason);
}
