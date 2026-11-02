using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>One clean sequence found by BOTH tools: the max ALC PEAKS gave it and the max SCORE
    /// DeepNovo gave it. A dot in the score-vs-score scatter.</summary>
    public sealed class ScoreScatterPoint
    {
        public string CleanSequence { get; set; } = string.Empty;
        public double PeaksAlc { get; set; }
        public double DeepNovoScore { get; set; }
    }

    /// <summary>
    /// The score-vs-score comparison of the two de novo tools on the clean sequences they SHARE, plus the
    /// confidence distributions of the sequences each tool finds alone. Clean sequence is the shared rule
    /// (modifications stripped, letters only, I folded to L); confidence is collapsed to the max per tool
    /// per sequence. Pure sequence bookkeeping over the CSVs: no XIC, no RAW.
    /// </summary>
    public sealed class DeNovoScoreScatter
    {
        public string PeaksField { get; set; } = "ALC (%)";
        public string DeepNovoField { get; set; } = "SCORE (%)";

        /// <summary>The shared clean sequences, one dot each: PEAKS ALC on X, DeepNovo SCORE on Y.</summary>
        public List<ScoreScatterPoint> Shared { get; set; } = new List<ScoreScatterPoint>();

        /// <summary>PEAKS ALC of each clean sequence PEAKS finds and DeepNovo does not (top marginal).</summary>
        public List<double> PeaksUniqueAlc { get; set; } = new List<double>();

        /// <summary>DeepNovo SCORE of each clean sequence DeepNovo finds and PEAKS does not (right marginal).</summary>
        public List<double> DeepNovoUniqueScore { get; set; } = new List<double>();

        public int SharedCount => Shared.Count;
        public int PeaksUniqueCount => PeaksUniqueAlc.Count;
        public int DeepNovoUniqueCount => DeepNovoUniqueScore.Count;

        /// <summary>The Pearson correlation of PEAKS ALC and DeepNovo SCORE over the shared sequences.</summary>
        public double PearsonR { get; set; }

        public void Validate()
        {
            if (SharedCount < 0 || PeaksUniqueCount < 0 || DeepNovoUniqueCount < 0)
                throw new InvalidOperationException("Score-scatter counts cannot be negative.");
            if (Shared.Any(p => p.CleanSequence.Length == 0))
                throw new InvalidOperationException("A shared point carries an empty clean sequence.");
        }
    }

}
