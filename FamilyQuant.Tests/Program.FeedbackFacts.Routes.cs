using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using PatternTools.PLP;
using FamilyQuant.DeNovo;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    private static void FeedbackFailureRoutes(string output, Dictionary<string, (string Text, string Note)> observed)
    {
        string fixture = "D:/FamilyQuant-data/out-evidence/y9-fixture"; Directory.CreateDirectory(fixture);
        string plpPath = WriteSparseRunFixture(fixture, "legacy.plp");
        File.WriteAllText(plpPath, File.ReadAllText(plpPath).Replace("0.6", "6.0"));
        string blocked = Path.Combine(fixture, "held-output.csv"); File.WriteAllText(blocked, "owned previous export");
        var oldPresenter = ShellDialogs.FilePresenter;
        var oldContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var file = new FamilyClusterFile { MyExtendedFastaItems = new()
        {
            new() { SequenceIdentifier="sp|X|PROT1", Description="Common fixture protein", Sequence="PEPTIDEK", UserID=1, USearchID=1 },
            new() { SequenceIdentifier="fixture_second", Description="Common fixture protein", Sequence="PEPTIDER", UserID=2, USearchID=2 }
        } };
        var screen = new ClusterGenerationControl(file);
        var window = new Window { Content=screen, Width=1366, Height=900, ShowActivated=false, ShowInTaskbar=false, Left=-30000, Top=-30000 };
        string Text() => UiField<TextBlock>(screen, "TextBlockStatusLine")!.Text;
        void Capture(string ids, string note)
        { foreach(string id in ids.Split(' ', StringSplitOptions.RemoveEmptyEntries)) observed.Add("FQ15-"+id, (Text(), note)); }
        void Click(string name)
        {
            if (name == "ButtonSaveReport") typeof(ClusterGenerationControl).GetMethod("ExportVisibleProteinTable", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, new object[] { screen });
            else if (name == "ButtonExportComposition") typeof(ClusterGenerationControl).GetMethod("ButtonExportComposition_Click", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(screen, new object[] { screen, new RoutedEventArgs() });
            else
            {
                var button = UiField<Button>(screen, name)!;
                if (!button.IsEnabled || !WalkTree(screen).Contains(button)) throw new InvalidOperationException("Action is not attached and enabled: " + name);
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            }
            FinishBusyOperation(screen.Operations); PumpDispatcher();
        }
        void Call(string name) => typeof(ClusterGenerationControl).GetMethod(name, BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(screen,null);
        try
        {
            window.Show(); PumpDispatcher();
            ShellDialogs.FilePresenter = (_, id, dialog) => { dialog.FileName = plpPath; return id == "curation.patternlab"; };
            Click("ButtonLoadQuantData");
            Check("Y9 normal legacy load states reporting mode and re-export advice", Text().Contains("four reporting modes", StringComparison.Ordinal)
                && Text().Contains("XQuant", StringComparison.Ordinal) && Text().Contains("nothing is required", StringComparison.OrdinalIgnoreCase)
                && Text().Contains("table currently shows", StringComparison.Ordinal), Text());
            Capture("6C89A4C935C2 273B67957802 6355E948918B DF5CDA5636EF", "Actual successful legacy PatternLab load button; dialog seam only supplies a synthetic input path.");
            ShellDialogs.FilePresenter = (_, id, dialog) => { dialog.FileName = Path.Combine(fixture,"missing.plp"); return id == "curation.patternlab"; };
            Click("ButtonLoadQuantData");
            Check("Y9 load failure preserves previous-table fact and file-in-use next step", Text().Contains("table still shows what it showed before",StringComparison.Ordinal)
                && Text().Contains("other program",StringComparison.Ordinal));
            Capture("B4A6F7BD890C F15217F2122B D440FE09F332", "Actual load button with missing input; regular failure handler and status, existing project retained.");
            ShellDialogs.FilePresenter = (_, id, dialog) => { dialog.FileName = blocked; return id == "curation.proteins-csv"; };
            using (var held = new FileStream(blocked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Click("ButtonSaveReport");
            Check("Y9 protein export failure offers Excel and folder-permission recovery",Text().Contains("Excel",StringComparison.Ordinal)&&Text().Contains("write to the folder",StringComparison.Ordinal));
            Capture("904FDA299D0C 70241B4AA382", "Actual protein export button; destination is held open exclusively, so the normal atomic writer fails.");
            ShellDialogs.FilePresenter = (_, id, dialog) => { dialog.FileName = Path.Combine(fixture,"report.csv"); return id == "curation.proteins-csv"; };
            Click("ButtonSaveReport");
            Check("Y9 successful export explains localized display and downstream tools",Text().Contains("your own language",StringComparison.Ordinal)
                &&Text().Contains("spreadsheet, R or Python",StringComparison.Ordinal));
            Capture("589536AE2E1F E442BE461C72", "Actual protein CSV export completion; no numeric export implementation changed.");
            FinishBusyTask(screen.SetFullXicQuantAsync(new[] { new QuantRow {Side=NativeQuantFractions.SideIdentified,Run="fixture",Sequence="PEPTIDEK",Charge=2,EnvelopeSum=10,IsotopeAreas=new[]{10d,0d,0d,0d}} }, null));
            ShellDialogs.FilePresenter = (_, id, dialog) => { dialog.FileName = blocked; return id == "curation.composition-csv"; };
            using (var held = new FileStream(blocked,FileMode.Open,FileAccess.ReadWrite,FileShare.None)) Click("ButtonExportComposition");
            Check("Y9 composition failure says nothing was written and retains program reason", Text().Contains("Nothing was written",StringComparison.Ordinal)
                &&Text().Contains("reason:",StringComparison.Ordinal));
            Capture("49E5CB397E81", "Actual composition export button with an exclusively held destination; normal exception reason is rendered.");
            // A damaged in-memory vendor matrix supplies a calculation fault, not replacement feedback.
            var project = screen.ViewModel.Quantitation.CurrentPLP!;
            var matrix = project.MySparseMatrix;
            var property = typeof(PatternLabProject).GetProperty("MySparseMatrix");
            var field = typeof(PatternLabProject).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).FirstOrDefault(f=>f.FieldType==matrix.GetType());
            void Matrix(object? value) { if(property?.SetMethod!=null) property.SetValue(project,value); else field!.SetValue(project,value); }
            Matrix(null);
            Call("RecalculateQuantitationInternal");
            Check("Y9 normal recalculation failure protects stale numbers and gives full recovery",Text().Contains("must not be read",StringComparison.Ordinal)
                &&Text().Contains("denominator",StringComparison.Ordinal)&&Text().Contains("maintainer",StringComparison.Ordinal));
            Capture("7418AADC227C FAA4BCDCA262 3373BFC10801 B3BB9EC8E1D5 1CD0C5F7DAA3", "Normal recalculation method; only vendor input matrix is faulted, no feedback override.");
            Call("PushUndoSnapshot"); Click("ButtonUndo");
            Check("Y9 Undo error reports partial completion and stale shares",Text().Contains("grouping was stepped back",StringComparison.Ordinal)&&Text().Contains("must not be read",StringComparison.Ordinal));
            Capture("11C617921F3C 828C4C669760 251E9FF748F2 B94D558E6F28", "Actual Undo click after injected vendor-data failure; normal inner failure remains visible.");
            var grid=UiField<DataGrid>(screen,"DataGridResultGrid")!; grid.SelectedItems.Add(file.MyExtendedFastaItems[0]);
            UiField<TextBox>(screen,"TextBoxBatchAnnotation")!.Text="owned edit"; Click("ButtonApplyToSelected");
            Check("Y9 batch error reports changed rows and next recalculation",Text().Contains("rows were changed",StringComparison.Ordinal)&&Text().Contains("Press Recalculate",StringComparison.Ordinal));
            Capture("073E2B6A0D4F 6C7A2F1B3784 B1DF357A25B8", "Actual selected-row batch edit click; input failure only, rendered error is normal route.");
            var grouping=typeof(ClusterGenerationControl).GetMethod("PerformClustering",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(double),typeof(List<string>)},null)!;
            FinishBusyTask((System.Threading.Tasks.Task)grouping.Invoke(screen,new object[]{0.95,new List<string>()})!);
            Check("Y9 regroup completion cannot overwrite failed-recalculation warning",Text().Contains("families were regrouped",StringComparison.Ordinal)&&Text().Contains("must not be read",StringComparison.Ordinal));
            Matrix(matrix);
            FinishBusyTask((System.Threading.Tasks.Task)grouping.Invoke(screen,new object[]{0.95,new List<string>()})!);
            Check("Y9 successful regroup confirms shares follow the new grouping",Text().Contains("recalculated over the new grouping",StringComparison.Ordinal));
            Capture("338223862E7C", "Actual word regrouping success with a valid restored synthetic PatternLab matrix.");
            grid.SelectedItems.Clear(); foreach(var protein in file.MyExtendedFastaItems)grid.SelectedItems.Add(protein);
            string? previous=Environment.GetEnvironmentVariable("FAMILYQUANT_USEARCH");
            try
            {
                string invalidExe=Path.Combine(fixture,"invalid-usearch.exe"); File.WriteAllText(invalidExe,"owned invalid executable fixture");
                Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH",invalidExe);
                FinishBusyTask(screen.AlignSelectedAsync());
                Check("Y9 alignment failure gives all troubleshooting facts",Text().Contains("antivirus",StringComparison.OrdinalIgnoreCase)
                    &&Text().Contains("fewer sequences",StringComparison.Ordinal)&&Text().Contains("FAMILYQUANT_USEARCH",StringComparison.Ordinal),Text());
                Capture("F6FC98BFA98B 2B91CC0E11B8 F37998D49A0C", "Actual alignment operation fails its executable validation; only process-local executable path is varied.");
                Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH",null);
                FinishBusyTask(screen.AlignSelectedAsync());
                var report=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.Title.StartsWith("Multiple sequence alignment",StringComparison.Ordinal));
                Check("Y9 normal alignment report title includes selection count and unit",report!=null&&report.Title.Contains("2 sequences",StringComparison.Ordinal));
                if(report!=null){observed.Add("FQ15-B49413632F0B",(report.Title,"Actual report window title after bundled USEARCH alignment of two synthetic sequences."));report.Close();}
            }
            finally{Environment.SetEnvironmentVariable("FAMILYQUANT_USEARCH",previous);}
        }
        finally{window.Close();ShellDialogs.FilePresenter=oldPresenter;SynchronizationContext.SetSynchronizationContext(oldContext);}
    }

    private static void WriteFeedbackFactRows(string output, Dictionary<string,(string Text,string Note)> observed)
    {
        string Quote(string value)=>"\""+value.Replace("\"","\"\"")+"\"";
        var lines=new List<string>{"row_id\tnew_text\tnote"};
        foreach(var pair in observed.OrderBy(p=>p.Key,StringComparer.Ordinal))lines.Add(pair.Key+"\t"+Quote(pair.Value.Text)+"\t"+Quote(pair.Value.Note));
        File.WriteAllText(Path.Combine(output,"factcheck-Y9-FQEvidence.tsv"),string.Join("\n",lines)+"\n",new System.Text.UTF8Encoding(false));
        Check("Y9 all 39 proposed restoration rows have captured normal-route text",observed.Count==39,observed.Count.ToString(CultureInfo.InvariantCulture));
    }
}
