using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant;

public sealed record ShellProjectContent(FamilyClusterFile Curation, List<QuantRow> Rows, QuantManifest? Manifest)
{
    /// <summary>Digest of the exact chosen family-file bytes parsed below; outside curation so Open preserves it.</summary>
    public string DatabaseFileSha256 { get; init; } = string.Empty;
}
public sealed record ShellReannotationResult(int Updated, IReadOnlyList<string> Missing);

/// <summary>Headless document operations used by shell commands. Existing numerical services retain their rules.</summary>
public static class ShellProjectOperations
{
    public static string WriteVerifiedProject(string path, FamilyClusterFile curation, List<QuantRow> rows, QuantManifest manifest,
        Action<string>? validateStaged = null, OperationContext? operation = null)
    {
        operation?.Report("verified-save", "Preparing the project copy");
        if (!ProjectStore.IsProjectPath(path))
            throw new ArgumentException("Save projects with a .fqproj filename. Use Export for a legacy Family Cluster File.", nameof(path));
        string full = Path.GetFullPath(path);
        string staged = full + ".saving-" + Guid.NewGuid().ToString("N") + ".fqproj";
        string expected = curation.JsonContentSha256();
        var snapshot = Evidence.SavedFileCheck.Capture(curation);
        var previousCheck = curation.LastSaveCheck;
        int previousVersion = manifest.FormatVersion;
        int? previousCount = manifest.SpectrumComparisonRowCount;
        bool published = false;
        try
        {
            // This is intermediate output. Only the final rename below commits the user's file.
            operation?.ThrowIfCancellationRequested();
            ProjectStore.WriteFqproj(staged, curation, rows, manifest);
            operation?.Report("verified-save", "Checking the prepared project");
            validateStaged?.Invoke(staged);
            curation.LastSaveCheck = Evidence.SavedFileCheck.Verify(snapshot, staged, project: true, operation);
            Evidence.SavedFileCheck.RequirePassed(curation.LastSaveCheck);
            var reopened = ProjectStore.ReadFqproj(staged, operation);
            string actual = reopened.fcf.JsonContentSha256();
            if (!string.Equals(expected, actual, StringComparison.Ordinal) || reopened.quant.Count != rows.Count)
                throw new InvalidDataException("The saved project did not reproduce its curation and stored row count.");
            operation?.Report("verified-save", "Publishing the checked project", 1, 1);
            void Publish() { File.Move(staged, full, true); published = true; }
            if (operation == null) Publish();
            else operation.Commit(Publish);
            return curation.LastSaveCheck.Message;
        }
        finally
        {
            if (!published)
            {
                curation.LastSaveCheck = previousCheck;
                manifest.FormatVersion = previousVersion;
                manifest.SpectrumComparisonRowCount = previousCount;
            }
            foreach (string temporary in new[] { staged, staged + ".tmp" })
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }


    public static ShellProjectContent Read(string path, OperationContext? operation = null)
    {
        if (!ProjectStore.IsProjectPath(path))
        {
            operation?.Report("document-read", "Opening family document", 0, 1);
            // Keep the source locked while parsing and hashing, so the provenance hash still
            // describes the exact bytes read, including a legacy BOM or encoding.
            using var held = File.OpenRead(path);
            try
            {
                var file = FamilyClusterFile.ReadJsonUtf8(() => File.OpenRead(path));
                operation?.ThrowIfCancellationRequested();
                string sourceSha256 = Convert.ToHexString(SHA256.HashData(held)).ToLowerInvariant();
                operation?.ThrowIfCancellationRequested();
                operation?.Report("document-read", "Family document read", 1, 1);
                return new(file, new(), null) { DatabaseFileSha256 = sourceSha256 };
            }
            catch (InvalidDataException) { throw new InvalidDataException("\"" + path + "\" is not a valid Family Cluster File."); }
        }
        var (curation, rows, manifest) = ProjectStore.ReadFqproj(path, operation);
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
