using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Xml.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private sealed record AppTokenRole(string Id, string OldKey, string OldColor, string CurrentKey, string Role);

    private static int LookAppTokenCommand(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("Supply frozen source root, fresh output and date timestamp.");
        string root = FindLedgerRoot()!, output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be fresh.");
        Directory.CreateDirectory(output);
        string mappingPath = "docs/LOOK-APP-TOKEN-ROLES.json";
        var roles = JsonSerializer.Deserialize<List<AppTokenRole>>(File.ReadAllText(Path.Combine(root, mappingPath)), FeatureLedgerEvidence.JsonOptions)!;
        var inventory = JsonSerializer.Deserialize<FeatureLedger.Inventory>(File.ReadAllText(Path.Combine(root, "docs/feature-ledger-scan.json")))!;
        const string source = "FamilyQuant/App.xaml";
        string oldPath = Path.Combine(args[1], source);
        if (FeatureLedgerEvidence.Hash(oldPath) != inventory.Files.Single(f => f.Path == source).Sha256)
            throw new InvalidDataException("Frozen App source differs.");
        var before = XDocument.Load(oldPath);
        var actual = new List<object>();
        var assertions = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var original = Console.Out;
        using var transcript = new StringWriter();
        try
        {
            Console.SetOut(new LedgerProofWriter(original, transcript));
            EnsureApplication();
            foreach (var theme in new[] { Themes.AppTheme.Dark, Themes.AppTheme.Light })
            {
                Themes.ThemeManager.Apply(theme);
                var home = new HomeControl(); LayoutAt(home, new Size(1366, 900), -1);
                foreach (var role in roles.Where(r => r.OldKey != "GoldBrush"))
                {
                    var old = before.Descendants().Single(e => e.Name.LocalName == "SolidColorBrush" && StyleKey(e) == role.OldKey);
                    var row = inventory.Observations.Single(r => r.Id == role.Id);
                    var brush = (SolidColorBrush)Application.Current.FindResource(role.CurrentKey);
                    var build = (System.Windows.Controls.Button)home.FindName("ButtonHomeBuild");
                    var open = (System.Windows.Controls.Button)home.FindName("ButtonHomeOpen");
                    var project = (System.Windows.Controls.TextBlock)home.FindName("TextBlockHomeProject");
                    DependencyObject? projectSurface = project;
                    while (projectSurface != null && projectSurface is not System.Windows.Controls.Border)
                        projectSurface = VisualTreeHelper.GetParent(projectSurface);
                    // Explicit named consumers, not another element that happens to share a color.
                    Brush? consumer = role.CurrentKey switch
                    {
                        "CanvasBrush" => home.Background,
                        "SurfaceBrush" => (projectSurface as System.Windows.Controls.Border)?.Background,
                        "SubtleBrush" => build.Background,
                        "AccentBrush" => open.Background,
                        "TextBrush" => build.Foreground,
                        "MutedBrush" => project.Foreground,
                        _ => null
                    };
                    bool consumed = consumer is SolidColorBrush color && color.Color == brush.Color;
                    bool ok = row.Source == source && row.Kind == "option/copy" && row.Detail == "SolidColorBrush.Key = " + role.OldKey
                        && (string?)old.Attribute("Color") == role.OldColor && consumed;
                    string assertion = "App token role " + role.Id + " / " + theme + ": " + role.OldKey + " -> " + role.CurrentKey;
                    Check(assertion, ok, role.Role + "; old=" + role.OldColor + "; current=" + brush.Color);
                    if (!assertions.TryGetValue(role.Id, out var list)) assertions[role.Id] = list = new();
                    if (ok) list.Add(assertion);
                    actual.Add(new { role.Id, theme = theme.ToString(), role.OldKey, role.OldColor, role.CurrentKey, role.Role, resolved = brush.Color.ToString(), consumed });
                }
            }
            Themes.ThemeManager.Apply(Themes.AppTheme.Dark);
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "proofs.log"), data = Path.Combine(output, "resolved-tokens.json");
        File.WriteAllText(log, transcript.ToString(), new UTF8Encoding(false)); FeatureLedgerEvidence.WriteJson(data, actual);
        var paths = new List<string> { log, data };
        string archive = Path.Combine(output, "executed"); Directory.CreateDirectory(archive);
        foreach (string path in new[] { mappingPath, "FamilyQuant/Themes/Tokens.xaml", "FamilyQuant/Themes/ThemeManager.cs", "FamilyQuant/HomeControl.xaml", "FamilyQuant.Tests/Program.LookAppTokens.cs" })
        {
            string target = Path.Combine(archive, path.Replace('/', '_')); File.Copy(Path.Combine(root, path), target); paths.Add(target);
        }
        foreach (string path in new[] { typeof(Program).Assembly.Location, typeof(App).Assembly.Location, typeof(HelpContent).Assembly.Location })
        { string target = Path.Combine(archive, Path.GetFileName(path)); File.Copy(path, target); paths.Add(target); }
        using var git = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
        string commit = git.StandardOutput.ReadToEnd().Trim(); git.WaitForExit();
        var run = new FeatureLedgerEvidence.Run("LK-app-tokens", Environment.CommandLine, args[3], commit,
            paths.Select(p => new FeatureLedgerEvidence.Artifact(p, FeatureLedgerEvidence.Hash(p))).ToList(), root);
        var rows = roles.Select(role => new FeatureLedgerEvidence.Row(role.Id,
            new() { new("FamilyQuant/Themes/Tokens.xaml", role.CurrentKey, FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant/Themes/Tokens.xaml"))),
                new("FamilyQuant/Themes/ThemeManager.cs", "ThemeManager", FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant/Themes/ThemeManager.cs"))),
                new("FamilyQuant/HomeControl.xaml", role.Role, FeatureLedgerEvidence.Hash(Path.Combine(root, "FamilyQuant/HomeControl.xaml"))) },
            failed == 0 && assertions.TryGetValue(role.Id, out var checks) && checks.Count == 2
                ? checks.Select(a => new FeatureLedgerEvidence.Proof(run.Id, "test", nameof(LookAppTokenCommand), a, log, "")).ToList() : new(),
            role.OldKey == "GoldBrush" ? "OPEN: focus and removed step-number roles need their own explicit native checks." : "Intentional themed visual-role replacement, not old-color equality.",
            null, role.OldKey + "=" + role.OldColor + " -> " + role.CurrentKey + ": " + role.Role)).ToList();
        FeatureLedgerEvidence.WriteJson(Path.Combine(output, "LK-app-tokens.json"), new FeatureLedgerEvidence.Register(1, "LK-app-tokens", FeatureLedger.ReferenceCommit, new() { run }, rows));
        return failed == 0 ? 0 : 1;
    }
}
