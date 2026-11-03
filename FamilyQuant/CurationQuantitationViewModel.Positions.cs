using FamilyQuant.Evidence.Positions;
using System.Collections.Generic;

namespace FamilyQuant;

public sealed partial class CurationQuantitationViewModel
{
    public IReadOnlyList<string> PositionAccessions(IReadOnlyCollection<int>? additionalFamilyIds = null) =>
        PositionFetchScope.FromMappedPeptides(fullXicHits, Document, additionalFamilyIds);
}
