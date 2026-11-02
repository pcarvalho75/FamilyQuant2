using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record CoverageHit(int Query, int Reference, double Identity, int Columns, int QueryLength,
        int ReferenceLength, int QuerySegment, int LetterPairs, int NativeSegment = 0, int NativeLow = 0,
        int NativeHigh = 0, double NativeCoveragePercent = 0)
    {
        internal double RawPercent => 100.0 * Columns / QueryLength;
        internal double SegmentPercent => 100.0 * QuerySegment / QueryLength;
        internal double PairedPercent => 100.0 * LetterPairs / QueryLength;
    }

    private static CoverageHit ParseCoverageHit(string line)
    {
        string[] fields = line.Split('\t');
        if (fields.Length != 12) throw new InvalidDataException("The native coverage capture must contain twelve fields.");
        int Number(int index) => int.Parse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture);
        int Label(int index, char prefix) => fields[index].Length > 1 && fields[index][0] == prefix
            ? int.Parse(fields[index].AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture)
            : throw new InvalidDataException("Unexpected indexed native label.");
        int columns = Number(3), queryLength = Number(4), nativeSegment = Number(6), low = Number(8), high = Number(9);
        string queryRow = fields[10], targetRow = fields[11];
        int segment = queryRow.Count(c => c != '-');
        if (columns != queryRow.Length || columns != targetRow.Length || queryLength <= 0 || segment <= 0 || segment > queryLength
            || targetRow.Count(c => c != '-') > Number(5))
            throw new InvalidDataException("Native alignment rows and sequence lengths disagree.");
        int paired = queryRow.Zip(targetRow).Count(pair => pair.First != '-' && pair.Second != '-');
        return new CoverageHit(Label(0, 'q'), Label(1, 'r'), double.Parse(fields[2], CultureInfo.InvariantCulture),
            columns, queryLength, Number(5), segment, paired, nativeSegment, low, high,
            double.Parse(fields[7], CultureInfo.InvariantCulture));
    }

    private static NearestNeighbour? ChooseCoverage(string identifier, string own, IReadOnlyList<NeighbourReference> references,
        IEnumerable<CoverageHit> hits, double identity, int coverage, int reading)
    {
        var filtered = hits.Where(h => reading == 0 || 100L * (reading == 1 ? h.QuerySegment : h.LetterPairs) >= (long)coverage * h.QueryLength);
        if (reading != 0) return NearestReviewed.Choose(identifier, own, filtered.Select(h =>
            (references[h.Reference], h.Identity, h.Columns, h.QueryLength, h.ReferenceLength, reading == 1 ? h.QuerySegment : h.LetterPairs)), identity, coverage);
        return NearestReviewed.Choose(identifier, own, filtered.Select(h =>
            (references[h.Reference], h.Identity, h.Columns, h.QueryLength, h.ReferenceLength)), identity, coverage);
    }

    private static void EvidenceCoverageTests()
    {
        var hit = ParseCoverageHit("q0\tr0\t50\t8\t10\t8\t10\t40\t1\t10\tAC----DE\tACXXXXDE");
        Check("A2 coverage counts query letters independently of gap-inclusive columns", hit.Columns == 8 && hit.QuerySegment == 4
            && hit.LetterPairs == 4 && hit.RawPercent == 80 && hit.SegmentPercent == 40);
        Check("A2 uses actual aligned query letters when native global qs includes unaligned flanks", hit.NativeSegment == 10 && hit.QuerySegment == 4);
        var references = new[] { new NeighbourReference { Accession = "fixture_a", Family = "Fixture A" },
            new NeighbourReference { Accession = "fixture_b", Family = "Fixture B" } };
        var hits = new[] { hit with { Identity = 80 }, new CoverageHit(0, 1, 75, 8, 10, 8, 8, 8) };
        Check("A2 counterfactual reselects the next qualifying neighbour through the existing chooser",
            ChooseCoverage("query", "own", references, hits, 50, 70, 0)?.Family == "Fixture A"
            && ChooseCoverage("query", "own", references, hits, 50, 70, 1)?.Family == "Fixture B");
        Check("A2 coverage refuses inconsistent native rows rather than guessing", Throws<InvalidDataException>(() =>
            ParseCoverageHit("q0\tr0\t50\t7\t10\t8\t10\t40\t1\t10\tAC----DE\tACXXXXDE")));
        var protein = new FastaItemExtended { SequenceIdentifier = "query", Sequence = new string('A', 103),
            Evidence = new ProteinEvidence { Keywords = new List<string> { "selected" } } };
        var neighbour = new NearestNeighbour { Query = "query", Accession = "reference", Reference = "reference", Family = "Fixture family",
            IdentityPercent = 80, AlignmentLength = 132, QueryLength = 103, ReferenceLength = 132, QueryAlignedResidues = 103 };
        var proposal = EvidenceClassifier.Propose(new[] { protein }, new EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } },
            new Dictionary<string, NearestNeighbour> { ["query"] = neighbour }, _ => new ProteinEvidence { Reviewed = true, Family = "Fixture family" });
        var row = proposal.Proteins.Single();
        Check("A2 audit gives counts without a false coverage percentage or a changed gate", row.Tier == EvidenceTier.ReviewedNeighbour
            && row.Family == "Fixture family" && row.Reason.Contains("132 alignment columns", StringComparison.Ordinal)
            && row.Reason.Contains("query length 103 residues", StringComparison.Ordinal) && !row.Reason.Contains("128.16%", StringComparison.Ordinal)
            && !row.Reason.Contains("% query alignment.", StringComparison.Ordinal) && neighbour.QueryAlignedPercent > 100
            && NearestReviewed.PassesGate(80, 103, 103, 60, 70));
    }

    private static int EvidenceCoverageCommand(string[] args)
    {
        try
        {
            bool replay = args.Length == 5;
            if (args.Length != 4 && !replay) throw new ArgumentException("Use --evidence-coverage-check <prepared.fcf> <applied.fcf> <new-output-directory> [existing-capture-directory].");
            if (Directory.Exists(args[3])) throw new IOException("Use a new coverage output directory.");
            var file = FamilyClusterFile.Load(args[1]);
            var applied = FamilyClusterFile.Load(args[2]);
            var recipe = applied.EvidenceRecipe ?? throw new InvalidDataException("The applied file has no evidence recipe.");
            if (recipe.SourceFingerprint != EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems))
                throw new InvalidDataException("The prepared source does not match the applied recipe.");
            var search = file.EvidenceSource?.NearestReviewed ?? throw new InvalidDataException("The prepared file has no stored search.");
            var queries = NearestReviewed.Targets(file).Where(q => !string.IsNullOrEmpty(q.Sequence)).ToList();
            var references = NearestReviewed.References(file, e => EvidenceClassifier.IsInScope(e, recipe.Options.ScopeTerms));
            if (search.CoverageMethod != null || !NearestReviewed.IsCurrent(search, queries, references, allowLegacyCoverage: true))
                throw new InvalidDataException("This historical comparison needs the original pre-A6 search and matching scoped inputs.");
            Directory.CreateDirectory(args[3]);
            string capture = replay ? args[4] : Path.Combine(args[3], "capture");
            if (!replay) CaptureCoverage(queries, references, search.MinIdentityPercent, capture).GetAwaiter().GetResult();
            MeasureCoverage(file, applied, search, queries, references, capture, args[3], Path.GetDirectoryName(Path.GetFullPath(args[2]))!);
            return 0;
        }
        catch (Exception ex) { Console.WriteLine(ex.GetBaseException()); return 1; }
    }

    private static async Task CaptureCoverage(IReadOnlyList<FastaItemExtended> queries, IReadOnlyList<NeighbourReference> references,
        double identity, string directory)
    {
        Directory.CreateDirectory(directory);
        string executable = (string)(typeof(USearchTools).GetMethod("GetUSearchPath",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)
            ?? throw new InvalidOperationException("The program did not resolve its native executable."));
        string executableHash = TestFileHash.Upper(executable);
        string referencePath = Path.Combine(directory, "references.fasta");
        WriteCoverageFasta(referencePath, 'r', references.Select(r => r.Sequence), 0);
        var batches = new List<string>();
        for (int first = 0; first < queries.Count; first += NearestReviewed.DefaultQueriesPerRun)
        {
            string queryPath = Path.Combine(directory, "queries.fasta");
            WriteCoverageFasta(queryPath, 'q', queries.Skip(first).Take(NearestReviewed.DefaultQueriesPerRun).Select(q => q.Sequence), first);
            string output = Path.Combine(directory, "hits_" + first.ToString("D6", CultureInfo.InvariantCulture) + ".tsv");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = directory };
            foreach (string value in new[] { "-search_global", queryPath, "-db", referencePath, "-id", (identity / 100).ToString("0.######", CultureInfo.InvariantCulture),
                "-maxaccepts", "0", "-maxrejects", "0", "-threads", "4", "-userout", output,
                "-userfields", "query+target+id+alnlen+ql+tl+qs+qcov+qlo+qhi+qrow+trow" }) start.ArgumentList.Add(value);
            using var process = Process.Start(start) ?? throw new IOException("Could not start the owned native coverage capture.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Native capture PID " + process.Id.ToString(CultureInfo.InvariantCulture) + ", queries starting at " + first.ToString(CultureInfo.InvariantCulture));
            if (!process.WaitForExit(30 * 60 * 1000))
            {
                process.Kill(); process.WaitForExit(); // This exact Process instance was started by this command.
                throw new TimeoutException("The owned native coverage batch exceeded thirty minutes.");
            }
            string outputText = await stdout.ConfigureAwait(false), errorText = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0 || !File.Exists(output)) throw new InvalidDataException(outputText + errorText);
            batches.Add(Path.GetFileName(output));
            Console.WriteLine("Captured " + Math.Min(first + NearestReviewed.DefaultQueriesPerRun, queries.Count).ToString(CultureInfo.InvariantCulture) + "/" + queries.Count.ToString(CultureInfo.InvariantCulture));
        }
        File.WriteAllText(Path.Combine(directory, "capture.json"), JsonSerializer.Serialize(new
        {
            queries = queries.Count, references = references.Count, minIdentityPercent = identity,
            executableSha256 = executableHash, batches,
            querySha256 = CoverageSequenceHash(queries.Select(q => q.Sequence)), referenceSha256 = CoverageSequenceHash(references.Select(r => r.Sequence)),
        }, DeliveryJson));
        if (TestFileHash.Upper(executable) != executableHash) throw new InvalidDataException("The native executable changed during capture.");
    }

    private static void WriteCoverageFasta(string path, char prefix, IEnumerable<string> sequences, int first)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (string sequence in sequences) { writer.WriteLine(">" + prefix + first.ToString(CultureInfo.InvariantCulture)); writer.WriteLine(sequence); first++; }
    }

    private static string CoverageSequenceHash(IEnumerable<string> sequences) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        Encoding.UTF8.GetBytes(string.Join("\n", sequences))));

    private static void MeasureCoverage(FamilyClusterFile file, FamilyClusterFile applied, NeighbourSearch search,
        IReadOnlyList<FastaItemExtended> queries, IReadOnlyList<NeighbourReference> references, string capture, string output, string baselineDirectory)
    {
        using var captured = JsonDocument.Parse(File.ReadAllText(Path.Combine(capture, "capture.json")));
        var root = captured.RootElement;
        if (root.GetProperty("queries").GetInt32() != queries.Count || root.GetProperty("references").GetInt32() != references.Count
            || root.GetProperty("minIdentityPercent").GetDouble() != search.MinIdentityPercent
            || root.GetProperty("querySha256").GetString() != CoverageSequenceHash(queries.Select(q => q.Sequence))
            || root.GetProperty("referenceSha256").GetString() != CoverageSequenceHash(references.Select(r => r.Sequence)))
            throw new InvalidDataException("The capture does not match these exact input sequences and search threshold.");
        var hits = Enumerable.Range(0, queries.Count).Select(_ => new List<CoverageHit>()).ToArray();
        long returned = 0, nonSelf = 0, rawGate = 0, segmentGate = 0, pairedGate = 0, nativeSpanDiffers = 0, qcovDisagrees = 0;
        foreach (var batch in root.GetProperty("batches").EnumerateArray())
            foreach (string line in File.ReadLines(Path.Combine(capture, batch.GetString()!)))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var hit = ParseCoverageHit(line); returned++;
                if (hit.NativeSegment != hit.QuerySegment || hit.NativeHigh - hit.NativeLow + 1 != hit.QuerySegment) nativeSpanDiffers++;
                if (Math.Abs(hit.NativeCoveragePercent - hit.SegmentPercent) > 0.51) qcovDisagrees++;
                if (hit.Query < 0 || hit.Query >= queries.Count || hit.Reference < 0 || hit.Reference >= references.Count
                    || hit.QueryLength != queries[hit.Query].Sequence.Length || hit.ReferenceLength != references[hit.Reference].Sequence.Length)
                    throw new InvalidDataException("A native hit does not match its indexed input sequences.");
                if (hit.Identity <= 1 && search.MinIdentityPercent > 1) hit = hit with { Identity = hit.Identity * 100 };
                if (NearestReviewed.EntryAccession(queries[hit.Query].SequenceIdentifier) == references[hit.Reference].Accession) continue;
                nonSelf++;
                if (!(hit.Identity + 1e-9 >= search.MinIdentityPercent && hit.QueryLength > 0
                    && 100L * hit.Columns >= (long)search.MinQueryAlignedPercent * hit.QueryLength)) continue;
                rawGate++;
                if (100L * hit.QuerySegment >= (long)search.MinQueryAlignedPercent * hit.QueryLength) segmentGate++;
                if (100L * hit.LetterPairs >= (long)search.MinQueryAlignedPercent * hit.QueryLength) pairedGate++;
                hits[hit.Query].Add(hit);
            }
        var neighbours = Enumerable.Range(0, 3).Select(reading => queries.Select((query, index) =>
            ChooseCoverage(query.SequenceIdentifier, NearestReviewed.EntryAccession(query.SequenceIdentifier), references,
                hits[index], search.MinIdentityPercent, search.MinQueryAlignedPercent, reading)).Where(n => n != null).Select(n => n!).ToList()).ToArray();
        if (JsonSerializer.Serialize(neighbours[0]) != JsonSerializer.Serialize(search.Neighbours))
            throw new InvalidDataException("The captured baseline does not exactly reproduce the stored native neighbours. No counterfactual conclusion was made.");
        var options = applied.EvidenceRecipe!.Options;
        var profiles = CoherenceCheck.Learn(file);
        // The original review is a persisted program result from the old gate. Do not silently
        // reinterpret its absent residue counts with the new classifier after A6.
        var recordedReview = ReadDelivery<EvidenceReviewInput>(Path.Combine(baselineDirectory, "review.json"));
        if (recordedReview.SourceFingerprint != applied.EvidenceRecipe.SourceFingerprint)
            throw new InvalidDataException("The persisted historical review does not match the original recipe.");
        var byQuery = search.ByQuery();
        var proposals = new EvidenceProposal[3];
        proposals[0] = new EvidenceProposal
        {
            SourceFingerprint = recordedReview.SourceFingerprint, Options = options,
            Proteins = recordedReview.Proteins.Select(p => new EvidenceProteinProposal
            {
                ProteinIndex = p.ProteinIndex, Identifier = p.Identifier, PreviousFamilyId = p.PreviousFamilyId, Family = p.Family,
                Tier = Enum.Parse<EvidenceTier>(p.Tier.Replace(" ", string.Empty), ignoreCase: true), Reason = p.Reason,
                HasScopeAnnotation = p.HasScopeAnnotation, Neighbour = byQuery.GetValueOrDefault(p.Identifier),
                Flags = p.Flags.Select(text => new EvidenceFlag { Code = "recorded", Message = text }).ToArray(),
            }).ToArray(),
        };
        for (int reading = 1; reading < 3; reading++) proposals[reading] = CoherenceCheck.Flag(EvidenceClassifier.Propose(file.MyExtendedFastaItems,
            options, neighbours[reading].ToDictionary(n => n.Query, StringComparer.Ordinal), NearestReviewed.ReferenceEvidence(file)), file.MyExtendedFastaItems, profiles);
        var selections = new HashSet<int>[3];
        selections[0] = new EvidenceReviewViewModel(recordedReview).Proteins.Where(p => p.Selected).Select(p => p.ProteinIndex).ToHashSet();
        for (int reading = 1; reading < 3; reading++) selections[reading] = new EvidenceReviewViewModel(EvidenceProposalPresentation.Present(proposals[reading], file))
            .Proteins.Where(p => p.Selected).Select(p => p.ProteinIndex).ToHashSet();
        if (!selections[0].SetEquals(applied.EvidenceRecipe.SelectedProteinIndices))
            throw new InvalidDataException("The reproduced default selection differs from the actual A1 recipe. No impact claim was made.");
        var baseline = proposals[0].Proteins.ToDictionary(p => p.ProteinIndex);
        if (applied.EvidenceRecipe.Assignments.Any(a => baseline[a.ProteinIndex].Family != a.Family || baseline[a.ProteinIndex].Tier != a.Tier))
            throw new InvalidDataException("The persisted historical review's selected families or tiers differ from its recipe.");
        var queryIndex = queries.Select((q, index) => (q.SequenceIdentifier, index)).ToDictionary(p => p.SequenceIdentifier, p => p.index, StringComparer.Ordinal);
        CoverageHit? ChosenHit(EvidenceProteinProposal row)
        {
            var neighbour = row.Neighbour;
            if (neighbour == null) return null;
            return hits[queryIndex[row.Identifier]].First(h => references[h.Reference].Accession == neighbour.Accession
                && references[h.Reference].Identifier == neighbour.Reference && references[h.Reference].Family == neighbour.Family
                && h.Columns == neighbour.AlignmentLength && Math.Abs(h.Identity - neighbour.IdentityPercent) <= 1e-9);
        }
        object Impact(int reading)
        {
            var changed = proposals[reading].Proteins.Where(row => baseline[row.ProteinIndex].Family != row.Family
                || selections[0].Contains(row.ProteinIndex) != selections[reading].Contains(row.ProteinIndex)).ToArray();
            File.WriteAllText(Path.Combine(output, reading == 1 ? "query-segment-changes.json" : "letter-pair-changes.json"),
                JsonSerializer.Serialize(changed.Select(row => new { row.Identifier, oldFamily = baseline[row.ProteinIndex].Family,
                    newFamily = row.Family, oldTier = baseline[row.ProteinIndex].Tier, newTier = row.Tier,
                    oldSelected = selections[0].Contains(row.ProteinIndex), newSelected = selections[reading].Contains(row.ProteinIndex),
                    oldNeighbour = baseline[row.ProteinIndex].Neighbour, newNeighbour = row.Neighbour }), DeliveryJson));
            File.WriteAllText(Path.Combine(output, reading == 1 ? "query-segment-neighbour-changes.json" : "letter-pair-neighbour-changes.json"),
                JsonSerializer.Serialize(proposals[reading].Proteins.Where(row =>
                    NeighbourIdentity(baseline[row.ProteinIndex].Neighbour) != NeighbourIdentity(row.Neighbour)).Select(row => new
                    {
                        row.Identifier, row.Family, row.Tier, selected = selections[reading].Contains(row.ProteinIndex),
                        oldNeighbour = baseline[row.ProteinIndex].Neighbour, newNeighbour = row.Neighbour,
                        originalBestNativeAlignment = ChosenHit(baseline[row.ProteinIndex]),
                    }), DeliveryJson));
            return new
            {
                neighbours = neighbours[reading].Count,
                neighbourChanges = proposals[reading].Proteins.Count(row => NeighbourIdentity(baseline[row.ProteinIndex].Neighbour) != NeighbourIdentity(row.Neighbour)),
                proposedFamilyChanges = proposals[reading].Proteins.Count(row => baseline[row.ProteinIndex].Family != row.Family),
                proposedTierChanges = proposals[reading].Proteins.Count(row => baseline[row.ProteinIndex].Tier != row.Tier),
                defaultSelected = selections[reading].Count,
                previouslySelectedLost = selections[0].Except(selections[reading]).Count(), newlySelected = selections[reading].Except(selections[0]).Count(),
                familyOrDefaultSelectionChanges = changed.Length,
            };
        }
        var outliers = applied.EvidenceRecipe.Assignments.Where(a => a.Reason.Contains("% query alignment.", StringComparison.Ordinal))
            .Select(a => (Assignment: a, Hit: ChosenHit(baseline[a.ProteinIndex])))
            .Where(row => row.Hit != null && row.Hit.RawPercent > 100).ToArray();
        File.WriteAllText(Path.Combine(output, "selected-audit-over-100.json"), JsonSerializer.Serialize(outliers.Select(row => new
        {
            row.Assignment.Identifier, row.Assignment.Family, row.Assignment.Name, row.Assignment.Reason,
            alignmentColumns = row.Hit!.Columns, queryLength = row.Hit.QueryLength, querySegmentResidues = row.Hit.QuerySegment,
            pairedQueryResidues = row.Hit.LetterPairs, rawPercent = row.Hit.RawPercent,
            querySegmentPercent = row.Hit.SegmentPercent, letterPairPercent = row.Hit.PairedPercent,
            nativeQs = row.Hit.NativeSegment, nativeQlo = row.Hit.NativeLow, nativeQhi = row.Hit.NativeHigh, nativeQcov = row.Hit.NativeCoveragePercent,
        }), DeliveryJson));
        var report = new
        {
            sourceFingerprint = applied.EvidenceRecipe.SourceFingerprint, search.QuerySha256, search.ReferenceSha256,
            executableSha256 = root.GetProperty("executableSha256").GetString(), baselineExactlyReproduced = true,
            baselineDefaultSelectionExactlyReproduced = true, queries = queries.Count, references = references.Count,
            returnedNativeHits = returned, otherEntryNativeHits = nonSelf, rawGateHits = rawGate,
            querySegmentGateHits = segmentGate, letterPairGateHits = pairedGate,
            nativeQsOrCoordinateSpanDiffersFromAlignedQueryLetters = nativeSpanDiffers,
            nativeQcovDisagreesWithAlignedQueryLetterPercentBeyondRounding = qcovDisagrees,
            search.MinQueryAlignedPercent, baselineNeighbours = neighbours[0].Count, baselineDefaultSelected = selections[0].Count,
            selectedAuditRowsOver100 = outliers.Length,
            querySegmentDefinition = "Non-gap query letters in qrow divided by full ql; includes query insertions. Native qs/qlo/qhi can include unaligned flanks and are retained, never substituted for aligned-row letters.",
            letterPairDefinition = "Columns containing a letter in both query and target; excludes insertions in either sequence.",
            querySegmentCounterfactual = Impact(1), letterPairCounterfactual = Impact(2),
            productionGateChanged = false,
        };
        string json = JsonSerializer.Serialize(report, DeliveryJson);
        File.WriteAllText(Path.Combine(output, "coverage-report.json"), json);
        Console.WriteLine(json);
    }

    private static string NeighbourIdentity(NearestNeighbour? n) => n == null ? string.Empty : JsonSerializer.Serialize(new
    { n.Query, n.Accession, n.Reference, n.Family, n.IdentityPercent, n.AlignmentLength, n.QueryLength, n.ReferenceLength, n.TiedFamilies });
}
