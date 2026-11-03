using FamilyQuant.Panels;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> PanelPfamCommandTable() =>
    [
        new("--test-panel-pfam", 72, TestCommandPhase.Raw, a => a.Contains("--test-panel-pfam", StringComparer.Ordinal), (_, _) =>
        {
            PanelPfamTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--check-panel-pfam", 73, TestCommandPhase.Raw, a => a.Length > 0 && a[0] == "--check-panel-pfam", (a, _) =>
        {
            if (a.Length != 3) { Console.Error.WriteLine("--check-panel-pfam <project.fqproj-or-fcf> <new-output-directory>"); return 2; }
            if (Directory.Exists(a[2])) throw new IOException("Choose a new output directory.");
            FamilyClusterFile file = a[1].EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)
                ? ProjectStore.ReadFqproj(a[1]).fcf : FamilyClusterFile.Load(a[1]);
            var panel = file.FamilyPanel ?? throw new InvalidDataException("Import a family panel before checking its domains.");
            var report = PanelPfamChecker.CheckAsync(panel).GetAwaiter().GetResult();
            Directory.CreateDirectory(a[2]);
            File.WriteAllText(Path.Combine(a[2], "pfam-check.csv"), PanelPfamChecker.BuildCsv(report), new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(a[2], "pfam-check.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(report.Rows.Count.ToString(CultureInfo.InvariantCulture) + " rows; "
                + report.Mismatches.ToString(CultureInfo.InvariantCulture) + " name mismatches; "
                + (report.Complete ? "all checks completed" : "could not check every accession"));
            return report.Complete ? 0 : 1;
        }),
    ];

    private sealed class PfamFixtureHandler : HttpMessageHandler
    {
        public int Requests;
        public bool Offline;
        public bool RateLimited;
        public bool AlwaysLimited;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("Fixture offline");
            if (RateLimited && (AlwaysLimited || Requests == 1))
            {
                var limited = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                limited.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                return Task.FromResult(limited);
            }
            string accession = request.RequestUri!.Segments.Last().Trim('/');
            if (accession == "PF99999") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            if (accession == "PF99998") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { metadata = new
                {
                    accession = accession == "PF99997" ? "wrong" : accession, source_database = "pfam",
                    name = new { name = "Long domain description", @short = "Short_domain" },
                } })),
            });
        }
    }

    private static void PanelPfamTests()
    {
        var panel = new FamilyPanel { Families = new()
        {
            new() { Key = "a", Pfam = new()
            {
                new() { Accession = "PF00001", Name = "Short_domain" },
                new() { Accession = "PF00001", Name = "Long domain description" },
                new() { Accession = "PF00001", Name = "Wrong name" },
                new() { Accession = "PF00001", Name = "" },
                new() { Accession = "PF99999", Name = "Missing" },
                new() { Accession = "PF99998", Name = "Unavailable" },
                new() { Accession = "PF99997", Name = "Wrong reply" },
                new() { Accession = "malformed", Name = "Invalid" },
            } },
        } };
        string before = JsonSerializer.Serialize(panel);
        using var handler = new PfamFixtureHandler();
        var report = PanelPfamChecker.CheckAsync(panel, handler).GetAwaiter().GetResult();
        Check("J1c every original accession/name row is reported", report.Rows.Count == 8);
        Check("J1c both official full and short names match", report.Rows.Count(r => r.Status == PanelPfamCheckStatus.Match) == 2);
        Check("J1c wrong panel name is listed without correction", report.Mismatches == 2
            && report.Rows.Any(r => r.PanelName == "Wrong name" && r.Status == PanelPfamCheckStatus.NameMismatch));
        Check("J1c an empty claimed name cannot pass as a name match", report.Rows.Any(r => r.PanelName == "" && r.Status == PanelPfamCheckStatus.NameMismatch));
        Check("J1c unknown and invalid accessions are explicit", report.Rows.Count(r => r.Status == PanelPfamCheckStatus.NotFound) == 2);
        Check("J1c provider refusal and wrong returned accession cannot pass", !report.Complete
            && report.Rows.Count(r => r.Status == PanelPfamCheckStatus.CouldNotCheck) == 2);
        Check("J1c duplicate accessions share one provider request", handler.Requests == 4);
        Check("J1c checking never rewrites source data", JsonSerializer.Serialize(panel) == before);
        string csv = PanelPfamChecker.BuildCsv(report);
        using var offline = new PfamFixtureHandler { Offline = true };
        var unavailable = PanelPfamChecker.CheckAsync(panel, offline).GetAwaiter().GetResult();
        Check("J1c offline says could not check, rather than no mismatch", !unavailable.Complete
            && unavailable.Rows.Any(r => r.Status == PanelPfamCheckStatus.CouldNotCheck && r.Reason.Length > 0));
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string portuguese = PanelPfamChecker.BuildCsv(report);
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            Check("J1c exported mismatch CSV is locale invariant", csv == portuguese && csv == PanelPfamChecker.BuildCsv(report));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        bool stopped = false;
        try { PanelPfamChecker.CheckAsync(panel, handler, cancelled.Token).GetAwaiter().GetResult(); }
        catch (OperationCanceledException) { stopped = true; }
        Check("J1c cancellation is not reported as a successful check", stopped);
        var single = new FamilyPanel { Families = new() { new() { Key = "rate", Pfam = new() { new() { Accession = "PF00001", Name = "Short_domain" } } } } };
        using var temporaryLimit = new PfamFixtureHandler { RateLimited = true };
        var recovered = PanelPfamChecker.CheckAsync(single, temporaryLimit).GetAwaiter().GetResult();
        Check("J1c a transient rate limit retries after the provider delay", temporaryLimit.Requests == 2
            && recovered.Complete && recovered.Rows.Single().Status == PanelPfamCheckStatus.Match);
        using var persistentLimit = new PfamFixtureHandler { RateLimited = true, AlwaysLimited = true };
        var refused = PanelPfamChecker.CheckAsync(single, persistentLimit).GetAwaiter().GetResult();
        Check("J1c rate-limit retries are bounded and remain unavailable on refusal", persistentLimit.Requests == 3
            && !refused.Complete && refused.Rows.Single().Status == PanelPfamCheckStatus.CouldNotCheck);
    }
}
