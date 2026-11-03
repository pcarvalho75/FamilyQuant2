using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using FamilyQuant.Report.Captions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Report;

public sealed record ReportProjectExport(ReportDocument Document, IReadOnlyDictionary<string, string> CsvExports,
    IReadOnlyList<ReportPlacedFigure> Figures)
{
    public IReadOnlyList<ReportSupplementSheet> SupplementSheets { get; init; } = Array.Empty<ReportSupplementSheet>();
    public ReportSelection? Selection { get; init; }
}

/// <summary>Program-owned saved-project route shared by the shell and reproducibility command.</summary>
public static class ReportProjectExporter
{
    public static ReportProjectExport Build(IReadOnlyList<QuantRow> rows, QuantManifest manifest,
        FamilyClusterFile curation, string pngFolder, ReportDetails? details = null,
        ReportInputProvenance? provenance = null, FamilyEnvelopeQuantResult? familyResults = null, AnalyticsData? analyticsData = null,
        OperationContext? operation = null, string? homologyCacheDirectory = null)
    {
        if (rows.Count == 0) throw new InvalidOperationException("No full XIC results are stored. Compute and save results before exporting a report.");
        FamilyStudyMembership.RequireCurrent(curation, manifest);
        manifest = FamilyUnobservedStudies.View(curation, manifest);
        var data = analyticsData ?? AnalyticsBuilder.Build(rows, manifest.PerRunTic);
        if (analyticsData == null && manifest.DerivedFractions.Count > 0) NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, manifest.DerivedFractions);
        var options = new Homology.HomologyOptions();
        if (manifest.FamilyMapping != null) options.MinIdentity = manifest.FamilyMapping.MinIdentity;
        var families = familyResults;
        if (families == null && curation.MyExtendedFastaItems.Count > 0)
        {
            operation?.Report("report-primary-start", "Data step 2: primary homology lookup and search");
            var hits = CurationHomologyCache.GetOrSearch(rows, curation, homologyCacheDirectory, options,
                progress: hit => operation?.Report("report-primary-cache", hit
                    ? "Data step 2: primary homology cache hit" : "Data step 2: primary homology cache miss"),
                cancellationToken: operation?.CancellationToken ?? default);
            operation?.Report("report-primary-end", "Data step 2: primary homology lookup and search complete");
            families = FamilyEnvelopeQuant.BuildFromAttribution(rows, FamilyEnvelopeQuant.AttributeHits(hits, curation), operation);
        }
        var grouping = manifest.FamilyRunShares == null ? null : FamilyReportThreshold.Build(manifest.FamilyRunShares, curation);
        var result = ReportCurrentResults.Build(data, manifest, families, manifest.FamilyRunShares?.Policy ?? SharedPeptidePolicy.FamilyUnique, grouping);
        if (manifest.FamilySensitivity is { } sensitivity && (sensitivity.CurationFingerprint != FamilySensitivityStudy.CurationFingerprint(curation)
            || sensitivity.Variants.Count == 0 || sensitivity.Variants[0].SourceSha256 != FamilySensitivityStudy.CurrentFingerprint(rows, manifest, curation)))
            throw new InvalidDataException("Stored sensitivity results do not match this project's current quantitation and grouping. Recompute the sensitivity study before reporting it.");
        var labels = families?.Families.ToDictionary(f => f.FamilyUserId, f => f.Description)
            ?? new Dictionary<int, string>();
        result = ReportStoredResults.Apply(result, manifest, labels, grouping);
        var plan = ReportFigureRenderer.Plan(data, manifest, curation, families, grouping);
        var studies = ReportStudyFigures.Build(manifest, curation, pngFolder, topProteinsPerPanelFamily: 1, reportGrouping: grouping);
        var figures = ReportFigureRenderer.Render(plan, pngFolder).Concat(studies.Figures).ToArray();
        var unavailable = plan.Unavailable.Concat(studies.Unavailable).ToArray();
        var document = ReportDocumentBuilder.Build(curation, manifest, details, provenance) with { ApplicationVersion = AppVersionInfo.Current() };
        var replacements = result.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        document = document with
        {
            Sections = document.Sections.Select(original =>
            {
                var section = replacements.GetValueOrDefault(original.Id) ?? original;
                var blocks = section.Blocks.ToList();
                if (section.Id == ReportSectionIds.Summary && curation.FamilyPanel == null)
                    blocks.Insert(0, new ReportParagraph("current-grouping-notice",
                        "Draft of the saved current grouping. No family panel is stored in this project. Group-specific, shared and unattributed signal remain separate; panel-based assignments can change both the grouping and its shared-signal fraction."));
                blocks.AddRange(figures.Where(f => f.SectionId == section.Id).Select(f => (ReportBlock)f.Figure));
                blocks.AddRange(unavailable.Where(f => f.SectionId == section.Id).Select((f, index) =>
                    (ReportBlock)new ReportParagraph(section.Id + ":unavailable-figure:" + index.ToString(CultureInfo.InvariantCulture), f.Title + ": " + f.Reason)));
                return section with { Blocks = blocks };
            }).ToArray(),
        };
        var sections = document.Sections.ToList();
        if (replacements.TryGetValue(ReportSectionIds.AssignmentConflicts, out var conflictSection))
            sections.Insert(sections.FindIndex(s => s.Id == ReportSectionIds.Results) + 1, conflictSection);
        int familyPosition = sections.FindIndex(s => s.Id == ReportSectionIds.AssignmentConflicts);
        familyPosition = familyPosition >= 0 ? familyPosition + 1 : sections.FindIndex(s => s.Id == ReportSectionIds.Results) + 1;
        sections.InsertRange(familyPosition, ReportPanelSections.BuildFromResults(curation, manifest, grouping).Select(section => section with
        {
            Blocks = section.Blocks.Concat(figures.Where(f => f.SectionId == section.Id).Select(f => (ReportBlock)f.Figure))
                .Concat(unavailable.Where(f => f.SectionId == section.Id).Select((f, index) => (ReportBlock)new ReportParagraph(section.Id + ":unavailable-map:" + index.ToString(CultureInfo.InvariantCulture), f.Title + ": " + f.Reason))).ToArray(),
        }));
        var sourceWorld = manifest.FamilyWorldResult?.ForLength(manifest.DatabaseGapMinimumTagLength ?? FamilyWorldOptions.DefaultMinimumTagLength);
        if (sourceWorld != null)
            for (int i = 0; i < sections.Count; i++)
            {
                var blocks = sections[i].Blocks.ToList();
                int sourcePosition = blocks.FindLastIndex(b => b is ReportFigure figure && figure.Id.StartsWith("figure:family_signal_sources", StringComparison.Ordinal));
                if (sourcePosition >= 0)
                {
                    blocks.Insert(sourcePosition + 1, ReportSourceSupport.Build(sourceWorld, grouping));
                    sections[i] = sections[i] with { Blocks = blocks };
                }
            }
        document = document with { Sections = sections };
        var figureCaptions = plan.Charts.ToDictionary(c => "figure:" + c.FileName,
            c => c.CaptionInput ?? throw new InvalidOperationException("Caption data are unavailable for " + c.FileName), StringComparer.Ordinal);
        foreach (var binding in studies.CaptionBindings) figureCaptions.Add(binding.Key, binding.Value);
        var tableCaptions = document.Sections.SelectMany(s => s.Blocks).OfType<ReportTable>().ToDictionary(t => t.Id,
            t => new TableCaptionInput(t.Title, string.IsNullOrWhiteSpace(t.Caption)
                ? "Read across each row using the named columns and units" : t.Caption,
                SummaryColumns: Array.Empty<int>()), StringComparer.Ordinal);
        document = ReportCaptionBuilder.WithCaptions(document, figureCaptions, tableCaptions);
        var exports = new Dictionary<string, string>(result.CsvExports, StringComparer.Ordinal)
        {
            ["protein_audit.csv"] = PanelAuditCsv.BuildCsv(curation, curation.PanelProteinAssignments == null
                ? Array.Empty<PanelFamilyAuditEvidence>()
                : Evidence.PanelAssignmentEvidenceStore.RequireCurrent(curation).Select(a => new PanelFamilyAuditEvidence
                {
                    ProteinIndex = a.ProteinIndex, Identifier = a.Identifier, EvidenceUsed = a.EvidenceKind.ToString(), Reason = a.Reason,
                }).ToArray()),
        };
        if (sourceWorld != null && studies.Figures.Any(f => f.Figure.Id.StartsWith("figure:family_signal_sources", StringComparison.Ordinal)))
            exports.Add("family_sources_support.csv", ReportStudyExports.SourceSupport(sourceWorld));
        var layout = ReportSupplementLayout.Apply(document, exports);
        var sheets = layout.Sheets.Concat(ReportCaptionTrace.Build(document, figureCaptions)).ToArray();
        return new(layout.Document, exports, figures) { SupplementSheets = ReportTableSupplements.Complete(layout.Document, sheets), Selection = curation.ReportSelection };
    }

    public static void Write(ReportProjectExport export, string docxPath, ReportSelection? selection = null)
    {
        selection ??= export.Selection;
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(docxPath))!, Path.GetFileNameWithoutExtension(docxPath) + "_data");
        Directory.CreateDirectory(folder);
        foreach (var csv in export.CsvExports) File.WriteAllText(Path.Combine(folder, csv.Key), csv.Value, new UTF8Encoding(false));
        string workbook = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(docxPath))!, Path.GetFileNameWithoutExtension(docxPath) + "_supplement.xlsx");
        File.WriteAllBytes(workbook, ReportSupplementWorkbook.BuildBytes(export.Document, export.SupplementSheets, selection));
        WordReportWriter.Write(docxPath, export.Document, selection);
    }
}
