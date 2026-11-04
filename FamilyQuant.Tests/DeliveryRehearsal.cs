using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal sealed record DeliveryFileCheck(string File, long Bytes, string Sha256, int XmlParts);
internal sealed record DeliveryResult(string Root, int NotFound, int Ambiguous, IReadOnlyList<DeliveryFileCheck> Files, string Summary);

/// <summary>Test-side packaging around the production File command. No result or report content is calculated here.</summary>
internal static class DeliveryRehearsal
{
    internal static DeliveryResult Run(string input, string target, string? panelOverride, bool allowNonempty,
        Func<string, string, int> export, string version, string date)
    {
        input = Path.GetFullPath(input);
        if (!File.Exists(input)) throw new FileNotFoundException("The computed project was not found.", input);
        var stage = new DeliveryWorkspace(target, allowNonempty);
        string sourceHash = DeliveryWorkspace.Sha256(input);
        string project = stage.Copy(input, "project.fqproj", allowNonempty);
        var saved = ProjectStore.ReadFqproj(project);
        if (saved.quant.Count == 0) throw new InvalidDataException("The project has no stored Results.");
        var provenance = saved.fcf.FamilyPanel?.Import ?? throw new InvalidDataException("The project has no recorded family-panel workbook.");
        string panel = ResolvePanel(input, provenance.SourceName, provenance.SourceSha256, panelOverride, saved.manifest.ScientificInputFingerprints.Keys);
        string panelHash = DeliveryWorkspace.Sha256(panel);
        stage.Copy(panel, "family-panel.xlsx", allowNonempty);
        string nativeRelative = ".work/export-" + Guid.NewGuid().ToString("N");
        string nativeFolder = stage.PathFor(nativeRelative);
        if (export(project, nativeFolder) != 0) throw new InvalidOperationException("The real report command failed; its work files are retained.");
        stage.Move(nativeRelative + "/report.docx", "report.docx", allowNonempty);
        stage.Move(nativeRelative + "/report_supplement.xlsx", "report_supplement.xlsx", allowNonempty);
        stage.Move(nativeRelative + "/report.html", "report.html", allowNonempty);
        string trace = stage.PathFor("trace.csv");
        int traceExit = ReportNumberTrace.Run(stage.PathFor("report.docx"), stage.PathFor(".work/trace-sources"), trace);
        stage.Writes.Add(trace);
        var traceRows = ReportCsvTable.Build("delivery-trace", "Trace", File.ReadAllText(trace), "");
        int status = traceRows.Columns.ToList().IndexOf("status");
        if (status < 0) throw new InvalidDataException("The trace has no status column.");
        int notFound = traceRows.Rows.Count(r => r[status] == "NOT FOUND");
        int ambiguous = traceRows.Rows.Count(r => r[status] == "AMBIGUOUS");
        if (traceExit != (notFound == 0 ? 0 : 1)) throw new InvalidDataException("The trace exit status disagrees with its rows.");
        stage.WriteText("READ-ME.txt", ReadMe(version, date, notFound, ambiguous));
        if (sourceHash != DeliveryWorkspace.Sha256(input) || sourceHash != DeliveryWorkspace.Sha256(project) || panelHash != DeliveryWorkspace.Sha256(panel))
            throw new InvalidDataException("A source project or panel changed during delivery rehearsal.");
        stage.AssertWritesInside();
        string[] names = { "project.fqproj", "family-panel.xlsx", "report.docx", "report_supplement.xlsx", "report.html", "trace.csv", "READ-ME.txt" };
        var files = names.Select(name =>
        {
            string path = stage.PathFor(name);
            long bytes = new FileInfo(path).Length;
            if (bytes == 0) throw new InvalidDataException("A delivery file is empty: " + name);
            int parts = name.EndsWith(".docx", StringComparison.Ordinal) || name.EndsWith(".xlsx", StringComparison.Ordinal)
                ? DeliveryWorkspace.ValidateXmlPackage(path) : 0;
            return new DeliveryFileCheck(name, bytes, DeliveryWorkspace.Sha256(path), parts);
        }).ToArray();
        var lines = new List<string>
        {
            "FamilyQuant delivery rehearsal", "Program version: " + version + "; date: " + date,
            "Staging folder: " + stage.Root, "Trace NOT FOUND: " + notFound.ToString(CultureInfo.InvariantCulture),
            "Trace AMBIGUOUS: " + ambiguous.ToString(CultureInfo.InvariantCulture),
            "Source project unchanged: yes", "Command-owned writes confined to staging: yes",
            "Program per-user cache/state folder: " + ApplicationPaths.LocalDataDirectory + " (normal program use, outside the delivery boundary)",
        };
        lines.AddRange(files.Select(f => f.File + " | " + f.Bytes.ToString(CultureInfo.InvariantCulture) + " bytes | SHA-256 " + f.Sha256
            + (f.XmlParts > 0 ? " | XML parts " + f.XmlParts.ToString(CultureInfo.InvariantCulture) : "")));
        var allFiles = Directory.EnumerateFiles(stage.Root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(stage.Root, path).Equals("delivery-checks.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).Select(path => new DeliveryFileCheck(Path.GetRelativePath(stage.Root, path),
                new FileInfo(path).Length, DeliveryWorkspace.Sha256(path), 0)).ToArray();
        lines.Add("All files checked, including command scratch: " + allFiles.Length.ToString(CultureInfo.InvariantCulture));
        string summary = string.Join(Environment.NewLine, lines);
        stage.WriteText("delivery-checks.json", JsonSerializer.Serialize(new { Version = version, Date = date, NotFound = notFound, Ambiguous = ambiguous,
            SourceProjectSha256 = sourceHash, SourceUnchanged = true, ProgramStateDirectory = ApplicationPaths.LocalDataDirectory,
            ProgramStateIsNormalPerUserUse = true, Files = files, AllFilesIncludingScratch = allFiles }, new JsonSerializerOptions { WriteIndented = true }));
        stage.AssertWritesInside();
        // The inventory cannot contain its own hash. Check it after writing and include that hash in console output.
        string inventory = stage.PathFor("delivery-checks.json");
        summary += Environment.NewLine + "delivery-checks.json | " + new FileInfo(inventory).Length.ToString(CultureInfo.InvariantCulture)
            + " bytes | SHA-256 " + DeliveryWorkspace.Sha256(inventory);
        return new(stage.Root, notFound, ambiguous, files, summary);
    }

    internal static string ResolvePanel(string project, string recordedName, string expectedHash, string? supplied,
        IEnumerable<string> recordedInputs)
    {
        if (expectedHash.Length != 64) throw new InvalidDataException("The project does not record a panel SHA-256.");
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(supplied)) candidates.Add(Path.GetFullPath(supplied));
        else
        {
            if (Path.IsPathRooted(recordedName)) candidates.Add(recordedName);
            else if (recordedName.Length > 0) candidates.Add(Path.Combine(Path.GetDirectoryName(project)!, recordedName));
            candidates.AddRange(recordedInputs.Where(Path.IsPathRooted).Where(p => Path.GetExtension(p).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)));
        }
        foreach (string path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            if (File.Exists(path) && DeliveryWorkspace.Sha256(path).Equals(expectedHash, StringComparison.OrdinalIgnoreCase)) return path;
        throw new InvalidDataException("The recorded panel workbook could not be located with its exact SHA-256. Supply --panel <workbook.xlsx>.");
    }

    private static string ReadMe(string version, string date, int notFound, int ambiguous) =>
        "FamilyQuant report files\nGenerated " + date + " with program version " + version + ".\n\n"
        + "report.docx is the report. report.html is its interactive graphical summary; open it in a browser, including offline. "
        + "report_supplement.xlsx holds its detailed tables. project.fqproj is the saved computed project. "
        + "family-panel.xlsx is the exact panel workbook recorded by that project. trace.csv records the report's numeric-token checks. "
        + "delivery-checks.json records file sizes and SHA-256 values. The .work folder retains the rehearsal's diagnostic files.\n\n"
        + "Open project.fqproj in FamilyQuant 2.1.0 using File > Open, then look at Results. "
        + "Help > What do I do first? describes the full path. To change the panel, use File > Import family panel, review its columns and decisions, "
        + "and apply the panel. Review the evidence grouping and refresh computed Results when the program asks. "
        + "Use File > Export report to choose sections and figures, change Report threshold (%) if wanted, and write new Word, HTML and supplement files together. "
        + "File > Save keeps changed panel choices, report details and threshold.\n\n"
        + "Trace NOT FOUND count: " + notFound.ToString(CultureInfo.InvariantCulture)
        + ". Trace AMBIGUOUS count: " + ambiguous.ToString(CultureInfo.InvariantCulture)
        + ". Ambiguous citations remain open for review. A numeric match is a checking aid, not a biological validation.\n";
}
