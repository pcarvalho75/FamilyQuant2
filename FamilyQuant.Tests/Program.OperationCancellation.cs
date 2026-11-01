using FamilyQuant.Operations;
using FamilyQuant.Homology;
using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace FamilyQuant.Tests
{
    internal static partial class Program
    {
        private static void LongOperationCancellationTests(string workDir)
        {
            using var source = new CancellationTokenSource();
            source.Cancel();
            var operation = new OperationContext(source.Token);
            var missing = new FileInfo(Path.Combine(workDir, "cancelled_missing.fasta"));
            Check("cancelled clustering never opens its input or launches USEARCH",
                Throws<OperationCanceledException>(() => USearchTools.GenerateFCF(missing, 70, operation: operation)));
            Check("cancelled alignment never creates its scratch input",
                Throws<OperationCanceledException>(() => USearchTools.Align(new List<FastaItemExtended>(), operation)));
            Check("cancelled identity scan never starts a probe",
                Throws<OperationCanceledException>(() => FamilyClusterFile.ScanIdentities(missing.FullName, operation: operation)));

            string saved = Path.Combine(workDir, "cancelled_save.fcf");
            File.WriteAllText(saved, "original");
            var document = new FamilyClusterFile();
            Check("cancelled document save preserves the existing file",
                Throws<OperationCanceledException>(() => document.Save(saved, operation)) && File.ReadAllText(saved) == "original");
            Check("cancelled redundancy removal preserves the existing output",
                Throws<OperationCanceledException>(() => RedundancyTools.RemoveRedundancy(missing, 100, saved, operation)) &&
                File.ReadAllText(saved) == "original");

            string plpPath = WriteDenominatorFixture(workDir, "cancel_attribution.plp", "NONE");
            var plp = new PatternTools.PLP.PatternLabProject(new FileInfo(plpPath));
            var item = new FastaItemExtended { SequenceIdentifier = "sp|X|PROT1", UserID = 10, USearchID = 10, XICUser = 0.25 };
            var target = new FamilyClusterFile { MyExtendedFastaItems = new List<FastaItemExtended> { item } };
            using var during = new CancellationTokenSource();
            var report = new InlineOperationProgress(value => { if (value.Completed == 1) during.Cancel(); });
            Check("cancelling attribution preparation leaves all displayed shares untouched",
                Throws<OperationCanceledException>(() => target.AttributeXICToGroups(plp, NormalizationMode.IdentifiedSignal,
                    SharedPeptidePolicy.FamilyUnique, new OperationContext(during.Token, report))) && item.XICUser == 0.25);

            using var saveCancel = new CancellationTokenSource();
            var saveReport = new InlineOperationProgress(value => { if (value.Completed == 1) saveCancel.Cancel(); });
            Check("cancelling a prepared save leaves the original and removes its temporary file",
                Throws<OperationCanceledException>(() => document.Save(saved, new OperationContext(saveCancel.Token, saveReport))) &&
                File.ReadAllText(saved) == "original" && Directory.GetFiles(workDir, "cancelled_save.fcf.*.tmp").Length == 0);
            NativeProcessCancellationTests(workDir);
            Check("cancelled homology database construction does not enumerate its input",
                Throws<OperationCanceledException>(() => HomologyDatabase.Build(new[] { ("A", "MKAWLL") }, operation: operation)));
            var database = HomologyDatabase.Build(new[] { ("A", "MKAWLL"), ("B", "QQQQQQ") });
            using var homologyCancel = new CancellationTokenSource();
            var homologyReport = new InlineOperationProgress(value => { if (value.Completed == 1) homologyCancel.Cancel(); });
            Check("cancelling homology does not return a partial hit table",
                Throws<OperationCanceledException>(() => HomologyMapper.Search(new[] { "MKAWLL", "QQQQQQ" }, database,
                    operation: new OperationContext(homologyCancel.Token, homologyReport))));
            string projectPath = Path.Combine(workDir, "cancelled_project.fqproj");
            File.WriteAllText(projectPath, "original");
            Check("cancelled project read does not open its input",
                Throws<OperationCanceledException>(() => ProjectStore.ReadFqproj(projectPath, operation)));
            var manifest = new QuantManifest { FormatVersion = 1 };
            using var projectCancel = new CancellationTokenSource();
            var projectReport = new InlineOperationProgress(value =>
            {
                if (value.Stage == "project-save" && value.Completed == 1) projectCancel.Cancel();
            });
            Check("cancelled prepared project save preserves file and manifest state",
                Throws<OperationCanceledException>(() => ProjectStore.WriteFqproj(projectPath, document,
                    Array.Empty<QuantRow>(), manifest, new OperationContext(projectCancel.Token, projectReport))) &&
                File.ReadAllText(projectPath) == "original" && manifest.FormatVersion == 1 &&
                manifest.SpectrumComparisonRowCount == null && Directory.GetFiles(workDir, "cancelled_project.fqproj.*.tmp").Length == 0);
        }

        private static void NativeProcessCancellationTests(string workDir)
        {
            string pidFile = Path.Combine(workDir, "native_cancel_" + Guid.NewGuid().ToString("N") + ".pid");
            string executable = Path.ChangeExtension(typeof(Program).Assembly.Location, ".exe");
            MethodInfo run = typeof(USearchTools).GetMethod("RunUSearch", BindingFlags.Static | BindingFlags.NonPublic)!;
            using var cancel = new CancellationTokenSource();
            int childPid = 0;
            Task task = Task.Run(() =>
            {
                try
                {
                    run.Invoke(null, new object[] { executable, "--operation-native-wait \"" + pidFile + "\"", workDir,
                        new OperationContext(cancel.Token) });
                }
                catch (TargetInvocationException ex) when (ex.InnerException != null)
                {
                    ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                }
            });
            try
            {
                bool started = SpinWait.SpinUntil(() => File.Exists(pidFile), TimeSpan.FromSeconds(15));
                Check("the native cancellation fixture starts its own helper process", started);
                if (!started) return;
                childPid = int.Parse(File.ReadAllText(pidFile), System.Globalization.CultureInfo.InvariantCulture);
                cancel.Cancel();
                bool completed = SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(10));
                bool wasCancelled = false;
                if (completed)
                {
                    try { task.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) { wasCancelled = true; }
                }
                Check("cancelling a running native operation returns cancellation promptly", completed && wasCancelled);
                Check("the cancelled native helper is no longer running", !IsProcessAlive(childPid));
            }
            finally
            {
                cancel.Cancel();
                if (childPid != 0 && IsProcessAlive(childPid))
                {
                    using Process child = Process.GetProcessById(childPid);
                    if (string.Equals(child.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase))
                    {
                        child.Kill(entireProcessTree: true);
                        child.WaitForExit();
                    }
                }
                SpinWait.SpinUntil(() => task.IsCompleted, TimeSpan.FromSeconds(5));
                if (File.Exists(pidFile)) File.Delete(pidFile);
            }
        }

        private static bool IsProcessAlive(int pid)
        {
            try { using Process process = Process.GetProcessById(pid); return !process.HasExited; }
            catch (ArgumentException) { return false; }
        }
    }
}
