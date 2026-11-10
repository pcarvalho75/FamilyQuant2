using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace FamilyQuant
{
    /// <summary>
    /// One run's (or the grouped) DERIVED fractions of the total ion current, stored in the manifest so
    /// the paper numbers persist beside the raw per-peptide quant that produced them. Every number here
    /// is DERIVED (see <see cref="QuantManifest.Provenance"/>): it is computed from the measured
    /// per-peptide areas and the measured TIC by the per-peptide-dedup method
    /// (<see cref="DeNovo.NativeQuantFractions"/>), never measured independently. The raw quant rows
    /// remain the source of truth; deleting this section loses nothing that cannot be recomputed.
    /// </summary>
    public sealed class DerivedRunFraction
    {
        /// <summary>The normalized run name, or the grouped-row name when <see cref="Grouped"/>.</summary>
        public string Run { get; set; } = string.Empty;

        /// <summary>True only for the aggregate over all runs.</summary>
        public bool Grouped { get; set; }

        /// <summary>The denominator: this run's total ion current (the sum over runs when grouped).</summary>
        public double Tic { get; set; }

        public double IdentifiedAreaM0 { get; set; }
        public double IdentifiedEnvelope { get; set; }
        public double DeNovoAreaM0 { get; set; }
        public double DeNovoEnvelope { get; set; }
        public double DeNovoEnvelopeReferenceCalibrated { get; set; }

        /// <summary>Distinct (sequence, charge) peptides on each side after the dedup collapse.</summary>
        public int IdentifiedPeptides { get; set; }
        public int DeNovoPeptides { get; set; }

        /// <summary>Identified M+0 over the TIC: the traditional monoisotopic fraction, ~11.9% on run
        /// _01, coincident with the .xic2 stored-quant number.</summary>
        public double IdentifiedFractionM0 { get; set; }

        /// <summary>Identified 6-isotope envelope over the TIC: the go-forward fraction, larger than
        /// M+0 by the ion current the higher isotopes carry.</summary>
        public double IdentifiedFractionEnvelope { get; set; }

        /// <summary>De novo 6-isotope envelope over the TIC.</summary>
        public double DeNovoFractionEnvelope { get; set; }
        public double DeNovoFractionReferenceCalibrated { get; set; }

        /// <summary>
        /// The IDENTIFIED-BUT-NOT-QUANTIFIED ion current on the identified side: peptides whose clusters
        /// were found but carried fewer than <see cref="QuantManifest.MinPointsForQuant"/> points, summed
        /// on the same per-peptide grain and in the same intensity-times-minutes units as the quantified
        /// envelope. Zero when nothing was excluded.
        /// </summary>
        public double IdentifiedUnderSampled { get; set; }

        /// <summary>The same quantity on the de novo side.</summary>
        public double DeNovoUnderSampled { get; set; }

        /// <summary>How many deduped peptides of each side carried under-sampled ion current.</summary>
        public int IdentifiedUnderSampledPeptides { get; set; }

        public int DeNovoUnderSampledPeptides { get; set; }

        /// <summary>(identified + de novo) under-sampled area over TIC.</summary>
        public double UnderSampledFraction { get; set; }

        /// <summary>True when any part of the under-sampled numbers is the single-point ESTIMATE rather
        /// than a trapezoid. It travels with them so a reader of the manifest cannot mistake an estimate
        /// for a measurement.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>What DDA and de novo together recover: (identified + de novo) envelope over the TIC,
        /// below 1 by construction because no elution peak is counted twice.</summary>
        public double RecoveredFractionEnvelope { get; set; }

        /// <summary>Primary estimate: identified observed envelope plus de novo M+0 scaled by the
        /// identified envelope/M+0 relationship in fixed precursor-mass strata, over TIC.</summary>
        public double RecoveredFractionReferenceCalibrated { get; set; }

        public double RecoveredFractionM0 { get; set; }
        public int DeNovoSuppressedCrossSideOverlaps { get; set; }

        /// <summary>Q6: the resolved isotope-series diagnostic area measured from the MS1 map by
        /// <see cref="DeNovo.Ms1EnvelopeDetector"/>. It has no peptide-level FDR calibration. Zero on a
        /// store written before Q6, or where detection did not run.</summary>
        public double DetectedPeptideLikeArea { get; set; }

        /// <summary>Q6: the resolved isotope-series diagnostic area over TIC.</summary>
        public double DetectedFractionPeptideLike { get; set; }

        /// <summary>Q6: how many resolved isotope-series RT features the diagnostic integrated.</summary>
        public int DetectedEnvelopeCount { get; set; }

        /// <summary>
        /// Q6, split by charge: the MULTIPLY CHARGED part of <see cref="DetectedPeptideLikeArea"/>
        /// (z &gt;= 2), measured. More specific than z=1, but not peptide-FDR calibrated. Zero on a store before the
        /// split existed, and then every surface falls back to the undivided detected level.
        /// </summary>
        public double DetectedAreaMultiplyCharged { get; set; }

        /// <summary>
        /// Q6, split by charge: the SINGLY CHARGED part of <see cref="DetectedPeptideLikeArea"/>
        /// (z == 1), MEASURED. WEAKER evidence, since at charge 1 a C13 isotope is true of essentially any
        /// organic molecule. NOT a non-peptide block: venom peptides are frequently singly charged and
        /// this dataset carries identified charge-1 peptides.
        /// </summary>
        public double DetectedAreaSinglyCharged { get; set; }

        /// <summary>Q6: the multiply charged part over the TIC. Derived.</summary>
        public double DetectedFractionMultiplyCharged { get; set; }

        /// <summary>Q6: the singly charged part over the TIC. Derived.</summary>
        public double DetectedFractionSinglyCharged { get; set; }

        public int DetectedCountMultiplyCharged { get; set; }
        public int DetectedCountSinglyCharged { get; set; }

        /// <summary>Q6: the detected area per charge, indexed BY CHARGE (index 0 unused), MEASURED. Null
        /// or empty on a store written before the split existed.</summary>
        public double[] DetectedAreaByCharge { get; set; } = Array.Empty<double>();

        /// <summary>Q6: the detected feature count per charge, indexed like
        /// <see cref="DetectedAreaByCharge"/>.</summary>
        public int[] DetectedCountByCharge { get; set; } = Array.Empty<int>();

        /// <summary>Optional detector areas in fixed neutral-mass ranges, recorded by U6 Quantify.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double[]? DetectedAreaByNeutralMass { get; set; }
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? NeutralMassBinWidth { get; set; }

        /// <summary>How every number in this row was obtained, machine-readable.</summary>
        public string Provenance { get; set; } = string.Empty;
    }
}
