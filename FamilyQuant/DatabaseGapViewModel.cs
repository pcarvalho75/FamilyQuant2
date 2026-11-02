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

public sealed class DatabaseGapLengthRow
{
    public int MinimumTagLength { get; }
    public bool HasTable { get; }
    public FamilyWorldLengthEstimate? Data { get; }
    public string LandedShare => Data == null ? "Not stored" : Data.LandedFraction.ToString("P2", CultureInfo.CurrentCulture);
    public string DecoyRate => Data == null ? "Not stored" : Data.DecoyRate?.ToString("P2", CultureInfo.CurrentCulture) ?? "Not recorded";
    public string KnownAnswerRight => Data == null ? "Not stored" : (Data.RightRate?.ToString("P2", CultureInfo.CurrentCulture) ?? "Not recorded")
        + " (" + Data.KnownRight.ToString("N0", CultureInfo.CurrentCulture) + "/" + Data.KnownLanded.ToString("N0", CultureInfo.CurrentCulture) + ")";
    public string Availability => HasTable ? "Stored table" : "Not computed";
    public DatabaseGapLengthRow(int length, FamilyWorldLengthEstimate? estimate, bool hasTable)
    { MinimumTagLength = length; Data = estimate; HasTable = hasTable; }
}

/// <summary>Presents only stored Core tables and estimates; choosing a length never runs attribution.</summary>
public sealed class DatabaseGapViewModel : ScreenViewModel
{
    private readonly FamilyWorldSummary? storedResult;
    private readonly Action<int>? storeChoice;
    private readonly FamilyWorldSnapshot? storedWorld;
    private readonly string verifiedWorldFingerprint = "";
    private readonly string worldProblem = "No matching stored family world is available.";
    private int minimumTagLength;
    public FamilyWorldSummary? Summary { get; private set; }
    public bool HasStoredResult => storedResult != null;
    public bool HasData => Summary != null && Summary.CompositionRules.Length > 0;
    public string EmptyMessage => storedResult == null
        ? "No family-world result is stored in this project. Open a project with that result to view Database gap."
        : storedResult.Lengths == null && storedResult.CompositionRules.Length == 0
            ? "This stored family-world result predates the three-part composition table. Its missing parts are not shown as zero."
        : Summary == null ? "No stored table was computed for minimum tag length " + MinimumTagLength.ToString(CultureInfo.CurrentCulture)
            + ". Choose a computed length. This screen does not compute a result."
        : "This stored family-world result predates the three-part composition table. Its missing parts are not shown as zero.";
    public IReadOnlyList<int> LengthChoices => FamilyWorldOptions.LengthChoices;
    public IReadOnlyList<DatabaseGapLengthRow> LengthRows { get; }
    public int MinimumTagLength
    {
        get => minimumTagLength;
        set
        {
            if (!LengthChoices.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (minimumTagLength == value) return;
            minimumTagLength = value; Rebuild(); storeChoice?.Invoke(value); Changed(string.Empty);
        }
    }
    public IReadOnlyList<DatabaseGapDisplayRow> Rows { get; private set; } = Array.Empty<DatabaseGapDisplayRow>();
    public IReadOnlyList<FamilyWorldCompositionRow> ChartRows { get; private set; } = Array.Empty<FamilyWorldCompositionRow>();
    public IReadOnlyList<DatabaseGapGlobalRow> GlobalRows { get; private set; } = Array.Empty<DatabaseGapGlobalRow>();
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
    public string FastaProblem { get; private set; } = "No matching stored family world is available.";
    public string FastaState => CanExportFasta ? candidates.Count.ToString(CultureInfo.CurrentCulture)
        + " stored world proteins hit by credited tags. FASTA contains suggested additions for you to add and search again in PatternLab."
        : "Suggested additions unavailable: " + FastaProblem;
    private readonly List<FamilyWorldEntry> candidates = new();
    private readonly string worldHash = "";

    public DatabaseGapViewModel(FamilyWorldSummary? summary, FamilyClusterFile? file = null, int? selectedLength = null, Action<int>? storeChoice = null)
    {
        storedResult = summary; this.storeChoice = storeChoice;
        minimumTagLength = selectedLength is int length && LengthChoices.Contains(length) ? length : FamilyWorldOptions.DefaultMinimumTagLength;
        LengthRows = LengthChoices.Select(length => new DatabaseGapLengthRow(length,
            summary?.LengthEstimates?.FirstOrDefault(estimate => estimate.MinimumTagLength == length), summary?.ForLength(length) != null)).ToArray();
        if (summary != null && file != null)
        {
            try
            {
                storedWorld = FamilyWorldFetcher.Stored(file);
                verifiedWorldFingerprint = FamilyWorld.FromSnapshot(storedWorld).Fingerprint;
                worldHash = storedWorld.ContentSha256;
            }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { storedWorld = null; worldProblem = ex.Message; }
        }
        Rebuild();
    }

    private void Rebuild()
    {
        Summary = storedResult?.ForLength(minimumTagLength);
        candidates.Clear(); FastaProblem = !HasData ? EmptyMessage : worldProblem;
        ChartRows = !HasData ? Array.Empty<FamilyWorldCompositionRow>() : Summary!.Composition
            .OrderByDescending(r => r.WorldDeNovoEnvelope).ThenBy(r => r.Kind, StringComparer.Ordinal)
            .ThenBy(r => r.Family, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Family, StringComparer.Ordinal)
            .ThenBy(r => r.CuratedUserIds.FirstOrDefault()).ToArray();
        Rows = ChartRows.Select(r => new DatabaseGapDisplayRow(r, Summary!.MinimumKnownCredits)).ToArray();
        GlobalRows = !HasData ? Array.Empty<DatabaseGapGlobalRow>() : new[] {
            new DatabaseGapGlobalRow("Global unexplained de novo", Summary!.UnexplainedEnvelope),
            new DatabaseGapGlobalRow("Identified, shared between families", Summary.SharedIdentifiedEnvelope),
            new DatabaseGapGlobalRow("De novo in database, shared between families", Summary.SharedDatabaseDeNovoEnvelope),
            new DatabaseGapGlobalRow("De novo in world, shared between families", Summary.SharedEnvelope),
            new DatabaseGapGlobalRow("Identified, in no family", Summary.UnattributedIdentifiedEnvelope) };
        if (!HasData || storedWorld == null) return;
        try
        {
            if (!string.Equals(verifiedWorldFingerprint, Summary!.WorldFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The result fingerprint does not match the stored world. No protein was substituted.");
            if (storedWorld.Release != Summary.WorldRelease || !storedWorld.Families.OrderBy(f => f, StringComparer.Ordinal).SequenceEqual(
                Summary.ScopeFamilies.OrderBy(f => f, StringComparer.Ordinal), StringComparer.Ordinal))
                throw new InvalidDataException("The stored world release or scope differs from the result.");
            var byAccession = storedWorld.Entries.ToDictionary(entry => entry.Accession, StringComparer.Ordinal);
            foreach (string id in Summary.Families.SelectMany(row => row.WorldProteins).Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            {
                if (!byAccession.TryGetValue(id, out var entry) || id.IndexOfAny(new[] { '\r', '\n' }) >= 0
                    || entry.Sequence.Length == 0 || entry.Sequence.IndexOfAny(new[] { '\r', '\n' }) >= 0)
                    throw new InvalidDataException("A recorded hit has no exact usable sequence in the stored world. No partial FASTA is available.");
                candidates.Add(entry);
            }
            if (candidates.Count == 0) throw new InvalidDataException("No world proteins were recorded for credited tags.");
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
        + "The length table reads stored estimates: landed share is credited envelope over unattributed de novo envelope, "
        + "and its decoy rate uses credited envelope. Known answers right shows right/landed counts and the recorded rate. "
        + "Choose a minimum tag length to use its stored family result, CSV and suggested FASTA; the choice is saved in the project. "
        + "An uncomputed length stays unavailable, and this screen never starts a search or computes an estimate. "
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
