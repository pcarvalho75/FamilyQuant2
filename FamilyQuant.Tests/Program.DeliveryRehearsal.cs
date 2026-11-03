using DocumentFormat.OpenXml.Packaging;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using S = DocumentFormat.OpenXml.Spreadsheet;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeliveryRehearsalCommands() => new[]
    {
        new TestCommand("--delivery-rehearsal", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--delivery-rehearsal", (args, _) => RunDeliveryCommand(args)),
        new TestCommand("--test-delivery-rehearsal", 10, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--test-delivery-rehearsal", (args, _) =>
            {
                DeliveryRehearsalChecks(args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "fq-delivery-" + Guid.NewGuid().ToString("N")));
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            })
    };

    private static int RunDeliveryCommand(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException("Use --delivery-rehearsal <computed.fqproj> <staging-folder> [--panel <workbook.xlsx>] [--allow-nonempty] [--protected-root <folder>].");
        string? panel = null, protectedRoot = null; bool allow = false;
        for (int i = 3; i < args.Length; i++)
            if (args[i] == "--allow-nonempty") allow = true;
            else if (args[i] == "--panel" && i + 1 < args.Length) panel = args[++i];
            else if (args[i] == "--protected-root" && i + 1 < args.Length) protectedRoot = args[++i];
            else throw new ArgumentException("Unknown or incomplete delivery option: " + args[i]);
        return StageDelivery(args[1], args[2], panel, allow, protectedRoot).NotFound == 0 ? 0 : 1;
    }

    private static DeliveryResult StageDelivery(string input, string target, string? panel, bool allow, string? protectedRoot)
    {
        var stage = new DeliveryWorkspace(target, allow);
        protectedRoot ??= FindProtectedDeliveryAncestor(stage.Root);
        var before = ProtectedTree(protectedRoot, stage.Root);
        string? oldTemp = Environment.GetEnvironmentVariable("TEMP"), oldTmp = Environment.GetEnvironmentVariable("TMP");
        string temp = stage.PathFor(".work/temp"); Directory.CreateDirectory(temp);
        try
        {
            Environment.SetEnvironmentVariable("TEMP", temp); Environment.SetEnvironmentVariable("TMP", temp);
            var result = DeliveryRehearsal.Run(input, target, panel, true, NativeReportExport, AppVersionInfo.Current(),
                DateTimeOffset.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            var after = ProtectedTree(protectedRoot, stage.Root);
            if (!before.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase).SequenceEqual(after.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase)))
                throw new IOException("The protected source tree changed outside the staging target.");
            stage.AssertWritesInside();
            Console.WriteLine(result.Summary);
            Console.WriteLine("Protected source tree unchanged: " + (protectedRoot == null ? "not applicable" : protectedRoot));
            return result;
        }
        finally { Environment.SetEnvironmentVariable("TEMP", oldTemp); Environment.SetEnvironmentVariable("TMP", oldTmp); }
    }

    private static string? FindProtectedDeliveryAncestor(string target)
    {
        for (var directory = new DirectoryInfo(target); directory != null; directory = directory.Parent)
            if (directory.Name.Equals("AnaG", StringComparison.OrdinalIgnoreCase)) return directory.FullName;
        return null;
    }

    private static Dictionary<string, string> ProtectedTree(string? root, string excluded)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (root == null) return result;
        root = Path.GetFullPath(root); excluded = Path.TrimEndingDirectorySeparator(Path.GetFullPath(excluded));
        var pending = new Stack<string>(); pending.Push(root);
        while (pending.Count > 0)
            foreach (string entry in Directory.EnumerateFileSystemEntries(pending.Pop()))
            {
                string path = Path.GetFullPath(entry);
                if (path.Equals(excluded, StringComparison.OrdinalIgnoreCase) || path.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("Protected-tree checks do not follow links.");
                if (Directory.Exists(path)) { result[path] = "directory"; pending.Push(path); }
                else { var file = new FileInfo(path); result[path] = file.Length.ToString(CultureInfo.InvariantCulture) + ":" + file.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)
                    + ":" + DeliveryWorkspace.Sha256(path); }
            }
        return result;
    }

    private static void DeliveryRehearsalChecks(string folder)
    {
        Directory.CreateDirectory(folder);
        string inputFolder = Path.Combine(folder, "inputs"); Directory.CreateDirectory(inputFolder);
        string workbook = Path.Combine(inputFolder, "panel.xlsx");
        using (var book = SpreadsheetDocument.Create(workbook, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook))
        {
            var main = book.AddWorkbookPart(); main.Workbook = new S.Workbook();
            var part = main.AddNewPart<WorksheetPart>();
            S.Cell Cell(string value) => new() { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(value)) };
            part.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(Cell("Key"), Cell("Name"), Cell("Decision")),
                new S.Row(Cell("item"), Cell("Synthetic reference"), Cell("Include"))));
            main.Workbook.Append(new S.Sheets(new S.Sheet { Id = main.GetIdOfPart(part), SheetId = 1, Name = "Panel" }));
        }
        var reader = new PanelSpreadsheetReader(); var table = reader.ReadTable(workbook, "Panel", 1);
        var panel = new FamilyPanelSpreadsheetImporter().Import(table, new FamilyPanelImport { FamilySheet = "Panel", ColumnHeaders = new()
        { [PanelImportFields.FamilyKey] = "Key", [PanelImportFields.DisplayName] = "Name", [PanelImportFields.Decision] = "Decision" } },
            new[] { new FamilyPanelDecisionMapping { SourceValue = "Include", Decision = FamilyPanelDecision.Quantify } });
        var file = new FamilyClusterFile { FamilyPanel = panel, ReportDetails = new() { Title = "Synthetic delivery fixture", Workflow = "Stored fixture workflow", DatabaseKind = "Synthetic database" },
            MyExtendedFastaItems = new() { new() { SequenceIdentifier = "DELIVERY_TARGET", Sequence = "TTACDEFGHIKGG", Description = "Synthetic reference", UserID = 1, USearchID = 1 } } };
        file.PanelProteinAssignments = new() { new() { ProteinIndex = 0, Identifier = "DELIVERY_TARGET", FamilyUserId = 1, PanelFamilyKey = "item", IsApplied = true } };
        MakePanelAssignmentsCurrent(file, new Evidence.EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        string project = Path.Combine(inputFolder, "computed.fqproj");
        ProjectStore.WriteFqproj(project, file, new[] { Qr("identified", "fixture", 1, 2, "ACDEFGHIK", 100, 150) }, new QuantManifest { PerRunTic = new() { ["fixture"] = 100000 }, FamilyMapping = new() { MinIdentity = .99 } });
        string original = DeliveryWorkspace.Sha256(project), originalPanel = DeliveryWorkspace.Sha256(workbook);
        var boundary = new DeliveryWorkspace(Path.Combine(folder, "guards"), false);
        Check("delivery refuses parent traversal before writing", Throws<IOException>(() => boundary.WriteText("../escaped.txt", "bad")) && !File.Exists(Path.Combine(folder, "escaped.txt")));
        boundary.WriteText("kept.txt", "preserve");
        Check("delivery refuses a non-empty target by default", Throws<IOException>(() => new DeliveryWorkspace(boundary.Root, false)) && File.ReadAllText(boundary.PathFor("kept.txt")) == "preserve");
        Check("panel lookup refuses a different workbook hash", Throws<InvalidDataException>(() => DeliveryRehearsal.ResolvePanel(project, "panel.xlsx", new string('0', 64), workbook, Array.Empty<string>())));
        var result = StageDelivery(project, Path.Combine(folder, "staging"), null, false, inputFolder);
        Check("real delivery rehearsal trace has no unmatched tokens", result.NotFound == 0);
        Check("delivery contains project panel report workbook trace and read-me", result.Files.Count == 6 && result.Files.All(f => f.Bytes > 0 && f.Sha256.Length == 64));
        Check("all report workbook and panel XML parts are readable", result.Files.Where(f => f.File.EndsWith(".docx", StringComparison.Ordinal) || f.File.EndsWith(".xlsx", StringComparison.Ordinal)).All(f => f.XmlParts > 0));
        Check("delivery keeps both original inputs unchanged", original == DeliveryWorkspace.Sha256(project) && originalPanel == DeliveryWorkspace.Sha256(workbook));
        Check("staged saved project and panel match their original input bytes", result.Files.Single(f => f.File == "project.fqproj").Sha256 == original
            && result.Files.Single(f => f.File == "family-panel.xlsx").Sha256 == originalPanel);
        Check("recipient guidance records actual version and export path", File.ReadAllText(Path.Combine(result.Root, "READ-ME.txt")).Contains(AppVersionInfo.Current(), StringComparison.Ordinal)
            && File.ReadAllText(Path.Combine(result.Root, "READ-ME.txt")).Contains("File > Export report", StringComparison.Ordinal));
        Check("delivery command refuses its non-empty completed target without explicit override", Throws<IOException>(() => StageDelivery(project, result.Root, null, false, inputFolder)));
        var repeated = StageDelivery(project, result.Root, workbook, true, inputFolder);
        Check("explicit non-empty override reruns into a fresh private work folder", repeated.NotFound == 0 && repeated.Files.Single(f => f.File == "project.fqproj").Sha256 == original);
        var sourceBefore = ProtectedTree(inputFolder, result.Root);
        string changed = Path.Combine(inputFolder, "new-source-file.txt"); File.WriteAllText(changed, "changed");
        Check("protected-tree census detects files created outside the target", !sourceBefore.OrderBy(p => p.Key).SequenceEqual(ProtectedTree(inputFolder, result.Root).OrderBy(p => p.Key)));
        var keyBefore = ProtectedTree(inputFolder, result.Root);
        Directory.CreateDirectory(Path.Combine(inputFolder, "new-empty-folder"));
        Check("protected-tree census detects an empty directory outside the target", !keyBefore.OrderBy(p => p.Key).SequenceEqual(ProtectedTree(inputFolder, result.Root).OrderBy(p => p.Key)));
    }
}
