using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> HelpAssistantCommands() =>
    [new("--test-help-assistant", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-help-assistant", (_, _) =>
        {
            HelpAssistantTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        })];

    private const string HelpFixtureToken = "fixture-github-token";
    private static readonly Type HelpGitHubType = typeof(HelpAssistant).Assembly.GetType("FamilyQuant.Ai.HelpGitHub", true)!;

    /// <summary>Answers every request through <paramref name="respond"/> and records what was sent.</summary>
    private sealed class HelpFakeHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Uri, string Headers, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(token);
            Requests.Add((request.RequestUri!.AbsoluteUri, request.Headers.ToString(), body));
            return respond(request, Requests.Count);
        }
    }

    private static HttpResponseMessage HelpJson(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    /// <summary>A synthetic repository: a tree, two files and a search answer.</summary>
    private static HttpResponseMessage HelpFakeGitHub(HttpRequestMessage request, int _)
    {
        string uri = request.RequestUri!.ToString();
        if (uri.Contains("/git/trees/master", StringComparison.Ordinal))
            return HelpJson("{\"tree\":[{\"path\":\"Core\",\"type\":\"tree\"},{\"path\":\"Core/Alpha.cs\",\"type\":\"blob\"},"
                + "{\"path\":\"Core/Beta.cs\",\"type\":\"blob\"},{\"path\":\"docs/Guide.md\",\"type\":\"blob\"}],\"truncated\":false}");
        if (uri.Contains("/contents/Core/Alpha.cs", StringComparison.Ordinal))
            return HelpJson(string.Join("\r\n", Enumerable.Range(1, 900).Select(line => "alpha line " + line)) + "\r\n");
        if (uri.Contains("/contents/Core/Beta.cs", StringComparison.Ordinal))
            return HelpJson("beta one\nbeta two\nbeta three\n");
        if (uri.Contains("/search/code", StringComparison.Ordinal))
            return HelpJson("{\"total_count\":1,\"items\":[{\"path\":\"Core/Beta.cs\",\"text_matches\":[{\"fragment\":\"beta\\n  two\"}]}]}");
        return HelpJson("{\"message\":\"Not Found\"}", HttpStatusCode.NotFound);
    }

    private static object NewHelpGitHub(HttpClient http) => Activator.CreateInstance(HelpGitHubType, http, HelpFixtureToken, "9.8.7.6")!;

    private static T HelpGitHubCall<T>(object gitHub, string method, params object[] arguments) =>
        ((Task<T>)HelpGitHubType.GetMethod(method)!.Invoke(gitHub, [.. arguments, CancellationToken.None])!).GetAwaiter().GetResult();

    private static void HelpAssistantTests()
    {
        HelpGitHubReaderTests();
        HelpClaudeLoopTests();
        HelpOutcomeTests();
    }

    private static string HelpMessage(string stop, params string[] blocks) =>
        "{\"id\":\"msg_fixture\",\"type\":\"message\",\"role\":\"assistant\",\"model\":\"claude-opus-5-5\",\"content\":["
        + string.Join(",", blocks) + "],\"stop_reason\":\"" + stop + "\",\"stop_sequence\":null,\"usage\":{\"input_tokens\":1,\"output_tokens\":1}}";
    private static string HelpTextBlock(string text) => "{\"type\":\"text\",\"text\":\"" + text + "\"}";
    private static string HelpToolUse(string id, string name, string input) =>
        "{\"type\":\"tool_use\",\"id\":\"" + id + "\",\"name\":\"" + name + "\",\"input\":" + input + ",\"caller\":{\"type\":\"direct\"}}";
    private const string HelpThinkingBlock = "{\"type\":\"thinking\",\"thinking\":\"\",\"signature\":\"fixture-signature\"}";

    private static readonly HelpState HelpFixtureState = new("state-version-marker", "state-screen-marker", "state-help-marker",
        "state-project-marker", "state-error-marker", "state-log-marker");
    private static readonly byte[] HelpFixturePicture = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0xFE, 0xED];

    private static HelpQuestion HelpFixtureQuestion(byte[]? picture = null, IReadOnlyList<HelpTurn>? earlier = null) =>
        new("question-marker: how are the weights computed?", HelpFixtureState, picture, earlier ?? []);

    private static HelpAnswer HelpAsk(HttpClient claude, HttpClient gitHub, string? token, HelpQuestion question,
        CancellationToken cancellationToken = default)
    {
        var assistant = (HelpAssistant)typeof(HelpAssistant).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(HttpClient), typeof(HttpClient), typeof(string)])!.Invoke([claude, gitHub, token]);
        return assistant.AskAsync("fixture-key", question, cancellationToken).GetAwaiter().GetResult();
    }

    private static bool HelpForcedAnswer(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("tool_choice", out JsonElement choice)
            && choice.ValueKind == JsonValueKind.Object && choice.GetProperty("type").GetString() == "none";
    }

    private static string[] HelpRoles(string body)
    {
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("messages").EnumerateArray().Select(message => message.GetProperty("role").GetString()!).ToArray();
    }

    private static string HelpSent(HttpRequestMessage request) => request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

    private static void HelpClaudeLoopTests()
    {
        var gitHubHandler = new HelpFakeHandler(HelpFakeGitHub);
        using var gitHubHttp = new HttpClient(gitHubHandler);
        var claude = new HelpFakeHandler((_, call) => HelpJson(call == 1
            ? HelpMessage("tool_use", HelpThinkingBlock, HelpTextBlock("Reading."),
                HelpToolUse("toolu_list", "list_files", "{\"prefix\":\"Core/\"}"),
                HelpToolUse("toolu_search", "search_code", "{\"query\":\"beta two\"}"),
                HelpToolUse("toolu_read", "read_file", "{\"path\":\"Core/Beta.cs\",\"from_line\":1,\"to_line\":3}"))
            : HelpMessage("end_turn", HelpTextBlock("fixture answer from Core/Beta.cs"))));
        using var claudeHttp = new HttpClient(claude);
        var answer = HelpAsk(claudeHttp, gitHubHttp, HelpFixtureToken, HelpFixtureQuestion(HelpFixturePicture));
        string first = claude.Requests[0].Body, second = claude.Requests.Count > 1 ? claude.Requests[1].Body : "";
        Check("H1c a tool round trip reads a file, returns the answer and names the lines read as its sources",
            answer.Outcome == AiOutcome.Success && answer.Text == "fixture answer from Core/Beta.cs"
            && answer.Sources.SequenceEqual([new HelpSource("Core/Beta.cs", 1, 3)]) && answer.SourceReadable
            && answer.GitHubLimitResetsAt == null && claude.Requests.Count == 2 && gitHubHandler.Requests.Count == 3
            && second.Contains("\"tool_use_id\":\"toolu_read\"", StringComparison.Ordinal) && second.Contains("1: beta one", StringComparison.Ordinal)
            && second.Contains("fixture-signature", StringComparison.Ordinal) && second.Contains("Core/Alpha.cs", StringComparison.Ordinal)
            && HelpRoles(second).SequenceEqual(["user", "assistant", "user"]));
        Check("H1c Claude gets the model, the system prompt with a cache breakpoint and the three tools",
            first.Contains(AiTransport.Model, StringComparison.Ordinal) && first.Contains("You are the help built into FamilyQuant", StringComparison.Ordinal)
            && first.Contains("cache_control", StringComparison.Ordinal) && first.Contains("\"list_files\"", StringComparison.Ordinal)
            && first.Contains("\"read_file\"", StringComparison.Ordinal) && first.Contains("\"search_code\"", StringComparison.Ordinal)
            && !first.Contains("\"tool_choice\"", StringComparison.Ordinal));
        string[] privateText = ["state-version-marker", "state-screen-marker", "state-help-marker", "state-project-marker",
            "state-error-marker", "state-log-marker", "question-marker", "weights", Convert.ToBase64String(HelpFixturePicture), "fixture-key"];
        Check("H1c the state, question and picture reach Claude but nothing of them, nor the key, reaches GitHub",
            privateText.Take(7).All(text => first.Contains(text, StringComparison.Ordinal))
            && gitHubHandler.Requests.All(request => !privateText.Any(text => (request.Uri + request.Headers + request.Body).Contains(text, StringComparison.Ordinal))));
        Check("H1c the picture goes as a PNG image block before the question",
            first.Contains("\"type\":\"image\"", StringComparison.Ordinal) && first.Contains("image/png", StringComparison.Ordinal)
            && first.IndexOf(Convert.ToBase64String(HelpFixturePicture), StringComparison.Ordinal) < first.IndexOf("question-marker", StringComparison.Ordinal));

        var plain = new HelpFakeHandler((_, _) => HelpJson(HelpMessage("end_turn", HelpTextBlock("plain answer"))));
        using var plainHttp = new HttpClient(plain);
        var earlier = new List<HelpTurn> { new("earlier-question-one", "earlier-answer-one"), new("earlier-question-two", "earlier-answer-two") };
        var withoutPicture = HelpAsk(plainHttp, gitHubHttp, HelpFixtureToken, HelpFixtureQuestion(null, earlier));
        string body = plain.Requests[0].Body;
        int[] order = new[] { "earlier-question-one", "earlier-answer-one", "earlier-question-two", "earlier-answer-two", "state-version-marker", "question-marker" }
            .Select(text => body.IndexOf(text, StringComparison.Ordinal)).ToArray();
        Check("H1c without a picture no image block is sent", withoutPicture.Outcome == AiOutcome.Success
            && !body.Contains("\"image\"", StringComparison.Ordinal) && !body.Contains("attached picture", StringComparison.Ordinal));
        Check("H1c earlier turns go first, oldest first, as user and assistant turns, then the new question with the state",
            order.All(index => index >= 0) && order.Zip(order.Skip(1)).All(pair => pair.First < pair.Second)
            && HelpRoles(body).SequenceEqual(["user", "assistant", "user", "assistant", "user"]));

        int gitHubBefore = gitHubHandler.Requests.Count;
        var noToken = new HelpFakeHandler((_, _) => HelpJson(HelpMessage("end_turn", HelpTextBlock("answer without the source"))));
        using var noTokenHttp = new HttpClient(noToken);
        var unread = HelpAsk(noTokenHttp, gitHubHttp, null, HelpFixtureQuestion());
        Check("H1c without a token there is no tool and no GitHub call, and SourceReadable is false",
            unread.Outcome == AiOutcome.Success && !unread.SourceReadable && unread.Sources.Count == 0
            && gitHubHandler.Requests.Count == gitHubBefore && !noToken.Requests[0].Body.Contains("\"tools\"", StringComparison.Ordinal)
            && noToken.Requests[0].Body.Contains("cannot read its source", StringComparison.Ordinal));

        // Every round asks for two searches and three reads of files that do not exist, so each read reaches GitHub.
        var greedyGitHub = new HelpFakeHandler(HelpFakeGitHub);
        using var greedyGitHubHttp = new HttpClient(greedyGitHub);
        var greedy = new HelpFakeHandler((request, call) => HelpJson(HelpForcedAnswer(HelpSent(request))
            ? HelpMessage("end_turn", HelpTextBlock("capped answer"))
            : HelpMessage("tool_use",
                HelpToolUse("toolu_s1_" + call, "search_code", "{\"query\":\"term\"}"),
                HelpToolUse("toolu_s2_" + call, "search_code", "{\"query\":\"term\"}"),
                HelpToolUse("toolu_r1_" + call, "read_file", "{\"path\":\"Core/Missing" + call + "a.cs\",\"from_line\":1,\"to_line\":9}"),
                HelpToolUse("toolu_r2_" + call, "read_file", "{\"path\":\"Core/Missing" + call + "b.cs\",\"from_line\":1,\"to_line\":9}"),
                HelpToolUse("toolu_r3_" + call, "read_file", "{\"path\":\"Core/Missing" + call + "c.cs\",\"from_line\":1,\"to_line\":9}"))));
        using var greedyHttp = new HttpClient(greedy);
        var capped = HelpAsk(greedyHttp, greedyGitHubHttp, HelpFixtureToken, HelpFixtureQuestion());
        int searchCalls = greedyGitHub.Requests.Count(request => request.Uri.Contains("/search/code", StringComparison.Ordinal));
        string last = greedy.Requests[^1].Body;
        Check("H1c at most 12 GitHub calls and 3 searches per question, then Claude is asked to answer with what it has",
            capped.Outcome == AiOutcome.Success && capped.Text == "capped answer" && greedyGitHub.Requests.Count == HelpAssistant.MaxGitHubCalls
            && searchCalls == HelpAssistant.MaxSearches && greedy.Requests.Count == 4 && HelpForcedAnswer(last)
            && last.Contains("search limit for this question is reached", StringComparison.Ordinal)
            && last.Contains("reading limit for this question is reached", StringComparison.Ordinal) && capped.Sources.Count == 0);

        var loopingGitHub = new HelpFakeHandler(HelpFakeGitHub);
        using var loopingGitHubHttp = new HttpClient(loopingGitHub);
        var looping = new HelpFakeHandler((request, call) => HelpJson(HelpForcedAnswer(HelpSent(request))
            ? HelpMessage("end_turn", HelpTextBlock("last round answer"))
            : HelpMessage("tool_use", HelpToolUse("toolu_list_" + call, "list_files", "{\"prefix\":\"docs\"}"))));
        using var loopingHttp = new HttpClient(looping);
        var rounds = HelpAsk(loopingHttp, loopingGitHubHttp, HelpFixtureToken, HelpFixtureQuestion());
        Check("H1c at most 8 Claude rounds per question, the last one unable to call tools",
            rounds.Outcome == AiOutcome.Success && rounds.Text == "last round answer" && looping.Requests.Count == HelpAssistant.MaxRounds
            && looping.Requests.Take(HelpAssistant.MaxRounds - 1).All(request => !HelpForcedAnswer(request.Body))
            && HelpForcedAnswer(looping.Requests[^1].Body) && loopingGitHub.Requests.Count == 1);

        var limitedGitHub = new HelpFakeHandler((_, _) =>
        {
            var response = HelpJson("{\"message\":\"API rate limit exceeded\"}", HttpStatusCode.Forbidden);
            response.Headers.Add("x-ratelimit-remaining", "0");
            response.Headers.Add("x-ratelimit-reset", "1900000000");
            return response;
        });
        using var limitedGitHubHttp = new HttpClient(limitedGitHub);
        var limitedClaude = new HelpFakeHandler((request, call) => HelpJson(HelpForcedAnswer(HelpSent(request))
            ? HelpMessage("end_turn", HelpTextBlock("answer after the limit"))
            : HelpMessage("tool_use", HelpToolUse("toolu_a_" + call, "search_code", "{\"query\":\"term\"}"),
                HelpToolUse("toolu_b_" + call, "read_file", "{\"path\":\"Core/Beta.cs\",\"from_line\":1,\"to_line\":3}"))));
        using var limitedClaudeHttp = new HttpClient(limitedClaude);
        var limited = HelpAsk(limitedClaudeHttp, limitedGitHubHttp, HelpFixtureToken, HelpFixtureQuestion());
        Check("H1c a GitHub limit stops the reading for the question and reports when it resets",
            limited.Outcome == AiOutcome.Success && limited.GitHubLimitResetsAt == DateTimeOffset.FromUnixTimeSeconds(1_900_000_000)
            && limited.SourceReadable && limitedGitHub.Requests.Count == 1 && limitedClaude.Requests.Count == 2
            && limitedClaude.Requests[1].Body.Contains("rate limit stopped the reading until", StringComparison.Ordinal));
    }

    private static void HelpOutcomeTests()
    {
        string error(string type) => "{\"type\":\"error\",\"error\":{\"type\":\"" + type + "\",\"message\":\"fixture\"}}";
        var noGitHub = new HelpFakeHandler((_, _) => throw new InvalidOperationException("GitHub must not be called"));
        using var noGitHubHttp = new HttpClient(noGitHub);
        void Case(string label, HttpStatusCode status, string response, AiOutcome expected, bool disconnected = false)
        {
            var handler = new HelpFakeHandler((_, _) => disconnected ? throw new HttpRequestException("fixture connection failure") : HelpJson(response, status));
            using var http = new HttpClient(handler);
            try
            {
                var answer = HelpAsk(http, noGitHubHttp, HelpFixtureToken, HelpFixtureQuestion());
                Check(label, answer.Outcome == expected && handler.Requests.Count == 1
                    && (expected == AiOutcome.Success) == (answer.Text.Length > 0) && answer.Sources.Count == 0);
            }
            catch { Check(label, false); }
        }
        Case("H1c a completed answer is a success", HttpStatusCode.OK, HelpMessage("end_turn", HelpTextBlock("fixture answer")), AiOutcome.Success);
        Case("H1c a refusal never becomes an answer", HttpStatusCode.OK, HelpMessage("refusal", HelpTextBlock("ignored")), AiOutcome.Refusal);
        Case("H1c truncated output is not presented as a complete answer", HttpStatusCode.OK, HelpMessage("max_tokens", HelpTextBlock("ignored")), AiOutcome.Incomplete);
        Case("H1c 401 reports a wrong key", HttpStatusCode.Unauthorized, error("authentication_error"), AiOutcome.WrongKey);
        Case("H1c 402 billing error is distinguished", HttpStatusCode.PaymentRequired, error("billing_error"), AiOutcome.Billing);
        Case("H1c another 402 is not called a billing error", HttpStatusCode.PaymentRequired, error("invalid_request_error"), AiOutcome.Unavailable);
        Case("H1c 429 is rate limited without retry", HttpStatusCode.TooManyRequests, error("rate_limit_error"), AiOutcome.RateLimited);
        Case("H1c server failure is reported without retry", HttpStatusCode.InternalServerError, error("api_error"), AiOutcome.Unavailable);
        Case("H1c connection failure is reported without retry", HttpStatusCode.OK, "", AiOutcome.NoConnection, true);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var unused = new HelpFakeHandler((_, _) => HelpJson(HelpMessage("end_turn", HelpTextBlock("fixture answer"))));
        using var cancelledHttp = new HttpClient(unused);
        bool cancelledAsRequested;
        try { HelpAsk(cancelledHttp, noGitHubHttp, HelpFixtureToken, HelpFixtureQuestion(), cancelled.Token); cancelledAsRequested = false; }
        catch (OperationCanceledException) { cancelledAsRequested = true; }
        Check("H1c cancellation propagates and starts no request", cancelledAsRequested && unused.Requests.Count == 0 && noGitHub.Requests.Count == 0);
    }

    private static void HelpGitHubReaderTests()
    {
        var handler = new HelpFakeHandler(HelpFakeGitHub);
        using var http = new HttpClient(handler);
        object gitHub = NewHelpGitHub(http);
        string listed = HelpGitHubCall<string>(gitHub, "ListFilesAsync", "core/");
        string again = HelpGitHubCall<string>(gitHub, "ListFilesAsync", "docs");
        Check("H1a list_files reads the tree once and returns only the blobs under the prefix",
            listed == "Core/Alpha.cs\nCore/Beta.cs\n" && again == "docs/Guide.md\n" && handler.Requests.Count == 1
            && handler.Requests[0].Uri == "https://api.github.com/repos/pcarvalho75/FamilyQuant2/git/trees/master?recursive=1");
        string headers = handler.Requests[0].Headers;
        Check("H1a GitHub requests carry the token, the program's User-Agent and the API version",
            headers.Contains("Authorization: Bearer " + HelpFixtureToken, StringComparison.Ordinal)
            && headers.Contains("User-Agent: FamilyQuant/9.8.7.6", StringComparison.Ordinal)
            && headers.Contains("X-GitHub-Api-Version: 2022-11-28", StringComparison.Ordinal));

        var (text, source) = HelpGitHubCall<(string, HelpSource?)>(gitHub, "ReadFileAsync", "Core/Alpha.cs", 10, 2000);
        var (cached, _) = HelpGitHubCall<(string, HelpSource?)>(gitHub, "ReadFileAsync", "/Core/Alpha.cs", 899, 905);
        Check("H1a read_file returns numbered lines, caps one read at 400 lines and fetches the file once",
            source == new HelpSource("Core/Alpha.cs", 10, 409) && text.StartsWith("Core/Alpha.cs lines 10-409 of 900\n10: alpha line 10\n", StringComparison.Ordinal)
            && text.EndsWith("409: alpha line 409\n", StringComparison.Ordinal) && !text.Contains('\r')
            && cached == "Core/Alpha.cs lines 899-900 of 900\n899: alpha line 899\n900: alpha line 900\n"
            && handler.Requests.Count == 2 && handler.Requests[1].Uri.EndsWith("/contents/Core/Alpha.cs?ref=master", StringComparison.Ordinal)
            && handler.Requests[1].Headers.Contains("application/vnd.github.raw", StringComparison.Ordinal));
        var (outside, none) = HelpGitHubCall<(string, HelpSource?)>(gitHub, "ReadFileAsync", "Core/Beta.cs", 7, 9);
        var (escape, refused) = HelpGitHubCall<(string, HelpSource?)>(gitHub, "ReadFileAsync", "Core/../../other/secret", 1, 5);
        var (missing, absent) = HelpGitHubCall<(string, HelpSource?)>(gitHub, "ReadFileAsync", "Core/Gamma.cs", 1, 5);
        Check("H1a read_file reports a range past the end, a path that leaves the repository and a missing file without a source",
            none == null && outside == "\"Core/Beta.cs\" has 3 lines." && refused == null && escape.Contains("not a repository file path", StringComparison.Ordinal)
            && absent == null && missing.Contains("not found", StringComparison.Ordinal)
            && handler.Requests.Count == 4 && !handler.Requests.Any(request => request.Uri.Contains("..", StringComparison.Ordinal)));

        string found = HelpGitHubCall<string>(gitHub, "SearchCodeAsync", "beta two");
        Check("H1a search_code asks GitHub inside the repository and returns paths with a compact fragment",
            found == "Core/Beta.cs\n  beta two\n"
            && handler.Requests[^1].Uri.StartsWith("https://api.github.com/search/code?q=beta%20two+repo:pcarvalho75/FamilyQuant2", StringComparison.Ordinal));

        DateTimeOffset reset = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000);
        object? Stop(HttpResponseMessage response)
        {
            using var limited = new HttpClient(new HelpFakeHandler((_, _) => response));
            try { HelpGitHubCall<string>(NewHelpGitHub(limited), "SearchCodeAsync", "anything"); return "no exception"; }
            catch (Exception exception) { return exception; }
        }
        DateTimeOffset? ResetOf(object? exception) => exception?.GetType().Name == "HelpGitHubStopException"
            ? (DateTimeOffset?)exception.GetType().GetProperty("LimitResetsAt")!.GetValue(exception) : DateTimeOffset.MinValue;
        var primary = HelpJson("{\"message\":\"API rate limit exceeded\"}", HttpStatusCode.Forbidden);
        primary.Headers.Add("x-ratelimit-remaining", "0");
        primary.Headers.Add("x-ratelimit-reset", "1900000000");
        Check("H1a a 403 with no requests remaining stops the reading at GitHub's reset time", ResetOf(Stop(primary)) == reset);
        var secondary = HelpJson("{\"message\":\"You have exceeded a secondary rate limit.\"}", HttpStatusCode.Forbidden);
        secondary.Headers.Add("x-ratelimit-remaining", "8");
        DateTimeOffset before = DateTimeOffset.UtcNow;
        Check("H1a a secondary-limit 403 stops the reading about a minute ahead",
            ResetOf(Stop(secondary)) is { } later && later > before.AddSeconds(50) && later < before.AddSeconds(70));
        var tooMany = HelpJson("{}", HttpStatusCode.TooManyRequests);
        tooMany.Headers.Add("Retry-After", "30");
        Check("H1a a 429 with Retry-After stops the reading for that long",
            ResetOf(Stop(tooMany)) is { } retry && retry > before.AddSeconds(25) && retry < before.AddSeconds(40));
        Check("H1a a 401 stops the reading as an unreadable source", ResetOf(Stop(HelpJson("{}", HttpStatusCode.Unauthorized))) == null);
        Check("H1a another 403 is an ordinary failure, not a limit",
            Stop(HelpJson("{\"message\":\"Resource not accessible\"}", HttpStatusCode.Forbidden)) is HttpRequestException);
    }
}
