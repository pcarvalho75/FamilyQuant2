using FamilyQuant.DeNovo;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using ThermoFisher.CommonCore.Data.Business;
using ThermoFisher.CommonCore.RawFileReader;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void DependencyCacheTests(string workDir)
        {
            Directory.CreateDirectory(workDir);
            Check("X11: Core no longer references Newtonsoft", !typeof(DeNovoXicCache).Assembly
                .GetReferencedAssemblies().Any(a => a.Name == "Newtonsoft.Json"));
            Check("X11: App no longer references Toolkit", !typeof(MainWindow).Assembly
                .GetReferencedAssemblies().Any(a => a.Name?.StartsWith("Xceed.Wpf", StringComparison.Ordinal) == true));
            string path = Path.Combine(workDir, "x11-legacy-cache.json");
            // Historical Json.NET v4 shape, including subnormal and maximal finite IEEE doubles.
            File.WriteAllText(path, """
                {"version":4,"runName":"legacy","ppm":10.0,"ms1Tolerance":2,
                 "isotopeCount":3,"minPointsForQuant":3,"sourceSha256":"source",
                 "entries":{"12|500.12345|2":{"RunName":"legacy","ScanNoMS2":12,
                 "PrecursorMz":500.12345,"Charge":2,"Tag":"<tag>",
                 "IsotopeAreas":[1.7976931348623157E+308,4.9406564584124654E-324,-1.0],
                 "IsotopePointCounts":[5,4,0],"UnderSampledArea":0.1,
                 "UnderSampledIsotopes":1,"UnderSampledIsEstimate":true,"AreaMMinus1":-1.0,
                 "Failure":null}}}
                """);
            var cache = DeNovoXicCache.Load(path, "legacy", 10, 2, 3, 3, "source");
            bool found = cache.TryGet(12, 500.12345, 2, out var row);
            Check("X11: legacy cache preserves exact doubles and fields", found && row != null &&
                row.IsotopeAreas.SequenceEqual(new[] { double.MaxValue, double.Epsilon, -1.0 }) &&
                row.IsotopePointCounts.SequenceEqual(new[] { 5, 4, 0 }) && row.Tag == "<tag>" &&
                row.UnderSampledArea == 0.1 && row.UnderSampledIsEstimate && row.Failure == null);
            if (row != null)
            {
                cache.Put(row);
                cache.Save();
                byte[] first = File.ReadAllBytes(path);
                cache.Put(row);
                cache.Save();
                Check("X11: repeated cache writes are byte identical", first.SequenceEqual(File.ReadAllBytes(path)));
                using var json = JsonDocument.Parse(first);
                var entry = json.RootElement.GetProperty("entries").GetProperty("12|500.12345|2");
                Check("X11: cached accessors remain omitted", !entry.TryGetProperty("AreaM0", out _) &&
                    !entry.TryGetProperty("AreaM1", out _) && !entry.TryGetProperty("AreaM2", out _) &&
                    !entry.TryGetProperty("EnvelopeSum", out _));
                Check("X11: cache round trip preserves double bits", DeNovoXicCache.Load(path, "legacy", 10, 2, 3, 3, "source")
                    .TryGet(12, 500.12345, 2, out var reloaded) && reloaded != null &&
                    row.IsotopeAreas.Select(BitConverter.DoubleToInt64Bits)
                        .SequenceEqual(reloaded.IsotopeAreas.Select(BitConverter.DoubleToInt64Bits)));
            }
            Check("X11: legacy cache rejects stale source", DeNovoXicCache.Load(path, "legacy", 10, 2, 3, 3, "other").Count == 0);
            Check("X11: legacy cache rejects wrong isotope policy", DeNovoXicCache.Load(path, "legacy", 10, 2, 6, 3, "source").Count == 0);
            File.WriteAllText(path, "{broken");
            Check("X11: corrupt cache remains recoverable", DeNovoXicCache.Load(path, "legacy", 10, 2).Count == 0);

            var detectorType = typeof(Ms1EnvelopeDetector);
            var load = detectorType.GetMethod("TryLoadCache", BindingFlags.Static | BindingFlags.NonPublic)!;
            var save = detectorType.GetMethod("SaveCache", BindingFlags.Static | BindingFlags.NonPublic)!;
            File.WriteAllText(path, """
                {"version":3,"runName":"legacy","sourceSha256":"source","ppmTolerance":10.0,
                 "minCharge":1,"maxCharge":5,"isotopeCeiling":4,"minIsotopes":3,"minScansPerFeature":2,
                 "detectedEnvelopeCount":2,"detectedPeptideLikeArea":0.30000000000000004,"ms1ScanCount":4,
                 "detectedAreaByCharge":[0.0,0.1,0.2,0.0,0.0,0.0],"detectedCountByCharge":[0,1,1,0,0,0],
                 "detectedAreaMultiplyCharged":0.2,"detectedAreaSinglyCharged":0.1,
                 "detectedCountMultiplyCharged":1,"detectedCountSinglyCharged":1}
                """);
            object?[] arguments = { path, "legacy", "source", null };
            bool loaded = (bool)load.Invoke(null, arguments)!;
            var detection = arguments[3] as Ms1EnvelopeDetectionResult;
            Check("X11: legacy MS1 cache retains charge split and exact total", loaded && detection != null &&
                detection.DetectedPeptideLikeArea == 0.30000000000000004 &&
                detection.DetectedAreaByCharge.SequenceEqual(new[] { 0.0, 0.1, 0.2, 0.0, 0.0, 0.0 }));
            if (detection != null)
            {
                save.Invoke(null, new object[] { path, detection, "source" });
                byte[] first = File.ReadAllBytes(path);
                save.Invoke(null, new object[] { path, detection, "source" });
                Check("X11: repeated MS1 cache writes are byte identical", first.SequenceEqual(File.ReadAllBytes(path)));
                arguments[3] = null;
                Check("X11: MS1 cache round trip preserves total bits", (bool)load.Invoke(null, arguments)! &&
                    arguments[3] is Ms1EnvelopeDetectionResult again &&
                    BitConverter.DoubleToInt64Bits(again.DetectedPeptideLikeArea) ==
                    BitConverter.DoubleToInt64Bits(detection.DetectedPeptideLikeArea));
            }
            NumericControlTests();
            UseSearchBinaryTests();
        }

        private static void NumericControlTests()
        {
            EnsureApplication();
            var view = new USearchLib();
            var host = new Window { Content = view, Width = 700, Height = 600, Left = -30000, Top = -30000,
                ShowActivated = false, ShowInTaskbar = false };
            host.Show();
            try
            {
            PumpDispatcher();
            var integer = (IntegerUpDown)view.FindName("IntegerUpDownIdentity");
            var fraction = (DecimalUpDown)view.FindName("DecimalUpDownMaxFDR");
            Check("X11: numeric defaults and bounds are retained", integer.Value == 85 && integer.Minimum == 1 &&
                integer.Maximum == 100 && fraction.Value == 0m && fraction.Maximum == 0.5m && fraction.Increment == 0.01m);
            integer.Step(-1);
            Check("X11: integer stepping preserves the two-way binding", integer.Value == 84 && view.ViewModel.Identity == 84);
            view.ViewModel.Identity = 91;
            PumpDispatcher();
            Check("X11: source changes still reach the typed integer editor", integer.Value == 91);
            fraction.Step(1);
            Check("X11: decimal stepping preserves the two-way binding", fraction.Value == 0.01m && view.ViewModel.MaximumMixing == 0.01m);
            fraction.Value = 0.5m;
            fraction.Step(1);
            Check("X11: decimal upper bound is enforced", fraction.Value == 0.5m && view.ViewModel.MaximumMixing == 0.5m);
            integer.Value = 1;
            integer.Step(-1);
            Check("X11: integer lower bound is enforced", integer.Value == 1 && view.ViewModel.Identity == 1);
            integer.Measure(new Size(100, 40));
            integer.Arrange(new Rect(0, 0, 100, 40));
            integer.ApplyTemplate();
            var editor = (TextBox)integer.Template.FindName("PART_Text", integer);
            editor.Text = "77";
            Check("X11: typed integer updates the existing binding", integer.Value == 77 && view.ViewModel.Identity == 77);
            editor.Text = "77.5";
            Check("X11: fractional or invalid integer input does not change the option", integer.Value == 77);
            ((RepeatButton)integer.Template.FindName("PART_Up", integer)).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Check("X11: spinner button updates and normalizes the editor", integer.Value == 78 && editor.Text == "78");
            foreach (var theme in new[] { FamilyQuant.Themes.AppTheme.Dark, FamilyQuant.Themes.AppTheme.Light })
            {
                FamilyQuant.Themes.ThemeManager.Apply(theme);
                PumpDispatcher();
                Check("X11: numeric text follows " + theme + " input token",
                    (editor.Background as SolidColorBrush)?.Color ==
                    ((SolidColorBrush)Application.Current.FindResource("InputBrush")).Color);
            }
            FamilyQuant.Themes.ThemeManager.Apply(FamilyQuant.Themes.AppTheme.Dark);
            }
            finally { host.Close(); }
        }

        private static int DependencyFileCheck(string[] args)
        {
            if (args.Length != 4) throw new ArgumentException("Supply RAW, .xic2 and new report path.");
            if (File.Exists(args[3])) throw new IOException("Report already exists.");
            using var raw = RawFileReaderAdapter.FileFactory(args[1]);
            if (!raw.IsOpen || raw.IsError) throw new IOException("RAW reader refused the file.");
            raw.SelectInstrument(Device.MS, 1);
            int first = raw.RunHeaderEx.FirstSpectrum;
            int last = raw.RunHeaderEx.LastSpectrum;
            var stats = raw.GetScanStatsForScanNumber(first);
            if (last < first || stats.TIC <= 0) throw new IOException("RAW scan data unavailable.");
            var quants = PatternLabRunQuants.FromXic2(args[2]);
            if (!quants.IsAvailable) throw new IOException(quants.Refusal);
            string report = JsonSerializer.Serialize(new { Raw = Path.GetFullPath(args[1]), FirstScan = first,
                LastScan = last, FirstScanTic = stats.TIC, Xic2 = Path.GetFullPath(args[2]),
                quants.AreaByRun, quants.StoredTicByRun }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(args[3], report);
            Console.WriteLine("RAW scan and read-only .xic2 opened; report " + args[3]);
            return 0;
        }
    }
}
