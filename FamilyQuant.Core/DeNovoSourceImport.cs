using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant;

/// <summary>Inspect selected sources with the same native cutoffs and shared criteria as full XIC.</summary>
public static class DeNovoSourceImport
{
    public static DeNovoImport Run(string directory, DeNovoFilterCriteria criteria, IReadOnlyList<DeNovoSourceSelection> selections)
    {
        if (selections.Count == 0) throw new ArgumentException("Choose at least one de novo source.", nameof(selections));
        if (selections.Count == 1)
        {
            var single = DeNovoScreenOperations.ImportAndFilter(directory, ForSource(criteria, selections[0]), selections[0].Source);
            Stamp(single, criteria, selections);
            return single;
        }
        foreach (var selection in selections) DeNovoSourceFileDiscovery.RequireFiles(directory, selection.Source);
        var result = new DeNovoImport { Directory = directory, SourceId = string.Join("+", selections.Select(s => s.Source.Id)),
            SourceDisplayName = string.Join(" + ", selections.Select(s => s.Source.DisplayName)), ConfidenceField = "Native confidence (%)" };
        foreach (var selection in selections.OrderBy(s => s.Priority))
        {
            var sourceCriteria = ForSource(criteria, selection);
            var input = DeNovoScreenOperations.ImportAndFilter(directory, sourceCriteria, selection.Source);
            result.FilesFound += input.FilesFound;
            result.RowsRead += input.RowsRead;
            result.Kept.AddRange(input.Kept);
            result.PerRun.AddRange(input.PerRun);
        }
        result.PerRun = result.PerRun.GroupBy(t => t.Run, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new DeNovoRunTally
                { Run = g.Key, RowsRead = g.Sum(t => t.RowsRead), RowsKept = g.Sum(t => t.RowsKept) }).ToList();
        Stamp(result, criteria, selections);
        return result;
    }
    private static DeNovoFilterCriteria ForSource(DeNovoFilterCriteria criteria, DeNovoSourceSelection source) => new()
        { MinLength = criteria.MinLength, StretchLength = criteria.StretchLength, StretchFloor = criteria.StretchFloor,
            MinAverageLocalConfidence = source.MinimumConfidence };
    private static void Stamp(DeNovoImport result, DeNovoFilterCriteria criteria, IReadOnlyList<DeNovoSourceSelection> selections)
    {
        result.Selections = selections.Select(s => new DeNovoSourceSelection(s.Source, s.MinimumConfidence, s.Priority)).ToList();
        result.AppliedCriteria = new() { MinLength = criteria.MinLength, MinAverageLocalConfidence = selections.OrderBy(s => s.Priority).First().MinimumConfidence,
            StretchLength = criteria.StretchLength, StretchFloor = criteria.StretchFloor };
    }
}
