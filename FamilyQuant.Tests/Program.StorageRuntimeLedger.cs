using FamilyQuant.DeNovo;
using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static List<CoreRuntimeCase> StorageRuntimeLedgerCases(string output, FeatureLedger.Observation[] observations, bool negativeControl)
    {
        var cases = new List<CoreRuntimeCase>();
        string At(int line, int occurrence = 0) => JsonSerializer.Deserialize<string>(
            observations.Where(row => row.Line == line).ElementAt(occurrence).Detail)!;
        string[] Between(int first, int last) => observations.Where(row => row.Line >= first && row.Line <= last)
            .Select(row => JsonSerializer.Deserialize<string>(row.Detail)!).ToArray();
        void Add(int first, int last, string operation, string expected, string actual, bool matches)
        {
            foreach (var row in observations.Where(row => row.Line >= first && row.Line <= last))
                cases.Add(new(row.Id, operation, expected, actual, matches));
        }
        Exception? Capture(Action work) { try { work(); return null; } catch (Exception ex) { return ex; } }
        void Refusal(int first, int last, string operation, Action work, string expected, Func<bool>? unchanged = null)
        {
            Exception? exception = Capture(work);
            Add(first, last, operation, "InvalidDataException: " + expected,
                exception == null ? "NO REFUSAL" : exception.GetType().Name + ": " + exception.Message,
                exception is InvalidDataException && exception.Message == expected && (unchanged?.Invoke() ?? true));
        }
        var fcf = new FamilyClusterFile { MyExtendedFastaItems = new() { new()
            { SequenceIdentifier = "native-storage-fixture", Sequence = "ACDEFGHIK", Description = "Synthetic codec contract", UserID = 41, USearchID = 41 } } };
        QuantManifest Manifest() => new() { FormatVersion = 2, IsotopeCount = 6, CreatedUtc = DateTime.UnixEpoch,
            UpdatedUtc = DateTime.UnixEpoch, SpectrumComparisonRowCount = 2, SpectrumComparisonRows = new()
            { new() { FileName = "fixture.raw", ScanNumber = 101, Charge = 3, PrecursorMz = 612.125 },
              new() { FileName = "fixture.raw", ScanNumber = 202, Charge = 2, PrecursorMz = 512.25 } } };
        QuantRow Sample(bool estimate) => new() { Side = estimate ? "identified" : "denovo", Run = "fixture-run", Scan = estimate ? 101 : 202,
            Charge = 3, Sequence = "ACDEFGHIK", PrecursorMz = 612.1234567890123, PatternLabXScore = 17.12345678901234,
            IsotopeAreas = new[] { 1.125, 2.25, 3.5, 4.75, 5.125, 6.25 }, EnvelopeSum = 23.12345678901234,
            UnderSampledArea = 0.01234567890123456, UnderSampledIsEstimate = estimate, FamilyUserId = 41,
            DeNovoEvidence = new() { new() { SourceId = "synthetic-caller", Sequence = "ACDEFGHIK", ConfidenceValue = 87.25 } } };
        string Cell(QuantRow row, string column) => column switch
        {
            "side" => row.Side, "run" => row.Run, "scan" => row.Scan.ToString(CultureInfo.InvariantCulture),
            "precursor_mz" => row.PrecursorMz.ToString("R", CultureInfo.InvariantCulture), "charge" => row.Charge.ToString(CultureInfo.InvariantCulture),
            "sequence" => row.Sequence, "patternlab_xscore" => row.PatternLabXScore?.ToString("R", CultureInfo.InvariantCulture) ?? "",
            "denovo_evidence_json" => JsonSerializer.Serialize(row.DeNovoEvidence), "envelope_sum" => row.EnvelopeSum.ToString("R", CultureInfo.InvariantCulture),
            "family_userid" => row.FamilyUserId?.ToString(CultureInfo.InvariantCulture) ?? "", "under_sampled" => row.UnderSampledArea.ToString("R", CultureInfo.InvariantCulture),
            "under_sampled_estimate" => row.UnderSampledIsEstimate ? "1" : "0",
            _ when column.StartsWith("area_m", StringComparison.Ordinal) => row.IsotopeAreas[int.Parse(column[6..], CultureInfo.InvariantCulture)]
                .ToString("R", CultureInfo.InvariantCulture),
            _ => throw new InvalidDataException("Unknown fixture column: " + column),
        };
        string[] columns = Between(600, 603), legacyColumns = Between(610, 611);
        string curationEntry = At(581), quantEntry = At(586), manifestEntry = At(588), spectrumEntry = At(593);
        var samples = new[] { Sample(true), Sample(false) };
        string template = Path.Combine(output, "template.fqproj");
        string oldDirectory = Environment.CurrentDirectory;
        CultureInfo oldCulture = CultureInfo.CurrentCulture;
        try
        {
            // Relative fixture names make refusal messages reproducible without rewriting any actual value.
            Environment.CurrentDirectory = output;
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            ProjectStore.WriteFqproj("template.fqproj", fcf, samples, Manifest());
            // These are authored corruption fixtures. Fix ZIP metadata before any probe reads them,
            // so their retained bytes reproduce as well as the values and complete refusal messages.
            using (var fixture = ZipFile.Open("template.fqproj", ZipArchiveMode.Update))
                foreach (var entry in fixture.Entries)
                    entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
            string ReadEntry(string name, string entry, bool gzip = false)
            {
                using var zip = ZipFile.OpenRead(name);
                using var stream = zip.GetEntry(entry)!.Open();
                using var compressed = gzip ? new GZipStream(stream, CompressionMode.Decompress) : null;
                using var reader = new StreamReader(compressed ?? stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            void Replace(string name, string entryName, string? text, bool gzip = false)
            {
                using var zip = ZipFile.Open(name, ZipArchiveMode.Update);
                zip.GetEntry(entryName)?.Delete();
                if (text == null) return;
                var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                entry.LastWriteTime = new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
                using var stream = entry.Open();
                using var compressed = gzip ? new GZipStream(stream, CompressionMode.Compress) : null;
                using var writer = new StreamWriter(compressed ?? stream, new UTF8Encoding(false));
                writer.Write(text);
            }
            string Clone(string name) { File.Copy(template, name, false); return name; }
            string PatchManifest(string name, Action<JsonObject> patch)
            {
                Clone(name);
                var json = JsonNode.Parse(ReadEntry(name, manifestEntry))!.AsObject();
                patch(json); Replace(name, manifestEntry, json.ToJsonString()); return name;
            }
            string Table(string[] header, IEnumerable<QuantRow> rows) => string.Join('\t', header) + "\n"
                + string.Join("\n", rows.Select(row => string.Join('\t', header.Select(column => Cell(row, column))))) + "\n";
            string wire = ReadEntry("template.fqproj", quantEntry, true);
            string[][] lines = wire.TrimEnd('\n').Split('\n').Select(line => line.Split('\t')).ToArray();
            Add(925, 925, "native quant TSV delimiter and complete ordered header", string.Join(At(925), columns),
                string.Join(At(925), lines[0]), lines[0].SequenceEqual(columns) && lines.Length == 3 && lines.All(line => line.Length == columns.Length));
            var reopened = ProjectStore.ReadFqproj("template.fqproj").quant;
            foreach (var pair in new[] { (933, "precursor_mz"), (937, "patternlab_xscore"), (949, "envelope_sum"), (950, "under_sampled") })
            {
                int index = Array.IndexOf(columns, pair.Item2);
                string[] expected = samples.Select(row => Cell(row, pair.Item2)).ToArray();
                string[] actual = lines.Skip(1).Select(line => line[index]).ToArray();
                Add(pair.Item1, pair.Item1, "invariant round-trip double for " + pair.Item2, string.Join(";", expected), string.Join(";", actual),
                    At(pair.Item1) == "R" && expected.SequenceEqual(actual) && reopened.Select(row => Cell(row, pair.Item2)).SequenceEqual(expected));
            }
            int estimateIndex = Array.IndexOf(columns, "under_sampled_estimate");
            Add(951, 951, "native true/false estimate wire cells and round trip", At(951, 0) + ";" + At(951, 1),
                lines[1][estimateIndex] + ";" + lines[2][estimateIndex], lines[1][estimateIndex] == At(951, 0)
                && lines[2][estimateIndex] == At(951, 1) && reopened[0].UnderSampledIsEstimate && !reopened[1].UnderSampledIsEstimate);

            string[] permuted = columns.Reverse().ToArray();
            string permutedTable = Table(permuted, samples);
            if (negativeControl)
            {
                string[] damaged = permutedTable.Split('\n');
                for (int i = 1; i <= samples.Length; i++)
                {
                    string[] cells = damaged[i].Split('\t');
                    int run = Array.IndexOf(permuted, "run"), sequence = Array.IndexOf(permuted, "sequence");
                    (cells[run], cells[sequence]) = (cells[sequence], cells[run]); damaged[i] = string.Join('\t', cells);
                }
                permutedTable = string.Join('\n', damaged);
            }
            Replace(Clone("permuted.fqproj"), quantEntry, permutedTable, true);
            var shuffled = ProjectStore.ReadFqproj("permuted.fqproj").quant;
            foreach (var row in observations.Where(row => row.Line >= 991 && row.Line <= 996))
            {
                string column = JsonSerializer.Deserialize<string>(row.Detail)!;
                string expected = string.Join(";", samples.Select(sample => Cell(sample, column)));
                string actual = string.Join(";", shuffled.Select(sample => Cell(sample, column)));
                cases.Add(new(row.Id, "read named column from reversed header: " + column, expected, actual, expected == actual));
            }
            string expectedIsotopes = JsonSerializer.Serialize(samples.Select(row => row.IsotopeAreas));
            string actualIsotopes = JsonSerializer.Serialize(shuffled.Select(row => row.IsotopeAreas));
            Add(1001, 1001, "resolve all six isotope columns by name from reversed header", expectedIsotopes, actualIsotopes,
                At(1001) == "area_m" && actualIsotopes == expectedIsotopes);
            string legacyTable = Table(legacyColumns.Reverse().ToArray(), samples.Take(1));
            Replace(Clone("legacy.fqproj"), quantEntry, legacyTable, true);
            QuantRow legacy = ProjectStore.ReadFqproj("legacy.fqproj").quant.Single();
            bool legacyRead = legacy.IsotopeAreas.SequenceEqual(samples[0].IsotopeAreas.Take(4)) && legacy.PatternLabXScore == null
                && legacy.DeNovoEvidence.Count == 0 && legacy.UnderSampledArea == 0 && !legacy.UnderSampledIsEstimate
                && legacyColumns.All(column => Cell(legacy, column) == Cell(samples[0], column));
            var legacyObservations = observations.Where(row => row.Line is 610 or 611).ToArray();
            for (int i = 0; i < legacyObservations.Length; i++)
            {
                string actual = i < ProjectStore.LegacyQuantColumns.Length ? ProjectStore.LegacyQuantColumns[i] : "missing";
                cases.Add(new(legacyObservations[i].Id, "legacy header slot " + i.ToString(CultureInfo.InvariantCulture) + " and native old-table read",
                    legacyColumns[i], actual, legacyRead && ProjectStore.LegacyQuantColumns.Length == legacyColumns.Length && actual == legacyColumns[i]));
            }
            string[] boolCells = { "1", "0", "true", "" };
            string boolTable = string.Join('\t', columns) + "\n" + string.Join("\n", boolCells.Select(value =>
                string.Join('\t', columns.Select(column => column == "under_sampled_estimate" ? value : Cell(samples[0], column))))) + "\n";
            Replace(Clone("booleans.fqproj"), quantEntry, boolTable, true);
            string actualBooleans = JsonSerializer.Serialize(ProjectStore.ReadFqproj("booleans.fqproj").quant.Select(row => row.UnderSampledIsEstimate));
            Add(1054, 1054, "only wire 1 reads as estimate true", "[true,false,false,false]", actualBooleans,
                At(1054) == "1" && actualBooleans == "[true,false,false,false]");

            var headerRefusals = new List<string>();
            foreach (var row in observations.Where(row => row.Line == 983))
            {
                string required = JsonSerializer.Deserialize<string>(row.Detail)!;
                string[] header = columns.Where(column => column != required).ToArray();
                string name = "missing-" + required + ".fqproj";
                Replace(Clone(name), quantEntry, Table(header, samples.Take(1)), true);
                string expected = At(987, 0) + required + At(987, 1) + At(988) + string.Join('\t', header);
                Exception? error = Capture(() => ProjectStore.ReadFqproj(name));
                bool correct = error is InvalidDataException && error.Message == expected;
                cases.Add(new(row.Id, "missing required named column refuses before returning rows: " + required,
                    expected, error?.Message ?? "NO REFUSAL", correct));
                headerRefusals.Add(correct ? "exact refusal: " + required : "WRONG refusal: " + required);
            }
            Add(987, 988, "all six missing-column refusal messages match complete frozen construction", "6 exact refusals",
                string.Join(";", headerRefusals), headerRefusals.Count == 6 && headerRefusals.All(value => value.StartsWith("exact refusal:", StringComparison.Ordinal)));

            var futureWrite = Manifest(); futureWrite.FormatVersion = ProjectStore.CurrentFormatVersion + 1;
            string futureMessage = At(662) + futureWrite.FormatVersion.ToString(CultureInfo.InvariantCulture) + At(664)
                + ProjectStore.CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) + At(666);
            Refusal(662, 666, "future-format write refuses before creating destination", () => ProjectStore.WriteFqproj("future-write.fqproj", fcf, samples, futureWrite),
                futureMessage, () => !File.Exists("future-write.fqproj"));
            var overCount = Manifest(); overCount.IsotopeCount = 7;
            Exception? overError = Capture(() => ProjectStore.WriteFqproj("over-count.fqproj", fcf, samples, overCount));
            string overMessage = At(675) + "6" + At(676) + "5" + At(678) + "7" + At(680);
            string fullOverMessage = new ArgumentOutOfRangeException("manifest", overMessage).Message;
            Add(675, 680, "isotope overflow write refuses before creating destination", fullOverMessage, overError?.Message ?? "NO REFUSAL",
                overError is ArgumentOutOfRangeException range && range.ParamName == "manifest"
                && range.Message == fullOverMessage && !File.Exists("over-count.fqproj"));
            var nullRow = Manifest(); nullRow.SpectrumComparisonRows.Add(null!);
            Refusal(687, 688, "null spectrum row write refuses before sidecar counting", () => ProjectStore.WriteFqproj("null-row.fqproj", fcf, samples, nullRow),
                At(687) + At(688), () => !File.Exists("null-row.fqproj"));
            string missingCuration = Clone("missing-curation.fqproj"); Replace(missingCuration, curationEntry, null);
            Refusal(732, 733, "missing curation ZIP entry refuses", () => ProjectStore.ReadFqproj(missingCuration),
                At(732, 0) + missingCuration + At(732, 1) + curationEntry + At(733));
            string missingManifest = Clone("missing-manifest.fqproj"); Replace(missingManifest, manifestEntry, null);
            Refusal(737, 738, "missing manifest ZIP entry refuses", () => ProjectStore.ReadFqproj(missingManifest),
                At(737, 0) + missingManifest + At(737, 1) + manifestEntry + At(738));
            string unreadable = Clone("null-manifest.fqproj"); Replace(unreadable, manifestEntry, "null");
            Refusal(745, 745, "JSON null manifest refuses", () => ProjectStore.ReadFqproj(unreadable),
                At(745, 0) + unreadable + At(745, 1) + manifestEntry + At(745, 2));
            string futureRead = PatchManifest("future-read.fqproj", json => json["FormatVersion"] = ProjectStore.CurrentFormatVersion + 1);
            Refusal(749, 752, "newer on-disk format refuses", () => ProjectStore.ReadFqproj(futureRead), At(749, 0) + futureRead + At(749, 1)
                + (ProjectStore.CurrentFormatVersion + 1).ToString(CultureInfo.InvariantCulture) + At(750)
                + ProjectStore.CurrentFormatVersion.ToString(CultureInfo.InvariantCulture) + At(752));
            string stale = PatchManifest("stale-concordance.fqproj", json => json["IdentificationConcordance"] =
                JsonSerializer.SerializeToNode(new IdentificationConcordanceSummary { SharedDatabaseSearchScanKeys = 1 }));
            Refusal(765, 769, "legacy shared-scan audit refuses as a current agreement denominator", () => ProjectStore.ReadFqproj(stale),
                At(765, 0) + stale + At(765, 1) + "1" + At(767) + At(768) + At(769));
            string noCount = PatchManifest("missing-count.fqproj", json => json.Remove("SpectrumComparisonRowCount"));
            Refusal(783, 785, "version-2 sidecar with missing count refuses", () => ProjectStore.ReadFqproj(noCount),
                At(783, 0) + noCount + At(783, 1) + manifestEntry + At(784) + At(785));
            string negativeCount = PatchManifest("negative-count.fqproj", json => json["SpectrumComparisonRowCount"] = -1);
            Refusal(790, 791, "negative spectrum count refuses", () => ProjectStore.ReadFqproj(negativeCount),
                At(790, 0) + negativeCount + At(790, 1) + "-1" + At(791));
            string missingSidecar = Clone("missing-sidecar.fqproj"); Replace(missingSidecar, spectrumEntry, null);
            Refusal(794, 797, "declared nonempty spectrum table with missing sidecar refuses", () => ProjectStore.ReadFqproj(missingSidecar),
                At(794, 0) + missingSidecar + At(794, 1) + "2" + At(796) + spectrumEntry + At(797));
            string wrongCount = PatchManifest("wrong-count.fqproj", json => json["SpectrumComparisonRowCount"] = 3);
            Refusal(802, 806, "truncated spectrum count refuses", () => ProjectStore.ReadFqproj(wrongCount),
                At(802, 0) + wrongCount + At(802, 1) + "3" + At(804) + spectrumEntry + At(805) + "2" + At(806));
            string nullSpectrum = Clone("null-spectrum.fqproj"); Replace(nullSpectrum, spectrumEntry, "{}\nnull\n", true);
            Exception? nullSpectrumError = Capture(() => ProjectStore.ReadFqproj(nullSpectrum));
            Add(898, 898, "JSON null spectrum row refuses with its specific inner error", At(898), nullSpectrumError?.InnerException?.Message ?? "NO INNER ERROR",
                nullSpectrumError is InvalidDataException && nullSpectrumError.InnerException is JsonException
                && nullSpectrumError.InnerException.Message == At(898));
            string malformedSpectrum = Clone("malformed-spectrum.fqproj"); Replace(malformedSpectrum, spectrumEntry, "{}\n{bad-json\n", true);
            Exception? malformedError = Capture(() => ProjectStore.ReadFqproj(malformedSpectrum));
            string physicalLineMessage = At(904, 0) + spectrumEntry + At(904, 1) + "2" + At(905);
            Add(904, 905, "null and malformed spectrum rows report exact physical line 2", physicalLineMessage,
                (nullSpectrumError?.Message ?? "NO REFUSAL") + ";" + (malformedError?.Message ?? "NO REFUSAL"),
                nullSpectrumError is InvalidDataException && malformedError is InvalidDataException
                && nullSpectrumError.Message == physicalLineMessage && malformedError.Message == physicalLineMessage);

            string atomic = "atomic.fqproj"; File.WriteAllText(atomic, "existing target", new UTF8Encoding(false));
            using var cancellation = new CancellationTokenSource();
            bool staged = false;
            var progress = new InlineOperationProgress(value =>
            {
                if (value.Stage != "project-save" || value.Completed != 1) return;
                string[] temporary = Directory.GetFiles(output, atomic + ".*.tmp");
                staged = temporary.Length == 1 && Path.GetExtension(temporary[0]) == At(700)
                    && Guid.TryParseExact(Path.GetFileName(temporary[0])[((atomic.Length + 1))..^4], "N", out _)
                    && File.ReadAllText(atomic) == "existing target";
                cancellation.Cancel();
            });
            Exception? cancelError = Capture(() => ProjectStore.WriteFqproj(atomic, fcf, samples, Manifest(),
                new OperationContext(cancellation.Token, progress)));
            bool cleanup = Directory.GetFiles(output, atomic + ".*.tmp").Length == 0;
            Add(700, 700, "GUID temporary file stages before atomic publish and cancellation cleans it", "staged .tmp; target preserved; cleanup",
                "staged=" + staged + ";cancelled=" + (cancelError is OperationCanceledException) + ";cleanup=" + cleanup,
                staged && cleanup && cancelError is OperationCanceledException && File.ReadAllText(atomic) == "existing target");
        }
        finally { Environment.CurrentDirectory = oldDirectory; CultureInfo.CurrentCulture = oldCulture; }
        return cases;
    }
}
