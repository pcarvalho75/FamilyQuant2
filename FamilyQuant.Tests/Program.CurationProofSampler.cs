using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> CurationProofSamplerCommands() =>
    [new("--test-curation-proof-sampler", 21117, TestCommandPhase.Raw,
        args => args is ["--test-curation-proof-sampler"], (_, _) =>
        { CurationProofSamplerTests(); return failed == 0 ? 0 : 1; })];

    private sealed class CurationPrivateMemorySampler(Func<long> read)
    {
        private int reading;
        private long peak, successful, failures, skipped;
        private Exception? lastFailure;
        public long Peak => Interlocked.Read(ref peak);
        public long SuccessfulSamples => Interlocked.Read(ref successful);
        public long FailedSamples => Interlocked.Read(ref failures);
        public long SkippedSamples => Interlocked.Read(ref skipped);
        public Exception? LastFailure => Volatile.Read(ref lastFailure);

        public void Sample()
        {
            // Skip overlapping timer ticks rather than sharing Process state or queuing a backlog.
            if (Interlocked.CompareExchange(ref reading, 1, 0) != 0) { Interlocked.Increment(ref skipped); return; }
            try
            {
                long current = read();
                Interlocked.Exchange(ref peak, Math.Max(Peak, current));
                Interlocked.Increment(ref successful);
            }
            catch (Exception error)
            {
                // Timer exceptions must never escape on a ThreadPool thread and kill the proof.
                Volatile.Write(ref lastFailure, error);
                Interlocked.Increment(ref failures);
            }
            finally { Volatile.Write(ref reading, 0); }
        }
    }

    private static void CurationProofSamplerTests()
    {
        Section("Z12p5f4 private-memory sampler concurrency and failure isolation");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int active = 0, maximum = 0;
        var sampler = new CurationPrivateMemorySampler(() =>
        {
            int now = Interlocked.Increment(ref active);
            maximum = Math.Max(maximum, now);
            entered.Set(); release.Wait(); Interlocked.Decrement(ref active); return 512;
        });
        var first = Task.Run(sampler.Sample);
        bool started = entered.Wait(TimeSpan.FromSeconds(5));
        Task? overlaps = null;
        bool returnedWithoutBlocking = false;
        try
        {
            if (started)
            {
                overlaps = Task.Run(() => Parallel.For(0, 64, _ => sampler.Sample()));
                returnedWithoutBlocking = overlaps.Wait(TimeSpan.FromSeconds(5));
            }
        }
        finally { release.Set(); overlaps?.GetAwaiter().GetResult(); first.GetAwaiter().GetResult(); }
        Check("sampler serializes access to one Process reader under overlapping timer ticks", started && returnedWithoutBlocking && maximum == 1
            && sampler.SuccessfulSamples == 1 && sampler.SkippedSamples == 64 && sampler.Peak == 512);

        int calls = 0;
        var transient = new CurationPrivateMemorySampler(() => Interlocked.Increment(ref calls) switch
        {
            1 => throw new InvalidOperationException("Synthetic process sampling failure."),
            2 => 2048,
            _ => 1024
        });
        Task.Run(transient.Sample).GetAwaiter().GetResult();
        Check("a sampling exception is recorded without escaping the callback", transient.FailedSamples == 1
            && transient.SuccessfulSamples == 0 && transient.LastFailure is InvalidOperationException);
        transient.Sample(); transient.Sample();
        Check("sampler resumes after a failed sample and retains the largest successful value", transient.FailedSamples == 1
            && transient.SuccessfulSamples == 2 && transient.Peak == 2048);
    }
}
