using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace FamilyQuant.Tests;

// Native calls only, linked unchanged into the frozen host. No real project or deck input.
internal static class AssayFigureGapSnapshot
{
    internal const string Argument = "--assay-figure-gap-snapshot";
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh lane-owned fixture output folder.");
        string output = Path.GetFullPath(args[1]);
        string lane = Path.GetFullPath("D:/FamilyQuant-data/out-assay") + Path.DirectorySeparatorChar;
        if (!output.StartsWith(lane, StringComparison.OrdinalIgnoreCase)) throw new IOException("Fixture figures must remain under out-assay.");
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        var oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            string Summary(FamilyClusterFile file)
            {
                var result = ReferenceComponentSummary.FromCuration(file);
                return string.Join("/", new[] { result.AllComponents, result.PureDecoyComponents, result.TargetBearingComponents,
                    result.ContaminantBearingTargetComponents, result.NonContaminantTargetComponents, result.MixedTargetDecoyComponents }
                    .Select(value => value.ToString(CultureInfo.InvariantCulture)));
            }
            values["reference/empty"] = Summary(new FamilyClusterFile());
            var fixture = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>() };
            string[][] groups =
            {
                new[] { "fixture_target" }, new[] { "contaminant_fixture" }, new[] { "CONTAMINANT_fixture" },
                new[] { "Reverse_fixture" }, new[] { "reverse_fixture" }, new[] { "fixture_mixed", "Reverse_fixture_mixed" },
                new[] { "Reverse_contaminant_fixture" }, new[] { "fixture_contaminant_internal" }
            };
            for (int index = 0; index < groups.Length; index++)
                foreach (string identifier in groups[index]) fixture.MyExtendedFastaItems.Add(new FastaItemExtended
                { SequenceIdentifier = identifier, Sequence = "ACDEFGHIK", UserID = index + 1, USearchID = index + 1 });
            values["reference/boundaries"] = Summary(fixture);
            foreach (string identifier in groups.SelectMany(group => group))
                values["reference/one/" + identifier] = Summary(new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
                    { new() { SequenceIdentifier = identifier, Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1 } } });

            string Draw(string mode)
            {
                var input = new DeNovoInputManifest { SourceId = "fixture-source", SourceDisplayName = "FixtureSourceAlpha",
                    ConfidenceField = "FixtureMetricAlpha", MinimumConfidence = 87.125, SourcePriority = 0 };
                if (mode == "SourceDisplayName") input.SourceDisplayName = "FixtureChangedSource";
                if (mode == "ConfidenceField") input.ConfidenceField = "FixtureChangedMetric";
                if (mode == "MinimumConfidence") input.MinimumConfidence = 91.375;
                var manifest = new QuantManifest { DeNovoIdentification = new DeNovoIdentificationSummary(),
                    DeNovoInputs = new List<DeNovoInputManifest> { input } };
                var chart = new DeNovoConsolidationChart(new AnalyticsData(), manifest);
                ChartImageExporter.Save(chart, Path.Combine(output, mode + ".png"));
                var text = new StringBuilder();
                void Walk(Drawing drawing)
                {
                    if (drawing is DrawingGroup group) foreach (Drawing child in group.Children) Walk(child);
                    if (drawing is not GlyphRunDrawing glyph) return;
                    var run = glyph.GlyphRun;
                    if (run.Characters != null && run.Characters.Count > 0) text.Append(new string(run.Characters.ToArray()));
                    else
                    {
                        var ascii = run.GlyphTypeface.CharacterToGlyphMap.Where(pair => pair.Key >= 32 && pair.Key < 127)
                            .GroupBy(pair => pair.Value).ToDictionary(group => group.Key, group => (char)group.Min(pair => pair.Key));
                        foreach (ushort id in run.GlyphIndices) text.Append(ascii.TryGetValue(id, out char character) ? character : '?');
                    }
                    text.Append('\n');
                }
                DrawingGroup? drawing = VisualTreeHelper.GetDrawing(chart);
                if (drawing == null) throw new InvalidDataException("Native exported chart retained no drawing.");
                Walk(drawing); return text.ToString();
            }
            values["render/base"] = Draw("base");
            foreach (var field in new[] { ("SourceDisplayName", "FixtureSourceAlpha", "FixtureChangedSource"),
                ("ConfidenceField", "FixtureMetricAlpha", "FixtureChangedMetric"), ("MinimumConfidence", "87.125", "91.375") })
            {
                string rendered = Draw(field.Item1); values["render/" + field.Item1] = rendered;
                values["dynamic/" + field.Item1] = (values["render/base"].Contains(field.Item2, StringComparison.Ordinal)
                    && rendered.Contains(field.Item3, StringComparison.Ordinal) && !rendered.Contains(field.Item2, StringComparison.Ordinal)).ToString();
            }
            File.WriteAllText(Path.Combine(output, "figure-values.json"), JsonSerializer.Serialize(values,
                new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
            Console.WriteLine("PASS native reference summary and four synthetic figure exports");
            return 0;
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }
    }
}
