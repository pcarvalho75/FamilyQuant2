using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyQuant.Panels;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelAuditCommandTable() =>
    [
        new("--test-panel-audit", 73, TestCommandPhase.Raw,
            args => args.Contains("--test-panel-audit", StringComparer.Ordinal), (_, _) =>
            {
                PanelAuditTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void PanelAuditTests()
    {
        var type = typeof(FamilyClusterFile).Assembly.GetType("FamilyQuant.PanelAuditCsv");
        var build = type?.GetMethod("BuildCsv", new[] { typeof(FamilyClusterFile) });
        Check("J2c panel audit export is available", build != null);
        if (build == null) return;
        var file = new FamilyClusterFile
        {
            FamilyPanel = new FamilyPanel { Families = new() { new FamilyPanelFamily { Key = "f", DisplayName = "Family, A",
                Subclasses = new() { new FamilyPanelSubclass { Key = "s", DisplayName = "Part \"one\"", RequiredPfam = new() { "PF00001" } } } } } },
            MyExtendedFastaItems = new()
            {
                new FastaItemExtended { SequenceIdentifier = "sp|P12345|ONE", Description = "Name, first OS=x", Sequence = "ACDEFGHIK" },
                new FastaItemExtended { SequenceIdentifier = "Reverse_sp|P12345|ONE", Description = "decoy", Sequence = "ACDEFGHIK" },
                new FastaItemExtended { SequenceIdentifier = "other", Description = "Other", Sequence = "ACDEFGHIK" },
            },
            ProteinSubclassLabels = new() { new ProteinSubclassLabel { ProteinIndex = 0, Identifier = "sp|P12345|ONE",
                PanelFamilyKey = "f", SubclassKey = "s", Status = ProteinSubclassStatus.Assigned, Reason = "Rule, \"matched\"" } }
        };
        string Make(string culture)
        {
            var old = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture); return (string)build.Invoke(null, new object[] { file })!; }
            finally { CultureInfo.CurrentCulture = old; }
        }
        file.MyExtendedFastaItems[0].UserID = 7;
        file.MyExtendedFastaItems[2].UserID = 8;
        file.MyExtendedFastaItems[0].Evidence = new Evidence.ProteinEvidence { Reviewed = true, FamilyAnnotation = "Recorded only", Subfamily = "Group", Pfam = new() { new Evidence.EvidenceDomain { Accession = "PF00001" } } };
        file.PanelProteinAssignments = new()
        {
            new() { ProteinIndex = 0, Identifier = file.MyExtendedFastaItems[0].SequenceIdentifier, FamilyUserId = 7, PanelFamilyKey = "f", IsApplied = true },
            new() { ProteinIndex = 2, Identifier = file.MyExtendedFastaItems[2].SequenceIdentifier, FamilyUserId = 8, IsApplied = false },
        };
        MakePanelAssignmentsCurrent(file, new Evidence.EvidenceClassificationOptions { ScopeTerms = new[] { "selected" } });
        var candidate = SubclassAssignment.ComputeCurrent(file);
        SubclassAssignment.CommitCandidate(file, candidate);
        string us = Make("en-US"), br = Make("pt-BR");
        var lines = us.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        Check("J2c output is identical under en-US and pt-BR", us == br);
        Check("J2c emits one row per target including unassigned proteins", lines.Length == 3);
        var row = ParseCsvLine(lines[1]);
        Check("J2c recorded annotation is not asserted as assignment evidence", row[4] == "" && row[5] == "Synthetic applied assignment" && row[7].Contains("Recorded only", StringComparison.Ordinal));
        Check("J2c preserves quoted names, panel labels and the subclass reason",
            row.Contains("P12345") && row.Contains("Name, first") && row.Contains("Family, A")
            && row.Contains("Part \"one\"") && row.Any(x => x.Contains("user-defined subclass rule", StringComparison.Ordinal)));
        var supplied = PanelAuditCsv.BuildCsv(file, new[] { new PanelFamilyAuditEvidence { ProteinIndex = 0,
            Identifier = "sp|P12345|ONE", EvidenceUsed = "source, line\r\nnext", Reason = "=formula" } });
        Check("J2c quotes embedded newline and guards spreadsheet formulas",
            supplied.Contains("\"source, line\r\nnext\"", StringComparison.Ordinal) && supplied.Contains("'=formula", StringComparison.Ordinal));
        bool refused = false;
        try { PanelAuditCsv.BuildCsv(file, new[] { new PanelFamilyAuditEvidence { ProteinIndex = 0, Identifier = "stale" } }); }
        catch (InvalidOperationException) { refused = true; }
        Check("J2c refuses stale protein joins", refused);
    }
}

