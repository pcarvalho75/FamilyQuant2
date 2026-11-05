using FamilyQuant.DeNovo;
using FamilyQuant.Evidence;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static MethodInfo ReuseMethod(Type type, string name) =>
        type.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;

    private static void CoreReuseContractTests()
    {
        string?[] inputs = { null, "", "plain", "-1", "=SUM(1)", "a,b", "a\"b", "a\nb", "a\tb" };
        string[] guarded = { "", "", "plain", "'-1", "'=SUM(1)", "\"a,b\"", "\"a\"\"b\"", "\"a\nb\"", "a\tb" };
        string[] quoted = { "\"\"", "\"\"", "\"plain\"", "\"-1\"", "\"=SUM(1)\"", "\"a,b\"", "\"a\"\"b\"", "\"a\nb\"", "\"a\tb\"" };
        foreach (Type type in new[] { typeof(EvidenceExports), typeof(LeftOutList), typeof(ValidationReport) })
            Check(type.Name + " retains guarded minimal CSV bytes", inputs.Select((v, i) =>
                (string)ReuseMethod(type, "Field").Invoke(null, new object?[] { v })! == guarded[i]).All(v => v));
        foreach (Type type in new[] { typeof(CsvReport), typeof(ThreeWayPartition), typeof(HomologyCsv) })
            Check(type.Name + " retains always-quoted CSV bytes without a formula prefix", inputs.Select((v, i) =>
                (string)ReuseMethod(type, "Field").Invoke(null, new object?[] { v })! == quoted[i]).All(v => v));

        CultureInfo oldCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            foreach (var (type, name) in new[] { (typeof(EvidenceExports), "Number"), (typeof(LeftOutList), "Count"),
                (typeof(ValidationReport), "Number"), (typeof(CoherenceCheck), "Count"), (typeof(AnalyticsCsv), "Int") })
                Check(type.Name + " integer formatting remains invariant", (string)ReuseMethod(type, name).Invoke(null, new object[] { -1234 })! == "-1234");
            foreach (var (type, name) in new[] { (typeof(AnalyticsCsv), "Num"), (typeof(ThreeWayPartition), "Number") })
                Check(type.Name + " round-trip formatting remains invariant", (string)ReuseMethod(type, name).Invoke(null, new object[] { 1.25 })! == "1.25");
        }
        finally { CultureInfo.CurrentCulture = oldCulture; }

        var evidence = new ProteinEvidence { Pfam = new()
        {
            new() { Accession = "PF2" }, new() { Accession = "PF1" }, new() { Accession = "PF2" },
            new() { Accession = " " }, new() { Accession = "pf1" }
        } };
        string[] expected = { "PF1", "PF2", "pf1" };
        string[] architecture = ((IEnumerable<string>)ReuseMethod(typeof(EvidenceArchitecture), "Domains").Invoke(null, new object?[] { evidence })!).ToArray();
        var classifier = (HashSet<string>)ReuseMethod(typeof(EvidenceClassifier), "Domains").Invoke(null, new object?[] { evidence })!;
        Check("architecture Pfam keys retain distinct ordinal order", architecture.SequenceEqual(expected));
        Check("classifier Pfam membership keeps ordinal case and ignores blank accessions", classifier.SetEquals(expected));
        Check("missing Pfam input stays empty in both callers", new[] { typeof(EvidenceArchitecture), typeof(EvidenceClassifier) }
            .All(t => !((IEnumerable<string>)ReuseMethod(t, "Domains").Invoke(null, new object?[] { null })!).Any()));

        var proteins = new[] { new FastaItemExtended { SequenceIdentifier = "row:\u00e9", Sequence = "PEPTIDEK", UserID = 12, USearchID = 13, UserAnnotation = "=name" } };
        string classifierHash = EvidenceClassifier.Fingerprint(proteins);
        string[][] rows = { new[] { "ab", "c" }, new[] { "\u00e9", "" } };
        string neighbourHash = (string)ReuseMethod(typeof(NearestReviewed), "Fingerprint").Invoke(null, new object[] { rows })!;
        Console.WriteLine("HK5 classifier fingerprint: " + classifierHash);
        Console.WriteLine("HK5 neighbour fingerprint: " + neighbourHash);
        Check("classifier row fingerprints remain upper-case hex", classifierHash.Length == 64 && classifierHash == classifierHash.ToUpperInvariant());
        Check("neighbour row fingerprints remain lower-case hex", neighbourHash.Length == 64 && neighbourHash == neighbourHash.ToLowerInvariant());
        Check("classifier row fingerprint bytes retain the captured contract", classifierHash == "3B2B48FDBB512AF2A1C6A800228CB71CC520E73B3DF73617C509829DE8B29F32");
        Check("neighbour row fingerprint bytes retain the captured contract", neighbourHash == "4e33e2473c9537ccb73faf1fe733299e2e71b22c7ca76b132bc37332256de1fc");
        string other = (string)ReuseMethod(typeof(NearestReviewed), "Fingerprint").Invoke(null,
            new object[] { new[] { new[] { "a", "bc" }, new[] { "\u00e9", "" } } })!;
        Check("length prefixes distinguish ambiguous field concatenations", neighbourHash != other);
        bool nullRefused = false;
        try { ReuseMethod(typeof(NearestReviewed), "Fingerprint").Invoke(null, new object[] { new[] { new string[] { null! } } }); }
        catch (TargetInvocationException ex) when (ex.InnerException is ArgumentNullException) { nullRefused = true; }
        Check("neighbour fingerprint retains its null-field refusal", nullRefused);

        foreach (var (type, message) in new[]
        {
            (typeof(EvidenceFetcher), "UniProt refused the evidence batch (HTTP 400)."),
            (typeof(ReferencePanelFetcher), "UniProt refused a panel page (HTTP 400). No panel was made.")
        })
        {
            using var handler = new ReuseBadRequestHandler();
            using var client = new HttpClient(handler);
            bool refused = false;
            try { ((Task<HttpResponseMessage>)ReuseMethod(type, "GetWithRetry").Invoke(null,
                new object?[] { client, "https://example.invalid/fixture", CancellationToken.None, null, null })!).GetAwaiter().GetResult(); }
            catch (HttpRequestException ex) { refused = ex.StatusCode == HttpStatusCode.BadRequest && ex.Message == message; }
            Check(type.Name + " retains its exact HTTP refusal and one nonretryable request", refused && handler.Calls == 1);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool cancelled = false;
            try { ((Task<HttpResponseMessage>)ReuseMethod(type, "GetWithRetry").Invoke(null,
                new object?[] { client, "https://example.invalid/fixture", cancellation.Token, null, null })!).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) { cancelled = true; }
            Check(type.Name + " retains immediate user cancellation without another request", cancelled && handler.Calls == 1);
        }
    }

    private sealed class ReuseBadRequestHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (token.IsCancellationRequested) return Task.FromCanceled<HttpResponseMessage>(token);
            Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }
    }
}
