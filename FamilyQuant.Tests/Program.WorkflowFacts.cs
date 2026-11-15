using System;
using System.Linq;
using System.Windows;
using System.IO;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WorkflowFactTests()
    {
        void Facts(string screen, string text, params string[] required)
        {
            var missing = required.Where(fact => !Flatten(text).Contains(fact, StringComparison.OrdinalIgnoreCase)).ToArray();
            Check("Y6 " + screen + " publishes its retained workflow facts", missing.Length == 0, string.Join("; ", missing));
        }
        var home = new HomeControl(); LayoutAt(home, new Size(1366, 900), -1);
        Facts("Home", string.Join(" ", ScreenWording(home)), "Five steps, one .fqproj", "once per database", "Skip this step",
            "User IDs", "Save afterwards", "reads no RAW", "inspection only", "not needed for quantitation", "whether full XIC has been computed");
        var build = new USearchLib(); LayoutAt(build, new Size(1366, 900), -1);
        Facts("Build", build.BuildHelpText() + " " + string.Join(" ", ScreenWording(build)), "higher identity gives more, smaller families",
            "type the reported value", "lower values are slower", "_NO_REDUNDANCY.fasta beside", "turn it off only for speed", "All files (*.*)");
        string general = HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique);
        Facts("Families", general, "Ctrl+A selects all rows in the filtered protein view", "0.10 to 1.00", "towards 0.90",
            "family numbers change", "family numbers stay unchanged", "starts off", "protein-level export makes every row read 0",
            "summed TIC of every recorded run", "shared-peptide row appears only with Family-unique only");
        var model = new ComputeXicViewModel("UI fact test");
        var quantify = new QuantifyControl(model); LayoutAt(quantify, new Size(1366, 900), -1);
        Facts("Quantify", model.Details + " " + string.Join(" ", ScreenWording(quantify)), "one acquisition at a time", "validated extraction cache",
            "3 requires at least one real detection", "5 requires at least three", "legacy heights are not comparable with integrated areas",
            "*.denovoOnly.csv", "*.denovo.csv", "*.deepnovo.csv", "different confidence scales", "measured once under the PEAKS call",
            "CSV is written only after a successful computation", "same rows as the spectrum table");
        string missing = Path.Combine(Path.GetTempPath(), "Y6-missing-" + Guid.NewGuid().ToString("N"));
        model.RawDirectory = missing; model.ComputeAsync().GetAwaiter().GetResult();
        Check("Y6 Quantify RAW refusal names the path and says nothing was computed",
            model.Status.Contains(missing, StringComparison.Ordinal) && model.Status.Contains("Nothing was computed", StringComparison.Ordinal));
        model.RawDirectory = Path.GetTempPath(); model.SeproPackage = missing + ".sepr2";
        model.ComputeAsync().GetAwaiter().GetResult();
        Check("Y6 Quantify SEPro refusal names the file and the matching-runs next step",
            model.Status.Contains(model.SeproPackage, StringComparison.Ordinal) && model.Status.Contains("same runs", StringComparison.Ordinal));
        Facts("Files and navigation", general, "nothing is saved", "whole numbers with digit grouping", "names the written CSV path",
            "status bar", "what to do next", "Tab and Shift+Tab", "without quantitation or analysis report facts");
        Facts("De novo", HelpContent.BuildDeNovo(), "one source or a union", "Quantify can combine sources", "per searched peptide",
            "family IDs, protein IDs and ambiguity", "reads peptides, not chromatograms", "use Quantify and Results");
        var sourceChoice = (System.Windows.Controls.ComboBox)new DeNovoControl().FindName("ComboBoxDeNovoSource");
        string sourceTip = (string)sourceChoice.ToolTip;
        Check("de novo inspection tooltip agrees with its selectable source unions",
            sourceChoice.Items.OfType<System.Windows.Controls.ComboBoxItem>().Any(item => item.Tag is string tag && tag.StartsWith("union-", StringComparison.Ordinal))
            && sourceTip.Contains("one source or a union", StringComparison.OrdinalIgnoreCase)
            && !sourceTip.Contains("one source at a time", StringComparison.OrdinalIgnoreCase));
        Facts("Locks", LockManagerViewModel.Explanation, "read-only", "zero remaining proteins", "Still in use", "already removed from the open file");
        Facts("Spectrum table", SpectrumComparisonViewModel.Explanation, "number of rows shown", "read-only", "one scan can have several rows");
        PrefixDefinitionTests();
    }
}
