using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

internal static class CurationExportSnapshot
{
    internal const string Argument = "--curation-export-snapshot";
    internal sealed record Value(string Key, string Text);

    internal static int Run(string[] args)
    {
        if (args.Length != 2 || File.Exists(args[1])) throw new ArgumentException("Supply one fresh snapshot file.");
        var values = new List<Value>();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            foreach (string culture in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                var items = new List<FastaItemExtended>
                {
                    new() { SequenceIdentifier = "identifier,with\"quotes", Description = "line one\nline two\rthird",
                        UserAnnotation = "=1+2", Sequence = "PEPTIDEK", UserID = 1001, USearchID = 2, XICUser = 0.125, FullXicShare = 0.0625 },
                    new() { SequenceIdentifier = "plain", Description = "", UserAnnotation = "", Sequence = "PEPTIDER",
                        UserID = 2, USearchID = 1001, XICUser = 0, FullXicShare = null },
                    new() { SequenceIdentifier = "null-fields", Description = null!, UserAnnotation = null!, Sequence = "PEPTIDEA",
                        UserID = 2, USearchID = 1001, XICUser = 1, FullXicShare = 1 }
                };
                var modes = new NormalizationMode?[] { null }.Concat(Enum.GetValues<NormalizationMode>().Select(mode => (NormalizationMode?)mode));
                foreach (var mode in modes)
                foreach (string? heading in new string?[] { null, "Explicit, quoted \"basis\"" })
                    values.Add(new("csv/" + culture + "/" + (mode?.ToString() ?? "none") + "/" + (heading == null ? "default" : "explicit"),
                        CsvReport.Build(items, mode, heading)));
                foreach (string path in new[] { "USearchID", "UserID", "MonoisotopicMass", "XIC", "XICUser", "FullXicShare",
                    "UserAnnotation", "SequenceIdentifier", "Description", "Domains", "unknown", "" })
                foreach (bool descending in new[] { false, true })
                    values.Add(new("sort/" + culture + "/" + path + "/" + descending,
                        string.Join("|", ReportView.Sort(items, path, descending).Select(item => item.SequenceIdentifier))));
                values.Add(new("counter/" + culture, ReportView.FamilyCounter(1001, 2002)));
                var quantified = typeof(ReportView).GetMethods().Single(method => method.Name == "QuantifiedFamilies");
                // The removed selector's retained branch is explicitly User in the frozen API.
                object? count = quantified.Invoke(null, quantified.GetParameters().Length == 1 ? new object[] { items }
                    : new object[] { items, Enum.Parse(quantified.GetParameters()[1].ParameterType, "User") });
                values.Add(new("quantified/" + culture + "/User", Convert.ToInt32(count, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)));
            }
        }
        finally { CultureInfo.CurrentCulture = previous; }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
        File.WriteAllText(args[1], JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return 0;
    }
}
