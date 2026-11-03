using FamilyQuant.Report;
using System;
using System.Text.Json.Serialization;

namespace FamilyQuant;

public partial class FamilyClusterFile
{
    /// <summary>User-authored report text, stored in fqproj curation.json. Absent in older files.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReportDetails? ReportDetails { get; set; }
}

public sealed partial class GroupingSnapshot
{
    private FamilyClusterFile? reportDetailsFile;
    private ReportDetails? reportDetails;

    /// <summary>One metadata edit; restoring it does not rewrite grouping, locks or annotations.</summary>
    public static GroupingSnapshot CaptureReportDetails(FamilyClusterFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var snapshot = Capture(file);
        snapshot.reportDetailsFile = file;
        snapshot.reportDetails = file.ReportDetails;
        return snapshot;
    }

    private bool RestoreReportDetails(FamilyClusterFile file)
    {
        if (reportDetailsFile == null) return false;
        if (!ReferenceEquals(file, reportDetailsFile))
            throw new InvalidOperationException("Report details belong to a different project; their Undo cannot be restored here.");
        file.ReportDetails = reportDetails;
        return true;
    }
}
