using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>
    /// How the acquisitions of a RAW folder pair with the runs the identifications and the kept de novo
    /// calls name, BY FILE NAME through <see cref="DeNovoReconciler.NormalizeRunName"/>. Every list holds
    /// normalized run names in name order.
    ///
    /// It exists so a folder whose RAWs match nothing is REFUSED rather than computed. A renamed
    /// acquisition ("x_01-003.raw" holding run "x_01") used to finish as a success with zero rows and
    /// zero runs, and that empty result then replaced the project's quantitation. Nothing about the
    /// pairing is relaxed here: the names still have to match exactly, the mismatch is only reported.
    /// </summary>
    public sealed class FullXicRunMatch
    {
        /// <summary>At most this many names are spelled out per list; the rest are counted.</summary>
        public const int MaxNamesListed = 8;

        /// <summary>Runs with a RAW and something to extract: the runs a computation processes.</summary>
        public IReadOnlyList<string> Matched { get; private set; } = Array.Empty<string>();

        public IReadOnlyList<string> RawRuns { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> IdentifiedRuns { get; private set; } = Array.Empty<string>();
        public IReadOnlyList<string> DeNovoRuns { get; private set; } = Array.Empty<string>();

        /// <summary>RAWs that no identification and no kept de novo call names.</summary>
        public IReadOnlyList<string> RawWithoutEvidence { get; private set; } = Array.Empty<string>();

        /// <summary>Runs the identifications name that have no RAW in the folder.</summary>
        public IReadOnlyList<string> IdentifiedWithoutRaw { get; private set; } = Array.Empty<string>();

        /// <summary>Runs the kept de novo calls name that have no RAW in the folder.</summary>
        public IReadOnlyList<string> DeNovoWithoutRaw { get; private set; } = Array.Empty<string>();

        public static FullXicRunMatch Build(IEnumerable<string> rawRuns, IEnumerable<string> identifiedRuns,
                                            IEnumerable<string> deNovoRuns)
        {
            List<string> Distinct(IEnumerable<string> names) => (names ?? Enumerable.Empty<string>())
                .Select(n => n ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<string> raw = Distinct(rawRuns);
            List<string> identified = Distinct(identifiedRuns);
            List<string> deNovo = Distinct(deNovoRuns);

            var rawSet = new HashSet<string>(raw, StringComparer.OrdinalIgnoreCase);
            var evidence = new HashSet<string>(identified.Concat(deNovo), StringComparer.OrdinalIgnoreCase);

            return new FullXicRunMatch
            {
                RawRuns = raw,
                IdentifiedRuns = identified,
                DeNovoRuns = deNovo,
                Matched = raw.Where(evidence.Contains).ToList(),
                RawWithoutEvidence = raw.Where(r => !evidence.Contains(r)).ToList(),
                IdentifiedWithoutRaw = identified.Where(r => !rawSet.Contains(r)).ToList(),
                DeNovoWithoutRaw = deNovo.Where(r => !rawSet.Contains(r)).ToList(),
            };
        }

        /// <summary>
        /// One line per unmatched RAW whose name extends, or is extended by, an unmatched run name, which
        /// is what a copy or download suffix produces ("x_01-003" for run "x_01"). Only a hint for the
        /// person renaming files: the computation never pairs on it.
        /// </summary>
        public IReadOnlyList<string> RenameHints()
        {
            var unmatchedEvidence = IdentifiedWithoutRaw.Concat(DeNovoWithoutRaw)
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var hints = new List<string>();
            foreach (string raw in RawWithoutEvidence)
            {
                string? run = unmatchedEvidence
                    .Where(e => raw.StartsWith(e, StringComparison.OrdinalIgnoreCase)
                             || e.StartsWith(raw, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => e.Length)
                    .ThenBy(e => e, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (run != null)
                    hints.Add("\"" + raw + ".raw\" looks like run \"" + run + "\": rename it to \"" + run + ".raw\".");
            }
            return hints;
        }

        /// <summary>The refusal shown when <see cref="Matched"/> is empty, or when
        /// <paramref name="restrictedTo"/> excluded every matched run.</summary>
        public string RefusalMessage(string rawDirectory, string identificationsName, bool deNovoGiven,
                                     IReadOnlyCollection<string>? restrictedTo = null)
        {
            if (Matched.Count > 0 && restrictedTo != null)
                return "Nothing was computed: none of the matched runs (" + Names(Matched) +
                       ") is among the runs this computation was restricted to (" + Names(restrictedTo) + ").";

            var sb = new System.Text.StringBuilder();
            sb.Append("Nothing was computed, so the project keeps the quantitation it already had: no .raw " +
                      "file in the RAW folder has the name of a run in the identifications" +
                      (deNovoGiven ? " or the kept de novo calls" : string.Empty) +
                      ". Runs are paired by file name (extensions such as .raw, .sqt and .yada are " +
                      "ignored), never by position.\n");
            sb.Append("\n.raw files in " + rawDirectory + ": " + Names(RawRuns));
            sb.Append("\nRuns in " + identificationsName + ": " + Names(IdentifiedRuns));
            if (deNovoGiven) sb.Append("\nRuns with kept de novo calls: " + Names(DeNovoRuns));

            IReadOnlyList<string> hints = RenameHints();
            if (hints.Count > 0) sb.Append("\n\n" + string.Join("\n", hints));

            sb.Append("\n\nNext step: " + (RawRuns.Count == 0
                ? "choose the folder that holds the .raw acquisitions themselves."
                : "give each .raw the name of its run (the name the search and the de novo export used), " +
                  "or choose the folder holding the acquisitions these identifications came from, then " +
                  "press Compute again."));
            return sb.ToString();
        }

        /// <summary>Warnings for a computation that does go ahead but leaves names unpaired, so a run
        /// missing from the result is never silent. Empty when every name pairs.</summary>
        public IReadOnlyList<string> MismatchNotes(string rawDirectory, string identificationsName)
        {
            var notes = new List<string>();
            if (IdentifiedWithoutRaw.Count > 0)
                notes.Add("WARNING: " + IdentifiedWithoutRaw.Count.ToString(CultureInfo.InvariantCulture) +
                    " run(s) in " + identificationsName + " have no .raw of the same name in " + rawDirectory +
                    " and were not quantified: " + Names(IdentifiedWithoutRaw));
            if (DeNovoWithoutRaw.Count > 0)
                notes.Add("WARNING: " + DeNovoWithoutRaw.Count.ToString(CultureInfo.InvariantCulture) +
                    " run(s) with kept de novo calls have no .raw of the same name in " + rawDirectory +
                    " and were not quantified: " + Names(DeNovoWithoutRaw));
            if (RawWithoutEvidence.Count > 0)
                notes.Add("WARNING: " + RawWithoutEvidence.Count.ToString(CultureInfo.InvariantCulture) +
                    " .raw file(s) match no run in the identifications or the kept de novo calls and were " +
                    "skipped: " + Names(RawWithoutEvidence));
            notes.AddRange(RenameHints());
            return notes;
        }

        private static string Names(IEnumerable<string> names)
        {
            List<string> list = names.Select(n => n.Length == 0 ? "(no file name)" : n).ToList();
            if (list.Count == 0) return "none";
            string shown = string.Join(", ", list.Take(MaxNamesListed));
            return list.Count <= MaxNamesListed
                ? shown
                : shown + " and " + (list.Count - MaxNamesListed).ToString(CultureInfo.InvariantCulture) + " more";
        }
    }

    /// <summary>Thrown by <see cref="FullXicComputer.ComputeFullXic"/> when no RAW pairs with anything to
    /// extract. Raised before any acquisition is opened, so nothing is computed and nothing is written; the
    /// message names the RAWs and runs on both sides.</summary>
    public sealed class FullXicNoMatchedRunsException : InvalidOperationException
    {
        public FullXicRunMatch Match { get; }

        public FullXicNoMatchedRunsException(FullXicRunMatch match, string message) : base(message)
        {
            Match = match;
        }
    }
}
