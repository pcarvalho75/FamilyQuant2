using FamilyQuant.DeNovo;
using PatternTools.PLP;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    internal static readonly string[] PreservedCommandFlags =
    [
        "--test-query-residue-coverage",
        "--evidence-coverage-check",
        "--test-evidence-coverage",
        "--test-evidence-fetch-atomic",
        "--evidence-delivery",
        "--test-evidence-delivery",
        "--list-test-commands",
        "--test-evidence-task-scope",
        "--test-chart-themes",
        "--evidence-task-check",
        "--test-evidence-task",
        "--busy-screens",
        "--test-busy",
        "--test-dispatch",
        "--dependency-file-check",
        "--test-dependencies",
        "--test-keyword-command",
        "--test-ui-icons",
        IdentitySearchStudy.Argument,
        "--test-identity-study",
        ClusteringSnapshot.Argument,
        ClusteringSnapshot.ExportArgument,
        ClusteringSnapshot.VerifyExportArgument,
        "--test-clustering-results",
        "--operation-native-wait",
        "--test-operations",
        WordGroupingSnapshot.Argument,
        ChartExportSnapshot.Argument,
        "--test-results",
        "--test-document",
        "--test-core-boundary",
        LogoAssets.Argument,
        "--test-inputs",
        ScreenHarness.Argument,
        GroupingReviewScreens.Argument,
        FeatureLedger.GenerateArgument,
        "--feature-ledger-check",
        "--feature-ledger-release-check",
        "--shell-checks",
        "--shell-menu-screens",
        "--start-page-screens",
        "--shell-save-smoke",
        "--evidence-recipe-replay-check",
        "--shell-save-screens",
        "--quantify-screens",
        "--results-screens",
        "--shell-window-screens",
        "--denovo-screens",
        "--homology-results-screens",
        "--build-family-screens",
        "--quantify-shell-screens",
        "--secondary-screens",
        EvidenceClassifierCheck.Argument,
        EvidenceClassifierCheck.ArchitectureArgument,
        EvidenceClassifierCheck.ApplyArgument,
        EvidenceClassifierCheck.SessionApplyArgument,
        EvidenceFetchCheck.Argument,
        EvidenceFetchCheck.VerifyArgument,
        EvidenceStorageCheck.Argument,
        ReferencePanelCheck.RefreshArgument,
        ReferencePanelCheck.Argument,
        EvidenceOperationsCheck.Argument,
        ScienceGateListRun.Argument,
        LeftOutCheckRun.Argument,
        ValidationCheckRun.Argument,
        MotifCheckRun.Argument,
        CoherenceCheckRun.Argument,
        NearestReviewedCheck.FullArgument,
        NearestReviewedCheck.Argument,
        "--golden",
        "--golden-record",
        "--golden-verify",
        TalkFigures.Argument,
        TalkFigures.OnlyPlusDeepNovoArgument,
        TalkFigures.SourceVennArgument,
        TalkFigures.VariantsArgument,
        TalkFigures.SweepArgument,
        TalkFigures.AgreementArgument,
        TalkFigures.AcceptedScoreScatterArgument,
        TalkFigures.ScatterArgument,
        AttributionRuleStudy.Argument,
        TalkFigures.ScoreDistributionArgument,
        TalkFigures.SetNoveltyArgument,
        "--overlap-study",
        ShortPsmPeaksTable.Argument,
        ChargeAudit.Argument,
        ThreeWayAudit.Argument,
    ];
}
