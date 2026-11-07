using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyQuant;

/// <summary>One part of the import chapter: its title, its paragraphs, and the wizard picture that goes with
/// it (a file in Help/ImportFamilyList, or empty for a part with no picture).</summary>
public sealed record FamilyListImportGuideSection(string Key, string Title, string Image, string Caption, IReadOnlyList<string> Paragraphs);

/// <summary>
/// The Help chapter "Import family list" (Z12p2c): one section for each page of File > Import family list,
/// with a picture of that page taken on the fictitious example list (Try with the example), plus the two routes
/// that save evidence matches later. Wording follows the wizard and the matching window's own Help; Z12p2c1 brings it
/// to the final wizard (the decision-column question, colours named by the program, no conditional action, labels the
/// program already uses confirmed at once, the Done page's next steps). The suite checks every control it names
/// against the program's own sources.
/// </summary>
public static class FamilyListImportGuide
{
    public const string Title = "Import family list";

    public static IReadOnlyList<FamilyListImportGuideSection> Sections { get; } = new[]
    {
        new FamilyListImportGuideSection("start", "Before you start", "", "", new[]
        {
            "File > Import family list brings a spreadsheet of family abbreviations and names into this project's family panel, in seven pages. Every page before Import is a draft: Back keeps your choices, and Cancel changes nothing.",
            "Import creates the family panel and nothing else. It does not group proteins or recompute quantitation; afterwards you review which proteins belong in each family in the Family checklist of Group by evidence.",
            "To practise, press Try with the example on the first page. It opens an invented list for a secreted-protein mixture, written in Portuguese like many consensus sheets, with a title above the column names, coloured abbreviations, a row naming four families and a repeated abbreviation. Every value in it is made up. The pictures in this chapter were taken on it.",
        }),
        new FamilyListImportGuideSection("welcome", "1. Welcome", "01-welcome.png", "The first page: what you need, what is kept, and the three ways in.", new[]
        {
            "The page lists what you need: an Excel workbook (.xlsx) with an abbreviation column and a family name column. Names in another language, notes and the original rows are kept with your list.",
            "Try with the example opens the invented list on the next page. Save a template writes a workbook with the recommended columns, which imports with no mapping. Edit current panel opens the panel editor for the panel this project already has.",
        }),
        new FamilyListImportGuideSection("file", "2. File and sheet", "02-file-and-sheet.png", "The example's first sheet: a title and a subtitle above the column names in row 3.", new[]
        {
            "Press Browse, or drop the workbook on the page, then choose the sheet that holds your families. FamilyQuant proposes the header row, the row with the column names; in the example it is row 3, below a title and a subtitle. If the proposal is wrong, type the right row number and press Read this header row.",
            "The spreadsheet preview shows your sheet as it is, with its row numbers, column letters, merged titles, accents, line breaks and cell colours. Colours are source data and never decide anything by themselves. Select a cell and open Cell details to read its full text.",
            "Other sheets are left out unless you tick Keep as source notes; kept sheets are stored as text with the panel and never read as rules. The workbook itself is never changed.",
        }),
        new FamilyListImportGuideSection("columns", "3. Columns", "03-columns.png", "Each meaning with the column proposed for it and an example from the sheet.", new[]
        {
            "For each meaning, choose the column of your sheet that holds it; an example from your sheet appears below each choice. Abbreviation and Family name are required. Other-language name, Notes, Pfam codes and Decision are optional, and every original column is kept anyway.",
            "When no column is chosen for Decision and one column holds a few short labels repeated over many rows, the page can ask whether that column says what to do with each family; in the example it asks: Could Decisão FamilyQuant hold the family actions? It is only a question. Use this decision column chooses that column and opens its values for your review; until you press it, nothing is mapped.",
            "Without a Decision column every family is Quantify, so Next asks once: Quantify every family for this import? No keeps you on Columns. Your answer holds for this sheet until you change the Decision column.",
            "When the sheet has coloured cells, Colour categories (optional) opens by itself. Choose the column whose colours mean something, like Sigla in the example: each colour is listed with its rows and named for you with its plain name, such as light blue. Rename a category if you like; clearing a name brings the plain name back, so the colours never hold up Next. The category is kept as text with each family and shown in the Colour column of the Summary; it never decides what happens to a family.",
        }),
        new FamilyListImportGuideSection("decisions", "3. Reviewing decisions", "03-columns-decisions.png", "One line per decision written in the sheet, with its rows and the proposed action.", new[]
        {
            "When you choose a Decision column, the page becomes Review spreadsheet decisions: one line for each different value written in that column, the rows that use it, and the proposed action: Quantify, Report under others, Not a family, or Merge into family with the abbreviation to merge into. The line under the title says it in short: " + FamilyListImportHelp.DecisionsSummary,
            "There is no conditional action. A condition written in the sheet, like Condicional (só na fração solúvel) in the example, is proposed as Quantify; choose the action that fits now. The value written in your sheet stays with each family as its source label.",
            "A cell that already holds one of the program's own labels, Quantify, Report under others or Not a family, or Merge into followed by an abbreviation of your list, is confirmed at once. When every value is like that, there is nothing to review.",
            "Check each other line, change what is wrong, and press Confirm these actions. Next stays disabled until every value is confirmed, and changing an action or its abbreviation clears that line's Confirmed tick, so no proposal is accepted for you. Back to columns returns without confirming, and Review decision values opens the list again.",
        }),
        new FamilyListImportGuideSection("special", "4. Special rows", "04-special-rows.png", "A cell naming four families, with its choice of how to import it.", new[]
        {
            "Rows that cannot be imported as written wait here, each with its row number, the problem and a proposed correction. Next waits until no row says Needs a choice. Open Original source row to see the row as written, choose in Use this row as, and press Use this choice.",
            "A cell that lists several names, like the OUTRAS row of the example with four rare families, can become One family per name or One group reported under others; Keep as one family imports it as written. When such a row becomes one family per name, its Pfam codes are kept as text only, because they cannot be told apart for each name. A comma inside a single name looks the same to the importer, as in row 17 of the example: choose Keep as one family there.",
        }),
        new FamilyListImportGuideSection("duplicate", "4. A repeated abbreviation", "04-special-rows-duplicate.png", "The second LEC, proposed as LEC-2.", new[]
        {
            "An abbreviation used twice, like the second LEC of the example, can be kept with a new abbreviation (Keep or rename this row, proposed as LEC-2), merged into the family of the first row (Merge into family), or left out (Skip this row).",
            "Empty and decorative rows are skipped and listed with the status Skipped, so no row disappears without a trace.",
        }),
        new FamilyListImportGuideSection("matches", "5. Evidence matches", "05-evidence-matches.png", "Suggestions for one family from the evidence stored in the project, none ticked until you tick them.", new[]
        {
            "For each family, FamilyQuant suggests labels from the evidence already stored in this project, such as UniProt families and keywords, that share words with the family's names. Nothing is ticked for you.",
            "Counts are database entries for each suggestion, not abundance; suggestions can overlap and must not be added together.",
            "Tick the labels that describe the family and press Use selected matches, or press Skip this family. Continue without matches skips matching for every family; you can match later. On a new import of the same list, matches confirmed before show as Kept from previous panel. The wizard never downloads evidence and never assigns proteins.",
            "A broad label can describe most of the database rather than one family. If a ticked label covers more than half of the database's entries, Use selected matches asks once, naming the count, before it keeps the match.",
        }),
        new FamilyListImportGuideSection("later", "Saving matches later", "", "", new[]
        {
            "Matches confirmed on page 5 are imported with the panel. Two routes save matches later, as the matching window's Help explains.",
            "With a grouping: in Group by evidence, open the Family checklist, use Match stored names for a family and confirm the labels to use. Apply grouping and File > Save keep the choices made in that review.",
            "Without grouping proteins: open File > Import family list, press Edit current panel, select the family, use Match stored names, then Apply panel and File > Save.",
        }),
        new FamilyListImportGuideSection("summary", "6. Summary", "06-summary.png", "The last check before anything changes.", new[]
        {
            "The last page before anything changes. Name the panel, then read the line of counts (families, rows split, skipped and renamed, matching choices), the source sheet, and the panel this import replaces. The table lists every family with its names, decision, colour category (in the Colour column) and matching choice; View source notes shows the original columns kept for it.",
            "Import replaces the current panel, if any, in one step. Just before, the workbook and the project are checked again; if either changed, nothing is imported. Back keeps your choices, and Cancel changes nothing.",
        }),
        new FamilyListImportGuideSection("done", "7. Done", "07-done.png", "The list is imported; the workbook and the protein grouping are unchanged.", new[]
        {
            "The family list is imported. The workbook and the protein grouping are unchanged, and the project is not saved yet: File > Save keeps the panel with it.",
            "The line under the title names the next steps: " + FamilyListImportHelp.DoneNext + " Family checklist closes the wizard and opens Group by evidence. When its Read database and scope step is done, press Update proposal; the review then shows its Family checklist tab, where you review which proteins belong in each family.",
            "The page also says: " + FamilyListImportHelp.MatchingEffect + " Importing the list and confirming matches move no protein by themselves. To place proteins by hand, use Place outside matches by hand in the Family checklist.",
            "Undo import restores the previous panel while the import is still the latest change. Close keeps the imported panel.",
        }),
        new FamilyListImportGuideSection("keyboard", "Keyboard", "", "", new[]
        {
            "Tab moves through the controls in reading order, and the arrow keys work in tables and lists. Enter presses the forward button when you are not editing a table. Escape cancels before Import and closes after it. F1 opens this chapter.",
        }),
    };

    /// <summary>The whole chapter as plain text, for copying or for a text-only view.</summary>
    public static string Text()
    {
        var text = new StringBuilder(Title.ToUpperInvariant()).Append("\n\n");
        foreach (var section in Sections)
        {
            text.Append(section.Title.ToUpperInvariant()).Append('\n');
            foreach (string paragraph in section.Paragraphs) text.Append(paragraph).Append("\n\n");
        }
        return text.ToString().TrimEnd() + "\n";
    }

    /// <summary>The pack address of a section's picture inside the program.</summary>
    public static Uri ImageUri(FamilyListImportGuideSection section) =>
        new("pack://application:,,,/" + typeof(FamilyListImportGuide).Assembly.GetName().Name + ";component/Help/ImportFamilyList/" + section.Image, UriKind.Absolute);

    public static IEnumerable<FamilyListImportGuideSection> Pictured => Sections.Where(s => s.Image.Length > 0);
}
