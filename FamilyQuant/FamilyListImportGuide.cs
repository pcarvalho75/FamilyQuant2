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
/// with a picture of that page taken on a synthetic list, plus the two routes
/// that save evidence matches later. Wording follows the wizard and the matching window's own Help; Z12p2c1 brings it
/// to the final wizard (the decision-column question, colours named by the program, no conditional action, labels the
/// program already uses confirmed at once, the Done page's next steps). Z16a8 adds a workbook with several sheets (W21,
/// D81): Review your workbook with its one Accept and import, the workbook map, the legend's names and meanings, the
/// Pfam domains of the sheet, the subclass proposals and the notes sheets. The suite checks every control it names
/// against the program's own sources.
/// </summary>
public static class FamilyListImportGuide
{
    public const string Title = "Import family list";

    // Z16a8: every part follows the merged wizard (Z16a3 ce9e0c6d, Z16a4 and Z16a5 through fq/v2111-w21, Z16a9 2b69f29f),
    // read on the example through the same whole-workbook route as File > Import family list; every picture is taken by
    // --import-guide-screens on that build.
    public static IReadOnlyList<FamilyListImportGuideSection> Sections { get; } = new[]
    {
        new FamilyListImportGuideSection("start", "Before you start", "", "", new[]
        {
            "File > Import family list opens a detached draft. A sheet with the program's own headers takes three pages: File and sheet, Columns, Summary. Other pages appear only when something needs your choice.",
            "Accept and import checks the source and project, applies one panel edit and closes the window. File > Save keeps it; the usual modified marker stays until saving. Undo in the main window restores the previous panel.",
            "Import does not group proteins or recompute quantitation. Afterwards, use Group by evidence and Family checklist to review protein membership.",
        }),
        new FamilyListImportGuideSection("welcome", "Use template", "", "", new[]
        {
            "Use template writes an empty workbook with the program's columns. Fill one family per row, then choose it with Browse. Writing a template leaves the project unchanged. Edit current panel opens the existing panel editor; closing a loaded draft asks before discarding its choices.",
        }),
        new FamilyListImportGuideSection("workbook", "Workbook choices when needed", "", "", new[]
        {
            "Every sheet is read and original text is kept. The program's marked Instructions sheet is instructions even though it contains examples. A sheet without family columns is named once as skipped in Summary.",
            "Workbook sheets lets you choose among plausible family sheets and change sheet roles. Review findings opens only when choices remain; each finding names its source and resolving page.",
        }),
        new FamilyListImportGuideSection("file", "1. File and sheet", "02-file-and-sheet.png", "Choose the workbook and check its family sheet and header row.", new[]
        {
            "Browse or drop one .xlsx file. Check the proposed sheet and header row, counted from 1. Read this header row applies an edited row number to the draft. The workbook stays unchanged.",
            "Use Sheet preview for original cells and Cell details for their complete text. Workbook sheets includes hidden sheets and proposed roles. An older colour legend can still explain custom Decision labels; fills alone never decide an action.",
        }),
        new FamilyListImportGuideSection("preview", "Checking source cells", "", "", new[]
        {
            "The preview keeps row numbers, column letters, complete text, merged ranges and fills. Select a cell and open Cell details. Yellow or gold source fills are shown neutrally. Other sheets are retained as source notes.",
        }),
        new FamilyListImportGuideSection("columns", "2. Columns", "03-columns.png", "The program columns are mapped directly; check their original values.", new[]
        {
            "Abbreviation and Family name are required. Other-language names, notes, Pfam, UniProt family and subclass names are optional. Every original column stays source text. The program's own headers are recognized without a hidden marker.",
            "Without a Decision column, every family is quantified. Exact program action names are accepted directly. Only custom words in a Decision column open decision-label review. Confirm these actions keeps their chosen meanings in the draft.",
            "UniProt family contains exact source family labels separated by semicolons. Spaces are trimmed, repetitions are kept once and an empty cell specifies none.",
        }),
        new FamilyListImportGuideSection("decisions", "Decision labels when needed", "", "", new[]
        {
            "For each distinct custom Decision word, check its source rows and proposed action. Choose Quantify, Report under others, Not a family, or Merge into family and a kept abbreviation. Confirm these actions keeps the draft choices.",
            "Conditional, conflicting or unrecognized meanings stay unresolved until you choose. The original labels and legend meanings remain source text.",
        }),
        new FamilyListImportGuideSection("special", "Special rows when needed", "", "A cell naming four families, with its choice of how to import it.", new[]
        {
            "Rows that cannot be imported as written wait here, each with its row number, the problem and a proposed correction; Review on Family rows opens this page. Next waits until no row says Needs a choice. Open Original source row to see the row as written, choose in Use this row as, and press Use this choice.",
            "A cell that lists several names, like the OUTRAS row of the example with four rare families, can become One family per name or One group reported under others; Keep as one family imports it as written. When such a row becomes one family per name, its Pfam codes are kept as text only, because they cannot be told apart for each name. A comma inside a single name looks the same to the importer, as in row 17 of the example: choose Keep as one family there.",
        }),
        new FamilyListImportGuideSection("duplicate", "Repeated abbreviations", "", "The second LEC, proposed as LEC-2.", new[]
        {
            "An abbreviation used twice, like the second LEC of the example, can be kept with a new abbreviation (Keep or rename this row, proposed as LEC-2), merged into the family of the first row (Merge into family), or left out (Skip this row).",
            "Empty and decorative rows are skipped and listed with the status Skipped, so no row disappears without a trace.",
        }),
        new FamilyListImportGuideSection("matches", "Pfam choices when needed", "", "", new[]
        {
            "PF codes found in stored evidence are taken directly from the sheet, including named codes such as Name (PF12345). They do not ask for a second confirmation.",
            "Only a row written as a name or containing a code not found needs a choice. Select its stored match and press Use selected matches, or Skip this family. Continue without matches skips unresolved questions and keeps valid supplied codes.",
            "A row without a Pfam code is named once in Summary and gets no proteins until it has one. Counts are distinct database entries, can overlap and must not be added. No evidence is downloaded during import.",
        }),
        new FamilyListImportGuideSection("later", "Saving matches later", "", "", new[]
        {
            "Matches confirmed on page 5 are imported with the panel. Two routes save matches later, as the matching window's Help explains.",
            "With a grouping: in Group by evidence, open the Family checklist, use Match stored names for a family and confirm the labels to use. Apply grouping and File > Save keep the choices made in that review.",
            "Without grouping proteins: open File > Import family list, press Edit current panel, select the family, use Match stored names, then Apply panel and File > Save.",
        }),
        new FamilyListImportGuideSection("subclasses", "Subclass proposals", "05-subclasses.png", "Review subclass proposals: Com hemopexina of MMP, not selected yet, with its two uncertain Pfam rules from 2. Subclasses.", new[]
        {
            "A subclass table is a sheet that names abbreviations of your list, in its title or in a column, with one row for each subclass and the Pfam domains that define it, like the example's 2. Subclasses. Review on Subclass proposals opens Review subclass proposals: the subclasses grouped by family, and for the one you select its Family key, Subclass key, Display name and Pfam rules.",
            "Each rule is only a proposal: Required Pfam, a domain the subclass must have, or Absent Pfam, one it must not have, and only for Pfam domains found in the evidence stored in this project. The column headings of the table suggest whether a domain is required or absent, like the example's Domínios Pfam exigidos and Domínios ausentes, so Com hemopexina is proposed with Hemopexin required and Sem hemopexina with it absent. A polarity read only from a heading is an uncertain proposal, and a heading that disagrees with the cell's own words leaves the rule unsettled. Still check each rule before accepting. Text the wizard cannot read as a rule stays with the subclass as its notes, as written.",
            "A subclass or rule the wizard is not sure of starts Not selected and is marked Uncertain proposal; on the example that is every one, because their polarity comes from the column headings. Proposal explanation and Original rule cells show why and where from. To keep a subclass, tick Include subclass, then Include rule for each rule you agree with, after checking Required Pfam or Absent Pfam in its list; Leave out takes one out again. Check choices checks the draft, and Keep subclass choices or Back returns to Review your workbook. Nothing becomes a rule until you press Accept and import.",
            "The Subclass names column of the family list names the subclasses you expect for each family, and each one is listed here. Subclasses you include are imported with the panel; the ones you leave out are not.",
        }),
        new FamilyListImportGuideSection("summary", "3. Summary", "06-summary.png", "Review the exact family list before accepting it.", new[]
        {
            "Check the panel name, families, decisions, matches and skipped sheets. View source notes shows every original field for a family. Source details opens the workbook findings and retained notes within Summary. Back revisits pages used by the draft and keeps your choices.",
            "Accept and import checks the source and project, applies one panel edit and closes the window. File > Save keeps it with the project. No grouping or quantitation runs.",
        }),
        new FamilyListImportGuideSection("accept", "Accept and save", "", "", new[]
        {
            "Accept and import waits for required choices. Each finding names its resolving page. Broad name matches can still ask for confirmation; valid supplied PF codes do not.",
            "The main window marks the project modified until File > Save. Closing before acceptance explains that only the import draft and its choices will be discarded.",
        }),
        new FamilyListImportGuideSection("done", "After acceptance", "", "", new[]
        {
            "Accept and import closes the import window. The main window marks the project modified; File > Save keeps the list with the project. Undo in the main window restores the previous list.",
            "Closing before acceptance explains that the import draft and its choices will be discarded. The workbook and project stay unchanged.",
            FamilyListImportHelp.DoneNext + " " + FamilyListImportHelp.MatchingEffect,
        }),
        new FamilyListImportGuideSection("keyboard", "Keyboard", "", "", new[]
        {
            "Tab moves through the controls in reading order, and the arrow keys work in tables and lists. Enter presses the forward button when you are not editing a table. Escape cancels before the import and closes after it. F1 opens this chapter.",
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
