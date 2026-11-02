using System;
using FamilyQuant.DeNovo;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ResultsTooltipTests()
    {
        Section("Results tooltip wording - two sentences and retained facts");
        EnsureApplication();
        var curation = new ClusterGenerationControl(new FamilyClusterFile());
        var panel = (CurationQuantitationPanel)curation.FindName("QuantitationPanel");
        void Verify(string name, object tooltip, params string[] facts)
        {
            string text = tooltip.ToString()!;
            int sentences = Regex.Split(text.Trim(), @"(?<=[.!?])\s+", RegexOptions.CultureInvariant).Length;
            Check(name + " tooltip has at most two sentences", sentences is > 0 and <= 2);
            Check(name + " tooltip retains each required fact and its dynamic default", facts.All(fact => text.Contains(fact, StringComparison.OrdinalIgnoreCase)), text);
        }
        Verify("shared-peptide policy", ((ComboBox)panel.FindName("ComboBoxSharedPeptides")).ToolTip,
            "peptide found in more than one family", "redundant database", "venom", "moves the numbers a great deal",
            "Default:", SharedPeptidePolicy.FamilyUnique.DisplayName());
        Verify("normalization", ((ComboBox)panel.FindName("ComboBoxNormalization")).ToolTip,
            "share column", "percentage", "measurements", "never change", "greyed out", "loaded project", "totals",
            "Default:", NormalizationMode.IdentifiedSignal.DisplayName());
        Verify("recovery", new RecoveryChart(Array.Empty<NativeRunFraction>()).ToolTip,
            "Top:", "recovered ion current", "M+0", "PRIMARY estimate", "unadjusted diagnostic", "Bottom: TIC",
            "isotope-series detector", "primary estimate", "not peptide-FDR calibrated", "does not partition the TIC", "biological classes");
        Verify("isotope count", new IsotopeCountChart(Array.Empty<AnalyticsPeptide>()).ToolTip,
            "Each bar counts", "peptides credited that many isotopes", "from 1 up to the",
            AnalyticsBuilder.IsotopeBasis.ToString(CultureInfo.CurrentCulture) + "-isotope ceiling",
            "isotope is credited only where signal was found", "colours", "split each bar by peptide mass");
    }
}
