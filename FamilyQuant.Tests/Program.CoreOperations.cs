using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void CoreOperationTests(string workDir)
        {
            var reports = new List<OperationProgress>();
            var progress = new InlineOperationProgress(reports.Add);
            var success = CoreOperation.Run("fixture", context =>
            {
                context.Report("read", "Read one item", 1, 2);
                return 0;
            }, progress: progress);
            Check("a successful zero is distinct from cancellation", success.Status == OperationStatus.Succeeded && success.Value == 0);
            Check("operation progress preserves stage, counts and message",
                reports.Count == 1 && reports[0].Stage == "read" && reports[0].Completed == 1 &&
                reports[0].Total == 2 && reports[0].Message == "Read one item");

            using var before = new CancellationTokenSource();
            before.Cancel();
            bool called = false;
            var refused = CoreOperation.Run("fixture", context => { called = true; return 1; }, before.Token);
            Check("a pre-cancelled operation never starts its work", refused.Status == OperationStatus.Cancelled && !called);
            Check("cancelled operations cannot supply a scientific zero through Value",
                Throws<InvalidOperationException>(() => { _ = refused.Value; }));

            var cause = new InvalidDataException("fixture input is incomplete");
            var failedResult = CoreOperation.Run<int>("fixture", _ => throw cause);
            Check("operation failure preserves the original exception and input reason",
                failedResult.Status == OperationStatus.Failed && failedResult.Failure?.Cause == cause &&
                failedResult.Failure.Message == cause.Message && failedResult.Failure.Operation == "fixture");
            Check("a failed operation has no usable result", Throws<InvalidOperationException>(() => { _ = failedResult.Value; }));

            var unrelatedCancel = CoreOperation.Run<int>("fixture", _ => throw new OperationCanceledException());
            Check("an unrelated cancellation exception is not labelled as the user's cancellation",
                unrelatedCancel.Status == OperationStatus.Failed);

            Directory.CreateDirectory(workDir);
            string path = Path.Combine(workDir, "operation_commit.txt");
            File.WriteAllText(path, "original");
            using var during = new CancellationTokenSource();
            var cancelProgress = new InlineOperationProgress(_ => during.Cancel());
            var cancelled = CoreOperation.Run("fixture", context =>
            {
                context.Report("prepare", "Prepared output", 1, 1);
                context.Commit(() => File.WriteAllText(path, "changed"));
                return 1;
            }, during.Token, cancelProgress);
            Check("cancellation before commit leaves the existing file unchanged",
                cancelled.Status == OperationStatus.Cancelled && File.ReadAllText(path) == "original");

            using var late = new CancellationTokenSource();
            var committed = CoreOperation.Run("fixture", context =>
            {
                context.Commit(() => { File.WriteAllText(path, "committed"); late.Cancel(); });
                return 1;
            }, late.Token);
            Check("a completed commit is never reported as cancellation after changing the file",
                committed.Status == OperationStatus.Succeeded && File.ReadAllText(path) == "committed");

            var asyncResult = CoreOperation.RunAsync("fixture", context => Task.FromResult("done"))
                .GetAwaiter().GetResult();
            Check("async work uses the same success contract", asyncResult.Status == OperationStatus.Succeeded && asyncResult.Value == "done");
            var asyncFailure = CoreOperation.RunAsync<int>("fixture", _ => Task.FromException<int>(cause))
                .GetAwaiter().GetResult();
            Check("async work uses the same failure contract", asyncFailure.Status == OperationStatus.Failed && asyncFailure.Failure?.Cause == cause);
        }

        private sealed class InlineOperationProgress : IProgress<OperationProgress>
        {
            private readonly Action<OperationProgress> report;
            public InlineOperationProgress(Action<OperationProgress> report) => this.report = report;
            public void Report(OperationProgress value) => report(value);
        }
    }
}
