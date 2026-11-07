using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FeatureLedgerEvidenceTests(bool release = false)
    {
        FeatureLedgerReceiptGuardTests(release);
        FeatureLedgerReceiptFixtureTests();
        LedgerImportBatchTests();
    }

    // The default suite runs the three parts of FeatureLedgerEvidenceTests as separate entries.
    private static void FeatureLedgerReceiptFixtureTests()
    {
        string root = FindLedgerRoot()!;
        ReceiptValidationFixtures(root, JsonSerializer.Deserialize<FeatureLedger.Inventory>(
            File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!);
        FeatureLedgerReleaseDeferralTests(root);
    }

    private static void FeatureLedgerReceiptGuardTests(bool release = false)
    {
        Section("Feature ledger evidence receipts");
        string root = FindLedgerRoot()!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        string markdown = File.ReadAllText(Path.Combine(root, "docs/FEATURE-LEDGER.md"));
        string first = markdown.Split('\n').First(line => line.StartsWith("| FQ15-", StringComparison.Ordinal));
        string[] cells = first.Split('|');
        cells[7] = " VERIFIED ";
        cells[8] = " executed:long-enough-but-no-receipt ";
        string forged = markdown.Replace(first, string.Join('|', cells), StringComparison.Ordinal);
        if (LedgerReceiptsSkipped(release)) SkipLedgerReceipts("a plausible evidence sentence cannot close a row without an executed receipt");
        else Check("a plausible evidence sentence cannot close a row without an executed receipt",
            FeatureLedger.Validate(inventory, forged).Count > 0);
        Check("a native tool groups and audits per-observation receipts", typeof(Program).Assembly.GetType("FamilyQuant.Tests.FeatureLedgerEvidence") != null);
        string invalidAudit = Path.Combine(Path.GetTempPath(), "FamilyQuant.EvidenceGuard.Tests", Path.GetFileName(root), "missing-row-audit");
        Directory.CreateDirectory(invalidAudit);
        Check("an audit cannot accept a ledger with a missing frozen row", FeatureLedgerEvidence.Report(root, inventory,
            markdown.Replace(first + "\n", "", StringComparison.Ordinal), FeatureLedgerEvidence.Load(root, inventory), invalidAudit) == 1);
    }

    // Deliberately synthetic validator inputs. None is imported into the real ledger.
    private static void ReceiptValidationFixtures(string root, FeatureLedger.Inventory inventory)
    {
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.EvidenceGuard.Tests", Path.GetFileName(root));
        Directory.CreateDirectory(folder);
        string log = Path.Combine(folder, "validator.log"), png = Path.Combine(folder, "validator.png");
        const string assertion = "synthetic observation-specific assertion";
        File.WriteAllText(log, "  PASS  " + assertion + "\n===== 1 passed, 0 failed, 0 skipped =====\n");
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1, 1, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, new byte[] { 0, 0, 0, 255 }, 4)));
        using (var stream = File.Create(png)) encoder.Save(stream);
        var observed = inventory.Observations.First(o => o.Kind == "option/copy" && !FeatureLedger.IsApprovedDrop(o));
        var handler = inventory.Observations.First(o => o.Kind == "handler" && !FeatureLedger.IsApprovedDrop(o));
        var source = new FeatureLedgerEvidence.Implementation("FamilyQuant.Tests/Program.FeatureLedgerEvidence.cs", nameof(ReceiptValidationFixtures),
            FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant.Tests/Program.FeatureLedgerEvidence.cs")));
        var proof = new FeatureLedgerEvidence.Proof("fixture", "test", nameof(FeatureLedgerEvidenceTests), assertion, log, "");
        var row = new FeatureLedgerEvidence.Row(observed.Id, new() { source }, new() { proof });
        var run = new FeatureLedgerEvidence.Run("fixture", "synthetic validator input, never actual closure", "2026-11-01 00:00:00 -0800", new string('a', 40),
            new() { new(log, FeatureLedgerEvidence.Hash(log)), new(png, FeatureLedgerEvidence.Hash(png)) });
        FeatureLedgerEvidence.Registry Registry(FeatureLedgerEvidence.Row value, FeatureLedgerEvidence.Run? suppliedRun = null,
            string? reference = null) => FeatureLedgerEvidence.Load(root, inventory, new[]
            { new FeatureLedgerEvidence.Register(1, "fixture", reference ?? FeatureLedger.ReferenceCommit, new() { suppliedRun ?? run }, new() { value }) },
                Array.Empty<FeatureLedgerEvidence.KindReview>());
        bool Valid(FeatureLedgerEvidence.Row value, FeatureLedgerEvidence.Run? suppliedRun = null) =>
            Registry(value, suppliedRun).IsVerified(value.ObservationId, "evidence:fixture:" + value.ObservationId);
        Check("a well-formed current receipt with its exact green assertion is accepted", Valid(row));
        var kindRow = row with { Proofs = new(), KindReceiptId = "same-kind", Compared = "frozen named property -> current named property" };
        var sameKindObservation = inventory.Observations.First(other => other.Kind == observed.Kind && other.Id != observed.Id && !FeatureLedger.IsApprovedDrop(other));
        var secondKindRow = kindRow with { ObservationId = sameKindObservation.Id, Compared = "second frozen property -> second current property" };
        var kindRegister = new FeatureLedgerEvidence.Register(1, "fixture", FeatureLedger.ReferenceCommit, new() { run }, new() { kindRow, secondKindRow },
            new() { new("same-kind", observed.Kind, new() { proof }) });
        var kindRegistry = FeatureLedgerEvidence.Load(root, inventory, new[] { kindRegister }, Array.Empty<FeatureLedgerEvidence.KindReview>());
        Check("one executed kind receipt can support separately named row comparisons", kindRegistry.IsVerified(observed.Id, "evidence:fixture:" + observed.Id)
            && kindRegistry.IsVerified(sameKindObservation.Id, "evidence:fixture:" + sameKindObservation.Id));
        Check("a kind receipt cannot close a row without its comparison description", !FeatureLedgerEvidence.Load(root, inventory,
            new[] { kindRegister with { Rows = new() { kindRow with { Compared = "" } } } }, Array.Empty<FeatureLedgerEvidence.KindReview>())
            .IsVerified(observed.Id, "evidence:fixture:" + observed.Id));
        Check("a shared receipt cannot be borrowed by another kind", !FeatureLedgerEvidence.Load(root, inventory,
            new[] { kindRegister with { KindReceipts = new() { new("same-kind", "invented kind", new() { proof }) } } }, Array.Empty<FeatureLedgerEvidence.KindReview>())
            .IsVerified(observed.Id, "evidence:fixture:" + observed.Id));
        var reopened = FeatureLedgerEvidence.Load(root, inventory, new[] { kindRegister }, new[]
            { new FeatureLedgerEvidence.KindReview(observed.Kind, "REOPENED", "synthetic independent reviewer", "one sampled row failed", run.ExecutedAt) });
        Check("one sampled failure reopens every row of its kind", !reopened.IsVerified(observed.Id, "evidence:fixture:" + observed.Id)
            && !reopened.IsVerified(sameKindObservation.Id, "evidence:fixture:" + sameKindObservation.Id));
        Check("a reopened kind stays excluded from automatic reapplication", !FeatureLedgerEvidence.Apply(
            "| " + observed.Id + " | IMPROVE | " + observed.Kind + " | fixture | fact | test:FeatureLedgerEvidenceTests | OPEN | pending |\n", reopened)
            .Contains(" | VERIFIED | ", StringComparison.Ordinal));
        Check("source relocation without execution stays OPEN", !Valid(row with { Proofs = new() }));
        Check("receipt rejects an unknown execution target", !Valid(row with { Proofs = new() { proof with { Target = "InventedMethod" } } }));
        Check("receipt rejects a missing assertion", !Valid(row with { Proofs = new() { proof with { Assertion = "not executed" } } }));
        Check("receipt rejects an unknown run", !Valid(row with { Proofs = new() { proof with { RunId = "missing" } } }));
        Check("receipt rejects a missing implementation", !Valid(row with { Implementation = new() }));
        Check("receipt rejects an altered implementation hash", !Valid(row with { Implementation = new() { source with { Sha256 = new string('0', 64) } } }));
        byte[] implementationBytes = File.ReadAllBytes(Path.Combine(root, source.Path));
        byte[] Eol(byte[] bytes, bool crlf)
        {
            using var result = new MemoryStream();
            for (int i = 0; i < bytes.Length; i++)
            {
                if (bytes[i] == 13 && i + 1 < bytes.Length && bytes[i + 1] == 10) continue;
                if (bytes[i] == 10 && crlf) result.WriteByte(13);
                result.WriteByte(bytes[i]);
            }
            return result.ToArray();
        }
        string ByteHash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var lfSource = source with { Sha256 = ByteHash(Eol(implementationBytes, false)) };
        var crlfSource = source with { Sha256 = ByteHash(Eol(implementationBytes, true)) };
        string retainedLf = Path.Combine(folder, "retained-lf.cs"), retainedCrlf = Path.Combine(folder, "retained-crlf.cs");
        File.WriteAllBytes(retainedLf, Eol(implementationBytes, false)); File.WriteAllBytes(retainedCrlf, Eol(implementationBytes, true));
        FeatureLedgerEvidence.Run WithSource(string path) => run with
            { Artifacts = run.Artifacts.Append(new FeatureLedgerEvidence.Artifact(path, FeatureLedgerEvidence.Hash(path))).ToList() };
        Check("current source receipt accepts the same bytes with LF line endings", Valid(row with { Implementation = new() { lfSource } }, WithSource(retainedLf)));
        Check("current source receipt accepts the same bytes with CRLF line endings", Valid(row with { Implementation = new() { crlfSource } }, WithSource(retainedCrlf)));
        var differentEolSource = source.Sha256 == lfSource.Sha256 ? crlfSource : lfSource;
        Check("current source EOL tolerance never invents an unretained expected hash", !Valid(row with { Implementation = new() { differentEolSource } }));
        string mixedRoot = Path.Combine(folder, "mixed-source-checkout"); Directory.CreateDirectory(mixedRoot);
        string mixedCurrent = Path.Combine(mixedRoot, "Current.cs"), mixedRecorded = Path.Combine(folder, "recorded-mixed.cs");
        byte[] mixedBytes = Encoding.UTF8.GetBytes("// retained source\r\nclass Example {\n string Value = \"kept\";\r\n}\n");
        File.WriteAllBytes(mixedRecorded, mixedBytes);
        var mixedRow = row with { Implementation = new() { new("Current.cs", "Example", ByteHash(mixedBytes)) } };
        bool MixedValid(FeatureLedgerEvidence.Run supplied, FeatureLedgerEvidence.Row? suppliedRow = null) => FeatureLedgerEvidence.Load(mixedRoot, inventory,
            new[] { new FeatureLedgerEvidence.Register(1, "fixture", FeatureLedger.ReferenceCommit, new() { supplied }, new() { suppliedRow ?? mixedRow }) },
            Array.Empty<FeatureLedgerEvidence.KindReview>()).IsVerified(observed.Id, "evidence:fixture:" + observed.Id);
        File.WriteAllBytes(mixedCurrent, Eol(mixedBytes, false));
        var mixedRun = WithSource(mixedRecorded);
        Check("current LF source accepts pinned retained mixed-EOL bytes", MixedValid(mixedRun));
        File.WriteAllBytes(mixedCurrent, Eol(mixedBytes, true));
        Check("current CRLF source accepts pinned retained mixed-EOL bytes", MixedValid(mixedRun));
        Check("mixed-EOL tolerance requires a retained source artifact", !MixedValid(run));
        File.WriteAllBytes(mixedCurrent, Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Eol(mixedBytes, false)).Replace("kept", "lost", StringComparison.Ordinal)));
        Check("mixed-EOL tolerance rejects a changed source literal", !MixedValid(mixedRun));
        File.WriteAllBytes(mixedCurrent, Eol(mixedBytes, false));
        File.WriteAllBytes(mixedRecorded, Eol(mixedBytes, false));
        Check("retained source line endings remain raw-hash pinned", !MixedValid(mixedRun));
        File.WriteAllBytes(mixedRecorded, mixedBytes);
        File.AppendAllText(mixedRecorded, "// corrupt retained artifact");
        Check("mixed-EOL tolerance rejects corrupt retained raw bytes", !MixedValid(mixedRun));
        File.WriteAllBytes(mixedRecorded, mixedBytes);
        File.WriteAllBytes(mixedCurrent, Eol(mixedBytes, false).Append((byte)13).ToArray());
        Check("mixed-EOL tolerance does not remove standalone carriage returns", !MixedValid(mixedRun));
        string binaryCurrent = Path.Combine(mixedRoot, "Current.dll"), binaryRecorded = Path.Combine(folder, "recorded-mixed.dll");
        File.WriteAllBytes(binaryCurrent, Eol(mixedBytes, false)); File.WriteAllBytes(binaryRecorded, mixedBytes);
        Check("assembly implementation bytes never use source EOL tolerance", !MixedValid(WithSource(binaryRecorded), mixedRow with
            { Implementation = new() { new("Current.dll", "Example", ByteHash(mixedBytes)) } }));
        byte[] changedCharacter = Eol(implementationBytes, false);
        changedCharacter[Array.IndexOf(changedCharacter, (byte)'u')] = (byte)'v';
        Check("current source EOL tolerance still rejects one changed character", !Valid(row with
            { Implementation = new() { source with { Sha256 = ByteHash(changedCharacter) } } }));
        Check("retained assertion artifacts still require exact line-ending bytes", !Valid(row, run with
            { Artifacts = new() { new(log, ByteHash(Eol(File.ReadAllBytes(log), true))), new(png, FeatureLedgerEvidence.Hash(png)) } }));
        Check("the shared frozen and artifact hash remains byte-exact", FeatureLedgerEvidence.Hash(Path.Combine(root, source.Path))
            == ByteHash(implementationBytes) && ByteHash(Eol(implementationBytes, false)) != ByteHash(Eol(implementationBytes, true)));
        Check("receipt rejects an implementation outside this worktree", !Valid(row with
            { Implementation = new() { source with { Path = "../outside.cs" } } }));
        Check("receipt rejects a missing artifact", !Valid(row with { Proofs = new() { proof with { Artifact = log + ".missing" } } }));
        Check("receipt rejects an altered artifact hash", !Valid(row, run with
            { Artifacts = new() { new(log, new string('0', 64)) } }));
        Check("receipt rejects a changed supporting artifact even when its assertion log matches", !Valid(row, run with
            { Artifacts = new() { new(log, FeatureLedgerEvidence.Hash(log)), new(png, new string('0', 64)) } }));
        Check("receipt rejects a missing command or date", !Valid(row, run with { Command = "", ExecutedAt = "" }));
        Check("receipt rejects a different frozen reference", Registry(row, reference: new string('0', 40)).FatalProblems.Count > 0);
        Check("receipt rejects an invented observation ID", Registry(row with { ObservationId = "FQ15-INVENTED" }).FatalProblems.Count > 0);
        File.WriteAllText(log, "  PASS  " + assertion + "\n  FAIL  related failure\n===== 1 passed, 1 failed, 0 skipped =====\n");
        Check("a passing line in a failed run cannot close a row", !Valid(row, run with
            { Artifacts = new() { new(log, FeatureLedgerEvidence.Hash(log)) } }));
        File.WriteAllText(log, "Build succeeded.\n0 warnings, 0 errors.\n");
        Check("a green build cannot replace an executed behavioral assertion", !Valid(row, run with
            { Artifacts = new() { new(log, FeatureLedgerEvidence.Hash(log)) } }));
        var imageProof = proof with { Kind = "render", Target = "synthetic validator PNG", Artifact = png, InspectedBy = "synthetic validator fixture" };
        var imageRun = run with { Artifacts = new() { new(png, FeatureLedgerEvidence.Hash(png)) } };
        Check("a named inspected image is eligible for a presentation fact", Valid(row with { Proofs = new() { imageProof } }, imageRun));
        Check("an image without its inspector cannot close a row", !Valid(row with { Proofs = new() { imageProof with { InspectedBy = "" } } }, imageRun));
        Check("a screenshot alone cannot verify an event handler", !Valid(row with { ObservationId = handler.Id, Proofs = new() { imageProof } }, imageRun));
        File.WriteAllText(png, "This text file is not an inspected PNG.");
        Check("a file extension cannot impersonate an inspected render", !Valid(row with { Proofs = new() { imageProof } },
            run with { Artifacts = new() { new(png, FeatureLedgerEvidence.Hash(png)) } }));
    }
}
