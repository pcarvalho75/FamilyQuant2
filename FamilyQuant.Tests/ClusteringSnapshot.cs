using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FamilyQuant.Tests
{
    // Shared with the frozen host so the producer and output schema are identical.
    internal static class ClusteringSnapshot
    {
        internal const string Argument = "--clustering-snapshot";
        internal const string ExportArgument = "--clustering-export-fcf";
        internal const string VerifyExportArgument = "--clustering-verify-fcf";

        internal static int Run(string[] args)
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 3 && (args[0] == ExportArgument || args[0] == VerifyExportArgument))
            {
                var document = FamilyClusterFile.Load(args[1]);
                if (args[0] == VerifyExportArgument)
                {
                    using var reader = new StreamReader(args[2], new UTF8Encoding(false, true));
                    foreach (var protein in document.MyExtendedFastaItems)
                    {
                        if (reader.ReadLine() != ">" + protein.SequenceIdentifier || reader.ReadLine() != protein.Sequence)
                            throw new InvalidDataException("The shared input differs from the stored protein order or sequences.");
                    }
                    if (reader.ReadLine() != null) throw new InvalidDataException("The shared input has extra records.");
                }
                else
                {
                    if (File.Exists(args[2])) throw new IOException("The shared clustering input already exists; it is never replaced.");
                    string temporary = args[2] + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)))
                        {
                            foreach (var protein in document.MyExtendedFastaItems)
                            {
                                if (string.IsNullOrWhiteSpace(protein.SequenceIdentifier) || string.IsNullOrWhiteSpace(protein.Sequence))
                                    throw new InvalidDataException("A stored protein has an empty identifier or sequence.");
                                writer.WriteLine(">" + protein.SequenceIdentifier);
                                writer.WriteLine(protein.Sequence);
                            }
                        }
                        File.Move(temporary, args[2]);
                    }
                    finally
                    {
                        if (File.Exists(temporary)) File.Delete(temporary);
                    }
                }
                var file = new FileInfo(args[2]);
                Console.WriteLine("Stored proteins=" + document.MyExtendedFastaItems.Count);
                Console.WriteLine("Bytes=" + file.Length);
                Console.WriteLine("SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.FullName))));
                return 0;
            }
            if (args.Length != 5 || args[0] != Argument)
                throw new ArgumentException("Supply input FASTA, output snapshot, integer identity and fast/exhaustive mode.");
            int identity = int.Parse(args[3], CultureInfo.InvariantCulture);
            bool exhaustive = args[4] == "exhaustive";
            if (!exhaustive && args[4] != "fast") throw new ArgumentException("Mode must be fast or exhaustive.");
            var clustered = USearchTools.GenerateFCF(new FileInfo(args[1]), identity, exhaustive);
            using (var writer = new StreamWriter(args[2], false, new UTF8Encoding(false)))
            {
                writer.WriteLine("identifier\tuser_id\tusearch_id\tsequence_sha256");
                foreach (var protein in clustered.MyExtendedFastaItems.OrderBy(p => p.SequenceIdentifier, StringComparer.Ordinal))
                {
                    if (protein.SequenceIdentifier.IndexOfAny(new[] { '\t', '\r', '\n' }) >= 0)
                        throw new InvalidDataException("An identifier cannot be written in the comparison table.");
                    writer.WriteLine(protein.SequenceIdentifier + "\t" + protein.UserID.ToString(CultureInfo.InvariantCulture) +
                        "\t" + protein.USearchID.ToString(CultureInfo.InvariantCulture) + "\t" +
                        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(protein.Sequence ?? string.Empty))));
                }
            }
            Console.WriteLine("Snapshot proteins=" + clustered.MyExtendedFastaItems.Count);
            Console.WriteLine("Snapshot families=" + clustered.GroupedFastaItems.Count);
            Console.WriteLine("Snapshot=" + args[2]);
            return 0;
        }
    }
}
