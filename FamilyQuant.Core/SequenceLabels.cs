using System;

namespace FamilyQuant
{
    public static class SequenceLabels
    {
        public const string DecoyPrefix = "Reverse_";
        public const string ContaminantMarker = "contaminant";
        public const string ContaminantPrefix = ContaminantMarker + "_";

        public static bool IsDecoy(string identifier) => identifier.StartsWith(DecoyPrefix, StringComparison.Ordinal);

        public static bool IsContaminant(string? identifier)
        {
            string target = identifier ?? string.Empty;
            if (target.StartsWith(DecoyPrefix, StringComparison.Ordinal)) target = target[DecoyPrefix.Length..];
            return target.StartsWith(ContaminantPrefix, StringComparison.Ordinal);
        }
    }
}
