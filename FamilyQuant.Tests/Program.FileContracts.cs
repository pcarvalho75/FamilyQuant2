using FamilyQuant.DeNovo;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record FileContractFact(string Group, string Frozen, string Current, string FrozenSha256, string CurrentSha256, bool Same);

    private static int FileContractCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--negative-control")
            throw new ArgumentException("Supply native audit folder, reference app, fresh output, date and optional --negative-control.");
        string root = FindLedgerRoot()!, audit = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("File proof output must be fresh.");
        Directory.CreateDirectory(output);
        var legacy = Assembly.LoadFrom(Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll"));
        var values = new List<FileContractFact>();
        var priorCulture = CultureInfo.CurrentCulture;
        var original = Console.Out; using var log = new StringWriter(CultureInfo.InvariantCulture);
        try
        {
            Console.SetOut(new LedgerProofWriter(original, log));
            Check("file contracts use the exact frozen source build", legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                .EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true);
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                string folder = Path.Combine(output, culture); Directory.CreateDirectory(folder);
                var file = new FamilyClusterFile { LockedUserIds = new() { 7 }, MyExtendedFastaItems = new() {
                    new() { SequenceIdentifier = SequenceLabels.ContaminantPrefix + "P12345|NATIVE", Description = "Native, quoted \"entry\"", Sequence = "ACDEFGHIK", UserID = 7, USearchID = 2,
                        UserAnnotation = "Annotation, \"quoted\"\nsecond line", XICUser = .125 },
                    new() { SequenceIdentifier = "native-b", Description = "Second native entry", Sequence = "VVVVVVVVK", UserID = 8, USearchID = 3,
                        UserAnnotation = "Other annotation", XICUser = .875 } } };
                object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!;
                string beforeFcf = Path.Combine(folder, "frozen.fcf"), currentFcf = Path.Combine(folder, "current.fcf");
                SecondaryCall(oldFile, oldFile.GetType(), "Save", beforeFcf); file.Save(currentFcf);
                // X7 deliberately omits the retired XIC field on new FCF writes. Compare the
                // parsed retained document, then require the frozen reader to accept the new file.
                using (var oldJson = JsonDocument.Parse(File.ReadAllText(beforeFcf)))
                using (var currentJson = JsonDocument.Parse(File.ReadAllText(currentFcf)))
                {
                    var oldNode = System.Text.Json.Nodes.JsonNode.Parse(oldJson.RootElement.GetRawText())!;
                    foreach (var item in oldNode["MyExtendedFastaItems"]!.AsArray()) item!.AsObject().Remove("XIC");
                    Check("FCF retained persisted document agrees after authorized retired-XIC omission " + culture,
                        System.Text.Json.Nodes.JsonNode.DeepEquals(oldNode, System.Text.Json.Nodes.JsonNode.Parse(currentJson.RootElement.GetRawText())));
                }
                object reopened = legacy.GetType("FamilyQuant.FamilyClusterFile", true)!.GetMethod("Load", new[] { typeof(string) })!.Invoke(null, new object[] { currentFcf })!;
                Check("frozen FCF reader reopens current annotation-bearing export " + culture,
                    ((IEnumerable)SecondaryRead(reopened, "MyExtendedFastaItems")!).Cast<object>().Select(item => (string)SecondaryRead(item, "UserAnnotation")!)
                        .SequenceEqual(file.MyExtendedFastaItems.Select(item => item.UserAnnotation)));
                string oldCsv = (string)legacy.GetType("FamilyQuant.CsvReport", true)!.GetMethods().Single(method => method.Name == "Build" && method.GetParameters().Length == 1)
                    .Invoke(null, new[] { SecondaryRead(oldFile, "MyExtendedFastaItems") })!;
                WritePair("family-report", "FamilyQuant_Report.csv", oldCsv, CsvReport.Build(file.MyExtendedFastaItems));
                FileContractAnnotations(legacy, folder, file, oldFile, reopened, args.Length == 6);

                var partition = new List<ThreeWayPartitionRow> {
                    new() { RunName = "native, \"one\"", Tic = PartitionValue.Measured(1000.25, "native-tic"), PatternLab = PartitionValue.Recorded(400.5, "native-recorded"),
                        DeNovo = PartitionValue.Measured(100.125, "native-denovo"), Remainder = PartitionValue.Derived(499.625, "native-remainder"), DeNovoEnvelope = 125.75 },
                    new() { RunName = "native-two", Tic = PartitionValue.Measured(2000, "native-tic"), PatternLab = PartitionValue.Recorded(1500, "native-recorded"),
                        DeNovo = PartitionValue.Measured(700, "native-denovo"), Remainder = PartitionValue.Derived(-200, "native-remainder"), DeNovoEnvelope = 900 } };
                var oldPartitionType = legacy.GetType("FamilyQuant.DeNovo.ThreeWayPartition", true)!;
                var oldWrite = oldPartitionType.GetMethod("WriteCsv")!;
                Type oldRowType = oldWrite.GetParameters()[1].ParameterType.GetGenericArguments()[0];
                var oldPartition = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(oldRowType))!;
                foreach (var row in partition)
                {
                    object oldRow = Activator.CreateInstance(oldRowType)!;
                    oldRowType.GetProperty("RunName")!.SetValue(oldRow, row.RunName);
                    oldRowType.GetProperty("DeNovoEnvelope")!.SetValue(oldRow, row.DeNovoEnvelope);
                    foreach (string property in new[] { "Tic", "PatternLab", "DeNovo", "Remainder" })
                    {
                        var value = (PartitionValue)typeof(ThreeWayPartitionRow).GetProperty(property)!.GetValue(row)!;
                        Type oldValueType = oldRowType.GetProperty(property)!.PropertyType;
                        object oldValue = Activator.CreateInstance(oldValueType, value.Value,
                            Enum.Parse(legacy.GetType(typeof(PartitionProvenance).FullName!, true)!, value.Provenance.ToString()), value.Source)!;
                        oldRowType.GetProperty(property)!.SetValue(oldRow, oldValue);
                    }
                    oldPartition.Add(oldRow);
                }
                string oldPartitionPath = Path.Combine(folder, "frozen-three_way_partition.csv"), newPartitionPath = Path.Combine(folder, "current-three_way_partition.csv");
                oldWrite.Invoke(null, new object[] { oldPartitionPath, oldPartition }); ThreeWayPartition.WriteCsv(newPartitionPath, partition);
                Compare("partition-csv", oldPartitionPath, newPartitionPath);

                EnsureApplication();
                var oldControl = (System.Windows.Controls.UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.DeNovoControl", true)!)!;
                var control = new DeNovoControl();
                var homology = LedgerDeNovoHomology();
                object oldHomology = LedgerLegacyValue(homology, oldControl.GetType().GetMethod("ApplyHomology")!.GetParameters()[0].ParameterType)!;
                LedgerDeNovoAssignments(homology); LedgerDeNovoAssignments(oldHomology);
                oldControl.GetType().GetMethod("ApplyHomology")!.Invoke(oldControl, new[] { oldHomology }); control.ApplyHomology(homology);
                string oldHomologyPath = Path.Combine(folder, "frozen-FamilyQuant_DeNovo_Homology.csv"), newHomologyPath = Path.Combine(folder, "current-FamilyQuant_DeNovo_Homology.csv");
                oldControl.GetType().GetMethod("ExportHomologyCsv")!.Invoke(oldControl, new object[] { oldHomologyPath }); control.ExportHomologyCsv(newHomologyPath);
                Compare("denovo-homology-csv", oldHomologyPath, newHomologyPath);
                FileContractScreens(legacy, folder, partition, oldPartition, oldControl, homology, Compare);

                void WritePair(string group, string name, string before, string current)
                {
                    string oldPath = Path.Combine(folder, "frozen-" + name), newPath = Path.Combine(folder, "current-" + name);
                    File.WriteAllText(oldPath, before, new UTF8Encoding(false)); File.WriteAllText(newPath, current, new UTF8Encoding(false)); Compare(group, oldPath, newPath);
                }
            }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); CultureInfo.CurrentCulture = priorCulture; }
        File.WriteAllText(Path.Combine(output, "files.log"), log.ToString(), new UTF8Encoding(false));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "files.json"), values);
        FileContractRegister(root, audit, output, args[4], values, legacy);
        return failed == 0 ? 0 : 1;

        void Compare(string group, string before, string current)
        {
            if (args.Length == 6 && group is "partition-csv" or "partition-screen") File.AppendAllText(current, "negative-control\n", new UTF8Encoding(false));
            string oldHash = FeatureLedgerEvidence.Hash(before), newHash = FeatureLedgerEvidence.Hash(current);
            bool same = oldHash == newHash;
            Check("native frozen/current file bytes " + CultureInfo.CurrentCulture.Name + "/" + group, same);
            values.Add(new(group, Path.GetRelativePath(output, before), Path.GetRelativePath(output, current), oldHash, newHash, same));
        }
    }

    private static void FileContractRegister(string root, string audit, string output, string date, List<FileContractFact> files, Assembly legacy)
    {
        const string step = "FL-file-contracts";
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(audit, "report.json"))))
            if (report.RootElement.GetProperty("inventorySha256").GetString() != FeatureLedger.Fingerprint(inventory))
                throw new InvalidDataException("The supplied native audit uses another inventory.");
        var rows = JsonSerializer.Deserialize<List<SecondaryLedgerAuditRow>>(File.ReadAllText(Path.Combine(audit, "mapping.json")), FeatureLedgerEvidence.JsonOptions)!
            .Where(row => row.State == "OPEN" && row.Kind is "format/export" or "export/storage fact").ToArray();
        foreach (var row in rows)
        {
            var frozen = inventory.Observations.Single(item => item.Id == row.Id);
            if (frozen.Source != row.Source || frozen.Line != row.Line || frozen.Kind != row.Kind || frozen.Detail != row.Detail)
                throw new InvalidDataException("The native audit row differs from the inventory: " + row.Id);
        }
        // These are screen/format groups, with row IDs derived from the immutable inventory.
        // A successful writer is never offered as proof of prose or of an input file parser.
        var proved = rows.Where(row => row.Source == "FamilyQuant/DeNovo/ThreeWayWindow.cs"
            && row.Line is 123 or 124 or 126 || row.Source == "FamilyQuant/DeNovoControl.xaml.cs" && row.Line is 1112 or 1780
            || row.Source == "FamilyQuant/ClusterGenerationControl.xaml.cs" && row.Line == 1491
            || row.Source == "FamilyQuant/HelpContent.cs" && row.Line == 264
            || row.Source == "FamilyQuant/FamilyNaming.cs" && row.Line == 81).ToArray();
        var excluded = rows.Where(row => row.Source == "FamilyQuant/CsvReport.cs").ToArray();
        FileContractWording(output);
        string shared = "D:/FamilyQuant-data/reviews/wording-for-paulo.tsv";
        var sharedRows = new List<string[]>();
        using (var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(shared, Encoding.UTF8))
        {
            parser.SetDelimiters("\t"); parser.HasFieldsEnclosedInQuotes = true; parser.TrimWhiteSpace = false;
            _ = parser.ReadFields();
            while (!parser.EndOfData) sharedRows.Add(parser.ReadFields()!);
        }
        string[] wording = sharedRows.Select(fields => string.Join('\t', fields.Select(SecondaryWordingCell)))
            .Concat(File.ReadAllLines(Path.Combine(output, "wording-file-gaps.tsv")).Skip(1)).ToArray();
        File.WriteAllLines(Path.Combine(output, "cohort-wording.tsv"), new[] { SecondaryWordingHeader }.Concat(wording), new UTF8Encoding(false));
        var listed = wording.Select(line => line.Split('\t')).Where(fields => fields.Length == 5 && fields.Skip(2).All(value => value.Length > 0))
            .Select(fields => fields[1]).ToHashSet(StringComparer.Ordinal);
        var unresolved = rows.Except(proved).Except(excluded).OrderBy(row => row.Id, StringComparer.Ordinal)
            .Select(row => new { Row = row, ListedForPaulo = listed.Contains(row.Id), Reason = row.Kind == "export/storage fact"
                ? "Not proved as a file contract. The stored-value or explanatory claim needs its own executed branch; current wording is listed for review."
                : "Not proved by the paired output writers. Input-picker, status, help and other copy remain OPEN; current wording is listed for review." }).ToArray();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "target-audit.json"), new { Date = date, CandidateCount = rows.Length,
            Proved = proved, ExcludedPendingShellCsvBatch = excluded, Unresolved = unresolved,
            MissingWording = unresolved.Where(row => !row.ListedForPaulo).ToArray(),
            FileGroups = files.Select(file => file.Group).Distinct(StringComparer.Ordinal).ToArray(), RegisterPublished = true });
        if (unresolved.Any(row => !row.ListedForPaulo)) throw new InvalidDataException("An unresolved format row has no explicit wording/reason entry.");
        string log = Path.Combine(output, "files.log");
        var artifacts = new List<FeatureLedgerEvidence.Artifact>();
        void Retain(string path, string relative)
        {
            string destination = Path.Combine(output, "executed", relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination, false);
        }
        string[] implementations = { "FamilyQuant/DeNovo/ThreeWayWindow.cs", "FamilyQuant.Core/DeNovo/ThreeWayPartition.cs",
            "FamilyQuant/DeNovoControl.Compatibility.cs", "FamilyQuant/AnalyticsControl.xaml.cs", "FamilyQuant/AnalyticsViewModel.cs",
            "FamilyQuant.Core/HomologyCsv.cs", "FamilyQuant.Core/FamilyNaming.cs", "FamilyQuant.Core/SequenceLabels.cs",
            "FamilyQuant.Core/FamilyEnvelopeQuant.cs", "FamilyQuant.Core/FamilyQuantFile.cs", "FamilyQuant.Core/CsvReport.cs" };
        foreach (string path in implementations.Concat(new[] { "FamilyQuant.Tests/Program.FileContracts.cs", "FamilyQuant.Tests/Program.FileContractScreens.cs",
            "FamilyQuant.Tests/Program.Commands.SecondaryLedger.cs", "FamilyQuant.Tests/Program.SecondaryInjectedMethod.cs", "FamilyQuant.Tests/Program.SecondaryWording.cs",
            "FamilyQuant.Tests/Program.FileContractWording.cs", "FamilyQuant.Tests/Program.DeNovoLedgerWorkflow.cs",
            "FamilyQuant.Tests/Program.SecondaryRuntime.cs", "FamilyQuant.Tests/Program.LedgerPreservation.cs",
            "FamilyQuant.Tests/FeatureLedgerEvidence.cs", "FamilyQuant.Tests/FeatureLedgerEvidence.Reporting.cs",
            "FamilyQuant.Core/SequenceLabels.cs", "FamilyQuant.Core/FamilyNaming.cs", "FamilyQuant.Core/FamilyClusterScoring.cs",
            "FamilyQuant/USearch.Actions.cs", "FamilyQuant/USearch.Report.cs", "FamilyQuant/USearch.Presentation.cs" }).Distinct(StringComparer.Ordinal))
            Retain(Path.Combine(root, path), path);
        Retain(typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll"); Retain(typeof(AnalyticsControl).Assembly.Location, "FamilyQuant2.dll");
        Retain(typeof(ThreeWayPartition).Assembly.Location, "FamilyQuant.Core.dll"); Retain(legacy.Location, "frozen/FamilyQuant.dll");
        Retain(Path.Combine(audit, "mapping.json"), "audit/mapping.json"); Retain(Path.Combine(audit, "report.json"), "audit/report.json");
        foreach (string path in Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            artifacts.Add(new(path, FeatureLedgerEvidence.Hash(path)));
        var mappings = implementations.Select(path => new FeatureLedgerEvidence.Implementation(path,
            "Native file writers, partition/Results export routes, annotation quantitation/name invariance and persisted accession marker", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList();
        var evidence = proved.Select(row => new FeatureLedgerEvidence.Row(row.Id, mappings, new(),
            "Executed native file/storage checks in en-US and pt-BR; retained file bytes and actual values are archived.",
            row.Source.EndsWith("FamilyNaming.cs", StringComparison.Ordinal) ? "accession-storage"
                : row.Source is "FamilyQuant/ClusterGenerationControl.xaml.cs" or "FamilyQuant/HelpContent.cs" ? "annotation-format"
                : row.Source.Contains("ThreeWayWindow", StringComparison.Ordinal) ? "partition-format" : "homology-format",
            row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail
            + (row.Source is "FamilyQuant/ClusterGenerationControl.xaml.cs" or "FamilyQuant/HelpContent.cs" or "FamilyQuant/FamilyNaming.cs"
                ? "; native FCF/CSV persisted identifiers and quoted multiline annotations; frozen reopen; native family names and unchanged nonzero quantitation after annotation edits."
                : "; exact filename/filter" + (row.Line == 126 ? "/default extension" : "")
                    + "; preserved cancelled destination; accepted files byte-identical."))).ToList();
        var receipts = new[] { ("partition-format", "partition-screen"), ("homology-format", "homology-screen") }.Select(group =>
            new FeatureLedgerEvidence.KindReceipt(group.Item1, "format/export", new() {
                new("native", "test", nameof(FileContractCommand), "native " + group.Item2 + ": chooser filename/filter, cancelled destination and accepted file route agree en-US", log, ""),
                new("native", "test", nameof(FileContractCommand), "native " + group.Item2 + ": chooser filename/filter, cancelled destination and accepted file route agree pt-BR", log, ""),
                new("native", "test", nameof(FileContractCommand), "native frozen/current file bytes en-US/" + group.Item2, log, ""),
                new("native", "test", nameof(FileContractCommand), "native frozen/current file bytes pt-BR/" + group.Item2, log, "") })).ToList();
        foreach (var group in new[] { ("annotation-format", "format/export", "native annotation FCF/CSV storage, family naming and nonzero quantitation invariance "),
            ("accession-storage", "export/storage fact", "native accession marker survives file writes and frozen reopen ") })
            receipts.Add(new(group.Item1, group.Item2, new() {
                new("native", "test", nameof(FileContractCommand), group.Item3 + "en-US", log, ""),
                new("native", "test", nameof(FileContractCommand), group.Item3 + "pt-BR", log, "") }));
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD"); using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("Cannot record source commit.");
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit,
            new() { new("native", Environment.CommandLine, date, commit, artifacts, root) }, evidence, receipts));
        Console.WriteLine(FormattableString.Invariant($"File cohort: {rows.Length} target rows; {proved.Length} executed format proofs; {excluded.Length} pending Shell exclusions; {unresolved.Length} explicitly unproved/listed; 0 missing wording."));
    }

    private static void FileContractAnnotations(Assembly legacy, string folder, FamilyClusterFile file, object oldFile, object reopened, bool negative)
    {
        var oldItems = ((IEnumerable)SecondaryRead(oldFile, "MyExtendedFastaItems")!).Cast<object>().ToArray();
        string[] annotations = file.MyExtendedFastaItems.Select(item => item.UserAnnotation).ToArray();
        var quant = new List<QuantRow> { Qr("identified", "native-run", 1, 2, "ACDEFGHIK", 10, 30), Qr("identified", "native-run", 2, 2, "VVVVVVVVK", 6, 18) };
        var oldBuild = legacy.GetType("FamilyQuant.FamilyEnvelopeQuant", true)!.GetMethod("BuildFromCuration")!;
        object oldQuant = LedgerLegacyValue(quant, oldBuild.GetParameters()[0].ParameterType)!;
        object OldQuantitation() => oldBuild.Invoke(null, new object?[] { oldQuant, oldFile, null })!;
        object oldBefore = OldQuantitation(); var before = FamilyEnvelopeQuant.BuildFromCuration(quant, file);
        string oldNumbers = JsonSerializer.Serialize(oldBefore), numbers = JsonSerializer.Serialize(before);
        var oneOld = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(oldItems[0].GetType()))!; oneOld.Add(oldItems[0]);
        string oldName = (string)legacy.GetType("FamilyQuant.FamilyNaming", true)!.GetMethod("Name")!.Invoke(null, new object[] { oneOld })!;
        string name = FamilyNaming.Name(file.MyExtendedFastaItems.Take(1));
        foreach (var item in oldItems) item.GetType().GetProperty("UserAnnotation")!.SetValue(item, "");
        foreach (var item in file.MyExtendedFastaItems) item.UserAnnotation = "";
        string oldAfter = JsonSerializer.Serialize(OldQuantitation()), after = JsonSerializer.Serialize(FamilyEnvelopeQuant.BuildFromCuration(quant, file));
        string oldFallback = (string)legacy.GetType("FamilyQuant.FamilyNaming", true)!.GetMethod("Name")!.Invoke(null, new object[] { oneOld })!;
        string fallback = FamilyNaming.Name(file.MyExtendedFastaItems.Take(1));
        if (negative)
        {
            var altered = System.Text.Json.Nodes.JsonNode.Parse(after)!;
            altered["MeasuredTotalEnvelope"] = before.MeasuredTotalEnvelope + 1;
            after = altered.ToJsonString();
        }
        for (int index = 0; index < annotations.Length; index++)
        { oldItems[index].GetType().GetProperty("UserAnnotation")!.SetValue(oldItems[index], annotations[index]); file.MyExtendedFastaItems[index].UserAnnotation = annotations[index]; }
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(Path.Combine(folder, "current-FamilyQuant_Report.csv"), Encoding.UTF8);
        parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true; parser.TrimWhiteSpace = false;
        int column = Array.IndexOf(parser.ReadFields()!, "User Annotation"); var csvAnnotations = new List<string>();
        while (!parser.EndOfData) csvAnnotations.Add(parser.ReadFields()![column]);
        Check("native annotation FCF/CSV storage, family naming and nonzero quantitation invariance " + CultureInfo.CurrentCulture.Name,
            oldName == annotations[0] && name == annotations[0] && oldFallback == file.MyExtendedFastaItems[0].Description && fallback == oldFallback && fallback != name
            && FileContractNumbers(oldNumbers) == FileContractNumbers(oldAfter) && FileContractNumbers(numbers) == FileContractNumbers(after)
            && before.MeasuredTotalEnvelope > 0 && (double)SecondaryRead(oldBefore, "MeasuredTotalEnvelope")! > 0
            && csvAnnotations.SequenceEqual(annotations));
        string identifier = file.MyExtendedFastaItems[0].SequenceIdentifier;
        string storedIdentifier = (string)SecondaryRead(((IEnumerable)SecondaryRead(reopened, "MyExtendedFastaItems")!).Cast<object>().First(), "SequenceIdentifier")!;
        string oldAccession = (string)legacy.GetType("FamilyQuant.FamilyNaming", true)!.GetMethod("Accession")!.Invoke(null, new object[] { storedIdentifier })!;
        Check("native accession marker survives file writes and frozen reopen " + CultureInfo.CurrentCulture.Name,
            storedIdentifier == identifier && oldAccession == "P12345" && FamilyNaming.Accession(identifier) == oldAccession
            && File.ReadAllText(Path.Combine(folder, "current-FamilyQuant_Report.csv")).Contains(identifier, StringComparison.Ordinal));
        FeatureLedgerEvidence.WriteJson(Path.Combine(folder, "annotation-claims.json"), new { FrozenName = oldName, CurrentName = name,
            FrozenBefore = oldNumbers, FrozenAfter = oldAfter, CurrentBefore = numbers, CurrentAfter = after, CsvAnnotations = csvAnnotations,
            FrozenFallbackName = oldFallback, CurrentFallbackName = fallback, NegativeNumericCapture = negative,
            StoredIdentifier = storedIdentifier, FrozenAccession = oldAccession, CurrentAccession = FamilyNaming.Accession(identifier) });
    }

    // Every numeric leaf, including its full path and exact JSON representation. No calculated
    // field is named or omitted. Names are checked separately because annotation edits change them.
    private static string FileContractNumbers(string json)
    {
        using var document = JsonDocument.Parse(json);
        var numbers = new SortedDictionary<string, string>(StringComparer.Ordinal);
        void Visit(JsonElement value, string path)
        {
            if (value.ValueKind == JsonValueKind.Number) numbers.Add(path, value.GetRawText());
            else if (value.ValueKind == JsonValueKind.Object)
                foreach (var field in value.EnumerateObject()) Visit(field.Value, path + "." + field.Name);
            else if (value.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in value.EnumerateArray()) Visit(item, path + "[" + (index++).ToString(CultureInfo.InvariantCulture) + "]");
            }
        }
        Visit(document.RootElement, "$");
        if (numbers.Count == 0) throw new InvalidDataException("The native quantitation snapshot contains no numeric values.");
        return JsonSerializer.Serialize(numbers);
    }
}
