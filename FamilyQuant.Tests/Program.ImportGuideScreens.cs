using FamilyQuant.Evidence;
using FamilyQuant.Examples;
using FamilyQuant.Panels;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

// Native captures for the current three-page import guide, using an owned synthetic workbook.
internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ImportGuideScreenCommands() =>
    [
        new("--import-guide-screens", 10100, TestCommandPhase.Raw, args => args.Contains("--import-guide-screens", StringComparer.Ordinal), (args, _) =>
        {
            ImportGuideScreens(args[Array.IndexOf(args, "--import-guide-screens") + 1], args.Contains("--dark", StringComparer.Ordinal));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
    ];

    private static FastaItemExtended GuideProtein(int id, string identifier, string description, string family, bool reviewed, params string[] keywords) => new()
    {
        SequenceIdentifier = identifier, Description = description, Sequence = "ACDEFGHIKLMNPQRSTVWY", UserID = id, USearchID = id,
        Evidence = new ProteinEvidence { Accession = identifier.Split('|')[1], Family = family, Reviewed = reviewed, Fragment = false, Keywords = keywords.ToList() },
    };

    // Z16a8: stored Pfam domains for the invented proteins, so the domains written in the example's rows meet stored entries.
    private static FastaItemExtended WithPfam(FastaItemExtended protein, params (string Accession, string Name)[] domains)
    {
        protein.Evidence!.Pfam = domains.Select(d => new EvidenceDomain { Accession = d.Accession, Name = d.Name }).ToList();
        return protein;
    }

    private static void ImportGuideScreens(string output, bool dark)
    {
        EnsureApplication();
        using var dialogs = new ImportFixtureDialogs();
        string work = Path.Combine(Path.GetFullPath(output), "example-workbook");
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, "family-list-template.xlsx");
        PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, new[] { PanelImportTemplate.Columns.Select(column => column.Role switch
        {
            PanelImportFields.FamilyKey => "A", PanelImportFields.DisplayName => "First family", PanelImportFields.Pfam => "PF12345", _ => "",
        }).ToArray() });
        var file = ManualChoiceFile();
        file.MyExtendedFastaItems[0].Evidence!.Pfam = new() { new() { Accession = "PF12345", Name = "First domain" } };
        string before = file.ToJsonText(), source = TestFileHash.Upper(workbook);
        var previous = Themes.ThemeManager.Current;
        var theme = dark ? Themes.AppTheme.Dark : Themes.AppTheme.Light;
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)),
            workbookSource: new FamilyListWorkbookCoreSource(), openWorkbookReview: true);
        var window = new FamilyListImportWindow(model, templateWriter: PanelImportTemplate.Write)
        { Width = 1366, Height = 820, Left = -30000, Top = -30000, WindowStyle = WindowStyle.None,
            WindowStartupLocation = WindowStartupLocation.Manual, ShowActivated = false, ShowInTaskbar = false };
        try
        {
            Themes.ThemeManager.Apply(theme); window.Show();
            FinishReviewWork(() => model.LoadAsync(workbook));
            Directory.CreateDirectory(output);
            void Shot(string name)
            {
                PumpDispatcher(); window.UpdateLayout();
                ScreenHarness.Save(window, Path.Combine(output, name + ".png"), 1366, 820);
            }
            Shot("02-file-and-sheet");
            FinishReviewWork(async () => { await model.NextAsync(); }); Shot("03-columns");
            FinishReviewWork(async () => { await model.NextAsync(); }); Shot("06-summary");
            Check("guide captures the current three-page path without importing or editing its source", model.Page == 5 && model.Steps.Count == 3
                && file.ToJsonText() == before && TestFileHash.Upper(workbook) == source);
        }
        finally { window.Close(); Themes.ThemeManager.Apply(previous); }
    }
}
