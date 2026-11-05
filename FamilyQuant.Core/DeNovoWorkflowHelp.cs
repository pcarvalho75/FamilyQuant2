using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System.Globalization;

namespace FamilyQuant;

/// <summary>Seven workflow facts. Code evidence for each paragraph is indexed in docs/DE-NOVO-WORKFLOW.md.</summary>
public static class DeNovoWorkflowHelp
{
    public const string Title = "De novo in FamilyQuant";

    public static string Build()
    {
        var filters = new DeNovoFilterCriteria();
        string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
        return Title + "\n\n"
            // FullXicInputStage.Prepare; ComputeXicViewModel.ComputeAsync.
            + "1. PatternLab identifications. In Quantify, choose the RAW folder and the trusted SEPro identification package (.sepr2) from the same runs. The optional PatternLab .plp share column is a separate input.\n\n"
            // FullXicSourceStage.Read; DeNovoFilter.Passes; DeNovoSourceSelection.
            + "2. De novo calls. Add the PEAKS export root and choose the source. The default PEAKS ALC cutoff is " + Number(filters.MinAverageLocalConfidence)
            + "%; minimum length is " + Number(filters.MinLength) + " residues, with " + Number(filters.StretchLength) + " consecutive local-confidence values at or above "
            + Number(filters.StretchFloor) + "%. Other selected sources keep their own confidence scales and cutoffs.\n\n"
            // SpectrumComparisonTable.ApplyPatternLabConflictRule; BuildClusters; FullXicCompletionStage.Complete.
            + "3. Same-scan alternatives. Runs match by file name. Within the same scan, charge and an isotope-aware " + Number(DeNovoUnionResolver.PrecursorTolerancePpm)
            + "-ppm precursor hypothesis, PatternLab wins a different-sequence de novo call when PatternLab wins conflicting precursors is ON, the default. Sequence comparison removes PTMs and treats I = L. Other precursor hypotheses stay separate. The choice is stored at Compute and shown in report Methods; old stored results change only after Compute.\n\n"
            + "The rule reads the package's FDR-accepted PSMs, including any decoy PSMs retained there; a decoy PSM can therefore win within the package's FDR control.\n"
            + "The cluster's top-XCorr PatternLab claim must have a sequence to win; a de novo call matching only a lower-ranked PSM is still excluded.\n\n"
            // NativeQuantFractions.CrossSideKey; IsCrossSideEquivalent; Compute.
            + "4. Avoid double credit. A de novo peptide equivalent to an identified peptide is not credited again. The production rule compares run, peptidoform, charge and isotope-aware precursor mass with I = L; genuinely different PTMs remain distinct.\n\n"
            // FullXicRunStage.Extract; NativeQuantFractions.DedupToPeptides; ReferenceEnvelopeEstimator.Apply.
            + "5. Peptide XICs. Quantify reads RAW data for identified and additional de novo peptide XICs. Repeated anchors collapse to one representative per run, peptidoform and charge. Identified primary signal uses the observed isotope envelope; de novo primary signal uses identified-reference calibration. A missing isotope adds no signal.\n\n"
            // FamilyEnvelopeQuant.SearchCuration; HomologyOptions; HomologySearch; DecoyIdentityGate.
            + "6. Family homology. De novo peptides are mapped by PepExplorer-style homology at a default minimum identity of " + Number(100 * HomologyOptions.DefaultMinIdentity)
            + "% with I = L. Recorded mapping choices can restrict the matches, including score-tier selection. If the decoy identity gate is enabled it can raise the identity floor; a decoy wins an exact tie.\n\n"
            // MainWindow.PresentQuantifyScreen; ProjectStore; HelpWorkflowFacts.DeNovo; DeNovoControl.
            + "7. Results and optional review. Save the project after Quantify and read the stored family shares in Results. De novo review is optional inspection: it can examine source calls and comparable-precursor alternatives, but it does not apply a quantitation rule or change stored quantities.\n";
    }
}
