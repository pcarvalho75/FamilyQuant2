using System.Globalization;

namespace FamilyQuant.Evidence;

public static class PanelEvidenceHelp
{
    public static string Build()
    {
        var options = new EvidenceClassificationOptions();
        return "PANEL FAMILIES BY EVIDENCE\n\n"
            + EvidenceScopeHelp.Build() + "\n"
            + EvidenceOutsideScopeHelp.Build() + "\n"
            + EvidenceReviewTerms.Text + "\n"
            + "The panel defines user keys, names, decisions and domain lists. Family names in the program do not decide membership.\n\n"
            + "Reviewed entries in the user scope connect source family annotations and observed domain combinations to panel families. "
            + "The panel list with the most distinct matching domains supplies a candidate; accompanying domains distinguish a shared domain. "
            + "Equal support is ambiguous. Every ambiguous link is listed with its candidates and supporting reviewed accessions. "
            + "Choose a family or leave that context outside the panel, then update the proposal. Choices are stored in the project and recipe.\n\n"
            + "In scope with a source family: use its evidence-derived or confirmed panel link. Several source memberships may join one panel family only when all their links agree. "
            + "Otherwise use the nearest reviewed entry in scope at " + options.AnnotatedIdentityPercent.ToString("0.##", CultureInfo.InvariantCulture)
            + "% identity with a scope annotation, or " + options.UnannotatedIdentityPercent.ToString("0.##", CultureInfo.InvariantCulture)
            + "% without one. " + NearestReviewed.CoverageRuleText(options.MinQueryAlignedPercent) + " "
            + "Self entries and isoforms cannot supply their own labels. Conflicting equally close labels are refused. "
            + "Without a scope annotation, compatible observed domains are also required, with no conflicting domain evidence.\n\n"
            + "A protein without qualifying evidence stays outside panel families. There is no domain-only fallback. "
            + "Excluded rows stay outside; merged rows follow the user's target key. Quantify and Report under others remain distinct panel decisions. "
            + "Review proposals before applying. One locked, undoable transaction stores grouping and link choices; the saved recipe reproduces them offline.";
    }
}
