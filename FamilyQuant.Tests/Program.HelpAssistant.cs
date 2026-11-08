using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
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
