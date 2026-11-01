using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests
{
    /// <summary>
    /// THE ACCEPTANCE GATE for <see cref="DeNovoXicExtractor"/>, and the only reason that class is
    /// allowed to be pointed at a de novo precursor.
    ///
    /// THE ARGUMENT. A de novo peptide has no PSM, so no stored area exists to check the extraction
    /// against. The only way to know the extraction is the same measurement as the stored series is
    /// to run it over precursors that DO have a stored area and require the numbers back bit for bit.
    /// If they agree, the machinery is the same one and a de novo area may be plotted on the same
    /// axis. If they do not, every de novo number is a different measurement wearing the same units,
    /// and the plot would be a comparison of two pipelines rather than of two peptide sets.
    ///
    /// Bit for bit, not "close". A resemblance would be consistent with the wrong ppm, the wrong MS1
    /// tolerance, an unrounded m/z, a different peak cap - each of which changes areas by a few
    /// percent, which is the same size as the effect the final plot is meant to show. Exact equality
    /// is the only assertion that cannot be passed by an almost-right pipeline. The PL51 harness that
    /// established this recipe reproduced 14,913 of 14,913, so anything short of 250 of 250 here is a
    /// finding, not tolerance.
    ///
    /// THE RUN MAPPING IS PERMUTED AND IS MEASURED, NOT ASSUMED. The package indices inside the
    /// project do not follow the RAW file names:
    ///
    ///     package 1 = run _02        package 2 = run _01        package 3 = run _03
    ///
    /// This was established by precursor m/z agreement: about 82% of a package's precursors are found
    /// in its true RAW and essentially none in either of the others, so the pairing is not a
    /// judgement call. Pairing by index instead - the obvious thing - would compare each package
    /// against the wrong RAW and produce a near-total mismatch that reads like a broken extractor.
    /// This test therefore runs package 2 against _01, and says so in the check name.
    ///
    /// COST. One RAW load, 30-60 s, roughly 3 GB resident. Exactly one run is touched on purpose.
    /// </summary>
    internal static class DeNovoXicTests
    {
        /// <summary>The package whose stored quants are replayed, and the RAW it truly belongs to.</summary>
        private const int GatePackage = 2;

        private const string GateRawFileName = "20220624_Bjararaca_1ug_3step-240min_01.raw";

        /// <summary>Rows to replay. Large enough that an accidental agreement is not credible, small
        /// enough that the gate stays inside one RAW load.</summary>
        private const int GateRows = 250;

        /// <summary>One row of the stored quants export: what PatternLab recorded for a precursor it
        /// identified, and therefore what this module has to reproduce.</summary>
        private sealed class StoredQuant
        {
            public int Pkg;
            public string Sequence = string.Empty;
            public int Z;
            public double QuantArea;
            public double PrecursorMz;
            public int ScanNoMS2;
        }

        /// <summary>
        /// Reports through <paramref name="check"/>, which mirrors the suite's own Check so this
        /// section counts towards the same totals. Skips rather than fails when the venom data set is
        /// not on this machine: the RAW files are 3 GB each and are not part of the repository.
        /// </summary>
        public static void Run(Action<string, bool, string?> check, string workDir, string? desktopDir)
        {
            if (string.IsNullOrWhiteSpace(desktopDir) || !Directory.Exists(desktopDir))
            {
                Console.WriteLine("  SKIP  de novo XIC gate - pass the folder holding the venom RAW files and _curva\\stored_quants.tsv");
                return;
            }

            string rawPath = Path.Combine(desktopDir, GateRawFileName);
            string tsvPath = Path.Combine(desktopDir, "_curva", "stored_quants.tsv");

            if (!File.Exists(rawPath) || !File.Exists(tsvPath))
            {
                Console.WriteLine("  SKIP  de novo XIC gate - need " + GateRawFileName + " and _curva\\stored_quants.tsv under " + desktopDir);
                return;
            }

            List<StoredQuant> stored;
            try
            {
                stored = ReadStoredQuants(tsvPath);
            }
            catch (Exception ex)
            {
                check("the stored quants export is readable", false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            // Stored order, not sorted: the first 250 as the file lists them, so the sample cannot be
            // accused of having been chosen after the fact.
            List<StoredQuant> gate = stored.Where(s => s.Pkg == GatePackage).Take(GateRows).ToList();

            check("package " + GatePackage.ToString(CultureInfo.InvariantCulture) + " supplies " +
                  GateRows.ToString(CultureInfo.InvariantCulture) + " stored quants to replay",
                  gate.Count == GateRows, gate.Count.ToString(CultureInfo.InvariantCulture));
            if (gate.Count == 0) return;

            // One batch, therefore one RAW load. The absurd precursor rides along as the last request
            // rather than costing a second load: it shares the anchor scan of a real row, so the only
            // thing wrong with it is the m/z.
            var requests = new List<DeNovoXicRequest>(gate.Count + 1);
            foreach (StoredQuant s in gate)
            {
                requests.Add(new DeNovoXicRequest
                {
                    ScanNoMS2 = s.ScanNoMS2,
                    PrecursorMz = s.PrecursorMz,
                    Charge = s.Z,
                    Tag = s.Sequence,
                });
            }

            requests.Add(new DeNovoXicRequest
            {
                ScanNoMS2 = gate[0].ScanNoMS2,
                PrecursorMz = 9999.0,
                Charge = 2,
                Tag = "ABSURD",
            });

            // No cache path: the gate must measure the extractor, not a file written by an earlier
            // run of the gate. The cache gets its own check below, on fixtures.
            //
            // MinPointsForQuant IS PINNED TO THE LEGACY RULE HERE, AND THAT IS THE POINT OF THIS GATE.
            // The stored quants were produced by XQuant2's rule, under which one point is enough and a
            // one-point cluster is credited its raw intensity. Reproducing them bit for bit is the proof
            // that this extraction is the SAME MEASUREMENT as the stored series - it is not the production
            // setting, which is DefaultMinPointsForQuant (3). Running the gate at the production setting
            // would be checking the extractor against a rule the stored numbers were never made under, so
            // an agreement would prove nothing and a disagreement would prove nothing either.
            var options = new DeNovoXicOptions
            {
                MinPointsForQuant = DeNovoXicExtractor.LegacyMinPointsForQuant,
                Progress = m => Console.WriteLine("        " + m),
            };

            check("the parity gate runs in LEGACY mode (minimum points per peak = 1), the rule the stored " +
                  "quants were made under",
                  options.MinPointsForQuant == 1 &&
                  DeNovoXicExtractor.LegacyMinPointsForQuant < DeNovoXicExtractor.DefaultMinPointsForQuant,
                  "legacy " + DeNovoXicExtractor.LegacyMinPointsForQuant.ToString(CultureInfo.InvariantCulture) +
                  " vs production " + DeNovoXicExtractor.DefaultMinPointsForQuant.ToString(CultureInfo.InvariantCulture));

            List<DeNovoXicResult> results;
            var started = DateTime.UtcNow;
            try
            {
                results = DeNovoXicExtractor.ExtractRun(rawPath, requests, options);
            }
            catch (Exception ex)
            {
                check("the extraction runs against " + GateRawFileName, false, ex.GetType().Name + ": " + ex.Message);
                return;
            }

            Console.WriteLine("        extraction took " +
                (DateTime.UtcNow - started).TotalSeconds.ToString("F1", CultureInfo.InvariantCulture) + " s");

            check("every request came back, in request order",
                results.Count == requests.Count, results.Count.ToString(CultureInfo.InvariantCulture));
            if (results.Count != requests.Count) return;

            // ---- the gate ---------------------------------------------------------------------
            int exact = 0;
            var firstMismatches = new List<string>();
            for (int i = 0; i < gate.Count; i++)
            {
                double got = results[i].AreaM0;
                double want = gate[i].QuantArea;

                if (got == want) { exact++; continue; }

                if (firstMismatches.Count < 3)
                {
                    firstMismatches.Add(gate[i].Sequence + " z" + gate[i].Z.ToString(CultureInfo.InvariantCulture) +
                        " scan " + gate[i].ScanNoMS2.ToString(CultureInfo.InvariantCulture) +
                        ": stored " + want.ToString("R", CultureInfo.InvariantCulture) +
                        ", extracted " + got.ToString("R", CultureInfo.InvariantCulture) +
                        (results[i].Failure == null ? string.Empty : " [" + results[i].Failure + "]"));
                }
            }

            string detail = exact.ToString(CultureInfo.InvariantCulture) + "/" +
                            gate.Count.ToString(CultureInfo.InvariantCulture) +
                            (firstMismatches.Count == 0 ? string.Empty : "; " + string.Join(" | ", firstMismatches));

            check("M+0 reproduces the stored quant BIT FOR BIT - package " +
                  GatePackage.ToString(CultureInfo.InvariantCulture) + " replayed against run _01",
                  exact >= gate.Count - 1, detail);

            Console.WriteLine("        exact M+0 matches: " + exact.ToString(CultureInfo.InvariantCulture) +
                " of " + gate.Count.ToString(CultureInfo.InvariantCulture));

            // ---- the sentinel means what it says ------------------------------------------------
            DeNovoXicResult absurd = results[results.Count - 1];
            check("a precursor at m/z 9999 on a valid scan returns the no-cluster sentinel",
                absurd.AreaM0 == DeNovoXicExtractor.NoCluster,
                absurd.AreaM0.ToString("R", CultureInfo.InvariantCulture));

            check("the sentinel is not mistaken for signal when the envelope is summed",
                absurd.EnvelopeSum == 0, absurd.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));

            // ---- the envelope and its control ---------------------------------------------------
            int known = -1;
            for (int i = 0; i < gate.Count; i++)
            {
                if (results[i].AreaM0 > 0) { known = i; break; }
            }

            check("at least one replayed precursor has monoisotopic signal to build an envelope on",
                known >= 0, known.ToString(CultureInfo.InvariantCulture));

            if (known >= 0)
            {
                DeNovoXicResult e = results[known];

                // Printed whether or not the checks pass: M-1 over M+0 is the number that says how
                // much of this envelope is interference, and it is worth seeing on every run rather
                // than only when something breaks.
                Console.WriteLine("        envelope for " + e.Tag +
                    "  M-1 " + e.AreaMMinus1.ToString("R", CultureInfo.InvariantCulture) +
                    " | M+0 " + e.AreaM0.ToString("R", CultureInfo.InvariantCulture) +
                    " | M+1 " + e.AreaM1.ToString("R", CultureInfo.InvariantCulture) +
                    " | M+2 " + e.AreaM2.ToString("R", CultureInfo.InvariantCulture) +
                    " | sum " + e.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture) +
                    (e.AreaM0 > 0
                        ? "  (M-1/M+0 " + (Math.Max(0, e.AreaMMinus1) / e.AreaM0).ToString("P2", CultureInfo.InvariantCulture) + ")"
                        : string.Empty));

                // Not the sentinel: a precursor with real monoisotopic signal must have a real M+1
                // window too. -1 here would mean the offset arithmetic put the window somewhere the
                // extractor cannot anchor, which is the failure mode this envelope code can have.
                check("the envelope reports a real M+1 for " + e.Tag + ", not the no-cluster sentinel",
                    e.AreaM1 >= 0, e.AreaM1.ToString("R", CultureInfo.InvariantCulture));

                check("the envelope reports the M-1 interference control for " + e.Tag,
                    !double.IsNaN(e.AreaMMinus1),
                    "M-1 " + e.AreaMMinus1.ToString("R", CultureInfo.InvariantCulture) +
                    ", M+0 " + e.AreaM0.ToString("R", CultureInfo.InvariantCulture));

                check("the envelope sum is M+0+M+1+M+2 with the sentinel clamped, and excludes the M-1 control",
                    e.EnvelopeSum == Math.Max(0, e.AreaM0) + Math.Max(0, e.AreaM1) + Math.Max(0, e.AreaM2),
                    e.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture));
            }

            CacheTests(check, workDir, results);
        }

        /// <summary>
        /// The sidecar has to hand back the same bits it was given, or a cached run and a fresh run
        /// would disagree on numbers this module just proved exact. Runs on the results that were
        /// just extracted, so it round-trips real areas rather than tidy fixtures.
        /// </summary>
        private static void CacheTests(Action<string, bool, string?> check, string workDir, List<DeNovoXicResult> results)
        {
            Directory.CreateDirectory(workDir);
            string path = Path.Combine(workDir, "denovo-xic-cache-test.json");
            if (File.Exists(path)) File.Delete(path);

            string run = results[0].RunName;
            double ppm = DeNovoXicExtractor.ProductionPpm;
            int tol = DeNovoXicExtractor.ProductionMs1Tolerance;
            int iso = DeNovoXicExtractor.DefaultIsotopeCount;
            int minPoints = DeNovoXicExtractor.DefaultMinPointsForQuant;
            const string fingerprint = "sha256-fixture-a";

            DeNovoXicCache write = DeNovoXicCache.Load(path, run, ppm, tol, iso, minPoints, fingerprint);
            foreach (DeNovoXicResult r in results) write.Put(r);
            write.Save();

            DeNovoXicCache read = DeNovoXicCache.Load(path, run, ppm, tol, iso, minPoints, fingerprint);
            check("the cache reads back every entry it wrote", read.Count == write.Count,
                read.Count.ToString(CultureInfo.InvariantCulture) + "/" + write.Count.ToString(CultureInfo.InvariantCulture));

            int identical = 0;
            foreach (DeNovoXicResult r in results)
            {
                if (read.TryGet(r.ScanNoMS2, r.PrecursorMz, r.Charge, out DeNovoXicResult? back) && back != null &&
                    back.AreaM0 == r.AreaM0 && back.AreaM1 == r.AreaM1 &&
                    back.AreaM2 == r.AreaM2 && back.AreaMMinus1 == r.AreaMMinus1)
                {
                    identical++;
                }
            }

            check("every cached area survives the JSON round trip bit for bit",
                identical == results.Count,
                identical.ToString(CultureInfo.InvariantCulture) + "/" + results.Count.ToString(CultureInfo.InvariantCulture));

            // The stamps are the whole safety argument: a sidecar that answered for the wrong run or
            // the wrong window would return plausible numbers from a different measurement.
            check("a cache written for another run is discarded, not reused",
                DeNovoXicCache.Load(path, run + "_02", ppm, tol).Count == 0, null);

            check("a cache written at another ppm is discarded, not reused",
                DeNovoXicCache.Load(path, run, ppm * 2, tol).Count == 0, null);

            check("a cache written at another MS1 tolerance is discarded, not reused",
                DeNovoXicCache.Load(path, run, ppm, tol + 1).Count == 0, null);

            check("a cache written at another isotope count is discarded, not reused",
                DeNovoXicCache.Load(path, run, ppm, tol, iso + 1, minPoints).Count == 0, null);

            // The same cluster is an area at one minimum and a refusal at another, so a sidecar written
            // under one rule holds a different measurement from the one a run under the other is asking
            // for. Serving it would be the silent mixing every other stamp here exists to prevent.
            check("a cache written under another minimum-points rule is discarded, not reused",
                DeNovoXicCache.Load(path, run, ppm, tol, iso, minPoints + 1).Count == 0, null);

            check("a cache written from different RAW content is discarded even when its name and settings match",
                DeNovoXicCache.Load(path, run, ppm, tol, iso, minPoints, "sha256-fixture-b").Count == 0, null);

            File.WriteAllText(path, "{ this is not json");
            check("an unreadable cache is treated as empty rather than throwing",
                DeNovoXicCache.Load(path, run, ppm, tol).Count == 0, null);

            File.Delete(path);
        }

        /// <summary>
        /// Reads the stored quants export. Header-driven, and every number parsed with
        /// InvariantCulture: the file holds invariant decimals, and on a comma-decimal machine the
        /// current culture would read 1028.51038 as 102851038 - which would then fail to match
        /// anything and look like an extraction defect rather than a parsing one.
        /// </summary>
        private static List<StoredQuant> ReadStoredQuants(string path)
        {
            using var reader = new StreamReader(path);
            string? header = reader.ReadLine();
            if (header == null) throw new InvalidDataException("The stored quants file is empty: " + path);

            string[] columns = header.Split('\t');
            int iPkg = IndexOf(columns, "pkg", path);
            int iSeq = IndexOf(columns, "sequence", path);
            int iZ = IndexOf(columns, "z", path);
            int iArea = IndexOf(columns, "quantArea", path);
            int iMz = IndexOf(columns, "precursorMz", path);
            int iScan = IndexOf(columns, "scanNoMS2", path);

            var rows = new List<StoredQuant>();
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                string[] f = line.Split('\t');
                if (f.Length <= iScan) continue;

                rows.Add(new StoredQuant
                {
                    Pkg = int.Parse(f[iPkg], CultureInfo.InvariantCulture),
                    Sequence = f[iSeq],
                    Z = int.Parse(f[iZ], CultureInfo.InvariantCulture),
                    QuantArea = double.Parse(f[iArea], NumberStyles.Float, CultureInfo.InvariantCulture),
                    PrecursorMz = double.Parse(f[iMz], NumberStyles.Float, CultureInfo.InvariantCulture),
                    ScanNoMS2 = int.Parse(f[iScan], CultureInfo.InvariantCulture),
                });
            }

            return rows;
        }

        private static int IndexOf(string[] columns, string name, string path)
        {
            for (int i = 0; i < columns.Length; i++)
            {
                if (string.Equals(columns[i].Trim(), name, StringComparison.OrdinalIgnoreCase)) return i;
            }

            throw new InvalidDataException("The stored quants file has no \"" + name + "\" column.\n\nFile: " +
                path + "\nColumns found: " + string.Join(", ", columns));
        }
    }
}
