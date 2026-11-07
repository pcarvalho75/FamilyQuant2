using System;

namespace FamilyQuant;

/// <summary>Presentation only; preserves the classifier's original detail and selection.</summary>
internal static class EvidenceWarningText
{
    internal static (string Label, string Help) Describe(string flag)
    {
        int separator = flag.IndexOf(':');
        string code = (separator < 0 ? flag : flag[..separator]).Trim();
        var (label, check) = code switch
        {
            "conflicting_markers" => ("Conflicting markers", "Check the learned domains supporting different families."),
            "domain_compatibility" => ("Domain compatibility", "Check whether observed domains support the reviewed relative's family."),
            "multiple_source_families" => ("Several source families", "Check the different source family memberships before assigning a family."),
            "neighbour_out_of_scope" => ("Neighbour out of scope", "Check the scope words on both the protein and its reviewed relative."),
            "neighbour_refused" => ("Neighbour refused", "Check the recorded reason why the reviewed relative could not be used."),
            "panel_domain_conflict" => ("Panel domain conflict", "Check the accompanying domains against the proposed panel family."),
            "panel_link_unsupported" => ("Panel link unsupported", "Check whether reviewed evidence connects this source context to a panel family."),
            "panel_link_ambiguous" => ("Panel link ambiguous", "Check the candidate panel families and confirm a link in Panel links."),
            "panel_link_conflict" => ("Panel link conflict", "Check the source memberships that link to different panel families."),
            "coherence.length.above" => ("Longer than reviewed members", "Check the sequence length against the reviewed family range."),
            "coherence.length.below" => ("Shorter than reviewed members", "Check for a fragment or a sequence shorter than the complete reviewed family range."),
            "coherence.cysteines.above" => ("More cysteines than reviewed members", "Check the cysteine count against the reviewed family range."),
            "coherence.cysteines.below" => ("Fewer cysteines than reviewed members", "Check for a fragment or a cysteine count below the complete reviewed family range."),
            "coherence.transmembrane" => ("Transmembrane region", "Check the transmembrane regions that differ from the reviewed family profile."),
            "coherence.domain" => ("Domain outside family profile", "Check the domain absent from the reviewed family profile."),
            _ => (code.Replace('_', ' ').Replace('.', ' '), "Check the recorded evidence detail before including this protein.")
        };
        string detail = separator < 0 ? "" : flag[(separator + 1)..].Trim();
        return (label, label + ": " + check + (detail.Length == 0 ? "" : Environment.NewLine + detail));
    }
}
