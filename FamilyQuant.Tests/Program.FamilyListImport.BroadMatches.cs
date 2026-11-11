using FamilyQuant.Evidence;
using FamilyQuant.Panels.Import;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FamilyListImportBroadMatchTests()
    {
        var file = ManualChoiceFile();
        foreach (var protein in file.MyExtendedFastaItems)
        {
            protein.Description = "Broad shared keyword";
            protein.Evidence = new()
            {
                Family = "Stored broad family",
                Keywords = new() { "Broad shared keyword", "Another broad keyword" },
            };
        }
        file.MyExtendedFastaItems[0].Evidence!.Keywords.Add("Narrow control label");
        string before = file.ToJsonText();
        string work = Path.Combine(Path.GetTempPath(), "fq-wizard-broad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        string workbook = Path.Combine(work, "synthetic-broad-match.xlsx");
        PanelImportTemplate.Write(workbook);
        FillTemplate(workbook, new[] { new[] { "broad", "Broad shared keyword", "", "", "Unresolved domain", "Quantify" } });
        var model = new FamilyListImportViewModel(new FamilyListImportTransaction(new CurationViewModel(file)));
        var window = new FamilyListImportWindow(model)
        {
            Left = -30000, Top = -30000, WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false, ShowInTaskbar = false,
        };
        var prompts = new List<string>();
        try
        {
            window.Show();
            FinishReviewWork(() => model.LoadAsync(workbook));
            FinishReviewWork(async () => { await model.NextAsync(); await model.NextAsync(); await model.NextAsync(); });
            var family = model.MatchFamilies.Single();
            var broad = family.Suggestions.Where(s => s.Text is "Broad shared keyword" or "Another broad keyword" or "Stored broad family").ToArray();
            Check("Z12p5i wizard broad-label fixture starts with three unticked broad choices", broad.Length == 3 && broad.All(s => !s.Use));
            foreach (var suggestion in broad) suggestion.Use = true;
            var confirm = WalkTree(window).OfType<Button>().Single(button => Equals(button.Content, "Use selected matches"));
            window.BroadMatchConfirmationOverride = message =>
            {
                prompts.Add(message);
                return false;
            };
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var expected = new PanelEvidenceMatching(file).ConfirmationWarning(broad.Select(s =>
                PanelEvidenceMatching.ChoiceFromKey(s.Key) with { Value = s.Text }));
            Check("Z12p5i one combined prompt uses Core's exact counted warning for all chosen broad labels", prompts.Count == 1
                && prompts[0] == expected && broad.All(s => prompts[0].Contains(s.Text + " (3)", StringComparison.Ordinal)));
            Check("Z12p5i Cancel preserves the selected labels as an unconfirmed draft", family.Choice == "Needs a choice"
                && broad.All(s => s.Use) && !model.CanNext && file.ToJsonText() == before);
            window.BroadMatchConfirmationOverride = message => { prompts.Add(message); return true; };
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Z12p5i accepting the single warning confirms the draft without changing the project", prompts.Count == 2
                && family.Choice == "Confirmed" && model.CanNext && file.ToJsonText() == before);

            foreach (var suggestion in broad) suggestion.Use = false;
            var narrow = family.Suggestions.Single(s => s.Text == "Narrow control label");
            narrow.Use = true;
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Z12p5i a narrow selected label confirms without a broad-label prompt", prompts.Count == 2 && family.Choice == "Confirmed");
            narrow.Use = false;
            var edited = broad.Single(s => s.Text == "Broad shared keyword");
            edited.Text = "Edited label with no stored entries";
            edited.Use = true;
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Check("Z12p5i the warning uses edited label coverage rather than the original broad label", prompts.Count == 2
                && edited.Proteins == 0 && family.Choice == "Confirmed" && file.ToJsonText() == before);
            edited.Text = "Broad shared keyword";
            model.ConfirmMatches();
            Check("Z12p5i a direct wizard confirmation cannot accept a broad warning without an answer", family.Choice == "Needs a choice");
            model.ConfirmMatches(_ => { edited.Text = "Changed during the question"; return true; });
            Check("Z12p5i changing selected values during the question leaves the draft unconfirmed", family.Choice == "Needs a choice"
                && file.ToJsonText() == before);
            edited.Text = "Broad shared keyword";
            model.ConfirmMatches(_ => { file.MyExtendedFastaItems[0].Evidence!.Keywords.Add("Concurrent evidence edit"); return true; });
            Check("Z12p5i changed stored evidence after the answer refuses wizard confirmation", family.Choice == "Needs a choice"
                && model.Status.Contains("evidence changed", StringComparison.Ordinal));
        }
        finally
        {
            window.Close(); PumpDispatcher();
        }
    }
}
