using FamilyQuant.DeNovo;
using FamilyQuant.Homology;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int PatternLabPriorityReplay(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("Supply stored project, SEPro package, PEAKS export root, output directory and date timestamp.");
        string projectPath = Path.GetFullPath(args[1]), seproPath = Path.GetFullPath(args[2]), root = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
        Directory.CreateDirectory(output);
        static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }
        var inputHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [projectPath] = Hash(projectPath), [seproPath] = Hash(seproPath) };
        var project = ProjectStore.ReadFqproj(projectPath);
        if (project.fcf.MyExtendedFastaItems.Count == 0) throw new InvalidDataException("The stored project has no family curation for the replay.");
        var files = DeNovoSourceFileDiscovery.RequireFiles(root, DeNovoSource.DenovoOnly);
        if (project.manifest.DeNovoInputs.Count != 3 || project.manifest.DeNovoInputs.Any(i => i.SourceId != DeNovoSource.DenovoOnly.Id || i.MinimumConfidence != 80)
            || !files.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(project.manifest.DeNovoInputs.Select(i => Path.GetFullPath(i.FilePath))))
            throw new InvalidDataException("The replay requires the recorded three-file PEAKS de novo only ALC80 cohort.");
        foreach (var input in project.manifest.DeNovoInputs)
        {
            string hash = Hash(input.FilePath);
            if (!hash.Equals(input.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("A recorded PEAKS input changed.");
            inputHashes.Add(Path.GetFullPath(input.FilePath), hash);
        }
        var sourceRows = files.SelectMany(path => PeaksCsvReader.Read(path, DeNovoSource.DenovoOnly)).ToList();
        var psms = SeproPsmAdapter.Load(seproPath);
        var operation = new OperationContext(progress: new FetchOperationProgress(value =>
        {
            if (value.Completed == null || value.Completed == value.Total || value.Completed % 1000 == 0)
                Console.WriteLine(value.Stage + ": " + value.Completed?.ToString(CultureInfo.InvariantCulture) + "/" + value.Total?.ToString(CultureInfo.InvariantCulture));
        }));
        var before = SpectrumComparisonTable.ApplyPatternLabConflictRule(sourceRows, psms, project.quant, false, operation);
        var after = SpectrumComparisonTable.ApplyPatternLabConflictRule(sourceRows, psms, project.quant, true, operation);
        var beforeFractions = NativeQuantFractions.Compute(before.Rows, project.manifest.PerRunTic).Where(f => !f.IsGrouped).OrderBy(f => f.Run, StringComparer.Ordinal).ToList();
        var afterFractions = NativeQuantFractions.Compute(after.Rows, project.manifest.PerRunTic).Where(f => !f.IsGrouped).ToDictionary(f => f.Run, StringComparer.OrdinalIgnoreCase);
        var mappingOptions = HomologyDefaults.ForNewCompute();
        if (mappingOptions.Science?.DecoyGate?.Enabled == true || mappingOptions.Science?.TagErrors?.Enabled == true)
            throw new InvalidOperationException("This fixed-mapping replay requires the current query-local mapping policy.");
        Console.WriteLine("Preparing the shared production family mapping");
        bool mappingCacheHit = false;
        var hits = CurationHomologyCache.GetOrSearch(before.Rows, project.fcf, options: mappingOptions, progress: hit => mappingCacheHit = hit, operation: operation);
        var mapping = FamilyEnvelopeQuant.AttributeHits(hits, project.fcf);
        FamilyRunSharesResult Shares(List<QuantRow> rows) => FamilyCompositionStudy.BuildRunShares(
            FamilyEnvelopeQuant.AttributePeptides(FamilyEnvelopeQuant.PreparePeptides(rows), mapping),
            project.manifest.PerRunTic.Keys, SharedPeptidePolicy.FamilyUnique, project.manifest.PerRunTic);
        var beforeShares = Shares(before.Rows); var afterShares = Shares(after.Rows);
        string[] runs = beforeFractions.Select(f => f.Run).ToArray();
        string RunLabel(string run) => "Run " + (Array.FindIndex(runs, r => r.Equals(run, StringComparison.OrdinalIgnoreCase)) + 1).ToString(CultureInfo.InvariantCulture);
        var totals = beforeFractions.Select(b =>
        {
            var a = afterFractions[b.Run];
            if (BitConverter.DoubleToInt64Bits(b.IdentifiedEnvelope) != BitConverter.DoubleToInt64Bits(a.IdentifiedEnvelope) || b.IdentifiedPeptides != a.IdentifiedPeptides)
                throw new InvalidDataException("The replay changed identified quantities.");
            return new
            {
                Run = RunLabel(b.Run), ExcludedDeNovoAnchors = after.ExcludedByRun.GetValueOrDefault(b.Run),
                BeforeDeNovoPeptides = b.DeNovoPeptides, AfterDeNovoPeptides = a.DeNovoPeptides,
                BeforeDeNovoShareOfTicPercent = b.Tic > 0 ? 100 * b.DeNovoEnvelopeReferenceCalibrated / b.Tic : (double?)null,
                AfterDeNovoShareOfTicPercent = a.Tic > 0 ? 100 * a.DeNovoEnvelopeReferenceCalibrated / a.Tic : (double?)null,
                BeforeCombinedShareOfTicPercent = b.Tic > 0 ? 100 * (b.IdentifiedEnvelope + b.DeNovoEnvelopeReferenceCalibrated) / b.Tic : (double?)null,
                AfterCombinedShareOfTicPercent = a.Tic > 0 ? 100 * (a.IdentifiedEnvelope + a.DeNovoEnvelopeReferenceCalibrated) / a.Tic : (double?)null,
                IdentifiedTotalsUnchanged = true,
            };
        }).ToList();
        var familyShares = beforeShares.Families.Select(f => f.FamilyUserId).Union(afterShares.Families.Select(f => f.FamilyUserId)).OrderBy(id => id)
            .SelectMany(id => runs.Select(run =>
            {
                var b = beforeShares.Families.FirstOrDefault(f => f.FamilyUserId == id)?.Runs.FirstOrDefault(r => r.Run.Equals(run, StringComparison.OrdinalIgnoreCase));
                var a = afterShares.Families.FirstOrDefault(f => f.FamilyUserId == id)?.Runs.FirstOrDefault(r => r.Run.Equals(run, StringComparison.OrdinalIgnoreCase));
                var br = beforeShares.Runs.Single(r => r.Run.Equals(run, StringComparison.OrdinalIgnoreCase));
                var ar = afterShares.Runs.Single(r => r.Run.Equals(run, StringComparison.OrdinalIgnoreCase));
                double? bs = b?.ShareOfAssignedSignal ?? (br.AssignedSignal > 0 ? 0d : (double?)null);
                double? @as = a?.ShareOfAssignedSignal ?? (ar.AssignedSignal > 0 ? 0d : (double?)null);
                return new
                {
                    FamilyUserId = id, Run = RunLabel(run), BeforeAssignedSharePercent = 100 * bs, AfterAssignedSharePercent = 100 * @as,
                    AssignedChangePercentagePoints = 100 * (@as - bs), BeforeMeasuredSharePercent = 100 * (b?.ShareOfMeasuredSignal ?? (br.MeasuredSignal > 0 ? 0d : (double?)null)),
                    AfterMeasuredSharePercent = 100 * (a?.ShareOfMeasuredSignal ?? (ar.MeasuredSignal > 0 ? 0d : (double?)null)),
                    BeforeTicSharePercent = 100 * (b?.ShareOfTic ?? (br.TotalIonCurrent > 0 ? 0d : (double?)null)),
                    AfterTicSharePercent = 100 * (a?.ShareOfTic ?? (ar.TotalIonCurrent > 0 ? 0d : (double?)null)),
                };
            })).ToList();
        var pooledShares = beforeShares.Families.Select(f => f.FamilyUserId).Union(afterShares.Families.Select(f => f.FamilyUserId)).OrderBy(id => id)
            .Select(id =>
            {
                double? b = beforeShares.Families.FirstOrDefault(f => f.FamilyUserId == id)?.PooledShare
                    ?? (beforeShares.Families.Any(f => f.PooledShare.HasValue) ? 0d : (double?)null);
                double? a = afterShares.Families.FirstOrDefault(f => f.FamilyUserId == id)?.PooledShare
                    ?? (afterShares.Families.Any(f => f.PooledShare.HasValue) ? 0d : (double?)null);
                return new { FamilyUserId = id, BeforePooledAssignedSharePercent = 100 * b, AfterPooledAssignedSharePercent = 100 * a, ChangePercentagePoints = 100 * (a - b) };
            }).ToList();
        if (inputHashes.Any(p => Hash(p.Key) != p.Value)) throw new InvalidDataException("A read-only replay input changed.");
        var json = new JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(Path.Combine(output, "before-after-totals.json"), JsonSerializer.Serialize(totals, json));
        File.WriteAllText(Path.Combine(output, "before-after-family-shares.json"), JsonSerializer.Serialize(familyShares, json));
        File.WriteAllText(Path.Combine(output, "before-after-pooled-family-shares.json"), JsonSerializer.Serialize(pooledShares, json));
        File.WriteAllText(Path.Combine(output, "method.json"), JsonSerializer.Serialize(new
        {
            Timestamp = args[5], ProjectSha256 = inputHashes[projectPath], SeproSha256 = inputHashes[seproPath], SourceSha256 = project.manifest.DeNovoInputs.Select(i => i.Sha256).ToArray(),
            CoreAssemblySha256 = Hash(typeof(SpectrumComparisonTable).Assembly.Location), TestAssemblySha256 = Hash(typeof(Program).Assembly.Location),
            InputsUnchanged = true, FreshRawExtraction = false, MappingCacheHit = mappingCacheHit, MappingPolicy = mappingOptions,
            FamilyPolicy = SharedPeptidePolicy.FamilyUnique.ToString(), BeforePatternLabWinsScanConflicts = false, AfterPatternLabWinsScanConflicts = true,
            Method = "Production Compute completion rule replayed over unchanged measured anchor rows. Native Spectrum comparison claim builder, non-chaining isotope-aware clusters and top PatternLab ranking. Conflicting anchors removed before production peptide collapse, reference calibration and family-share aggregation; a nonconflicting anchor can retain the peptide. One shared query-local current Compute family mapping, 80% identity with I/L equivalence, reused for both sides; no decoy identity gate or tag-error rewriting in this profile. Family rows are aggregate IDs only.",
        }, json));
        string Quote(string text) => "'" + text.Replace("'", "''", StringComparison.Ordinal) + "'";
        File.WriteAllText(Path.Combine(output, "command.ps1"), "& " + Quote(Environment.ProcessPath!) + " " + string.Join(" ", args.Select(Quote)) + Environment.NewLine);
        var report = new StringBuilder("# PatternLab priority before and after\n\n");
        report.AppendLine("Native replay of the same stored measured anchors. Before: option OFF. After: option ON. No RAW extraction. Inputs remained unchanged. Current Compute family mapping and the family-unique policy are held constant.");
        report.AppendLine("\n| Run | Excluded de novo anchors | De novo peptides before | After | De novo TIC share before | After | Combined TIC share before | After |\n|---|---:|---:|---:|---:|---:|---:|---:|");
        static string Number(double? value) => value?.ToString("G10", CultureInfo.InvariantCulture) ?? "unavailable";
        foreach (var row in totals) report.AppendLine("| " + row.Run + " | " + row.ExcludedDeNovoAnchors.ToString(CultureInfo.InvariantCulture) + " | "
            + row.BeforeDeNovoPeptides.ToString(CultureInfo.InvariantCulture) + " | " + row.AfterDeNovoPeptides.ToString(CultureInfo.InvariantCulture) + " | "
            + Number(row.BeforeDeNovoShareOfTicPercent) + "% | " + Number(row.AfterDeNovoShareOfTicPercent) + "% | "
            + Number(row.BeforeCombinedShareOfTicPercent) + "% | " + Number(row.AfterCombinedShareOfTicPercent) + "% |");
        report.AppendLine("\nLargest pooled family-share changes (ranked by absolute change):\n\n| Family ID | Pooled assigned share before | After | Change (percentage points) |\n|---|---:|---:|---:|");
        foreach (var row in pooledShares.OrderByDescending(r => Math.Abs(r.ChangePercentagePoints ?? 0)).ThenBy(r => r.FamilyUserId).Take(10))
            report.AppendLine("| " + row.FamilyUserId.ToString(CultureInfo.InvariantCulture) + " | " + Number(row.BeforePooledAssignedSharePercent)
                + "% | " + Number(row.AfterPooledAssignedSharePercent) + "% | " + Number(row.ChangePercentagePoints) + " |");
        report.AppendLine("\n| Family ID | Run | Assigned share before | After | Change (percentage points) | TIC share before | After |\n|---|---|---:|---:|---:|---:|---:|");
        foreach (var row in familyShares) report.AppendLine("| " + row.FamilyUserId.ToString(CultureInfo.InvariantCulture) + " | " + row.Run + " | "
            + Number(row.BeforeAssignedSharePercent) + "% | " + Number(row.AfterAssignedSharePercent) + "% | " + Number(row.AssignedChangePercentagePoints) + " | "
            + Number(row.BeforeTicSharePercent) + "% | " + Number(row.AfterTicSharePercent) + "% |");
        report.AppendLine("\nIdentified totals are unchanged in every run. Assigned shares divide by credited family signal under the family-unique policy; TIC shares divide by recorded per-run measured TIC. Only counts and aggregate shares are exported. See command.ps1 and method.json for the exact invocation and provenance.");
        File.WriteAllText(Path.Combine(output, "report.md"), report.ToString());
        foreach (var row in totals) Console.WriteLine(JsonSerializer.Serialize(row));
        Console.WriteLine("Aggregate family rows: " + familyShares.Count.ToString(CultureInfo.InvariantCulture));
        return 0;
    }
}
