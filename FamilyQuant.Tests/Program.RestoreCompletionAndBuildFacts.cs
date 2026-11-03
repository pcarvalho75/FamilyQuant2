using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void RestoreCompletionAndBuildFacts(string output)
    {
        var rows = new List<(string Id, string Text, string Note)>();
        string package = Path.Combine(output, "restore-facts.sepr2");
        File.WriteAllText(package, "Synthetic completion fixture; the injected engine never reads this file.");
        var model = new ComputeXicViewModel("restore fixture", compute: (_, _, _) => Task.FromResult(
            new FullXicComputeResult { Rows = new(), Manifest = new() }))
            { RawDirectory = output, SeproPackage = package };
        var quantify = new QuantifyControl(model);
        LayoutAt(quantify, new Size(1024, 768), -1);
        model.ComputeAsync().GetAwaiter().GetResult();
        PumpDispatcher();
        string completion = UiField<TextBlock>(quantify, "TextBlockStatus")!.Text;
        Check("R4c normal Quantify completion visibly retains results and unsaved-project warning",
            completion.Contains("held", StringComparison.OrdinalIgnoreCase)
            && completion.Contains("project itself has not been saved yet", StringComparison.OrdinalIgnoreCase)
            && completion.Contains("spectrum evidence table", StringComparison.OrdinalIgnoreCase), completion);
        rows.Add(("FQ15-776EFFB6D540", completion,
            "Normal completed Quantify status TextBlock at 1024x768; synthetic injected engine, no RAW or saved project produced."));

        var build = new USearchLib();
        LayoutAt(build, new Size(1024, 768), -1);
        string identity = WalkTree(build).OfType<Label>().Single(label =>
            Equals(label.Content, "Redundancy identity (%)")).ToolTip.ToString()!;
        Check("R4c redundancy tooltip states the actual shorter-sequence denominator",
            identity.Contains("shorter sequence", StringComparison.OrdinalIgnoreCase)
            && identity.Contains("ungapped", StringComparison.OrdinalIgnoreCase), identity);
        rows.Add(("FQ15-BE7BFC4196C9", identity,
            "Actual label tooltip. Core delegates below 100 to bundled MAligner.Compress: best ungapped IDScore divided by shorter sequence length; I/L count alike. At 100 Core uses exact substring elimination."));
        build.ViewModel.TryBegin("fixture running");
        string busy = build.ShellStatusText;
        Check("R4c running USEARCH status explains same-files interference",
            busy.Contains("same files", StringComparison.OrdinalIgnoreCase)
            && busy.Contains("interfere", StringComparison.OrdinalIgnoreCase), busy);
        foreach (string id in new[] { "FQ15-92BF28F4DE7B", "FQ15-64F6298F9D52" })
            rows.Add((id, busy, "Normal running Build families shell status; ViewModel.TryBegin sets busy without starting a process."));
        UiField<Button>(build, "ButtonRemoveRedundancy")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        PumpDispatcher();
        Check("R4c existing redundancy reentry guard also explains interference",
            build.ViewModel.Details.Contains("same files", StringComparison.OrdinalIgnoreCase)
            && build.ViewModel.Details.Contains("interfere", StringComparison.OrdinalIgnoreCase), build.ViewModel.Details);
        build.ViewModel.End(false);
        string redundancy = build.BuildHelpText().Split('\n').First(line => line.StartsWith("Remove redundancy", StringComparison.Ordinal));
        rows.Add(("FQ15-4A644A3EA1F3", redundancy,
            "Left unchanged: current PatternLab database-generator equivalence was not established from available code. No unsupported equivalence added."));
        if (failed != 0) return;
        string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        File.WriteAllText(Path.Combine(output, "factcheck-R4c2-FQEvidence.tsv"),
            "row_id\tnew_text\tnote\n" + string.Join("\n", rows.OrderBy(row => row.Id, StringComparer.Ordinal)
                .Select(row => row.Id + "\t" + Quote(row.Text) + "\t" + Quote(row.Note))) + "\n", new UTF8Encoding(false));
    }
}
