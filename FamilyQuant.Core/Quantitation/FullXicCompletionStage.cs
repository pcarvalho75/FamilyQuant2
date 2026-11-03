using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>Derives the stored fractions and diagnostics, builds the complete spectrum evidence
    /// table, then attempts the optional CSV. No RAW extraction is performed here.</summary>
    internal static class FullXicCompletionStage
    {
        internal static FullXicComputeResult Complete(FullXicInput input, FullXicOptions options,
            FullXicComputeResult result, IReadOnlyList<RawScanPrecursorMetadata> rawPrecursorMetadata)
        {
            string rawDirectory = input.RawDirectory;
            List<PatternLabPsm> psms = input.Psms;
            FullXicSourceInput deNovoImport = input.Sources;
            QuantManifest manifest = result.Manifest;
            // Q3: derive the fractions of the TIC from the raw per-peptide quant and record them in the
            // manifest, so the paper numbers persist in the .fqproj. The raw rows stay the source of
            // truth; this only reads them. The dedup collapse (one max-M+0 area per sequence, charge and
            // run) is what keeps every fraction below 100%.
            NativeQuantFractions.PopulateManifest(manifest, result.Rows);

            // Persist the exact reference-envelope model diagnostics beside the derived recovery
            // fractions. A reopened project can therefore reproduce the diagnostics PNG/CSV without
            // reopening RAW files or silently refitting a different reference set.
            manifest.ReferenceEnvelopeModelDiagnostics = ReferenceEnvelopeEstimator.Evaluate(
                NativeQuantFractions.DedupToPeptides(result.Rows));

            // The complete table is built while every source call and the RAW-selected precursor
            // metadata are still available, then persisted in the project. Rebuilding from QuantRow
            // alone would lose rejected audit calls and accepted calls that produced no stored XIC.
            PeptideFamilyAttribution? familyAttribution = null;
            CurationHomologyHits? familyHits = null;
            if (options.FamilyCuration != null)
            {
                try
                {
                    familyHits = FamilyEnvelopeQuant.SearchCuration(
                        result.Rows, options.FamilyCuration);
                    familyAttribution = FamilyEnvelopeQuant.AttributeHits(familyHits, options.FamilyCuration);
                    manifest.FamilyMapping = FamilyEnvelopeQuant.ProvenanceFor(options.FamilyCuration,
                        familyAttribution.IdentityThreshold);
                    result.Notes.Add("Spectrum-table family attribution: every matching User family at " +
                        familyAttribution.IdentityThreshold.ToString("P0", CultureInfo.InvariantCulture) +
                        " homology identity; shared peptides retain all memberships.");
                }
                catch (Exception ex)
                {
                    result.Notes.Add("WARNING: family attribution for the spectrum table did not run (" +
                        ex.GetType().Name + ": " + ex.Message + "). Quantitation completed; the table " +
                        "marks family mapping as unavailable and the project curation remains intact.");
                }
            }
            if (familyAttribution != null)
            {
                FamilyPeptideAttribution assigned = FamilyEnvelopeQuant.AttributePeptides(
                    FamilyEnvelopeQuant.PreparePeptides(result.Rows), familyAttribution);
                FamilyCompositionStudy.StoreRunShares(manifest, assigned);
                if (familyHits != null && options.FamilyCuration != null)
                {
                    FamilyCompositionStudy.StoreSubclassesFromCuration(manifest, assigned, familyHits, options.FamilyCuration);
                    FamilyCompositionStudy.StorePositionsFromCuration(manifest, assigned, familyHits, options.FamilyCuration);
                }
                if (options.FamilyCuration != null)
                {
                    try
                    {
                        FamilyCompositionStudy.StoreProteinCounts(manifest, assigned, options.FamilyCuration.MyExtendedFastaItems);
                    }
                    catch (ArgumentException ex)
                    {
                        manifest.FamilyProteinCounts = null;
                        result.Notes.Add("Protein explanation counts unavailable: " + ex.Message);
                    }
                }
            }
            result.SpectrumComparisonRows = SpectrumComparisonTable.Build(
                deNovoImport.AllSourceRows.Where(r => !string.Equals(r.SourceId,
                    DeNovoSource.DeepNovo.Id, StringComparison.OrdinalIgnoreCase)),
                deNovoImport.AllSourceRows.Where(r => string.Equals(r.SourceId,
                    DeNovoSource.DeepNovo.Id, StringComparison.OrdinalIgnoreCase)),
                psms, result.Rows, familyAttribution: familyAttribution,
                rawPrecursors: rawPrecursorMetadata,
                perRunTic: manifest.PerRunTic,
                acceptedDeNovoRows: deNovoImport.AcceptedSourceRows);
            manifest.SpectrumComparisonRows = result.SpectrumComparisonRows;
            manifest.DeNovoAcceptedScoreScatter = DeNovoAcceptedScoreScatterStudy.Build(
                result.SpectrumComparisonRows, manifest.DeNovoSourceScoreDistributions);
            FamilyWorldAnalysis.AttachToCompute(options.FamilyCuration, result);
            if (options.ExportSpectrumComparisonCsv)
            {
                string tablePath = string.IsNullOrWhiteSpace(options.SpectrumComparisonCsvPath)
                    ? new FullXicStoragePaths(rawDirectory, options.CacheDirectory).SpectrumComparisonCsvPath
                    : options.SpectrumComparisonCsvPath!;
                result.TryExportSpectrumComparisonCsvOutsideRaw(tablePath, rawDirectory);
            }

            // Q6: fold the detected peptide-like estimate into the manifest's derived section (measured
            // area, derived fraction), so the .fqproj carries the middle level of the decomposition beside
            // the assigned fractions it sits above.
            NativeQuantFractions.ApplyDetectedToManifest(manifest, result.Detected);

            // The identified-but-not-quantified block, named in the status line even when it is zero: a
            // reader has to be able to tell "nothing was excluded" from "exclusions were not reported".
            foreach (DerivedRunFraction d in manifest.DerivedFractions)
            {
                if (d.Grouped) continue;
                double under = d.IdentifiedUnderSampled + d.DeNovoUnderSampled;
                result.Notes.Add(d.Run + " identified but not quantified (minimum " +
                    options.MinPointsForQuant.ToString(CultureInfo.InvariantCulture) +
                    " point(s) per peak): " +
                    (d.IdentifiedUnderSampledPeptides + d.DeNovoUnderSampledPeptides)
                        .ToString("N0", CultureInfo.InvariantCulture) + " peptide(s), area " +
                    under.ToString("E4", CultureInfo.InvariantCulture) +
                    (d.Tic > 0
                        ? " (" + d.UnderSampledFraction.ToString("P4", CultureInfo.InvariantCulture) + " of TIC)"
                        : string.Empty) +
                    (d.UnderSampledIsEstimate ? " - includes an ESTIMATE for single-point clusters" : string.Empty));
            }

            result.TicShareSummary = FullXicComputer.DescribeTicShares(manifest.DerivedFractions);

            manifest.UpdatedUtc = DateTime.UtcNow;
            return result;
        }
    }
}
