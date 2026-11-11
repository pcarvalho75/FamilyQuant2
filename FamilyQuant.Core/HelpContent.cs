using FamilyQuant.DeNovo;
using System;
using System.Globalization;
using System.Text;

namespace FamilyQuant
{
    /// <summary>
    /// The "How to read these numbers" panel, as plain text so it can be asserted by the test suite and
    /// so it cannot drift from the wording used by the controls: every mode and policy name below comes
    /// from the same extension methods the ComboBoxes are filled from.
    ///
    /// It answers the four questions that were actually asked out loud: what the denominator is, why the
    /// families do not add up to 100%, what a shared peptide is, and why the Divide and Conquer identity
    /// is a floor rather than a recommendation.
    ///
    /// <see cref="BuildDeNovo"/> is the second page, for the De novo review screen. Two pages and not
    /// one: they are two workflows, and neither page's text applies to the other's screen.
    /// </summary>
    public static class HelpContent
    {
        public const string GroupingOrder =
            "0. Import family list (optional): use File > Import family list to choose your workbook or Use template to write an empty workbook to fill and import, with every family quantified. Older sheets can still carry Notes and Decision columns. Only a Decision column with custom words needs label review; source colours remain in the workbook preview. Supplied Pfam codes found in stored evidence are accepted directly; names and codes not found need a choice. A row without a code gets no proteins until it has one. UniProt family holds exact family names separated by semicolons; empty means none. Sheets without family columns are skipped and named in the summary; their source text is kept.\n" +
            "1. Fetch evidence: download UniProt annotations for the project's proteins.\n" +
            "2. Group by evidence: review the scope and proposal, then Apply grouping.\n" +
            "3. Fetch UniProt families: find the families of in-scope database proteins and download their reviewed and database-clade entries.\n" +
            "4. Quantify: measure peptide signal from the RAW files.\n";

        /// <summary>
        /// <paramref name="profile"/> is null before a project is loaded; the text then explains what the
        /// choice WILL be rather than claiming a denominator the program cannot yet name.
        /// </summary>
        public static string Build(QuantitationProfile? profile, NormalizationMode mode, SharedPeptidePolicy policy)
        {
            var sb = new StringBuilder();

            sb.AppendLine("HOW TO READ THESE NUMBERS");
            sb.AppendLine("=========================");
            sb.AppendLine();

            sb.AppendLine("HOW QUANTITATION WORKS");
            sb.AppendLine("Two sources: the Full XIC column comes from Quantify.");
            sb.AppendLine("The optional PatternLab .plp supplies the 'Share of signal' column.");
            sb.AppendLine("Shared peptides applies to both columns; PatternLab share of and Recalculate apply");
            sb.AppendLine("to the .plp column. Share of under Full XIC changes that column immediately.");
            sb.AppendLine("Each share column names its denominator; Full XIC details and PatternLab details");
            sb.AppendLine("retain the source facts. The bottom status line describes the PatternLab calculation.");
            sb.AppendLine();

            sb.AppendLine("THE ORDER OF WORK");
            sb.AppendLine("-----------------");
            sb.AppendLine("The five steps, as Home and the sidebar list them (steps 1 to 4 in order, 5 optional):");
            sb.AppendLine(WorkflowNames.Flow);
            sb.AppendLine("1. " + WorkflowNames.BuildFamilies + " groups by sequence identity with USearch: File > Build families,");
            sb.AppendLine("   Home or the sidebar opens it. Choose database, then Build families on a FASTA or");
            sb.AppendLine("   target-decoy (.T-R) database. That writes a Family Cluster File (.fcf) next to the");
            sb.AppendLine("   database: proteins grouped into families.");
            sb.AppendLine("2. " + WorkflowNames.CurateFamilies + " opens that .fcf after Build; File > Open also opens existing files. Part a, " + WorkflowNames.GroupByEvidence + ",");
            sb.AppendLine("   follows the numbered Grouping order below; part b, " + WorkflowNames.GroupByWords + ", merges families by");
            sb.AppendLine("   their description words (Recluster by words) or a keyword list (Group by keywords).");
            sb.AppendLine("3. " + WorkflowNames.Quantify + ", on Home or the sidebar, is the primary quantitation route.");
            sb.AppendLine("   In Curate families, PatternLab (optional) offers Load PatternLab project for a .plp. It");
            sb.AppendLine("   must have been exported at PEPTIDE level - a protein-level export has nothing");
            sb.AppendLine("   this program can attribute to a family.");
            sb.AppendLine("   In Quantitation, choose Shared peptides for either source. Share of applies to");
            sb.AppendLine("   Full XIC immediately; PatternLab share of needs Recalculate for the .plp column.");
            sb.AppendLine("4. " + WorkflowNames.Results + " reads the stored quantitation without reading RAW. File > Export writes");
            sb.AppendLine("   CSV; Tools > Reannotate SEPro stamps the family numbers back into a SEPro result package.");
            sb.AppendLine("5. " + WorkflowNames.DeNovoReview + " is optional: it inspects de novo calls beside PatternLab, and");
            sb.AppendLine("   Quantify does not need it.");
            sb.AppendLine();
            sb.AppendLine(DatabaseUpdateHelp.Text);
            sb.AppendLine();
            sb.AppendLine("The families and the quantitation you keep live in one FamilyQuant project file, a");
            sb.AppendLine(".fqproj, written by File > Save. Beyond the share above, Quantify measures each");
            sb.AppendLine("peptide's full isotope envelope straight from the RAW and stores it in that project,");
            sb.AppendLine("and the Results screen then reads the recovery from it. An old Family Cluster File");
            sb.AppendLine("(.fcf) still opens, with the quantitation empty until it is computed.");
            sb.AppendLine("In the HTML report, Signal recovery and Database gap draw interactive charts from");
            sb.AppendLine("the same stored readings as the program. Hover, focus or select a bar or value for");
            sb.AppendLine("its exact reading and reference total; All exact readings opens a printable table.");
            sb.AppendLine("These charts keep their named bases when Normalize to changes. Word keeps its pictures.");
            sb.AppendLine("The Signal recovery guide defines a peptide feature, explains each TIC-based reading,");
            sb.AppendLine("and explains isotope, detector and TIC gaps without treating them as biological classes.");
            sb.AppendLine("Database gap also gives each part's share of its own family's displayed signal total;");
            sb.AppendLine("the separate comparison panel keeps all measured peptide feature signal as its base.");
            sb.AppendLine("Signal sources by family lists families by pooled total signal, largest first.");
            sb.AppendLine("What the numbers mean at the top defines the reference totals and peptide features.");
            sb.AppendLine("Share rows use compact percentages. The chart heading names their reference total.");
            sb.AppendLine("Hover or select a value for its exact reading and base, including rounded ring totals.");
            sb.AppendLine("Family labels stop before database header fields; tooltips and details retain the full name.");
            sb.AppendLine("The compare-measures plot's mean and SD follow Normalize to, with its base named on the axis.");
            sb.AppendLine("Coverage uses matching choosers, a residue ruler and List placements and annotations.");
            sb.AppendLine();

            sb.AppendLine("WHAT THE PERCENTAGE IS OF (THE DENOMINATOR)");
            sb.AppendLine("-------------------------------------------");
            sb.AppendLine("A family's number is a share, and a share is meaningless until you know of what.");
            sb.AppendLine("For the optional .plp column, PatternLab share of is that choice. It currently reads:");
            sb.AppendLine();
            sb.AppendLine("    " + mode.DisplayName());
            sb.AppendLine();
            sb.AppendLine(Wrap(mode.Explanation(), "    "));
            sb.AppendLine();

            if (profile == null)
            {
                sb.AppendLine("No PatternLab project is loaded, so the program cannot yet say which .plp denominators");
                sb.AppendLine("are available. Use Load PatternLab project: unsupported modes are greyed out,");
                sb.AppendLine("each with the reason in its tooltip.");
            }
            else
            {
                sb.AppendLine("The loaded project says:");
                sb.AppendLine();
                sb.AppendLine(Wrap(profile.Summary(), "    "));
                sb.AppendLine();

                if (profile.Evidence == DenominatorEvidence.Inferred)
                {
                    sb.AppendLine("Note the words \"NOT recorded\" above. This project was exported before");
                    sb.AppendLine("PatternLab wrote down which divisor it had used, so the program worked it out");
                    sb.AppendLine("from the size of the numbers. That is a guess, and it is labelled as one");
                    sb.AppendLine("everywhere it appears. Re-exporting from XQuant turns it into a recorded fact.");
                }
            }

            sb.AppendLine();
            sb.AppendLine("WHY THE FAMILIES DO NOT ADD UP TO 100%");
            sb.AppendLine("--------------------------------------");
            sb.AppendLine("In \"" + NormalizationMode.IdentifiedSignal.ShortName() + "\" they do, by construction:");
            sb.AppendLine("every family is divided by the sum of all families.");
            sb.AppendLine();
            sb.AppendLine("In \"" + NormalizationMode.TotalChromatogram.ShortName() + "\" they do not, and that is");
            sb.AppendLine("the point of the mode. The denominator is the whole chromatogram, and most of a");
            sb.AppendLine("chromatogram is never identified: signal from peptides your database does not");
            sb.AppendLine("contain, from non-peptide material, from charge states that were not extracted.");
            sb.AppendLine("The gap between your families' total and 100% is that unidentified remainder. It is");
            sb.AppendLine("a measurement, not an error - reading it as one is the mistake this mode exists to");
            sb.AppendLine("prevent.");
            sb.AppendLine();
            sb.AppendLine("Under \"" + SharedPeptidePolicy.FamilyUnique.DisplayName() + "\" a second piece is");
            sb.AppendLine("missing as well: the signal on peptides that belong to more than one family and is");
            sb.AppendLine("therefore counted for none of them.");
            sb.AppendLine();

            sb.AppendLine("WHAT A SHARED PEPTIDE IS");
            sb.AppendLine("------------------------");
            sb.AppendLine("A peptide whose sequence occurs in proteins of more than one family. In a redundant");
            sb.AppendLine("database, where whole families differ by a handful of residues, this is not a");
            sb.AppendLine("corner case: most of the signal can sit on shared peptides. Nothing can say which");
            sb.AppendLine("family such a peptide came from, so the program does not pretend to. In the Full");
            sb.AppendLine("XIC column an identified peptide belongs to the families whose proteins contain");
            sb.AppendLine("it, exactly as above; a de novo sequence, which has no protein of its own, to the");
            sb.AppendLine("families of every protein it resembles at the minimum homology identity. The");
            sb.AppendLine("choice is yours, and it is currently:");
            sb.AppendLine();
            sb.AppendLine("    " + policy.DisplayName());
            sb.AppendLine();
            sb.AppendLine(Wrap(policy.Explanation(), "    "));
            sb.AppendLine();
            sb.AppendLine("The other options:");
            sb.AppendLine();

            foreach (SharedPeptidePolicy other in new[]
                     {
                         SharedPeptidePolicy.FamilyUnique,
                         SharedPeptidePolicy.SplitEqually
                     })
            {
                if (other == policy) continue;

                sb.AppendLine("    " + other.DisplayName());
                sb.AppendLine(Wrap(other.Explanation(), "        "));
                sb.AppendLine();
            }

            sb.AppendLine("A project saved with count in every family opens with family-unique instead.");
            sb.AppendLine("The status line says so once; run Quantify again to refresh its stored results.");
            sb.AppendLine();

            sb.AppendLine("HOW THE FAMILIES ARE BUILT: TWO STAGES");
            sb.AppendLine("--------------------------------------");
            sb.AppendLine("Clustering runs in two stages. The fast first pass groups the database quickly,");
            sb.AppendLine("but it is greedy and order-sensitive: two proteins that align above your identity");
            sb.AppendLine("can still land in separate families, because the second was only ever compared");
            sb.AppendLine("against the seed of a family it did not quite match. That leaves biological holes,");
            sb.AppendLine("full-length homologues split apart.");
            sb.AppendLine();
            sb.AppendLine("The \"Exhaustive second pass\" checkbox (on by default) closes them. After the first");
            sb.AppendLine("pass it aligns EVERY pair of proteins in full and merges two families whenever the");
            sb.AppendLine("proteins reach your identity over most of the LONGER sequence. Proteins that share");
            sb.AppendLine("only a short motif fail that coverage test and are correctly kept apart, so an");
            sb.AppendLine("angiotensin fragment does not pull in a whole metalloproteinase, but two full-length");
            sb.AppendLine("metalloproteinases the fast pass split are put back together.");
            sb.AppendLine();
            sb.AppendLine("This is single linkage: every pair at or above the identity chains its two families");
            sb.AppendLine("into one, so the merge can chain a whole superfamily together. That is the intended");
            sb.AppendLine("meaning of the threshold, not a side effect: the identity you choose IS the tightness");
            sb.AppendLine("knob. Raise it for more, smaller families; lower it to chain more together.");
            sb.AppendLine();
            sb.AppendLine("The exhaustive pass runs at any database size and is never capped. On a database");
            sb.AppendLine("over 10,000 sequences the all-versus-all alignment takes longer and more memory, so");
            sb.AppendLine("you are warned once before it starts and can proceed or stop; it still runs at any");
            sb.AppendLine("size if you proceed. Turn the checkbox off to use the faster first-pass-only");
            sb.AppendLine("clustering, which is quicker but leaves the holes.");
            sb.AppendLine();

            sb.AppendLine("THE FAMILY OVERVIEW AND PROTEIN TABLE");
            sb.AppendLine("-------------------------------------");
            sb.AppendLine("Curate families keeps the family overview, protein table and actions beside one another.");
            sb.AppendLine("Select a family to show its proteins; All proteins restores the filtered protein");
            sb.AppendLine("list. Each overview row shows its whole-family target count and one family share,");
            sb.AppendLine("including members hidden by display filters; it never adds the repeated protein");
            sb.AppendLine("shares together. Column headings name the current denominator.");
            sb.AppendLine();
            sb.AppendLine("Edit the family name to annotate every target member, including hidden members.");
            sb.AppendLine("The family checkbox protects it from automatic grouping or removes that protection.");
            sb.AppendLine("Each committed name or checkbox edit is one Undo step. Edit selected proteins can");
            sb.AppendLine("join or split selected rows and set their annotation in one batch; destination and");
            sb.AppendLine("surviving source families are locked, while emptied source families are not.");
            sb.AppendLine();
            sb.AppendLine("A legacy PatternLab export can lack the recorded divisor and per-run totals.");
            sb.AppendLine("Re-exporting it from XQuant records the divisor and every run's totals, making");
            sb.AppendLine("TIC reporting available when those totals are present; the other reporting modes");
            sb.AppendLine("remain usable while that mode is greyed out.");
            sb.AppendLine();
            sb.AppendLine("GROUPING: PART A OF CURATE FAMILIES, GROUP BY EVIDENCE");
            sb.AppendLine("------------------------------------------------------");
            sb.AppendLine(GroupingOrder);
            sb.AppendLine("Fetch evidence stores annotations for grouping; Fetch UniProt families stores reference");
            sb.AppendLine("sequences for Database gap. Both need an internet connection when downloading.");
            sb.AppendLine("Save the project to keep the evidence, applied grouping and UniProt families.");
            sb.AppendLine();
            sb.AppendLine("PART B OF CURATE FAMILIES, GROUP BY WORDS, AND CURATING BY HAND");
            sb.AppendLine("----------------------------------------------------------------");
            sb.AppendLine("Group by words merges families by their description words (Recluster by words) or builds");
            sb.AppendLine("one family from a keyword list (Group by keywords); both are reviewed before they apply.");
            sb.AppendLine();
            sb.AppendLine("Undo steps back through the changes you make in Curate families, one at a time, without");
            sb.AppendLine("reloading the file. It reverses Recluster by words, Group by keywords, evidence");
            sb.AppendLine("grouping, a family name or checkbox edit, a batch edit, a User ID edit or lock removal,");
            sb.AppendLine("putting the changed family numbers, annotations and");
            sb.AppendLine("the locks back exactly as they were just before. It remembers the last 50 steps of");
            sb.AppendLine("this session, does not survive closing the file, and there is no redo.");
            sb.AppendLine();
            sb.AppendLine("Group by keywords opens Review grouping. \"Match\" decides whether one description");
            sb.AppendLine("must contain ALL of your keywords or just ANY one of them; the default is \"match all");
            sb.AppendLine("words\", so a phrase such as \"acidic phospholipase\" gathers only the acidic");
            sb.AppendLine("phospholipases and leaves the plain ones alone, while \"match any word\" gathers");
            sb.AppendLine("every family carrying even one keyword. A single keyword behaves the same either");
            sb.AppendLine("way. The review's own \"Exclude words\" box keeps a protein OUT even when its");
            sb.AppendLine("family matches: exclude \"inhibitor\" to leave out inhibitor descriptions while");
            sb.AppendLine("the rest of a matching family joins. It is separate from the Recluster by words Ignore words box,");
            sb.AppendLine("which drops words from the comparison rather than dropping a protein.");
            sb.AppendLine("Separate its words with commas or spaces. It matches WHOLE words of the description");
            sb.AppendLine("before OS=, and keeps out the proteins carrying the word; the rest of the family joins.");
            sb.AppendLine();
            sb.AppendLine("Group by keywords changes nothing until you confirm Apply grouping in the review.");
            sb.AppendLine("Keywords find the families; every protein of those families is listed, each with a");
            sb.AppendLine("tick, its Locus, its description as the database has it, and its domains. A protein");
            sb.AppendLine("with none of your words is in red: it is here because another protein of its family");
            sb.AppendLine("carries one, so a protein without a keyword can come along with a matching protein.");
            sb.AppendLine("Untick a protein and it stays in its family, while the rest of that family joins.");
            sb.AppendLine("\"Untick the red ones\" does that for all of them at once. To untick many by hand,");
            sb.AppendLine("click a heading to sort, Shift+click to select, and press Untick selected. When part");
            sb.AppendLine("of a family leaves, the new family is locked, so that Recluster by words cannot put");
            sb.AppendLine("back what you left out; the lock manager removes it. An annotation stays on each protein.");
            sb.AppendLine();
            sb.AppendLine("A familiar protein name may be absent from the database description, so that keyword");
            sb.AppendLine("can find nothing even for a protein you expected to include. The preview shows");
            sb.AppendLine("the supplied descriptions and domains so you can decide which proteins to untick.");
            sb.AppendLine("The keywords and exclude words can be edited there and everything follows as you");
            sb.AppendLine("type. If the Search box hides families that match, one tick brings them in.");
            sb.AppendLine();
            sb.AppendLine("Fetch evidence asks UniProt for protein evidence, including Pfam domains, family,");
            sb.AppendLine("keyword and tissue annotations, plus reviewed references for the families found.");
            sb.AppendLine("The operation publishes the completed evidence and panel together; saving keeps them");
            sb.AppendLine("in the file with their release for offline review. Pfam domains fill the Domains column; the Search box");
            sb.AppendLine("finds them, and in the preview Keep domains unticks the proteins with none of the");
            sb.AppendLine("domains you name: choosing a domain keeps proteins carrying it regardless of their");
            sb.AppendLine("descriptions, and unticks noncarriers without an exclude word. A protein with no");
            sb.AppendLine("domains stays ticked if it carries a keyword; short proteins and fragments can have");
            sb.AppendLine("no Pfam call. Domains do not separate everything: different families can share them.");
            sb.AppendLine();
            sb.AppendLine("Fetch UniProt families finds the UniProt families of the in-scope proteins of your");
            sb.AppendLine("database, then downloads every reviewed UniProt entry of those families and their");
            sb.AppendLine("entries from your database's own clade. It uses the confirmed database task and scope");
            sb.AppendLine("from Group by evidence.");
            sb.AppendLine("Fetch evidence first to supply the database's family annotations.");
            sb.AppendLine("If no matching task and scope are confirmed, it offers to open Group by evidence first.");
            sb.AppendLine("The screen shows the stored release, scope, family count, entry count and size. Cancel or a");
            sb.AppendLine("failed fetch keeps the previous file. Save the project to keep these references for offline");
            sb.AppendLine("use. Quantify uses them to populate Database gap, where credited tags can suggest FASTA additions.");
            sb.AppendLine();
            sb.AppendLine("Group by evidence opens the Evidence tab of Review grouping. Confirm the scope,");
            sb.AppendLine("then review the families, flags, protein ticks and Selected protein details in Simple view.");
            sb.AppendLine("Selected protein details wraps and grows up to ten lines; longer text has a scrollbar.");
            sb.AppendLine("Align selected counts the selected proteins and reviewed references included in its heading. References are");
            sb.AppendLine("stored nearest reviewed proteins and family representatives from the database or reference panel; alignment ticks change only the comparison.");
            sb.AppendLine("Show all options opens Keywords, all evidence tabs, full protein columns and extra tools;");
            sb.AppendLine("each tab it reveals starts with a sentence saying what to use it for; Review grouping Help lists the same sentences.");
            sb.AppendLine("it keeps the scope and pending choices. With an imported family list, Family checklist");
            sb.AppendLine("opens that review in one click. Apply grouping commits the reviewed choices.");
            sb.AppendLine("In Panel links, tied domain matches use the fraction of each row's Pfam list present");
            sb.AppendLine("in the protein. The most complete row wins; equal coverage still asks for a choice.");
            sb.AppendLine("Your confirmed choices take precedence and are kept when the project is saved.");
            sb.AppendLine("If Apply grouping waits for Panel links, Review choices shows only links needing a choice;");
            sb.AppendLine("choose a family or Leave outside panel, then Use link choices to update the proposal.");
            sb.AppendLine("Each Panel links row shows an example database protein and its stored UniProt family to help you recognize it.");
            sb.AppendLine("The destination panel counts target proteins proposed in your list, in scope outside your list, and outside scope.");
            sb.AppendLine("In-scope proteins in no family of the list keep their sequence family, named after its proteins,");
            sb.AppendLine("and are measured together in it. Outside-scope proteins keep their current family. These are proposal counts:");
            sb.AppendLine("locked or unticked proteins keep their current family; decoys and contaminants are excluded from the counts.");
            sb.AppendLine("Family checklist keeps every row of your list and counts those with proposed proteins, waiting for a choice,");
            sb.AppendLine("with no Pfam code, or matching no database protein; matching evidence alone does not assign a protein.");
            sb.AppendLine("Save panel as spreadsheet writes named link choices in the UniProt family column.");
            sb.AppendLine("Importing that column restores exact family links among the supported rows without");
            sb.AppendLine("asking again. Separate several source family names with semicolons.");
            sb.AppendLine("A row naming a UniProt family takes only proteins with that complete family name.");
            sb.AppendLine("Those proteins enter that row even without matching scope words. Rows sharing its");
            sb.AppendLine("domains without a family name take the other supported proteins under the existing");
            sb.AppendLine("scope rules. A neighbour cannot supply membership of a named UniProt family.");
            sb.AppendLine("If the project has no stored UniProt evidence, Download and continue runs Fetch evidence");
            sb.AppendLine("before opening the review; Cancel leaves the project unchanged. If no reviewed accession");
            sb.AppendLine("is counted, no scope can be suggested: fetch evidence first, or type a scope.");
            sb.AppendLine("Cancelling leaves the open grouping unchanged and adds no Undo step.");
            sb.AppendLine(EvidenceScopeHelp.Build());
            sb.AppendLine();
            sb.AppendLine("In Review grouping and the Curate families protein table, right-click to copy a UniProt code or");
            sb.AppendLine("description or to open its UniProt page. Ctrl+C copies the column you clicked, for");
            sb.AppendLine("every selected row, or the whole row after a click on a row number.");
            sb.AppendLine();
            sb.AppendLine("Put words in quotes to require them together, in that order: \"ribosomal subunit\",");
            sb.AppendLine("\"ribosomal protein\" with \"match any word\" gathers either phrase. A phrase is");
            sb.AppendLine("matched on the whole description, so a common word such as \"protein\", ignored as a");
            sb.AppendLine("single keyword, still counts inside a phrase.");
            sb.AppendLine();
            sb.AppendLine("The Search box takes several terms separated by commas, for example");
            sb.AppendLine("\"domain, fragment\": a row is shown when it carries ANY of them.");
            sb.AppendLine();
            sb.AppendLine("Typing in the Search box limits both Recluster by words and Group by keywords");
            sb.AppendLine("to the families the search shows: every family the search hides keeps its");
            sb.AppendLine("numbers untouched, so you can organise one subgroup at a time. The word vectors are");
            sb.AppendLine("then computed within the shown families, which is the intended \"organise just this");
            sb.AppendLine("subgroup\" behaviour. The scope follows the SEARCH TEXT only, not the Hide decoys");
            sb.AppendLine("or Hide contaminants checkboxes, so hiding rows for display never silently drops a");
            sb.AppendLine("family from a recluster. Clear the Search box and both act on the whole database");
            sb.AppendLine("again, exactly as before. Locks still hold within the subgroup.");
            sb.AppendLine();

            sb.AppendLine("WHY DIVIDE AND CONQUER GIVES A FLOOR, NOT A RECOMMENDATION");
            sb.AppendLine("---------------------------------------------------------");
            sb.AppendLine("Divide and Conquer searches for the lowest sequence identity at which no family");
            sb.AppendLine("holds both a target and a decoy sequence. It is tempting to read its answer as");
            sb.AppendLine("\"the right identity\". It is not, for a reason built into what the score can see.");
            sb.AppendLine();
            sb.AppendLine("A reversed database has the same internal redundancy as the real one: if two");
            sb.AppendLine("proteins are similar, their reversed copies are similar too. So decoy families");
            sb.AppendLine("collapse at the same rate as target families, and putting two decoys together is");
            sb.AppendLine("not an error. The only event the score can detect is a target aligning to a decoy,");
            sb.AppendLine("which happens once alignments have stopped being better than noise.");
            sb.AppendLine();
            sb.AppendLine("It therefore marks the identity below which clustering is meaningless. It CANNOT");
            sb.AppendLine("see two unrelated target families being fused, which is the error you actually");
            sb.AppendLine("care about. The number it gives you is a floor, not a recommendation.");
            sb.AppendLine();
            sb.AppendLine("So: cluster at that identity or above, and");
            sb.AppendLine("choose the exact value from \"Scan identity range\" and from the biology of your");
            sb.AppendLine("proteins. Raising the allowance above 0 does not buy accuracy, it buys merges made");
            sb.AppendLine("inside the noise.");
            sb.AppendLine();

            sb.AppendLine("THE OTHER COLUMNS");
            sb.AppendLine("-----------------");
            sb.AppendLine("USearch ID       the last automatic grouping (USearch, then any reclustering the");
            sb.AppendLine("                 program ran). Read-only. It equals User ID except where you");
            sb.AppendLine("                 renumbered a family by hand.");
            sb.AppendLine("User ID          your own family number. Editable: change it and the families are");
            sb.AppendLine("                 regrouped, the User column recalculated, and it is the number");
            sb.AppendLine("                 written back by Tools > Reannotate SEPro.");
            sb.AppendLine("User Annotation  free text, saved in the .fcf and exported. It also NAMES the family");
            sb.AppendLine("                 in the Full XIC composition CSV, the Results chart and the");
            sb.AppendLine("                 evidence pack. A family without one is named by the protein name");
            sb.AppendLine("                 most of its members share (read before \"OS=\", \"(Fragment)\"");
            sb.AppendLine("                 ignored); a tie follows the stored member order, not the grid sort.");
            sb.AppendLine("Locus            a UniProt accession where available, otherwise the database identifier.");
            sb.AppendLine("                 Hover over a cell for the full original identifier, kept in the file.");
            sb.AppendLine("Residue mass     the sum of the protein's amino-acid residue masses, in daltons.");
            sb.AppendLine("                 It does NOT include the terminal water: add 18.011 Da for the");
            sb.AppendLine("                 neutral protein, and 1.007 Da more for the protonated (MH+) mass.");
            sb.AppendLine("                 The column used to be called \"Monoisotopic MH\", which this number");
            sb.AppendLine("                 has never been.");
            sb.AppendLine();
            sb.AppendLine("THE FAMILY COUNTER AND THE FILTERS");
            sb.AppendLine("----------------------------------");
            sb.AppendLine("The counter reads \"N shown of M in file\" and names only the active filters after");
            sb.AppendLine("\"hidden:\": decoys, contaminants, search or only quantified. Display and export filters hide rows;");
            sb.AppendLine("they never change a number: hiding decoys or contaminants lowers the visible count");
            sb.AppendLine("and leaves every percentage exactly as it was. In \"" +
                          NormalizationMode.IdentifiedSignal.ShortName() + "\" the hidden rows");
            sb.AppendLine("are still inside the denominator, because that denominator is the sum over all");
            sb.AppendLine("families, filtered or not.");
            sb.AppendLine("Decoys are hidden by default; Show automatic IDs reveals the retained USearch ID.");
            sb.AppendLine("Hide contaminants matches the case-sensitive prefix '" + SequenceLabels.ContaminantPrefix +
                          "', after one optional '" + SequenceLabels.DecoyPrefix + "' prefix.");
            sb.AppendLine("It includes labelled keratins, trypsin and serum proteins; hiding them affects the");
            sb.AppendLine("table and visible-row CSV only, so visible identified-signal shares can total below 100%.");
            sb.AppendLine();
            sb.AppendLine("File > Export opens one window for Report (HTML, Word and supplement workbook),");
            sb.AppendLine("Tables (CSV), Suggested additions (FASTA), Families (.fcf) and Interpretation input");
            sb.AppendLine("(Markdown). The Export shortcuts in Results and Curate families open the same window.");
            sb.AppendLine("The report contains the figures; separate screen-figure and presentation-pack exports");
            sb.AppendLine("are no longer offered. Visible protein table writes the rows you can see, with values as fractions");
            sb.AppendLine("between 0 and 1 and a dot as the decimal separator, whatever your machine's");
            sb.AppendLine("language is. That is on purpose: the file is meant to be read by a program.");

            sb.AppendLine();
            sb.AppendLine("Recovery by mass range divides measured peptide envelope signal by the detector's isotope-envelope area in the same 500 Da neutral-mass range, pooling run areas. Identified-equivalent de novo features are excluded. The view spans the first through last range with detector signal; blank ranges have no detector signal. It is not a share of TIC or a mean of run percentages; the independent detector is not peptide-FDR calibrated and ratios can exceed 100%. Older projects need Quantify again to record the detector ranges for this view.");
            sb.AppendLine();
            sb.AppendLine(HelpWorkflowFacts.FilesAndNavigation);
            sb.AppendLine(HelpWorkflowFacts.Families);
            sb.AppendLine(DeNovoWorkflowHelp.Build());
            sb.AppendLine();
            sb.AppendLine(PanelReportHelp.Build());
            return sb.ToString();
        }

        /// <summary>
        /// The De novo review screen's own help, opened by its Help button and by the main window's
        /// Help menu while that screen is the one on display.
        ///
        /// It is a separate page rather than a section of <see cref="Build"/> because it is a separate
        /// workflow: none of the family text above applies to it, and none of this applies to the family
        /// screen. It answers the questions this feature was actually asked: what the selected source is,
        /// what the three filters mean, why a chimeric spectrum is coverage rather than a duplicate,
        /// what the review queue decides and why nothing decides it automatically, what the extracted
        /// numbers count, what the decoy-only count is for, and what the three-way bar partitions.
        ///
        /// <paramref name="criteria"/> is null when the caller has no parsed criteria to hand; the
        /// defaults are then taken from <see cref="DeNovoFilterCriteria"/> itself, so the numbers quoted
        /// here cannot drift from the ones the screen starts with.
        /// </summary>
        public static string BuildDeNovo(DeNovoFilterCriteria? criteria = null,
                                         DeNovoSource? source = null)
        {
            DeNovoFilterCriteria c = criteria ?? new DeNovoFilterCriteria();
            source ??= DeNovoSource.DenovoOnly;
            string sourceName = string.Equals(source.Id, DeNovoSource.DenovoOnly.Id,
                    StringComparison.OrdinalIgnoreCase)
                ? "PEAKS de novo only"
                : string.Equals(source.Id, DeNovoSource.DeNovoAll.Id,
                    StringComparison.OrdinalIgnoreCase)
                    ? "PEAKS de novo all"
                    : source.DisplayName;
            string confidenceName = source.ConfidenceLabel;
            string confidenceShortName = confidenceName.EndsWith(" (%)", StringComparison.Ordinal)
                ? confidenceName.Substring(0, confidenceName.Length - 4)
                : confidenceName;
            var sb = new StringBuilder();

            sb.AppendLine("THE DE NOVO REVIEW SCREEN (OPTIONAL)");
            sb.AppendLine("====================================");
            sb.AppendLine(DeNovoWorkflowHelp.Build());
            sb.AppendLine("Selected source: " + sourceName + " (" + source.FileGlob + "; " +
                          confidenceName + ").");
            sb.AppendLine();
            sb.AppendLine("A separate workflow, not a step of the family one. It needs no Family Cluster");
            sb.AppendLine("File to start, it changes no family, no User ID and no quantitation, and it");
            sb.AppendLine("writes nothing except the CSV files you ask it for. It asks which de novo calls");
            sb.AppendLine("pass their source-specific filters and how comparable precursor hypotheses relate");
            sb.AppendLine("to the PatternLab search.");
            sb.AppendLine();
            sb.AppendLine("Nothing on this screen interrupts you with a dialog. A parameter it cannot");
            sb.AppendLine("accept, a folder with no export in it, a file that will not open: all of them");
            sb.AppendLine("are reported in the status line at the bottom, and the previous value or the");
            sb.AppendLine("previous result is left in force.");
            sb.AppendLine();

            sb.AppendLine("THE ORDER OF WORK");
            sb.AppendLine("-----------------");
            sb.AppendLine("This screen has two core steps, and an optional advanced section below them.");
            sb.AppendLine();
            sb.AppendLine("Step 1, Import and filter, reads the selected de novo export and keeps the confident");
            sb.AppendLine("sequences. Step 2, Reconcile and review, compares them with a PatternLab search");
            sb.AppendLine("of the SAME runs and lists comparable-precursor alternative hypotheses and weak");
            sb.AppendLine("agreements as a review queue.");
            sb.AppendLine("That queue is the one thing only this screen produces.");
            sb.AppendLine();
            sb.AppendLine("Quantify uses the selected sources and shared filters for full");
            sb.AppendLine("XIC computation and reported by Results through the .fqproj project, so the");
            sb.AppendLine("de novo set no longer has to carry the whole story on its own.");
            sb.AppendLine();
            sb.AppendLine("The collapsed Standalone homology (advanced) section maps the kept peptide");
            sb.AppendLine("sequences against a family file. It needs neither quantitation nor an open");
            sb.AppendLine("project, and it can run straight after Import and filter. After the search,");
            sb.AppendLine("open Results, choose Homology assignments (CSV), and press Export. Results");
            sb.AppendLine("retains that completed snapshot independently of full XIC.");
            sb.AppendLine();

            sb.AppendLine("WHAT THE SELECTED SOURCE LIST IS");
            sb.AppendLine("--------------------------------");
            sb.AppendLine(sourceName + " writes one " + source.FileGlob + " export per run.");
            if (source.IsDenovoOnly)
            {
                sb.AppendLine("These are spectra sequenced from fragments alone that the PEAKS database");
                sb.AppendLine("search did not identify. This is an export category, not proof that the");
                sb.AppendLine("sequence is correct or biologically novel.");
            }
            else
            {
                sb.AppendLine("This source is not restricted to PEAKS database-search misses. Treat it as");
                sb.AppendLine("a caller-specific sequence list; novelty is established only by the explicit");
                sb.AppendLine("PatternLab reconciliation below, never by the file name alone.");
            }
            sb.AppendLine();
            sb.AppendLine("A sequence read this way carries no protein and no statistical support from a");
            sb.AppendLine("search engine. It carries " + sourceName + "'s own " + confidenceName +
                          ", which is why this screen");
            sb.AppendLine("starts with a filter rather than with a count.");
            sb.AppendLine();
            sb.AppendLine("The folder is searched recursively, so the folder holding the per-run");
            sb.AppendLine("sub-folders is the right one to give, and every run of the export is read");
            sb.AppendLine("together. Runs are named by their FILE NAME everywhere on this screen, never by");
            sb.AppendLine("their position in a folder or in a project: a de novo export, a directory listing");
            sb.AppendLine("and a PatternLab project are not in the same order, and pairing them by");
            sb.AppendLine("position joins every spectrum against the wrong acquisition.");
            sb.AppendLine();

            sb.AppendLine("THE THREE DE NOVO QUALITY FILTERS");
            sb.AppendLine("---------------------------------");
            sb.AppendLine("A row is kept only if it clears all three. The third filter uses two boxes.");
            sb.AppendLine();
            sb.AppendLine("Minimum length (" + c.MinLength.ToString(CultureInfo.CurrentCulture) + " residues)");
            sb.AppendLine(Wrap("A short call carries too little information to be matched against a " +
                               "protein afterwards. Lowering this is what turns the homology step of " +
                               "standalone homology into a random-match generator.", "    "));
            sb.AppendLine();
            sb.AppendLine("Minimum " + confidenceShortName + " (" +
                          c.MinAverageLocalConfidence.ToString("0.###", CultureInfo.CurrentCulture) + "%)");
            string confidenceExplanation = string.Equals(confidenceShortName, "ALC",
                StringComparison.OrdinalIgnoreCase)
                ? "ALC is PEAKS' average local confidence: the mean of the per-residue confidences " +
                  "of one call. It is the blunt filter; the local-confidence stretch below asks a " +
                  "different question."
                : confidenceShortName + " is this source's native per-call confidence.";
            sb.AppendLine(Wrap(confidenceExplanation + " It is an operational threshold, not a " +
                               "calibrated de novo FDR, and it is not interchangeable with another " +
                               "caller's score.", "    "));
            sb.AppendLine();
            sb.AppendLine("Stretch length (" + c.StretchLength.ToString(CultureInfo.CurrentCulture) +
                          " residues) and stretch floor (" + c.StretchFloor.ToString(CultureInfo.CurrentCulture) + "%)");
            sb.AppendLine(Wrap("One criterion in two boxes, and the sharp one: somewhere in the sequence " +
                               "there must be a run of that many CONSECUTIVE residues, each of them at " +
                               "or above that confidence. A residue below the floor breaks the run. This " +
                               "is what keeps the sequence with one trustworthy region and drops the one " +
                               "a global score can flatter; it complements rather than replaces the " +
                               "source-native score cutoff.", "    "));
            sb.AppendLine();
            sb.AppendLine("Counts depend on the selected source and cutoffs. The counters on this screen show the loaded data,");
            sb.AppendLine("including how many rows were read and kept; the per-run breakdown shows each acquisition.");
            sb.AppendLine("Loosening the filters is a decision about how much uncertain sequence you carry");
            sb.AppendLine("into every later step. The");
            sb.AppendLine("per-run breakdown under the counters is where to look for a run that behaved");
            sb.AppendLine("differently from the others.");
            sb.AppendLine();

            sb.AppendLine("THE FIVE VERDICTS, IN PLAIN LANGUAGE");
            sb.AppendLine("------------------------------------");
            sb.AppendLine("Reconcile first joins by normalized acquisition file and MS2 scan. It then requires");
            sb.AppendLine("the same reported charge and a direct precursor m/z match within 20 ppm before");
            sb.AppendLine("comparing sequences. Reconcile applies the tolerance to the reported m/z values.");
            sb.AppendLine("Quantify's PatternLab-wins rule and cross-side suppression use isotope-aware matching.");
            sb.AppendLine("The scan rule groups calls with the same run, scan and charge by reported precursor m/z.");
            sb.AppendLine("Suppression requires the same run, canonical peptidoform and charge, then compares neutral precursor masses.");
            sb.AppendLine("Both permit direct 20-ppm agreement or a one- or two-isotope selection correction.");
            sb.AppendLine("Each filtered row from the selected source receives one of five verdicts:");
            sb.AppendLine();
            sb.AppendLine("De novo only        nothing in the search shares that spectrum. This is new");
            sb.AppendLine("                    relative to PatternLab at the file-and-scan level; the");
            sb.AppendLine("                    current screen reports the count for the loaded source.");
            sb.AppendLine("Different precursor the scan is shared, but no PatternLab PSM has both the same");
            sb.AppendLine("context             charge and m/z within 20 ppm. No sequence comparison is made.");
            sb.AppendLine("Redundant equal     same file, scan, charge, m/z and sequence once the notation");
            sb.AppendLine("                    is normalised. It only confirms what PatternLab has.");
            sb.AppendLine("Equal but weak PSM  same comparable precursor and sequence, but PatternLab's own");
            sb.AppendLine("                    cross-correlation is below the threshold.");
            sb.AppendLine("Overlapping         same file, scan, charge and m/z, but a different sequence.");
            sb.AppendLine("alternative         It is excluded from additive de novo quantification and kept");
            sb.AppendLine("                    for review. It is not a database-search accuracy estimate.");
            sb.AppendLine();
            sb.AppendLine("The loaded data determines how many rows share a file and scan, then a charge and");
            sb.AppendLine("20-ppm precursor m/z, before strict or I/L-folded sequence comparison. Read the");
            sb.AppendLine("on-screen counters and comparison table for the current source and cutoffs.");
            sb.AppendLine();
            sb.AppendLine("The different-precursor-context verdict is worth understanding. A mass spectrometer");
            sb.AppendLine("isolates a window, not a molecule, so two species eluting together are");
            sb.AppendLine("fragmented into ONE spectrum: a chimeric spectrum. PatternLab explained one of");
            sb.AppendLine("them; the de novo call may be an alternative ion in that window. The scan number");
            sb.AppendLine("alone cannot establish duplication, sequence conflict, or biological truth.");
            sb.AppendLine();
            sb.AppendLine("That is why scan number alone is never used to claim equality. Charge and m/z are");
            sb.AppendLine("explicit gates; failing either means a different precursor context, not a conflict.");
            sb.AppendLine();

            sb.AppendLine("THE DATABASE-SEARCH AGREEMENT CONTROL");
            sb.AppendLine("-------------------------------------");
            sb.AppendLine("The valid search-vs-search control compares PatternLab PSMs with PEAKS db.psms,");
            sb.AppendLine("not with a de novo export. It compares shared file+scan keys, reported charge and");
            sb.AppendLine("exact sequence after removing PTM notation. The counts and agreement percentages");
            sb.AppendLine("depend on the loaded searches; use the loaded project's database-search concordance table.");
            sb.AppendLine();

            sb.AppendLine("THE REVIEW QUEUE, AND WHY IT IS MANUAL");
            sb.AppendLine("--------------------------------------");
            sb.AppendLine("The queue holds exactly two of the five verdicts: overlapping alternatives, and");
            sb.AppendLine("agreements where PatternLab's own evidence was weak. The other three are");
            sb.AppendLine("results, and a result does not need a decision.");
            sb.AppendLine();
            sb.AppendLine("An overlapping alternative is a de novo hypothesis on a comparable precursor.");
            sb.AppendLine("Nothing in either");
            sb.AppendLine("answer decides which is right: a cross-correlation and " + confidenceName + " are not on the");
            sb.AppendLine("same scale, are not calibrated against each other, and are not measuring the");
            sb.AppendLine("same thing. An automatic rule here would be an invented threshold dressed as a");
            sb.AppendLine("result, and it would be invisible afterwards.");
            sb.AppendLine();
            sb.AppendLine("So the program presents the evidence and decides nothing. The table is");
            sb.AppendLine("read-only and shows both sequences side by side with the cross-correlation on");
            sb.AppendLine("the PatternLab side and " + confidenceName + " plus the weakest residue confidence on the de");
            sb.AppendLine("novo side, plus how far apart the two precursors were in ppm. It is a");
            sb.AppendLine("review set for the loaded source. Export it and");
            sb.AppendLine("the evidence can be inspected away from this screen. These rows are not added to");
            sb.AppendLine("the de novo signal while unresolved, so review cannot inflate quantification.");
            sb.AppendLine();
            sb.AppendLine("An equal-but-weak row is retained in the review export rather than labelled as an");
            sb.AppendLine("ordinary redundant confirmation. It is excluded from additive de novo quantification");
            sb.AppendLine("until reviewed, so the evidence remains visible without double-counting its signal.");
            sb.AppendLine();

            sb.AppendLine("The following legacy definitions remain as scientific reference for old files.");
            sb.AppendLine("Their standalone extraction and partition controls were retired from De novo review");
            sb.AppendLine("in 2.0; use Quantify and Results for full XIC.");
            sb.AppendLine();
            sb.AppendLine("LEGACY EXTRACTION SEMANTICS (REFERENCE)");
            sb.AppendLine("----------------------------------------");
            sb.AppendLine("The legacy extraction engine opens a run's RAW once and integrates every kept");
            sb.AppendLine("precursor, at its own anchor scan. The acquisitions are found BY FILE NAME, one");
            sb.AppendLine("run is held in memory at a time, and the results are cached beside the RAW");
            sb.AppendLine("files so a second session over the same runs costs seconds instead of minutes.");
            sb.AppendLine();
            sb.AppendLine("M+0 is the monoisotopic peak alone: the isotope that carries no heavy carbon.");
            sb.AppendLine("The whole isotope envelope (M+0 plus M+1 plus M+2) is measured beside it, but");
            sb.AppendLine("only as a diagnostic in the legacy three-way comparison described below.");
            sb.AppendLine("PatternLab's stored quants are M+0, and one bar cannot be built from two");
            sb.AppendLine("different bases without saying something false about the comparison.");
            sb.AppendLine();
            sb.AppendLine("The counts come in TWO GRAINS and the column headings say which:");
            sb.AppendLine();
            sb.AppendLine("    ANCHORS   one request per MS2 scan that fired on that precursor. \"Rows");
            sb.AppendLine("              in\", \"unique precursors\" and \"with signal\" are anchors.");
            sb.AppendLine("    PEAKS     one integrated elution peak. \"Distinct peaks\" and every area");
            sb.AppendLine("              are peaks.");
            sb.AppendLine();
            sb.AppendLine("They differ because one elution peak is usually sampled by several MS2 scans:");
            sb.AppendLine("several anchors, one peak, and the extractor hands that same peak back to each");
            sb.AppendLine("of them. Legacy extraction totals are summed over DISTINCT PEAKS. Summing per");
            sb.AppendLine("anchor instead would add the same trapezoid once per MS2 event, which inflates");
            sb.AppendLine("the ion current by a few percent, always upwards, and never fails a check. The");
            sb.AppendLine("\"redundant anchors\" and \"redundant area\" columns report exactly what that");
            sb.AppendLine("mistake would have added.");
            sb.AppendLine();
            sb.AppendLine("The \"shared-window bound\" column is a BOUND, not a correction. Two peaks of the");
            sb.AppendLine("same precursor inside one extraction window can be two genuine elution events");
            sb.AppendLine("across a long gradient, or one broad peak entered twice; telling them apart");
            sb.AppendLine("needs each peak's scan span, which this extraction does not record. So they are");
            sb.AppendLine("all kept, and the most that could still be double-counted (each window's total");
            sb.AppendLine("less its largest peak) is printed instead of being quietly removed.");
            sb.AppendLine();

            sb.AppendLine("WHAT STANDALONE HOMOLOGY MAPS");
            sb.AppendLine("--------------------------------------");
            sb.AppendLine("A de novo sequence has no protein attached. Standalone homology asks whether it looks");
            sb.AppendLine("like one anyway: each DISTINCT kept sequence is aligned against the proteins of");
            sb.AppendLine("a Family Cluster File, and every surviving hit is attributed to the User ID");
            sb.AppendLine("family of the protein it landed in. One file answers both halves, which is why");
            sb.AppendLine("a hit can never land in a protein the family table does not know.");
            sb.AppendLine();
            sb.AppendLine("The sequences are deduplicated first: the same tag read in fifty spectra is one");
            sb.AppendLine("alignment, not fifty, so \"peptides searched\" is far below the rows kept in");
            sb.AppendLine("Import and filter. The minimum identity is a PREFILTER, not the score: a protein that");
            sb.AppendLine("never reaches that fraction of literal identity is never aligned at all.");
            sb.AppendLine("Isoleucine and leucine are treated as one residue there by default, because");
            sb.AppendLine("they have the same mass and a fragment spectrum cannot tell them apart, so");
            sb.AppendLine("punishing the engine's arbitrary choice between them would be punishing physics.");
            sb.AppendLine();
            sb.AppendLine("WHAT THE DECOY-ONLY COUNT MEANS. It is a CONTROL, and it is not an FDR. The");
            sb.AppendLine("family file carries reversed sequences beside the real ones. A peptide that");
            sb.AppendLine("found only reversed proteins was attributed to something that does not exist,");
            sb.AppendLine("so the rate of that is the rate at which this identity threshold invents a");
            sb.AppendLine("family out of chance similarity. Read \"mapped to targets\" beside it and never");
            sb.AppendLine("on its own: if the two are of the same size, the threshold is too low, whatever");
            sb.AppendLine("the mapped number looks like alone.");
            sb.AppendLine();
            sb.AppendLine("It is not an FDR because nothing here is calibrated into a corrected error rate");
            sb.AppendLine("per hit: there is no competition between a target and its decoy for one");
            sb.AppendLine("spectrum, and no q-value is computed or implied. It is one number to read the");
            sb.AppendLine("other against. A peptide that found both keeps its targets and drops its");
            sb.AppendLine("decoys; a peptide that found only decoys keeps them, which is the entire point");
            sb.AppendLine("of having them, and it stays out of the family table below.");
            sb.AppendLine();
            sb.AppendLine("Most kept sequences match nothing at all, and that is not a defect. They are");
            sb.AppendLine("the reason a de novo search was run.");
            sb.AppendLine();

            sb.AppendLine("LEGACY THREE-WAY COMPARISON (REFERENCE)");
            sb.AppendLine("---------------------------------------------");
            sb.AppendLine("The legacy comparison has a bar per run and one pooled bar. It uses the WHOLE ion");
            sb.AppendLine("current, read from the RAW with no trimming at either end, and it is cut into");
            sb.AppendLine("three exhaustive parts:");
            sb.AppendLine();
            sb.AppendLine("    PatternLab   the ion current of the peptides the database search");
            sb.AppendLine("                 identified and quantified.");
            sb.AppendLine("    De novo      what the kept de novo set adds on top of that.");
            sb.AppendLine("    Remainder    what is left, and still has no owner.");
            sb.AppendLine();
            sb.AppendLine("The three shares add up to 100% by construction, because the remainder is");
            sb.AppendLine("defined as what the other two leave. That is arithmetic, not a result, and the");
            sb.AppendLine("thing that had to be earned instead is COMPARABILITY: both of the first two");
            sb.AppendLine("numerators are monoisotopic, and both count one integrated peak once.");
            sb.AppendLine();
            sb.AppendLine("A NEGATIVE remainder is never clamped to zero. It is a finding: it says the two");
            sb.AppendLine("numerators together claim more signal than the run's chromatogram holds, and");
            sb.AppendLine("hiding it would hide exactly the case worth knowing about.");
            sb.AppendLine();
            sb.AppendLine("THE PROVENANCE WORDS. Each number of the table carries one of three, because");
            sb.AppendLine("the three parts arrive by three different routes and only two of them are");
            sb.AppendLine("measurements:");
            sb.AppendLine();
            sb.AppendLine("    measured   computed here, from the RAW file, by the machinery named beside");
            sb.AppendLine("               the word. The total ion current and the de novo areas are this.");
            sb.AppendLine("    recorded   read out of a file that had written it down: a divisor recorded");
            sb.AppendLine("               in the .plp, or the stored quants of a .xic2. The PatternLab");
            sb.AppendLine("               part is this.");
            sb.AppendLine("    derived    neither measured nor read, but obtained from the others by");
            sb.AppendLine("               arithmetic. The remainder is always this, and that is why it");
            sb.AppendLine("               can come out negative.");
            sb.AppendLine();
            sb.AppendLine("The word alone is not enough, so the source is printed with it: \"recorded\"");
            sb.AppendLine("does not distinguish a divisor read from a .plp from quants summed out of a");
            sb.AppendLine(".xic2, and those are different measurements of different things.");
            sb.AppendLine();
            sb.AppendLine("The legacy branch note records which source actually answered WORD FOR WORD,");
            sb.AppendLine("refusals included. A project that merged its technical replicates holds one");
            sb.AppendLine("averaged column for three acquisitions and cannot answer per run; no arithmetic");
            sb.AppendLine("recovers what was averaged away, so it says so and the .xic2 is used instead.");
            sb.AppendLine("Reducing that to a flag would hide which of the two you are looking at.");
            sb.AppendLine();
            sb.AppendLine("Every inspection or Results CSV uses a dot for the decimal separator whatever your");
            sb.AppendLine("machine's language is. Legacy partition numbers are written in their shortest");
            sb.AppendLine("round-trippable form, so a value read back is the same bits that were plotted.");
            sb.AppendLine();
            sb.AppendLine(HelpWorkflowFacts.DeNovo);
            return sb.ToString();
        }

        /// <summary>
        /// Hard-wraps a paragraph at 76 columns with an indent, so the panel reads the same whatever the
        /// window is doing and the assertions in the test suite see a stable shape.
        /// </summary>
        private static string Wrap(string text, string indent)
        {
            var sb = new StringBuilder();
            var line = new StringBuilder(indent);

            foreach (string word in (text ?? string.Empty).Split(' '))
            {
                if (word.Length == 0) continue;

                if (line.Length > indent.Length && line.Length + 1 + word.Length > 76)
                {
                    sb.AppendLine(line.ToString());
                    line.Clear();
                    line.Append(indent);
                }

                if (line.Length > indent.Length) line.Append(' ');
                line.Append(word);
            }

            if (line.Length > indent.Length) sb.Append(line.ToString());

            return sb.ToString();
        }
    }
}
