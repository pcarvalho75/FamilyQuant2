using System.Text.Json;
using System.Text.Json.Serialization;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// A JSON sidecar holding one run's extracted areas, so that re-opening the project does not pay
    /// the 30-60 s RAW load again.
    ///
    /// WHAT MAKES IT SAFE RATHER THAN MERELY FAST. A cache of numbers that were expensive to compute
    /// is only useful if a stale entry is impossible, not unlikely. Three things are stamped into the
    /// file and re-checked on load - the run name, the ppm and the MS1 tolerance - and any
    /// disagreement discards the whole file rather than mixing extractions:
    ///
    /// * RUN NAME, because the run order inside a .xic2 is PERMUTED relative to the RAW file names in
    ///   this very project (package 1 is run _02). A sidecar that landed next to the wrong RAW would
    ///   otherwise return another run's areas, with every key matching and nothing looking wrong.
    /// * PPM and MS1 TOLERANCE, because they select the window. Areas extracted at 20 ppm and areas
    ///   extracted at 10 ppm are different measurements, and a cache that served one for the other
    ///   would break the comparability this whole module exists to guarantee.
    /// * ISOTOPE COUNT, because it sets how many entries each row's IsotopeAreas carries. A cache
    ///   written for three isotopes would hand a five-isotope run a row of the wrong length, and the
    ///   envelope sum would silently miss the isotopes that were never stored.
    /// * MINIMUM POINTS PER PEAK, because it decides whether a cluster is quantified at all. The same
    ///   cluster is an area at 1 and a refusal at 3, so a cache written under one rule holds a different
    ///   measurement from the one a run under the other rule is asking for.
    ///
    /// A corrupt or unreadable sidecar is treated as an empty one. It holds nothing that cannot be
    /// recomputed, so failing the run over it would trade a recoverable cost for an unrecoverable one.
    /// </summary>
    public sealed class DeNovoXicCache
    {
        /// <summary>Bumped whenever the meaning of a stored field changes. An older or newer number
        /// discards the file: reading a v1 area as a v2 area is exactly the silent mixing this class
        /// exists to prevent. v2 moved the monoisotopic and isotope areas into the IsotopeAreas array
        /// and added the isotope-count stamp, so a v1 file (flat AreaM0/AreaM1/AreaM2) is discarded
        /// and recomputed rather than read with the areas dropped. v3 added the minimum-points-per-peak
        /// stamp and the per-isotope point counts, so a v2 file - which cannot say which rule produced
        /// its areas, nor how many points each cluster carried - is discarded for the same reason.</summary>
        public const int CurrentVersion = 4;

        private readonly Dictionary<string, DeNovoXicResult> _entries;
        private bool _dirty;

        private DeNovoXicCache(string path, string runName, double ppm, int ms1Tolerance, int isotopeCount,
                               int minPointsForQuant, string sourceFingerprint,
                               Dictionary<string, DeNovoXicResult> entries)
        {
            Path = path;
            RunName = runName;
            Ppm = ppm;
            Ms1Tolerance = ms1Tolerance;
            IsotopeCount = isotopeCount;
            MinPointsForQuant = minPointsForQuant;
            SourceFingerprint = sourceFingerprint;
            _entries = entries;
        }

        public string Path { get; }

        public string RunName { get; }

        public double Ppm { get; }

        public int Ms1Tolerance { get; }

        /// <summary>The isotope count the stored areas were extracted at; a load for a different count
        /// discards the file. See <see cref="DeNovoXicOptions.IsotopeCount"/>.</summary>
        public int IsotopeCount { get; }

        /// <summary>The minimum points per peak the stored areas were quantified under; a load for a
        /// different rule discards the file. See <see cref="DeNovoXicOptions.MinPointsForQuant"/>.</summary>
        public int MinPointsForQuant { get; }

        public string SourceFingerprint { get; }

        public int Count => _entries.Count;

        /// <summary>True when <see cref="Put"/> added something that <see cref="Save"/> has not
        /// written yet. Save is a no-op otherwise, so a fully cached run never rewrites the file.</summary>
        public bool IsDirty => _dirty;

        /// <summary>
        /// The key: anchor scan, m/z to five decimals, charge. The same three values that select the
        /// extraction window, so two requests share a key exactly when they would produce the same
        /// number.
        ///
        /// The m/z is formatted with F5 under InvariantCulture. Both halves matter - "1028.51038" and
        /// "1028,51038" are different keys, and on a comma-decimal machine the current culture would
        /// produce the second while the file on disk holds the first.
        /// </summary>
        public static string KeyFor(int scanNoMS2, double roundedMz, int charge) =>
            scanNoMS2.ToString(CultureInfo.InvariantCulture) + "|" +
            roundedMz.ToString("F5", CultureInfo.InvariantCulture) + "|" +
            charge.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// Reads the sidecar, or returns an empty cache when it is absent, unreadable, or was written
        /// for a different run or a different window. Never throws for a bad file.
        ///
        /// <paramref name="isotopeCount"/> defaults to <see cref="DeNovoXicExtractor.DefaultIsotopeCount"/>,
        /// so a caller that never touched the isotope count reads and writes the same three-isotope
        /// caches it always did.
        /// </summary>
        public static DeNovoXicCache Load(string path, string runName, double ppm, int ms1Tolerance,
            int isotopeCount = DeNovoXicExtractor.DefaultIsotopeCount,
            int minPointsForQuant = DeNovoXicExtractor.DefaultMinPointsForQuant,
            string? sourceFingerprint = null, string? fallbackPath = null)
        {
            DeNovoXicCache current = LoadSingle(path, runName, ppm, ms1Tolerance, isotopeCount, minPointsForQuant, sourceFingerprint);
            if (fallbackPath != null && !string.Equals(path, fallbackPath, StringComparison.OrdinalIgnoreCase))
            {
                DeNovoXicCache legacy = LoadSingle(fallbackPath, runName, ppm, ms1Tolerance, isotopeCount, minPointsForQuant, sourceFingerprint);
                foreach (var entry in legacy._entries) current._entries.TryAdd(entry.Key, entry.Value);
            }
            // Legacy entries are read only. If extraction adds anything, Save targets the modern
            // path; a fully cached run can keep reading the legacy file without rewriting either.
            return current;
        }

        private static DeNovoXicCache LoadSingle(string path, string runName, double ppm, int ms1Tolerance,
            int isotopeCount, int minPointsForQuant, string? sourceFingerprint)
        {
            string fingerprint = sourceFingerprint ?? string.Empty;
            var empty = new DeNovoXicCache(path, runName, ppm, ms1Tolerance, isotopeCount, minPointsForQuant, fingerprint,
                new Dictionary<string, DeNovoXicResult>(StringComparer.Ordinal));

            try
            {
                if (!File.Exists(path)) return empty;

                CacheFile? file = JsonSerializer.Deserialize<CacheFile>(File.ReadAllText(path), ExtractionCacheJson.Options);
                if (file == null) return empty;

                if (file.Version != CurrentVersion) return empty;
                if (!string.Equals(file.RunName, runName, StringComparison.OrdinalIgnoreCase)) return empty;
                if (file.Ppm != ppm) return empty;
                if (file.Ms1Tolerance != ms1Tolerance) return empty;
                if (file.IsotopeCount != isotopeCount) return empty;
                if (file.MinPointsForQuant != minPointsForQuant) return empty;
                if (!string.Equals(file.SourceFingerprint ?? string.Empty, fingerprint, StringComparison.Ordinal)) return empty;
                if (file.Entries == null) return empty;

                return new DeNovoXicCache(path, runName, ppm, ms1Tolerance, isotopeCount, minPointsForQuant, fingerprint,
                    new Dictionary<string, DeNovoXicResult>(file.Entries, StringComparer.Ordinal));
            }
            catch (Exception)
            {
                // A cache is a convenience. It holds nothing that cannot be recomputed from the RAW,
                // so an unreadable one costs a re-extraction and nothing else.
                return empty;
            }
        }

        /// <summary>Looks up one precursor. <paramref name="roundedMz"/> must already be rounded to 5
        /// decimals, exactly as the extractor rounds it, or the key will not match.</summary>
        public bool TryGet(int scanNoMS2, double roundedMz, int charge, out DeNovoXicResult? result)
            => _entries.TryGetValue(KeyFor(scanNoMS2, roundedMz, charge), out result);

        /// <summary>Stores one extracted precursor. The <see cref="DeNovoXicResult.Tag"/> is written
        /// as it stands but is never trusted on the way back out: it belongs to the request, not to
        /// the extraction, and the same precursor may carry a different label next time.</summary>
        public void Put(DeNovoXicResult result)
        {
            if (result == null) throw new ArgumentNullException(nameof(result));

            _entries[KeyFor(result.ScanNoMS2, result.PrecursorMz, result.Charge)] = result;
            _dirty = true;
        }

        /// <summary>
        /// Writes the sidecar, via a temporary file and a move, so that a process killed mid-write
        /// leaves the previous cache intact rather than a truncated one.
        ///
        /// Doubles round-trip exactly: System.Text.Json writes them in shortest round-trippable form, so an
        /// area read back from this file is the same bits that came out of the trapezoid. A cache
        /// that returned a value one ulp away would break the bit-for-bit reproduction that licenses
        /// this module in the first place.
        /// </summary>
        public void Save()
        {
            if (!_dirty) return;

            string? folder = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);

            var file = new CacheFile
            {
                Version = CurrentVersion,
                RunName = RunName,
                Ppm = Ppm,
                Ms1Tolerance = Ms1Tolerance,
                IsotopeCount = IsotopeCount,
                MinPointsForQuant = MinPointsForQuant,
                SourceFingerprint = SourceFingerprint,
                Entries = _entries,
            };

            string json = JsonSerializer.Serialize(file, ExtractionCacheJson.Options);

            string temp = Path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, Path, overwrite: true);

            _dirty = false;
        }

        /// <summary>The on-disk shape. Separate from the cache so that the stamps sit at the top of
        /// the file, where a human opening it can see which run and which window it belongs to.</summary>
        private sealed class CacheFile
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("runName")]
            public string RunName { get; set; } = string.Empty;

            [JsonPropertyName("ppm")]
            public double Ppm { get; set; }

            [JsonPropertyName("ms1Tolerance")]
            public int Ms1Tolerance { get; set; }

            [JsonPropertyName("isotopeCount")]
            public int IsotopeCount { get; set; }

            [JsonPropertyName("minPointsForQuant")]
            public int MinPointsForQuant { get; set; }

            [JsonPropertyName("sourceSha256")]
            public string SourceFingerprint { get; set; } = string.Empty;

            [JsonPropertyName("entries")]
            public Dictionary<string, DeNovoXicResult>? Entries { get; set; }
        }
    }
}
