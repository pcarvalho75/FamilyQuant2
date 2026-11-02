using System;

namespace FamilyQuant.Tests
{
    internal static class ReferenceGoldenHost
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == ShellMetadataSnapshot.Argument) return ShellMetadataSnapshot.Run(args);
            if (args.Length > 0 && args[0] == AssayBuildOutputSnapshot.Argument) return AssayBuildOutputSnapshot.Run(args);
            if (args.Length > 0 && args[0] == "-cluster_fast") return Environment.GetEnvironmentVariable("FQ_ASSAY_NOOUTPUT_NATIVE") == "1" ? 0 : 2;
            if (args.Length > 0 && args[0] == AssayPartitionSnapshot.Argument) return AssayPartitionSnapshot.Run(args);
            if (args.Length > 0 && args[0] == AssayBuildReportSnapshot.Argument) return AssayBuildReportSnapshot.Run(args);
            if (args.Length > 0 && args[0] == AssayViewSnapshot.Argument) return AssayViewSnapshot.Run(args);
            if (args.Length > 0 && args[0] == AssayCacheSnapshot.Argument) return AssayCacheSnapshot.Run(args);
            if (args.Length > 0 && args[0] == FcfCompatibilitySnapshot.Argument)
                return FcfCompatibilitySnapshot.Run(args);
            if (args.Length > 0 && (args[0] == ClusteringSnapshot.Argument || args[0] == ClusteringSnapshot.ExportArgument || args[0] == ClusteringSnapshot.VerifyExportArgument))
                return ClusteringSnapshot.Run(args);
            if (args.Length > 0 && args[0] == WordGroupingSnapshot.Argument) return WordGroupingSnapshot.Run(args);
            if (args.Length > 0 && args[0] == ChartExportSnapshot.Argument) return ChartExportSnapshot.Run(args);
            if (args.Length > 0 && args[0] == ResultsRuntimeSnapshot.Argument) return ResultsRuntimeSnapshot.Run(args);
            var app = new App();
            app.GetType().GetMethod("InitializeComponent")?.Invoke(app, null);
            int failures = 0;
            GoldenNumbers.Run(args, (name, passed, detail) =>
            {
                Console.WriteLine((passed ? "PASS " : "FAIL ") + name + (detail == null ? "" : ": " + detail));
                if (!passed) failures++;
            });
            return failures == 0 ? 0 : 1;
        }
    }
}
