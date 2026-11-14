using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    public static class GroupingReviewHelp
    {
        public static string ForMode(bool evidenceMode, bool panelMode = false) => (evidenceMode
            ? EvidenceHelp.Build(panelMode: panelMode) + Environment.NewLine + Environment.NewLine + Text
            : Text + Environment.NewLine + Environment.NewLine + EvidenceHelp.Build(panelMode: panelMode))
            + Environment.NewLine + Environment.NewLine + AdvancedTabsText;
        public static IReadOnlyDictionary<string, string> AdvancedTabGuides { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Keywords"] = "Use description words to group proteins when you want to review matches before applying them.",
            ["Panel links"] = "Choose how stored UniProt families and domain combinations link to your list when a choice is unresolved.",
            ["Family checklist"] = "Check every family in your list when you want to see which have proteins and why others are waiting.",
            ["Architecture"] = "Compare domain combinations when you need to decide whether a proposed family should split.",
            ["Left out"] = "Check these proteins when a family marker is present but the proposal put them elsewhere.",
            ["Unassigned"] = "Review proteins with no proposed family when you want to place them by hand or keep their current family.",
            ["Markers"] = "Check which domains support each family when you want to understand a proposal.",
            ["Motif counts"] = "Read motif matches after using Check motif to compare complete members of the proposed families.",
        };
        public static string AdvancedTabsText => "Show all options tabs" + Environment.NewLine
            + string.Join(Environment.NewLine, AdvancedTabGuides.Select(guide => guide.Key + ": " + guide.Value));
        public const string Text = """
        Review grouping

        Nothing in the open document changes until Apply grouping is confirmed. Cancel, Escape or closing leaves it alone and adds no undo step.
        When Apply grouping waits for choices in Panel links, Review choices opens that tab filtered to Needs a choice. Choose a family or Leave outside panel, then Use link choices to rebuild the proposal. The waiting count disappears when every link is resolved.

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
        Accession shows a UniProt accession when present; FASTA identifier retains the full original identifier. Current family ID is the unchanged document family number. Reviewed relative text includes the supplied accession, identity percentage, aligned-length percentage of the query, alignment/query/reference lengths, family and any tied families. Scope words found records whether stored keyword or tissue annotations match the scope. Selected protein details wraps and grows with its text up to ten lines as space allows, taking space from the protein table; longer details have a scrollbar. Simple view reserves room for eight protein rows before growing the details pane; longer text scrolls. Short windows cap that growth so the scope and motif controls stay on screen.
        Proposed family shows the supplied family key, including in All proteins. The edited output name is in the family table on the left.
        Add to family assigns highlighted Proteins or Unassigned rows by hand to a proposed family, or to New family with a name you type. The rows are ticked and labelled Added by hand, with your optional note and the original automatic evidence retained separately. Update proposal keeps these choices; Remove from family clears the hand choice and restores the automatic proposal. Locked proteins are refused. These are review choices until Apply grouping, then File > Save keeps them in the grouping recipe; Undo restores the preceding grouping.
        With a family list, Where proteins go shows three compact counts for database target proteins in the current proposal. The collapsed line reserves no explanation space; expand it to read all three destination explanations and the proposal-count note. Opening it changes only the view. Proteins proposed in a family of your list join that family when selected. In-scope proteins in no family of your list keep their sequence family, named after its proteins, and are measured together in that family. Proteins outside the scope keep their current family. Decoys and contaminants are excluded from these three counts. Locked or unticked proteins keep their current family, even if a different family is proposed. Apply grouping commits selected placements; Undo restores the preceding grouping.
        Panel links shows an example database protein with its accession, name and stored UniProt family. Examples come from the link's supporting accessions in the open database. Reviewed proteins come first, then entries with more stored annotation fields; accession breaks equal evidence ties. No database example available means no matching database protein was supplied for that link.
        Align selected opens a read-only alignment of highlighted proteins beside reviewed family representatives; it changes no protein tick or family proposal. The heading counts the highlighted proteins and reviewed references currently ticked for alignment. References are the stored nearest reviewed proteins and representatives of the highlighted proteins' families, from the database or stored reference panel; their source is shown per row. Changing alignment ticks updates the heading and comparison only.
        Architecture groups describe combinations of called domains; their order is unknown. Tick Split beside a group to move only its selected members into a separate family, then enter its New family name. All unticked groups stay together in the proposed family. Split all groups selects or clears every group, and a mixed tick means only some groups are selected. The domain memberships and groups are fixed by the evidence analysis.
        Select a domain group to see its members. Placed by size marks members with no Pfam call that the analysis placed by length and cysteines; the supplied counts and measurements stay visible. The no-domain group is shown explicitly. Not placed in a domain group lists members no learned range placed, with the exact reason beside the split. No domain call is a missing call, not proof that the domain is absent.
        Left out shows marker-carrying proteins the analysis did not assign to that marker's family, including the supplied reason, marker, alternative assignment and source facts. Unassigned separately lists proteins with no proposed family. Neither list silently assigns a family. Marker carrier counts and vote fractions are the supplied quantities, without a new denominator inferred by the review.
        Under Show all options, Proteins, Panel links and Family checklist stay available. Architecture, Left out, Unassigned and Markers appear when the project has rows for them; Motif counts appears after a checked report is available. Empty tabs return when their content arrives. Search and the selected family do not hide content elsewhere in the project. Simple view keeps only Proteins, and switching views keeps pending choices.

        Motifs are user choices. Enter an amino-acid regular expression, prefix it with "not " to count proteins without it, or use "count C 12-16", "count C >= 8" or "count C <= 10" for amino-acid counts. Check motif shows Matching proteins and Proteins counted for each proposed family on the Motif counts tab, including unticked members. Known fragments stay outside that denominator; entries without a fragment annotation are counted. Editing the pattern requires a new check before Apply. Invalid patterns are reported and prevent Apply; the checked pattern travels with the review selection.

        Consensus family spreadsheet
        Use File > Import family list to read your consensus spreadsheet. Choose the abbreviation as the stable family key, select the display-name and other-language columns, and map each decision label. Group by evidence > Family checklist shows database matches and why the proposal leaves them outside. Choose a stored phrase and Add phrase to scope to preview a wider scope, then Update proposal. Place outside matches by hand opens the explicit user-choice dialog and records Added by hand. Review the ticks, Apply grouping, then File > Save. Matching names or domains alone never assigns a family.

        Matching stored evidence labels
        The Family checklist retains every row of your list, including rows with no proteins. The table uses the full review width and one line per row; hover over Panel family for its full name, stable key and excluded-contaminant count. Complete reasons and scope or matching actions scroll below it. After Add phrase to scope, its preview stays beside those scrolling actions until Update proposal. Its top line counts rows with proposed proteins, waiting for a choice in Panel links, with no Pfam code, matching no database protein, with no protein in the scope, with proteins in another row, or with no proteins placed. Waiting means an actual unresolved Panel links choice; outside-scope matches and proteins assigned elsewhere do not need a link choice. A row with proteins shows how many are placed in the proposal; candidate matches alone do not assign proteins. A pending link takes priority over a missing Pfam code in this summary. The Decision and outside reasons remain visible for excluded or merged rows.
        Match stored names uses the panel display name and every other name under its original spreadsheet header. Suggestions come from shared name words and labels found on the same stored database or reference entries. Every new suggestion starts unticked; edit its type or value, or Add label, then confirm the labels to use. Coverage counts exact database target entries and stored reference accessions for each label; rows can overlap. Incomplete stored references are identified. Confirmed labels identify checklist candidates, where the scope and hand-placement actions continue the review. Apply grouping and File > Save keep choices made in this review. To save labels without grouping proteins, select the family in File > Import family list, use Match stored names, then Apply panel and File > Save. Saved records follow explicit family-key edits with a moved/dropped notice.

        Copy
        Right-click for a UniProt code, the description or the UniProt page. Ctrl+C copies the column clicked; the tick column copies the locus. Selecting a row header preserves the grid's ordinary row copy. A held clipboard is reported rather than silently ignored.
        """;
    }
}
