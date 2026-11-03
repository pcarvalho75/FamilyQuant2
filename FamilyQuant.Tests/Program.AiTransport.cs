using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiTransportCommands() =>
    [new("--test-ai-transport", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-transport", (_, _) =>
        {
            AiTransportTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private sealed class AiFakeHandler(HttpStatusCode status, string body, bool disconnect = false) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Body { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
            if (disconnect) throw new HttpRequestException("fixture connection failure");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static void AiTransportTests()
    {
        const string prompt = "A preview chosen by the person.";
        const string key = "fixture-key";
        string message(string stop, string text) => "{\"id\":\"msg_fixture\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\",\"content\":[{\"type\":\"text\",\"text\":\"" + text + "\"}],\"stop_reason\":\"" + stop + "\",\"stop_sequence\":null,\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
        void Case(string label, HttpStatusCode status, string response, AiOutcome expected, bool disconnected = false)
        {
            var handler = new AiFakeHandler(status, response, disconnected);
            using var http = new HttpClient(handler);
            try
            {
                var result = new AiTransport(http).SendAsync(key, prompt, 32, CancellationToken.None).GetAwaiter().GetResult();
                Check(label, result.Outcome == expected && handler.Calls == 1
                    && (expected != AiOutcome.Success || result.Text == "fixture answer")
                    && handler.Body.Contains(prompt, StringComparison.Ordinal) && handler.Body.Contains(AiTransport.Model, StringComparison.Ordinal));
            }
            catch { Check(label, false); }
        }
        string error(string type) => "{\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"fixture\"}}";
        Case("J46 SDK sends the preview and reads a completed response", HttpStatusCode.OK, message("end_turn", "fixture answer"), AiOutcome.Success);
        Case("J46 a refusal never becomes a draft", HttpStatusCode.OK, message("refusal", "ignored"), AiOutcome.Refusal);
        Case("J46 truncated output is not presented as a complete draft", HttpStatusCode.OK, message("max_tokens", "ignored"), AiOutcome.Incomplete);
        Case("J46 401 reports a wrong key", HttpStatusCode.Unauthorized, error("authentication_error"), AiOutcome.WrongKey);
        Case("J46 402 billing error is distinguished", HttpStatusCode.PaymentRequired, error("billing_error"), AiOutcome.Billing);
        Case("J46 another 402 is not called a billing error", HttpStatusCode.PaymentRequired, error("invalid_request_error"), AiOutcome.Unavailable);
        Case("J46 429 is rate limited without retry", HttpStatusCode.TooManyRequests, error("rate_limit_error"), AiOutcome.RateLimited);
        Case("J46 server failure is reported without retry", HttpStatusCode.InternalServerError, error("api_error"), AiOutcome.Unavailable);
        Case("J46 connection failure is reported without retry", HttpStatusCode.OK, "", AiOutcome.NoConnection, true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var unused = new AiFakeHandler(HttpStatusCode.OK, message("end_turn", "fixture answer"));
        using var cancelledHttp = new HttpClient(unused);
        bool cancelledAsRequested;
        try { new AiTransport(cancelledHttp).SendAsync(key, prompt, 32, cancelled.Token).GetAwaiter().GetResult(); cancelledAsRequested = false; }
        catch (OperationCanceledException) { cancelledAsRequested = true; }
        Check("J46 cancellation propagates and starts no HTTP request", cancelledAsRequested && unused.Calls == 0);
    }
}
