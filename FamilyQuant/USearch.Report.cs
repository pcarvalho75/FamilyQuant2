using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace FamilyQuant;

public static class BuildReportFormatter
{
    public static string Format(List<IdentityScanRow> rows)
        {
            var sb = new StringBuilder();

            sb.AppendLine("Identity   Families   With targets   Mixed   Score   Score (of target families)");
            sb.AppendLine("---------------------------------------------------------------------------------");

            foreach (var row in rows)
            {
                sb.AppendLine(string.Format(
                    "{0,7}%   {1,8}   {2,12}   {3,5}   {4,5:P2}   {5,24:P2}",
                    row.Identity, row.Groups, row.TargetGroups, row.MixedGroups, row.Score, row.TargetRelativeScore));
            }

            sb.AppendLine();

            // Groups - TargetGroups is the number of decoy-only families: if that is 0 everywhere and
            // nothing ever mixed, the database simply has no decoys and the score column means nothing.
            bool hasDecoys = rows.Any(r => r.Groups > r.TargetGroups || r.MixedGroups > 0);

            if (!hasDecoys)
            {
                sb.AppendLine("This database contains no decoy sequences, so the two score columns are 0 by");
                sb.AppendLine("construction and say nothing about the clustering. Choose the threshold from the");
                sb.AppendLine("family counts and from the biology, or re-run on a target-decoy (.T-R) database.");
            }
            else
            {
                var lowestClean = rows.Where(r => r.MixedGroups == 0).OrderBy(r => r.Identity).FirstOrDefault();

                sb.AppendLine(lowestClean != null
                    ? $"Lowest identity with no target/decoy mixing: {lowestClean.Identity}% ({lowestClean.Groups} families)."
                    : "Every identity in this range already mixes target and decoy sequences.");

                // WHICH families mix, not only how many: one short peptide matching its own reversed
                // decoy can hold the floor up on its own, and only its name shows that.
                List<string> mixed = IdentityScanRow.MixedFamiliesSection(rows);
                if (mixed.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("Families that mix target and decoy (smallest first)");
                    sb.AppendLine("---------------------------------------------------");
                    foreach (string line in mixed) sb.AppendLine(line);
                }
            }

            sb.AppendLine();
            sb.AppendLine("How to read this table");
            sb.AppendLine("----------------------");
            sb.AppendLine("'Mixed' counts families holding both target and decoy sequences. A reversed database has");
            sb.AppendLine("the same internal redundancy as the target one, so decoy families collapse at the same rate");
            sb.AppendLine("as target families and grouping two decoys is not an error. The score therefore marks only");
            sb.AppendLine("where alignments stop being better than noise - it CANNOT see two unrelated target families");
            sb.AppendLine("being fused. Use the identity where mixing first appears as a floor, then choose a threshold");
            sb.AppendLine("above it from the family counts and from what the biology of your proteins supports.");
            sb.AppendLine();
            sb.AppendLine("What to do with this table");
            sb.AppendLine("--------------------------");
            sb.AppendLine("Nothing here has been saved. Pick an identity, type it into the Identity box, and press");
            sb.AppendLine("\"Build families\" to build the Family Cluster File at that value.");

            return sb.ToString();
        }
}
