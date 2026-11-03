using System;
using System.Collections.Generic;

namespace FamilyQuant.Report.Captions;

public enum ReportFigureTopic
{
    Composition, Subclasses, Diversity, Ring, Coverage, IonCurrent, Recovery, XicMass,
    IsotopeCount, DeNovoScores, DeNovoSources, AcceptedScores, FamilyWorldRates, DatabaseGap, IdentityScan
}

public enum ReportTableTopic
{
    Composition, PerRun, ProteinCounts, Subclasses, DomainsAndProducts, Coverage, Sensitivity,
    MeanAndPooled, Others, Quality, DatabaseGap, Inputs, PanelReference
}

/// <summary>General reading guidance for report producers. Facts and entity/denominator labels still come from their source data.</summary>
public static class ReportCaptionGuidance
{
    public static FigureCaptionInput Figure(ReportFigureTopic topic, IReadOnlyList<CaptionNumber> numbers,
        IReadOnlyList<string>? notes = null)
    {
        var (what, how) = topic switch
        {
            ReportFigureTopic.Composition => ("Mean family shares of credited signal", "Read bar length as mean per-run share and whiskers as one sample SD across available runs. SD describes spread across runs, not a confidence interval"),
            ReportFigureTopic.Subclasses => ("Subclass contributions, all runs pooled", "Stacked parts add to each family on the labelled denominator. Grey denotes unresolved or unavailable parts as labelled"),
            ReportFigureTopic.Diversity => ("Family abundance against database-protein diversity", "Horizontal position gives mean per-run family share; vertical position gives proteins needed to explain the peptides (greedy estimate). Abundance and protein diversity are separate quantities"),
            ReportFigureTopic.Ring => ("Family and subclass shares, all runs pooled", "Inner arcs show families; outer arcs show their subclass parts on the same pooled denominator. Others takes the report's single mean-of-runs membership list"),
            ReportFigureTopic.Coverage => ("Protein domains, features and peptide placements", "Read canonical residue positions from left to right. Exact and aligned peptide placements retain their match kind; gaps and uncertain feature intervals receive no fill"),
            ReportFigureTopic.IonCurrent => ("Credited signal by peptide mass", "Read each mass-bin share against the denominator named by its series. Credited-signal and total-ion-current shares are different readings"),
            ReportFigureTopic.Recovery => ("Recovery relative to recorded total ion current", "The M+0 sensitivity, primary calibrated estimate and observed-envelope diagnostic are separate readings. Their spread is not a confidence interval, and independent detector levels are not a biological partition"),
            ReportFigureTopic.XicMass => ("Extracted isotope-envelope signal against peptide mass", "Mass is horizontal and envelope area uses the labelled logarithmic vertical scale. Colors distinguish the sequence sources"),
            ReportFigureTopic.IsotopeCount => ("Credited isotopes per peptide", "The horizontal number is credited isotope count; bar height counts peptide representatives. Stacked colors distinguish the displayed mass bands"),
            ReportFigureTopic.DeNovoScores => ("De novo score distributions", "Use each source's labelled native score and vertical scale. Native score scales differ between sequence callers, so read source-specific cutoffs on their own scales"),
            ReportFigureTopic.DeNovoSources => ("De novo source contributions and overlap", "Use the entity declared by the figure, its legend and overlap labels. Shared regions have membership in multiple displayed sources; exclusive regions belong to one"),
            ReportFigureTopic.AcceptedScores => ("Scores of accepted sequence calls", "Read accepted calls with their source-native score scale and stated acceptance cutoff. Axis and legend labels identify the entity being compared"),
            ReportFigureTopic.FamilyWorldRates => ("Stored family-world quality rates", "Read each rate with its supporting count, reference protocol and declared denominator. A rate retains the meaning assigned by its assessment"),
            ReportFigureTopic.DatabaseGap => ("Recorded database-gap composition", "Legend labels identify the stored evidence buckets and their denominator. Broader-reference matches remain separate from assignments supported by the starting database"),
            ReportFigureTopic.IdentityScan => ("Family grouping across identity thresholds", "Read identity on the horizontal axis and each curve on its labelled count or mixing-rate scale. Target/decoy group mixing describes grouping and is not a statistical false-discovery rate"),
            _ => throw new ArgumentOutOfRangeException(nameof(topic))
        };
        return new FigureCaptionInput(what, how, numbers, notes);
    }

    public static TableCaptionInput Table(ReportTableTopic topic, int labelColumn = 0,
        IReadOnlyList<int>? summaryColumns = null)
    {
        var (what, how) = topic switch
        {
            ReportTableTopic.Composition => ("Family composition from mean per-run shares", "Each available run receives equal weight in the mean. SD and CV summarize run-to-run spread; protein counts describe a separate quantity"),
            ReportTableTopic.PerRun => ("Family shares in individual acquisition runs", "Read across a family to compare runs. Each run uses its own declared signal denominator; unavailable shares remain unavailable"),
            ReportTableTopic.ProteinCounts => ("Proteins needed to explain the peptides (greedy estimate)", "The greedy cover approximates a minimum database-protein set. Proteins with a peptide of their own are listed separately; these counts do not count genes or proteoforms"),
            ReportTableTopic.Subclasses => ("Subclass signal within families, all runs pooled", "Read each part against the denominator in its column heading. Shared subclass evidence remains explicitly unresolved instead of being forced into a class"),
            ReportTableTopic.DomainsAndProducts => ("Signal assigned to domains or named products, all runs pooled", "Parts follow the stored allocation method and labelled denominator. Outside or unresolved signal stays visible; overlapping annotations do not justify double counting"),
            ReportTableTopic.Coverage => ("Protein-coordinate evidence and localized peptide blocks", "Positions are one-based and inclusive on the verified sequence. Separate intervals retain alignment or annotation gaps; uncertain boundaries remain marked as uncertain"),
            ReportTableTopic.Sensitivity => ("Stored sensitivity variants", "Compare matching families across the recorded policies and source-native score cutoffs. Only computed, stored variants are shown; missing variants are not substituted"),
            ReportTableTopic.MeanAndPooled => ("Mean per-run and pooled family shares", "The mean weights available runs equally; the pooled share weights signal across runs. Read the signed difference in the direction and units stated by its heading"),
            ReportTableTopic.Others => ("Members of the report's Others group", "Membership is decided once from mean per-run shares and the stored threshold. Pooled charts reuse that membership while retaining their pooled angles and values"),
            ReportTableTopic.Quality => ("Stored quality measurements", "Read each rate with its supporting count, assessment scope and declared denominator. Missing quality measurements remain unavailable"),
            ReportTableTopic.DatabaseGap => ("Stored database-gap results", "Read the evidence buckets and their signal denominator together. Reference-supported proposals and starting-database assignments remain distinct"),
            ReportTableTopic.Inputs => ("Recorded analysis inputs and provenance", "Read the saved input identifiers, hashes and settings across each row"),
            ReportTableTopic.PanelReference => ("User-supplied family reference fields beside observed results", "Panel decisions, literature text and notes are printed as supplied; measured columns come from the saved project results"),
            _ => throw new ArgumentOutOfRangeException(nameof(topic))
        };
        return new TableCaptionInput(what, how, labelColumn,
            summaryColumns ?? (topic is ReportTableTopic.Inputs or ReportTableTopic.PanelReference ? Array.Empty<int>() : null));
    }
}
