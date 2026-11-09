using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static class GroupingReviewScreens
{
    internal const string Argument = "--grouping-review-screens";

    internal static EvidenceReviewInput Fixture(int count = 31777)
    {
        var proteins = Enumerable.Range(0, count).Select(i => new ReviewProteinInput(i,
            i == 5 ? "sp|P0DW29|PROVIDED" : "sp|P31987|presentation_" + i, 100 + i % 2, i == 0 ? "" : "Supplied family " + i % 2,
            i % 2 == 0 ? "UniProt family" : "Reviewed neighbour", "Supplied reason for presentation row " + i,
            i % 2 == 0, "P31987; 72% identity; 85% of query aligned", i % 11 == 0 ? new[] { "Supplied evidence flag" } : Array.Empty<string>(),
            i > 0 && i % 11 != 0, i == 5 ? "Morulustatin" : "Presentation fixture protein " + i, "Supplied domain", IsLocked: i == 3,
            Reviewed: i % 2 == 0 || i == 5, HasPfamCalls: i != 3 && i != 5)).ToArray();
        var families = proteins.Where(p => p.Family.Length > 0).GroupBy(p => p.Family).Select(g =>
            new ReviewFamilyInput(g.Key, g.Key, g.Count(), 1)).ToArray();
        return new EvidenceReviewInput("presentation fixture, no scientific result", proteins, families,
            new[] { new ReviewMarkerInput("PF-supplied", "Supplied marker", families[0].Family, 12, 15, 13, 12.0 / 13) },
            families.Select(f =>
            {
                var members = proteins.Where(p => p.Family == f.Family).ToArray();
                var noCall = members[2];
                var first = members.Where((p, i) => i != 2 && i % 2 == 0).ToArray();
                var second = members.Where((p, i) => i != 2 && i % 2 != 0).ToArray();
                return new ReviewArchitectureInput(f.Family, true, new[]
                {
                    new ReviewArchitectureGroupInput("supplied group 1", "Supplied split 1", "PF-one", new[] { "PF-one" }, first.Select(p => p.ProteinIndex).ToArray(), first.Count(p => p.Reviewed == true), first.Where(p => p.HasPfamCalls == false).Select(p => p.ProteinIndex).ToArray()),
                    new ReviewArchitectureGroupInput("supplied group 2", "Supplied split 2", "PF-two", new[] { "PF-one", "PF-two" }, second.Select(p => p.ProteinIndex).ToArray(), second.Count(p => p.Reviewed == true), second.Where(p => p.HasPfamCalls == false).Select(p => p.ProteinIndex).ToArray()),
                    new ReviewArchitectureGroupInput("[]", "No domain calls", "", Array.Empty<string>(), new[] { noCall.ProteinIndex }, noCall.Reviewed == true ? 1 : 0)
                }, new[] { new ReviewArchitectureUndeterminedInput(noCall.ProteinIndex, "Provided reason: no learned range placed this member.") });
            }).ToArray(), new[] { new ReviewLeftOutInput(families[0].Family, "P31987", proteins[0].Identifier,
                proteins[0].Description, 71, false, false, "PF-supplied", "Supplied marker", "", "Supplied marker-carrier reason") }, SourceLabel: "Presentation fixture.");
    }

    internal static int Run(string output, string? fcfPath)
    {
        App? app = null;
        try
        {
            output = Path.GetFullPath(output);
            if (fcfPath != null && Path.GetFullPath(fcfPath).StartsWith(output.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Keep review renders separate from the read-only file.");
            string? Hash() { if (fcfPath == null) return null; using var input = File.OpenRead(fcfPath); return Convert.ToHexString(SHA256.HashData(input)); }
            string? before = Hash();
            var real = fcfPath == null ? null : FamilyClusterFile.Load(fcfPath);
            Directory.CreateDirectory(output);
            string manifestPath = Path.Combine(output, "review-screens.json");
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            app = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown }; app.InitializeComponent();
            var captures = new List<object>();
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var keyword = new SuperClusterPreview(new List<FastaItemExtended>
                {
                    new() { SequenceIdentifier = "presentation_1", Description = "Example description", Sequence = "PEPTIDE", UserID = 1 },
                    new() { SequenceIdentifier = "presentation_2", Description = "Example second description", Sequence = "PEPTIDEK", UserID = 2 }
                }, null, "Example", "", KeywordMatch.Any, null, null);
                var fixture = new SuperClusterPreviewWindow(keyword, Fixture(), null,
                    _ => "Presentation fixture, no scientific result\nFamily\tCarriers\tComplete\nSupplied family 0\t2\t3\nSupplied family 1\t1\t4");
                fixture.ShowAllOptions = true;
                var model = (GroupingReviewViewModel)fixture.DataContext;
                model.MotifPattern = "user supplied pattern";
                var previousContext = SynchronizationContext.Current;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                try
                {
                    var check = model.CheckMotifAsync();
                    while (!check.IsCompleted)
                    {
                        var frame = new DispatcherFrame();
                        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                        Dispatcher.PushFrame(frame);
                    }
                    check.GetAwaiter().GetResult();
                }
                finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
                Capture(fixture, "fixture-proteins", "Synthetic presentation fixture, 31,777 rows; no scientific result");
                var tabs = (TabControl)fixture.FindName("evidenceTabs");
                for (int i = 1; i < tabs.Items.Count; i++)
                {
                    tabs.SelectedIndex = i;
                    if (((TabItem)tabs.Items[i]).Header as string == "Architecture")
                    {
                        var architecture = model.Evidence!.SelectedArchitecture!;
                        architecture.SelectedGroup = architecture.Groups.FirstOrDefault(g => g.InferredProteins > 0) ?? architecture.Groups[0];
                    }
                    Capture(fixture, "fixture-" + ((TabItem)tabs.Items[i]).Header.ToString()!.ToLowerInvariant().Replace(' ', '-'), "Synthetic presentation fixture; no scientific result");
                }
                fixture.Close();
                if (real != null)
                {
                    var options = new Evidence.EvidenceClassificationOptions { ScopeTerms = new[] { "presentation fixture scope" } };
                    var proposal = Evidence.EvidenceClassifier.Propose(real.MyExtendedFastaItems, options);
                    var window = new SuperClusterPreviewWindow(keyword, EvidenceProposalPresentation.Present(proposal, real), null,
                        initialScope: string.Join(", ", options.ScopeTerms));
                    ((GroupingReviewViewModel)window.DataContext).Evidence!.SelectedFamily = null;
                    Capture(window, "real-ana-proteins", "Read-only original file with E5 proposal for an explicit presentation-only scope; no fetch, search or apply");
                    window.Close();
                }

                void Capture(SuperClusterPreviewWindow window, string screen, string source)
                {
                    if (!window.IsLoaded)
                    {
                        window.WindowStartupLocation = WindowStartupLocation.Manual; window.Left = -30000; window.Top = -30000;
                        window.WindowStyle = WindowStyle.None; window.ResizeMode = ResizeMode.NoResize;
                        window.ShowActivated = false; window.ShowInTaskbar = false; window.Show();
                    }
                    foreach (var size in new[] { (Width: 1366, Height: 820), (Width: 1024, Height: 768) })
                    {
                        window.Width = size.Width; window.Height = size.Height;
                        for (int i = 0; i < 3; i++)
                        {
                            var frame = new DispatcherFrame();
                            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                            Dispatcher.PushFrame(frame);
                        }
                        string path = Path.Combine(theme.ToString().ToLowerInvariant(), size.Width + "x" + size.Height, screen + ".png");
                        string full = Path.Combine(output, path); Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                        ScreenHarness.Save(window, full, size.Width, size.Height);
                        captures.Add(new { Path = path.Replace('\\', '/'), Source = source, size.Width, size.Height });
                        Console.WriteLine(path);
                    }
                }
            }
            if (before != Hash()) throw new InvalidOperationException("The read-only source changed during capture.");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
            { Source = fcfPath, SourceSha256 = before, InputsUnchanged = true, Captures = captures }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally { if (app != null) { foreach (Window window in app.Windows.Cast<Window>().ToArray()) window.Close(); app.Shutdown(); } }
    }
}
