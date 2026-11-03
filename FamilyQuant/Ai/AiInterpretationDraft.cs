using FamilyQuant.Report;
using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Ai;

public sealed record AiInterpretationDraft(string Text, AiInterpretationProvenance Provenance)
{
    public static AiInterpretationDraft Create(string input, string output, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);
        string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
        string label = "AI draft (model " + AiTransport.Model + ", "
            + now.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "); review before use.";
        return new(label + "\n" + output, new(AiTransport.Model, fingerprint));
    }

    public ReportDetails Apply(ReportDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return details with { WrittenInterpretation = Text, AiInterpretation = Provenance };
    }
}
