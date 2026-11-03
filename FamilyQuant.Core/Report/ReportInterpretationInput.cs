using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FamilyQuant.Report;

/// <summary>What the interpretation input file holds: the user's report details and panel, the report's Methods section
/// and the stored facts the summary of findings reads. Nothing here is computed for the file alone.</summary>
public sealed record ReportInterpretationInputData
{
    public ReportDetails Details { get; init; } = new();
    /// <summary>The report's own Methods section: project, inputs, search and quantitation rules, link choices, de novo cutoffs.</summary>
    public ReportSection? Methods { get; init; }
    public ReportFindingsInput Findings { get; init; } = new();
    public string ApplicationVersion { get; init; } = string.Empty;
}

/// <summary>
/// One Markdown file a user can give to an assistant of their choice: report details, the panel as entered, the summary
/// of findings and every stored table it reads, the analysis choices, and a neutral instruction block. The program never
/// calls a language model. Same project, same bytes: no clock and invariant numbers.
/// </summary>
public static class ReportInterpretationInput
{
    public const string FileName = "FamilyQuant_interpretation_input.md";
    public const string Title = "Interpretation input";
    public static readonly IReadOnlyList<string> Instructions = new[]
    {
        "Quote every number exactly as printed here and name the table or sentence it comes from.",
        "Compute a new number only when you show the calculation and the printed values it uses.",
        "Shares are percentages of the credited family signal; a column that names the measured signal is a percentage of the measured peptide signal.",
        "SD is the sample standard deviation across runs, in percentage points; CV is the coefficient of variation.",
        "Panel names, decisions and text fields are the user's entries, printed as entered; they are not measurements.",
        "Families under Others are one aggregate row in the report; their rows here are for reference.",
        "When these tables cannot answer a question, say so.",
    };

    public static string FromProject(FamilyClusterFile curation, QuantManifest manifest, string applicationVersion)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.FamilyRunShares == null)
            throw new InvalidOperationException("The project has no stored family shares. Use Analysis > Compute full XIC and save the Results first.");
        FamilyStudyMembership.RequireCurrent(curation, manifest);
        // The same stored view and grouping as the report: no-peptide rows first, then the canonical partition.
        manifest = FamilyUnobservedStudies.View(curation, manifest);
        var labels = FamilyNaming.Names(curation.MyExtendedFastaItems.Where(p => !SequenceLabels.IsDecoy(p.SequenceIdentifier)));
        var grouping = FamilyReportThreshold.Build(manifest.FamilyRunShares!, curation);
        return Build(new ReportInterpretationInputData
        {
            Details = curation.ReportDetails ?? new(), Methods = ReportMethods.Build(curation, manifest),
            Findings = ReportFindings.InputFromProject(curation, manifest, grouping, labels)!, ApplicationVersion = applicationVersion,
        });
    }

    public static string Build(ReportInterpretationInputData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var input = data.Findings;
        var text = new StringBuilder();
        void Line(string value = "") => text.Append(value).Append('\n');
        void Heading(string value) { Line(); Line(value); Line(); }
        void Table(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
        {
            Line("| " + string.Join(" | ", headers.Select(Cell)) + " |");
            Line("|" + string.Concat(headers.Select(_ => "---|")));
            foreach (var row in rows) Line("| " + string.Join(" | ", row.Select(Cell)) + " |");
        }
        // Columns of a program export, renamed for reading; the values stay the export's own text.
        void Export(string csv, (string Column, string Header)[] columns)
        {
            var table = ReportCsvTable.Build("input", "input", csv, "", columns.Select(c => c.Column).ToArray());
            Table(columns.Select(c => c.Header).ToArray(), table.Rows);
        }

        Line("# " + Title);
        Line();
        Line("Written by FamilyQuant" + (string.IsNullOrWhiteSpace(data.ApplicationVersion) ? "" : " " + data.ApplicationVersion.Trim())
            + " from the project's stored results. It holds the report details, the family panel as entered, the summary of findings, "
            + "the stored tables it reads and the analysis choices in force. It contains no interpretation.");

        Heading("## Instructions for the reader");
        foreach (string instruction in Instructions) Line("- " + instruction);

        Heading("## Report details");
        var details = data.Details;
        Table(new[] { "Field", "Value" }, new[]
        {
            new[] { "Title", Entered(details.Title) }, new[] { "Authors", Entered(details.Authors) },
            new[] { "Sample description", Entered(details.SampleDescription) }, new[] { "Workflow", Entered(details.Workflow) },
            new[] { "Kind of database", Entered(details.DatabaseKind) }, new[] { "Written interpretation", Entered(details.WrittenInterpretation) },
        });

        if (input.Panel is { } panel)
        {
            Heading("## Family panel");
            if (!string.IsNullOrWhiteSpace(panel.Name)) { Line("Panel name: " + Flat(panel.Name)); Line(); }
            Table(new[] { "Key", "Name", "Decision as entered", "Action", "Merge target", "Pfam domains", "Database members" },
                panel.Families.Select(f => (IReadOnlyList<string>)new[]
                {
                    f.Key, f.DisplayName, f.SourceDecision, f.Decision.ToString(), f.MergeTargetFamilyKey,
                    string.Join("; ", f.Pfam.Select(d => string.IsNullOrEmpty(d.Name) ? d.Accession : d.Accession + " " + d.Name)),
                    Integer(input.MembersByPanelKey.GetValueOrDefault(f.Key)),
                }));
            var rules = panel.Families.SelectMany(f => f.Subclasses.Select(s => (IReadOnlyList<string>)new[]
            {
                f.Key, s.Key, s.DisplayName, string.Join("; ", s.RequiredPfam), string.Join("; ", s.AbsentPfam),
                string.Join("; ", s.UniProtSubfamilies), s.ReportAsSubclassKey,
            })).ToList();
            if (rules.Count > 0)
            {
                Heading("### Subclass rules");
                Table(new[] { "Family key", "Subclass key", "Name", "Required Pfam", "Absent Pfam", "Source subfamilies", "Reported as" }, rules);
            }
            var fields = panel.Levels.Select(t => (IReadOnlyList<string>)new[] { "Panel", "Level", t.Name, t.Text })
                .Concat(panel.TextFields.Select(t => (IReadOnlyList<string>)new[] { "Panel", "Text", t.Name, t.Text }))
                .Concat(panel.Families.SelectMany(f => f.Names.Select(t => (IReadOnlyList<string>)new[] { f.Key, "Name", t.Name, t.Text })
                    .Concat(f.TextFields.Select(t => (IReadOnlyList<string>)new[] { f.Key, "Text", t.Name, t.Text }))))
                .Where(r => !string.IsNullOrWhiteSpace(r[3])).ToList();
            if (fields.Count > 0)
            {
                Heading("### Panel text as entered");
                Table(new[] { "Key", "Kind", "Field", "Text" }, fields);
            }
        }

        Heading("## " + ReportFindings.Title);
        Line(string.Join("\n\n", ReportFindings.Build(input).Section.Blocks.OfType<ReportParagraph>().Select(p => p.Text)));

        Heading("## Families in the headline");
        Export(ReportStudyExports.Headline(input.Grouping, input.Counts, input.Labels), new[]
        {
            ("family_user_id", "User ID"), ("family", "Family"), ("status", "Status"), ("available_runs", "Runs"), ("mean_pct", "Mean share (%)"),
            ("sample_sd_pct", "SD (percentage points)"), ("cv_pct", "CV (%)"), ("mean_measured_pct", "Mean share of measured signal (%)"),
            ("greedy_protein_estimate", "Greedy protein estimate"), ("unique_peptide_proteins", "Proteins with a peptide of their own"),
            ("protein_explanation", "Protein estimate"),
        });

        if (input.Grouping.OtherFamilies.Count > 0)
        {
            Heading("## Families under Others");
            Export(ReportStudyExports.Others(input.Grouping, input.Labels), new[]
            {
                ("family_user_id", "User ID"), ("family", "Family"), ("reason", "Reason"), ("mean_pct", "Mean share (%)"),
                ("sample_sd_pct", "SD (percentage points)"), ("cv_pct", "CV (%)"),
            });
        }

        var unobserved = ReportFindings.UnobservedPanelKeys(input);
        if (unobserved.Count > 0)
        {
            Heading("## Quantified panel families with no peptide observed");
            Table(new[] { "Key", "Name", "Database members" }, unobserved.Select(k => (IReadOnlyList<string>)new[]
                { k, input.Panel!.Families.Single(f => f.Key == k).DisplayName, Integer(input.MembersByPanelKey[k]) }));
        }

        var parts = ReportFindings.StatedSubclassParts(input);
        if (parts.Count > 0)
        {
            Heading("## Subclass parts, all runs pooled");
            Line(ReportFindings.NotResolvedMethod);
            Line();
            var order = parts.Select(p => p.FamilyUserId).ToList();
            var rows = ReportCsvTable.Build("parts", "parts", ReportStudyExports.Parts(parts, input.Labels), "",
                    "family_user_id", "family", "part", "unresolved", "within_family_pct").Rows
                .OrderBy(r => order.IndexOf(int.Parse(r[0], CultureInfo.InvariantCulture))).ToList();
            Table(new[] { "User ID", "Family", "Part", "Not resolved", "Share of the family (%)" },
                rows.Select(r => (IReadOnlyList<string>)new[] { r[0], r[1], r[2], r[3] == "1" ? "Yes" : "No", r[4] }));
        }

        if (input.Shares.Runs.Count > 0)
        {
            Heading("## Signal per run");
            Export(ReportStudyExports.RunTotals(input.Shares), new[]
            {
                ("run", "Run"), ("assigned_family_signal", "Credited family signal"), ("measured_peptide_signal", "Measured peptide signal"),
                ("raw_tic", "Total ion current"),
            });
        }

        Heading("## Analysis choices in force");
        Line("- Report threshold: " + Percent(input.Grouping.Threshold) + "% of the credited family signal");
        Line("- Shared peptides: " + input.Shares.Policy);
        Line("- Evidence scope phrases: " + (input.ScopeTerms.Count == 0 ? "none" : string.Join(", ", input.ScopeTerms.Select(t => "\"" + Flat(t) + "\""))));

        // The report's Methods section as written: paragraphs, and each table under its title with its caption.
        if (data.Methods is { } methods)
        {
            Heading("## " + methods.Title);
            foreach (var block in methods.Blocks)
                if (block is ReportParagraph paragraph) { Line(paragraph.Text); Line(); }
                else if (block is ReportTable table && !table.SupplementOnly)
                {
                    Line("### " + table.Title);
                    Line();
                    if (!string.IsNullOrWhiteSpace(table.Caption)) { Line(Flat(table.Caption)); Line(); }
                    Table(table.Columns, table.Rows);
                    Line();
                }
        }
        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static string Entered(string value) => string.IsNullOrWhiteSpace(value) ? "None entered" : value;
    // One line of text: line breaks kept as <br>, otherwise as entered.
    private static string Flat(string? value) => (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
        .Replace("\n", "<br>", StringComparison.Ordinal);
    // One table cell: also escapes the column separator.
    private static string Cell(string value) => Flat(value).Replace("|", "\\|", StringComparison.Ordinal);
    // The same renderings as ReportStudyExports: percentages to six significant digits, integers invariant.
    private static string Percent(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? (100 * value.Value).ToString("G6", CultureInfo.InvariantCulture) : string.Empty;
    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);
}
