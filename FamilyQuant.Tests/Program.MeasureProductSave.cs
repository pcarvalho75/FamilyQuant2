using FamilyQuant.Operations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> ProductSaveMeasurementCommands() =>
    [new("--measure-product-save", 447, TestCommandPhase.Raw,
        args => args.Length == 3 && args[0] == "--measure-product-save", (args, _) => MeasureProductSave(args[1], args[2]))];

    private static int MeasureProductSave(string projectPath, string outputDirectory)
    {
        string input = OwnedComputeProofPath(projectPath), output = OwnedComputeProofPath(outputDirectory);
        foreach (string key in new[] { "DOTNET_GCHeapHardLimit", "COMPlus_GCHeapHardLimit", "DOTNET_GCHeapHardLimitPercent", "COMPlus_GCHeapHardLimitPercent" })
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key))) throw new InvalidOperationException("Uncapped Save measurement requires no process heap cap: " + key);
        if (Directory.Exists(output)) throw new IOException("Use a fresh Save measurement folder.");
        Directory.CreateDirectory(output);
        using var process = Process.GetCurrentProcess();
        File.WriteAllText(Path.Combine(output, "process.pid"), process.Id.ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("PID=" + process.Id + "; machine=" + Environment.MachineName + "; processors=" + Environment.ProcessorCount + "; uncapped=True");
        string before = FileFingerprint.Sha256(input);
        var readTimer = Stopwatch.StartNew(); var loaded = ProjectStore.ReadFqproj(input); double readSeconds = readTimer.Elapsed.TotalSeconds;
        string destination = Path.Combine(output, "saved.fqproj");
        var gate = new object(); long peakUsed = 0, peakHeap = 0, peakCommitted = 0, peakWorkingSet = 0; int samples = 0;
        void Sample()
        {
            lock (gate)
            {
                process.Refresh(); var info = GC.GetGCMemoryInfo();
                peakUsed = Math.Max(peakUsed, GC.GetTotalMemory(false)); peakHeap = Math.Max(peakHeap, info.HeapSizeBytes);
                peakCommitted = Math.Max(peakCommitted, info.TotalCommittedBytes); peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64); samples++;
            }
        }
        Sample(); long entryUsed = peakUsed, entryWorkingSet = peakWorkingSet;
        long allocatedBefore = GC.GetTotalAllocatedBytes(false); var timer = Stopwatch.StartNew();
        int exit = 1; string? error = null, check = null;
        using var sampling = new Timer(_ => Sample(), null, 0, 20);
        try
        {
            // Exactly the headless verified-Save service used by MainWindow.Documents:230.
            check = ShellProjectOperations.WriteVerifiedProject(destination, loaded.fcf, loaded.quant, loaded.manifest,
                operation: new OperationContext(progress: new SaveMeasurementProgress(output, Sample)));
            exit = 0;
        }
        catch (Exception failure)
        {
            error = failure.ToString(); File.WriteAllText(Path.Combine(output, "failure.txt"), error); Console.WriteLine(error);
        }
        finally
        {
            double saveSeconds = timer.Elapsed.TotalSeconds;
            using var drained = new ManualResetEvent(false);
            sampling.Dispose(drained); drained.WaitOne(); Sample();
            lock (gate)
            {
                var result = new { exit, machine = Environment.MachineName, process = process.Id, processors = Environment.ProcessorCount,
                    uncapped = true, readSeconds, saveSeconds, samples, entryManagedUsedBytes = entryUsed, entryWorkingSetBytes = entryWorkingSet,
                    sampledPeakManagedUsedBytes = peakUsed, peakReportedGCHeapBytes = peakHeap, peakCommittedGCBytes = peakCommitted,
                    sampledPeakSaveWorkingSetBytes = peakWorkingSet, processPeakWorkingSetBytes = process.PeakWorkingSet64,
                    saveAllocatedBytes = GC.GetTotalAllocatedBytes(false) - allocatedBefore,
                    savedFileBytes = File.Exists(destination) ? new FileInfo(destination).Length : 0,
                    inputUnchanged = before == FileFingerprint.Sha256(input), check,
                    route = "MainWindow.Documents -> ShellProjectOperations.WriteVerifiedProject -> ProjectStore.WriteFqproj -> SavedFileCheck.Capture/Curation -> FamilyClusterFile.ToJsonText",
                    error };
                File.WriteAllText(Path.Combine(output, "save-metrics.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("SAVE MEASUREMENT COMPLETE " + JsonSerializer.Serialize(result));
            }
        }
        return exit;
    }

    private sealed class SaveMeasurementProgress(string output, Action sample) : IProgress<OperationProgress>
    {
        public void Report(OperationProgress value)
        {
            sample(); string line = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) + "\t" + value.Stage + "\t" + value.Message;
            Console.WriteLine(line); File.AppendAllText(Path.Combine(output, "save-progress.log"), line + Environment.NewLine);
        }
    }
}
