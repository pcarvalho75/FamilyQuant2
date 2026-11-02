using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Text;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FeatureLedgerEvidenceTests()
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
        Check("a plausible evidence sentence cannot close a row without an executed receipt",
            FeatureLedger.Validate(inventory, forged).Count > 0);
        Check("a native tool groups and audits per-observation receipts", typeof(Program).Assembly.GetType("FamilyQuant.Tests.FeatureLedgerEvidence") != null);
        string invalidAudit = Path.Combine(Path.GetTempPath(), "FamilyQuant.EvidenceGuard.Tests", Path.GetFileName(root), "missing-row-audit");
        Directory.CreateDirectory(invalidAudit);
        Check("an audit cannot accept a ledger with a missing frozen row", FeatureLedgerEvidence.Report(root, inventory,
            markdown.Replace(first + "\n", "", StringComparison.Ordinal), FeatureLedgerEvidence.Load(root, inventory), invalidAudit) == 1);
        ReceiptValidationFixtures(root, inventory);
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
            { new FeatureLedgerEvidence.Register(1, "fixture", reference ?? FeatureLedger.ReferenceCommit, new() { suppliedRun ?? run }, new() { value }) });
        bool Valid(FeatureLedgerEvidence.Row value, FeatureLedgerEvidence.Run? suppliedRun = null) =>
            Registry(value, suppliedRun).IsVerified(value.ObservationId, "evidence:fixture:" + value.ObservationId);
        Check("a well-formed current receipt with its exact green assertion is accepted", Valid(row));
        Check("source relocation without execution stays OPEN", !Valid(row with { Proofs = new() }));
        Check("receipt rejects an unknown execution target", !Valid(row with { Proofs = new() { proof with { Target = "InventedMethod" } } }));
        Check("receipt rejects a missing assertion", !Valid(row with { Proofs = new() { proof with { Assertion = "not executed" } } }));
        Check("receipt rejects an unknown run", !Valid(row with { Proofs = new() { proof with { RunId = "missing" } } }));
        Check("receipt rejects a missing implementation", !Valid(row with { Implementation = new() }));
        Check("receipt rejects an altered implementation hash", !Valid(row with { Implementation = new() { source with { Sha256 = new string('0', 64) } } }));
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
