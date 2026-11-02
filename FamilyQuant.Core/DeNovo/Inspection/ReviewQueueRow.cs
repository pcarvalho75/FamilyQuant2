using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One comparable precursor shaped for reading: overlapping de-novo-only alternatives and the
    /// equal-but-weak-PSM rows retained for review.
    ///
    /// The CSV and the on-screen review queue are the SAME rows. Shaping them here rather than in each
    /// consumer is what stops the two from drifting, which is how a table and its export end up
    /// disagreeing about what is in the queue.
    /// </summary>
    public class ReviewQueueRow
    {
        public string Run { get; set; } = string.Empty;
        public int Scan { get; set; }
        public ReconciliationVerdict Verdict { get; set; }
        public string VerdictLabel => DeNovoReconciliationReport.DisplayVerdict(Verdict);

        public string DeNovoSequence { get; set; } = string.Empty;
        public double Alc { get; set; }

        /// <summary>The weakest residue of the de novo call, 0 when the export carried no trace.</summary>
        public int MinResidueConfidence { get; set; }

        public double DeNovoMz { get; set; }
        public int Charge { get; set; }

        public string PatternLabSequence { get; set; } = string.Empty;
        public double XCorr { get; set; }
        public double PatternLabMz { get; set; }

        /// <summary>How far apart the two precursors are. Within the join tolerance by construction,
        /// so it is a measure of agreement rather than a filter.</summary>
        public double PpmApart { get; set; }
    }

}
