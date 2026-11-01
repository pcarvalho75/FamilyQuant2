using Newtonsoft.Json;
using PatternTools.MSParserLight;
using PatternTools.XIC;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace FamilyQuant.DeNovo
{
    /// <summary>
    /// One precursor to extract: the MS2 scan that anchors the elution, its measured m/z and its
    /// charge. Everything the extraction needs, and nothing it does not.
    ///
    /// The anchor scan matters more here than the m/z. <see cref="XICGet5.GetXIC4"/> does not scan
    /// the whole run for the best peak: it finds the MS1 cluster present AT this scan and walks
    /// outwards from it. Hand it a scan from a different run, or from a different numbering space,
    /// and it silently returns a different peak rather than failing.
    /// </summary>
    public class DeNovoXicRequest
    {
        /// <summary>The MS2 scan number that identified (or, for de novo, sequenced) this precursor.
        /// Must belong to the same RAW file the extraction runs against.</summary>
        public int ScanNoMS2 { get; set; }

        /// <summary>Measured precursor m/z, as reported by the search engine. Rounded to 5 decimals
        /// before it reaches the extractor - see <see cref="DeNovoXicExtractor"/>.</summary>
        public double PrecursorMz { get; set; }

        /// <summary>Precursor charge. Zero or negative is rejected rather than guessed: the isotope
        /// step is 1.00335483/z and a zero charge would make it infinite.</summary>
        public int Charge { get; set; }

        /// <summary>Free-form label the caller uses to find its own row again - the peptide sequence,
        /// a PEAKS feature id, a row index. Never read by the extraction, copied to the result.</summary>
        public string Tag { get; set; } = string.Empty;
    }

    /// <summary>
    /// One extracted precursor. The areas are RAW: <see cref="DeNovoXicExtractor.NoCluster"/>
    /// (-1) survives into them untouched, because -1 and 0 mean different things and collapsing them
    /// here would destroy that distinction for every downstream reader.
    /// </summary>
    public class DeNovoXicResult
    {
        /// <summary>The RAW file this came from, without path or extension. Present because the run
        /// order in a .xic2 is PERMUTED relative to the file names in this very project, so a result
        /// that does not carry its own provenance is a defect waiting to happen.</summary>
        public string RunName { get; set; } = string.Empty;

        public int ScanNoMS2 { get; set; }

        /// <summary>The m/z actually handed to GetXIC4: the request's m/z after Math.Round(mz, 5).</summary>
        public double PrecursorMz { get; set; }

        public int Charge { get; set; }

        public string Tag { get; set; } = string.Empty;

        /// <summary>
        /// The credited isotope areas, index 0 = M+0, 1 = M+1, ..., i = M+i, at m/z
        /// mz + i*(Carbon13Spacing/Charge). Length equals the configured
        /// <see cref="DeNovoXicOptions.IsotopeCount"/> for an extracted row (default 3), so at the
        /// default this holds exactly [M+0, M+1, M+2] and every existing number is unchanged.
        ///
        /// THIS IS THE SINGLE SOURCE OF TRUTH for the envelope. <see cref="AreaM0"/>,
        /// <see cref="AreaM1"/> and <see cref="AreaM2"/> are thin accessors over indices 0..2 of this
        /// array, kept so that every existing reader and every pinned number carries on working
        /// untouched; the M-1 control is separate, in <see cref="AreaMMinus1"/>, because it is a
        /// control and not a channel.
        ///
        /// NOTE: how many isotopes to credit (a fixed N, an averagine-derived count, an
        /// intensity-threshold cut-off) is a scientific decision deferred to a later Q-phase. Q1 only
        /// makes N a parameter; it does not choose a policy for it.
        /// </summary>
        public double[] IsotopeAreas { get; set; } = Array.Empty<double>();

        /// <summary>Monoisotopic area, i.e. <see cref="IsotopeAreas"/>[0]. This is the number that
        /// must reproduce the stored quant bit for bit; if it does not, the machinery is not the same
        /// one and nothing else here is comparable. An accessor, not a field, so it can never drift
        /// out of step with the array it names.</summary>
        [JsonIgnore]
        public double AreaM0 { get => GetIsotope(0); set => SetIsotope(0, value); }

        /// <summary>M+1 area, i.e. <see cref="IsotopeAreas"/>[1] when the configured isotope count
        /// credits it. Returns 0 when it was not credited (IsotopeCount &lt; 2), the same value the
        /// field defaulted to before this became an accessor.</summary>
        [JsonIgnore]
        public double AreaM1 { get => GetIsotope(1); set => SetIsotope(1, value); }

        /// <summary>M+2 area, i.e. <see cref="IsotopeAreas"/>[2] when the configured isotope count
        /// credits it. Returns 0 when it was not credited (IsotopeCount &lt; 3).</summary>
        [JsonIgnore]
        public double AreaM2 { get => GetIsotope(2); set => SetIsotope(2, value); }

        private double GetIsotope(int i) =>
            IsotopeAreas != null && i < IsotopeAreas.Length ? IsotopeAreas[i] : 0.0;

        // Grows the backing array on demand so an object initializer that sets AreaM0, then AreaM1,
        // then AreaM2 (the shape the tests and Rejected use) fills [0], [1], [2] in turn without the
        // caller having to size the array first. New slots default to 0.0, exactly the value the old
        // auto-properties held before they were set.
        private void SetIsotope(int i, double value)
        {
            if (IsotopeAreas == null || IsotopeAreas.Length <= i)
            {
                var grown = new double[i + 1];
                if (IsotopeAreas != null) Array.Copy(IsotopeAreas, grown, IsotopeAreas.Length);
                IsotopeAreas = grown;
            }
            IsotopeAreas[i] = value;
        }

        /// <summary>
        /// How many points the cluster behind each credited isotope carried, index for index with
        /// <see cref="IsotopeAreas"/>. Zero means no cluster spanned the anchor at that m/z.
        ///
        /// THE POINT COUNT IS THE ROWS OF THE ARRAY THE AREA RULE SEES, not the count of real detections.
        /// PatternTools' cluster builder (XICGet5.GetClusterCandidates2) pads every cluster with one
        /// zero-intensity row immediately before its first scan and one immediately after its last, so a
        /// cluster built from a single real detection arrives here with three rows. Measured on run _01:
        /// 52,617 of 52,617 clusters carried exactly two zero rows, and the shortest array was 3.
        /// </summary>
        public int[] IsotopePointCounts { get; set; } = Array.Empty<int>();

        /// <summary>
        /// The ion current of this precursor's credited isotopes that a cluster WAS found for but that the
        /// minimum-points rule refused to quantify, in the same intensity-times-minutes units as every
        /// other area here. Zero when nothing was refused.
        ///
        /// It is carried rather than discarded so an excluded cluster cannot migrate silently into the
        /// unassigned remainder: see <see cref="DeNovoXicExtractor.DefaultMinPointsForQuant"/> and
        /// <see cref="UnderSampledIsEstimate"/>.
        /// </summary>
        public double UnderSampledArea { get; set; }

        /// <summary>How many credited isotopes were refused by the minimum-points rule.</summary>
        public int UnderSampledIsotopes { get; set; }

        /// <summary>True when any part of <see cref="UnderSampledArea"/> is an ESTIMATE rather than an
        /// integral: a cluster of exactly one point has no width, so its area is estimated as intensity
        /// times the run's median MS1 scan spacing in minutes. Everything with two or more points already
        /// has a real trapezoid and is used as it stands.</summary>
        public bool UnderSampledIsEstimate { get; set; }

        /// <summary>The M-1 window: one carbon-13 step BELOW the monoisotopic peak, where no
        /// isotopologue of this peptide can exist. Whatever area appears here is interference, and it
        /// is the floor on how much of M+1 and M+2 can be believed.
        ///
        /// For a de novo precursor this control is MORE load-bearing than for a PSM, not less: a PSM
        /// had a database sequence to agree with, so its monoisotopic assignment was corroborated. A
        /// de novo assignment was not, so "this window has signal too" is real evidence that the
        /// monoisotopic peak was picked one step too high.</summary>
        public double AreaMMinus1 { get; set; }

        /// <summary>The sum of the credited isotopes (every entry of <see cref="IsotopeAreas"/>) with
        /// the -1 sentinel clamped to 0, because a missing isotope contributes nothing rather than
        /// subtracting one count. Computed, never stored, so it can never drift out of step with its
        /// parts. At the default isotope count of 3 this is M+0 + M+1 + M+2, bit for bit as before.
        /// M-1 is deliberately NOT in this sum: it is the control, not a channel.</summary>
        [JsonIgnore]
        public double EnvelopeSum
        {
            get
            {
                if (IsotopeAreas == null) return 0.0;
                double sum = 0.0;
                for (int i = 0; i < IsotopeAreas.Length; i++) sum += Math.Max(0, IsotopeAreas[i]);
                return sum;
            }
        }

        /// <summary>Null on the normal path. Set when the extraction of this one precursor threw, or
        /// when the request was rejected before extraction. The areas are then all
        /// <see cref="DeNovoXicExtractor.NoCluster"/>.
        ///
        /// The failure is recorded rather than thrown because one bad precursor out of eleven thousand
        /// must not abandon a thirty-second RAW load - but it is recorded rather than swallowed,
        /// because a run where every row quietly returned -1 would otherwise look like a run with no
        /// signal.</summary>
        public string? Failure { get; set; }
    }

    /// <summary>
    /// Knobs that legitimately vary between projects. Everything NOT here is a constant on
    /// <see cref="DeNovoXicExtractor"/> on purpose: the spectrum-loading arguments are the values
    /// that produced the stored numbers, and a knob is an invitation to change one.
    /// </summary>
    public class DeNovoXicOptions
    {
        /// <summary>Extraction window, in ppm. MUST equal the ppm the stored quants were extracted
        /// with - PatternLab records it in the .xic2 under XICClusteringPPMMS1Tol (Item1). 10 is the
        /// value for the Bothrops jararaca project this module was built for.</summary>
        public double Ppm { get; set; } = DeNovoXicExtractor.ProductionPpm;

        /// <summary>MS1 clustering tolerance handed to the XICGet5 constructor; .xic2 item 2.</summary>
        public int Ms1Tolerance { get; set; } = DeNovoXicExtractor.ProductionMs1Tolerance;

        /// <summary>
        /// How many isotopes to credit into <see cref="DeNovoXicResult.IsotopeAreas"/>, N &gt;= 1:
        /// index 0 = M+0, 1 = M+1, ..., N-1 = M+(N-1), each at mz + i*(Carbon13Spacing/Charge).
        ///
        /// DEFAULTS TO 3 ON PURPOSE. At 3 the array is [M+0, M+1, M+2] and every de novo number this
        /// module ever produced is byte-identical, so making N configurable disturbs nothing. The M-1
        /// interference control is extracted regardless of N and is never one of the credited
        /// isotopes.
        ///
        /// This is the PARAMETER, not a policy. Whether the right N is a fixed number, an
        /// averagine-derived count or an intensity-threshold cut-off is a scientific decision left to
        /// a later Q-phase.
        /// </summary>
        public int IsotopeCount { get; set; } = DeNovoXicExtractor.DefaultIsotopeCount;

        /// <summary>
        /// The MINIMUM POINTS PER PEAK a cluster must carry before it is quantified at all, N &gt;= 1.
        /// Enforced per CLUSTER, which is to say per ISOTOPE independently, so a single-point M+1 cannot
        /// inject a raw intensity into an envelope whose M+0 is a proper integral.
        ///
        /// DEFAULTS TO <see cref="DeNovoXicExtractor.DefaultMinPointsForQuant"/> (3), the production
        /// setting. A cluster below the threshold is not quantified: it contributes zero to the totals and
        /// its area is recorded separately as under-sampled (see
        /// <see cref="DeNovoXicResult.UnderSampledArea"/>), so it can never migrate silently into the
        /// unassigned remainder.
        ///
        /// SET IT TO <see cref="DeNovoXicExtractor.LegacyMinPointsForQuant"/> (1) TO REPRODUCE THE LEGACY
        /// XQUANT2 RULE BIT FOR BIT. That is what the parity gate runs at, and it is the proof that the
        /// extraction is faithful - not the production setting.
        /// </summary>
        public int MinPointsForQuant { get; set; } = DeNovoXicExtractor.DefaultMinPointsForQuant;

        /// <summary>Path of the JSON sidecar to read and write, or null for no cache. A re-run then
        /// skips the RAW entirely when every requested precursor is already known.</summary>
        public string? CachePath { get; set; }

        /// <summary>SHA-256 of the RAW content. Full-project extraction computes it once per run and
        /// shares it between identified, de novo, and detector caches.</summary>
        public string? SourceFingerprint { get; set; }

        /// <summary>Where progress lines go. Null means silent. The extractor never writes to the
        /// console itself: it is library code and may be running behind a WPF window.</summary>
        public Action<string>? Progress { get; set; }
    }

    /// <summary>
    /// What the area rule made of ONE cluster: the area that counts, how many points the cluster carried,
    /// and - when the minimum-points rule refused it - the ion current that was refused.
    ///
    /// The three states are told apart by <see cref="IsQuantified"/>, <see cref="IsUnderSampled"/> and
    /// <see cref="NoCluster"/>, never by comparing <see cref="Area"/> against a magic number: the two
    /// sentinels are both negative and both below <see cref="DeNovoXicExtractor.NoCluster"/>, so a reader
    /// that tested only for "negative" would conflate two different facts.
    /// </summary>
    public readonly struct ClusterArea
    {
        private ClusterArea(double area, int pointCount, bool quantified,
                            double underSampledArea, bool underSampledIsEstimate)
        {
            Area = area;
            PointCount = pointCount;
            IsQuantified = quantified;
            UnderSampledArea = underSampledArea;
            UnderSampledIsEstimate = underSampledIsEstimate;
        }

        /// <summary>The value the extraction writes into its isotope slot:
        /// <see cref="DeNovoXicExtractor.NoCluster"/> (-1) when nothing was anchored,
        /// <see cref="DeNovoXicExtractor.UnderSampled"/> (-2) when a cluster was refused, otherwise the
        /// integrated area.</summary>
        public double Area { get; }

        /// <summary>Rows of the array the area rule saw (real detections plus the two zero pads
        /// PatternTools adds). Zero when no cluster was anchored.</summary>
        public int PointCount { get; }

        /// <summary>True when the cluster passed the minimum-points rule and <see cref="Area"/> is a real
        /// area.</summary>
        public bool IsQuantified { get; }

        /// <summary>The refused cluster's ion current, in intensity-times-minutes, or 0 when nothing was
        /// refused. Never folded into <see cref="Area"/>: it is deliberately not part of the quantified
        /// total, and it is deliberately not lost either.</summary>
        public double UnderSampledArea { get; }

        /// <summary>True when <see cref="UnderSampledArea"/> is the single-point ESTIMATE (intensity times
        /// the run's MS1 scan spacing) rather than a trapezoid.</summary>
        public bool UnderSampledIsEstimate { get; }

        /// <summary>No cluster spanned the anchor at this m/z.</summary>
        public bool NoCluster => PointCount == 0;

        /// <summary>A cluster was found and the minimum-points rule refused to quantify it.</summary>
        public bool IsUnderSampled => PointCount > 0 && !IsQuantified;

        public static ClusterArea NoClusterFound() =>
            new ClusterArea(DeNovoXicExtractor.NoCluster, 0, false, 0.0, false);

        public static ClusterArea Quantified(double area, int pointCount) =>
            new ClusterArea(area, pointCount, true, 0.0, false);

        public static ClusterArea UnderSampledCluster(double area, int pointCount, bool isEstimate) =>
            new ClusterArea(DeNovoXicExtractor.UnderSampled, pointCount, false, area, isEstimate);
    }

    /// <summary>
    /// Extracts XIC areas straight from a Thermo RAW for precursors that have no PSM.
    ///
    /// WHY THIS EXISTS. A peptide that only PEAKS de novo sequenced was never identified, so it has
    /// no entry anywhere in the .plp/.xic2 chain and no stored area. Plotting it next to the
    /// PatternLab series requires extracting it - and requires extracting it with EXACTLY the
    /// machinery that produced the stored series, or the two are different measurements sharing an
    /// axis.
    ///
    /// "Exactly" is proven, not assumed. FamilyQuant.Tests\DeNovoXicTests re-extracts stored PSMs
    /// through this class and requires bit-for-bit equality with their stored areas before any de
    /// novo precursor is put through it.
    ///
    /// THE THREE THINGS THAT MAKE IT THE SAME MACHINERY:
    ///
    /// 1. The spectra are loaded through ParserUltraLightRawFlash.Parse with the production
    ///    arguments spelled out (see <see cref="LoadMs1Spectra"/>), NOT through the XICGet5 string
    ///    constructor. That constructor reaches Console.CursorTop and throws
    ///    IOException("The handle is invalid") whenever stdout is not a real console window; its
    ///    toPrintConsole parameter is dead and does not prevent it.
    /// 2. The area rule is the one from XQuant2's Quant constructor, reproduced here because
    ///    XQuant2.dll is no longer referenced (it was 19 months stale and unused). See
    ///    <see cref="AreaFor"/>. The production path adds a MINIMUM-POINTS-PER-PEAK gate on top of it
    ///    (<see cref="Classify"/>, <see cref="DeNovoXicOptions.MinPointsForQuant"/>); setting that to
    ///    <see cref="LegacyMinPointsForQuant"/> switches the gate off and restores this rule exactly,
    ///    which is the mode the parity gate runs in.
    /// 3. m/z is rounded to 5 decimals before every GetXIC4 call, including the isotope offsets,
    ///    which are computed from the ALREADY-ROUNDED monoisotopic m/z and then rounded again.
    /// </summary>
    public static class DeNovoXicExtractor
    {
        /// <summary>Returned as an area when GetXIC4 found no cluster spanning the anchor scan.
        ///
        /// IT MEANS "NO CLUSTER SPANNING THE ANCHOR", NOT "NO SIGNAL". There may be abundant signal
        /// at this m/z elsewhere in the run; what is absent is a cluster that GetXIC4 could walk out
        /// from at THIS scan. Summing it as if it were zero is a decision the caller makes
        /// deliberately (<see cref="DeNovoXicResult.EnvelopeSum"/> does exactly that, and says so);
        /// averaging it as if it were an intensity is a defect.</summary>
        public const double NoCluster = -1.0;

        /// <summary>Returned as an area when a cluster WAS found but carried fewer points than
        /// <see cref="DeNovoXicOptions.MinPointsForQuant"/> asks for.
        ///
        /// IT IS A DIFFERENT FACT FROM <see cref="NoCluster"/> AND CARRIES A DIFFERENT SENTINEL. -1 means
        /// the extractor could not anchor a cluster here at all; -2 means it did, and the quantitation rule
        /// declined to integrate it. Both are below <see cref="NoCluster"/>, so every existing
        /// "area &lt;= NoCluster" guard keeps treating this as "no quantified signal", which is exactly the
        /// intended arithmetic: an under-sampled cluster contributes ZERO to the quantified totals. What it
        /// does contribute is recorded apart, in <see cref="DeNovoXicResult.UnderSampledArea"/>, so the
        /// decomposition still accounts for it.</summary>
        public const double UnderSampled = -2.0;

        /// <summary>C13 - C12, the spacing between isotopologues of a singly charged ion. Divided by
        /// the charge to get the m/z step.</summary>
        public const double Carbon13Spacing = 1.00335483;

        /// <summary>The minimum-points setting that reproduces the LEGACY XQuant2 area rule exactly: one
        /// point is enough, and a one-point cluster is credited its raw intensity. This is the value the
        /// bit-for-bit parity gate runs at, because it is the rule the stored quants were produced by. It
        /// is a proof setting, not a production one.</summary>
        public const int LegacyMinPointsForQuant = 1;

        /// <summary>
        /// The production minimum points per peak (owner decision): three. Below it a cluster is not
        /// quantified.
        ///
        /// WHY THREE, AND WHY IT COSTS NOTHING HERE. The point count is the ROWS OF THE ARRAY the area
        /// rule sees, and PatternTools pads every cluster with one zero-intensity row before its first
        /// scan and one after its last, so the shortest array a real cluster can produce is 3 (one real
        /// detection plus its two pads). Measured on run _01, 52,617 of 52,617 clusters carried exactly
        /// two zero rows and none was shorter than 3, so at this setting nothing on that acquisition is
        /// excluded. The rule is a GUARD on the one branch that is not an integral - a single-row array,
        /// which the legacy rule credits as a raw intensity, in intensity units rather than in
        /// intensity-times-minutes, inflated by 1/spacing (about 195x at run _01's 0.005134 min median
        /// MS1 spacing). That branch never fires on this data; the guard makes sure it cannot fire
        /// unnoticed on another.
        /// </summary>
        public const int DefaultMinPointsForQuant = 3;

        /// <summary>The default number of isotopes credited into
        /// <see cref="DeNovoXicResult.IsotopeAreas"/>: M+0, M+1, M+2. At this value every de novo
        /// number is byte-identical to the pre-generalization machinery.</summary>
        public const int DefaultIsotopeCount = 3;

        /// <summary>
        /// The isotope count of the go-forward full-envelope quantitation (owner decision): up to M+5.
        ///
        /// IT IS A CEILING, NOT A FLOOR, AND THE CREDITING STAYS EMPIRICAL. Each isotope is extracted at
        /// its own m/z computed from the charge; an isotope that is not there returns the no-cluster
        /// sentinel and contributes 0 through the clamp in <see cref="DeNovoXicResult.EnvelopeSum"/>.
        /// There is no averagine policy and none is wanted: the program credits the peaks it can detect,
        /// and the isotopes-credited histogram reports how many were really found. Raising the ceiling
        /// therefore only adds signal where a real isotope exists, and a noise spike cannot qualify
        /// because it will not form a cluster of <see cref="DefaultMinPointsForQuant"/> points at the
        /// isotope spacing.
        ///
        /// The de novo default stays <see cref="DefaultIsotopeCount"/> (3) so its recorded numbers do not
        /// move; the identified extraction and the envelope basis use this.
        /// </summary>
        public const int DefaultEnvelopeIsotopeCount = 6;

        /// <summary>Decimals every m/z is rounded to before extraction. Not cosmetic: the stored
        /// quants were extracted from m/z rounded this way, so extracting from an unrounded m/z
        /// selects a marginally different window and can return a different area.</summary>
        public const int MzDecimals = 5;

        /// <summary>The extraction window used for the venom project; the .xic2 is the authority for
        /// any other project.</summary>
        public const double ProductionPpm = 10.0;

        /// <summary>MS1 clustering tolerance used for the venom project.</summary>
        public const int ProductionMs1Tolerance = 1;

        // ---- the spectrum-loading arguments -----------------------------------------------------
        // These are NOT options. They are the production defaults, and each one changes which peaks
        // survive into the MS1 list that XICGet5 then integrates. Changing any of them silently
        // changes every area this class returns, which would break comparability with the stored
        // numbers without breaking anything visible.

        /// <summary>Peak cap per spectrum. The parser keeps the most intense 900 and discards the
        /// rest.</summary>
        public const int MaximumNumberOfPeaks = 900;

        /// <summary>Intensity floor per spectrum, as a percentage of that spectrum's base peak. The
        /// 9-argument Parse overload forwards exactly this value, so it is the production default
        /// whether a caller names it or not.</summary>
        public const double RelativeThresholdPercent = 0.01;

        /// <summary>
        /// Loads the MS1 spectra of one RAW headlessly, with the production parse arguments.
        ///
        /// EVERY ARGUMENT IS NAMED ON PURPOSE. The parameters are (string, short, short, bool,
        /// List&lt;int&gt;, bool, int, bool, bool, double) - five of them booleans in a row - so a
        /// positional call is unreadable and a positional MISTAKE is invisible. This overload also
        /// has no defaults at all (the defaulted 9-argument overload is the one WITHOUT
        /// relativeThresholdPercent), so all ten must be supplied.
        ///
        /// printConsole:false is the load-bearing one, and it does NOT mean "be quiet". Both branches
        /// write the same progress text; what the false branch skips is the cursor arithmetic. The
        /// true branch calls Console.CursorTop and Console.SetCursorPosition
        /// (ParserUltraLightRawFlash.cs:331-334), which throw when stdout is a pipe, a file, or a WPF
        /// process with no console at all - and the exception looks exactly like a corrupt RAW.
        ///
        /// So the parser still prints, from inside a library, and this class cannot stop it: the
        /// "Parsing file: ..." line (ParserUltraLightRawFlash.cs:76) is not guarded by the flag at
        /// all. A caller that must not emit anything has to redirect Console.Out around this call.
        /// That is a property of the vendored binary, not a defect here, and it is written down so
        /// that nobody "fixes" it by flipping printConsole back to true - which would trade harmless
        /// noise for an exception in every non-console host, the WPF app included.
        ///
        /// Costs 30-60 s and holds the whole run in memory (a 3.2 GB RAW is several GB as an MS1
        /// list), which is why the batch API below loads once per run and why runs are processed one
        /// at a time.
        /// </summary>
        public static List<MSUltraLight> LoadMs1Spectra(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                throw new ArgumentException("A RAW file path is required.", nameof(rawPath));

            var file = new FileInfo(rawPath);
            if (!file.Exists)
                throw new FileNotFoundException("The RAW file was not found: " + rawPath, rawPath);

            // A OneDrive placeholder has a name, a size and no bytes. Thermo's reader fails on it in
            // a way that reads as file corruption, so it is worth naming here.
            if ((file.Attributes & FileAttributes.Offline) == FileAttributes.Offline)
                throw new IOException(
                    "The RAW file is an offline placeholder (OneDrive has not downloaded it): " + rawPath);

            // printConsole:false only skips the Console.CursorTop/SetCursorPosition calls that throw
            // without a console; both branches of the parser still WRITE progress, and its "Parsing
            // file:" line is not guarded at all. Swallow the chatter for the duration of the parse so
            // a WPF host stays silent and the test suite's output stays legible. Console.Out is
            // process-global state: this is safe here because nothing legitimate writes to the
            // console concurrently with a RAW load, and the writer is restored in finally either way.
            TextWriter original = Console.Out;
            try
            {
                Console.SetOut(TextWriter.Null);

                return ParserUltraLightRawFlash.Parse(
                    rawPath,
                    MsnLevel: 1,
                    fileIndex: -1,
                    saveScanHeader: false,
                    scanNumbers: null,
                    printConsole: false,
                    maximumNumberOfPeaks: MaximumNumberOfPeaks,
                    loadSpectralPeaks: true,
                    forceCentroid: false,
                    relativeThresholdPercent: RelativeThresholdPercent);
            }
            finally
            {
                Console.SetOut(original);
            }
        }

        /// <summary>
        /// The m/z of isotope <paramref name="isotopeIndex"/> of a precursor whose monoisotopic m/z is
        /// <paramref name="roundedMz"/> (already rounded to <see cref="MzDecimals"/>) at charge
        /// <paramref name="charge"/>: mz + isotopeIndex*(Carbon13Spacing/charge), rounded again.
        ///
        /// Index 0 is the monoisotopic peak, 1..N-1 the credited isotopes, and -1 the interference
        /// control below it. Rounding the offset from the ALREADY-ROUNDED monoisotopic m/z, then
        /// rounding again, is what produced the stored numbers; Math.Round(mz + 0*step, 5) is mz to the
        /// bit, so index 0 returns the monoisotopic m/z unchanged.
        /// </summary>
        public static double IsotopeMz(double roundedMz, int charge, int isotopeIndex)
        {
            if (charge <= 0)
                throw new ArgumentException(
                    "Charge must be positive; " + charge.ToString(CultureInfo.InvariantCulture) +
                    " would make the isotope step infinite.", nameof(charge));

            double step = Carbon13Spacing / charge;
            return Math.Round(roundedMz + isotopeIndex * step, MzDecimals);
        }

        /// <summary>
        /// The area rule, reproduced from XQuant2's Quant constructor (Quant.cs:82-102) because
        /// XQuant2.dll is no longer referenced. Ten lines, and every one of them is load-bearing:
        ///
        ///   null signal        -> <see cref="NoCluster"/>, meaning no cluster spanned the anchor.
        ///   exactly one point  -> that point's INTENSITY, column 0. Not column 1, which is the scan
        ///                         number under the [intensity, scanNo, retentionTime] row layout.
        ///                         A single point has no width, so there is no area to integrate.
        ///   two or more points -> trapezoid over retention time (column 2) against intensity
        ///                         (column 0), rounded to 1 decimal. The rounding is part of the
        ///                         stored value, not a display choice.
        /// </summary>
        public static double AreaFor(double[][]? signal)
        {
            if (ReferenceEquals(signal, null)) return NoCluster;
            if (signal.Length == 1) return signal[0][0];
            if (signal.Length > 1) return Math.Round(PatternTools.pTools.TrapezoidalIntegration(signal, 2, 0), 1);
            return NoCluster;
        }

        /// <summary>
        /// The area rule WITH the minimum-points-per-peak gate, applied to ONE cluster - which is to say
        /// to one isotope, independently of every other isotope of the same precursor.
        ///
        /// THE FOUR OUTCOMES:
        ///
        ///   no cluster                  -> <see cref="ClusterArea.NoClusterFound"/>, area
        ///                                  <see cref="NoCluster"/>, point count 0.
        ///   points &gt;= minPoints      -> quantified with EXACTLY the legacy rule
        ///                                  (<see cref="AreaFor"/>), bit for bit.
        ///   points &lt; minPoints, &gt;= 2 -> under-sampled. It already has a valid trapezoid over
        ///                                  retention time, so that trapezoid is what is recorded.
        ///   exactly 1 point             -> under-sampled, and there is NO width to integrate. Its area is
        ///                                  ESTIMATED as intensity times
        ///                                  <paramref name="scanSpacingMinutes"/>, which puts it in the
        ///                                  same intensity-times-minutes units as every other area rather
        ///                                  than in raw intensity. The estimate is flagged as one.
        ///
        /// At <paramref name="minPointsForQuant"/> = <see cref="LegacyMinPointsForQuant"/> the second
        /// branch is the only reachable one for any non-empty cluster, so the result is today's number
        /// exactly, including the raw intensity of a one-point cluster.
        /// </summary>
        /// <param name="signal">The [intensity, scanNo, retentionTime] rows GetXIC4 returned, or null.</param>
        /// <param name="minPointsForQuant">Minimum rows before the cluster is quantified at all.</param>
        /// <param name="scanSpacingMinutes">The run's MS1 scan spacing in minutes, used only to give a
        /// one-point cluster a width. Zero (unknown) yields a zero estimate rather than an invented one.</param>
        public static ClusterArea Classify(double[][]? signal, int minPointsForQuant, double scanSpacingMinutes)
        {
            if (minPointsForQuant < 1)
                throw new ArgumentOutOfRangeException(nameof(minPointsForQuant),
                    "MinPointsForQuant must be at least 1; " +
                    minPointsForQuant.ToString(CultureInfo.InvariantCulture) + " was requested.");

            if (ReferenceEquals(signal, null) || signal.Length == 0) return ClusterArea.NoClusterFound();

            int points = signal.Length;

            if (points >= minPointsForQuant) return ClusterArea.Quantified(AreaFor(signal), points);

            // Two or more points already integrate; one point has no width and is estimated. Both are
            // recorded, never dropped: an excluded cluster that vanished would reappear as unassigned
            // remainder and the decomposition would stop meaning what it says.
            if (points == 1)
            {
                double estimate = scanSpacingMinutes > 0 ? signal[0][0] * scanSpacingMinutes : 0.0;
                return ClusterArea.UnderSampledCluster(estimate, 1, isEstimate: true);
            }

            return ClusterArea.UnderSampledCluster(
                Math.Round(PatternTools.pTools.TrapezoidalIntegration(signal, 2, 0), 1), points, isEstimate: false);
        }

        /// <summary>
        /// The median gap, in minutes, between consecutive MS1 spectra of one loaded run. The width a
        /// one-point cluster is given by <see cref="Classify"/> when it has none of its own.
        ///
        /// THE MEDIAN AND NOT THE MEAN, because a top-N duty cycle over a long gradient makes the
        /// distribution skewed: run _01 measures a 0.005134 min median against a 0.006799 min mean over
        /// 41,035 MS1 spectra. Fewer than two spectra, or no positive gap, yields 0, which
        /// <see cref="Classify"/> reads as "unknown" rather than as "instantaneous".
        /// </summary>
        public static double MedianMs1ScanSpacingMinutes(IReadOnlyList<MSUltraLight>? spectra)
        {
            if (spectra == null || spectra.Count < 2) return 0.0;

            var gaps = new List<double>(spectra.Count - 1);
            for (int i = 1; i < spectra.Count; i++)
            {
                double gap = spectra[i].CromatographyRetentionTime - spectra[i - 1].CromatographyRetentionTime;
                if (gap > 0) gaps.Add(gap);
            }

            if (gaps.Count == 0) return 0.0;

            gaps.Sort();
            int middle = gaps.Count / 2;
            return gaps.Count % 2 == 1 ? gaps[middle] : (gaps[middle - 1] + gaps[middle]) / 2.0;
        }

        /// <summary>
        /// One window: extract at <paramref name="mz"/> anchored at <paramref name="ms2Scan"/>, then
        /// apply the legacy area rule. Returns <see cref="NoCluster"/> when the extraction throws.
        ///
        /// The catch is deliberate and is part of the machinery being reproduced: a precursor whose
        /// window falls outside the acquired mass range, or whose anchor scan has no MS1 neighbour,
        /// must yield the same -1 that a null signal yields rather than abandoning the batch. The
        /// exception type is handed to <paramref name="onFailure"/> so a run that fails wholesale
        /// cannot masquerade as a run with no signal.
        /// </summary>
        public static double AreaAt(XICGet5 xic, double mz, double ppm, int ms2Scan, Action<string>? onFailure = null)
            => AreaAt(xic, mz, ppm, ms2Scan, LegacyMinPointsForQuant, 0.0, out _, onFailure);

        /// <summary>
        /// The same window, with the minimum-points-per-peak gate applied to THIS cluster alone. This is
        /// the per-isotope seam: every credited isotope goes through its own call, so a precursor whose
        /// M+0 qualifies and whose M+3 does not credits only what qualifies.
        /// </summary>
        public static double AreaAt(XICGet5 xic, double mz, double ppm, int ms2Scan,
            int minPointsForQuant, double scanSpacingMinutes, out ClusterArea cluster,
            Action<string>? onFailure = null)
        {
            cluster = ClusterAt(xic, mz, ppm, ms2Scan, minPointsForQuant, scanSpacingMinutes, onFailure);
            return cluster.Area;
        }

        /// <summary>The same window, returning the whole verdict rather than only the area. An extraction
        /// that throws yields <see cref="ClusterArea.NoClusterFound"/>, for the reason spelled out on
        /// <see cref="AreaAt(XICGet5, double, double, int, Action{string})"/>.</summary>
        public static ClusterArea ClusterAt(XICGet5 xic, double mz, double ppm, int ms2Scan,
            int minPointsForQuant, double scanSpacingMinutes, Action<string>? onFailure = null)
        {
            try
            {
                return Classify(xic.GetXIC4(mz, ppm, ms2Scan), minPointsForQuant, scanSpacingMinutes);
            }
            catch (Exception ex)
            {
                onFailure?.Invoke(ex.GetType().Name + ": " + ex.Message);
                return ClusterArea.NoClusterFound();
            }
        }

        /// <summary>
        /// Writes one precursor's credited isotopes into <paramref name="row"/>, applying the
        /// minimum-points verdict of each isotope INDEPENDENTLY: what qualifies is credited, what does not
        /// contributes zero to the envelope and has its ion current accumulated into
        /// <see cref="DeNovoXicResult.UnderSampledArea"/> instead.
        ///
        /// THIS IS THE CRUX OF THE RULE AND IT IS A METHOD SO IT CAN BE DRIVEN WITHOUT A RAW. If the rule
        /// were applied once per PEPTIDE rather than once per isotope, a single-point M+1 would still
        /// inject a raw intensity into an envelope whose M+0 is a proper integral, and the unit
        /// inconsistency would survive in exactly the number the paper reports.
        /// </summary>
        /// <param name="clusterFor">Isotope index (0 = M+0) to that isotope's verdict.</param>
        /// <param name="isotopeCount">How many isotopes to credit.</param>
        /// <param name="row">The row to fill; its previous isotope arrays are replaced.</param>
        public static void CreditIsotopes(Func<int, ClusterArea> clusterFor, int isotopeCount, DeNovoXicResult row)
        {
            if (clusterFor == null) throw new ArgumentNullException(nameof(clusterFor));
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (isotopeCount < 1)
                throw new ArgumentOutOfRangeException(nameof(isotopeCount),
                    "At least the monoisotopic peak must be credited; " +
                    isotopeCount.ToString(CultureInfo.InvariantCulture) + " was requested.");

            var isotopeAreas = new double[isotopeCount];
            var isotopePoints = new int[isotopeCount];
            double underSampledArea = 0.0;
            int underSampledIsotopes = 0;
            bool underSampledIsEstimate = false;

            for (int k = 0; k < isotopeCount; k++)
            {
                ClusterArea cluster = clusterFor(k);
                isotopeAreas[k] = cluster.Area;
                isotopePoints[k] = cluster.PointCount;

                if (cluster.IsUnderSampled)
                {
                    underSampledArea += cluster.UnderSampledArea;
                    underSampledIsotopes++;
                    underSampledIsEstimate |= cluster.UnderSampledIsEstimate;
                }
            }

            row.IsotopeAreas = isotopeAreas;
            row.IsotopePointCounts = isotopePoints;
            row.UnderSampledArea = underSampledArea;
            row.UnderSampledIsotopes = underSampledIsotopes;
            row.UnderSampledIsEstimate = underSampledIsEstimate;
        }

        /// <summary>
        /// Extracts every requested precursor from one RAW, loading its MS1 spectra exactly once.
        ///
        /// ONE RUN AT A TIME, ON PURPOSE. The MS1 list of a 3.2 GB RAW dominates this process's
        /// memory; holding two would be the difference between a batch that completes and one that
        /// dies at eighty percent. Callers with three runs call this three times.
        ///
        /// Results come back in REQUEST ORDER, one per request, including the rejected and the
        /// failed. Callers correlate by index or by <see cref="DeNovoXicRequest.Tag"/>; neither
        /// requires the caller to re-derive the key.
        ///
        /// The RAW is never opened when the cache already answers every request.
        /// </summary>
        public static List<DeNovoXicResult> ExtractRun(
            string rawPath,
            IEnumerable<DeNovoXicRequest> requests,
            DeNovoXicOptions? options = null)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));

            DeNovoXicOptions opt = options ?? new DeNovoXicOptions();
            if (opt.IsotopeCount < 1)
                throw new ArgumentException(
                    "IsotopeCount must be at least 1 (M+0); " +
                    opt.IsotopeCount.ToString(CultureInfo.InvariantCulture) + " was requested.",
                    nameof(options));

            if (opt.MinPointsForQuant < 1)
                throw new ArgumentException(
                    "MinPointsForQuant must be at least 1; " +
                    opt.MinPointsForQuant.ToString(CultureInfo.InvariantCulture) + " was requested.",
                    nameof(options));

            int isotopeCount = opt.IsotopeCount;
            int minPoints = opt.MinPointsForQuant;
            string runName = Path.GetFileNameWithoutExtension(rawPath) ?? string.Empty;
            string sourceFingerprint = string.IsNullOrWhiteSpace(opt.SourceFingerprint)
                ? FileFingerprint.Sha256(rawPath) : opt.SourceFingerprint;

            var pending = new List<DeNovoXicRequest>(requests);
            var results = new List<DeNovoXicResult>(pending.Count);

            // IsotopeCount is stamped into the cache alongside ppm and MS1 tolerance: areas extracted
            // for three isotopes are a different measurement from areas extracted for five, and a
            // cache that served one for the other would return a row whose IsotopeAreas is the wrong
            // length. A mismatch discards the whole file rather than mixing the two. MinPointsForQuant is
            // stamped for the same reason: the same cluster is quantified at 1 and refused at 3, so the
            // two are different measurements sharing a key.
            DeNovoXicCache? cache = opt.CachePath == null
                ? null
                : DeNovoXicCache.Load(opt.CachePath, runName, opt.Ppm, opt.Ms1Tolerance, isotopeCount,
                    minPoints, sourceFingerprint);

            // Pass one: reject what cannot be extracted, answer what the cache already knows, and
            // note what is left. Nothing here touches the RAW.
            var toExtract = new List<int>();
            for (int i = 0; i < pending.Count; i++)
            {
                DeNovoXicRequest r = pending[i];
                double mz = Math.Round(r.PrecursorMz, MzDecimals);

                if (r.Charge <= 0)
                {
                    results.Add(Rejected(runName, r, mz, isotopeCount,
                        "charge " + r.Charge.ToString(CultureInfo.InvariantCulture) +
                        " is not a usable charge; the isotope step would be infinite"));
                    continue;
                }

                if (cache != null && cache.TryGet(r.ScanNoMS2, mz, r.Charge, out DeNovoXicResult? hit) && hit != null)
                {
                    hit.Tag = r.Tag;   // the tag belongs to this request, not to the cached extraction
                    results.Add(hit);
                    continue;
                }

                results.Add(new DeNovoXicResult
                {
                    RunName = runName,
                    ScanNoMS2 = r.ScanNoMS2,
                    PrecursorMz = mz,
                    Charge = r.Charge,
                    Tag = r.Tag,
                });
                toExtract.Add(i);
            }

            if (toExtract.Count == 0)
            {
                opt.Progress?.Invoke(runName + ": every precursor answered from the cache; the RAW was not opened.");
                return results;
            }

            opt.Progress?.Invoke(runName + ": loading MS1 spectra (" +
                toExtract.Count.ToString("N0", CultureInfo.InvariantCulture) + " of " +
                pending.Count.ToString("N0", CultureInfo.InvariantCulture) + " precursors to extract)...");

            List<MSUltraLight> spectra = LoadMs1Spectra(rawPath);
            opt.Progress?.Invoke(runName + ": " + spectra.Count.ToString("N0", CultureInfo.InvariantCulture) +
                " MS1 spectra loaded.");

            var xic = new XICGet5(spectra, opt.Ms1Tolerance);

            // The width a one-point cluster is given, computed ONCE per run from the spectra just loaded.
            // A one-point cluster has no neighbours of its own to take a local spacing from, so the run
            // median is the only honest width available; it is only ever used for the under-sampled
            // ESTIMATE and never for a quantified area.
            double scanSpacing = MedianMs1ScanSpacingMinutes(spectra);
            opt.Progress?.Invoke(runName + ": median MS1 scan spacing " +
                scanSpacing.ToString("F6", CultureInfo.InvariantCulture) + " min; minimum points per peak " +
                minPoints.ToString(CultureInfo.InvariantCulture) + ".");

            foreach (int i in toExtract)
            {
                DeNovoXicResult row = results[i];
                string? failure = null;
                Action<string> note = m => failure ??= m;

                double mz = row.PrecursorMz;   // already rounded in pass one

                // Index i sits at mz + i*step, rounded from the already-rounded monoisotopic m/z, which
                // is what produced the stored numbers. IsotopeMz(mz, z, 0) is mz to the bit, so i = 0
                // reproduces the old M+0 exactly, and i = 1, 2 reproduce the old M+1, M+2 exactly at the
                // default count.
                //
                // THE MINIMUM-POINTS RULE IS APPLIED PER ISOTOPE, inside CreditIsotopes: each k is a
                // separate cluster with its own verdict.
                CreditIsotopes(
                    k => ClusterAt(xic, IsotopeMz(mz, row.Charge, k), opt.Ppm, row.ScanNoMS2,
                                   minPoints, scanSpacing, note),
                    isotopeCount, row);

                // The M-1 control is one step BELOW the monoisotopic peak and is never a credited
                // isotope; it is extracted whatever the isotope count, and on the LEGACY rule on purpose:
                // it is the interference floor, and zeroing a thin interference cluster would make the
                // control quieter than the data it is meant to police.
                row.AreaMMinus1 = AreaAt(xic, IsotopeMz(mz, row.Charge, -1), opt.Ppm, row.ScanNoMS2, note);
                row.Failure = failure;

                cache?.Put(row);
            }

            if (cache != null)
            {
                cache.Save();
                opt.Progress?.Invoke(runName + ": cache written to " + cache.Path);
            }

            return results;
        }

        /// <summary>
        /// D2, the run's total ion current: the same chromatogram and the same trapezoid that
        /// PatternLab's own TIC divisor uses, so a de novo area normalised by it is normalised by the
        /// same denominator as everything else.
        ///
        /// THE TRIMS ARE RELATIVE TO THE RUN'S OWN START AND END, NOT ABSOLUTE MINUTES. Inside the
        /// parser they become startTime += RunHeaderEx.StartTime and endTime += RunHeaderEx.EndTime
        /// (ParserUltraLightRawFlash.cs:429-430), so:
        ///
        ///   (0, 0)    the whole run - the trim proved correct for the venom project
        ///   (5, 0)    drop the first five minutes
        ///   (0, -10)  drop the last ten minutes
        ///
        /// A POSITIVE end trim therefore asks for time AFTER the run ends and yields an empty
        /// chromatogram, whose area is 0. That zero is not an error and nothing downstream can tell
        /// it from a genuinely empty run, which is why both parameters are REQUIRED here and have no
        /// default: a hidden default is a divisor nobody chose.
        /// </summary>
        public static double GetTotalIonCurrentArea(string rawPath, double chromStartTrimMinutes, double chromEndTrimMinutes)
        {
            (double RetentionTime, double Intensity, int ScanNumber)[] chromatogram =
                MSFileResolver.GetChromatogram(rawPath, true, chromStartTrimMinutes, chromEndTrimMinutes);

            return MSFileResolver.GetChromatogramArea(chromatogram);
        }

        private static DeNovoXicResult Rejected(string runName, DeNovoXicRequest r, double mz, int isotopeCount, string why)
        {
            // Every credited isotope is the no-cluster sentinel, so EnvelopeSum clamps to 0 and the
            // row reads as "no signal" rather than as a small negative area.
            var isotopeAreas = new double[isotopeCount];
            for (int i = 0; i < isotopeAreas.Length; i++) isotopeAreas[i] = NoCluster;

            return new DeNovoXicResult
            {
                RunName = runName,
                ScanNoMS2 = r.ScanNoMS2,
                PrecursorMz = mz,
                Charge = r.Charge,
                Tag = r.Tag,
                IsotopeAreas = isotopeAreas,
                // Nothing was extracted, so no cluster carried any point and nothing was under-sampled:
                // a rejected request is not an under-sampled measurement, it is no measurement at all.
                IsotopePointCounts = new int[isotopeCount],
                AreaMMinus1 = NoCluster,
                Failure = why,
            };
        }
    }
}
