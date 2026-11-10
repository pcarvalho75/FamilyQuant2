using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static async Task CaptureMassQualityPage(string edge, string page, string screenshot, int width, string profile)
    {
        var start = new ProcessStartInfo(edge) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in new[] { "--headless=new", "--disable-gpu", "--no-first-run", "--no-default-browser-check", "--disable-background-networking",
            "--hide-scrollbars", "--remote-debugging-port=0", "--user-data-dir=" + profile, "about:blank" }) start.ArgumentList.Add(arg);
        using var browser = Process.Start(start) ?? throw new IOException("Could not start the isolated screen browser.");
        var stdout = browser.StandardOutput.ReadToEndAsync(); var stderr = browser.StandardError.ReadToEndAsync();
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        try
        {
            string portFile = Path.Combine(profile, "DevToolsActivePort");
            string[] address = Array.Empty<string>();
            while (address.Length < 2)
            {
                limit.Token.ThrowIfCancellationRequested();
                try { if (File.Exists(portFile)) address = File.ReadAllLines(portFile); }
                catch (IOException) { }
                if (address.Length < 2) await Task.Delay(100, limit.Token).ConfigureAwait(false);
            }
            using var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri("ws://127.0.0.1:" + address[0] + address[1]), limit.Token).ConfigureAwait(false);
            int next = 0;
            async Task<JsonElement> Call(string method, object parameters, string? session = null)
            {
                int id = ++next;
                var message = new Dictionary<string, object> { ["id"] = id, ["method"] = method, ["params"] = parameters };
                if (session != null) message["sessionId"] = session;
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(message);
                await socket.SendAsync(new ArraySegment<byte>(json), WebSocketMessageType.Text, true, limit.Token).ConfigureAwait(false);
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    using var content = new MemoryStream(); WebSocketReceiveResult reply;
                    do
                    {
                        reply = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), limit.Token).ConfigureAwait(false);
                        if (reply.MessageType == WebSocketMessageType.Close) throw new IOException("Screen browser closed early.");
                        content.Write(buffer, 0, reply.Count);
                    } while (!reply.EndOfMessage);
                    using var parsed = JsonDocument.Parse(content.ToArray());
                    var root = parsed.RootElement;
                    if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) continue;
                    if (root.TryGetProperty("error", out var error)) throw new IOException("Screen protocol: " + error.GetRawText());
                    return root.GetProperty("result").Clone();
                }
            }
            string target = (await Call("Target.createTarget", new { url = "about:blank" }).ConfigureAwait(false)).GetProperty("targetId").GetString()!;
            string session = (await Call("Target.attachToTarget", new { targetId = target, flatten = true }).ConfigureAwait(false)).GetProperty("sessionId").GetString()!;
            await Call("Emulation.setDeviceMetricsOverride", new { width, height = 900, deviceScaleFactor = 1, mobile = width < 500 }, session).ConfigureAwait(false);
            await Call("Page.navigate", new { url = new Uri(page).AbsoluteUri }, session).ConfigureAwait(false);
            bool ready = false;
            while (!ready)
            {
                await Task.Delay(100, limit.Token).ConfigureAwait(false);
                var result = await Call("Runtime.evaluate", new { expression = "document.readyState==='complete' && document.getElementById('app')?.hidden===false", returnByValue = true }, session).ConfigureAwait(false);
                ready = result.GetProperty("result").TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
            }
            await Task.Delay(300, limit.Token).ConfigureAwait(false);
            var metrics = await Call("Runtime.evaluate", new { expression = "JSON.stringify({width:innerWidth,height:Math.max(document.documentElement.scrollHeight,document.body.scrollHeight)})", returnByValue = true }, session).ConfigureAwait(false);
            using var size = JsonDocument.Parse(metrics.GetProperty("result").GetProperty("value").GetString()!);
            if (size.RootElement.GetProperty("width").GetInt32() != width) throw new IOException("Screen viewport did not match the requested width.");
            var image = await Call("Page.captureScreenshot", new { format = "png", captureBeyondViewport = true,
                clip = new { x = 0, y = 0, width, height = size.RootElement.GetProperty("height").GetInt32(), scale = 1 } }, session).ConfigureAwait(false);
            File.WriteAllBytes(screenshot, Convert.FromBase64String(image.GetProperty("data").GetString()!));
        }
        finally
        {
            if (!browser.HasExited) { browser.Kill(true); browser.WaitForExit(); }
            File.WriteAllText(screenshot + ".browser.log", await stderr.ConfigureAwait(false));
            _ = await stdout.ConfigureAwait(false);
        }
    }
}
