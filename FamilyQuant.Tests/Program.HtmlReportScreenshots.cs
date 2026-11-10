using FamilyQuant.Report;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void CaptureHtmlReportScreens(string output, HtmlReportPayload payload, string tab, string setup = "")
    {
        Directory.CreateDirectory(output);
        string? edge = new[] { Environment.SpecialFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFiles }
            .Select(folder => Path.Combine(Environment.GetFolderPath(folder), "Microsoft", "Edge", "Application", "msedge.exe")).FirstOrDefault(File.Exists);
        if (edge == null) { skipped++; Console.WriteLine("  SKIP HTML screenshots require installed Microsoft Edge."); return; }
        foreach (string theme in new[] { "light", "dark" })
            foreach (int width in new[] { 1366, 390 })
            {
                string name = tab + "-" + theme + "-" + width;
                string path = Path.Combine(output, name + ".html"), png = Path.Combine(output, name + ".png");
                string script = "document.documentElement.dataset.theme=" + JsonSerializer.Serialize(theme)
                    + ";document.getElementById('theme-label').textContent=" + JsonSerializer.Serialize(theme == "dark" ? "Dark theme" : "Light theme")
                    + ";document.getElementById(" + JsonSerializer.Serialize("tab-" + tab) + ").click();" + setup
                    + "document.documentElement.style.scrollBehavior='auto';document.getElementById(" + JsonSerializer.Serialize("panel-" + tab)
                    + ").scrollIntoView({block:'start',behavior:'instant'});";
                string page = Encoding.UTF8.GetString(HtmlReportWriter.BuildBytes(payload));
                File.WriteAllText(path, page, new UTF8Encoding(false));
                string profile = Path.Combine(output, "browser-" + name + "-" + Guid.NewGuid().ToString("N"));
                var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (string argument in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
                    "--disable-component-update", "--force-device-scale-factor=1", "--hide-scrollbars", "--window-size=1366,900",
                    "--user-data-dir=" + profile, "--remote-debugging-port=0", new Uri(path).AbsoluteUri })
                    start.ArgumentList.Add(argument);
                using var browser = Process.Start(start) ?? throw new IOException("Could not start the isolated screenshot browser.");
                var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
                bool captured = false;
                try { captured = CaptureExactViewport(profile, png, width, script).GetAwaiter().GetResult(); }
                finally
                {
                    if (!browser.HasExited) browser.Kill(entireProcessTree: true);
                    browser.WaitForExit();
                    File.WriteAllText(Path.Combine(output, name + "-browser.txt"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                }
                Check("HTML screenshot " + name, captured && File.Exists(png) && new FileInfo(png).Length > 1000);
                Console.WriteLine("HTML screenshot: " + png);
            }
    }

    private static async Task<bool> CaptureExactViewport(string profile, string png, int width, string setup)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        string portFile = Path.Combine(profile, "DevToolsActivePort");
        string[] portLines = Array.Empty<string>();
        while (portLines.Length == 0 || !int.TryParse(portLines[0], out _))
        {
            try { if (File.Exists(portFile)) portLines = File.ReadAllLines(portFile); }
            catch (IOException) { }
            if (portLines.Length == 0 || !int.TryParse(portLines[0], out _)) await Task.Delay(50, timeout.Token);
        }
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false });
        string targets = await http.GetStringAsync("http://127.0.0.1:" + portLines[0] + "/json/list", timeout.Token);
        using var list = JsonDocument.Parse(targets);
        string endpoint = list.RootElement.EnumerateArray().First(target => target.GetProperty("type").GetString() == "page")
            .GetProperty("webSocketDebuggerUrl").GetString()!;
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(endpoint), timeout.Token);
        int nextId = 0;
        async Task<JsonElement> Command(string method, object parameters)
        {
            int id = ++nextId;
            byte[] request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
            await socket.SendAsync(new ArraySegment<byte>(request), WebSocketMessageType.Text, true, timeout.Token);
            while (true)
            {
                using var message = new MemoryStream();
                var buffer = new byte[65536];
                WebSocketReceiveResult received;
                do
                {
                    received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), timeout.Token);
                    message.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);
                using var reply = JsonDocument.Parse(message.ToArray());
                if (!reply.RootElement.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id) continue;
                if (reply.RootElement.TryGetProperty("error", out var error)) throw new IOException(error.GetRawText());
                return reply.RootElement.GetProperty("result").Clone();
            }
        }
        await Command("Emulation.setDeviceMetricsOverride", new { width, height = 900, deviceScaleFactor = 1, mobile = false });
        string expression = "(async()=>{while(!document.getElementById('app')||document.getElementById('app').hidden)await new Promise(r=>setTimeout(r,20));"
            + setup + "await new Promise(r=>requestAnimationFrame(()=>requestAnimationFrame(r)));"
            + "document.querySelector('[role=tabpanel]:not([hidden])').scrollIntoView({block:'start',behavior:'instant'});"
            + "await new Promise(r=>requestAnimationFrame(r));return innerWidth;})()";
        var evaluated = await Command("Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true });
        if (evaluated.TryGetProperty("exceptionDetails", out var exception)) throw new IOException(exception.GetRawText());
        int actualWidth = evaluated.GetProperty("result").GetProperty("value").GetInt32();
        var capture = await Command("Page.captureScreenshot", new { format = "png", captureBeyondViewport = false, fromSurface = true });
        byte[] bytes = Convert.FromBase64String(capture.GetProperty("data").GetString()!);
        File.WriteAllBytes(png, bytes);
        int pixelWidth = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        return actualWidth == width && pixelWidth == width;
    }
}
