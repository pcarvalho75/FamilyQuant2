using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant;

public sealed record DatabaseGapGlobalRow(string Label, double Envelope);

public sealed class DatabaseGapDisplayRow
{
    public FamilyWorldCompositionRow Data { get; }
    public string FamilyLabel => Label(Data);
    public static string Label(FamilyWorldCompositionRow row) => row.Kind == FamilyWorldCompositionRow.CuratedKind
        && row.CuratedUserIds.Count > 0 ? row.Family + " (group " + string.Join(", ", row.CuratedUserIds) + ")" : row.Family;
    public string Kind => Data.Kind == FamilyWorldCompositionRow.CuratedKind ? "Curated group" : "UniProt family";
    public string DecoyRate => Data.WorldDecoyRate is double value ? value.ToString("P6", CultureInfo.CurrentCulture) : "Not recorded";
    public string RightRate { get; }
    public DatabaseGapDisplayRow(FamilyWorldCompositionRow data, int minimumKnownCredits)
    {
        Data = data;
        string counts = " (" + data.WorldKnownCreditedTrue.ToString(CultureInfo.CurrentCulture) + "/"
            + data.WorldKnownCredited.ToString(CultureInfo.CurrentCulture) + ")";
        RightRate = data.WorldKnownCredited >= minimumKnownCredits && data.WorldRightRate is double rate
            ? rate.ToString("P2", CultureInfo.CurrentCulture) + counts
            : (data.WorldEvidence.Length > 0 ? data.WorldEvidence : "No family-world evaluation") + counts;
    }
}

/// <summary>Presents the stored Core result. It never attributes peptides or recalculates a family amount.</summary>
public sealed class DatabaseGapViewModel
{
    public FamilyWorldSummary? Summary { get; }
    public bool HasData => Summary != null && Summary.CompositionRules.Length > 0;
    public string EmptyMessage => Summary == null
        ? "No family-world result is stored in this project. Open a project with that result to view Database gap."
        : "This stored family-world result predates the three-part composition table. Its missing parts are not shown as zero.";
    public IReadOnlyList<DatabaseGapDisplayRow> Rows { get; }
    public IReadOnlyList<FamilyWorldCompositionRow> ChartRows { get; }
    public IReadOnlyList<DatabaseGapGlobalRow> GlobalRows { get; }
    public string Rules => !HasData ? "" : "Minimum tag length: " + Summary!.MinimumTagLength.ToString(CultureInfo.CurrentCulture)
        + " residues. Identity gate: " + Summary.MinIdentity.ToString("P2", CultureInfo.CurrentCulture)
        + ". Family-world credits go only to families in the stored scope.";
    public string SecondPassRules => Summary?.Rules ?? "";
    public string Scope => !HasData ? "" : "Stored scope: " + string.Join("; ", Summary!.ScopeFamilies);
    public string Basis => !HasData ? "" : "Signal is envelope area: observed for identified peptides, reference-calibrated for de novo. "
        + "Measured total: " + Summary!.MeasuredTotalEnvelope.ToString("G6", CultureInfo.CurrentCulture)
        + ". Shared signal and the global unexplained bucket are separate from every family.";
    public string FirstPassRules => Summary?.CompositionRules ?? "";
    public string ValidationRule => !HasData ? "" : "Known-answer right rates require at least "
        + Summary!.MinimumKnownCredits.ToString(CultureInfo.CurrentCulture) + " known answers credited to the family; true/credited counts stay beside each rate.";
    public string Provenance => !HasData ? "" : "World release: " + Recorded(Summary!.WorldRelease)
        + "; result fingerprint: " + Recorded(Summary.WorldFingerprint) + ".";
    public bool CanExportFasta => candidates.Count > 0 && FastaProblem.Length == 0;
    public string FastaProblem { get; } = "No matching stored family world is available.";
    public string FastaState => CanExportFasta ? candidates.Count.ToString(CultureInfo.CurrentCulture)
        + " stored world proteins hit by credited tags. FASTA contains suggested additions for you to add and search again in PatternLab."
        : "Suggested additions unavailable: " + FastaProblem;
    private readonly List<FamilyWorldEntry> candidates = new();
    private readonly string worldHash = "";

    public DatabaseGapViewModel(FamilyWorldSummary? summary, FamilyClusterFile? file = null)
    {
        Summary = summary;
        ChartRows = !HasData ? Array.Empty<FamilyWorldCompositionRow>() : summary!.Composition
            .OrderByDescending(r => r.WorldDeNovoEnvelope).ThenBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.Family, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Family, StringComparer.Ordinal)
            .ThenBy(r => r.CuratedUserIds.FirstOrDefault()).ToArray();
        Rows = ChartRows.Select(r => new DatabaseGapDisplayRow(r, summary!.MinimumKnownCredits)).ToArray();
        GlobalRows = !HasData ? Array.Empty<DatabaseGapGlobalRow>() : new[] {
            new DatabaseGapGlobalRow("Global unexplained de novo", summary!.UnexplainedEnvelope),
            new DatabaseGapGlobalRow("Identified, shared between families", summary.SharedIdentifiedEnvelope),
            new DatabaseGapGlobalRow("De novo in database, shared between families", summary.SharedDatabaseDeNovoEnvelope),
            new DatabaseGapGlobalRow("De novo in world, shared between families", summary.SharedEnvelope),
            new DatabaseGapGlobalRow("Identified, in no family", summary.UnattributedIdentifiedEnvelope) };
        if (!HasData || file == null) return;
        try
        {
            FamilyWorldSnapshot stored = FamilyWorldFetcher.Stored(file);
            // Use the same verified snapshot identity as the production second pass. Exact accession
            // lookup below still refuses missing records; this does not infer aliases or new attribution.
            FamilyWorld world = FamilyWorld.FromSnapshot(stored);
            if (!string.Equals(world.Fingerprint, summary!.WorldFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The result fingerprint does not match the stored world. No protein was substituted.");
            if (stored.Release != summary.WorldRelease || !stored.Families.OrderBy(f => f, StringComparer.Ordinal).SequenceEqual(
                summary.ScopeFamilies.OrderBy(f => f, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException("The stored world release or scope differs from the result.");
            var byAccession = stored.Entries.ToDictionary(entry => entry.Accession, StringComparer.Ordinal);
            foreach (string id in summary.Families.SelectMany(row => row.WorldProteins).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!byAccession.TryGetValue(id, out var entry) || id.IndexOfAny(new[] { '\r', '\n' }) >= 0
                    || entry.Sequence.Length == 0 || entry.Sequence.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new InvalidDataException("A recorded hit has no exact usable sequence in the stored world. No partial FASTA is available.");
                candidates.Add(entry);
            }
            if (candidates.Count == 0) throw new InvalidDataException("No world proteins were recorded for credited tags.");
            worldHash = stored.ContentSha256;
            FastaProblem = "";
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        { candidates.Clear(); FastaProblem = ex.Message; }
    }

    public string BuildCsv() => HasData ? Summary!.BuildCompositionCsv()
        : throw new InvalidOperationException(EmptyMessage);

    public string BuildSuggestedFasta()
    {
        if (!CanExportFasta) throw new InvalidOperationException(FastaProblem);
        var text = new StringBuilder();
        foreach (var entry in candidates)
        {
            text.Append('>').Append(entry.Accession).Append(" FamilyQuant_suggested_addition world_sha256=").Append(worldHash).Append('\n');
            for (int offset = 0; offset < entry.Sequence.Length; offset += 60)
                text.Append(entry.Sequence, offset, Math.Min(60, entry.Sequence.Length - offset)).Append('\n');
        }
        return text.ToString();
    }

    private static string Recorded(string value) => value.Length > 0 ? value : "not recorded";
}

public static class DatabaseGapHelp
{
    public const string Text = "Database gap ranks the stored family rows by de novo signal found only in the family world. "
        + "Each bar has three parts: identified, de novo placed by your database, and de novo placed only by the family world. "
        + "Only peptides credited to one family enter that family's own bar. Shared signal stays in separate global rows. "
        + "The global unexplained de novo bucket belongs to no family. Amounts use the measured envelope basis, not RAW TIC. "
        + "The minimum tag length and the in-scope family rule stored with the result are shown beside it and in the CSV. "
        + "The decoy rate is the reversed-copy envelope divided by the envelope credited to that family, including shared credits. "
        + "The known-answer right rate always shows its true/credited counts. "
        + "A credit counts as right for a family when that family is among the known answers, even if the peptide was also credited to another family. "
        + "Below the stored minimum known-credit count, the evidence message replaces that rate. "
        + "The CSV comes directly from the same stored table. Suggested additions exports exact sequences of the recorded hit proteins "
        + "from the verified matching stored world. Add any candidates you accept to your database and search again in PatternLab. "
        + "FamilyQuant never changes your database. Missing or mismatched world evidence refuses the FASTA, with no partial export.";
}
