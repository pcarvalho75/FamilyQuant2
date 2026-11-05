using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant;

public partial class MainWindow
{
    private sealed class ShellHelpPresenter
    {
        private readonly MainWindow owner;
        public ShellHelpPresenter(MainWindow owner) => this.owner = owner;

        public string CurrentContextHelp()
        {
            if (owner.IsQuantifyVisible) return owner.QuantifyHelpText();
            // Home is the hub, not a step of the sequence. It names the project so the through line is
            // stated even here, where none is open, and draws the five steps in the order Home lists them.
            if (owner.home != null)
            {
                return
                    WorkflowNames.Home + ". " + owner.ProjectStatusText() + " The steps, in order: " + WorkflowNames.Flow +
                    ". Each step opens from Home or the sidebar; File > Open opens a project.";
            }

            // The analytics screen is NOT a step of that sequence either: it summarises what the earlier
            // steps produced, reads only, and says how to get back to the families.
            if (owner.analytics != null)
            {
                return
                    "Results. A read-only view of this project's own full-envelope quantitation: the XIC " +
                    "by mass, the isotopes credited per peptide, the percent of ion current by mass, what " +
                    "each family contributes, and the recovery of the total ion current. These Results graphs can be " +
                    "saved as a projector-sized PNG. It changes no family and reads no RAW. If it says " +
                    "there is nothing to analyse, use Quantify first. The open " +
                    "project stays open: File > Save saves it from here, and View > Families " +
                    "returns to it. File > Open opens a different project or Family Cluster File.\n\n" + DatabaseGapHelp.Text;
            }

            // The de novo screen is NOT a step of that sequence, so it says what it is instead of a
            // step number, and it says how to get back to the families.
            if (owner.dnc != null)
            {
                return
                    WorkflowNames.DeNovoReview + " · An optional, separate workflow: select PEAKS or DeepNovo, apply the three " +
                    "source-aware filters, and reconcile the accepted calls with PatternLab by file, " +
                    "scan, charge and precursor m/z. It needs no Family Cluster File and changes no family. The " +
                    "status line at the bottom of the screen says what to do next, and the Help button " +
                    "beside it explains every number this screen shows. To return to the family " +
                    "workflow, use View > Families (the open project stays open) or File > Open " +
                    "for another project or Family Cluster File.";
            }

            if (owner.cgc != null)
            {
                return
                    "Families loaded. " + owner.ProjectStatusText() + " Curate them here, then " +
                    "Quantify gives the project its own quantitation, and Results " +
                    "reads it. Help · How to read these numbers explains every column.";
            }

            if (owner.usearch != null)
            {
                return
                    "Build. Use \"Choose database\" for a FASTA or target-decoy (.T-R) file, then press " +
                    "\"Build families\". This writes a Family Cluster File (.fcf) beside the database. Open it " +
                    "with File > Open.";
            }

            return
                "Start here. Use File > New from database to group a new database's " +
                "proteins into families. If you already have a project (.fqproj) or a Family Cluster File " +
                "(.fcf), open it with File > Open.";
        }

        public string ProjectStatusText()
        {
            if (owner.cgc == null || owner.lastFcfPath == null)
            {
                return "Project: none open.";
            }

            string name = Path.GetFileName(owner.lastFcfPath);
            string quant = owner.loadedQuant.Count > 0
                ? owner.loadedQuant.Count.ToString("N0", CultureInfo.CurrentCulture) + " quant rows stored"
                : "no full XIC computed yet";

            string sources = owner.loadedManifest?.DeNovoInputs != null && owner.loadedManifest.DeNovoInputs.Count > 0
                ? "; de novo: " + string.Join(" + ", owner.loadedManifest.DeNovoInputs
                    .GroupBy(i => i.SourceId, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => g.Min(i => i.SourcePriority))
                    .Select(g => g.First().SourceDisplayName + " " + g.First().CutoffDescription))
                : string.Empty;

            return "Project: " + name + " (" + quant + sources + ").";
        }

        public void MenuItemHowToRead_Click(object sender, RoutedEventArgs e)
        {
            if (owner.IsQuantifyVisible)
            {
                TextReportWindow.Show("Quantify: inputs and results", owner.QuantifyHelpText(), owner);
                return;
            }
            if (owner.usearch != null)
            {
                owner.usearch.ShowHelp();
                return;
            }
            // Two workflows, two pages. Neither page's text applies to the other's screen, so the menu
            // shows the one belonging to what is actually on display.
            if (owner.dnc != null)
            {
                owner.dnc.ShowHelp();
                return;
            }

            // When a cluster view is open it can name the denominator actually in use; otherwise the help
            // explains what the choice will be, without claiming a denominator nothing has established.
            if (owner.cgc != null)
            {
                owner.cgc.ShowHelp();
                return;
            }

            TextReportWindow.Show(
                "FamilyQuant · How to read these numbers",
                HelpContent.Build(null, NormalizationMode.IdentifiedSignal, SharedPeptidePolicy.FamilyUnique),
                owner);
        }

        public void MenuItemWhereAmI_Click(object sender, RoutedEventArgs e)
        {
            TextReportWindow.Show("FamilyQuant · What do I do first?", owner.WorkflowText(), owner, wrap: true);
        }

        public string WorkflowText()
        {
            return
                "WHAT DO I DO FIRST?\n" +
                "===================\n\n" +
                "You are at: " + owner.StatusModel.Text + "\n\n" + owner.StatusModel.ContextHelp + "\n\n" +
                "IN FAMILIES, FOLLOW THIS ORDER\n" + HelpContent.GroupingOrder + "\n" +
                "STEP 1 · Group the proteins into families\n" +
                "   Menu: File > New from database\n" +
                "   Needs: a protein database, either a plain .fasta or a target-decoy .T-R file.\n" +
                "   Produces: a Family Cluster File (.fcf) written next to the database.\n" +
                "   Choose database, set identity, then Build families. 85% is the initial value. \"Scan\n" +
                "   identity range\" shows how the family count and the target/decoy mixing behave\n" +
                "   across the whole range, which is a better basis for the choice than a single\n" +
                "   number. Find lowest identity uses Divide and Conquer to find the lowest identity\n" +
                "   with no mixing at all. That is a floor, not a recommendation. Help > How to read\n" +
                "   these numbers explains why.\n\n" +
                "STEP 2 · Open the families\n" +
                "   Menu: File > Open\n" +
                "   Needs: the .fcf from step 1.\n" +
                "   You can now merge families further by their description words, or build one family\n" +
                "   from a keyword list. Curation changes can be undone in this session. Use Save as\n" +
                "   to keep the previous grouping in a separate project. On reopen, unchanged peptide\n" +
                "   matches can be read from cached results; changed inputs are checked again. After\n" +
                "   opening a project with stored Results, Export report becomes available when its\n" +
                "   family attribution is ready.\n\n" +
                "BEFORE STEP 3 · Prepare the family panel and report\n" +
                "   Menu: File > Import family panel\n" +
                "   Needs: an open project and a spreadsheet with one row per family.\n" +
                "   Choose spreadsheet, select its sheet and header row, then Preview columns.\n" +
                "   Pick the columns for family key, family name, decision and optional Pfam. Map\n" +
                "   each spreadsheet decision to Quantify, Report under others, Not a family or\n" +
                "   Merge into family; for a merge, enter the target family key. Preview panel.\n" +
                "   Check Pfam reviews the panel's codes and names without changing them. Review\n" +
                "   the results, correct the draft if needed, then choose Apply panel.\n\n" +
                "   Menu: View > Families\n" +
                "   Needs: the panel and protein evidence. Use Fetch evidence if needed.\n" +
                "   Choose Group by evidence, review or edit the scope, then Update proposal. In\n" +
                "   Panel links, review ambiguous links and confirm choices with Use link choices.\n" +
                "   Scope phrases can contain several words: put each phrase in double quotes.\n" +
                "   The scope is checked against evidence keywords and tissue annotations. The audit\n" +
                "   explains when a linked source family stays outside because its evidence does not\n" +
                "   match that scope. You can add a note to a link choice; saved choices and notes\n" +
                "   are included in the report methods.\n" +
                "   The reviewed-entry accuracy check accepts multiple scope phrases. It holds each\n" +
                "   entry out when comparing it with other reviewed entries; its expected family\n" +
                "   comes from source annotations and is not independent proof.\n" +
                "   Review the updated proposal and protein ticks, then choose Apply grouping.\n" +
                "   Applying a panel-based grouping also computes subclass labels from its rules.\n" +
                "   Produces: reviewed family assignments and subclass labels in the project.\n\n" +
                "   Menu: File > Report details\n" +
                "   Needs: an open project. Enter the title, authors, sample description, workflow,\n" +
                "   kind of database and written interpretation you want, then choose Save details.\n" +
                "   Produces: report details in the project. Continue to step 3 to compute.\n\n" +
                "STEP 3 · Attach the quantitation\n" +
                "   Open: 3 Quantify on Home or the sidebar (Alt+3)\n" +
                "   Needs: the RAW folder and matching identifications (.sepr2); de novo and search\n" +
                "   settings are optional. Press Quantify on that screen.\n" +
                "   Compute and project opening show progress for each phase, the current item,\n" +
                "   elapsed time and time since the last step. Where a known total is available,\n" +
                "   the progress line counts completed work. Operation log shows recorded steps.\n" +
                "   Produces: stored Results for the project and its report.\n" +
                "   " + HomologyMigrationHelp.Text + "\n" +
                "   A quantitative panel family marked no peptide observed has no peptide observed\n" +
                "   in the completed study. This is not proof that the family is absent.\n" +
                "   In Families > Quantitation use Full XIC > Share of and Shared peptides.\n" +
                "   For the secondary source, open PatternLab (optional) and Load PatternLab project.\n" +
                "   PatternLab needs a project (.plp) exported at PEPTIDE level. A protein-level\n" +
                "   export contains nothing that can be attributed to a family, and every row will\n" +
                "   read 0.\n" +
                "   In PatternLab (optional), PatternLab share of chooses what the percentages are a\n" +
                "   share of. Choose what to do with peptides that belong to several families, then\n" +
                "   press Recalculate. Modes the project cannot support are greyed out, each with\n" +
                "   the reason in its own tooltip.\n\n" +
                "STEP 4 · Take the result away\n" +
                "   Export to CSV writes exactly the rows on screen, with the shares as fractions\n" +
                "   between 0 and 1 and a dot for the decimal separator, so any program can read them.\n" +
                "   File > Save keeps the project for next time. File > Export also writes families\n" +
                "   for FamilyQuant 1.5 (.fcf), preserving the grouping without stored quantitation.\n" +
                "   Tools > Reannotate SEPro stamps your User ID family numbers into a copy of a SEPro\n" +
                "   result package, leaving the original untouched.\n\n" +
                "EXPORT THE REPORT\n" +
                "   Menu: File > Export report\n" +
                "   Needs: stored Results from step 3 and the report details you want to include.\n" +
                "   Choose sections and figures with the checkboxes. Set Report threshold (%) to\n" +
                "   group families below that mean assigned-signal share under Others; their list\n" +
                "   remains in the supplement. Families marked Report under others also go there.\n" +
                "   Preparation and export show progress, the current step and elapsed time. Choose\n" +
                "   Operation log to read the timestamped steps while the operation is running. The\n" +
                "   report log is also saved in %LOCALAPPDATA%\\FamilyQuant2\\operation-logs. Export can\n" +
                "   reuse current family attribution and cached matches when the inputs still match.\n" +
                "   Summary of findings states the stored values in fixed sentences; its numbers are\n" +
                "   also listed in the supplement.\n" +
                "   Produces: a Word report, a companion supplement workbook and an interactive\n" +
                "   HTML report beside it. Open the HTML file in a browser; it works offline.\n" +
                "   Long lists have their own supplement sheets, one row per item; the report points\n" +
                "   to those sheets and CSV exports retain their values. Report narrative is written by the program from\n" +
                "   project data, without AI. Your panel and report details also appear in the report.\n" +
                "   File > Save keeps the project, report details, choices and threshold.\n\n" +
                "EXPORT THE INTERPRETATION INPUT\n" +
                "   Menu: File > Export > Interpretation input (Markdown)\n" +
                "   Needs: stored Results from step 3.\n" +
                "   Writes one Markdown file with the report details, the panel as entered, the summary\n" +
                "   of findings, the stored tables it reads, the methods and a short instruction block.\n" +
                "   Give it to an assistant of your choice; FamilyQuant itself does not interpret it.\n\n" +

                "OPTIONAL · Draft an interpretation with AI\n" +
                "   Menu: Help > AI assistant key...\n" +
                "   Add, test, import or remove your Anthropic key. It is protected for this Windows\n" +
                "   user and is not saved in the project or report. Test key sends a short request\n" +
                "   without project data. On first use, you may import a key file beside the project.\n\n" +
                "   Menu: File > Draft interpretation with AI...\n" +
                "   The program shows the exact interpretation input and model before you confirm\n" +
                "   sending it to Anthropic. The input is the same text as File > Export > Interpretation\n" +
                "   input (Markdown): your report details, panel text, result tables and analysis\n" +
                "   choices. Cancel leaves the project unchanged. After a response, review the draft\n" +
                "   and every number not found in the input tables before accepting it. A matching\n" +
                "   number is not proof that a claim is correct. Accepting stores a labelled draft\n" +
                "   as your written interpretation; Undo restores the previous text.\n" +
                "   The program's own report narrative is written without AI. Your accepted written\n" +
                "   interpretation is included separately when that report section is selected.\n\n" +

                // The de novo screen is a separate workflow and is written as one: it is numbered
                // OPTIONAL rather than as a fifth step, because nothing above it needs it and it needs
                // nothing above it except, for family attribution, a Family Cluster File.
                "OPTIONAL · 5 De novo review\n" +
                "   Open: 5 De novo review on Home or the sidebar (Alt+5)\n" +
                "   A separate workflow, not a step of the four above. It changes no family, no User ID\n" +
                "   and no quantitation, and it needs no Family Cluster File to start. Use it to ask\n" +
                "   how the selected de novo calls relate to PatternLab. Its two core steps are:\n" +
                "     1. Select PEAKS or DeepNovo and keep calls passing all three filters: minimum\n" +
                "        length, the source-native score, and a consecutive local-confidence stretch.\n" +
                "     2. Compare them with a SEPro search of the SAME runs. Each kept row comes back\n" +
                "        as new territory, a co-isolated second species, a redundant confirmation, an\n" +
                "        agreement PatternLab itself scored weakly, or a straight disagreement. The\n" +
                "        last two are listed as a review queue, because they are decisions.\n" +
                "   Quantify provides extraction and Results reads the ion-current recovery. Family\n" +
                "   attribution remains available for de novo work without a saved project.\n" +
                "   That screen has a Help button of its own, and while it is open Help · How to read\n" +
                "   these numbers shows its page instead of this one.\n\n" +

                "Help > Check for updates asks GitHub for the latest public release only when you choose\n" +
                "it, and sends nothing but that request. It names a newer version and opens its release\n" +
                "page on request; nothing is downloaded or installed.\n";
        }
    }
}
