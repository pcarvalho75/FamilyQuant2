using FamilyQuant.DeNovo;
using Microsoft.Win32;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static readonly List<string> CurationFileMessages = new();
    private static MessageBoxResult CurationFileMessage(string text, string caption, MessageBoxButton buttons, MessageBoxImage image)
    { CurationFileMessages.Add(caption + ": " + text); return MessageBoxResult.OK; }

    private static int CurationFileCommand(string[] args)
    {
        if (args.Length is not (5 or 6) || args.Length == 6 && args[5] != "--negative-control") throw new ArgumentException("Supply native audit, frozen app, fresh output and date.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Fresh output required.");
        Directory.CreateDirectory(output); EnsureApplication();
        var legacy = Assembly.LoadFrom(Path.Combine(Path.GetFullPath(args[2]), "FamilyQuant.dll"));
        var original = Console.Out; var culture = CultureInfo.CurrentCulture; using var transcript = new StringWriter(CultureInfo.InvariantCulture);
        bool coreProved = false;
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            Check("curation files use exact frozen build", legacy.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.EndsWith("+" + FeatureLedger.ReferenceCommit, StringComparison.Ordinal) == true);
            try
            {
                foreach (string language in new[] { "en-US", "pt-BR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                    CurationFileScreen(legacy, output, args.Length == 6);
                }
                string[] csvs = Directory.GetFiles(Path.Combine(output, "en-US"), "*.csv");
                Check("native curation CSV bytes are invariant across cultures", csvs.Length > 0
                    && csvs.All(path => File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "pt-BR", Path.GetFileName(path))))));
                coreProved = CurationNoDecoy(legacy, output);
            }
            catch (Exception error) { Check("native curation file routes execute without an unexpected failure", false, error.ToString()); }
            Console.WriteLine(FormattableString.Invariant($"===== {passed} passed, {failed} failed, {skipped} skipped ====="));
        }
        finally { Console.SetOut(original); CultureInfo.CurrentCulture = culture; }
        File.WriteAllText(Path.Combine(output, "files.log"), transcript.ToString(), new UTF8Encoding(false));
        if (failed == 0) CurationFileRegister(root, Path.GetFullPath(args[1]), output, args[4], legacy, coreProved);
        return failed == 0 ? 0 : 1;
    }

    private static void CurationFileScreen(Assembly legacy, string output, bool negative)
    {
        string folder = Path.Combine(output, CultureInfo.CurrentCulture.Name); Directory.CreateDirectory(folder);
        var file = new FamilyClusterFile { MyExtendedFastaItems = new() {
            new() { SequenceIdentifier = "native-c1", Description = "Native, quoted \"first\"", Sequence = "ACDEFGHIK", UserID = 11, USearchID = 11, UserAnnotation = "One, \"quoted\"", XICUser = .125 },
            new() { SequenceIdentifier = "native-c2", Description = "Native second", Sequence = "VVVVVVVVK", UserID = 22, USearchID = 22, UserAnnotation = "Two", XICUser = .875 } } };
        object oldFile = SecondaryCall(null, legacy.GetType("FamilyQuant.FamilyClusterFile", true)!, "FromJsonText", file.ToJsonText())!;
        var old = (UserControl)Activator.CreateInstance(legacy.GetType("FamilyQuant.ClusterGenerationControl", true)!, oldFile)!;
        var current = new ClusterGenerationControl(file);
        var oldHost = SecondaryHost(new Window { Content = old, Width = 1366, Height = 820 });
        var host = SecondaryHost(new Window { Content = current, Width = 1366, Height = 820 });
        var presenter = ShellDialogs.FilePresenter; var messages = ShellMessages.Presenter; var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var trace = new List<SecondaryInjection>(); var values = new List<object>();
        try
        {
            ShellMessages.Presenter = (_, _) => MessageBoxResult.OK;
            MethodInfo chooser = typeof(CommonDialog).GetMethod("ShowDialog", Type.EmptyTypes)!;
            MethodInfo message = typeof(MessageBox).GetMethod("Show", new[] { typeof(string), typeof(string), typeof(MessageBoxButton), typeof(MessageBoxImage) })!;
            foreach (string group in new[] { "protein", "composition" })
            {
                if (group == "composition")
                {
                    var quant = new[] { Qr("identified", "native-run", 1, 2, "ACDEFGHIK", 10, 30), Qr("identified", "native-run", 2, 2, "VVVVVVVVK", 6, 18) };
                    var set = old.GetType().GetMethod("SetFullXicQuant")!;
                    set.Invoke(old, new[] { LedgerLegacyValue(quant, set.GetParameters()[0].ParameterType), null });
                    current.SetFullXicQuant(quant, null); PumpDispatcher();
                }
                string handler = group == "protein" ? "ButtonSaveReport_Click" : "ButtonExportComposition_Click";
                string button = group == "protein" ? "ButtonSaveReport" : "ButtonExportComposition";
                var method = old.GetType().GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!;
                var replacements = new Dictionary<MethodBase, MethodInfo> { [chooser] = SecondaryBoundary(nameof(SecondaryFileBoundary)), [message] = SecondaryBoundary(nameof(CurationFileMessage)) };
                int messageCalls = group == "protein" ? 3 : 2;
                bool wrongCountRejected = false;
                try { SecondaryInject(method, replacements, new(), new Dictionary<MethodBase, int> { [chooser] = 1, [message] = messageCalls - 1 }); }
                catch (InvalidDataException) { wrongCountRejected = true; }
                Check("native curation rejects wrong message-call count " + group + " " + CultureInfo.CurrentCulture.Name, wrongCountRejected);
                var injected = SecondaryInject(method, replacements, trace, new Dictionary<MethodBase, int> { [chooser] = 1, [message] = messageCalls });
                var configurations = new List<string>(); bool contract = true;
                foreach (bool frozen in new[] { true, false })
                foreach (bool accept in new[] { false, true })
                {
                    string path = Path.Combine(folder, (frozen ? "frozen-" : "current-") + group + ".csv");
                    const string marker = "Destination remains intact on Cancel."; File.WriteAllText(path, marker);
                    string configuration = ""; int shown = 0; CurationFileMessages.Clear();
                    bool? Choose(CommonDialog selected)
                    {
                        var dialog = (FileDialog)selected; shown++;
                        configuration = JsonSerializer.Serialize(new { dialog.Filter, Name = Path.GetFileName(dialog.FileName) });
                        dialog.FileName = path; return accept;
                    }
                    secondaryFrozenChooser = Choose;
                    ShellDialogs.FilePresenter = (_, id, dialog) => id == "curation." + (group == "protein" ? "proteins-csv" : "composition-csv")
                        ? Choose(dialog) : throw new InvalidOperationException("Unexpected curation chooser: " + id);
                    if (frozen) injected.Invoke(null, new object[] { old, old.FindName(button), new RoutedEventArgs() });
                    else { SecondaryClick(UiField<Button>(current, button)!); FinishBusyOperation(current.Operations); PumpDispatcher(); }
                    bool sameMarker = File.ReadAllText(path) == marker;
                    contract &= shown == 1 && (accept ? !sameMarker : sameMarker) && (!frozen || CurationFileMessages.Count == (accept ? 1 : 0));
                    configurations.Add(configuration);
                    values.Add(new { Culture = CultureInfo.CurrentCulture.Name, Group = group, Frozen = frozen, Accepted = accept, Configuration = configuration,
                        DestinationPreserved = sameMarker, Messages = CurationFileMessages.Select(text => text.Replace(output, "<output>", StringComparison.Ordinal)).ToArray() });
                }
                bool safe = HasProofWindowPlacement(oldHost) && HasProofWindowPlacement(host) && !oldHost.IsActive && !host.IsActive;
                Check("native curation " + group + " chooser/filter/name and cancel/accept route " + CultureInfo.CurrentCulture.Name,
                    contract && safe && configurations.Count == 4 && configurations.Distinct(StringComparer.Ordinal).Count() == 1);
                string before = Path.Combine(folder, "frozen-" + group + ".csv"), after = Path.Combine(folder, "current-" + group + ".csv");
                if (negative && group == "composition") File.AppendAllText(after, "negative-control\n");
                Check("native curation " + group + " file bytes " + CultureInfo.CurrentCulture.Name, File.ReadAllBytes(before).SequenceEqual(File.ReadAllBytes(after)));
            }
            FeatureLedgerEvidence.WriteJson(Path.Combine(folder, "dialogs.json"), values);
            FeatureLedgerEvidence.WriteJson(Path.Combine(folder, "injections.json"), trace);
        }
        finally { secondaryFrozenChooser = null; ShellDialogs.FilePresenter = presenter; ShellMessages.Presenter = messages; SynchronizationContext.SetSynchronizationContext(context); oldHost.Close(); host.Close(); }
    }

    private static bool CurationNoDecoy(Assembly legacy, string output)
    {
        string folder = Path.Combine(output, "no-decoy"); Directory.CreateDirectory(folder);
        string input = Path.Combine(Path.GetDirectoryName(output)!, "native-no-decoy-input.fasta");
        const string fasta = ">native-one generic input\nACDEFGHIK\n>native-two generic input\nVVVVVVVVK\n";
        if (File.Exists(input) && File.ReadAllText(input) != fasta) throw new IOException("The owned no-decoy input has different contents.");
        File.WriteAllText(input, fasta, new UTF8Encoding(false));
        string beforeInput = FeatureLedgerEvidence.Hash(input);
        string? saved = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
        var messages = new List<string>(); var binaryHashes = new List<string>();
        try
        {
            foreach (bool frozen in new[] { true, false })
            {
                var assembly = frozen ? legacy : typeof(USearchTools).Assembly;
                var resources = assembly.GetType("FamilyQuant.MyResources");
                var payloadProperty = resources?.GetProperty("usearch11_0_667_win32", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (payloadProperty == null)
                { skipped++; Console.WriteLine("  SKIP  no-decoy format group: frozen/current bundled engine resource is not available."); return false; }
                string role = frozen ? "frozen" : "current", binary = Path.Combine(folder, role + "-usearch.exe");
                File.WriteAllBytes(binary, (byte[])payloadProperty.GetValue(null)!); binaryHashes.Add(FeatureLedgerEvidence.Hash(binary));
                Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", binary);
                object generated = frozen ? legacy.GetType("FamilyQuant.USearchTools", true)!.GetMethod("GenerateFCF")!
                    .Invoke(null, new object[] { new FileInfo(input), 100, false })! : USearchTools.GenerateFCF(new FileInfo(input), 100, false);
                string file = Path.Combine(folder, role + ".fcf");
                if (frozen) SecondaryCall(generated, generated.GetType(), "Save", file); else ((FamilyClusterFile)generated).Save(file);
                try
                {
                    if (frozen) legacy.GetType("FamilyQuant.FamilyClusterFile", true)!.GetMethod("DivideAndConquerDetailed")!
                        .Invoke(null, new object[] { input, 0d, SequenceLabels.DecoyPrefix, false });
                    else FamilyClusterFile.DivideAndConquerDetailed(input, 0, SequenceLabels.DecoyPrefix, false);
                    messages.Add("");
                }
                catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { messages.Add(error.InnerException.Message); }
                catch (InvalidOperationException error) { messages.Add(error.Message); }
            }
        }
        finally { Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", saved); }
        var left = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "frozen.fcf")))!;
        var right = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "current.fcf")))!;
        foreach (var item in left["MyExtendedFastaItems"]!.AsArray()) item!.AsObject().Remove("XIC");
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string clause = JsonSerializer.Deserialize<string>(inventory.Observations.Single(row => row.Source == "FamilyQuant/FamilyQuantFile.cs" && row.Line == 682).Detail)!;
        bool same = messages.Count == 2 && messages[0] == messages[1] && messages[0].Contains(clause, StringComparison.Ordinal)
            && System.Text.Json.Nodes.JsonNode.DeepEquals(left, right) && beforeInput == FeatureLedgerEvidence.Hash(input);
        Check("native no-decoy engine files and refusal agree", same);
        FeatureLedgerEvidence.WriteJson(Path.Combine(folder, "values.json"), new { Messages = messages, BinaryHashes = binaryHashes, InputSha256 = beforeInput, RetainedFcfEqual = System.Text.Json.Nodes.JsonNode.DeepEquals(left, right) });
        return same;
    }

    private static void CurationFileRegister(string root, string audit, string output, string date, Assembly legacy, bool coreProved)
    {
        const string step = "FL-curation-file-contracts";
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        using (var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(audit, "report.json"))))
            if (report.RootElement.GetProperty("inventorySha256").GetString() != FeatureLedger.Fingerprint(inventory)) throw new InvalidDataException("Wrong audit inventory.");
        var audited = JsonSerializer.Deserialize<List<SecondaryLedgerAuditRow>>(File.ReadAllText(Path.Combine(audit, "mapping.json")), FeatureLedgerEvidence.JsonOptions)!;
        var selected = inventory.Observations.Where(row => row.Source == "FamilyQuant/ClusterGenerationControl.xaml.cs" && row.Line is 642 or 643 or 1048 or 1049
            || coreProved && row.Source == "FamilyQuant/FamilyQuantFile.cs" && row.Line == 682).ToArray();
        foreach (var row in selected)
            if (!audited.Any(item => item.Id == row.Id && item.State == "OPEN" && item.Source == row.Source && item.Kind == row.Kind && item.Detail == row.Detail && item.Line == row.Line))
                throw new InvalidDataException("The format row is not in the supplied native OPEN cohort: " + row.Id);
        var mappings = new[] { "FamilyQuant/ClusterGenerationControl.xaml.cs", "FamilyQuant/CurationQuantitationViewModel.cs", "FamilyQuant.Core/CsvReport.cs",
            "FamilyQuant.Core/USearchTools.cs", "FamilyQuant.Core/FamilyClusterScoring.cs", "FamilyQuant.Core/FamilyQuantFile.cs" }.Select(path => new FeatureLedgerEvidence.Implementation(path,
                "Native curation CSV chooser and writer paths; native no-decoy refusal when executed", FeatureLedgerEvidence.Hash(Path.Combine(root, path)))).ToList();
        var receipts = new List<FeatureLedgerEvidence.KindReceipt>();
        foreach (string group in new[] { "protein", "composition" })
            receipts.Add(new(group, "format/export", new[] { "en-US", "pt-BR" }.SelectMany(language => new[] {
                new FeatureLedgerEvidence.Proof("native", "test", nameof(CurationFileCommand), "native curation " + group + " chooser/filter/name and cancel/accept route " + language, Path.Combine(output, "files.log"), ""),
                new FeatureLedgerEvidence.Proof("native", "test", nameof(CurationFileCommand), "native curation " + group + " file bytes " + language, Path.Combine(output, "files.log"), "") }).ToList()));
        if (coreProved) receipts.Add(new("no-decoy", selected.Single(row => row.Source == "FamilyQuant/FamilyQuantFile.cs").Kind,
            new() { new("native", "test", nameof(CurationFileCommand), "native no-decoy engine files and refusal agree", Path.Combine(output, "files.log"), "") }));
        var rows = selected.Select(row => new FeatureLedgerEvidence.Row(row.Id, mappings, new(), "Native attached CSV screen route and actual frozen/current files in two cultures.",
            row.Source == "FamilyQuant/FamilyQuantFile.cs" ? "no-decoy" : row.Line is 642 or 643 ? "protein" : "composition",
            row.Source + ":" + row.Line.ToString(CultureInfo.InvariantCulture) + " " + row.Detail
                + (row.Source == "FamilyQuant/FamilyQuantFile.cs" ? "; actual frozen/current engine FCF writes, retained data equality and exact target-only rejection message."
                    : "; native exact filter/default filename, cancelled destination preserved, accepted CSV bytes equal."))).ToList();
        string ledgerSnapshot = Path.Combine(output, "cohort-ledger.md"); File.Copy(Path.Combine(root, "docs/FEATURE-LEDGER.md"), ledgerSnapshot);
        var currentStates = File.ReadAllLines(ledgerSnapshot).Where(line => line.StartsWith("| FQ15-", StringComparison.Ordinal))
            .Select(line => line.Split('|').Select(cell => cell.Trim()).ToArray()).ToDictionary(cells => cells[1], cells => cells[7], StringComparer.Ordinal);
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "target-audit.json"), new { Proved = selected,
            Remaining = audited.Where(row => currentStates.GetValueOrDefault(row.Id) == "OPEN" && row.Kind is "format/export" or "export/storage fact" && selected.All(item => item.Id != row.Id))
                .Select(row => new { Row = row, Reason = "Not executed by this curation/engine packet; retains the earlier OPEN wording/reason decision entry." }).ToArray() });
        File.Copy(Path.Combine(audit, "mapping.json"), Path.Combine(output, "cohort-mapping.json"));
        File.Copy(Path.Combine(audit, "report.json"), Path.Combine(output, "cohort-report.json"));
        string[] sources = mappings.Select(item => item.Path).Concat(new[] { "FamilyQuant.Tests/Program.CurationFileContracts.cs", "FamilyQuant.Tests/Program.SecondaryInjectedMethod.cs",
            "FamilyQuant.Tests/Program.Commands.SecondaryLedger.cs" }).ToArray();
        foreach (string source in sources)
        { string target = Path.Combine(output, "executed", source); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(Path.Combine(root, source), target); }
        foreach (var item in new[] { (typeof(Program).Assembly.Location, "FamilyQuant.Tests.dll"), (typeof(ClusterGenerationControl).Assembly.Location, "FamilyQuant2.dll"),
            (typeof(FamilyClusterFile).Assembly.Location, "FamilyQuant.Core.dll"), (legacy.Location, "frozen-FamilyQuant.dll") }) File.Copy(item.Item1, Path.Combine(output, "executed", item.Item2));
        var artifacts = Directory.GetFiles(output, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList();
        var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
        start.ArgumentList.Add("rev-parse"); start.ArgumentList.Add("HEAD"); using var process = Process.Start(start)!;
        string commit = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); if (process.ExitCode != 0) throw new IOException("Cannot record source commit.");
        var register = new FeatureLedgerEvidence.Register(1, step, FeatureLedger.ReferenceCommit,
            new() { new("native", Environment.CommandLine, date, commit, artifacts, root) }, rows, receipts);
        var validation = FeatureLedgerEvidence.Load(root, inventory, new[] { register });
        if (validation.FatalProblems.Count > 0 || validation.Entries.Values.Any(entry => !entry.Valid))
            throw new InvalidDataException("Curation register failed native receipt validation: " + string.Join("; ", validation.FatalProblems.Concat(validation.Entries.Values.SelectMany(entry => entry.Problems))));
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, step + ".json"), register);
        Console.WriteLine(FormattableString.Invariant($"Native curation receipt validation passed for {rows.Count} rows; ledger activation is left to the desk."));
    }
}
