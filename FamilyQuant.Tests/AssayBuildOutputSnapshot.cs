using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests;

// The same fixture and native calls execute against the frozen and current Core assemblies.
internal static class AssayBuildOutputSnapshot
{
    internal const string Argument = "--assay-build-output-snapshot";
    internal const string Sequence = "ACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWYACDEFGHIKLMNPQRSTVWY";
    internal static string Fasta => ">fixture_first original header\n" + Sequence +
        "\n>fixture_duplicate duplicate header\n" + Sequence +
        "\n>fixture_fragment fragment header\n" + Sequence[..30] + "\n";

    internal static string Members(FamilyClusterFile file) => string.Join("\n", file.MyExtendedFastaItems
        .Select(item => item.SequenceIdentifier + "\t" + item.UserID.ToString(CultureInfo.InvariantCulture)
            + "\t" + item.USearchID.ToString(CultureInfo.InvariantCulture) + "\t" + item.Sequence));

    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh fixture output folder.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        var oldCulture = CultureInfo.CurrentCulture;
        string? oldTool = Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", null);
            string input = Path.Combine(output, "fixture.fasta"), reduced = Path.Combine(output, "fixture_NO_REDUNDANCY.fasta");
            File.WriteAllText(input, Fasta, new UTF8Encoding(false));
            var cluster = USearchTools.GenerateFCF(new FileInfo(input), 85, exhaustive: true);
            var result = RedundancyTools.RemoveRedundancy(new FileInfo(input), 100, reduced);
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["build/members"] = Members(cluster),
                ["redundancy/before"] = result.before.ToString(CultureInfo.InvariantCulture),
                ["redundancy/after"] = result.after.ToString(CultureInfo.InvariantCulture),
                ["redundancy/fasta"] = File.ReadAllText(reduced)
            };
            File.WriteAllText(Path.Combine(output, "native-values.json"), JsonSerializer.Serialize(values,
                new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
            Console.WriteLine("PASS native build and redundancy fixture outputs");
            return 0;
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH", oldTool);
        }
    }
}
