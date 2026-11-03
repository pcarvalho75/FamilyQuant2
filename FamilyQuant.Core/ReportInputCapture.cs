using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Report;

/// <summary>Reads provenance from chosen input files. Never infers missing search settings.</summary>
public static class ReportInputCapture
{
    public static ReportInputProvenance Read(string? databasePath, string seproPath, string? parameterPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seproPath);
        if (!File.Exists(seproPath)) throw new FileNotFoundException("The identification package could not be found.", seproPath);
        var facts = new ReportInputProvenance
        {
            DatabaseFileName = string.IsNullOrWhiteSpace(databasePath) ? "" : Path.GetFileName(databasePath),
            SeproFileName = Path.GetFileName(seproPath)
        };
        if (parameterPath == null)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(seproPath))!;
            string[] candidates = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.params").Take(2).ToArray() : Array.Empty<string>();
            if (candidates.Length != 1) return facts;
            parameterPath = candidates[0];
        }
        byte[] bytes = File.ReadAllBytes(parameterPath);
        var values = new List<ReportSearchParameter>();
        using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        while (reader.ReadLine() is string line)
        {
            int comment = line.IndexOf('#');
            if (comment >= 0) line = line[..comment];
            int equals = line.IndexOf('=');
            if (equals < 0) continue;
            string key = line[..equals].Trim();
            if (key.Length > 0) values.Add(new(key, line[(equals + 1)..].Trim()));
        }
        return facts with
        {
            SearchParameterFileName = Path.GetFileName(parameterPath),
            SearchParameterSha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            SearchParameters = values.AsReadOnly()
        };
    }

    public static string Describe(ReportInputProvenance? facts)
    {
        static string Available(string? value) => string.IsNullOrEmpty(value) ? "Not recorded" : value;
        var text = new StringBuilder();
        text.Append("Database file: ").AppendLine(Available(facts?.DatabaseFileName));
        text.Append("Identification package: ").AppendLine(Available(facts?.SeproFileName));
        text.Append("Search parameter file: ").AppendLine(Available(facts?.SearchParameterFileName));
        text.Append("Search parameter SHA-256: ").AppendLine(Available(facts?.SearchParameterSha256));
        text.AppendLine().AppendLine("Search settings:");
        if (facts?.SearchParameters == null || facts.SearchParameters.Count == 0) text.Append("Not recorded");
        else foreach (var value in facts.SearchParameters) text.Append(value.Key).Append(" = ").AppendLine(value.Value);
        return text.ToString();
    }
}
