using System.Collections.Generic;

namespace FamilyQuant.Report;

/// <summary>A complete native detail table and its stable owning section/block. The writer uses
/// Name verbatim as its unique Excel sheet name. Excluding Id or OwnerSectionId omits this sheet.</summary>
public sealed record ReportSupplementSheet(string Id, string OwnerSectionId, string Name, ReportTable Table);

/// <summary>The compact Word document and its complete companion tables.</summary>
public sealed record ReportSupplementLayoutResult(ReportDocument Document, IReadOnlyList<ReportSupplementSheet> Sheets);
