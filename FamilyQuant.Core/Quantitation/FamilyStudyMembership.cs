using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FamilyQuant;

public interface IFamilyMembershipStudy
{
    string? MembershipSha256 { get; set; }
}

/// <summary>Binds a saved UserID study to the actual memberships, independently of the protein-set
/// fingerprint which deliberately permits regrouping without re-extracting peptide signal.</summary>
public static class FamilyStudyMembership
{
    public const string ChangedMessage = "The saved family results do not match the current family membership. Run Quantify again to refresh them.";
    public const string UnverifiedMessage = "The saved family results do not record their family membership. Run Quantify again to refresh them.";
    public static string Fingerprint(FamilyClusterFile curation)
    {
        ArgumentNullException.ThrowIfNull(curation);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes("FamilyQuant membership v1\n"));
        foreach (var protein in (curation.MyExtendedFastaItems ?? new()).Where(p => p != null)
            .OrderBy(p => p.SequenceIdentifier, StringComparer.Ordinal).ThenBy(p => p.Sequence, StringComparer.Ordinal).ThenBy(p => p.UserID))
        {
            hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new[] { protein.SequenceIdentifier ?? string.Empty,
                protein.Sequence ?? string.Empty, protein.UserID.ToString(CultureInfo.InvariantCulture) }));
            hash.AppendData(new byte[] { 10 });
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static IEnumerable<(string Name, IFamilyMembershipStudy Study)> Studies(QuantManifest manifest)
    {
        if (manifest.FamilyRunShares != null) yield return ("Per-run family shares", manifest.FamilyRunShares);
        if (manifest.FamilyProteinCounts != null) yield return ("Protein explanation counts", manifest.FamilyProteinCounts);
        if (manifest.FamilySubclassParts != null) yield return ("Subclass parts", manifest.FamilySubclassParts);
        if (manifest.FamilyDomainParts != null) yield return ("Domain and product parts", manifest.FamilyDomainParts);
        if (manifest.FamilyPeptideLocalization != null) yield return ("Peptide localization", manifest.FamilyPeptideLocalization);
        if (manifest.EffectivePanelFamilies != null) yield return ("Effective panel families", manifest.EffectivePanelFamilies);
    }

    public static string? Problem(FamilyClusterFile? curation, QuantManifest? manifest)
    {
        if (manifest == null) return null;
        var studies = Studies(manifest).ToArray();
        if (studies.Length == 0 && manifest.FamilySensitivity == null) return null;
        if (curation == null) return "Open the family database to verify the saved family results before using them.";
        string current = Fingerprint(curation);
        foreach (var item in studies)
        {
            if (string.IsNullOrWhiteSpace(item.Study.MembershipSha256)) return item.Name + ": " + UnverifiedMessage;
            if (!string.Equals(current, item.Study.MembershipSha256, StringComparison.OrdinalIgnoreCase)) return item.Name + ": " + ChangedMessage;
        }
        if (manifest.EffectivePanelFamilies is { } effective && EffectivePanelFamilyStudy.Problem(curation, effective) is string effectiveProblem)
            return effectiveProblem;
        if (manifest.FamilySensitivity is { } sensitivity)
        {
            if (string.IsNullOrWhiteSpace(sensitivity.CurationFingerprint)) return "Sensitivity: " + UnverifiedMessage;
            try
            {
                // Sensitivity already records a stronger grouping/label fingerprint. Retain that
                // protocol, including legacy nested run shares whose enclosing study is verified.
                if (sensitivity.CurationFingerprint != FamilySensitivityStudy.CurationFingerprint(curation)) return "Sensitivity: " + ChangedMessage;
            }
            catch (InvalidDataException) { return "Sensitivity: " + ChangedMessage; }
            foreach (var shares in sensitivity.Variants.SelectMany(v => v.Policies).Select(p => p.PerRunShares).Where(p => p != null))
                if (!string.IsNullOrEmpty(shares!.MembershipSha256)
                    && !string.Equals(shares.MembershipSha256, current, StringComparison.OrdinalIgnoreCase)) return "Sensitivity run shares: " + ChangedMessage;
        }
        return null;
    }

    public static void RequireCurrent(FamilyClusterFile? curation, QuantManifest? manifest)
    {
        if (Problem(curation, manifest) is string message) throw new InvalidOperationException(message);
    }

    /// <summary>Standalone drawing can use explicit legacy data without claiming a current project.
    /// Once one component carries membership provenance, all joined components must agree.</summary>
    public static void RequireCompatible(params IFamilyMembershipStudy?[] studies)
    {
        var present = studies.Where(s => s != null).Select(s => s!).ToArray();
        if (present.Length < 2 || present.All(s => string.IsNullOrWhiteSpace(s.MembershipSha256))) return;
        if (present.Any(s => string.IsNullOrWhiteSpace(s.MembershipSha256))) throw new InvalidOperationException(UnverifiedMessage);
        if (present.Select(s => s.MembershipSha256!).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            throw new InvalidOperationException(ChangedMessage);
    }
}
