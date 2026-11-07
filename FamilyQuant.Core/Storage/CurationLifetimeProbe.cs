using System;

namespace FamilyQuant
{
    internal static class CurationLifetimeProbe
    {
        internal static Action<object>? Created = null;
    }
}

namespace FamilyQuant.Evidence
{
    public sealed partial class EvidenceProvenance
    {
        public EvidenceProvenance() => CurationLifetimeProbe.Created?.Invoke(this);
    }
}
