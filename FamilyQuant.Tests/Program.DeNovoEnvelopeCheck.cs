using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoEnvelopeCheckCommands() =>
    [
        new("--denovo-envelope-check", 100, TestCommandPhase.Raw,
            args => args[0] == "--denovo-envelope-check", (args, _) => DeNovoEnvelopeCheckCommand(args)),
        new("--test-denovo-envelope-check", 100, TestCommandPhase.Raw,
            args => args[0] == "--test-denovo-envelope-check", (_, _) =>
            {
                DeNovoEnvelopeCheckTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private sealed record EnvelopeRatioSummary(int Count, double? P10, double? Q1,
        double? Median, double? Q3, double? P90);
    private sealed record EnvelopeRatioGroup(int? Charge, EnvelopeRatioSummary R1, EnvelopeRatioSummary R2);
    private sealed record EnvelopeCheckSummary(string Scope, int IdentifiedFeatures, int DeNovoFeatures,
        int MatchedFeatures, int WithoutPositiveIdentifiedEnvelope, int ExtractedAnchors,
        EnvelopeRatioGroup Overall, EnvelopeRatioGroup[] ByCharge);

    private static int DeNovoEnvelopeCheckCommand(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("Use --denovo-envelope-check <project.fqproj> <raw-folder-or-dash> <out-dir>.");
        var (_, rows, manifest) = ProjectStore.ReadFqproj(args[1]);
        // Do not echo a source row, sequence, accession or extraction exception into the receipt.
        var summary = CheckDeNovoEnvelopes(rows, manifest, args[2]);
        Directory.CreateDirectory(args[3]);
        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(args[3], "summary.json"), json);
        Console.WriteLine(json);
        return 0;
    }

    private static EnvelopeCheckSummary CheckDeNovoEnvelopes(List<QuantRow> rows, QuantManifest manifest,
        string rawFolder, Func<string, IReadOnlyList<DeNovoXicRequest>, DeNovoXicOptions,
            List<DeNovoXicResult>>? extract = null)
    {
        var identified = NativeQuantFractions.DedupToPeptides(rows)
            .Where(p => p.Side == NativeQuantFractions.SideIdentified).ToList();
        var identifiedByKey = identified.GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        bool HasMatch(QuantRow row) => identifiedByKey.TryGetValue(
            NativeQuantFractions.CrossSideKey(ToPeptide(row)), out var candidates)
            && candidates.Any(p => NativeQuantFractions.IsCrossSideEquivalent(p, ToPeptide(row)));

        // Reconciliation excludes search-won calls before extraction. The spectrum table keeps their
        // source metadata, but its single shared quant preferentially belongs to PatternLab.
        var calls = EnvelopeCheckCalls(manifest.SpectrumComparisonRows).Where(HasMatch).ToList();
        var deNovoRows = rows.Where(r => r.Side == NativeQuantFractions.SideDeNovo).ToList();
        var storedKeys = deNovoRows.Select(EnvelopeAnchorKey).ToHashSet(StringComparer.Ordinal);
        var missing = calls.Where(r => !storedKeys.Contains(EnvelopeAnchorKey(r)))
            .GroupBy(EnvelopeAnchorKey, StringComparer.Ordinal).Select(g => g.First()).ToList();
        if (missing.Count > 0)
        {
            if (rawFolder == "-")
                throw new InvalidOperationException("Independent de novo areas are absent for " +
                    missing.Count.ToString(CultureInfo.InvariantCulture) + " matched anchors. Supply the RAW folder.");
            if (manifest.IsotopeCount < 1 || manifest.MinPointsForQuant < 1 || !(manifest.Ppm > 0)
                || manifest.Ms1Tolerance < 1)
                throw new InvalidOperationException("The project lacks recorded extraction settings; no defaults were substituted.");
            var rawByRun = Directory.GetFiles(rawFolder, "*.raw").GroupBy(
                    DeNovoReconciler.NormalizeRunName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Single(), StringComparer.OrdinalIgnoreCase);
            foreach (var run in missing.GroupBy(r => r.Run, StringComparer.OrdinalIgnoreCase))
            {
                if (!rawByRun.TryGetValue(run.Key, out string? raw))
                    throw new InvalidOperationException("A RAW acquisition is missing for the matched calls.");
                var requests = run.Select(r => new DeNovoXicRequest
                {
                    ScanNoMS2 = r.Scan, PrecursorMz = r.PrecursorMz, Charge = r.Charge,
                }).GroupBy(r => DeNovoXicCache.KeyFor(r.ScanNoMS2,
                    Math.Round(r.PrecursorMz, DeNovoXicExtractor.MzDecimals), r.Charge), StringComparer.Ordinal)
                    .Select(g => g.First()).ToList();
                var options = new DeNovoXicOptions
                {
                    IsotopeCount = manifest.IsotopeCount, MinPointsForQuant = manifest.MinPointsForQuant,
                    Ppm = manifest.Ppm, Ms1Tolerance = manifest.Ms1Tolerance,
                };
                var measured = extract != null ? extract(raw, requests, options)
                    : DeNovoXicExtractor.ExtractRun(raw, requests, options);
                if (measured.Count != requests.Count || measured.Any(r => r.Failure != null))
                    throw new InvalidOperationException("Matched-call extraction did not complete; no ratio receipt was produced.");
                var areas = measured.ToDictionary(r => DeNovoXicCache.KeyFor(r.ScanNoMS2,
                    Math.Round(r.PrecursorMz, DeNovoXicExtractor.MzDecimals), r.Charge), StringComparer.Ordinal);
                foreach (var call in run)
                {
                    var area = areas[DeNovoXicCache.KeyFor(call.Scan,
                        Math.Round(call.PrecursorMz, DeNovoXicExtractor.MzDecimals), call.Charge)];
                    call.IsotopeAreas = area.IsotopeAreas;
                    call.EnvelopeSum = area.EnvelopeSum;
                    deNovoRows.Add(call);
                }
            }
        }
        // Retain same-side representatives and the production I/L, PTM, charge and isotope-offset
        // equivalence. A re-anchored peptide contributes one ratio, never one per MS2 scan.
        var peptides = NativeQuantFractions.DedupToPeptides(rows.Where(r => r.Side == NativeQuantFractions.SideIdentified)
            .Concat(deNovoRows));
        ReferenceEnvelopeEstimator.Apply(peptides);
        var pairs = new List<(int Charge, double R1, double R2)>();
        int withoutEnvelope = 0, matched = 0;
        foreach (var p in peptides.Where(p => p.Side == NativeQuantFractions.SideDeNovo))
        {
            if (!identifiedByKey.TryGetValue(NativeQuantFractions.CrossSideKey(p), out var candidates)) continue;
            var counterpart = candidates.FirstOrDefault(i => NativeQuantFractions.IsCrossSideEquivalent(i, p));
            if (counterpart == null) continue;
            matched++;
            if (!(counterpart.EnvelopeSum > 0)) { withoutEnvelope++; continue; }
            double r1 = p.EnvelopeSum / counterpart.EnvelopeSum;
            double r2 = p.ReferenceCalibratedEnvelope / counterpart.EnvelopeSum;
            if (!double.IsFinite(r1) || !double.IsFinite(r2))
                throw new InvalidOperationException("A matched ratio is non-finite; no ratio receipt was produced.");
            pairs.Add((p.Charge, r1, r2));
        }
        EnvelopeRatioGroup Group(int? charge, IEnumerable<(int Charge, double R1, double R2)> values)
        {
            var list = values.ToList();
            return new(charge, SummarizeEnvelopeRatios(list.Select(p => p.R1)),
                SummarizeEnvelopeRatios(list.Select(p => p.R2)));
        }
        return new("Configured-filter accepted de novo calls; one largest-M+0 representative per run, peptidoform and charge; " +
            "cross-side precursor equivalence; r1=measured de novo envelope/identified envelope; " +
            "r2=fixed-mass-bin model/identified envelope; linearly interpolated percentiles",
            identified.Count, peptides.Count(p => p.Side == NativeQuantFractions.SideDeNovo), matched,
            withoutEnvelope, missing.Count, Group(null, pairs), pairs.GroupBy(p => p.Charge).OrderBy(g => g.Key)
                .Select(g => Group(g.Key, g)).ToArray());
    }

    private static NativePeptideQuant ToPeptide(QuantRow row) => new()
    {
        Run = DeNovoReconciler.NormalizeRunName(row.Run), Side = row.Side, Sequence = row.Sequence,
        Charge = row.Charge, PrecursorMz = row.PrecursorMz,
    };

    private static string EnvelopeAnchorKey(QuantRow row) =>
        DeNovoReconciler.NormalizeRunName(row.Run).ToUpperInvariant() + "\u001f" + row.Scan + "\u001f" +
        row.Charge + "\u001f" + Math.Round(row.PrecursorMz, DeNovoXicExtractor.MzDecimals).ToString("R", CultureInfo.InvariantCulture)
        + "\u001f" + PeptidoformIdentity.Canonicalize(row.Sequence, foldIsoleucineLeucine: false);

    private static IEnumerable<QuantRow> EnvelopeCheckCalls(IEnumerable<SpectrumComparisonRow> spectra)
    {
        foreach (var row in spectra)
        {
            foreach (var source in new[]
            {
                (row.PeaksDeNovoSequence, row.PeaksDeNovoCharge, row.PeaksDeNovoMz, row.PeaksDeNovoAccepted, row.PeaksDeNovoAlternateSequences),
                (row.DeepNovoSequence, row.DeepNovoCharge, row.DeepNovoMz, row.DeepNovoAccepted, row.DeepNovoAlternateSequences),
            })
            {
                if (source.Item4 == true && source.Item2 is > 0 && source.Item3 is > 0 && source.Item1.Length > 0)
                    yield return Call(source.Item1, source.Item2.Value, source.Item3.Value);
                foreach (string alternate in source.Item5.Split(" | ", StringSplitOptions.RemoveEmptyEntries))
                {
                    var match = Regex.Match(alternate, @"^(.*) \[.*filter=accepted; z=(\d+); m/z=([0-9.]+)\]$");
                    if (match.Success)
                        yield return Call(match.Groups[1].Value, int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                            double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture));
                }
            }
            QuantRow Call(string sequence, int charge, double mz) => new()
            {
                Side = NativeQuantFractions.SideDeNovo, Run = DeNovoReconciler.NormalizeRunName(
                    row.NormalizedRunName.Length > 0 ? row.NormalizedRunName : row.FileName),
                Scan = row.ScanNumber, Charge = charge, PrecursorMz = mz, Sequence = sequence,
            };
        }
    }

    private static EnvelopeRatioSummary SummarizeEnvelopeRatios(IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();
        double? Q(double fraction)
        {
            if (sorted.Length == 0) return null;
            double position = fraction * (sorted.Length - 1);
            int lo = (int)Math.Floor(position), hi = (int)Math.Ceiling(position);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (position - lo);
        }
        return new(sorted.Length, Q(.1), Q(.25), Q(.5), Q(.75), Q(.9));
    }

    private static void DeNovoEnvelopeCheckTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "envelope-check");
        Directory.CreateDirectory(root);
        var rows = new List<QuantRow>
        {
            new() { Run = "run01", Side = "identified", Scan = 10, Charge = 2, PrecursorMz = 500,
                Sequence = "PEPTIDE", IsotopeAreas = new[] { 10d, 10d }, EnvelopeSum = 20 },
            new() { Run = "run01", Side = "denovo", Scan = 11, Charge = 2, PrecursorMz = 500,
                Sequence = "PEPTIDE", IsotopeAreas = new[] { 5d, 10d }, EnvelopeSum = 15 },
        };
        var manifest = new QuantManifest
        {
            IsotopeCount = 6, MinPointsForQuant = 3, Ppm = DeNovoXicExtractor.ProductionPpm,
            Ms1Tolerance = DeNovoXicExtractor.ProductionMs1Tolerance,
        };
        string project = Path.Combine(root, "fixture.fqproj");
        ProjectStore.WriteFqproj(project, new FamilyClusterFile(), rows, manifest);
        string hash = FileFingerprint.Sha256(project);
        var (_, stored, parameters) = ProjectStore.ReadFqproj(project);
        var result = CheckDeNovoEnvelopes(stored, parameters, "-");
        Check("T1 stored independent sides give known measured and model ratios without RAW",
            result.Overall.R1.Median == .75 && result.Overall.R2.Median == .5 && result.Overall.R1.Count == 1
            && result.ByCharge.Single().Charge == 2 && result.ExtractedAnchors == 0);
        Check("T1 check preserves its input project bytes", hash == FileFingerprint.Sha256(project));
        var oldOut = Console.Out;
        var console = new StringWriter(CultureInfo.InvariantCulture);
        int commandExit;
        try { Console.SetOut(console); commandExit = DeNovoEnvelopeCheckCommand(new[] { "--denovo-envelope-check", project, "-", root }); }
        finally { Console.SetOut(oldOut); }
        string receipt = File.ReadAllText(Path.Combine(root, "summary.json"));
        Check("T1 public command writes the same aggregate JSON it prints",
            commandExit == 0 && console.ToString().Trim() == receipt.Trim()
            && JsonDocument.Parse(receipt).RootElement.GetProperty("Overall").GetProperty("R1").GetProperty("Median").GetDouble() == .75);

        var offset = new QuantRow { Run = "run01.raw", Side = "denovo", Scan = 12, Charge = 2,
            PrecursorMz = 500 + DeNovoXicExtractor.Carbon13Spacing / 2, Sequence = "PEPTLDE",
            IsotopeAreas = new[] { 5d, 10d }, EnvelopeSum = 15 };
        var offsetResult = CheckDeNovoEnvelopes(new() { stored[0], offset }, parameters, "-");
        Check("T1 uses production I/L and isotope-offset cross-side equivalence",
            offsetResult.MatchedFeatures == 1 && offsetResult.Overall.R1.Median == .75);
        offset.Sequence = "PEPTLDE(+15.99)";
        Check("T1 cross-side equivalence keeps distinct peptidoforms apart",
            CheckDeNovoEnvelopes(new() { stored[0], offset }, parameters, "-").MatchedFeatures == 0);

        var call = new PeaksDeNovoRow { RunName = "run01", Scan = 10, Charge = 2, Mz = 500, Peptide = "PEPTIDE" };
        var psms = new[] { new PatternLabPsm { FileName = "run01.raw", ScanNumber = 10,
            ChargeState = 2, PrecursorMz = 500, Sequence = "PEPTIDE", XCorr = 5 } };
        parameters.SpectrumComparisonRows = SpectrumComparisonTable.Build(new[] { call }, Array.Empty<PeaksDeNovoRow>(),
            psms, stored.Take(1), acceptedDeNovoRows: new[] { call });
        var spectrum = parameters.SpectrumComparisonRows.Single();
        Check("T1 search-won spectrum retains call metadata but only the identified shared area",
            spectrum.PeaksDeNovoAccepted == true && spectrum.QuantSide == "identified" && spectrum.QuantEnvelopeObserved == 20);
        bool refused = false;
        try { CheckDeNovoEnvelopes(stored.Take(1).ToList(), parameters, "-"); }
        catch (InvalidOperationException) { refused = true; }
        Check("T1 refuses to reuse a search-won shared XIC as an independent de novo measurement", refused);
        File.WriteAllText(Path.Combine(root, "run01.raw"), "synthetic acquisition placeholder");
        bool exactSettings = false;
        var extracted = CheckDeNovoEnvelopes(stored.Take(1).ToList(), parameters, root, (_, requests, options) =>
        {
            exactSettings = options.IsotopeCount == 6 && options.MinPointsForQuant == 3 && options.Ppm == parameters.Ppm
                && options.Ms1Tolerance == parameters.Ms1Tolerance && options.CachePath == null;
            return requests.Select(r => new DeNovoXicResult { ScanNoMS2 = r.ScanNoMS2,
                PrecursorMz = r.PrecursorMz, Charge = r.Charge, IsotopeAreas = new[] { 5d, 10d } }).ToList();
        });
        Check("T1 missing de novo side uses its own RAW request and recorded settings",
            exactSettings && extracted.ExtractedAnchors == 1 && extracted.Overall.R1.Median == .75 && extracted.Overall.R2.Median == .5);
        var quantiles = SummarizeEnvelopeRatios(new[] { 0d, 1d, 2d, 3d, 4d });
        Check("T1 quartiles and tail percentiles use linear interpolation",
            quantiles.Count == 5 && quantiles.Q1 == 1 && quantiles.Median == 2 && quantiles.Q3 == 3
            && quantiles.P10 == .4 && quantiles.P90 == 3.6 && SummarizeEnvelopeRatios(Array.Empty<double>()).Median == null);
        string json = JsonSerializer.Serialize(extracted);
        Check("T1 receipt exposes aggregates only", !json.Contains("PEPTIDE", StringComparison.Ordinal)
            && !json.Contains("run01", StringComparison.Ordinal) && !json.Contains("Sequence", StringComparison.Ordinal));
        spectrum.PeaksDeNovoAlternateSequences = "PEPTIDE [ALC=90; filter=accepted; z=2; m/z=500] | OTHER [ALC=10; filter=rejected; z=3; m/z=400]";
        Check("T1 retains accepted alternative metadata and excludes rejected alternatives",
            EnvelopeCheckCalls(new[] { spectrum }).Count() == 2);
    }
}
