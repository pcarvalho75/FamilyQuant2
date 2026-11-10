using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoMeasuredEnvelopeCommands() =>
    [
        new("--denovo-switch-compare", 100, TestCommandPhase.Raw,
            args => args[0] == "--denovo-switch-compare", (args, _) => DeNovoSwitchCompare(args)),
        new("--test-denovo-measured-envelope", 100, TestCommandPhase.Raw,
            args => args[0] == "--test-denovo-measured-envelope", (_, _) =>
            {
                string root = Path.Combine(Path.GetTempPath(), "denovo-measured");
                Directory.CreateDirectory(root);
                DeNovoMeasuredEnvelopeTests(root);
                ResultsMethodGateTests();
                NativeFractionUnitTests(root);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private sealed record DeNovoRuleRecovery(string Run, int IdentifiedFeatures, int DeNovoFeatures,
        double? OldModelRecovery, double? NewMeasuredRecovery);
    private sealed record DeNovoRuleFamily(int FamilyUserId, int PeptideFeatures,
        double? ShareOfAssignedSignal, double? ShareOfMeasuredSignal);
    private sealed record DeNovoRuleComparison(string FamilyUnit, string SharedPeptidePolicy,
        int IdentifiedFeatures, int DeNovoFeatures, int? SharedFeatures, int? UnattributedFeatures,
        DeNovoRuleRecovery[] RecoveryPerRun, DeNovoRuleFamily[] OldModelTop15, DeNovoRuleFamily[] NewMeasuredTop15,
        string FamilyAttribution = "stored family assignments", bool FamilySharesAvailable = true,
        bool HomologySearchPerformed = false, double HomologySearchSeconds = 0);

    private sealed class ComparisonCacheMiss : Exception { }

    private static int DeNovoSwitchCompare(string[] args)
    {
        if (args.Length != 3 && (args.Length != 5 || args[3] != "--homology-cache"))
            throw new ArgumentException("Use --denovo-switch-compare <project.fqproj> <out-dir> [--homology-cache <folder>].");
        var (curation, rows, manifest) = ProjectStore.ReadFqproj(args[1]);
        Directory.CreateDirectory(args[2]);
        // The CLI normally isolates UI state. This read-only lookup uses the actual program hit-cache
        // location; synthetic tests explicitly pass a private cache and never read real per-user data.
        string cache = args.Length == 5 ? args[4] : Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ApplicationPaths.DataFolderName,
            FullXicComputer.CacheFolderName, "curation-homology");
        DeNovoRuleRecovery[] recovery = Array.Empty<DeNovoRuleRecovery>();
        bool recoveryReady = false;
        bool searched = false;
        double searchSeconds = 0;
        DeNovoRuleComparison comparison;
        try
        {
            comparison = CompareStoredDeNovoRules(rows, manifest, curation, cache, runs =>
            {
                recovery = runs; recoveryReady = true;
                File.WriteAllText(Path.Combine(args[2], "recovery.json"),
                    JsonSerializer.Serialize(runs, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Recovery per run: old model / measured envelope; same recorded TIC.");
                foreach (var run in runs)
                {
                    static string P(double? share) => share?.ToString("P4", CultureInfo.InvariantCulture) ?? "unavailable";
                    Console.WriteLine(run.Run + ": " + P(run.OldModelRecovery) + " / " + P(run.NewMeasuredRecovery)
                        + "; identified features " + run.IdentifiedFeatures.ToString(CultureInfo.InvariantCulture)
                        + "; de novo features " + run.DeNovoFeatures.ToString(CultureInfo.InvariantCulture));
                }
            }, Console.WriteLine, seconds => { searched = true; searchSeconds = seconds; });
        }
        catch (Exception error) when (recoveryReady && error is InvalidOperationException or ArgumentException or IOException)
        {
            // Recovery remains usable even when this project's families cannot be attributed. Do not
            // echo a search exception, which could contain a peptide or protein identifier.
            comparison = new("Project first-pass UserID families, pooled over runs", (manifest.FamilyRunShares?.Policy ?? SharedPeptidePolicy.FamilyUnique).ToString(),
                recovery.Sum(r => r.IdentifiedFeatures), recovery.Sum(r => r.DeNovoFeatures), null, null,
                recovery, Array.Empty<DeNovoRuleFamily>(), Array.Empty<DeNovoRuleFamily>(),
                "Family shares unavailable (" + error.GetType().Name + "); recovery is complete", false, searched, searchSeconds);
        }
        string json = JsonSerializer.Serialize(comparison, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(args[2], "switch-comparison.json"), json);
        Console.WriteLine(json);
        return comparison.FamilySharesAvailable ? 0 : 1;
    }

    private static DeNovoRuleComparison CompareStoredDeNovoRules(List<QuantRow> rows, QuantManifest manifest,
        FamilyClusterFile? curation = null, string? cacheDirectory = null,
        Action<DeNovoRuleRecovery[]>? recovered = null, Action<string>? note = null, Action<double>? searchReported = null)
    {
        var peptides = NativeQuantFractions.DedupToPeptides(rows);
        // Historical comparison only. Live quantitation never calls this model.
        ReferenceEnvelopeEstimator.Apply(peptides);
        var identified = peptides.Where(p => p.Side == NativeQuantFractions.SideIdentified)
            .GroupBy(NativeQuantFractions.CrossSideKey, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var credited = peptides.Where(p => p.Side == NativeQuantFractions.SideIdentified
            || !identified.TryGetValue(NativeQuantFractions.CrossSideKey(p), out var matches)
            || !matches.Any(i => NativeQuantFractions.IsCrossSideEquivalent(i, p))).ToList();
        double? Share(double signal, double denominator) => denominator > 0 ? signal / denominator : null;
        var tic = manifest.PerRunTic.GroupBy(p => DeNovoReconciler.NormalizeRunName(p.Key), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Single().Value, StringComparer.OrdinalIgnoreCase);
        var recovery = credited.GroupBy(p => p.Run, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var runs = recovery.Keys.Concat(tic.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(run =>
            {
                var list = recovery.GetValueOrDefault(run) ?? new();
                double denominator = tic.GetValueOrDefault(run);
                return new DeNovoRuleRecovery(run, list.Count(p => p.Side == NativeQuantFractions.SideIdentified),
                    list.Count(p => p.Side == NativeQuantFractions.SideDeNovo),
                    Share(list.Sum(p => p.Side == NativeQuantFractions.SideIdentified ? p.EnvelopeSum : p.ReferenceCalibratedEnvelope), denominator),
                    Share(list.Sum(p => p.EnvelopeSum), denominator));
            }).ToArray();
        recovered?.Invoke(runs);
        var policy = manifest.FamilyRunShares?.Policy ?? SharedPeptidePolicy.FamilyUnique;
        if (policy is not (SharedPeptidePolicy.FamilyUnique or SharedPeptidePolicy.SplitEqually))
            throw new InvalidOperationException("Stored overlapping family counts cannot form additive family shares.");

        string Identity(string run, string side, string sequence, int charge) =>
            DeNovoReconciler.NormalizeRunName(run).ToUpperInvariant() + "\u001f" + side + "\u001f" +
            PeptidoformIdentity.Canonicalize(sequence, foldIsoleucineLeucine: false) + "\u001f" + charge;
        var spectrumByIdentity = manifest.SpectrumComparisonRows.Where(r => r.QuantSide.Length > 0)
            .GroupBy(r => Identity(r.NormalizedRunName.Length > 0 ? r.NormalizedRunName : r.FileName,
                r.QuantSide, r.QuantSequence, r.QuantCharge ?? r.Charge), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var rowByIdentity = rows.GroupBy(r => Identity(r.Run, r.Side, r.Sequence, r.Charge), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        int[]? StoredFamilies(NativePeptideQuant peptide)
        {
            string key = Identity(peptide.Run, peptide.Side, peptide.Sequence, peptide.Charge);
            var spectrum = spectrumByIdentity.GetValueOrDefault(key)?.FirstOrDefault(r => r.ScanNumber == peptide.RepresentativeScan);
            if (spectrum != null)
            {
                if (spectrum.FamilyMappingStatus.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
                    || spectrum.FamilyMappingStatus.Contains("kept", StringComparison.OrdinalIgnoreCase)) return null;
                var ids = new List<int>();
                foreach (string text in spectrum.FamilyUserIds.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) return null;
                    ids.Add(id);
                }
                if (ids.Count > 0) return ids.Distinct().Order().ToArray();
                if (spectrum.FamilyUserId is int legacyId) return new[] { legacyId };
                return spectrum.FamilyMappingStatus.Contains("unattributed", StringComparison.OrdinalIgnoreCase) ? Array.Empty<int>() : null;
            }
            var anchor = rowByIdentity[key].First(r => r.Scan == peptide.RepresentativeScan);
            return anchor.FamilyUserId is int family ? new[] { family } : null;
        }
        var stored = credited.Select(p => (Peptide: p, Families: StoredFamilies(p))).ToList();
        PeptideFamilyAttribution? attribution = null;
        string attributionSource = "complete stored family assignments";
        bool searched = false;
        double searchSeconds = 0;
        if (stored.Any(p => p.Families == null))
        {
            if (curation?.MyExtendedFastaItems is not { Count: > 0 })
                throw new InvalidOperationException("This project contains no searchable family proteins.");
            var options = new HomologyOptions();
            if (manifest.FamilyMapping != null)
            {
                options.MinIdentity = manifest.FamilyMapping.MinIdentity;
                options.Science = manifest.FamilyMapping.Science?.Clone();
            }
            CurationHomologyHits hits;
            try
            {
                // The native cache callback runs before a miss can search or write. A hit has already
                // passed its full content/options/query/localization validation.
                hits = CurationHomologyCache.GetOrSearch(rows, curation, cacheDirectory, options,
                    progress: hit => { if (!hit) throw new ComparisonCacheMiss(); });
                attributionSource = "verified retained homology hits, joined to project UserID families";
            }
            catch (ComparisonCacheMiss)
            {
                searched = true;
                note?.Invoke("Stored family assignments/hits are incomplete; running the program's homology attribution in memory.");
                var watch = Stopwatch.StartNew();
                searchReported?.Invoke(0);
                try { hits = FamilyEnvelopeQuant.SearchCuration(rows, curation, options); }
                finally
                {
                    watch.Stop(); searchSeconds = watch.Elapsed.TotalSeconds;
                    searchReported?.Invoke(searchSeconds);
                    note?.Invoke("In-memory homology search: " + searchSeconds.ToString("0.000", CultureInfo.InvariantCulture) + " seconds.");
                }
                attributionSource = "program homology search in memory, joined to project UserID families";
            }
            attribution = FamilyEnvelopeQuant.AttributeHits(hits, curation);
        }
        var families = new Dictionary<int, (int Count, double Old, double New)>();
        int shared = 0, unattributed = 0;
        double oldTotal = 0, newTotal = 0;
        foreach (var item in stored)
        {
            var peptide = item.Peptide;
            bool isIdentified = peptide.Side == NativeQuantFractions.SideIdentified;
            double oldSignal = isIdentified ? peptide.EnvelopeSum : peptide.ReferenceCalibratedEnvelope;
            double newSignal = peptide.EnvelopeSum;
            oldTotal += oldSignal; newTotal += newSignal;
            IReadOnlyList<int> ids = attribution?.Resolve(peptide.Side, peptide.Sequence) ?? item.Families!;
            if (ids.Count == 0) { unattributed++; continue; }
            if (ids.Count > 1) shared++;
            if (policy == SharedPeptidePolicy.FamilyUnique && ids.Count > 1) continue;
            double fraction = 1.0 / ids.Count;
            foreach (int id in ids)
            {
                var prior = families.GetValueOrDefault(id);
                families[id] = (prior.Count + 1, prior.Old + oldSignal * fraction, prior.New + newSignal * fraction);
            }
        }
        double oldAssigned = families.Values.Sum(f => f.Old), newAssigned = families.Values.Sum(f => f.New);
        DeNovoRuleFamily[] Top(bool old) => families.OrderByDescending(f => old ? f.Value.Old : f.Value.New)
            .ThenBy(f => f.Key).Take(15).Select(f => new DeNovoRuleFamily(f.Key, f.Value.Count,
                Share(old ? f.Value.Old : f.Value.New, old ? oldAssigned : newAssigned),
                Share(old ? f.Value.Old : f.Value.New, old ? oldTotal : newTotal))).ToArray();
        return new("Project first-pass UserID families, pooled over runs; same attribution for both rules; no family-world credit",
            policy.ToString(), credited.Count(p => p.Side == NativeQuantFractions.SideIdentified),
            credited.Count(p => p.Side == NativeQuantFractions.SideDeNovo), shared, unattributed,
            runs, Top(true), Top(false), attributionSource, true, searched, searchSeconds);
    }

    private static void DeNovoMeasuredEnvelopeTests(string root)
    {
        Directory.CreateDirectory(root);
        var rows = new List<QuantRow>
        {
            Qr("identified", "run01", 1, 2, "PEPTIDEK", 10, 20),
            Qr("denovo", "run01", 2, 2, "AAAAAAAK", 5, 15),
            Qr("denovo", "run01", 3, 2, "VVVVVVVK", 10, 50),
            Qr("denovo", "run01", 4, 2, "GGGGGGGK", 6, 36),
            Qr("denovo", "run01", 5, 2, "TTTTTTTK", 2, 8),
        };
        foreach (var row in rows) row.PrecursorMz = 500;
        var map = new PeptideFamilyAttribution();
        map.IdentifiedBySequence[DeNovoReconciler.NormalizeSequence("PEPTIDEK")] = new[] { 1 };
        map.DeNovoBySequence["AAAAAAAK"] = new[] { 1 };
        map.DeNovoBySequence["VVVVVVVK"] = new[] { 2 };
        map.DeNovoBySequence["GGGGGGGK"] = new[] { 1, 2 };
        var prepared = FamilyEnvelopeQuant.PreparePeptides(rows);
        Check("T3 prepared peptides retain measured envelopes on both sides", prepared.All(p => p.ReferenceCalibratedEnvelope == p.EnvelopeSum));
        var assigned = FamilyEnvelopeQuant.AttributePeptides(prepared, map);
        var table = FamilyEnvelopeQuant.BuildFromAttributedPeptides(assigned);
        Check("T3 primary family, measured, shared and residual values use the measured envelope",
            table.MeasuredDeNovoEnvelopePrimary == 109 && table.SharedDeNovoEnvelopePrimary == 36
            && table.UnattributedDeNovoEnvelopePrimary == 8 && table.Families.Single(f => f.FamilyUserId == 1).UniqueDeNovoEnvelopePrimary == 15
            && table.Families.Single(f => f.FamilyUserId == 1).SplitDeNovoEnvelopePrimary == 33);
        var stale = new FamilyEnvelopeQuantRow { DeNovoEnvelope = 7, DeNovoEnvelopeReferenceCalibrated = 99,
            UniqueDeNovoEnvelope = 3, UniqueDeNovoEnvelopeReferenceCalibrated = 98,
            SplitDeNovoEnvelope = 5, SplitDeNovoEnvelopeReferenceCalibrated = 97 };
        Check("T3 primary property names ignore legacy calibrated fields", stale.DeNovoEnvelopePrimary == 7
            && stale.UniqueDeNovoEnvelopePrimary == 3 && stale.SplitDeNovoEnvelopePrimary == 5);
        var fraction = NativeQuantFractions.Compute(rows, new Dictionary<string, double> { ["run01"] = 1000 }).Single();
        Check("T3 recovery uses measured envelopes and retains the M+0 reading", fraction.RecoveredFractionReferenceCalibrated == .129
            && fraction.RecoveredFractionReferenceCalibrated == fraction.RecoveredFractionEnvelope && fraction.RecoveredFractionM0 == .033);
        var spectra = SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(),
            Array.Empty<PatternLabPsm>(), rows, familyAttribution: map);
        spectra.Single(s => s.QuantSequence == "TTTTTTTK").FamilyMappingStatus = "no target family met the configured homology rule (unattributed)";
        Check("T3 spectrum primary assignment is the measured envelope", spectra.All(s => s.QuantPrimaryAssigned == s.QuantEnvelopeObserved)
            && spectra.Where(s => s.QuantSide == "denovo").All(s => s.QuantPrimaryBasis.Contains("measured envelope", StringComparison.Ordinal)));
        var manifest = new QuantManifest { IsotopeCount = 6, PerRunTic = new() { ["run01"] = 1000 }, SpectrumComparisonRows = spectra };
        NativeQuantFractions.PopulateManifest(manifest, rows);
        Check("T3 a new computation records the new measured-envelope method and provenance", manifest.DerivedMethod == NativeQuantFractions.Method
            && manifest.DerivedMethod != "per-peptidoform-charge-maxm0-over-tic-v2"
            && manifest.Provenance["recovered_fraction_reference_calibrated"].Contains("measured", StringComparison.Ordinal));
        var comparison = CompareStoredDeNovoRules(rows, manifest);
        Check("T3 old/new comparator has known recovery and top family shares on identical stored rows",
            comparison.RecoveryPerRun.Single().OldModelRecovery == .066 && comparison.RecoveryPerRun.Single().NewMeasuredRecovery == .129
            && comparison.OldModelTop15[0].FamilyUserId == 1 && comparison.OldModelTop15[0].ShareOfAssignedSignal == .6
            && comparison.NewMeasuredTop15[0].FamilyUserId == 2 && comparison.NewMeasuredTop15[0].ShareOfAssignedSignal == 50d / 85);
        Check("T3 comparator counts shared and unattributed features once", comparison.IdentifiedFeatures == 1
            && comparison.DeNovoFeatures == 4 && comparison.SharedFeatures == 1 && comparison.UnattributedFeatures == 1);
        manifest.FamilyRunShares = new() { Policy = SharedPeptidePolicy.SplitEqually };
        var split = CompareStoredDeNovoRules(rows, manifest);
        Check("T3 comparator uses the recorded split policy without counting shared signal twice",
            split.OldModelTop15.Single(f => f.FamilyUserId == 1).ShareOfAssignedSignal == 36d / 62
            && split.NewMeasuredTop15.Single(f => f.FamilyUserId == 1).ShareOfAssignedSignal == 53d / 121);
        manifest.FamilyRunShares = null;
        string project = Path.Combine(root, "fixture.fqproj");
        ProjectStore.WriteFqproj(project, new FamilyClusterFile(), rows, manifest);
        string hash = FileFingerprint.Sha256(project);
        var console = new StringWriter(CultureInfo.InvariantCulture); var oldOut = Console.Out;
        try { Console.SetOut(console); DeNovoSwitchCompare(new[] { "--denovo-switch-compare", project, root }); }
        finally { Console.SetOut(oldOut); }
        string json = File.ReadAllText(Path.Combine(root, "switch-comparison.json"));
        Check("T3 public comparator prints its aggregate receipt and preserves source bytes",
            console.ToString().TrimEnd().EndsWith(json.TrimEnd(), StringComparison.Ordinal) && hash == FileFingerprint.Sha256(project)
            && !json.Contains("PEPTIDEK", StringComparison.Ordinal) && !json.Contains("\"OldSignal\"", StringComparison.Ordinal)
            && !json.Contains("\"NewSignal\"", StringComparison.Ordinal));
    }

    private static void ResultsMethodGateTests()
    {
        var rows = new[] { Qr("identified", "run01", 1, 2, "PEPTIDEK", 10, 20) };
        foreach (string method in new[] { "per-peptidoform-charge-maxm0-over-tic-v2", "" })
        {
            var manifest = new QuantManifest { DerivedMethod = method, IsotopeCount = 6, PerRunTic = new() { ["run01"] = 1000 } };
            string before = JsonSerializer.Serialize(manifest);
            var old = new AnalyticsViewModel(rows, manifest);
            Check("T3 Results asks for Quantify again for " + (method.Length == 0 ? "an unrecorded method" : "the previous method"),
                old.RequiresQuantify && !old.HasData && old.Families == null && old.Cards.Count == 0 && !old.CanExport
                && old.EmptyMessage.Contains("Run Quantify again", StringComparison.Ordinal) && old.BuildCompositionFigures().Count == 0
                && before == JsonSerializer.Serialize(manifest));
        }
        var current = new AnalyticsViewModel(rows, new QuantManifest { DerivedMethod = NativeQuantFractions.Method,
            IsotopeCount = 6, PerRunTic = new() { ["run01"] = 1000 } });
        Check("T3 Results presents current measured-envelope computations", !current.RequiresQuantify && current.HasData
            && current.Cards.Count == 3 && current.Cards[0].Detail.Contains("measured envelope", StringComparison.Ordinal));
    }
}
