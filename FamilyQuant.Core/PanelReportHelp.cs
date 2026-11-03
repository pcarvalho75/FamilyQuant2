namespace FamilyQuant;

/// <summary>General user guidance for panel-based analysis and report export.</summary>
public static class PanelReportHelp
{
    public const string Panel = """
        FAMILY PANEL AND EVIDENCE

        File > Import family panel opens a draft for the open project. Choose a
        spreadsheet, sheet and header row, then select the family key, name and
        decision columns and, optionally, a Pfam column. Map every source decision
        to Quantify, Report under others, Not a family or Merge into family. A merge
        names the target family key. Preview the panel before applying it. Source
        column text and the chosen mappings stay with the panel.

        Keys must be unique within their scope. Add, edit or remove family rows in
        the table. Multiple Pfam and subclass keys are separated by semicolons.
        Unchanged keys keep their existing domain and subclass details.
        Save panel as spreadsheet writes a reusable template. Its Families and
        Subclasses sheets can be edited and imported again; the template also
        retains panel details not shown in those sheets. The Subclasses sheet can
        hold required and absent Pfam codes, source subfamily text and a report-as
        key. Apply panel commits one project change; Undo can restore the prior
        panel. Later assignment or subclass refresh operations have their own
        Undo steps. Save the project to retain the change.

        Check Pfam compares the panel's accessions and supplied names with the
        provider. A mismatch, missing entry or unavailable check is shown; it
        never silently changes the panel. A missing domain call is not proof that
        a domain is absent.

        Evidence grouping proposes family assignments from stored source evidence
        and reviewed neighbours. Review and confirm ambiguous links between source
        contexts and panel families, including an explicit outside-panel choice.
        Apply only the proposals you accept. Unassigned or conflicting cases stay
        visible rather than being guessed. Changing relevant panel rules can make
        assignments and subclass labels out of date; rebuild them before use.

        Subclass rules use the panel's required and absent domains or source
        subfamily text. A fragment or missing evidence may not establish an
        absence. Tied or incompatible evidence stays unresolved. A protein label
        alone does not make a peptide unique to that subclass: shared peptide
        evidence remains at the family or unresolved level when it cannot
        distinguish a subclass.
        """;

    public const string NumbersAndFigures = """
        FAMILY NUMBERS AND FIGURES

        The composition headline is the mean of available per-run shares of the
        signal credited to database families. The measured peptide signal and RAW
        total ion current (TIC) are separate denominators; a peptide-signal share
        is not a TIC share. Family-world-only signal is shown separately and is
        not added to the database-family composition. These are signal shares,
        not direct measurements of sample mass.

        Run-to-run spread is the sample standard deviation, available only with
        at least two runs. CV is that standard deviation divided by the mean and
        is unavailable when the mean is zero. A run with a zero denominator has
        an unavailable share, not zero. A pooled share sums signal across runs
        before dividing and can differ from the mean of available per-run shares.
        Where a measured-signal or TIC summary requires every run, missing values
        leave that summary unavailable.

        The protein count is a greedy estimate of the database proteins needed
        to explain the observed peptides, not an exact minimum and not a gene
        or proteoform count. A protein with a peptide of its own has an exactly
        contained peptide unique among database targets, including labelled
        contaminants; decoys are not targets. Shared evidence can support the
        greedy explanation; homology-only matches can remain
        unexplained. Read the unexplained count beside the estimate. Abundance
        and the number of explanatory proteins answer different questions.

        A family goes to Others when its mean share is strictly below the report
        threshold, or when its panel decision is Report under others. The default
        threshold is 0.1 percent and can be changed in Export report. The
        member list retains the individual families and the reason for grouping.
        Excluded panel rows are not families. A numeric family that conflicts
        between panel keys or mixes panel and outside members stays a separate
        flagged row rather than being assigned or added to Others.

        Sensitivity comparisons, when stored, show how results change with the
        selected shared-peptide policy and de novo cutoff. A missing combination
        is unavailable, not an invented result. Count in every family overlaps:
        those contributions cannot be summed as a normalized composition.

        Family bars show mean shares with sample-SD whiskers when available.
        Subclass stacks and the family/subclass ring use their stated pooled
        basis; read their captions rather than equating pooled values with run
        means. When available, the ring uses the same Others membership as the
        report. The
        abundance-versus-diversity plot pairs share with the greedy protein
        estimate. A coverage map shows the stored protein positions, domains and
        supported peptide placements; missing or ambiguous positions stay
        unavailable or unresolved. The signal-source figure separates identified,
        database-matched de novo and family-world-only contributions. Other
        quality figures retain their own denominators and captions.
        """;

    public const string Report = """
        REPORT AND SUPPLEMENT

        File > Report details stores the title, authors, sample description,
        workflow, database kind and your written interpretation in the project.
        Recorded input provenance is shown there. The interpretation is your own
        text; check that any numbers it quotes are present in the report.

        File > Export report needs stored Results. Choose sections and figures;
        clearing a section removes its contents, and clearing a figure removes
        that figure. The choices and report threshold can be saved in the project.
        The report uses the project's own facts and the Light export palette.
        Missing studies are identified as unavailable rather than filled in.
        When a companion supplement is produced, use it for the long tables and
        the individual families grouped as Others. Exporting a report does not
        save the project: use File > Save to retain the choices and details for
        a later export.
        """;

    public static string Build() => Panel + "\n\n" + NumbersAndFigures + "\n\n" + Report;
}
