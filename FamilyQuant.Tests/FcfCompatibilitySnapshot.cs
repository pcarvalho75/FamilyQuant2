using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    // Linked into ReferenceGoldenHost so the exported FCF is opened by the frozen reader itself.
    internal static class FcfCompatibilitySnapshot
    {
        internal const string Argument = "--fcf-compatibility-snapshot";
        internal const string ExportArgument = "--export-fcf-compatibility";

        internal static int Run(string[] args)
        {
            bool export = args[0] == ExportArgument;
            if (args.Length != (export ? 4 : 3))
                throw new ArgumentException(export
                    ? "Supply source.fcf/fqproj, new exported.fcf and snapshot.json."
                    : "Supply exported.fcf and snapshot.json.");
            FamilyClusterFile document = Path.GetExtension(args[1]).Equals(".fqproj", StringComparison.OrdinalIgnoreCase)
                ? ProjectStore.ReadFqproj(args[1]).fcf : FamilyClusterFile.Load(args[1]);
            string output = args[export ? 3 : 2];
            if (File.Exists(output)) throw new IOException("The snapshot already exists: " + output);
            if (export)
            {
                if (File.Exists(args[2])) throw new IOException("The export already exists: " + args[2]);
                document.Save(args[2]);
                document = FamilyClusterFile.Load(args[2]);
            }
            var snapshot = new
            {
                document.ClusteringIdentity,
                document.WordSimilarity,
                document.IsKeywordSuperCluster,
                Locks = document.LockedUserIds.OrderBy(i => i).ToArray(),
                Proteins = document.MyExtendedFastaItems.Select((p, index) => new
                {
                    Index = index,
                    p.SequenceIdentifier,
                    p.Sequence,
                    p.Description,
                    p.UserID,
                    p.USearchID,
                    p.UserAnnotation,
                    p.XICUser,
                }).ToArray(),
            };
            File.WriteAllText(output, JsonSerializer.Serialize(snapshot), new UTF8Encoding(false));
            Console.WriteLine("FCF reader: " + typeof(FamilyClusterFile).Assembly.FullName);
            Console.WriteLine(document.MyExtendedFastaItems.Count.ToString(CultureInfo.InvariantCulture)
                + " proteins; " + document.MyExtendedFastaItems.Select(p => p.UserID).Distinct().Count()
                    .ToString(CultureInfo.InvariantCulture) + " families; " + output);
            return 0;
        }
    }
}
