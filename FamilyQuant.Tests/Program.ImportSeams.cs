using FamilyQuant.Evidence;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportSeamCommands() =>
    [new("--test-panel-visible-links", 100, TestCommandPhase.Raw, args => args is ["--test-panel-visible-links"], (_, _) =>
        {
            PanelVisibleLinkTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private sealed class ImportFixtureDialogs : IDisposable
    {
        private readonly Func<Window?, ShellMessage, MessageBoxResult>? previous = ShellMessages.Presenter;
        public ImportFixtureDialogs() => ShellMessages.Presenter = (_, _) => MessageBoxResult.Yes;
        public void Dispose() => ShellMessages.Presenter = previous;
    }

    private static void PanelVisibleLinkTests()
    {
        Section("RG4 only current named choices reach visible source-family cells");
        var panel = new FamilyPanel { Families = new() { new() { Key = "A", DisplayName = "First family",
            Pfam = new() { new() { Accession = "PF12345" } }, UniProtFamilies = new() { "Written family" } } } };
        string current = PanelEvidenceClassifier.DefinitionFingerprint(panel);
        panel.EvidenceLinks = new()
        {
            new() { SourceFamily = "Current family", PanelFamilyKey = "A", PanelDefinitionFingerprint = current },
            new() { SourceFamily = "CURRENT FAMILY", PanelFamilyKey = "A", PanelDefinitionFingerprint = current },
            new() { SourceFamily = "Stale family", PanelFamilyKey = "A", PanelDefinitionFingerprint = "old-definition" },
            new() { SourceFamily = "Outside family", PanelFamilyKey = null, PanelDefinitionFingerprint = current },
            new() { SourceFamily = "", ArchitectureKey = "[\"PF12345\"]", PanelFamilyKey = "A", PanelDefinitionFingerprint = current },
        };
        string before = System.Text.Json.JsonSerializer.Serialize(panel);
        string path = Path.Combine(ImportSimpleWork("IM-seams"), "links.xlsx");
        FamilyPanelTemplate.Write(path, panel);
        var row = new PanelSpreadsheetReader().ReadTable(path, "Families", 2).Rows.Single();
        var restored = FamilyPanelTemplate.Read(path);
        Check("RG4 visible labels omit stale, leave-out and architecture-only links without changing hidden history",
            row.Cells["UniProt family"] == "Written family; Current family"
            && System.Text.Json.JsonSerializer.Serialize(restored.EvidenceLinks) == System.Text.Json.JsonSerializer.Serialize(panel.EvidenceLinks)
            && System.Text.Json.JsonSerializer.Serialize(panel) == before);
        panel.Families[0].Pfam.Add(new() { Accession = "PF23456" });
        FamilyPanelTemplate.Write(path, panel);
        row = new PanelSpreadsheetReader().ReadTable(path, "Families", 2).Rows.Single();
        Check("RG4 editing the panel definition stops previously current choices being promoted",
            row.Cells["UniProt family"] == "Written family" && FamilyPanelTemplate.Read(path).EvidenceLinks.Count == 5);
    }
}
