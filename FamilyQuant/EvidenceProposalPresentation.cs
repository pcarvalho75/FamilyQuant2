using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyQuant
{
    /// <summary>Copies supplied scientific facts into display rows without classifying or applying them.</summary>
    public static class EvidenceProposalPresentation
    {
        public const string QueryCoverageRule =
            "At least 70 percent of the query's residues occur in the alignment; inserted gap columns do not count.";
        public static string QueryCoverageSummary => "The comparison must cover at least "
            + NearestReviewed.DefaultMinQueryAlignedPercent.ToString("0.##", CultureInfo.CurrentCulture) + "% of this protein's length.";
        public static EvidenceReviewInput Present(EvidenceProposal proposal, FamilyClusterFile file)
        {
            var input = Present(proposal, file.MyExtendedFastaItems, file.LockedUserIds);
            return input with { RuleSummary = input.RuleSummary + "\nStored motifs:\n"
                + string.Join("\n", (file.Motifs ?? new List<MotifPattern>()).Select(m => m.Name + ": " + m.Pattern))
                + (proposal.PanelContext == null ? "" : "\n\n" + PanelEvidenceHelp.Build()),
                PanelNames = file.FamilyPanel?.Families.ToDictionary(f => f.Key, f => f.DisplayName, StringComparer.Ordinal) };
        }

        public static EvidenceReviewInput Present(EvidenceProposal proposal, IReadOnlyList<FastaItemExtended> proteins,
            ISet<int>? lockedFamilyIds = null)
        {
            ArgumentNullException.ThrowIfNull(proposal);
            ArgumentNullException.ThrowIfNull(proteins);
            if (!string.Equals(proposal.SourceFingerprint, EvidenceClassifier.Fingerprint(proteins), StringComparison.Ordinal))
                throw new InvalidOperationException("The file changed after this proposal was made. Build a new proposal before reviewing it.");
            var rows = proposal.Proteins.Select(p =>
            {
                if (p.ProteinIndex < 0 || p.ProteinIndex >= proteins.Count
                    || proteins[p.ProteinIndex].SequenceIdentifier != p.Identifier)
                    throw new ArgumentException("The proposal does not identify the supplied protein list.", nameof(proposal));
                var source = proteins[p.ProteinIndex];
                return new ReviewProteinInput(p.ProteinIndex, p.Identifier, p.PreviousFamilyId, p.Family,
                    TierName(p.Tier), p.Reason, p.HasScopeAnnotation, NeighbourText(p.Neighbour),
                    p.Flags.Select(f => f.Code + ": " + f.Message).ToArray(), p.SuggestedSelected,
                    source.Description, source.Domains, lockedFamilyIds?.Contains(source.UserID) == true,
                    source.Evidence?.Reviewed, source.Sequence?.Length ?? 0, CoherenceCheck.Cysteines(source.Sequence ?? ""),
                    source.Evidence?.Pfam?.Count > 0);
            }).ToArray();
            return new EvidenceReviewInput(proposal.SourceFingerprint, rows,
                proposal.Families.Select(f => new ReviewFamilyInput(f.Family, f.SuggestedName, f.Proteins, f.ReviewedProteins)).ToArray(),
                proposal.Markers.Select(m => new ReviewMarkerInput(m.Accession, m.Name, m.Family,
                    m.FamilyCarriers, m.AllCarriers, m.LabelledCarriers, m.VoteFraction)).ToArray(),
                Architectures: proposal.PanelContext != null ? Array.Empty<ReviewArchitectureInput>()
                : EvidenceArchitecture.Suggest(proteins, proposal).Select(a => new ReviewArchitectureInput(a.Family, a.CanSplit,
                    a.Groups.Select(g => new ReviewArchitectureGroupInput(g.Key, g.SuggestedName, g.DominantDomainAccession,
                        g.Domains, g.ProteinIndices, g.ReviewedProteins, g.InferredProteinIndices)).ToArray(),
                    a.Undetermined.Select(p => new ReviewArchitectureUndeterminedInput(p.ProteinIndex, p.Reason)).ToArray())).ToArray(),
                LeftOut: LeftOutList.Build(proteins, proposal).Select(p => new ReviewLeftOutInput(p.Family, p.Accession,
                    p.Identifier, p.Description, p.Length, p.Reviewed, p.Fragment, p.MarkerAccession, p.MarkerName, p.AssignedFamily, p.Why)).ToArray(), RuleSummary:
                    "Scope terms: " + string.Join(", ", proposal.Options.ScopeTerms)
                    + "\nAnnotated identity threshold (%): " + proposal.Options.AnnotatedIdentityPercent.ToString(CultureInfo.CurrentCulture)
                    + "\nUnannotated identity threshold (%): " + proposal.Options.UnannotatedIdentityPercent.ToString(CultureInfo.CurrentCulture)
                    + "\nMinimum query aligned (%): " + proposal.Options.MinQueryAlignedPercent.ToString(CultureInfo.CurrentCulture)
                    + (proposal.PanelContext == null ? "\nMarker vote fraction: " + proposal.Options.MarkerVoteFraction.ToString(CultureInfo.CurrentCulture)
                        : "\nPanel links require one explicit choice when the evidence is ambiguous."),
                PanelLinks: proposal.PanelContext?.Links);
        }

        public static string CountMotif(EvidenceProposal proposal, IReadOnlyList<FastaItemExtended> proteins, string pattern)
        {
            if (proposal.SourceFingerprint != EvidenceClassifier.Fingerprint(proteins))
                throw new InvalidOperationException("The file changed. Update the proposal before checking its motifs.");
            var result = MotifCheck.Count(new MotifPattern { Name = pattern, Pattern = pattern },
                proposal.Proteins.Where(p => p.Family.Length > 0).Select(p => (p.Family, proteins[p.ProteinIndex])));
            return "Pattern: " + result.Motif.Pattern + "\nFamily\tMatching proteins\tProteins counted\n" + string.Join("\n",
                result.Families.Select(f => f.Family + "\t" + f.Carriers.ToString(CultureInfo.CurrentCulture)
                    + "\t" + f.Complete.ToString(CultureInfo.CurrentCulture)));
        }

        private static string TierName(EvidenceTier tier) => tier switch
        {
            EvidenceTier.UniProtFamily => "UniProt family",
            EvidenceTier.ReviewedNeighbour => "Reviewed neighbour",
            EvidenceTier.LearnedMarker => "Learned marker",
            EvidenceTier.UnannotatedNeighbour => "Unannotated neighbour",
            _ => tier.ToString()
        };

        private static string NeighbourText(NearestNeighbour? n)
        {
            if (n == null) return "";
            string coverage = n.QueryCoveragePercent is double percent
                ? percent.ToString("0.##", CultureInfo.CurrentCulture) + "% of query residues ("
                    + n.QueryAlignedResidues!.Value.ToString(CultureInfo.CurrentCulture) + "/"
                    + n.QueryLength.ToString(CultureInfo.CurrentCulture) + ")"
                : "query coverage not recorded; aligned query residues not recorded";
            return n.Accession + "; " + n.IdentityPercent.ToString("0.##", CultureInfo.CurrentCulture) + "% identity; "
                + coverage + "; alignment columns " + n.AlignmentLength.ToString(CultureInfo.CurrentCulture)
                + "; query length " + n.QueryLength.ToString(CultureInfo.CurrentCulture)
                + "; reference length " + n.ReferenceLength.ToString(CultureInfo.CurrentCulture)
                + "; family " + n.Family + (n.TiedFamilies.Count == 0 ? "" : "; tied families: " + string.Join(", ", n.TiedFamilies));
        }
    }
}
