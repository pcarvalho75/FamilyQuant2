namespace FamilyQuant;

/// <summary>Retained workflow facts used by existing screen Help surfaces.</summary>
public static class HelpWorkflowFacts
{
    public const string FilesAndNavigation =
        "FILES, NAVIGATION AND KEYBOARD\n" +
        "Home steps also have sidebar and menu commands; the status bar names the current stage and open project and explains what to do next. Tab and Shift+Tab move between enabled controls, and the native focus indicator marks the active control.\n\n" +
        "If no project is open, nothing is saved: use File > Open for a .fqproj or .fcf, or File > New from database to build families. File and row counts use whole numbers with digit grouping and no decimal places.\n\n" +
        "File > Save keeps families, User IDs, annotations and computed quantitation together in one .fqproj; use Save as to keep the previous version. The .fcf export keeps the families and annotations without the full XIC result.\n\n" +
        "Results reads stored full-envelope quantitation, including family shares and total-ion-current recovery, without reading RAW or changing families. Use Quantify first when the project has no stored quantitation, then save the project to keep it.\n\n" +
        "The spectrum comparison is read-only, with one row per file, scan, charge and precursor m/z hypothesis, comparing PEAKS, DeepNovo and PatternLab. Its CSV export names the written CSV path in the completion message.\n\n";

    public const string Build =
        "BUILD WORKFLOW DETAILS\n" +
        "Build families uses USearch sequence identity on a FASTA or target-decoy database once per database, writes a .fcf beside it, and asks before replacing that file. Open the resulting .fcf with File > Open; skip Build when an existing .fcf or .fqproj is available.\n\n" +
        "The database chooser offers All files (*.*) for an input whose extension is not listed. The file still needs to contain a supported protein database.\n\n" +
        "Higher identity gives more, smaller families; lower identity gives fewer, larger ones, and Scan identity range compares the values before choosing a threshold. The scan writes no family file and a large database can take a while.\n\n" +
        "Find lowest identity requires a target-decoy database and only reports a number: type the reported value into Identity, then Build families. Treat it as a floor, not a recommended threshold.\n\n" +
        "Exhaustive second pass aligns every pair after the fast pass and rejoins families that pass split; turn it off only for speed. It starts on, and asks before processing more than 10,000 sequences.\n\n" +
        "For a target-decoy database, zero target-decoy mixing is the desired value; without decoys, zero alone carries no evidence about mixing. The status line describes the current action and what to do next.\n\n" +
        "Remove redundancy at identity 100 removes exact subsets and duplicates; lower values also remove near-identical sequences, and lower values are slower. It writes <name>_NO_REDUNDANCY.fasta beside the input, preserving order and headers and leaving the original unchanged.\n\n";

    public const string Families =
        "CURATION AND FAMILY SHARES\n" +
        "The recorded identity is the threshold used to build the file; higher identity gives more, smaller families, and changing it requires a new Build run. Align selected opens an alignment in a new window to check whether the selected family members are homologous.\n\n" +
        "Fetch evidence needs internet when the requested facts are not already cached; retrieved Pfam domains fill Domains, can be found by Search and can be selected in the grouping preview. Saving keeps them in the file, and family numbers stay unchanged by fetching.\n\n" +
        "Recluster by words merges descriptions meeting the similarity threshold, from 0.10 to 1.00; default 0.95 keeps near-identical wording, and lower it towards 0.90 to gather related descriptions. Family numbers change and percentages are recalculated; Undo restores the previous curation.\n\n" +
        "Ignore words accepts commas or spaces and is useful when a common word pulls unrelated families together. Ctrl+A selects all rows in the filtered protein view; Ctrl or Shift selects individual rows or ranges for User ID and annotation edits.\n\n" +
        "PatternLab projects must be exported at PEPTIDE level; a protein-level export makes every row read 0. The optional PatternLab share column stays empty until a project is loaded in PatternLab (optional), and Recalculate is unavailable until then.\n\n" +
        "For Full XIC, quantified signal gives family composition and total ion current gives each family's share of the whole sample, using the summed TIC of every recorded run. A Full XIC basis change applies at once.\n\n" +
        "The Full XIC summary accounts for families, peptides shared between families and peptides with no family. Export family composition writes CSV with one row per family and an unattributed row; its shared-peptide row appears only with Family-unique only.\n\n" +
        "Show quantified families only starts off and hides unquantified families without changing percentages. Hide decoys hides identifiers starting with " + SequenceLabels.DecoyPrefix + " from the table and CSV, without deleting data or changing percentages; show them while judging the clustering.\n\n" +
        "Manage locks is unavailable while no family is locked; removing a lock changes the open file immediately, and saving keeps it on disk. Apply grouping gathers the ticked proteins into one family and can be reversed with Undo; it is unavailable when there is no change to apply.\n\n";

    public const string Quantify =
        "QUANTIFY WORKFLOW DETAILS\n" +
        "Compute requires at least the RAW folder and a trusted .sepr2 identification package from the same runs; trust is required because the legacy package reader uses unsafe deserialization. Add a de novo export root if available, select its source, then press Quantify and save the project afterwards.\n\n" +
        "Runs match RAW acquisitions by file name and are read one acquisition at a time; the first extraction can take a while. Unchanged inputs can reuse a validated extraction cache for a second computation.\n\n" +
        "Type the folder containing .raw acquisitions in RAW folder and the .sepr2 path in Identifications. The optional de novo root is searched recursively and only the selected sources are read and filtered; leave it empty for identified peptides only.\n\n" +
        "PEAKS de novo only reads *.denovoOnly.csv, PEAKS de novo all reads *.denovo.csv and DeepNovo reads *.deepnovo.csv. PEAKS ALC and DeepNovo SCORE are different confidence scales and are never mixed: a SCORE of 80 is not a PEAKS ALC of 80, and each source has its own 0 to 100 cutoff, default 80, applying only to its own calls.\n\n" +
        "Quantify can combine PEAKS and DeepNovo sources. A precursor called by both is measured once under the PEAKS call, with the DeepNovo call kept beside it.\n\n" +
        "The isotope ceiling ranges from 1 to 6, default 6; a missing isotope adds nothing, and 1 credits only M+0, the basis of PatternLab's stored reading. Keep 6 for the full envelope; legacy heights are not comparable with integrated areas.\n\n" +
        "Minimum points per peak ranges from 1 to 20 and is saved with the project; default 3 requires at least one real detection, while 5 requires at least three because two padding points are counted. Setting 1 switches the point-count gate off and restores the legacy height rule.\n\n" +
        "The displayed isotope ceiling, minimum points, extraction window and MS1 tolerance are the next computation's settings. Window and MS1 tolerance stay fixed to the production values used for the stored quants.\n\n" +
        "The spectrum comparison CSV is written only after a successful computation and contains the same rows as the spectrum table. Before a completed result exists there is no spectrum table to show; complete full XIC first, then inspect its exact result.\n\n" +
        "Status reports progress, per-run counts and refusals; failed validation means nothing was computed. A missing or unreachable file is named in the refusal so its path can be corrected.\n\n";

    public const string DeNovo =
        "DE NOVO WORKFLOW DETAILS\n" +
        "De novo review is optional inspection only and accepts one source or a union of PEAKS and DeepNovo; Quantify can combine sources and reads its de novo folder independently. Review changes no family or quantitation, so it is not needed for quantitation.\n\n" +
        "With a project, use Quantify and Results for extraction and reporting. Standalone family attribution reads peptides, not chromatograms, so it can run directly after importing and filtering the calls.\n\n" +
        "Results > Homology assignments exports CSV with one line per searched peptide, including family IDs, protein IDs and ambiguity. It is unavailable until a homology search result exists, including a result retained from an earlier search.\n\n";
}
