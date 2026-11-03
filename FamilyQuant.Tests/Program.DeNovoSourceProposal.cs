using FamilyQuant.DeNovo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace FamilyQuant.Tests;

internal static partial class Program
{
    [TestCommandTable]
    private static IEnumerable<TestCommand> DeNovoSourceProposalCommands() =>
    [
        new("--test-denovo-source-proposal", 372, TestCommandPhase.Raw,
            args => args.Contains("--test-denovo-source-proposal", StringComparer.Ordinal), (args, _) =>
            {
                if (args.Length == 3)
                {
                    var model = new ComputeXicViewModel("source-discovery-check") { DeNovoDirectory = args[1] };
                    string[] expected = args[2].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    Check("actual export folder proposes the requested native source ids",
                        model.Filters.Sources.Select(s => s.Id).SequenceEqual(expected, StringComparer.Ordinal));
                    Console.WriteLine(model.Filters.SourceProposalText + " Selected: " + model.Filters.ModeTag);
                }
                else if (args.Length == 1) DeNovoSourceProposalTests();
                else throw new ArgumentException("Optional arguments: export folder and comma-separated expected source ids.");
                Console.WriteLine($"===== {passed} passed, {failed} failed, {skipped} skipped =====");
                return failed == 0 ? 0 : 1;
            }),
    ];

    private static void DeNovoSourceProposalTests()
    {
        string root = Path.Combine(Path.GetTempPath(), "FamilyQuant.SourceProposal", Guid.NewGuid().ToString("N"));
        string Folder(string name, params string[] files)
        {
            string folder = Path.Combine(root, name);
            Directory.CreateDirectory(folder);
            foreach (string file in files)
            {
                string path = Path.Combine(folder, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "owned source-discovery fixture");
            }
            return folder;
        }
        string all = Folder("all", "nested/run.denovo.csv");
        string only = Folder("only", "run.denovoOnly.csv");
        string deep = Folder("deep", "run.deepnovo.csv");
        string union = Folder("union", "run.denovo.csv", "run.deepnovo.csv");
        string both = Folder("ambiguous", "run.denovo.csv", "run.denovoOnly.csv");
        string none = Folder("empty", "notes.txt");
        Check("source discovery recognizes nested full de novo exports from the reader's source registry",
            DeNovoSourceDiscovery.Inspect(all).ProposedSources.SequenceEqual(new[] { DeNovoSource.DeNovoAll }));
        Check("de novo only remains a distinct detected export",
            DeNovoSourceDiscovery.Inspect(only).ProposedSources.SequenceEqual(new[] { DeNovoSource.DenovoOnly }));
        Check("DeepNovo retains its separate source and confidence scale",
            DeNovoSourceDiscovery.Inspect(deep).ProposedSources.SequenceEqual(new[] { DeNovoSource.DeepNovo }));
        Check("a mixed caller folder proposes the matching existing union",
            DeNovoSourceDiscovery.Inspect(union).ProposedSources.SequenceEqual(new[] { DeNovoSource.DeNovoAll, DeNovoSource.DeepNovo }));
        var ambiguous = DeNovoSourceDiscovery.Inspect(both);
        Check("different PEAKS export types require a choice instead of silently omitting one",
            ambiguous.AvailableSources.Count == 2 && ambiguous.ProposedSources.Count == 0 && ambiguous.Message.Length > 0);
        var model = new ComputeXicViewModel("fixture");
        model.DeNovoDirectory = all;
        Check("Quantify proposes de novo all when its chosen folder holds that export",
            model.Filters.ModeTag == "peaks-all" && model.Filters.SourceProposalText.Length > 0);
        model.DeNovoDirectory = only;
        Check("a still-automatic source follows a subsequently chosen export folder", model.Filters.ModeTag == "peaks-only");
        model.DeNovoDirectory = union;
        Check("Quantify's union proposal preserves native source-specific cutoffs", model.Filters.ModeTag == "union-all-deepnovo"
            && model.Filters.TryBuild(out _, out var sources, out _) && sources.Count == 2
            && sources[0].Source.ConfidenceField == "ALC (%)" && sources[1].Source.ConfidenceField == "SCORE (%)");
        model.Filters.ModeTag = "deepnovo";
        model.Filters.DeepNovoMinimumScore = "77.5";
        model.DeNovoDirectory = all;
        Check("a person's changed source and native cutoff survive later folder proposals",
            model.Filters.ModeTag == "deepnovo" && model.Filters.DeepNovoMinimumScore == "77.5");
        var saved = new DeNovoFilterViewModel(new QuantManifest { DeNovoInputs = new()
        {
            new() { SourceId = DeNovoSource.DenovoOnly.Id, MinimumConfidence = 91 },
        } });
        saved.ProposeSourceForDirectory(all);
        Check("stored project source and confidence are not replaced by a proposal",
            saved.ModeTag == "peaks-only" && saved.PeaksMinimumAlc == "91");
        string original = model.Filters.ModeTag;
        model.DeNovoDirectory = none;
        Check("a folder without supported exports says so without fabricating a source",
            model.Filters.ModeTag == original && model.Filters.SourceProposalText.Length > 0);
        EnsureApplication();
        var automatic = new ComputeXicViewModel("fixture");
        var control = new DeNovoFilterControl();
        control.Attach(automatic.Filters);
        automatic.DeNovoDirectory = all;
        PumpDispatcher();
        var picker = (ComboBox)control.FindName("ComboBoxDeNovoSource");
        Check("the native source picker reflects the folder proposal after binding initialization", Equals(picker.SelectedValue, "peaks-all"));
        picker.SetCurrentValue(Selector.SelectedValueProperty, "deepnovo");
        picker.GetBindingExpression(Selector.SelectedValueProperty)?.UpdateSource();
        automatic.DeNovoDirectory = only;
        PumpDispatcher();
        Check("the native source picker can override automatic detection", automatic.Filters.ModeTag == "deepnovo");

        var previous = new DeNovoImport { RowsRead = 1, Kept = new() { new() { Peptide = "PEPTIDEK" } } };
        var pending = new TaskCompletionSource<DeNovoImport>();
        var inspection = new DeNovoViewModel { ImportDirectory = all, ImportOverride = (_, _) => pending.Task };
        inspection.ApplyImport(previous);
        string originalCriteria = JsonSerializer.Serialize(inspection.Criteria);
        string originalProposal = inspection.Filters.SourceProposalText;
        Task importing = inspection.ImportAsync();
        Check("an automatic import proposal retains the prior evidence until replacement is published",
            inspection.Filters.ModeTag == "peaks-all" && ReferenceEquals(inspection.LastImport, previous) && inspection.IsBusy);
        inspection.Operations.Cancel();
        pending.SetResult(new DeNovoImport());
        FinishBusyTask(importing);
        Check("cancelled automatic selection restores the prior source, criteria and evidence",
            ReferenceEquals(inspection.LastImport, previous) && JsonSerializer.Serialize(inspection.Criteria) == originalCriteria
            && inspection.Filters.ModeTag == "peaks-only" && inspection.Filters.SourceProposalText == originalProposal
            && inspection.Status.StartsWith("Cancelled", StringComparison.Ordinal) && inspection.CanEdit);
        var replacement = new DeNovoImport { RowsRead = 2, Kept = new() { new() { Peptide = "PEPTIDEK" } } };
        inspection.ImportOverride = (_, _) => Task.FromResult(replacement);
        FinishBusyTask(inspection.ImportAsync());
        Check("retry after Cancel still automatically selects and publishes the detected source",
            inspection.Filters.ModeTag == "peaks-all" && ReferenceEquals(inspection.LastImport, replacement)
            && replacement.SourceId == DeNovoSource.DeNovoAll.Id && inspection.CanEdit);
        inspection.Filters.ModeTag = "deepnovo";
        Check("an explicit source change still clears the previous inspection", inspection.LastImport == null);

        var failed = new DeNovoViewModel { ImportDirectory = deep,
            ImportOverride = (_, _) => Task.FromException<DeNovoImport>(new IOException("Owned import failure")) };
        failed.Filters.DeepNovoMinimumScore = "77.5";
        failed.ApplyImport(previous);
        string previousCriteria = JsonSerializer.Serialize(failed.Criteria);
        FinishBusyTask(failed.ImportAsync());
        Check("a failed import rolls back its automatic source and native criteria without clearing evidence",
            failed.Filters.ModeTag == "peaks-only" && failed.Filters.DeepNovoMinimumScore == "77.5"
            && JsonSerializer.Serialize(failed.Criteria) == previousCriteria && ReferenceEquals(failed.LastImport, previous)
            && failed.Status.Contains("Owned import failure", StringComparison.Ordinal) && failed.CanEdit);
        failed.Filters.DeepNovoMinimumScore = "not a score";
        FinishBusyTask(failed.ImportAsync());
        Check("invalid proposed-source criteria preserve the original source and evidence before import",
            failed.Filters.ModeTag == "peaks-only" && ReferenceEquals(failed.LastImport, previous)
            && JsonSerializer.Serialize(failed.Criteria) == previousCriteria && failed.CanEdit && !failed.Operations.IsBusy);
    }
}
