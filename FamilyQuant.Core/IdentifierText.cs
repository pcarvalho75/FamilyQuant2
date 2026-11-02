namespace FamilyQuant
{
    public static class IdentifierText
    {
        public const string HideDecoysToolTip =
            "Hides decoys, whose identifier starts with '" + SequenceLabels.DecoyPrefix +
            "', from the table and the CSV. Nothing is deleted and no percentage moves; keep them visible while judging the clustering.";

        public const string HideContaminantsToolTip =
            "Hides table and CSV entries labelled '" + SequenceLabels.ContaminantPrefix +
            "' after one optional '" + SequenceLabels.DecoyPrefix +
            "' prefix, both case-sensitive. Percentages stay unchanged; labelled keratins, trypsin and serum proteins are included, " +
            "so visible identified-signal shares may total below 100%.";

        public const string CalculateFdrToolTip =
            "Shows the fraction of families that mix real and decoy (" + SequenceLabels.DecoyPrefix +
            ") sequences in a target-decoy (.T-R) database; zero is the usual value. Disabled until a Family Cluster File is generated above.";

        public const string DecoyPrefixToolTip =
            "Case-sensitive decoy prefix, normally " + SequenceLabels.DecoyPrefix +
            ", which FamilyQuant writes. Search refuses an empty box.";
    }
}
