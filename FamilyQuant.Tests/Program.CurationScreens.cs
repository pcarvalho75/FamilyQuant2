using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static int CurationScreens(string output, string input)
    {
        EnsureApplication();
        output = Path.GetFullPath(output); input = Path.GetFullPath(input);
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            throw new ArgumentException("Use a fresh output directory for curation captures.");
        string Hash() { using var stream = File.OpenRead(input); return Convert.ToHexString(SHA256.HashData(stream)); }
        string before = Hash();
        Directory.CreateDirectory(output);
        var captures = new List<object>();
        foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
        foreach (var size in new[] { (1024, 768), (1366, 820) })
        {
            FamilyQuant.Themes.ThemeManager.Apply(theme);
            var screen = new ClusterGenerationControl(FamilyClusterFile.Load(input));
            var window = new Window { Content = screen, Width = size.Item1, Height = size.Item2,
                Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual, WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize };
            string name = theme.ToString().ToLowerInvariant() + "-" + size.Item1 + "x" + size.Item2 + ".png";
            window.Show();
            try
            {
                PumpDispatcher(); window.UpdateLayout();
                ScreenHarness.Save(window, Path.Combine(output, name), size.Item1, size.Item2);
                captures.Add(new { file = name, theme = theme.ToString(), width = size.Item1, height = size.Item2,
                    proteinsInFile = screen.fcf.MyExtendedFastaItems.Count, visibleProteins = screen.ViewModel.Rows.Count,
                    state = "Stored curation only; no RAW or quantitation recomputed" });
            }
            finally { window.Close(); }
        }
        if (Hash() != before) throw new InvalidOperationException("The capture input changed during the run.");
        File.WriteAllText(Path.Combine(output, "curation-screens.json"), JsonSerializer.Serialize(new
        {
            input, sha256 = before, dpi = 96, captures
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Curation: 4 native captures, dark/light at both client sizes; input unchanged: " + output);
        return 0;
    }
}
