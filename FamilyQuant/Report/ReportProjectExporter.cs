using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
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
        ReportInputProvenance? provenance = null, FamilyEnvelopeQuantResult? familyResults = null)
    {
        if (rows.Count == 0) throw new InvalidOperationException("No full XIC results are stored. Compute and save results before exporting a report.");
        FamilyStudyMembership.RequireCurrent(curation, manifest);
        var data = AnalyticsBuilder.Build(rows, manifest.PerRunTic);
        if (manifest.DerivedFractions.Count > 0) NativeQuantFractions.ApplyDetectedToFractions(data.Fractions, data.Grouped, manifest.DerivedFractions);
        var options = new Homology.HomologyOptions();
        if (manifest.FamilyMapping != null) options.MinIdentity = manifest.FamilyMapping.MinIdentity;
        var families = familyResults ?? (curation.MyExtendedFastaItems.Count > 0 ? FamilyEnvelopeQuant.BuildFromCuration(rows, curation, options) : null);
        var result = ReportCurrentResults.Build(data, manifest, families, manifest.FamilyRunShares?.Policy ?? SharedPeptidePolicy.FamilyUnique);
        var plan = ReportFigureRenderer.Plan(data, manifest, curation, families);
        var figures = ReportFigureRenderer.Render(plan, pngFolder);
        var document = ReportDocumentBuilder.Build(curation, manifest, details, provenance);
        var replacements = result.Sections.ToDictionary(s => s.Id, StringComparer.Ordinal);
        document = document with
        {
            Sections = document.Sections.Select(original =>
            {
                var section = replacements.GetValueOrDefault(original.Id) ?? original;
                var blocks = section.Blocks.ToList();
                blocks.AddRange(figures.Where(f => f.SectionId == section.Id).Select(f => (ReportBlock)f.Figure));
                blocks.AddRange(plan.Unavailable.Where(f => f.SectionId == section.Id).Select((f, index) =>
                    (ReportBlock)new ReportParagraph(section.Id + ":unavailable-figure:" + index, f.Title + ": " + f.Reason)));
                return section with { Blocks = blocks };
            }).ToArray(),
        };
        return new(document, result.CsvExports, figures);
    }

    public static void Write(ReportProjectExport export, string docxPath, ReportSelection? selection = null)
    {
        string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(docxPath))!, Path.GetFileNameWithoutExtension(docxPath) + "_data");
        Directory.CreateDirectory(folder);
        foreach (var csv in export.CsvExports) File.WriteAllText(Path.Combine(folder, csv.Key), csv.Value, new UTF8Encoding(false));
        WordReportWriter.Write(docxPath, export.Document, selection);
    }
}
