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
            // The home screen is the hub, not a step of the sequence. It names the project so the through
            // line is stated even here, where none is open.
            if (owner.home != null)
            {
                return
                    "Start page. The family workflow and the optional de novo workflow stay available. " +
                    owner.ProjectStatusText() + " File > Open opens a project. The navigation list keeps " +
                    "Families, Quantify, Results and De novo within reach.";
            }

            // The analytics screen is NOT a step of that sequence either: it summarises what the earlier
            // steps produced, reads only, and says how to get back to the families.
            if (owner.analytics != null)
            {
                return
                    "Results. A read-only view of this project's own full-envelope quantitation: the XIC " +
                    "by mass, the isotopes credited per peptide, the percent of ion current by mass, what " +
                    "each family contributes, and the recovery of the total ion current. Every graph can be " +
                    "saved as a projector-sized PNG. It changes no family and reads no RAW. If it says " +
                    "there is nothing to analyse, use Analysis > Compute full XIC first. The open " +
                    "project stays open: File > Save saves it from here, and View > Families " +
                    "returns to it. File > Open opens a different project or Family Cluster File.";
            }

            // The de novo screen is NOT a step of that sequence, so it says what it is instead of a
            // step number, and it says how to get back to the families.
            if (owner.dnc != null)
            {
                return
                    "De Novo evidence · A separate workflow: select PEAKS or DeepNovo, apply the three " +
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
                    "Analysis > Compute full XIC gives the project its own quantitation, and Results " +
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
                "   to keep the previous grouping in a separate project.\n\n" +
                "STEP 3 · Attach the quantitation\n" +
                "   Layer 2: Load PLP File\n" +
                "   Needs: a PatternLab project (.plp) exported at PEPTIDE level. A protein-level\n" +
                "   export contains nothing that can be attributed to a family, and every row will\n" +
                "   read 0.\n" +
                "   Then in Layer 3 choose what the percentages are a share of, choose what to do with\n" +
                "   peptides that belong to several families, and press Recalculate. Modes the project\n" +
                "   cannot support are greyed out, each with the reason in its own tooltip.\n\n" +
                "STEP 4 · Take the result away\n" +
                "   Export to CSV writes exactly the rows on screen, with the shares as fractions\n" +
                "   between 0 and 1 and a dot for the decimal separator, so any program can read them.\n" +
                "   File > Save keeps the project for next time. File > Export also writes families\n" +
                "   for FamilyQuant 1.5 (.fcf), preserving the grouping without stored quantitation.\n" +
                "   Analysis > Reannotate SEPro stamps your User ID family numbers into a copy of a SEPro\n" +
                "   result package, leaving the original untouched.\n\n" +

                // The de novo screen is a separate workflow and is written as one: it is numbered
                // OPTIONAL rather than as a fifth step, because nothing above it needs it and it needs
                // nothing above it except, for its Layer 4, a Family Cluster File.
                "OPTIONAL · Review de novo evidence\n" +
                "   Menu: Analysis > De novo evidence\n" +
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
                "   these numbers shows its page instead of this one.\n";
        }
    }
}
