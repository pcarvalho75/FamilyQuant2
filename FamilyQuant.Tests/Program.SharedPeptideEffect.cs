using FamilyQuant.DeNovo;
using PatternTools.FastaTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> SharedEffectCommands() =>
    [new("--shared-peptide-effect", 10390, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--shared-peptide-effect", (args, _) => SharedPeptideEffect(args))];

    private static int SharedPeptideEffect(string[] args)
    {
        if (args.Length is not (9 or 10) || args.Length == 10 && args[9] != "--prepare-only")
            throw new ArgumentException("Supply copied curation project, database, trusted package, RAW folder, new output, cache folder, dataset label, date and optional --prepare-only.");
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        Console.WriteLine("Study process PID: " + Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (ScopeCsv("a,b\n\"line\nwith\"\"quote\",1.25\n", "scope") !=
            "\"dataset_scope\",a,b\n\"scope\",\"line\nwith\"\"quote\",1.25\n")
            throw new InvalidDataException("The scope column changed a quoted record or numerical bytes.");
        string project = Path.GetFullPath(args[1]), fasta = Path.GetFullPath(args[2]), package = Path.GetFullPath(args[3]);
        string rawDirectory = Path.GetFullPath(args[4]), output = Path.GetFullPath(args[5]), cache = Path.GetFullPath(args[6]);
        string label = args[7];
        if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("A dataset-scope label is required.");
        string studyRoot = Directory.GetParent(rawDirectory)!.FullName + Path.DirectorySeparatorChar;
        foreach (string path in new[] { project, fasta, package, output, cache })
            if (!path.StartsWith(studyRoot, StringComparison.OrdinalIgnoreCase)) throw new IOException("All study paths must be copies under the RAW folder's parent.");
        if (Directory.Exists(output)) throw new IOException("Use a fresh study output directory.");
        Directory.CreateDirectory(output); Directory.CreateDirectory(cache);
        string[] raws = Directory.GetFiles(rawDirectory, "*.raw");
        if (raws.Length != 1) throw new InvalidDataException("This measurement requires exactly one copied RAW.");
        string run = DeNovoReconciler.NormalizeRunName(raws[0]);
        Console.WriteLine(label);
        var (curation, _, _) = ProjectStore.ReadFqproj(project);
        var parser = new FastaFileParser { PreserveHead = true };
        using (var reader = File.OpenText(fasta)) parser.ParseFile(reader, false);
        string Key(string id, string sequence) => id + "\t" + sequence;
        var source = parser.MyItems.Select(item => Key(item.SequenceIdentifier, item.Sequence)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        var curated = curation.MyExtendedFastaItems.Select(item => Key(item.SequenceIdentifier, item.Sequence)).OrderBy(value => value, StringComparer.Ordinal).ToArray();
        bool sameDatabase = source.SequenceEqual(curated);
        var psms = SeproPsmAdapter.Load(package);
        var psmRuns = psms.GroupBy(psm => DeNovoReconciler.NormalizeRunName(psm.FileName), StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => new { run = group.Key, psms = group.Count() }).ToArray();
        int selectedPsms = psmRuns.Where(group => group.run.Equals(run, StringComparison.OrdinalIgnoreCase)).Sum(group => group.psms);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "input-check.json"), new { datasetLabel = label, run, sameDatabase,
            databaseEntries = source.Length, curationEntries = curated.Length, selectedPsms, packageRuns = psmRuns,
            databaseOnly = source.Except(curated, StringComparer.Ordinal).Take(8).ToArray(),
            curationOnly = curated.Except(source, StringComparer.Ordinal).Take(8).ToArray(),
            databaseSha256 = TestFileHash.Upper(fasta), packageSha256 = TestFileHash.Upper(package), curationProjectSha256 = TestFileHash.Upper(project) });
        Console.WriteLine("Exact database/curation identity and sequence match: " + sameDatabase + "; selected run PSMs: " + selectedPsms.ToString(CultureInfo.InvariantCulture));
        if (!sameDatabase) throw new InvalidDataException("The supplied project does not curate the supplied database. No family definition was guessed.");
        if (selectedPsms == 0) throw new InvalidDataException("The copied RAW has no matching package PSM run.");
        if (args.Length == 10) return 0;
        string packageHash = TestFileHash.Upper(package), fastaHash = TestFileHash.Upper(fasta);
        var options = new FullXicOptions { FamilyCuration = curation, ExportSpectrumComparisonCsv = false,
            AppVersion = typeof(ProjectStore).Assembly.GetName().Version?.ToString() ?? "" };
        FullXicComputeResult computed = FullXicComputer.ComputeFullXic(rawDirectory, package, null, options,
            (side, name) => Path.Combine(cache, side + "_" + name + ".json"), Console.WriteLine, new[] { run },
            name => Path.Combine(cache, "detected_" + name + ".json"));
        if (computed.Rows.Count == 0 || computed.Rows.Any(row => !row.Run.Equals(run, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("The computation did not produce only the selected run.");
        var attribution = FamilyEnvelopeQuant.BuildAttributionFromCuration(computed.Rows, curation);
        var table = FamilyEnvelopeQuant.BuildFromAttribution(computed.Rows, attribution);
        var peptides = FamilyEnvelopeQuant.PreparePeptides(computed.Rows);
        int identified = peptides.Count(peptide => peptide.Side == NativeQuantFractions.SideIdentified);
        double measured = table.MeasuredTotalEnvelopeReferenceCalibrated;
        var policies = Enum.GetValues<SharedPeptidePolicy>().Select(policy => new
        {
            policy = policy.ToString(), familySignalSum = table.Families.Sum(family => family.Signal(policy)),
            fractionOfMeasured = measured > 0 ? (double?)(table.Families.Sum(family => family.Signal(policy)) / measured) : null,
        }).ToArray();
        foreach (var policy in Enum.GetValues<SharedPeptidePolicy>())
            File.WriteAllText(Path.Combine(output, policy + ".csv"), ScopeCsv(CsvReport.BuildFamilyComposition(table, policy, measured,
                "fraction of quantified identified envelope; one run; no de novo input"), label), new UTF8Encoding(false));
        var summary = new { datasetLabel = label, run, databaseSha256 = fastaHash, packageSha256 = packageHash,
            curationProjectSha256 = TestFileHash.Upper(project), curationDefinition = "Stored UserID families of the exactly matching database project",
            rawQuantRows = computed.Rows.Count, identifiedRepresentatives = identified, deNovoRepresentatives = peptides.Count - identified,
            sharedIdentifiedRepresentatives = table.SharedIdentifiedPeptides,
            sharedRepresentativeFraction = identified > 0 ? (double?)table.SharedIdentifiedPeptides / identified : null,
            familiesReached = table.Families.Count, measuredIdentifiedEnvelope = table.MeasuredIdentifiedEnvelope,
            sharedIdentifiedEnvelope = table.SharedIdentifiedEnvelope, sharedSignalFraction = measured > 0 ? (double?)table.SharedShare : null,
            unattributedIdentifiedEnvelope = table.UnattributedIdentifiedEnvelope,
            countEveryFamilyExcessEnvelope = table.TotalEnvelope - table.MeasuredTotalEnvelope,
            countEveryFamilyExcessFraction = measured > 0 ? (double?)((table.TotalEnvelope - table.MeasuredTotalEnvelope) / measured) : null,
            policies, isotopeCount = options.IsotopeCount, minimumPoints = options.MinPointsForQuant, ppm = options.Ppm,
            ms1Tolerance = options.Ms1Tolerance, identityThreshold = table.IdentityThreshold,
            identifiedBestScoreOnly = FamilyEnvelopeQuant.IdentifiedBestScoreOnlyDefault };
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "shared-peptide-effect.json"), summary);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "family-table.json"), new { datasetLabel = label, table });
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "quant-rows.json"), new { datasetLabel = label, rows = computed.Rows });
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "provenance.json"), new { datasetLabel = label, executedAt = args[8],
            command = Environment.CommandLine, options = new { options.IsotopeCount, options.MinPointsForQuant, options.Ppm, options.Ms1Tolerance }, notes = computed.Notes });
        if (packageHash != TestFileHash.Upper(package) || fastaHash != TestFileHash.Upper(fasta))
            throw new InvalidDataException("A copied input changed during computation.");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(summary));
        return 0;
    }

    private static string ScopeCsv(string csv, string label)
    {
        var output = new StringBuilder(); bool start = true, header = true, quoted = false;
        foreach (char character in csv)
        {
            if (start) { output.Append('"').Append((header ? "dataset_scope" : label).Replace("\"", "\"\"", StringComparison.Ordinal)).Append("\","); start = false; header = false; }
            output.Append(character);
            if (character == '"') quoted = !quoted;
            if (character == '\n' && !quoted) start = true;
        }
        return output.ToString();
    }
}
