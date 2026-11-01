using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// The confidence criteria a de novo row must clear before anything downstream sees it.
    ///
    /// Measured on the Bothrops jararaca exports that motivated this feature: of 181,643 raw de novo
    /// only rows across three runs, these defaults keep 35,686 (19.6%), which collapse to 21,939
    /// distinct (normalised sequence, charge) pairs. An earlier census quoted 33,119 because its
    /// scan-keyed dictionary silently collapsed the spectra carrying more than one de novo candidate;
    /// re-measure both numbers rather than copying them. Loosening the criteria is a user decision,
    /// not a code default: a de novo sequence with a weak stretch is exactly the kind of input that
    /// turns the homology step into a random-match generator.
    /// </summary>
    public class DeNovoFilterCriteria
    {
        /// <summary>Minimum peptide length, in residues.</summary>
        public int MinLength { get; set; } = 7;

        /// <summary>Minimum PEAKS ALC for the legacy/default PEAKS workflow, 0-100. A multi-source
        /// computation supplies each source's own cutoff separately; DeepNovo SCORE is never interpreted
        /// as ALC merely because it occupies this historical property in an older call path.</summary>
        public double MinAverageLocalConfidence { get; set; } = 80;

        /// <summary>The requested stretch criterion: at least this many CONSECUTIVE residues at or
        /// above <see cref="StretchFloor"/>. Five residues over 90 was the wording of the request.</summary>
        public int StretchLength { get; set; } = 5;

        public int StretchFloor { get; set; } = 90;
    }

    public static class DeNovoFilter
    {
        /// <summary>True when the row clears every criterion. Rows whose confidence array is empty
        /// (unparseable in the export) are rejected: absence of evidence is not confidence.</summary>
        public static bool Passes(PeaksDeNovoRow row, DeNovoFilterCriteria c)
            => Passes(row, c, c.MinAverageLocalConfidence);

        /// <summary>Applies the shared length/local-stretch gates and a cutoff on THIS ROW'S OWN source
        /// confidence field. The caller supplies the cutoff chosen for that source.</summary>
        public static bool Passes(PeaksDeNovoRow row, DeNovoFilterCriteria c, double minimumConfidence)
        {
            if (row.Length < c.MinLength) return false;
            if (row.ConfidenceValue < minimumConfidence) return false;
            return HasStretch(row.LocalConfidence, c.StretchLength, c.StretchFloor);
        }

        public static List<PeaksDeNovoRow> Apply(IEnumerable<PeaksDeNovoRow> rows, DeNovoFilterCriteria c)
            => rows.Where(r => Passes(r, c)).ToList();

        public static List<PeaksDeNovoRow> Apply(IEnumerable<PeaksDeNovoRow> rows,
                                                  DeNovoFilterCriteria c,
                                                  double minimumConfidence)
            => rows.Where(r => Passes(r, c, minimumConfidence)).ToList();

        /// <summary>At least <paramref name="need"/> consecutive values at or above
        /// <paramref name="floor"/>.</summary>
        public static bool HasStretch(int[] confidence, int need, int floor)
        {
            if (need <= 0) return true;

            int run = 0;
            foreach (int v in confidence)
            {
                run = v >= floor ? run + 1 : 0;
                if (run >= need) return true;
            }
            return false;
        }
    }
}
