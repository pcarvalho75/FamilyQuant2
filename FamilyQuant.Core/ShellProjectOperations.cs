using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

public sealed record ShellProjectContent(FamilyClusterFile Curation, List<QuantRow> Rows, QuantManifest? Manifest);
public sealed record ShellReannotationResult(int Updated, IReadOnlyList<string> Missing);

/// <summary>Headless document operations used by shell commands. Existing numerical services retain their rules.</summary>
public static class ShellProjectOperations
{
    public static ShellProjectContent Read(string path)
    {
        if (!ProjectStore.IsProjectPath(path)) return new(FamilyClusterFile.Load(path), new(), null);
        var (curation, rows, manifest) = ProjectStore.ReadFqproj(path);
        return new(curation, rows, manifest);
    }

    public static QuantManifest CreateManifest(string appVersion) => new()
    {
        FormatVersion = ProjectStore.CurrentFormatVersion,
        IsotopeCount = DeNovoXicExtractor.DefaultEnvelopeIsotopeCount,
        MinPointsForQuant = DeNovoXicExtractor.DefaultMinPointsForQuant,
        Ppm = DeNovoXicExtractor.ProductionPpm,
        Ms1Tolerance = DeNovoXicExtractor.ProductionMs1Tolerance,
        AppVersion = appVersion,
        CreatedUtc = DateTime.UtcNow,
        Provenance = QuantManifest.DefaultProvenance()
    };

    public static string? RefreshSpectrumFamilies(List<SpectrumComparisonRow> rows, QuantManifest manifest,
        FamilyClusterFile curation, List<QuantRow> quant, Func<PeptideFamilyAttribution?> cachedAttribution,
        string? proteinFingerprint) => FamilyEnvelopeQuant.RefreshSpectrumFamilies(rows, manifest.FamilyMapping, curation,
        identity =>
        {
            PeptideFamilyAttribution? cached = cachedAttribution();
            return cached != null && cached.IdentityThreshold == identity ? cached
                : FamilyEnvelopeQuant.BuildAttributionFromCuration(quant, curation,
                    new Homology.HomologyOptions { MinIdentity = identity });
        }, proteinFingerprint);

    public static (IReadOnlyDictionary<string, int> Families, IReadOnlyList<string> Missing) PlanReannotation(
        FamilyClusterFile curation, IEnumerable<string> packageLoci)
    {
        var families = curation.MyExtendedFastaItems.GroupBy(item => item.SequenceIdentifier)
            .ToDictionary(group => group.Key, group => group.First().UserID);
        var missing = packageLoci.Where(locus => !families.ContainsKey(locus)).Distinct().ToList();
        return (families, missing);
    }

    public static ShellReannotationResult Reannotate(FamilyClusterFile curation, SeproPckg2.ResultPackage package)
    {
        var plan = PlanReannotation(curation, package.MyProteins.MyProteinList.Select(protein => protein.Locus));
        if (plan.Missing.Count > 0) return new(0, plan.Missing);
        foreach (var protein in package.MyProteins.MyProteinList) protein.GroupNo = plan.Families[protein.Locus];
        return new(package.MyProteins.MyProteinList.Count, plan.Missing);
    }
}
