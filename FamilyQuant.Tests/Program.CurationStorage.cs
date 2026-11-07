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
        private static void CurationSaveResultTests(string workDir)
        {
            var file = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended>
            {
                new() { SequenceIdentifier = "save_fixture", Sequence = "PEPTIDE", Description = "Save fixture", UserID = 7, USearchID = 7 }
            } };
            var control = new ClusterGenerationControl(file) { UseShellStatusBar = true };
            var status = UiField<TextBlock>(control, "TextBlockStatusLine")!;
            string originalFact = (string)status.ToolTip;
            int changed = 0; control.ShellStatusChanged += (_, _) => changed++;
            string path = Path.Combine(workDir, "curation-save-result.fcf");
            control.SaveFamilyClusterFile(path);
            Check("curation save receives the native E12 verification result", file.LastSaveCheck?.Passed == true);
            Check("curation save reports its result through the shell status and retains the written document",
                control.ShellStatusText == file.LastSaveCheck!.Message && changed == 1
                && UiField<TextBlock>(control, "TextBlockStatusLine")!.Text == control.ShellStatusText
                && ((string)status.ToolTip).Contains(originalFact, StringComparison.Ordinal)
                && FamilyClusterFile.Load(path).MyExtendedFastaItems.Single().UserID == 7);
            control.UseShellStatusBar = false;
            var host = new Window { Content = control, Width = 1366, Height = 820, Left = -30000, Top = -30000,
                WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None,
                ShowActivated = false, ShowInTaskbar = false };
            host.Show();
            try
            {
                foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
                {
                    FamilyQuant.Themes.ThemeManager.Apply(theme); PumpDispatcher(); host.UpdateLayout();
                    ScreenHarness.Save(host, Path.Combine(workDir, "curation-save-result-" + theme.ToString().ToLowerInvariant() + ".png"), 1366, 820);
                }
            }
            finally { host.Close(); FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark); }
            typeof(ClusterGenerationControl).GetMethod("UpdateStatusLine", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, null);
            Check("the ordinary curation status restores its tooltip fact after a save result", status.ToolTip as string == originalFact && changed == 2);
        }

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
            Check("the current User XIC survives bit-exactly",
                BitConverter.DoubleToInt64Bits(b.XICUser) == BitConverter.DoubleToInt64Bits(a.XICUser));
            using (var written = JsonDocument.Parse(File.ReadAllText(path)))
            {
                var body = written.RootElement.ValueKind == JsonValueKind.Array
                    ? written.RootElement[0].GetProperty("Curation") : written.RootElement;
                Check("new curation files omit the unused legacy XIC field",
                    !body.GetProperty("MyExtendedFastaItems")[0].TryGetProperty("XIC", out _));
            }

            const string legacy = "{\"ClusteringIdentity\":70,\"MyExtendedFastaItems\":[{\"SequenceIdentifier\":\"legacy\",\"Sequence\":\"MKAWLL\",\"USearchID\":2,\"UserID\":5,\"XIC\":0.125,\"XICUser\":0.0625}]}";
            var imported = FamilyClusterFile.FromJsonText(legacy);
            Check("old files still import legacy XIC bit-exactly",
                BitConverter.DoubleToInt64Bits(imported.MyExtendedFastaItems[0].XIC) ==
                BitConverter.DoubleToInt64Bits(0.125));
            Check("legacy import retains current quantitation, sequences and both family numbers",
                imported.MyExtendedFastaItems[0].XICUser == 0.0625 &&
                imported.MyExtendedFastaItems[0].Sequence == "MKAWLL" &&
                imported.MyExtendedFastaItems[0].USearchID == 2 && imported.MyExtendedFastaItems[0].UserID == 5);
            string firstWrite = imported.ToJsonText();
            string reopenedWrite = FamilyClusterFile.FromJsonText(firstWrite).ToJsonText();
            Check("save and reopen retain byte-identical canonical document text", firstWrite == reopenedWrite);
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
