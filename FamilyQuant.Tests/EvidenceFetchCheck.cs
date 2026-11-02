using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace FamilyQuant.Tests
{
    internal static class EvidenceFetchCheck
    {
        internal const string Argument = "--evidence-fetch-check";
        internal const string VerifyArgument = "--evidence-fetch-verify";

        internal static void Run(string[] args, Action<string, bool, string?> check)
        {
            try
            {
                bool verify = args.Contains(VerifyArgument);
                int flag = Array.IndexOf(args, verify ? VerifyArgument : Argument);
                string input = At(1, "D:/FamilyQuant-data/ana_source.fcf");
                string referencePath = At(2, "D:/FamilyQuant-data/baseline/viperidae_rich.tsv");
                string output = At(3, "D:/FamilyQuant-data/out-evidence/ana-evidence.fcf");
                if (string.Equals(Path.GetFullPath(input), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Path.GetExtension(output), ".fcf", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("The native evidence copy must be a separate FCF file.");
                string inputHash = FileHash(input);
                FamilyClusterFile file = FamilyClusterFile.Load(input);
                FamilyClusterFile original = file;
                string originalCuration = CurationHash(file);
                Dictionary<string, ProteinEvidence> reference = EvidenceStorageCheck.ReadReference(referencePath);
                var referenceExpected = file.MyExtendedFastaItems.Where(i => reference.ContainsKey(FamilyNaming.Accession(i.SequenceIdentifier).Split('-')[0]))
                    .Select(i => i.SequenceIdentifier).ToHashSet(StringComparer.Ordinal);
                (int Asked, int Found, EvidenceProvenance Provenance) result;
                if (verify)
                {
                    file = FamilyClusterFile.Load(output);
                    EvidenceProvenance provenance = file.EvidenceSource ?? throw new InvalidDataException("The evidence copy has no source.");
                    if (file.MyExtendedFastaItems.Count != original.MyExtendedFastaItems.Count)
                        throw new InvalidDataException("The evidence copy changed the protein count.");
                    var requested = ProteinDomains.Batches(original.MyExtendedFastaItems).SelectMany(b => b).ToHashSet(StringComparer.Ordinal);
                    int found = original.MyExtendedFastaItems.Zip(file.MyExtendedFastaItems)
                        .Where(p => p.Second.Evidence?.Source is { } source && source.Provider == provenance.Provider
                            && source.Release == provenance.Release && source.RetrievedAtUtc == provenance.RetrievedAtUtc)
                        .Select(p => FamilyNaming.Accession(p.First.SequenceIdentifier).Split('-')[0])
                        .Where(requested.Contains).Distinct(StringComparer.Ordinal).Count();
                    result = (requested.Count, found, provenance);
                }
                else
                {
                    EvidenceFetchResult fetched = EvidenceFetcher.FetchAsync(file, progress: new ConsoleProgress()).GetAwaiter().GetResult();
                    result = (fetched.Asked, fetched.Found, fetched.Provenance);
                }
                check("Ana's requested and found accession counts", result.Asked == 31775 && result.Found == 31775,
                    result.Asked.ToString(CultureInfo.InvariantCulture) + " asked, " + result.Found.ToString(CultureInfo.InvariantCulture) + " found");
                bool curationKept = CurationHash(file) == originalCuration;
                bool sourceKept = FileHash(input) == inputHash;
                check("live evidence changes no curation, sequence, lock or quantitation value", curationKept, null);
                check("Ana's original FCF remains byte-identical", sourceKept, inputHash);
                if (!curationKept || !sourceKept) throw new InvalidDataException("The source or curation changed; no evidence copy was written.");

                var pairs = file.MyExtendedFastaItems.Where(i => i.Evidence != null && reference.ContainsKey(i.Evidence.Accession))
                    .Select(i => (Item: i, Live: i.Evidence!, Reference: reference[i.Evidence!.Accession])).ToList();
                var counts = new List<object>();
                var differences = new List<object>();
                check("the live comparison covers every originally reference-matched protein",
                    referenceExpected.Count > 0 && referenceExpected.IsSubsetOf(pairs.Select(p => p.Item.SequenceIdentifier).ToHashSet(StringComparer.Ordinal)),
                    pairs.Count.ToString(CultureInfo.InvariantCulture) + " matched rows");
                Count("reviewed_entries", e => e.Reviewed == true);
                Count("family_annotations", e => e.FamilyAnnotation.Length > 0);
                Count("subfamily_annotations", e => e.FamilyMemberships?.Any(m => m.Subfamily.Length > 0) ?? e.Subfamily.Length > 0,
                    e => e.FamilyAnnotation.Split(new[] { ',', ';' }).Any(p => p.Trim().EndsWith(" subfamily", StringComparison.Ordinal)));
                Count("keyword_annotations", e => e.Keywords.Count > 0);
                Count("tissue_annotations", e => e.TissueSpecificity.Count > 0);
                Count("pfam_annotations", e => e.Pfam.Count > 0);
                Count("panther_annotations", e => e.Panther.Count > 0);
                Count("transmembrane_entries", e => e.TransmembraneCount > 0);
                Count("fragment_entries", e => e.Fragment == true);
                foreach (var pair in pairs)
                {
                    Equal("reviewed", pair.Reference.Reviewed?.ToString() ?? "unknown", pair.Live.Reviewed?.ToString() ?? "unknown");
                    Equal("fragment", pair.Reference.Fragment?.ToString() ?? "unknown", pair.Live.Fragment?.ToString() ?? "unknown");
                    Equal("transmembrane_count", (pair.Reference.TransmembraneCount ?? -1).ToString(CultureInfo.InvariantCulture),
                        (pair.Live.TransmembraneCount ?? -1).ToString(CultureInfo.InvariantCulture));
                    Equal("pfam_accessions", Terms(pair.Reference.Pfam.Select(p => p.Accession)), Terms(pair.Live.Pfam.Select(p => p.Accession)));
                    Equal("panther_accessions", Terms(pair.Reference.Panther.Select(p => p.Accession)), Terms(pair.Live.Panther.Select(p => p.Accession)));
                    Equal("keywords", Terms(pair.Reference.Keywords), Terms(pair.Live.Keywords));
                    Equal("family_annotation", NormalizeFamily(pair.Reference.FamilyAnnotation), NormalizeFamily(pair.Live.FamilyAnnotation), ignoreCase: true);

                    void Equal(string field, string expected, string actual, bool ignoreCase = false)
                    {
                        if (!string.Equals(expected, actual, ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                            differences.Add(new { protein = pair.Item.SequenceIdentifier, accession = pair.Live.Accession, field, expected, actual });
                    }
                }
                check("per-accession frozen metadata agrees on review, fragment, TM, domain codes, keywords and family annotation",
                    differences.Count == 0, differences.Count.ToString(CultureInfo.InvariantCulture) + " field differences");
                check("every fetched Domains value uses the stored Pfam names",
                    file.MyExtendedFastaItems.Where(i => i.Evidence != null).All(i => i.Domains == EvidenceFetcher.DomainNames(i.Evidence!)), null);

                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                string evidenceHash = AnnotationHash(file);
                if (!verify) file.Save(output);
                FamilyClusterFile reopened = FamilyClusterFile.Load(output);
                check("the native evidence copy reopens with identical annotations and curation",
                    AnnotationHash(reopened) == evidenceHash && CurationHash(reopened) == originalCuration, output);
                string report = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!, "e2-fetch-report.json");
                File.WriteAllText(report, JsonSerializer.Serialize(new
                {
                    inputFileSha256 = inputHash,
                    askedAccessions = result.Asked,
                    foundAccessions = result.Found,
                    source = result.Provenance,
                    totalProteinRows = file.MyExtendedFastaItems.Count,
                    evidenceProteinRows = file.MyExtendedFastaItems.Count(i => i.Evidence != null),
                    referenceRows = reference.Count,
                    referenceMatchedProteinRows = pairs.Count,
                    comparisonScope = "Protein rows whose returned canonical accession is present in the frozen taxonomy reference; accessions outside that reference are listed separately. Domain names are supplied by the live API and are absent from the frozen table.",
                    counts,
                    differences,
                    outsideReference = file.MyExtendedFastaItems.Where(i => i.Evidence != null && !reference.ContainsKey(i.Evidence.Accession))
                        .Select(i => new { protein = i.SequenceIdentifier, accession = i.Evidence!.Accession }).ToList(),
                    sourceFileBytes = new FileInfo(input).Length,
                    evidenceFileBytes = new FileInfo(output).Length,
                    curationHash = originalCuration,
                    annotationHash = evidenceHash,
                    completePfamNames = file.MyExtendedFastaItems.Where(i => i.Evidence != null)
                        .Sum(i => i.Evidence!.Pfam.Count(p => p.Name.Length > 0)),
                    unnamedPfamReferences = file.MyExtendedFastaItems.Where(i => i.Evidence != null)
                        .Sum(i => i.Evidence!.Pfam.Count(p => p.Name.Length == 0)),
                }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("Evidence copy: " + output);
                Console.WriteLine("Comparison report: " + report);
                Console.WriteLine("Reference-matched protein rows: " + pairs.Count.ToString(CultureInfo.InvariantCulture));
                Console.WriteLine("Evidence file bytes: " + new FileInfo(output).Length.ToString(CultureInfo.InvariantCulture));

                void Count(string name, Func<ProteinEvidence, bool> predicate, Func<ProteinEvidence, bool>? referencePredicate = null)
                {
                    int expected = pairs.Count(p => (referencePredicate ?? predicate)(p.Reference));
                    int observed = pairs.Count(p => predicate(p.Live));
                    counts.Add(new { field = name, reference = expected, observed });
                    check("frozen field count: " + name, expected == observed,
                        expected.ToString(CultureInfo.InvariantCulture) + " reference, " + observed.ToString(CultureInfo.InvariantCulture) + " observed");
                }
                string At(int offset, string fallback) => TestArguments.EmptyPassesThrough(args, flag, offset, fallback);
            }
            catch (Exception ex) { check("native evidence fetch check completed", false, ex.GetBaseException().Message); }
        }

        private static string Terms(IEnumerable<string> values) => string.Join(";", values.Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal));

        private static string NormalizeFamily(string text)
        {
            const string prefix = "Belongs to the ";
            string value = text.Trim().TrimEnd('.');
            if (value.StartsWith(prefix, StringComparison.Ordinal)) value = value.Substring(prefix.Length);
            return value.Replace(". ", ", ");
        }

        private static string FileHash(string path) => TestFileHash.Upper(path);

        private static string CurationHash(FamilyClusterFile file) => Hash(writer =>
        {
            using JsonDocument document = JsonSerializer.SerializeToDocument(file, FamilyClusterFile.CurationJsonOptions);
            writer.WriteStartObject();
            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (property.Name == "EvidenceSource" || property.Name == "DomainSource") continue;
                writer.WritePropertyName(property.Name);
                if (property.Name != "MyExtendedFastaItems") { property.Value.WriteTo(writer); continue; }
                writer.WriteStartArray();
                foreach (JsonElement item in property.Value.EnumerateArray())
                {
                    writer.WriteStartObject();
                    foreach (JsonProperty field in item.EnumerateObject())
                        if (field.Name != "Evidence" && field.Name != "Domains") field.WriteTo(writer);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        });

        private static string AnnotationHash(FamilyClusterFile file) => Hash(writer =>
        {
            writer.WriteStartArray();
            JsonSerializer.Serialize(writer, file.EvidenceSource);
            foreach (FastaItemExtended item in file.MyExtendedFastaItems)
            {
                writer.WriteStartObject();
                writer.WriteString("identifier", item.SequenceIdentifier);
                writer.WriteString("domains", item.Domains);
                writer.WritePropertyName("evidence");
                JsonSerializer.Serialize(writer, item.Evidence);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        });

        private static string Hash(Action<Utf8JsonWriter> write)
        {
            using SHA256 hash = SHA256.Create();
            using var sink = new CryptoStream(Stream.Null, hash, CryptoStreamMode.Write);
            using (var writer = new Utf8JsonWriter(sink)) write(writer);
            sink.FlushFinalBlock();
            return Convert.ToHexString(hash.Hash!);
        }

        private sealed class ConsoleProgress : IProgress<FetchProgress>
        {
            public void Report(FetchProgress value) => Console.WriteLine("Evidence batches "
                + value.CompletedBatches.ToString(CultureInfo.InvariantCulture) + "/" + value.TotalBatches.ToString(CultureInfo.InvariantCulture)
                + ", found " + value.Found.ToString(CultureInfo.InvariantCulture) + "/" + value.Asked.ToString(CultureInfo.InvariantCulture));
        }
    }
}
