using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void KeywordCommandTests()
    {
        EnsureApplication();
        var control = new ClusterGenerationControl(new FamilyClusterFile { MyExtendedFastaItems = Vipers() });
        var button = UiField<Button>(control, "ButtonSuperCluster")!;
        bool ready = button.IsEnabled && button.Content as string == "Group by keywords";
        Check("keyword grouping opens from one enabled command without duplicate outer inputs", ready
            && control.FindName("TextBoxSuperClusterWording") == null
            && control.FindName("TextBoxSuperClusterExclude") == null
            && control.FindName("ComboBoxSuperClusterMatch") == null);
        if (!ready) return; // The old handler would otherwise show its empty-keyword modal dialog.

        var previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            int asked = 0;
            control.ConfirmSuperClusterOverride = preview =>
            {
                asked++;
                Check("the first keyword review opens empty with match all and no pending edit",
                    preview.KeywordsText.Length == 0 && preview.ExcludeText.Length == 0
                    && preview.Match == KeywordMatch.All && preview.JoiningFamilies.Count == 0);
                preview.KeywordsText = "cancelled wording";
                return false;
            };
            Task Run() => (Task)typeof(ClusterGenerationControl).GetMethod("RunSuperCluster",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(control, null)!;
            FinishReviewWork(Run);
            control.ConfirmSuperClusterOverride = preview =>
            {
                asked++;
                Check("Cancel keeps the next keyword review at the last accepted settings", preview.KeywordsText.Length == 0);
                preview.KeywordsText = ViperKeywords; preview.ExcludeText = AnaExcludeBox;
                preview.Match = KeywordMatch.Any; preview.Refresh();
                return true;
            };
            FinishReviewWork(Run);
            control.ConfirmSuperClusterOverride = preview =>
            {
                asked++;
                Check("accepted keyword, exclusion and match settings reopen in the review window",
                    preview.KeywordsText == ViperKeywords && preview.ExcludeText == AnaExcludeBox
                    && preview.Match == KeywordMatch.Any);
                return false;
            };
            FinishReviewWork(Run);
            Check("the single keyword command reached all three reviews", asked == 3);
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
    }
}
