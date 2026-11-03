using System.Linq;

namespace FamilyQuant.Report;

/// <summary>Display-only subset for legacy charts. Native denominators and row values are preserved.</summary>
internal static class ReportFigureMembership
{
    internal static FamilyEnvelopeQuantResult Visible(FamilyEnvelopeQuantResult source, FamilyReportThresholdResult? grouping)
    {
        if (grouping == null) return source;
        var hidden = ReportWordMembership.HiddenIds(grouping);
        var census = source.SourceSequenceSummary;
        var result = new FamilyEnvelopeQuantResult
        {
            IdentityThreshold = source.IdentityThreshold,
            MeasuredIdentifiedEnvelope = source.MeasuredIdentifiedEnvelope,
            MeasuredDeNovoEnvelope = source.MeasuredDeNovoEnvelope,
            MeasuredDeNovoEnvelopeReferenceCalibrated = source.MeasuredDeNovoEnvelopeReferenceCalibrated,
            UnattributedIdentifiedEnvelope = source.UnattributedIdentifiedEnvelope,
            UnattributedDeNovoEnvelope = source.UnattributedDeNovoEnvelope,
            UnattributedDeNovoEnvelopeReferenceCalibrated = source.UnattributedDeNovoEnvelopeReferenceCalibrated,
            UnattributedIdentifiedPeptides = source.UnattributedIdentifiedPeptides,
            UnattributedDeNovoPeptides = source.UnattributedDeNovoPeptides,
            SharedIdentifiedEnvelope = source.SharedIdentifiedEnvelope,
            SharedDeNovoEnvelope = source.SharedDeNovoEnvelope,
            SharedDeNovoEnvelopeReferenceCalibrated = source.SharedDeNovoEnvelopeReferenceCalibrated,
            SharedIdentifiedPeptides = source.SharedIdentifiedPeptides,
            SharedDeNovoPeptides = source.SharedDeNovoPeptides,
            SourceSequenceSummary = new()
            {
                MappingMinimumIdentity = census.MappingMinimumIdentity,
                SearchInputDistinctSequences = census.SearchInputDistinctSequences,
                PeaksDeNovoOnlyInputDistinctSequences = census.PeaksDeNovoOnlyInputDistinctSequences,
                DeepNovoInputDistinctSequences = census.DeepNovoInputDistinctSequences,
                UnknownSourceInputDistinctSequences = census.UnknownSourceInputDistinctSequences,
            },
        };
        result.Families.AddRange(source.Families.Where(f => !hidden.Contains(f.FamilyUserId)));
        result.SourceSequenceSummary.Families.AddRange(census.Families.Where(f => !hidden.Contains(f.FamilyUserId)));
        return result;
    }
}
