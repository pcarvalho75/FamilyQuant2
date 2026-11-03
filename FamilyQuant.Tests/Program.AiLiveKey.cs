using FamilyQuant.Ai;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> AiLiveKeyCommands() =>
    [new("--test-ai-live-key", 10, TestCommandPhase.Raw,
        args => args.Length > 0 && args[0] == "--test-ai-live-key", (args, _) =>
        {
            if (args.Length != 2) throw new ArgumentException("Use --test-ai-live-key <key-file>.");
            try
            {
                string key = File.ReadAllText(args[1]).Trim();
                var outcome = new AiTransport().SendAsync(key, "Reply with OK.", 32, CancellationToken.None).GetAwaiter().GetResult().Outcome;
                string result = outcome switch
                {
                    AiOutcome.Success or AiOutcome.Refusal or AiOutcome.Incomplete => "accepted",
                    AiOutcome.WrongKey => "rejected (HTTP 401)",
                    AiOutcome.Billing => "billing problem (HTTP 402)",
                    AiOutcome.RateLimited => "rate limited (HTTP 429)",
                    AiOutcome.NoConnection => "connection failed",
                    _ => "service unavailable or request failed",
                };
                Console.WriteLine("Single live key test: " + result + ".");
                return outcome is AiOutcome.Success or AiOutcome.Refusal or AiOutcome.Incomplete ? 0 : 1;
            }
            catch (Exception)
            {
                Console.WriteLine("Single live key test could not complete; no key value was printed.");
                return 1;
            }
        })];
}
