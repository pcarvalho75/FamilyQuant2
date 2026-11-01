using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    // A checker only. The patterns below are typed here as a user would type them into the motif box, and
    // the families are exp8's own classes, so only the counting is graded against exp8's biology table.
    internal static class MotifCheckRun
    {
        internal const string Argument = "--motif-check";

        private static readonly (string Family, string Name, string Pattern)[] Typed =
        {
            ("SVSP", "His-Asp-Ser triad: TAAHC or GDSGGP", "[TSV]AAHC|GDSGGP"),
            ("PLA2", "active-site loop CC..H.C (K49/S49 included)", "CC..H.C"),
            ("PLA2", "12-14 cysteines", "count C 12-16"),
            ("Snaclec (CTL)", "WND-type lectin motif", "W[A-Z]D"),
            ("LAAO", "FAD site GxGxxG", "G.G..G"),
            ("CRISP", "14-18 cysteines", "count C 14-18"),
            ("Kunitz", "6-8 cysteines", "count C 5-10"),
            ("Natriuretic peptide", "ring C..DRI...S.LGC", "C.....[DN]RI"),
            ("svVEGF", "cystine-knot PDGF, 8+ cysteines", "count C >= 8"),
            ("NGF", "6 cysteines", "count C >= 6"),
            ("SVMP", "zinc motif HE..H..G..H", "HE..H..G..H"),
            ("Disintegrin", "no zinc motif", "not HE..H..G..H"),
        };

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string At(int offset, string fallback) =>
                    args.Length > flag + offset && args[flag + offset].Length > 0 ? args[flag + offset] : fallback;
                string input = At(1, "D:/FamilyQuant-data/out-evidence/ana-evidence.fcf");
                string classes = At(2, "D:/FamilyQuant-data/baseline/classes_all.tsv");
                string expected = At(3, "D:/FamilyQuant-data/out-opus/e8-exp8-table.tsv");
                string outDir = At(4, "D:/FamilyQuant-data/out-opus");

                FamilyClusterFile file = FamilyClusterFile.Load(input);
                file.Motifs = Typed.Select(t => new MotifPattern { Name = t.Name, Pattern = t.Pattern }).ToList();
                string saved = Path.Combine(outDir, "e8-motifs.fcf");
                file.Save(saved);
                FamilyClusterFile reopened = FamilyClusterFile.Load(saved);
                check("the typed motifs are stored in the project and read back", reopened.Motifs != null
                    && JsonSerializer.Serialize(reopened.Motifs) == JsonSerializer.Serialize(file.Motifs), saved);

                Dictionary<string, FastaItemExtended> byId = reopened.MyExtendedFastaItems
                    .ToDictionary(i => i.SequenceIdentifier, StringComparer.Ordinal);
                var members = File.ReadLines(classes).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t'))
                    .Select(c => (Family: c[1], Protein: byId[c[0]])).ToList();
                List<MotifResult> results = MotifCheck.CountAll(reopened, members);

                Dictionary<(string, string), (int Carriers, int Complete)> python = File.ReadLines(expected).Skip(1)
                    .Where(l => l.Length > 0).Select(l => l.Split('\t'))
                    .ToDictionary(c => (c[0], c[1]), c => (int.Parse(c[2]), int.Parse(c[3])));
                var rows = new List<object>();
                int equal = 0;
                for (int i = 0; i < Typed.Length; i++)
                {
                    MotifFamilyCount row = results[i].Families.Single(f => f.Family == Typed[i].Family);
                    var theirs = python[(Typed[i].Family, Typed[i].Name)];
                    bool same = row.Carriers == theirs.Carriers && row.Complete == theirs.Complete;
                    if (same) equal++;
                    rows.Add(new
                    {
                        family = Typed[i].Family, motif = Typed[i].Name, typed = Typed[i].Pattern,
                        program = row.Carriers + " of " + row.Complete, exp8 = theirs.Carriers + " of " + theirs.Complete, same,
                    });
                }
                string text = JsonSerializer.Serialize(new { input, classes, rows }, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(outDir, "e8-report.json"), text);
                Console.WriteLine(text);
                check("typing exp8's patterns reproduces its biology table", equal == Typed.Length,
                    equal + " of " + Typed.Length + " rows equal");
            }
            catch (Exception ex) { check("motif real-data check completed", false, ex.GetBaseException().ToString()); }
        }
    }
}
