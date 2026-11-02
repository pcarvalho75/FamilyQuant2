using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Diagnostics;

// Candidate policies produce separate reports. No product grouping is changed.
public static class ClusteringDiagnostics
{
    public sealed record Join(string Query, string Target, double Identity, int Columns, int Pairs,
        int QueryLength, int TargetLength, double? NativeQueryCoverage, double? NativeTargetCoverage, string Cigar, string Origin = "userout");
    public sealed record RejectedJoin(string Query, string Target, string QueryFamilies, string TargetFamilies);
    public sealed record Policy(string Name, Dictionary<string, string> Membership, int AcceptedJoins, int VetoedJoins,
        IReadOnlyList<RejectedJoin>? Rejected = null);
    private sealed record Metric(string Policy, int Proteins, int Targets, int Families, int TargetFamilies,
        int DecoyFamilies, int MixedFamilies, int ChangedMembershipProteins, int ReviewedLabelledTargets,
        int FamiliesWithReviewedLabels, int FamiliesWithTwoReviewedProteins, int ConflictingReviewedFamilies,
        int IdentifiedDistinctPeptides, int IdentifiedMappedPeptides,
        int IdentifiedSharedPeptides, int AcceptedJoins, int VetoedJoins,
        int LegacyNonContaminantTargetGroups, int NonContaminantTargetGroups);
    private static readonly UTF8Encoding Utf8 = new(false);
    private const double Epsilon = 1e-9;
    public sealed record Result(string Folder, string SummaryFile, string TextReport);

    public static bool PassesPairs(Join edge, int identity) => edge.Identity + Epsilon >= identity
        && edge.Pairs + Epsilon >= identity / 100.0 * Math.Max(edge.QueryLength, edge.TargetLength);
    public static bool PassesColumns(Join edge, int identity) => USearchTools.PassesExhaustiveGate(
        new USearchTools.PairwiseEdge(edge.Query, edge.Target, edge.Identity, edge.Columns, edge.QueryLength, edge.TargetLength), identity);
    public static bool ProposedContaminant(string? identifier) => SequenceLabels.IsContaminant(identifier);
    private static bool LegacyFilter(FastaItemExtended protein) =>
        (protein.SequenceIdentifier ?? "").IndexOf(SequenceLabels.ContaminantMarker, StringComparison.OrdinalIgnoreCase) >= 0;
    private static bool Decoy(string id) => id.StartsWith(SequenceLabels.DecoyPrefix, StringComparison.Ordinal);
    public static string ReviewedFamily(FastaItemExtended protein)
    {
        if (Decoy(protein.SequenceIdentifier) || ProposedContaminant(protein.SequenceIdentifier) || protein.Evidence?.Reviewed != true) return "";
        ProteinEvidence evidence = protein.Evidence;
        if (evidence.FamilyMemberships?.Select(m => m.Family.Trim()).Where(f => f.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any() == true) return "";
        return (evidence.Family ?? "").Trim();
    }

    public static Result WriteReport(string documentPath, string output, int identity,
        bool smallExhaustive = false, string? matchingFasta = null, OperationContext? operation = null)
        => WriteReportCore(documentPath, output, identity, smallExhaustive, matchingFasta, operation, null);

    public static Result ReplayReport(string documentPath, string output, int identity, string recordedPacket,
        bool smallExhaustive = false, string? matchingFasta = null, OperationContext? operation = null)
        => WriteReportCore(documentPath, output, identity, smallExhaustive, matchingFasta, operation, Path.GetFullPath(recordedPacket));

    private static Result WriteReportCore(string documentPath, string output, int identity,
        bool smallExhaustive, string? matchingFasta, OperationContext? operation, string? recordedPacket)
    {
        operation?.ThrowIfCancellationRequested();
        documentPath = Path.GetFullPath(documentPath); output = Path.GetFullPath(output);
        if (identity is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(identity));
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output directory must be fresh.");
        Directory.CreateDirectory(output);
        FamilyClusterFile document;
        List<QuantRow> quant;
        if (ProjectStore.IsProjectPath(documentPath))
        {
            var loaded = ProjectStore.ReadFqproj(documentPath, operation); document = loaded.fcf; quant = loaded.quant;
        }
        else { document = FamilyClusterFile.Load(documentPath, operation); quant = new(); }
        var proteins = document.MyExtendedFastaItems.ToDictionary(p => p.SequenceIdentifier, StringComparer.Ordinal);
        if (smallExhaustive && proteins.Count > 1000)
            throw new InvalidOperationException("This diagnostic never runs all-pairs on more than 1000 records.");
        string fasta = matchingFasta == null ? Path.Combine(output, "input.fasta") : Path.GetFullPath(matchingFasta);
        if (matchingFasta == null)
        {
            using var writer = new StreamWriter(fasta, false, Utf8);
            foreach (var protein in document.MyExtendedFastaItems)
            {
                operation?.ThrowIfCancellationRequested();
                writer.WriteLine(">" + protein.SequenceIdentifier); writer.WriteLine(protein.Sequence);
            }
        }
        VerifyFasta(fasta, proteins);
        string executable = USearchTools.GetUSearchPath();
        if (recordedPacket != null)
        {
            using var recorded = JsonDocument.Parse(File.ReadAllText(Path.Combine(recordedPacket, "summary.json")));
            var metadata = recorded.RootElement;
            if (metadata.GetProperty("identityPercent").GetInt32() != identity
                || !string.Equals(metadata.GetProperty("documentSha256").GetString(), FileFingerprint.Sha256(documentPath), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(metadata.GetProperty("fastaSha256").GetString(), FileFingerprint.Sha256(fasta), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(metadata.GetProperty("usearchSha256").GetString(), FileFingerprint.Sha256(executable), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Recorded alignments do not match this exact document, FASTA, identity and executable.");
        }
        string fraction = (identity / 100.0).ToString("0.00", CultureInfo.InvariantCulture);
        void Cluster(string name, bool nativeCoverage)
        {
            string uc = Path.Combine(output, name + ".uc"), table = Path.Combine(output, name + ".native.tsv");
            if (recordedPacket != null)
            {
                operation?.ThrowIfCancellationRequested();
                File.Copy(Path.Combine(recordedPacket, name + ".uc"), uc);
                File.Copy(Path.Combine(recordedPacket, name + ".native.tsv"), table);
                return;
            }
            string arguments = $"-cluster_fast \"{fasta}\" -id {fraction} -uc \"{uc}\" -userout \"{table}\" "
                + "-userfields query+target+id+alnlen+pairs+ql+tl+qcov+tcov+caln"
                + (nativeCoverage ? " -query_cov " + fraction + " -target_cov " + fraction : "");
            operation?.Report("clustering-diagnostic", "Comparing " + name);
            USearchTools.RunUSearch(executable, arguments, output, operation);
            if (!File.Exists(uc) || !File.Exists(table)) throw new IOException("Native clustering output is incomplete.");
        }
        Cluster("before", false);
        Cluster("native-coverage", true);
        var before = ReadMembership(Path.Combine(output, "before.uc"), proteins.Keys);
        var native = ReadMembership(Path.Combine(output, "native-coverage.uc"), proteins.Keys);
        var joins = CompleteJoins(Path.Combine(output, "before.native.tsv"), Path.Combine(output, "before.uc"), proteins);
        var nativeJoins = CompleteJoins(Path.Combine(output, "native-coverage.native.tsv"), Path.Combine(output, "native-coverage.uc"), proteins);
        if (joins.Count != proteins.Count - before.Values.Distinct(StringComparer.Ordinal).Count())
            throw new InvalidDataException("The native user table does not cover every fast hit exactly once.");
        if (joins.Concat(nativeJoins).Any(j => !proteins.ContainsKey(j.Query) || !proteins.ContainsKey(j.Target)
            || proteins[j.Query].Sequence.Length != j.QueryLength || proteins[j.Target].Sequence.Length != j.TargetLength))
            throw new InvalidDataException("Native alignment labels or lengths differ from the input.");
        var evidence = proteins.ToDictionary(p => p.Key, p => ReviewedFamily(p.Value), StringComparer.Ordinal);
        var posthoc = Gate(proteins.Keys, joins, identity, evidence, false, operation);
        var veto = Gate(proteins.Keys, joins, identity, evidence, true, operation);
        var policies = new List<Policy> { new("before-fast", before, joins.Count, 0),
            new("native-terminal-coverage", native, nativeJoins.Count, 0), posthoc, veto };
        policies.Add(new("overmerge-negative-control", proteins.Keys.ToDictionary(id => id,
            id => Decoy(id) ? "all-decoys" : "all-targets", StringComparer.Ordinal),
            proteins.Count - (proteins.Keys.Any(Decoy) ? 1 : 0) - (proteins.Keys.Any(id => !Decoy(id)) ? 1 : 0), 0));
        if (smallExhaustive)
        {
            string path = Path.Combine(output, "small-allpairs.native.tsv");
            string arguments = $"-allpairs_global \"{fasta}\" -acceptall -userout \"{path}\" "
                + "-userfields query+target+id+alnlen+pairs+ql+tl+qcov+tcov+caln";
            operation?.Report("clustering-diagnostic", "Comparing bounded all-pairs");
            if (recordedPacket == null) USearchTools.RunUSearch(executable, arguments, output, operation);
            else File.Copy(Path.Combine(recordedPacket, "small-allpairs.native.tsv"), path);
            var all = ReadJoins(path);
            if (all.Count != proteins.Count * (proteins.Count - 1) / 2)
                throw new InvalidDataException("The bounded all-pairs table does not contain every unordered pair.");
            var numbers = before.Values.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal)
                .Select((label, index) => (label, index)).ToDictionary(p => p.label, p => p.index, StringComparer.Ordinal);
            var first = before.ToDictionary(p => p.Key, p => numbers[p.Value], StringComparer.Ordinal);
            var merged = USearchTools.MergeFirstPassClusters(first,
                all.Select(j => new USearchTools.PairwiseEdge(j.Query, j.Target, j.Identity, j.Columns, j.QueryLength, j.TargetLength)), identity);
            var names = merged.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).OrderBy(s => s, StringComparer.Ordinal).First());
            var currentExhaustive = merged.ToDictionary(p => p.Key, p => names[p.Value], StringComparer.Ordinal);
            policies.Add(new("current-exhaustive", currentExhaustive, proteins.Count - names.Count, 0));
            var corrected = Gate(proteins.Keys, all, identity, evidence, false, operation);
            var correctedVeto = Gate(proteins.Keys, all, identity, evidence, true, operation);
            policies.Add(corrected with { Name = "allpairs-paired" });
            policies.Add(correctedVeto with { Name = "allpairs-paired-reviewed-veto" });
            WriteJoins(Path.Combine(output, "small-allpairs-gated.tsv"), all.Where(j => j.Identity + Epsilon >= identity), identity, evidence);
        }
        var peptides = quant.Where(q => q.Side == NativeQuantFractions.SideIdentified)
            .Select(q => DeNovoReconciler.NormalizeSequenceIgnoringPtm(q.Sequence, false)).Where(s => s.Length > 0)
            .Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToArray();
        var targets = proteins.Values.Where(p => !Decoy(p.SequenceIdentifier) && !ProposedContaminant(p.SequenceIdentifier)).ToArray();
        var peptideProteins = peptides.ToDictionary(s => s, s =>
        {
            operation?.ThrowIfCancellationRequested();
            return targets.Where(p => p.Sequence.Contains(s, StringComparison.Ordinal)).Select(p => p.SequenceIdentifier).ToArray();
        }, StringComparer.Ordinal);
        var metrics = policies.Select(p => Summarize(p, before, proteins, evidence, peptideProteins)).ToArray();
        WriteJoins(Path.Combine(output, "fast-joins.tsv"), joins, identity, evidence);
        WriteJoins(Path.Combine(output, "native-coverage-joins.tsv"), nativeJoins, identity, evidence);
        Write(output, "vetoed-joins.tsv", new[] { "policy\tquery\ttarget\tquery_component_reviewed_families\ttarget_component_reviewed_families" }
            .Concat(policies.SelectMany(p => (p.Rejected ?? Array.Empty<RejectedJoin>()).Select(j => Cell(p.Name) + "\t"
                + Cell(j.Query) + "\t" + Cell(j.Target) + "\t" + Cell(j.QueryFamilies) + "\t" + Cell(j.TargetFamilies)))));
        Write(output, "reviewed-conflicts.tsv", new[] { "policy\tcomponent\tprotein\tsource_family" }
            .Concat(policies.SelectMany(p => p.Membership.GroupBy(m => m.Value).OrderBy(g => g.Key, StringComparer.Ordinal)
                .Where(g => g.Select(m => evidence[m.Key]).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
                .SelectMany(g => g.Where(m => evidence[m.Key].Length > 0).OrderBy(m => m.Key, StringComparer.Ordinal)
                    .Select(m => Cell(p.Name) + "\t" + Cell(g.Key) + "\t" + Cell(m.Key) + "\t" + Cell(evidence[m.Key]))))));
        Write(output, "assignments.tsv", new[] { "identifier\t" + string.Join("\t", policies.Select(p => p.Name)) }
            .Concat(proteins.Keys.OrderBy(s => s, StringComparer.Ordinal).Select(id => Cell(id) + "\t"
                + string.Join("\t", policies.Select(p => Cell(p.Membership[id]))))));
        Write(output, "identified-peptides.tsv", new[] { "sequence\tmatching_target_proteins\t" + string.Join("\t", policies.Select(p => p.Name)) }
            .Concat(peptideProteins.Select(pair => Cell(pair.Key) + "\t" + N(pair.Value.Length) + "\t"
                + string.Join("\t", policies.Select(p => N(pair.Value.Select(id => p.Membership[id]).Distinct(StringComparer.Ordinal).Count()))))));
        Write(output, "contaminants.tsv", new[] { "identifier\tdecoy\tlegacy_filter_substring_ignorecase\tlegacy_score_prefix_ignorecase\tlegacy_evidence_prefix_ordinal\tproposed_unwrapped_prefix_ordinal" }
            .Concat(proteins.Values.OrderBy(p => p.SequenceIdentifier, StringComparer.Ordinal).Select(p => Cell(p.SequenceIdentifier) + "\t" + B(Decoy(p.SequenceIdentifier)) + "\t"
                + B(LegacyFilter(p)) + "\t" + B(p.SequenceIdentifier.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.OrdinalIgnoreCase)) + "\t"
                + B(p.SequenceIdentifier.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)) + "\t" + B(ProposedContaminant(p.SequenceIdentifier)))));
        var summary = new
        {
            schemaVersion = 1, document = documentPath, documentSha256 = FileFingerprint.Sha256(documentPath),
            fasta = matchingFasta == null ? "input.fasta" : fasta, fastaSha256 = FileFingerprint.Sha256(fasta),
            usearchSha256 = FileFingerprint.Sha256(executable), identityPercent = identity,
            documentClusteringIdentity = document.ClusteringIdentity,
            identifiedPeptideDataAvailable = peptides.Length > 0,
            note = "Diagnostics only. Posthoc policies split the observed fast join tree; they do not re-search rejected queries. Native coverage discards terminal gaps but can credit internal gap columns. Reviewed-family conflicts are diagnostics, not exhaustive biological truth. Exact identified peptide sharing is ambiguity, not proof of a full-protein merge.",
            joins = joins.Count, targetQueryJoins = joins.Count(j => !Decoy(j.Query)),
            fastJoinsRecoveredFromUc = joins.Count(j => j.Origin == "uc"),
            failLegacyColumnGate = joins.Count(j => !PassesColumns(j, identity)), failPairedLetterGate = joins.Count(j => !PassesPairs(j, identity)),
            targetQueryFailLegacyColumnGate = joins.Count(j => !Decoy(j.Query) && !PassesColumns(j, identity)),
            targetQueryFailPairedLetterGate = joins.Count(j => !Decoy(j.Query) && !PassesPairs(j, identity)),
            nativeCoverageResidualFailPairedLetterGate = nativeJoins.Count(j => !PassesPairs(j, identity)),
            legacyFilterContaminants = proteins.Values.Count(LegacyFilter),
            legacyScoreContaminants = proteins.Keys.Count(id => id.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.OrdinalIgnoreCase)),
            legacyEvidenceContaminants = proteins.Keys.Count(id => id.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.Ordinal)),
            proposedContaminants = proteins.Keys.Count(ProposedContaminant),
            filterChangedTargets = proteins.Values.Count(p => !Decoy(p.SequenceIdentifier) && LegacyFilter(p) != ProposedContaminant(p.SequenceIdentifier)),
            metrics,
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }) + "\n", Utf8);
        string report = Path.Combine(output, "diagnostic.txt");
        var text = new StringBuilder("Clustering diagnostic\n\nThese comparisons do not change your saved families.\n");
        text.Append("Identity: ").Append(N(identity)).Append(" percent. Input records: ").Append(N(proteins.Count)).Append(".\n");
        text.Append("Observed fast joins: ").Append(N(joins.Count)).Append(". Below paired-letter coverage: ")
            .Append(N(joins.Count(j => !PassesPairs(j, identity)))).Append(".\n");
        text.Append("Coverage counts paired letters divided by the longer protein; gaps earn no credit.\n");
        text.Append("Native coverage can credit internal gaps. Splitting observed joins does not search again.\n\n");
        text.Append("Comparison\tFamilies\tMixed target/decoy families\tReviewed-label conflicts\tShared identified peptides\n");
        foreach (var metric in metrics)
            text.Append(PolicyTitle(metric.Policy)).Append('\t').Append(N(metric.Families)).Append('\t').Append(N(metric.MixedFamilies)).Append('\t')
                .Append(metric.ReviewedLabelledTargets == 0 ? "not available" : N(metric.ConflictingReviewedFamilies)).Append('\t')
                .Append(peptides.Length == 0 ? "not available" : N(metric.IdentifiedSharedPeptides)).Append('\n');
        text.Append("\nCommon contaminant rule: one exact ").Append(SequenceLabels.DecoyPrefix)
            .Append(" unwrap, then exact ").Append(SequenceLabels.ContaminantPrefix).Append(" prefix.\n");
        text.Append("Noncontaminant target families in the current fast grouping, previous rule: ")
            .Append(N(metrics[0].LegacyNonContaminantTargetGroups)).Append("; common rule: ")
            .Append(N(metrics[0].NonContaminantTargetGroups)).Append(".\n");
        text.Append("\nReviewed labels are direct source observations, not independent biological validation.\n")
            .Append("Shared peptides report ambiguous attribution; they do not justify merging complete proteins.\n")
            .Append("The overmerge negative control exposes a limitation of target/decoy mixing and sharing alone.\n")
            .Append("Details and exact assignments are in the adjacent JSON and TSV files.\n");
        File.WriteAllText(report, text.ToString(), Utf8);
        return new(output, Path.Combine(output, "summary.json"), report);
    }

    public static Policy Gate(IEnumerable<string> identifiers, IEnumerable<Join> joins, int identity,
        IReadOnlyDictionary<string, string> evidence, bool veto, OperationContext? operation = null)
    {
        var parent = identifiers.ToDictionary(s => s, s => s, StringComparer.Ordinal);
        var labels = identifiers.ToDictionary(s => s, s => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.Ordinal);
        foreach (var pair in evidence) if (pair.Value.Length > 0) labels[pair.Key].Add(pair.Value);
        string Find(string id)
        {
            while (parent[id] != id) { parent[id] = parent[parent[id]]; id = parent[id]; }
            return id;
        }
        int accepted = 0, refused = 0;
        var rejected = new List<RejectedJoin>();
        // Stable identifier ordering makes the diagnostic veto independent of native output ordering.
        // Other valid edge orderings can produce different partitions; this is not a product policy.
        foreach (var join in joins.OrderBy(j => j.Query, StringComparer.Ordinal).ThenBy(j => j.Target, StringComparer.Ordinal))
        {
            operation?.ThrowIfCancellationRequested();
            if (!PassesPairs(join, identity)) continue;
            string a = Find(join.Query), b = Find(join.Target);
            if (a == b) continue;
            if (veto && labels[a].Concat(labels[b]).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any())
            {
                refused++;
                rejected.Add(new(join.Query, join.Target, string.Join("; ", labels[a].OrderBy(s => s, StringComparer.Ordinal)),
                    string.Join("; ", labels[b].OrderBy(s => s, StringComparer.Ordinal))));
                continue;
            }
            string keep = string.CompareOrdinal(a, b) <= 0 ? a : b, drop = keep == a ? b : a;
            parent[drop] = keep; labels[keep].UnionWith(labels[drop]); accepted++;
        }
        return new(veto ? "posthoc-paired-reviewed-veto" : "posthoc-paired", parent.Keys.ToDictionary(s => s, Find, StringComparer.Ordinal), accepted, refused, rejected);
    }

    public static List<Join> ReadJoins(string path)
    {
        var rows = File.ReadLines(path).Where(s => s.Length > 0 && s[0] != '#').Select(s => s.Split('\t')).ToArray();
        if (rows.Any(r => r.Length != 10)) throw new InvalidDataException("Expected ten native alignment columns.");
        double Parse(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        bool fractional = rows.Length > 0 && rows.Max(r => Parse(r[2])) <= 1 + Epsilon;
        var joins = rows.Select(r => new Join(Identifier(r[0]), Identifier(r[1]), Parse(r[2]) * (fractional ? 100 : 1), Int(r[3]), Int(r[4]),
            Int(r[5]), Int(r[6]), Parse(r[7]), Parse(r[8]), r[9])).ToList();
        if (joins.Any(j => !double.IsFinite(j.Identity) || j.Identity is < 0 or > 100 || j.QueryLength <= 0 || j.TargetLength <= 0
            || j.Pairs < 0 || j.Columns < j.Pairs || j.Pairs > Math.Min(j.QueryLength, j.TargetLength)))
            throw new InvalidDataException("Native alignment has invalid identity, length or paired-letter count.");
        return joins;
    }

    public static (int Pairs, int Columns) CigarLengths(string cigar, int queryLength, int targetLength)
    {
        if (cigar == "=" && queryLength == targetLength) return (queryLength, queryLength);
        var operations = new List<(char Op, int Count)>();
        int count = 0;
        foreach (char letter in cigar)
        {
            if (letter is >= '0' and <= '9') { count = checked(count * 10 + letter - '0'); continue; }
            if (letter is not ('M' or 'I' or 'D')) throw new InvalidDataException("Unsupported UC alignment operation.");
            operations.Add((letter, count == 0 ? 1 : count)); count = 0;
        }
        if (count != 0 || operations.Count == 0 || !operations.Any(o => o.Op == 'M')) throw new InvalidDataException("Invalid UC alignment.");
        int first = operations.FindIndex(o => o.Op == 'M'), last = operations.FindLastIndex(o => o.Op == 'M');
        return (operations.Where(o => o.Op == 'M').Sum(o => o.Count), operations.Skip(first).Take(last - first + 1).Sum(o => o.Count));
    }

    private static List<Join> CompleteJoins(string path, string uc, IReadOnlyDictionary<string, FastaItemExtended> proteins)
    {
        var joins = ReadJoins(path);
        var byQuery = joins.ToDictionary(j => j.Query, StringComparer.Ordinal);
        foreach (string line in File.ReadLines(uc))
        {
            string[] columns = line.Split('\t');
            if (columns[0] != "H") continue;
            string query = Identifier(columns[8]), target = Identifier(columns[9]);
            int ql = proteins[query].Sequence.Length, tl = proteins[target].Sequence.Length;
            var lengths = CigarLengths(columns[7], ql, tl);
            if (byQuery.TryGetValue(query, out var native))
            {
                if (native.Target != target || native.Pairs != lengths.Pairs || native.Columns != lengths.Columns)
                    throw new InvalidDataException("UC and userout disagree about the accepted alignment.");
                continue;
            }
            if (columns[7] == "=" && proteins[query].Sequence != proteins[target].Sequence)
                throw new InvalidDataException("UC exact-match alignment has different sequences.");
            var recovered = new Join(query, target, double.Parse(columns[3], CultureInfo.InvariantCulture), lengths.Columns, lengths.Pairs,
                ql, tl, null, null, columns[7], "uc");
            joins.Add(recovered); byQuery.Add(query, recovered);
        }
        return joins;
    }

    private static Dictionary<string, string> ReadMembership(string path, IEnumerable<string> identifiers)
    {
        var groups = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in File.ReadLines(path))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            string[] columns = line.Split('\t');
            if (columns.Length != 10) throw new InvalidDataException("Expected ten UC columns.");
            if (columns[0] is "S" or "H") groups.Add(Identifier(columns[8]), columns[1]);
        }
        if (!groups.Keys.OrderBy(s => s, StringComparer.Ordinal).SequenceEqual(identifiers.OrderBy(s => s, StringComparer.Ordinal)))
            throw new InvalidDataException("UC membership differs from the input identifiers.");
        var names = groups.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => g.Select(p => p.Key).OrderBy(s => s, StringComparer.Ordinal).First());
        return groups.ToDictionary(p => p.Key, p => names[p.Value], StringComparer.Ordinal);
    }

    private static Metric Summarize(Policy policy, IReadOnlyDictionary<string, string> before,
        IReadOnlyDictionary<string, FastaItemExtended> proteins, IReadOnlyDictionary<string, string> evidence,
        IReadOnlyDictionary<string, string[]> peptideProteins)
    {
        var groups = policy.Membership.GroupBy(p => p.Value).ToArray();
        var oldMembers = before.GroupBy(p => p.Value).ToDictionary(g => g.Key, g => string.Join("\n", g.Select(p => p.Key).OrderBy(s => s, StringComparer.Ordinal)));
        var members = groups.ToDictionary(g => g.Key, g => string.Join("\n", g.Select(p => p.Key).OrderBy(s => s, StringComparer.Ordinal)));
        int TargetCount(IEnumerable<KeyValuePair<string, string>> g) => g.Count(p => !Decoy(p.Key));
        return new(policy.Name, proteins.Count, proteins.Keys.Count(id => !Decoy(id)), groups.Length,
            groups.Count(g => TargetCount(g) == g.Count()), groups.Count(g => TargetCount(g) == 0), groups.Count(g => TargetCount(g) > 0 && TargetCount(g) < g.Count()),
            policy.Membership.Count(p => oldMembers[before[p.Key]] != members[p.Value]), evidence.Values.Count(s => s.Length > 0),
            groups.Count(g => g.Any(p => evidence[p.Key].Length > 0)), groups.Count(g => g.Count(p => evidence[p.Key].Length > 0) >= 2),
            groups.Count(g => g.Select(p => evidence[p.Key]).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Skip(1).Any()),
            peptideProteins.Count, peptideProteins.Count(p => p.Value.Length > 0),
            peptideProteins.Count(p => p.Value.Select(id => policy.Membership[id]).Distinct(StringComparer.Ordinal).Skip(1).Any()), policy.AcceptedJoins, policy.VetoedJoins,
            groups.Count(g => g.Any(p => !Decoy(p.Key) && !p.Key.StartsWith(SequenceLabels.ContaminantPrefix, StringComparison.OrdinalIgnoreCase))),
            groups.Count(g => g.Any(p => !Decoy(p.Key) && !SequenceLabels.IsContaminant(p.Key))));
    }

    private static void VerifyFasta(string path, IReadOnlyDictionary<string, FastaItemExtended> proteins)
    {
        using var reader = new StreamReader(path);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        string? header;
        while ((header = reader.ReadLine()) != null)
        {
            string sequence = reader.ReadLine() ?? throw new InvalidDataException("Missing FASTA sequence.");
            string id = header.Length > 0 ? header[1..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "" : "";
            if (!header.StartsWith('>') || !seen.Add(id) || !proteins.TryGetValue(id, out var protein) || protein.Sequence != sequence)
                throw new InvalidDataException("FASTA does not match document identifiers and exact sequences: " + header);
        }
        if (seen.Count != proteins.Count) throw new InvalidDataException("FASTA does not cover the document.");
    }
    private static void WriteJoins(string path, IEnumerable<Join> joins, int identity, IReadOnlyDictionary<string, string> evidence) =>
        File.WriteAllLines(path, new[] { "query\ttarget\tidentity_percent\talignment_columns\tpaired_letters\tquery_length\ttarget_length\tpaired_coverage_longer_percent\tnative_query_coverage\tnative_target_coverage\tpasses_legacy_columns\tpasses_paired_letters\treviewed_query_family\treviewed_target_family\tcigar\talignment_origin" }
            .Concat(joins.OrderBy(j => j.Query, StringComparer.Ordinal).ThenBy(j => j.Target, StringComparer.Ordinal).Select(j => string.Join("\t",
                Cell(j.Query), Cell(j.Target), D(j.Identity), N(j.Columns), N(j.Pairs), N(j.QueryLength), N(j.TargetLength),
                D(100.0 * j.Pairs / Math.Max(j.QueryLength, j.TargetLength)), j.NativeQueryCoverage.HasValue ? D(j.NativeQueryCoverage.Value) : "",
                j.NativeTargetCoverage.HasValue ? D(j.NativeTargetCoverage.Value) : "", B(PassesColumns(j, identity)), B(PassesPairs(j, identity)),
                Cell(evidence[j.Query]), Cell(evidence[j.Target]), Cell(j.Cigar), j.Origin))), Utf8);
    private static void Write(string output, string name, IEnumerable<string> lines) => File.WriteAllText(Path.Combine(output, name), string.Join("\n", lines) + "\n", Utf8);
    private static string Cell(string text) => text.Replace("\t", "\\t", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
    private static string Identifier(string label) => label.Split(' ')[0];
    private static string PolicyTitle(string name) => name switch
    {
        "before-fast" => "Existing fast pass",
        "native-terminal-coverage" => "Fast pass with native coverage",
        "posthoc-paired" => "Observed joins with paired-letter coverage",
        "posthoc-paired-reviewed-veto" => "Observed joins with coverage and experimental reviewed-label veto",
        "overmerge-negative-control" => "Negative control: one target family and one decoy family",
        "current-exhaustive" => "Existing exhaustive pass",
        "allpairs-paired" => "All pairs with paired-letter coverage",
        "allpairs-paired-reviewed-veto" => "All pairs with coverage and experimental reviewed-label veto",
        _ => name,
    };
    private static string D(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
    private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
    private static string B(bool value) => value ? "true" : "false";
}
