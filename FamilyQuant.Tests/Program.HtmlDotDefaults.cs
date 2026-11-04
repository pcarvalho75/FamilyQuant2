using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HtmlDotDefaultsCommands() =>
    [new("--test-html-dot-defaults", 10510, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-dot-defaults", (args, _) =>
        {
            HtmlDotDefaultsTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-dot-defaults"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlDotDefaultsTests(string output)
    {
        string? edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe"))
            .FirstOrDefault(File.Exists);
        if (edge is null)
        {
            skipped++;
            Console.WriteLine("  SKIP HTML dot defaults require installed Microsoft Edge for the actual page check.");
            return;
        }
        // These are explicit UI stand-ins, never a source-data count or delivered scientific report.
        var basis = HtmlDotDefaultsFixture();
        HtmlReportPayload Without(params string[] ids) => basis with
        { DotMeasures = basis.DotMeasures.Where(measure => !ids.Contains(measure.Id)).ToArray() };
        HtmlReportPayload WithZero(params string[] ids)
        {
            var fields = basis.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
            foreach (string id in ids) fields["entries-" + id] = fields["entries-" + id] with { Text = "0", Value = 0 };
            return basis with { Fields = fields };
        }
        var cases = new (string Name, HtmlReportPayload Payload, string[] Expected)[]
        {
            ("complete requested trio", basis, new[] { "databaseEntries", "mean", "uniqueProteins" }),
            ("old payload without database entries", Without("databaseEntries"), new[] { "proteins", "mean", "uniqueProteins" }),
            ("missing mean retains existing fallback", Without("mean"), new[] { "proteins", "proteins", "uniqueProteins" }),
            ("missing own-peptide measure retains existing fallback", Without("uniqueProteins"), new[] { "proteins", "mean", "proteins" }),
            ("requested trio without protein estimate", Without("proteins"), new[] { "databaseEntries", "mean", "uniqueProteins" }),
            ("only database entries retains existing fallback", Without("mean", "proteins", "uniqueProteins"), new[] { "databaseEntries", "databaseEntries", "databaseEntries" }),
            ("empty database measure is unavailable", basis with { DotMeasures = basis.DotMeasures.Select(measure => measure.Id == "databaseEntries"
                ? measure with { Values = new Dictionary<string, string>() } : measure).ToArray() }, new[] { "proteins", "mean", "uniqueProteins" }),
            ("conflicted panel family has absent count without inference", basis with { DotMeasures = basis.DotMeasures.Select(measure => measure.Id == "databaseEntries"
                ? measure with { Values = new Dictionary<string, string> { ["a"] = "entries-a" } } : measure).ToArray() }, new[] { "databaseEntries", "mean", "uniqueProteins" }),
            ("family with no entries retains its recorded zero", WithZero("b"), new[] { "databaseEntries", "mean", "uniqueProteins" }),
            ("all recorded zeros remain an available measure", WithZero("a", "b"), new[] { "databaseEntries", "mean", "uniqueProteins" })
        };
        string folder = Path.Combine(output, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var html = new StringBuilder("<!doctype html><html><body><pre id=\"qa-result\">pending</pre><script>const results=[];</script>");
        const string verify = """
            <script>
            function inspect(frame,name,expected,hasDatabase,absent,zero,expectedValue){
              const d=frame.contentDocument,w=frame.contentWindow;
              try {
                const actual=['x','y','size'].map(a=>d.getElementById('dot-'+a).value);
                const offered=!!d.querySelector('#dot-x option[value="databaseEntries"]');
                const mark=d.querySelector('.evidence-dot[data-family="a"]');
                mark.focus();mark.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));
                const exact=d.querySelector('#dot-tooltip [data-fq-field="entries-a"]');
                const label=d.querySelector('#dot-tooltip [data-fq-field="label-databaseEntries"]');
                const tooltip=hasDatabase ? exact?.textContent===expectedValue&&label?.textContent==='Database entries' : !label;
                let missing=true;
                if(absent||zero){const other=d.querySelector('.evidence-dot[data-family="b"]');other.focus();other.dispatchEvent(new w.KeyboardEvent('keydown',{key:'Enter',bubbles:true}));const tip=d.getElementById('dot-tooltip'),value=tip.querySelector('[data-fq-field="entries-b"]');missing=other.classList.contains('is-hollow')&&(absent ? !value&&tip.textContent.includes('not available') : value?.textContent==='0'&&!tip.textContent.includes('not available')&&tip.textContent.includes('outside the chosen scale'));}
                results.push({name,pass:JSON.stringify(actual)===JSON.stringify(expected)&&offered===hasDatabase&&tooltip&&missing&&w.__errors.length===0,actual,expected,offered,tooltip,missing,errors:w.__errors});
              }catch(e){results.push({name,pass:false,error:String(e)});}
              document.getElementById('qa-result').textContent=JSON.stringify(results);
            }
            </script>
            """;
        html.Append(verify);
        foreach (var item in cases)
        {
            string page = Encoding.UTF8.GetString(HtmlReportWriter.BuildBytes(item.Payload)).Replace("<head>",
                "<head><script>window.__errors=[];addEventListener('error',e=>__errors.push(e.message));</script>", StringComparison.Ordinal);
            bool hasDatabase = item.Payload.DotMeasures.Any(measure => measure.Id == "databaseEntries" && measure.Values.Count > 0);
            string invocation = "inspect(this," + JsonSerializer.Serialize(item.Name) + "," + JsonSerializer.Serialize(item.Expected)
                + "," + (hasDatabase ? "true" : "false") + "," + (item.Name.StartsWith("conflicted", StringComparison.Ordinal) ? "true" : "false")
                + "," + (item.Payload.Fields["entries-b"].Value == 0 ? "true" : "false") + "," + JsonSerializer.Serialize(item.Payload.Fields["entries-a"].Text) + ")";
            html.Append("<iframe style=\"width:390px;height:844px\" onload=\"").Append(WebUtility.HtmlEncode(invocation))
                .Append("\" srcdoc=\"").Append(WebUtility.HtmlEncode(page)).Append("\"></iframe>");
        }
        string path = Path.Combine(folder, "defaults.html");
        File.WriteAllText(path, html.Append("</body></html>").ToString(), new UTF8Encoding(false));
        var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--disable-component-update", "--renderer-process-limit=2", "--user-data-dir=" + Path.Combine(folder, "browser"), "--dump-dom", "--virtual-time-budget=5000", new Uri(path).AbsoluteUri })
            start.ArgumentList.Add(argument);
        using var browser = Process.Start(start) ?? throw new IOException("Could not start the isolated page browser.");
        var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
        if (!browser.WaitForExit(60000))
        {
            browser.Kill(entireProcessTree: true); browser.WaitForExit();
            File.WriteAllText(Path.Combine(folder, "timeout-dom.html"), stdout.GetAwaiter().GetResult());
            File.WriteAllText(Path.Combine(folder, "browser-stderr.txt"), stderr.GetAwaiter().GetResult());
            Check("HTML dot defaults browser completes", false, "Isolated browser exceeded 60 seconds.");
            return;
        }
        string dom = stdout.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(folder, "browser-stderr.txt"), stderr.GetAwaiter().GetResult());
        var result = Regex.Match(dom, "<pre id=\"qa-result\">(.*?)</pre>", RegexOptions.Singleline);
        string json = result.Success ? WebUtility.HtmlDecode(result.Groups[1].Value) : "[]";
        File.WriteAllText(Path.Combine(folder, "page-results.json"), json);
        try
        {
            using var document = JsonDocument.Parse(json);
            Check("HTML dot defaults browser executed every case", browser.ExitCode == 0 && document.RootElement.GetArrayLength() == cases.Length);
            foreach (var item in document.RootElement.EnumerateArray())
                Check("HTML dot defaults: " + item.GetProperty("name").GetString(), item.GetProperty("pass").GetBoolean(), item.GetRawText());
        }
        catch (JsonException) { Check("HTML dot defaults page produced valid evidence", false, json); }
    }

    private static HtmlReportPayload HtmlDotDefaultsFixture()
    {
        var source = new HtmlReportReference { Kind = "synthetic-ui-fixture" };
        var fields = new Dictionary<string, HtmlReportField>
        {
            ["name-a"] = new("Group A", null, "", source), ["name-b"] = new("Group B", null, "", source),
            ["mean-a"] = new("25", 25, "%", source), ["mean-b"] = new("75", 75, "%", source),
            ["proteins-a"] = new("3", 3, "", source), ["proteins-b"] = new("8", 8, "", source),
            ["uniqueProteins-a"] = new("2", 2, "", source), ["uniqueProteins-b"] = new("6", 6, "", source),
            ["entries-a"] = new("125", 125, "", source), ["entries-b"] = new("630", 630, "", source)
        };
        var measures = new List<HtmlDotMeasure>();
        foreach (string id in new[] { "mean", "proteins", "uniqueProteins", "databaseEntries" })
        {
            if (!HtmlReportAxisTicks.TryResolve(new() { Kind = "axis-label", Property = id }, out var label)) throw new InvalidDataException("Missing axis label.");
            fields["label-" + id] = label;
            var ticks = new List<string>();
            for (int exponent = 0; exponent <= 3; exponent++)
            {
                string property = id + "/log10/" + exponent.ToString(CultureInfo.InvariantCulture);
                if (!HtmlReportAxisTicks.TryResolve(new() { Kind = "axis-tick", Property = property, Format = "axis-plain" }, out var tick)) throw new InvalidDataException("Missing axis tick.");
                fields[property] = tick; ticks.Add(property);
            }
            string key = id == "databaseEntries" ? "entries" : id;
            measures.Add(new(id, "label-" + id, id == "mean" ? "%" : "", "log", ticks,
                new Dictionary<string, string> { ["a"] = key + "-a", ["b"] = key + "-b" }));
        }
        return new HtmlReportPayload { Title = "UI fixture", Fields = fields, DotMeasures = measures, Composition = new()
        { Families = new[] { new HtmlReportFamily { Id = "a", Name = "name-a", Mean = "mean-a", Proteins = "proteins-a", UniqueProteins = "uniqueProteins-a" },
            new HtmlReportFamily { Id = "b", Name = "name-b", Mean = "mean-b", Proteins = "proteins-b", UniqueProteins = "uniqueProteins-b" } } } };
    }
}
