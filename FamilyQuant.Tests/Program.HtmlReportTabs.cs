using FamilyQuant.Report;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private static IEnumerable<TestCommand> HtmlReportTabsCommands() =>
    [new("--test-html-report-tabs", 10520, TestCommandPhase.Raw,
        args => args.Length is 1 or 2 && args[0] == "--test-html-report-tabs", (args, _) =>
        {
            HtmlReportTabsTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "html-report-tabs"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private static void HtmlReportTabsTests(string output)
    {
        var payload = HtmlReportTabsFixture();
        HtmlReportBrowserChecks(output, new[] { ("phone", payload, 360), ("desktop", payload, 1280) }, """
            const tabs = [...d.querySelectorAll('[role="tab"]')];
            const names = ['Families','Signal recovery','Mass and isotopes','Coverage','Database gap','Quality','Sensitivity'];
            const checks = {};
            checks.order = JSON.stringify(tabs.map(t=>t.textContent.trim())) === JSON.stringify(names);
            checks.relationships = tabs.every(t=>d.getElementById(t.getAttribute('aria-controls'))?.getAttribute('aria-labelledby')===t.id && t.title.length>20);
            checks.initial = tabs[0].getAttribute('aria-selected')==='true' && d.querySelectorAll('[role="tabpanel"]:not([hidden])').length===1;
            checks.phoneFit = tabs.every(t=>{const r=t.getBoundingClientRect();return r.left>=0&&r.right<=w.innerWidth;});
            checks.allTabs = tabs.every(t=>{t.click();const p=d.getElementById(t.getAttribute('aria-controls'));return !p.hidden&&w.getComputedStyle(p).display!=='none'&&d.querySelectorAll('[role="tabpanel"]:not([hidden])').length===1;});
            tabs[0].focus();
            const key = k => d.activeElement.dispatchEvent(new w.KeyboardEvent('keydown',{key:k,bubbles:true}));
            key('ArrowLeft'); checks.wrap = d.activeElement===tabs[6]&&tabs[6].getAttribute('aria-selected')==='true';
            key('Home'); checks.home = d.activeElement===tabs[0];
            key('ArrowRight'); checks.next = d.activeElement===tabs[1];
            key('End'); checks.end = d.activeElement===tabs[6];
            tabs[0].click();
            const rank = d.getElementById('family-ranking');
            const values = rank.textContent;
            d.getElementById('rank-sort-spread').click();
            d.getElementById('rank-sort-share').click();
            checks.ranking = rank.textContent===values && rank.querySelectorAll('.rank-name').length===2;
            d.getElementById('tab-coverage').click();
            const choice=d.getElementById('coverage-family');choice.value='b';choice.dispatchEvent(new w.Event('change'));
            checks.coverage = d.getElementById('family-title').textContent==='Group B' && d.getElementById('panel-coverage').contains(d.getElementById('coverage-content'));
            d.getElementById('family-info').click();
            checks.popup = d.getElementById('detail-dialog').open && d.getElementById('dialog-title').textContent==='Group B';
            d.getElementById('dialog-close').click();
            checks.signal = d.getElementById('panel-recovery').contains(d.getElementById('signal-panel')) && !d.getElementById('signal-panel').hidden;
            checks.gap = d.getElementById('panel-gap').contains(d.getElementById('gap-panel')) && !d.getElementById('gap-panel').hidden;
            checks.figures = ['quality','sensitivity'].every(k=>d.getElementById(k+'-figures').querySelector('img[data-fq-figure]') && d.getElementById(k+'-empty').hidden);
            checks.offline = !d.querySelector('script[src],link[rel="stylesheet"],img[src^="http"]') && d.querySelector('meta[http-equiv="Content-Security-Policy"]').content.includes("connect-src 'none'");
            return checks;
            """);
    }

    private static HtmlReportPayload HtmlReportTabsFixture()
    {
        var basis = HtmlDotDefaultsFixture();
        var fields = basis.Fields.ToDictionary(pair => pair.Key, pair => pair.Value);
        var source = new HtmlReportReference { Kind = "synthetic-ui-fixture" };
        fields["run"] = new("Run 1", null, "", source);
        fields["credited"] = new("55", 55, "%", source);
        fields["gap-name"] = new("Unassigned evidence", null, "", source);
        fields["gap-fraction"] = new("0.25", .25, "", source);
        fields["gap-percent"] = new("25", 25, "%", source);
        fields["quality-title"] = new("Quality fixture", null, "", source);
        fields["sensitivity-title"] = new("Sensitivity fixture", null, "", source);
        fields["caption"] = new("Stored synthetic figure caption.", null, "", source);
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=";
        return basis with
        {
            Fields = fields,
            CreditedRuns = new[] { new HtmlRunCredit("run", "credited") },
            DatabaseGap = new[] { new HtmlGapRow("gap-name", "gap-fraction") { Percent = "gap-percent" } },
            Quality = new[] { new HtmlReportFigure("quality-title", "caption", png, "quality-fixture") },
            Sensitivity = new[] { new HtmlReportFigure("sensitivity-title", "caption", png, "sensitivity-fixture") },
        };
    }

    private static void HtmlReportBrowserChecks(string output, (string Name, HtmlReportPayload Payload, int Width)[] cases, string verify)
    {
        Directory.CreateDirectory(output);
        string? edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe"))
            .FirstOrDefault(File.Exists);
        if (edge == null)
        {
            skipped++;
            Console.WriteLine("  SKIP HTML page check requires installed Microsoft Edge.");
            return;
        }
        var html = new StringBuilder("<!doctype html><html><body><pre id=\"qa-result\"></pre><script>const results=[];function inspect(frame,name){const w=frame.contentWindow,d=w.document;try{const checks=(function(){")
            .Append(verify).Append("})();checks.noErrors=w.__errors.length===0;results.push({name,checks});}catch(e){results.push({name,checks:{completed:false},error:String(e)});}document.getElementById('qa-result').textContent=JSON.stringify(results);}</script>");
        foreach (var item in cases)
        {
            string page = Encoding.UTF8.GetString(HtmlReportWriter.BuildBytes(item.Payload)).Replace("<head>",
                "<head><script>window.__errors=[];addEventListener('error',e=>__errors.push(e.message));</script>", StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(output, item.Name + ".html"), page, new UTF8Encoding(false));
            html.Append("<iframe style=\"width:").Append(item.Width).Append("px;height:844px\" onload=\"")
                .Append(WebUtility.HtmlEncode("inspect(this," + JsonSerializer.Serialize(item.Name) + ")"))
                .Append("\" srcdoc=\"").Append(WebUtility.HtmlEncode(page)).Append("\"></iframe>");
        }
        string path = Path.Combine(output, "check.html");
        File.WriteAllText(path, html.Append("</body></html>").ToString(), new UTF8Encoding(false));
        var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--disable-component-update", "--renderer-process-limit=2", "--user-data-dir=" + Path.Combine(output, "browser"), "--dump-dom", "--virtual-time-budget=5000", new Uri(path).AbsoluteUri })
            start.ArgumentList.Add(argument);
        using var browser = Process.Start(start) ?? throw new IOException("Could not start the isolated page browser.");
        var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
        if (!browser.WaitForExit(60000))
        {
            browser.Kill(entireProcessTree: true); browser.WaitForExit();
            Check("HTML page browser completes", false, "Isolated browser exceeded 60 seconds.");
            return;
        }
        string dom = stdout.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "browser-stderr.txt"), stderr.GetAwaiter().GetResult());
        var result = Regex.Match(dom, "<pre id=\"qa-result\">(.*?)</pre>", RegexOptions.Singleline);
        string json = result.Success ? WebUtility.HtmlDecode(result.Groups[1].Value) : "[]";
        File.WriteAllText(Path.Combine(output, "page-results.json"), json);
        using var document = JsonDocument.Parse(json);
        Check("HTML page browser executes every fixture", browser.ExitCode == 0 && document.RootElement.GetArrayLength() == cases.Length);
        foreach (var item in document.RootElement.EnumerateArray())
            foreach (var check in item.GetProperty("checks").EnumerateObject())
                Check("HTML page " + item.GetProperty("name").GetString() + ": " + check.Name, check.Value.GetBoolean(), item.GetRawText());
    }
}
