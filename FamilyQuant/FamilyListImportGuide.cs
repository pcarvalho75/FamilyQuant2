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
            "File > Import family list brings a spreadsheet of family abbreviations and names into this project's family panel. When you open a workbook, FamilyQuant reads every sheet and opens on Review your workbook, one page with everything it found and proposes; Accept and import imports it in one step. Each part of that page opens its own page of the wizard to change it. Until Accept and import, everything is a draft: Back keeps your choices, and Cancel changes nothing.",
            "Importing creates the family panel and nothing else. It does not group proteins or recompute quantitation; afterwards you review which proteins belong in each family in the Family checklist of Group by evidence.",
            "To practise, press Try with the example on the first page. It opens an invented workbook for a secreted-protein mixture, written in Portuguese like many consensus sheets, with six sheets: the family list, a table of subclasses, three sheets of notes (resolved divergences, quantification rules and references) and a legend that names each colour of the abbreviations. The family list has a title above the column names, coloured abbreviations, a row naming four families and a repeated abbreviation. Every value in it is made up. The pictures in this chapter were taken on it.",
        }),
        new FamilyListImportGuideSection("welcome", "1. Welcome", "01-welcome.png", "The first page: what you need, what is kept, where it goes, and the three ways in.", new[]
        {
            "The page lists what you need, an Excel workbook (.xlsx) with an abbreviation column and a family name column; what is kept with your list, names in another language, notes and the original rows; and where it goes, this project's family panel, after which you review protein choices in the Family checklist of Group by evidence.",
            "Try with the example opens the invented workbook on Review your workbook. Save a template writes a workbook with the recommended columns, which imports with no mapping. Edit current panel opens the panel editor for the panel this project already has. Next leads to File and sheet, where Browse chooses your own workbook.",
        }),
        new FamilyListImportGuideSection("workbook", "Review your workbook", "02-workbook.png", "Review your workbook: what the example's six sheets give, with the choices still waiting for you.", new[]
        {
            "When you open a workbook, with Browse, by dropping it on File and sheet, or with Try with the example, FamilyQuant reads every sheet before you choose anything and opens on Review your workbook. It tells the sheets apart by their structure, never by their names: the family list, a colour legend, a subclass table, and sheets of notes.",
            "The page has one box for each part, with what was found and how many items are proposed or still to review: Workbook sheets, Family list and columns, Source categories, Decision labels, Family rows, Pfam and evidence matches, Subclass proposals and Source notes. Review in a box opens that part's page of the wizard, described below; Back to workbook review, or Review workbook on the Summary, brings you back here. The tabs below the boxes list the parts themselves: Families, Columns, Source categories, Decisions, Special rows, Pfam, Subclasses, Notes, and All findings, where each finding shows its state, its reason and the cells it comes from.",
            "Whatever the wizard is not sure of is left for you and listed below the tabs; it never guesses. In the example, the line says 10 choices need review, about the four decision labels the legend leaves open, the row that names four families and the repeated abbreviation LEC. Review findings lists them all. Anything the wizard cannot place is shown as Could not place.",
            "Accept and import stays unavailable while such a choice is open. When you press it, it accepts everything the page shows, exactly as shown, and imports it as one change: that press is your confirmation, and nothing is set before it. The line at the bottom says so: Nothing has changed yet. Accept and import keeps one Undo step.",
            "A workbook of another shape, such as a single sheet, or a list with no legend and no decisions, is read the same way from its structure and shows only the parts it has.",
        }),
        new FamilyListImportGuideSection("file", "2. File and sheet", "02-workbook-map.png", "Workbook sheets: the six sheets of the example, each with the role proposed for it.", new[]
        {
            "Review on Workbook sheets opens this page on Workbook sheets: each sheet of the workbook, hidden ones too, with the role proposed for it under Use as: Family list, Colour legend, Subclass table or Notes. Kept as says what each sheet gives the import, such as Kept as source notes for a sheet of notes, and Why this role gives the reason, read from the sheet's shape, never from its name. Review shows Proposed, or Review role for a sheet the wizard is not sure of; check that one.",
            "The line above the table names the family list, and the line below it says whether the legend's names and the meanings of the decision labels were read. To change a role, choose another under Use as; to take another sheet as the family list, select it and press Use selected family sheet. The proposal is read again with your choice, and the workbook and the project stay unchanged. When two sheets could each be the family list, the line says choose the family list, and neither Next nor Accept and import goes on until you choose; the wizard never takes the first one for you.",
            "A colour legend is a sheet whose first column carries the same cell colours as the family list, with the meaning of each colour beside it; when its labels are the values of one column, it also explains your decision labels. In the example, 6. Legenda names the colour of each decision and says what each decision means.",
            "Sheets with no family rows, like the example's divergences, quantification rules and references, are notes, and the legend's own text is kept with them. Notes kept names them; Read kept notes shows their text in full, and Kept notes preview shows the start of it. They are imported with the panel as source notes, shown under Source notes in the panel editor and listed in the Methods of the report. They are never read as families or as rules.",
        }),
        new FamilyListImportGuideSection("preview", "2. Sheet preview", "02-file-and-sheet.png", "The example's family list: a title and a subtitle above the column names in row 3.", new[]
        {
            "Sheet preview shows the family list as it is, with its row numbers, column letters, merged titles, accents, line breaks and cell colours. Colours are source data and never decide anything by themselves. Select a cell and open Cell details to read its full text.",
            "Sheet shows the family list and Header row the row with the column names; in the example it is row 3, below a title and a subtitle. If the proposal is wrong, type the right row number and press Read this header row. The workbook itself is never changed.",
        }),
        new FamilyListImportGuideSection("columns", "3. Columns", "03-columns.png", "Each meaning with the column proposed for it and an example from the sheet.", new[]
        {
            "For each meaning, choose the column of your sheet that holds it; an example from your sheet appears below each choice. Abbreviation and Family name are required. Other-language name, Notes, Pfam codes, Subclass names and Decision are optional, and every original column is kept anyway. Subclass names holds the subclasses you expect for each family, like Subclasses in the example; Subclass proposals, below, explains how they are used.",
            "When the legend's labels are the values written in one column, as in the example, that column is proposed for Decision, and the number of values to review appears under it with Review decision values, which opens them, as Review on Decision labels does. Without such a legend, when no column is chosen for Decision and one column holds a few short labels repeated over many rows, the page can ask whether that column says what to do with each family, naming it: Could (the column's name) hold the family actions? It is only a question. Use this decision column chooses that column and opens its values for your review; until you press it, nothing is mapped.",
            "Without a Decision column every family is Quantify, so Next asks once: Quantify every family for this import? No keeps you on Columns. Your answer holds for this sheet until you change the Decision column.",
        }),
        new FamilyListImportGuideSection("colours", "3. Colour categories", "03-columns-colours.png", "Colour categories: each fill of Sigla named from the example's legend, or with its plain name.", new[]
        {
            "When the sheet has coloured cells, Colour categories (optional) opens by itself below the column choices. Column with coloured cells holds the column whose colours mean something; each colour is listed under Source colour with its Source rows and its Category name. Review on Source categories brings you here.",
            "When the workbook has a colour legend, the column whose cells carry the legend's colours is chosen for you, Sigla in the example, and each colour starts with the name the legend gives it: light turquoise starts as Núcleo. A colour without a legend name, or with two, is named for you with its plain name, such as light grey in the example, which the legend gives to two labels. Rename a category if you like; clearing a name brings the plain name back, so the colours never hold up Next.",
            "The category is kept as text with each family and shown in the Colour column of the Summary; it never decides what happens to a family.",
        }),
        new FamilyListImportGuideSection("decisions", "3. Reviewing decisions", "03-columns-decisions.png", "One line per decision written in the sheet, with its meaning from the legend and the proposed action; none is confirmed yet.", new[]
        {
            "Review decision values, or Review on Decision labels, turns the page into Review spreadsheet decisions: one line for each different value written in the Decision column, the rows that use it under Source rows, and the action under Use as (proposed): Quantify, Report under others, Not a family, or Merge into family with the abbreviation under Merge into abbreviation. The line under the title says it in short: " + FamilyListImportHelp.DecisionsSummary,
            "When the workbook's legend explains the labels, as the example's does, Meaning from legend shows each label's meaning (point at it to read it in full), and the proposed action starts from that meaning: in the example, Núcleo and Padrão are proposed as Quantify, Excluir as Not a family, and Fundir com LEC as Merge into family with LEC, the abbreviation it names. A meaning that is optional, conditional or still pending proposes nothing, so the action is left empty for you to choose: in the example, Opcional, Condicional (só na fração solúvel), Excluir (ou só varredura) and Revisar. The meaning only helps you decide; each line is still your choice, and none is confirmed for you.",
            "There is no conditional action. A condition written in the sheet, like Condicional (só na fração solúvel) in the example, is never turned into a rule; choose the action that fits now. The value written in your sheet stays with each family as its source label.",
            "A cell that already holds one of the program's own labels, Quantify, Report under others or Not a family, or Merge into followed by an abbreviation of your list, is confirmed at once. When every value is like that, there is nothing to review.",
            "Check each other line, change what is wrong, and press Confirm these actions. Next stays disabled until every value is confirmed, and changing an action or its abbreviation clears that line's Confirmed tick, so no proposal is accepted for you. Back to columns returns without confirming, and Review decision values opens the list again.",
        }),
        new FamilyListImportGuideSection("special", "4. Special rows", "04-special-rows.png", "A cell naming four families, with its choice of how to import it.", new[]
        {
            "Rows that cannot be imported as written wait here, each with its row number, the problem and a proposed correction; Review on Family rows opens this page. Next waits until no row says Needs a choice. Open Original source row to see the row as written, choose in Use this row as, and press Use this choice.",
            "A cell that lists several names, like the OUTRAS row of the example with four rare families, can become One family per name or One group reported under others; Keep as one family imports it as written. When such a row becomes one family per name, its Pfam codes are kept as text only, because they cannot be told apart for each name. A comma inside a single name looks the same to the importer, as in row 17 of the example: choose Keep as one family there.",
        }),
        new FamilyListImportGuideSection("duplicate", "4. A repeated abbreviation", "04-special-rows-duplicate.png", "The second LEC, proposed as LEC-2.", new[]
        {
            "An abbreviation used twice, like the second LEC of the example, can be kept with a new abbreviation (Keep or rename this row, proposed as LEC-2), merged into the family of the first row (Merge into family), or left out (Skip this row).",
            "Empty and decorative rows are skipped and listed with the status Skipped, so no row disappears without a trace.",
        }),
        new FamilyListImportGuideSection("matches", "5. Evidence matches", "05-evidence-matches.png", "TRP: the Pfam domain from your sheet comes first and ticked; Peptidase S1 family is ticked by you.", new[]
        {
            "For each family, FamilyQuant suggests labels from the evidence already stored in this project, such as UniProt families and keywords, that share words with the family's names. Nothing is ticked for you. Review on Pfam and evidence matches opens this page, and the Pfam tab of Review your workbook lists the domains written in your sheet.",
            "The Pfam domains written in the family's own row of your sheet are the one exception. They come first, by their Pfam code with the type Pfam domain, marked from your sheet, and ticked, because they are your own statement, even when no stored entry has that domain yet. In the example, TRP comes with PF00089, written in its row as Trypsin (PF00089). Untick any you do not want.",
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
        new FamilyListImportGuideSection("subclasses", "Subclass proposals", "05-subclasses.png", "Review subclass proposals: Com hemopexina of MMP, with its two Pfam rules proposed from 2. Subclasses.", new[]
        {
            "A subclass table is a sheet that names abbreviations of your list, in its title or in a column, with one row for each subclass and the Pfam domains that define it, like the example's 2. Subclasses. Review on Subclass proposals opens Review subclass proposals: the subclasses grouped by family, and for the one you select its Family key, Subclass key, Display name and Pfam rules.",
            "Each rule is only a proposal: Required Pfam, a domain the subclass must have, or Absent Pfam, one it must not have, and only for Pfam domains found in the evidence stored in this project. Whether a domain is required or absent is read from the column headings of the table, like the example's Domínios Pfam exigidos and Domínios ausentes, so Com hemopexina requires Hemopexin and Sem hemopexina proposes it as absent; when a heading and the cell's own words disagree, the rule is left out for you. Still check each rule before accepting. Text the wizard cannot read as a rule stays with the subclass as its notes, as written.",
            "The subclasses and rules the wizard is sure of arrive in the draft with Include subclass and Include rule ticked; a subclass it is not sure of, or a rule it cannot settle, starts left out for you to decide. Check each one: Leave out takes a subclass or a rule out, and the list under a rule changes Required Pfam to Absent Pfam. Check choices checks the draft, and Keep subclass choices or Back returns to Review your workbook. Nothing becomes a rule until you press Accept and import.",
            "The Subclass names column of the family list names the subclasses you expect for each family, and each one is listed here. Subclasses you include are imported with the panel; the ones you leave out are not.",
        }),
        new FamilyListImportGuideSection("summary", "6. Summary", "06-summary.png", "The list as it will be imported; nothing has changed yet.", new[]
        {
            "Name the panel, then read the line of counts (families, rows split, skipped and renamed, matching choices), the source sheet, and the panel this import replaces. The table lists every family with its names, decision, colour category (in the Colour column) and matching choice; View source notes shows the original columns kept for it.",
            "Review workbook takes you back to Review your workbook, where Accept and import imports the panel. Back keeps your choices, and Cancel changes nothing.",
        }),
        new FamilyListImportGuideSection("accept", "6. Accept and import", "06-accept.png", "Review your workbook with every choice made: Accept and import is available.", new[]
        {
            "Once every open choice is made, the boxes say 0 to review for the parts that needed you, and Accept and import becomes available. In the example, the Pfam and evidence matches box can still show items to review: those are optional and do not hold up the import.",
            "Accept and import replaces the current panel, if any, in one step. Just before, the workbook and the project are checked again; if either changed, nothing is imported. If a ticked match covers more than half of the database's entries, it asks once before importing.",
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
