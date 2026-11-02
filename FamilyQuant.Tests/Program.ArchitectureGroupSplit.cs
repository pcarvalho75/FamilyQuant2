using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void ArchitectureGroupSplitTests(string? output = null)
    {
        Section("Architecture choices - one group can leave while the rest stays together");
        Check("the C15 command is registered with its own raw handler", TestCommandRegistry.Commands.Any(c =>
            c.Flag == "--test-architecture-group-split" && c.Phase == TestCommandPhase.Raw && c.Order == 5
            && c.Run.Method.Name == nameof(RunArchitectureGroupSplitCommand)));
        EnsureApplication();
        var splitProperty = typeof(ReviewArchitectureRow).GetProperty("Split");
        Check("each supplied architecture group exposes its own split choice", splitProperty != null);
        if (splitProperty == null) return;
        var previousTheme = Themes.ThemeManager.Current;
        try
        {
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                FastaItemExtended Protein(int index, params string[] domains) => new()
                {
                    SequenceIdentifier = "sp|P0000" + index + "|FIXTURE", Description = "Supplied architecture member " + index,
                    Sequence = "ACDEFGHIK", UserID = 1, USearchID = 1,
                    Evidence = new ProteinEvidence { Accession = "P0000" + index, Family = "Supplied family", Reviewed = true,
                        Keywords = new() { "selected" }, Pfam = domains.Select(d => new EvidenceDomain { Accession = d, Name = d + " supplied domain" }).ToList() }
                };
                var file = new FamilyClusterFile { MyExtendedFastaItems = new()
                {
                    Protein(1, "binding", "catalytic"), Protein(2, "catalytic"), Protein(3, "binding")
                } };
                string before = file.ToJsonText();
                var session = new EvidenceGroupingSession(file, "C15 fixture");
                var input = session.Propose("selected");
                var window = new SuperClusterPreviewWindow(new SuperClusterPreview(new List<FastaItemExtended>(), null,
                    "", "", KeywordMatch.All, null, null), input, null, initialScope: "selected")
                    { Width = 1180, Height = 740, Left = -30000, Top = -30000,
                        WindowStartupLocation = WindowStartupLocation.Manual, ShowInTaskbar = false, ShowActivated = false };
                try
                {
                    var model = ((GroupingReviewViewModel)window.DataContext).Evidence!.Architectures.Single();
                    var free = model.Groups.Single(g => g.Source.Domains.SequenceEqual(new[] { "binding" }));
                    splitProperty.SetValue(free, true);
                    Check(theme + " capture marks only the chosen architecture key for splitting", model.Capture().Count(c => c.Split) == 1
                        && model.Capture().Single(c => c.Split).ArchitectureKey == free.Key);
                    Check(theme + " the bulk checkbox reports a mixed choice", model.SplitAll == null);
                    var blocked = new ArchitectureReviewViewModel(input.Architectures!.Single() with { CanSplit = false },
                        input.Proteins.ToDictionary(p => p.ProteinIndex), () => { });
                    splitProperty.SetValue(blocked.Groups[0], true);
                    Check(theme + " groups without a supplied split option cannot be selected", blocked.Capture().All(c => !c.Split));
                    var other = model.Groups.First(g => !ReferenceEquals(g, free));
                    string otherName = other.Name; other.Name = "";
                    Check(theme + " a retained group's unused split name does not block confirmation", model.IsValid);
                    other.Name = otherName;
                    string freeName = free.Name; free.Name = "";
                    Check(theme + " a chosen split still requires its name", !model.IsValid);
                    free.Name = freeName;
                    model.Split = true;
                    Check(theme + " the existing bulk split API still selects every supplied group", model.Capture().All(c => c.Split));
                    model.Split = false;
                    Check(theme + " clearing the bulk choice clears every group", model.Capture().All(c => !c.Split));
                    ((TabControl)window.FindName("evidenceTabs")).SelectedIndex = 1;
                    window.Show(); PumpDispatcher();
                    var control = (ArchitectureReviewControl)window.FindName("architectureReview");
                    var table = (DataGrid)control.FindName("groupGrid");
                    table.ScrollIntoView(free); PumpDispatcher();
                    var row = (DataGridRow)table.ItemContainerGenerator.ContainerFromItem(free);
                    var checkbox = WalkTree(row).OfType<CheckBox>().Single();
                    ((IToggleProvider)new CheckBoxAutomationPeer(checkbox).GetPattern(PatternInterface.Toggle)!).Toggle();
                    PumpDispatcher();
                    Check(theme + " the native group checkbox selects just that key", model.Capture().Count(c => c.Split) == 1
                        && model.Capture().Single(c => c.Split).ArchitectureKey == free.Key);
                    if (output != null)
                    {
                        System.IO.Directory.CreateDirectory(output);
                        System.Threading.Thread.Sleep(350); PumpDispatcher();
                        ScreenHarness.Save(window, System.IO.Path.Combine(output, "split-" + theme + ".png"), 1180, 740);
                    }
                    ((Button)window.FindName("createButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    if (window.EvidenceSelection == null) { Check(theme + " Apply returns a reviewed architecture selection", false); continue; }
                    var applied = session.Apply(window.EvidenceSelection);
                    var proteins = file.MyExtendedFastaItems;
                    Check(theme + " the free-domain member leaves while combined and catalytic-only members stay together",
                        proteins[0].UserID == proteins[1].UserID && proteins[2].UserID != proteins[0].UserID
                        && applied.Recipe.ArchitectureChoices.Count(c => c.Split) == 1);
                    var replay = FamilyClusterFile.FromJsonText(before);
                    EvidenceApply.ApplyRecipe(replay, applied.Recipe);
                    Check(theme + " per-key recipe replay is byte exact", replay.ToJsonText() == file.ToJsonText());
                }
                finally { window.Close(); PumpDispatcher(); }
            }
        }
        finally { Themes.ThemeManager.Apply(previousTheme); }
    }
}
