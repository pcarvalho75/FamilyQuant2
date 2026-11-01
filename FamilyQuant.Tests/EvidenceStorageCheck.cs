using FamilyQuant.Evidence;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests
{
    // A storage checker only. Frozen annotations never enter an application or a delivered curation.
    internal static class EvidenceStorageCheck
    {
        internal const string Argument = "--evidence-storage-check";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                int flag = Array.IndexOf(args, Argument);
                string input = At(1, "D:/FamilyQuant-data/ana_source.fcf");
                string reference = At(2, "D:/FamilyQuant-data/baseline/viperidae_rich.tsv");
                string report = At(3, "D:/FamilyQuant-data/out-evidence/e1-storage.json");
                if (!string.Equals(Path.GetExtension(report), ".json", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The storage report must be a JSON file.");
                string beforeHash = Hash(input);
                FamilyClusterFile file = FamilyClusterFile.Load(input);
                string without = file.ToJsonText();
                long emptyBytes = Encoding.UTF8.GetByteCount(without);
                Dictionary<string, ProteinEvidence> evidence = ReadReference(reference);
                int populated = 0;
                foreach (FastaItemExtended item in file.MyExtendedFastaItems)
                {
                    string accession = FamilyNaming.Accession(item.SequenceIdentifier).Split('-')[0];
                    if (!evidence.TryGetValue(accession, out ProteinEvidence? entry)) continue;
                    item.Evidence = entry;
                    populated++;
                }
                file.EvidenceSource = new EvidenceProvenance
                {
                    Provider = "UniProt frozen reference (storage check only)",
                };
                string with = file.ToJsonText();
                long populatedBytes = Encoding.UTF8.GetByteCount(with);
                JsonNode stripped = JsonNode.Parse(with)!;
                stripped.AsObject().Remove("EvidenceSource");
                foreach (JsonNode? item in stripped["MyExtendedFastaItems"]!.AsArray())
                    item!.AsObject().Remove("Evidence");
                check("storage check adds annotations without changing curation values",
                    JsonNode.DeepEquals(JsonNode.Parse(without), stripped), null);
                check("storage check leaves Ana's source bytes unchanged", Hash(input) == beforeHash, beforeHash);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
                File.WriteAllText(report, JsonSerializer.Serialize(new
                {
                    method = "Storage-only fixture using the frozen reference fields; no curation file is written. Domain names are absent from that table, so this is a lower bound for a complete live fetch.",
                    sourceSha256 = beforeHash,
                    sourceFileBytes = new FileInfo(input).Length,
                    proteins = file.MyExtendedFastaItems.Count,
                    referenceRows = evidence.Count,
                    proteinsWithReferenceEvidence = populated,
                    serializedBytesWithoutEvidence = emptyBytes,
                    serializedBytesWithReferenceEvidence = populatedBytes,
                    evidenceGrowthBytes = populatedBytes - emptyBytes,
                    evidenceGrowthPercent = emptyBytes == 0 ? 0 : 100.0 * (populatedBytes - emptyBytes) / emptyBytes,
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(File.ReadAllText(report));

                string At(int offset, string fallback) => args.Length > flag + offset ? args[flag + offset] : fallback;
            }
            catch (Exception ex) { check("evidence storage measurement completed", false, ex.GetBaseException().Message); }
        }

        private static string Hash(string path)
        {
            using FileStream input = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(input));
        }

        internal static Dictionary<string, ProteinEvidence> ReadReference(string path)
        {
            using var reader = new TextFieldParser(path);
            reader.SetDelimiters("\t");
            reader.HasFieldsEnclosedInQuotes = true;
            reader.TrimWhiteSpace = false;
            string[] header = reader.ReadFields() ?? throw new InvalidDataException("No reference header.");
            var indices = header.Select((name, i) => (name, i)).ToDictionary(p => p.name, p => p.i, StringComparer.Ordinal);
            var entries = new Dictionary<string, ProteinEvidence>(StringComparer.Ordinal);
            while (!reader.EndOfData)
            {
                string[] row = reader.ReadFields()!;
                string Get(string name) => row[indices[name]];
                string families = Get("Protein families");
                string[] parts = families.Split(',').Select(p => p.Trim()).ToArray();
                string accession = Get("Entry");
                entries.Add(accession, new ProteinEvidence
                {
                    Accession = accession,
                    Reviewed = string.Equals(Get("Reviewed"), "reviewed", StringComparison.Ordinal),
                    Family = parts[0],
                    Subfamily = string.Join(", ", parts.Skip(1)),
                    FamilyAnnotation = families,
                    Keywords = Terms(Get("Keywords")),
                    TissueSpecificity = string.IsNullOrWhiteSpace(Get("Tissue specificity"))
                        ? new List<string>() : new List<string> { Get("Tissue specificity") },
                    Pfam = Terms(Get("Pfam")).Select(id => new EvidenceDomain { Accession = id }).ToList(),
                    Panther = Terms(Get("PANTHER")).Select(id => new EvidenceDomain { Accession = id }).ToList(),
                    TransmembraneCount = Regex.Matches(Get("Transmembrane"), @"\bTRANSMEM\b", RegexOptions.CultureInvariant).Count,
                    Fragment = Get("Fragment").Length > 0,
                });
            }
            return entries;
        }

        private static List<string> Terms(string value) => value.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
    }
}
