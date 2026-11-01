namespace FamilyQuant
{
    /// <summary>
    /// One point of the identity sweep: what a given USEARCH identity threshold produced.
    /// </summary>
    public class IdentityScanRow
    {
        public int Identity { get; set; }
        public int Groups { get; set; }

        /// <summary>Families containing at least one target sequence (excludes pure-decoy families).</summary>
        public int TargetGroups { get; set; }

        /// <summary>Connected components with at least one non-decoy, non-contaminant target.</summary>
        public int NonContaminantTargetGroups { get; set; } = -1;

        public int ReportedTargetGroups => NonContaminantTargetGroups >= 0
            ? NonContaminantTargetGroups : TargetGroups;

        public int MixedGroups { get; set; }
        public double Score { get; set; }
        public double TargetRelativeScore { get; set; }

        /// <summary>The families behind <see cref="MixedGroups"/>, smallest first.</summary>
        public System.Collections.Generic.List<MixedFamily> MixedFamilies { get; set; } =
            new System.Collections.Generic.List<MixedFamily>();

        /// <summary>
        /// The report section naming the mixed families at each identity that has any, from the strictest
        /// down, with at most <paramref name="perIdentity"/> families spelled out per line. Empty when
        /// nothing mixed or the rows carry no family detail.
        /// </summary>
        public static System.Collections.Generic.List<string> MixedFamiliesSection(
            System.Collections.Generic.IEnumerable<IdentityScanRow> rows, int perIdentity = 2)
        {
            var lines = new System.Collections.Generic.List<string>();
            foreach (IdentityScanRow row in System.Linq.Enumerable.OrderByDescending(rows, r => r.Identity))
            {
                if (row.MixedGroups == 0 || row.MixedFamilies.Count == 0) continue;

                var score = new GroupingScore { MixedFamilies = row.MixedFamilies };
                System.Collections.Generic.List<string> described = score.DescribeMixedFamilies(perIdentity);
                lines.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0,3}%  {1}", row.Identity, described[0]));
                for (int i = 1; i < described.Count; i++) lines.Add("      " + described[i]);
            }
            return lines;
        }
    }
}
