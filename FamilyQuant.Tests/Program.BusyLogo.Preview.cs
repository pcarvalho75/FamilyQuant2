using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void WriteBusyLogoPreview(string output)
    {
        var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><title>FamilyQuant wait screen</title><style>");
        html.Append("body{font:16px 'Segoe UI',sans-serif;margin:32px;background:Canvas;color:CanvasText;color-scheme:light dark}h1{font-size:24px}main{display:grid;grid-template-columns:repeat(auto-fit,minmax(340px,1fr));gap:24px}.sequence{position:relative;aspect-ratio:10/7;overflow:hidden;border-radius:12px}.sequence img{position:absolute;width:100%;opacity:0;animation:frame 6s step-end infinite;animation-delay:calc(var(--i)*.125s)}.sequence .still{animation:none}@keyframes frame{0%{opacity:1}2.08333%,100%{opacity:0}}@media(prefers-reduced-motion:reduce){.sequence img{animation:none;opacity:0}.sequence .still{opacity:1}}a{color:LinkText}");
        html.Append("</style><h1>FamilyQuant wait screen</h1><p>Native WPF captures of one six-second loop. Synthetic operation fixture; no scientific result.</p><main>");
        foreach (string theme in new[] { "Dark", "Light" })
        {
            html.Append("<section><h2>").Append(theme).Append("</h2><div class=\"sequence\">");
            for (int frame = 0; frame < BusyLogoFrameCount; frame++)
                html.Append("<img alt=\"FamilyQuant native animation phase\" style=\"--i:").Append(frame).Append("\" src=\"")
                    .Append(theme).Append("-100-").Append(frame.ToString("00", CultureInfo.InvariantCulture)).Append(".png\">");
            html.Append("<img class=\"still\" alt=\"Still FamilyQuant logo\" src=\"").Append(theme).Append("-100-still.png\"></div><p>Scale: ");
            foreach (int scale in new[] { 100, 125, 150 })
                html.Append("<a href=\"").Append(theme).Append('-').Append(scale).Append("-00.png\">").Append(scale).Append("%</a> ");
            html.Append("<a href=\"").Append(theme).Append("-100-still.png\">Reduced motion</a></p></section>");
        }
        html.Append("</main></html>"); File.WriteAllText(Path.Combine(output, "preview.html"), html.ToString(), new UTF8Encoding(false));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ulong Y7ComputeWork()
    {
        ulong value = 14695981039346656037;
        for (int index = 0; index < 1_200_000_000; index++) value = unchecked((value ^ (uint)index) * 1099511628211);
        return value;
    }

    private static void BusyLogoPerformance(string output)
    {
        Section("Y7 bounded offscreen computation-cost probe");
        var host = new AppOperationHost(); var overlay = new BusyOverlay { DataContext = host };
        var window = Y7Host(overlay);
        try
        {
            using var operation = host.Begin("Synthetic computation-cost probe");
            operation.Context.Report("work", "Timing the same background work with the logo still and animated.", 1, 2); PumpDispatcher();
            var logo = (FrameworkElement)overlay.FindName("AnimatedLogo");
            var preference = logo.GetType().GetProperty("AnimationPreferenceOverride", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var warm = Task.Run(Y7ComputeWork); FinishBusyTask(warm); // Warm both paths without blocking the dispatcher.
            (double Wall, double Cpu, ulong Value) Read(bool animated)
            {
                preference.SetValue(logo, animated); PumpDispatcher();
                using var process = Process.GetCurrentProcess(); TimeSpan cpu = process.TotalProcessorTime;
                var watch = Stopwatch.StartNew(); var work = Task.Run(Y7ComputeWork);
                while (!work.IsCompleted) { PumpDispatcher(); Thread.Sleep(2); }
                ulong result = work.GetAwaiter().GetResult(); watch.Stop(); process.Refresh();
                return (watch.Elapsed.TotalSeconds, (process.TotalProcessorTime - cpu).TotalSeconds, result);
            }
            var samples = Enumerable.Range(0, 3).SelectMany(_ => new[] { (Animated: false, Result: Read(false)), (Animated: true, Result: Read(true)) }).ToArray();
            double still = samples.Where(sample => !sample.Animated).Select(sample => sample.Result.Wall).OrderBy(value => value).ElementAt(1);
            double animated = samples.Where(sample => sample.Animated).Select(sample => sample.Result.Wall).OrderBy(value => value).ElementAt(1);
            Check("the animation leaves deterministic background work unchanged", samples.Select(sample => sample.Result.Value).Distinct().Count() == 1);
            Check("bounded animation overhead is below 20 percent plus 20 milliseconds", animated <= still * 1.2 + .02,
                FormattableString.Invariant($"still median {still:F3}s, animated median {animated:F3}s"));
            FeatureLedgerEvidence.WriteJson(Path.Combine(output, "performance.json"), new
            {
                scope = "Three interleaved offscreen WPF samples per mode; one deterministic CPU worker; not a RAW extraction benchmark.",
                renderTier = System.Windows.Media.RenderCapability.Tier >> 16, frameRateLimit = 30, propertyAnimationCount = 12, workIterationsPerSample = 1_200_000_000,
                stillMedianSeconds = still, animatedMedianSeconds = animated,
                samples = samples.Select(sample => new { sample.Animated, sample.Result.Wall, sample.Result.Cpu, sample.Result.Value })
            });
            Console.WriteLine(FormattableString.Invariant($"Y7 timing: still {still:F3}s; animated {animated:F3}s; frame cap 30fps; 12 opacity/transform animations."));
        }
        finally { window.Close(); PumpDispatcher(); }
    }
}
