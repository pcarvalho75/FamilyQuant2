using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void SecondaryIdentityRuntime(Assembly legacy, string output, Themes.AppTheme theme, SecondaryAdd add)
    {
        var rows = new List<IdentityScanRow> { new() { Identity = 95, Groups = 6000, TargetGroups = 5000, MixedGroups = 0, Score = 0 },
            new() { Identity = 75, Groups = 4000, TargetGroups = 3000, MixedGroups = 0, Score = 0 },
            new() { Identity = 55, Groups = 2000, TargetGroups = 1500, MixedGroups = 20, Score = .25 } };
        Type oldRow = legacy.GetType("FamilyQuant.IdentityScanRow", true)!;
        object oldRows = JsonSerializer.Deserialize(JsonSerializer.Serialize(rows), typeof(List<>).MakeGenericType(oldRow))!;
        var oldChart = (FrameworkElement)Activator.CreateInstance(legacy.GetType("FamilyQuant.IdentityScanChart", true)!, new object?[] { oldRows, 75, null })!;
        var chart = new IdentityScanChart(rows, 75, ChartPalette.UiPalette);
        var oldHost = SecondaryHost(new Window { Content = oldChart }); var host = SecondaryHost(new Window { Content = chart });
        try
        {
            foreach (var window in new[] { oldHost, host }) { window.Width = 1800; window.Height = 500; window.UpdateLayout(); }
            PumpDispatcher();
            var oldText = new List<string>(); var text = new List<string>(); var oldFonts = new List<string>(); var fonts = new List<string>();
            void Text(Drawing? drawing, List<string> labels, List<string> families)
            {
                if (drawing is DrawingGroup group) foreach (var child in group.Children) Text(child, labels, families);
                if (drawing is GlyphRunDrawing glyph)
                {
                    if (glyph.GlyphRun.Characters != null) labels.Add(new string(glyph.GlyphRun.Characters.ToArray()));
                    families.AddRange(glyph.GlyphRun.GlyphTypeface.FamilyNames.Values);
                }
            }
            Text(VisualTreeHelper.GetDrawing(oldChart), oldText, oldFonts); Text(VisualTreeHelper.GetDrawing(chart), text, fonts);
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "identity-glyphs-" + theme + ".json"), new { Frozen = oldText, Current = text });
            bool Label(List<string> glyphs, string expected)
            {
                // DirectWrite can split one caption into consecutive glyph runs at a hyphen.
                for (int start = 0; start < glyphs.Count; start++)
                {
                    string joined = "";
                    for (int end = start; end < glyphs.Count && joined.Length < expected.Length; end++)
                    { joined += glyphs[end]; if (joined == expected) return true; }
                }
                return false;
            }
            const string chartSource = "FamilyQuant/IdentityScanChart.cs";
            add(chartSource, 123, 123, "actual rendered glyphs retain the frozen UI typeface", oldFonts.Distinct().Order().ToArray(), fonts.Distinct().Order().ToArray(),
                oldFonts.Contains("Segoe UI") && fonts.Contains("Segoe UI"), "IdentityScanChart rendered glyph typeface");
            string[] counts = Enumerable.Range(0, 5).Select(index => (5000 * index / 4d).ToString("N0", CultureInfo.CurrentCulture)).ToArray();
            string[] percentages = Enumerable.Range(0, 5).Select(index => (.25 * index / 4d).ToString("P1", CultureInfo.CurrentCulture)).ToArray();
            add(chartSource, 159, 159, "actual frozen/current family-axis labels resolve N0 values", counts, text.Where(counts.Contains).ToArray(),
                counts.All(value => oldText.Contains(value) && text.Contains(value)), "IdentityScanChart left-axis labels");
            add(chartSource, 164, 164, "actual frozen/current mixing-axis labels resolve P1 values", percentages, text.Where(percentages.Contains).ToArray(),
                percentages.All(value => oldText.Contains(value) && text.Contains(value)), "IdentityScanChart right-axis labels");
            string[] ticks = { "95%", "75%", "55%" };
            add(chartSource, 179, 179, "actual identity ticks retain percent suffix", ticks, text.Where(ticks.Contains).ToArray(),
                ticks.All(value => oldText.Contains(value) && text.Contains(value)), "IdentityScanChart identity tick labels");
            foreach (var descriptor in new[] { (196, "floor 75%"), (213, "this file, 75%"), (228, "non-contaminant target connected components"),
                (233, "families mixing target and decoy"), (239, "USEARCH identity, strictest on the left") })
                add(chartSource, descriptor.Item1, descriptor.Item1, "actual frozen/current rendered chart label across consecutive glyph runs", descriptor.Item2,
                    new { Present = Label(text, descriptor.Item2), Glyphs = text.ToArray() }, Label(oldText, descriptor.Item2) && Label(text, descriptor.Item2),
                    "IdentityScanChart glyph label " + descriptor.Item2);
        }
        finally { host.Close(); oldHost.Close(); }
        string report = "Native identity fixture\n95\t5000\n75\t3000\n55\t1500\n";
        Type oldWindowType = legacy.GetType("FamilyQuant.IdentityScanWindow", true)!;
        var old = (Window)SecondaryCall(null, oldWindowType, "Create", "Native identity", oldRows, report, 75)!;
        var current = IdentityScanWindow.Create("Native identity", rows, report, 75);
        SecondaryHost(old); SecondaryHost(current);
        try
        {
            var oldSave = WalkTree(old).OfType<Button>().Single(button => button.Content as string == "Save figure (PNG)");
            var save = WalkTree(current).OfType<Button>().Single(button => button.Content as string == "Save figure (PNG)");
            add("FamilyQuant/IdentityScanWindow.cs", 77, 77, "native Save figure buttons are reachable", oldSave.GetType().Name, save.GetType().Name,
                oldSave.IsVisible && save.IsVisible, "IdentityScanWindow Save figure button", row => row.Kind == "code control");
            add("FamilyQuant/IdentityScanWindow.cs", 79, 79, "native Save figure caption", oldSave.Content, save.Content, Equals(oldSave.Content, save.Content), "IdentityScanWindow Save figure caption");
            var oldReport = WalkTree(old).OfType<TextBox>().Single(box => box.Text == report);
            var newReport = WalkTree(current).OfType<TextBox>().Single(box => box.Text == report);
            add("FamilyQuant/IdentityScanWindow.cs", 101, 101, "native identity report remains selectable and read-only with exact source report",
                new { Text = oldReport.Text, ReadOnly = oldReport.IsReadOnly }, new { Text = newReport.Text, ReadOnly = newReport.IsReadOnly },
                oldReport.IsReadOnly && newReport.IsReadOnly && oldReport.Text == newReport.Text && oldReport.IsVisible && newReport.IsVisible,
                "IdentityScanWindow report TextBox", row => row.Kind == "code control");
        }
        finally { current.Close(); old.Close(); }
        var disconnectedOld = (Window)SecondaryCall(null, oldWindowType, "Create", "Disconnected identity", oldRows, report, 75)!;
        var disconnected = IdentityScanWindow.Create("Disconnected identity", rows, report, 75);
        try
        {
            var oldButton = WalkTree(disconnectedOld).OfType<Button>().Single(button => button.Content as string == "Save figure (PNG)");
            var button = WalkTree(disconnected).OfType<Button>().Single(candidate => candidate.Content as string == "Save figure (PNG)");
            SecondaryClick(oldButton); SecondaryClick(button);
            string ReadRefusal(Window window) => WalkTree(window).OfType<TextBlock>().Select(block => block.Text).Single(value => value.StartsWith("Nothing was saved", StringComparison.Ordinal));
            string before = ReadRefusal(disconnectedOld), actual = ReadRefusal(disconnected);
            add("FamilyQuant/IdentityScanWindow.cs", 167, 169, "actual Save click on disconnected visuals refuses a native dialog", before, actual,
                before == actual, "IdentityScanWindow SaveFigureFromDialog headless refusal");
            add("FamilyQuant/IdentityScanWindow.cs", 88, 88, "actual Save button event reaches the display guard", before, actual,
                before == actual, "IdentityScanWindow Save button Click", row => row.Kind == "handler");
        }
        finally { disconnected.Close(); disconnectedOld.Close(); }
        string png = Path.Combine(output, "identity-" + theme + ".png");
        IdentityScanWindow.SaveFigure(rows, png, 75);
        var bitmap = BitmapFrame.Create(new Uri(png, UriKind.Absolute), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        add("FamilyQuant/IdentityScanWindow.cs", 179, 179, "native programmatic figure export writes a readable PNG at production dimensions", ".png/1920/1080",
            Path.GetExtension(png) + "/" + bitmap.PixelWidth + "/" + bitmap.PixelHeight, bitmap.PixelWidth == 1920 && bitmap.PixelHeight == 1080,
            "IdentityScanWindow.SaveFigure / ChartImageExporter");
    }
}
