using System;
using System.Collections.Generic;
using System.Globalization;
using FamilyQuant.Operations;

namespace FamilyQuant.Report;

/// <summary>Base document from saved project metadata. Results are supplied by result builders.</summary>
public static class ReportDocumentBuilder
{
    public static ReportDocument Build(FamilyClusterFile curation, QuantManifest manifest,
        ReportDetails? details = null, ReportInputProvenance? methodFacts = null, OperationContext? operation = null,
        int additionalSectionCount = 0)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        details ??= curation.ReportDetails ?? new ReportDetails();
        methodFacts ??= curation.ReportInputProvenance;
        var factories = new (string Title, Func<ReportSection> Build)[]
        {
            ("Title", () => new(ReportSectionIds.Title, "Title", Array.Empty<ReportBlock>())),
            ("Contents", () => new(ReportSectionIds.Contents, "Contents", Array.Empty<ReportBlock>())),
            ("Summary", () => new(ReportSectionIds.Summary, "Summary", Array.Empty<ReportBlock>())),
            (ReportFindings.Title, () => new(ReportSectionIds.Findings, ReportFindings.Title, Array.Empty<ReportBlock>())),
            ("Methods", () => ReportMethods.Build(curation, manifest, details, methodFacts)),
            ("Results", () => new(ReportSectionIds.Results, "Results", Array.Empty<ReportBlock>())),
            ("Quality", () => new(ReportSectionIds.Quality, "Quality", Array.Empty<ReportBlock>())),
            ("Sensitivity", () => new(ReportSectionIds.Sensitivity, "Sensitivity", Array.Empty<ReportBlock>())),
            ("Database gap", () => new(ReportSectionIds.DatabaseGap, "Database gap", Array.Empty<ReportBlock>())),
            ("Interpretation", () => new(ReportSectionIds.Interpretation, "Interpretation", new ReportBlock[] { new ReportParagraph("interpretation-text", details.WrittenInterpretation) })),
            ("Supplements", () => new(ReportSectionIds.Supplements, "Supplements", Array.Empty<ReportBlock>())),
        };
        int total = factories.Length + additionalSectionCount;
        operation?.Report("report-prepare-section", "Preparing report sections", 0, total);
        var sections = new List<ReportSection>(factories.Length);
        for (int i = 0; i < factories.Length; i++)
        {
            operation?.Report("report-prepare-section", "Section " + (i + 1).ToString(CultureInfo.InvariantCulture) + " of "
                + total.ToString(CultureInfo.InvariantCulture) + ": " + factories[i].Title, i + 1, total);
            sections.Add(factories[i].Build());
        }
        DateTime Date(DateTime value) => value == default ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : value;
        return new ReportDocument
        {
            Title = string.IsNullOrWhiteSpace(details.Title) ? "Analysis report" : details.Title,
            Authors = details.Authors,
            ApplicationVersion = manifest.AppVersion,
            CreatedUtc = Date(manifest.CreatedUtc), ModifiedUtc = Date(manifest.UpdatedUtc),
            Sections = sections,
        };
    }
}
