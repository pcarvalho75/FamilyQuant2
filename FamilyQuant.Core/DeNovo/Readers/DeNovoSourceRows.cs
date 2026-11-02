using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>One source's sequence claim for an extracted precursor. A union keeps all of these while
    /// selecting exactly one representative sequence for the one XIC that precursor owns.</summary>
    public sealed class DeNovoEvidence
    {
        public string SourceId { get; set; } = string.Empty;
        public string SourceDisplayName { get; set; } = string.Empty;
        public string ConfidenceField { get; set; } = string.Empty;
        public double ConfidenceValue { get; set; }
        public string Sequence { get; set; } = string.Empty;
        public string InputFilePath { get; set; } = string.Empty;
        public string InputFileSha256 { get; set; } = string.Empty;
    }

    /// <summary>
    /// One row of a PEAKS "de novo only" export: a spectrum PEAKS sequenced that its own database
    /// search could not identify.
    /// </summary>
    public class PeaksDeNovoRow
    {
        private double confidenceValue;

        public int Scan { get; set; }
        public string Peptide { get; set; } = string.Empty;
        /// <summary>The source's confidence value. The historical property name remains for source
        /// compatibility, but callers displaying it must use <see cref="ConfidenceField"/>: the value is
        /// PEAKS ALC for PEAKS and DeepNovo SCORE for DeepNovo, and those scales are not interchangeable.</summary>
        public double Alc { get => confidenceValue; set => confidenceValue = value; }

        public double ConfidenceValue { get => confidenceValue; set => confidenceValue = value; }

        public string ConfidenceField { get; set; } = "ALC (%)";

        public string SourceId { get; set; } = DeNovoSource.DenovoOnly.Id;

        public string SourceDisplayName { get; set; } = DeNovoSource.DenovoOnly.DisplayName;

        /// <summary>Canonical source path and content fingerprint. Both are persisted downstream; the
        /// fingerprint is the identity, while the path tells a human where the bytes were read from.</summary>
        public string InputFilePath { get; set; } = string.Empty;

        public string InputFileSha256 { get; set; } = string.Empty;

        /// <summary>Every source claim on this precursor after deterministic union resolution, including
        /// the selected row itself. Empty on legacy/in-memory rows that never passed through the reader.</summary>
        public List<DeNovoEvidence> SourceEvidence { get; set; } = new List<DeNovoEvidence>();
        public int Length { get; set; }
        public double Mz { get; set; }
        public int Charge { get; set; }
        public double RetentionTimeMin { get; set; }

        /// <summary>The neutral monoisotopic peptide mass PEAKS reports in the "Mass" column, in Da.
        /// Optional: 0 when the source has no "Mass" column. Read only, never used to drive an
        /// extraction; a study that plots mass takes it from here rather than recomputing it.</summary>
        public double Mass { get; set; }

        /// <summary>PEAKS' own feature area. Kept for cross-checks, never used as the reported XIC:
        /// the reported number must come from the same extraction machinery as the PatternLab side,
        /// or the two series of the final plot would not be comparable.</summary>
        public double PeaksArea { get; set; }

        public double Ppm { get; set; }
        public string Ptm { get; set; } = string.Empty;

        /// <summary>Per-residue local confidence, 0-100, one entry per residue of <see cref="Peptide"/>
        /// (net of PTM annotations).</summary>
        public int[] LocalConfidence { get; set; } = Array.Empty<int>();

        /// <summary>The PEAKS feature this spectrum was assigned to, or -1. Links into the features
        /// export, which carries the RT begin/end used to bound XIC extraction.</summary>
        public long FeatureId { get; set; } = -1;

        /// <summary>The run this row belongs to, as the RAW file name without extension. PEAKS exports
        /// one folder per run; the reader stamps this from the file it read.</summary>
        public string RunName { get; set; } = string.Empty;
    }

}
