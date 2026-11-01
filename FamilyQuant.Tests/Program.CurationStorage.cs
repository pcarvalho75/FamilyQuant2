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

        // ========================================================= round trip

        private static void RoundTripTests(string workDir)
        {
            var original = new FamilyClusterFile
            {
                ClusteringIdentity = 70,
                MyExtendedFastaItems = new List<FastaItemExtended>
                {
                    new FastaItemExtended
                    {
                        SequenceIdentifier = "sp|P00001|TEST",
                        Description = "Metalloproteinase, \"partial\"",
                        Sequence = "MKAWLL",
                        UserAnnotation = "SVMP",
                        USearchID = 2,
                        UserID = 5,
                        XIC = 0.125,
                        XICUser = 0.0625
                    }
                }
            };

            string path = Path.Combine(workDir, "roundtrip.fcf");
            original.Save(path);
            var loaded = FamilyClusterFile.Load(path);
            var a = original.MyExtendedFastaItems[0];
            var b = loaded.MyExtendedFastaItems[0];

            Check("clustering identity survives", loaded.ClusteringIdentity == 70);
            Check("the protein survives", loaded.MyExtendedFastaItems.Count == 1);
            Check("locus survives", b.SequenceIdentifier == a.SequenceIdentifier);
            Check("a description containing quotes survives", b.Description == a.Description);
            Check("sequence survives", b.Sequence == a.Sequence);
            Check("user annotation survives", b.UserAnnotation == a.UserAnnotation);
            Check("both family ids survive", b.USearchID == a.USearchID && b.UserID == a.UserID);
            Check("XIC survives bit-exactly",
                BitConverter.DoubleToInt64Bits(b.XIC) == BitConverter.DoubleToInt64Bits(a.XIC));
            Check("the computed grouping is not duplicated into the file",
                !File.ReadAllText(path).Contains("GroupedFastaItems"));

            // How the families were built must survive a save/load, otherwise a reopened file looks like a
            // plain identity clustering even when it was reclustered by words before saving.
            Check("a plain identity clustering reports no word reclustering",
                loaded.GroupingDescription() == "none", loaded.GroupingDescription());

            original.WordSimilarity = 0.7;
            string wordPath = Path.Combine(workDir, "reclustered.fcf");
            original.Save(wordPath);
            Check("the word-similarity threshold survives a save/load",
                FamilyClusterFile.Load(wordPath).WordSimilarity == 0.7);

            original.IsKeywordSuperCluster = true;
            string keywordPath = Path.Combine(workDir, "supercluster.fcf");
            original.Save(keywordPath);
            Check("a keyword super cluster is labelled as such after reloading",
                FamilyClusterFile.Load(keywordPath).GroupingDescription() == "keywords");

            Check("a corrupt file fails loudly", Throws<Exception>(() =>
            {
                string bad = Path.Combine(workDir, "bad.fcf");
                File.WriteAllText(bad, "this is not json");
                FamilyClusterFile.Load(bad);
            }));
        }
    }
}
