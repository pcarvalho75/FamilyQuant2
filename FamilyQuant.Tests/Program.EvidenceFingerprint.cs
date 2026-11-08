using FamilyQuant.Evidence;
using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void EvidenceFingerprintTests()
    {
        Section("Z12p5f2g streamed evidence Apply and replay fingerprints");
        var method = typeof(EvidenceApply).GetMethod("Hash", BindingFlags.Static | BindingFlags.NonPublic)!;
        string Actual<T>(T value) => (string)method.MakeGenericMethod(typeof(T)).Invoke(null, new object?[] { value })!;
        string Previous<T>(T value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value))));
        void Same<T>(string name, T value) => Check("Z12p5f2g " + name + " retains the exact prior fingerprint", Actual(value) == Previous(value));
        var fixtures = new[] { ManualChoiceFile(), CurationStreamingFixture(64, 4096), CurationFormat2Fixture() };
        var large = new EvidenceProvenance
        {
            Provider = "Synthetic fingerprint source", Release = "fixture-release",
            ReferencePanel = new ReferencePanel
            {
                Entries = Enumerable.Range(0, 512).Select(index => new PanelEntry
                {
                    Accession = "HASH_REFERENCE_" + index, Sequence = "ACDEFGHIK",
                    Evidence = new ProteinEvidence { Keywords = new() { new string('A', 2048) } },
                }).ToList(),
            },
        };
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            foreach (string language in new[] { "en-US", "pt-BR" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(language);
                Same(language + " null source", (EvidenceProvenance?)null);
                Same(language + " empty and escaped text", new[] { "", "< & >", "Line one\nLine two", "\u00e1", "\ud800" });
                for (int index = 0; index < fixtures.Length; index++)
                {
                    var file = fixtures[index];
                    Same(language + " fixture " + index + " source", file.EvidenceSource);
                    Same(language + " fixture " + index + " recipe", file.EvidenceRecipe);
                    Same(language + " fixture " + index + " panel", file.FamilyPanel);
                    Same(language + " fixture " + index + " state", new
                    {
                        Proteins = EvidenceClassifier.Fingerprint(file.MyExtendedFastaItems),
                        file.SourceSha256, file.SourceSequenceCount, file.WordSimilarity, file.IsKeywordSuperCluster,
                        Locks = file.LockedUserIds.OrderBy(id => id), file.DomainSource, EvidenceSource = file.EvidenceSource,
                    });
                }
                Same(language + " large source", large);
                Same(language + " nested large state", new { Source = large, Null = (string?)null, Number = 1.25m, Values = new[] { 3, 1, 2 } });
            }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        string expected = Previous(large);
        var hashes = Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Actual(large)))).GetAwaiter().GetResult();
        Check("Z12p5f2g independent concurrent hashes retain the same value", hashes.All(hash => hash == expected));
    }
}
