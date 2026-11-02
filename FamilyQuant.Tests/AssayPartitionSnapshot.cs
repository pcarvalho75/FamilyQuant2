using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static class AssayPartitionSnapshot
{
    internal const string Argument = "--assay-partition-snapshot";
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("Supply a fresh output folder.");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        foreach (string culture in new[] { "en-US", "pt-BR" })
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            Dictionary<string, PartitionValue> tic = new(), pl = new(), dn = new();
            foreach (var fixture in new[] { (Name: "synthetic,\"quoted\" run", Tic: 1000d, Pl: 1.2345678901234567, Dn: double.Epsilon),
                (Name: "synthetic\nline", Tic: 100d, Pl: 80.125, Dn: 30.375), (Name: "zero numerators", Tic: 1d, Pl: 0d, Dn: 0d) })
            {
                tic[fixture.Name] = PartitionValue.Measured(fixture.Tic, "fixture \"TIC\"");
                pl[fixture.Name] = PartitionValue.Recorded(fixture.Pl, "fixture, primary");
                dn[fixture.Name] = PartitionValue.Measured(fixture.Dn, "fixture\nde novo");
            }
            var rows = ThreeWayPartition.Build(tic, pl, dn);
            ThreeWayPartition.WriteCsv(Path.Combine(output, culture + ".csv"), rows);
        }
        if (!File.ReadAllBytes(Path.Combine(output, "en-US.csv")).SequenceEqual(File.ReadAllBytes(Path.Combine(output, "pt-BR.csv"))))
            throw new InvalidDataException("Native partition CSV is culture dependent.");
        Console.WriteLine("PASS native partition CSV written in two cultures");
        return 0;
    }
}
