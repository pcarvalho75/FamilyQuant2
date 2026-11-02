using System;

namespace FamilyQuant;

internal static class CurationFeedback
{
    private static string Reason(Exception error) => error.Message.TrimEnd('.');

    internal static string RecalculationFailure(string completed, Exception error) =>
        completed + "; the share column still holds the previous numbers and must not be read (reason: " + Reason(error) + "). "
        + "Press Recalculate in Quantitation; if it fails again, try a different PatternLab share of denominator or reload the project with Load PatternLab project under PatternLab (optional), then send this message and the project file to the FamilyQuant maintainer if the problem persists.";

    internal static string Failure(string operation, Exception error) => operation switch
    {
        "Recalculate family shares" => RecalculationFailure("The family shares could not be recalculated", error),
        "Load PatternLab quantitation" => "The PatternLab project could not be opened; the table still shows what it showed before (reason: " + Reason(error)
            + "). Check the file and close any other program holding it, then load it again.",
        "Export visible protein rows" or "Export family composition" => "Nothing was written; "
            + (operation == "Export family composition" ? "the family composition" : "the protein table") + " could not be exported (reason: " + Reason(error)
            + "). Close the file in Excel and check that you can write to the folder, then export again.",
        "Align selected proteins" => "Alignment could not finish (reason: " + Reason(error)
            + "). Try fewer sequences; antivirus software may block the bundled USEARCH program, and FAMILYQUANT_USEARCH can point to a copy of usearch.exe you control.",
        _ => operation + " could not finish: " + error.Message
    };

    internal static string PatternLabLoaded(QuantitationProfile profile, NormalizationMode mode)
    {
        string message = "PatternLab project loaded correctly; nothing is required; the table currently shows \"" + mode.DisplayName()
            + "\"; " + profile.DenominatorSentence().TrimEnd('.').Replace(". ", "; ") + ".";
        if (!profile.CanReportTotalChromatogram)
            message += " One of the four reporting modes, \"" + NormalizationMode.TotalChromatogram.DisplayName()
                + "\", is greyed out for this project; "
                + (profile.Evidence == DenominatorEvidence.Recorded
                    ? "hover the mode for its per-run totals and quantity requirements"
                    : "this legacy export does not record its divisor; re-export it from XQuant to record the divisor and every run's totals and make that mode available") + ".";
        return message;
    }
}
