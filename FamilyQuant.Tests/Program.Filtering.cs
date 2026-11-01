using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {

        // ============================================================= filters

        private static void FilterTests()
        {
            var items = new List<FastaItemExtended>
            {
                new FastaItemExtended { SequenceIdentifier = "sp|A|X", Description = "Snake venom metalloproteinase", USearchID = 0, UserID = 0, XIC = 0.4, XICUser = 0.4 },
                new FastaItemExtended { SequenceIdentifier = "sp|B|Y", Description = "Phospholipase A2", USearchID = 1, UserID = 1, XIC = 0 },
                new FastaItemExtended { SequenceIdentifier = "Reverse_sp|A|X", Description = "", USearchID = 2, UserID = 2, XIC = 0 },
                new FastaItemExtended { SequenceIdentifier = "contaminant_KRT1", Description = "Keratin", USearchID = 3, UserID = 3, XIC = 0.1, XICUser = 0.1 },
            };

            Check("no filter keeps every row", FamilyFilter.Apply(items, "", false, false).Count == 4);
            Check("the counter sees every family", FamilyFilter.CountFamilies(items) == 4);

            var hidden = FamilyFilter.Apply(items, "", false, true);
            Check("hiding decoys drops the Reverse_ row", hidden.Count == 3);
            Check("the counter follows the filter - this is why it can read lower than the file",
                FamilyFilter.CountFamilies(hidden) == 3);

            Check("the XIC filter keeps only quantified rows", FamilyFilter.Apply(items, "", true, false).Count == 2);

            var noContaminants = FamilyFilter.Apply(items, "", false, false, true);
            Check("hiding contaminants drops the contaminant row", noContaminants.Count == 3);
            Check("hiding contaminants keeps the real proteins",
                noContaminants.Any(i => i.SequenceIdentifier == "sp|A|X") && noContaminants.Any(i => i.SequenceIdentifier == "sp|B|Y"));

            var neither = FamilyFilter.Apply(items, "", false, true, true);
            Check("hiding both decoys and contaminants leaves only real proteins", neither.Count == 2);

            Check("a decoy of a contaminant counts as a contaminant",
                FamilyFilter.IsContaminant(new FastaItemExtended { SequenceIdentifier = "Reverse_contaminant_KRT1" }));
            Check("a real protein is not a contaminant",
                !FamilyFilter.IsContaminant(new FastaItemExtended { SequenceIdentifier = "sp|P30431|VM3JA_BOTJA" }));
            Check("search matches the description", FamilyFilter.Apply(items, "phospholipase", false, false).Count == 1);
            Check("search ignores case", FamilyFilter.Apply(items, "METALLOPROTEINASE", false, false).Count == 1);
            Check("search matches the locus", FamilyFilter.Apply(items, "sp|B|", false, false).Count == 1);
            Check("whitespace is not a search", FamilyFilter.Apply(items, "   ", false, false).Count == 4);
            Check("no match yields an empty grid", FamilyFilter.Apply(items, "zzzz", false, false).Count == 0);
            Check("filters combine", FamilyFilter.Apply(items, "", true, true).Count == 2);
        }
    }
}
