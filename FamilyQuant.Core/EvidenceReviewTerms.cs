namespace FamilyQuant;

/// <summary>Plain words for the review's supplied facts, shared by both evidence modes.</summary>
public static class EvidenceReviewTerms
{
    public const string Text = "READING THE FAMILY PROPOSAL\n"
        + "Ticks: ticked proteins join the named family on Apply grouping; unticked proteins keep their current family and are still quantified.\n"
        + "Red italic: the protein carries a warning flag; read its recorded details before deciding whether to tick it.\n"
        + "Add to family assigns highlighted rows to a proposed family or a new name by explicit user choice; Added by hand and your note distinguish it from automatic evidence. Update proposal keeps the choice; Remove from family restores the automatic proposal, and locked proteins stay protected. Apply grouping and Undo use the normal transaction; File > Save keeps the recipe.\n"
        + "Coherence ranges combine reviewed members in your file and the reference panel from all species, each accession once with the file copy first. Each warning gives the relevant source counts; complete here means not marked as a fragment.\n"
        + "Length and cysteine ranges use complete members; helix limits use complete members with recorded counts. Domain warnings compare all reviewed members and separately show how many complete members had domain calls.\n"
        + "Selected protein details and coherence warning tooltips list complete profile members by source and species; Show members opens their accessions, species, lengths and cysteine counts. Stored UniProt organism metadata is used, with OS/OX fallback for older file entries; missing panel species stay not recorded and no data are downloaded during review.\n"
        + "Total includes unticked and locked members; Reviewed is the subset marked reviewed in stored UniProt evidence.\n"
        + "Evidence source says how the family was supported; Reason explains the decision for this protein.\n"
        + "Reviewed relative means another reviewed protein compared by sequence; identity measures similarity and coverage says how much of this protein was compared.\n"
        + "Scope words found means the keyword or tissue annotations match your words, not the protein name.\n"
        + "A domain group contains a particular combination of domain calls; it does not specify their order in the sequence.\n"
        + "Placed by size means length and cysteine count placed a member without a Pfam call; a missing call is not proof that a domain is absent.\n"
        + "Not placed in a domain group still belongs to the proposed family; Unassigned has no new family proposed and keeps its current family.\n"
        + "Marker support uses source-family votes among labelled scope proteins carrying that domain; all domain carriers are counted separately, and none of these counts is abundance.\n"
        + "Motif counts include all members of proposed families, even unticked ones; known fragments are excluded, while entries without a fragment annotation are counted.\n"
        + "Apply grouping changes the open project; use File > Save to keep the changes on disk.\n";
}
