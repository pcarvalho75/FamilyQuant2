using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using FamilyQuant.Operations;
using ClusteringImprovementAudit = FamilyQuant.Diagnostics.ClusteringDiagnostics;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int ClusteringImprovementAuditCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--small-exhaustive")
            throw new ArgumentException("Supply document, matching FASTA, fresh output directory, identity percent and optional --small-exhaustive.");
        var result = ClusteringImprovementAudit.WriteReport(args[1], args[3], int.Parse(args[4], System.Globalization.CultureInfo.InvariantCulture), args.Length == 6, args[2]);
        Console.WriteLine(File.ReadAllText(result.SummaryFile));
        return 0;
    }
    private static int ClusteringImprovementTests(string workDir)
    {
        PrefixDefinitionTests();
        ClusteringImprovementGuards(workDir);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int ClusteringImprovementReplayCommand(string[] args)
    {
        if (args.Length is not (6 or 7) || args.Length == 7 && args[6] != "--small-exhaustive")
            throw new ArgumentException("Supply document, matching FASTA, recorded packet, fresh output, identity and optional --small-exhaustive.");
        var result = ClusteringImprovementAudit.ReplayReport(args[1], args[4],
            int.Parse(args[5], System.Globalization.CultureInfo.InvariantCulture), args[3], args.Length == 7, args[2]);
        Console.WriteLine("Replayed recorded alignments from " + Path.GetFullPath(args[3]));
        Console.WriteLine(File.ReadAllText(result.SummaryFile));
        return 0;
    }

    private static void ClusteringImprovementGuards(string workDir)
    {
        Section("X12 diagnostic policy guards");
        var internalGaps = new ClusteringImprovementAudit.Join("a", "b", 90, 100, 80, 100, 100, 100, 100, "80M20I");
        Check("X12 internal gap columns can pass legacy coverage while letter pairs fail",
            ClusteringImprovementAudit.PassesColumns(internalGaps, 85) && !ClusteringImprovementAudit.PassesPairs(internalGaps, 85));
        Check("X12 paired-letter boundary is inclusive", ClusteringImprovementAudit.PassesPairs(internalGaps with { Pairs = 85 }, 85));
        Check("X12 identity is still required", !ClusteringImprovementAudit.PassesPairs(internalGaps with { Pairs = 100, Identity = 84 }, 85));
        Check("X12 longer sequence is the paired-letter denominator", !ClusteringImprovementAudit.PassesPairs(internalGaps with { Pairs = 100, TargetLength = 200 }, 85));
        var edges = new[] { internalGaps with { Query = "a", Target = "b", Pairs = 100 }, internalGaps with { Query = "c", Target = "b", Pairs = 100 } };
        var labels = new Dictionary<string, string>(StringComparer.Ordinal) { ["a"] = "source family one", ["b"] = "", ["c"] = "source family two" };
        var ids = labels.Keys;
        var unrestricted = ClusteringImprovementAudit.Gate(ids, edges, 85, labels, false);
        var veto = ClusteringImprovementAudit.Gate(ids, edges, 85, labels, true);
        Check("X12 unlabelled bridge joins both labelled endpoints without veto", unrestricted.Membership.Values.Distinct().Count() == 1);
        Check("X12 component veto blocks a transitive reviewed-label conflict", veto.Membership.Values.Distinct().Count() == 2 && veto.VetoedJoins == 1);
        var reordered = ClusteringImprovementAudit.Gate(ids.Reverse(), edges.Reverse(), 85, labels, true);
        Check("X12 veto diagnostic has a fixed edge order", ids.All(id => veto.Membership[id] == reordered.Membership[id]));
        labels["c"] = "SOURCE FAMILY ONE";
        Check("X12 same source label with different case does not veto",
            ClusteringImprovementAudit.Gate(ids, edges, 85, labels, true).Membership.Values.Distinct().Count() == 1);
        Check("X12 posthoc policy actually removes a low-coverage fast join",
            ClusteringImprovementAudit.Gate(new[] { "a", "b" }, new[] { internalGaps }, 85,
                new Dictionary<string, string> { ["a"] = "", ["b"] = "" }, false).Membership.Values.Distinct().Count() == 2);
        var protein = new FastaItemExtended { SequenceIdentifier = "synthetic", Evidence = new ProteinEvidence { Reviewed = true, Family = "source family" } };
        Check("X12 veto uses a directly reviewed source family", ClusteringImprovementAudit.ReviewedFamily(protein) == "source family");
        protein.Evidence.Reviewed = false;
        Check("X12 unreviewed source label is not veto truth", ClusteringImprovementAudit.ReviewedFamily(protein).Length == 0);
        protein.Evidence.Reviewed = true;
        protein.Evidence.FamilyMemberships = new() { new() { Family = "source family" }, new() { Family = "other source family" } };
        Check("X12 multiple source memberships are not veto truth", ClusteringImprovementAudit.ReviewedFamily(protein).Length == 0);
        Check("X12 contaminant proposal uses exact prefix and case",
            ClusteringImprovementAudit.ProposedContaminant("contaminant_synthetic")
            && !ClusteringImprovementAudit.ProposedContaminant("Contaminant_synthetic")
            && !ClusteringImprovementAudit.ProposedContaminant("synthetic_contaminant_synthetic"));
        Check("X12 contaminant proposal unwraps the exact decoy prefix once",
            ClusteringImprovementAudit.ProposedContaminant("Reverse_contaminant_synthetic")
            && !ClusteringImprovementAudit.ProposedContaminant("reverse_contaminant_synthetic")
            && !ClusteringImprovementAudit.ProposedContaminant("Reverse_Reverse_contaminant_synthetic"));
        string folder = Path.Combine(workDir, "x12-diagnostics"); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "native.tsv");
        File.WriteAllText(path, "a full description\tb target description\t90\t100\t80\t100\t100\t100\t100\t80M20I\n");
        var parsed = ClusteringImprovementAudit.ReadJoins(path).Single();
        Check("X12 native userout reads pairs separately from columns and strips descriptions",
            parsed.Query == "a" && parsed.Target == "b" && parsed.Identity == 90 && parsed.Pairs == 80 && parsed.Columns == 100);
        File.WriteAllText(path, "a\tb\t90\t100\t101\t100\t100\t100\t100\t101M\n");
        bool refused = false;
        try { ClusteringImprovementAudit.ReadJoins(path); } catch (InvalidDataException) { refused = true; }
        Check("X12 refuses paired-letter coverage over one hundred percent", refused);
        File.WriteAllText(path, "a\tb\t90\n"); refused = false;
        try { ClusteringImprovementAudit.ReadJoins(path); } catch (InvalidDataException) { refused = true; }
        Check("X12 refuses a truncated native alignment", refused);
        Check("X12 UC pairs count M and ignore terminal gaps",
            ClusteringImprovementAudit.CigarLengths("3D40M20I40M7D", 90, 100) == (80, 100));
        Check("X12 UC omitted repeat means one alignment column", ClusteringImprovementAudit.CigarLengths("40MI40M", 80, 81) == (80, 81));
        Check("X12 UC exact sequence shorthand has full paired coverage", ClusteringImprovementAudit.CigarLengths("=", 80, 80) == (80, 80));
        Check("D15 filter requires the exact database prefix and case",
            !FamilyFilter.IsContaminant(new() { SequenceIdentifier = "Contaminant_synthetic" })
            && !FamilyFilter.IsContaminant(new() { SequenceIdentifier = "synthetic_contaminant_synthetic" }));
        foreach (var owner in new[] { typeof(EvidenceClassifier), typeof(EvidenceTaskDiscovery) })
        {
            var predicate = owner.GetMethod("IsContaminant", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
            Check("D15 " + owner.Name + " recognizes the exact wrapped database contaminant",
                (bool)predicate.Invoke(null, new object[] { new FastaItemExtended { SequenceIdentifier = "Reverse_contaminant_synthetic" } })!);
        }
        string caseInput = Path.Combine(folder, "case-sensitive-contaminant.fasta");
        File.WriteAllText(caseInput, ">Contaminant_synthetic\nMKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHRQ\n");
        var scan = FamilyClusterFile.ScanIdentities(caseInput, 100, 100, 1, exhaustive: false).Single();
        Check("D15 scoring denominator keeps a differently cased target label", scan.NonContaminantTargetGroups == 1);
        ClusteringDiagnosticReportTests(folder);
    }

    private static void ClusteringDiagnosticReportTests(string folder)
    {
        const string sequence = "MKAWLLVLGLVALSECQEEHRSPHIVATSKMTWDFYGDSLQIVFRHTKAAHRQ";
        string saved = Path.Combine(folder, "saved-diagnostic.fcf");
        var document = new FamilyClusterFile { ClusteringIdentity = 70, MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier = "synthetic-a", Sequence = sequence, UserID = 71, USearchID = 1 },
            new() { SequenceIdentifier = "synthetic-b", Sequence = sequence, UserID = 72, USearchID = 2 },
        } };
        document.Save(saved);
        string before = FileFingerprint.Sha256(saved);
        string token = Guid.NewGuid().ToString("N");
        var first = ClusteringImprovementAudit.WriteReport(saved, Path.Combine(folder, "report-a-" + token), 70);
        var second = ClusteringImprovementAudit.WriteReport(saved, Path.Combine(folder, "report-b-" + token), 70);
        Check("D14 product diagnostic generates its matching FASTA and readable report",
            File.Exists(Path.Combine(first.Folder, "input.fasta")) && File.Exists(first.TextReport)
            && File.ReadAllText(first.TextReport).Contains("These comparisons do not change your saved families.", StringComparison.Ordinal));
        Check("D14 generated-input report is byte-identical on two runs",
            Directory.GetFiles(first.Folder).All(path => FileFingerprint.Sha256(path)
                == FileFingerprint.Sha256(Path.Combine(second.Folder, Path.GetFileName(path)))));
        Check("D14 diagnostic preserves saved and in-memory family assignments",
            before == FileFingerprint.Sha256(saved) && document.MyExtendedFastaItems.Select(p => p.UserID).SequenceEqual(new[] { 71, 72 }));
        Check("D14 absent identified evidence is labelled unavailable", File.ReadAllText(first.TextReport).Contains("not available", StringComparison.Ordinal));
        bool refused = false;
        try { ClusteringImprovementAudit.WriteReport(saved, first.Folder, 70); } catch (IOException) { refused = true; }
        Check("D14 refuses to overwrite an existing report packet", refused);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        string cancelledFolder = Path.Combine(folder, "cancelled-" + token);
        var cancelledRun = CoreOperation.Run("clustering diagnostic", context =>
            ClusteringImprovementAudit.WriteReport("missing.fcf", cancelledFolder, 70, operation: context), cancelled.Token);
        Check("D14 cancellation is observed before file access or output creation",
            cancelledRun.Status == OperationStatus.Cancelled && !Directory.Exists(cancelledFolder));
        string large = Path.Combine(folder, "too-large-diagnostic.fcf");
        new FamilyClusterFile { MyExtendedFastaItems = Enumerable.Range(0, 1001).Select(i => new FastaItemExtended
            { SequenceIdentifier = "synthetic-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture), Sequence = sequence }).ToList() }.Save(large);
        refused = false;
        string largeOutput = Path.Combine(folder, "large-" + token);
        try { ClusteringImprovementAudit.WriteReport(large, largeOutput, 70, smallExhaustive: true); }
        catch (InvalidOperationException) { refused = true; }
        Check("D14 large all-pairs requests are refused before invoking native tools",
            refused && !Directory.EnumerateFileSystemEntries(largeOutput).Any());
        var replay = ClusteringImprovementAudit.ReplayReport(saved, Path.Combine(folder, "replay-" + token), 70, first.Folder);
        Check("D14 recorded native alignments reproduce every report byte",
            Directory.GetFiles(first.Folder).All(path => FileFingerprint.Sha256(path)
                == FileFingerprint.Sha256(Path.Combine(replay.Folder, Path.GetFileName(path)))));
        refused = false;
        try { ClusteringImprovementAudit.ReplayReport(saved, Path.Combine(folder, "wrong-identity-" + token), 71, first.Folder); }
        catch (InvalidDataException) { refused = true; }
        Check("D14 replay refuses a different identity", refused);
        refused = false;
        try { ClusteringImprovementAudit.ReplayReport(large, Path.Combine(folder, "wrong-document-" + token), 70, first.Folder); }
        catch (InvalidDataException) { refused = true; }
        Check("D14 replay refuses a different document and FASTA", refused);
        string invalidPacket = Path.Combine(folder, "wrong-executable-packet-" + token);
        Directory.CreateDirectory(invalidPacket);
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(first.SummaryFile))!;
        metadata["usearchSha256"] = new string('0', 64);
        File.WriteAllText(Path.Combine(invalidPacket, "summary.json"), metadata.ToJsonString());
        refused = false;
        try { ClusteringImprovementAudit.ReplayReport(saved, Path.Combine(folder, "wrong-executable-" + token), 70, invalidPacket); }
        catch (InvalidDataException) { refused = true; }
        Check("D14 replay refuses a different native executable", refused);
    }
}
