using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void MotifCheckTests(string workDir)
        {
            Check("a typed motif is a regular expression over the residues",
                MotifCheck.Compile("GDSGGP")("MKVGDSGGPLV") && !MotifCheck.Compile("GDSGGP")("MKVGDSGGALV")
                && MotifCheck.Compile("[TSV]AAHC|GDSGGP")("QSAAHCY") && MotifCheck.Compile("CC..H.C")("ACCKLHGC"));
            Check("\"not\" counts the proteins without the motif",
                MotifCheck.Compile("not HE..H..G..H")("MAAAA") && !MotifCheck.Compile("not HE..H..G..H")("HEAAHAAGAAH"));
            Check("a count of one residue, as a range or a bound",
                MotifCheck.Compile("count C 2-3")("CACAC") && !MotifCheck.Compile("count C 2-3")("CCCC")
                && MotifCheck.Compile("count c >= 4")("CCCC") && !MotifCheck.Compile("count C <= 3")("CCCC"));
            Check("lower case is read as the residues it names",
                MotifCheck.Compile("gdsggp")("MKVGDSGGPLV"));
            bool Refused(string pattern, string mustSay)
            {
                try { MotifCheck.Compile(pattern); return false; }
                catch (ArgumentException ex) { return ex.Message.Contains(mustSay, StringComparison.Ordinal); }
            }
            Check("a motif that cannot be read is refused with the reason, never counted as no carrier",
                Refused("CC[..H", "is not a motif the program can read") && Refused("count C 9-3", "is empty")
                && Refused("count C some", "is not a count") && Refused("  ", "Type a motif first") && Refused("not ", "needs a motif"));

            FastaItemExtended P(string id, string sequence, bool? fragment = false, string description = "") => new FastaItemExtended
            {
                SequenceIdentifier = id, Sequence = sequence, Description = description,
                Evidence = fragment == null ? null : new ProteinEvidence { Accession = id, Fragment = fragment },
            };
            FastaItemExtended shared = P("p1", "AAGDSGGPAA");
            var members = new List<(string, FastaItemExtended)>
            {
                ("Beta", P("p0", "GDSGGP")),
                ("Alpha", shared),
                ("Alpha", shared),
                ("Alpha", P("p2", "AAAAAA")),
                ("Alpha", P("p3", "GDSGGP", fragment: true)),
                ("Alpha", P("p4", "GDSGGP", fragment: null, description: "x (Fragment) OS=Testus")),
                ("Alpha", P("p5", "GDSGGP", fragment: null)),
            };
            MotifResult result = MotifCheck.Count(new MotifPattern { Name = "triad", Pattern = "GDSGGP" }, members);
            Check("carriers are counted per family on complete members, each protein once, families in the order met",
                result.Families.Select(f => f.Family + " " + f.Carriers + "/" + f.Complete).SequenceEqual(new[] { "Beta 1/1", "Alpha 2/3" }),
                string.Join(", ", result.Families.Select(f => f.Family + " " + f.Carriers + "/" + f.Complete)));
            Check("a fragment is known from the evidence or from its description",
                !MotifCheck.IsComplete(P("x", "A", fragment: true)) && !MotifCheck.IsComplete(P("x", "A", null, "y (Fragments) OS=Z"))
                && MotifCheck.IsComplete(P("x", "A", null, "y OS=Z")));

            FamilyClusterFile file = BuildSyntheticFcf();
            string before = file.ToJsonText();
            Check("a file with no motif is written exactly as before", !before.Contains("\"Motifs\"", StringComparison.Ordinal));
            file.Motifs = new List<MotifPattern> { new() { Name = "triad", Pattern = "GDSGGP" }, new() { Name = "rich", Pattern = "count C >= 6" } };
            string path = Path.Combine(workDir, "motifs.fcf");
            file.Save(path);
            FamilyClusterFile reopened = FamilyClusterFile.Load(path);
            Check("the motifs the user typed are stored in the project, in order, and read back",
                reopened.Motifs?.Select(m => m.Name + "=" + m.Pattern).SequenceEqual(new[] { "triad=GDSGGP", "rich=count C >= 6" }) == true);
            Check("every stored motif is counted against the same families",
                MotifCheck.CountAll(reopened, members).Select(r => r.Families.Single(f => f.Family == "Alpha").Carriers)
                    .SequenceEqual(new[] { 2, 0 }));
        }
    }
}
