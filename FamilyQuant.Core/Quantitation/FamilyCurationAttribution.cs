using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyQuant
{
    public static partial class FamilyEnvelopeQuant
    {
        /// <summary>
        /// Runs the production peptide-to-curated-family homology mapping once and exposes the complete
        /// many-to-many assignment for other reproducible outputs such as the spectrum evidence table.
        /// It applies the same target/decoy and UserID rules as <see cref="BuildFromCuration"/>.
        /// </summary>
        public static PeptideFamilyAttribution BuildAttributionFromCuration(
            IEnumerable<QuantRow> rows,
            FamilyClusterFile curation,
            Homology.HomologyOptions? options = null,
            bool identifiedBestScoreOnly = IdentifiedBestScoreOnlyDefault)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            return AttributeHits(SearchCuration(rows, curation, options, identifiedBestScoreOnly), curation);
        }

        /// <summary>Whether an IDENTIFIED peptide reaches only the proteins tied at its best score (the
        /// proteins that contain it) rather than every protein past the identity gate. De novo peptides
        /// always keep every qualifying hit: their sequence is a tag, not an established identification.</summary>
        public const bool IdentifiedBestScoreOnlyDefault = true;

        /// <summary>
        /// The expensive half of <see cref="BuildAttributionFromCuration"/>: the homology search of every
        /// distinct peptide of each side against the curation's proteins. It depends on the proteins and
        /// the quant, NOT on the family numbers, so a screen that regroups the same proteins can keep it and
        /// redo only the cheap join (<see cref="AttributeHits"/>) after each regrouping.
        /// </summary>
        public static CurationHomologyHits SearchCuration(IEnumerable<QuantRow> rows, FamilyClusterFile curation,
                                                         Homology.HomologyOptions? options = null,
                                                         bool identifiedBestScoreOnly = IdentifiedBestScoreOnlyDefault)
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            List<QuantRow> rowList = rows.Where(r => r != null).ToList();
            List<FastaItemExtended> items = (curation.MyExtendedFastaItems ?? new List<FastaItemExtended>()).ToList();

            // No cut at the mapper's top three, on either side: a peptide reaching a second family through a
            // fourth protein is shared, and cutting it there called it family-unique. An IDENTIFIED peptide
            // then keeps only the proteins tied at its best score, which are the proteins containing it (the
            // database search established its sequence, so a homolog that merely resembles it is not
            // evidence; on Ana's curation the every-past-gate rule made 218 of 4,720 identified sequences
            // shared through such homologs). A DE NOVO peptide keeps every protein past the identity gate,
            // because its sequence is a tag that may only resemble the database. The mapper's own default,
            // and every other caller of it, are unchanged.
            Homology.HomologyOptions effective = (options ?? new Homology.HomologyOptions()).Clone();
            effective.TopHitsPerPeptide = int.MaxValue;
            Homology.HomologyOptions identifiedOptions = effective.Clone();
            identifiedOptions.BestScoreOnly = identifiedBestScoreOnly;
            var hits = new CurationHomologyHits
            {
                IdentityThreshold = effective.MinIdentity,
                EffectiveOptions = effective,
                IdentifiedOptions = identifiedOptions,
            };
            if (items.Count == 0) return hits;

            Homology.HomologyDatabase database = Homology.HomologyDatabase.Build(items);
            hits.Database = database;

            // Kept COMPACT: per peptide only the indices of the proteins it hit (all a family join reads),
            // never the hit objects with their matched substrings, which with every qualifying hit kept
            // would grow with peptides x homologs on a redundant database and be walked at every regroup.
            var proteinIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            var proteinIds = new List<string>();

            List<(string Key, int[] Proteins)> SearchSide(string side, Homology.HomologyOptions sideOptions)
            {
                List<string> sequences = rowList
                    .Where(r => string.Equals(r.Side, side, StringComparison.Ordinal))
                    .Select(r => DeNovoReconciler.NormalizeSequence(r.Sequence ?? string.Empty))
                    .Where(s => s.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var compact = new List<(string, int[])>();
                if (sequences.Count == 0) return compact;

                foreach (Homology.HomologyResult r in Homology.HomologyMapper.Search(sequences, database, sideOptions))
                {
                    int[] proteins = r.Hits.Select(h => h.ProteinId).Distinct(StringComparer.Ordinal).Select(id =>
                    {
                        if (!proteinIndex.TryGetValue(id, out int i))
                        {
                            proteinIndex[id] = i = proteinIds.Count;
                            proteinIds.Add(id);
                        }
                        return i;
                    }).ToArray();
                    // The same key MapToFamilies gives the assignment.
                    compact.Add((r.IsRejected ? r.Peptide : r.NormalizedPeptide, proteins));
                }
                return compact;
            }

            hits.Identified = SearchSide(NativeQuantFractions.SideIdentified, identifiedOptions);
            hits.DeNovo = SearchSide(NativeQuantFractions.SideDeNovo, effective);
            hits.ProteinIds = proteinIds.ToArray();
            return hits;
        }

        /// <summary>
        /// The cheap half: joins the search hits onto the curation's CURRENT User IDs (target members
        /// only). Same result as <see cref="BuildAttributionFromCuration"/> for the same proteins.
        /// </summary>
        public static PeptideFamilyAttribution AttributeHits(CurationHomologyHits hits, FamilyClusterFile curation)
        {
            if (hits == null) throw new ArgumentNullException(nameof(hits));
            if (curation == null) throw new ArgumentNullException(nameof(curation));

            Homology.HomologyOptions effective = hits.EffectiveOptions;
            var attribution = new PeptideFamilyAttribution
            {
                IdentityThreshold = hits.IdentityThreshold,
                EffectiveOptions = effective,
                IdentifiedOptions = hits.IdentifiedOptions,
            };
            List<FastaItemExtended> items = curation.MyExtendedFastaItems ?? new List<FastaItemExtended>();
            if (items.Count == 0 || hits.Database == null) return attribution;

            string decoyLabel = effective.DecoyLabel ?? string.Empty;
            List<FastaItemExtended> targets = decoyLabel.Length == 0
                ? items
                : items.Where(i => i != null
                                   && !(i.SequenceIdentifier ?? string.Empty)
                                       .StartsWith(decoyLabel, StringComparison.Ordinal))
                       .ToList();
            attribution.Database = hits.Database;
            attribution.TargetItems = targets;

            // The join HomologyMapper.MapToFamilies performs, over the compact hits: target members only,
            // the first occurrence of an identifier wins, family ids distinct and ascending.
            var byIdentifier = new Dictionary<string, FastaItemExtended>(StringComparer.Ordinal);
            foreach (FastaItemExtended item in targets)
            {
                if (item == null || string.IsNullOrEmpty(item.SequenceIdentifier)) continue;
                if (!byIdentifier.ContainsKey(item.SequenceIdentifier)) byIdentifier.Add(item.SequenceIdentifier, item);
            }

            void MapSide(List<(string Key, int[] Proteins)> sideHits, Dictionary<string, IReadOnlyList<int>> map)
            {
                foreach ((string key, int[] proteins) in sideHits)
                {
                    var families = new SortedSet<int>();
                    foreach (int p in proteins)
                        if (byIdentifier.TryGetValue(hits.ProteinIds[p], out FastaItemExtended? item)) families.Add(item.UserID);
                    map[key] = families.ToArray();
                }
            }

            MapSide(hits.Identified, attribution.IdentifiedBySequence);
            MapSide(hits.DeNovo, attribution.DeNovoBySequence);

            // The annotation, else the most frequent protein name (FamilyNaming), not the first member's.
            foreach (KeyValuePair<int, string> name in FamilyNaming.Names(targets))
                attribution.FamilyDescriptions[name.Key] = name.Value;
            return attribution;
        }

    }
}
