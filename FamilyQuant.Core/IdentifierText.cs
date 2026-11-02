namespace FamilyQuant
{
    public static class IdentifierText
    {
        public const string HideDecoysToolTip =
            "Hides decoys, whose identifier starts with '" + SequenceLabels.DecoyPrefix +
            "', from the table and the CSV. Nothing is deleted and no percentage moves; keep them visible while judging the clustering.";

        public const string HideContaminantsToolTip =
            "Hides entries whose identifier starts with '" + SequenceLabels.ContaminantPrefix +
            "' after one optional '" + SequenceLabels.DecoyPrefix +
            "' prefix. Both prefixes are case-sensitive. This filters the table and CSV, including labelled keratins, trypsin and serum proteins. " +
            "Percentages do not change, so in '% of identified signal' the visible ones no longer add up to 100%.";

        public const string CalculateFdrToolTip =
            "Shows the fraction of families that mix real and decoy (" + SequenceLabels.DecoyPrefix +
            ") sequences; zero is what you want. Needs a target-decoy (.T-R) database. Disabled until a Family Cluster File is generated above.";

        public const string DecoyPrefixToolTip =
            "The decoy prefix, case-sensitive. Default " + SequenceLabels.DecoyPrefix +
            ", which FamilyQuant writes. An empty box is refused when you press Search.";
    }
}
