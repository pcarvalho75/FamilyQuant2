namespace FamilyQuant
{
    public static class GroupingReviewHelp
    {
        public static string ForMode(bool evidenceMode, bool panelMode = false) => evidenceMode
            ? EvidenceHelp.Build(panelMode: panelMode) + "\n\n" + Text
            : Text + "\n\n" + EvidenceHelp.Build(panelMode: panelMode);
        public const string Text = """
        Review grouping

        Nothing in the open document changes until Apply grouping is confirmed. Cancel, Escape or closing leaves it alone and adds no undo step.

        Keywords
        Keywords are separated by commas or spaces. Quoted words are a phrase; a hyphenated entry also works. Match all requires every keyword in one protein description; match any requires at least one. Matching reads the description before OS=.
        Exclude words keep individual proteins out. The rest of the family still joins. Ignore words from Recluster by words remains part of the matching scope.
        A red row carries no keyword itself: it is offered because another protein in its family does. Untick the red ones keeps only carriers. Tick all restores the offered rows. Tick selected and Untick selected act only on the selected rows; use a heading to sort and Shift+click to select many.
        The hidden-families option includes matching families hidden by the main Search without changing that Search.
        Domains are fetched from UniProt and remain stored offline with the file. Keep domains unticks proteins carrying none of the supplied domains; it does not tick new rows. A protein with no domains stays ticked if it carries a keyword. Choose a domain from the list, most proteins first, to append its name without the count, or type domain names from the Domains column separated by commas/spaces. If the file has no domains yet, use Fetch evidence in the main screen.

        Evidence
        At least 70 percent of the query's residues occur in the alignment; inserted gap columns do not count.
        Neighbour coverage shows the recorded aligned query residues over the full query length. Legacy or invalid coverage facts show not recorded; alignment columns remain a separate diagnostic and are never used as residue coverage in the display.
        Scope words match keyword and tissue annotations. Update proposal rebuilds the review from stored evidence; a changed scope must be updated before Apply. Updating never fetches evidence or changes the open document.
        Tiers, reasons, neighbours, marker carriers and flags are supplied by the evidence analysis. Ticks start with its suggested selection; a flagged protein starts unticked and is shown in red. You may explicitly select it after reviewing its reason and flags. Family names and ticks are your choices. All proteins shows the whole proposal; selecting a family limits the list on the right. Search filters the displayed proteins without changing ticks.
        A protein in a locked document family stays unticked and its checkbox is disabled, including when its proposed family is selected. Its tooltip explains the lock. Unlock it in Curate families before including it in an evidence grouping.
        Accession shows a UniProt accession when present; FASTA identifier retains the full original identifier. Current family ID is the unchanged document family number. Reviewed relative text includes the supplied accession, identity percentage, aligned-length percentage of the query, alignment/query/reference lengths, family and any tied families. Scope words found records whether stored keyword or tissue annotations match the scope.
        Proposed family shows the supplied family key, including in All proteins. The edited output name is in the family table on the left.
        Add to family assigns highlighted Proteins or Unassigned rows by hand to a proposed family, or to New family with a name you type. The rows are ticked and labelled Added by hand, with your optional note and the original automatic evidence retained separately. Update proposal keeps these choices; Remove from family clears the hand choice and restores the automatic proposal. Locked proteins are refused. These are review choices until Apply grouping, then File > Save keeps them in the grouping recipe; Undo restores the preceding grouping.
        Align selected opens a read-only alignment of highlighted proteins beside reviewed family representatives; it changes no protein tick or family proposal.
        Architecture groups describe combinations of called domains; their order is unknown. Tick Split beside a group to move only its selected members into a separate family, then enter its New family name. All unticked groups stay together in the proposed family. Split all groups selects or clears every group, and a mixed tick means only some groups are selected. The domain memberships and groups are fixed by the evidence analysis.
        Select a domain group to see its members. Placed by size marks members with no Pfam call that the analysis placed by length and cysteines; the supplied counts and measurements stay visible. The no-domain group is shown explicitly. Not placed in a domain group lists members no learned range placed, with the exact reason beside the split. No domain call is a missing call, not proof that the domain is absent.
        Left out shows marker-carrying proteins the analysis did not assign to that marker's family, including the supplied reason, marker, alternative assignment and source facts. Unassigned separately lists proteins with no proposed family. Neither list silently assigns a family. Marker carrier counts and vote fractions are the supplied quantities, without a new denominator inferred by the review.
        Motifs are user choices. Enter an amino-acid regular expression, prefix it with "not " to count proteins without it, or use "count C 12-16", "count C >= 8" or "count C <= 10" for amino-acid counts. Check motif shows Matching proteins and Proteins counted for each proposed family on the Motif counts tab, including unticked members. Known fragments stay outside that denominator; entries without a fragment annotation are counted. Editing the pattern requires a new check before Apply. Invalid patterns are reported and prevent Apply; the checked pattern travels with the review selection.

        Copy
        Right-click for a UniProt code, the description or the UniProt page. Ctrl+C copies the column clicked; the tick column copies the locus. Selecting a row header preserves the grid's ordinary row copy. A held clipboard is reported rather than silently ignored.
        """;
    }
}
