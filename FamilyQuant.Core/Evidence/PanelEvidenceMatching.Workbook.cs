using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant.Evidence;

public sealed partial class PanelEvidenceMatching
{
    /// <summary>Detached canonical pairs from stored provider evidence, excluding source-sheet family definitions.</summary>
    public IReadOnlyList<FamilyPanelDomain> RecognizedPfamDomains() => Array.AsReadOnly(entries.SelectMany(entry => entry.Labels)
        .Where(label => label.Kind == PanelEvidenceMatchKind.Pfam && label.Value.Length > 0)
        .DistinctBy(label => (label.Value, label.Name))
        .OrderBy(label => label.Value, StringComparer.Ordinal).ThenBy(label => label.Name, StringComparer.Ordinal)
        .Select(label => new FamilyPanelDomain { Accession = label.Value, Name = label.Name }).ToArray());
}
