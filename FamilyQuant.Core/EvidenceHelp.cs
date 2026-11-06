using FamilyQuant.Evidence;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant
{
    /// <summary>
    /// The help page for grouping by evidence, as plain text for <c>TextReportWindow</c>. Thresholds are
    /// read from the constants the engine uses, and every result is read from a validation report passed
    /// in, so the page cannot quote a number the program did not compute for the open file.
    /// </summary>
    public static class EvidenceHelp
    {
        public const string ScopeExpansionHint = "Add to scope or use Group by words > Group by keywords. Left-out proteins keep their families for Quantify.";
        public const string DatabaseEntryCountDefinition = "Database entries counted are target FASTA records after excluding contaminants and decoys.";
        public const string ContaminantCountDefinition = "Contaminants excluded counts target records carrying the database contaminant prefix. Decoys are removed before this count.";
        public const string KeywordCountExplanation = "One protein can carry several keywords, so keyword rows are not summed. These are reviewed-entry counts, not abundance.";
        public const string CountDefinitionsHelp = "DATABASE COUNTS\n" + DatabaseEntryCountDefinition + "\n"
            + ContaminantCountDefinition + "\nKeyword rows count distinct reviewed accessions, once per keyword.\n" + KeywordCountExplanation;
        public const string ReviewCountHelp = """
            REVIEWED STATUS AND FASTA PREFIXES
            Reviewed in UniProt counts distinct matching accessions marked reviewed in the
            stored fetched evidence. The displayed release comes from that evidence. Marked
            sp in the FASTA counts target FASTA records with an sp prefix, after contaminants
            and decoys are excluded. That prefix records the database download's status.
            UniProt can change a record's reviewed status in a later release. The tooltip
            lists tr accessions now reviewed and sp accessions now unreviewed, with unresolved
            sp records and repeated or isoform headers listed separately when present.

            An accession merged into another entry can return unresolved. It contributes no
            reviewed UniProt accession. An unresolved entry still marked tr in the FASTA
            contributes to neither displayed reviewed count. Raw sp headers remain part of
            the FASTA-prefix count, and repeated/isoform headers can exceed the distinct
            reviewed-accession count.
            """;
        public const string ScopeExpansionHelp = """
            EXTENDING THE SCOPE
            The suggested scope is a starting point. To include more proteins, select their
            keyword rows and choose Add to scope, or double-click a keyword row. You can also
            type a keyword or tissue phrase in quotes. Choose Update proposal, review the
            family and protein selections, then choose Apply grouping.

            For manual family construction, open Curate families and use Group by words >
            Group by keywords. Enter description words and review the protein ticks before
            Apply grouping. You can keep only part of an existing family in the selection.

            Proteins left out keep the families they had before the evidence proposal,
            including their step 1 families from Build families. Quantify uses those current
            family assignments too.
            """;
        public static string Build(LeaveOneOutResult? validation = null, IReadOnlyList<RouteScore>? routes = null,
            string? routeFamily = null, bool panelMode = false)
        {
            if (panelMode) return ScopeExpansionHelp + "\n\n" + CountDefinitionsHelp + "\n\n" + ReviewCountHelp + "\n\n" + PanelEvidenceHelp.Build();
            var options = new EvidenceClassificationOptions();
            var sb = new StringBuilder();
            sb.AppendLine("GROUPING BY EVIDENCE");
            sb.AppendLine("====================");
            sb.AppendLine();
            sb.AppendLine(ScopeExpansionHelp);
            sb.AppendLine();
            sb.AppendLine(CountDefinitionsHelp);
            sb.AppendLine();
            sb.AppendLine(ReviewCountHelp);
            sb.AppendLine();
            sb.AppendLine("Each protein is placed in a family by what UniProt's curators and its own sequence");
            sb.AppendLine("say, never by the words in its name. Each one gets a family, a tier and a reason.");
            sb.AppendLine();

            sb.AppendLine("THE STEPS");
            sb.AppendLine("1. Fetch evidence: from UniProt, review status, family memberships, keywords, tissue,");
            sb.AppendLine("   Pfam and PANTHER domains, transmembrane helices and fragment status. The release is");
            sb.AppendLine("   kept with the file, so the file answers the same offline.");
            sb.AppendLine("2. Nearest reviewed protein: every protein is aligned with every reviewed protein. A");
            sb.AppendLine("   neighbour counts at " + Pct(NearestReviewed.DefaultMinIdentityPercent) + " identity or more over "
                + Pct(NearestReviewed.DefaultMinQueryAlignedPercent) + " or more of the protein's");
            sb.AppendLine("   length. A protein is never its own neighbour (same entry, isoforms included), and a");
            sb.AppendLine("   tie between two families is reported, not settled.");
            sb.AppendLine("3. Reference panel: the reviewed proteins of the families found, from every species,");
            sb.AppendLine("   are kept in the file, so a protein with few reviewed relatives still has neighbours.");
            sb.AppendLine("4. The rule, in order. The words you type choose the scope; they are matched in the");
            sb.AppendLine("   keywords and tissue of each protein, never in its name.");
            sb.AppendLine("   a. In scope, and UniProt gives one family: that family.");
            sb.AppendLine("   b. In scope, with a reviewed neighbour at " + Pct(options.AnnotatedIdentityPercent) + " or more: the neighbour's family.");
            sb.AppendLine("   c. In scope, with a domain that marks a family: a domain marks a family when at least");
            sb.AppendLine("      " + Pct(options.MarkerVoteFraction * 100) + " of the labelled proteins carrying it in scope belong to that family.");
            sb.AppendLine("   d. Out of scope, with a reviewed neighbour at " + Pct(options.UnannotatedIdentityPercent)
                + " or more and compatible domains.");
            sb.AppendLine("   e. Otherwise unassigned. A protein UniProt places in several families is left for you.");
            sb.AppendLine("5. Coherence check: from the complete reviewed members of each family (at least "
                + CoherenceCheck.DefaultMinimumMembers.ToString(CultureInfo.InvariantCulture) + "), the");
            sb.AppendLine("   program learns their length, cysteines and helices, and the domains they carry. A");
            sb.AppendLine("   protein outside what they ever show is flagged with the reason and starts unticked.");
            sb.AppendLine("6. Architecture split: inside a family, proteins with a different domain combination can");
            sb.AppendLine("   be split off; the choice is yours and is kept in the project.");
            sb.AppendLine("7. Motif check: motifs you type are counted per family on complete members.");
            sb.AppendLine("8. Left-out list: proteins carrying a family's marker domain that were not placed in it,");
            sb.AppendLine("   with the reason.");
            sb.AppendLine();

            sb.AppendLine("WHAT IS NEW COMPARED WITH WORD AND KEYWORD GROUPING");
            sb.AppendLine("- Words group proteins by what they are called, and names in unreviewed entries are");
            sb.AppendLine("  inconsistent. Evidence uses the curated family, homology to reviewed proteins and the");
            sb.AppendLine("  domains, and no family, name or domain list is built into the program.");
            sb.AppendLine("- Words join whole families; evidence decides protein by protein, and shows why.");
            sb.AppendLine("- An exclude word removes by name. The coherence check and the left-out list say what in");
            sb.AppendLine("  the data made a protein stand out, and leave the decision to you.");
            sb.AppendLine("- The result can be measured: the validation report hides each reviewed protein's family");
            sb.AppendLine("  and calls it back from its nearest other reviewed protein.");
            sb.AppendLine();

            sb.AppendLine("ON THIS FILE");
            if (validation == null)
            {
                sb.AppendLine("Run the validation report to see how the rule does on this file.");
            }
            else
            {
                sb.AppendLine("Leave-one-out on " + N(validation.Reviewed) + " reviewed proteins, neighbour at "
                    + Pct(validation.MinIdentityPercent) + " or more:");
                sb.AppendLine("  " + N(validation.Right) + " right, " + N(validation.Wrong) + " wrong, "
                    + N(validation.NoNeighbour) + " without a neighbour.");
                foreach (ConfusionRow row in validation.Confusion.Take(3))
                    sb.AppendLine("  " + N(row.Count) + " of " + row.Families + " called " + row.NeighbourFamilies + ".");
            }
            if (routes != null && routes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Ways of forming " + (routeFamily ?? "the family") + ", against its reviewed members:");
                foreach (RouteScore route in routes)
                    sb.AppendLine("  " + route.Route + ": " + N(route.Proteins) + " proteins; reviewed members taken "
                        + N(route.ReviewedMembersTaken) + ", missed " + N(route.ReviewedMembersMissed)
                        + "; other reviewed proteins taken " + N(route.ReviewedOthersTaken) + ".");
            }
            sb.AppendLine();

            sb.AppendLine("WHAT IT DOES NOT DO");
            sb.AppendLine("- A short fragment with no domain call and none of your words stays unassigned and flagged:");
            sb.AppendLine("  identity over a few dozen residues does not decide a family.");
            sb.AppendLine("- Two kinds of protein that differ only in their names cannot be told apart. They are");
            sb.AppendLine("  listed for you rather than guessed.");
            return sb.ToString();
        }

        private static string Pct(double value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";
        private static string N(int value) => value.ToString("N0", CultureInfo.InvariantCulture);
    }
}
