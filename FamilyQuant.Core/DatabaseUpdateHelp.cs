namespace FamilyQuant;

public static class DatabaseUpdateHelp
{
    public const string Text = "UPDATE THE PROTEIN DATABASE\n"
        + "1. Save the open project, then choose File > Update database and select the newer FASTA or target-decoy file.\n"
        + "2. FamilyQuant compares records and places added proteins, with progress and Cancel; the open project is unchanged during preparation.\n"
        + "3. Review the changes, ambiguous pairs and Keep the old name choices; changing a pair requires placement to run again.\n"
        + "4. Apply update changes the open project; File > Save keeps it, while Save as keeps both versions. Earlier Undo steps are cleared because protein rows may have changed; close without saving to return to the saved project.\n\n"
        + "Retained proteins keep their family numbers, locks and curation. Removed records leave the database; added proteins join one retained family or form a new family, and existing families are kept separate. Inspect additions close to several families and additions joining locked families in the preview. Build families remains available for a full rebuild.\n"
        + "Sequence changes and new accessions clear their old protein evidence; added proteins start without evidence. Use Fetch evidence, confirm the offered scope in Group by evidence, and run Quantify again to refresh Results.\n"
        + "Stored quantitation and spectrum family columns remain as computed against the previous database. Search PatternLab again with the new file for identifications of new proteins; until then additions can receive de novo signal or identified peptides shared with retained proteins.\n"
        + "Decoy or contaminant side mismatches refuse the update. Low overlap or many additions warns that a rebuild may be more suitable. Cancel or failure before the update commits leaves the project unchanged.\n"
        + "The saved update record retains source hashes, matching and placement methods, changes and family-name choices. Export changes as CSV keeps the preview or saved rows, including ambiguity picks, placing relatives and user-visible notes.\n";
}
