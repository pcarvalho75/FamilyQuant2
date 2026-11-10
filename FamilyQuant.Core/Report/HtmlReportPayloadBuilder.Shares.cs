using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class HtmlReportPayloadBuilder
{
    /// <summary>Resolves report share geometry from stored quantities only. Mean and sample SD use
    /// complete per-run shares; unavailable denominators never become zero or a different basis.</summary>
    public static double? ReadFamilyShare(HtmlReportReference source, QuantManifest manifest)
    {
        if (source.Kind != "family-basis" || source.Family == null || source.Property == null)
            throw new ArgumentException("A family-basis source is required.", nameof(source));
        if (source.Study is not ("source" or "effective")) throw new InvalidDataException("Unknown family share study.");
        var study = source.Study == "effective" ? manifest.EffectivePanelFamilies?.RunShares : manifest.FamilyRunShares;
        if (study == null || study.Policy is not (SharedPeptidePolicy.FamilyUnique or SharedPeptidePolicy.SplitEqually)) return null;
        FamilyRunShareSummary? family;
        if (source.Family.StartsWith("others:", StringComparison.Ordinal))
        {
            int[] ids = source.Family[7..].Length == 0 ? Array.Empty<int>()
                : source.Family[7..].Split(',').Select(id => int.Parse(id, CultureInfo.InvariantCulture)).ToArray();
            if (ids.Distinct().Count() != ids.Length) throw new InvalidDataException("Duplicate canonical Others member.");
            var members = ids.Select(id => study.Families.SingleOrDefault(f => f.FamilyUserId == id)).ToArray();
            if (members.Any(f => f == null)) return null;
            family = new();
            foreach (var total in study.Runs)
            {
                var rows = members.Select(f => f!.Runs.SingleOrDefault(r => string.Equals(DeNovoReconciler.NormalizeRunName(r.Run),
                    DeNovoReconciler.NormalizeRunName(total.Run), StringComparison.OrdinalIgnoreCase))).ToArray();
                if (rows.Any(r => r == null)) return null;
                family.Runs.Add(new()
                {
                    Run = total.Run, Signal = rows.Sum(r => r!.Signal),
                    IdentifiedSignal = rows.All(r => r!.IdentifiedSignal.HasValue) ? rows.Sum(r => r!.IdentifiedSignal!.Value) : null,
                    ShareOfMeasuredSignal = rows.All(r => r!.ShareOfMeasuredSignal.HasValue) ? rows.Sum(r => r!.ShareOfMeasuredSignal!.Value) : null,
                    ShareOfTic = rows.All(r => r!.ShareOfTic.HasValue) ? rows.Sum(r => r!.ShareOfTic!.Value) : null,
                });
            }
        }
        else family = study.Families.SingleOrDefault(f => f.FamilyUserId.ToString(CultureInfo.InvariantCulture) == source.Family);
        if (family == null) return null;
        string[] parts = source.Property.Split('/');
        if (parts[0] is not ("IdentifiedOnly" or "IdentifiedAndDeNovo" or "DetectorEnvelopes" or "Tic"))
            throw new InvalidDataException("Unknown family share basis.");
        if (source.Run != null && parts.Length != 1) throw new InvalidDataException("A run share cannot name a summary statistic.");
        bool identifiedPart = parts.Length == 2 && parts[1] == "identified-mean";
        bool deNovoPart = parts.Length == 2 && parts[1] == "denovo-mean";
        bool component = identifiedPart || deNovoPart;
        double? Share(FamilyRunShare run)
        {
            string name = DeNovoReconciler.NormalizeRunName(run.Run);
            var total = study.Runs.SingleOrDefault(r => string.Equals(DeNovoReconciler.NormalizeRunName(r.Run), name, StringComparison.OrdinalIgnoreCase));
            if (component && !run.IdentifiedSignal.HasValue) return null;
            double signal = identifiedPart ? run.IdentifiedSignal!.Value
                : deNovoPart ? run.Signal - run.IdentifiedSignal!.Value : run.Signal;
            double? value = parts[0] switch
            {
                "IdentifiedOnly" => run.IdentifiedSignal.HasValue && total?.IdentifiedSignal > 0
                    ? deNovoPart ? 0 : run.IdentifiedSignal / total.IdentifiedSignal : null,
                "IdentifiedAndDeNovo" => (!component ? run.ShareOfMeasuredSignal : null)
                    ?? (total?.MeasuredSignal > 0 ? signal / total.MeasuredSignal : null),
                "Tic" => (!component ? run.ShareOfTic : null)
                    ?? (total?.TotalIonCurrent > 0 ? signal / total.TotalIonCurrent : null),
                "DetectorEnvelopes" => DetectorShare(signal, name),
                _ => throw new InvalidDataException("Unknown family share basis."),
            };
            return value.HasValue && double.IsFinite(value.Value) ? value : null;
        }
        double? DetectorShare(double signal, string run)
        {
            var detected = manifest.DerivedFractions.SingleOrDefault(d => !d.Grouped
                && string.Equals(DeNovoReconciler.NormalizeRunName(d.Run), run, StringComparison.OrdinalIgnoreCase));
            return detected is { DetectedPeptideLikeArea: > 0 } && double.IsFinite(detected.DetectedPeptideLikeArea)
                ? signal / detected.DetectedPeptideLikeArea : null;
        }
        if (source.Run != null)
        {
            var run = family.Runs.SingleOrDefault(r => string.Equals(DeNovoReconciler.NormalizeRunName(r.Run),
                DeNovoReconciler.NormalizeRunName(source.Run), StringComparison.OrdinalIgnoreCase));
            return run == null ? null : Share(run);
        }
        if (parts.Length != 2 || parts[1] is not ("mean" or "sd" or "cv" or "identified-mean" or "denovo-mean"))
            throw new InvalidDataException("A family share statistic is required.");
        // Existing CV is for assigned-family composition, a different denominator from these bases.
        if (parts[1] == "cv") return null;
        if (family.Runs.Count == 0 || family.Runs.Count != study.Runs.Count) return null;
        var names = new HashSet<string>(study.Runs.Select(r => DeNovoReconciler.NormalizeRunName(r.Run)), StringComparer.OrdinalIgnoreCase);
        if (names.Count != study.Runs.Count) throw new InvalidDataException("Duplicate stored run name.");
        double mean = 0, m2 = 0;
        int count = 0;
        foreach (var run in family.Runs)
        {
            if (!names.Remove(DeNovoReconciler.NormalizeRunName(run.Run))) return null;
            double? value = Share(run);
            if (!value.HasValue) return null;
            count++;
            double delta = value.Value - mean;
            mean += delta / count;
            m2 += delta * (value.Value - mean);
        }
        return parts[1] == "sd" ? count > 1 ? Math.Sqrt(Math.Max(0, m2 / (count - 1))) : null : mean;
    }

    private sealed partial class Builder
    {
        private HtmlReportFamily OtherFamilyShares(HtmlReportFamily family)
        {
            var study = context.StoredStudies?.FamilyRunShares;
            if (study == null || context.SourceGrouping == null) return family;
            var sourceIds = context.SourceGrouping.OtherFamilies.Select(f => f.FamilyUserId).ToHashSet();
            var ids = study.Families.Where(f => context.UsesPanel && !context.IsIdentity
                ? context.SourceUserIds.TryGetValue(f.FamilyUserId, out var members) && members.Count > 0 && members.All(sourceIds.Contains)
                : sourceIds.Contains(f.FamilyUserId)).Select(f => f.FamilyUserId).Order().ToArray();
            var represented = context.UsesPanel && !context.IsIdentity
                ? ids.SelectMany(id => context.SourceUserIds[id]).ToHashSet() : ids.ToHashSet();
            if (!sourceIds.SetEquals(represented)) throw new InvalidDataException("Canonical Others membership does not match the stored family study.");
            string key = "others:" + string.Join(",", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            var runs = new System.Collections.Generic.List<HtmlFamilyRun>();
            if (!excluded.Contains("family-run-shares"))
                for (int i = 0; i < study.Runs.Count; i++)
                {
                    string run = study.Runs[i].Run;
                    if (!runNameFields.TryGetValue(run, out string? name))
                    {
                        name = Add(run, new() { Kind = "stored-value", Property = "/manifest/" + (context.UsesPanel && !context.IsIdentity
                            ? "EffectivePanelFamilies/RunShares" : "FamilyRunShares") + "/Runs/" + i.ToString(CultureInfo.InvariantCulture) + "/Run" });
                        runNameFields.Add(run, name);
                    }
                    string? measured = FamilyShareField(key, "IdentifiedAndDeNovo", run);
                    runs.Add(new(name, null, measured)
                    {
                        IdentifiedOnly = FamilyShareField(key, "IdentifiedOnly", run), IdentifiedAndDeNovo = measured,
                        DetectorEnvelopes = FamilyShareField(key, "DetectorEnvelopes", run), Tic = FamilyShareField(key, "Tic", run),
                    });
                }
            return family with
            {
                Runs = runs, IdentifiedOnly = FamilyShareStatistics(key, "IdentifiedOnly"),
                IdentifiedOnlyParts = FamilyShareMeanParts(key, "IdentifiedOnly"),
                IdentifiedAndDeNovo = FamilyShareStatistics(key, "IdentifiedAndDeNovo"),
                IdentifiedAndDeNovoParts = FamilyShareMeanParts(key, "IdentifiedAndDeNovo"),
                DetectorEnvelopes = FamilyShareStatistics(key, "DetectorEnvelopes"),
                DetectorEnvelopesParts = FamilyShareMeanParts(key, "DetectorEnvelopes"),
                Tic = FamilyShareStatistics(key, "Tic"), TicParts = FamilyShareMeanParts(key, "Tic"),
            };
        }

        private string? FamilyShareField(string family, string basis, string? run = null, string? statistic = null)
        {
            if (context.StoredStudies == null) return null;
            var source = new HtmlReportReference { Kind = "family-basis", Family = family, Run = run,
                Study = context.UsesPanel && !context.IsIdentity ? "effective" : "source",
                Property = basis + (statistic == null ? "" : "/" + statistic), Format = "G6", Scale = 100 };
            double? value = ReadFamilyShare(source, context.StoredStudies) * 100;
            if (!value.HasValue) return null;
            string id = Add(value.Value.ToString("G6", CultureInfo.InvariantCulture), source,
                statistic == "sd" ? "pp" : "%", value);
            AddShort(id, value.Value, source);
            return id;
        }

        private HtmlFamilyShareStatistics FamilyShareStatistics(string family, string basis) => new(
            FamilyShareField(family, basis, statistic: "mean"),
            FamilyShareField(family, basis, statistic: "sd"),
            FamilyShareField(family, basis, statistic: "cv"));

        private HtmlFamilyMeanParts? FamilyShareMeanParts(string family, string basis)
        {
            if (context.StoredStudies == null) return null;
            var identifiedSource = new HtmlReportReference { Kind = "family-basis", Family = family,
                Study = context.UsesPanel && !context.IsIdentity ? "effective" : "source",
                Property = basis + "/identified-mean", Format = "G6", Scale = 100 };
            var deNovoSource = identifiedSource with { Property = basis + "/denovo-mean" };
            double? identified = ReadFamilyShare(identifiedSource, context.StoredStudies) * 100;
            double? deNovo = ReadFamilyShare(deNovoSource, context.StoredStudies) * 100;
            if (!identified.HasValue || !deNovo.HasValue) return null;
            string identifiedId = Add(identified.Value.ToString("G6", CultureInfo.InvariantCulture), identifiedSource, "%", identified);
            string deNovoId = Add(deNovo.Value.ToString("G6", CultureInfo.InvariantCulture), deNovoSource, "%", deNovo);
            AddShort(identifiedId, identified.Value, identifiedSource);
            AddShort(deNovoId, deNovo.Value, deNovoSource);
            return new(identifiedId, deNovoId);
        }
    }
}
