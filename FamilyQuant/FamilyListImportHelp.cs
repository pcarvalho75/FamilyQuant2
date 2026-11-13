namespace FamilyQuant;

public static class FamilyListImportHelp
{
    public const string DecisionsSummary = "Quantify, merge, report under others or leave out; no conditional action. Your labels stay with each family.";
    public const string DoneNext = "Next: Group by evidence reads the database and scope by itself when it opens (this takes minutes); then press Update proposal to see the family checklist.";
    public const string MatchingEffect = "Matching labels families; proteins move only when you change the scope or place them by hand, then Apply grouping.";
    public const string Text = "IMPORT FAMILY LIST\n\n"
        + "File and sheet, Columns, Summary are the normal three pages. Browse chooses a workbook; Use template writes an empty workbook to fill and import. The program recognizes its own headers, keeps source text and names skipped sheets in Summary.\n\n"
        + "Without a Decision column, every family is quantified. Exact program action labels are accepted directly; custom words need their actions confirmed. Special rows appears only for a row needing a choice. Well-formed PF codes are confirmed as written, before or after Fetch evidence; only a domain name without a code needs a matching choice. Un-confirm in Summary removes a family's confirmation from the draft while keeping its source codes. A row without a Pfam code is named once and gets no proteins until it has one.\n\n"
        + "DECISION ACTIONS\n" + DecisionsSummary + "\n\n"
        + "Subclass proposals appear when their source rules need a choice. UniProt family holds exact source family labels separated by semicolons; an empty cell specifies none. Source notes keep original cells, never measurements.\n\n"
        + "Accept and import checks the workbook and captured project, applies one panel edit and closes the window. File > Save keeps the list; the modified marker stays until saving. Undo in the main window restores the previous panel. Closing before acceptance explains that the draft choices will be discarded; project and workbook stay unchanged.\n\n"
        + "KEYBOARD\nTab follows the controls; arrows work in native lists. Enter activates the enabled forward action outside a table edit. Escape closes the draft with its loss warning; F1 opens this Help.\n";
}
