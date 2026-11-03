using System;

namespace FamilyQuant.Report;

/// <summary>Base document from saved project metadata. Results are supplied by result builders.</summary>
public static class ReportDocumentBuilder
{
    public static ReportDocument Build(FamilyClusterFile curation, QuantManifest manifest,
        ReportDetails? details = null, ReportInputProvenance? methodFacts = null)
    {
        ArgumentNullException.ThrowIfNull(curation);
        ArgumentNullException.ThrowIfNull(manifest);
        details ??= curation.ReportDetails ?? new ReportDetails();
        DateTime Date(DateTime value) => value == default ? new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) : value;
        return new ReportDocument
        {
            Title = string.IsNullOrWhiteSpace(details.Title) ? "Analysis report" : details.Title,
            Authors = details.Authors,
            ApplicationVersion = manifest.AppVersion,
            CreatedUtc = Date(manifest.CreatedUtc), ModifiedUtc = Date(manifest.UpdatedUtc),
            Sections = new[]
            {
                new ReportSection(ReportSectionIds.Title, "Title", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.Contents, "Contents", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.Summary, "Summary", Array.Empty<ReportBlock>()),
                ReportMethods.Build(curation, manifest, details, methodFacts),
                new ReportSection(ReportSectionIds.Results, "Results", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.Quality, "Quality", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.Sensitivity, "Sensitivity", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.DatabaseGap, "Database gap", Array.Empty<ReportBlock>()),
                new ReportSection(ReportSectionIds.Interpretation, "Interpretation",
                    new ReportBlock[] { new ReportParagraph("interpretation-text", details.WrittenInterpretation) }),
                new ReportSection(ReportSectionIds.Supplements, "Supplements", Array.Empty<ReportBlock>()),
            },
        };
    }
}
