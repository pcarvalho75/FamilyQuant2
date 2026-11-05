using FamilyQuant.DeNovo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record ScanConflictSummary(string Run, int ComparisonScans, int ConflictingScans,
        int ConflictingPrecursorHypotheses, int CreditedDeNovoPeptides, int ConflictingCreditedDeNovoPeptides,
        double ConflictingDeNovoPrimarySignal, double AllDeNovoPrimarySignal, double AllAssignedPrimarySignal,
        double AllSignalTic, double? ShareOfDeNovoPercent, double? ShareOfAssignedPercent, double? ShareOfAllSignalPercent);

    // Study-only access to the production join and representative matcher. No cloned precursor rule.
    private static List<ScanConflictSummary> MeasureSameScanConflicts(List<PeaksDeNovoRow> peaks,
        List<PeaksDeNovoRow> accepted, List<PatternLabPsm> psms, List<QuantRow> quant, IReadOnlyDictionary<string, double> tics)
    {
        var table = SpectrumComparisonTable.Build(peaks, Array.Empty<PeaksDeNovoRow>(), psms, quant,
            perRunTic: tics, acceptedDeNovoRows: accepted);
        Type owner = typeof(SpectrumComparisonTable);
        Type claimType = owner.GetNestedType("Claim", BindingFlags.NonPublic)!;
        Type kindType = owner.GetNestedType("ClaimKind", BindingFlags.NonPublic)!;
        var claims = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(claimType))!;
        object Kind(string name) => Enum.Parse(kindType, name);
        object Make(string kind, string run, int scan, int charge, double mz, string sequence, double? score = null, QuantRow? row = null)
        {
            object claim = Activator.CreateInstance(claimType, nonPublic: true)!;
            foreach (var pair in new (string Name, object? Value)[] { ("Kind", Kind(kind)), ("Run", DeNovoReconciler.NormalizeRunName(run)),
                ("Scan", scan), ("Charge", charge), ("Mz", mz), ("Sequence", sequence), ("Score", score), ("Quant", row) })
                claimType.GetProperty(pair.Name)!.SetValue(claim, pair.Value);
            return claim;
        }
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        var sourceClaim = ReuseMethod(owner, "SourceClaim"); var psmClaim = ReuseMethod(owner, "PatternLabClaim");
        var claimKey = ReuseMethod(owner, "EquivalentClaimKey"); var acceptedSet = accepted.ToHashSet();
        void AddExternal(object claim) { claims.Add(claim); sourceKeys.Add((string)claimKey.Invoke(null, new[] { claim })!); }
        void AddEmbedded(object claim) { if (!sourceKeys.Contains((string)claimKey.Invoke(null, new[] { claim })!)) claims.Add(claim); }
        foreach (var row in peaks) AddExternal(sourceClaim.Invoke(null, new[] { (object)row, Kind("Peaks"), true, acceptedSet.Contains(row) })!);
        foreach (var psm in psms) AddExternal(psmClaim.Invoke(null, new object[] { psm, true })!);
        foreach (var row in quant)
        {
            claims.Add(Make("Quant", row.Run, row.Scan, row.Charge, row.PrecursorMz, row.Sequence, row: row));
            if (row.Side == NativeQuantFractions.SideIdentified)
                AddEmbedded(Make("PatternLab", row.Run, row.Scan, row.Charge, row.PrecursorMz, row.Sequence, row.PatternLabXScore));
            foreach (var evidence in row.DeNovoEvidence ?? new())
                AddEmbedded(Make(evidence.SourceId == DeNovoSource.DeepNovo.Id ? "DeepNovo" : "Peaks", row.Run, row.Scan,
                    row.Charge, row.PrecursorMz, evidence.Sequence, evidence.ConfidenceValue));
        }
        var clusters = ((IEnumerable)ReuseMethod(owner, "BuildClusters").Invoke(null, new object[] { claims })!).Cast<object>().ToList();
        static T Get<T>(object item, string name) => (T)item.GetType().GetProperty(name)!.GetValue(item)!;
        static string ClusterKey(string run, int scan, int charge, double mz) => run.ToUpperInvariant() + "|" + scan.ToString(CultureInfo.InvariantCulture)
            + "|" + charge.ToString(CultureInfo.InvariantCulture) + "|" + BitConverter.DoubleToInt64Bits(mz).ToString(CultureInfo.InvariantCulture);
        var byCluster = table.ToDictionary(r => ClusterKey(r.NormalizedRunName, r.ScanNumber, r.CanonicalCharge, r.CanonicalPrecursorMz), StringComparer.Ordinal);
        if (clusters.Count != table.Count || clusters.Any(c => !byCluster.ContainsKey(ClusterKey(Get<string>(c, "Run"), Get<int>(c, "Scan"), Get<int>(c, "Charge"), Get<double>(c, "OpeningMz")))))
            throw new InvalidDataException("The native inspection clusters differ from the public Spectrum comparison table.");
        var peptides = NativeQuantFractions.DedupToPeptides(quant);
        ReferenceEnvelopeEstimator.Apply(peptides);
        var identified = peptides.Where(p => p.Side == NativeQuantFractions.SideIdentified).GroupBy(NativeQuantFractions.CrossSideKey)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var credited = peptides.Where(p => p.Side == NativeQuantFractions.SideDeNovo
            && (!identified.TryGetValue(NativeQuantFractions.CrossSideKey(p), out var matches)
                || !matches.Any(i => NativeQuantFractions.IsCrossSideEquivalent(i, p)))).ToList();
        string RepKey(string run, string side, string sequence, int charge) => DeNovoReconciler.NormalizeRunName(run).ToUpperInvariant() + "|" + side + "|"
            + PeptidoformIdentity.Canonicalize(sequence, foldIsoleucineLeucine: false) + "|" + charge.ToString(CultureInfo.InvariantCulture);
        var reps = credited.ToDictionary(p => RepKey(p.Run, p.Side, p.Sequence, p.Charge), StringComparer.Ordinal);
        var found = new HashSet<NativePeptideQuant>(); var conflicts = new HashSet<NativePeptideQuant>();
        var conflictClusters = new HashSet<string>(StringComparer.Ordinal); var conflictScans = new HashSet<string>(StringComparer.Ordinal);
        var matchesRepresentative = ReuseMethod(owner, "QuantMatchesRepresentative");
        foreach (object cluster in clusters)
        {
            string key = ClusterKey(Get<string>(cluster, "Run"), Get<int>(cluster, "Scan"), Get<int>(cluster, "Charge"), Get<double>(cluster, "OpeningMz"));
            var row = byCluster[key];
            foreach (object claim in (IEnumerable)Get<object>(cluster, "Claims"))
            {
                var q = claimType.GetProperty("Quant")!.GetValue(claim) as QuantRow;
                if (q == null || !reps.TryGetValue(RepKey(q.Run, q.Side, q.Sequence, q.Charge), out var rep)) continue;
                if ((bool)matchesRepresentative.Invoke(null, new object[] { q, rep })!) found.Add(rep);
                if (q.Charge <= 0 || !(q.PrecursorMz > 0) || !(row.PatternLabCharge > 0) || !(row.PatternLabMz > 0)
                    || row.PatternLabSequence.Length == 0 || DeNovoReconciler.SequencesEquivalent(q.Sequence, row.PatternLabSequence)) continue;
                conflicts.Add(rep); conflictClusters.Add(key);
                conflictScans.Add(row.NormalizedRunName.ToUpperInvariant() + "|" + row.ScanNumber.ToString(CultureInfo.InvariantCulture));
            }
        }
        if (found.Count != credited.Count) throw new InvalidDataException("A credited de novo representative is missing from the native comparison clusters.");
        var fractions = NativeQuantFractions.Compute(quant, tics).Where(f => !f.IsGrouped).OrderBy(f => f.Run, StringComparer.Ordinal).ToList();
        var summaries = new List<ScanConflictSummary>(); int runIndex = 0;
        foreach (var fraction in fractions)
        {
            string run = fraction.Run; string prefix = run.ToUpperInvariant() + "|";
            var runCredited = credited.Where(p => p.Run.Equals(run, StringComparison.OrdinalIgnoreCase)).ToList();
            var runConflicts = conflicts.Where(p => p.Run.Equals(run, StringComparison.OrdinalIgnoreCase)).ToList();
            double all = runCredited.Sum(p => p.ReferenceCalibratedEnvelope), signal = runConflicts.Sum(p => p.ReferenceCalibratedEnvelope);
            if (Math.Abs(all - fraction.DeNovoEnvelopeReferenceCalibrated) > 1e-10 * Math.Max(1, Math.Abs(all)))
                throw new InvalidDataException("The credited de novo denominator differs from production quantitation.");
            double assigned = fraction.IdentifiedEnvelope + all;
            static double? Share(double value, double total) => total > 0 ? 100 * value / total : null;
            summaries.Add(new ScanConflictSummary("Run " + (++runIndex).ToString(CultureInfo.InvariantCulture),
                table.Where(r => r.NormalizedRunName.Equals(run, StringComparison.OrdinalIgnoreCase)).Select(r => r.ScanNumber).Distinct().Count(),
                conflictScans.Count(k => k.StartsWith(prefix, StringComparison.Ordinal)), conflictClusters.Count(k => k.StartsWith(prefix, StringComparison.Ordinal)),
                runCredited.Count, runConflicts.Count, signal, all, assigned, fraction.Tic, Share(signal, all), Share(signal, assigned), Share(signal, fraction.Tic)));
        }
        return summaries;
    }

    private static int SameScanConflictsCommand(string[] args)
    {
        if (args.Length != 7) throw new ArgumentException("Supply project, SEPro package, PEAKS export root, output directory, date timestamp and run label.");
        string projectPath = Path.GetFullPath(args[1]), psmPath = Path.GetFullPath(args[2]), exportRoot = Path.GetFullPath(args[3]);
        string output = Path.GetFullPath(args[4]);
        // The final argument is an explicitly supplied run label so the command can be run twice into separate folders.
        string timestamp = args[5], runLabel = args[6];
        Directory.CreateDirectory(output);
        var project = ProjectStore.ReadFqproj(projectPath);
        var inputs = project.manifest.DeNovoInputs;
        if (inputs.Count != 3 || inputs.Any(i => i.SourceId != DeNovoSource.DenovoOnly.Id || i.MinimumConfidence != 80))
            throw new InvalidDataException("The selected project must record three PEAKS de novo only source files at ALC 80.");
        var paths = DeNovoSourceFileDiscovery.RequireFiles(exportRoot, DeNovoSource.DenovoOnly);
        if (!paths.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(inputs.Select(i => Path.GetFullPath(i.FilePath))))
            throw new InvalidDataException("The selected export files differ from the stored project's source manifest.");
        static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var fingerprints = inputs.ToDictionary(i => Path.GetFullPath(i.FilePath), i => Hash(i.FilePath), StringComparer.OrdinalIgnoreCase);
        if (inputs.Any(i => !fingerprints[Path.GetFullPath(i.FilePath)].Equals(i.Sha256, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A selected PEAKS input changed since the quantitation was stored.");
        fingerprints.Add(projectPath, Hash(projectPath)); fingerprints.Add(psmPath, Hash(psmPath));
        var overlap = project.manifest.DeNovoOverlap ?? throw new InvalidDataException("The stored project has no recorded filter criteria.");
        var criteria = new DeNovoFilterCriteria { MinLength = overlap.MinLength, MinAverageLocalConfidence = 80, StretchLength = overlap.StretchLength, StretchFloor = overlap.StretchFloor };
        var peaks = paths.SelectMany(p => PeaksCsvReader.Read(p, DeNovoSource.DenovoOnly)).ToList();
        var accepted = DeNovoFilter.Apply(peaks, criteria, 80);
        var psms = SeproPsmAdapter.Load(psmPath);
        var summaries = MeasureSameScanConflicts(peaks, accepted, psms, project.quant, project.manifest.PerRunTic);
        if (summaries.Count != 3) throw new InvalidDataException("The measurement did not resolve exactly three runs.");
        if (fingerprints.Any(p => Hash(p.Key) != p.Value)) throw new InvalidDataException("A measurement input changed during the read-only run.");
        File.WriteAllText(Path.Combine(output, "counts-and-shares.json"), JsonSerializer.Serialize(summaries, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(output, "method.json"), JsonSerializer.Serialize(new
        {
            Timestamp = timestamp, RunLabel = runLabel, ProjectSha256 = fingerprints[projectPath], SeproSha256 = fingerprints[psmPath],
            CoreAssemblySha256 = Hash(typeof(SpectrumComparisonTable).Assembly.Location), TestAssemblySha256 = Hash(typeof(Program).Assembly.Location),
            SourceSha256 = inputs.Select(i => i.Sha256).OrderBy(s => s, StringComparer.Ordinal).ToArray(), Source = DeNovoSource.DenovoOnly.Id, Cutoff = 80, criteria.MinLength, criteria.StretchLength, criteria.StretchFloor,
            InputsUnchanged = true, RawExtraction = false,
            Method = "Native SpectrumComparisonTable.Build/BuildClusters and QuantMatchesRepresentative. Top PatternLab sequence per native precursor cluster; PTMs removed and I/L folded for sequence disagreement. A conflict at any stored anchor of a credited de novo peptide marks its single production representative as involved. Production collapse/suppression/reference calibration; each peptide's signal counted once even across conflicting scans. All credited representatives must be found in the native clusters. Run labels follow normalized run-name order.",
            AllSignalBasis = "Per-run measured TIC; also report the separately named assigned-primary denominator. Stored XIC areas, no fresh extraction. Current peptide signal involved is not the predicted signal change under scan exclusion. Counts and shares only; no source rows exported.",
        }, new JsonSerializerOptions { WriteIndented = true }));
        string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
        File.WriteAllText(Path.Combine(output, "command.ps1"), "& " + Quote(Environment.ProcessPath!) + " " + string.Join(" ", args.Select(Quote)) + Environment.NewLine);
        var report = new System.Text.StringBuilder("# Same-scan conflicts\n\n");
        report.AppendLine("Stored PEAKS de novo only quantitation at ALC 80, three runs. Inputs were read only and their fingerprints remained equal.");
        report.AppendLine("A conflict at any stored anchor marks its credited peptide signal once. Production precursor clustering, peptide collapse, cross-side suppression and reference calibration are used. This measures signal involved, not the signal change under a future scan-exclusion rule.");
        report.AppendLine();
        report.AppendLine("| Run | Conflict scans | Conflict precursor hypotheses | Involved credited de novo peptides | Involved de novo primary signal | Share of all de novo | Share of assigned primary | Share of all signal (TIC) |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
        string Number(double? value) => value?.ToString("G12", CultureInfo.InvariantCulture) ?? "unavailable";
        foreach (var row in summaries) report.AppendLine("| " + row.Run + " | " + row.ConflictingScans.ToString(CultureInfo.InvariantCulture)
            + " | " + row.ConflictingPrecursorHypotheses.ToString(CultureInfo.InvariantCulture) + " | " + row.ConflictingCreditedDeNovoPeptides.ToString(CultureInfo.InvariantCulture)
            + " | " + Number(row.ConflictingDeNovoPrimarySignal) + " | " + Number(row.ShareOfDeNovoPercent) + "% | " + Number(row.ShareOfAssignedPercent)
            + "% | " + Number(row.ShareOfAllSignalPercent) + "% |");
        report.AppendLine("\nAll-signal denominator: the recorded per-run measured TIC. Assigned-primary denominator: identified observed envelope plus reference-calibrated de novo primary signal. No source rows are exported. Exact invocation: command.ps1. Full count and denominator values: counts-and-shares.json.");
        File.WriteAllText(Path.Combine(output, "report.md"), report.ToString());
        foreach (var row in summaries) Console.WriteLine(JsonSerializer.Serialize(row));
        return 0;
    }

    private static void SameScanConflictTests()
    {
        QuantRow Quant(string side, int scan, string sequence, double mz, double m0) => new()
        {
            Run = "fixture", Side = side, Scan = scan, Sequence = sequence, Charge = 2, PrecursorMz = mz,
            IsotopeAreas = new[] { m0, m0 }, EnvelopeSum = 2 * m0, PatternLabXScore = side == NativeQuantFractions.SideIdentified ? 4 : null,
        };
        var psm = new PatternLabPsm { FileName = "fixture", ScanNumber = 10, Sequence = "AAAAAAAK", ChargeState = 2, PrecursorMz = 500, XCorr = 4 };
        var quants = new List<QuantRow>
        {
            Quant(NativeQuantFractions.SideIdentified, 10, psm.Sequence, 500, 10),
            Quant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK", 500, 4),
            Quant(NativeQuantFractions.SideDeNovo, 11, "PEPTIDEK", 500, 2),
            Quant(NativeQuantFractions.SideDeNovo, 20, "WWWWWWWK", 600, 3),
        };
        var tics = new Dictionary<string, double> { ["fixture"] = 100 };
        var visible = SpectrumComparisonTable.Build(Array.Empty<PeaksDeNovoRow>(), Array.Empty<PeaksDeNovoRow>(), new[] { psm }, quants);
        Check("Z6a the synthetic conflict is hidden by the public table's selected identified quantity",
            visible.Single(r => r.ScanNumber == 10).QuantSide == NativeQuantFractions.SideIdentified);
        var summary = MeasureSameScanConflicts(new(), new(), new() { psm }, quants, tics).Single();
        Check("Z6a counts the hidden credited de novo conflict once after native peptide collapse",
            summary.ConflictingScans == 1 && summary.ConflictingPrecursorHypotheses == 1
            && summary.CreditedDeNovoPeptides == 2 && summary.ConflictingCreditedDeNovoPeptides == 1
            && summary.ConflictingDeNovoPrimarySignal == 8 && summary.AllDeNovoPrimarySignal == 14
            && summary.ShareOfAllSignalPercent == 8 && summary.AllAssignedPrimarySignal == 34);
        var suppressed = new List<QuantRow>
        {
            Quant(NativeQuantFractions.SideIdentified, 10, "PEPTLDEK", 500, 10),
            Quant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK", 500, 4),
        };
        var suppressedResult = MeasureSameScanConflicts(new(), new(), new(), suppressed, tics).Single();
        Check("Z6a native cross-side suppression excludes I/L-equivalent peptides", suppressedResult.CreditedDeNovoPeptides == 0
            && suppressedResult.ConflictingScans == 0 && suppressedResult.ConflictingDeNovoPrimarySignal == 0);
        var separate = new List<QuantRow>
        {
            Quant(NativeQuantFractions.SideIdentified, 10, psm.Sequence, 500, 10),
            Quant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK", 600, 4),
        };
        var separateResult = MeasureSameScanConflicts(new(), new(), new() { psm }, separate, tics).Single();
        Check("Z6a a shared scan with another precursor is not a sequence conflict", separateResult.CreditedDeNovoPeptides == 1
            && separateResult.ConflictingScans == 0 && separateResult.ConflictingDeNovoPrimarySignal == 0);
        var otherAnchor = new List<QuantRow>
        {
            Quant(NativeQuantFractions.SideIdentified, 30, psm.Sequence, 500, 10),
            Quant(NativeQuantFractions.SideDeNovo, 10, "PEPTIDEK", 500, 4),
            Quant(NativeQuantFractions.SideDeNovo, 11, "PEPTIDEK", 500, 2),
        };
        var anchorPsm = new PatternLabPsm { FileName = "fixture", ScanNumber = 11, Sequence = psm.Sequence, ChargeState = 2, PrecursorMz = 500, XCorr = 4 };
        var anchorResult = MeasureSameScanConflicts(new(), new(), new() { anchorPsm }, otherAnchor, tics).Single();
        Check("Z6a a conflict at a nonrepresentative anchor marks its credited peptide signal once",
            anchorResult.ConflictingScans == 1 && anchorResult.ConflictingCreditedDeNovoPeptides == 1 && anchorResult.ConflictingDeNovoPrimarySignal == 8);
    }
}
