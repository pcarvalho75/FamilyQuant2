using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.IO;
using System.Text;
using FamilyQuant.Evidence;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelAuditCurrentCommands() =>
    [
        new("--panel-audit-excel-fixture", 82, TestCommandPhase.Raw,
            args => args.Length == 2 && args[0] == "--panel-audit-excel-fixture", (args, _) =>
            {
                PanelAuditCurrentTests(args[1]);
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--test-panel-audit-current", 81, TestCommandPhase.Raw,
            args => args.Contains("--test-panel-audit-current", StringComparer.Ordinal), (_, _) =>
            {
                PanelAuditCurrentTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void PanelAuditCurrentTests(string? output = null)
    {
        var file = new FamilyClusterFile
        {
            FamilyPanel = new FamilyPanel { Families = new() { new() { Key = "f", DisplayName = "Family, A" } } },
            MyExtendedFastaItems = new()
            {
                new() { SequenceIdentifier = "sp|P12345|ONE", Description = "Protein one OS=source", Sequence = "ACDEFGHIK", UserID = 1 },
                new() { SequenceIdentifier = "outside", Description = "Outside", Sequence = "ACDEFGHIK", UserID = 2 },
                new() { SequenceIdentifier = "Reverse_decoy", Description = "Decoy", Sequence = "ACDEFGHIK", UserID = 3 },
            },
            PanelProteinAssignments = new()
            {
                new() { ProteinIndex = 0, Identifier = "sp|P12345|ONE", FamilyUserId = 1, PanelFamilyKey = "f", IsApplied = true,
                    EvidenceKind = PanelAssignmentEvidenceKind.UniProtFamily | PanelAssignmentEvidenceKind.NearestReviewed | PanelAssignmentEvidenceKind.DomainArchitecture | PanelAssignmentEvidenceKind.ConfirmedLink,
                    SourceFamilies = new() { "source, family" }, NeighbourAccession = "P99999", IdentityPercent = 87.5, QueryCoveragePercent = 92.25,
                    ObservedPfam = new() { "PF12345" }, ReferencePfam = new() { "PF23456" }, LinkKeys = new() { "link, one" } },
                new() { ProteinIndex = 1, Identifier = "outside", FamilyUserId = 2, IsApplied = false,
                    EvidenceKind = PanelAssignmentEvidenceKind.NearestReviewed, NeighbourAccession = "P88888" },
            }
        };
        MakePanelAssignmentsCurrent(file, new EvidenceClassificationOptions());
        string Make(string culture)
        {
            var previous = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); return PanelAuditCsv.BuildCsv(file); }
            finally { CultureInfo.CurrentCulture = previous; }
        }
        string us = Make("en-US"), br = Make("pt-BR");
        if (output != null)
        {
            if (Directory.Exists(output)) throw new IOException("Use a new fixture output directory.");
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "en-US.csv"), us, new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(output, "pt-BR.csv"), br, new UTF8Encoding(true));
        }
        var rows = us.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Select(ParseCsvLine).ToArray();
        Check("J2c current assignment CSV is identical across cultures and contains every target", us == br && rows.Length == 3);
        Check("J2c exports actual applied evidence and family reason", rows[1][4].Contains("source, family", StringComparison.Ordinal)
            && rows[1][4].Contains("P99999", StringComparison.Ordinal) && rows[1][4].Contains("87.5", StringComparison.Ordinal)
            && rows[1][4].Contains("92.25", StringComparison.Ordinal) && rows[1][4].Contains("PF12345", StringComparison.Ordinal)
            && rows[1][4].Contains("link, one", StringComparison.Ordinal) && rows[1][5] == "Synthetic applied assignment");
        Check("J2c distinguishes a considered but unapplied candidate", rows[2][4].Contains("considered", StringComparison.OrdinalIgnoreCase)
            && rows[2][4].Contains("P88888", StringComparison.Ordinal) && rows[2][5] == "Synthetic applied assignment");
        file.MyExtendedFastaItems[0].Sequence = "CHANGED";
        var stale = PanelAuditCsv.BuildCsv(file).Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Select(ParseCsvLine).ToArray();
        Check("J2c does not present stale family assignment facts as current", !stale[1][4].Contains("P99999", StringComparison.Ordinal)
            && stale[1][5].Contains("out of date", StringComparison.OrdinalIgnoreCase));
    }
}
