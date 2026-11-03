using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Report;

public static partial class ReportStudyFigures
{
    /// <summary>One stable figure id owns its caption facts and complete reference data, so the same
    /// checkbox excludes both without duplicate workbook ids.</summary>
    public static IReadOnlyList<ReportSupplementSheet> CombineFigureSupplements(
        IReadOnlyList<ReportSupplementSheet> details, IReadOnlyList<ReportSupplementSheet> captions)
    {
        var remaining = details.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var result = new List<ReportSupplementSheet>();
        foreach (var caption in captions)
        {
            if (!remaining.Remove(caption.Id, out var detail)) { result.Add(caption); continue; }
            if (caption.OwnerSectionId != detail.OwnerSectionId)
                throw new InvalidOperationException("Figure details and caption facts have different section owners.");
            var columns = caption.Table.Columns.Concat(detail.Table.Columns.Select(c => "Data " + c)).ToArray();
            var emptyDetails = Enumerable.Repeat("", detail.Table.Columns.Count);
            var emptyCaption = Enumerable.Repeat("", caption.Table.Columns.Count);
            var rows = caption.Table.Rows.Select(r => (IReadOnlyList<string>)r.Concat(emptyDetails).ToArray())
                .Concat(detail.Table.Rows.Select(r => (IReadOnlyList<string>)emptyCaption.Concat(r).ToArray())).ToArray();
            result.Add(new(caption.Id, caption.OwnerSectionId, detail.Name, caption.Table with
            {
                Title = caption.Table.Title + " and complete figure data", Columns = columns, Rows = rows,
                Caption = caption.Table.Caption + " " + detail.Table.Caption
            }));
        }
        result.AddRange(remaining.Values);
        return result.AsReadOnly();
    }
}
