using FamilyQuant.Evidence;
using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Security.Cryptography;
using System.IO;
using System.Threading.Tasks;

namespace FamilyQuant;

public sealed partial class EvidenceGroupingSession
{
    private static readonly JsonSerializerOptions withoutReferencePanel = WithoutReferencePanelOptions();
    private static JsonSerializerOptions WithoutReferencePanelOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(info =>
        {
            if (info.Type != typeof(EvidenceProvenance)) return;
            var panel = info.Properties.Single(property => property.Name == nameof(EvidenceProvenance.ReferencePanel));
            panel.ShouldSerialize = (_, _) => false;
        });
        return new() { TypeInfoResolver = resolver };
    }
    private static string MetadataFingerprint(EvidenceProvenance? source, bool omitPanel = false)
    {
        if (!omitPanel) return PanelEvidenceMatching.FingerprintSource(source);
        using var hash = SHA256.Create(); using var stream = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
        JsonSerializer.Serialize(stream, omitPanel ? source ?? new() : source, omitPanel ? withoutReferencePanel : null);
        stream.FlushFinalBlock(); return Convert.ToHexString(hash.Hash!);
    }
    public bool PanelScopeCovered(string scope)
    {
        try { ReferencePanelFetcher.RequireScope(file.EvidenceSource?.ReferencePanel, WordCluster.ParseKeywords(scope)); return true; }
        catch (InvalidDataException) { return false; }
    }
    public string FullPanelRequirement() => file.EvidenceSource?.ReferencePanel?.Coverage != null
        && ReferencePanelUseRequirements.HasSubclasses(selectedReviewPanel ?? reviewPanel ?? file.FamilyPanel)
        ? ReferencePanelUseRequirements.SubclassApply : "";
    public string PanelCoverageStatus(string scope)
    {
        if (file.EvidenceSource?.ReferencePanel == null) return "No stored reference panel.";
        try { ReferencePanelFetcher.RequireScope(file.EvidenceSource?.ReferencePanel, WordCluster.ParseKeywords(scope)); }
        catch (System.IO.InvalidDataException error) { return error.Message; }
        return file.EvidenceSource?.ReferencePanel?.Coverage == null ? "Full reference panel stored." : "Reference panel covers this scope.";
    }

    public async Task RefreshPanelAsync(Func<Task> fetch)
    {
        EnsureCurrent();
        string prior = WithoutPanel();
        await fetch();
        if (EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems) != sourceFingerprint
            || JsonSerializer.Serialize(file.FamilyPanel) != sourcePanel || !sourceLocks.SetEquals(file.LockedUserIds) || prior != WithoutPanel())
            throw new InvalidOperationException("The source changed while the panel was fetched. Reopen the grouping review.");
        sourceMetadata = MetadataFingerprint(file.EvidenceSource);
        Proposal = null; Search = null; References = null; automaticProposal = null; coherenceProfiles = null;
        checkedMotif = null;
        string WithoutPanel()
        {
            return MetadataFingerprint(file.EvidenceSource, omitPanel: true);
        }
    }
}
