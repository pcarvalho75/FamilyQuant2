using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> EvidenceCommandTable() =>
    [
        new("--test-evidence-fetch-atomic", 1, TestCommandPhase.Raw, EvidenceAtomicRequested, RunEvidenceAtomic),
        new("--test-panel-recovery", 1, TestCommandPhase.Raw, args => args.Contains("--test-panel-recovery", StringComparer.Ordinal), (_, workDir) =>
        {
            EnsureApplication();
            PanelRecoveryTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
            PanelRetryUiTests();
            InheritedRetryProgressTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-evidence-resume", 1, TestCommandPhase.Raw, args => args.Contains("--test-evidence-resume", StringComparer.Ordinal), (_, workDir) =>
        {
            EvidenceResumeTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-uniprot-retry", 1, TestCommandPhase.Raw, args => args.Contains("--test-uniprot-retry", StringComparer.Ordinal), (_, _) =>
        {
            UniProtRetryTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--evidence-delivery", 2, TestCommandPhase.Raw, EvidenceDeliveryRequested, RunEvidenceDelivery),
        new("--test-evidence-delivery", 3, TestCommandPhase.Raw, EvidenceDeliveryTestsRequested, RunEvidenceDeliveryTests),
        new("--evidence-task-check", 60, TestCommandPhase.Raw, EvidenceTaskCheckRequested, RunEvidenceTaskCheck),
        new("--grouping-discovery-check", 60, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--grouping-discovery-check", (args, _) => GroupingDiscoveryCheck(args)),
        new("--test-grouping-discovery-safety", 60, TestCommandPhase.Raw,
            args => args.Contains("--test-grouping-discovery-safety", StringComparer.Ordinal), (_, _) =>
            {
                GroupingDiscoverySafetyTests();
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
        new("--evidence-task-header-audit", 65, TestCommandPhase.Raw,
            args => args.Length > 0 && args[0] == "--evidence-task-header-audit", (args, _) => EvidenceTaskHeaderAuditCommand(args)),
        new("--test-evidence-task", 70, TestCommandPhase.Raw, EvidenceTaskTestsRequested, RunEvidenceTaskTests),
        new("--test-taxa-entry-counts", 70, TestCommandPhase.Raw, args => args.Contains("--test-taxa-entry-counts", StringComparer.Ordinal), (_, _) =>
        {
            TaxaEntryCountTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-task-taxa-clipboard", 70, TestCommandPhase.Raw, args => args.Contains("--test-task-taxa-clipboard", StringComparer.Ordinal), (_, _) =>
        {
            TaskTaxaClipboardTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-keyword-categories", 70, TestCommandPhase.Raw, args => args.Contains("--test-keyword-categories", StringComparer.Ordinal), (_, _) =>
        {
            KeywordCategoryTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-keyword-category-filter", 70, TestCommandPhase.Raw, args => args.Contains("--test-keyword-category-filter", StringComparer.Ordinal), (_, _) =>
        {
            KeywordCategoryFilterTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-add-keyword-scope", 70, TestCommandPhase.Raw, args => args.Contains("--test-add-keyword-scope", StringComparer.Ordinal), (_, _) =>
        {
            AddKeywordScopeTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-scope-expansion-help", 70, TestCommandPhase.Raw, args => args.Contains("--test-scope-expansion-help", StringComparer.Ordinal), (_, _) =>
        {
            ScopeExpansionHelpTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-scope-reachability", 70, TestCommandPhase.Raw, args => args.Contains("--test-scope-reachability", StringComparer.Ordinal), (_, _) =>
        {
            ScopeReachabilityTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-scope-count-wording", 70, TestCommandPhase.Raw, args => args.Contains("--test-scope-count-wording", StringComparer.Ordinal), (_, _) =>
        {
            ScopeCountWordingTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-reviewed-header-counts", 70, TestCommandPhase.Raw, args => args.Contains("--test-reviewed-header-counts", StringComparer.Ordinal), (_, _) =>
        {
            ReviewedHeaderCountTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-organism-evidence", 70, TestCommandPhase.Raw, args => args.Contains("--test-organism-evidence", StringComparer.Ordinal), (_, _) =>
        {
            OrganismEvidenceTests();
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-scope-change-preview", 70, TestCommandPhase.Raw, args => args.Contains("--test-scope-change-preview", StringComparer.Ordinal), (args, _) =>
        {
            ScopeChangePreviewTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.ScopePreview"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-taxonomy-batches", 70, TestCommandPhase.Raw, args => args.Contains("--test-taxonomy-batches", StringComparer.Ordinal), (_, workDir) =>
        {
            TaxonomyBatchTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
            Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
            return failed == 0 ? 0 : 1;
        }),
        new("--test-evidence-task-scope", 430, TestCommandPhase.Raw, EvidenceTaskScopeRequested, RunEvidenceTaskScope),
    ];

    private static bool EvidenceAtomicRequested(string[] args) => args.Contains("--test-evidence-fetch-atomic", StringComparer.Ordinal);
    private static bool EvidenceDeliveryRequested(string[] args) => args.Length > 0 && args[0] == "--evidence-delivery";
    private static bool EvidenceDeliveryTestsRequested(string[] args) => args.Contains("--test-evidence-delivery", StringComparer.Ordinal);

    private static int RunEvidenceAtomic(string[] args, string workDir)
    {
        EnsureApplication();
        EvidenceFetchAtomicTests();
        EvidenceFetchCoordinatorTests();
        EvidenceFetchTests();
        string folder = Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests", "fetch-focused");
        Directory.CreateDirectory(folder);
        ProteinDomainsTests(folder);
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunEvidenceDelivery(string[] args, string workDir) => EvidenceDeliveryCommand(args);

    private static int RunEvidenceDeliveryTests(string[] args, string workDir)
    {
        EnsureApplication();
        EvidenceDeliveryFixtureTests(Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static bool EvidenceTaskCheckRequested(string[] args) => args.Length > 0 && args[0] == "--evidence-task-check";
    private static bool EvidenceTaskTestsRequested(string[] args) => args.Contains("--test-evidence-task", StringComparer.Ordinal);
    private static bool EvidenceTaskScopeRequested(string[] args) => args.Contains("--test-evidence-task-scope", StringComparer.Ordinal);

    private static int RunEvidenceTaskScope(string[] args, string workDir)
    {
        EnsureApplication();
        EvidenceTaskScopeTests();
        EvidenceTaskScopeRecipeTests();
        EvidenceTaskScopeWindowTests(args.Length == 2 ? args[1] : Path.Combine(Path.GetTempPath(), "FamilyQuant.Tests.TaskScope"));
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }

    private static int RunEvidenceTaskCheck(string[] args, string workDir)
    {
        return EvidenceTaskCheck(args);
    }

    private static int RunEvidenceTaskTests(string[] args, string workDir)
    {
        EvidenceTaskTests();
        Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
        return failed == 0 ? 0 : 1;
    }
}
